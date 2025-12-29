# Synaptic Pro - MCP Unity Integration
# Synaptic Pro - Unity MCP 統合

[English](#english) | [日本語](#japanese)

---

<a name="english"></a>
## English

Transform your Unity development with AI-powered automation and natural language control. With 187+ professional tools, Synaptic Pro revolutionizes game creation through seamless AI integration.

### Key Features

#### **AI Integration**
- **Multi-Platform Support**: Claude Desktop, ChatGPT Desktop, Claude Code CLI, Gemini CLI
- **Natural Language Control**: Control Unity entirely through plain English commands
- **Real-time Synchronization**: Instant feedback between Unity and AI clients
- **One-Click Setup**: Automatic configuration for all supported AI platforms

#### **187+ Professional Tools**
- **GameObject & Scene Management** (15+ tools): Create, transform, and manage objects
- **UI Creation Suite** (16+ tools): Generate complete UI systems from descriptions
- **Visual Enhancement** (24+ tools): Weather, post-processing, lighting, shaders
- **Audio Revolution** (15+ tools): 3D spatial audio, adaptive music, effects
- **AI Systems** (13+ tools): GOAP, behavior trees, pathfinding, ML integration
- **Development Tools** (20+ tools): Project analysis, batch operations, debugging
- **Physics & Animation** (18+ tools): Rigidbodies, colliders, animators, timeline
- **Input System** (13+ tools): Custom mapping, gestures, accessibility

### Advanced AI Systems

#### **GOAP (Goal-Oriented Action Planning)**
Define complex AI behaviors using natural language:
```
"Create a guard AI that patrols waypoints, investigates noises, 
and calls for backup when health is below 30%"
```

Pre-built templates include:
- Guard AI (patrol, investigate, engage)
- Collector AI (gather resources, deliver)
- Hunter AI (track and pursue targets)
- Companion AI (follow and assist player)
- Merchant AI (trading and economy)

#### **Smart Features**
- **Shader Auto-Generation**: Prevents pink materials by detecting pipeline
- **Unified Weather System**: Movie-quality weather with single commands
- **Project Planning AI**: Breaks down complex tasks automatically
- **Operation History**: Full undo/redo support with tracking

### Technical Architecture

- **WebSocket Server**: Real-time bidirectional communication (port 8080)
- **Main Thread Dispatcher**: Safe Unity API calls from async operations
- **State Management**: Comprehensive tracking and inspection
- **Auto-Reconnection**: Maintains connection stability
- **Error Recovery**: Comprehensive error handling and reporting

### Requirements

- Unity 2022.3 LTS or higher
- Windows 10/11, macOS 10.15+, or Linux Ubuntu 20.04+
- Node.js 16+ (for MCP server)
- 4GB RAM minimum (8GB recommended)

### Quick Start

1. **Installation**
   ```
   1. Import from Unity Asset Store
   2. Go to: Synaptic Pro > Setup
   3. Click "Complete MCP Setup"
   4. Click "Start AI Connection"
   ```

2. **Basic Usage**
   ```
   "Create a red cube at position 0,5,0"
   "Add rigidbody to the selected object"
   "Create sunny weather with clouds"
   "Generate a UI health bar in top left"
   ```

3. **Advanced Examples**
   ```
   "Create a complete third-person controller with animations"
   "Setup an inventory system with 5x5 grid"
   "Create a day-night cycle that lasts 10 minutes"
   "Generate an enemy spawner that increases difficulty over time"
   ```

### Troubleshooting

**Newtonsoft.Json Missing?**
```
Package Manager > + > Add package by name
Enter: com.unity.nuget.newtonsoft-json
```

**Connection Issues?**
1. Check Node.js: `node --version`
2. Verify server status in Unity console
3. Restart: `Synaptic Pro > Restart Server`

### Links

- **Discord Community**: [Join us](https://discord.com/invite/Y2nUyWvqR3)

---

<a name="japanese"></a>
## 日本語

187以上のプロフェッショナルツールで、AIパワーによる自動化と自然言語制御でUnity開発を革新します。Synaptic ProはAI統合によるゲーム制作の新しい形を提供します。

### 主な機能

#### **AI統合**
- **マルチプラットフォーム対応**: Claude Desktop、ChatGPT Desktop、Claude Code CLI、Gemini CLI
- **自然言語制御**: 日本語や英語の普通の文章でUnityを完全制御
- **リアルタイム同期**: UnityとAIクライアント間の即時フィードバック
- **ワンクリック設定**: 対応AI全プラットフォームの自動設定

#### **187以上のプロフェッショナルツール**
- **GameObject＆シーン管理** (15以上): オブジェクトの作成、変形、管理
- **UI作成スイート** (16以上): 説明文から完全なUIシステムを生成
- **ビジュアル強化** (24以上): 天候、ポストプロセス、ライティング、シェーダー
- **オーディオ革命** (15以上): 3D空間音響、アダプティブ音楽、エフェクト
- **AIシステム** (13以上): GOAP、ビヘイビアツリー、パスファインディング、ML統合
- **開発ツール** (20以上): プロジェクト分析、バッチ操作、デバッグ
- **物理＆アニメーション** (18以上): Rigidbody、コライダー、アニメーター、タイムライン
- **入力システム** (13以上): カスタムマッピング、ジェスチャー、アクセシビリティ

### 高度なAIシステム

#### **GOAP（ゴール指向行動計画）**
自然言語で複雑なAI行動を定義：
```
"巡回して、物音を調査し、体力が30％以下になったら
援軍を呼ぶ警備AIを作成して"
```

組み込みテンプレート：
- ガードAI（巡回、調査、交戦）
- コレクターAI（リソース収集、配送）
- ハンターAI（追跡と追撃）
- コンパニオンAI（プレイヤーの追従と支援）
- 商人AI（取引と経済）

#### **スマート機能**
- **シェーダー自動生成**: パイプライン検出でピンクマテリアルを防止
- **統合天候システム**: 単一コマンドで映画品質の天候
- **プロジェクト計画AI**: 複雑なタスクを自動分解
- **操作履歴**: 完全なアンドゥ/リドゥサポート

### 技術アーキテクチャ

- **WebSocketサーバー**: リアルタイム双方向通信（ポート8080）
- **メインスレッドディスパッチャー**: 非同期操作からの安全なUnity API呼び出し
- **状態管理**: 包括的な追跡と検査
- **自動再接続**: 接続の安定性を維持
- **エラー回復**: 包括的なエラー処理とレポート

### 必要環境

- Unity 2022.3 LTS以上
- Windows 10/11、macOS 10.15以上、またはLinux Ubuntu 20.04以上
- Node.js 16以上（MCPサーバー用）
- 最小4GB RAM（推奨8GB）

### クイックスタート

1. **インストール**
   ```
   1. Unity Asset Storeからインポート
   2. メニュー：Synaptic Pro > Setup
   3. 「Complete MCP Setup」をクリック
   4. 「Start AI Connection」をクリック
   ```

2. **基本的な使い方**
   ```
   "位置0,5,0に赤いキューブを作成"
   "選択中のオブジェクトにRigidbodyを追加"
   "雲のある晴天を作成"
   "左上にUIヘルスバーを生成"
   ```

3. **高度な例**
   ```
   "アニメーション付きの完全な三人称コントローラーを作成"
   "5x5グリッドのインベントリシステムをセットアップ"
   "10分間続く昼夜サイクルを作成"
   "時間とともに難易度が上がる敵スポナーを生成"
   ```

### トラブルシューティング

**Newtonsoft.Jsonが見つからない？**
```
Package Manager > + > Add package by name
入力: com.unity.nuget.newtonsoft-json
```

**接続の問題？**
1. Node.jsを確認: `node --version`
2. Unityコンソールでサーバーステータスを確認
3. 再起動: `Synaptic Pro > Restart Server`

### リンク

- **Discordコミュニティ**: [参加する](https://discord.com/invite/Y2nUyWvqR3)

---

## Why Choose Synaptic Pro?

- **Save 80% Development Time**: Automate repetitive tasks
- **No Coding Required**: Natural language commands
- **Enterprise-Ready**: Production-quality code generation
- **Active Community**: Regular updates and support
- **Future-Proof**: AI-powered development is the future

---

**Transform your Unity workflow today with Synaptic Pro!**

© 2024 Synaptic Team. All rights reserved.
