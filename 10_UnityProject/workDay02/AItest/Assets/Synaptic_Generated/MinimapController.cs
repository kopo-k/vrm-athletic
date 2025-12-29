using UnityEngine;
using UnityEngine.UI;

public class MinimapController : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Camera minimapCamera;
    public RawImage minimapImage;
    
    [Header("Settings")]
    public float height = 50f;
    public float zoomLevel = 30f;
    public bool rotateWithPlayer = false;
    
    private RenderTexture minimapTexture;
    
    void Start()
    {
        // プレイヤーを自動検索
        if (player == null)
        {
            GameObject playerObj = GameObject.Find("Player");
            if (playerObj != null) player = playerObj.transform;
        }
        
        // ミニマップカメラを自動検索
        if (minimapCamera == null)
        {
            GameObject camObj = GameObject.Find("MinimapCamera");
            if (camObj != null) minimapCamera = camObj.GetComponent<Camera>();
        }
        
        // RenderTextureを作成
        minimapTexture = new RenderTexture(256, 256, 16);
        minimapTexture.Create();
        
        if (minimapCamera != null)
        {
            minimapCamera.targetTexture = minimapTexture;
            minimapCamera.orthographicSize = zoomLevel;
        }
        
        // ミニマップUIを自動検索または作成
        if (minimapImage == null)
        {
            GameObject imgObj = GameObject.Find("MinimapImage");
            if (imgObj != null) minimapImage = imgObj.GetComponent<RawImage>();
        }
        
        if (minimapImage != null)
        {
            minimapImage.texture = minimapTexture;
        }
    }
    
    void LateUpdate()
    {
        if (player == null || minimapCamera == null) return;
        
        // カメラをプレイヤーの上に追従
        Vector3 newPos = player.position;
        newPos.y = height;
        minimapCamera.transform.position = newPos;
        
        // プレイヤーの向きに合わせて回転（オプション）
        if (rotateWithPlayer)
        {
            minimapCamera.transform.rotation = Quaternion.Euler(90f, player.eulerAngles.y, 0f);
        }
    }
    
    void OnDestroy()
    {
        if (minimapTexture != null)
        {
            minimapTexture.Release();
        }
    }
}
