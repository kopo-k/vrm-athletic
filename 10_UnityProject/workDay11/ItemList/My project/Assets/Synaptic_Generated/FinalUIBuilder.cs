using UnityEngine;
using UnityEngine.UI;

public class FinalUIBuilder : MonoBehaviour
{
    void Start()
    {
        BuildCompleteUI();
    }
    
    void BuildCompleteUI()
    {
        Debug.Log("🚀 アイテムリストUI構築開始！");
        
        // Canvas作成
        GameObject canvas = new GameObject("ItemListCanvas");
        Canvas c = canvas.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvas.AddComponent<GraphicRaycaster>();
        
        // 背景
        GameObject bg = MakePanel(canvas.transform, "BG", new Color(0.1f, 0.1f, 0.15f));
        
        // ヘッダー
        GameObject header = MakePanel(canvas.transform, "Header", new Color(0.2f, 0.2f, 0.3f));
        RectTransform headerRect = header.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0, 100);
        MakeText(header.transform, "Title", "アイテムリスト", 48);
        
        // タブパネル
        GameObject tabPanel = MakePanel(canvas.transform, "TabPanel", new Color(0.15f, 0.15f, 0.2f));
        RectTransform tabRect = tabPanel.GetComponent<RectTransform>();
        tabRect.anchorMin = new Vector2(0, 1);
        tabRect.anchorMax = new Vector2(1, 1);
        tabRect.pivot = new Vector2(0.5f, 1);
        tabRect.anchoredPosition = new Vector2(0, -100);
        tabRect.sizeDelta = new Vector2(0, 80);
        
        HorizontalLayoutGroup hlg = tabPanel.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.padding = new RectOffset(20, 20, 10, 10);
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        
        GameObject allBtn = MakeButton(tabPanel.transform, "AllTab", "すべて");
        GameObject weaponBtn = MakeButton(tabPanel.transform, "WeaponTab", "武器");
        GameObject armorBtn = MakeButton(tabPanel.transform, "ArmorTab", "防具");
        GameObject potionBtn = MakeButton(tabPanel.transform, "PotionTab", "ポーション");
        GameObject materialBtn = MakeButton(tabPanel.transform, "MaterialTab", "素材");
        
        // コンテンツエリア
        GameObject content = new GameObject("ContentArea");
        content.transform.SetParent(canvas.transform, false);
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(20, 20);
        contentRect.offsetMax = new Vector2(-20, -200);
        
        // スクロールビュー
        GameObject scroll = MakePanel(content.transform, "ScrollView", new Color(0.05f, 0.05f, 0.1f));
        RectTransform scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = new Vector2(0.6f, 1);
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = new Vector2(-10, 0);
        
        ScrollRect sr = scroll.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        
        GameObject scrollContent = new GameObject("Content");
        scrollContent.transform.SetParent(scroll.transform, false);
        RectTransform scRect = scrollContent.AddComponent<RectTransform>();
        scRect.anchorMin = new Vector2(0, 1);
        scRect.anchorMax = new Vector2(1, 1);
        scRect.pivot = new Vector2(0.5f, 1);
        scRect.anchoredPosition = Vector2.zero;
        scRect.sizeDelta = new Vector2(0, 1000);
        
        GridLayoutGroup grid = scrollContent.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(180, 220);
        grid.spacing = new Vector2(15, 15);
        grid.padding = new RectOffset(15, 15, 15, 15);
        
        ContentSizeFitter csf = scrollContent.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        sr.content = scRect;
        sr.viewport = scrollRect;
        
        // 詳細パネル
        GameObject detail = MakePanel(content.transform, "DetailPanel", new Color(0.1f, 0.1f, 0.15f));
        RectTransform detailRect = detail.GetComponent<RectTransform>();
        detailRect.anchorMin = new Vector2(0.62f, 0);
        detailRect.anchorMax = Vector2.one;
        detailRect.offsetMin = Vector2.zero;
        detailRect.offsetMax = Vector2.zero;
        
