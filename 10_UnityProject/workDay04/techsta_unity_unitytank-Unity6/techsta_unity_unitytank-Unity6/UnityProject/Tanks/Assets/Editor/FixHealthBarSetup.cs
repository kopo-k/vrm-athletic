using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using Complete;

#if UNITY_EDITOR
/// <summary>
/// 体力バーの設定を修正するエディタスクリプト
/// TankNetworkControllerのm_HealthSliderとm_FillImageを自動設定
/// </summary>
public class FixHealthBarSetup : Editor
{
    [MenuItem("Tools/Tanks Network Setup/11. Fix Health Bar References")]
    public static void FixHealthBarReferences()
    {
        Debug.Log("=== Fixing Health Bar References ===");

        // シーン内のTankNetworkControllerを検索
        TankNetworkController[] tanks = FindObjectsByType<TankNetworkController>(FindObjectsSortMode.None);

        if (tanks.Length == 0)
        {
            Debug.LogWarning("No TankNetworkController found in scene. Try in Play mode after tanks spawn.");

            // プレハブも確認
            CheckPrefab();
            return;
        }

        foreach (TankNetworkController tank in tanks)
        {
            FixTankHealthBar(tank);
        }

        Debug.Log("=== Health Bar Fix Complete ===");
    }

    private static void CheckPrefab()
    {
        string[] prefabPaths = new string[]
        {
            "Assets/Prefabs/TankNetwork.prefab",
            "Assets/Prefabs/CompleteTank.prefab"
        };

        foreach (string path in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            Debug.Log($"\n=== Checking {path} ===");

            // Sliderを探す
            Slider[] sliders = prefab.GetComponentsInChildren<Slider>(true);
            Debug.Log($"Found {sliders.Length} Sliders");

            foreach (Slider slider in sliders)
            {
                Debug.Log($"  Slider: {GetPath(slider.transform)}");

                if (slider.fillRect != null)
                {
                    Image fillImage = slider.fillRect.GetComponent<Image>();
                    if (fillImage != null)
                    {
                        Debug.Log($"    FillImage found: {fillImage.gameObject.name}");
                        Debug.Log($"    Current color: {fillImage.color}");
                    }
                }
            }

            // TankNetworkControllerの参照を確認
            TankNetworkController controller = prefab.GetComponent<TankNetworkController>();
            if (controller != null)
            {
                Debug.Log($"\nTankNetworkController references:");
                Debug.Log($"  m_HealthSlider: {(controller.m_HealthSlider != null ? controller.m_HealthSlider.name : "NULL")}");
                Debug.Log($"  m_FillImage: {(controller.m_FillImage != null ? controller.m_FillImage.name : "NULL")}");
                Debug.Log($"  m_AimSlider: {(controller.m_AimSlider != null ? controller.m_AimSlider.name : "NULL")}");
            }
        }
    }

