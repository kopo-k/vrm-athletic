using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using Fusion;
using Complete;

/// <summary>
/// Photon Fusionネットワーク設定ウィザード
/// メニュー: Tools > Tanks Network Setup
/// </summary>
[InitializeOnLoad]
public class NetworkSetupWizard : EditorWindow
{
    static NetworkSetupWizard()
    {
        // エディタ起動後に一度だけ実行をスケジュール
        EditorApplication.delayCall += OnEditorLoaded;
    }

    private static void OnEditorLoaded()
    {
        // 自動セットアップは無効化（手動で実行するため）
        // セットアップが必要な場合は Tools > Tanks Network Setup から実行
        Debug.Log("[NetworkSetupWizard] Ready. Use Tools > Tanks Network Setup to configure.");
    }

    [MenuItem("Tools/Tanks Network Setup/1. Setup All (Recommended)")]
    public static void SetupAll()
    {
        Debug.Log("=== Tanks Network Setup Started ===");

        SetupNetworkTankPrefab();
        SetupNetworkShellPrefab();
        SetupSceneReferences();

        Debug.Log("=== Tanks Network Setup Complete! ===");
        EditorUtility.DisplayDialog("Setup Complete",
            "Network setup completed!\n\n" +
            "1. NetworkTank prefab configured\n" +
            "2. NetworkShell prefab configured\n" +
            "3. Scene references set\n\n" +
            "Please check the console for details.", "OK");
    }

