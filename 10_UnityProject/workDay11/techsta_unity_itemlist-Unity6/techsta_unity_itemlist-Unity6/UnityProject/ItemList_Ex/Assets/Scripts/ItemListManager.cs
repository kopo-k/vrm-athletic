using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class ItemListManager : MonoBehaviour
{
    private enum BTN
    {
        Add = 0,
        Remove,
        Buy
    }
    [SerializeField] private Button[] _funcButtons = default;
    [SerializeField] private GameObject _itemPrefab = default;
    [SerializeField] private GameObject _inventoryItemPrefab = default;
    [SerializeField] private GameObject _content = default;         //ボタン生成場所
    [SerializeField] private GameObject _inventoryContent = default;//アイテムの追加場所
    [SerializeField] private Text _itemInfoText = default;          //ボタンの情報を表示するテキスト
    [SerializeField] private Text _moneyText = default;             //所持金表示テキスト
    [SerializeField] private Text _buyButtonText = default;
    private List<ItemContents> _itemButtons;                        //ボタンリスト
    private ItemMasterData _itemMasterData;
    private ItemData[] _itemdatas;
    private int _currentMoney = 300;                                //所持金

    private int _current_ManagedId = 0;                             //受け取ったボタンの番号を格納
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
        _funcButtons[(int)BTN.Buy].onClick.AddListener(OnClickUseItemButton);
        _funcButtons[(int)BTN.Buy].interactable = false;

        //生成したアイテムの情報を保管しておくリスト
        _itemButtons = new List<ItemContents>();

        //ItemMasterDataクラスにシングルトンでアクセス
        _itemMasterData = ItemMasterData.GetInstance();
        _itemdatas = _itemMasterData.GetItemMasterData();

        //初期アイテム生成。UserDataManagerクラスにシングルトンでアクセス
        UserDataManager userDataManager = UserDataManager.GetInstance();
        int[] userItemInventory = userDataManager.GetUserInventorys();

        for (int index = 0; index < userItemInventory.Length; index++)
        {
            MakeButton(userItemInventory[index]);
        }

        //所持金表示
        UpdateMoneyText();
    }

    /// <summary>
    /// クリックされたボタンの情報を受け取る。_managedIdにアイテム識別IDを格納、パネルにアイテムの情報を表示させる\
    /// </summary>
    /// <param name="itemId">ItemContentsクラスからItemIdを受け取る</param>
    /// <param name="fromItemContents">ItemContentsクラスからItemContents自体を受け取る</param>
    public void OnClickItemButton(int itemId, ItemContents fromItemContents)
    {
        _current_ManagedId = itemId;
        _current_ItemContents = fromItemContents;

        //infoパネル更新
        UpdateItemInfoText(_current_ManagedId);
        //Buyボタンの有効化
        _funcButtons[(int)BTN.Buy].interactable = true;

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

        item_content.SendIdToItem(itemId);                    　// アイテム識別Idを与える
        item_content.SendOnClickDelegate(OnClickItemButton);	// アイテムにアイテムボタンが押された時に実行してほしい関数を伝える＝デリゲート

    }

    /// <summary>
    /// 引数で受け取ったターゲットのItemを削除する。UseItemボタンが押下された場合は引数なしのRemove()を呼ぶ。
    /// </summary>
    /// <param name="targetNum">削除したいアイテムをint型で指定</param>
    private void RemoveItem(int targetNum)
    {   //アイテム削除
        Destroy(_itemButtons[targetNum].gameObject);
        _itemButtons.RemoveAt(targetNum);

        //UseButton無効化
        _funcButtons[(int)BTN.Buy].interactable = false;
        if (_itemButtons.Count <= 0)
        {
            _funcButtons[(int)BTN.Remove].interactable = false;
        }
        //所持金表示
        UpdateMoneyText();
    }
    /// <summary>
    /// remove対象のアイテムをGetItemTargetNum()で見つけ、RemoveItem()を呼び出す
    /// </summary>
    private void RemoveCurrentItem()
    {
        int contentNumber = GetItemTargetNum(_current_ItemContents);
        if (contentNumber >= 0)
        {
            RemoveItem(contentNumber);
        }
    }

    /// <summary>
    /// remove対象の_itemButtonsを見つけ、その番号をintで返す。見つからなかった場合は-1を返す
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
        int itemPickup = Random.Range(0, _itemdatas.Length);
        MakeButton(_itemdatas[itemPickup]._id);

        //Removeボタン有効化
        _funcButtons[(int)BTN.Remove].interactable = true;
    }

    /// <summary>
    /// Removeボタン押下時、リストが1以上ならRemoveItem()呼び出し、それ以外の場合はボタン無効化
    /// </summary>
    private void OnClickRemoveItemButton()
    {
        if (_itemButtons.Count > 0)
        {
            //_itemButtonsの要素数をカウントし、最後のアイテムを削除
            RemoveItem(_itemButtons.Count - 1);
        }
        else
        {
            _funcButtons[(int)BTN.Buy].interactable = false;
        }
    }

    /// <summary>
    /// パネル内ボタン押下時はそれに該当するアイテムを削除
    /// </summary>
    private void OnClickUseItemButton()
    {
        if (BuyItem(_current_ManagedId))
        {   //アイテムリストから削除
            RemoveCurrentItem();
            //インベントリに追加
            AddUserInventory(_current_ManagedId);
        }
        else
        {
            //引数に-1を与え、所持金がなかった場合のUpdateInfoTextを呼び出す
            UpdateItemInfoText(-1);
            _funcButtons[(int)BTN.Buy].interactable = false;
        }

    }

    /// <summary>
    /// マネーテキストを更新する
    /// </summary>
    private void UpdateMoneyText()
    {
        _moneyText.text = string.Format("所持金： {0} 円", _currentMoney);
    }

    /// <summary>
    /// アイテムインフォメーションテキストとBuyItemボタンテキストを更新する
    /// </summary>
    /// <param name="itemId">アイテムID</param>
    private void UpdateItemInfoText(int itemId)
    {
        if (0 <= itemId)
        {
            _itemInfoText.text = string.Format("Item : {0}\n\n《{1}》\n\n{2}円",
                itemId,
                _itemdatas[itemId]._name,
                _itemdatas[itemId]._price);
            _buyButtonText.text = "Buy Item";
        }
        else
        {
            _buyButtonText.text = "所持金が足りません!";
        }
    }


    /// <summary>
    /// 対象のアイテムを買う->アイテムの値段分所持金から減らす
    /// </summary>
    private bool BuyItem(int itemId)
    {
        int price = _itemdatas[itemId]._price;
        if (IsBuyItem(itemId))
        {
            _currentMoney -= price;
            return true;
        }
        else
        { return false; }
    }

    /// <summary>
    /// 対象のアイテムを買う事が出来るか？->true,falseで値段より所持金が多いかどうかを返す
    /// </summary>
    private bool IsBuyItem(int itemId)
    {
        int price = _itemdatas[itemId]._price;
        if (price <= _currentMoney)
        {
            return true;
        }
        else { return false; }
    }

  

    /// <summary>
    /// インベントリにアイテムを追加する。
    /// </summary>
    /// <param name="itemid">作成したいアイテムのid</param>
    private void AddUserInventory(int itemId)
    {
        //生成
        GameObject itemObject = Instantiate(_inventoryItemPrefab, Vector3.zero, Quaternion.identity);
        itemObject.transform.SetParent(_inventoryContent.transform);


        InventoryItems itemInventory = itemObject.GetComponent<InventoryItems>();

        itemInventory.SendIdToItem(itemId);                    　// アイテム識別Idを与える
    }

    
}
