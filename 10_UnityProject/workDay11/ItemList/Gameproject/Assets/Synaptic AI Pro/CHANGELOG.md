# Changelog

All notable changes to Synaptic AI Pro for Unity will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.5] - 2025-12-13

### 🚀 Major Update - 42,600+ lines added!

### Added - AI Systems
- **GOAP (Goal-Oriented Action Planning) Runtime Engine**
  - `GOAPAgent.cs` - Full agent implementation with planning & execution
  - `GOAPPlanner.cs` - A* search-based planner for action sequences
  - `GOAPActionBase.cs` - Base class for custom actions
  - `GOAPDynamicAction.cs` - Runtime-configurable actions
  - `GOAPGoal.cs` - Goal definition with priorities
  - `WorldState.cs` - State representation for planning
  - MCP Tools: CREATE_GOAP_AGENT, ADD_GOAP_ACTION, ADD_GOAP_GOAL, SET_WORLD_STATE, etc.

- **Behavior Tree Runtime**
  - `BTNode.cs` - Base node with Success/Failure/Running states
  - `BTComposites.cs` - Selector, Sequence, Parallel nodes
  - `BTDecorators.cs` - Inverter, Repeater, Succeeder, UntilFail, Cooldown
  - `BTLeaves.cs` - Wait, Log, SetBlackboard, CheckBlackboard, MoveTo, etc.
  - `BehaviorTreeRunner.cs` - Runtime executor with blackboard
  - MCP Tools: CREATE_BEHAVIOR_TREE, ADD_BT_NODE, SET_BT_BLACKBOARD, etc.

### Added - Shaders (URP/HDRP/Built-in Compatible)
- **SynapticWaterPro.shader** - Advanced ocean with Gerstner waves, foam, refraction, caustics
- **SynapticSkyPro.shader** - Procedural sky with volumetric clouds, day/night cycle
- **SynapticToonPro.shader** - Anime-style cel shading with outline, rim light
- **SynapticGrassPro.shader** - GPU-instanced grass with wind animation
- **GrassInstancer.compute** - Compute shader for grass placement
- **CloudNoise.compute** - Procedural cloud generation
- Runtime controllers: DissolveController, ShieldController, GrassRenderer

### Added - Water System
- **OceanSystem.cs** - Wave simulation and water surface management
- **Buoyancy.cs** - Physics-based floating objects
- **WaterPhysics.cs** - Water interaction and splash effects
- MCP Tools: CREATE_OCEAN_SYSTEM, ADD_BUOYANCY

### Added - VFX Graph Editing (6 New Tools)
- `unity_vfx_set_output` - Change blendMode, texture, softParticle
- `unity_vfx_set_block_value` - Modify block values (color, size, turbulence)
- `unity_vfx_set_spawn_rate` - Adjust spawn rate
- `unity_vfx_list_blocks` - List all contexts/blocks with indices
- `unity_vfx_remove_block` - Remove blocks from contexts
- `unity_vfx_get_block_info` - Get detailed block information
- New VFXBuilder methods: SetOutputSettings, SetBlockValue, SetSpawnRate, ListBlocks, RemoveBlock, GetBlockInfo

### Added - VFX Textures (150+ CC0 Kenney Textures)
- Fire/Flame: flame_01~06, fire_01~02, flare_01
- Smoke: smoke_01~10, whitePuff00~24, blackSmoke00~24
- Explosion: explosion00~08
- Sparks: spark_01~07
- Magic: magic_01~05, star_01~09, twirl_01~03
- Trails: trace_01~07
- Effects: muzzle_01~05, slash_01~04, light_01~03, circle_01~05
- CC0 License - free to use and redistribute
- `SetParticleTexture()` method for easy texture assignment

### Added - MCP Auto-Retry System
- Automatic 3 retry attempts with 3-second intervals
- Handles Unity recompilation gracefully
- Success response includes retry info when applicable
- Clear error messages after all retries exhausted

### Added - Script Safety Features
- `READ_SCRIPT` requirement before editing (like Claude Code)
- `UpdateScriptVariable` now requires prior read

### Fixed - VFX
- **Fire Preset**: Now with natural flickering
  - Random lifetime (0.8-2.0s), size (0.3-0.7), angle (0-360°)
  - Random velocity spread, angular velocity for rotation
  - Enhanced turbulence (intensity 3.0, frequency 5)
