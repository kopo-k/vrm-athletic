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

    private GameTimer gameTimer;
    private bool hasGoaled = false;

    private void Start()
    {
        // GameTimerを探す
        gameTimer = FindObjectOfType<GameTimer>();
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
        // タイマーを停止
        if (gameTimer != null)
        {
            gameTimer.StopTimer();

            // クリアタイムを保存（PlayerPrefsを使用）
            float clearTime = gameTimer.ElapsedTime;
            PlayerPrefs.SetFloat("ClearTime", clearTime);
            PlayerPrefs.Save();

            Debug.Log($"ゴール！ クリアタイム: {clearTime:F2}秒");
        }

        // Resultシーンに遷移
        SceneManager.LoadScene("Result");
    }
}
