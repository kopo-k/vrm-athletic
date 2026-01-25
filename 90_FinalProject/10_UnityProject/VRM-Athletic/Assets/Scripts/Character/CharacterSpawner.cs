using UnityEngine;

/// <summary>
/// キャラクター選択に応じてプレハブを生成するクラス
/// </summary>
public class CharacterSpawner : MonoBehaviour
{
    [Header("キャラクタープレハブ")]
    [SerializeField] private GameObject maleCharacterPrefab;
    [SerializeField] private GameObject femaleCharacterPrefab;

    [Header("スポーン設定")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnHeightOffset = 2f; // 地面から浮かせる高さ

    /// <summary>
    /// 生成されたプレイヤーへの参照
    /// </summary>
    public GameObject SpawnedPlayer { get; private set; }

    private void Start()
    {
        SpawnCharacter();
    }

    /// <summary>
    /// 選択されたキャラクターを生成
    /// </summary>
    public void SpawnCharacter()
    {
        // 選択されたキャラクタータイプを取得
        string characterType = PlayerPrefs.GetString("SelectedCharacter", "male");
        Debug.Log($"Spawning character: {characterType}");

        // プレハブを選択
        GameObject prefab = characterType == "female" ? femaleCharacterPrefab : maleCharacterPrefab;

        if (prefab == null)
        {
            Debug.LogError($"Character prefab is null! Type: {characterType}");
            return;
        }

        // SpawnPointの位置をそのまま使用
        Vector3 position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        // キャラクターを生成
        SpawnedPlayer = Instantiate(prefab, position, rotation);
        SpawnedPlayer.name = "Player";
        SpawnedPlayer.tag = "Player";

        Debug.Log($"Character spawned at {position}");
    }
}
