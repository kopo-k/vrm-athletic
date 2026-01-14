using UnityEditor;
using UnityEngine;
using Fusion;
using System;

namespace Complete
{
    /// <summary>
    /// Photon Fusion用のネットワークPrefabを自動セットアップするエディタースクリプト
    /// </summary>
    public class NetworkPrefabSetup : EditorWindow
    {
        [MenuItem("Tanks/Setup Network Prefabs")]
        public static void SetupNetworkPrefabs()
        {
            SetupNetworkTank();
            SetupNetworkShell();
            Debug.Log("✅ Network Prefabs setup complete!");
        }

        [MenuItem("Tanks/Setup Network Tank")]
        public static void SetupNetworkTank()
        {
            // CompleteTankをロード
            string sourcePath = "Assets/Prefabs/CompleteTank.prefab";
            string destPath = "Assets/Prefabs/NetworkTank.prefab";

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"Source prefab not found: {sourcePath}");
                return;
            }

            // 既存のNetworkTankを削除
            if (AssetDatabase.LoadAssetAtPath<GameObject>(destPath) != null)
            {
                AssetDatabase.DeleteAsset(destPath);
            }

            // Prefabを複製
            AssetDatabase.CopyAsset(sourcePath, destPath);
            AssetDatabase.Refresh();

            GameObject networkTankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
            if (networkTankPrefab == null)
            {
                Debug.LogError("Failed to create network tank prefab");
                return;
            }

            // Prefabをインスタンス化して編集
            GameObject instance = PrefabUtility.InstantiatePrefab(networkTankPrefab) as GameObject;

            // 古いスクリプトを削除
            var oldMovement = instance.GetComponent<TankMovement>();
            if (oldMovement != null) DestroyImmediate(oldMovement);

            var oldShooting = instance.GetComponent<TankShooting>();
            if (oldShooting != null) DestroyImmediate(oldShooting);

            var oldHealth = instance.GetComponent<TankHealth>();
            if (oldHealth != null) DestroyImmediate(oldHealth);

            // NetworkObjectを追加
            if (instance.GetComponent<NetworkObject>() == null)
            {
                instance.AddComponent<NetworkObject>();
            }

            // NetworkTransformを追加（位置と回転の同期）
            if (instance.GetComponent<NetworkTransform>() == null)
            {
                instance.AddComponent<NetworkTransform>();
            }

            // NetworkRigidbody3Dを追加（物理の同期）- 存在する場合のみ
            TryAddNetworkRigidbody(instance);

            // TankNetworkControllerを追加
            var tankController = instance.GetComponent<TankNetworkController>();
            if (tankController == null)
            {
                tankController = instance.AddComponent<TankNetworkController>();
            }

            // 参照を設定
            SetupTankControllerReferences(instance, tankController);

            // Prefabを保存
            PrefabUtility.SaveAsPrefabAsset(instance, destPath);
            DestroyImmediate(instance);

