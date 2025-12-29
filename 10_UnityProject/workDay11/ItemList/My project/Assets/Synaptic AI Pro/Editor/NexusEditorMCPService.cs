using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Linq;

namespace SynapticPro
{
    /// <summary>
    /// Editor-only MCP Service - Independent service that doesn't depend on scenes
    /// Not affected by PlayMode switching or scene changes
    /// </summary>
    [InitializeOnLoad]
    public static class NexusEditorMCPService
    {
        private static ClientWebSocket webSocket;
        private static CancellationTokenSource cancellationTokenSource;
        private static bool isConnected = false;
        private static Queue<MCPMessage> messageQueue = new Queue<MCPMessage>();
        private static string serverUrl = null; // Set dynamically
        private static bool isInitialized = false;
        private static bool shouldReconnect = true;
        private static int reconnectAttempts = 0;
        private static int maxReconnectAttempts = 5;
        private static float lastReconnectTime = 0;
        private static float lastConnectionCheckTime = 0;
        private static bool isReconnecting = false;
        private static int reconnectPhase = 0; // 0: Standby, 1: Light test (2s), 2: Full reconnect (5s), 3: Retry on failure (10s)
        
        // Play Mode auto-reconnect related
        private static bool wasConnectedBeforePlayMode = false;
        private static bool enableAutoReconnect = true;
        private static string connectionStateKey = "NexusMCP_ConnectionState";
        private static string autoReconnectKey = "NexusMCP_AutoReconnect";

        public static bool IsConnected => isConnected && webSocket != null && webSocket.State == WebSocketState.Open;
        
        public static string GetServerUrl() => serverUrl ?? "ws://127.0.0.1:8080";
        
        public static event Action<string> OnMessageReceived;
        public static event Action OnConnected;
        public static event Action OnDisconnected;
        public static event Action<string> OnError;

        [Serializable]
        public class MCPMessage
        {
            public string type;
            public string id;
            public string provider;
            public string content;
            public Dictionary<string, object> parameters;
            public string tool;
            public string command;
            public object data;
        }

        static NexusEditorMCPService()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (isInitialized) return;
            
            Debug.Log("[Nexus Editor MCP] Initializing Editor MCP Service");
            
            // Auto-detect available port
            DetectAndSetAvailablePort();

            // Subscribe to EditorApplication events
            EditorApplication.update += Update;
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            
            // Subscribe to compilation-related events
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            // Load settings
            enableAutoReconnect = EditorPrefs.GetBool(autoReconnectKey, true);

