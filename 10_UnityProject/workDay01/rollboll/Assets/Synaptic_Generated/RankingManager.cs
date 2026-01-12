using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class RankingData
{
    public int id;
    public string player_name;
    public float clear_time;
    public string created_at;
}

[System.Serializable]
public class RankingResponse
{
    public bool success;
    public List<RankingData> rankings;
}

[System.Serializable]
public class RegisterRequest
{
    public string player_name;
    public float clear_time;
}

public class RankingManager : MonoBehaviour
{
    public static RankingManager Instance { get; private set; }

    public Text[] rankTexts; // 5つのランキング表示用テキスト
    private List<float> rankingTimes = new List<float>();
    private const int MAX_RANKING = 5;

    // サーバーURL（ローカルホスト）
    private const string SERVER_URL = "http://localhost/game_ranking";

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
        // サーバーからランキングを取得
        StartCoroutine(FetchRankingFromServer());
    }

    // サーバーからランキングを取得
    IEnumerator FetchRankingFromServer()
    {
        UnityWebRequest request = UnityWebRequest.Get(SERVER_URL + "/get_ranking.php?limit=5");
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string json = request.downloadHandler.text;
            Debug.Log("サーバーからランキング取得: " + json);

            RankingResponse response = JsonUtility.FromJson<RankingResponse>(json);
            
            if (response.success && response.rankings != null)
            {
                rankingTimes.Clear();
                foreach (var rank in response.rankings)
                {
                    rankingTimes.Add(rank.clear_time);
                }
                UpdateRankingDisplay();
            }
        }
        else
        {
            Debug.LogWarning("サーバーからランキング取得失敗: " + request.error);
            // サーバーが利用できない場合はローカルから読み込み
            LoadRankingFromLocal();
        }
    }

    // ローカル（PlayerPrefs）からランキングを読み込み
    void LoadRankingFromLocal()
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
        UpdateRankingDisplay();
    }

    // ローカル（PlayerPrefs）にランキングを保存
    void SaveRankingToLocal()
    {
        for (int i = 0; i < rankingTimes.Count && i < MAX_RANKING; i++)
        {
            PlayerPrefs.SetFloat("Rank" + i, rankingTimes[i]);
        }
        PlayerPrefs.Save();
    }

    // クリアタイムを登録（サーバー＆ローカル）
    public void RegisterTime(float clearTime)
    {
        // サーバーに送信
        StartCoroutine(SendTimeToServer(clearTime));
        
        // ローカルにも保存（バックアップ）
        rankingTimes.Add(clearTime);
        rankingTimes = rankingTimes.OrderBy(t => t).ToList();
        
        if (rankingTimes.Count > MAX_RANKING)
        {
            rankingTimes = rankingTimes.Take(MAX_RANKING).ToList();
        }

        SaveRankingToLocal();
        UpdateRankingDisplay();
    }

    // サーバーにタイムを送信
    IEnumerator SendTimeToServer(float clearTime)
    {
        RegisterRequest data = new RegisterRequest
        {
            player_name = "Player",
            clear_time = clearTime
        };

        string json = JsonUtility.ToJson(data);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(SERVER_URL + "/register.php", "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("サーバーにランキング送信成功: " + request.downloadHandler.text);
            // 送信後、サーバーから最新のランキングを取得
            StartCoroutine(FetchRankingFromServer());
        }
        else
        {
            Debug.LogWarning("サーバーにランキング送信失敗: " + request.error);
        }
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
