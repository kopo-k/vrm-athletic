using UnityEngine;

public class ItemMasterData : MonoBehaviour
{
    private static ItemMasterData _instance = null;



    //アイテムマスターデータ
    private ItemData[] itemMasterData ={
        new ItemData(   0,  "リンゴ",  50),
        new ItemData(   1,  "バナナ",  10),
        new ItemData(   2,  "みかん",  20),
        new ItemData(   3,  "メロン",  30)
    };

    /// <summary>
    ///Start()よりも先に呼ばれる関数Awake()、インスタンスを生成する
    /// </summary>
    private void Awake()
    {
        _instance = this;
    }
    /// <summary>
    /// 外部から呼び出される用の、static型の関数。
    /// </summary>
    /// <returns>インスタンスを返す</returns>
    public static ItemMasterData GetInstance()
    {
        return _instance;
    }

    /// <summary>
    /// インスタンスを介して、itemMasterDataにアクセス
    /// </summary>
    /// <returns>itemMasterDataを返す</returns>
    public ItemData[] GetItemMasterData()
    {
        return _instance.itemMasterData;
    }
}