        GameObject detailIconBg = MakePanel(detail.transform, "DetailRarityBg", new Color(0.2f, 0.2f, 0.3f));
        RectTransform dibRect = detailIconBg.GetComponent<RectTransform>();
        dibRect.anchorMin = new Vector2(0.5f, 1);
        dibRect.anchorMax = new Vector2(0.5f, 1);
        dibRect.pivot = new Vector2(0.5f, 1);
        dibRect.anchoredPosition = new Vector2(0, -20);
        dibRect.sizeDelta = new Vector2(150, 150);
        
        GameObject icon = MakePanel(detailIconBg.transform, "DetailIconImage", Color.clear);
        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.offsetMin = new Vector2(10, 10);
        iconRect.offsetMax = new Vector2(-10, -10);
        
        GameObject detailName = MakeText(detail.transform, "DetailNameText", "", 32);
        RectTransform dnRect = detailName.GetComponent<RectTransform>();
        dnRect.anchorMin = new Vector2(0.5f, 1);
        dnRect.anchorMax = new Vector2(0.5f, 1);
        dnRect.pivot = new Vector2(0.5f, 1);
        dnRect.anchoredPosition = new Vector2(0, -190);
        dnRect.sizeDelta = new Vector2(0, 60);
        
        GameObject detailDesc = MakeText(detail.transform, "DetailDescriptionText", "", 20);
        RectTransform ddRect = detailDesc.GetComponent<RectTransform>();
        ddRect.anchorMin = new Vector2(0.5f, 1);
        ddRect.anchorMax = new Vector2(0.5f, 1);
        ddRect.pivot = new Vector2(0.5f, 1);
        ddRect.anchoredPosition = new Vector2(0, -270);
        ddRect.sizeDelta = new Vector2(-40, 100);
        detailDesc.GetComponent<Text>().alignment = TextAnchor.UpperLeft;
        
        GameObject detailStats = MakeText(detail.transform, "DetailStatsText", "", 20);
        RectTransform dsRect = detailStats.GetComponent<RectTransform>();
        dsRect.anchorMin = new Vector2(0.5f, 1);
        dsRect.anchorMax = new Vector2(0.5f, 1);
        dsRect.pivot = new Vector2(0.5f, 1);
        dsRect.anchoredPosition = new Vector2(0, -390);
        dsRect.sizeDelta = new Vector2(-40, 200);
        detailStats.GetComponent<Text>().alignment = TextAnchor.UpperLeft;
        
        GameObject detailPrice = MakeText(detail.transform, "DetailPriceText", "", 24);
        RectTransform dpRect = detailPrice.GetComponent<RectTransform>();
        dpRect.anchorMin = new Vector2(0.5f, 1);
        dpRect.anchorMax = new Vector2(0.5f, 1);
        dpRect.pivot = new Vector2(0.5f, 1);
        dpRect.anchoredPosition = new Vector2(0, -610);
        dpRect.sizeDelta = new Vector2(-40, 80);
        detailPrice.GetComponent<Text>().alignment = TextAnchor.UpperLeft;
        
        // アイテムセルプレハブ
        GameObject cellPrefab = MakeItemCell();
        
        // マネージャー追加
        ItemListManager mgr = canvas.AddComponent<ItemListManager>();
        mgr.itemGridContent = scrollContent.transform;
        mgr.itemCellPrefab = cellPrefab;
        mgr.detailPanel = detail;
        mgr.detailNameText = detailName.GetComponent<Text>();
        mgr.detailDescriptionText = detailDesc.GetComponent<Text>();
        mgr.detailStatsText = detailStats.GetComponent<Text>();
        mgr.detailPriceText = detailPrice.GetComponent<Text>();
        mgr.detailIconImage = icon.GetComponent<Image>();
        mgr.detailRarityBg = detailIconBg.GetComponent<Image>();
        mgr.allButton = allBtn.GetComponent<Button>();
        mgr.weaponButton = weaponBtn.GetComponent<Button>();
        mgr.armorButton = armorBtn.GetComponent<Button>();
        mgr.potionButton = potionBtn.GetComponent<Button>();
        mgr.materialButton = materialBtn.GetComponent<Button>();
        
