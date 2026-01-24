using UnityEngine;

/// <summary>
/// ゲーム全体の進行を管理するクラス
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("参照")]
    [SerializeField] private GameTimer gameTimer;
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private GameObject playerPrefab;

    private GameObject currentPlayer;

    private void Awake()
    {
        // シングルトンパターン
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // プレイヤーをスポーン（必要な場合）
        if (playerPrefab != null && playerSpawnPoint != null)
        {
            SpawnPlayer();
        }
    }

    /// <summary>
    /// プレイヤーをスポーンする
    /// </summary>
    private void SpawnPlayer()
    {
        if (currentPlayer != null)
        {
            Destroy(currentPlayer);
        }

        currentPlayer = Instantiate(playerPrefab, playerSpawnPoint.position, playerSpawnPoint.rotation);
    }

    /// <summary>
    /// ゲームをリスタート
    /// </summary>
    public void RestartGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
    }
}