- **Color Attribute**: Uses Vector3 (RGB) instead of Vector4
- **VFXSlotFloat3 Angle**: Float → Vector3 conversion added

### Fixed - Serialization
- **Vector3 circular reference**: GET_TERRAIN_INFO, GET_LIGHTING_INFO, GET_UI_INFO, GET_PHYSICS_INFO
- Using `Vector3ToString()` helper throughout

### Fixed - MCP Tools
- `unity_update_component`: Accepts both `gameObject`/`component` and `gameObjectName`/`componentName`
- `unity_set_property`: Fixed `propertyName` → `property` mapping
- Weather system controllers now properly attached (Rain, Snow, Wind, Lightning, Thunderstorm)

### Fixed - Build Errors
- Editor-only code properly wrapped in `#if UNITY_EDITOR`
- Debug namespace ambiguity resolved
- Shader function redefinition and LerpWhiteTo errors

### Changed
- **Auto Reconnect Menu**: Shows "Enable" when OFF, "Disable" when ON
- **Tool Registry**: Regenerated with 342+ tools
- **VFX Presets**: Now use Kenney textures by default

### Technical Stats
- 93 files changed
- 42,641 insertions, 6,561 deletions
- New Runtime scripts: 15+
- New Shaders: 6
- New MCP Tools: 20+

## [1.1.4] - 2025-11-26

### Added
- **HTTP API Endpoints**: Direct Unity control from AI CLIs without MCP
  - `GET /health` - Check Unity connection status
  - `GET /tools` - List all 246 available tools with descriptions
  - `POST /tool/:toolName` - Execute any Unity tool via HTTP
  - Compatible with Claude Code, Codex CLI, Gemini CLI, and custom AI tools
  - Full tool-registry.json integration for tool discovery

### Fixed
- **WebSocket Message Handling**: Improved operationId recognition
  - NexusEditorMCPService now correctly reads operationId from parameters
  - Fixed timeout issues when calling tools via HTTP endpoints
  - Unified response format between MCP stdio and HTTP/WebSocket paths

### Changed
- Version bumped to 1.1.4 across all components
  - package.json, MCPServer/package.json
  - NexusWebSocketClient.cs, NexusEditorMCPService.cs
  - NexusSetupWindow.cs, NexusSetupManager.cs

## [1.1.3] - 2025-11-25

### Added
- **LM Studio Essential Mode**: New lightweight 80-tool configuration optimized for LM Studio and Cursor
  - 80 carefully selected essential tools (67% reduction from full 246 tools)
  - 62% smaller file size (90KB vs 239KB)
  - Perfect for local AI workflows without subscription costs
  - Includes: GameObject, Camera, Scene, UI, Screenshot, Animation basics
  - Removed: Scripting, GOAP, Weather, Advanced VFX, Batch operations
- **3-Mode Setup Window**: Easy selection for different AI clients
  - Claude Desktop/Cursor (Full 246 tools)
  - Cursor/LM Studio Essential (80 tools) ← NEW!
  - GitHub Copilot (Dynamic 8→N tools)
- **index-essential.js**: Optimized server script for essential mode

### Fixed
- **LM Studio Configuration**: Added missing `cwd` parameter to LM Studio mcp.json
  - Tools now correctly loaded and recognized by LM Studio
  - Proper working directory ensures Node.js modules load correctly

## [1.1.2] - 2025-11-24

### Fixed
- **Cinemachine 3.x Complete Support**: Full API compatibility
  - Fixed all 27 compilation errors when using Cinemachine 3.1.5
  - API Changes: Enum names (PositionModes, RotationModes, UpdateMethods)
  - API Changes: LensSettings.Orthographic → ModeOverride pattern
  - API Changes: ICinemachineCamera → CinemachineCamera casting
  - API Changes: Component property structs (PositionComposer, RotationComposer, Deoccluder)
  - API Changes: InputAxisController.Controllers.Length → Count
  - API Changes: ImpulseSource.SignalDefinition → ImpulseDefinition
  - API Changes: CinemachineDeoccluder.ObstacleAvoidance → AvoidObstacles
  - FreeLook implementation: Added missing CinemachineRotationComposer component
  - Added GetNoiseProfile() helper function for CM3
  - Readonly struct property handling (get-modify-set pattern)

