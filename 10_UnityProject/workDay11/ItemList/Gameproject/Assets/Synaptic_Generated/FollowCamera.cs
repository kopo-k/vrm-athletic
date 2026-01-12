using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("追従設定")]
    public Transform target; // 追従するターゲット（VRMキャラクター）
    public Vector3 offset = new Vector3(0, 2, -3); // カメラのオフセット位置
    
    [Header("追従の滑らかさ")]
    public float smoothSpeed = 5f; // 小さいほど遅く追従
    
    [Header("マウス操作")]
    public float mouseSensitivity = 3f; // マウス感度
    public float minVerticalAngle = -20f; // 上下の角度制限（下）
    public float maxVerticalAngle = 60f; // 上下の角度制限（上）
    
    private float currentX = 0f; // 水平回転角度
    private float currentY = 20f; // 垂直回転角度

    void LateUpdate()
    {
        if (target == null) return;

        // マウス入力を取得
        currentX += Input.GetAxis("Mouse X") * mouseSensitivity;
        currentY -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // 垂直角度を制限
        currentY = Mathf.Clamp(currentY, minVerticalAngle, maxVerticalAngle);

        // 回転を計算
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        
        // カメラ位置を計算（ターゲットの周りを回転）
        Vector3 desiredPosition = target.position + rotation * offset;

        // 滑らかに移動
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.position = smoothedPosition;

        // 常にターゲットを見る
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }
}
