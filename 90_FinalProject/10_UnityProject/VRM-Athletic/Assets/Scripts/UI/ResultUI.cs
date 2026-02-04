using System.Collections.Generic;
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

    [Header("設定")]
    [SerializeField] private string stageName = "Stage1";
    [SerializeField] private int rankingLimit = 5;

    [Header("効果音")]
    [SerializeField] private AudioClip buttonClickSE;

    private float clearTime;
    private ScoreApi scoreApi;

    private void Start()
    {
        // マウスカーソルを表示（Gameシーンでロックされているため）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // ScoreApiを取得または作成
        scoreApi = FindObjectOfType<ScoreApi>();
        if (scoreApi == null)
        {
            GameObject apiObj = new GameObject("ScoreApi");
            scoreApi = apiObj.AddComponent<ScoreApi>();
        }

        // クリアタイムを取得
        clearTime = PlayerPrefs.GetFloat("ClearTime", 0f);

        // クリアタイムを表示
        DisplayClearTime();

        // ランキングを取得・表示
        FetchAndDisplayRanking();

        // ボタンイベントを登録
        if (retryButton != null)
        {
            retryButton.onClick.AddListener(OnRetryButtonClicked);
        }

        if (titleButton != null)
        {
            titleButton.onClick.AddListener(OnTitleButtonClicked);
        }
    }

    /// <summary>
    /// クリアタイムを表示（黄色で表示）
    /// </summary>
    private void DisplayClearTime()
    {
        if (clearTimeText != null)
        {
            int minutes = Mathf.FloorToInt(clearTime / 60f);
            int seconds = Mathf.FloorToInt(clearTime % 60f);
            int milliseconds = Mathf.FloorToInt((clearTime * 100f) % 100f);

            // 黄色で表示
            clearTimeText.text = string.Format("<color=yellow>{0:00}:{1:00}.{2:00}</color>", minutes, seconds, milliseconds);
        }
    }

    /// <summary>
    /// ランキングを取得して表示
    /// </summary>
    private void FetchAndDisplayRanking()
    {
        if (rankingText != null)
        {
            rankingText.text = "Loading...";
        }

        scoreApi.GetRanking(stageName, rankingLimit, OnRankingReceived);
    }

    /// <summary>
    /// ランキング取得完了時のコールバック
    /// </summary>
    private void OnRankingReceived(List<ScoreApi.ScoreResponse> rankings)
    {
        if (rankingText == null) return;

        if (rankings == null || rankings.Count == 0)
        {
            rankingText.text = "No data";
            return;
        }

        // ランキングを整形して表示（タイムのみ、背景画像の番号に合わせて余白を追加）
        // 自分の記録と一致するものは黄色で表示
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < rankings.Count; i++)
        {
            var score = rankings[i];
            string timeStr = FormatTime(score.clear_time);

            // 自分の記録と一致するか判定（0.01秒以内の誤差は同一とみなす）
            bool isMyScore = Mathf.Abs(score.clear_time - clearTime) < 0.01f;

            if (isMyScore)
            {
                sb.AppendLine($"<color=yellow>{timeStr}</color>");
            }
            else
            {
                sb.AppendLine(timeStr);
            }
            sb.AppendLine(); // 余白用の空行
        }

        rankingText.text = sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 時間をフォーマット
    /// </summary>
    private string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        int milliseconds = Mathf.FloorToInt((time * 100f) % 100f);
        return string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
    }

    /// <summary>
    /// リトライボタンがクリックされたときの処理
    /// </summary>
    private void OnRetryButtonClicked()
    {
        PlayButtonSE();
        Invoke(nameof(LoadCharacterSelectScene), 0.3f);
    }

    /// <summary>
    /// タイトルボタンがクリックされたときの処理
    /// </summary>
    private void OnTitleButtonClicked()
    {
        PlayButtonSE();
        Invoke(nameof(LoadTitleScene), 0.3f);
    }

    private void PlayButtonSE()
    {
        if (buttonClickSE != null)
        {
            AudioSource.PlayClipAtPoint(buttonClickSE, Camera.main.transform.position);
        }
    }

    private void LoadCharacterSelectScene()
    {
        SceneManager.LoadScene("CharacterSelect");
    }

    private void LoadTitleScene()
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