- **Advanced Material Properties**: JSON parsing
  - Fixed nested `properties` parameter parsing
  - Properties now correctly applied (color, metallic, smoothness, emission)
  - Added detailed debug logging for property parsing
  - Backward compatibility maintained for direct parameter format

### Improved
- **FreeLook Camera UX**: Clearer error handling
  - Made `follow` parameter required (removed confusing auto-dummy-target)
  - Detailed error messages with cause + solutions + example code
  - Tool description improvements in index.js and tool-registry.json
  - Success messages show all created components
  - Cleanup on error (removes partially-created camera objects)

### Technical
- Both Cinemachine 2.9.x and 3.1.x fully supported via preprocessor directives
- No breaking changes - all existing code continues to work
- Enhanced logging throughout Cinemachine operations

## [1.1.1] - 2025-11-23

### Fixed
- **Cinemachine Compatibility**: Both v2 and v3 support
  - Fixed assembly reference issues for Cinemachine 2.9.7
  - Added proper version detection for Cinemachine v2 (`CINEMACHINE_2`) and v3 (`CINEMACHINE_3`)
  - Assembly definition now handles both `Cinemachine` (v2) and `Unity.Cinemachine` + `Unity.Splines` (v3)

- **URP Material Support**: Universal Render Pipeline compatibility
  - Added render pipeline detection with caching for performance
  - `CreatePrimitiveWithMaterial()` helper automatically applies URP/HDRP/Legacy shaders
  - Fixed pink material issue in URP projects when creating primitives
  - Updated all GameObject creation functions to use pipeline-compatible materials
  - Updated all game template functions (FPS, Platformer, RPG, Puzzle, Racing, Strategy)
  - Shader not found warnings added for debugging

### Improved
- **Code Quality**: Refactored primitive creation
  - Eliminated code duplication in `CreateGameObject()` function
  - Centralized material creation logic
  - Performance optimization: Cached render pipeline detection
  - Updated 50+ primitive creation locations across codebase

## [1.1.0] - 2025-11-23

### Added

#### Dynamic Tool Loading System (GitHub Copilot Support)
- **MCP Hub Server** (`hub-server.js`): Dynamic tool loading for GitHub Copilot
  - Starts with 8 essential management tools
  - Dynamic tool loading via `select_tools()` by category or keywords
  - Support for `notifications/tools/list_changed` (MCP spec 2025-06-18)
  - Automatic tool list refresh in GitHub Copilot (VS Code)
  - **No OpenAI API required**: Text-based keyword matching and category search
  - 31 tool categories for comprehensive Unity feature coverage
  - Access all 246 tools dynamically without hitting IDE tool limits
  - Unity WebSocket integration (port 8080) matching index.js protocol

#### Prompt Caching Support (Claude Desktop)
- **Tool Catalog Resource** (`unity://tools/catalog`): Dramatic session capacity improvement
  - **Verified Results (11 actual tool calls)**:
    - First request: 57,511 tokens (tool definitions cached)
    - 2-11 requests average: 1,596 tokens/call
    - **Token reduction: 97.2%** per subsequent call
    - Total used: 73,476 tokens (11 calls)
    - Remaining: 116,524 tokens (190,000 budget)
  - **Session Capacity**: ~84 total tool calls per session
    - Practical development: 70-90 operations
    - With heavy data fetching: 50-60 operations
  - **Use Cases Enabled**:
    - Create 3 UI screens (20 ops each) = 60 calls
    - 10 debugging iterations
    - 20 adjustments/confirmations
    - Total: 90 operations in single session

#### Lightweight Scene Information Tools
- **unity_get_scene_summary**: Fast scene overview (<200KB)
  - Scene name, GameObject count, cameras, lights
  - Root GameObjects list (max 50)
  - Replaces heavy `unity_get_scene_info` for large scenes

- **unity_get_gameobjects_list**: Filtered GameObject queries (<50KB)
  - Filter by layer, tag, name (contains), active state
  - Returns GameObject names, IDs, paths, metadata
  - Max 100 results per query

