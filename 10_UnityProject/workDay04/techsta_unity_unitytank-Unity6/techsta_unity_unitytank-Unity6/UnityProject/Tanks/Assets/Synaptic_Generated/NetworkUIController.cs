using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UIボタンとNetworkManagerを接続するスクリプト
/// </summary>
public class NetworkUIController : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;
    private NetworkManager networkManager;

    private void Awake()
    {
        // ボタンを自動検索
        if (hostButton == null || clientButton == null)
        {
            Button[] buttons = GetComponentsInChildren<Button>();
            foreach (Button button in buttons)
            {
                if (button.gameObject.name.Contains("Host") || button.gameObject.name.Contains("host"))
                {
                    hostButton = button;
                    Debug.Log($"[NetworkUI] Auto-found Host button: {button.gameObject.name}");
                }
                else if (button.gameObject.name.Contains("Client") || button.gameObject.name.Contains("client") || button.gameObject.name.Contains("Join"))
                {
                    clientButton = button;
                    Debug.Log($"[NetworkUI] Auto-found Client button: {button.gameObject.name}");
                }
            }
        }
    }

    private void Start()
    {
        // NetworkManagerを検索
        networkManager = FindFirstObjectByType<NetworkManager>();
        
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager not found in scene!");
            return;
        }

        Debug.Log("✅ NetworkManager found");

        // ボタンにクリックイベントを設定
        if (hostButton != null)
        {
            hostButton.onClick.AddListener(OnHostButtonClick);
            Debug.Log("✅ Host button connected");
        }
        else
        {
            Debug.LogWarning("⚠️ Host button not found!");
        }
        
        if (clientButton != null)
        {
            clientButton.onClick.AddListener(OnClientButtonClick);
            Debug.Log("✅ Client button connected");
        }
        else
        {
            Debug.LogWarning("⚠️ Client button not found!");
        }
    }

    private void OnHostButtonClick()
    {
        Debug.Log("🎮 [NetworkUI] Host button clicked!");
        networkManager.StartHost();
        
        // ボタンを非表示にする
        if (hostButton != null) hostButton.gameObject.SetActive(false);
        if (clientButton != null) clientButton.gameObject.SetActive(false);
    }

    private void OnClientButtonClick()
    {
        Debug.Log("🎮 [NetworkUI] Client button clicked!");
        networkManager.StartClient();
        
        // ボタンを非表示にする
        if (hostButton != null) hostButton.gameObject.SetActive(false);
        if (clientButton != null) clientButton.gameObject.SetActive(false);
    }
}
