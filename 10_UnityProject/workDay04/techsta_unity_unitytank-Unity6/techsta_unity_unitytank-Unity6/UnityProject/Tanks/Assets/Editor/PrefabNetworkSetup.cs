using UnityEngine;
using UnityEditor;
using Fusion;

#if UNITY_EDITOR
/// <summary>
/// PrefabにNetworkObjectとNetworkTransformを追加するエディタースクリプト
/// </summary>
public class PrefabNetworkSetup : MonoBehaviour
{
    [MenuItem("Tools/Setup Tank Prefab Network")]
    public static void SetupTankPrefab()
    {
        // CompleteTank Prefabをロード
        string prefabPath = "Assets/Prefabs/CompleteTank.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        if (prefab == null)
        {
            Debug.LogError($"Prefab not found at {prefabPath}");
            return;
        }

        // Prefabをインスタンス化
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        
        if (instance == null)
        {
            Debug.LogError("Failed to instantiate prefab");
            return;
        }

        // NetworkObjectを追加（存在しない場合）
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            networkObject = instance.AddComponent<NetworkObject>();
            Debug.Log("Added NetworkObject to CompleteTank");
        }
        
        // NetworkTransformを追加（存在しない場合）
        NetworkTransform networkTransform = instance.GetComponent<NetworkTransform>();
        if (networkTransform == null)
        {
            networkTransform = instance.AddComponent<NetworkTransform>();
            Debug.Log("Added NetworkTransform to CompleteTank");
        }

        // Prefabに変更を適用
        PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
        
        // インスタンスを削除
        DestroyImmediate(instance);
        
        Debug.Log("CompleteTank prefab setup complete!");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Setup Shell Prefab Network")]
    public static void SetupShellPrefab()
    {
        // CompleteShell Prefabをロード
        string prefabPath = "Assets/Prefabs/CompleteShell.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        if (prefab == null)
        {
            Debug.LogError($"Prefab not found at {prefabPath}");
            return;
        }

        // Prefabをインスタンス化
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        
        if (instance == null)
        {
            Debug.LogError("Failed to instantiate prefab");
            return;
        }

        // NetworkObjectを追加（存在しない場合）
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            networkObject = instance.AddComponent<NetworkObject>();
            Debug.Log("Added NetworkObject to CompleteShell");
        }
        
        // NetworkTransformも追加（物理同期用）
        NetworkTransform networkTransform = instance.GetComponent<NetworkTransform>();
        if (networkTransform == null)
        {
            networkTransform = instance.AddComponent<NetworkTransform>();
            Debug.Log("Added NetworkTransform to CompleteShell");
        }

        // Prefabに変更を適用
        PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
        
        // インスタンスを削除
        DestroyImmediate(instance);
        
        Debug.Log("CompleteShell prefab setup complete!");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
#endif