            Debug.Log($"✅ NetworkTank prefab created: {destPath}");
        }

        /// <summary>
        /// NetworkRigidbody3D または NetworkRigidbody を追加（存在する場合のみ）
        /// </summary>
        private static void TryAddNetworkRigidbody(GameObject instance)
        {
            // Fusionの物理コンポーネントを検索して追加
            // Fusion 2では NetworkRigidbody3D、Fusion 1では NetworkRigidbody
            Type networkRigidbodyType = null;

            // まず NetworkRigidbody3D を検索
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                networkRigidbodyType = assembly.GetType("Fusion.Addons.Physics.NetworkRigidbody3D");
                if (networkRigidbodyType != null) break;
                
                networkRigidbodyType = assembly.GetType("Fusion.NetworkRigidbody3D");
                if (networkRigidbodyType != null) break;

                networkRigidbodyType = assembly.GetType("Fusion.NetworkRigidbody");
                if (networkRigidbodyType != null) break;
            }

            if (networkRigidbodyType != null)
            {
                if (instance.GetComponent(networkRigidbodyType) == null)
                {
                    instance.AddComponent(networkRigidbodyType);
                    Debug.Log($"Added {networkRigidbodyType.Name} to {instance.name}");
                }
            }
            else
            {
                Debug.LogWarning("NetworkRigidbody component not found. Make sure Fusion Physics addon is installed if you need physics synchronization.");
            }
        }

        private static void SetupTankControllerReferences(GameObject tank, TankNetworkController controller)
        {
            // FireTransformを検索
            controller.m_FireTransform = tank.transform.Find("TankRenderers/TankTurret/FireTransform");

            // AudioSourceを検索
            AudioSource[] audioSources = tank.GetComponents<AudioSource>();
            if (audioSources.Length > 0) controller.m_MovementAudio = audioSources[0];
            if (audioSources.Length > 1) controller.m_ShootingAudio = audioSources[1];

            // AudioClipを検索
            controller.m_EngineIdling = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/EngineIdle.wav");
            controller.m_EngineDriving = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/EngineDriving.wav");
            controller.m_ChargingClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/ShotCharging.wav");
            controller.m_FireClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AudioClips/ShotFiring.wav");

            // UIを検索
            var canvas = tank.GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                var sliders = canvas.GetComponentsInChildren<UnityEngine.UI.Slider>();
                foreach (var slider in sliders)
                {
                    if (slider.gameObject.name.Contains("Aim"))
                    {
                        controller.m_AimSlider = slider;
                    }
                    else if (slider.gameObject.name.Contains("Health"))
                    {
                        controller.m_HealthSlider = slider;
                        controller.m_FillImage = slider.fillRect.GetComponent<UnityEngine.UI.Image>();
                    }
                }
            }

            // 爆発エフェクト
            controller.m_ExplosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CompleteTankExplosion.prefab");
        }

        [MenuItem("Tanks/Setup Network Shell")]
        public static void SetupNetworkShell()
        {
            // CompleteShellをロード
            string sourcePath = "Assets/Prefabs/CompleteShell.prefab";
            string destPath = "Assets/Prefabs/NetworkShell.prefab";

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"Source prefab not found: {sourcePath}");
                return;
            }

            // 既存のNetworkShellを削除
            if (AssetDatabase.LoadAssetAtPath<GameObject>(destPath) != null)
            {
                AssetDatabase.DeleteAsset(destPath);
            }

            // Prefabを複製
            AssetDatabase.CopyAsset(sourcePath, destPath);
            AssetDatabase.Refresh();

            GameObject networkShellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
            if (networkShellPrefab == null)
            {
                Debug.LogError("Failed to create network shell prefab");
                return;
            }

            // Prefabをインスタンス化して編集
            GameObject instance = PrefabUtility.InstantiatePrefab(networkShellPrefab) as GameObject;

            // 古いShellExplosionを削除
            var oldExplosion = instance.GetComponent<ShellExplosion>();
            if (oldExplosion != null) DestroyImmediate(oldExplosion);

            // NetworkObjectを追加
            if (instance.GetComponent<NetworkObject>() == null)
            {
                instance.AddComponent<NetworkObject>();
            }

            // NetworkTransformを追加
            if (instance.GetComponent<NetworkTransform>() == null)
            {
                instance.AddComponent<NetworkTransform>();
            }

            // NetworkRigidbodyを追加（存在する場合のみ）
            TryAddNetworkRigidbody(instance);

            // ShellNetworkControllerを追加
            var shellController = instance.GetComponent<ShellNetworkController>();
            if (shellController == null)
            {
                shellController = instance.AddComponent<ShellNetworkController>();
            }

            // 参照を設定
            SetupShellControllerReferences(instance, shellController);

            // Prefabを保存
            PrefabUtility.SaveAsPrefabAsset(instance, destPath);
            DestroyImmediate(instance);

            Debug.Log($"✅ NetworkShell prefab created: {destPath}");
        }

        private static void SetupShellControllerReferences(GameObject shell, ShellNetworkController controller)
        {
            // TankMaskを設定
            controller.m_TankMask = LayerMask.GetMask("Players");

            // パーティクルとオーディオを検索
            controller.m_ExplosionParticles = shell.GetComponentInChildren<ParticleSystem>();
            controller.m_ExplosionAudio = shell.GetComponentInChildren<AudioSource>();
        }
    }
}
