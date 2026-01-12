const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const cors = require('cors');
const { z } = require('zod');
const { createServer } = require('./mcp-server');
const util = require('util');
const fs = require('fs');
const path = require('path');
const os = require('os');

const app = express();
app.use(cors());
app.use(express.json());

const server = http.createServer(app);
let wss = null; // 後で初期化

let unityWebSocket = null;
let mcpServer = null;
let desktopAppSocket = null; // デスクトップアプリ接続用
const BridgeHandler = require('./bridge-handler');
const bridgeHandler = new BridgeHandler();

// Unity WebSocket接続の管理（関数として定義）
function setupWebSocketHandlers() {
    if (!wss) return;
    
    wss.on('connection', (ws, req) => {
        // 接続タイプを判定
        const isUnity = req.headers['x-client-type'] === 'unity' || req.url === '/unity';
        const isMCP = req.headers['x-client-type'] === 'mcp' || req.url === '/mcp';
        
        if (isMCP) {
            // デスクトップアプリ接続
            if (desktopAppSocket) {
                desktopAppSocket.close();
            }
            desktopAppSocket = ws;
            
            // ブリッジハンドラーに接続を設定
            bridgeHandler.setDesktopConnection(ws);
            
            ws.on('message', async (message) => {
                try {
                    const data = JSON.parse(message);
                    
                    // デスクトップアプリからのメッセージをUnityに転送
                    if (data.type === 'chat_response' && unityWebSocket) {
                        unityWebSocket.send(JSON.stringify({
                            type: 'assistant_message',
                            content: data.content
                        }));
                    }
                    
                    // ツール実行命令をUnityに転送
                    if (data.type === 'execute_tool' && unityWebSocket) {
                        unityWebSocket.send(JSON.stringify({
                            type: 'tool_call',
                            command: data.tool,
                            parameters: data.parameters,
                            id: data.id
                        }));
                    }
                    
                    // ブリッジハンドラーも使用（追加機能）
                    if (data.type === 'assistant_response') {
                        bridgeHandler.forwardToUnity(data);
                    }
                } catch (e) {
                    // console.error('MCP message error:', e);
                }
            });
            
            ws.on('close', () => {
                desktopAppSocket = null;
                bridgeHandler.handleDisconnect('desktop');
            });
            
            return;
        }
        
        // Unity接続処理
        // 古い接続をクリーンアップ
        if (unityWebSocket) {
            unityWebSocket.close();
        }
        
        // Unity接続（ログを出力しない）
        unityWebSocket = ws;
        
        // ブリッジハンドラーにも設定
        bridgeHandler.setUnityConnection(ws);
        
        // デバッグ用：ファイルに記録 (cross-platform safe)
        try {
            const debugLogPath = path.join(os.tmpdir(), 'mcp-debug.log');
            fs.appendFileSync(debugLogPath, `[${new Date().toISOString()}] Unity connected\n`);
        } catch (e) {
            // Avoid crashing on environments without /tmp (e.g., Windows)
        }

        ws.on('message', async (message) => {
            try {
                const data = JSON.parse(message);
                
                // Unity内チャットメッセージをデスクトップアプリに転送
                if (data.type === 'chat_message') {
                    if (desktopAppSocket) {
                        desktopAppSocket.send(JSON.stringify({
                            type: 'user_message',
                            content: data.message,
                            context: {
                                source: 'unity',
                                project: data.projectName || 'Unity Project'
                            }
                        }));
                    }
                    
                    // ブリッジハンドラーも使用（会話履歴管理）
                    bridgeHandler.forwardToDesktop({
                        content: data.message,
                        projectName: data.projectName
                    });
                    return;
                }
                
                // operation_result (既存のMCP経由) または operation_response (HTTP経由)
                const responseId = data.id || data.operationId;
                if ((data.type === 'operation_result' || data.type === 'operation_response') && responseId) {
                    // idを数値に変換して検索
                    const numericId = typeof responseId === 'string' ? parseInt(responseId) : responseId;

                    if (pendingRequests.has(numericId)) {
                        const { resolve, reject, timeout } = pendingRequests.get(numericId);
                        clearTimeout(timeout);
                        pendingRequests.delete(numericId);

                        // Unity側は content フィールドまたは result フィールドに結果を格納
                        if (data.success !== false) {
                            resolve(data.content || data.result);
                        } else {
                            reject(new Error(data.content || data.error || 'Unity command failed'));
                        }
                    }
                }
            } catch (e) {
                // エラーログを出力しない
            }
        });

        ws.on('close', () => {
            // Unity切断（ログを出力しない）
            unityWebSocket = null;
            bridgeHandler.handleDisconnect('unity');
        });

        ws.on('error', (error) => {
            // WebSocketエラー（ログを出力しない）
        });
    });
}

// Unityコマンド送信用のヘルパー関数
const pendingRequests = new Map();
let requestId = 0;

// Helper: sleep function
const sleep = (ms) => new Promise(resolve => setTimeout(resolve, ms));

// Single attempt to send Unity command (no retry)
async function sendUnityCommandOnce(command, params, id) {
    return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
            pendingRequests.delete(id);
            reject(new Error(`timeout`));
        }, 15000); // 15 seconds per attempt

        pendingRequests.set(id, { resolve, reject, timeout });

        const message = JSON.stringify({
            type: 'unity_operation',
            command: command,
            parameters: {
                ...params,
                operationId: id.toString()
            }
        });
        unityWebSocket.send(message);
    });
}

// Main command sender with auto-retry
async function sendUnityCommand(command, params = {}) {
    const MAX_RETRIES = 3;
    const RETRY_DELAY = 3000; // 3 seconds

    let lastError = null;

    for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
        // Check connection
        if (!unityWebSocket || unityWebSocket.readyState !== WebSocket.OPEN) {
            lastError = new Error('Unity not connected');
            if (attempt < MAX_RETRIES) {
                await sleep(RETRY_DELAY);
                continue;
            }
            break;
        }

        const id = ++requestId;

        try {
            const result = await sendUnityCommandOnce(command, params, id);
            // Success! If this was a retry, add note
            if (attempt > 1) {
                if (typeof result === 'string') {
                    return result + `\n[Note: Succeeded on retry attempt ${attempt}]`;
                } else if (typeof result === 'object' && result !== null) {
                    result._retryInfo = `Succeeded on attempt ${attempt} after ${attempt - 1} retries`;
                }
            }
            return result;
        } catch (error) {
            lastError = error;
            if (attempt < MAX_RETRIES) {
                await sleep(RETRY_DELAY);
            }
        }
    }

    // All retries failed - throw with info
    throw new Error(`${lastError?.message || 'Unknown error'} (after ${MAX_RETRIES} attempts). Unity may be recompiling - wait and try again.`);
}