    private static void FixTankHealthBar(TankNetworkController tank)
    {
        Debug.Log($"\nFixing tank: {tank.gameObject.name}");

        // Sliderを探す
        Slider[] sliders = tank.GetComponentsInChildren<Slider>(true);
        Debug.Log($"  Found {sliders.Length} sliders in children");

        Slider healthSlider = null;
        Slider aimSlider = null;

        foreach (Slider slider in sliders)
        {
            string sliderPath = GetPath(slider.transform);
            Debug.Log($"  Slider at: {sliderPath}");

            // 名前で判別
            if (slider.gameObject.name.ToLower().Contains("health") ||
                slider.transform.parent?.name.ToLower().Contains("health") == true)
            {
                healthSlider = slider;
                Debug.Log($"    -> Identified as Health Slider");
            }
            else if (slider.gameObject.name.ToLower().Contains("aim") ||
                     slider.transform.parent?.name.ToLower().Contains("aim") == true)
            {
                aimSlider = slider;
                Debug.Log($"    -> Identified as Aim Slider");
            }
        }

        // 名前で判別できなかった場合、位置で判別（上が体力、下がエイム）
        if (healthSlider == null && sliders.Length >= 1)
        {
            // 最初に見つかったスライダーを体力バーとする
            healthSlider = sliders[0];
            Debug.Log($"  Using first slider as Health: {healthSlider.gameObject.name}");
        }

        if (aimSlider == null && sliders.Length >= 2)
        {
            aimSlider = sliders[1];
            Debug.Log($"  Using second slider as Aim: {aimSlider.gameObject.name}");
        }

        // 参照を設定
        bool changed = false;

        if (healthSlider != null && tank.m_HealthSlider != healthSlider)
        {
            tank.m_HealthSlider = healthSlider;
            Debug.Log($"  Set m_HealthSlider to: {healthSlider.gameObject.name}");
            changed = true;
        }

        if (aimSlider != null && tank.m_AimSlider != aimSlider)
        {
            tank.m_AimSlider = aimSlider;
            Debug.Log($"  Set m_AimSlider to: {aimSlider.gameObject.name}");
            changed = true;
        }

        // FillImageを設定
        if (healthSlider != null)
        {
            Image fillImage = null;

            if (healthSlider.fillRect != null)
            {
                fillImage = healthSlider.fillRect.GetComponent<Image>();
            }

            if (fillImage == null)
            {
                // Fill Area/Fill を探す
                Transform fillArea = healthSlider.transform.Find("Fill Area");
                if (fillArea != null)
                {
                    Transform fill = fillArea.Find("Fill");
                    if (fill != null)
                    {
                        fillImage = fill.GetComponent<Image>();
                    }
                }
            }

            if (fillImage != null)
            {
                if (tank.m_FillImage != fillImage)
                {
                    tank.m_FillImage = fillImage;
                    Debug.Log($"  Set m_FillImage to: {fillImage.gameObject.name}");
                    changed = true;
                }

                // 色を緑に設定
                fillImage.color = tank.m_FullHealthColor;
                Debug.Log($"  Set FillImage color to: {tank.m_FullHealthColor}");
            }
            else
            {
                Debug.LogWarning($"  Could not find FillImage for health slider!");
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(tank);
        }
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    [MenuItem("Tools/Tanks Network Setup/12. Force Set Health Bar Color (Runtime)")]
    public static void ForceSetHealthBarColorRuntime()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("This tool only works in Play mode!");
            return;
        }

        Debug.Log("=== Force Setting Health Bar Colors ===");

        TankNetworkController[] tanks = FindObjectsByType<TankNetworkController>(FindObjectsSortMode.None);

        foreach (TankNetworkController tank in tanks)
        {
            Debug.Log($"\nTank: {tank.gameObject.name}");

            // Sliderを探す
            Slider[] sliders = tank.GetComponentsInChildren<Slider>(true);

            foreach (Slider slider in sliders)
            {
                if (slider.fillRect != null)
                {
                    Image fillImage = slider.fillRect.GetComponent<Image>();
                    if (fillImage != null)
                    {
                        // 緑色に設定
                        fillImage.color = Color.green;
                        Debug.Log($"  Set {slider.gameObject.name} fill to GREEN");
                    }
                }

                // 子のImageも全て確認
                Image[] images = slider.GetComponentsInChildren<Image>(true);
                foreach (Image img in images)
                {
                    if (img.gameObject.name.ToLower().Contains("fill"))
                    {
                        img.color = Color.green;
                        Debug.Log($"  Set {img.gameObject.name} to GREEN");
                    }
                }
            }
        }

        Debug.Log("=== Done ===");
    }

    [MenuItem("Tools/Tanks Network Setup/13. Debug Health Bar Structure")]
    public static void DebugHealthBarStructure()
    {
        Debug.Log("=== Debug Health Bar Structure ===");

        // プレハブを確認
        string[] prefabPaths = new string[]
        {
            "Assets/Prefabs/TankNetwork.prefab",
            "Assets/Prefabs/CompleteTank.prefab"
        };

        foreach (string path in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.Log($"NOT FOUND: {path}");
                continue;
            }

            Debug.Log($"\n=== {path} ===");
            PrintHierarchy(prefab.transform, 0);
        }

