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

    // ボタンクリック時の効果音
    [SerializeField] private AudioClip buttonClickSE;

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
    /// 効果音を再生してからCharacterSelectシーンに遷移する
    /// </summary>
    private void OnCharacterSelectButtonClicked()
    {
        if (buttonClickSE != null)
        {
            AudioSource.PlayClipAtPoint(buttonClickSE, Camera.main.transform.position);
        }
        // SE再生後に少し待ってからシーン遷移
        Invoke(nameof(LoadCharacterSelect), 0.3f);
    }

    private void LoadCharacterSelect()
    {
        SceneManager.LoadScene("CharacterSelect");
    }
}
