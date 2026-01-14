using Fusion;
using UnityEngine;

namespace Complete
{
    /// <summary>
    /// ネットワーク経由で送信される入力データ構造体
    /// 【ポイント】INetworkInputを実装してFusionで入力を同期
    /// </summary>
    public struct NetworkInputData : INetworkInput
    {
        // TankMovement用の入力
        public Vector2 movementInput;  // x: 左右回転, y: 前後移動
        
        // TankShooting用の入力（ボタンの状態を詳細に追跡）
        public NetworkBool fireButtonDown;    // 射撃ボタンが押された瞬間
        public NetworkBool fireButtonUp;      // 射撃ボタンが離された瞬間
        public NetworkBool fireButton;        // 射撃ボタンが押されている
    }
}