        // シーン内のタンクも確認
        if (Application.isPlaying)
        {
            TankNetworkController[] tanks = FindObjectsByType<TankNetworkController>(FindObjectsSortMode.None);
            foreach (TankNetworkController tank in tanks)
            {
                Debug.Log($"\n=== Scene Tank: {tank.gameObject.name} ===");
                PrintHierarchy(tank.transform, 0);
            }
        }
    }

    private static void PrintHierarchy(Transform t, int depth)
    {
        string indent = new string(' ', depth * 2);

        // コンポーネント情報
        string components = "";
        if (t.GetComponent<Slider>() != null) components += "[Slider]";
        if (t.GetComponent<Image>() != null)
        {
            Image img = t.GetComponent<Image>();
            components += $"[Image: {ColorToHex(img.color)}]";
        }
        if (t.GetComponent<Canvas>() != null) components += "[Canvas]";

        Debug.Log($"{indent}{t.name} {components}");

        // 子を再帰的に処理（深さ制限）
        if (depth < 5)
        {
            foreach (Transform child in t)
            {
                PrintHierarchy(child, depth + 1);
            }
        }
    }

    private static string ColorToHex(Color c)
    {
        return $"#{ColorUtility.ToHtmlStringRGBA(c)}";
    }

    [MenuItem("Tools/Tanks Network Setup/14. Setup TankNetwork Prefab Health Bar")]
    public static void SetupTankNetworkPrefabHealthBar()
    {
        Debug.Log("=== Setting up TankNetwork Prefab Health Bar ===");

        string tankNetworkPath = "Assets/Prefabs/TankNetwork.prefab";
        string completeTankPath = "Assets/Prefabs/CompleteTank.prefab";

        GameObject tankNetworkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(tankNetworkPath);
        GameObject completeTankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(completeTankPath);

        if (tankNetworkPrefab == null)
        {
            Debug.LogError($"TankNetwork prefab not found at {tankNetworkPath}");
            return;
        }

        // TankNetworkプレハブをインスタンス化
        GameObject instance = PrefabUtility.InstantiatePrefab(tankNetworkPrefab) as GameObject;
        if (instance == null)
        {
            Debug.LogError("Failed to instantiate TankNetwork prefab");
            return;
        }

        TankNetworkController controller = instance.GetComponent<TankNetworkController>();
        if (controller == null)
        {
            Debug.LogError("TankNetworkController not found on prefab");
            DestroyImmediate(instance);
            return;
        }

        // Canvasを探す
        Canvas canvas = instance.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Debug.Log("No Canvas found on TankNetwork. Checking CompleteTank for reference...");

            if (completeTankPrefab != null)
            {
                // CompleteTankからCanvasの構造を確認
                Canvas sourcCanvas = completeTankPrefab.GetComponentInChildren<Canvas>(true);
                if (sourcCanvas != null)
                {
                    Debug.Log($"Found Canvas in CompleteTank: {sourcCanvas.gameObject.name}");
                    Debug.Log("You need to copy the Canvas structure manually or re-create the prefab from CompleteTank.");
                }
            }
        }
        else
        {
            Debug.Log($"Found Canvas: {canvas.gameObject.name}");
        }

        // Sliderを探して設定
        Slider[] sliders = instance.GetComponentsInChildren<Slider>(true);
        Debug.Log($"Found {sliders.Length} sliders");

        Slider healthSlider = null;
        Slider aimSlider = null;

        foreach (Slider slider in sliders)
        {
            Debug.Log($"  Slider: {slider.gameObject.name} at {GetPath(slider.transform)}");

            // 親の名前もチェック
            string fullPath = GetPath(slider.transform).ToLower();

            if (fullPath.Contains("health"))
            {
                healthSlider = slider;
            }
            else if (fullPath.Contains("aim"))
            {
                aimSlider = slider;
            }
        }

        // 名前で見つからなかった場合、インデックスで設定
        if (healthSlider == null && sliders.Length >= 1)
        {
            healthSlider = sliders[0];
        }
        if (aimSlider == null && sliders.Length >= 2)
        {
            aimSlider = sliders[1];
        }

        // 参照を設定
        bool changed = false;

        if (healthSlider != null)
        {
            controller.m_HealthSlider = healthSlider;
            Debug.Log($"Set m_HealthSlider: {healthSlider.gameObject.name}");
            changed = true;

            // FillImageを設定
            if (healthSlider.fillRect != null)
            {
                Image fillImage = healthSlider.fillRect.GetComponent<Image>();
                if (fillImage != null)
                {
                    controller.m_FillImage = fillImage;
                    fillImage.color = Color.green;
                    Debug.Log($"Set m_FillImage: {fillImage.gameObject.name}, color: green");
                }
            }
            else
            {
                // Fill Area/Fill を探す
                Transform fillArea = healthSlider.transform.Find("Fill Area");
                if (fillArea != null)
                {
                    Transform fill = fillArea.Find("Fill");
                    if (fill != null)
                    {
                        Image fillImage = fill.GetComponent<Image>();
                        if (fillImage != null)
                        {
                            controller.m_FillImage = fillImage;
                            fillImage.color = Color.green;
                            Debug.Log($"Set m_FillImage from path: {fillImage.gameObject.name}");
                        }
                    }
                }
            }
        }

        if (aimSlider != null)
        {
            controller.m_AimSlider = aimSlider;
            Debug.Log($"Set m_AimSlider: {aimSlider.gameObject.name}");
            changed = true;
        }

        if (changed)
        {
            // Prefabに変更を適用
            PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
            Debug.Log("Changes applied to prefab!");
        }

        DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("=== Setup Complete ===");
        Debug.Log("Please check the TankNetwork prefab in the Inspector.");
        Debug.Log("If health bar is still gray, the FillImage sprite may need to be white/UI sprite.");

        EditorUtility.DisplayDialog("Health Bar Setup",
            "TankNetwork prefab health bar references set!\n\n" +
            $"Health Slider: {(healthSlider != null ? healthSlider.gameObject.name : "NOT FOUND")}\n" +
            $"Aim Slider: {(aimSlider != null ? aimSlider.gameObject.name : "NOT FOUND")}\n\n" +
            "If the health bar is still gray:\n" +
            "1. Check that the Fill Image has a white sprite\n" +
            "2. Check the Image Type is 'Simple' or 'Filled'\n" +
            "3. Run '12. Force Set Health Bar Color' in Play mode",
            "OK");
    }

    [MenuItem("Tools/Tanks Network Setup/15. Check and Fix Fill Image Sprite")]
    public static void CheckAndFixFillImageSprite()
    {
        Debug.Log("=== Checking Fill Image Sprites ===");

        string[] prefabPaths = new string[]
        {
            "Assets/Prefabs/TankNetwork.prefab",
            "Assets/Prefabs/CompleteTank.prefab"
        };

        foreach (string path in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.Log($"NOT FOUND: {path}");
                continue;
            }

            Debug.Log($"\n=== {path} ===");

            // プレハブをインスタンス化
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            bool modified = false;

            // 全てのImageをチェック
            Image[] images = instance.GetComponentsInChildren<Image>(true);
            foreach (Image img in images)
            {
                if (img.gameObject.name.ToLower().Contains("fill"))
                {
                    Debug.Log($"  Image: {img.gameObject.name}");
                    Debug.Log($"    Sprite: {(img.sprite != null ? img.sprite.name : "NULL")}");
                    Debug.Log($"    Type: {img.type}");
                    Debug.Log($"    Color: {ColorToHex(img.color)}");
                    Debug.Log($"    RaycastTarget: {img.raycastTarget}");

                    // スプライトがない場合は設定
                    if (img.sprite == null)
                    {
                        // 組み込みのUIスプライトを使用
                        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                        if (uiSprite != null)
                        {
                            img.sprite = uiSprite;
                            Debug.Log($"    -> Set default UI sprite");
                            modified = true;
                        }
                    }

                    // 色が灰色やデフォルトの場合は緑に
                    if (img.color == Color.gray || img.color == Color.white || img.color == new Color(0.5f, 0.5f, 0.5f, 1f))
                    {
                        img.color = Color.green;
                        Debug.Log($"    -> Set color to green");
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                Debug.Log($"  Changes applied to {path}");
            }

            DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("\n=== Check Complete ===");
    }
}
#endif
