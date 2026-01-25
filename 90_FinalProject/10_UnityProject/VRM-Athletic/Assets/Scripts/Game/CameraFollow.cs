using UnityEngine;

/// <summary>
/// プレイヤーを追従するカメラスクリプト
/// 常にプレイヤーの後ろから見る
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("追従設定")]
    [SerializeField] private float distance = 7f;      // プレイヤーからの距離
    [SerializeField] private float height = 4f;        // プレイヤーからの高さ
    [SerializeField] private float smoothSpeed = 5f;   // 追従の滑らかさ

    [Header("カメラ方向")]
    [Tooltip("カメラがプレイヤーを見る方向（Y軸回転角度）0=後ろから、90=右から、180=前から、270=左から")]
    [Range(0, 360)]
    [SerializeField] private float cameraAngle = 0f;

    [Header("ターゲット")]
    [SerializeField] private Transform target;

    private void LateUpdate()
    {
        // ターゲットがない場合は探す
        if (target == null)
        {
            FindPlayer();
            return;
        }

        // 角度からカメラの位置を計算
        float angleRad = cameraAngle * Mathf.Deg2Rad;
        float offsetX = Mathf.Sin(angleRad) * distance;
        float offsetZ = -Mathf.Cos(angleRad) * distance;

        Vector3 desiredPosition = target.position + new Vector3(offsetX, height, offsetZ);

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
