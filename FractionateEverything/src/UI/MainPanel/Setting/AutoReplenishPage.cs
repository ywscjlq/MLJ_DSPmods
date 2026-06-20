﻿using System;

using System.Linq;

using FE.Logic.Economy;

using FE.UI.Controls;

using FE.UI.Foundation.Window;

using FE.UI.Layout;

using FE.UI.MainPanel.Theme;

using UnityEngine;

using UnityEngine.UI;

using static FE.UI.Layout.GridDsl;

using static FE.Utils.Utils;

using CommonAPI.Systems;
using static FE.Logic.Items.ItemManager;



namespace FE.UI.MainPanel.Setting;



/// <summary>

/// 自动补货配置页面。

/// </summary>

public static class AutoReplenishPage

{

    private static RectTransform tab;

    private static PageLayout.HeaderRefs header;

    private static Text txtSummary;



    private static Vector2 scrollPos;

    private static int editItemId;

    private static string editTargetText = "100";

    private static string addItemIdText = "";

    private static string addTargetText = "100";

    private static string statusText = "";

    private static float statusTimer;



    // 弹出选物品

    private static bool pickingItem;

    private static int pickTarget;

    private static bool popupOpened;

    private static bool popupPending;

    private static float popupPendingX;

    private static float popupPendingY;

    private static bool _isInOnGUI;



    public static void AddTranslations()

    {

        Register("自动补货", "Auto Replenish");

        Register("补货清单", "Replenish List");

    }



    public static void CreateUI(MyWindow wnd, RectTransform trans)

    {

        tab = trans;

        AutoReplenishGUIHook.Ensure(wnd.gameObject);

        BuildLayout(wnd, tab,

            Grid(

                rows: [Px(PageLayout.HeaderHeight), 1],

                rowGap: PageLayout.Gap,

                children: [

                    Header("自动补货", objectName: "auto-replenish-header", pos: (0, 0),

                        onBuilt: refs =>

                        {

                            header = refs;

                            txtSummary = refs.Summary;

                        }),

                    ContentCard(

                        pos: (1, 0),

                        objectName: "auto-replenish-card",

                        strong: true,

                        children: [

                            TextNode("配置页面使用右上角按钮", 13,

                                pos: (0, 0), objectName: "auto-replenish-hint"),

                        ]),

                ]));

    }



    public static void UpdateUI()

