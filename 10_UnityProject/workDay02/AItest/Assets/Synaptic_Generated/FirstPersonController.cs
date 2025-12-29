using UnityEngine;

public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.5f;
    
    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;
    
    [Header("References")]
    public Transform cameraTransform;
    
    private Rigidbody rb;
    private float rotationX = 0f;
    private bool isCursorLocked = true;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        // カメラが設定されていない場合、子のカメラを探す
        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cameraTransform = cam.transform;
            }
            else
            {
                // メインカメラを使用
                cam = Camera.main;
                if (cam != null)
                {
                    cameraTransform = cam.transform;
                    cameraTransform.SetParent(transform);
                    cameraTransform.localPosition = new Vector3(0, 0.5f, 0);
                }
            }
        }
        
        LockCursor();
    }
    
    void Update()
    {
        HandleMouseLook();
        HandleCursorLock();
    }
    
    void FixedUpdate()
    {
        HandleMovement();
    }
    
    void HandleMouseLook()
    {
        if (!isCursorLocked) return;
        
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // 左右回転（Y軸）
        transform.Rotate(Vector3.up * mouseX);
        
        // 上下回転（X軸）- カメラのみ
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -maxLookAngle, maxLookAngle);
        
        if (cameraTransform != null)
        {
            cameraTransform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        }
    }
    
    void HandleMovement()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        
        Vector3 moveDirection = transform.right * horizontal + transform.forward * vertical;
        moveDirection.y = 0;
        moveDirection.Normalize();
        
        float currentSpeed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed *= sprintMultiplier;
        }
        
        Vector3 velocity = moveDirection * currentSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;
    }
    
    void HandleCursorLock()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            isCursorLocked = !isCursorLocked;
            if (isCursorLocked)
            {
                LockCursor();
            }
            else
            {
                UnlockCursor();
            }
        }
    }
    
    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        isCursorLocked = true;
    }
    
    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        isCursorLocked = false;
    }
}