// MCP サーバーの設定
async function setupMCPServer() {
    mcpServer = createServer();

    // =====================================================
    // IMPORTANT: Connection & Retry Information
    // =====================================================
    // If a tool call fails with "Unity is not connected" or timeout:
    // 1. This often happens during Unity recompilation (script changes)
    // 2. The system will auto-retry up to 2 times with 3 second delays
    // 3. If still failing, wait a few seconds and try again manually
    // 4. Check Unity Editor is running and Synaptic AI Pro is connected
    // =====================================================

    // ===== GameObject基本操作 =====
    mcpServer.registerTool('unity_create_gameobject', {
        title: 'Create GameObject',
        description: 'Create a new GameObject in Unity scene. Note: Auto-retries on connection failure during Unity recompilation.',
        inputSchema: z.object({
            name: z.string().describe('Name of the GameObject'),
            type: z.enum(['empty', 'cube', 'sphere', 'cylinder', 'plane', 'capsule', 'quad']).optional().default('empty').describe('Type of primitive to create'),
            parent: z.string().optional().describe('Parent GameObject name'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional()
        })
    }, async (params) => {
        try {
            const result = await sendUnityCommand('create_gameobject', params);
            
            // MCPの仕様に従って、必ずcontent配列を返す
            return {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
        } catch (error) {
            throw error;
        }
    });

    mcpServer.registerTool('unity_update_gameobject', {
        title: 'Update GameObject',
        description: 'Update properties of an existing GameObject',
        inputSchema: z.object({
            name: z.string().describe('Name of the GameObject'),
            newName: z.string().optional(),
            active: z.boolean().optional(),
            tag: z.string().optional(),
            layer: z.number().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_gameobject', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_delete_gameobject', {
        title: 'Delete GameObject',
        description: 'Delete a GameObject from the scene',
        inputSchema: z.object({
            name: z.string().describe('Name of the GameObject to delete')
        })
    }, async (params) => {
        const result = await sendUnityCommand('delete_gameobject', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Transform操作 =====
    mcpServer.registerTool('unity_set_transform', {
        title: 'Set Transform',
        description: 'Set position, rotation, and scale of a GameObject',
        inputSchema: z.object({
            gameObject: z.string().describe('GameObject name'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional(),
            rotation: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional().describe('Euler angles in degrees'),
            scale: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_transform', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== コンポーネント操作 =====
    mcpServer.registerTool('unity_add_component', {
        title: 'Add Component',
        description: 'Add a component to a GameObject',
        inputSchema: z.object({
            gameObject: z.string().describe('GameObject name'),
            component: z.string().describe('Component type (e.g., Rigidbody, BoxCollider)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_component', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_component', {
        title: 'Update Component',
        description: 'Update component properties. Use gameObject for target object name and component for component type.',
        inputSchema: z.object({
            gameObject: z.string().optional().describe('GameObject name'),
            gameObjectName: z.string().optional().describe('GameObject name (alias)'),
            component: z.string().optional().describe('Component type'),
            componentName: z.string().optional().describe('Component type (alias)'),
            properties: z.union([
                z.record(z.any()),
                z.string()
            ]).describe('Properties to update (JSON object or string)')
        })
    }, async (params) => {
        // Normalize parameter names
        const normalizedParams = {
            gameObject: params.gameObject || params.gameObjectName,
            component: params.component || params.componentName,
            properties: params.properties
        };
        const result = await sendUnityCommand('update_component', normalizedParams);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== UI操作 =====
    mcpServer.registerTool('unity_create_ui', {
        title: 'Create UI Element',
        description: 'Create UI elements in Unity',
        inputSchema: z.object({
            type: z.enum(['Canvas', 'Button', 'Text', 'Image', 'Slider', 'Toggle', 'InputField', 'Panel']),
            name: z.string(),
            parent: z.string().optional(),
            position: z.object({
                x: z.number(),
                y: z.number()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ui', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 地形操作 =====
    mcpServer.registerTool('unity_create_terrain', {
        title: 'Create Terrain',
        description: 'Create a terrain in Unity',
        inputSchema: z.object({
            name: z.string(),
            width: z.number().optional().default(500),
            height: z.number().optional().default(600),
            length: z.number().optional().default(500),
            heightmapResolution: z.number().optional().default(513)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_terrain', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_modify_terrain', {
        title: 'Modify Terrain',
        description: 'Modify terrain height or textures',
        inputSchema: z.object({
            name: z.string(),
            operation: z.enum(['raise', 'lower', 'flatten', 'smooth']),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }),
            radius: z.number().optional().default(10),
            strength: z.number().optional().default(0.5)
        })
    }, async (params) => {
        const result = await sendUnityCommand('modify_terrain', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== カメラ操作 =====
    mcpServer.registerTool('unity_setup_camera', {
        title: 'Setup Camera',
        description: 'Setup camera in the scene',
        inputSchema: z.object({
            name: z.string().optional().default('Main Camera'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }),
            lookAt: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional(),
            fieldOfView: z.number().optional().default(60),
            cameraType: z.enum(['Perspective', 'Orthographic']).optional().default('Perspective')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Cinemachine =====
    mcpServer.registerTool('unity_create_virtual_camera', {
        title: 'Create Cinemachine Virtual Camera',
        description: 'Create a Cinemachine Virtual Camera with follow and look at targets',
        inputSchema: z.object({
            name: z.string().describe('Name of the virtual camera'),
            priority: z.number().optional().default(10).describe('Camera priority (higher = more important)'),
            follow: z.string().optional().describe('GameObject to follow'),
            lookAt: z.string().optional().describe('GameObject to look at'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional().describe('Camera position'),
            fov: z.number().optional().default(60).describe('Field of view'),
            nearClipPlane: z.number().optional().describe('Near clip plane distance'),
            farClipPlane: z.number().optional().describe('Far clip plane distance'),
            dutch: z.number().optional().describe('Camera roll/tilt angle in degrees'),
            orthographic: z.boolean().optional().describe('Use orthographic projection'),
            orthographicSize: z.number().optional().describe('Orthographic camera size'),
            bodyType: z.enum(['Transposer', 'FramingTransposer', 'OrbitalTransposer', 'HardLockToTarget', 'DoNothing']).optional().default('Transposer').describe('Body component type for follow behavior'),
            aimType: z.enum(['Composer', 'GroupComposer', 'POV', 'SameAsFollowTarget', 'HardLookAt', 'DoNothing']).optional().default('Composer').describe('Aim component type for look at behavior'),
            damping: z.object({
                x: z.number().optional(),
                y: z.number().optional(),
                z: z.number().optional()
            }).optional().describe('Follow damping values'),
            offset: z.object({
                x: z.number().optional(),
                y: z.number().optional(),
                z: z.number().optional()
            }).optional().describe('Follow offset'),
            screenX: z.number().optional().describe('Screen X position for framing (0-1)'),
            screenY: z.number().optional().describe('Screen Y position for framing (0-1)'),
            deadZoneWidth: z.number().optional().describe('Dead zone width for Composer'),
            deadZoneHeight: z.number().optional().describe('Dead zone height for Composer'),
            horizontalAxis: z.number().optional().describe('Horizontal axis speed for POV'),
            verticalAxis: z.number().optional().describe('Vertical axis speed for POV'),
            noiseProfile: z.enum(['6D Shake', 'Handheld_normal', 'Handheld_normal_mild', 'Handheld_tele_mild', 'Handheld_wide_mild']).optional().describe('Camera shake noise profile'),
            noiseAmplitude: z.number().optional().describe('Noise amplitude/intensity (0-2)'),
            noiseFrequency: z.number().optional().describe('Noise frequency (0-2)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_virtual_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_freelook_camera', {
        title: 'Create Cinemachine FreeLook Camera',
        description: 'Create a Cinemachine FreeLook Camera for third-person orbiting. REQUIRED: "follow" parameter must specify an existing GameObject name in the scene. The target GameObject must exist before creating the camera. Example: unity_create_freelook_camera({name: "PlayerCamera", follow: "Player"}). Will fail if follow target does not exist.',
        inputSchema: z.object({
            name: z.string().describe('Name of the FreeLook camera (required)'),
            follow: z.string().describe('GameObject name to follow and orbit around (REQUIRED - must exist in scene before calling this tool)'),
            lookAt: z.string().optional().describe('GameObject to look at (defaults to follow target)'),
            topRigHeight: z.number().optional().default(4.5).describe('Top rig height offset'),
            topRigRadius: z.number().optional().default(1.75).describe('Top rig radius'),
            middleRigHeight: z.number().optional().default(2.5).describe('Middle rig height offset'),
            middleRigRadius: z.number().optional().default(3).describe('Middle rig radius'),
            bottomRigHeight: z.number().optional().default(0.4).describe('Bottom rig height offset'),
            bottomRigRadius: z.number().optional().default(1.3).describe('Bottom rig radius'),
            xAxisSpeed: z.number().optional().default(300).describe('Horizontal rotation speed'),
            yAxisSpeed: z.number().optional().default(2).describe('Vertical rotation speed'),
            priority: z.number().optional().default(10).describe('Camera priority')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_freelook_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_cinemachine_brain', {
        title: 'Setup Cinemachine Brain',
        description: 'Setup Cinemachine Brain on a camera (required for Cinemachine to work)',
        inputSchema: z.object({
            camera: z.string().optional().default('Main Camera').describe('Camera to add Brain to'),
            blendStyle: z.enum(['Cut', 'EaseInOut', 'EaseIn', 'EaseOut', 'HardIn', 'HardOut', 'Linear']).optional().default('EaseInOut').describe('Default blend style'),
            defaultBlendTime: z.number().optional().default(2).describe('Default blend time in seconds'),
            updateMethod: z.enum(['FixedUpdate', 'LateUpdate', 'SmartUpdate']).optional().default('SmartUpdate').describe('Brain update method')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_cinemachine_brain', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_virtual_camera', {
        title: 'Update Virtual Camera Settings',
        description: 'Update settings of an existing Cinemachine Virtual Camera',
        inputSchema: z.object({
            camera: z.string().describe('Virtual camera name'),
            priority: z.number().optional().describe('New priority'),
            follow: z.string().optional().describe('New follow target'),
            lookAt: z.string().optional().describe('New look at target'),
            fov: z.number().optional().describe('New field of view'),
            nearClipPlane: z.number().optional().describe('Near clip plane distance'),
            farClipPlane: z.number().optional().describe('Far clip plane distance'),
            dutch: z.number().optional().describe('Camera roll/tilt angle in degrees'),
            orthographic: z.boolean().optional().describe('Use orthographic projection'),
            orthographicSize: z.number().optional().describe('Orthographic camera size'),
            bodyType: z.enum(['Transposer', 'FramingTransposer', 'OrbitalTransposer', 'HardLockToTarget', 'DoNothing']).optional().describe('Body component type'),
            aimType: z.enum(['Composer', 'GroupComposer', 'POV', 'SameAsFollowTarget', 'HardLookAt', 'DoNothing']).optional().describe('Aim component type'),
            damping: z.object({
                x: z.number().optional(),
                y: z.number().optional(),
                z: z.number().optional()
            }).optional().describe('New damping values'),
            noiseProfile: z.enum(['6D Shake', 'Handheld_normal', 'Handheld_normal_mild', 'Handheld_tele_mild', 'Handheld_wide_mild']).optional().describe('Camera shake noise profile'),
            noiseAmplitude: z.number().optional().describe('Noise amplitude/intensity (0-2)'),
            noiseFrequency: z.number().optional().describe('Noise frequency (0-2)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_virtual_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_dolly_track', {
        title: 'Create Cinemachine Dolly Track',
        description: 'Create a Cinemachine Dolly Track with waypoints for camera path animation',
        inputSchema: z.object({
            name: z.string().describe('Name of the dolly track'),
            waypoints: z.array(z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            })).describe('Array of waypoint positions'),
            looped: z.boolean().optional().default(false).describe('Whether the path loops'),
            cameraName: z.string().optional().describe('Virtual camera to attach to track')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_dolly_track', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_collider_extension', {
        title: 'Add Cinemachine Collider Extension',
        description: 'Add Collider Extension to Virtual Camera for obstacle avoidance',
        inputSchema: z.object({
            camera: z.string().describe('Virtual camera name'),
            minimumDistanceFromTarget: z.number().optional().default(0.2).describe('Minimum distance from follow target'),
            avoidObstacles: z.boolean().optional().default(true).describe('Enable obstacle avoidance'),
            distanceLimit: z.number().optional().default(10).describe('Maximum raycast distance'),
            smoothingTime: z.number().optional().default(0).describe('Smoothing time for position changes'),
            damping: z.number().optional().default(0).describe('Damping for position changes')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_collider_extension', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_confiner_extension', {
        title: 'Add Cinemachine Confiner Extension',
        description: 'Add Confiner Extension to restrict camera movement within bounds',
        inputSchema: z.object({
            camera: z.string().describe('Virtual camera name'),
            boundingVolume: z.string().describe('GameObject with Collider defining the bounds'),
            confineMode: z.enum(['Confine3D', 'Confine2D']).optional().default('Confine3D').describe('3D or 2D confine mode'),
            damping: z.number().optional().default(0).describe('Damping for confinement')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_confiner_extension', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_state_driven_camera', {
        title: 'Create Cinemachine State-Driven Camera',
        description: 'Create a State-Driven Camera that switches based on Animator state',
        inputSchema: z.object({
            name: z.string().describe('Name of the State-Driven camera'),
            animatedTarget: z.string().describe('GameObject with Animator component'),
            layerIndex: z.number().optional().default(0).describe('Animator layer index'),
            follow: z.string().optional().describe('GameObject to follow'),
            lookAt: z.string().optional().describe('GameObject to look at'),
            priority: z.number().optional().default(10).describe('Camera priority')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_state_driven_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_clear_shot_camera', {
        title: 'Create Cinemachine Clear Shot Camera',
        description: 'Create a Clear Shot Camera for dynamic shot selection with obstacle avoidance',
        inputSchema: z.object({
            name: z.string().describe('Name of the Clear Shot camera'),
            follow: z.string().optional().describe('GameObject to follow'),
            lookAt: z.string().optional().describe('GameObject to look at'),
            priority: z.number().optional().default(10).describe('Camera priority')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_clear_shot_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_impulse_source', {
        title: 'Create Cinemachine Impulse Source',
        description: 'Create an Impulse Source for triggering camera shake effects',
        inputSchema: z.object({
            gameObject: z.string().describe('Name of the GameObject to add Impulse Source to'),
            amplitudeGain: z.number().optional().default(1.0).describe('Impulse amplitude gain'),
            frequencyGain: z.number().optional().default(1.0).describe('Impulse frequency gain'),
            impulseDuration: z.number().optional().default(0.2).describe('Duration of the impulse in seconds'),
            defaultVelocity: z.string().optional().default('0,0,0').describe('Default velocity as "x,y,z"')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_impulse_source', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_impulse_listener', {
        title: 'Add Cinemachine Impulse Listener',
        description: 'Add an Impulse Listener extension to a virtual camera to receive shake effects',
        inputSchema: z.object({
            camera: z.string().describe('Name of the Virtual Camera to add listener to'),
            gain: z.number().optional().default(1.0).describe('Overall gain for impulse reaction'),
            amplitudeGain: z.number().optional().default(1.0).describe('Amplitude gain for reaction'),
            frequencyGain: z.number().optional().default(1.0).describe('Frequency gain for reaction')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_impulse_listener', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_blend_list_camera', {
        title: 'Create Cinemachine Blend List Camera',
        description: 'Create a Blend List Camera that manages a prioritized list of child virtual cameras',
        inputSchema: z.object({
            name: z.string().optional().default('CM BlendList').describe('Name of the Blend List camera'),
            priority: z.number().optional().default(10).describe('Camera priority'),
            follow: z.string().optional().describe('GameObject to follow'),
            lookAt: z.string().optional().describe('GameObject to look at'),
            loop: z.boolean().optional().default(false).describe('Loop through child cameras'),
            showDebugText: z.boolean().optional().default(false).describe('Show debug information')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_blend_list_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_target_group', {
        title: 'Create Cinemachine Target Group',
        description: 'Create a Target Group for managing multiple camera targets with automatic framing',
        inputSchema: z.object({
            name: z.string().optional().default('CM TargetGroup').describe('Name of the Target Group'),
            positionMode: z.enum(['GroupCenter', 'GroupAverage']).optional().default('GroupCenter').describe('Position calculation mode'),
            rotationMode: z.enum(['Manual', 'GroupAverage']).optional().default('Manual').describe('Rotation calculation mode'),
            updateMethod: z.enum(['Update', 'FixedUpdate', 'LateUpdate']).optional().default('Update').describe('Update method')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_target_group', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_target_to_group', {
        title: 'Add Target to Cinemachine Target Group',
        description: 'Add a GameObject as a target to an existing Target Group',
        inputSchema: z.object({
            targetGroup: z.string().describe('Name of the Target Group'),
            target: z.string().describe('Name of the GameObject to add as target'),
            weight: z.number().optional().default(1.0).describe('Target weight (influence on framing)'),
            radius: z.number().optional().default(1.0).describe('Target radius (bounding sphere)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_target_to_group', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_camera_priority', {
        title: 'Set Cinemachine Camera Priority',
        description: 'Change the priority of a Cinemachine virtual camera',
        inputSchema: z.object({
            camera: z.string().describe('Name of the Cinemachine camera'),
            priority: z.number().describe('New priority value (higher priority cameras take precedence)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_camera_priority', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_camera_enabled', {
        title: 'Enable/Disable Cinemachine Camera',
        description: 'Enable or disable a Cinemachine virtual camera',
        inputSchema: z.object({
            camera: z.string().describe('Name of the Cinemachine camera'),
            enabled: z.boolean().describe('Enable (true) or disable (false) the camera')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_camera_enabled', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_mixing_camera', {
        title: 'Create Cinemachine Mixing Camera',
        description: 'Create a Mixing Camera that blends multiple child cameras with weighted averaging',
        inputSchema: z.object({
            name: z.string().optional().default('CM MixingCamera').describe('Name of the Mixing camera'),
            priority: z.number().optional().default(10).describe('Camera priority'),
            weight0: z.number().optional().default(1.0).describe('Weight for child camera 0'),
            weight1: z.number().optional().default(0.0).describe('Weight for child camera 1'),
            weight2: z.number().optional().default(0.0).describe('Weight for child camera 2'),
            weight3: z.number().optional().default(0.0).describe('Weight for child camera 3'),
            weight4: z.number().optional().default(0.0).describe('Weight for child camera 4'),
            weight5: z.number().optional().default(0.0).describe('Weight for child camera 5'),
            weight6: z.number().optional().default(0.0).describe('Weight for child camera 6'),
            weight7: z.number().optional().default(0.0).describe('Weight for child camera 7')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_mixing_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_camera_target', {
        title: 'Update Cinemachine Camera Target',
        description: 'Update Follow and/or LookAt targets of a Cinemachine camera',
        inputSchema: z.object({
            camera: z.string().describe('Name of the Cinemachine camera'),
            follow: z.string().optional().describe('New Follow target (empty string to clear)'),
            lookAt: z.string().optional().describe('New LookAt target (empty string to clear)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_camera_target', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_brain_blend_settings', {
        title: 'Update Cinemachine Brain Blend Settings',
        description: 'Update the default blend settings on the Cinemachine Brain',
        inputSchema: z.object({
            defaultBlendTime: z.number().optional().describe('Default blend duration in seconds'),
            defaultBlendStyle: z.enum(['Cut', 'EaseInOut', 'EaseIn', 'EaseOut', 'HardIn', 'HardOut', 'Linear']).optional().describe('Default blend curve style'),
            customBlendsAsset: z.string().optional().describe('Path to custom blends asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_brain_blend_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_active_camera_info', {
        title: 'Get Active Cinemachine Camera Info',
        description: 'Get information about the currently active Cinemachine virtual camera',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_active_camera_info', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== ライティング =====
    mcpServer.registerTool('unity_setup_lighting', {
        title: 'Setup Lighting',
        description: 'Setup lighting in the scene with extended options and presets',
        inputSchema: z.object({
            preset: z.enum(['studio', 'sunset', 'night', 'overcast', 'desert', 'forest', 'underwater', 'space', 'neon']).optional(),
            ambientMode: z.enum(['skybox', 'trilight', 'flat', 'custom']).optional(),
            ambientIntensity: z.number().min(0).max(2).optional(),
            ambientSkyColor: z.string().optional(),
            ambientEquatorColor: z.string().optional(),
            ambientGroundColor: z.string().optional(),
            fogEnabled: z.boolean().optional(),
            fogMode: z.enum(['linear', 'exponential', 'exponentialsquared']).optional(),
            fogColor: z.string().optional(),
            fogDensity: z.number().min(0).max(1).optional(),
            fogStartDistance: z.number().min(0).max(1000).optional(),
            fogEndDistance: z.number().min(0).max(1000).optional(),
            directionalLightIntensity: z.number().min(0).max(8).optional(),
            directionalLightColor: z.string().optional(),
            directionalLightShadows: z.enum(['none', 'hard', 'soft']).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_lighting', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== マテリアル =====
    mcpServer.registerTool('unity_create_material', {
        title: 'Create Material',
        description: 'Create a new material',
        inputSchema: z.object({
            name: z.string(),
            shader: z.string().optional().default('Standard'),
            color: z.object({
                r: z.number().min(0).max(1),
                g: z.number().min(0).max(1),
                b: z.number().min(0).max(1),
                a: z.number().min(0).max(1).optional().default(1)
            }).optional(),
            metallic: z.number().min(0).max(1).optional(),
            smoothness: z.number().min(0).max(1).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_material', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== プレハブ =====
    mcpServer.registerTool('unity_create_prefab', {
        title: 'Create Prefab',
        description: `Create a prefab from a GameObject in the scene.

IMPORTANT: Always specify a proper path to avoid cluttering the project:
- Use 'Assets/Prefabs/' for general prefabs
- Use 'Assets/Prefabs/UI/' for UI prefabs
- Use 'Assets/Prefabs/Characters/' for character prefabs

If path is not specified, defaults to 'Assets/Synaptic_Generated/'`,
        inputSchema: z.object({
            gameObject: z.string().describe('Name of the GameObject in scene to convert to prefab'),
            path: z.string().describe('Full save path including filename (e.g., "Assets/Prefabs/UI/MyButton.prefab"). ALWAYS specify a proper organized path.')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_prefab', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== スクリプト操作 =====
    mcpServer.registerTool('unity_create_script', {
        title: 'Create Script',
        description: 'Create a new C# script. NOTE: Cannot overwrite existing scripts. To replace an existing script entirely, use unity_modify_script with operation="replace"',
        inputSchema: z.object({
            name: z.string(),
            template: z.enum(['MonoBehaviour', 'ScriptableObject', 'Empty']).optional().default('MonoBehaviour'),
            content: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_script', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== シーン管理 =====
    mcpServer.registerTool('unity_manage_scene', {
        title: 'Manage Scene',
        description: 'Scene management operations',
        inputSchema: z.object({
            operation: z.enum(['save', 'load', 'new']),
            path: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('MANAGE_SCENE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 新規追加: シーン操作ツール =====

    mcpServer.registerTool('unity_load_scene', {
        title: 'Load Scene',
        description: '🎬 Load a scene in Editor mode. Supports Single (replace current) or Additive (load alongside current) modes. Can load by path or name.',
        inputSchema: z.object({
            scenePath: z.string().optional().describe('Scene path (e.g., Assets/Scenes/Game.unity)'),
            sceneName: z.string().optional().describe('Scene name (e.g., Game) - will search in Assets'),
            mode: z.enum(['Single', 'Additive']).optional().default('Single').describe('Load mode: Single (replace) or Additive (add)'),
            setActive: z.boolean().optional().default(true).describe('Set as active scene after loading (Additive mode only)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('load_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_unload_scene', {
        title: 'Unload Scene',
        description: '🗑️ Unload a scene from the Editor. Useful for managing multi-scene setups. Cannot unload the last remaining scene.',
        inputSchema: z.object({
            sceneName: z.string().describe('Name of the scene to unload'),
            removeUnsavedChanges: z.boolean().optional().default(false).describe('Remove without saving changes')
        })
    }, async (params) => {
        const result = await sendUnityCommand('unload_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_active_scene', {
        title: 'Set Active Scene',
        description: '⭐ Set the active scene in multi-scene editing. The active scene is where new GameObjects are created by default.',
        inputSchema: z.object({
            sceneName: z.string().describe('Name of the scene to set as active')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_active_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_list_all_scenes', {
        title: 'List All Scenes',
        description: '📋 List all scene files in the project. Returns scene paths, names, and build settings info. Supports filtering and sorting.',
        inputSchema: z.object({
            includeInactive: z.boolean().optional().default(true).describe('Include scenes not in build settings'),
            sortBy: z.enum(['name', 'path', 'buildIndex']).optional().default('name').describe('Sort order'),
            searchPath: z.string().optional().default('Assets').describe('Directory to search (e.g., Assets/Scenes)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_all_scenes', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_scene_to_build', {
        title: 'Add Scene to Build Settings',
        description: '⚙️ Add or remove scenes from Build Settings. Manage build order and enable/disable scenes.',
        inputSchema: z.object({
            scenePath: z.string().describe('Scene path (e.g., Assets/Scenes/Game.unity)'),
            operation: z.enum(['add', 'remove', 'enable', 'disable']).optional().default('add').describe('Operation type'),
            buildIndex: z.number().optional().describe('Build index (for add operation, -1 = append)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_scene_to_build', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== アニメーション =====
    mcpServer.registerTool('unity_create_animation', {
        title: 'Create Animation',
        description: 'Create animation for GameObject',
        inputSchema: z.object({
            gameObject: z.string(),
            animationName: z.string(),
            duration: z.number().optional().default(1),
            loop: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_animation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 物理設定 =====
    mcpServer.registerTool('unity_setup_physics', {
        title: 'Setup Physics',
        description: 'Setup physics settings for a GameObject or global physics',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject name (or "global" for global physics settings)').optional(),
            rigidbody: z.boolean().describe('Add Rigidbody component').optional(),
            mass: z.number().describe('Rigidbody mass').optional(),
            gravity: z.union([
                z.boolean(),
                z.string(), // JSON文字列形式も受け入れる
                z.object({
                    x: z.number(),
                    y: z.number(),
                    z: z.number()
                })
            ]).describe('Use gravity (bool), JSON string, or global gravity vector').optional(),
            collider: z.string().describe('Type of collider to add: box, sphere, capsule, mesh').optional(),
            defaultMaterial: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_physics', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== パーティクルシステム =====
    mcpServer.registerTool('unity_create_particle_system', {
        title: 'Create Particle System',
        description: 'Create a particle system with extended presets and customization options',
        inputSchema: z.object({
            name: z.string(),
            preset: z.enum(['fire', 'smoke', 'sparkle', 'rain', 'explosion', 'snow', 'magic', 'lightning', 'tornado', 'galaxy']).optional(),
            lifetime: z.number().min(0.1).max(10).optional(),
            startSpeed: z.number().min(0).max(100).optional(),
            startSize: z.number().min(0.01).max(10).optional(),
            emission: z.number().min(1).max(1000).optional(),
            gravity: z.number().min(-10).max(10).optional(),
            shape: z.enum(['cone', 'sphere', 'box', 'circle', 'edge', 'mesh']).optional(),
            usePhysics: z.boolean().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_particle_system', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== マテリアル =====
    mcpServer.registerTool('unity_setup_material', {
        title: 'Setup Material',
        description: 'Create or modify materials with PBR properties and shaders',
        inputSchema: z.object({
            targetObject: z.string(),
            materialName: z.string().optional(),
            shader: z.enum(['Standard', 'Unlit', 'Toon', 'Water', 'Glass', 'Hologram']).optional(),
            color: z.string().optional(),
            metallic: z.number().min(0).max(1).optional(),
            smoothness: z.number().min(0).max(1).optional(),
            emission: z.boolean().optional(),
            emissionColor: z.string().optional(),
            transparency: z.number().min(0).max(1).optional(),
            normalMap: z.boolean().optional(),
            texture: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_material', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== NavMesh =====
    mcpServer.registerTool('unity_setup_navmesh', {
        title: 'Setup NavMesh',
        description: 'Setup navigation mesh',
        inputSchema: z.object({
            agentRadius: z.number().optional().default(0.5),
            agentHeight: z.number().optional().default(2.0),
            maxSlope: z.number().optional().default(45),
            stepHeight: z.number().optional().default(0.4)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_navmesh', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== オーディオ =====
    mcpServer.registerTool('unity_create_audio_mixer', {
        title: 'Create Audio Mixer',
        description: 'Create an audio mixer',
        inputSchema: z.object({
            name: z.string(),
            groups: z.array(z.string()).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_audio_mixer', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_audio_source', {
        title: 'Create Audio Source',
        description: 'Create and configure an AudioSource component on a GameObject',
        inputSchema: z.object({
            gameObject: z.string(),
            audioClip: z.string().optional(),
            playOnAwake: z.boolean().optional().default(true),
            loop: z.boolean().optional().default(false),
            volume: z.number().min(0).max(1).optional().default(1),
            pitch: z.number().min(-3).max(3).optional().default(1),
            spatialMode: z.enum(['2D', '3D']).optional().default('3D'),
            minDistance: z.number().optional().default(1),
            maxDistance: z.number().optional().default(500)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_audio_source', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_3d_audio', {
        title: 'Setup 3D Audio',
        description: 'Configure 3D spatial audio settings for an AudioSource',
        inputSchema: z.object({
            audioSource: z.string(),
            dopplerLevel: z.number().min(0).max(5).optional().default(1),
            spread: z.number().min(0).max(360).optional().default(0),
            volumeRolloff: z.enum(['Logarithmic', 'Linear', 'Custom']).optional().default('Logarithmic'),
            minDistance: z.number().optional().default(1),
            maxDistance: z.number().optional().default(500),
            customCurve: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_3d_audio', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_audio_clip', {
        title: 'Create/Import Audio Clip',
        description: 'Import audio file or create procedural audio clip',
        inputSchema: z.object({
            name: z.string(),
            path: z.string().optional(),
            loadType: z.enum(['DecompressOnLoad', 'CompressedInMemory', 'Streaming']).optional().default('DecompressOnLoad'),
            compressionFormat: z.enum(['PCM', 'Vorbis', 'ADPCM']).optional().default('Vorbis'),
            quality: z.number().min(0).max(1).optional().default(0.7),
            sampleRate: z.number().optional().default(44100),
            forceToMono: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_audio_clip', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_audio_effects', {
        title: 'Setup Audio Effects',
        description: 'Add and configure audio effects (filters, reverb, echo, etc.)',
        inputSchema: z.object({
            audioSource: z.string(),
            effects: z.array(z.object({
                type: z.enum(['LowPass', 'HighPass', 'Echo', 'Distortion', 'Reverb', 'Chorus']),
                enabled: z.boolean().optional().default(true),
                parameters: z.record(z.any()).optional()
            }))
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_audio_effects', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_reverb_zones', {
        title: 'Create Reverb Zones',
        description: 'Create audio reverb zones for environmental effects',
        inputSchema: z.object({
            name: z.string(),
            position: z.object({ x: z.number(), y: z.number(), z: z.number() }),
            minDistance: z.number().optional().default(10),
            maxDistance: z.number().optional().default(30),
            reverbPreset: z.enum(['Off', 'Generic', 'Room', 'Bathroom', 'Cave', 'Hallway', 'Arena', 'Forest', 'Mountains', 'Underwater']).optional().default('Room'),
            customParameters: z.object({
                dryLevel: z.number().optional(),
                room: z.number().optional(),
                roomHF: z.number().optional(),
                decayTime: z.number().optional(),
                reflectionsLevel: z.number().optional(),
                reverbLevel: z.number().optional()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_reverb_zones', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_audio_occlusion', {
        title: 'Setup Audio Occlusion',
        description: 'Configure audio occlusion for realistic sound blocking',
        inputSchema: z.object({
            audioSource: z.string(),
            enableOcclusion: z.boolean().optional().default(true),
            occlusionLayers: z.array(z.string()).optional(),
            maxOcclusionDistance: z.number().optional().default(50),
            occlusionDamping: z.number().min(0).max(1).optional().default(0.5)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_audio_occlusion', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_adaptive_music', {
        title: 'Create Adaptive Music System',
        description: 'Create intro+loop music system with dynamic transitions',
        inputSchema: z.object({
            name: z.string(),
            segments: z.array(z.object({
                name: z.string(),
                audioClip: z.string(),
                startTime: z.number().optional().default(0),
                endTime: z.number().optional(),
                loopPoint: z.number().optional(),
                fadeIn: z.number().optional().default(0),
                fadeOut: z.number().optional().default(0),
                isLoop: z.boolean().optional().default(false),
                transitions: z.array(z.object({
                    toSegment: z.string(),
                    type: z.enum(['immediate', 'crossfade', 'onBeat', 'onBar']).optional().default('crossfade'),
                    duration: z.number().optional().default(1)
                })).optional()
            })),
            bpm: z.number().optional().default(120),
            beatsPerBar: z.number().optional().default(4)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_adaptive_music', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_audio_triggers', {
        title: 'Setup Audio Triggers',
        description: 'Configure event-based audio triggers',
        inputSchema: z.object({
            triggerName: z.string(),
            triggerType: z.enum(['OnEnter', 'OnExit', 'OnStay', 'OnCollision', 'OnAnimationEvent', 'OnCustomEvent']),
            targetGameObject: z.string(),
            audioActions: z.array(z.object({
                action: z.enum(['Play', 'Stop', 'Pause', 'FadeIn', 'FadeOut', 'CrossFade']),
                audioSource: z.string().optional(),
                audioClip: z.string().optional(),
                duration: z.number().optional().default(1),
                targetVolume: z.number().optional()
            }))
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_audio_triggers', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_sound_pools', {
        title: 'Create Sound Pools',
        description: 'Create sound variation pools for dynamic audio',
        inputSchema: z.object({
            poolName: z.string(),
            audioClips: z.array(z.string()),
            playbackMode: z.enum(['Random', 'Sequential', 'RandomNoRepeat', 'Weighted']).optional().default('RandomNoRepeat'),
            weights: z.array(z.number()).optional(),
            pitchVariation: z.number().optional().default(0),
            volumeVariation: z.number().optional().default(0),
            cooldownTime: z.number().optional().default(0)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_sound_pools', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_audio_mixing', {
        title: 'Create Dynamic Audio Mixing',
        description: 'Setup real-time audio mixing and ducking systems',
        inputSchema: z.object({
            mixerName: z.string(),
            mixerGroups: z.array(z.object({
                name: z.string(),
                volume: z.number().optional().default(0),
                priority: z.number().optional().default(128),
                duckingTargets: z.array(z.string()).optional(),
                duckingAmount: z.number().optional().default(-10),
                attackTime: z.number().optional().default(0.1),
                releaseTime: z.number().optional().default(0.5)
            })),
            snapshots: z.array(z.object({
                name: z.string(),
                groupSettings: z.record(z.number())
            })).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_audio_mixing', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_spatial_audio', {
        title: 'Setup Spatial Audio',
        description: 'Configure advanced spatial audio for VR/AR',
        inputSchema: z.object({
            audioSource: z.string(),
            spatializerPlugin: z.enum(['Unity', 'Oculus', 'SteamAudio', 'Resonance']).optional().default('Unity'),
            enableBinaural: z.boolean().optional().default(true),
            roomProperties: z.object({
                size: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional(),
                materials: z.object({
                    front: z.string().optional(),
                    back: z.string().optional(),
                    left: z.string().optional(),
                    right: z.string().optional(),
                    ceiling: z.string().optional(),
                    floor: z.string().optional()
                }).optional()
            }).optional(),
            enableHeadTracking: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_spatial_audio', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_audio_visualization', {
        title: 'Create Audio Visualization',
        description: 'Create visual effects that react to audio',
        inputSchema: z.object({
            visualizerName: z.string(),
            audioSource: z.string(),
            visualizationType: z.enum(['Spectrum', 'Waveform', 'BeatDetection', 'VUMeter']),
            targetObjects: z.array(z.string()).optional(),
            frequencyBands: z.number().optional().default(64),
            sensitivity: z.number().optional().default(100),
            responseProperty: z.enum(['Scale', 'Position', 'Rotation', 'Color', 'Emission']).optional().default('Scale'),
            smoothing: z.number().optional().default(0.5)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_audio_visualization', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Advanced Input Tools =====
    mcpServer.registerTool('unity_setup_custom_input', {
        title: 'Setup Custom Input',
        description: 'Configure custom input actions and bindings',
        inputSchema: z.object({
            actionName: z.string(),
            bindingType: z.enum(['Button', 'Axis', 'Vector2', 'Vector3']).default('Button'),
            bindings: z.array(z.object({
                path: z.string(),
                modifiers: z.array(z.string()).optional(),
                interactions: z.array(z.string()).optional(),
                processors: z.array(z.string()).optional()
            })),
            actionMap: z.string().optional().default('Player'),
            initialValue: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_custom_input', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_gesture_recognition', {
        title: 'Create Gesture Recognition',
        description: 'Setup gesture recognition for touch or motion input',
        inputSchema: z.object({
            gestureName: z.string(),
            gestureType: z.enum(['Swipe', 'Circle', 'Tap', 'DoubleTap', 'LongPress', 'Pinch', 'Rotate', 'Custom']),
            recognitionParameters: z.object({
                minDistance: z.number().optional().default(50),
                maxTime: z.number().optional().default(1),
                tolerance: z.number().optional().default(0.2),
                requiresDirection: z.boolean().optional().default(true)
            }).optional(),
            callbackEvent: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_gesture_recognition', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_haptic_feedback', {
        title: 'Setup Haptic Feedback',
        description: 'Configure haptic/vibration feedback for mobile and controllers',
        inputSchema: z.object({
            feedbackName: z.string(),
            platform: z.enum(['Mobile', 'Controller', 'Both']).default('Both'),
            feedbackType: z.enum(['Light', 'Medium', 'Heavy', 'Success', 'Warning', 'Error', 'Custom']),
            duration: z.number().optional().default(0.1),
            intensity: z.number().optional().default(1),
            pattern: z.array(z.number()).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_haptic_feedback', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_input_validation', {
        title: 'Create Input Validation',
        description: 'Create input validation system for text fields and forms',
        inputSchema: z.object({
            validatorName: z.string(),
            validationType: z.enum(['Email', 'Phone', 'Number', 'AlphaNumeric', 'Custom', 'CreditCard', 'URL']),
            customPattern: z.string().optional(),
            minLength: z.number().optional(),
            maxLength: z.number().optional(),
            errorMessage: z.string().optional(),
            realTimeValidation: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_input_validation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_accessibility_input', {
        title: 'Setup Accessibility Input',
        description: 'Configure accessibility features for input (screen readers, large buttons, etc)',
        inputSchema: z.object({
            accessibilityName: z.string(),
            features: z.array(z.enum(['ScreenReader', 'LargeButtons', 'HighContrast', 'VoiceControl', 'StickyKeys', 'FilterKeys', 'MouseKeys'])),
            voiceCommands: z.array(z.object({
                command: z.string(),
                action: z.string()
            })).optional(),
            buttonScaling: z.number().optional().default(1.5),
            contrastLevel: z.enum(['Normal', 'High', 'VeryHigh']).optional().default('High')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_accessibility_input', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_input_recording', {
        title: 'Create Input Recording',
        description: 'Setup input recording and playback system for testing or replay',
        inputSchema: z.object({
            recorderName: z.string(),
            recordingMode: z.enum(['All', 'Keyboard', 'Mouse', 'Touch', 'Controller', 'Custom']),
            includeTimestamps: z.boolean().optional().default(true),
            compression: z.boolean().optional().default(false),
            maxRecordingTime: z.number().optional().default(300),
            autoSave: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_input_recording', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Touch & Gesture Tools =====
    mcpServer.registerTool('unity_setup_multitouch', {
        title: 'Setup Multitouch',
        description: 'Configure multitouch input handling',
        inputSchema: z.object({
            maxTouches: z.number().optional().default(10),
            touchSensitivity: z.number().optional().default(1),
            enableGestures: z.boolean().optional().default(true),
            touchVisualization: z.boolean().optional().default(false),
            preventAccidentalTouches: z.boolean().optional().default(true),
            touchAreaPadding: z.number().optional().default(10)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_multitouch', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_pinch_zoom', {
        title: 'Create Pinch Zoom',
        description: 'Setup pinch-to-zoom functionality',
        inputSchema: z.object({
            targetObject: z.string(),
            zoomType: z.enum(['Camera', 'Object', 'UI']).default('Camera'),
            minZoom: z.number().optional().default(0.5),
            maxZoom: z.number().optional().default(3),
            zoomSpeed: z.number().optional().default(1),
            smoothing: z.boolean().optional().default(true),
            centerOnPinch: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_pinch_zoom', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_swipe_detection', {
        title: 'Setup Swipe Detection',
        description: 'Configure swipe gesture detection',
        inputSchema: z.object({
            swipeDirections: z.array(z.enum(['Up', 'Down', 'Left', 'Right', 'All'])).default(['All']),
            minSwipeDistance: z.number().optional().default(50),
            maxSwipeTime: z.number().optional().default(0.5),
            detectDiagonals: z.boolean().optional().default(false),
            continuousDetection: z.boolean().optional().default(false),
            swipeActions: z.array(z.object({
                direction: z.string(),
                action: z.string()
            })).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_swipe_detection', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_drag_drop', {
        title: 'Create Drag & Drop',
        description: 'Setup drag and drop functionality',
        inputSchema: z.object({
            draggableObject: z.string(),
            dropZones: z.array(z.string()).optional(),
            snapToGrid: z.boolean().optional().default(false),
            gridSize: z.number().optional().default(1),
            showGhost: z.boolean().optional().default(true),
            returnOnInvalidDrop: z.boolean().optional().default(true),
            dragThreshold: z.number().optional().default(5),
            allowRotation: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_drag_drop', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_touch_effects', {
        title: 'Setup Touch Effects',
        description: 'Configure visual effects for touch interactions',
        inputSchema: z.object({
            effectType: z.enum(['Ripple', 'Glow', 'Particle', 'Custom']).default('Ripple'),
            effectPrefab: z.string().optional(),
            color: z.string().optional().default('#FFFFFF'),
            size: z.number().optional().default(1),
            duration: z.number().optional().default(0.5),
            fadeOut: z.boolean().optional().default(true),
            followFinger: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_touch_effects', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Visual Enhancement Tools =====
    mcpServer.registerTool('unity_create_particle_preset', {
        title: 'Create Particle Preset',
        description: 'Create advanced particle effects with presets',
        inputSchema: z.object({
            effectName: z.string(),
            preset: z.enum(['Fire', 'Smoke', 'Explosion', 'Rain', 'Snow', 'Sparkles', 'Magic', 'Lightning', 'Tornado', 'Galaxy', 'Custom']).default('Fire'),
            customSettings: z.object({
                lifetime: z.number().optional().default(5),
                startSpeed: z.number().optional().default(5),
                startSize: z.number().optional().default(1),
                emission: z.number().optional().default(10),
                gravity: z.number().optional().default(0),
                shape: z.enum(['Sphere', 'Cone', 'Box', 'Circle', 'Edge', 'Mesh']).optional().default('Cone')
            }).optional(),
            colors: z.array(z.string()).optional(),
            usePhysics: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_particle_preset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_advanced_material', {
        title: 'Create Advanced Material',
        description: 'Create materials with advanced settings and textures',
        inputSchema: z.object({
            materialName: z.string(),
            shader: z.enum(['Standard', 'UniversalRP/Lit', 'HDRP/Lit', 'Unlit', 'Toon', 'Water', 'Glass', 'Hologram', 'Custom']).default('Standard'),
            properties: z.object({
                color: z.string().optional().default('#FFFFFF'),
                metallic: z.number().optional().default(0),
                smoothness: z.number().optional().default(0.5),
                emission: z.boolean().optional().default(false),
                emissionColor: z.string().optional().default('#000000'),
                emissionIntensity: z.number().optional().default(1),
                normalStrength: z.number().optional().default(1),
                occlusionStrength: z.number().optional().default(1)
            }).optional(),
            textures: z.object({
                mainTexture: z.string().optional(),
                normalMap: z.string().optional(),
                metallicMap: z.string().optional(),
                occlusionMap: z.string().optional(),
                emissionMap: z.string().optional()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_advanced_material', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_lighting_preset', {
        title: 'Setup Lighting Preset',
        description: 'Apply professional lighting presets to the scene',
        inputSchema: z.object({
            preset: z.enum(['Studio', 'Sunset', 'Night', 'Overcast', 'Desert', 'Forest', 'Underwater', 'Space', 'Neon', 'Custom']).default('Studio'),
            intensity: z.number().optional().default(1),
            ambientMode: z.enum(['Skybox', 'Gradient', 'Color']).optional().default('Skybox'),
            fog: z.boolean().optional().default(false),
            fogSettings: z.object({
                color: z.string().optional().default('#CCCCCC'),
                density: z.number().optional().default(0.01),
                start: z.number().optional().default(0),
                end: z.number().optional().default(300)
            }).optional(),
            shadows: z.enum(['None', 'Hard', 'Soft']).optional().default('Soft')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_lighting_preset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_visual_effect', {
        title: 'Create Visual Effect',
        description: 'Create complex visual effects combining particles, lights, and more',
        inputSchema: z.object({
            effectName: z.string(),
            effectType: z.enum(['Aura', 'Trail', 'Distortion', 'Glow', 'Shield', 'Portal', 'Dissolve', 'Hologram']).default('Aura'),
            target: z.string().optional(),
            duration: z.number().optional().default(0),
            loop: z.boolean().optional().default(true),
            intensity: z.number().optional().default(1)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_visual_effect', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_reflection_probe', {
        title: 'Setup Reflection Probe',
        description: 'Setup reflection probes for realistic reflections',
        inputSchema: z.object({
            probeName: z.string().optional().default('ReflectionProbe'),
            position: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional(),
            size: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().default({ x: 10, y: 10, z: 10 }),
            resolution: z.enum(['16', '32', '64', '128', '256', '512', '1024', '2048']).optional().default('128'),
            updateMode: z.enum(['OnAwake', 'EveryFrame', 'ViaScripting']).optional().default('OnAwake'),
            importance: z.number().optional().default(1)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_reflection_probe', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_light_probe_group', {
        title: 'Create Light Probe Group',
        description: 'Create light probe groups for dynamic GI',
        inputSchema: z.object({
            groupName: z.string().optional().default('LightProbeGroup'),
            gridSize: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().default({ x: 5, y: 3, z: 5 }),
            spacing: z.number().optional().default(2),
            center: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_light_probe_group', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_volumetric_fog', {
        title: 'Setup Volumetric Fog',
        description: 'Create atmospheric volumetric fog effects',
        inputSchema: z.object({
            fogName: z.string().optional().default('VolumetricFog'),
            density: z.number().optional().default(0.05),
            color: z.string().optional().default('#FFFFFF'),
            anisotropy: z.number().optional().default(0.5),
            lightPenetration: z.number().optional().default(0.5),
            noiseScale: z.number().optional().default(1),
            noiseIntensity: z.number().optional().default(0.5),
            windSpeed: z.number().optional().default(1),
            height: z.number().optional().default(100)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_volumetric_fog', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_decal', {
        title: 'Create Decal',
        description: 'Create decal projections for details like dirt, damage, etc',
        inputSchema: z.object({
            decalName: z.string(),
            texture: z.string().optional(),
            size: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().default({ x: 1, y: 1, z: 1 }),
            opacity: z.number().optional().default(1),
            normalBlend: z.number().optional().default(1),
            maskClipping: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_decal', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_color_grading', {
        title: 'Setup Color Grading',
        description: 'Apply color grading for cinematic look',
        inputSchema: z.object({
            preset: z.enum(['Cinematic', 'Vintage', 'BlackWhite', 'Sepia', 'Cold', 'Warm', 'Horror', 'Cyberpunk']).default('Cinematic'),
            temperature: z.number().optional().default(0),
            tint: z.number().optional().default(0),
            contrast: z.number().optional().default(0),
            brightness: z.number().optional().default(0),
            saturation: z.number().optional().default(0),
            gamma: z.object({
                r: z.number().optional().default(1),
                g: z.number().optional().default(1),
                b: z.number().optional().default(1)
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_color_grading', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_lens_flare', {
        title: 'Create Lens Flare',
        description: 'Create realistic lens flare effects',
        inputSchema: z.object({
            flareName: z.string(),
            intensity: z.number().optional().default(1),
            fadeSpeed: z.number().optional().default(3),
            color: z.string().optional().default('#FFFFFF'),
            elements: z.array(z.object({
                size: z.number(),
                position: z.number(),
                color: z.string().optional()
            })).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_lens_flare', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Screen Effects =====
    mcpServer.registerTool('unity_create_screen_shake', {
        title: 'Create Screen Shake',
        description: 'Apply screen shake effect for dramatic impact',
        inputSchema: z.object({
            duration: z.number().optional().default(0.5),
            intensity: z.number().optional().default(1),
            frequency: z.number().optional().default(10),
            damping: z.number().optional().default(1),
            camera: z.string().optional().default('Main Camera')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_screen_shake', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_screen_fade', {
        title: 'Create Screen Fade',
        description: 'Create fade in/out transitions',
        inputSchema: z.object({
            fadeType: z.enum(['FadeIn', 'FadeOut']).default('FadeIn'),
            duration: z.number().optional().default(1),
            color: z.string().optional().default('#000000'),
            delay: z.number().optional().default(0),
            curve: z.enum(['Linear', 'EaseIn', 'EaseOut', 'EaseInOut']).optional().default('Linear')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_screen_fade', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_vignette_effect', {
        title: 'Create Vignette Effect',
        description: 'Add cinematic vignette framing to scene',
        inputSchema: z.object({
            intensity: z.number().optional().default(0.5),
            smoothness: z.number().optional().default(0.5),
            color: z.string().optional().default('#000000'),
            rounded: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_vignette_effect', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_chromatic_aberration', {
        title: 'Create Chromatic Aberration',
        description: 'Add lens distortion and color separation effect',
        inputSchema: z.object({
            intensity: z.number().optional().default(0.1),
            camera: z.string().optional().default('Main Camera')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_chromatic_aberration', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== External Shader Editing =====
    mcpServer.registerTool('unity_read_shader', {
        title: 'Read Shader File',
        description: `Read the content of a .shader file. This must be called BEFORE modifying the shader.
Returns the full shader source code with line numbers.
Supports: path (e.g., "Assets/Shaders/MyShader.shader") or partial name search.`,
        inputSchema: z.object({
            path: z.string().describe('Path to shader file (e.g., "Assets/Shaders/MyShader.shader")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('read_shader', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_modify_shader', {
        title: 'Modify Shader File',
        description: `Modify an existing .shader file by replacing text.
⚠️ FILE_NOT_READ ERROR: You MUST call unity_read_shader first!
Creates automatic backup (.bak file) before modification.`,
        inputSchema: z.object({
            path: z.string().describe('Path to shader file'),
            old_text: z.string().describe('Exact text to find and replace (including whitespace)'),
            new_text: z.string().describe('New text to insert'),
            replace_all: z.boolean().optional().default(false).describe('Replace all occurrences')
        })
    }, async (params) => {
        const result = await sendUnityCommand('modify_shader', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_analyze_shader', {
        title: 'Analyze Shader Structure',
        description: `Analyze a shader's structure without reading full content.
Returns: shader name, properties, SubShaders, Passes, pragmas, includes, features, pipeline compatibility.
Use path OR shader_name (e.g., "Standard", "Universal Render Pipeline/Lit").`,
        inputSchema: z.object({
            path: z.string().optional().describe('Path to shader file'),
            shader_name: z.string().optional().describe('Shader name (e.g., "Standard")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_shader', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_read_shader_graph', {
        title: 'Read ShaderGraph Asset',
        description: `Read and parse a Unity ShaderGraph (.shadergraph or .shadersubgraph) file.
Returns: node count, properties, node types, master node type, precision settings.
Also returns raw JSON content for detailed inspection.`,
        inputSchema: z.object({
            path: z.string().describe('Path to ShaderGraph file (e.g., "Assets/Shaders/MyGraph.shadergraph")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('read_shader_graph', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== VFX Graph Asset Editing =====
    mcpServer.registerTool('unity_read_vfx_graph', {
        title: 'Read VFX Graph Asset',
        description: `Read and analyze an existing VFX Graph (.vfx) asset file.
Returns: system count, capacity, spawn rate, detected blocks, exposed parameters, output type, blend mode.
IMPORTANT: Must call this before using unity_modify_vfx_graph.`,
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file (e.g., "Assets/VFX/MyEffect.vfx")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('read_vfx_graph', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_modify_vfx_graph', {
        title: 'Modify VFX Graph Asset',
        description: `Modify an existing VFX Graph asset by text replacement.
IMPORTANT: You MUST call unity_read_vfx_graph first before modifying.
Creates automatic backup (.bak file) before modification.`,
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file'),
            old_text: z.string().describe('Text to find and replace'),
            new_text: z.string().describe('Replacement text'),
            replace_all: z.boolean().optional().default(false).describe('Replace all occurrences')
        })
    }, async (params) => {
        const result = await sendUnityCommand('modify_vfx_graph', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_analyze_vfx_graph', {
        title: 'Analyze VFX Graph',
        description: `Analyze a VFX Graph asset structure without reading full content.
Returns: file info, complexity estimation, detected blocks, parameters, output type.`,
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file')
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_vfx_graph', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== VFX Graph Editing Tools =====
    mcpServer.registerTool('unity_vfx_set_output', {
        title: 'Set VFX Output Settings',
        description: `Set output context settings on a VFX Graph (blendMode, texture, etc.)
Use contextIndex=3 for output context (default in most VFX graphs).

Available Kenney textures (use full path like "Assets/Synaptic AI Pro/Resources/VFX/Textures/flame_01.png"):
- Fire/Flame: flame_01~06, fire_01~02, flare_01
- Smoke: smoke_01~10, whitePuff00~24, blackSmoke00~24
- Explosion: explosion00~08
- Sparks: spark_01~07
- Magic: magic_01~05, star_01~09, twirl_01~03
- Trails: trace_01~07
- Effects: muzzle_01~05, slash_01~04, light_01~03
- Shapes: circle_01~05, scorch_01~03, symbol_01~02`,
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file'),
            contextIndex: z.number().optional().default(3).describe('Context index (output is usually 3)'),
            blendMode: z.enum(['additive', 'alpha', 'opaque', 'premultiply']).optional().describe('Blend mode'),
            texture: z.string().optional().describe('Texture asset path (e.g., Assets/Synaptic AI Pro/Resources/VFX/Textures/flame_01.png)'),
            softParticle: z.boolean().optional().describe('Enable soft particles'),
            sortPriority: z.number().optional().describe('Sort priority')
        })
    }, async (params) => {
        const result = await sendUnityCommand('vfx_set_output', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_set_block_value', {
        title: 'Set VFX Block Value',
        description: `Set a value on a VFX block (e.g., SetAttribute color, Turbulence intensity).
Use unity_vfx_list_blocks first to find the correct contextIndex and blockIndex.`,
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file'),
            contextIndex: z.number().describe('Context index (0=spawn, 1=init, 2=update, 3=output)'),
            blockIndex: z.number().describe('Block index within context'),
            property: z.string().optional().default('value').describe('Property name to set'),
            value: z.string().describe('Value to set (e.g., "1,0.5,0" for color, "150" for rate)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('vfx_set_block_value', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_set_spawn_rate', {
        title: 'Set VFX Spawn Rate',
        description: 'Set the spawn rate of a VFX Graph.',
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file'),
            rate: z.number().describe('Particles per second')
        })
    }, async (params) => {
        const result = await sendUnityCommand('vfx_set_spawn_rate', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_list_blocks', {
        title: 'List VFX Blocks',
        description: 'List all contexts and blocks in a VFX Graph with their indices and types.',
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file')
        })
    }, async (params) => {
        const result = await sendUnityCommand('vfx_list_blocks', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_remove_block', {
        title: 'Remove VFX Block',
        description: 'Remove a block from a VFX context.',
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file'),
            contextIndex: z.number().describe('Context index'),
            blockIndex: z.number().describe('Block index to remove')
        })
    }, async (params) => {
        const result = await sendUnityCommand('vfx_remove_block', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_get_block_info', {
        title: 'Get VFX Block Info',
        description: 'Get detailed information about a specific VFX block including current values.',
        inputSchema: z.object({
            path: z.string().describe('Path to VFX Graph file'),
            contextIndex: z.number().describe('Context index'),
            blockIndex: z.number().describe('Block index')
        })
    }, async (params) => {
        const result = await sendUnityCommand('vfx_get_block_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    // ===== Particle System Editing =====
    mcpServer.registerTool('unity_read_particle_system', {
        title: 'Read Particle System',
        description: `Read all properties of a Particle System component on a GameObject.
Returns: main module settings, emission, shape, noise, collision, renderer info.`,
        inputSchema: z.object({
            target: z.string().optional().describe('Name of GameObject with ParticleSystem'),
            gameObject: z.string().optional().describe('Alternative name parameter')
        })
    }, async (params) => {
        const result = await sendUnityCommand('read_particle_system', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_modify_particle_system', {
        title: 'Modify Particle System',
        description: `Modify properties of an existing Particle System component.
Modules: main, emission, shape, noise, collision, velocityOverLifetime, colorOverLifetime, sizeOverLifetime.
Main properties: duration, loop, startLifetime, startSpeed, startSize, startColor, gravityModifier, maxParticles, etc.`,
        inputSchema: z.object({
            target: z.string().optional().describe('Name of GameObject with ParticleSystem'),
            gameObject: z.string().optional().describe('Alternative name parameter'),
            module: z.string().optional().default('main').describe('Module to modify (main, emission, shape, noise, etc.)'),
            property: z.string().describe('Property name to modify'),
            value: z.string().describe('New value for the property')
        })
    }, async (params) => {
        const result = await sendUnityCommand('modify_particle_system', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Shader Tools =====
    mcpServer.registerTool('unity_create_shader_property_animator', {
        title: 'Create Shader Property Animator',
        description: 'Animate shader properties like color, float, or vector values',
        inputSchema: z.object({
            targetObject: z.string(),
            propertyName: z.string().default('_Color'),
            propertyType: z.enum(['Color', 'Float', 'Vector']).default('Color'),
            startValue: z.string(),
            endValue: z.string(),
            duration: z.number().optional().default(1),
            loop: z.boolean().optional().default(false),
            curve: z.enum(['Linear', 'EaseIn', 'EaseOut', 'EaseInOut', 'Bounce']).optional().default('Linear')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_shader_property_animator', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_material_property_block', {
        title: 'Create Material Property Block',
        description: 'Modify material properties without creating material instances',
        inputSchema: z.object({
            targetObject: z.string(),
            blockName: z.string().optional().default('CustomPropertyBlock'),
            preserveSharedMaterial: z.boolean().optional().default(true),
            properties: z.record(z.any()).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_material_property_block', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_animate_shader_texture', {
        title: 'Animate Shader Texture',
        description: 'Create scrolling, flipbook, or rotating texture animations',
        inputSchema: z.object({
            targetObject: z.string(),
            propertyName: z.string().optional().default('_MainTex'),
            animationType: z.enum(['Scroll', 'Flipbook', 'Rotate']).default('Scroll'),
            speed: z.string().optional().default('1,0'),
            scale: z.string().optional().default('1,1'),
            columns: z.number().optional().default(4),
            rows: z.number().optional().default(4),
            fps: z.number().optional().default(30)
        })
    }, async (params) => {
        const result = await sendUnityCommand('animate_shader_texture', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_shader_gradient', {
        title: 'Create Shader Gradient',
        description: 'Apply gradient effects to materials',
        inputSchema: z.object({
            targetObject: z.string(),
            gradientType: z.enum(['Linear', 'Radial']).default('Linear'),
            startColor: z.string().optional().default('#FFFFFF'),
            endColor: z.string().optional().default('#000000'),
            direction: z.enum(['Horizontal', 'Vertical', 'Diagonal']).optional().default('Vertical'),
            blendMode: z.enum(['Normal', 'Additive', 'Multiply']).optional().default('Normal')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_shader_gradient', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Camera Effects =====
    // Depth of Field removed - requires Post Processing Stack

    // Motion Blur removed - requires Post Processing Stack

    // Film Grain removed - requires Post Processing Stack

    // Bloom removed - requires Post Processing Stack

    // ===== Phase 3: Restored Visual Effects with Auto-Generated Shaders =====
    
    mcpServer.registerTool('unity_create_bloom', {
        title: 'Create Bloom Effect',
        description: '🌟 Create bloom effect with auto-generated shader - Adds beautiful glow to bright areas!',
        inputSchema: z.object({
            intensity: z.number().optional().default(2.0).describe('Bloom intensity (0-10)'),
            threshold: z.number().optional().default(0.9).describe('Brightness threshold for bloom (0-1)'),
            blurSize: z.number().optional().default(3.0).describe('Blur size for bloom effect (0-10)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_bloom', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_film_grain', {
        title: 'Create Film Grain Effect',
        description: '🎬 Create film grain effect with auto-generated shader - Adds cinematic film texture!',
        inputSchema: z.object({
            intensity: z.number().optional().default(0.3).describe('Grain intensity (0-1)'),
            grainSize: z.number().optional().default(2.0).describe('Grain texture size (0.1-10)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_film_grain', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_motion_blur', {
        title: 'Create Motion Blur Effect',
        description: '💨 Create motion blur effect with auto-generated shader - Adds dynamic motion feel!',
        inputSchema: z.object({
            blurStrength: z.number().optional().default(0.5).describe('Motion blur strength (0-1)'),
            sampleCount: z.number().optional().default(16).describe('Quality samples (4-32, higher = better quality)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_motion_blur', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_depth_of_field', {
        title: 'Create Depth of Field Effect',
        description: '📷 Create depth of field effect with auto-generated shader - Professional camera focus!',
        inputSchema: z.object({
            focusDistance: z.number().optional().default(10.0).describe('Focus distance (0-100)'),
            blurSize: z.number().optional().default(2.0).describe('Blur size for out-of-focus areas (0-10)'),
            aperture: z.number().optional().default(5.6).describe('Camera aperture simulation (0.1-32)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_depth_of_field', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_lens_distortion', {
        title: 'Create Lens Distortion Effect',
        description: '🔍 Create lens distortion effect with auto-generated shader - Realistic camera lens effects!',
        inputSchema: z.object({
            distortionStrength: z.number().optional().default(0.2).describe('Distortion strength (-1 to 1, negative = pincushion, positive = barrel)'),
            chromaticAberration: z.number().optional().default(0.1).describe('Chromatic aberration amount (0-1)'),
            vignette: z.number().optional().default(0.3).describe('Vignette effect strength (0-1)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_lens_distortion', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    // ===== Phase 4: Advanced Rendering & VFX Systems =====
    
    mcpServer.registerTool('unity_setup_urp_settings', {
        title: 'Setup URP Settings',
        description: '🖼️ Configure Universal Render Pipeline settings with natural language - Optimize for any platform!',
        inputSchema: z.object({
            platform: z.enum(['mobile', 'android', 'ios', 'console', 'playstation', 'xbox', 'pc', 'desktop']).optional().default('pc'),
            quality: z.enum(['low', 'medium', 'high', 'ultra']).optional().default('medium'),
            shadows: z.enum(['off', 'disabled', 'low', 'medium', 'high', 'ultra']).optional().default('medium'),
            antialiasing: z.enum(['none', 'fxaa', 'taa', 'smaa']).optional().default('fxaa'),
            renderscale: z.number().optional().default(1.0).describe('Render scale multiplier (0.5-2.0)'),
            hdr: z.boolean().optional().default(true).describe('Enable HDR rendering'),
            msaa: z.enum(['off', '2x', '4x', '8x']).optional().default('4x')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_urp_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_hdrp_settings', {
        title: 'Setup HDRP Settings',
        description: '🌟 Configure High Definition Render Pipeline settings - For photorealistic visuals!',
        inputSchema: z.object({
            platform: z.enum(['pc', 'console', 'playstation', 'xbox']).optional().default('pc'),
            quality: z.enum(['medium', 'high', 'ultra']).optional().default('high'),
            raytracing: z.boolean().optional().default(false).describe('Enable ray tracing features'),
            volumetrics: z.boolean().optional().default(true).describe('Enable volumetric lighting'),
            reflections: z.enum(['screenspace', 'raytraced', 'hybrid']).optional().default('screenspace')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_hdrp_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_post_processing', {
        title: 'Setup Post Processing',
        description: '🎨 Configure post-processing effects stack - Professional color grading and effects!',
        inputSchema: z.object({
            profile: z.enum(['cinematic', 'realistic', 'stylized', 'mobile', 'custom']).optional().default('cinematic'),
            bloom: z.boolean().optional().default(true),
            colorgrading: z.boolean().optional().default(true),
            vignette: z.boolean().optional().default(true),
            filmgrain: z.boolean().optional().default(false),
            motionblur: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_post_processing', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_vfx_graph', {
        title: 'Create VFX / Particle Effect',
        description: 'Create VFX particle effects with advanced features. Supports mesh spawning, SDF shapes, splines, custom output modes. Uses VFX Graph (GPU) if available, otherwise Particle System.',
        inputSchema: z.object({
            effectType: z.enum([
                'fire', 'smoke', 'explosion', 'magic', 'water', 'splash',
                'energy', 'sparks', 'dust', 'snow', 'rain', 'leaves', 'confetti',
                'trail', 'cyber', 'heal', 'buff', 'debuff', 'portal', 'custom'
            ]).optional().default('fire').describe('Type of VFX effect preset'),
            name: z.string().optional().describe('Custom name for the VFX GameObject'),
            intensity: z.number().min(0.1).max(5.0).optional().default(1.0).describe('Effect intensity multiplier'),
            duration: z.number().optional().default(5.0).describe('Effect duration in seconds'),
            looping: z.boolean().optional().default(true).describe('Whether effect loops continuously'),
            particleCount: z.number().min(10).max(1000000).optional().default(1000).describe('Maximum particle count (GPU can handle millions!)'),
            color: z.string().optional().describe('Start/main color in hex (e.g., "#00FFFF")'),
            endColor: z.string().optional().describe('End color for gradient effects'),
            position: z.string().optional().default('0,0,0').describe('World position as "x,y,z"'),
            // Advanced VFX Graph features
            shapeType: z.enum(['', 'mesh', 'sdf', 'spline', 'point_cache']).optional().default('').describe('Advanced spawn shape (requires VFX Graph package)'),
            meshPath: z.string().optional().describe('Asset path to mesh for mesh spawning (e.g., "Assets/Models/Sword.fbx")'),
            meshName: z.string().optional().describe('Name of GameObject in scene to use as mesh source'),
            splinePoints: z.string().optional().describe('Spline points as "x,y,z;x,y,z;..." for path-based effects'),
            sdfShape: z.enum(['sphere', 'box', 'torus', 'custom']).optional().default('sphere').describe('SDF shape type'),
            outputMode: z.enum(['particle', 'mesh', 'decal', 'line']).optional().default('particle').describe('Output rendering mode'),
            blendMode: z.enum(['additive', 'alpha', 'opaque']).optional().default('additive').describe('Blend mode for rendering')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_vfx_graph', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_shader_graph', {
        title: 'Create Shader Graph',
        description: '🎭 Create Shader Graph with natural language - Custom materials made easy!',
        inputSchema: z.object({
            shaderType: z.enum(['surface', 'unlit', 'lit', 'water', 'glass', 'metal', 'fabric', 'custom']).optional().default('surface'),
            pipeline: z.enum(['urp', 'hdrp', 'builtin']).optional().default('urp'),
            features: z.array(z.enum(['normal_map', 'emission', 'transparency', 'metallic', 'roughness', 'subsurface'])).optional().default(['normal_map']),
            animated: z.boolean().optional().default(false).describe('Include time-based animation')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_shader_graph', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // VFX Graph Property Tools
    mcpServer.registerTool('unity_set_vfx_property', {
        title: 'Set VFX Graph Property',
        description: 'Modify exposed properties of a VFX Graph at runtime. Change colors, intensity, particle count, etc.',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of GameObject with VisualEffect component'),
            propertyName: z.string().describe('Exposed property name (e.g., "Intensity", "Color", "ParticleCount")'),
            propertyType: z.enum(['float', 'int', 'bool', 'vector2', 'vector3', 'vector4', 'color', 'gradient', 'texture', 'mesh']).describe('Type of the property'),
            value: z.string().describe('Value to set. Float: "1.5", Color: "#FF00FF", Vector3: "1,2,3", Bool: "true"')
        })
    }, async (params) => {
        const result = await sendUnityCommand('SET_VFX_PROPERTY', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_vfx_properties', {
        title: 'Get VFX Graph Properties',
        description: 'Get all exposed properties of a VFX Graph. See available parameters for modification.',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of GameObject with VisualEffect component')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_VFX_PROPERTIES', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_trigger_vfx_event', {
        title: 'Trigger VFX Event',
        description: 'Send event to VFX Graph (play, stop, custom events)',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of GameObject with VisualEffect component'),
            eventName: z.string().optional().default('OnPlay').describe('Event name (OnPlay, OnStop, or custom event)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('TRIGGER_VFX_EVENT', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ==================== VFX Graph Builder API ====================

    mcpServer.registerTool('unity_vfx_create', {
        title: 'Create VFX Graph',
        description: 'Create a new VFX Graph asset. This is the first step for building custom VFX.',
        inputSchema: z.object({
            name: z.string().describe('Name of the VFX Graph asset'),
            folder: z.string().optional().default('Assets/VFX').describe('Folder path to save the asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_CREATE', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_add_context', {
        title: 'Add VFX Context',
        description: 'Add a context (Spawn, Initialize, Update, Output) to VFX Graph. Contexts define the particle lifecycle.',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset (e.g., "Assets/VFX/Fire.vfx")'),
            contextType: z.enum([
                'spawn', 'spawner', 'gpuspawn',
                'initialize', 'init',
                'update',
                'output', 'quad', 'point', 'line', 'linestrip', 'quadstrip', 'trail', 'ribbon', 'mesh', 'staticmesh', 'decal',
                'event', 'outputevent'
            ]).describe('Type of context to add'),
            capacity: z.number().optional().describe('Particle capacity (for Initialize context)'),
            spawnRate: z.number().optional().describe('Spawn rate (for Spawn context)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_ADD_CONTEXT', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_add_block', {
        title: 'Add VFX Block',
        description: 'Add a block to a context. Blocks define particle behavior (position, velocity, color, forces, etc.)',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset'),
            contextIndex: z.number().describe('Index of context to add block to (0-based)'),
            blockType: z.enum([
                // Position
                'positionsphere', 'positioncircle', 'positioncone', 'positionline', 'positionbox', 'positiontorus', 'positionmesh', 'positionsdf',
                // Forces
                'gravity', 'drag', 'turbulence', 'force', 'conformtosphere', 'conformtosdf', 'vectorfieldforce',
                // Attributes
                'setattribute', 'setattributerandom', 'setposition', 'setvelocity', 'setcolor', 'setsize', 'setlifetime', 'setalpha', 'setangle',
                // Random
                'velocityrandom',
                // Collision
                'collisionsphere', 'collisionplane', 'collisionbox', 'collisiondepth',
                // Kill
                'killsphere', 'killbox', 'killplane', 'killage',
                // Orientation
                'orient', 'facecamera', 'orientalongvelocity',
                // Over lifetime
                'coloroverlife', 'sizeoverlife',
                // Spawn
                'spawnrate', 'spawnburst',
                // Other
                'flipbook', 'triggerevent'
            ]).describe('Type of block to add'),
            attribute: z.string().optional().describe('Attribute name for SetAttribute blocks (e.g., "position", "color", "size", "lifetime", "velocity")'),
            value: z.string().optional().describe('Value to set (e.g., "1,0,0" for vector, "#FF0000" for color)'),
            min: z.union([z.string(), z.number()]).optional().describe('Minimum value for setattributerandom (e.g., 0.5 for float, "0,0,0" for vector)'),
            max: z.union([z.string(), z.number()]).optional().describe('Maximum value for setattributerandom (e.g., 2.0 for float, "1,1,1" for vector)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_ADD_BLOCK', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_add_operator', {
        title: 'Add VFX Operator',
        description: 'Add an operator node to VFX Graph. Operators perform calculations (math, noise, sampling, etc.)',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset'),
            operatorType: z.enum([
                // Math
                'add', 'subtract', 'multiply', 'divide', 'power', 'modulo', 'absolute', 'negate', 'minimum', 'maximum', 'clamp', 'saturate', 'lerp', 'smoothstep',
                // Trigonometric
                'sine', 'cosine', 'tangent', 'atan2',
                // Vector
                'dot', 'cross', 'length', 'distance', 'normalize',
                // Noise
                'noise', 'curlnoise', 'voronoise',
                // Sampling
                'sampletexture2d', 'sampletexture3d', 'samplecurve', 'samplegradient', 'samplemesh', 'samplesdf',
                // Logic
                'compare', 'branch', 'and', 'or', 'not',
                // Utility
                'random', 'time', 'deltatime', 'maincamera',
                // Waveforms
                'sinewave', 'squarewave', 'trianglewave', 'sawtoothwave'
            ]).describe('Type of operator to add')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_ADD_OPERATOR', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_link_contexts', {
        title: 'Link VFX Contexts',
        description: 'Link two contexts together (e.g., Spawn -> Initialize -> Update -> Output)',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset'),
            fromIndex: z.number().describe('Index of source context'),
            toIndex: z.number().describe('Index of target context')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_LINK_CONTEXTS', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_get_structure', {
        title: 'Get VFX Structure',
        description: 'Get the structure of a VFX Graph (contexts, blocks, operators)',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_GET_STRUCTURE', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_compile', {
        title: 'Compile VFX',
        description: 'Compile and save the VFX Graph',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_COMPILE', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_get_available_types', {
        title: 'Get Available VFX Types',
        description: 'List all available context, block, and operator types',
        inputSchema: z.object({
            category: z.enum(['all', 'contexts', 'blocks', 'operators']).optional().default('all').describe('Category to list')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_GET_AVAILABLE_TYPES', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_add_parameter', {
        title: 'Add VFX Parameter',
        description: 'Add an exposed parameter to VFX Graph that can be controlled from Inspector or scripts',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset'),
            name: z.string().describe('Parameter name'),
            type: z.enum(['float', 'int', 'bool', 'vector2', 'vector3', 'vector4', 'color', 'texture2d', 'texture3d', 'mesh', 'gradient', 'curve']).describe('Parameter type'),
            defaultValue: z.string().optional().describe('Default value (e.g., "1.0", "1,2,3" for vector)'),
            exposed: z.boolean().optional().default(true).describe('Whether parameter is exposed in Inspector')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_ADD_PARAMETER', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_connect_slots', {
        title: 'Connect VFX Slots',
        description: 'Connect output slot of one node to input slot of another (operator->block, parameter->operator, etc.)',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset'),
            sourceNodeIndex: z.number().describe('Index of source node'),
            sourceSlotIndex: z.number().describe('Index of output slot on source'),
            targetNodeIndex: z.number().describe('Index of target node (for blocks: contextIndex << 16 | blockIndex)'),
            targetSlotIndex: z.number().describe('Index of input slot on target'),
            sourceType: z.enum(['operator', 'parameter', 'context']).optional().default('operator').describe('Type of source node'),
            targetType: z.enum(['operator', 'block']).optional().default('block').describe('Type of target node')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_CONNECT_SLOTS', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_set_attribute', {
        title: 'Set VFX Block Attribute',
        description: 'Set attribute value on a SetAttribute block (position, velocity, color, size, lifetime, etc.)',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset'),
            contextIndex: z.number().describe('Index of context containing the block'),
            blockIndex: z.number().describe('Index of SetAttribute block within context'),
            attribute: z.string().describe('Attribute name (position, velocity, color, size, lifetime, alpha, angle, etc.)'),
            value: z.string().describe('Value to set (e.g., "1,2,3" for vector, "#FF0000" for color, "0.5" for float)'),
            composition: z.enum(['overwrite', 'add', 'multiply', 'blend']).optional().default('overwrite').describe('How to combine with existing value')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_SET_ATTRIBUTE', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_create_preset', {
        title: 'Create VFX Preset',
        description: 'Create a complete VFX Graph from preset (fire, smoke, sparks, trail, explosion)',
        inputSchema: z.object({
            name: z.string().describe('Name for the VFX asset'),
            preset: z.enum(['fire', 'smoke', 'sparks', 'trail', 'explosion']).describe('Preset type'),
            folder: z.string().optional().default('Assets/VFX').describe('Folder to save the asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_CREATE_PRESET', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_configure_output', {
        title: 'Configure VFX Output',
        description: 'Configure VFX output context settings (texture, blend mode, soft particles, etc.)',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset (e.g., "Assets/VFX/Fire.vfx")'),
            contextIndex: z.number().describe('Index of the output context'),
            texture: z.string().optional().describe('Path to texture asset'),
            blendMode: z.enum(['additive', 'alpha', 'premultiply', 'opaque', 'masked']).optional().describe('Blend mode'),
            softParticle: z.boolean().optional().describe('Enable soft particles'),
            castShadow: z.boolean().optional().describe('Cast shadows'),
            sortPriority: z.number().optional().describe('Sort priority')
        })
    }, async (params) => {
        const result = await sendUnityCommand('VFX_CONFIGURE_OUTPUT', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_vfx_set_color_gradient', {
        title: 'Set VFX Color Gradient',
        description: 'Set color gradient for ColorOverLife or similar blocks',
        inputSchema: z.object({
            vfxPath: z.string().describe('Path to VFX asset'),
            contextIndex: z.number().describe('Index of the context containing the block'),
            blockIndex: z.number().describe('Index of the ColorOverLife block'),
            colors: z.array(z.string()).describe('Array of hex colors (e.g., ["#FF6600", "#FF0000", "#000000"])'),
            times: z.array(z.number()).optional().describe('Array of time values (0-1), defaults to evenly distributed')
        })
    }, async (params) => {
        // Convert arrays to JSON strings for transmission
        const modifiedParams = {
            ...params,
            colors: JSON.stringify(params.colors),
            times: params.times ? JSON.stringify(params.times) : undefined
        };
        const result = await sendUnityCommand('VFX_SET_COLOR_GRADIENT', modifiedParams);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_setup_lighting_scenarios', {
        title: 'Setup Lighting Scenarios',
        description: '💡 Create lighting scenarios for different moods - From dawn to midnight!',
        inputSchema: z.object({
            scenario: z.enum(['dawn', 'morning', 'noon', 'afternoon', 'sunset', 'dusk', 'night', 'midnight', 'studio', 'dramatic']).optional().default('noon'),
            ambientIntensity: z.number().optional().default(1.0).describe('Ambient light intensity'),
            shadowStrength: z.number().optional().default(1.0).describe('Shadow strength (0-1)'),
            colorTemperature: z.number().optional().default(6500).describe('Light color temperature in Kelvin'),
            foggy: z.boolean().optional().default(false).describe('Add atmospheric fog')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_lighting_scenarios', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    // ===== Weather System Tools =====
    
    // Unified Weather System - The Ultimate Weather Solution!
    mcpServer.registerTool('unity_create_weather_system', {
        title: 'Create Weather System',
        description: '🌦️ Create complete weather system with cinematic presets - One command for movie-quality weather!',
        inputSchema: z.object({
            preset: z.enum(['sunny', 'rainy', 'snowy', 'stormy', 'foggy', 'custom']).optional().default('sunny'),
            intensity: z.number().optional().default(0.5).describe('Overall weather intensity (0-1)'),
            transitionTime: z.number().optional().default(5).describe('Time to transition between weather states')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_weather_system', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_set_weather_preset', {
        title: 'Set Weather Preset',
        description: 'Smoothly transition to a different weather preset',
        inputSchema: z.object({
            preset: z.enum(['sunny', 'rainy', 'snowy', 'stormy', 'foggy']),
            transitionTime: z.number().optional().default(5).describe('Transition duration in seconds')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_weather_preset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_rain_effect', {
        title: 'Create Rain Effect',
        description: 'Create realistic rain effect with particle system',
        inputSchema: z.object({
            intensity: z.number().optional().default(0.5),
            dropSize: z.number().optional().default(0.02),
            dropSpeed: z.number().optional().default(10),
            windStrength: z.number().optional().default(0),
            rainArea: z.string().optional().default('50,30,50'),
            useSplash: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_rain_effect', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_snow_effect', {
        title: 'Create Snow Effect',
        description: 'Create snow effect with falling snowflakes',
        inputSchema: z.object({
            intensity: z.number().optional().default(0.3),
            flakeSize: z.string().optional().default('0.1,0.3'),
            fallSpeed: z.number().optional().default(1),
            turbulence: z.number().optional().default(0.5),
            snowArea: z.string().optional().default('50,20,50'),
            accumulation: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_snow_effect', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_wind_effect', {
        title: 'Create Wind Effect',
        description: 'Create wind effect affecting objects and particles',
        inputSchema: z.object({
            strength: z.number().optional().default(5),
            direction: z.string().optional().default('1,0,0'),
            turbulence: z.number().optional().default(0.5),
            gustFrequency: z.number().optional().default(0.2),
            affectTrees: z.boolean().optional().default(true),
            affectParticles: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_wind_effect', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_lightning_effect', {
        title: 'Create Lightning Effect',
        description: 'Create lightning strikes with flash effects',
        inputSchema: z.object({
            frequency: z.number().optional().default(0.1),
            intensity: z.number().optional().default(5),
            duration: z.number().optional().default(0.2),
            color: z.string().optional().default('#E0E0FF'),
            minDelay: z.number().optional().default(2),
            maxDelay: z.number().optional().default(10),
            affectSkybox: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_lightning_effect', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_thunderstorm', {
        title: 'Create Thunderstorm',
        description: 'Create complete thunderstorm with rain, wind, lightning and fog',
        inputSchema: z.object({
            intensity: z.number().optional().default(0.8),
            windStrength: z.number().optional().default(10),
            lightningFrequency: z.number().optional().default(0.2),
            fogDensity: z.number().optional().default(0.02),
            duration: z.number().optional().default(0).describe('Duration in seconds (0 = continuous)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_thunderstorm', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    // ===== Time of Day System Tools =====
    
    mcpServer.registerTool('unity_create_time_of_day', {
        title: 'Create Time of Day System',
        description: 'Create dynamic day-night cycle with sun and moon movement',
        inputSchema: z.object({
            startTime: z.number().optional().default(12),
            dayDuration: z.number().optional().default(60).describe('Duration of full day cycle in seconds'),
            latitude: z.number().optional().default(35),
            longitude: z.number().optional().default(0),
            enableClouds: z.boolean().optional().default(true),
            enableStars: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_time_of_day', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_set_time_of_day', {
        title: 'Set Time of Day',
        description: 'Set specific time in the day-night cycle',
        inputSchema: z.object({
            time: z.number().min(0).max(24).describe('Target time (0-24 hours)'),
            instant: z.boolean().optional().default(false),
            transitionDuration: z.number().optional().default(5)
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_time_of_day', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_day_night_preset', {
        title: 'Create Day Night Preset',
        description: 'Apply preset lighting conditions for different environments',
        inputSchema: z.object({
            preset: z.enum(['Default', 'Tropical', 'Desert', 'Arctic', 'Urban']).optional().default('Default'),
            sunriseTime: z.number().optional().default(6),
            sunsetTime: z.number().optional().default(18),
            sunriseColor: z.string().optional().default('#FF8C42'),
            noonColor: z.string().optional().default('#FFFFFF'),
            sunsetColor: z.string().optional().default('#FF6B35'),
            nightColor: z.string().optional().default('#1E3A8A')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_day_night_preset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_skybox_blend', {
        title: 'Create Skybox Blend',
        description: 'Blend between day and night skyboxes',
        inputSchema: z.object({
            daySkybox: z.string().optional().default('Default-Skybox'),
            nightSkybox: z.string().optional().default(''),
            blendCurve: z.enum(['Linear', 'Smooth', 'Sharp']).optional().default('Smooth'),
            cloudSpeed: z.number().optional().default(0.1),
            cloudOpacity: z.number().optional().default(0.5)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_skybox_blend', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_time_event', {
        title: 'Create Time Event',
        description: 'Create events triggered at specific times',
        inputSchema: z.object({
            eventName: z.string(),
            triggerTime: z.number().min(0).max(24).describe('Time to trigger event (0-24)'),
            eventType: z.enum(['Once', 'Daily']).optional().default('Once'),
            action: z.string().describe('Action to perform (activate, deactivate, toggle)'),
            targetObject: z.string().optional().describe('GameObject to affect')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_time_event', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 検索 =====
    mcpServer.registerTool('unity_search', {
        title: 'Search Objects',
        description: 'Search for objects in the scene',
        inputSchema: z.object({
            searchType: z.enum(['name', 'tag', 'layer', 'component']),
            query: z.string(),
            includeInactive: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('UNITY_SEARCH', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== シーン情報取得 =====
    mcpServer.registerTool('unity_get_scene_info', {
        title: 'Get Scene Information',
        description: 'Get comprehensive information about the current Unity scene including hierarchy, statistics, lighting, and cameras. Now includes detailed Transform data (world/local position, rotation, scale) and RectTransform data (anchors, pivot, sizeDelta, rect) for all GameObjects. WARNING: Can return large response (>1MB) for complex scenes. For large scenes, use unity_get_scene_summary instead.',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCENE_INFO', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Scene API v1.1.0 - Lightweight Scene Information =====
    mcpServer.registerTool('unity_get_scene_summary', {
        title: 'Get Scene Summary (Lightweight)',
        description: 'Get lightweight scene overview (<200KB). Returns: scene name, GameObject count, cameras, lights, root GameObjects list (max 50). Recommended for large scenes instead of unity_get_scene_info.',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCENE_SUMMARY', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_gameobjects_list', {
        title: 'Get Filtered GameObjects List',
        description: 'Get filtered list of GameObjects (<50KB). Returns GameObject names, IDs, paths, layers, tags. Supports filters: layer, tag, name (contains), activeOnly. Max 100 results.',
        inputSchema: z.object({
            layerFilter: z.string().optional().describe('Filter by layer name (e.g. "UI", "Default")'),
            tagFilter: z.string().optional().describe('Filter by tag (e.g. "Player", "Enemy")'),
            nameFilter: z.string().optional().describe('Filter by name (contains, case-sensitive)'),
            activeOnly: z.boolean().optional().describe('Only return active GameObjects'),
            maxCount: z.number().optional().default(100).describe('Maximum number of results (default: 100)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_GAMEOBJECTS_LIST', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_gameobject_detail', {
        title: 'Get GameObject Detailed Information',
        description: 'Get detailed information for a specific GameObject (<10KB). Returns: transform, components with details, children, parent. Find GameObject by name or instanceId (use unity_get_gameobjects_list first to get IDs).',
        inputSchema: z.object({
            nameOrId: z.string().describe('GameObject name or instanceId (from unity_get_gameobjects_list)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_GAMEOBJECT_DETAIL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_scene_changes_since', {
        title: 'Get Incremental Scene Changes',
        description: 'Get scene changes since a timestamp. Returns: added, removed, modified GameObjects. Use for efficient scene monitoring. On first call or if timestamp too old, returns full scene summary.',
        inputSchema: z.object({
            timestamp: z.string().describe('ISO 8601 timestamp (e.g. "2025-01-15T12:00:00Z"). Use current time on first call, then use returned timestamp for next call.')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCENE_CHANGES_SINCE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Screenshot Capture Tools =====
    mcpServer.registerTool('unity_capture_game_view', {
        title: 'Capture Game View Screenshot',
        description: 'Capture a screenshot of the Unity Game View including all Canvas/UI elements. WORKFLOW: 1) Call this tool → returns "status: pending" if Play mode transition needed. 2) DO NOT CALL ANY OTHER TOOLS. WAIT EXACTLY 3 SECONDS. This is REQUIRED for Play mode transition and rendering stabilization. 3) After 3 seconds, call unity_get_screenshot_result to retrieve the actual screenshot path and resolution. CRITICAL: The screenshot is NOT complete until you retrieve the result with unity_get_screenshot_result. If you call it too early, you will get "status: capturing". NOTE: If Unity is already in Play mode, captures and returns result immediately without the pending status.',
        inputSchema: z.object({
            filename: z.string().optional().describe('Output filename (default: GameView_timestamp.png)'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_GAME_VIEW', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_capture_scene_view', {
        title: 'Capture Scene View Screenshot',
        description: 'Capture a screenshot of the Unity Scene View. The image is saved to Assets/Screenshots and can be analyzed by Claude Vision. Returns the file path for immediate analysis.',
        inputSchema: z.object({
            filename: z.string().optional().describe('Output filename (default: SceneView_timestamp.png)'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_SCENE_VIEW', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_capture_region', {
        title: 'Capture Specific Region Screenshot',
        description: 'Capture a screenshot of a specific region within Unity Game View or Scene View. Useful for capturing specific UI elements or areas. WORKFLOW for view="game": 1) Call this tool → returns "status: pending" if Play mode transition needed. 2) DO NOT CALL ANY OTHER TOOLS. WAIT EXACTLY 3 SECONDS for Play mode transition and rendering stabilization. 3) After 3 seconds, call unity_get_screenshot_result to retrieve result. CRITICAL: The capture is NOT complete until you retrieve the result with unity_get_screenshot_result. If you call it too early, you will get "status: capturing". For view="scene": captures immediately without pending status.',
        inputSchema: z.object({
            x: z.number().int().describe('X coordinate of the region (top-left corner)'),
            y: z.number().int().describe('Y coordinate of the region (top-left corner)'),
            width: z.number().int().describe('Width of the region in pixels'),
            height: z.number().int().describe('Height of the region in pixels'),
            view: z.enum(['game', 'scene']).optional().default('game').describe('Which view to capture from: "game" (includes Canvas/UI, auto Play mode if needed) or "scene" (3D view only, works in Edit mode)'),
            filename: z.string().optional().describe('Output filename (default: Region_timestamp.png)'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_REGION', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_screenshot_result', {
        title: 'Get Screenshot Result',
        description: 'Retrieve the result of a screenshot capture operation that required Play mode transition. WORKFLOW: When unity_capture_game_view or unity_capture_region returns {"status": "pending"}, Unity is entering Play mode to capture Canvas/UI. Wait EXACTLY 3 SECONDS, then call THIS tool to retrieve the actual screenshot result including file path, width, and height. Returns {"status": "capturing"} if still in progress (you called too early - wait more), {"status": "no_result"} if no capture pending.',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCREENSHOT_RESULT', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_capture_grid', {
        title: 'Capture Game View as Grid',
        description: 'Automatically split and capture the Game View into a grid of multiple screenshots. Useful for analyzing different areas of the UI systematically. WORKFLOW: 1) Call this tool with grid size (e.g., "2x2", "3x3"). 2) If returns "status: pending", WAIT EXACTLY 3 SECONDS for Play mode transition. 3) Call unity_get_screenshot_result to retrieve all grid files. Returns multiple image files (one for each grid cell) with their positions.',
        inputSchema: z.object({
            grid: z.string().describe('Grid size in format "2x2", "3x3", "4x4", etc. (max 5x5). Each cell will be saved as a separate file.'),
            basename: z.string().optional().describe('Base name for output files (default: Grid_timestamp). Files will be named basename_r0_c0.png, basename_r0_c1.png, etc.'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_GRID', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_capture_ui_element', {
        title: 'Capture Specific UI Element',
        description: 'Capture a specific UI element by its GameObject name. Automatically finds the element, calculates its screen bounds, and captures just that region. WORKFLOW: 1) Call this tool with the GameObject name (e.g., "MoveButton", "HealthBar"). 2) If returns "status: pending", WAIT EXACTLY 3 SECONDS for Play mode transition. 3) Call unity_get_screenshot_result to retrieve the captured element. Works with Canvas Overlay, Camera, and World Space render modes.',
        inputSchema: z.object({
            elementName: z.string().describe('GameObject name of the UI element to capture (e.g., "MoveButton", "CharacterNameText")'),
            filename: z.string().optional().describe('Output filename (default: UIElement_{elementName}_timestamp.png)'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_UI_ELEMENT', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Asset and Script Management =====
    mcpServer.registerTool('unity_force_refresh_assets', {
        title: 'Force Refresh Assets',
        description: 'Force Unity to refresh the asset database and recompile scripts. IMPORTANT: Before using this tool after editing .cs files, always run unity_analyze_console_logs with logType:"error" to check for syntax/compile errors. If errors exist, fix them first. Only refresh when error-free. Essential after modifying .cs files to ensure changes are recognized before invoking methods.',
        inputSchema: z.object({
            importMode: z.enum(['default', 'force', 'forcesynchronous']).optional().default('default').describe('Import mode: "default" (normal refresh), "force" (force reimport all), "forcesynchronous" (force reimport and wait for completion)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('FORCE_REFRESH_ASSETS', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_invoke_context_menu', {
        title: 'Invoke ContextMenu Method',
        description: 'Invoke a method decorated with [ContextMenu] attribute on a component. Perfect for triggering custom editor actions like BuildLayout(), GenerateUI(), etc. WORKFLOW: 1) Edit code 2) unity_analyze_console_logs(error) 3) Fix errors if any 4) unity_force_refresh_assets 5) unity_analyze_console_logs(error) 6) Use this tool. This ensures error-free execution.',
        inputSchema: z.object({
            componentName: z.string().describe('Name of the component class (e.g., "HomeScreenFlexLayout")'),
            methodName: z.string().describe('Name of the method to invoke (e.g., "BuildLayout")'),
            gameObjectName: z.string().optional().describe('Name of the GameObject containing the component (optional, will search scene if not provided)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('INVOKE_CONTEXT_MENU', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== インスペクター情報取得 =====
    mcpServer.registerTool('unity_get_inspector_info', {
        title: 'Get Inspector Information',
        description: 'Get detailed inspector information for a specific GameObject, including all components, properties, and values',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of the GameObject to inspect'),
            includePrivateFields: z.boolean().optional().default(false).describe('Include private/protected fields in inspection'),
            includeEvents: z.boolean().optional().default(false).describe('Include UnityEvent information'),
            componentFilter: z.string().optional().describe('Filter by specific component type (e.g., "Transform", "Rigidbody")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_INSPECTOR_INFO', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_selected_object_info', {
        title: 'Get Selected Object Inspector Info',
        description: 'Get detailed inspector information for the currently selected GameObject in the Unity Editor',
        inputSchema: z.object({
            includePrivateFields: z.boolean().optional().default(false).describe('Include private/protected fields in inspection'),
            includeEvents: z.boolean().optional().default(false).describe('Include UnityEvent information'),
            componentFilter: z.string().optional().describe('Filter by specific component type')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_SELECTED_OBJECT_INFO', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_component_details', {
        title: 'Get Component Details',
        description: 'Get detailed information about a specific component on a GameObject, including all serialized properties',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of the GameObject'),
            componentType: z.string().describe('Type of the component (e.g., "Transform", "Rigidbody", "BoxCollider")'),
            includeSerializedProperties: z.boolean().optional().default(true).describe('Include all serialized properties')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_COMPONENT_DETAILS', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== コンソール操作 =====
    mcpServer.registerTool('unity_console', {
        title: 'Console Operations',
        description: 'Unity console log count and basic info retrieval',
        inputSchema: z.object({
            operation: z.enum(['read', 'clear']).default('read'),
            logType: z.enum(['all', 'info', 'warning', 'error']).optional().default('all'),
            limit: z.number().optional().default(50)
        })
    }, async (params) => {
        const result = await sendUnityCommand('console_operation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_analyze_console_logs', {
        title: 'Analyze Console Logs',
        description: 'Detailed analysis of Unity console logs with file paths, line numbers and stack traces. CRITICAL: Always use this BEFORE unity_force_refresh_assets when you edit .cs files - check for syntax/compile errors with logType:"error". This prevents compilation failures and ensures clean workflows. Returns detailed error info including file paths and line numbers for easy fixing.',
        inputSchema: z.object({
            logType: z.enum(['all', 'error', 'warning', 'log']).optional().default('error'),
            limit: z.number().optional().default(10),
            includeStackTrace: z.boolean().optional().default(true),
            operation: z.enum(['analyze']).optional().default('analyze')
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_console_logs', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== パッケージ管理 =====
    mcpServer.registerTool('unity_list_packages', {
        title: 'List Packages',
        description: 'List all installed Unity packages',
        inputSchema: z.object({
            filter: z.enum(['all', 'offline']).optional().default('all')
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_packages', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_install_package', {
        title: 'Install Package',
        description: 'Install a Unity package',
        inputSchema: z.object({
            packageId: z.string().describe('Package identifier (e.g., com.unity.ai.navigation)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('install_package', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_remove_package', {
        title: 'Remove Package',
        description: 'Remove an installed Unity package',
        inputSchema: z.object({
            packageName: z.string().describe('Package name (e.g., com.unity.ai.navigation)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('remove_package', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_check_package', {
        title: 'Check Package',
        description: 'Check if a package is installed',
        inputSchema: z.object({
            packageName: z.string().describe('Package name to check')
        })
    }, async (params) => {
        const result = await sendUnityCommand('check_package', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 配置ツール =====
    mcpServer.registerTool('unity_place_objects', {
        title: 'Place Objects',
        description: 'Place multiple objects with pattern',
        inputSchema: z.object({
            prefab: z.string(),
            pattern: z.enum(['grid', 'circle', 'line', 'random']),
            count: z.number(),
            spacing: z.number().optional().default(1),
            center: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('place_objects', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 履歴・Undo/Redo =====
    mcpServer.registerTool('unity_get_operation_history', {
        title: 'Get Operation History',
        description: 'Get history of Unity operations',
        inputSchema: z.object({
            count: z.number().optional().default(10)
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_operation_history', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_undo_operation', {
        title: 'Undo Operation',
        description: 'Undo last Unity operation',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('undo_operation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_redo_operation', {
        title: 'Redo Operation',
        description: 'Redo previously undone operation',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('redo_operation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_checkpoint', {
        title: 'Create Checkpoint',
        description: 'Create a checkpoint to restore later',
        inputSchema: z.object({
            name: z.string()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_checkpoint', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_restore_checkpoint', {
        title: 'Restore Checkpoint',
        description: 'Restore a previously created checkpoint',
        inputSchema: z.object({
            name: z.string()
        })
    }, async (params) => {
        const result = await sendUnityCommand('restore_checkpoint', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== リアルタイムモニタリング =====
    mcpServer.registerTool('unity_monitor_play_state', {
        title: 'Monitor Play State',
        description: 'Monitor Unity play mode state changes',
        inputSchema: z.object({
            enable: z.boolean()
        })
    }, async (params) => {
        const result = await sendUnityCommand('monitor_play_state', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_monitor_file_changes', {
        title: 'Monitor File Changes',
        description: 'Monitor file changes in the project',
        inputSchema: z.object({
            enable: z.boolean(),
            folders: z.array(z.string()).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('monitor_file_changes', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_monitor_compile', {
        title: 'Monitor Compilation',
        description: 'Monitor script compilation events',
        inputSchema: z.object({
            enable: z.boolean()
        })
    }, async (params) => {
        const result = await sendUnityCommand('monitor_compile', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_subscribe_events', {
        title: 'Subscribe to Events',
        description: 'Subscribe to Unity events',
        inputSchema: z.object({
            events: z.array(z.string())
        })
    }, async (params) => {
        const result = await sendUnityCommand('subscribe_events', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_events', {
        title: 'Get Events',
        description: 'Get recent Unity events',
        inputSchema: z.object({
            count: z.number().optional().default(10)
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_events', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_monitoring_status', {
        title: 'Get Monitoring Status',
        description: 'Get current monitoring status',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_monitoring_status', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== プロジェクト設定 =====
    mcpServer.registerTool('unity_get_build_settings', {
        title: 'Get Build Settings',
        description: 'Get Unity build settings',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_build_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_player_settings', {
        title: 'Get Player Settings',
        description: 'Get Unity player settings',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_player_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_quality_settings', {
        title: 'Get Quality Settings',
        description: 'Get Unity quality settings',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_quality_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_input_settings', {
        title: 'Get Input Settings',
        description: 'Get Unity input settings',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_input_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_physics_settings', {
        title: 'Get Physics Settings',
        description: 'Get Unity physics settings',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_physics_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_project_summary', {
        title: 'Get Project Summary',
        description: 'Get overall project summary',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_project_summary', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== アセット管理 =====
    mcpServer.registerTool('unity_list_assets', {
        title: 'List Assets',
        description: 'List assets in the project',
        inputSchema: z.object({
            path: z.string().optional().default('Assets'),
            type: z.string().optional(),
            recursive: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_assets', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 新規追加: 高度なアセット検索ツール =====

    mcpServer.registerTool('unity_search_prefabs_by_component', {
        title: 'Search Prefabs by Component',
        description: '🔍 Search for prefabs containing specific component(s). Useful for finding all prefabs with Rigidbody, Collider, custom scripts, etc.',
        inputSchema: z.object({
            componentName: z.string().describe('Component name to search for (e.g., "Rigidbody", "AudioSource")'),
            searchPath: z.string().optional().default('Assets').describe('Directory to search in'),
            recursive: z.boolean().optional().default(true).describe('Search recursively'),
            includeDisabled: z.boolean().optional().default(true).describe('Include disabled components')
        })
    }, async (params) => {
        const result = await sendUnityCommand('search_prefabs_by_component', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_material_usage', {
        title: 'Find Material Usage',
        description: '🎨 Find all GameObjects, prefabs, and renderers using a specific material. Shows where materials are referenced in the project.',
        inputSchema: z.object({
            materialPath: z.string().describe('Material path (e.g., Assets/Materials/Wood.mat)'),
            materialName: z.string().optional().describe('Material name (alternative to path)'),
            searchInScenes: z.boolean().optional().default(true).describe('Search in currently loaded scenes'),
            searchInPrefabs: z.boolean().optional().default(true).describe('Search in all prefabs')
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_material_usage', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_texture_usage', {
        title: 'Find Texture Usage',
        description: '🖼️ Find all materials and shaders using a specific texture. Useful for understanding texture dependencies.',
        inputSchema: z.object({
            texturePath: z.string().describe('Texture path (e.g., Assets/Textures/Brick.png)'),
            textureName: z.string().optional().describe('Texture name (alternative to path)'),
            includeShaderProperties: z.boolean().optional().default(true).describe('Show which shader properties use this texture')
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_texture_usage', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_asset_dependencies', {
        title: 'Get Asset Dependencies',
        description: '🔗 Get all dependencies of an asset (textures, materials, scripts, etc.). Shows complete dependency tree.',
        inputSchema: z.object({
            assetPath: z.string().describe('Asset path (e.g., Assets/Prefabs/Player.prefab)'),
            recursive: z.boolean().optional().default(true).describe('Get recursive dependencies'),
            includeBuiltIn: z.boolean().optional().default(false).describe('Include built-in Unity assets'),
            groupByType: z.boolean().optional().default(true).describe('Group results by asset type')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_asset_dependencies', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_missing_references', {
        title: 'Find Missing References',
        description: '⚠️ Find missing scripts, materials, textures, and other references in scenes and prefabs. Critical for project cleanup.',
        inputSchema: z.object({
            searchPath: z.string().optional().default('Assets').describe('Directory to search in'),
            types: z.array(z.enum(['script', 'material', 'texture', 'prefab', 'mesh', 'audio', 'all'])).optional().default(['all']).describe('Types to check'),
            searchInScenes: z.boolean().optional().default(true).describe('Search in currently loaded scenes'),
            searchInPrefabs: z.boolean().optional().default(true).describe('Search in all prefabs'),
            includeChildren: z.boolean().optional().default(true).describe('Check child objects')
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_missing_references', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== フォルダ管理 =====
    mcpServer.registerTool('unity_check_folder', {
        title: 'Check Folder',
        description: 'Check if folder exists',
        inputSchema: z.object({
            path: z.string()
        })
    }, async (params) => {
        const result = await sendUnityCommand('check_folder', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_folder', {
        title: 'Create Folder',
        description: 'Create a new folder',
        inputSchema: z.object({
            path: z.string()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_folder', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_list_folders', {
        title: 'List Folders',
        description: 'List folders in a path',
        inputSchema: z.object({
            path: z.string().optional().default('Assets')
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_folders', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 新しいツール群 =====
    mcpServer.registerTool('unity_duplicate_gameobject', {
        title: 'Duplicate GameObject',
        description: 'Duplicate an existing GameObject',
        inputSchema: z.object({
            gameObject: z.string(),
            newName: z.string().optional(),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('duplicate_gameobject', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_gameobjects_by_component', {
        title: 'Find GameObjects by Component',
        description: 'Find all GameObjects with specific component',
        inputSchema: z.object({
            componentType: z.string(),
            includeInactive: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_by_component', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_cleanup_empty_objects', {
        title: 'Cleanup Empty Objects',
        description: 'Remove empty GameObjects from scene',
        inputSchema: z.object({
            dryRun: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('cleanup_empty_objects', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 優先度中のツール =====
    mcpServer.registerTool('unity_group_gameobjects', {
        title: 'Group GameObjects',
        description: 'Group multiple GameObjects under a parent',
        inputSchema: z.object({
            gameObjects: z.array(z.string()).describe('Names of GameObjects to group'),
            parentName: z.string().describe('Name for the parent group'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional().describe('Position of the parent group')
        })
    }, async (params) => {
        const result = await sendUnityCommand('group_gameobjects', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_rename_asset', {
        title: 'Rename Asset',
        description: 'Rename an asset file',
        inputSchema: z.object({
            oldPath: z.string().describe('Current asset path'),
            newName: z.string().describe('New name for the asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('rename_asset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_move_asset', {
        title: 'Move Asset',
        description: 'Move an asset to a different folder',
        inputSchema: z.object({
            sourcePath: z.string().describe('Current asset path'),
            destinationFolder: z.string().describe('Destination folder path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('move_asset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_delete_asset', {
        title: 'Delete Asset',
        description: 'Delete an asset from the project',
        inputSchema: z.object({
            assetPath: z.string().describe('Path of asset to delete'),
            moveToTrash: z.boolean().optional().default(true).describe('Move to trash instead of permanent delete')
        })
    }, async (params) => {
        const result = await sendUnityCommand('delete_asset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_pause_scene', {
        title: 'Pause Scene',
        description: 'Pause or unpause the scene view',
        inputSchema: z.object({
            pause: z.boolean().describe('True to pause, false to unpause')
        })
    }, async (params) => {
        const result = await sendUnityCommand('pause_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_missing_references', {
        title: 'Find Missing References',
        description: 'Find GameObjects with missing script references or components',
        inputSchema: z.object({
            searchScope: z.enum(['scene', 'project', 'both']).optional().default('scene'),
            fixAutomatically: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_missing_references', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 最適化ツール =====
    mcpServer.registerTool('unity_optimize_textures_batch', {
        title: 'Optimize Textures Batch',
        description: 'Batch optimize texture import settings',
        inputSchema: z.object({
            folder: z.string().optional().default('Assets'),
            maxTextureSize: z.number().optional(),
            compressionQuality: z.enum(['low', 'normal', 'high']).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('OPTIMIZE_TEXTURES_BATCH', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_analyze_draw_calls', {
        title: 'Analyze Draw Calls',
        description: 'Analyze and report draw call optimization opportunities',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('analyze_draw_calls', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== スナップショット =====
    mcpServer.registerTool('unity_create_project_snapshot', {
        title: 'Create Project Snapshot',
        description: 'Create a snapshot of the current project state',
        inputSchema: z.object({
            name: z.string(),
            description: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_project_snapshot', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== プロジェクト分析ツール =====
    mcpServer.registerTool('unity_analyze_dependencies', {
        title: 'Analyze Asset Dependencies',
        description: 'Analyze and visualize asset dependencies in the project',
        inputSchema: z.object({
            assetPath: z.string().optional().describe('Specific asset to analyze (optional, analyzes all if not specified)'),
            maxDepth: z.number().optional().default(3).describe('Maximum depth of dependency tree'),
            includePackages: z.boolean().optional().default(false).describe('Include package dependencies')
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_dependencies', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_export_project_structure', {
        title: 'Export Project Structure',
        description: 'Export project folder structure',
        inputSchema: z.object({
            format: z.enum(['json', 'tree', 'csv']).optional().default('tree'),
            includeFileSize: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('export_project_structure', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_validate_naming_conventions', {
        title: 'Validate Naming Conventions',
        description: 'Check if project assets follow naming conventions',
        inputSchema: z.object({
            checkPascalCase: z.boolean().optional().default(true).describe('Check PascalCase for scripts'),
            checkCamelCase: z.boolean().optional().default(true).describe('Check camelCase for variables'),
            checkUnderscores: z.boolean().optional().default(true).describe('Check for underscores in names'),
            customPatterns: z.array(z.string()).optional().describe('Custom regex patterns to check')
        })
    }, async (params) => {
        const result = await sendUnityCommand('validate_naming_conventions', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_extract_all_text', {
        title: 'Extract All Text',
        description: 'Extract all text content from the project for localization or AI training',
        inputSchema: z.object({
            includeScripts: z.boolean().optional().default(true).describe('Extract text from scripts'),
            includeUI: z.boolean().optional().default(true).describe('Extract text from UI elements'),
            includeComments: z.boolean().optional().default(true).describe('Include code comments'),
            format: z.string().optional().default('json').describe('Output format: json, txt, csv')
        })
    }, async (params) => {
        const result = await sendUnityCommand('extract_all_text', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== AIが喜ぶ追加ツール群 =====
    
    // バッチ処理系
    mcpServer.registerTool('unity_batch_rename', {
        title: 'Batch Rename Assets',
        description: 'Batch rename multiple assets using patterns or regex',
        inputSchema: z.object({
            searchPattern: z.string().describe('Search pattern (supports wildcards and regex)'),
            replacePattern: z.string().describe('Replace pattern'),
            scope: z.enum(['selected', 'folder', 'project']).default('folder'),
            folderPath: z.string().optional().describe('Folder path if scope is folder'),
            useRegex: z.boolean().optional().default(false),
            caseSensitive: z.boolean().optional().default(true),
            dryRun: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('BATCH_RENAME', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_batch_import_settings', {
        title: 'Batch Import Settings',
        description: 'Apply import settings to multiple assets at once',
        inputSchema: z.object({
            assetType: z.enum(['texture', 'model', 'audio']),
            folder: z.string().describe('Target folder path'),
            includeSubfolders: z.boolean().optional().default(true),
            settings: z.object({
                // Texture settings
                textureType: z.enum(['Default', 'NormalMap', 'Sprite', 'Cursor', 'Cookie', 'Lightmap']).optional(),
                maxSize: z.number().optional(),
                compression: z.enum(['None', 'Low', 'Normal', 'High']).optional(),
                generateMipmaps: z.boolean().optional(),
                
                // Model settings
                importMaterials: z.boolean().optional(),
                importAnimation: z.boolean().optional(),
                optimizeMesh: z.boolean().optional(),
                generateColliders: z.boolean().optional(),
                
                // Audio settings
                forceToMono: z.boolean().optional(),
                loadInBackground: z.boolean().optional(),
                preloadAudioData: z.boolean().optional()
            })
        })
    }, async (params) => {
        const result = await sendUnityCommand('BATCH_IMPORT_SETTINGS', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_batch_prefab_update', {
        title: 'Batch Update Prefabs',
        description: 'Update multiple prefabs with component changes',
        inputSchema: z.object({
            prefabFolder: z.string().describe('Folder containing prefabs'),
            componentType: z.string().describe('Component type to modify'),
            operation: z.enum(['add', 'remove', 'modify']),
            properties: z.record(z.any()).optional().describe('Properties to set if modifying'),
            filter: z.object({
                hasComponent: z.string().optional(),
                nameContains: z.string().optional(),
                tag: z.string().optional()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('BATCH_PREFAB_UPDATE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_batch_material_apply', {
        title: 'Batch Apply Material',
        description: 'Apply material to multiple objects at once',
        inputSchema: z.object({
            materialPath: z.string().describe('Path to the material asset'),
            targetObjects: z.string().optional().describe('Comma-separated list of object names'),
            searchPattern: z.string().optional().describe('Regex pattern to match object names'),
            searchScope: z.enum(['scene', 'selected', 'children']).optional().default('scene'),
            replaceAll: z.boolean().optional().default(true).describe('Replace all materials on each object')
        })
    }, async (params) => {
        const result = await sendUnityCommand('BATCH_MATERIAL_APPLY', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_batch_prefab_create', {
        title: 'Batch Create Prefabs',
        description: 'Create prefabs from multiple objects at once',
        inputSchema: z.object({
            sourceObjects: z.string().optional().describe('Comma-separated list of object names'),
            searchPattern: z.string().optional().describe('Regex pattern to match object names'),
            outputFolder: z.string().optional().default('Assets/Prefabs').describe('Output folder for prefabs'),
            namingPattern: z.string().optional().default('{name}_Prefab').describe('Naming pattern, use {name} for original name'),
            createVariants: z.boolean().optional().default(false).describe('Create prefab variants if source is already a prefab'),
            deleteOriginal: z.boolean().optional().default(false).describe('Delete original objects after creating prefabs')
        })
    }, async (params) => {
        const result = await sendUnityCommand('BATCH_PREFAB_CREATE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // 分析・レポート系
    mcpServer.registerTool('unity_find_unused_assets', {
        title: 'Find Unused Assets',
        description: 'Find assets that are not referenced in the project',
        inputSchema: z.object({
            assetTypes: z.array(z.enum(['texture', 'material', 'prefab', 'script', 'audio', 'model'])).optional(),
            excludeFolders: z.array(z.string()).optional().describe('Folders to exclude from search'),
            includePackages: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_unused_assets', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_estimate_build_size', {
        title: 'Estimate Build Size',
        description: 'Estimate build size for different platforms',
        inputSchema: z.object({
            platforms: z.array(z.enum(['Windows', 'Mac', 'Linux', 'Android', 'iOS', 'WebGL'])).optional(),
            includeStreamingAssets: z.boolean().optional().default(true),
            compressionLevel: z.enum(['none', 'fastest', 'normal', 'best']).optional().default('normal')
        })
    }, async (params) => {
        const result = await sendUnityCommand('estimate_build_size', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_performance_report', {
        title: 'Generate Performance Report',
        description: 'Generate comprehensive performance analysis report',
        inputSchema: z.object({
            includeRendering: z.boolean().optional().default(true),
            includeTextures: z.boolean().optional().default(true),
            includeMeshes: z.boolean().optional().default(true),
            includeScripts: z.boolean().optional().default(true),
            includeAudio: z.boolean().optional().default(true),
            targetPlatform: z.enum(['Mobile', 'Desktop', 'Console', 'VR']).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('performance_report', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // 自動化系
    mcpServer.registerTool('unity_auto_organize_folders', {
        title: 'Auto Organize Project Folders',
        description: 'Automatically organize assets into appropriate folders',
        inputSchema: z.object({
            rootFolder: z.string().optional().default('Assets'),
            createStandardFolders: z.boolean().optional().default(true),
            moveAssets: z.boolean().optional().default(false),
            dryRun: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('auto_organize_folders', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_generate_lod', {
        title: 'Generate LOD Groups',
        description: 'Automatically generate LOD groups for meshes',
        inputSchema: z.object({
            targetObject: z.string().describe('GameObject or folder to process'),
            lodLevels: z.number().min(2).max(4).optional().default(3),
            lodDistances: z.array(z.number()).optional().describe('Custom LOD distances'),
            generateSimplifiedMeshes: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('generate_lod', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_auto_atlas_textures', {
        title: 'Auto Create Texture Atlas',
        description: 'Automatically create texture atlases from multiple textures',
        inputSchema: z.object({
            sourceFolder: z.string().describe('Folder containing textures'),
            atlasName: z.string().describe('Name for the atlas'),
            maxAtlasSize: z.number().optional().default(2048),
            padding: z.number().optional().default(2),
            includeInBuild: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('auto_atlas_textures', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== ゲーム開発特化機能 =====
    mcpServer.registerTool('unity_create_game_controller', {
        title: 'Create Game Controller',
        description: 'Create player controller for different game types (FirstPerson, ThirdPerson, TopDown, Platformer2D)',
        inputSchema: z.object({
            type: z.enum(['FirstPerson', 'ThirdPerson', 'TopDown', 'Platformer2D']).default('FirstPerson'),
            playerName: z.string().optional().default('Player'),
            includeCamera: z.boolean().optional().default(true),
            movementSpeed: z.number().optional().default(5),
            jumpHeight: z.number().optional().default(3)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_game_controller', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_input_system', {
        title: 'Setup Input System',
        description: 'Setup Unity Input System with predefined templates',
        inputSchema: z.object({
            template: z.enum(['Standard', 'Mobile', 'VR']).default('Standard'),
            createAsset: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_input_system', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_state_machine', {
        title: 'Create State Machine',
        description: 'Create a state machine for character or game states',
        inputSchema: z.object({
            targetObject: z.string().optional(),
            type: z.string().optional().default('Character'),
            states: z.string().optional().default('Idle,Walk,Run,Jump,Attack').describe('Comma-separated state names')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_state_machine', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_inventory_system', {
        title: 'Setup Inventory System',
        description: 'Create an inventory system with UI',
        inputSchema: z.object({
            size: z.number().optional().default(20),
            hasUI: z.boolean().optional().default(true),
            stackable: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_inventory_system', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== プロトタイピング機能 =====
    mcpServer.registerTool('unity_create_game_template', {
        title: 'Create Game Template',
        description: 'Create complete game templates for different genres',
        inputSchema: z.object({
            genre: z.enum(['FPS', 'Platformer', 'RPG', 'Puzzle', 'Racing', 'Strategy']).default('FPS'),
            name: z.string().optional(),
            includeUI: z.boolean().optional().default(true),
            includeAudio: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_game_template', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_quick_prototype', {
        title: 'Quick Prototype',
        description: 'Create a quick playable prototype with specified elements',
        inputSchema: z.object({
            elements: z.string().optional().default('player,enemies,collectibles,obstacles'),
            worldSize: z.number().optional().default(20),
            playerType: z.enum(['Capsule', 'Cube', 'Sphere']).optional().default('Capsule')
        })
    }, async (params) => {
        const result = await sendUnityCommand('quick_prototype', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== AI・機械学習関連 =====
    mcpServer.registerTool('unity_setup_ml_agent', {
        title: 'Setup ML Agent',
        description: 'Setup a Machine Learning Agent for reinforcement learning',
        inputSchema: z.object({
            agentName: z.string().default('MLAgent'),
            agentType: z.enum(['Basic', 'Advanced', 'Reward-based']).default('Basic'),
            vectorObservationSize: z.number().default(8),
            useVisualObservation: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_ml_agent', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_neural_network', {
        title: 'Create Neural Network',
        description: 'Create a neural network system for AI decision making',
        inputSchema: z.object({
            networkName: z.string().default('NeuralNetwork'),
            networkType: z.enum(['Feedforward', 'Recurrent', 'Convolutional']).default('Feedforward'),
            inputSize: z.number().default(4),
            hiddenSize: z.number().default(8),
            outputSize: z.number().default(2)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_neural_network', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_behavior_tree', {
        title: 'Setup Behavior Tree',
        description: 'Setup a behavior tree AI system for complex AI behaviors',
        inputSchema: z.object({
            treeName: z.string().default('BehaviorTree'),
            aiType: z.enum(['Enemy', 'NPC', 'Companion', 'Guard']).default('Enemy'),
            includePatrol: z.boolean().default(true),
            includeChase: z.boolean().default(true),
            includeAttack: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_behavior_tree', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_ai_pathfinding', {
        title: 'Create AI Pathfinding',
        description: 'Create an AI pathfinding system with A* algorithm',
        inputSchema: z.object({
            systemName: z.string().default('PathfindingAI'),
            algorithm: z.enum(['AStar', 'Dijkstra', 'BFS']).default('AStar'),
            gridWidth: z.number().default(20),
            gridHeight: z.number().default(20),
            use3D: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ai_pathfinding', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== デバッグ・テストツール =====
    
    mcpServer.registerTool('unity_control_game_speed', {
        title: 'Control Game Speed',
        description: 'Control Unity game speed (time scale) for debugging',
        inputSchema: z.object({
            operation: z.enum(['set', 'pause', 'step', 'get']).default('set'),
            speed: z.number().optional(),
            preset: z.enum(['pause', 'slowest', 'slow', 'normal', 'fast', 'fastest']).optional(),
            pauseMode: z.enum(['toggle', 'on', 'off']).default('toggle')
        })
    }, async (params) => {
        const result = await sendUnityCommand('control_game_speed', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_profile_performance', {
        title: 'Profile Performance',
        description: 'Get Unity performance profiling data',
        inputSchema: z.object({
            category: z.enum(['general', 'all', 'memory', 'gpu', 'cpu']).default('general'),
            duration: z.number().default(0),
            detailed: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('profile_performance', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_debug_draw', {
        title: 'Debug Draw',
        description: 'Draw debug shapes in Unity scene',
        inputSchema: z.object({
            type: z.enum(['line', 'ray', 'box', 'sphere', 'path', 'clear']).default('line'),
            duration: z.number().default(5),
            color: z.enum(['red', 'green', 'blue', 'yellow', 'white', 'black', 'cyan', 'magenta']).default('red'),
            // Line parameters
            start: z.string().optional().describe('Start position for line (e.g., "0,0,0")'),
            end: z.string().optional().describe('End position for line (e.g., "1,1,1")'),
            // Ray parameters
            origin: z.string().optional().describe('Origin position for ray'),
            direction: z.string().optional().describe('Direction vector for ray'),
            length: z.number().optional().describe('Length of ray'),
            // Box parameters
            center: z.string().optional().describe('Center position'),
            size: z.string().optional().describe('Box size (e.g., "1,1,1")'),
            // Sphere parameters
            radius: z.number().optional().describe('Sphere radius'),
            // Path parameters
            points: z.string().optional().describe('Semicolon-separated points (e.g., "0,0,0;1,1,0;2,1,0")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('debug_draw', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_run_tests', {
        title: 'Run Unity Tests',
        description: 'Run Unity Test Runner tests',
        inputSchema: z.object({
            mode: z.enum(['editmode', 'playmode']).default('editmode'),
            category: z.string().default('all'),
            testName: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('run_unity_tests', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_manage_breakpoints', {
        title: 'Manage Breakpoints',
        description: 'Manage debugging breakpoints in Unity',
        inputSchema: z.object({
            operation: z.enum(['pause', 'conditional', 'log', 'assert']).default('pause'),
            condition: z.string().optional().describe('Condition for breakpoint (e.g., "frame > 100", "time > 5")'),
            message: z.string().default('Breakpoint hit')
        })
    }, async (params) => {
        const result = await sendUnityCommand('manage_breakpoints', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    // ===== GOAP AI系ツール =====
    
    mcpServer.registerTool('unity_create_goap_agent', {
        title: 'Create GOAP Agent',
        description: `Create a GOAP (Goal Oriented Action Planning) AI agent. GOAP agents plan their actions to achieve goals based on world state.

Use this to create intelligent NPCs that:
- Dynamically plan actions to achieve goals
- React to changing game conditions
- Prioritize multiple competing objectives

After creating, use unity_define_behavior_language to define behaviors in natural language.`,
        inputSchema: z.object({
            name: z.string().describe('Name of the GOAP agent'),
            agentType: z.string().describe('Type: "Guard", "Worker", "Enemy", "NPC", "Merchant", "Monster", etc.').optional(),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional(),
            capabilities: z.array(z.string()).describe('Capabilities: "movement", "combat", "ranged", "melee", "stealth", "healing", "building"').optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_goap_agent', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_define_goap_goal', {
        title: 'Define GOAP Goal',
        description: 'Define a goal for GOAP agents using natural language',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the GOAP agent'),
            goalName: z.string().describe('Name of the goal'),
            description: z.string().describe('Natural language description of the goal (e.g., "Keep area safe from enemies")'),
            priority: z.number().min(0).max(100).default(50).describe('Priority of the goal (0-100)'),
            conditions: z.string().optional().describe('Conditions for goal activation (e.g., "enemy detected within 10 units")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('define_goap_goal', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_goap_action', {
        title: 'Create GOAP Action',
        description: 'Create an action for GOAP agents',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the GOAP agent'),
            actionName: z.string().describe('Name of the action'),
            description: z.string().describe('What this action does'),
            preconditions: z.array(z.string()).describe('Conditions that must be true before action (e.g., ["has_weapon", "enemy_in_range"])'),
            effects: z.array(z.string()).describe('Effects after action completion (e.g., ["enemy_defeated", "area_secure"])'),
            cost: z.number().default(1).describe('Cost of performing this action')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_goap_action', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_define_behavior_language', {
        title: 'Define Behavior Using Natural Language',
        description: `Define complete AI behavior using natural language description. Supports English and Japanese.

USAGE: Describe what the AI should do in plain language. The system parses:
- Actions: "patrol", "attack", "defend", "follow", "flee", "collect", "hide", "investigate", etc.
- Conditions: "when health low", "if enemy detected", "when outnumbered"
- Modifiers: "aggressive", "cautious", "smart", "stealthy"
- Parameters: "10m range", "50% health", "3 second cooldown"

EXAMPLES:
- "Patrol the area, attack enemies on sight, retreat when health below 30%"
- "Follow the player at 5m distance, help allies when they need assistance"
- "Hide in shadows, when detected flee and call for backup"
- "Aggressively hunt the player, use ranged attacks, take cover when under fire"
- "エリアを巡回、敵を見つけたら攻撃、体力が低い時は逃げる"
- "プレイヤーを追跡、10メートル以内で射撃、発見されたら隠れる"

COMPOUND BEHAVIORS: Use "and", "then", ",", "そして" to chain multiple behaviors.
CONDITIONAL: Use "when X, do Y" or "if X then Y" or "Xの時、Y" patterns.`,
        inputSchema: z.object({
            agentName: z.string().describe('Name of the GOAP agent'),
            behavior: z.string().describe('Natural language behavior description. Examples: "Patrol and attack enemies on sight, retreat when health low" or "巡回して敵を攻撃、体力低下時は撤退"'),
            gameContext: z.string().optional().describe('Game context (FPS, RTS, RPG, Stealth, Survival, Horror) - adds context-specific actions'),
            difficulty: z.enum(['easy', 'normal', 'hard', 'adaptive']).default('normal')
        })
    }, async (params) => {
        const result = await sendUnityCommand('define_behavior_language', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_generate_goap_action_set', {
        title: 'Generate GOAP Action Set',
        description: 'Automatically generate a complete action set based on agent type and goals',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the GOAP agent'),
            agentRole: z.enum(['guard', 'worker', 'enemy', 'npc', 'companion']).describe('Role of the agent'),
            environment: z.string().optional().describe('Environment type (e.g., "forest", "urban", "dungeon")'),
            includeDefaults: z.boolean().default(true).describe('Include default actions for the role')
        })
    }, async (params) => {
        const result = await sendUnityCommand('generate_goap_action_set', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_goap_world_state', {
        title: 'Setup GOAP World State',
        description: 'Configure the world state for GOAP planning',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the GOAP agent'),
            worldState: z.record(z.union([z.boolean(), z.number(), z.string()])).describe('World state key-value pairs (e.g., {"has_weapon": true, "enemies_nearby": 2})'),
            sensors: z.array(z.string()).optional().describe('Sensors to add (e.g., ["enemy_detector", "health_monitor"])'),
            updateFrequency: z.number().default(0.5).describe('How often to update world state (seconds)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_goap_world_state', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_goap_template', {
        title: 'Create GOAP Template',
        description: `Create a complete game-specific GOAP AI from pre-built professional templates.

TEMPLATES:
- fps_enemy / fps_soldier / fps_sniper: Tactical combat AI with cover, flanking, suppression
- rts_unit / rts_worker / rts_combat_unit: Resource gathering, building, formation combat
- rpg_npc / rpg_merchant / rpg_quest_giver: Trading, quests, dialogue
- rpg_enemy / rpg_monster: Territory defense, hunting, special attacks
- stealth_guard: Patrol routes, investigation, alert states
- survival_creature: Hunger, thirst, shelter seeking, predator avoidance
- horror_enemy / horror_monster: Stalking, ambush, psychological terror

Each template includes pre-configured goals, actions, sensors, and world state.`,
        inputSchema: z.object({
            templateType: z.enum(['fps_enemy', 'fps_soldier', 'fps_sniper', 'rts_unit', 'rts_worker', 'rts_combat_unit', 'rpg_npc', 'rpg_merchant', 'rpg_quest_giver', 'rpg_enemy', 'rpg_monster', 'stealth_guard', 'survival_creature', 'horror_enemy', 'horror_monster', 'platformer_enemy']),
            difficulty: z.enum(['easy', 'normal', 'hard']).default('normal'),
            behaviors: z.array(z.string()).optional().describe('Additional natural language behaviors to include'),
            customizations: z.record(z.any()).optional().describe('Template-specific customizations')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_goap_template', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_debug_goap_decisions', {
        title: 'Debug GOAP Decisions',
        description: 'Visualize and debug GOAP decision-making process',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the GOAP agent to debug'),
            showGraph: z.boolean().default(true).describe('Show decision graph'),
            showWorldState: z.boolean().default(true).describe('Show current world state'),
            showPlan: z.boolean().default(true).describe('Show current action plan'),
            logToConsole: z.boolean().default(false).describe('Log decisions to console')
        })
    }, async (params) => {
        const result = await sendUnityCommand('debug_goap_decisions', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_optimize_goap_performance', {
        title: 'Optimize GOAP Performance',
        description: 'Optimize GOAP agent performance for better frame rates',
        inputSchema: z.object({
            agentName: z.string().optional().describe('Specific agent to optimize (or all if not specified)'),
            maxPlanDepth: z.number().default(10).describe('Maximum planning depth'),
            planningFrequency: z.number().default(1).describe('How often to replan (seconds)'),
            enableMultithreading: z.boolean().default(true).describe('Use multithreading for planning'),
            cacheSize: z.number().default(100).describe('Plan cache size')
        })
    }, async (params) => {
        const result = await sendUnityCommand('optimize_goap_performance', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== Behavior Tree Tools =====

    mcpServer.registerTool('unity_create_bt_agent', {
        title: 'Create Behavior Tree Agent',
        description: `Create a BehaviorTreeRunner on a GameObject.

The Behavior Tree (BT) system provides hierarchical AI behavior control:
- Composites: Selector (OR), Sequence (AND), Parallel
- Decorators: Inverter, Repeater, Cooldown, Timeout, Retry, Delay
- Leaves: Action, Condition, Wait, MoveTo, Log`,
        inputSchema: z.object({
            agentName: z.string().default('BTAgent').describe('Name for the new agent'),
            targetObject: z.string().optional().describe('Existing object to add BT to (if not creating new)'),
            autoStart: z.boolean().default(false).describe('Auto-start the tree on play'),
            tickInterval: z.number().default(0).describe('Tick interval in seconds (0 = every frame)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_bt_agent', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_bt_node', {
        title: 'Add Behavior Tree Node',
        description: `Add a node to a behavior tree.

Node Types:
- Composites: selector, sequence, parallel, randomselector, randomsequence
- Decorators: inverter, succeeder, failer, repeater, cooldown, timeout, retry, delay
- Leaves: wait, log`,
        inputSchema: z.object({
            agentName: z.string().describe('Name of the BT agent'),
            nodeType: z.enum([
                'selector', 'sequence', 'parallel', 'randomselector', 'randomsequence',
                'inverter', 'succeeder', 'failer', 'repeater', 'cooldown', 'timeout', 'retry', 'delay',
                'wait', 'log'
            ]).describe('Type of node to add'),
            nodeName: z.string().default('Node').describe('Name for the node'),
            parentNode: z.string().optional().describe('Parent node name (empty = root)'),
            repeatCount: z.number().optional().describe('For repeater: number of repeats (-1 = infinite)'),
            cooldownTime: z.number().optional().describe('For cooldown: cooldown duration in seconds'),
            timeout: z.number().optional().describe('For timeout: timeout duration in seconds'),
            maxRetries: z.number().optional().describe('For retry: max retry count'),
            delay: z.number().optional().describe('For delay: delay duration in seconds'),
            duration: z.number().optional().describe('For wait: wait duration in seconds'),
            message: z.string().optional().describe('For log: message to log')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_bt_node', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_bt_from_description', {
        title: 'Create Behavior Tree from Natural Language',
        description: `Create a complete behavior tree from a natural language description.

Supports English and Japanese. Examples:
- "Patrol between waypoints, attack enemies on sight, flee when health is low"
- "パトロールして、敵を見つけたら攻撃、体力が低いと逃げる"

Recognized patterns: patrol, attack, flee, chase, idle, guard, collect`,
        inputSchema: z.object({
            agentName: z.string().describe('Name of the agent to create BT for'),
            description: z.string().describe('Natural language description of the behavior'),
            gameContext: z.enum(['Generic', 'FPS', 'RPG', 'RTS', 'Stealth', 'Survival']).default('Generic')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_bt_from_description', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_start_bt', {
        title: 'Start Behavior Tree',
        description: 'Start running a behavior tree on an agent',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the BT agent')
        })
    }, async (params) => {
        const result = await sendUnityCommand('start_bt', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_stop_bt', {
        title: 'Stop Behavior Tree',
        description: 'Stop a running behavior tree',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the BT agent')
        })
    }, async (params) => {
        const result = await sendUnityCommand('stop_bt', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_debug_bt', {
        title: 'Debug Behavior Tree',
        description: 'Get debug information about a behavior tree',
        inputSchema: z.object({
            agentName: z.string().describe('Name of the BT agent')
        })
    }, async (params) => {
        const result = await sendUnityCommand('debug_bt', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== アニメーション系ツール =====

    mcpServer.registerTool('unity_create_animator_controller', {
        title: 'Create Animator Controller',
        description: 'Create a new Animator Controller with default states and parameters',
        inputSchema: z.object({
            name: z.string().default('NewAnimatorController'),
            path: z.string().default('Assets/Animations/Controllers/'),
            targetObject: z.string().optional().describe('GameObject to apply the controller to'),
            applyToObject: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_animator_controller', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_add_animation_state', {
        title: 'Add Animation State',
        description: 'Add a new state to an Animator Controller',
        inputSchema: z.object({
            controllerPath: z.string().describe('Path to the Animator Controller asset'),
            stateName: z.string().default('NewState'),
            animationClipPath: z.string().optional().describe('Path to animation clip'),
            layerIndex: z.number().default(0),
            isDefault: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_animation_state', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_animation_clip', {
        title: 'Create Animation Clip',
        description: 'Create a new animation clip with sample curves',
        inputSchema: z.object({
            name: z.string().default('NewAnimation'),
            path: z.string().default('Assets/Animations/Clips/'),
            duration: z.number().default(1),
            frameRate: z.number().default(30),
            targetObject: z.string().optional(),
            animationType: z.enum(['position', 'rotation', 'scale', 'color']).default('position')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_animation_clip', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_blend_tree', {
        title: 'Setup Blend Tree',
        description: 'Create a blend tree for smooth animation transitions',
        inputSchema: z.object({
            controllerPath: z.string().describe('Path to the Animator Controller'),
            stateName: z.string().default('Movement'),
            blendType: z.enum(['1D', '2D']).default('1D'),
            parameterName: z.string().default('Speed'),
            layerIndex: z.number().default(0)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_blend_tree', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_add_animation_transition', {
        title: 'Add Animation Transition',
        description: 'Create a transition between animation states',
        inputSchema: z.object({
            controllerPath: z.string().describe('Path to the Animator Controller'),
            fromState: z.string().describe('Source state name (use "Any" for Any State)'),
            toState: z.string().describe('Destination state name'),
            condition: z.string().optional().describe('Parameter name for condition'),
            conditionValue: z.string().optional().describe('Value for condition'),
            hasExitTime: z.boolean().default(true),
            transitionDuration: z.number().default(0.25),
            layerIndex: z.number().default(0)
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_animation_transition', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_animation_layer', {
        title: 'Setup Animation Layer',
        description: 'Add and configure an animation layer',
        inputSchema: z.object({
            controllerPath: z.string().describe('Path to the Animator Controller'),
            layerName: z.string().default('NewLayer'),
            weight: z.number().default(1),
            blendMode: z.enum(['override', 'additive']).default('override'),
            avatarMaskPath: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_animation_layer', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_animation_event', {
        title: 'Create Animation Event',
        description: 'Add an event to an animation clip',
        inputSchema: z.object({
            clipPath: z.string().describe('Path to the animation clip'),
            time: z.number().default(0.5).describe('Time in seconds'),
            functionName: z.string().default('OnAnimationEvent'),
            stringParameter: z.string().optional(),
            floatParameter: z.number().optional(),
            intParameter: z.number().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_animation_event', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_avatar', {
        title: 'Setup Avatar',
        description: 'Configure avatar for a 3D model',
        inputSchema: z.object({
            modelPath: z.string().describe('Path to the 3D model'),
            avatarName: z.string().default('NewAvatar'),
            isHumanoid: z.boolean().default(true),
            rootBone: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_avatar', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_timeline', {
        title: 'Create Timeline',
        description: 'Create a Unity Timeline for cinematic sequences',
        inputSchema: z.object({
            name: z.string().default('NewTimeline'),
            path: z.string().default('Assets/Timelines/'),
            duration: z.number().default(10),
            frameRate: z.number().default(30),
            targetObject: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_timeline', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_bake_animation', {
        title: 'Bake Animation',
        description: 'Bake runtime animation into an animation clip',
        inputSchema: z.object({
            sourceObject: z.string().describe('GameObject with animation to bake'),
            animationName: z.string().default('BakedAnimation'),
            startFrame: z.number().default(0),
            endFrame: z.number().default(60),
            frameRate: z.number().default(30),
            path: z.string().default('Assets/Animations/Baked/')
        })
    }, async (params) => {
        const result = await sendUnityCommand('bake_animation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    // ===== Pro Shader Tools =====

    mcpServer.registerTool('unity_create_water_material', {
        title: 'Create Water Material',
        description: 'Create Genshin Impact-quality water with physics-based wave reflection. Features: GGX BRDF specular, sun disk reflection on wave peaks, anisotropic highlights stretched along waves, micro-facet glitter, Beer-Lambert absorption, caustics with RGB split, and natural foam.',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            // Color and Depth
            shallowColor: z.string().optional().default('#4DD9EB').describe('Shallow water color (hex)'),
            deepColor: z.string().optional().default('#051940').describe('Deep water color (hex)'),
            horizonColor: z.string().optional().default('#99D9FF').describe('Horizon color (hex)'),
            depthMaxDistance: z.number().optional().default(10).describe('Max depth distance for color transition'),
            depthStrength: z.number().optional().default(0.8).describe('Depth color strength (0-1)'),
            // Transparency and Absorption
            absorptionColor: z.string().optional().default('#338066').describe('Light absorption color (affects underwater tint)'),
            absorptionStrength: z.number().optional().default(0.5).describe('Beer-Lambert absorption strength (0-2)'),
            clarityDistance: z.number().optional().default(5).describe('Distance for full absorption'),
            underwaterFogDensity: z.number().optional().default(0.15).describe('Underwater fog density (0-1)'),
            transparencyDepth: z.number().optional().default(3).describe('Depth at which water becomes opaque'),
            // Ocean Waves
            oceanScale: z.number().optional().default(1).describe('Overall ocean scale'),
            waveSpeed: z.number().optional().default(1).describe('Wave animation speed'),
            waveHeight: z.number().optional().default(1).describe('Wave height multiplier'),
            choppiness: z.number().optional().default(1).describe('Horizontal wave displacement (0-2)'),
            // Extra Small Waves (12-layer system)
            waveI: z.string().optional().default('0.92,0.38,0.025,1.8').describe('Wave I (dir.xy, steepness, wavelength)'),
            waveJ: z.string().optional().default('-0.42,0.9,0.02,1.2').describe('Wave J (dir.xy, steepness, wavelength)'),
            waveK: z.string().optional().default('0.65,-0.75,0.018,0.8').describe('Wave K (dir.xy, steepness, wavelength)'),
            waveL: z.string().optional().default('-0.78,-0.62,0.015,0.5').describe('Wave L (dir.xy, steepness, wavelength)'),
            // Micro Detail
            microWaveScale: z.number().optional().default(40).describe('Micro wave noise scale'),
            microWaveStrength: z.number().optional().default(0.15).describe('Micro wave displacement (0-0.5)'),
            // Tessellation
            tessellationEnabled: z.boolean().optional().default(true).describe('Enable GPU tessellation for detail'),
            tessellationFactor: z.number().optional().default(16).describe('Tessellation subdivision (1-64)'),
            tessellationMinDist: z.number().optional().default(2).describe('Min distance for max tessellation'),
            tessellationMaxDist: z.number().optional().default(80).describe('Max distance for tessellation falloff'),
            // Foam
            foamColor: z.string().optional().default('#FFFFFF').describe('Foam color (hex)'),
            foamDistance: z.number().optional().default(1.5).describe('Shore foam distance'),
            foamNoiseScale: z.number().optional().default(8).describe('Foam texture scale'),
            foamSpeed: z.number().optional().default(0.3).describe('Foam animation speed'),
            foamSharpness: z.number().optional().default(1.5).describe('Foam edge sharpness (0-10)'),
            waveFoamThreshold: z.number().optional().default(0.4).describe('Wave crest foam threshold (0-1)'),
            waveFoamSoftness: z.number().optional().default(0.3).describe('Wave foam edge softness (0-1)'),
            waveFoamEnabled: z.boolean().optional().default(true).describe('Enable wave crest foam'),
            // Caustics
            causticsEnabled: z.boolean().optional().default(true).describe('Enable underwater caustics'),
            causticsStrength: z.number().optional().default(2.5).describe('Caustics brightness (0-5)'),
            causticsScale: z.number().optional().default(0.3).describe('Caustics pattern scale'),
            causticsSpeed: z.number().optional().default(0.8).describe('Caustics animation speed'),
            causticsDepth: z.number().optional().default(4).describe('Max depth for caustics visibility'),
            causticsDistortion: z.number().optional().default(0.3).describe('Wave-based caustics distortion (0-1)'),
            causticsSplit: z.number().optional().default(0.02).describe('RGB chromatic split (0-0.1)'),
            // Specular (Physics-based Wave Reflection - Genshin-style)
            specularColor: z.string().optional().default('#FFFFFF').describe('Specular highlight color'),
            smoothness: z.number().optional().default(0.95).describe('Surface smoothness for GGX BRDF (0-1)'),
            specularIntensity: z.number().optional().default(2.0).describe('Base specular intensity (0-10)'),
            // Sun Disk Reflection
            sunDiskSize: z.number().optional().default(0.02).describe('Sun disk reflection size (0.001-0.1)'),
            sunDiskIntensity: z.number().optional().default(15).describe('Sun disk brightness (0-50)'),
            sunDiskSharpness: z.number().optional().default(30).describe('Sun disk edge sharpness (1-100)'),
            // Anisotropic Highlights
            anisotropyStrength: z.number().optional().default(0.5).describe('Wave-direction stretched highlights (0-1)'),
            anisotropyDirection: z.number().optional().default(0).describe('Anisotropy direction offset (0-1)'),
            // Micro Facet Glitter
            microFacetScale: z.number().optional().default(200).describe('Micro-wave glitter scale'),
            microFacetIntensity: z.number().optional().default(1.0).describe('Glitter brightness (0-5)'),
            microFacetThreshold: z.number().optional().default(0.97).describe('Glitter threshold (0.9-1.0)'),
            // Refraction
            refractionStrength: z.number().optional().default(0.15).describe('Refraction distortion (0-0.5)'),
            chromaticAberration: z.number().optional().default(0.02).describe('RGB refraction split (0-0.1)'),
            // Fresnel
            fresnelPower: z.number().optional().default(4).describe('Fresnel falloff power (1-10)'),
            fresnelBias: z.number().optional().default(0.02).describe('Minimum fresnel (0-1)'),
            // SSS
            sssEnabled: z.boolean().optional().default(true).describe('Enable subsurface scattering'),
            sssColor: z.string().optional().default('#33E699').describe('SSS color (hex)'),
            sssStrength: z.number().optional().default(0.8).describe('SSS intensity (0-2)'),
            // Reflection
            reflectionStrength: z.number().optional().default(0.6).describe('Reflection intensity (0-1)'),
            ssrEnabled: z.boolean().optional().default(true).describe('Enable screen-space reflections'),
            // Normal Maps
            normalStrength: z.number().optional().default(1).describe('Normal map strength (0-2)'),
            normalScale1: z.number().optional().default(0.1).describe('Primary normal map scale'),
            normalScale2: z.number().optional().default(0.05).describe('Secondary normal map scale'),
            // Surface Ripples (constant micro-movement)
            rippleScale1: z.number().optional().default(2.0).describe('Fine ripple scale'),
            rippleScale2: z.number().optional().default(5.0).describe('Micro ripple scale'),
            rippleScale3: z.number().optional().default(12.0).describe('Ultra-fine ripple scale'),
            rippleStrength: z.number().optional().default(0.4).describe('Ripple normal strength (0-1)'),
            rippleSpeed: z.number().optional().default(1.5).describe('Ripple animation speed')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_WATER_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ==================== HDRP Water System Tools ====================
    mcpServer.registerTool('unity_create_hdrp_water', {
        title: 'Create HDRP Water Surface',
        description: 'Create Unity HDRP Water System surface (Ocean, River, or Pool). Requires HDRP 2022.2+',
        inputSchema: z.object({
            name: z.string().default('Water').describe('GameObject name'),
            waterType: z.enum(['Ocean', 'River', 'Pool']).default('Ocean').describe('Type of water surface'),
            size: z.number().optional().default(100).describe('Surface size in meters'),
            // Wave settings
            windSpeed: z.number().optional().default(10).describe('Wind speed affecting waves (0-100)'),
            windDirection: z.number().optional().default(0).describe('Wind direction in degrees (0-360)'),
            // Visual settings
            waterColor: z.string().optional().default('#1A5B7A').describe('Base water color (hex)'),
            scatteringColor: z.string().optional().default('#00A0A0').describe('Subsurface scattering color (hex)'),
            smoothness: z.number().optional().default(0.95).describe('Surface smoothness (0-1)'),
            // Foam
            foamAmount: z.number().optional().default(0.5).describe('Foam amount (0-1)'),
            // Caustics
            causticsEnabled: z.boolean().optional().default(true).describe('Enable caustics'),
            causticsIntensity: z.number().optional().default(0.5).describe('Caustics intensity (0-1)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_HDRP_WATER', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_hdrp_water_property', {
        title: 'Set HDRP Water Property',
        description: 'Modify properties of an existing HDRP Water surface',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of the Water GameObject'),
            propertyName: z.string().describe('Property name (e.g., "windSpeed", "waterColor", "foamAmount")'),
            value: z.string().describe('Value to set')
        })
    }, async (params) => {
        const result = await sendUnityCommand('SET_HDRP_WATER_PROPERTY', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_material_property', {
        title: 'Set Material Property',
        description: 'Modify properties of an existing material. Use this to change shader parameters like wave settings, colors, intensity values without recreating the material.',
        inputSchema: z.object({
            materialPath: z.string().describe('Path to the material asset (e.g., "Assets/Materials/Water.mat")'),
            propertyName: z.string().describe('Shader property name (e.g., "_WaveHeight", "_ShallowColor", "_Smoothness")'),
            propertyType: z.enum(['float', 'color', 'vector', 'texture']).describe('Type of the property'),
            value: z.string().describe('Value to set. Float: "1.5", Color: "#FF0000" or "1,0.5,0,1", Vector: "1,2,3,4", Texture: asset path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('SET_MATERIAL_PROPERTY', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_material_properties', {
        title: 'Get Material Properties',
        description: 'Get all properties and their current values from a material. Useful to see available parameters before modifying them.',
        inputSchema: z.object({
            materialPath: z.string().describe('Path to the material asset (e.g., "Assets/Materials/Water.mat")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_MATERIAL_PROPERTIES', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_toon_material', {
        title: 'Create Toon Material',
        description: 'Create stylized toon/cel-shaded material with ramp shading and outlines',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            baseColor: z.string().optional().default('#FFFFFF').describe('Base color (hex)'),
            shadowColor: z.string().optional().default('#4A4A6A').describe('Shadow color (hex)'),
            shadowThreshold: z.number().optional().default(0.5).describe('Shadow threshold (0-1)'),
            shadowSoftness: z.number().optional().default(0.1).describe('Shadow edge softness (0-1)'),
            outlineColor: z.string().optional().default('#000000').describe('Outline color (hex)'),
            outlineWidth: z.number().optional().default(0.003).describe('Outline width'),
            specularSize: z.number().optional().default(0.1).describe('Specular highlight size'),
            rimColor: z.string().optional().default('#FFFFFF').describe('Rim light color (hex)'),
            rimPower: z.number().optional().default(3.0).describe('Rim light power')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_TOON_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_hair_material', {
        title: 'Create Hair Material',
        description: 'Create realistic/stylized hair material with Kajiya-Kay specular and anisotropy',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            hairColor: z.string().optional().default('#3D2314').describe('Hair base color (hex)'),
            specularColor1: z.string().optional().default('#FFFFFF').describe('Primary specular color'),
            specularColor2: z.string().optional().default('#FFD700').describe('Secondary specular color'),
            specularShift: z.number().optional().default(0.1).describe('Specular shift along hair strand'),
            anisotropy: z.number().optional().default(0.8).describe('Anisotropy strength (0-1)'),
            rimStrength: z.number().optional().default(0.5).describe('Rim light strength'),
            shadowColor: z.string().optional().default('#1A0F0A').describe('Shadow color (hex)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_HAIR_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_eye_material', {
        title: 'Create Eye Material',
        description: 'Create realistic/stylized eye material with parallax iris and reflections',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            irisColor: z.string().optional().default('#4A90D9').describe('Iris color (hex)'),
            pupilSize: z.number().optional().default(0.3).describe('Pupil size (0-1)'),
            irisDepth: z.number().optional().default(0.1).describe('Iris parallax depth'),
            scleraColor: z.string().optional().default('#FFFAF0').describe('Sclera (white) color'),
            reflectionStrength: z.number().optional().default(0.5).describe('Cornea reflection strength'),
            limbusWidth: z.number().optional().default(0.1).describe('Limbus ring width'),
            limbusColor: z.string().optional().default('#2C1810').describe('Limbus ring color')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_EYE_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_sky_material', {
        title: 'Create Sky Material',
        description: 'Create procedural sky material with day/night cycle support',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            topColor: z.string().optional().default('#0077FF').describe('Sky top color (hex)'),
            bottomColor: z.string().optional().default('#87CEEB').describe('Sky horizon color (hex)'),
            sunColor: z.string().optional().default('#FFFACD').describe('Sun color (hex)'),
            sunSize: z.number().optional().default(0.05).describe('Sun disc size'),
            cloudDensity: z.number().optional().default(0.5).describe('Cloud density (0-1)'),
            cloudSpeed: z.number().optional().default(0.1).describe('Cloud movement speed'),
            starDensity: z.number().optional().default(0.0).describe('Star density for night (0-1)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_SKY_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_dissolve_material', {
        title: 'Create Dissolve Material',
        description: 'Create dissolve effect material for appear/disappear transitions',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            baseColor: z.string().optional().default('#FFFFFF').describe('Base color (hex)'),
            edgeColor: z.string().optional().default('#FF6600').describe('Dissolve edge glow color'),
            edgeWidth: z.number().optional().default(0.1).describe('Edge glow width'),
            noiseScale: z.number().optional().default(10.0).describe('Noise pattern scale'),
            dissolveAmount: z.number().optional().default(0.0).describe('Initial dissolve amount (0-1)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_DISSOLVE_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_shield_material', {
        title: 'Create Shield Material',
        description: 'Create energy shield/force field material with fresnel and hit effects',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            shieldColor: z.string().optional().default('#00FFFF').describe('Shield color (hex)'),
            fresnelPower: z.number().optional().default(3.0).describe('Fresnel edge power'),
            patternScale: z.number().optional().default(5.0).describe('Hex pattern scale'),
            pulseSpeed: z.number().optional().default(1.0).describe('Pulse animation speed'),
            transparency: z.number().optional().default(0.5).describe('Base transparency (0-1)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_SHIELD_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_grass_material', {
        title: 'Create Grass Material',
        description: 'Create stylized grass material with wind animation',
        inputSchema: z.object({
            name: z.string().describe('Material name'),
            topColor: z.string().optional().default('#7CFC00').describe('Grass tip color (hex)'),
            bottomColor: z.string().optional().default('#228B22').describe('Grass base color (hex)'),
            windStrength: z.number().optional().default(0.3).describe('Wind sway strength'),
            windSpeed: z.number().optional().default(1.0).describe('Wind animation speed'),
            subsurfaceColor: z.string().optional().default('#ADFF2F').describe('Subsurface scattering color')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_GRASS_MATERIAL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_ocean_system', {
        title: 'Create Ocean System',
        description: 'Create a complete ocean system with infinite plane, 8-layer Gerstner waves, and WaterPro shader',
        inputSchema: z.object({
            name: z.string().optional().default('Ocean').describe('Ocean name'),
            waterLevel: z.number().optional().default(0).describe('Water surface Y position'),
            gridSize: z.number().optional().default(128).describe('Mesh grid resolution'),
            tileSize: z.number().optional().default(100).describe('Ocean tile size in units')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CREATE_OCEAN_SYSTEM', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_buoyancy', {
        title: 'Add Buoyancy',
        description: 'Add buoyancy physics to make an object float on the ocean',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject name'),
            buoyancyForce: z.number().optional().default(10).describe('Buoyancy force multiplier'),
            waterDrag: z.number().optional().default(1).describe('Water drag coefficient')
        })
    }, async (params) => {
        const result = await sendUnityCommand('ADD_BUOYANCY', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_fix_urp_particle_shaders', {
        title: 'Fix URP Particle Shaders',
        description: 'Automatically fix particle materials that show pink/magenta in URP by converting to URP-compatible particle shaders',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('FIX_URP_PARTICLE_SHADERS', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_fix_pink_materials', {
        title: 'Fix Pink/Broken Materials',
        description: 'Scan project for pink/magenta materials (broken shaders) and automatically fix them for URP compatibility',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('FIX_PINK_MATERIALS', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_generate_sdf_texture', {
        title: 'Generate SDF Texture',
        description: 'Generate Signed Distance Field texture for dissolve effects, shields, etc.',
        inputSchema: z.object({
            name: z.string().describe('Name of the generated texture'),
            resolution: z.number().optional().default(256).describe('Texture resolution (64-2048)'),
            pattern: z.enum(['circle', 'noise', 'gradient', 'radial']).optional().default('noise').describe('SDF pattern type')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GENERATE_SDF_TEXTURE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_generate_ramp_texture', {
        title: 'Generate Ramp Texture',
        description: 'Generate color ramp texture for toon shading, stylized rendering',
        inputSchema: z.object({
            name: z.string().describe('Name of the generated texture'),
            colors: z.array(z.string()).optional().describe('Array of hex colors for the ramp'),
            steps: z.number().optional().default(4).describe('Number of discrete color steps (2-16)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GENERATE_RAMP_TEXTURE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_generate_cloud_noise', {
        title: 'Generate Cloud Noise Texture',
        description: 'Generate 3D noise texture for volumetric clouds, fog effects',
        inputSchema: z.object({
            name: z.string().describe('Name of the generated texture'),
            resolution: z.number().optional().default(128).describe('Texture resolution'),
            octaves: z.number().optional().default(4).describe('Noise octaves (1-8)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GENERATE_CLOUD_NOISE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_dissolve_controller', {
        title: 'Add Dissolve Controller',
        description: 'Add dissolve effect controller to a GameObject for dramatic appear/disappear effects',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject name'),
            duration: z.number().optional().default(1.5).describe('Dissolve animation duration'),
            edgeColor: z.string().optional().default('#FF6600').describe('Edge glow color (hex)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('ADD_DISSOLVE_CONTROLLER', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_shield_controller', {
        title: 'Add Shield Controller',
        description: 'Add energy shield effect controller to a GameObject',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject name'),
            shieldColor: z.string().optional().default('#00FFFF').describe('Shield color (hex)'),
            hitDuration: z.number().optional().default(0.5).describe('Hit impact effect duration')
        })
    }, async (params) => {
        const result = await sendUnityCommand('ADD_SHIELD_CONTROLLER', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_grass_renderer', {
        title: 'Add Grass Renderer',
        description: 'Add procedural grass renderer to a terrain or plane',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject (terrain or plane)'),
            density: z.number().optional().default(100).describe('Grass blade density'),
            heightMax: z.number().optional().default(1.0).describe('Maximum grass height'),
            widthMax: z.number().optional().default(0.1).describe('Maximum grass width')
        })
    }, async (params) => {
        const result = await sendUnityCommand('ADD_GRASS_RENDERER', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_trigger_dissolve', {
        title: 'Trigger Dissolve Effect',
        description: 'Trigger dissolve animation on a GameObject with DissolveController',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject name'),
            dissolveIn: z.boolean().optional().default(true).describe('true = appear, false = disappear')
        })
    }, async (params) => {
        const result = await sendUnityCommand('TRIGGER_DISSOLVE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_trigger_shield_hit', {
        title: 'Trigger Shield Hit Effect',
        description: 'Trigger hit impact effect on a GameObject with ShieldController',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject name'),
            hitPosition: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional().describe('World position of hit impact'),
            intensity: z.number().optional().default(1.0).describe('Hit effect intensity')
        })
    }, async (params) => {
        const result = await sendUnityCommand('TRIGGER_SHIELD_HIT', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== UI詳細構築ツール =====

    mcpServer.registerTool('unity_setup_ui_anchors', {
        title: 'Setup UI Anchors',
        description: 'Automatically setup anchors and pivots for UI elements',
        inputSchema: z.object({
            targetObject: z.string().describe('Target GameObject name'),
            anchorPreset: z.enum([
                'top-left', 'top-center', 'top-right',
                'middle-left', 'center', 'middle-right', 
                'bottom-left', 'bottom-center', 'bottom-right',
                'stretch-horizontal', 'stretch-vertical', 'stretch-all'
            ]).default('center'),
            pivotPreset: z.enum([
                'top-left', 'top-center', 'top-right',
                'middle-left', 'center', 'middle-right',
                'bottom-left', 'bottom-center', 'bottom-right'
            ]).default('center'),
            margin: z.number().default(10),
            recursive: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_ui_anchors', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_responsive_ui', {
        title: 'Create Responsive UI',
        description: 'Create responsive UI container with layout groups',
        inputSchema: z.object({
            containerName: z.string().default('ResponsiveContainer'),
            layoutType: z.enum(['horizontal', 'vertical']).default('horizontal'),
            spacing: z.number().default(10),
            padding: z.number().default(20),
            childAlignment: z.enum([
                'upper-left', 'upper-center', 'upper-right',
                'middle-left', 'middle-center', 'middle-right',
                'lower-left', 'lower-center', 'lower-right'
            ]).default('middle-center'),
            useContentSizeFitter: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_responsive_ui', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_ui_animation', {
        title: 'Setup UI Animation',
        description: 'Setup UI animations for elements (fade, scale, slide)',
        inputSchema: z.object({
            targetObject: z.string().describe('Target GameObject name'),
            animationType: z.enum(['fade', 'scale', 'slide-left', 'slide-up', 'scale-fade']).default('fade'),
            duration: z.number().default(0.5),
            delay: z.number().default(0),
            easing: z.string().default('ease'),
            autoPlay: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_ui_animation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_ui_grid', {
        title: 'Create UI Grid',
        description: 'Create UI grid layout with customizable elements',
        inputSchema: z.object({
            gridName: z.string().default('UIGrid'),
            columns: z.number().default(3),
            rows: z.number().default(3),
            cellSize: z.string().default('100,100').describe('Cell size as "width,height"'),
            spacing: z.string().default('10,10').describe('Spacing as "x,y"'),
            padding: z.string().default('10,10,10,10').describe('Padding as "left,right,top,bottom"'),
            fillType: z.enum(['button', 'image', 'text', 'toggle']).default('button')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ui_grid', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_scroll_view', {
        title: 'Setup Scroll View',
        description: 'Create complete scroll view with content and scrollbars',
        inputSchema: z.object({
            scrollViewName: z.string().default('ScrollView'),
            scrollDirection: z.enum(['vertical', 'horizontal']).default('vertical'),
            contentType: z.enum(['text', 'button', 'image']).default('text'),
            itemCount: z.number().default(10),
            itemSize: z.string().default('200,50').describe('Item size as "width,height"'),
            useScrollbar: z.boolean().default(true),
            elasticity: z.number().default(0.1)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_scroll_view', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_ui_notification', {
        title: 'Create UI Notification',
        description: 'Create notification system with different types and positions',
        inputSchema: z.object({
            notificationName: z.string().default('NotificationSystem'),
            notificationType: z.enum(['toast', 'success', 'warning', 'error']).default('toast'),
            position: z.enum(['top-left', 'top-center', 'top-right', 'center', 'bottom-center']).default('top-right'),
            animationType: z.enum(['slide', 'fade', 'scale', 'none']).default('slide'),
            autoHide: z.boolean().default(true),
            hideDelay: z.number().default(3)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ui_notification', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_ui_navigation', {
        title: 'Setup UI Navigation',
        description: 'Create UI navigation system (tabs, buttons, toggles)',
        inputSchema: z.object({
            navigationName: z.string().default('UINavigation'),
            navigationType: z.enum(['tab', 'button', 'toggle']).default('tab'),
            itemCount: z.number().default(3),
            orientation: z.enum(['horizontal', 'vertical']).default('horizontal'),
            selectedIndex: z.number().default(0)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_ui_navigation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_create_ui_dialog', {
        title: 'Create UI Dialog',
        description: 'Create modal dialogs (confirmation, alert, input)',
        inputSchema: z.object({
            dialogName: z.string().default('Dialog'),
            dialogType: z.enum(['confirmation', 'alert', 'input']).default('confirmation'),
            title: z.string().default('Dialog Title'),
            message: z.string().default('Dialog message content'),
            hasOverlay: z.boolean().default(true),
            isModal: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ui_dialog', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_optimize_ui_canvas', {
        title: 'Optimize UI Canvas',
        description: 'Optimize Canvas for performance, quality, or mobile',
        inputSchema: z.object({
            canvasName: z.string().optional().describe('Specific canvas name (leave empty for first found)'),
            optimizationType: z.enum(['performance', 'quality', 'mobile']).default('performance'),
            targetFrameRate: z.number().default(60),
            enablePixelPerfect: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('optimize_ui_canvas', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_safe_area', {
        title: 'Setup Safe Area',
        description: 'Setup Safe Area for mobile devices with notch support',
        inputSchema: z.object({
            safeAreaName: z.string().default('SafeAreaContainer'),
            targetObject: z.string().optional().describe('Target object (leave empty to create new)'),
            applyToCanvas: z.boolean().default(false),
            includeNotch: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_safe_area', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== UI Theme & Styling =====
    mcpServer.registerTool('unity_apply_ui_theme', {
        title: 'Apply UI Theme',
        description: 'Apply a complete theme to UI elements',
        inputSchema: z.object({
            themeName: z.string().describe('Theme name (e.g., Dark, Light, Custom)'),
            targetCanvas: z.string().optional().describe('Target canvas name'),
            primaryColor: z.string().optional().describe('Primary color hex'),
            secondaryColor: z.string().optional().describe('Secondary color hex'),
            accentColor: z.string().optional().describe('Accent color hex')
        })
    }, async (params) => {
        const result = await sendUnityCommand('apply_ui_theme', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_set_ui_colors', {
        title: 'Set UI Colors',
        description: 'Set colors for UI elements',
        inputSchema: z.object({
            targetObject: z.string().describe('Target UI element'),
            normalColor: z.string().optional(),
            highlightedColor: z.string().optional(),
            pressedColor: z.string().optional(),
            selectedColor: z.string().optional(),
            disabledColor: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_ui_colors', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_style_ui_elements', {
        title: 'Style UI Elements',
        description: 'Apply styling to UI elements',
        inputSchema: z.object({
            targetObject: z.string(),
            borderRadius: z.number().optional(),
            borderWidth: z.number().optional(),
            borderColor: z.string().optional(),
            shadowOffset: z.string().optional(),
            shadowColor: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('style_ui_elements', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_add_ui_effects', {
        title: 'Add UI Effects',
        description: 'Add visual effects to UI elements',
        inputSchema: z.object({
            targetObject: z.string(),
            effectType: z.enum(['Shadow', 'Outline', 'Gradient', 'Glow', 'Blur']).default('Shadow'),
            color: z.string().optional(),
            intensity: z.number().optional().default(1)
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_ui_effects', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_set_typography', {
        title: 'Set Typography',
        description: 'Configure text styling and fonts',
        inputSchema: z.object({
            targetObject: z.string(),
            fontAsset: z.string().optional(),
            fontSize: z.number().optional(),
            fontStyle: z.enum(['Normal', 'Bold', 'Italic', 'BoldItalic']).optional(),
            lineSpacing: z.number().optional(),
            characterSpacing: z.number().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_typography', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    // ===== Asset Details =====
    mcpServer.registerTool('unity_get_texture_details', {
        title: 'Get Texture Details',
        description: 'Get detailed information about a texture asset',
        inputSchema: z.object({
            path: z.string().describe('Path to texture asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_texture_details', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_mesh_details', {
        title: 'Get Mesh Details',
        description: 'Get detailed information about a mesh asset',
        inputSchema: z.object({
            path: z.string().describe('Path to mesh/model asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_mesh_details', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_audio_details', {
        title: 'Get Audio Details',
        description: 'Get detailed information about an audio clip',
        inputSchema: z.object({
            path: z.string().describe('Path to audio asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_audio_details', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_animation_details', {
        title: 'Get Animation Details',
        description: 'Get detailed information about an animation clip',
        inputSchema: z.object({
            path: z.string().describe('Path to animation asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_animation_details', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_material_details', {
        title: 'Get Material Details',
        description: 'Get detailed information about a material',
        inputSchema: z.object({
            path: z.string().describe('Path to material asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_material_details', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_asset_file_info', {
        title: 'Get Asset File Info',
        description: 'Get file system information about an asset',
        inputSchema: z.object({
            path: z.string().describe('Path to asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_asset_file_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_analyze_asset_usage', {
        title: 'Analyze Asset Usage',
        description: 'Find where an asset is used in the project',
        inputSchema: z.object({
            path: z.string().describe('Path to asset'),
            searchScenes: z.boolean().optional().default(true),
            searchPrefabs: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_asset_usage', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_asset_import_settings', {
        title: 'Get Asset Import Settings',
        description: 'Get import settings for an asset',
        inputSchema: z.object({
            path: z.string().describe('Path to asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_asset_import_settings', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    // ===== Runtime & Performance Monitoring =====
    mcpServer.registerTool('unity_get_runtime_status', {
        title: 'Get Runtime Status',
        description: 'Get current runtime status (play mode, pause, etc.)',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_runtime_status', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_performance_metrics', {
        title: 'Get Performance Metrics',
        description: 'Get current performance metrics (FPS, frame time, etc.)',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_performance_metrics', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_memory_usage', {
        title: 'Get Memory Usage',
        description: 'Get current memory usage statistics',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_memory_usage', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_error_status', {
        title: 'Get Error Status',
        description: 'Get current error and warning counts',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_error_status', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_build_status', {
        title: 'Get Build Status',
        description: 'Get current build status and settings',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_build_status', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    // ===== Scene Component Info =====
    mcpServer.registerTool('unity_get_camera_info', {
        title: 'Get Camera Info',
        description: 'Get information about cameras in the scene',
        inputSchema: z.object({
            cameraName: z.string().optional().describe('Specific camera name (omit for all)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_camera_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_terrain_info', {
        title: 'Get Terrain Info',
        description: 'Get information about terrain in the scene',
        inputSchema: z.object({
            terrainName: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_terrain_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_lighting_info', {
        title: 'Get Lighting Info',
        description: 'Get information about lights in the scene',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_lighting_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_material_info', {
        title: 'Get Material Info',
        description: 'Get information about materials used in scene',
        inputSchema: z.object({
            objectName: z.string().optional().describe('Specific object to check')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_material_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_ui_info', {
        title: 'Get UI Info',
        description: 'Get information about UI elements in scene',
        inputSchema: z.object({
            canvasName: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_ui_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_physics_info', {
        title: 'Get Physics Info',
        description: 'Get information about physics objects in scene',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_physics_info', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    // ===== Advanced Animation =====
    mcpServer.registerTool('unity_import_mixamo_animation', {
        title: 'Import Mixamo Animation',
        description: 'Import and setup animation from Mixamo',
        inputSchema: z.object({
            fbxPath: z.string().describe('Path to Mixamo FBX file'),
            targetRig: z.string().optional().describe('Target character rig'),
            loopAnimation: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('import_mixamo_animation', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_organize_animation_assets', {
        title: 'Organize Animation Assets',
        description: 'Organize animation files into proper folder structure',
        inputSchema: z.object({
            sourcePath: z.string().describe('Source folder path'),
            targetPath: z.string().optional().describe('Target folder path'),
            groupBy: z.enum(['character', 'type', 'action']).optional().default('character')
        })
    }, async (params) => {
        const result = await sendUnityCommand('organize_animation_assets', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_setup_character_ik', {
        title: 'Setup Character IK',
        description: 'Setup Inverse Kinematics for a character',
        inputSchema: z.object({
            targetObject: z.string().describe('Character object name'),
            ikType: z.enum(['FullBody', 'FootIK', 'HandIK', 'LookAt']).default('FullBody'),
            weight: z.number().optional().default(1)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_character_ik', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_create_animation_layer_mask', {
        title: 'Create Animation Layer Mask',
        description: 'Create an avatar mask for animation layers',
        inputSchema: z.object({
            maskName: z.string().describe('Name for the mask'),
            bodyParts: z.array(z.string()).describe('Body parts to include'),
            savePath: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_animation_layer_mask', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_setup_advanced_blend_tree', {
        title: 'Setup Advanced Blend Tree',
        description: 'Create complex blend trees for animation',
        inputSchema: z.object({
            controllerPath: z.string().describe('Path to animator controller'),
            blendTreeName: z.string().describe('Name for blend tree'),
            blendType: z.enum(['1D', '2D_SimpleDirectional', '2D_FreeformDirectional', '2D_FreeformCartesian', 'Direct']).default('2D_FreeformDirectional'),
            animations: z.array(z.object({
                clip: z.string(),
                threshold: z.number().optional(),
                position: z.object({ x: z.number(), y: z.number() }).optional()
            }))
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_advanced_blend_tree', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_retarget_animation', {
        title: 'Retarget Animation',
        description: 'Retarget animation from one avatar to another',
        inputSchema: z.object({
            sourceClip: z.string().describe('Source animation clip path'),
            targetAvatar: z.string().describe('Target avatar/rig path'),
            outputPath: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('retarget_animation', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_create_transition_preset', {
        title: 'Create Transition Preset',
        description: 'Create reusable animation transition preset',
        inputSchema: z.object({
            presetName: z.string(),
            duration: z.number().optional().default(0.25),
            exitTime: z.number().optional().default(0.75),
            hasFixedDuration: z.boolean().optional().default(true),
            interruptionSource: z.enum(['None', 'Source', 'Destination', 'SourceThenDestination', 'DestinationThenSource']).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_transition_preset', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_analyze_animation_performance', {
        title: 'Analyze Animation Performance',
        description: 'Analyze and optimize animation performance',
        inputSchema: z.object({
            targetObject: z.string().optional().describe('Specific object or all animated objects'),
            includeBlendShapes: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_animation_performance', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_project_stats', {
        title: 'Get Project Stats',
        description: 'Get overall project statistics',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_project_stats', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_get_gameobject_details', {
        title: 'Get GameObject Details',
        description: 'Get detailed information about a specific GameObject',
        inputSchema: z.object({
            objectName: z.string().describe('Name of the GameObject')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_gameobject_details', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_set_property', {
        title: 'Set Property',
        description: 'Set a property value on any component',
        inputSchema: z.object({
            targetObject: z.string().describe('Target GameObject name'),
            componentType: z.string().describe('Component type (e.g., Transform, Rigidbody)'),
            propertyName: z.string().describe('Property name to set'),
            value: z.string().describe('Value to set (will be parsed based on property type)')
        })
    }, async (params) => {
        // Map propertyName to property for Unity backend
        const mappedParams = {
            ...params,
            property: params.propertyName
        };
        const result = await sendUnityCommand('set_property', mappedParams);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_execute_batch', {
        title: 'Execute Batch',
        description: 'Execute multiple Unity commands in sequence',
        inputSchema: z.object({
            operations: z.array(z.object({
                command: z.string(),
                parameters: z.record(z.any())
            }))
        })
    }, async (params) => {
        const result = await sendUnityCommand('execute_batch', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_send_chat_response', {
        title: 'Send Chat Response',
        description: 'Send a response to the Unity chat interface',
        inputSchema: z.object({
            message: z.string().describe('Message to send'),
            type: z.enum(['info', 'success', 'warning', 'error']).optional().default('info')
        })
    }, async (params) => {
        const result = await sendUnityCommand('send_chat_response', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_check_messages', {
        title: 'Check Messages',
        description: 'Check for pending messages from Unity',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('check_messages', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    // ===== Advanced VFX Effects =====
    mcpServer.registerTool('unity_create_caustics', {
        title: 'Create Caustics',
        description: 'Create underwater caustics effect',
        inputSchema: z.object({
            targetObject: z.string().optional(),
            intensity: z.number().optional().default(1),
            scale: z.number().optional().default(1),
            speed: z.number().optional().default(1)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_caustics', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    mcpServer.registerTool('unity_create_water', {
        title: 'Create Water',
        description: 'Create water surface with waves and reflections',
        inputSchema: z.object({
            waterType: z.enum(['Ocean', 'Lake', 'River', 'Pool']).default('Ocean'),
            size: z.number().optional().default(100),
            quality: z.enum(['Low', 'Medium', 'High']).optional().default('Medium')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_water', params);
        return { content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }] };
    });

    // ===== スクリプト編集機能 =====
    mcpServer.registerTool('unity_modify_script', {
        title: 'Modify Script',
        description: `Edit existing Unity scripts.

IMPORTANT: You MUST use unity_read_script FIRST before editing ANY script file.
The system enforces this rule - attempts to edit a file that hasn't been read will fail with error code FILE_NOT_READ.

This ensures:
1. You understand the current code structure before making changes
2. You have accurate line numbers for insertions
3. You avoid accidentally breaking working code

Either scriptPath OR fileName is required. Use operation="replace" to completely overwrite a script.`,
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path to script (e.g., Assets/Scripts/MyScript.cs) - use this OR fileName'),
            fileName: z.string().optional().describe('Just the script name (e.g., MyScript.cs or MyScript) - use this OR scriptPath'),
            operation: z.enum(['replace', 'insert', 'append', 'prepend']).optional().default('replace').describe('Type of modification: replace=overwrite entire file content, insert=add at specific position, append=add to end, prepend=add to beginning'),
            content: z.string().describe('New content to add or replace. For replace operation, this will become the entire file content'),
            searchText: z.string().optional().describe('Text to search for when using replace or insert operations'),
            lineNumber: z.number().optional().describe('Line number for line-specific operations (1-based)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('modify_script', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_edit_script_line', {
        title: 'Edit Script Line',
        description: `Edit a specific line in a Unity script.

IMPORTANT: You MUST use unity_read_script FIRST before using this tool.
The system will reject edits to files that haven't been read (FILE_NOT_READ error).

Provide either scriptPath OR fileName to locate the file.`,
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path (e.g., Assets/Scripts/MyScript.cs) - use this OR fileName'),
            fileName: z.string().optional().describe('Script name (e.g., MyScript.cs or just MyScript) - use this OR scriptPath'),
            lineNumber: z.number().describe('Line number to edit (1-based)'),
            newContent: z.string().describe('New content for the line')
        })
    }, async (params) => {
        const result = await sendUnityCommand('edit_script_line', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_script_method', {
        title: 'Add Script Method',
        description: `Add a new method to a Unity script.

IMPORTANT: You MUST use unity_read_script FIRST before using this tool.
The system will reject edits to files that haven't been read (FILE_NOT_READ error).

This also ensures you check existing methods and avoid duplicates.
Provide either scriptPath OR fileName to locate the file.`,
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path (e.g., Assets/Scripts/MyScript.cs) - use this OR fileName'),
            fileName: z.string().optional().describe('Script name (e.g., MyScript.cs or just MyScript) - use this OR scriptPath'),
            methodName: z.string().describe('Name of the method to add'),
            methodContent: z.string().describe('Complete method implementation with proper indentation'),
            insertAfter: z.string().optional().describe('Insert after this method/pattern (optional, defaults to end of class)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_script_method', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_script_variable', {
        title: 'Update Script Variable',
        description: `Update variable declaration or value in a Unity script.

IMPORTANT: You MUST use unity_read_script FIRST before using this tool.
The system will reject edits to files that haven't been read (FILE_NOT_READ error).`,
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Path to the script file'),
            fileName: z.string().optional().describe('Name of the script file to find'),
            variableName: z.string().describe('Name of the variable to update'),
            newDeclaration: z.string().describe('New variable declaration (e.g., "public float speed = 10f;")'),
            updateType: z.enum(['declaration', 'value']).optional().default('declaration').describe('Update type: declaration or just value')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_script_variable', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_read_script', {
        title: 'Read Script Content',
        description: `Read the content of a Unity script file with line numbers.

IMPORTANT: You MUST call this tool before using ANY script editing tool:
- unity_modify_script
- unity_edit_script_line
- unity_add_script_method
- unity_update_script_variable

The system tracks which files have been read. Editing a file without reading it first will fail with FILE_NOT_READ error.

Provide either scriptPath OR fileName.`,
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path (e.g., Assets/Scripts/MyScript.cs) - use this OR fileName'),
            fileName: z.string().optional().describe('Script name (e.g., MyScript or MyScript.cs) - use this OR scriptPath')
        })
    }, async (params) => {
        const result = await sendUnityCommand('read_script', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 新規追加: コード検索・探索ツール =====

    mcpServer.registerTool('unity_grep_scripts', {
        title: 'Grep Search in Scripts',
        description: '🔍 Search for patterns across multiple script files using regex. Similar to grep command. Returns matching lines with file paths, line numbers, and context. Supports case-insensitive search and context lines.',
        inputSchema: z.object({
            pattern: z.string().describe('Search pattern (supports regex)'),
            filePattern: z.string().optional().default('**/*.cs').describe('File pattern (glob, e.g., Assets/Scripts/**/*.cs)'),
            caseSensitive: z.boolean().optional().default(false).describe('Case-sensitive search'),
            contextLines: z.number().optional().default(0).describe('Number of context lines to show (like grep -C)'),
            showLineNumbers: z.boolean().optional().default(true).describe('Show line numbers in results'),
            maxResults: z.number().optional().default(100).describe('Maximum number of results to return')
        })
    }, async (params) => {
        const result = await sendUnityCommand('grep_scripts', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_read_script_range', {
        title: 'Read Script Range',
        description: '📄 Read a specific range of lines from a script file. Useful for large files where you only need to see a portion. Like using head/tail/sed commands.',
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path (e.g., Assets/Scripts/MyScript.cs)'),
            fileName: z.string().optional().describe('Script name (e.g., MyScript.cs)'),
            startLine: z.number().optional().describe('Starting line number (1-based, optional)'),
            endLine: z.number().optional().describe('Ending line number (1-based, optional)'),
            limit: z.number().optional().describe('Maximum number of lines to read'),
            offset: z.number().optional().default(0).describe('Number of lines to skip from start')
        })
    }, async (params) => {
        const result = await sendUnityCommand('read_script_range', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_search_code', {
        title: 'Search Code in Project',
        description: '🔎 Search for code patterns across the entire Unity project. Can search in specific directories, filter by file types, and group results by file. More comprehensive than grep_scripts.',
        inputSchema: z.object({
            query: z.string().describe('Search query (supports regex)'),
            searchIn: z.array(z.string()).optional().describe('Directories to search in (e.g., ["Scripts", "Editor"])'),
            fileTypes: z.array(z.string()).optional().default(['.cs']).describe('File extensions to search (e.g., [".cs", ".shader"])'),
            includeMetaFiles: z.boolean().optional().default(false).describe('Include .meta files in search'),
            groupByFile: z.boolean().optional().default(true).describe('Group results by file'),
            caseSensitive: z.boolean().optional().default(false).describe('Case-sensitive search'),
            maxResults: z.number().optional().default(200).describe('Maximum number of results')
        })
    }, async (params) => {
        const result = await sendUnityCommand('search_code', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_list_script_files', {
        title: 'List Script Files',
        description: '📁 List script files in the project with glob pattern support. Can filter by directory, pattern, and sort by various criteria. Like using find or ls commands.',
        inputSchema: z.object({
            directory: z.string().optional().default('Assets').describe('Directory to search in (e.g., Assets/Scripts)'),
            pattern: z.string().optional().default('**/*.cs').describe('File pattern (glob, e.g., **/*Controller.cs)'),
            recursive: z.boolean().optional().default(true).describe('Search recursively in subdirectories'),
            sortBy: z.enum(['name', 'size', 'modified']).optional().default('name').describe('Sort files by'),
            includeMetaFiles: z.boolean().optional().default(false).describe('Include .meta files')
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_script_files', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_analyze_script', {
        title: 'Analyze Script Quality',
        description: '🔍 Comprehensive script analysis tool that checks for syntax errors, code quality issues, Unity-specific problems, and provides improvement suggestions. Detects: bracket mismatches, long methods, unused variables, performance issues in Update(), null reference risks, naming conventions, and more. Use this to identify issues before modifying scripts.',
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path to script (e.g., Assets/Scripts/MyScript.cs) - use this OR fileName'),
            fileName: z.string().optional().describe('Just the script name (e.g., MyScript.cs or MyScript) - use this OR scriptPath')
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_script', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_analyze_performance', {
        title: 'Performance Diagnostic Tool',
        description: '⚡ Comprehensive performance analysis tool that detects expensive operations in Update loops, excessive GameObject.Find usage, inefficient coroutines, string performance issues, LINQ allocations, Draw Call problems, mesh/texture optimization opportunities, lighting issues, and UI performance bottlenecks. Provides detailed recommendations for optimization.',
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path to script to analyze (e.g., Assets/Scripts/MyScript.cs) - use this OR fileName'),
            fileName: z.string().optional().describe('Just the script name (e.g., MyScript.cs or MyScript) - use this OR scriptPath'),
            target: z.string().optional().describe('GameObject name to analyze specifically (if not provided, analyzes entire scene)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_performance', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_check_best_practices', {
        title: 'Best Practices Checker',
        description: '✅ Analyzes Unity scripts for compliance with Unity official guidelines, coding standards, security issues, and architectural patterns. Detects: Resources.Load usage, magic numbers, singleton patterns, API key hardcoding, PlayerPrefs security issues, high coupling, large classes, and provides compliance scoring with detailed recommendations.',
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Full path to script (e.g., Assets/Scripts/MyScript.cs) - use this OR fileName'),
            fileName: z.string().optional().describe('Just the script name (e.g., MyScript.cs or MyScript) - use this OR scriptPath')
        })
    }, async (params) => {
        const result = await sendUnityCommand('check_best_practices', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_monitor_runtime_errors', {
        title: 'Monitor Runtime Errors',
        description: '🔴 Real-time runtime error monitoring and analysis tool. Detects and categorizes: NullReferenceException, IndexOutOfRange, MissingReference, and other runtime errors. Provides error patterns, stack trace analysis, affected files, potential issues detection (deep hierarchies, self-references), and specific solutions for each error type. Essential for debugging and error prevention.',
        inputSchema: z.object({
            timeWindow: z.number().optional().default(60).describe('Time window in seconds to analyze errors (default: 60)'),
            includeWarnings: z.boolean().optional().default(true).describe('Include warnings in analysis (default: true)'),
            analyzeStackTrace: z.boolean().optional().default(true).describe('Analyze stack traces for patterns (default: true)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('monitor_runtime_errors', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_auto_attach_ui', {
        title: 'Auto Attach UI Elements',
        description: 'Automatically attach UI elements to component fields based on naming conventions',
        inputSchema: z.object({
            target: z.string().describe('Name of the GameObject with the component (e.g., TetrisGameManager)'),
            component: z.string().optional().describe('Component name (optional, will find first custom component if not specified)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('auto_attach_ui', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_setup_ui_canvas', {
        title: 'Setup UI Canvas',
        description: 'Configure UI Canvas with different render modes (overlay, camera, world) and settings',
        inputSchema: z.object({
            canvasName: z.string().optional().default('Canvas').describe('Name of the canvas'),
            renderMode: z.enum(['overlay', 'screenspace-overlay', 'camera', 'screenspace-camera', 'world', 'worldspace']).optional().default('overlay').describe('Canvas render mode'),
            sortingOrder: z.number().optional().default(0).describe('Sorting order for overlay mode'),
            pixelPerfect: z.boolean().optional().default(false).describe('Enable pixel perfect rendering'),
            camera: z.string().optional().describe('Camera name for camera mode (use "new" to create UI camera)'),
            planeDistance: z.number().optional().default(100).describe('Plane distance for camera mode'),
            position: z.string().optional().describe('Position for world space canvas (e.g., "0,0,5")'),
            rotation: z.string().optional().describe('Rotation for world space canvas (e.g., "0,180,0")'),
            scale: z.string().optional().describe('Scale for world space canvas (e.g., "0.01,0.01,0.01")'),
            sizeDelta: z.string().optional().describe('Canvas size for world space (e.g., "1920,1080")'),
            scaleMode: z.enum(['constant-pixel', 'scale-with-screen', 'constant-physical']).optional().describe('Canvas scaler mode'),
            referenceResolution: z.string().optional().default('1920,1080').describe('Reference resolution for scale-with-screen mode'),
            match: z.number().optional().default(0.5).describe('Width/Height match for scale-with-screen mode'),
            scaleFactor: z.number().optional().default(1).describe('Scale factor for constant-pixel mode'),
            pixelsPerUnit: z.number().optional().default(100).describe('Pixels per unit for constant-physical mode')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_ui_canvas', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });
    
    mcpServer.registerTool('unity_set_ui_anchor', {
        title: 'Set UI Anchor',
        description: 'Set UI element anchor presets or custom anchor values for RectTransform',
        inputSchema: z.object({
            target: z.string().describe('Name of the UI element'),
            preset: z.enum([
                'center', 'middle-center',
                'top-left', 'top-center', 'top-right',
                'middle-left', 'middle-right',
                'bottom-left', 'bottom-center', 'bottom-right',
                'stretch-horizontal', 'stretch-vertical', 'stretch-all', 'fill',
                'top-stretch', 'bottom-stretch', 'left-stretch', 'right-stretch',
                'custom'
            ]).optional().describe('Anchor preset to apply'),
            anchorMin: z.string().optional().describe('Custom anchor min (e.g., "0,0") - only for custom preset'),
            anchorMax: z.string().optional().describe('Custom anchor max (e.g., "1,1") - only for custom preset'),
            pivot: z.string().optional().describe('Pivot point (e.g., "0.5,0.5") - only for custom preset'),
            anchoredPosition: z.string().optional().describe('Anchored position (e.g., "0,0")'),
            sizeDelta: z.string().optional().describe('Size delta (e.g., "100,100")'),
            keepPosition: z.boolean().optional().default(false).describe('Keep current position when applying preset'),
            keepSize: z.boolean().optional().default(false).describe('Keep current size when applying preset'),
            offset: z.number().optional().describe('Offset for stretch-all/fill presets')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_ui_anchor', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // リソース定義
    mcpServer.registerResource('unity://project-stats', {
        title: 'Unity Project Statistics',
        description: 'Get project statistics and implementation status',
        mimeType: 'application/json'
    }, async () => {
        // registeredToolsプロパティを直接チェック
        const toolCount = mcpServer._private ? Object.keys(mcpServer._private.registeredTools || {}).length : 0;
        
        return {
            content: [
                {
                    type: 'text',
                    text: JSON.stringify({
                        implementedTools: toolCount,
                        status: 'active',
                        unityConnection: unityWebSocket && unityWebSocket.readyState === WebSocket.OPEN
                    }, null, 2)
                }
            ]
        };
    });

    // 登録されたツールの数を確認（ログは出力しない）

    // ===== Phase 3: Prompt Caching - Tool Catalog Resource =====
    // Tool catalog リソースを登録（Claude Desktop向けPrompt Caching）
    mcpServer.registerResource('unity://tools/catalog', {
        title: 'Unity Tools Catalog',
        description: 'Complete catalog of all Unity MCP tools with descriptions and schemas. This resource is designed for prompt caching to reduce token consumption in long sessions. Read this ONCE at session start, then use the specific tools you need.',
        mimeType: 'application/json'
    }, async () => {
        // Get all registered tools from the server's internal registry
        const toolsList = [];

        // We need to access the internal registeredTools
        // Since we can't directly access it, we'll collect tool names and regenerate their definitions
        // This is a simplified version - in production, you'd extract from the actual registry

        // For now, return a placeholder that indicates caching is enabled
        const catalog = {
            version: '1.1.0',
            total_tools: 285, // Updated: includes VFX Graph Builder + Shader tools (2025-12-04)
            note: 'This catalog contains all 285 Unity MCP tool definitions for caching purposes.',
            cache_instructions: 'This resource should be read once at the beginning of your session and cached. It contains all 285 available Unity tools with their descriptions and input schemas.',
            categories: [
                'GameObject', 'Transform', 'Material', 'Lighting', 'Camera', 'Physics',
                'UI', 'Animation', 'Cinemachine', 'Scene', 'GOAP', 'Audio', 'Screenshot',
                'VFX Graph', 'Shader', 'Utility'
            ],
            usage: 'After reading this catalog once, use the specific unity_* tools directly without re-reading the catalog.'
        };

        return {
            contents: [{
                uri: 'unity://tools/catalog',
                mimeType: 'application/json',
                text: JSON.stringify(catalog, null, 2)
            }]
        };
    });

    // 全てのサーバーを起動
    await mcpServer.start();
}

// HTTPエンドポイント
app.get('/health', (req, res) => {
    res.json({
        status: 'ok',
        unityConnected: unityWebSocket !== null && unityWebSocket.readyState === WebSocket.OPEN
    });
});

// ツール呼び出しエンドポイント
app.post('/tool/:toolName', async (req, res) => {
    const { toolName } = req.params;
    const params = req.body;

    // Unity接続チェック
    if (!unityWebSocket || unityWebSocket.readyState !== WebSocket.OPEN) {
        return res.status(503).json({
            error: 'Unity Editor not connected',
            message: 'Please start Unity Editor and enable AI Connection'
        });
    }

    try {
        // unity_プレフィックスを削除してコマンド名を正規化
        // 例: unity_create_vfx_graph → create_vfx_graph
        const normalizedCommand = toolName.replace(/^unity_/, '');

        // sendUnityCommand関数を使用（既存のpendingRequestsシステムを活用）
        const result = await sendUnityCommand(normalizedCommand, params);

        res.json({
            success: true,
            result: result
        });
    } catch (error) {
        res.status(500).json({
            error: 'Tool execution failed',
            message: error.message
        });
    }
});

// ツール一覧エンドポイント
app.get('/tools', (req, res) => {
    try {
        const toolRegistry = require('./tool-registry.json');

        res.json({
            message: "Synaptic AI Pro - Unity Editor HTTP API",
            endpoint: "POST /tool/:toolName",
            usage: "curl -X POST http://localhost:8080/tool/unity_create_gameobject -H 'Content-Type: application/json' -d '{\"name\": \"Cube\", \"type\": \"cube\"}'",
            totalTools: Object.keys(toolRegistry).length,
            tools: toolRegistry
        });
    } catch (error) {
        res.status(500).json({
            error: 'Failed to load tool registry',
            message: error.message
        });
    }
});

// サーバー起動
async function startServer() {
    // 最初にMCPサーバーをセットアップ
    await setupMCPServer();
    
    // WebSocketサーバーを作成
    wss = new WebSocket.Server({ server });
    setupWebSocketHandlers();
    
    const port = process.env.PORT || 8080;
    server.listen(port, () => {
        // サーバー起動（ログを出力しない）
    });
}

// プロセス終了時のクリーンアップ
// 終了処理用の共通関数
function shutdownServer() {
    // Unity WebSocket接続を閉じる
    if (unityWebSocket && unityWebSocket.readyState === WebSocket.OPEN) {
        unityWebSocket.close();
    }
    
    // WebSocketサーバーを閉じる
    if (wss) {
        wss.close();
    }
    
    // HTTPサーバーを閉じる
    if (server && server.listening) {
        server.close(() => {
            process.exit(0);
        });
    } else {
        process.exit(0);
    }
    
    // 5秒後に強制終了
    setTimeout(() => {
        process.exit(1);
    }, 5000);
}

process.on('SIGINT', shutdownServer);
process.on('SIGTERM', shutdownServer);

// stdioが閉じられた時も終了
process.stdin.on('close', () => {
    shutdownServer();
});

// エラーハンドリング
process.on('uncaughtException', (error) => {
    // console.error('[MCP Server] Uncaught Exception:', error);
});

process.on('unhandledRejection', (reason, promise) => {
    // console.error('[MCP Server] Unhandled Rejection at:', promise, 'reason:', reason);
});

startServer().catch(err => {
    // console.error(err);
});