            // Delayed auto-connect (after 0.1s) - Connect reliably even in closed state
            EditorApplication.delayCall += () =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100); // Wait 0.1 seconds (connect immediately)
                    if (enableAutoReconnect && !isConnected)
                    {
                        try
                        {
                            await ConnectToMCPServer();
                            Debug.Log("[Nexus MCP] 🚀 Initial auto-connect completed");
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Nexus MCP] Initial auto-connect failed: {e.Message}");
                            isConnected = false;
                            // On failure, gradual reconnection takes over
                            OnConnectionLost();
                        }
                    }
                });
            };
            
            isInitialized = true;
            Debug.Log("[Nexus Editor MCP] Service initialized - auto-connection will start immediately");
        }

        /// <summary>
        /// Set server URL to fixed port 8080 (v1.1.0)
        /// Both index.js and hub-server.js use port 8080
        /// </summary>
        private static void DetectAndSetAvailablePort()
        {
            try
            {
                // Fixed port 8080 for all MCP servers (index.js and hub-server.js)
                const int mcpPort = 8080;
                serverUrl = $"ws://localhost:{mcpPort}";

                Debug.Log($"[Nexus Editor MCP] Using MCP server at {serverUrl}");

                // Auto-update Claude Desktop settings
                UpdateClaudeDesktopConfigForPort(mcpPort);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nexus Editor MCP] Setup failed: {e.Message}, using default port");
                serverUrl = "ws://127.0.0.1:8080";
            }
        }

        /// <summary>
        /// Check if MCP server responds on that port
        /// </summary>
        private static bool IsPortInUse(int port)
        {
            try
            {
                // Verify MCP server existence with simple TCP connection test
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect("localhost", port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));

                    if (success && client.Connected)
                    {
                        client.Close();
                        return true; // MCP server responded
                    }

                    return false; // Cannot connect
                }
            }
            catch
            {
                return false; // Consider unavailable on error
            }
        }

        private static void Update()
        {
            // Process messages on main thread
            while (messageQueue.Count > 0)
            {
                var message = messageQueue.Dequeue();
                ProcessMessage(message);
            }

            // Gradual reconnection processing
            if (!enableAutoReconnect || isReconnecting)
            {
                // Set reconnect flag if connection is lost even during compilation
                if (EditorApplication.isCompiling && !isConnected)
                {
                    EditorPrefs.SetBool("NexusMCP_NeedsReconnectAfterCompile", true);
                }
                return;
            }
                
            float currentTime = Time.realtimeSinceStartup;
            
            // Include WebSocket in closed state as reconnection target
            bool needsReconnection = !isConnected ||
                                   (webSocket != null && (webSocket.State == WebSocketState.Closed ||
                                                        webSocket.State == WebSocketState.Aborted ||
                                                        webSocket.State == WebSocketState.None));

            if (needsReconnection)
            {
                switch (reconnectPhase)
                {
                    case 0: // After disconnect detection, wait 2 seconds then light test
                        if (currentTime - lastConnectionCheckTime > 2f)
                        {
                            reconnectPhase = 1;
                            lastReconnectTime = currentTime;
                            _ = Task.Run(LightConnectionTest);
                        }
                        break;

                    case 1: // Full reconnect 5 seconds after light test
                        if (currentTime - lastReconnectTime > 5f)
                        {
                            reconnectPhase = 2;
                            lastReconnectTime = currentTime;
                            _ = Task.Run(FullReconnectAttempt);
                        }
                        break;

                    case 2: // Retry 10 seconds after full reconnect failure
                        if (currentTime - lastReconnectTime > 10f)
                        {
                            reconnectPhase = 3;
                            lastReconnectTime = currentTime;
                            _ = Task.Run(RetryReconnect);
                        }
                        break;

                    case 3: // Wait another 10 seconds after retry, then return to phase 1
                        if (currentTime - lastReconnectTime > 10f)
                        {
                            reconnectPhase = 1;
                            lastReconnectTime = currentTime;
                        }
                        break;
                }
            }
            else
            {
                // Reset phase on successful connection
                reconnectPhase = 0;

                // Check WebSocket state even while connected (start reconnection immediately if Closed)
                if (webSocket != null && webSocket.State == WebSocketState.Closed)
                {
                    Debug.Log("[Nexus MCP] WebSocket closed state detected, starting reconnection");
                    OnConnectionLost();
                }
            }
        }

        private static void OnEditorQuitting()
        {
            Debug.Log("[Nexus Editor MCP] Editor quitting, disconnecting MCP service");
            DisconnectFromMCPServer();

            // Unsubscribe from events
            EditorApplication.update -= Update;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!enableAutoReconnect) return;

            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // When exiting Edit Mode - save connection state
                    wasConnectedBeforePlayMode = IsConnected;
                    if (wasConnectedBeforePlayMode)
                    {
                        Debug.Log("[Nexus Editor MCP] 🎮 Transitioning to Play Mode. Connection state saved.");
                        EditorPrefs.SetBool(connectionStateKey, true);
                        EditorPrefs.SetString(connectionStateKey + "_ServerUrl", serverUrl);
                    }
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    Debug.Log("[Nexus Editor MCP] 🎮 Entered Play Mode. Basic tools are available but editing functions are restricted.");

                    // Reconnect if connection lost even in Play Mode
                    if (wasConnectedBeforePlayMode && !IsConnected)
                    {
                        Debug.Log("[Nexus Editor MCP] 🔄 Connection lost in Play Mode. Reconnecting...");
                        EditorApplication.delayCall += () =>
                        {
                            _ = Task.Run(async () => await ConnectToMCPServer());
                        };
                    }
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    Debug.Log("[Nexus Editor MCP] ⏹️ Exiting Play Mode...");
                    // Save current connection state before exiting Play Mode
                    if (IsConnected)
                    {
                        EditorPrefs.SetBool(connectionStateKey, true);
                        EditorPrefs.SetString(connectionStateKey + "_ServerUrl", serverUrl);
                    }
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    // When returning to Edit Mode - auto-reconnect
                    if (EditorPrefs.GetBool(connectionStateKey, false))
                    {
                        var savedServerUrl = EditorPrefs.GetString(connectionStateKey + "_ServerUrl", "");
                        if (!string.IsNullOrEmpty(savedServerUrl))
                        {
                            serverUrl = savedServerUrl;
                        }

                        Debug.Log("[Nexus Editor MCP] ⏹️ Play Mode ended. Starting auto-reconnect...");

                        // Reconnect with slight delay
                        EditorApplication.delayCall += () =>
                        {
                            _ = Task.Run(async () => await ConnectToMCPServer());
                            EditorPrefs.DeleteKey(connectionStateKey);
                            EditorPrefs.DeleteKey(connectionStateKey + "_ServerUrl");
                        };
                    }
                    break;
            }
        }
        
        /// <summary>
        /// Handler for compilation start
        /// </summary>
        private static void OnCompilationStarted(object context)
        {
            Debug.Log("[Nexus MCP] 🔨 Compilation start detected");

            // Keep current connection as compilation errors may occur
            if (isConnected)
            {
                EditorPrefs.SetBool("NexusMCP_WasConnectedBeforeCompile", true);
            }
        }

        /// <summary>
        /// Handler for compilation completion
        /// </summary>
        private static void OnCompilationFinished(object context)
        {
            if (!enableAutoReconnect) return;
            
            // In Unity 2022.3, CompilationPipeline.compilationFinished event is passed as object type
            // Need to check for errors using a different method
            bool hasErrors = UnityEditor.EditorUtility.scriptCompilationFailed;

            if (hasErrors)
            {
                Debug.LogError("[Nexus MCP] ❌ Compilation errors detected");
            }

            Debug.Log($"[Nexus MCP] 🔨 Compilation completed (Errors: {hasErrors}) - Starting fast reconnection");

            // Reconnect 0.5 seconds after compilation completion (including when errors exist)
            EditorApplication.delayCall += () =>
            {
                // Attempt reconnection if previously connected, even with compilation errors
                bool wasConnectedBeforeCompile = EditorPrefs.GetBool("NexusMCP_WasConnectedBeforeCompile", false);

                if (wasConnectedBeforeCompile || !isConnected || (webSocket != null && webSocket.State != WebSocketState.Open))
                {
                    // Reset current reconnection phase and reconnect immediately
                    reconnectPhase = 0;
                    lastConnectionCheckTime = 0;
                    lastReconnectTime = 0;

                    // Execute fast reconnection
                    EditorApplication.delayCall += async () =>
                    {
                        try
                        {
                            Debug.Log("[Nexus MCP] 🔄 Starting post-compilation reconnection...");
                            await ConnectToMCPServer();
                            Debug.Log("[Nexus MCP] ⚡ Fast reconnection after compilation successful!");

                            if (hasErrors)
                            {
                                Debug.LogWarning("[Nexus MCP] ⚠️ Compilation had errors but connection was restored");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Nexus MCP] Post-compilation reconnection failed: {e.Message}");
                            // Fall back to normal gradual reconnection on failure
                            OnConnectionLost();
                        }
                        finally
                        {
                            // Clear flag
                            EditorPrefs.DeleteKey("NexusMCP_WasConnectedBeforeCompile");
                        }
                    };
                }
            };
        }

        public static async Task ConnectToMCPServer()
        {
            try
            {
                if (isConnected)
                {
                    Debug.Log("[Nexus Editor MCP] Already connected");
                    return;
                }

                // Re-detect if serverUrl is null
                if (serverUrl == null)
                {
                    DetectAndSetAvailablePort();
                }

                Debug.Log($"[Nexus Editor MCP] Connecting to MCP Server: {serverUrl} (Attempt {reconnectAttempts + 1})");

                // Clean up existing connection
                if (webSocket != null)
                {
                    webSocket.Dispose();
                }
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource.Dispose();
                }
                
                webSocket = new ClientWebSocket();
                webSocket.Options.SetRequestHeader("x-client-type", "unity"); // Identify Unity client to MCP server
                cancellationTokenSource = new CancellationTokenSource();
                
                await webSocket.ConnectAsync(new Uri(serverUrl), cancellationTokenSource.Token);
                
                Debug.Log($"[Nexus Editor MCP] WebSocket State after connect: {webSocket.State}");
                
                isConnected = true;
                reconnectAttempts = 0; // Reset on success
                OnConnected?.Invoke();

                Debug.Log("[Nexus Editor MCP] Connected to MCP Server successfully");

                // Start message listener first to avoid race conditions on some platforms
                _ = Task.Run(async () => await ListenForMessages());

                // Wait for listener to initialize before sending ping (fixes Windows WebSocket Aborted issue)
                await Task.Delay(200);

                // Send connection confirmation message
                await SendConnectionPing();
            }
            catch (Exception e)
            {
                reconnectAttempts++;
                Debug.LogError($"[Nexus Editor MCP] Failed to connect (attempt {reconnectAttempts}): {e.Message}");
                OnError?.Invoke(e.Message);
                isConnected = false;

                // Start auto-reconnect on connection failure
                OnConnectionLost();
                
                if (reconnectAttempts >= maxReconnectAttempts)
                {
                    Debug.LogError("[Nexus Editor MCP] Max reconnection attempts reached. Please check MCP server status.");
                }
            }
        }

        private static async Task ListenForMessages()
        {
            Debug.Log("[Nexus Editor MCP] Starting message listener");
            
            var buffer = new byte[1024 * 16]; // Increased buffer size

            try
            {
                while (webSocket.State == WebSocketState.Open && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var messageBuffer = new List<byte>();
                    WebSocketReceiveResult result;

                    // Loop until entire message is received
                    do
                    {
                        var segment = new ArraySegment<byte>(buffer);
                        result = await webSocket.ReceiveAsync(segment, cancellationTokenSource.Token);
                        
                        if (result.Count > 0)
                        {
                            messageBuffer.AddRange(buffer.Take(result.Count));
                        }
                    } 
                    while (!result.EndOfMessage);
                    
                    if (result.MessageType == WebSocketMessageType.Text && messageBuffer.Count > 0)
                    {
                        var messageText = Encoding.UTF8.GetString(messageBuffer.ToArray());
                        Debug.Log($"[Nexus Editor MCP] ⚡ RAW MESSAGE RECEIVED: {messageText}");
                        
                        try
                        {
                            var message = JsonConvert.DeserializeObject<MCPMessage>(messageText);
                            if (message != null)
                            {
                                // Add to queue for processing on main thread
                                messageQueue.Enqueue(message);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[Nexus Editor MCP] Failed to parse message: {e.Message}");
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[Nexus Editor MCP] WebSocket closed by server");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                var wsException = e as WebSocketException;
                string state = webSocket?.State.ToString() ?? "null";
                string closeStatus = webSocket?.CloseStatus?.ToString() ?? "none";
                string closeDescription = webSocket?.CloseStatusDescription ?? "n/a";
                string errorCode = wsException?.WebSocketErrorCode.ToString() ?? "n/a";
                string nativeError = wsException?.NativeErrorCode.ToString() ?? "n/a";

                Debug.LogError(
                    $"[Nexus Editor MCP] Message listener error: {e.Message} " +
                    $"(State: {state}, CloseStatus: {closeStatus}, CloseDesc: {closeDescription}, " +
                    $"WebSocketError: {errorCode}, NativeError: {nativeError})\n{e}");

                // Attempt auto-reconnect if WebSocket exception
                if (e is WebSocketException || e.Message.Contains("WebSocket"))
                {
                    Debug.Log("[Nexus Editor MCP] WebSocket error detected, will attempt reconnection");
                }
            }
            finally
            {
                // Only process disconnection if WebSocket is not properly closed
                if (webSocket?.State != WebSocketState.Open)
                {
                    OnConnectionLost();
                    OnDisconnected?.Invoke();
                }
            }
        }

        private static void ProcessMessage(MCPMessage message)
        {
            Debug.Log($"[Nexus Editor MCP] Processing message type: {message.type}, tool: {message.tool}, command: {message.command}");
            
            switch (message.type)
            {
                case "unity_operation":
                case "tool_call":
                    ExecuteUnityOperation(message);
                    break;
                    
                case "ai_response":
                    OnMessageReceived?.Invoke(message.content);
                    break;
                    
                case "error":
                    Debug.LogWarning($"[Nexus Editor MCP] {message.content}");
                    OnMessageReceived?.Invoke($"❗ {message.content}");
                    break;
                    
                default:
                    Debug.Log($"[Nexus Editor MCP] Unknown message type: {message.type}");
                    break;
            }
        }

        private static void ExecuteUnityOperation(MCPMessage message)
        {
            Debug.Log($"[Nexus Editor MCP] Executing Unity operation: {message.tool} with command: {message.command}");
            
            try
            {
                // Map MCP tool name to Unity operation
                string operationType = message.command ?? message.tool ?? "";

                // Convert tool name to existing operation type
                operationType = ConvertMCPToolToOperation(operationType);

                Debug.Log($"[Nexus Editor MCP] Converted operation type: {operationType}");

                var operation = new NexusUnityOperation
                {
                    type = operationType,
                    parameters = new Dictionary<string, string>()
                };

                // Parameter conversion
                if (message.parameters != null)
                {
                    foreach (var kvp in message.parameters)
                    {
                        if (kvp.Value != null)
                        {
                            // Processing nested objects
                            if (kvp.Value is Newtonsoft.Json.Linq.JArray jArray)
                            {
                                // Keep array as JSON string (for search tools)
                                operation.parameters[kvp.Key] = jArray.ToString(Newtonsoft.Json.Formatting.None);
                            }
                            else if (kvp.Value is Newtonsoft.Json.Linq.JObject jObj)
                            {
                                // Processing structs like Vector3
                                if (jObj.ContainsKey("x") && jObj.ContainsKey("y") && jObj.ContainsKey("z"))
                                {
                                    operation.parameters[kvp.Key] = $"{jObj["x"]},{jObj["y"]},{jObj["z"]}";
                                }
                                else if (jObj.ContainsKey("x") && jObj.ContainsKey("y"))
                                {
                                    operation.parameters[kvp.Key] = $"{jObj["x"]},{jObj["y"]}";
                                }
                                else if (jObj.ContainsKey("r") && jObj.ContainsKey("g") && jObj.ContainsKey("b"))
                                {
                                    operation.parameters[kvp.Key] = $"{jObj["r"]},{jObj["g"]},{jObj["b"]}";
                                }
                                else
                                {
                                    operation.parameters[kvp.Key] = jObj.ToString();
                                }
                            }
                            else
                            {
                                operation.parameters[kvp.Key] = kvp.Value.ToString();
                            }
                        }
                    }
                }

                Debug.Log($"[Nexus Editor MCP] About to execute operation with parameters: {operation.parameters.Count}");
                foreach (var param in operation.parameters)
                {
                    Debug.Log($"[Nexus Editor MCP] Parameter: {param.Key} = '{param.Value}'");
                }

                // Get message ID from either message.id or parameters.operationId
                string messageId = message.id;
                if (string.IsNullOrEmpty(messageId) && message.parameters != null && message.parameters.ContainsKey("operationId"))
                {
                    messageId = message.parameters["operationId"]?.ToString();
                }

                // Execute Unity operation (synchronous execution on main thread)
                ExecuteOperationAsync(operation, messageId);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus Editor MCP] Unity operation error: {e.Message}");
                _ = SendOperationResult(message.id, false, $"Error: {e.Message}");
            }
        }

        private static async void ExecuteOperationAsync(NexusUnityOperation operation, string messageId)
        {
            try
            {
                var executor = new NexusUnityExecutor();
                string result = await executor.ExecuteOperation(operation);
                bool success = !result.StartsWith("Error:") && !result.StartsWith("Failed:");
                
                Debug.Log($"[Nexus Editor MCP] Operation result: {result}");
                Debug.Log($"[Nexus Editor MCP] Operation success: {success}");

                // Send result to MCP server
                await SendOperationResult(messageId, success, result);

                // Output result to log
                if (success)
                {
                    Debug.Log($"[Nexus Editor MCP] SUCCESS: {result}");
                }
                else
                {
                    Debug.LogError($"[Nexus Editor MCP] FAILED: {result}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus Editor MCP] Async operation error: {e.Message}");
                await SendOperationResult(messageId, false, $"Error: {e.Message}");
            }
        }

        private static async Task SendConnectionPing()
        {
            try
            {
                var pingMessage = new MCPMessage
                {
                    type = "ping",
                    id = Guid.NewGuid().ToString(),
                    content = "Unity Editor connected"
                };
                
                var json = JsonConvert.SerializeObject(pingMessage);
                var buffer = Encoding.UTF8.GetBytes(json);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer), 
                    WebSocketMessageType.Text, 
                    true, 
                    CancellationToken.None
                );
                
                Debug.Log("[Nexus Editor MCP] Sent connection ping");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nexus Editor MCP] Failed to send ping: {e.Message}");
            }
        }

        private static async Task SendOperationResult(string messageId, bool success, string result)
        {
            if (!IsConnected) return;

            object structuredData = null;
            string displayContent = result;

            // Attempt JSON parsing and send as structured data
            try
            {
                // Send as structured data if result is JSON
                if (result.TrimStart().StartsWith("{") || result.TrimStart().StartsWith("["))
                {
                    structuredData = JsonConvert.DeserializeObject(result);
                    displayContent = success ? "Structured data retrieved" : result;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nexus Editor MCP] JSON parse failed: {e.Message}");
            }

            // Store result in content field according to MCP protocol
            var response = new MCPMessage
            {
                type = "operation_result",
                id = messageId,
                content = result, // Return original result (JSON string) as is
                data = new { success = success }
            };

            try
            {
                var json = JsonConvert.SerializeObject(response, Formatting.Indented);
                var buffer = Encoding.UTF8.GetBytes(json);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer), 
                    WebSocketMessageType.Text, 
                    true, 
                    cancellationTokenSource.Token
                );
                
                Debug.Log($"[Nexus Editor MCP] Sent operation result: {success}");
                Debug.Log($"[Nexus Editor MCP] Response JSON: {json}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus Editor MCP] Failed to send operation result: {e.Message}");
            }
        }

        public static void DisconnectFromMCPServer()
        {
            try
            {
                shouldReconnect = false; // Stop auto-reconnect on manual disconnect

                if (webSocket != null && isConnected)
                {
                    isConnected = false;
                    cancellationTokenSource?.Cancel();

                    if (webSocket.State == WebSocketState.Open)
                    {
                        webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                    }

                    webSocket.Dispose();
                    webSocket = null;

                    OnDisconnected?.Invoke();
                    Debug.Log("[Nexus Editor MCP] Disconnected from MCP Server");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus Editor MCP] Error during disconnect: {e.Message}");
            }
        }

        public static void SetServerUrl(string url)
        {
            serverUrl = url;
            Debug.Log($"[Nexus Editor MCP] Server URL changed to: {url}");
        }

        public static async void ReconnectToMCPServer()
        {
            shouldReconnect = true; // Enable reconnection
            reconnectAttempts = 0; // Reset counter

            DisconnectFromMCPServer();
            shouldReconnect = true; // Re-enable as Disconnect sets it to false

            await Task.Delay(1000); // Wait 1 second
            await ConnectToMCPServer();
        }

        private static string ConvertMCPToolToOperation(string mcpTool)
        {
            switch (mcpTool)
            {
                // GameObject operations
                case "unity_create_gameobject":
                case "create_gameobject":
                    return "CREATE_GAMEOBJECT";
                    
                case "unity_update_gameobject":
                case "update_gameobject":
                    return "UPDATE_GAMEOBJECT";
                    
                case "unity_delete_gameobject":
                case "delete_gameobject":
                    return "DELETE_GAMEOBJECT";
                    
                case "unity_set_transform":
                case "set_transform":
                    return "SET_TRANSFORM";
                    
                // Components
                case "unity_add_component":
                case "add_component":
                    return "ADD_COMPONENT";
                    
                case "unity_update_component":
                case "update_component":
                    return "UPDATE_COMPONENT";
                    
                // UI
                case "unity_create_ui":
                case "create_ui":
                    return "CREATE_UI";
                    
                // Terrain
                case "unity_create_terrain":
                case "create_terrain":
                    return "CREATE_TERRAIN";
                    
                case "unity_modify_terrain":
                case "modify_terrain":
                    return "MODIFY_TERRAIN";
                    
                // Camera
                case "unity_setup_camera":
                case "setup_camera":
                    return "SETUP_CAMERA";

                // Cinemachine
                case "unity_create_virtual_camera":
                case "create_virtual_camera":
                    return "CREATE_VIRTUAL_CAMERA";

                case "unity_create_freelook_camera":
                case "create_freelook_camera":
                    return "CREATE_FREELOOK_CAMERA";

                case "unity_setup_cinemachine_brain":
                case "setup_cinemachine_brain":
                    return "SETUP_CINEMACHINE_BRAIN";

                case "unity_update_virtual_camera":
                case "update_virtual_camera":
                    return "UPDATE_VIRTUAL_CAMERA";

                case "unity_create_dolly_track":
                case "create_dolly_track":
                    return "CREATE_DOLLY_TRACK";

                case "unity_add_collider_extension":
                case "add_collider_extension":
                    return "ADD_COLLIDER_EXTENSION";

                case "unity_add_confiner_extension":
                case "add_confiner_extension":
                    return "ADD_CONFINER_EXTENSION";

                case "unity_create_state_driven_camera":
                case "create_state_driven_camera":
                    return "CREATE_STATE_DRIVEN_CAMERA";

                case "unity_create_clear_shot_camera":
                case "create_clear_shot_camera":
                    return "CREATE_CLEAR_SHOT_CAMERA";

                case "unity_create_impulse_source":
                case "create_impulse_source":
                    return "CREATE_IMPULSE_SOURCE";

                case "unity_add_impulse_listener":
                case "add_impulse_listener":
                    return "ADD_IMPULSE_LISTENER";

                case "unity_create_blend_list_camera":
                case "create_blend_list_camera":
                    return "CREATE_BLEND_LIST_CAMERA";

                case "unity_create_target_group":
                case "create_target_group":
                    return "CREATE_TARGET_GROUP";

                case "unity_add_target_to_group":
                case "add_target_to_group":
                    return "ADD_TARGET_TO_GROUP";

                case "unity_set_camera_priority":
                case "set_camera_priority":
                    return "SET_CAMERA_PRIORITY";

                case "unity_set_camera_enabled":
                case "set_camera_enabled":
                    return "SET_CAMERA_ENABLED";

                case "unity_create_mixing_camera":
                case "create_mixing_camera":
                    return "CREATE_MIXING_CAMERA";

                case "unity_update_camera_target":
                case "update_camera_target":
                    return "UPDATE_CAMERA_TARGET";

                case "unity_update_brain_blend_settings":
                case "update_brain_blend_settings":
                    return "UPDATE_BRAIN_BLEND_SETTINGS";

                case "unity_get_active_camera_info":
                case "get_active_camera_info":
                    return "GET_ACTIVE_CAMERA_INFO";

                // Placement
                case "unity_place_objects":
                case "place_objects":
                    return "PLACE_OBJECTS";

                // Lighting
                case "unity_setup_lighting":
                case "setup_lighting":
                    return "SETUP_LIGHTING";

                // Material
                case "unity_create_material":
                case "create_material":
                    return "CREATE_MATERIAL";

                // Prefab
                case "unity_create_prefab":
                case "create_prefab":
                    return "CREATE_PREFAB";

                // Script
                case "unity_create_script":
                case "create_script":
                    return "CREATE_SCRIPT";

                // Scene
                case "unity_manage_scene":
                case "manage_scene":
                    return "MANAGE_SCENE";

                // Animation
                case "unity_create_animation":
                case "create_animation":
                    return "CREATE_ANIMATION";

                // Physics
                case "unity_setup_physics":
                case "setup_physics":
                    return "SETUP_PHYSICS";

                // Particle/VFX
                case "unity_create_particle_system":
                case "create_particle_system":
                    return "CREATE_PARTICLE_SYSTEM";

                case "unity_create_vfx_graph":
                case "create_vfx_graph":
                    return "CREATE_VFX_GRAPH";

                case "unity_create_shader_graph":
                case "create_shader_graph":
                    return "CREATE_SHADER_GRAPH";

                case "unity_setup_post_processing":
                case "setup_post_processing":
                    return "SETUP_POST_PROCESSING";

                case "unity_setup_lighting_scenarios":
                case "setup_lighting_scenarios":
                    return "SETUP_LIGHTING_SCENARIOS";

                case "unity_set_vfx_property":
                case "set_vfx_property":
                    return "SET_VFX_PROPERTY";

                case "unity_get_vfx_properties":
                case "get_vfx_properties":
                    return "GET_VFX_PROPERTIES";

                case "unity_trigger_vfx_event":
                case "trigger_vfx_event":
                    return "TRIGGER_VFX_EVENT";

                // VFX Graph Builder API
                case "unity_vfx_create":
                case "vfx_create":
                    return "VFX_CREATE";

                case "unity_vfx_add_context":
                case "vfx_add_context":
                    return "VFX_ADD_CONTEXT";

                case "unity_vfx_add_block":
                case "vfx_add_block":
                    return "VFX_ADD_BLOCK";

                case "unity_vfx_add_operator":
                case "vfx_add_operator":
                    return "VFX_ADD_OPERATOR";

                case "unity_vfx_link_contexts":
                case "vfx_link_contexts":
                    return "VFX_LINK_CONTEXTS";

                case "unity_vfx_get_structure":
                case "vfx_get_structure":
                    return "VFX_GET_STRUCTURE";

                case "unity_vfx_compile":
                case "vfx_compile":
                    return "VFX_COMPILE";

                case "unity_vfx_get_available_types":
                case "vfx_get_available_types":
                    return "VFX_GET_AVAILABLE_TYPES";

                case "unity_vfx_add_parameter":
                case "vfx_add_parameter":
                    return "VFX_ADD_PARAMETER";

                case "unity_vfx_connect_slots":
                case "vfx_connect_slots":
                    return "VFX_CONNECT_SLOTS";

                case "unity_vfx_set_attribute":
                case "vfx_set_attribute":
                    return "VFX_SET_ATTRIBUTE";

                case "unity_vfx_create_preset":
                case "vfx_create_preset":
                    return "VFX_CREATE_PRESET";

                // Navigation
                case "unity_setup_navmesh":
                case "setup_navmesh":
                    return "SETUP_NAVMESH";

                // Audio
                case "unity_create_audio_mixer":
                case "create_audio_mixer":
                    return "CREATE_AUDIO_MIXER";

                // Operation History/Undo/Redo
                case "unity_get_operation_history":
                    return "GET_OPERATION_HISTORY";
                    
                case "unity_undo_operation":
                    return "UNDO_OPERATION";
                    
                case "unity_redo_operation":
                    return "REDO_OPERATION";
                    
                case "unity_create_checkpoint":
                    return "CREATE_CHECKPOINT";
                    
                case "unity_restore_checkpoint":
                    return "RESTORE_CHECKPOINT";

                // Real-time Event Monitoring
                case "unity_monitor_play_state":
                    return "MONITOR_PLAY_STATE";

                case "unity_monitor_file_changes":
                    return "MONITOR_FILE_CHANGES";

                case "unity_monitor_compile":
                    return "MONITOR_COMPILE";

                case "unity_subscribe_events":
                    return "SUBSCRIBE_EVENTS";

                case "unity_get_events":
                    return "GET_EVENTS";

                case "unity_get_monitoring_status":
                    return "GET_MONITORING_STATUS";

                // Project Settings
                case "unity_get_build_settings":
                    return "GET_BUILD_SETTINGS";

                case "unity_get_player_settings":
                    return "GET_PLAYER_SETTINGS";

                case "unity_get_quality_settings":
                    return "GET_QUALITY_SETTINGS";

                case "unity_get_input_settings":
                    return "GET_INPUT_SETTINGS";

                case "unity_get_physics_settings":
                    return "GET_PHYSICS_SETTINGS";

                case "unity_get_project_summary":
                    return "GET_PROJECT_SUMMARY";

                // Scene Information
                case "unity_get_scene_info":
                    return "GET_SCENE_INFO";

                // Screenshot Capture Tools
                case "unity_capture_game_view":
                case "capture_game_view":
                    return "CAPTURE_GAME_VIEW";

                case "unity_capture_scene_view":
                case "capture_scene_view":
                    return "CAPTURE_SCENE_VIEW";

                case "unity_capture_region":
                case "capture_region":
                    return "CAPTURE_REGION";

                // Asset and Script Management
                case "unity_force_refresh_assets":
                case "force_refresh_assets":
                    return "FORCE_REFRESH_ASSETS";

                case "unity_invoke_context_menu":
                case "invoke_context_menu":
                    return "INVOKE_CONTEXT_MENU";

                // Inspector Information
                case "unity_get_inspector_info":
                    return "GET_INSPECTOR_INFO";

                case "unity_get_selected_object_info":
                    return "GET_SELECTED_OBJECT_INFO";

                case "unity_get_component_details":
                    return "GET_COMPONENT_DETAILS";

                // Asset Management
                case "unity_list_assets":
                case "unity_list_project_assets":
                case "list_assets":
                    return "LIST_ASSETS";

                // Folder Management
                case "unity_check_folder":
                case "check_folder":
                    return "CHECK_FOLDER";

                case "unity_create_folder":
                case "create_folder":
                    return "CREATE_FOLDER";

                case "unity_list_folders":
                case "list_folders":
                    return "LIST_FOLDERS";

                // New Tool Set
                case "unity_duplicate_gameobject":
                case "duplicate_gameobject":
                    return "DUPLICATE_GAMEOBJECT";
                    
                case "unity_find_gameobjects_by_component":
                case "find_gameobjects_by_component":
                case "find_by_component":
                    return "FIND_BY_COMPONENT";
                    
                case "unity_cleanup_empty_objects":
                case "cleanup_empty_objects":
                    return "CLEANUP_EMPTY_OBJECTS";
                    
                case "unity_group_gameobjects":
                case "group_gameobjects":
                    return "GROUP_GAMEOBJECTS";
                    
                case "unity_rename_asset":
                case "rename_asset":
                    return "RENAME_ASSET";
                    
                case "unity_move_asset":
                case "move_asset":
                    return "MOVE_ASSET";
                    
                case "unity_delete_asset":
                case "delete_asset":
                    return "DELETE_ASSET";
                    
                case "unity_pause_scene":
                case "pause_scene":
                    return "PAUSE_SCENE";

                case "unity_optimize_textures_batch":
                case "optimize_textures_batch":
                    return "OPTIMIZE_TEXTURES_BATCH";
                
                case "unity_analyze_draw_calls":
                case "analyze_draw_calls":
                    return "ANALYZE_DRAW_CALLS";
                
                case "unity_create_project_snapshot":
                case "create_project_snapshot":
                    return "CREATE_PROJECT_SNAPSHOT";
                
                case "unity_analyze_dependencies":
                case "analyze_dependencies":
                    return "ANALYZE_DEPENDENCIES";
                
                case "unity_export_project_structure":
                case "export_project_structure":
                    return "EXPORT_PROJECT_STRUCTURE";
                
                case "unity_validate_naming_conventions":
                case "validate_naming_conventions":
                    return "VALIDATE_NAMING_CONVENTIONS";
                
                case "unity_extract_all_text":
                case "extract_all_text":
                    return "EXTRACT_ALL_TEXT";
                
                case "unity_batch_rename":
                case "batch_rename":
                    return "BATCH_RENAME";
                
                case "unity_batch_import_settings":
                case "batch_import_settings":
                    return "BATCH_IMPORT_SETTINGS";
                
                case "unity_batch_prefab_update":
                case "batch_prefab_update":
                    return "BATCH_PREFAB_UPDATE";
                
                case "unity_find_unused_assets":
                case "find_unused_assets":
                    return "FIND_UNUSED_ASSETS";
                
                case "unity_estimate_build_size":
                case "estimate_build_size":
                    return "ESTIMATE_BUILD_SIZE";
                
                case "unity_performance_report":
                case "performance_report":
                    return "PERFORMANCE_REPORT";
                
                case "unity_auto_organize_folders":
                case "auto_organize_folders":
                    return "AUTO_ORGANIZE_FOLDERS";
                
                case "unity_generate_lod":
                case "generate_lod":
                    return "GENERATE_LOD";
                
                case "unity_auto_atlas_textures":
                case "auto_atlas_textures":
                    return "AUTO_ATLAS_TEXTURES";
                    
                // Game development features
                case "unity_create_game_controller":
                case "create_game_controller":
                    return "CREATE_GAME_CONTROLLER";
                    
                case "unity_setup_input_system":
                case "setup_input_system":
                    return "SETUP_INPUT_SYSTEM";
                    
                case "unity_create_state_machine":
                case "create_state_machine":
                    return "CREATE_STATE_MACHINE";
                    
                case "unity_setup_inventory_system":
                case "setup_inventory_system":
                    return "SETUP_INVENTORY_SYSTEM";
                    
                // Prototyping features
                case "unity_create_game_template":
                case "create_game_template":
                    return "CREATE_GAME_TEMPLATE";
                    
                case "unity_quick_prototype":
                case "quick_prototype":
                    return "QUICK_PROTOTYPE";
                    
                // AI and machine learning
                case "unity_setup_ml_agent":
                case "setup_ml_agent":
                    return "SETUP_ML_AGENT";
                    
                case "unity_create_neural_network":
                case "create_neural_network":
                    return "CREATE_NEURAL_NETWORK";
                    
                case "unity_setup_behavior_tree":
                case "setup_behavior_tree":
                    return "SETUP_BEHAVIOR_TREE";
                    
                case "unity_create_ai_pathfinding":
                case "create_ai_pathfinding":
                    return "CREATE_AI_PATHFINDING";
                    
                // Script editing features
                case "unity_modify_script":
                case "modify_script":
                    return "MODIFY_SCRIPT";
                    
                case "unity_edit_script_line":
                case "edit_script_line":
                    return "EDIT_SCRIPT_LINE";
                    
                case "unity_add_script_method":
                case "add_script_method":
                    return "ADD_SCRIPT_METHOD";
                    
                case "unity_update_script_variable":
                case "update_script_variable":
                    return "UPDATE_SCRIPT_VARIABLE";
                    
                // Debug and test tools
                case "unity_control_game_speed":
                case "control_game_speed":
                    return "CONTROL_GAME_SPEED";
                    
                case "unity_profile_performance":
                case "profile_performance":
                    return "PROFILE_PERFORMANCE";
                    
                case "unity_debug_draw":
                case "debug_draw":
                    return "DEBUG_DRAW";
                    
                case "unity_run_tests":
                case "run_unity_tests":
                    return "RUN_UNITY_TESTS";
                    
                case "unity_manage_breakpoints":
                case "manage_breakpoints":
                    return "MANAGE_BREAKPOINTS";
                    
                // Animation tools
                case "unity_create_animator_controller":
                case "create_animator_controller":
                    return "CREATE_ANIMATOR_CONTROLLER";
                    
                case "unity_add_animation_state":
                case "add_animation_state":
                    return "ADD_ANIMATION_STATE";
                    
                case "unity_create_animation_clip":
                case "create_animation_clip":
                    return "CREATE_ANIMATION_CLIP";
                    
                case "unity_setup_blend_tree":
                case "setup_blend_tree":
                    return "SETUP_BLEND_TREE";
                    
                case "unity_add_animation_transition":
                case "add_animation_transition":
                    return "ADD_ANIMATION_TRANSITION";
                    
                case "unity_setup_animation_layer":
                case "setup_animation_layer":
                    return "SETUP_ANIMATION_LAYER";
                    
                case "unity_create_animation_event":
                case "create_animation_event":
                    return "CREATE_ANIMATION_EVENT";
                    
                case "unity_setup_avatar":
                case "setup_avatar":
                    return "SETUP_AVATAR";
                    
                case "unity_create_timeline":
                case "create_timeline":
                    return "CREATE_TIMELINE";
                    
                case "unity_bake_animation":
                case "bake_animation":
                    return "BAKE_ANIMATION";
                    
                // Others
                case "unity_search":
                case "search_objects":
                    return "SEARCH_OBJECTS";
                    
                case "unity_console":
                case "console_operation":
                    return "CONSOLE_OPERATION";
                    
                case "unity_analyze_console_logs":
                case "analyze_console_logs":
                    return "ANALYZE_CONSOLE_LOGS";

                // Code search and exploration tools
                case "unity_grep_scripts":
                case "grep_scripts":
                    return "GREP_SCRIPTS";

                case "unity_read_script_range":
                case "read_script_range":
                    return "READ_SCRIPT_RANGE";

                case "unity_search_code":
                case "search_code":
                    return "SEARCH_CODE";

                case "unity_list_script_files":
                case "list_script_files":
                    return "LIST_SCRIPT_FILES";

                // Scene manipulation tools
                case "unity_load_scene":
                case "load_scene":
                    return "LOAD_SCENE";

                case "unity_unload_scene":
                case "unload_scene":
                    return "UNLOAD_SCENE";

                case "unity_set_active_scene":
                case "set_active_scene":
                    return "SET_ACTIVE_SCENE";

                case "unity_list_all_scenes":
                case "list_all_scenes":
                    return "LIST_ALL_SCENES";

                case "unity_add_scene_to_build":
                case "add_scene_to_build":
                    return "ADD_SCENE_TO_BUILD";

                // Asset search tools
                case "unity_search_prefabs_by_component":
                case "search_prefabs_by_component":
                    return "SEARCH_PREFABS_BY_COMPONENT";

                case "unity_find_material_usage":
                case "find_material_usage":
                    return "FIND_MATERIAL_USAGE";

                case "unity_find_texture_usage":
                case "find_texture_usage":
                    return "FIND_TEXTURE_USAGE";

                case "unity_get_asset_dependencies":
                case "get_asset_dependencies":
                    return "GET_ASSET_DEPENDENCIES";

                case "unity_find_missing_references":
                case "find_missing_references":
                    return "FIND_MISSING_REFERENCES";

                default:
                    return mcpTool.ToUpper();
            }
        }

        /// <summary>
        /// MCP Service status for debugging
        /// </summary>
        [MenuItem("Tools/Synaptic Pro/AI Connection Status", false, 11)]
        public static void ShowMCPStatus()
        {
            string connectionStatus = IsConnected ? "✅ Connected" : "❌ Disconnected";
            string webSocketState = webSocket?.State.ToString() ?? "null";

            string status = $@"🔗 AI Connection Status

{connectionStatus}

Details:
• Initialized: {(isInitialized ? "✅" : "❌")}
• Server URL: {serverUrl ?? "Not configured"}
• Message Queue: {messageQueue.Count} items
• WebSocket State: {webSocketState}
• Reconnect Attempts: {reconnectAttempts}/{maxReconnectAttempts}
• Auto Reconnect: {(shouldReconnect ? "Enabled" : "Disabled")}

If you have issues, try 'AI Reconnect'.";

            Debug.Log(status);
            EditorUtility.DisplayDialog("AI Connection Status", status, "OK");
        }
        
        /// <summary>
        /// Connection status display for toolbar
        /// </summary>
        // [MenuItem("Tools/AI Connection Status", false, 1)]
        public static void QuickStatus()
        {
            string status = IsConnected ? "✅ AI: Connected" : "❌ AI: Disconnected";
            Debug.Log($"[Nexus MCP] {status}");
            EditorUtility.DisplayDialog("Connection Status", status, "OK");
        }

        /// <summary>
        /// Manual reconnect for debugging
        /// </summary>
        [MenuItem("Tools/Synaptic Pro/AI Reconnect", false, 12)]
        public static void ManualReconnect()
        {
            Debug.Log("[Nexus Editor MCP] Manual reconnect requested");
            
            // Display confirmation dialog to user
            bool reconnect = EditorUtility.DisplayDialog(
                "Repair AI Connection",
                "Re-establish connection with Claude and other AI.\n\n" +
                "Use in the following cases:\n" +
                "• No response from AI\n" +
                "• Unity tools have operation timeout\n" +
                "• Connection became unstable\n\n" +
                "Execute reconnection?",
                "Reconnect",
                "Cancel"
            );

            if (reconnect)
            {
                ReconnectToMCPServer();
                EditorUtility.DisplayDialog(
                    "Reconnection Complete",
                    "Re-established connection with Claude and other AI.\n" +
                    "Check console log for connection status.",
                    "OK"
                );
            }
        }
        
        /// <summary>
        /// Simple reconnect button (for toolbar)
        /// </summary>
        // [MenuItem("Tools/🔗 AI Reconnect", false, 0)]
        public static void QuickReconnect()
        {
            Debug.Log("[Nexus Editor MCP] Quick reconnect requested");
            ReconnectToMCPServer();
        }
        
        /// <summary>
        /// Enable auto-reconnect (shown when currently OFF)
        /// </summary>
        [MenuItem("Tools/Synaptic Pro/⚙️ Auto Reconnect: Enable", false, 13)]
        public static void EnableAutoReconnect()
        {
            enableAutoReconnect = true;
            EditorPrefs.SetBool(autoReconnectKey, enableAutoReconnect);
            Debug.Log("[Nexus Editor MCP] Auto-reconnect: enabled");
        }

        [MenuItem("Tools/Synaptic Pro/⚙️ Auto Reconnect: Enable", true)]
        public static bool EnableAutoReconnectValidate()
        {
            // Load from EditorPrefs to ensure correct state
            enableAutoReconnect = EditorPrefs.GetBool(autoReconnectKey, true);
            // Show "Enable" option only when currently OFF
            return !enableAutoReconnect;
        }

        /// <summary>
        /// Disable auto-reconnect (shown when currently ON)
        /// </summary>
        [MenuItem("Tools/Synaptic Pro/⚙️ Auto Reconnect: Disable", false, 13)]
        public static void DisableAutoReconnect()
        {
            enableAutoReconnect = false;
            EditorPrefs.SetBool(autoReconnectKey, enableAutoReconnect);
            Debug.Log("[Nexus Editor MCP] Auto-reconnect: disabled");
        }

        [MenuItem("Tools/Synaptic Pro/⚙️ Auto Reconnect: Disable", true)]
        public static bool DisableAutoReconnectValidate()
        {
            // Load from EditorPrefs to ensure correct state
            enableAutoReconnect = EditorPrefs.GetBool(autoReconnectKey, true);
            // Show "Disable" option only when currently ON
            return enableAutoReconnect;
        }

        /// <summary>
        /// Update Claude Desktop config file port
        /// </summary>
        private static void UpdateClaudeDesktopConfigForPort(int newPort)
        {
            try
            {
                string configPath;

                // Detect platform-specific Claude Desktop config path
                if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor)
                {
                    configPath = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "Claude", "claude_desktop_config.json"
                    );
                }
                else if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
                {
                    configPath = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                        "Claude", "claude_desktop_config.json"
                    );
                }
                else
                {
                    Debug.LogWarning("[Nexus Editor MCP] Unsupported platform for Claude Desktop config update");
                    return;
                }
                
                if (!System.IO.File.Exists(configPath))
                {
                    Debug.LogWarning($"[Nexus Editor MCP] Claude Desktop config file not found: {configPath}");
                    return;
                }

                string configContent = System.IO.File.ReadAllText(configPath);
                
                // Update WebSocket port (supports multiple patterns)
                bool updated = false;
                string newPattern = $"ws://localhost:{newPort}";
                
                // Check all known port patterns
                string[] oldPatterns = {
                    "ws://localhost:8080",
                    "ws://localhost:8081", 
                    "ws://localhost:8082",
                    "ws://localhost:8083",
                    "ws://localhost:8084"
                };
                
                foreach (string oldPattern in oldPatterns)
                {
                    if (configContent.Contains(oldPattern) && oldPattern != newPattern)
                    {
                        configContent = configContent.Replace(oldPattern, newPattern);
                        updated = true;
                        Debug.Log($"[Nexus Editor MCP] 🔄 Auto-updated Claude Desktop config: {oldPattern} → {newPattern}");
                    }
                }
                
                if (updated)
                {
                    
                    // Create backup
                    string backupPath = configPath + $".backup_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                    System.IO.File.Copy(configPath, backupPath);
                    
                    // Write new config
                    System.IO.File.WriteAllText(configPath, configContent);
                    
                    Debug.Log($"[Nexus Editor MCP] 🔄 Auto-updated Claude Desktop config");
                    Debug.Log($"[Nexus Editor MCP] 📁 Backup: {backupPath}");
                    Debug.Log($"[Nexus Editor MCP] ⚠️ Claude Desktop restart required");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Nexus Editor MCP] Claude Desktop config update error: {e.Message}");
            }
        }
        
        
        /// <summary>
        /// Light connection test (WebSocket state check only)
        /// </summary>
        private static async Task LightConnectionTest()
        {
            try
            {
                if (webSocket?.State == WebSocketState.Open)
                {
                    // WebSocket is open but test actual communication
                    var testMessage = JsonConvert.SerializeObject(new { type = "ping", id = "test" });
                    var buffer = Encoding.UTF8.GetBytes(testMessage);
                    await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    
                    // Connection check successful
                    isConnected = true;
                    reconnectPhase = 0;
                    return;
                }
                else if (webSocket?.State == WebSocketState.Closed || webSocket?.State == WebSocketState.Aborted)
                {
                    // If Closed state, immediately proceed to full reconnect
                    Debug.Log("[Nexus MCP] WebSocket is closed, skipping to full reconnection");
                    reconnectPhase = 2;
                    lastReconnectTime = Time.realtimeSinceStartup;
                    return;
                }
            }
            catch (Exception)
            {
                // Light test failed, proceed to next phase
            }
        }
        
        /// <summary>
        /// Full reconnect attempt
        /// </summary>
        private static async Task FullReconnectAttempt()
        {
            isReconnecting = true;
            try
            {
                // Clean up existing connection
                if (webSocket != null)
                {
                    try { webSocket.Dispose(); } catch { }
                    webSocket = null;
                }
                
                await Task.Delay(1000); // Wait 1 second
                
                // Attempt new connection
                await ConnectToMCPServer();
                
                if (isConnected)
                {
                    reconnectPhase = 0;
                    Debug.Log("[Nexus MCP] 🔄 Auto-reconnect successful");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nexus MCP] Reconnect failed: {e.Message}");
            }
            finally
            {
                isReconnecting = false;
            }
        }
        /// <summary>
        /// Retry after failure
        /// </summary>
        private static async Task RetryReconnect()
        {
            isReconnecting = true;
            try
            {
                // Rescan port and reconnect
                DetectAndSetAvailablePort();
                await Task.Delay(50); // Immediate reconnect
                await ConnectToMCPServer();
                
                if (isConnected)
                {
                    reconnectPhase = 0;
                    Debug.Log("[Nexus MCP] 🔄 Retry reconnect successful");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nexus MCP] Retry failed: {e.Message}");
            }
            finally
            {
                isReconnecting = false;
            }
        }
        
        /// <summary>
        /// Initialize when connection loss detected
        /// </summary>
        private static void OnConnectionLost()
        {
            isConnected = false;
            lastConnectionCheckTime = Time.realtimeSinceStartup;
            reconnectPhase = 0;
        }
        
        /// <summary>
        /// About Nexus - Version information and credits
        /// </summary>
        [MenuItem("Tools/Synaptic Pro/Join Discord Community", false, 20)]
        public static void OpenDiscord()
        {
            Application.OpenURL("https://discord.gg/MXwHCVWmPe");
        }
        
        [MenuItem("Tools/Synaptic Pro/About Synaptic Pro", false, 21)]
        public static void ShowAbout()
        {
            EditorUtility.DisplayDialog(
                "About Synaptic AI Pro for Unity",
                "Synaptic AI Pro for Unity\n" +
                "Version 1.1.5\n\n" +
                "Control Unity Editor with natural language through Claude AI\n\n" +
                "Features:\n" +
                "• 235+ Professional Tools\n" +
                "• Scene Management & GameObject Control\n" +
                "• Lighting, Cinemachine, Physics, Animation\n" +
                "• Natural Language Automation\n" +
                "• Unity 2022.3+ and Unity 6.0+ Compatible\n\n" +
                "Website: https://synaptic-ai.net\n" +
                "Discord: https://discord.gg/MXwHCVWmPe\n" +
                "Support: sekiguchimiu@gmail.com\n\n" +
                "© 2025 Miu Sekiguchi",
                "OK"
            );
        }
    }
}














