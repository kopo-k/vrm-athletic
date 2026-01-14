using UnityEngine;
using UnityEngine.UI;
using Complete;

/// <summary>
/// シーン開始時にNetworkGameManagerの参照を自動設定するスクリプト
/// GameManagerオブジェクトにアタッチしてください
/// </summary>
public class NetworkSceneSetup : MonoBehaviour
{
    [Header("Auto Setup Settings")]
    [Tooltip("Awake時に自動的に参照を設定する")]
    public bool autoSetupOnAwake = true;

    [Header("Manual References (Optional)")]
    [Tooltip("手動で指定する場合はここに設定")]
    public CameraControl cameraControlOverride;
    public Text messageTextOverride;
    public Transform[] spawnPointsOverride;
    public GameObject networkUIOverride;

    private void Awake()
    {
        if (autoSetupOnAwake)
        {
            SetupReferences();
        }
    }

    [ContextMenu("Setup References")]
    public void SetupReferences()
    {
        NetworkGameManager networkGM = GetComponent<NetworkGameManager>();
        if (networkGM == null)
        {
            Debug.LogError("[NetworkSceneSetup] NetworkGameManager not found on this GameObject!");
            return;
        }

        Debug.Log("[NetworkSceneSetup] Setting up references...");

        // CameraControl
        if (cameraControlOverride != null)
        {
            networkGM.m_CameraControl = cameraControlOverride;
        }
        else
        {
            CameraControl cc = FindFirstObjectByType<CameraControl>();
            if (cc != null)
            {
                networkGM.m_CameraControl = cc;
                Debug.Log("[NetworkSceneSetup] CameraControl set");
            }
        }

        // MessageText
        if (messageTextOverride != null)
        {
            networkGM.m_MessageText = messageTextOverride;
        }
        else
        {
            GameObject messageCanvas = GameObject.Find("MessageCanvas");
            if (messageCanvas != null)
            {
                Text txt = messageCanvas.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    networkGM.m_MessageText = txt;
                    Debug.Log("[NetworkSceneSetup] MessageText set");
                }
            }
        }

        // SpawnPoints
        if (spawnPointsOverride != null && spawnPointsOverride.Length > 0)
        {
            networkGM.m_SpawnPoints = spawnPointsOverride;
        }
        else
        {
            GameObject sp1 = GameObject.Find("SpawnPoint1");
            GameObject sp2 = GameObject.Find("SpawnPoint2");
            if (sp1 != null && sp2 != null)
            {
                networkGM.m_SpawnPoints = new Transform[] { sp1.transform, sp2.transform };
                Debug.Log("[NetworkSceneSetup] SpawnPoints set");
            }
        }

        // NetworkUI
        if (networkUIOverride != null)
        {
            networkGM.m_NetworkUI = networkUIOverride;
        }
        else
        {
            // NetworkUIという名前のCanvasを探す
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (canvas.gameObject.name == "NetworkUI")
                {
                    networkGM.m_NetworkUI = canvas.gameObject;
                    Debug.Log("[NetworkSceneSetup] NetworkUI set");
                    break;
                }
            }
        }

        Debug.Log("[NetworkSceneSetup] Setup complete!");
    }
}
