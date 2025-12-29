using UnityEngine;

[System.Serializable]
public class ItemData
{
    public int id;
    public string itemName;
    public string description;
    public ItemCategory category;
    public ItemRarity rarity;
    public int price;
    public int sellPrice;
    public int quantity;
    public Sprite icon;
    
    // ステータス
    public int attack;
    public int defense;
    public int magicPower;
    public int healAmount;
    public int manaAmount;
}

public enum ItemCategory
{
    All,
    Weapon,
    Armor,
    Potion,
    Material
}

public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}
