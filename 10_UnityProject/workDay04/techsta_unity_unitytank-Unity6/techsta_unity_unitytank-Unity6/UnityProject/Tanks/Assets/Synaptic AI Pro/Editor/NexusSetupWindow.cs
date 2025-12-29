using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// MCP Server Setup and Local CLI Management Window
    /// One-touch setup for MCP server and configuration of various AI tools
    /// </summary>
    public class NexusMCPSetupWindow : EditorWindow
    {
        [MenuItem("Tools/Synaptic Pro/Synaptic Setup", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<NexusMCPSetupWindow>("Synaptic Pro Setup");
            window.minSize = new Vector2(800, 600);
            window.Show();
        }

        private NexusMCPSetupManager mcpSetupManager;
        private NexusMCPSetupManager.SetupStatus mcpStatus;
        private Vector2 scrollPosition;
        private bool mcpServerRunning = false;
        
        // Tabs
        private int selectedTab = 0;
        private string[] tabNames = new string[] { "AI Connection", "Help" };

        // MCP Settings
        private int mcpPort = 3000;
        private int wsPort = 8080;

        // Animation
        private bool isConnecting = false;
        private float animationTime = 0f;
        private const float CONNECTION_TIMEOUT = 60f; // 60 second timeout

        // Setup state management
        private bool mcpConfigured = false;

        // AI Client selection (v1.1.3)
        private enum AIClientType
        {
            ClaudeDesktopOrCursor,      // Full mode (index.js) - 246 tools
            CursorOrLMStudioEssential,  // Essential mode (index-essential.js) - 80 tools
            GitHubCopilot               // Dynamic mode (hub-server.js) - 8→dynamic tools
        }
        private AIClientType selectedAIClient = AIClientType.ClaudeDesktopOrCursor;

        private string[] connectingMessages = new string[] 
        {
            "Preparing AI Connection",
            "Starting MCP Server",
            "Establishing connection with desktop AI apps",
            "Auto-generating configuration files",
            "AI connection almost ready"
        };
        
        private GUIStyle headerStyle;
        private GUIStyle setupButtonStyle;
        private GUIStyle statusStyle;
        
        private async void OnEnable()
        {
            mcpSetupManager = NexusMCPSetupManager.Instance;
            await RefreshStatus();
        }
        
        private void InitializeStyles()
        {
            headerStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.2f, 0.6f, 1f) }
            };
            
            setupButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(20, 20, 10, 10),
                normal = { background = CreateColorTexture(new Color(0.2f, 0.6f, 1f)) },
                hover = { background = CreateColorTexture(new Color(0.3f, 0.7f, 1f)) },
                active = { background = CreateColorTexture(new Color(0.1f, 0.5f, 0.9f)) }
            };
            
            statusStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 14,
                padding = new RectOffset(10, 10, 10, 10),
                wordWrap = true
            };
        }
        
        private Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
        
        private void OnGUI()
        {
            if (headerStyle == null)
                InitializeStyles();
            
            // Animation update
            if (isConnecting)
            {
                animationTime += Time.deltaTime;
                Repaint(); // Repaint for animation
            }

            DrawHeader();

            // Tab rendering
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(30));
            EditorGUILayout.Space(10);
            
            switch (selectedTab)
            {
                case 0:
                    DrawAIConnectionTab();
                    break;
                // case 1:
                //     DrawCLIConfigTab();
                //     break;
                case 1:
                    DrawHelpTab();
                    break;
            }
        }
        
        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("Synaptic Pro Setup", headerStyle, GUILayout.Height(40));
            EditorGUILayout.Space(10);
            
            // Concise status display
            if (mcpStatus != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                
                var statusText = "";
                var statusColor = Color.gray;
                
                if (mcpServerRunning)
                {
                    statusText = "MCP Server Running";
                    statusColor = Color.green;
                }
                else if (mcpStatus.isMCPInstalled)
                {
                    statusText = "AI Connection Ready";
                    statusColor = new Color(0.2f, 0.8f, 0.2f);
                }
                else
                {
                    statusText = "Initial Setup";
                    statusColor = new Color(0.5f, 0.5f, 0.5f);
                }
                
                var oldColor = GUI.contentColor;
                GUI.contentColor = statusColor;
                GUILayout.Label(statusText, EditorStyles.boldLabel);
                GUI.contentColor = oldColor;
                
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.Space(5);
        }
        
        private void DrawAIConnectionTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            // One-click startup
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("MCP Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Once MCP setup is complete, Unity tools are immediately available in Claude Desktop", EditorStyles.wordWrappedLabel);
            
            EditorGUILayout.Space(10);
            
            // Display connection animation
            if (isConnecting)
            {
                DrawConnectingAnimation();
            }
            else if (!mcpConfigured)
            {
                // Setup not complete
                EditorGUILayout.LabelField("Don't have Claude Desktop? Download it first:", EditorStyles.helpBox);
                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Claude Desktop"))
                {
                    Application.OpenURL("https://claude.ai/download");
                }
                if (GUILayout.Button("ChatGPT Desktop"))
                {
                    Application.OpenURL("https://chatgpt.com/");
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(15);

                // AI Tool Selection (v1.1.0)
                EditorGUILayout.LabelField("Select Your AI Tool:", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                var oldBgColor = GUI.backgroundColor;

                // Claude Desktop / Cursor option
                GUI.backgroundColor = selectedAIClient == AIClientType.ClaudeDesktopOrCursor
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : Color.white;
                if (GUILayout.Button(
                    "Claude Desktop / Cursor\n" +
                    "(Full Mode - All 246 tools)",
                    GUILayout.Height(60)))
                {
                    selectedAIClient = AIClientType.ClaudeDesktopOrCursor;
                }

                EditorGUILayout.Space(5);

                // Cursor/LM Studio Essential option
                GUI.backgroundColor = selectedAIClient == AIClientType.CursorOrLMStudioEssential
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : Color.white;
                if (GUILayout.Button(
                    "Cursor / LM Studio Essential\n" +
                    "(Lighter Mode - 80 essential tools, 62% smaller)",
                    GUILayout.Height(60)))
                {
                    selectedAIClient = AIClientType.CursorOrLMStudioEssential;
                }

                EditorGUILayout.Space(5);

                // VS Code (GitHub Copilot) option
                GUI.backgroundColor = selectedAIClient == AIClientType.GitHubCopilot
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : Color.white;
                if (GUILayout.Button(
                    "VS Code (GitHub Copilot)\n" +
                    "(Dynamic Mode - 8 tools + on-demand loading)",
                    GUILayout.Height(60)))
                {
                    selectedAIClient = AIClientType.GitHubCopilot;
                }

                GUI.backgroundColor = oldBgColor;

                EditorGUILayout.Space(5);

                // Info box based on selection
                if (selectedAIClient == AIClientType.ClaudeDesktopOrCursor)
                {
                    EditorGUILayout.HelpBox(
                        "Full Mode: All 246 Unity tools available immediately.\n" +
                        "• Claude Desktop: Prompt caching for longer sessions\n" +
                        "• Cursor: May show 80-tool warning (can be ignored)",
                        MessageType.Info);
                }
                else if (selectedAIClient == AIClientType.CursorOrLMStudioEssential)
                {
                    EditorGUILayout.HelpBox(
                        "Essential Mode: 80 carefully selected tools (62% lighter).\n" +
                        "• Perfect for Cursor and LM Studio to avoid context bloat\n" +
                        "• Includes: GameObject, Camera, Scene, UI, Screenshot, Animation basics\n" +
                        "• Removed: Scripting, GOAP, Weather, Advanced VFX, Batch ops",
                        MessageType.Info);
                }
                else // VS Code (GitHub Copilot)
                {
                    EditorGUILayout.HelpBox(
                        "Dynamic Mode: Start with 8 tools, load more on-demand.\n" +
                        "• GitHub Copilot with MCP support\n" +
                        "• Use select_tools() to load additional tool categories\n" +
                        "• Perfect for avoiding tool limit warnings",
                        MessageType.Info);
                }

                EditorGUILayout.Space(10);

                // MCP Setup button
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
                if (GUILayout.Button("Complete MCP Setup", setupButtonStyle, GUILayout.Height(60)))
                {
                    ConfigureMCP();
                }
                GUI.backgroundColor = oldColor;
            }
            else
            {
                // Setup complete
                EditorGUILayout.BeginHorizontal();

                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
                GUILayout.Button("✓ MCP Setup Complete", setupButtonStyle, GUILayout.Height(50));
                GUI.backgroundColor = oldColor;

                // Reconfigure button
                GUI.backgroundColor = new Color(0.8f, 0.6f, 0.2f);
                if (GUILayout.Button("🔄 Reconfigure", GUILayout.Width(100), GUILayout.Height(50)))
                {
                    ResetMCPConfiguration();
                }
                GUI.backgroundColor = oldColor;

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);

                // Display appropriate info based on selected mode (v1.1.0)
                string setupCompleteMessage;
                if (selectedAIClient == AIClientType.GitHubCopilot)
                {
                    setupCompleteMessage =
                        "Setup complete! Dynamic Mode (hub-server.js)\n" +
                        "• GitHub Copilot (.vscode/mcp.json)\n\n" +
                        "Restart VS Code to activate.\n" +
                        "Use select_tools() to load tool categories dynamically.";
                }
                else
                {
                    setupCompleteMessage =
                        "Setup complete! Full Mode (index.js) - All 246 tools\n" +
                        "• Claude Desktop\n" +
                        "• Cursor (~/.cursor/mcp.json)\n" +
                        "• VS Code (.vscode/mcp.json)\n\n" +
                        "Restart/Reload your AI tool to activate Unity MCP.";
                }

                EditorGUILayout.HelpBox(setupCompleteMessage, MessageType.Info);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(20);

            // How to Use
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("How to Use Unity Tools", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("1. Open Claude Desktop / Cursor / VS Code", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("2. Ask: \"What Unity tools are available?\"", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("3. Give instructions: \"Create a red cube\" or \"Show me the scene hierarchy\"", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Full Usage Guide", GUILayout.Height(35)))
            {
                ShowUsageGuide();
            }
            
            EditorGUILayout.EndVertical();
            
            // Auto-generated connection settings
            if (mcpServerRunning)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("Connection Settings (Auto-generated)", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);
                
                EditorGUILayout.LabelField("MCP Server: localhost:8080");
                EditorGUILayout.LabelField("Tool Name: unity");
                
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("💫 Usage:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1. Open ChatGPT or Claude Desktop");
                EditorGUILayout.LabelField("2. Start a new chat");
                EditorGUILayout.LabelField("3. Tips for using tools:");
                EditorGUILayout.LabelField("   • Include words like \"tools\" or \"unity\"");
                EditorGUILayout.LabelField("   Example: \"Use unity tools to create a red cube\"");
                EditorGUILayout.LabelField("4. AI will automatically control Unity with MCP tools");
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        
        private void DrawServerManagementTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("MCP Server Management", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // Server status
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Server Status:");
            
            var statusColor = mcpServerRunning ? Color.green : Color.red;
            var statusText = mcpServerRunning ? "● Running" : "● Stopped";
            
            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            GUILayout.Label(statusText);
            GUI.contentColor = oldColor;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // Control buttons
            EditorGUILayout.BeginHorizontal();
            
            EditorGUI.BeginDisabledGroup(!mcpStatus?.isMCPInstalled ?? true);
            
            if (!mcpServerRunning)
            {
                if (GUILayout.Button("▶️ Start Server", GUILayout.Height(30)))
                {
                    StartMCPServer();
                }
            }
            else
            {
                if (GUILayout.Button("⏹️ Stop Server", GUILayout.Height(30)))
                {
                    StopMCPServer();
                }
            }
            
            if (GUILayout.Button("🔄 Restart", GUILayout.Height(30)))
            {
                RestartMCPServer();
            }
            
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(20);
            
            // Server settings
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Server Settings:", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("MCP Port:", GUILayout.Width(100));
            mcpPort = EditorGUILayout.IntField(mcpPort);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("WebSocket Port:", GUILayout.Width(100));
            wsPort = EditorGUILayout.IntField(wsPort);
            EditorGUILayout.EndHorizontal();
            
            EditorGUI.indentLevel--;
            
            // Connection Info
            if (mcpStatus?.isMCPInstalled ?? false)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Connection Info:", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"MCP: localhost:{mcpPort}");
                EditorGUILayout.LabelField($"WebSocket: ws://localhost:{wsPort}");
                EditorGUILayout.LabelField($"Accessible from Desktop AI");
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            
            // Log Viewer
            EditorGUILayout.Space(20);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Server Log", EditorStyles.boldLabel);
            
            // Display server logs here
            EditorGUILayout.TextArea("Server logs will be displayed here...", GUILayout.Height(200));
            
            EditorGUILayout.EndVertical();
        }
        
        /* Planned for future implementation
        private void DrawCLIConfigTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("CLI AI Configuration Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Batch create configuration files for various CLI AI tools", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);
            
            // Claude Code configuration (2025 MCP specification)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Claude Code (Anthropic)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Official CLI for Claude with MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                GenerateClaudeCodeConfig();
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://docs.anthropic.com/en/docs/claude-code/mcp");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("Config: .claude/settings.local.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            
            // Cursor settings (2025 Popular MCP Client)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Cursor (Anysphere)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("AI-powered code editor with MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                if (GenerateCursorConfig())
                {
                    EditorUtility.DisplayDialog(
                        "Cursor Setup Complete",
                        "Cursor MCP configuration created successfully!\n\n" +
                        "Config file: ~/.cursor/mcp.json\n\n" +
                        "Next steps:\n" +
                        "1. Reload MCP Servers in Cursor:\n" +
                        "   Settings → MCP Servers → Reload\n" +
                        "2. Unity tools will be available in Cursor",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Cursor Setup Failed",
                        "Failed to create Cursor configuration.\n\n" +
                        "Please check the Console for details.",
                        "OK"
                    );
                }
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://www.synaptic-ai.net/ja/docs/setup#cursor");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Config: ~/.cursor/mcp.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // VS Code settings (Claude Code extension)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("VS Code (Microsoft)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Visual Studio Code with Claude Code extension MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                if (GenerateVSCodeConfig())
                {
                    EditorUtility.DisplayDialog(
                        "VS Code Setup Complete",
                        "VS Code MCP configuration created successfully!\n\n" +
                        "Config file: .vscode/mcp.json\n\n" +
                        "Next steps:\n" +
                        "1. Reload VS Code window:\n" +
                        "   Cmd/Ctrl + Shift + P → 'Reload Window'\n" +
                        "2. Unity tools will be available in Claude Code",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "VS Code Setup Failed",
                        "Failed to create VS Code configuration.\n\n" +
                        "Please check the Console for details.",
                        "OK"
                    );
                }
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://www.synaptic-ai.net/ja/docs/setup#vscode");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Config: .vscode/mcp.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Windsurf settings (2025 MCP Client)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Windsurf (Codeium)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The IDE that writes with you - MCP enabled", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                GenerateWindsurfConfig();
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://codeium.com/windsurf");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("Config: ~/.windsurf/mcp_servers.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Gemini CLI settings (2025 MCP Support)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Gemini CLI (Google)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Google's CLI with built-in MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                GenerateGeminiCLIConfig();
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://google-gemini.github.io/gemini-cli/docs/tools/mcp-server.html");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("Config: ~/.gemini/settings.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // OpenAI Codex CLI settings (2025 MCP Support)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("OpenAI Codex CLI", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Lightweight coding agent with MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                GenerateCodexCLIConfig();
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://github.com/openai/codex");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("Config: ~/.codex/config.toml", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(20);
            
            // Batch settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Batch Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("Configure all supported MCP clients at once", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);
            
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("Configure All MCP Clients", GUILayout.Height(40)))
            {
                GenerateAllCLIConfigs();
            }
            GUI.backgroundColor = oldColor;
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(20);
            
            // File watch settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("File Watch Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Monitor project file changes and notify AI", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Watch Configuration", GUILayout.Height(30)))
            {
                GenerateWatchConfig();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Monitored files: .cs, .js, .ts, .json, .md", EditorStyles.miniLabel);
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        */
        
        private void DrawHelpTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("AI Connection Help", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // Simple description
            DrawHelpSection("What is Synaptic Pro?",
                "A tool that connects Unity with desktop AI apps like Claude, Gemini, and more.\n" +
                "No complex setup required - get started with one click.");
            
            // How to use
            DrawHelpSection("How to Use",
                "1. Click 'Start AI Connection' in the AI Connection tab\n" +
                "2. Open Claude/Gemini desktop app\n" +
                "3. Connect to localhost:3000 in AI app settings\n" +
                "4. AI is now available in Unity!");
            
            // Supported AI apps
            DrawHelpSection("Supported AI Apps",
                "• Claude Desktop (Recommended)\n" +
                "• ChatGPT Desktop\n" +
                "• Gemini Desktop\n" +
                "• Other MCP-compatible AI apps");
            
            // Troubleshooting
            DrawHelpSection("When Things Don't Work",
                "• Error when clicking 'Start AI Connection'\n" +
                "  → Run Unity as administrator\n\n" +
                "• Cannot connect from AI app\n" +
                "  → Check firewall settings\n\n" +
                "• Connection succeeds but cannot control Unity\n" +
                "  → Confirm 'Start AI Connection' is pressed in Unity");
            
            // Links
            EditorGUILayout.Space(20);
            if (GUILayout.Button("Download Claude Desktop"))
            {
                Application.OpenURL("https://claude.ai/download");
            }
            
            if (GUILayout.Button("Download ChatGPT Desktop"))
            {
                Application.OpenURL("https://openai.com/chatgpt/download/");
            }
            
            if (GUILayout.Button("Download Gemini Desktop"))
            {
                Application.OpenURL("https://gemini.google.com/app");
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawStatusItem(string label, bool isInstalled, string version)
        {
            EditorGUILayout.BeginHorizontal();
            
            var icon = isInstalled ? "✅" : "❌";
            var color = isInstalled ? Color.green : Color.red;
            
            var oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(icon, GUILayout.Width(20));
            GUI.contentColor = oldColor;
            
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            
            if (!string.IsNullOrEmpty(version))
            {
                EditorGUILayout.LabelField(version, EditorStyles.miniLabel);
            }
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawHelpSection(string title, string content)
        {
            GUILayout.Label(title, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(content, EditorStyles.wordWrappedLabel);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        
        private void DrawConnectingAnimation()
        {
            // Animation box
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Rotating spinner
            var spinnerChars = new string[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            var spinnerIndex = (int)(animationTime * 10) % spinnerChars.Length;

            // Current message (fixed to avoid looping)
            var messageIndex = Mathf.Min((int)(animationTime / 3), connectingMessages.Length - 1);
            var currentMessage = connectingMessages[messageIndex];
            
            // Animation display
            var animatedText = $"{spinnerChars[spinnerIndex]} {currentMessage}...";

            var centeredStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16
            };

            GUILayout.Label(animatedText, centeredStyle, GUILayout.Height(60));

            // Progress bar (100% at the last message)
            var progress = (messageIndex + 1f) / connectingMessages.Length;
            var rect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(rect, progress, "");
            
            // Cancel button
            EditorGUILayout.Space(10);
            if (GUILayout.Button("⏹️ Cancel", GUILayout.Height(30)))
            {
                isConnecting = false;
                Debug.Log("[Synaptic] AI connection cancelled");
                Repaint();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private async void StartAIConnection()
        {
            try
            {
                Debug.Log("[Synaptic] Starting AI Connection...");

                // Start animation
                isConnecting = true;
                animationTime = 0f;
                Repaint();

                // Auto-setup if needed
                if (mcpSetupManager != null)
                {
                    var status = await mcpSetupManager.CheckSetupStatus();
                    if (!status.isMCPInstalled)
                    {
                        await mcpSetupManager.RunCompleteSetup();
                    }
                }

                // Start MCP server
                mcpServerRunning = await mcpSetupManager.StartMCPServer();

                // Stop animation
                isConnecting = false;

                if (mcpServerRunning)
                {
                    // Auto-generate configuration files for desktop AI
                    GenerateDesktopAIConfigs();

                    Debug.Log("[Synaptic] ✅ AI connection setup complete! Desktop AI will auto-connect");
                    EditorUtility.DisplayDialog("AI Connection Ready", 
                        "Connection completed successfully.\n\n" +
                        "Unity tools are now available in AI apps.\n" +
                        "Type \"tools\" or \"unity\" to use the tools.", 
                        "OK");
                }
                else
                {
                    Debug.LogError("[Synaptic] ❌ Failed to start AI connection");

                    // Detailed guide when MCP server is not found
                    EditorUtility.DisplayDialog(
                        "MCP Server Not Found",
                        "Please launch Claude Desktop first to start AI connection.\n\n" +
                        "Steps:\n" +
                        "1. Launch Claude Desktop app\n" +
                        "2. Wait a moment, then press 'Start AI Connection' again\n\n" +
                        "Note: Unity acts as a client to the MCP server.",
                        "OK");
                }
                
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] AI connection error: {e.Message}");
                isConnecting = false; // Stop animation
                mcpServerRunning = false;
                Repaint();
            }
        }
        
        private void StopAIConnection()
        {
            mcpServerRunning = false;
            Debug.Log("[Synaptic] AI Connection stopped");
            Repaint();
        }
        
        private void ShowUsageGuide()
        {
            EditorUtility.DisplayDialog(
                "How to Use Unity MCP Tools",
                "Tips for reliable tool usage\n\n" +
                "1. Launch your AI tool:\n" +
                "   • Claude Desktop / ChatGPT Desktop\n" +
                "   • Cursor / VS Code\n\n" +
                "2. Start a new chat\n\n" +
                "3. First, ask the AI:\n" +
                "   • \"What tools are available?\"\n" +
                "   • \"What can unity tools do?\"\n\n" +
                "4. Then, give specific instructions:\n" +
                "   • \"Use unity tools to create a red cube\"\n" +
                "   • \"Add a Player controller with tools\"\n\n" +
                "※ Including words like \"tools\" or \"unity\"\n" +
                "  helps the AI use tools more reliably!",
                "OK"
            );
        }
        
        private void ShowAIAppsDialog()
        {
            var option = EditorUtility.DisplayDialogComplex(
                "Select Desktop AI App",
                "Which AI app would you like to use?\n\n" +
                "ChatGPT: The world's most popular AI\n" +
                "Claude: AI with advanced code understanding\n\n" +
                "※ MCP-compatible version required",
                "ChatGPT",
                "Claude Desktop",
                "Cancel"
            );
            
            switch (option)
            {
                case 0: // ChatGPT
                    Application.OpenURL("https://chatgpt.com/");
                    break;
                case 1: // Claude
                    Application.OpenURL("https://claude.ai/download");
                    break;
            }
        }
        
        // Helper method to get appropriate server path based on selected AI client
        private string GetServerScriptPath()
        {
            var mcpServerPath = FindMCPServerPath();

            if (selectedAIClient == AIClientType.GitHubCopilot)
            {
                // Dynamic mode for GitHub Copilot (VS Code)
                return Path.Combine(mcpServerPath, "hub-server.js");
            }
            else if (selectedAIClient == AIClientType.CursorOrLMStudioEssential)
            {
                // Essential mode: 80 tools
                return Path.Combine(mcpServerPath, "index-essential.js");
            }
            else
            {
                // Full mode: 246 tools
                return Path.Combine(mcpServerPath, "index.js");
            }
        }

        // Update package.json "type" field based on selected server
        private void UpdatePackageJsonForSelectedServer()
        {
            try
            {
                var mcpServerPath = FindMCPServerPath();
                var packageJsonPath = Path.Combine(mcpServerPath, "package.json");

                if (!File.Exists(packageJsonPath))
                {
                    Debug.LogWarning("[Synaptic] package.json not found. Skipping update.");
                    return;
                }

                // Read existing package.json
                var packageJsonContent = File.ReadAllText(packageJsonPath);
                var packageJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(packageJsonContent);

                if (selectedAIClient == AIClientType.GitHubCopilot)
                {
                    // hub-server.js uses ESM (import) - requires "type": "module"
                    packageJson["type"] = "module";
                    packageJson["main"] = "hub-server.js";
                    Debug.Log("[Synaptic] package.json updated for hub-server.js (ESM)");
                }
                else
                {
                    // index.js uses CommonJS (require) - remove "type" field or set to "commonjs"
                    if (packageJson.ContainsKey("type"))
                    {
                        packageJson.Remove("type");
                    }
                    packageJson["main"] = "index.js";
                    Debug.Log("[Synaptic] package.json updated for index.js (CommonJS)");
                }

                // Write updated package.json
                File.WriteAllText(packageJsonPath, JsonConvert.SerializeObject(packageJson, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Failed to update package.json: {e.Message}");
            }
        }

        private void GenerateDesktopAIConfigs()
        {
            try
            {
                var detectedAIs = DetectInstalledAIs();
                var configuredCount = 0;
                var configuredTools = new List<string>();

                foreach (var ai in detectedAIs)
                {
                    switch (ai.ToLower())
                    {
                        case "claude":
                            if (GenerateClaudeConfig())
                            {
                                configuredCount++;
                                configuredTools.Add("Claude Desktop");
                            }
                            break;
                        case "chatgpt":
                            if (GenerateChatGPTConfig())
                            {
                                configuredCount++;
                                configuredTools.Add("ChatGPT Desktop");
                            }
                            break;
                        case "gemini":
                            if (GenerateGeminiConfig())
                            {
                                configuredCount++;
                                configuredTools.Add("Gemini Desktop");
                            }
                            break;
                    }
                }

                // Always configure Cursor (user-level config)
                if (GenerateCursorConfig())
                {
                    configuredCount++;
                    configuredTools.Add("Cursor");
                }

                // Always configure VS Code (project-level config)
                if (GenerateVSCodeConfig())
                {
                    configuredCount++;
                    configuredTools.Add("VS Code");
                }

                // Always configure LM Studio (user-level config)
                if (GenerateLMStudioConfig())
                {
                    configuredCount++;
                    configuredTools.Add("LM Studio");
                }

                // Update MCPServer/mcp-config.json with current project path
                UpdateMCPServerConfig();

                Debug.Log($"[Synaptic] Auto-generated {configuredCount} MCP configurations: {string.Join(", ", configuredTools)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Configuration file generation error: {e.Message}");
            }
        }
        
        private List<string> DetectInstalledAIs()
        {
            var installedAIs = new List<string>();
            
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // macOS application detection
                var appsDir = "/Applications";
                if (Directory.Exists($"{appsDir}/Claude.app")) installedAIs.Add("Claude");
                if (Directory.Exists($"{appsDir}/ChatGPT.app")) installedAIs.Add("ChatGPT");
                if (Directory.Exists($"{appsDir}/Gemini.app")) installedAIs.Add("Gemini");

                // Homebrew cask installation detection
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (Directory.Exists($"{homeDir}/Applications/Claude.app")) installedAIs.Add("Claude");
                if (Directory.Exists($"{homeDir}/Applications/ChatGPT.app")) installedAIs.Add("ChatGPT");
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // Windows installation detection
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // Claude: Check both installation paths and config directory
                if (Directory.Exists($"{programFiles}/Claude") ||
                    Directory.Exists($"{localAppData}/Programs/Claude") ||
                    Directory.Exists($"{appData}/Claude"))
                {
                    installedAIs.Add("Claude");
                }

                // ChatGPT
                if (Directory.Exists($"{programFiles}/ChatGPT") ||
                    Directory.Exists($"{localAppData}/ChatGPT") ||
                    Directory.Exists($"{appData}/ChatGPT"))
                {
                    installedAIs.Add("ChatGPT");
                }
            }
            
            return installedAIs.Distinct().ToList();
        }
        
        private bool GenerateClaudeConfig()
        {
            try
            {
                var claudeConfigDir = DetectClaudeConfigPath();
                if (string.IsNullOrEmpty(claudeConfigDir))
                {
                    Debug.LogWarning("[Synaptic] Claude Desktop not found.");
                    return false;
                }

            if (!Directory.Exists(claudeConfigDir))
            {
                Directory.CreateDirectory(claudeConfigDir);
            }

            var configPath = Path.Combine(claudeConfigDir, "claude_desktop_config.json");

            // Load existing configuration
            dynamic existingConfig = null;
            if (File.Exists(configPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(configPath);
                    existingConfig = JsonConvert.DeserializeObject(existingJson);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Synaptic] Failed to load existing Claude configuration: {e.Message}");
                }
            }

            // Unity MCP server configuration (v1.1.0: uses selected server script)
            // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
            var unityMcpServer = new
            {
                command = "node",
                args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                env = new { }
            };

            // Merge with existing configuration
            dynamic claudeConfig;
            if (existingConfig?.mcpServers != null)
            {
                // Preserve existing mcpServers and add unity server
                var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig.mcpServers));
                mcpServers["unity"] = unityMcpServer;

                claudeConfig = new
                {
                    mcpServers = mcpServers
                };

                // Preserve other existing settings
                var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig));
                configDict["mcpServers"] = mcpServers;
                claudeConfig = configDict;
            }
            else
            {
                // Create new configuration
                claudeConfig = new
                {
                    mcpServers = new Dictionary<string, object>
                    {
                        ["unity"] = unityMcpServer
                    }
                };
            }
            
            File.WriteAllText(configPath, JsonConvert.SerializeObject(claudeConfig, Newtonsoft.Json.Formatting.Indented));

            Debug.Log($"[Synaptic] Claude configuration file created: {configPath}");
            return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Claude configuration error: {e.Message}");
                return false;
            }
        }
        
        private string DetectClaudeConfigPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // macOS Claude configuration path candidates
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "com.anthropic.claudefordesktop"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "claude")
                };
                
                foreach (var path in candidates)
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(parentDir))
                    {
                        return path;
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            }
            
            return null;
        }
        
        private bool GenerateGeminiConfig()
        {
            try
            {
                var geminiConfigDir = DetectGeminiConfigPath();
                if (string.IsNullOrEmpty(geminiConfigDir))
                {
                    Debug.LogWarning("[Synaptic] Gemini Desktop not found.");
                    return false;
                }

            if (!Directory.Exists(geminiConfigDir))
            {
                Directory.CreateDirectory(geminiConfigDir);
            }

            var geminiConfig = new
            {
                servers = new[]
                {
                    new
                    {
                        name = "unity",
                        url = "http://localhost:3000",
                        type = "mcp"
                    }
                }
            };

            var configPath = Path.Combine(geminiConfigDir, "config.json");
            File.WriteAllText(configPath, JsonConvert.SerializeObject(geminiConfig, Newtonsoft.Json.Formatting.Indented));

            Debug.Log($"[Synaptic] Gemini configuration file created: {configPath}");
            return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Gemini configuration error: {e.Message}");
                return false;
            }
        }
        
        private string DetectGeminiConfigPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "gemini"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Gemini")
                };

                foreach (var path in candidates)
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(parentDir))
                    {
                        return path;
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gemini");
            }

            return null;
        }

        private string DetectCursorConfigPath()
        {
            // Cursor native MCP config: ~/.cursor/mcp.json
            var cursorDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cursor"
            );

            return cursorDir;
        }

        private bool GenerateCursorConfig()
        {
            try
            {
                var cursorConfigDir = DetectCursorConfigPath();
                if (string.IsNullOrEmpty(cursorConfigDir))
                {
                    Debug.LogWarning("[Synaptic] Could not determine Cursor config path.");
                    return false;
                }

                if (!Directory.Exists(cursorConfigDir))
                {
                    Directory.CreateDirectory(cursorConfigDir);
                }

                var configPath = Path.Combine(cursorConfigDir, "mcp.json");

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Synaptic] Failed to load existing Cursor configuration: {e.Message}");
                    }
                }

                // Unity MCP server configuration (v1.1.3: uses selected server script)
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                // Essential mode uses index-essential.js (80 tools), Full mode uses index.js (246 tools)
                var unityMcpServer = new
                {
                    command = "node",
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    cwd = NormalizePathForJson(FindMCPServerPath())  // CRITICAL: Node.js needs to run from MCPServer directory
                };

                // Merge with existing configuration
                dynamic cursorConfig;
                if (existingConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    cursorConfig = configDict;
                }
                else
                {
                    cursorConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(cursorConfig, Newtonsoft.Json.Formatting.Indented));

                Debug.Log($"[Synaptic] ✅ Cursor configuration file created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Cursor configuration error: {e.Message}");
                return false;
            }
        }

        private string DetectVSCodeConfigPath()
        {
            // VS Code project-level MCP config: .vscode/mcp.json
            var projectPath = Application.dataPath.Replace("/Assets", "");
            var vscodeDir = Path.Combine(projectPath, ".vscode");

            return vscodeDir;
        }

        private bool GenerateVSCodeConfig()
        {
            try
            {
                var vscodeConfigDir = DetectVSCodeConfigPath();
                if (string.IsNullOrEmpty(vscodeConfigDir))
                {
                    Debug.LogWarning("[Synaptic] Could not determine VS Code config path.");
                    return false;
                }

                if (!Directory.Exists(vscodeConfigDir))
                {
                    Directory.CreateDirectory(vscodeConfigDir);
                }

                var configPath = Path.Combine(vscodeConfigDir, "mcp.json");

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Synaptic] Failed to load existing VS Code configuration: {e.Message}");
                    }
                }

                // Unity MCP server configuration (v1.1.0: VS Code format with "servers" and "type")
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    type = "stdio",  // Required by VS Code
                    command = "node",
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    cwd = NormalizePathForJson(FindMCPServerPath())  // CRITICAL: Node.js needs to run from MCPServer directory
                };

                // Merge with existing configuration (VS Code uses "servers", not "mcpServers")
                dynamic vscodeConfig;
                if (existingConfig?.servers != null)
                {
                    var servers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.servers));
                    servers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["servers"] = servers;
                    vscodeConfig = configDict;
                }
                else
                {
                    vscodeConfig = new
                    {
                        servers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(vscodeConfig, Newtonsoft.Json.Formatting.Indented));

                Debug.Log($"[Synaptic] ✅ VS Code configuration file created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] VS Code configuration error: {e.Message}");
                return false;
            }
        }

        private string DetectLMStudioConfigPath()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // macOS: ~/.lmstudio/
                return Path.Combine(homeDir, ".lmstudio");
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // Windows: %USERPROFILE%/.lmstudio/
                return Path.Combine(homeDir, ".lmstudio");
            }

            return null;
        }

        private bool GenerateLMStudioConfig()
        {
            try
            {
                var lmstudioConfigDir = DetectLMStudioConfigPath();
                if (string.IsNullOrEmpty(lmstudioConfigDir))
                {
                    Debug.LogWarning("[Synaptic] Could not determine LM Studio config path.");
                    return false;
                }

                if (!Directory.Exists(lmstudioConfigDir))
                {
                    Directory.CreateDirectory(lmstudioConfigDir);
                }

                var configPath = Path.Combine(lmstudioConfigDir, "mcp.json");

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Synaptic] Failed to load existing LM Studio configuration: {e.Message}");
                    }
                }

                // Unity MCP server configuration (LM Studio uses same format as Cursor)
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    command = "node",
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    cwd = NormalizePathForJson(FindMCPServerPath()),  // CRITICAL: Node.js needs to run from MCPServer directory
                    env = new { }
                };

                // Merge with existing configuration
                dynamic lmstudioConfig;
                if (existingConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    lmstudioConfig = configDict;
                }
                else
                {
                    lmstudioConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(lmstudioConfig, Newtonsoft.Json.Formatting.Indented));

                Debug.Log($"[Synaptic] ✅ LM Studio configuration file created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] LM Studio configuration error: {e.Message}");
                return false;
            }
        }

        private bool UpdateMCPServerConfig()
        {
            try
            {
                var mcpServerPath = FindMCPServerPath();
                if (string.IsNullOrEmpty(mcpServerPath))
                {
                    Debug.LogWarning("[Synaptic] MCP Server path not found.");
                    return false;
                }

                var configPath = Path.Combine(mcpServerPath, "mcp-config.json");
                if (!File.Exists(configPath))
                {
                    Debug.LogWarning($"[Synaptic] mcp-config.json not found at: {configPath}");
                    return false;
                }

                // Read the config file
                var configContent = File.ReadAllText(configPath);

                // Replace placeholder with actual path
                configContent = configContent.Replace("{{PROJECT_MCP_SERVER_PATH}}", mcpServerPath);

                // Write back
                File.WriteAllText(configPath, configContent);

                Debug.Log($"[Synaptic] ✅ Updated mcp-config.json with project path: {mcpServerPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] mcp-config.json update error: {e.Message}");
                return false;
            }
        }

        private bool GenerateChatGPTConfig()
        {
            try
            {
                var chatgptConfigDir = DetectChatGPTConfigPath();
                if (string.IsNullOrEmpty(chatgptConfigDir))
                {
                    Debug.LogWarning("[Synaptic] ChatGPT Desktop not found.");
                    return false;
                }

            if (!Directory.Exists(chatgptConfigDir))
            {
                Directory.CreateDirectory(chatgptConfigDir);
            }

            var configPath = Path.Combine(chatgptConfigDir, "config.json");

            // Load existing configuration
            dynamic existingConfig = null;
            if (File.Exists(configPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(configPath);
                    existingConfig = JsonConvert.DeserializeObject(existingJson);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Synaptic] Failed to load existing ChatGPT configuration: {e.Message}");
                }
            }

            // Unity MCP server configuration (v1.1.0: uses selected server script)
            // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
            var unityMcpServer = new
            {
                command = "node",
                args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                env = new { }
            };

            // Merge with existing configuration
            dynamic chatgptConfig;
            if (existingConfig?.mcpServers != null)
            {
                // Preserve existing mcpServers and add unity server
                var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig.mcpServers));
                mcpServers["unity"] = unityMcpServer;

                // Preserve other existing settings
                var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig));
                configDict["mcpServers"] = mcpServers;
                chatgptConfig = configDict;
            }
            else
            {
                // Create new configuration
                chatgptConfig = new
                {
                    mcpServers = new Dictionary<string, object>
                    {
                        ["unity"] = unityMcpServer
                    }
                };
            }

            File.WriteAllText(configPath, JsonConvert.SerializeObject(chatgptConfig, Newtonsoft.Json.Formatting.Indented));

            Debug.Log($"[Synaptic] ChatGPT configuration file created: {configPath}");
            return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] ChatGPT configuration error: {e.Message}");
                return false;
            }
        }
        
        private string DetectChatGPTConfigPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "com.openai.chat"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "ChatGPT"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "chatgpt")
                };
                
                foreach (var path in candidates)
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(parentDir))
                    {
                        return path;
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatGPT");
            }
            
            return null;
        }
        
        private async Task RefreshStatus()
        {
            mcpStatus = await mcpSetupManager.CheckSetupStatus();
            Repaint();
        }
        
        private async void CheckMCPStatus()
        {
            await RefreshStatus();
        }
        
        private void SaveMCPSettings()
        {
            // Save to .env file
            var envPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), "MCPServer", ".env");
            var envContent = new List<string>
            {
                $"PORT={mcpPort}",
                $"WS_PORT={wsPort}"
            };

            System.IO.File.WriteAllLines(envPath, envContent);
            Debug.Log("[Synaptic] MCP settings saved");
        }


        private async void StartMCPServer()
        {
            mcpServerRunning = await mcpSetupManager.StartMCPServer();
            Repaint();
        }

        private void StopMCPServer()
        {
            // Server stop implementation
            mcpServerRunning = false;
            Repaint();
        }
        
        private async void RestartMCPServer()
        {
            StopMCPServer();
            await Task.Delay(1000);
            await Task.Run(() => StartMCPServer());
        }
        
        private void ConfigureMCP()
        {
            try
            {
                Debug.Log("[Synaptic] Starting MCP setup...");
                
                // Check and install dependencies
                if (!CheckAndInstallDependencies())
                {
                    EditorUtility.DisplayDialog(
                        "Setup Error",
                        "Failed to install required dependencies. Please check the console for details.",
                        "OK"
                    );
                    return;
                }
                
                // Generate MCP configuration files
                GenerateDesktopAIConfigs();

                // Update package.json based on selected server
                UpdatePackageJsonForSelectedServer();

                // Initialize MCP server
                if (mcpSetupManager == null)
                {
                    mcpSetupManager = NexusMCPSetupManager.Instance;
                }

                // Set configuration complete flag
                mcpConfigured = true;
                
                Debug.Log("[Synaptic] MCP setup completed. Ready for AI integration.");

                // Display success message based on selected AI client (v1.1.0)
                string successMessage;
                if (selectedAIClient == AIClientType.GitHubCopilot)
                {
                    successMessage =
                        "MCP configuration completed for GitHub Copilot!\n\n" +
                        "Mode: Dynamic Tool Loading (hub-server.js)\n" +
                        "Configuration: .vscode/mcp.json\n\n" +
                        "⚠️ Important: Restart VS Code to activate Unity MCP.\n\n" +
                        "Initial tools: 8 management tools\n" +
                        "Dynamic loading: Use select_tools() to load more\n\n" +
                        "Example:\n" +
                        "\"Use select_tools to load GameObject and Material tools\"";
                }
                else
                {
                    successMessage =
                        "MCP configuration completed successfully!\n\n" +
                        "Mode: Full Mode (index.js) - All 246 tools\n" +
                        "Configurations created for:\n" +
                        "• Claude Desktop (claude_desktop_config.json)\n" +
                        "• Cursor (~/.cursor/mcp.json)\n" +
                        "• VS Code (.vscode/mcp.json)\n\n" +
                        "⚠️ Important: Restart/Reload your AI tool to activate Unity MCP.\n\n" +
                        "After restarting, ask:\n" +
                        "\"What Unity tools are available?\"";
                }

                EditorUtility.DisplayDialog(
                    "MCP Setup Complete",
                    successMessage,
                    "OK"
                );
                
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] MCP configuration error: {e.Message}");
                EditorUtility.DisplayDialog(
                    "MCP Setup Error",
                    $"An error occurred during MCP setup:\n{e.Message}",
                    "OK"
                );
            }
        }
        
        private void ResetMCPConfiguration()
        {
            var confirmed = EditorUtility.DisplayDialog(
                "Reset MCP Settings",
                "Reset MCP settings and reconfigure?\n\n" +
                "Current AI connection will also be stopped.",
                "Reset",
                "Cancel"
            );
            
            if (confirmed)
            {
                // Reset configuration flag
                mcpConfigured = false;
                mcpSetupManager = null;

                Debug.Log("[Synaptic] MCP settings have been reset. Please reconfigure.");

                EditorUtility.DisplayDialog(
                    "Settings Reset Complete",
                    "MCP settings have been reset.\n" +
                    "Please reconfigure from the 'Complete MCP Setup' button.",
                    "OK"
                );

                Repaint();
            }
        }
        
        // CLI AI configuration generation methods
        private void GenerateClaudeCodeConfig()
        {
            try
            {
                // Create Claude Code specific configuration (no detection required)
                if (GenerateClaudeCodeSpecificConfig())
                {
                    Debug.Log("[Synaptic] Claude Code configuration complete");

                    EditorUtility.DisplayDialog(
                        "Claude Code Configuration Complete",
                        "Claude Code configuration has been completed.\n\n" +
                        "Configuration files:\n" +
                        "• Project: .claude/settings.local.json\n" +
                        "• Global: ~/.claude.json\n\n" +
                        "How to use:\n" +
                        "1. Restart Claude Code\n" +
                        "2. Unity MCP tools will be automatically enabled\n\n" +
                        "Verification commands:\n" +
                        "• claude mcp list\n" +
                        "• claude mcp get unity",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create Claude Code configuration. Please check Unity Console for errors.", "OK");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Claude Code configuration error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Claude Code configuration failed:\n{e.Message}", "OK");
            }
        }


        private void GenerateGeminiCLIConfig()
        {
            try
            {
                // Gemini CLI configuration path (2025 specification)
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".gemini", "settings.json"
                );
                
                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
                
                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Synaptic] Failed to load existing Gemini CLI configuration: {e.Message}");
                    }
                }

                // Unity MCP server configuration (Gemini CLI 2025 format)
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    command = "node",
                    args = new[] { NormalizePathForJson(Path.Combine(FindMCPServerPath(), "index.js")) },
                    env = new Dictionary<string, object>(),
                    timeout = 30000,
                    trust = false
                };

                // Gemini CLI configuration structure
                dynamic geminiConfig;
                if (existingConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity"] = unityMcpServer;
                    
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    geminiConfig = configDict;
                }
                else
                {
                    geminiConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity"] = unityMcpServer
                        }
                    };
                }
                
                File.WriteAllText(configPath, JsonConvert.SerializeObject(geminiConfig, Newtonsoft.Json.Formatting.Indented));
                
                Debug.Log($"[Synaptic] Gemini CLI configuration complete: {configPath}");
                
                EditorUtility.DisplayDialog(
                    "Gemini CLI Configuration Complete",
                    "Gemini CLI MCP configuration created successfully.\n\n" +
                    "Config file: ~/.gemini/settings.json\n\n" +
                    "Next steps:\n" +
                    "1. Restart Gemini CLI\n" +
                    "2. Unity MCP tools will be available\n\n" +
                    "Commands:\n" +
                    "• gemini mcp list\n" +
                    "• gemini mcp add unity",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Gemini CLI configuration error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create Gemini CLI configuration:\n{e.Message}", "OK");
            }
        }
        
        private void GenerateCodexCLIConfig()
        {
            try
            {
                // OpenAI Codex CLI configuration path (2025 specification - TOML format)
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex", "config.toml"
                );

                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                // Load existing configuration (simple string processing for TOML format)
                var existingContent = "";
                if (File.Exists(configPath))
                {
                    existingContent = File.ReadAllText(configPath);
                }

                // Unity MCP server configuration (TOML format)
                var mcpServerConfig = @"
[mcp_servers.unity]
command = ""node""
args = [""{0}""]
description = ""Unity game development tools via MCP""

";
                // Normalize paths for TOML: use forward slashes (works cross-platform)
                var formattedConfig = string.Format(mcpServerConfig,
                    NormalizePathForJson(Path.Combine(FindMCPServerPath(), "index.js")));

                // Check if Unity configuration is already included in existing settings
                if (!existingContent.Contains("[mcp_servers.unity]"))
                {
                    // Check if MCP servers section exists
                    if (!existingContent.Contains("[mcp_servers"))
                    {
                        existingContent += "\n# MCP Server Configurations\n";
                    }
                    existingContent += formattedConfig;
                }
                else
                {
                    // Update existing Unity configuration
                    var startIndex = existingContent.IndexOf("[mcp_servers.unity]");
                    var endIndex = existingContent.IndexOf("\n[", startIndex + 1);
                    if (endIndex == -1) endIndex = existingContent.Length;
                    
                    existingContent = existingContent.Remove(startIndex, endIndex - startIndex);
                    existingContent = existingContent.Insert(startIndex, formattedConfig);
                }
                
                File.WriteAllText(configPath, existingContent);
                
                Debug.Log($"[Synaptic] OpenAI Codex CLI configuration complete: {configPath}");

                EditorUtility.DisplayDialog(
                    "Codex CLI Configuration Complete",
                    "OpenAI Codex CLI MCP configuration created successfully.\n\n" +
                    "Config file: ~/.codex/config.toml\n\n" +
                    "Next steps:\n" +
                    "1. Restart Codex CLI\n" +
                    "2. Unity MCP tools will be available\n\n" +
                    "Usage:\n" +
                    "• codex --help\n" +
                    "• Enable MCP servers in your session",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] OpenAI Codex CLI configuration error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"OpenAI Codex CLI configuration failed:\n{e.Message}", "OK");
            }
        }
        
        
        private void GenerateWindsurfConfig()
        {
            try
            {
                // Windsurf uses user-level config
                var windsurfConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".windsurf", "mcp_servers.json"
                );
                
                var windsurfDir = Path.GetDirectoryName(windsurfConfigPath);
                if (!Directory.Exists(windsurfDir))
                {
                    Directory.CreateDirectory(windsurfDir);
                }
                
                // Unity MCP server settings
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    command = "node",
                    args = new[] { NormalizePathForJson(Path.Combine(FindMCPServerPath(), "index.js")) },
                    env = new Dictionary<string, object>(),
                    description = "Unity game development tools"
                };
                
                // Load existing settings
                dynamic existingConfig = null;
                if (File.Exists(windsurfConfigPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(windsurfConfigPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Synaptic] Failed to load existing Windsurf config: {e.Message}");
                    }
                }
                
                // Windsurf MCP settings (2025 specification)
                dynamic windsurfConfig;
                if (existingConfig?.servers != null)
                {
                    var servers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.servers));
                    servers["unity"] = unityMcpServer;
                    
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["servers"] = servers;
                    windsurfConfig = configDict;
                }
                else
                {
                    windsurfConfig = new
                    {
                        servers = new Dictionary<string, object>
                        {
                            ["unity"] = unityMcpServer
                        }
                    };
                }
                
                File.WriteAllText(windsurfConfigPath, JsonConvert.SerializeObject(windsurfConfig, Newtonsoft.Json.Formatting.Indented));
                
                Debug.Log($"[Synaptic] Windsurf config file created: {windsurfConfigPath}");
                
                EditorUtility.DisplayDialog(
                    "Windsurf Configuration Complete",
                    "Windsurf MCP configuration created successfully.\n\n" +
                    "Config file: ~/.windsurf/mcp_servers.json\n\n" +
                    "Next steps:\n" +
                    "1. Restart Windsurf\n" +
                    "2. Unity MCP server will be available\n" +
                    "3. Use '@unity' to access Unity tools\n\n" +
                    "The IDE that writes with you!",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Windsurf config error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create Windsurf configuration:\n{e.Message}", "OK");
            }
        }
        
        private void GenerateAllCLIConfigs()
        {
            try
            {
                Debug.Log("[Synaptic] Configuring all MCP clients...");
                
                var configuredTools = new List<string>();
                
                // Claude Code
                try
                {
                    if (GenerateClaudeCodeSpecificConfig())
                    {
                        configuredTools.Add("Claude Code");
                    }
                }
                catch { /* Ignore individual CLI errors */ }
                
                // Cursor
                try
                {
                    if (GenerateCursorConfig())
                    {
                        configuredTools.Add("Cursor");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Synaptic] Cursor config skipped: {e.Message}");
                }

                // VS Code
                try
                {
                    if (GenerateVSCodeConfig())
                    {
                        configuredTools.Add("VS Code");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Synaptic] VS Code config skipped: {e.Message}");
                }

                // Windsurf
                try
                {
                    GenerateWindsurfConfig();
                    configuredTools.Add("Windsurf");
                }
                catch (Exception e) 
                { 
                    Debug.LogWarning($"[Synaptic] Windsurf config skipped: {e.Message}");
                }
                
                // Gemini CLI
                try
                {
                    GenerateGeminiCLIConfig();
                    configuredTools.Add("Gemini CLI");
                }
                catch (Exception e) 
                { 
                    Debug.LogWarning($"[Synaptic] Gemini CLI config skipped: {e.Message}");
                }
                
                // Codex CLI
                try
                {
                    GenerateCodexCLIConfig();
                    configuredTools.Add("OpenAI Codex CLI");
                }
                catch (Exception e) 
                { 
                    Debug.LogWarning($"[Synaptic] Codex CLI config skipped: {e.Message}");
                }
                
                // Generic settings for other MCP-compatible tools
                GenerateUniversalCLIConfig();
                configuredTools.Add("Generic MCP Clients");
                
                Debug.Log($"[Synaptic] All CLI AI configs completed: {string.Join(", ", configuredTools)}");
                
                EditorUtility.DisplayDialog(
                    "MCP Client Configuration Complete",
                    $"Successfully configured the following MCP clients:\n\n" +
                    $"✅ {string.Join("\n✅ ", configuredTools)}\n\n" +
                    "Unity MCP tools are now available in these applications.\n\n" +
                    "Next steps:\n" +
                    "1. Restart the configured applications\n" +
                    "2. Unity MCP tools will be automatically available\n" +
                    "3. Check each app's MCP panel or use '@unity' mention",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] All CLI AI config error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Error during CLI AI configuration:\n{e.Message}", "OK");
            }
        }
        
        private void GenerateUniversalCLIConfig()
        {
            try
            {
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var configDir = Path.Combine(homeDir, ".config", "synaptic-pro");
                
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
                
                // Generic MCP config file
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var universalConfig = new
                {
                    mcp_servers = new Dictionary<string, object>
                    {
                        ["unity"] = new
                        {
                            command = "node",
                            args = new[] { NormalizePathForJson(Path.Combine(FindMCPServerPath(), "index.js")) },
                            env = new { },
                            description = "Unity game development tools via MCP",
                            capabilities = new[] { "game_objects", "ui_creation", "scripting", "ai_agents", "animation" }
                        }
                    },
                    version = "1.1.5",
                    created_by = "Synaptic Pro Unity Integration"
                };
                
                var configPath = Path.Combine(configDir, "mcp-config.json");
                File.WriteAllText(configPath, JsonConvert.SerializeObject(universalConfig, Newtonsoft.Json.Formatting.Indented));
                
                // Create usage example script
                var scriptPath = Path.Combine(configDir, "start-unity-mcp.sh");
                var scriptContent = $@"#!/bin/bash
# Unity MCP Server Starter Script
# Generated by Synaptic Pro Unity Integration

export MCP_CONFIG_PATH=""{NormalizePathForJson(configPath)}""
export UNITY_PROJECT_PATH=""{NormalizePathForJson(Application.dataPath.Replace($"{Path.DirectorySeparatorChar}Assets", ""))}""
export MCP_SERVER_PATH=""{NormalizePathForJson(FindMCPServerPath())}""

echo ""Starting Unity MCP Server...""
echo ""Project: $UNITY_PROJECT_PATH""
echo ""MCP Server: $MCP_SERVER_PATH""
echo ""Config: $MCP_CONFIG_PATH""

node ""$MCP_SERVER_PATH/index.js""
";
                
                File.WriteAllText(scriptPath, scriptContent);
                
                // Grant execute permission (Unix)
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    System.Diagnostics.Process.Start("chmod", $"+x \"{scriptPath}\"");
                }
                
                Debug.Log($"[Synaptic] Generic CLI config created: {configPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Synaptic] Failed to create generic CLI config: {e.Message}");
            }
        }
        
        private void GenerateWatchConfig()
        {
            try
            {
                var projectPath = Application.dataPath.Replace("/Assets", "");
                var watchConfigPath = Path.Combine(projectPath, ".synaptic-watch");
                
                var watchConfig = new
                {
                    watch_patterns = new[]
                    {
                        "**/*.cs",
                        "**/*.js", 
                        "**/*.ts",
                        "**/*.json",
                        "**/*.md",
                        "**/*.yaml",
                        "**/*.yml"
                    },
                    ignore_patterns = new[]
                    {
                        "**/node_modules/**",
                        "**/Library/**",
                        "**/Temp/**",
                        "**/Logs/**",
                        "**/obj/**",
                        "**/bin/**"
                    },
                    commands = new
                    {
                        on_change = new[]
                        {
                            "echo \"File changed: {file}\"",
                            "# Add your custom commands here"
                        }
                    },
                    mcp_integration = new
                    {
                        enabled = true,
                        server_url = "ws://127.0.0.1:8080",
                        notify_on_change = true
                    }
                };
                
                File.WriteAllText(watchConfigPath, JsonConvert.SerializeObject(watchConfig, Newtonsoft.Json.Formatting.Indented));
                
                // Create watch script
                var watchScriptPath = Path.Combine(projectPath, "watch-synaptic.sh");
                var watchScript = $@"#!/bin/bash
# Synaptic File Watcher Script
# Monitors project files and notifies AI tools

if command -v fswatch >/dev/null 2>&1; then
    echo ""Starting Synaptic file watcher with fswatch...""
    fswatch -o . | while read f; do
        echo ""Files changed - notifying AI tools...""
        # Add notification logic here
    done
elif command -v inotifywait >/dev/null 2>&1; then
    echo ""Starting Synaptic file watcher with inotifywait...""
    inotifywait -m -r -e modify,create,delete . | while read path action file; do
        echo ""$path$file $action - notifying AI tools...""
        # Add notification logic here
    done
else
    echo ""Please install fswatch (macOS) or inotify-tools (Linux)""
    echo ""macOS: brew install fswatch""
    echo ""Linux: sudo apt-get install inotify-tools""
fi
";
                
                File.WriteAllText(watchScriptPath, watchScript);
                
                // Grant execute permission
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    System.Diagnostics.Process.Start("chmod", $"+x \"{watchScriptPath}\"");
                }
                
                Debug.Log($"[Synaptic] Watch config creation completed: {watchConfigPath}");

                EditorUtility.DisplayDialog(
                    "Watch Config Created",
                    $"File monitoring configuration created:\n\n" +
                    $"Config file: .synaptic-watch\n" +
                    $"Script: watch-synaptic.sh\n\n" +
                    "Monitored:\n" +
                    "• C#, JavaScript, TypeScript\n" +
                    "• JSON, Markdown, YAML\n\n" +
                    "Usage: ./watch-synaptic.sh",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Watch config error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create Watch config:\n{e.Message}", "OK");
            }
        }
        
        // CLI detection and config path detection methods
        private string CheckCLIInstallation(string cliName)
        {
            try
            {
                // Try normal which command
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = cliName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return output;
                    }
                }
                
                // CLI-specific path detection
                return CheckCLISpecificPaths(cliName);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Synaptic] CLI check error ({cliName}): {e.Message}");
            }
            
            return null;
        }
        
        private string CheckCLISpecificPaths(string cliName)
        {
            switch (cliName)
            {
                case "claude-code":
                    return CheckClaudeCodePaths();
                    
                    
                case "gemini":
                    return CheckGeminiCLIPaths();
                    
                case "codex":
                    return CheckCodexCLIPaths();
                    
                default:
                    return null;
            }
        }
        
        private string CheckClaudeCodePaths()
        {
            var paths = new[]
            {
                // Homebrew (Apple Silicon)
                "/opt/homebrew/Caskroom/claude-code/*/claude",
                "/opt/homebrew/bin/claude-code",
                
                // Homebrew (Intel)
                "/usr/local/Caskroom/claude-code/*/claude",
                "/usr/local/bin/claude-code",
                
                // NPM global
                "/usr/local/lib/node_modules/@anthropic/claude-code/bin/claude-code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/bin/claude-code",
                
                // Standalone installers
                "/Applications/Claude Code.app/Contents/MacOS/Claude Code",
                "/Applications/Claude.app/Contents/MacOS/Claude",
                
                // Direct download installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Applications/Claude Code.app/Contents/MacOS/Claude Code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads/claude-code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/bin/claude-code",
                
                // pnpm/yarn global
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/share/pnpm/claude-code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.yarn/bin/claude-code"
            };
            
            return FindExecutableInPaths(paths, "claude");
        }
        
        
        private string CheckGeminiCLIPaths()
        {
            var paths = new[]
            {
                // Google Cloud SDK
                "/usr/local/bin/gcloud",
                "/opt/homebrew/bin/gcloud",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/google-cloud-sdk/bin/gcloud",
                
                // Gemini specific CLI
                "/usr/local/bin/gemini",
                "/opt/homebrew/bin/gemini",
                
                // NPM installs
                "/usr/local/lib/node_modules/@google/gemini-cli/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/bin/gemini",
                
                // pip installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/bin/google-generativeai",
                
                // conda
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/anaconda3/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/miniconda3/bin/gemini",
                
                // Direct installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads/gemini",
                
                // Snap (if on Linux)
                "/snap/bin/gemini",
                
                // pnpm/yarn
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/share/pnpm/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.yarn/bin/gemini"
            };
            
            return FindExecutableInPaths(paths, "gemini");
        }
        
        private string CheckCodexCLIPaths()
        {
            var paths = new[]
            {
                // NPM global installs
                "/usr/local/bin/codex",
                "/opt/homebrew/bin/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/bin/codex",
                
                // OpenAI official CLI
                "/usr/local/lib/node_modules/@openai/codex/bin/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/lib/node_modules/@openai/codex/bin/codex",
                
                // pnpm/yarn global
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/share/pnpm/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.yarn/bin/codex",
                
                // Direct installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/bin/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads/codex",
                
                // Homebrew
                "/opt/homebrew/bin/openai-codex",
                "/usr/local/bin/openai-codex",
                
                // Rust cargo install
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.cargo/bin/codex",
                
                // Alternative names
                "/usr/local/bin/openai-codex-cli",
                "/opt/homebrew/bin/openai-codex-cli"
            };
            
            return FindExecutableInPaths(paths, "codex");
        }
        
        private string FindExecutableInPaths(string[] paths, string fallbackName)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (path.Contains("*"))
                    {
                        // Path with wildcards
                        var baseDir = Path.GetDirectoryName(path);
                        var fileName = Path.GetFileName(path);
                        
                        if (Directory.Exists(baseDir))
                        {
                            var subdirs = Directory.GetDirectories(baseDir);
                            foreach (var subdir in subdirs)
                            {
                                var fullPath = Path.Combine(subdir, fileName);
                                if (File.Exists(fullPath))
                                {
                                    return fullPath;
                                }
                            }
                        }
                    }
                    else if (File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Synaptic] Path check error ({path}): {e.Message}");
                }
            }
            
            // Detect from running processes
            return FindInRunningProcesses(fallbackName);
        }
        
        private string FindInRunningProcesses(string processName)
        {
            try
            {
                var psStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = "aux",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using (var psProcess = System.Diagnostics.Process.Start(psStartInfo))
                {
                    var psOutput = psProcess.StandardOutput.ReadToEnd();
                    psProcess.WaitForExit();
                    
                    var lines = psOutput.Split('\n');
                    foreach (var line in lines)
                    {
                        // More strict process name matching
                        if (IsProcessLineMatch(line, processName))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 10)
                            {
                                var executablePath = parts[10];
                                if (File.Exists(executablePath) && IsCorrectExecutable(executablePath, processName))
                                {
                                    return executablePath;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Synaptic] Process detection error ({processName}): {e.Message}");
            }
            
            return null;
        }
        
        private bool IsProcessLineMatch(string line, string processName)
        {
            switch (processName.ToLower())
            {
                case "claude":
                    // For Claude Code: claude-code or claude, but exclude gemini etc
                    return (line.Contains("claude-code") || line.Contains("/claude")) && 
                           !line.Contains("gemini") && !line.Contains("codex");
                           
                    
                case "gemini":
                    // For Gemini
                    return line.Contains("gemini") && !line.Contains("claude") && !line.Contains("codex");
                    
                case "codex":
                    // For Codex
                    return line.Contains("codex") && !line.Contains("claude") && !line.Contains("gemini");
                    
                default:
                    return line.Contains(processName);
            }
        }
        
        private bool IsCorrectExecutable(string executablePath, string processName)
        {
            var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLower();
            var fullPath = executablePath.ToLower();
            
            switch (processName.ToLower())
            {
                case "claude":
                    // For Claude Code - distinguish from Claude Desktop
                    if (fullPath.Contains("claude-code") || fullPath.Contains("caskroom/claude-code"))
                    {
                        return true; // Clearly Claude Code path
                    }
                    
                    // Exclude Claude Desktop
                    if (fullPath.Contains("/applications/claude.app") || fullPath.Contains("claude.app/contents"))
                    {
                        return false; // Exclude because it's Claude Desktop
                    }
                    
                    return (fileName == "claude-code") && !fullPath.Contains("gemini") && !fullPath.Contains("codex");
                           
                           
                case "gemini":
                    return (fileName.Contains("gemini") || fileName == "gemini" || fileName == "gcloud") && 
                           (fullPath.Contains("gemini") || fullPath.Contains("google")) && 
                           !fullPath.Contains("claude") && !fullPath.Contains("codex");
                           
                case "codex":
                    return (fileName.Contains("codex") || fileName == "codex" || fileName == "openai-codex") && 
                           fullPath.Contains("codex") && 
                           !fullPath.Contains("claude") && !fullPath.Contains("chatgpt") && !fullPath.Contains("gemini");
                           
                default:
                    return fileName == processName.ToLower() || fileName.Contains(processName.ToLower());
            }
        }
        
        
        private string DetectGeminiCLIConfigPath()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "gemini", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".google", "gemini", "config.json")
                };
                
                foreach (var path in candidates)
                {
                    var dir = Path.GetDirectoryName(path);
                    if (Directory.Exists(dir) || path.Contains("gemini"))
                    {
                        return path;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Synaptic] Gemini CLI config path detection error: {e.Message}");
            }
            
            return null;
        }
        
        private string DetectCodexCLIConfigPath()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openai", "codex.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "codex", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openai", "codex.json")
                };
                
                foreach (var path in candidates)
                {
                    var dir = Path.GetDirectoryName(path);
                    if (Directory.Exists(dir) || path.Contains("openai") || path.Contains("codex"))
                    {
                        return path;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Synaptic] OpenAI Codex CLI config path detection error: {e.Message}");
            }
            
            return null;
        }
        
        private bool GenerateClaudeCodeSpecificConfig()
        {
            try
            {
                var projectPath = Application.dataPath.Replace("/Assets", "");
                var claudeDir = Path.Combine(projectPath, ".claude");
                var claudeConfigPath = Path.Combine(claudeDir, "settings.local.json");
                
                // Load existing settings
                dynamic existingConfig = null;
                if (File.Exists(claudeConfigPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(claudeConfigPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Synaptic] Failed to load existing Claude Code config: {e.Message}");
                    }
                }
                
                // Unity MCP server settings (2025 specification)
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    command = "node",
                    args = new[] { NormalizePathForJson(Path.Combine(FindMCPServerPath(), "index.js")) },
                    env = new Dictionary<string, object>()
                };
                
                // Create .claude directory
                if (!Directory.Exists(claudeDir))
                {
                    Directory.CreateDirectory(claudeDir);
                }
                
                // Project-specific config structure (2025 Claude Code format)
                dynamic claudeCodeConfig;
                if (existingConfig?.mcpServers != null)
                {
                    // Get existing mcpServers
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity"] = unityMcpServer;
                    
                    // Update entire config
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    claudeCodeConfig = configDict;
                }
                else
                {
                    // Create new - 2025 MCP specification compliant
                    claudeCodeConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity"] = unityMcpServer
                        },
                        permissions = new
                        {
                            allow = new[] { "mcp__unity" },
                            deny = new object[] { }
                        }
                    };
                }
                
                File.WriteAllText(claudeConfigPath, JsonConvert.SerializeObject(claudeCodeConfig, Newtonsoft.Json.Formatting.Indented));
                
                Debug.Log($"[Synaptic] Claude Code config file created: {claudeConfigPath}");
                
                // Also create alternate config path (user global settings)
                var userConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude.json"
                );
                
                CreateUserLevelConfig(userConfigPath, unityMcpServer);
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Claude Code specific config error: {e.Message}");
                return false;
            }
        }
        
        private void CreateUserLevelConfig(string userConfigPath, object unityMcpServer)
        {
            try
            {
                dynamic userConfig = null;
                
                // Load existing user-level settings
                if (File.Exists(userConfigPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(userConfigPath);
                        userConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Synaptic] Failed to load existing user settings: {e.Message}");
                    }
                }
                
                // Update or create user-level settings
                if (userConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(userConfig.mcpServers));
                    mcpServers["unity"] = unityMcpServer;
                    
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(userConfig));
                    configDict["mcpServers"] = mcpServers;
                    userConfig = configDict;
                }
                else
                {
                    userConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity"] = unityMcpServer
                        }
                    };
                }
                
                File.WriteAllText(userConfigPath, JsonConvert.SerializeObject(userConfig, Newtonsoft.Json.Formatting.Indented));
                Debug.Log($"[Synaptic] User global settings also created: {userConfigPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Synaptic] User global settings creation skipped: {e.Message}");
            }
        }
        
        /// <summary>
        /// Normalize file path for JSON/Node.js consumption.
        /// Converts Windows backslashes to forward slashes.
        /// Node.js handles both, but forward slashes work cross-platform in JSON.
        /// </summary>
        private string NormalizePathForJson(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Convert backslashes to forward slashes for cross-platform JSON compatibility
            return path.Replace("\\", "/");
        }

        private string FindMCPServerPath()
        {
            // 1. Search in Assets
            string[] assetsSearchPaths = Directory.GetDirectories(Application.dataPath, "MCPServer", SearchOption.AllDirectories);
            if (assetsSearchPaths.Length > 0)
            {
                Debug.Log($"[Synaptic] Found MCPServer in Assets: {assetsSearchPaths[0]}");
                return assetsSearchPaths[0];
            }

            // 2. Search in Packages (if installed via Package Manager)
            // Fix: Use Path.DirectorySeparatorChar instead of hardcoded "/"
            string projectPath = Application.dataPath.Replace($"{Path.DirectorySeparatorChar}Assets", "");
            string packagesPath = Path.Combine(projectPath, "Packages");

            if (Directory.Exists(packagesPath))
            {
                string[] packageSearchPaths = Directory.GetDirectories(packagesPath, "MCPServer", SearchOption.AllDirectories);
                if (packageSearchPaths.Length > 0)
                {
                    Debug.Log($"[Synaptic] Found MCPServer in Packages: {packageSearchPaths[0]}");
                    return packageSearchPaths[0];
                }
            }
            
            // 3. Search in Library/PackageCache (UPM cache)
            string packageCachePath = Path.Combine(projectPath, "Library", "PackageCache");
            if (Directory.Exists(packageCachePath))
            {
                string[] cacheSearchPaths = Directory.GetDirectories(packageCachePath, "MCPServer", SearchOption.AllDirectories);
                if (cacheSearchPaths.Length > 0)
                {
                    Debug.Log($"[Synaptic] Found MCPServer in PackageCache: {cacheSearchPaths[0]}");
                    return cacheSearchPaths[0];
                }
            }
            
            // 4. Return default path if not found
            string defaultPath = Path.Combine(Application.dataPath, "com.synaptic.mcp-unity", "MCPServer");
            Debug.LogWarning($"[Synaptic] MCPServer not found, using default path: {defaultPath}");
            return defaultPath;
        }
        
        private bool CheckAndInstallDependencies()
        {
            try
            {
                Debug.Log("[Synaptic] Checking dependencies...");
                
                // Check if Newtonsoft.Json is installed
                var listRequest = Client.List();
                while (!listRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }
                
                if (listRequest.Status == StatusCode.Success)
                {
                    bool hasNewtonsoft = false;
                    foreach (var package in listRequest.Result)
                    {
                        if (package.name == "com.unity.nuget.newtonsoft-json")
                        {
                            hasNewtonsoft = true;
                            Debug.Log($"[Synaptic] Newtonsoft.Json already installed: v{package.version}");
                            break;
                        }
                    }
                    
                    if (!hasNewtonsoft)
                    {
                        Debug.Log("[Synaptic] Installing Newtonsoft.Json...");
                        
                        // Install Newtonsoft.Json
                        var addRequest = Client.Add("com.unity.nuget.newtonsoft-json");
                        
                        while (!addRequest.IsCompleted)
                        {
                            System.Threading.Thread.Sleep(10);
                        }
                        
                        if (addRequest.Status == StatusCode.Success)
                        {
                            Debug.Log("[Synaptic] Newtonsoft.Json installed successfully");
                            
                            // Wait for compilation
                            EditorUtility.DisplayDialog(
                                "Dependencies Installed",
                                "Newtonsoft.Json has been installed. Unity will now recompile.\n\nPlease run setup again after compilation completes.",
                                "OK"
                            );
                            
                            return false; // Return false to stop setup and wait for recompilation
                        }
                        else if (addRequest.Status == StatusCode.Failure)
                        {
                            Debug.LogError($"[Synaptic] Failed to install Newtonsoft.Json: {addRequest.Error.message}");
                            return false;
                        }
                    }
                }
                
                // Check TextMeshPro
                bool hasTMP = false;
                if (listRequest.Status == StatusCode.Success)
                {
                    foreach (var package in listRequest.Result)
                    {
                        if (package.name == "com.unity.textmeshpro")
                        {
                            hasTMP = true;
                            Debug.Log($"[Synaptic] TextMeshPro already installed: v{package.version}");
                            break;
                        }
                    }
                }
                
                if (!hasTMP)
                {
                    Debug.LogWarning("[Synaptic] TextMeshPro not found. Some features may not work properly.");
                }
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Dependency check error: {e.Message}");
                return true; // Continue anyway
            }
        }
        
    }
}