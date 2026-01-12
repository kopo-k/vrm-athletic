using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class RankingManager : MonoBehaviour
{
    public static RankingManager Instance { get; private set; }

    public Text[] rankTexts; // 5つのランキング表示用テキスト
    private List<float> rankingTimes = new List<float>();
    private const int MAX_RANKING = 5;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        LoadRanking();
        UpdateRankingDisplay();
    }

    // ランキングをPlayerPrefsから読み込み
    void LoadRanking()
    {
        rankingTimes.Clear();
        for (int i = 0; i < MAX_RANKING; i++)
        {
            float time = PlayerPrefs.GetFloat("Rank" + i, -1f);
            if (time > 0)
            {
                rankingTimes.Add(time);
            }
        }
    }

    // ランキングをPlayerPrefsに保存
    void SaveRanking()
    {
        for (int i = 0; i < rankingTimes.Count && i < MAX_RANKING; i++)
        {
            PlayerPrefs.SetFloat("Rank" + i, rankingTimes[i]);
        }
        PlayerPrefs.Save();
    }

    // クリアタイムを登録
    public void RegisterTime(float clearTime)
    {
        rankingTimes.Add(clearTime);
        // 昇順（早い順）にソート
        rankingTimes = rankingTimes.OrderBy(t => t).ToList();
        
        // 上位5件のみ保持
        if (rankingTimes.Count > MAX_RANKING)
        {
            rankingTimes = rankingTimes.Take(MAX_RANKING).ToList();
        }

        SaveRanking();
        UpdateRankingDisplay();
    }

    // ランキング表示を更新
    void UpdateRankingDisplay()
    {
        for (int i = 0; i < rankTexts.Length; i++)
        {
            if (i < rankingTimes.Count)
            {
                rankTexts[i].text = (i + 1) + ". " + rankingTimes[i].ToString("F2") + "s";
                rankTexts[i].color = Color.yellow;
            }
            else
            {
                rankTexts[i].text = (i + 1) + ". ---";
                rankTexts[i].color = Color.gray;
            }
        }
    }

    // ランキングをリセット（デバッグ用）
    public void ResetRanking()
    {
        for (int i = 0; i < MAX_RANKING; i++)
        {
            PlayerPrefs.DeleteKey("Rank" + i);
        }
        rankingTimes.Clear();
        UpdateRankingDisplay();
    }
}
