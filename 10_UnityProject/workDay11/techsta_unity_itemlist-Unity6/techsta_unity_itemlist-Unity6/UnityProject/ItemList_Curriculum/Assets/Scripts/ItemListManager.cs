using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class ItemListManager : MonoBehaviour
{
    private enum BTN
    {
        Add = 0,
        Remove,
        Use
    }

    [SerializeField] private Button[] _funcButtons = default;
    [SerializeField] private GameObject _itemPrefab = default;
    [SerializeField] private GameObject _content = default;//ボタン生成場所
    [SerializeField] private Text _itemInfoText = default; //ボタンの情報を表示するテキスト
    private List<ItemContents> _itemButtons;               //ボタンリスト
    private int DEFAULT_ITEM_SIZE = 10;

    private int          _current_ManagedId = 0;           //受け取ったボタンの番号を格納
    private ItemContents _current_ItemContents = null;

    /// <summary>
    /// 各ボタン設定、UserInventoryを基にアイテムを生成
    /// </summary>
    void Start()
    {
        //button_ItemAdd
        _funcButtons[(int)BTN.Add].onClick.AddListener(OnClickAddItemButton);
        //button_ItemRemove
        _funcButtons[(int)BTN.Remove].onClick.AddListener(OnClickRemoveItemButton);
        //button_ItemUse
        _funcButtons[(int)BTN.Use].onClick.AddListener(OnClickUseItemButton);
        _funcButtons[(int)BTN.Use].interactable = false;

        _itemButtons = new List<ItemContents>();


        for (int index = 0; index < DEFAULT_ITEM_SIZE; index++)
        {
            MakeButton(index);
        }
    }

    /// <summary>
    /// クリックされたボタンの情報を受け取る。_managedIdにアイテム識別IDを格納、パネルにアイテムの情報を表示させる
    /// </summary>
    /// <param name="itemId">ItemContentsクラスからItemIdを受け取る</param>
    public void OnClickItemButton(int itemId, ItemContents fromItemContents)
    {
        _current_ManagedId = itemId;
        _current_ItemContents = fromItemContents;

        _itemInfoText.text = "Item : " + _current_ManagedId;

        //Useボタンの有効化
        _funcButtons[(int)BTN.Use].interactable = true;
    }

    /// <summary>
    /// ボタン生成
    /// </summary>
    /// <param name="itemId">受け取ったIdを基にボタン生成、生成されたボタンにもIdを伝える</param>
	private void MakeButton(int itemId)
    {
        //生成
        GameObject itemObject = Instantiate(_itemPrefab, Vector3.zero, Quaternion.identity);
        itemObject.transform.SetParent(_content.transform);

        //リスト追加
        ItemContents item_content = itemObject.GetComponent<ItemContents>();
        _itemButtons.Add(item_content);

        item_content.SendIdToItem(itemId);                    // アイテム識別Idを与える
        item_content.SendOnClickDelegate(OnClickItemButton);	// アイテムにアイテムボタンが押された時に実行してほしい関数を伝える＝デリゲート

    }

    /// <summary>
    /// 引数で受け取ったターゲットのItemを削除する。UseItemボタンが押下された場合は引数なしのRemove()を呼ぶ。
    /// </summary>
    /// <param name="targetNum">削除したいアイテムをint型で指定</param>
    private void RemoveItem(int targetNum)
    {
        Destroy(_itemButtons[targetNum].gameObject);
        _itemButtons.RemoveAt(targetNum);
        //UseButton無効化
        _funcButtons[(int)BTN.Use].interactable = false;
        if(_itemButtons.Count<= 0)
        {
            _funcButtons[(int)BTN.Remove].interactable = false;
        }
    }
    /// <summary>
    /// remove対象のアイテムをItemContentsクラスから受け取り、GetTargetNum()で該当する_itemButtonsを見つけ、RemoveItem()を呼び出す
    /// </summary>
    private void RemoveCurrentItem()
    {
        int contentNumber = GetItemTargetNum(_current_ItemContents);
        if(contentNumber >= 0)
        {
            RemoveItem(contentNumber);
        }
    }

    /// <summary>
    /// remove対象の_itemButtonsを見つけ、その番号をintで返す
    /// </summary>
    /// <param name="target">ItemContentsクラスから受け取ったremove対象のアイテム</param>
    /// <returns></returns>
    private int GetItemTargetNum(ItemContents target)
    {
        for (int index = 0; index < _itemButtons.Count; index++)
        {
            if (target == _itemButtons[index])
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>
    /// Addボタン押下時、itemMasterDataのもつアイテムデータからランダムで生成
    /// </summary>
    private void OnClickAddItemButton()
    {
        int itemPickup = Random.Range(0, DEFAULT_ITEM_SIZE);
        MakeButton(itemPickup);

        //Removeボタン有効化
        _funcButtons[(int)BTN.Remove].interactable = true;
    }

    /// <summary>
    /// Removeボタン押下時、リストが1以下ならアイテムを削除しボタンを無効化、リストが2以上の場合はアイテム削除のみ
    /// </summary>
    private void OnClickRemoveItemButton()
    {
        if (_itemButtons.Count > 0)
        {
            //_itemButtonsの要素数をカウントし、最後のアイテムを削除
            RemoveItem(_itemButtons.Count - 1);
        }
    }

    /// <summary>
    /// パネル内ボタン押下時はそれに該当するアイテムを削除
    /// </summary>
    private void OnClickUseItemButton()
    {
        RemoveCurrentItem();
    }
}
