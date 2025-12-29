using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ItemListManager : MonoBehaviour
{
    public Transform itemGridContent;
    public GameObject itemCellPrefab;
    
    public GameObject detailPanel;
    public Text detailNameText;
    public Text detailDescriptionText;
    public Text detailStatsText;
    public Text detailPriceText;
    public Image detailIconImage;
    public Image detailRarityBg;
    
    public Button allButton;
    public Button weaponButton;
    public Button armorButton;
    public Button potionButton;
    public Button materialButton;
    
    public List<ItemData> allItems = new List<ItemData>();
    
    private List<ItemCell> currentItemCells = new List<ItemCell>();
    private ItemCategory currentFilter = ItemCategory.All;
    
    void Start()
    {
        SetupFilterButtons();
        GenerateSampleItems();
        RefreshItemList();
        
        if (detailPanel != null)
            detailPanel.SetActive(false);
    }
    
    void SetupFilterButtons()
    {
        if (allButton != null)
            allButton.onClick.AddListener(() => FilterItems(ItemCategory.All));
        if (weaponButton != null)
            weaponButton.onClick.AddListener(() => FilterItems(ItemCategory.Weapon));
        if (armorButton != null)
            armorButton.onClick.AddListener(() => FilterItems(ItemCategory.Armor));
        if (potionButton != null)
            potionButton.onClick.AddListener(() => FilterItems(ItemCategory.Potion));
        if (materialButton != null)
            materialButton.onClick.AddListener(() => FilterItems(ItemCategory.Material));
    }
    
    public void FilterItems(ItemCategory category)
    {
        currentFilter = category;
        RefreshItemList();
    }
    
    void RefreshItemList()
    {
        foreach (var cell in currentItemCells)
        {
            if (cell != null)
                Destroy(cell.gameObject);
        }
        currentItemCells.Clear();
        
        List<ItemData> filteredItems = GetFilteredItems();
        
        foreach (var item in filteredItems)
        {
            GameObject cellObj = Instantiate(itemCellPrefab, itemGridContent);
            ItemCell cell = cellObj.GetComponent<ItemCell>();
            
            if (cell != null)
            {
                cell.SetupItem(item);
                currentItemCells.Add(cell);
            }
        }
    }
    
    List<ItemData> GetFilteredItems()
    {
        if (currentFilter == ItemCategory.All)
            return allItems;
        else
            return allItems.Where(item => item.category == currentFilter).ToList();
    }
    
    public void OnItemSelected(ItemData item)
    {
        if (detailPanel != null)
            detailPanel.SetActive(true);
        
        if (detailNameText != null)
            detailNameText.text = item.itemName;
        
        if (detailDescriptionText != null)
            detailDescriptionText.text = item.description;
        
        if (detailStatsText != null)
            detailStatsText.text = GenerateStatsText(item);
        
        if (detailPriceText != null)
            detailPriceText.text = "購入: " + item.price + "G\\n売却: " + item.sellPrice + "G";
        
        if (detailRarityBg != null)
            detailRarityBg.color = GetRarityColor(item.rarity);
    }
    
    string GenerateStatsText(ItemData item)
    {
        string text = "カテゴリ: " + GetCategoryText(item.category) + "\\n";
        text += "レア度: " + GetRarityText(item.rarity) + "\\n\\n";
        
        switch (item.category)
        {
            case ItemCategory.Weapon:
                text += "攻撃力: +" + item.attack + "\\n";
                if (item.magicPower > 0)
                    text += "魔力: +" + item.magicPower + "\\n";
                break;
            case ItemCategory.Armor:
                text += "防御力: +" + item.defense + "\\n";
                break;
            case ItemCategory.Potion:
                if (item.healAmount > 0)
                    text += "HP回復: +" + item.healAmount + "\\n";
                if (item.manaAmount > 0)
                    text += "MP回復: +" + item.manaAmount + "\\n";
                break;
        }
        
        return text;
    }
    
    string GetCategoryText(ItemCategory category)
    {
        switch (category)
        {
            case ItemCategory.Weapon: return "武器";
            case ItemCategory.Armor: return "防具";
            case ItemCategory.Potion: return "ポーション";
            case ItemCategory.Material: return "素材";
            default: return "その他";
        }
    }
    
    string GetRarityText(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return "コモン";
            case ItemRarity.Uncommon: return "アンコモン";
            case ItemRarity.Rare: return "レア";
            case ItemRarity.Epic: return "エピック";
            case ItemRarity.Legendary: return "レジェンダリー";
            default: return "不明";
        }
    }
    
    Color GetRarityColor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return new Color(0.7f, 0.7f, 0.7f);
            case ItemRarity.Uncommon: return new Color(0.2f, 0.8f, 0.2f);
            case ItemRarity.Rare: return new Color(0.2f, 0.5f, 1f);
            case ItemRarity.Epic: return new Color(0.6f, 0.2f, 0.9f);
            case ItemRarity.Legendary: return new Color(1f, 0.8f, 0.2f);
            default: return Color.white;
        }
    }
    
    void GenerateSampleItems()
    {
        allItems.Add(CreateItem(1, "伝説の剣", "伝説の勇者が使ったという剣", ItemCategory.Weapon, ItemRarity.Legendary, 50000, 25000, 1, 150, 0, 50));
        allItems.Add(CreateItem(2, "鋼の剣", "頑丈な鋼で作られた剣", ItemCategory.Weapon, ItemRarity.Rare, 5000, 2500, 1, 80, 0, 0));
        allItems.Add(CreateItem(3, "木の剣", "訓練用の木の剣", ItemCategory.Weapon, ItemRarity.Common, 100, 50, 1, 10, 0, 0));
        allItems.Add(CreateItem(4, "魔法の杖", "魔力を高める杖", ItemCategory.Weapon, ItemRarity.Epic, 15000, 7500, 1, 30, 0, 120));
        
        allItems.Add(CreateItem(5, "ドラゴンアーマー", "ドラゴンの鱗で作られた鎧", ItemCategory.Armor, ItemRarity.Legendary, 60000, 30000, 1, 0, 200, 0));
        allItems.Add(CreateItem(6, "鉄の鎧", "一般的な鉄の鎧", ItemCategory.Armor, ItemRarity.Uncommon, 2000, 1000, 1, 0, 50, 0));
        allItems.Add(CreateItem(7, "革の鎧", "軽量な革の鎧", ItemCategory.Armor, ItemRarity.Common, 500, 250, 1, 0, 20, 0));
        
        allItems.Add(CreateItem(8, "万能薬", "すべてを癒す薬", ItemCategory.Potion, ItemRarity.Legendary, 10000, 5000, 5, 0, 0, 0, 9999, 9999));
        allItems.Add(CreateItem(9, "高級回復薬", "HPを大きく回復する", ItemCategory.Potion, ItemRarity.Epic, 1000, 500, 10, 0, 0, 0, 500, 0));
        allItems.Add(CreateItem(10, "回復薬", "HPを回復する", ItemCategory.Potion, ItemRarity.Uncommon, 100, 50, 20, 0, 0, 0, 100, 0));
        allItems.Add(CreateItem(11, "マナポーション", "MPを回復する", ItemCategory.Potion, ItemRarity.Uncommon, 100, 50, 15, 0, 0, 0, 0, 50));
        
        allItems.Add(CreateItem(12, "竜の牙", "強力な武器の素材", ItemCategory.Material, ItemRarity.Epic, 5000, 2500, 3));
        allItems.Add(CreateItem(13, "魔石", "魔法の触媒", ItemCategory.Material, ItemRarity.Rare, 1000, 500, 10));
        allItems.Add(CreateItem(14, "鉄鉱石", "武器や防具の材料", ItemCategory.Material, ItemRarity.Common, 50, 25, 50));
        allItems.Add(CreateItem(15, "薬草", "回復薬の材料", ItemCategory.Material, ItemRarity.Common, 10, 5, 99));
    }
    
    ItemData CreateItem(int id, string name, string desc, ItemCategory category, ItemRarity rarity, int price, int sellPrice, int quantity, int attack = 0, int defense = 0, int magic = 0, int heal = 0, int mana = 0)
    {
        ItemData item = new ItemData
        {
            id = id,
            itemName = name,
            description = desc,
            category = category,
            rarity = rarity,
            price = price,
            sellPrice = sellPrice,
            quantity = quantity,
            attack = attack,
            defense = defense,
            magicPower = magic,
            healAmount = heal,
            manaAmount = mana
        };
        return item;
    }
}
