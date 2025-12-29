using UnityEngine;

public class MazePlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 120f;
    public float mouseSensitivity = 2f;
    public float mouseDeadzone = 0.01f;
    
    [Header("References")]
    public Camera playerCamera;
    
    private Rigidbody rb;
    private float verticalRotation = 0f;
    private bool cursorLocked = true;
    private bool isInitialized = false;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.freezeRotation = true;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        
        if (playerCamera != null)
        {
            playerCamera.transform.SetParent(transform);
            playerCamera.transform.localPosition = new Vector3(0, 0.5f, 0);
            playerCamera.transform.localRotation = Quaternion.identity;
        }
        
        // 初期化完了まで少し待つ（マウス入力の安定化）
        Invoke("Initialize", 0.1f);
    }
    
    void Initialize()
    {
        LockCursor();
        isInitialized = true;
    }
    
    void Update()
    {
        if (!isInitialized) return;
        
        HandleCursorLock();
        
        if (cursorLocked)
        {
            HandleMouseLook();
        }
    }
    
    void FixedUpdate()
    {
        if (!isInitialized) return;
        HandleMovement();
    }
    
    void HandleMovement()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        
        Vector3 moveDirection = transform.forward * vertical + transform.right * horizontal;
        moveDirection.y = 0;
        if (moveDirection.magnitude > 0.1f)
        {
            moveDirection = moveDirection.normalized * moveSpeed;
        }
        else
        {
            moveDirection = Vector3.zero;
        }
        
        rb.linearVelocity = new Vector3(moveDirection.x, rb.linearVelocity.y, moveDirection.z);
    }
    
    void HandleMouseLook()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
        
        // デッドゾーンを適用して小さな動きを無視
        if (Mathf.Abs(mouseX) < mouseDeadzone) mouseX = 0f;
        if (Mathf.Abs(mouseY) < mouseDeadzone) mouseY = 0f;
        
        if (Mathf.Abs(mouseX) > 0.001f)
        {
            transform.Rotate(0, mouseX, 0);
        }
        
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -80f, 80f);
        
        if (playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
        }
    }
    
    void HandleCursorLock()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            cursorLocked = !cursorLocked;
            if (cursorLocked)
                LockCursor();
            else
                UnlockCursor();
        }
        
        if (Input.GetMouseButtonDown(0) && !cursorLocked)
        {
            LockCursor();
        }
    }
    
    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursorLocked = true;
    }
    
    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorLocked = false;
    }
}
