using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// キャラクター選択画面のUI制御クラス
/// 男性/女性キャラクターの選択とGameシーンへの遷移を管理
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    [SerializeField] private Button maleButton;
    [SerializeField] private Button femaleButton;

    [Header("効果音")]
    [SerializeField] private AudioClip buttonClickSE;

    private void Start()
    {
        // ボタンのクリックイベントを登録
        maleButton.onClick.AddListener(OnMaleButtonClicked);
        femaleButton.onClick.AddListener(OnFemaleButtonClicked);
    }

    /// <summary>
    /// 男性キャラクターボタンがクリックされたときの処理
    /// </summary>
    private void OnMaleButtonClicked()
    {
        PlayButtonSE();
        // キャラクター選択を保存（後でGameData.csに変更）
        PlayerPrefs.SetString("SelectedCharacter", "male");
        PlayerPrefs.Save();

        // SE再生後に遷移
        Invoke(nameof(LoadGameScene), 0.3f);
    }

    /// <summary>
    /// 女性キャラクターボタンがクリックされたときの処理
    /// </summary>
    private void OnFemaleButtonClicked()
    {
        PlayButtonSE();
        // キャラクター選択を保存（後でGameData.csに変更）
        PlayerPrefs.SetString("SelectedCharacter", "female");
        PlayerPrefs.Save();

        // SE再生後に遷移
        Invoke(nameof(LoadGameScene), 0.3f);
    }

    private void PlayButtonSE()
    {
        if (buttonClickSE != null)
        {
            AudioSource.PlayClipAtPoint(buttonClickSE, Camera.main.transform.position);
        }
    }

    private void LoadGameScene()
    {
        SceneManager.LoadScene("Game");
    }
}
