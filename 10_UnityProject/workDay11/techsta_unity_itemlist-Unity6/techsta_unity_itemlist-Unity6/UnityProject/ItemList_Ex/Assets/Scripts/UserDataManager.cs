using UnityEngine;

public class UserDataManager : MonoBehaviour
{
    private static UserDataManager _userDataInstance = null;

    //初期所持アイテム情報
    private int[] itemInventory ={
        0,
        0,
        1,
        2,
        3,
        3,
        1,
        0,
        2,
        0,
    };

    /// <summary>
    ///Start()よりも先に呼ばれる関数Awake()、インスタンスを生成する
    /// </summary>
    private void Awake()
    {
        _userDataInstance = this;
    }

    public static UserDataManager GetInstance()
    {
        return _userDataInstance;
    }
    /// <summary>
    /// インスタンスを介して、UserDataManagerにアクセス
    /// </summary>
    /// <returns>itemInventoryを返す</returns>
    public int[] GetUserInventorys()
    {
        return _userDataInstance.itemInventory;
    }
}
