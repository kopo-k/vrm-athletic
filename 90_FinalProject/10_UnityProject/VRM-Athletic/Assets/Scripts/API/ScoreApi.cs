using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Supabaseとのスコア送受信を管理するクラス
/// </summary>
public class ScoreApi : MonoBehaviour
{
    /// <summary>
    /// スコアデータ構造（送信用）
    /// </summary>
    [Serializable]
    public class ScoreData
    {
        public string player_name;
        public string character_type;
        public string stage_name;
        public float clear_time;
    }

    /// <summary>
    /// スコアデータ構造（受信用）
    /// </summary>
    [Serializable]
    public class ScoreResponse
    {
        public int id;
        public string player_name;
        public string character_type;
        public string stage_name;
        public float clear_time;
        public string created_at;
    }

    /// <summary>
    /// ランキングデータのラッパー（JSONパース用）
    /// </summary>
    [Serializable]
    public class ScoreResponseArray
    {
        public ScoreResponse[] items;
    }

    /// <summary>
    /// スコアを保存
    /// </summary>
    public void SaveScore(string playerName, string characterType, string stageName, float clearTime, Action<bool> onComplete = null)
    {
        StartCoroutine(PostScore(playerName, characterType, stageName, clearTime, onComplete));
    }

    /// <summary>
    /// ランキングを取得
    /// </summary>
    public void GetRanking(string stageName, int limit, Action<List<ScoreResponse>> onComplete)
    {
        StartCoroutine(FetchRanking(stageName, limit, onComplete));
    }

    /// <summary>
    /// スコアをPOST
    /// </summary>
    private IEnumerator PostScore(string playerName, string characterType, string stageName, float clearTime, Action<bool> onComplete)
    {
        var config = SupabaseConfig.Instance;
        if (config == null)
        {
            onComplete?.Invoke(false);
            yield break;
        }

        ScoreData data = new ScoreData
        {
            player_name = playerName,
            character_type = characterType,
            stage_name = stageName,
            clear_time = clearTime
        };

        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(config.ScoresEndpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            // ヘッダー設定
            request.SetRequestHeader("apikey", config.API_KEY);
            request.SetRequestHeader("Authorization", $"Bearer {config.API_KEY}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Prefer", "return=minimal");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Score saved successfully!");
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError($"Failed to save score: {request.error}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
                onComplete?.Invoke(false);
            }
        }
    }

    /// <summary>
    /// ランキングをGET
    /// </summary>
    private IEnumerator FetchRanking(string stageName, int limit, Action<List<ScoreResponse>> onComplete)
    {
        var config = SupabaseConfig.Instance;
        if (config == null)
        {
            onComplete?.Invoke(new List<ScoreResponse>());
            yield break;
        }

        // クエリパラメータ: ステージ名でフィルタ、クリアタイム昇順、上位N件
        string url = $"{config.ScoresEndpoint}?stage_name=eq.{stageName}&select=*&order=clear_time.asc&limit={limit}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            // ヘッダー設定
            request.SetRequestHeader("apikey", config.API_KEY);
            request.SetRequestHeader("Authorization", $"Bearer {config.API_KEY}");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                Debug.Log($"Ranking received: {responseText}");

                // JSONをパース
                List<ScoreResponse> rankings = ParseRankingJson(responseText);
                onComplete?.Invoke(rankings);
            }
            else
            {
                Debug.LogError($"Failed to get ranking: {request.error}");
                onComplete?.Invoke(new List<ScoreResponse>());
            }
        }
    }

    /// <summary>
    /// JSON配列をパース
    /// </summary>
    private List<ScoreResponse> ParseRankingJson(string json)
    {
        List<ScoreResponse> result = new List<ScoreResponse>();

        try
        {
            // Supabaseは配列で返すため、ラッパーで包む
            string wrappedJson = "{\"items\":" + json + "}";
            ScoreResponseArray array = JsonUtility.FromJson<ScoreResponseArray>(wrappedJson);

            if (array != null && array.items != null)
            {
                result.AddRange(array.items);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON parse error: {e.Message}");
        }

        return result;
    }
}
