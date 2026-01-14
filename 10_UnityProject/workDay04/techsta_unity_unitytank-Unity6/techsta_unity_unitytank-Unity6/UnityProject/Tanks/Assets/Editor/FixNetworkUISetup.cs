using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEditor.SceneManagement;
using Complete;

/// <summary>
/// NetworkUIの問題を修正するエディタスクリプト
/// - 重複したNetworkUIを削除
/// - ボタンのEventSystemを確認
/// - Canvas設定を修正
/// </summary>
public class FixNetworkUISetup : Editor
{
    [MenuItem("Tools/Tanks Network Setup/7. Fix NetworkUI (Button Click Issue)")]
    public static void FixNetworkUI()
    {
        Debug.Log("=== Fixing NetworkUI Setup ===");

        // 全てのNetworkUIオブジェクトを検索
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        int networkUICount = 0;
        GameObject correctNetworkUI = null;
        GameObject emptyNetworkUI = null;

        foreach (GameObject obj in allObjects)
        {
            if (obj.name == "NetworkUI" && obj.scene.IsValid())
            {
                networkUICount++;
                Debug.Log($"Found NetworkUI: {obj.name} (ID: {obj.GetInstanceID()})");

                // コンポーネント数をチェック
                Component[] components = obj.GetComponents<Component>();
                Debug.Log($"  - Component count: {components.Length}");

                foreach (Component c in components)
                {
                    Debug.Log($"    - {c.GetType().Name}");
                }

                // ボタンがあるかチェック
                Button[] buttons = obj.GetComponentsInChildren<Button>();
                Debug.Log($"  - Button count in children: {buttons.Length}");

                if (buttons.Length > 0)
                {
                    correctNetworkUI = obj;
                }
                else if (components.Length <= 2) // Transform + Canvas or less
                {
                    emptyNetworkUI = obj;
                }
            }
        }

        Debug.Log($"Total NetworkUI objects found: {networkUICount}");

        // 空のNetworkUIを削除
        if (emptyNetworkUI != null && correctNetworkUI != null)
        {
            Debug.Log($"Deleting empty NetworkUI: {emptyNetworkUI.GetInstanceID()}");
            Undo.DestroyObjectImmediate(emptyNetworkUI);
            Debug.Log("Empty NetworkUI deleted!");
        }

        // 正しいNetworkUIの設定を確認・修正
        if (correctNetworkUI != null)
        {
            FixNetworkUICanvas(correctNetworkUI);
            FixNetworkUIButtons(correctNetworkUI);
            SetupNetworkUIReference(correctNetworkUI);
        }
        else
        {
            Debug.LogError("No valid NetworkUI with buttons found!");
            return;
        }

        // EventSystemを確認
        CheckEventSystem();

        // シーンをダーティにする
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("=== NetworkUI Fix Complete ===");
        EditorUtility.DisplayDialog("Fix Complete",
            "NetworkUI has been fixed!\n\n" +
            "- Empty NetworkUI removed (if found)\n" +
            "- Canvas settings corrected\n" +
            "- Button Raycasts verified\n" +
            "- EventSystem checked\n\n" +
            "Please save the scene and try Play mode.", "OK");
    }

    private static void FixNetworkUICanvas(GameObject networkUI)
    {
        Canvas canvas = networkUI.GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("NetworkUI doesn't have Canvas component!");
            return;
        }

        // Canvasの設定を確認
        Debug.Log($"Canvas Render Mode: {canvas.renderMode}");
        Debug.Log($"Canvas Sort Order: {canvas.sortingOrder}");

        // GraphicRaycasterを確認
        GraphicRaycaster raycaster = networkUI.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            raycaster = networkUI.AddComponent<GraphicRaycaster>();
            Debug.Log("Added GraphicRaycaster to NetworkUI");
        }
        else
        {
            Debug.Log("GraphicRaycaster already exists");
        }

        // CanvasGroupを確認（もしあれば、interactableをtrueに）
        CanvasGroup canvasGroup = networkUI.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            Debug.Log("CanvasGroup set to interactable");
        }
    }

    private static void FixNetworkUIButtons(GameObject networkUI)
    {
        Button[] buttons = networkUI.GetComponentsInChildren<Button>(true);

        foreach (Button button in buttons)
        {
            // ボタンのInteractableを確認
            button.interactable = true;

            // RaycastTargetを確認
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = true;
            }

            // Textの RaycastTargetをオフに（ボタンの邪魔になることがある）
            Text[] texts = button.GetComponentsInChildren<Text>();
            foreach (Text text in texts)
            {
                text.raycastTarget = false;
            }

            Debug.Log($"Button '{button.gameObject.name}' - Interactable: {button.interactable}");
        }

        // NetworkUIManagerを確認
        NetworkUIManager uiManager = networkUI.GetComponent<NetworkUIManager>();
        if (uiManager == null)
        {
            uiManager = networkUI.AddComponent<NetworkUIManager>();
            Debug.Log("Added NetworkUIManager to NetworkUI");
        }
        else
        {
            Debug.Log("NetworkUIManager already exists");
        }

        EditorUtility.SetDirty(networkUI);
    }

    private static void SetupNetworkUIReference(GameObject networkUI)
    {
        // NetworkGameManagerにNetworkUIを設定
        NetworkGameManager networkGM = Object.FindFirstObjectByType<NetworkGameManager>();
        if (networkGM != null)
        {
            networkGM.m_NetworkUI = networkUI;
            EditorUtility.SetDirty(networkGM);
            Debug.Log("Set NetworkUI reference in NetworkGameManager");
        }
    }

    private static void CheckEventSystem()
    {
        UnityEngine.EventSystems.EventSystem eventSystem = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();

        if (eventSystem == null)
        {
            Debug.LogError("EventSystem not found in scene! UI clicks will not work.");
            return;
        }

        Debug.Log($"EventSystem found: {eventSystem.gameObject.name}");

        // InputModuleを確認
        var inputModule = eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        if (inputModule != null)
        {
            Debug.Log("InputSystemUIInputModule found (New Input System)");
        }
        else
        {
            var standaloneInput = eventSystem.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (standaloneInput != null)
            {
                Debug.Log("StandaloneInputModule found (Old Input System)");
            }
            else
            {
                Debug.LogWarning("No Input Module found on EventSystem!");
            }
        }
    }

    [MenuItem("Tools/Tanks Network Setup/8. Debug UI Hierarchy")]
    public static void DebugUIHierarchy()
    {
        Debug.Log("=== UI Hierarchy Debug ===");

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);

        foreach (Canvas canvas in canvases)
        {
            Debug.Log($"\nCanvas: {canvas.gameObject.name}");
            Debug.Log($"  Render Mode: {canvas.renderMode}");
            Debug.Log($"  Sort Order: {canvas.sortingOrder}");
            Debug.Log($"  Active: {canvas.gameObject.activeInHierarchy}");

            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            Debug.Log($"  Has GraphicRaycaster: {raycaster != null}");

            // 子要素のボタンを探す
            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
            foreach (Button btn in buttons)
            {
                Debug.Log($"    Button: {btn.gameObject.name}");
                Debug.Log($"      Interactable: {btn.interactable}");
                Debug.Log($"      Active: {btn.gameObject.activeInHierarchy}");

                Image img = btn.GetComponent<Image>();
                if (img != null)
                {
                    Debug.Log($"      RaycastTarget: {img.raycastTarget}");
                }
            }
        }

        Debug.Log("\n=== End Debug ===");
    }
}
