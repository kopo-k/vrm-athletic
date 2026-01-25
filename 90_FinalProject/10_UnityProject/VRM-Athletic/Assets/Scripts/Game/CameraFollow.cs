using UnityEngine;

/// <summary>
/// プレイヤーを追従するカメラスクリプト
/// マウス操作で回転可能
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("追従設定")]
    [SerializeField] private float distance = 7f;      // プレイヤーからの距離
    [SerializeField] private float height = 4f;        // プレイヤーからの高さ
    [SerializeField] private float smoothSpeed = 5f;   // 追従の滑らかさ

    [Header("マウス操作設定")]
    [SerializeField] private float mouseSensitivity = 3f;  // マウス感度
    [SerializeField] private float minVerticalAngle = -20f; // 最小垂直角度
    [SerializeField] private float maxVerticalAngle = 60f;  // 最大垂直角度

    [Header("ターゲット")]
    [SerializeField] private Transform target;

    // カメラの回転角度
    private float horizontalAngle = 0f;  // 水平角度（Y軸回転）
    private float verticalAngle = 20f;   // 垂直角度（X軸回転）

    private void Start()
    {
        // カーソルを非表示にしてロック
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        // Escキーでカーソルロックを切り替え
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // クリックでカーソルをロック
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void LateUpdate()
    {
        // ターゲットがない場合は探す
        if (target == null)
        {
            FindPlayer();
            return;
        }

        // カーソルがロックされていない場合はマウス操作を無効化
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        // マウス入力を取得
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 角度を更新（マウス方向に合わせて反転）
        horizontalAngle -= mouseX;
        verticalAngle += mouseY;

        // 垂直角度を制限
        verticalAngle = Mathf.Clamp(verticalAngle, minVerticalAngle, maxVerticalAngle);

        // 角度からカメラの位置を計算
        float angleRad = horizontalAngle * Mathf.Deg2Rad;
        float verticalRad = verticalAngle * Mathf.Deg2Rad;

        // 垂直角度を考慮した距離と高さ
        float horizontalDistance = distance * Mathf.Cos(verticalRad);
        float verticalOffset = distance * Mathf.Sin(verticalRad);

        float offsetX = Mathf.Sin(angleRad) * horizontalDistance;
        float offsetZ = -Mathf.Cos(angleRad) * horizontalDistance;

        Vector3 desiredPosition = target.position + new Vector3(offsetX, height + verticalOffset, offsetZ);

        // スムーズに移動
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.position = smoothedPosition;

        // プレイヤーの方を向く
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }

    /// <summary>
    /// プレイヤーを探す
    /// </summary>
    private void FindPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            target = player.transform;
            Debug.Log("Camera found player");
        }
    }

    /// <summary>
    /// ターゲットを設定（外部から）
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
