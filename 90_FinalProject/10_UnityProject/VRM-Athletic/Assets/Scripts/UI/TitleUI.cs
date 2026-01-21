using UnityEngine;
using UnityEngine.SceneManagement;  // シーン切り替えに必要
using UnityEngine.UI;               // Buttonコンポーネントに必要

/// <summary>
/// タイトル画面のUI制御クラス
/// キャラクター選択画面への遷移を管理する
/// </summary>
public class TitleUI : MonoBehaviour
{
    // Inspectorから設定するボタン参照
    // [SerializeField]でprivateでもInspectorに表示される
    [SerializeField] private Button characterSelectButton;

    /// <summary>
    /// シーン開始時に1回だけ呼ばれる
    /// ボタンのクリックイベントを登録する
    /// </summary>
    private void Start()
    {
        // ボタンがクリックされたらOnCharacterSelectButtonClickedを実行
        characterSelectButton.onClick.AddListener(OnCharacterSelectButtonClicked);
    }

    /// <summary>
    /// キャラクター選択ボタンがクリックされたときの処理
    /// CharacterSelectシーンに遷移する
    /// </summary>
    private void OnCharacterSelectButtonClicked()
    {
        // シーンを切り替える（Build Settingsに登録が必要）
        SceneManager.LoadScene("CharacterSelect");
    }
}
