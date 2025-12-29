using UnityEngine;
using UnityEngine.UI;


public class InventoryItems : MonoBehaviour
{
    [SerializeField] private Text _itemText = default;
    [SerializeField] private Image _image = default;

    public int _itemId;

    /// <summary>
    /// ItemListManagerからアイテムIDを受け取り、instantiateされたアイテムに格納する
    /// </summary>
    /// <param name="itemId">受け取ったアイテムID</param>
    public void SendIdToItem(int itemId)
    {
        _itemId = itemId;
        SetItemDisplay();
    }

    /// <summary>
    /// インベントリアイテムの表示設定
    /// </summary>
    private void SetItemDisplay()
    {
        ItemMasterData itemdata = ItemMasterData.GetInstance();
        ItemData[] itemDatas = itemdata.GetItemMasterData();

        string spritAaddress = string.Format("fruit_{0:000}", itemDatas[_itemId]._id);
        Sprite setImage = Resources.Load<Sprite>(spritAaddress);
        _itemText.text = itemDatas[_itemId]._name;

        _image.sprite = setImage;

    }
    /// <summary>
    /// ボタンが押されたらゲームオブジェクトを削除
    /// </summary>
    public void OnClickItemButton()
    {
        Destroy(this.gameObject);
    }

    

}
