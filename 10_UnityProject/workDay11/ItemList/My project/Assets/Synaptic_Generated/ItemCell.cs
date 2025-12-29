using UnityEngine;
using UnityEngine.UI;

public class ItemCell : MonoBehaviour
{
    public Image iconImage;
    public Image backgroundImage;
    public Text nameText;
    public Text quantityText;
    public Image rarityBorder;
    
    private ItemData itemData;
    private Button button;
    
    void Awake()
    {
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnItemClicked);
        }
    }
    
    public void SetupItem(ItemData data)
    {
        itemData = data;
        
        if (nameText != null)
            nameText.text = data.itemName;
        
        if (quantityText != null)
            quantityText.text = "x" + data.quantity;
        
        if (rarityBorder != null)
            rarityBorder.color = GetRarityColor(data.rarity);
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
    
    void OnItemClicked()
    {
        ItemListManager manager = FindObjectOfType<ItemListManager>();
        if (manager != null)
        {
            manager.OnItemSelected(itemData);
        }
    }
}
