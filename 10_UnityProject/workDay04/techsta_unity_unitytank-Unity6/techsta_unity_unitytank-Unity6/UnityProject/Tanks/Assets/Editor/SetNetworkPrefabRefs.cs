using UnityEngine;
using UnityEditor;
using Fusion;
using Complete;
using System.Reflection;

/// <summary>
/// NetworkPrefabRefの設定を自動化するエディタスクリプト
/// </summary>
public class SetNetworkPrefabRefs : Editor
{
    [MenuItem("Tools/Tanks Network Setup/6. Set NetworkPrefab References")]
    public static void SetPrefabReferences()
    {
        Debug.Log("[SetNetworkPrefabRefs] Setting NetworkPrefabRef references...");

        // NetworkGameManagerを探す
        NetworkGameManager networkGM = Object.FindFirstObjectByType<NetworkGameManager>();
        if (networkGM == null)
        {
            Debug.LogError("[SetNetworkPrefabRefs] NetworkGameManager not found in scene!");
            return;
        }

        // NetworkTankプレハブを取得
        GameObject networkTankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkTank.prefab");
        if (networkTankPrefab == null)
        {
            Debug.LogError("[SetNetworkPrefabRefs] NetworkTank.prefab not found!");
            return;
        }

        NetworkObject tankNetObj = networkTankPrefab.GetComponent<NetworkObject>();
        if (tankNetObj == null)
        {
            Debug.LogError("[SetNetworkPrefabRefs] NetworkTank prefab doesn't have NetworkObject component!");
            return;
        }

        // SerializedObjectを使用してNetworkPrefabRefを設定
        SerializedObject serializedGM = new SerializedObject(networkGM);
        SerializedProperty tankPrefabProp = serializedGM.FindProperty("m_TankPrefab");

        // NetworkPrefabRefの内部プロパティを設定
        if (tankPrefabProp != null)
        {
            // Fusion 2ではNetworkPrefabRefは内部的にNetworkObjectを参照する
            // SerializedPropertyの子プロパティを探索
            SerializedProperty iter = tankPrefabProp.Copy();
            if (iter.Next(true))
            {
                do
                {
                    Debug.Log($"[SetNetworkPrefabRefs] Found property: {iter.name} ({iter.propertyType})");
                    if (iter.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        iter.objectReferenceValue = tankNetObj;
                        Debug.Log($"[SetNetworkPrefabRefs] Set {iter.name} to NetworkTank's NetworkObject");
                    }
                } while (iter.Next(false));
            }
        }

        serializedGM.ApplyModifiedProperties();

        // NetworkShellプレハブの参照をNetworkTankに設定
        SetShellPrefabOnTank(networkTankPrefab);

        EditorUtility.SetDirty(networkGM);
        AssetDatabase.SaveAssets();

        Debug.Log("[SetNetworkPrefabRefs] NetworkPrefabRef setup complete!");
        EditorUtility.DisplayDialog("Setup Complete",
            "NetworkPrefabRef references have been set!\n\n" +
            "- NetworkGameManager.m_TankPrefab -> NetworkTank\n" +
            "- TankNetworkController.m_ShellPrefab -> NetworkShell\n\n" +
            "Please verify in Inspector and save scene.", "OK");
    }

    private static void SetShellPrefabOnTank(GameObject tankPrefab)
    {
        // NetworkShellプレハブを取得
        GameObject networkShellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkShell.prefab");
        if (networkShellPrefab == null)
        {
            Debug.LogWarning("[SetNetworkPrefabRefs] NetworkShell.prefab not found!");
            return;
        }

        NetworkObject shellNetObj = networkShellPrefab.GetComponent<NetworkObject>();
        if (shellNetObj == null)
        {
            Debug.LogWarning("[SetNetworkPrefabRefs] NetworkShell doesn't have NetworkObject!");
            return;
        }

        // プレハブを編集モードで開く
        string prefabPath = AssetDatabase.GetAssetPath(tankPrefab);
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject root = editScope.prefabContentsRoot;
            TankNetworkController tankController = root.GetComponent<TankNetworkController>();

            if (tankController != null)
            {
                SerializedObject serializedTank = new SerializedObject(tankController);
                SerializedProperty shellPrefabProp = serializedTank.FindProperty("m_ShellPrefab");

                if (shellPrefabProp != null)
                {
                    // 内部プロパティを設定
                    SerializedProperty iter = shellPrefabProp.Copy();
                    if (iter.Next(true))
                    {
                        do
                        {
                            if (iter.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                iter.objectReferenceValue = shellNetObj;
                                Debug.Log($"[SetNetworkPrefabRefs] Set Shell {iter.name} reference");
                            }
                        } while (iter.Next(false));
                    }

                    serializedTank.ApplyModifiedProperties();
                    Debug.Log("[SetNetworkPrefabRefs] Set Shell Prefab on TankNetworkController");
                }
            }
        }

        AssetDatabase.SaveAssets();
    }
}
