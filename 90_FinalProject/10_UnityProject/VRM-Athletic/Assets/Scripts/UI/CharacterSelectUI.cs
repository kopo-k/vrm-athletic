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
        // キャラクター選択を保存（後でGameData.csに変更）
        PlayerPrefs.SetString("SelectedCharacter", "male");
        PlayerPrefs.Save();

        // Gameシーンに遷移
        SceneManager.LoadScene("Game");
    }

    /// <summary>
    /// 女性キャラクターボタンがクリックされたときの処理
    /// </summary>
    private void OnFemaleButtonClicked()
    {
        // キャラクター選択を保存（後でGameData.csに変更）
        PlayerPrefs.SetString("SelectedCharacter", "female");
        PlayerPrefs.Save();

        // Gameシーンに遷移
        SceneManager.LoadScene("Game");
    }
}
