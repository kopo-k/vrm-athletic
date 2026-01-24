using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// リザルト画面のUI制御クラス
/// クリアタイムの表示とシーン遷移を管理
/// </summary>
public class ResultUI : MonoBehaviour
{
    [Header("UI要素")]
    [SerializeField] private TextMeshProUGUI clearTimeText;
    [SerializeField] private TextMeshProUGUI rankingText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button titleButton;

    private float clearTime;

    private void Start()
    {
        // クリアタイムを取得
        clearTime = PlayerPrefs.GetFloat("ClearTime", 0f);

        // クリアタイムを表示
        DisplayClearTime();

        // ランキングを表示（後でSupabaseから取得に変更）
        DisplayRanking();

        // ボタンイベントを登録
        if (retryButton != null)
        {
            retryButton.onClick.AddListener(OnRetryButtonClicked);
        }

        if (titleButton != null)
        {
            titleButton.onClick.AddListener(OnTitleButtonClicked);
        }

        // スコアをサーバーに送信（後で実装）
        // SendScoreToServer();
    }

    /// <summary>
    /// クリアタイムを表示
    /// </summary>
    private void DisplayClearTime()
    {
        if (clearTimeText != null)
        {
            int minutes = Mathf.FloorToInt(clearTime / 60f);
            int seconds = Mathf.FloorToInt(clearTime % 60f);
            int milliseconds = Mathf.FloorToInt((clearTime * 100f) % 100f);

            clearTimeText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
        }
    }

    /// <summary>
    /// ランキングを表示
    /// TODO: Supabaseから取得するように変更
    /// </summary>
    private void DisplayRanking()
    {
        if (rankingText != null)
        {
            // 仮のランキング表示（後でSupabaseから取得）
            rankingText.text = "Loading...";

            // TODO: Supabaseからランキング取得後に以下のような形式で表示
            // 1. PlayerName  00:32.15
            // 2. PlayerName  00:38.44
            // 3. PlayerName  00:41.22
        }
    }

    /// <summary>
    /// リトライボタンがクリックされたときの処理
    /// </summary>
    private void OnRetryButtonClicked()
    {
        SceneManager.LoadScene("CharacterSelect");
    }

    /// <summary>
    /// タイトルボタンがクリックされたときの処理
    /// </summary>
    private void OnTitleButtonClicked()
    {
        SceneManager.LoadScene("Title");
    }

    /// <summary>
    /// クリアタイムを取得（外部から参照用）
    /// </summary>
    public float GetClearTime()
    {
        return clearTime;
    }
}
