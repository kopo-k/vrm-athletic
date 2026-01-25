using UnityEngine;

/// <summary>
/// プレイヤーが落下した際にリスポーンさせるクラス
/// Y座標が一定以下になったらスポーン地点に戻す
/// </summary>
public class RespawnManager : MonoBehaviour
{
    [Header("リスポーン設定")]
    [Tooltip("この高さ以下に落ちたらリスポーン")]
    [SerializeField] private float fallThreshold = -5f;

    [Tooltip("リスポーン地点（未設定の場合はCharacterSpawnerのSpawnPointを使用）")]
    [SerializeField] private Transform respawnPoint;

    private Transform player;
    private CharacterController characterController;

    private void Start()
    {
        // リスポーン地点が未設定の場合、CharacterSpawnerのSpawnPointを探す
        if (respawnPoint == null)
        {
            // SpawnPointタグまたは名前で探す
            GameObject spawnObj = GameObject.Find("SpawnPoint");
            if (spawnObj != null)
            {
                respawnPoint = spawnObj.transform;
            }
        }
    }

    private void Update()
    {
        // プレイヤーを探す
        if (player == null)
        {
            FindPlayer();
            return;
        }

        // プレイヤーが落下閾値以下になったらリスポーン
        if (player.position.y < fallThreshold)
        {
            Respawn();
        }
    }

    /// <summary>
    /// プレイヤーを探す
    /// </summary>
    private void FindPlayer()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            characterController = playerObj.GetComponent<CharacterController>();
        }
    }

    /// <summary>
    /// プレイヤーをリスポーン地点に戻す
    /// </summary>
    private void Respawn()
    {
        if (respawnPoint == null)
        {
            Debug.LogWarning("Respawn point is not set!");
            return;
        }

        Debug.Log("Player fell! Respawning...");

        // CharacterControllerがある場合は一時的に無効化（位置を直接変更するため）
        if (characterController != null)
        {
            characterController.enabled = false;
            player.position = respawnPoint.position;
            player.rotation = respawnPoint.rotation;
            characterController.enabled = true;
        }
        else
        {
            player.position = respawnPoint.position;
            player.rotation = respawnPoint.rotation;
        }
    }
}
