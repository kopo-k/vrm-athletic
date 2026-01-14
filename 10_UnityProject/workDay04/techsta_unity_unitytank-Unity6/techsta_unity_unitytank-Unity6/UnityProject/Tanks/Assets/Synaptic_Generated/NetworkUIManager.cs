using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Complete
{
    /// <summary>
    /// UIボタンとNetworkGameManagerを接続するスクリプト
    /// </summary>
    public class NetworkUIManager : MonoBehaviour
    {
        public Button hostButton;
        public Button clientButton;
        private NetworkGameManager networkGameManager;

        private void Awake()
        {
            Debug.Log($"[NetworkUIManager] Awake called on {gameObject.name}");

            // ボタン以外のImageのraycastTargetを無効にする（クリックブロック防止）
            DisableNonButtonRaycasts();

            // ボタンを自動検索
            if (hostButton == null || clientButton == null)
            {
                Button[] buttons = GetComponentsInChildren<Button>(true); // includeInactive=true
                Debug.Log($"[NetworkUIManager] Found {buttons.Length} buttons in children");

                foreach (Button button in buttons)
                {
                    Debug.Log($"[NetworkUIManager] Checking button: {button.gameObject.name}");

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

        /// <summary>
        /// ボタン以外のImageのraycastTargetを無効にして、クリックがボタンに届くようにする
        /// </summary>
        private void DisableNonButtonRaycasts()
        {
            // 全ての子Imageを取得
            Image[] allImages = GetComponentsInChildren<Image>(true);

            foreach (Image img in allImages)
            {
                // ボタンコンポーネントを持っていない場合はraycastTargetを無効にする
                Button btn = img.GetComponent<Button>();
                if (btn == null)
                {
                    // パネルや背景のraycastTargetを無効にする
                    if (img.raycastTarget)
                    {
                        img.raycastTarget = false;
                        Debug.Log($"[NetworkUIManager] Disabled raycastTarget on: {img.gameObject.name}");
                    }
                }
                else
                {
                    // ボタンの場合はraycastTargetを有効にする
                    img.raycastTarget = true;
                    Debug.Log($"[NetworkUIManager] Enabled raycastTarget on button: {img.gameObject.name}");
                }
            }
        }

        private void Start()
        {
            Debug.Log($"[NetworkUIManager] Start called on {gameObject.name}");

            // NetworkGameManagerを検索
            networkGameManager = FindFirstObjectByType<NetworkGameManager>();

            if (networkGameManager == null)
            {
                Debug.LogError("[NetworkUIManager] NetworkGameManager not found in scene!");
                return;
            }

            Debug.Log("[NetworkUIManager] ✅ NetworkGameManager found");

            // ボタンにクリックイベントを設定
            if (hostButton != null)
            {
                hostButton.onClick.RemoveAllListeners(); // 重複防止
                hostButton.onClick.AddListener(OnHostButtonClick);
                hostButton.interactable = true;
                Debug.Log($"[NetworkUIManager] ✅ Host button connected: {hostButton.gameObject.name}, interactable: {hostButton.interactable}");

                // Imageの raycastTarget確認
                Image img = hostButton.GetComponent<Image>();
                if (img != null)
                {
                    img.raycastTarget = true;
                    Debug.Log($"[NetworkUIManager] Host button raycastTarget: {img.raycastTarget}");
                }
            }
            else
            {
                Debug.LogError("[NetworkUIManager] ⚠️ Host button not found!");
            }

            if (clientButton != null)
            {
                clientButton.onClick.RemoveAllListeners(); // 重複防止
                clientButton.onClick.AddListener(OnClientButtonClick);
                clientButton.interactable = true;
                Debug.Log($"[NetworkUIManager] ✅ Client button connected: {clientButton.gameObject.name}, interactable: {clientButton.interactable}");

                // Imageの raycastTarget確認
                Image img = clientButton.GetComponent<Image>();
                if (img != null)
                {
                    img.raycastTarget = true;
                    Debug.Log($"[NetworkUIManager] Client button raycastTarget: {img.raycastTarget}");
                }
            }
            else
            {
                Debug.LogError("[NetworkUIManager] ⚠️ Client button not found!");
            }

            // EventSystem確認
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                Debug.LogError("[NetworkUIManager] ⚠️ EventSystem not found! UI clicks will not work!");
            }
            else
            {
                Debug.Log($"[NetworkUIManager] ✅ EventSystem found: {eventSystem.gameObject.name}");
            }

            // Canvas確認
            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                Debug.Log($"[NetworkUIManager] Canvas renderMode: {canvas.renderMode}, sortingOrder: {canvas.sortingOrder}");
            }

            // GraphicRaycaster確認
            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                Debug.LogError("[NetworkUIManager] ⚠️ GraphicRaycaster not found on Canvas!");
            }
            else
            {
                Debug.Log("[NetworkUIManager] ✅ GraphicRaycaster found");
            }
        }

        private void Update()
        {
            // マウスクリックのデバッグと手動ボタン処理
            if (Input.GetMouseButtonDown(0))
            {
                Debug.Log($"[NetworkUIManager] Mouse clicked at: {Input.mousePosition}");

                // 何がクリックされたか確認
                PointerEventData pointerData = new PointerEventData(EventSystem.current)
                {
                    position = Input.mousePosition
                };

                var results = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current?.RaycastAll(pointerData, results);

                Debug.Log($"[NetworkUIManager] Raycast hit {results.Count} objects:");
                foreach (var result in results)
                {
                    Debug.Log($"  - {result.gameObject.name} (depth: {result.depth})");

                    // ボタンがヒットした場合、手動でクリック処理を実行
                    if (result.gameObject.name.Contains("Host"))
                    {
                        Debug.Log("[NetworkUIManager] Manual Host button trigger!");
                        OnHostButtonClick();
                        return;
                    }
                    else if (result.gameObject.name.Contains("Client") || result.gameObject.name.Contains("Join"))
                    {
                        Debug.Log("[NetworkUIManager] Manual Client button trigger!");
                        OnClientButtonClick();
                        return;
                    }
                }
            }
        }

        private void OnHostButtonClick()
        {
            Debug.Log("🎮 [NetworkUIManager] Host button clicked!");
            if (networkGameManager != null)
            {
                networkGameManager.StartHost();
            }
            else
            {
                Debug.LogError("[NetworkUIManager] Cannot start host - NetworkGameManager is null!");
            }
        }

        private void OnClientButtonClick()
        {
            Debug.Log("🎮 [NetworkUIManager] Client button clicked!");
            if (networkGameManager != null)
            {
                networkGameManager.StartClient();
            }
            else
            {
                Debug.LogError("[NetworkUIManager] Cannot start client - NetworkGameManager is null!");
            }
        }
    }
}
