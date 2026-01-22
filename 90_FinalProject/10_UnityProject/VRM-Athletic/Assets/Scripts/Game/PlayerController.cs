using UnityEngine;

/// <summary>
/// プレイヤーキャラクターの移動・ジャンプを制御するクラス
/// Character Controllerを使用して物理演算を行う
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("移動設定")]
    [SerializeField] private float moveSpeed = 5f;      // 移動速度
    [SerializeField] private float rotationSpeed = 10f; // 回転速度

    [Header("ジャンプ設定")]
    [SerializeField] private float jumpForce = 8f;      // ジャンプ力
    [SerializeField] private float gravity = 20f;       // 重力

    // コンポーネント参照
    private CharacterController characterController;

    // 移動用変数
    private Vector3 moveDirection = Vector3.zero;
    private float verticalVelocity = 0f;

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Start()
    {
        // Character Controllerを取得
        characterController = GetComponent<CharacterController>();

        if (characterController == null)
        {
            Debug.LogError("CharacterController が見つかりません。アタッチしてください。");
        }
    }

    /// <summary>
    /// 毎フレーム呼ばれる更新処理
    /// </summary>
    private void Update()
    {
        // 入力を取得
        float horizontal = Input.GetAxis("Horizontal"); // A/D または ←/→
        float vertical = Input.GetAxis("Vertical");     // W/S または ↑/↓

        // カメラの向きを基準に移動方向を計算
        Vector3 inputDirection = new Vector3(horizontal, 0f, vertical).normalized;

        // 移動入力がある場合
        if (inputDirection.magnitude >= 0.1f)
        {
            // 移動方向を計算
            moveDirection = inputDirection * moveSpeed;

            // キャラクターを移動方向に向ける
            Quaternion targetRotation = Quaternion.LookRotation(inputDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
        else
        {
            // 入力がない場合は水平移動を停止
            moveDirection = Vector3.zero;
        }

        // 地面にいる場合
        if (characterController.isGrounded)
        {
            // 落下速度をリセット
            verticalVelocity = -1f;

            // ジャンプ入力
            if (Input.GetButtonDown("Jump"))
            {
                verticalVelocity = jumpForce;
            }
        }
        else
        {
            // 空中にいる場合は重力を適用
            verticalVelocity -= gravity * Time.deltaTime;
        }

        // 垂直方向の速度を適用
        moveDirection.y = verticalVelocity;

        // キャラクターを移動
        characterController.Move(moveDirection * Time.deltaTime);
    }
}