    {

        if (tab == null || !tab.gameObject.activeInHierarchy) return;



        header.Title.text = "自动补货".Translate().WithColor(Orange);

        header.Summary.text = $"已设置 {AutoReplenishManager.Entries.Count} 项";



        // 右侧弹出物品选择器

        if (pickingItem && !popupOpened)

        {

            popupOpened = true;

            // 弹出框放在tab左侧，避免跑出屏幕
            float px = tab.anchoredPosition.x - tab.rect.width * 0.5f;

            float py = tab.anchoredPosition.y + tab.rect.height * 0.5f;

            // 限制不超出屏幕左边界
            float popupWidth = 420f;
            px = Mathf.Max(px, 0f);
            px = Mathf.Min(px, Screen.width - popupWidth);

            popupPendingX = px;

            popupPendingY = py;

            popupPending = true;

        }

        if (pickingItem) return;



        // 全屏 IMGUI 覆盖绘制

        if (!_isInOnGUI) return;
        // 获取 tab 屏幕区域，用 BeginGroup 限制 IMGUI 绘制范围
        Vector3[] corners = new Vector3[4];
        tab.GetWorldCorners(corners);
        Canvas canvas = tab.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        float guiLeft = bl.x;
        float guiTop = Screen.height - tr.y;
        float guiSizeW = tr.x - bl.x;
        float guiSizeH = tr.y - bl.y;
        // 直接使用绝对屏幕坐标

        float y = guiTop + 10;
        float x = guiLeft + 10;
        float w = guiSizeW - 20;
        float rowH = 26;

        GUI.Box(new Rect(x, y, w, guiSizeH - 60), "");



        // 添加新物品行

        y += 8;

        GUI.Label(new Rect(x + 8, y, 60, rowH), "添加:");

        addItemIdText = GUI.TextField(new Rect(x + 68, y, 140, rowH), addItemIdText, 20);

        if (GUI.Button(new Rect(x + 212, y, 80, rowH), "选物品"))

        {

            int.TryParse(addItemIdText, out int id);

            pickTarget = int.TryParse(addTargetText, out int t) ? t : 100;

            pickingItem = true;

        }

        GUI.Label(new Rect(x + 296, y, 40, rowH), "目标:");

        addTargetText = GUI.TextField(new Rect(x + 336, y, 80, rowH), addTargetText, 10);

        if (GUI.Button(new Rect(x + 420, y, 60, rowH), "添加"))

        {

            if (int.TryParse(addItemIdText, out int itemId) && itemId > 0

                && int.TryParse(addTargetText, out int target) && target > 0)

            {

                AutoReplenishManager.AddEntry(itemId, target, true);

                addItemIdText = "";

                statusText = $"已添加 #{itemId} x{target}";

                statusTimer = 3f;

            }

        }

        // 操作栏
        y += rowH + 8;

        if (GUI.Button(new Rect(x + 8, y, 110, rowH), "扫描常用物品"))
        {
            int added = AutoReplenishManager.ScanAndAddFrequentItems();
            statusText = string.Format("扫描完成: 新增 {0} 项", added);
            statusTimer = 3f;
        }

        if (GUI.Button(new Rect(x + 126, y, 80, rowH), "强制补货"))
        {
            int count = AutoReplenishManager.ForceReplenishAll();
            statusText = string.Format("补货完成: {0} 项", count);
            statusTimer = 3f;
        }
        GUI.Label(new Rect(x + 214, y, 70, rowH), "清理重复:");
        if (GUI.Button(new Rect(x + 284, y, 80, rowH), "清理重复"))
        {
            int removed = AutoReplenishManager.RemoveDuplicates();
            statusText = string.Format("删除了 {0} 条重复项", removed);
            statusTimer = 3f;
        }

        GUI.Label(new Rect(x + 8, y, 70, rowH), "超出抛售:");
        string selTxt = GUI.TextField(new Rect(x + 448, y, 60, rowH), AutoReplenishManager.MaxStockSellThreshold.ToString(), 8);
        if (int.TryParse(selTxt, out int sel))
            AutoReplenishManager.MaxStockSellThreshold = sel;

        // 第三行：模式 + 运行统计
        y += rowH + 4;
        GUI.Label(new Rect(x + 8, y, 30, rowH), "搜索:");
        string sf = GUI.TextField(new Rect(x + 230, y, w - 248, rowH), AutoReplenishManager.SearchFilter, 30);
        if (sf != AutoReplenishManager.SearchFilter)
            AutoReplenishManager.SearchFilter = sf;

        GUI.Label(new Rect(x + 116, y, w - 130, rowH), 
            string.Format("本周期: 已补 {0}  跳过 {1} | {2}",
                AutoReplenishManager.CycleBoughtCount, AutoReplenishManager.CycleSkipCount,
                AutoReplenishManager.DiagInfo));

        y += rowH + 4;

        // 表头        // 表头

        GUI.Label(new Rect(x + 8, y, 50, rowH), "启用");

        GUI.Label(new Rect(x + 62, y, 120, rowH), "物品");

        GUI.Label(new Rect(x + 186, y, 80, rowH), "当前库存");

        GUI.Label(new Rect(x + 270, y, 80, rowH), "目标数量");

        GUI.Label(new Rect(x + 354, y, 60, rowH), "操作");



        y += rowH;



        // 列表

        // 搜索过滤
        var allEntries = AutoReplenishManager.Entries;
        bool hasFilter = !string.IsNullOrEmpty(AutoReplenishManager.SearchFilter);
        var entries = hasFilter
            ? allEntries.Where(e =>
              {
                  var p = LDB.items.Select(e.ItemId);
                  return p != null && p.Name.IndexOf(AutoReplenishManager.SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
              }).ToList()
            : allEntries.ToList();

        float listH = guiTop + guiSizeH - y - 50 - rowH * 14 - 30 - rowH - rowH;

        scrollPos = GUI.BeginScrollView(new Rect(x, y, w, listH), scrollPos,

            new Rect(0, 0, w - 20, entries.Count * rowH + 10));



        for (int i = 0; i < entries.Count; i++)

        {

            var e = entries[i];

            float rowY = i * rowH;



            bool en = GUI.Toggle(new Rect(8, rowY + 2, 40, rowH - 4), e.Enabled, "");

            if (en != e.Enabled) e.Enabled = en;



            var proto = LDB.items.Select(e.ItemId);

            string name = proto?.Name ?? $"#{e.ItemId}";

            GUI.Label(new Rect(62, rowY, 90, rowH), name);



            long stock = FE.Logic.DataCenter.PlayerInventoryAccess.GetItemTotalCount(e.ItemId);

            GUI.Label(new Rect(210, rowY, 60, rowH), stock.ToString());



            string tgt = GUI.TextField(new Rect(274, rowY, 70, rowH), e.TargetCount.ToString(), 10);

            if (int.TryParse(tgt, out int nt) && nt != e.TargetCount && nt > 0)

                e.TargetCount = nt;



            if (GUI.Button(new Rect(348, rowY, 50, rowH), "删除"))

                AutoReplenishManager.RemoveEntry(e.ItemId);

        }



        GUI.EndScrollView();


        // 状态提示

        if (statusTimer > 0)
        {
            GUI.Label(new Rect(x + 8, guiTop + guiSizeH - 30, w - 16, 24), statusText);
            statusTimer -= Time.deltaTime;
        }

                float logY = guiTop + guiSizeH - 30 - rowH * 14;
        long cumSell = AutoReplenishManager.TotalSellFragments;
        long cumBuy = AutoReplenishManager.TotalBuyFragments;
        long curFrags = FE.Logic.DataCenter.PlayerInventoryAccess.GetItemTotalCount(8060);
        long unaccounted = (cumSell - cumBuy) - curFrags;
        string cumLine = string.Format("累计: +{0} / -{1} = {2:+0;-#}f  |  当前残片: {3}  |  差额: {4:+0;-#}f", cumSell, cumBuy, cumSell - cumBuy, curFrags, unaccounted);
        GUI.Label(new Rect(x + 8, logY - rowH, w - 16, rowH), cumLine);
        string diag = AutoReplenishManager.DiagInfo;
        if (!string.IsNullOrEmpty(diag))
            GUI.Label(new Rect(x + 8, logY - rowH * 2, w - 16, rowH), "!" + diag);
        var evts = AutoReplenishManager.GetRecentEvents();
        var sellList = new System.Collections.Generic.List<FE.Logic.Economy.AutoReplenishManager.ReplenishEvent>();
        var buyList = new System.Collections.Generic.List<FE.Logic.Economy.AutoReplenishManager.ReplenishEvent>();
        for (int ei = 0; ei < evts.Length; ei++)
        {
            if (evts[ei].IsSell) sellList.Add(evts[ei]);
            else buyList.Add(evts[ei]);
        }
        int sellStart = Math.Max(0, sellList.Count - 5);
        int buyStart = Math.Max(0, buyList.Count - 5);
        GUI.Label(new Rect(x + 8, logY, w - 16, rowH), "--- 卖出 ---");
        logY += rowH;
        for (int si = sellStart; si < sellList.Count; si++)
        {
            var ev = sellList[si];
            var proto = LDB.items.Select(ev.ItemId);
            string ename = proto?.Name ?? "#" + ev.ItemId;
            long gs = ev.Tick / 60L; long h = gs / 3600L; long m = (gs % 3600L) / 60L; long s = gs % 60L;
            GUI.Label(new Rect(x + 8, logY, w - 16, rowH), string.Format("  [{0:D2}:{1:D2}:{2:D2}] {3} x{4} +{5}f", h, m, s, ename, ev.SoldCount, ev.FragCost));
            logY += rowH;
        }
        GUI.Label(new Rect(x + 8, logY, w - 16, rowH), "--- 买入 ---");
        logY += rowH;
        for (int bi = buyStart; bi < buyList.Count; bi++)
        {
            var ev = buyList[bi];
            var proto = LDB.items.Select(ev.ItemId);
            string ename = proto?.Name ?? "#" + ev.ItemId;
            long gs = ev.Tick / 60L; long h = gs / 3600L; long m = (gs % 3600L) / 60L; long s = gs % 60L;
            GUI.Label(new Rect(x + 8, logY, w - 16, rowH), string.Format("  [{0:D2}:{1:D2}:{2:D2}] {3} x{4} -{5}f", h, m, s, ename, ev.BoughtCount, ev.FragCost));
            logY += rowH;
        }

    }


internal sealed class AutoReplenishGUIHook : MonoBehaviour
{
    internal static void Ensure(GameObject go)
    {
        if (go.GetComponent<AutoReplenishGUIHook>() == null)
            go.AddComponent<AutoReplenishGUIHook>();
    }

    private void OnGUI()
    {
        AutoReplenishPage._isInOnGUI = true;
        AutoReplenishPage.UpdateUI();
        AutoReplenishPage._isInOnGUI = false;
    }

    private void LateUpdate()
    {
        if (!AutoReplenishPage.popupPending) return;
        AutoReplenishPage.popupPending = false;
        Canvas.ForceUpdateCanvases();
        float px = AutoReplenishPage.popupPendingX;
        float py = AutoReplenishPage.popupPendingY;
        UIItemPickerExtension.Popup(new(px, py), item =>
        {
            AutoReplenishPage.pickingItem = false;
            AutoReplenishPage.popupOpened = false;
            if (item != null)
            {
                AutoReplenishManager.AddEntry(item.ID, AutoReplenishPage.pickTarget, true);
                AutoReplenishPage.statusText = $"已添加: {item.Name} x{AutoReplenishPage.pickTarget}";
                AutoReplenishPage.statusTimer = 5f;
            }
        }, true, _ => true);
    }
}
}