    [MenuItem("Tools/Tanks Network Setup/2. Setup Tank Prefab")]
    public static void SetupNetworkTankPrefab()
    {
        Debug.Log("[Setup] Creating Network Tank Prefab...");

        // CompleteTankを基にネットワーク対応プレハブを作成
        string sourcePath = "Assets/Prefabs/CompleteTank.prefab";
        string destPath = "Assets/Prefabs/NetworkTank.prefab";

        // 既存のNetworkTank.prefabを削除して新規作成
        if (AssetDatabase.LoadAssetAtPath<GameObject>(destPath) != null)
        {
            AssetDatabase.DeleteAsset(destPath);
        }

        // コピーを作成
        AssetDatabase.CopyAsset(sourcePath, destPath);
        AssetDatabase.Refresh();

        // プレハブを編集モードで開く
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
        if (prefab == null)
        {
            Debug.LogError("[Setup] Failed to load prefab!");
            return;
        }

        // プレハブのインスタンスを作成して編集
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject root = editScope.prefabContentsRoot;

            // 古いスクリプトを削除
            RemoveComponentIfExists<Complete.TankMovement>(root);
            RemoveComponentIfExists<Complete.TankShooting>(root);
            RemoveComponentIfExists<Complete.TankHealth>(root);
            RemoveComponentIfExists<TankNetwork>(root);

            // NetworkObjectを追加（最初に追加）
            if (root.GetComponent<NetworkObject>() == null)
            {
                root.AddComponent<NetworkObject>();
                Debug.Log("[Setup] Added NetworkObject");
            }

            // NetworkTransformを追加
            if (root.GetComponent<NetworkTransform>() == null)
            {
                root.AddComponent<NetworkTransform>();
                Debug.Log("[Setup] Added NetworkTransform");
            }

            // TankNetworkControllerを追加
            TankNetworkController tankController = root.GetComponent<TankNetworkController>();
            if (tankController == null)
            {
                tankController = root.AddComponent<TankNetworkController>();
                Debug.Log("[Setup] Added TankNetworkController");
            }

            // コンポーネントの参照を設定
            SetupTankControllerReferences(root, tankController);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Setup] Network Tank Prefab created successfully!");
    }

    private static void SetupTankControllerReferences(GameObject root, TankNetworkController controller)
    {
        // FireTransformを探す
        Transform fireTransform = root.transform.Find("TankRenderers/TankTurret/TankBarrel/FireTransform");
        if (fireTransform != null)
        {
            controller.m_FireTransform = fireTransform;
            Debug.Log("[Setup] Set FireTransform reference");
        }

        // AudioSourceを探す
        AudioSource[] audioSources = root.GetComponents<AudioSource>();
        if (audioSources.Length >= 2)
        {
            controller.m_MovementAudio = audioSources[0];
            controller.m_ShootingAudio = audioSources[1];
            Debug.Log("[Setup] Set AudioSource references");
        }

        // AudioClipを設定
        controller.m_EngineIdling = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/EngineIdle.aif");
        controller.m_EngineDriving = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/EngineDriving.aif");
        controller.m_ChargingClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/ShotCharging.wav");
        controller.m_FireClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/ShotFiring.wav");

        // UIを探す
        Transform healthSliderTransform = root.transform.Find("TankRenderers/HealthCanvas/Slider");
        if (healthSliderTransform != null)
        {
            Slider healthSlider = healthSliderTransform.GetComponent<Slider>();
            controller.m_HealthSlider = healthSlider;

            Transform fillTransform = healthSliderTransform.Find("Fill Area/Fill");
            if (fillTransform != null)
            {
                controller.m_FillImage = fillTransform.GetComponent<Image>();
            }
            Debug.Log("[Setup] Set Health UI references");
        }

        Transform aimSliderTransform = root.transform.Find("TankRenderers/AimCanvas/AimSlider");
        if (aimSliderTransform != null)
        {
            controller.m_AimSlider = aimSliderTransform.GetComponent<Slider>();
            Debug.Log("[Setup] Set Aim UI reference");
        }

        // ExplosionPrefabを設定
        controller.m_ExplosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CompleteTankExplosion.prefab");

        // ShellPrefabを設定（後でNetworkShellに更新）
        // NetworkPrefabRefはSerializedPropertyで設定が必要
    }

    [MenuItem("Tools/Tanks Network Setup/3. Setup Shell Prefab")]
    public static void SetupNetworkShellPrefab()
    {
        Debug.Log("[Setup] Creating Network Shell Prefab...");

        string sourcePath = "Assets/Prefabs/CompleteShell.prefab";
        string destPath = "Assets/Prefabs/NetworkShell.prefab";

        // 既存を削除
        if (AssetDatabase.LoadAssetAtPath<GameObject>(destPath) != null)
        {
            AssetDatabase.DeleteAsset(destPath);
        }

        // コピー
        AssetDatabase.CopyAsset(sourcePath, destPath);
        AssetDatabase.Refresh();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
        if (prefab == null)
        {
            Debug.LogError("[Setup] Failed to load shell prefab!");
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject root = editScope.prefabContentsRoot;

            // 古いShellExplosionを削除
            RemoveComponentIfExists<Complete.ShellExplosion>(root);

            // NetworkObjectを追加
            if (root.GetComponent<NetworkObject>() == null)
            {
                root.AddComponent<NetworkObject>();
                Debug.Log("[Setup] Added NetworkObject to Shell");
            }

            // ShellNetworkControllerを追加
            ShellNetworkController shellController = root.GetComponent<ShellNetworkController>();
            if (shellController == null)
            {
                shellController = root.AddComponent<ShellNetworkController>();
                Debug.Log("[Setup] Added ShellNetworkController");
            }

            // レイヤーマスク設定
            shellController.m_TankMask = LayerMask.GetMask("Players");

            // パーティクルとオーディオの参照
            ParticleSystem explosionParticles = root.GetComponentInChildren<ParticleSystem>();
            if (explosionParticles != null)
            {
                shellController.m_ExplosionParticles = explosionParticles;
            }

            AudioSource explosionAudio = root.GetComponentInChildren<AudioSource>();
            if (explosionAudio != null)
            {
                shellController.m_ExplosionAudio = explosionAudio;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Setup] Network Shell Prefab created successfully!");
    }

    [MenuItem("Tools/Tanks Network Setup/4. Setup Scene References")]
    public static void SetupSceneReferences()
    {
        Debug.Log("[Setup] Setting up scene references...");

        // NetworkGameManagerを探す
        NetworkGameManager networkGM = Object.FindFirstObjectByType<NetworkGameManager>();
        if (networkGM == null)
        {
            Debug.LogError("[Setup] NetworkGameManager not found in scene!");
            return;
        }

        // CameraControlを設定
        CameraControl cameraControl = Object.FindFirstObjectByType<CameraControl>();
        if (cameraControl != null)
        {
            networkGM.m_CameraControl = cameraControl;
            Debug.Log("[Setup] Set CameraControl reference");
        }

        // MessageTextを設定
        GameObject messageCanvas = GameObject.Find("MessageCanvas");
        if (messageCanvas != null)
        {
            Text messageText = messageCanvas.GetComponentInChildren<Text>();
            if (messageText != null)
            {
                networkGM.m_MessageText = messageText;
                Debug.Log("[Setup] Set MessageText reference");
            }
        }

        // SpawnPointsを設定
        GameObject sp1 = GameObject.Find("SpawnPoint1");
        GameObject sp2 = GameObject.Find("SpawnPoint2");
        if (sp1 != null && sp2 != null)
        {
            networkGM.m_SpawnPoints = new Transform[] { sp1.transform, sp2.transform };
            Debug.Log("[Setup] Set SpawnPoints references");
        }

        // NetworkUIを設定
        GameObject networkUI = GameObject.Find("NetworkUI");
        if (networkUI != null)
        {
            // 最初に見つかったNetworkUIのCanvasを使用
            Canvas canvas = networkUI.GetComponent<Canvas>();
            if (canvas != null)
            {
                networkGM.m_NetworkUI = networkUI;
                Debug.Log("[Setup] Set NetworkUI reference");
            }
        }

        // TankPrefabを設定（NetworkPrefabRef）
        // これはSerializedObjectで設定する必要がある
        SerializedObject serializedGM = new SerializedObject(networkGM);
        SerializedProperty tankPrefabProp = serializedGM.FindProperty("m_TankPrefab");

        GameObject networkTankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkTank.prefab");
        if (networkTankPrefab != null)
        {
            // NetworkObjectからGUIDを取得してNetworkPrefabRefを設定
            NetworkObject netObj = networkTankPrefab.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                // NetworkPrefabRefの設定はアセットGUIDが必要
                string assetPath = AssetDatabase.GetAssetPath(networkTankPrefab);
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                Debug.Log($"[Setup] NetworkTank GUID: {guid}");
            }
        }

        serializedGM.ApplyModifiedProperties();

        // オブジェクトをダーティにしてシーンを保存可能に
        EditorUtility.SetDirty(networkGM);

        Debug.Log("[Setup] Scene references setup complete!");
    }

    [MenuItem("Tools/Tanks Network Setup/5. Create Network Project Config")]
    public static void CreateNetworkProjectConfig()
    {
        Debug.Log("[Setup] Checking Network Project Config...");

        // Fusionのメニューからプロジェクト設定を開く
        EditorApplication.ExecuteMenuItem("Fusion/Network Project Config");
    }

    private static void RemoveComponentIfExists<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        if (component != null)
        {
            Object.DestroyImmediate(component, true);
            Debug.Log($"[Setup] Removed {typeof(T).Name}");
        }
    }
}