        Debug.Log("✅ UI構築完了！");
        Destroy(gameObject);
    }
    
    GameObject MakePanel(Transform parent, string name, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = obj.AddComponent<Image>();
        img.color = color;
        return obj;
    }
    
    GameObject MakeText(Transform parent, string name, string text, int size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Text t = obj.AddComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        return obj;
    }
    
    GameObject MakeButton(Transform parent, string name, string label)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160, 60);
        Image img = obj.AddComponent<Image>();
        img.color = new Color(0.3f, 0.3f, 0.4f);
        Button btn = obj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = new Color(0.3f, 0.3f, 0.4f);
        cb.highlightedColor = new Color(0.4f, 0.4f, 0.5f);
        cb.pressedColor = new Color(0.2f, 0.2f, 0.3f);
        btn.colors = cb;
        MakeText(obj.transform, "Text", label, 24);
        return obj;
    }
    
    GameObject MakeItemCell()
    {
        GameObject cell = new GameObject("ItemCellPrefab");
        RectTransform rt = cell.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(180, 220);
        Image bg = cell.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.2f);
        Button btn = cell.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = new Color(0.15f, 0.15f, 0.2f);
        cb.highlightedColor = new Color(0.25f, 0.25f, 0.35f);
        cb.pressedColor = new Color(0.1f, 0.1f, 0.15f);
        btn.colors = cb;
        
        GameObject border = MakePanel(cell.transform, "RarityBorder", Color.white);
        GameObject iconBg = MakePanel(cell.transform, "IconBg", new Color(0.1f, 0.1f, 0.15f));
        RectTransform ibRect = iconBg.GetComponent<RectTransform>();
        ibRect.anchorMin = new Vector2(0.5f, 1);
        ibRect.anchorMax = new Vector2(0.5f, 1);
        ibRect.pivot = new Vector2(0.5f, 1);
        ibRect.anchoredPosition = new Vector2(0, -10);
        ibRect.sizeDelta = new Vector2(140, 140);
        
        GameObject icon = MakePanel(iconBg.transform, "IconImage", Color.clear);
        RectTransform icRect = icon.GetComponent<RectTransform>();
        icRect.offsetMin = new Vector2(5, 5);
        icRect.offsetMax = new Vector2(-5, -5);
        
        GameObject nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(cell.transform, false);
        RectTransform nRect = nameObj.AddComponent<RectTransform>();
        nRect.anchorMin = new Vector2(0, 0);
        nRect.anchorMax = new Vector2(1, 0);
        nRect.pivot = new Vector2(0.5f, 0);
        nRect.anchoredPosition = new Vector2(0, 30);
        nRect.sizeDelta = new Vector2(-10, 30);
        Text nt = nameObj.AddComponent<Text>();
        nt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nt.fontSize = 18;
        nt.alignment = TextAnchor.MiddleCenter;
        nt.color = Color.white;
        
        GameObject qtyObj = new GameObject("QuantityText");
        qtyObj.transform.SetParent(cell.transform, false);
        RectTransform qRect = qtyObj.AddComponent<RectTransform>();
        qRect.anchorMin = new Vector2(0, 0);
        qRect.anchorMax = new Vector2(1, 0);
        qRect.pivot = new Vector2(0.5f, 0);
        qRect.anchoredPosition = new Vector2(0, 5);
        qRect.sizeDelta = new Vector2(-10, 20);
        Text qt = qtyObj.AddComponent<Text>();
        qt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        qt.fontSize = 16;
        qt.alignment = TextAnchor.MiddleCenter;
        qt.color = new Color(0.8f, 0.8f, 0.8f);
        
        ItemCell ic = cell.AddComponent<ItemCell>();
        ic.iconImage = icon.GetComponent<Image>();
        ic.backgroundImage = bg;
        ic.nameText = nt;
        ic.quantityText = qt;
        ic.rarityBorder = border.GetComponent<Image>();
        
        return cell;
    }
}
