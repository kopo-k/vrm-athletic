using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

#if UNITY_EDITOR
public class CreateNetworkUI : MonoBehaviour
{
    [MenuItem("Tools/Create Network UI")]
    public static void CreateUI()
    {
        // Canvasを作成
        GameObject canvasObj = new GameObject("NetworkUI");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // ホストボタン
        GameObject hostButtonObj = new GameObject("HostButton");
        hostButtonObj.transform.SetParent(canvasObj.transform);
        RectTransform hostRect = hostButtonObj.AddComponent<RectTransform>();
        hostRect.anchoredPosition = new Vector2(-200, -400);
        hostRect.sizeDelta = new Vector2(180, 60);
        
        Button hostButton = hostButtonObj.AddComponent<Button>();
        Image hostImage = hostButtonObj.AddComponent<Image>();
        hostImage.color = new Color(0.2f, 0.8f, 0.2f);

        GameObject hostTextObj = new GameObject("Text");
        hostTextObj.transform.SetParent(hostButtonObj.transform);
        RectTransform hostTextRect = hostTextObj.AddComponent<RectTransform>();
        hostTextRect.anchorMin = Vector2.zero;
        hostTextRect.anchorMax = Vector2.one;
        hostTextRect.sizeDelta = Vector2.zero;
        
        Text hostText = hostTextObj.AddComponent<Text>();
        hostText.text = "Start Host";
        hostText.alignment = TextAnchor.MiddleCenter;
        hostText.color = Color.white;
        hostText.fontSize = 24;
        hostText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // クライアントボタン
        GameObject clientButtonObj = new GameObject("ClientButton");
        clientButtonObj.transform.SetParent(canvasObj.transform);
        RectTransform clientRect = clientButtonObj.AddComponent<RectTransform>();
        clientRect.anchoredPosition = new Vector2(200, -400);
        clientRect.sizeDelta = new Vector2(180, 60);
        
        Button clientButton = clientButtonObj.AddComponent<Button>();
        Image clientImage = clientButtonObj.AddComponent<Image>();
        clientImage.color = new Color(0.2f, 0.2f, 0.8f);

        GameObject clientTextObj = new GameObject("Text");
        clientTextObj.transform.SetParent(clientButtonObj.transform);
        RectTransform clientTextRect = clientTextObj.AddComponent<RectTransform>();
        clientTextRect.anchorMin = Vector2.zero;
        clientTextRect.anchorMax = Vector2.one;
        clientTextRect.sizeDelta = Vector2.zero;
        
        Text clientText = clientTextObj.AddComponent<Text>();
        clientText.text = "Join as Client";
        clientText.alignment = TextAnchor.MiddleCenter;
        clientText.color = Color.white;
        clientText.fontSize = 24;
        clientText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // NetworkManagerを探してボタンにリンク
        NetworkManager networkManager = FindObjectOfType<NetworkManager>();
        if (networkManager != null)
        {
            hostButton.onClick.AddListener(() => networkManager.StartHost());
            clientButton.onClick.AddListener(() => networkManager.StartClient());
            Debug.Log("Network UI created and linked to NetworkManager!");
        }
        else
        {
            Debug.LogWarning("NetworkManager not found in scene!");
        }
    }
}
#endif
