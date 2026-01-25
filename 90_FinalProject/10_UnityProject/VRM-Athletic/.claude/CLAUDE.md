# VRM Athletic Time Attack - プロジェクト設定
タスクが終わったらTODO.mdに書き込むこと
## プロジェクト概要

VRMアバターを操作してアスレチックコースを攻略するシングルプレイ用タイムアタックゲーム。

- **ジャンル**: 3Dアクション・アスレチック
- **プレイ人数**: 1人（シングルプレイ）
- **プラットフォーム**: PC（Windows）
- **開発期間**: 14日間

## 技術スタック

- **Unity**: 2021.3 LTS または 2022.3 LTS
- **言語**: C#
- **VRM**: UniVRM
- **UI**: TextMeshPro
- **Backend**: Supabase（PostgreSQL）
- **API通信**: UnityWebRequest（REST API / PostgREST）

## ゲームフロー

1. タイトル画面
2. キャラクター選択（男性/女性）
3. カウントダウン → ゲーム開始
4. アスレチックコース攻略
5. ゴール → クリアタイム計測
6. スコアをSupabaseに保存
7. リザルト画面（自己記録・ランキング表示）

## ディレクトリ構造

```
Assets/
├─ Scenes/
│  ├─ Title.unity
│  ├─ CharacterSelect.unity
│  ├─ Game.unity
│  └─ Result.unity
│
├─ Scripts/
│  ├─ API/
│  │  ├─ SupabaseConfig.cs    // URL / API Key
│  │  └─ ScoreApi.cs          // POST / GET処理
│  │
│  ├─ Game/
│  │  ├─ PlayerController.cs  // キャラクター操作
│  │  ├─ GameManager.cs       // ゲーム進行管理
│  │  ├─ Timer.cs             // タイム計測
│  │  └─ GoalTrigger.cs       // ゴール判定
│  │
│  ├─ Character/
│  │  ├─ CharacterData.cs     // キャラクター種別情報
│  │  └─ CharacterSpawner.cs  // キャラクター生成
│  │
│  ├─ UI/
│  │  ├─ TitleUI.cs
│  │  ├─ CharacterSelectUI.cs
│  │  ├─ ResultUI.cs
│  │  └─ RankingUI.cs
│  │
│  └─ Common/
│     ├─ GameData.cs          // 共有データ管理
│     ├─ Constants.cs         // 定数定義
│     └─ JsonHelper.cs        // JSON処理
│
├─ Prefabs/
│  ├─ Characters/
│  │  ├─ MaleCharacter.prefab
│  │  └─ FemaleCharacter.prefab
│  └─ UI.prefab
│
├─ Materials/
├─ UI/
└─ Resources/
```

## Supabase設定

### scoresテーブル

| column         | type      | note            |
|----------------|-----------|-----------------|
| id             | bigint    | PK / auto       |
| player_name    | text      | 表示名          |
| character_type | text      | male / female   |
| stage_name     | text      | Stage1          |
| clear_time     | float     | 秒              |
| created_at     | timestamp | default now()   |

### API通信

**共通ヘッダー:**
```
apikey: API_KEY
Authorization: Bearer API_KEY
Content-Type: application/json
```

**スコア保存 (POST):**
```
POST {URL}/rest/v1/scores
```

**ランキング取得 (GET):**
```
GET {URL}/rest/v1/scores?select=*&order=clear_time.asc&limit=10
```

## 開発ルール

### 必須機能（優先度高）
- VRM表示
- キャラクター選択（男性/女性）
- キャラクター操作（移動・ジャンプ）
- タイム計測
- ゴール判定
- スコア保存（Supabase）
- ランキング表示

### 省略可能（時間があれば）
- 複数ステージ
- 高度な演出

### 実装ルール
- 通信失敗してもゲーム進行は止めない
- スコア保存はゴール時1回のみ
- ランキング取得はResult画面表示時
- ユーザー認証は行わない（匿名スコア保存）

### やらないこと
- Auth（認証）
- ユーザー削除
- UPDATE処理
- 重複チェック
- 高度なセキュリティ

## コーディング規約

- C# 命名規則に従う
  - クラス名: PascalCase
  - メソッド名: PascalCase
  - 変数名: camelCase
  - 定数: UPPER_SNAKE_CASE
- シーン間データ共有は `GameData.cs` を使用
- API関連処理は `Scripts/API/` に集約
- コルーチンでの非同期処理を使用

## ビルド・実行

```bash
# Unityプロジェクトを開く
# Unity Hub → Open → このフォルダを選択
```

## 参考リンク

- [UniVRM](https://github.com/vrm-c/UniVRM)
- [Supabase Docs](https://supabase.com/docs)
- [UnityWebRequest](https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequest.html)

## ノート・ガイド

| ファイル | 内容 |
|----------|------|
| [notes/UniVRM_Setup.md](notes/UniVRM_Setup.md) | UniVRM導入手順 |
| [notes/Supabase_APIKey.md](notes/Supabase_APIKey.md) | Supabase URL・API Key取得方法 |

### Obsidian ノート（外部）

| ファイル | 内容 |
|----------|------|
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\Unity_シーン作成ガイド.md` | シーン作成手順 |
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\Title画面作成ガイド.md` | Title画面の作成手順 |
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\CharacterSelect画面作成ガイド.md` | CharacterSelect画面の作成手順 |
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\VRMキャラクター設定ガイド.md` | VRMキャラクター設定手順 |
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\VRMアニメーション設定ガイド.md` | アニメーション・プレハブ化 |
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\Platformer_Deathrun_アセットガイド.md` | 購入アセットの使い方 |
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\タイマー_ゴール_Result設定ガイド.md` | タイマー・ゴール設定 |
| `C:\Users\kopo-\Documents\Obsidian Vault\techStadium\Result画面作成ガイド.md` | Result画面作成（画像版） |

## 環境変数

API Keyは `.env` ファイルで管理（Git管理対象外）

```bash
# .env.example をコピーして .env を作成
cp .env.example .env
```
