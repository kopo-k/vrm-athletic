using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private Animator animator;
    
    [Header("移動速度")]
    public float walkSpeed = 2f;
    public float runSpeed = 4f;
    public float sprintSpeed = 6f;
    
    [Header("回転速度")]
    public float rotationSpeed = 10f;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        float speed = 0f;
        
        // WASDキーで移動方向を取得
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        
        if (horizontal != 0 || vertical != 0)
        {
            // 移動速度を決定
            if (Input.GetKey(KeyCode.LeftShift))
            {
                speed = sprintSpeed; // Shiftでダッシュ
            }
            else if (Input.GetKey(KeyCode.LeftControl))
            {
                speed = runSpeed; // Ctrlで走る
            }
            else
            {
                speed = walkSpeed; // 通常は歩き
            }
            
            // 移動方向のベクトルを作成
            Vector3 moveDirection = new Vector3(horizontal, 0, vertical).normalized;
            
            // キャラクターを移動方向に回転
            if (moveDirection != Vector3.zero)
            {
                Quaternion toRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                transform.rotation = Quaternion.Lerp(transform.rotation, toRotation, rotationSpeed * Time.deltaTime);
            }
            
            // 実際に移動
            transform.position += moveDirection * speed * Time.deltaTime;
        }
        
        // Animatorにスピードを送る
        animator.SetFloat("Speed", speed);
    }
}