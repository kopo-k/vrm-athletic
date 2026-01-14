using UnityEngine;
using UnityEditor;
using Fusion;

#if UNITY_EDITOR
/// <summary>
/// Tankプレハブのネットワーク設定を確認・修正するエディタスクリプト
/// 【変更】TankNetworkController.csの移動コードをtransform.position直接更新に変更したため、
/// NetworkTransformで正しく同期されるようになった
/// </summary>
public class FixNetworkRigidbody : Editor
{
    [MenuItem("Tools/Tanks Network Setup/9. Verify Network Components")]
    public static void VerifyNetworkComponents()
    {
        Debug.Log("=== Verifying Network Components ===");

        // TankNetwork Prefabをロード
        string[] prefabPaths = new string[]
        {
            "Assets/Prefabs/TankNetwork.prefab",
            "Assets/Prefabs/CompleteTank.prefab"
        };

        foreach (string prefabPath in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                Debug.Log($"Prefab not found: {prefabPath}");
                continue;
            }

            Debug.Log($"\n=== {prefabPath} ===");

            // Prefabをインスタンス化
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

            if (instance == null)
            {
                Debug.LogError($"Failed to instantiate: {prefabPath}");
                continue;
            }

            bool modified = false;

            // NetworkObjectを確認・追加
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = instance.AddComponent<NetworkObject>();
                Debug.Log($"  Added NetworkObject");
                modified = true;
            }
            else
            {
                Debug.Log($"  NetworkObject: OK");
            }

            // NetworkTransformを確認・追加
            NetworkTransform networkTransform = instance.GetComponent<NetworkTransform>();
            if (networkTransform == null)
            {
                networkTransform = instance.AddComponent<NetworkTransform>();
                Debug.Log($"  Added NetworkTransform");
                modified = true;
            }
            else
            {
                Debug.Log($"  NetworkTransform: OK");
            }

            // TankNetworkControllerを確認
            var tankController = instance.GetComponent<Complete.TankNetworkController>();
            if (tankController != null)
            {
                Debug.Log($"  TankNetworkController: OK");
            }

            // Rigidbodyを確認
            Rigidbody rb = instance.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Debug.Log($"  Rigidbody: OK (isKinematic: {rb.isKinematic})");
            }

            if (modified)
            {
                // Prefabに変更を適用
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                Debug.Log($"  Changes applied to prefab");
            }

            // インスタンスを削除
            DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("\n=== Verification Complete ===");
        Debug.Log("NOTE: TankNetworkController now uses transform.position directly for movement.");
        Debug.Log("This ensures NetworkTransform properly syncs position/rotation to all clients.");

        EditorUtility.DisplayDialog("Verification Complete",
            "Tank prefabs verified!\n\n" +
            "- NetworkObject: Present\n" +
            "- NetworkTransform: Present\n\n" +
            "Movement code has been updated to use transform.position directly\n" +
            "instead of Rigidbody.MovePosition for proper NetworkTransform sync.\n\n" +
            "Please rebuild and test.", "OK");
    }

    [MenuItem("Tools/Tanks Network Setup/10. Check Prefab Components")]
    public static void CheckPrefabComponents()
    {
        Debug.Log("=== Checking Prefab Components ===");

        string[] prefabPaths = new string[]
        {
            "Assets/Prefabs/TankNetwork.prefab",
            "Assets/Prefabs/CompleteTank.prefab",
            "Assets/Prefabs/CompleteShell.prefab"
        };

        foreach (string prefabPath in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                Debug.Log($"NOT FOUND: {prefabPath}");
                continue;
            }

            Debug.Log($"\n=== {prefabPath} ===");

            // コンポーネント一覧を表示
            Component[] components = prefab.GetComponents<Component>();
            foreach (Component comp in components)
            {
                if (comp != null)
                {
                    Debug.Log($"  - {comp.GetType().Name}");
                }
            }

            // 重要なコンポーネントのチェック
            bool hasNetworkObject = prefab.GetComponent<NetworkObject>() != null;
            bool hasNetworkTransform = prefab.GetComponent<NetworkTransform>() != null;
            bool hasRigidbody = prefab.GetComponent<Rigidbody>() != null;

            Debug.Log($"\n  Summary:");
            Debug.Log($"    NetworkObject: {(hasNetworkObject ? "YES" : "NO")}");
            Debug.Log($"    NetworkTransform: {(hasNetworkTransform ? "YES" : "NO")}");
            Debug.Log($"    Rigidbody: {(hasRigidbody ? "YES" : "NO")}");

            if (!hasNetworkObject)
            {
                Debug.LogWarning($"  WARNING: Missing NetworkObject! Run 'Verify Network Components' to fix.");
            }
            if (!hasNetworkTransform)
            {
                Debug.LogWarning($"  WARNING: Missing NetworkTransform! Run 'Verify Network Components' to fix.");
            }
        }

        Debug.Log("\n=== Check Complete ===");
    }
}
#endif
