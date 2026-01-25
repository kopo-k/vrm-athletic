using UnityEngine;

/// <summary>
/// Supabase接続設定を管理するScriptableObject
/// Assets/Resources/SupabaseSettings.asset に配置して使用
/// </summary>
[CreateAssetMenu(fileName = "SupabaseSettings", menuName = "Config/Supabase Settings")]
public class SupabaseConfig : ScriptableObject
{
    [Header("Supabase設定")]
    [SerializeField] private string supabaseUrl;
    [SerializeField] private string apiKey;

    public string URL => supabaseUrl;
    public string API_KEY => apiKey;
    public string ScoresEndpoint => $"{supabaseUrl}/rest/v1/scores";

    // シングルトンインスタンス
    private static SupabaseConfig _instance;

    public static SupabaseConfig Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Resources.Load<SupabaseConfig>("SupabaseSettings");
                if (_instance == null)
                {
                    Debug.LogError("SupabaseSettings not found in Resources folder!");
                }
            }
            return _instance;
        }
    }
}