- **unity_get_gameobject_detail**: Individual GameObject inspection (<10KB)
  - Detailed transform, components, children, parent info
  - Find by name or instanceId
  - Component-specific details (MeshRenderer, Light, Camera, etc.)

- **unity_get_scene_changes_since**: Incremental scene updates
  - Timestamp-based differential updates
  - Returns added, removed, modified GameObjects
  - Efficient monitoring for large scenes

#### Setup Window AI Client Selection
- **AI Tool Selection UI**: Choose between Full Mode and Dynamic Mode
  - Claude Desktop / Cursor: Full Mode (246 tools, prompt caching)
  - GitHub Copilot: Dynamic Mode (8→dynamic tools, notifications)
  - Mode-specific setup instructions and success messages
  - Automatic server path selection (index.js or hub-server.js)

#### Infrastructure
- **Tool Loader System** (`utils/tool-loader.js`)
  - **31 tool categories**: GameObject, Transform, Material, Lighting, Camera, Physics, UI, Animation, Cinemachine, Scene, GOAP, Audio, Input, VFX, Shader, Weather, TimeOfDay, Editor, Package, Build, Monitoring, AssetManagement, Optimization, Batch, GameSystems, AI, Debug, Timeline, Scripting, Screenshot, Utility
  - **Text-based keyword matching** with relevance scoring (no API required)
  - Category-based filtering with default presets
  - Multi-keyword support with score boosting
  - Input normalization (arrays, strings, comma/pipe separated)

- **Tool Registry** (`tool-registry.json`)
  - Pre-generated metadata for all 246 tools
  - Category assignments and descriptions
  - **No external API dependency**: Generated via `generate-simple-registry.js`
  - Tool name, title, description, and category information

- **Optional OpenAI Integration** (`utils/embedding.js`)
  - Semantic search enhancement (if OPENAI_API_KEY provided)
  - Automatic fallback to text matching when unavailable
  - Not required for normal operation

### Changed
- **MCP Server Architecture**: Dual-mode support
  - `index.js`: Full Mode server (246 tools, all clients)
  - `hub-server.js`: Dynamic Mode server (GitHub Copilot only)

- **Setup Window**: Enhanced with AI client selection
  - Visual selection buttons with mode explanations
  - Dynamic info boxes based on selection
  - Mode-specific success messages

- **Tool Count**: Corrected to 246 unique tools
  - Verified count (247 registrations, 1 duplicate removed)
  - Updated all user-facing documentation

