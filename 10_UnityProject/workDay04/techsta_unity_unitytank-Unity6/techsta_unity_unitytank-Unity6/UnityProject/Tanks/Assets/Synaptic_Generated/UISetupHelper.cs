using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UISetupHelper : MonoBehaviour
{
    [ContextMenu("Setup Lobby UI")]
    public void SetupLobbyUI()
    {
        // HostButton setup
        GameObject hostButton = GameObject.Find("LobbyCanvas/HostButton");
        if (hostButton != null)
        {
            RectTransform hostRect = hostButton.GetComponent<RectTransform>();
            hostRect.anchoredPosition = new Vector2(0, 100);
            hostRect.sizeDelta = new Vector2(200, 60);
            
            Transform hostText = hostButton.transform.Find("Text");
            if (hostText != null)
            {
                TextMeshProUGUI hostTmp = hostText.GetComponent<TextMeshProUGUI>();
                if (hostTmp != null)
                {
                    hostTmp.text = "Host Game";
                    hostTmp.fontSize = 24;
                    hostTmp.alignment = TextAlignmentOptions.Center;
                    hostTmp.color = Color.black;
                }
                
                RectTransform textRect = hostText.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
                textRect.anchoredPosition = Vector2.zero;
            }
        }
        
        // ClientButton setup
        GameObject clientButton = GameObject.Find("LobbyCanvas/ClientButton");
        if (clientButton != null)
        {
            RectTransform clientRect = clientButton.GetComponent<RectTransform>();
            clientRect.anchoredPosition = new Vector2(0, -100);
            clientRect.sizeDelta = new Vector2(200, 60);
            
            Transform clientText = clientButton.transform.Find("Text");
            if (clientText != null)
            {
                TextMeshProUGUI clientTmp = clientText.GetComponent<TextMeshProUGUI>();
                if (clientTmp != null)
                {
                    clientTmp.text = "Join Game";
                    clientTmp.fontSize = 24;
                    clientTmp.alignment = TextAlignmentOptions.Center;
                    clientTmp.color = Color.black;
                }
                
                RectTransform textRect = clientText.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;
                textRect.anchoredPosition = Vector2.zero;
            }
        }
        
        // Setup Button Events
        NetworkManager netManager = GameObject.Find("NetworkManager")?.GetComponent<NetworkManager>();
        if (netManager != null)
        {
            if (hostButton != null)
            {
                Button hostBtn = hostButton.GetComponent<Button>();
                hostBtn.onClick.RemoveAllListeners();
                hostBtn.onClick.AddListener(netManager.StartHost);
            }
            
            if (clientButton != null)
            {
                Button clientBtn = clientButton.GetComponent<Button>();
                clientBtn.onClick.RemoveAllListeners();
                clientBtn.onClick.AddListener(netManager.StartClient);
            }
        }
        
        Debug.Log("Lobby UI setup complete!");
    }
}
