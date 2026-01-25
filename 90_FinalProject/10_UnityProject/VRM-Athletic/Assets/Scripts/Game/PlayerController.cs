using UnityEngine;

/// <summary>
/// プレイヤーキャラクターの移動・ジャンプを制御するクラス
/// Character Controllerを使用して物理演算を行う
/// アニメーション制御も含む
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("移動設定")]
    [SerializeField] private float walkSpeed = 3f;      // 歩行速度
    [SerializeField] private float runSpeed = 6f;       // 走り速度
    [SerializeField] private float rotationSpeed = 10f; // 回転速度

    [Header("ジャンプ設定")]
    [SerializeField] private float jumpForce = 8f;      // ジャンプ力
    [SerializeField] private float gravity = 20f;       // 重力

    [Header("地面判定")]
    [SerializeField] private float groundCheckDistance = 0.15f;
    [SerializeField] private LayerMask groundLayer = ~0; // 全レイヤー

    // コンポーネント参照
    private CharacterController characterController;
    private Animator animator;

    // 移動用変数
    private Vector3 moveDirection = Vector3.zero;
    private float verticalVelocity = 0f;
    private bool isGrounded = false;
    private float jumpCooldown = 0f; // ジャンプ直後の地面判定を無効化

    // Animator パラメータのハッシュ（高速化のため）
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int IsGroundedParam = Animator.StringToHash("IsGrounded");
    private static readonly int JumpParam = Animator.StringToHash("Jump");

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Start()
    {
        // Character Controllerを取得
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

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
        // 地面判定（より確実な方法）
        CheckGrounded();

        // 入力を取得
        float horizontal = Input.GetAxis("Horizontal"); // A/D または ←/→
        float vertical = Input.GetAxis("Vertical");     // W/S または ↑/↓

        // カメラの向きを基準に移動方向を計算
        Vector3 inputDirection = Vector3.zero;
        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            // カメラの前方向と右方向を取得（Y成分は無視）
            Vector3 cameraForward = mainCamera.transform.forward;
            Vector3 cameraRight = mainCamera.transform.right;
            cameraForward.y = 0f;
            cameraRight.y = 0f;
            cameraForward.Normalize();
            cameraRight.Normalize();

            // カメラ基準の移動方向
            inputDirection = (cameraForward * vertical + cameraRight * horizontal).normalized;
        }
        else
        {
            // カメラがない場合はワールド座標
            inputDirection = new Vector3(horizontal, 0f, vertical).normalized;
        }

        float inputMagnitude = new Vector2(horizontal, vertical).magnitude;

        // 走り判定（Shift押下）
        bool isRunning = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        // 移動入力がある場合
        if (inputMagnitude >= 0.1f)
        {
            // 移動方向を計算
            moveDirection = inputDirection * currentSpeed;

            // キャラクターを移動方向に向ける
            Quaternion targetRotation = Quaternion.LookRotation(inputDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
        else
        {
            // 入力がない場合は水平移動を停止
            moveDirection = Vector3.zero;
        }

        // ジャンプクールダウンを更新
        if (jumpCooldown > 0f)
        {
            jumpCooldown -= Time.deltaTime;
        }

        // 地面にいる場合（ジャンプ直後は除く）
        if (isGrounded && jumpCooldown <= 0f)
        {
            // 落下速度を完全にリセット
            verticalVelocity = 0f;

            // ジャンプ入力
            if (Input.GetButtonDown("Jump"))
            {
                verticalVelocity = jumpForce;
                jumpCooldown = 0.2f; // 0.2秒間は地面判定を無視
                // ジャンプアニメーションをトリガー
                animator?.SetTrigger(JumpParam);
                Debug.Log($"Jump! Force: {jumpForce}");
            }
        }
        else if (jumpCooldown <= 0f)
        {
            // 空中にいる場合は重力を適用
            verticalVelocity -= gravity * Time.deltaTime;

            // 最大落下速度を制限（地面突き抜け防止）
            if (verticalVelocity < -15f)
            {
                verticalVelocity = -15f;
            }
        }

        // 垂直方向の速度を適用
        moveDirection.y = verticalVelocity;

        // キャラクターを移動
        CollisionFlags flags = characterController.Move(moveDirection * Time.deltaTime);

        // 下方向に衝突したら落下速度をリセット
        if ((flags & CollisionFlags.Below) != 0)
        {
            verticalVelocity = 0f;
        }

        // アニメーションを更新
        UpdateAnimation(inputMagnitude, isRunning);
    }

    /// <summary>
    /// 地面判定（CharacterControllerを優先）
    /// </summary>
    private void CheckGrounded()
    {
        // CharacterControllerの判定を優先
        isGrounded = characterController.isGrounded;

        // CharacterControllerが検出しない場合はRaycastも試す
        if (!isGrounded)
        {
            float checkRadius = characterController.radius * 0.9f;
            Vector3 checkPosition = transform.position + Vector3.up * checkRadius;

            isGrounded = Physics.SphereCast(
                checkPosition,
                checkRadius,
                Vector3.down,
                out RaycastHit hit,
                groundCheckDistance,
                groundLayer
            );
        }
    }

    /// <summary>
    /// アニメーションパラメータを更新
    /// </summary>
    private void UpdateAnimation(float inputMagnitude, bool isRunning)
    {
        if (animator == null) return;

        // Speed: 0 = Idle, 0.5 = Walk, 1 = Run
        float animSpeed = 0f;
        if (inputMagnitude > 0.1f)
        {
            animSpeed = isRunning ? 1f : 0.5f;
        }

        // Speedをスムーズに変化させる
        animator.SetFloat(SpeedParam, animSpeed, 0.1f, Time.deltaTime);
        animator.SetBool(IsGroundedParam, isGrounded);
    }
}
