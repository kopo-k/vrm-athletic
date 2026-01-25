using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ゴール判定を行うクラス
/// プレイヤーがゴールに触れたらResultシーンに遷移
/// </summary>
public class GoalTrigger : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string stageName = "Stage1";
    [SerializeField] private string defaultPlayerName = "Player";

    private GameTimer gameTimer;
    private ScoreApi scoreApi;
    private bool hasGoaled = false;

    private void Start()
    {
        // GameTimerを探す
        gameTimer = FindObjectOfType<GameTimer>();

        // ScoreApiを取得または作成
        scoreApi = FindObjectOfType<ScoreApi>();
        if (scoreApi == null)
        {
            GameObject apiObj = new GameObject("ScoreApi");
            scoreApi = apiObj.AddComponent<ScoreApi>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 既にゴールしている場合は無視
        if (hasGoaled) return;

        // プレイヤーかどうか確認
        if (other.CompareTag(playerTag))
        {
            hasGoaled = true;
            OnGoal();
        }
    }

    /// <summary>
    /// ゴール時の処理
    /// </summary>
    private void OnGoal()
    {
        float clearTime = 0f;

        // タイマーを停止
        if (gameTimer != null)
        {
            gameTimer.StopTimer();
            clearTime = gameTimer.ElapsedTime;

            // クリアタイムを保存（PlayerPrefsを使用）
            PlayerPrefs.SetFloat("ClearTime", clearTime);
            PlayerPrefs.Save();

            Debug.Log($"ゴール！ クリアタイム: {clearTime:F2}秒");
        }

        // スコアをSupabaseに送信してからシーン遷移
        SendScoreToServer(clearTime);
    }

    /// <summary>
    /// スコアをサーバーに送信
    /// </summary>
    private void SendScoreToServer(float clearTime)
    {
        // キャラクタータイプを取得
        string characterType = PlayerPrefs.GetString("SelectedCharacter", "male");

        // プレイヤー名を取得（設定されていない場合はデフォルト）
        string playerName = PlayerPrefs.GetString("PlayerName", defaultPlayerName);

        // スコアを送信し、完了後にシーン遷移
        scoreApi.SaveScore(playerName, characterType, stageName, clearTime, (success) =>
        {
            if (success)
            {
                Debug.Log("Score saved to server!");
            }
            else
            {
                Debug.LogWarning("Failed to save score to server, but game continues.");
            }

            // 通信完了後にResultシーンに遷移
            SceneManager.LoadScene("Result");
        });
    }
}
