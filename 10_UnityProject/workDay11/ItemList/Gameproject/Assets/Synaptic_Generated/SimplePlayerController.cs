using UnityEngine;

public class SimplePlayerController : MonoBehaviour
{
    private Animator anim;
    public float walkSpeed = 2f;
    public float runSpeed = 4f;
    public float sprintSpeed = 6f;
    public float rotationSpeed = 10f; // キャラクターの回転速度
    
    [Header("ジャンプ設定")]
    public float jumpForce = 5f; // ジャンプの高さ
    public float gravity = -15f; // 重力の強さ
    
    private Transform cameraTransform; // カメラの参照
    private float verticalVelocity = 0f; // 垂直方向の速度
    private bool isGrounded = true; // 地面についているか

    void Start()
    {
        anim = GetComponent<Animator>();
        // メインカメラを取得
        cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        // 地面判定（簡易版）
        CheckGrounded();
        
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float speed = 0f;

        if (h != 0 || v != 0)
        {
            // 移動速度を決定
            if (Input.GetKey(KeyCode.LeftShift))
                speed = sprintSpeed;
            else if (Input.GetKey(KeyCode.LeftControl))
                speed = runSpeed;
            else
                speed = walkSpeed;

            // カメラの向きを基準に移動方向を計算
            Vector3 cameraForward = cameraTransform.forward;
            Vector3 cameraRight = cameraTransform.right;
            
            // Y軸（高さ）の影響を除去（水平方向のみ）
            cameraForward.y = 0;
            cameraRight.y = 0;
            cameraForward.Normalize();
            cameraRight.Normalize();
            
            // カメラ基準の移動方向を計算
            Vector3 moveDirection = (cameraForward * v + cameraRight * h).normalized;
            
            if (moveDirection != Vector3.zero)
            {
                // キャラクターを移動方向に回転
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                
                // 水平移動
                transform.position += moveDirection * speed * Time.deltaTime;
            }
        }

        // ジャンプ処理
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            verticalVelocity = jumpForce;
            isGrounded = false;
        }

        // 重力を適用
        if (!isGrounded)
        {
            verticalVelocity += gravity * Time.deltaTime;
        }
        else
        {
            verticalVelocity = 0f;
        }

        // 垂直移動を適用
        transform.position += Vector3.up * verticalVelocity * Time.deltaTime;

        if (anim != null)
            anim.SetFloat("Speed", speed);
    }

    void CheckGrounded()
    {
        // 地面判定（Raycastを使用）
        Ray ray = new Ray(transform.position + Vector3.up * 0.1f, Vector3.down);
        isGrounded = Physics.Raycast(ray, 0.2f);
        
        // 地面より下に行かないようにする
        if (transform.position.y < 0f)
        {
            transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
            verticalVelocity = 0f;
            isGrounded = true;
        }
    }

    // この部分は既に存在するので、70-74行目を以下に書き換えてください：
    // if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
    // {
    //     verticalVelocity = jumpForce;
    //     isGrounded = false;
    //     if (anim != null)
    //         anim.SetTrigger("Jump"); // この行を追加
    // }
}
