# VRM Athletic - 作業手順チェックリスト

## Phase 1: プロジェクト準備（1〜2日目）

- [x] Unityプロジェクト作成
- [x] ディレクトリ構造作成
- [x] .claude設定ファイル作成
- [x] UniVRM導入（パッケージインポート）
- [x] Supabase設定
  - [x] Supabaseプロジェクト作成
  - [x] scoresテーブル作成
  - [x] RLS設定（INSERT/SELECT許可）
  - [x] URL・API Key取得
- [x] シーン作成
  - [x] Title.unity
  - [x] CharacterSelect.unity
  - [x] Game.unity
  - [x] Result.unity

## Phase 2: キャラクター・基本機能（3〜6日目）

- [ ] VRMキャラクター準備
  - [ ] 男性キャラクターVRM用意
  - [ ] 女性キャラクターVRM用意
  - [ ] プレハブ化（MaleCharacter.prefab / FemaleCharacter.prefab）
- [ ] Scripts/Common 実装
  - [ ] GameData.cs（共有データ管理）
  - [ ] Constants.cs（定数定義）
- [ ] Scripts/Character 実装
  - [ ] CharacterData.cs（キャラクター種別情報）
  - [ ] CharacterSpawner.cs（キャラクター生成）
- [ ] Scripts/Game 実装
  - [ ] PlayerController.cs（移動・ジャンプ）
  - [ ] Timer.cs（タイム計測）
  - [ ] GoalTrigger.cs（ゴール判定）
  - [ ] GameManager.cs（ゲーム進行管理）

## Phase 3: API・画面遷移（7〜10日目）

- [ ] Scripts/API 実装
  - [ ] SupabaseConfig.cs（URL/API Key設定）
  - [ ] ScoreApi.cs（POST/GET処理）
- [ ] Scripts/UI 実装
  - [x] TitleUI.cs
  - [ ] CharacterSelectUI.cs
  - [ ] ResultUI.cs
  - [ ] RankingUI.cs
- [ ] 画面遷移実装
  - [x] Title → CharacterSelect
  - [ ] CharacterSelect → Game
  - [ ] Game → Result
  - [ ] Result → Title（リトライ）

## Phase 4: ステージ・UI調整（11〜12日目）

- [ ] アスレチックコース作成
  - [ ] スタート地点
  - [ ] ジャンプ台
  - [ ] 移動する足場
  - [ ] 障害物
  - [ ] ゴール地点
- [ ] UI調整
  - [x] タイトル画面デザイン
  - [ ] キャラ選択画面デザイン
  - [ ] ゲーム中UI（タイマー表示）
  - [ ] リザルト画面デザイン
- [ ] 演出追加（余裕があれば）
  - [ ] カウントダウン演出
  - [ ] ゴール演出
  - [ ] SE/BGM

## Phase 5: テスト・デバッグ（13日目）

- [ ] 動作テスト
  - [ ] キャラクター選択の動作確認
  - [ ] ゲームプレイの動作確認
  - [ ] スコア保存の動作確認
  - [ ] ランキング取得の動作確認
- [ ] バグ修正
- [ ] 通信エラー時の挙動確認

## Phase 6: 仕上げ・提出（14日目）

- [ ] 最終テスト
- [ ] ビルド作成
- [ ] 資料作成
- [ ] 提出

---

## 現在の進捗

**現在のフェーズ**: Phase 2 / Phase 3 並行
**次のタスク**: CharacterSelectシーン UI構築 & CharacterSelectUI.cs 実装

---

## メモ

- 通信失敗してもゲーム進行は止めない
- スコア保存はゴール時1回のみ
- 認証機能は実装しない