### Technical Details
- **MCP Capabilities**:
  - Full Mode: `tools: {}, resources: {}` (Prompt Caching via unity://tools/catalog)
  - Dynamic Mode: `tools: { listChanged: true }` (Notifications for dynamic tool updates)

- **Hub Server Communication**:
  - Unity WebSocket server on port 8080
  - Request/response tracking with pendingRequests Map
  - Protocol matching index.js for consistency
  - Proper error handling and message routing

- **Tool Search System**:
  - Primary: Text-based keyword matching with relevance scoring
  - Fallback chain: Multi-keyword matching → category filtering
  - No external API calls required for normal operation
  - Optional: OpenAI Embedding enhancement (if API key provided)

- **Supported AI Clients**:
  - ✅ Claude Desktop: Full Mode with Prompt Caching (~52% token reduction verified)
  - ✅ Cursor: Full Mode (246 tools, 80-tool warning can be ignored)
  - ✅ GitHub Copilot: Dynamic Mode with notifications support (tested)

- **Session Lifespan Improvements**:
  - First call: 57,511 tokens (tool definitions cached)
  - Subsequent calls: ~1,596 tokens average
  - **Result: ~84 total tool calls per session** (vs ~13 without caching)
  - **6.5x capacity increase** verified with real usage

### Dependencies
- **Added**: `openai` ^4.20.0 (optional - for enhanced semantic search only)
  - **Not required**: Hub server works with text-based keyword matching by default
  - Only needed if you want embedding-based semantic search enhancement

### Performance
- Scene information retrieval: 80-95% size reduction for large scenes
- Claude Desktop token usage: 52% reduction verified
- GitHub Copilot: All 246 tools accessible within 80-tool limit

## [1.0.3] - 2025-11-21 

### Added
- **One-Touch MCP Setup**: Automatic configuration for multiple AI tools with a single click
  - Configure Claude Desktop, Cursor, and VS Code simultaneously
  - `~/.cursor/mcp.json` automatically created for Cursor
  - `.vscode/mcp.json` automatically created for VS Code
  - **Smart Merge**: Preserves existing MCP server configurations, only adds/updates unity-synaptic
  - No manual path configuration needed
  - Setup Window button: "Complete MCP Setup"

- **unity_capture_grid**: Auto-split Game View into grid and capture all cells
  - Grid sizes: "2x2", "3x3", "4x4", up to "5x5"
  - Each cell saved as separate file with position info (basename_r0_c0.png, etc.)
  - Perfect for systematic UI analysis and debugging
  - Works with Canvas Overlay UI

- **unity_capture_ui_element**: Capture specific UI element by GameObject name
  - Automatically finds element and calculates screen bounds
  - Works with all Canvas render modes (Overlay, Camera, World Space)
  - No need to manually specify coordinates
  - Example: `elementName: "MoveButton"` captures just that button

- **unity_get_screenshot_result**: Async result retrieval for Play mode captures
  - Call after 3 seconds when capture returns `"status": "pending"`
  - Returns screenshot path, width, height
  - Works with all async capture operations

### Fixed
- **Screenshot Capture**: Complete overhaul of all screenshot tools to properly capture Canvas/UI
  - **CaptureGameView**:
    - Automatically enters Play mode if needed to capture Canvas Overlay
    - 60-frame wait for rendering stabilization before capture
    - Captures at native Game View resolution (fixed 3024x40 bug)
    - Exits Play mode automatically after capture
    - MCP tool description enhanced to force LLM to wait 3 seconds
  - **CaptureRegion**:
    - Game View mode now requires Play mode for Canvas Overlay capture
    - Captures full screenshot then extracts specified region
    - Uses actual screenshot dimensions instead of fixed resolution
    - Correctly captures Canvas elements in all render modes
  - **CaptureUIElement**:
    - Fixed Canvas Overlay coordinate conversion (use GetWorldCorners directly)
    - Fixed Y-coordinate inversion bug (both GetWorldCorners and GetPixels use bottom-left origin)
    - Added dimension validation before texture creation
    - Added debug logging for troubleshooting
  - **All Capture Tools**:
    - Auto-append .png extension if not specified
    - Case-insensitive extension check
    - Improved error messages

### Technical Details
- **60-Frame Wait Mechanism**: `ScreenshotFrameUpdate()` method ensures UI is fully rendered
- **EditorPrefs State Persistence**: Survives domain reload when entering Play mode
- **Canvas Overlay Support**: Direct coordinate usage without WorldToScreenPoint conversion
- **Y-Coordinate Fix**: No coordinate transformation needed (both systems use bottom-left origin)
- **Async Workflow**: LLM instructions updated to "WAIT EXACTLY 3 SECONDS" before result retrieval
- All screenshot tools now use proper resolution detection
- Canvas Overlay (Screen Space - Overlay) now correctly captured in Play mode
- Canvas Camera and World Space modes work in both Edit and Play mode

## [1.0.2] - 2025-11-20

### Fixed
- **Windows Compatibility**: Resolved MCP server startup issues on Windows
- **Cinemachine 3.x Support**: Added automatic API detection for Cinemachine 3.0+
  - Package now supports both Cinemachine 2.9.7 and 3.x versions
  - Automatic detection and adaptation to installed version

## [1.0.1] - 2025-11-19

### Added
- **Screenshot Capture Tools**: Three new MCP tools for visual analysis
  - `CaptureGameView`: Capture the Game View window
  - `CaptureSceneView`: Capture the Scene View window
  - `CaptureRegion`: Capture specific regions with coordinates
  - Enables Claude Vision to analyze Unity UI layouts

### Known Issues
- Screenshot capture may not correctly capture Canvas/UI elements (fixed in v1.0.3)

## [1.0.0] - 2025-11-15

### Added
- Initial release of Synaptic AI Pro for Unity
- 235+ professional MCP tools for Unity Editor control
- Natural language interface through Claude AI
- Scene management and GameObject manipulation
- Advanced lighting and rendering controls
- Physics and animation systems
- GOAP AI system with natural language planning
- Comprehensive documentation and examples
- Unity 2022.3+ and Unity 6.0+ support
