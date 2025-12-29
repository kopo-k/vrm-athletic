using UnityEngine;
using UnityEngine.UI;

public class ItemContents : MonoBehaviour
{

    [SerializeField] private Button _selfBtn = null;
    [SerializeField] private Text _thisBtnTxt = null;

    public delegate void OnDelegate(int num, ItemContents itemcontents);//delegate定義

    public int _itemId = 0;                                            //Item識別Id
    private OnDelegate _delegateOnClickButton = null;	               //デリゲート変数

    /// <summary>
    /// 自身の紐づいたボタンを取得し、ItemListManagerから受け取ったIdを表示。
    /// ボタンが押下されたらデリゲート変数に代入された関数を引数_itemIdを用いて呼び出し
    /// </summary>
    private void Start()
    {
        _thisBtnTxt.text = "Item : " + _itemId;
        _selfBtn.onClick.AddListener(() =>
        {
            _delegateOnClickButton(_itemId, this);
        }
        );

    }
    /// <summary>
    /// デリゲート変数に、ItemListManagerから受け取った関数を代入
    /// </summary>
    /// <param name="del">ItemListManagerから受け取った関数</param>
    public void SendOnClickDelegate(OnDelegate del)
    {
        _delegateOnClickButton = del;
    }


    /// <summary>
    /// //自身の番号を受け取り、_itemIdに番号を格納
    /// </summary>
    /// <param name="num">ItemListManagerから受け取ったId</param>
    public void SendIdToItem(int num)
    {
        _itemId = num;
    }

}
