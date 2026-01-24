using UnityEngine;
using TMPro;

/// <summary>
/// ゲーム中のタイマーを管理するクラス
/// タイムの計測と表示を行う
/// </summary>
public class GameTimer : MonoBehaviour
{
    [Header("UI設定")]
    [SerializeField] private TextMeshProUGUI timerText;

    // タイマー変数
    private float elapsedTime = 0f;
    // タイマーが動いているか
    private bool isRunning = false;

    /// <summary>
    /// 現在の経過時間を取得
    /// </summary>
    public float ElapsedTime => elapsedTime;

    private void Start()
    {
        // ゲーム開始時にタイマースタート
        StartTimer();
    }

    private void Update()
    {
        if (isRunning)
        {
            elapsedTime += Time.deltaTime;
            UpdateTimerDisplay();
        }
    }

    /// <summary>
    /// タイマーを開始
    /// </summary>
    public void StartTimer()
    {
        elapsedTime = 0f;
        isRunning = true;
    }

    /// <summary>
    /// タイマーを停止
    /// </summary>
    public void StopTimer()
    {
        isRunning = false;
    }

    /// <summary>
    /// タイマー表示を更新
    /// </summary>
    private void UpdateTimerDisplay()
    {
        if (timerText != null)
        {
            // 分:秒.ミリ秒 形式で表示
            int minutes = Mathf.FloorToInt(elapsedTime / 60f);
            int seconds = Mathf.FloorToInt(elapsedTime % 60f);
            int milliseconds = Mathf.FloorToInt((elapsedTime * 100f) % 100f);

            timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
        }
    }
}
