using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using Fusion;
#endif

/// <summary>
/// Tank PrefabにPhoton Fusionのネットワークコンポーネントを追加するエディタツール
/// </summary>
public class TankPrefabNetworkSetup : MonoBehaviour
{
#if UNITY_EDITOR
    [ContextMenu("Setup Tank for Network")]
    public void SetupTankForNetwork()
    {
        // Tank Prefabをロード
        GameObject tankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tank.prefab");
        
        if (tankPrefab == null)
        {
            Debug.LogError("Tank Prefab not found at Assets/Prefabs/Tank.prefab");
            return;
        }

        // Prefabのインスタンスを作成（編集用）
        GameObject tankInstance = PrefabUtility.InstantiatePrefab(tankPrefab) as GameObject;
        
        if (tankInstance == null)
        {
            Debug.LogError("Failed to instantiate Tank Prefab");
            return;
        }

        Debug.Log("Setting up Tank Prefab for network...");

        // NetworkObjectを追加（まだ無ければ）
        if (tankInstance.GetComponent<NetworkObject>() == null)
        {
            tankInstance.AddComponent<NetworkObject>();
            Debug.Log("Added NetworkObject");
        }

        // NetworkTransformを追加（まだ無ければ）
        if (tankInstance.GetComponent<NetworkTransform>() == null)
        {
            tankInstance.AddComponent<NetworkTransform>();
            Debug.Log("Added NetworkTransform");
        }

        // TankNetworkスクリプトを追加（まだ無ければ）
        if (tankInstance.GetComponent<TankNetwork>() == null)
        {
            TankNetwork tankNetwork = tankInstance.AddComponent<TankNetwork>();
            
            // 既存のTankMovementから参照をコピー
            Complete.TankMovement tankMovement = tankInstance.GetComponent<Complete.TankMovement>();
            if (tankMovement != null)
            {
                tankNetwork.speed = tankMovement.m_Speed;
                tankNetwork.turnSpeed = tankMovement.m_TurnSpeed;
                tankNetwork.movementAudio = tankMovement.m_MovementAudio;
                tankNetwork.engineIdling = tankMovement.m_EngineIdling;
                tankNetwork.engineDriving = tankMovement.m_EngineDriving;
                tankNetwork.pitchRange = tankMovement.m_PitchRange;
                
                // ParticleSystemsを取得
                tankNetwork.dustTrails = tankInstance.GetComponentsInChildren<ParticleSystem>();
                
                Debug.Log("Copied settings from TankMovement to TankNetwork");
                
                // TankMovementを無効化（削除はしない）
                tankMovement.enabled = false;
                Debug.Log("Disabled TankMovement (use TankNetwork instead)");
            }
        }

        // 変更をPrefabに適用
        PrefabUtility.SaveAsPrefabAsset(tankInstance, "Assets/Prefabs/TankNetwork.prefab");
        Debug.Log("Saved as TankNetwork.prefab");

        // インスタンスを削除
        DestroyImmediate(tankInstance);
        
        Debug.Log("Tank Prefab setup complete! New prefab: Assets/Prefabs/TankNetwork.prefab");
        Debug.Log("Original Tank.prefab was not modified.");
    }
#endif
}
