using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using CommonAPI.Systems;
using FE.Logic.Economy;
using FE.UI.Controls;
using FE.UI.Foundation.Window;
using FE.UI.MainPanel.Theme;
using UnityEngine;
using UnityEngine.UI;
using static FE.UI.Layout.GridDsl;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Logic.DataCenter.PlayerInventoryAccess;
using static FE.UI.Foundation.RectTransformUtils;

public static class DashboardPage {
    private static RectTransform tab;
    private static FE.UI.MainPanel.Theme.PageLayout.HeaderRefs header;

    // Card 1: 残片余额
    private static Text txtBalanceTitle;
    private static Text txtFragmentBalance;

    // Card 2: 自选价格
    private static Text txtWatchlistTitle;
    private static Text txtWatchlistContent;

    // Card 3: 短缺物品
    private static Text txtShortageTitle;
    private static Text txtShortageContent;
    private static UIButton btnRefreshShortage;

    // Card 4: 最近补货
    private static Text txtReplenishTitle;
    private static Text txtReplenishContent;

    private static readonly List<int> watchlist = new(ExchangeManager.ListedItems);
    private const int MaxWatchlistDisplay = 30;

    public static void AddTranslations() {
        Register("系统仪表盘", "Dashboard");
        Register("残片余额", "Fragment Balance");
        Register("数据中心残片库存", "Data Center Fragment Stock");
        Register("交易所总交易", "Total Exchange Trades");
        Register("交易所品种数", "Listed Items");
        Register("自选价格", "Watchlist");
        Register("短缺物品", "Shortage Items");
        Register("最近补货", "Recent Replenish");
        Register("刷新", "Refresh");
        Register("暂无数据", "No data");
        Register("暂无短缺物品", "No shortage items");
        Register("暂无补货记录", "No replenish records");
        Register("价格", "Price");
        Register("开盘", "Open");
        Register("涨跌", "Chg");
    }

    public static void LoadConfig(ConfigFile configFile) { }

    public static void CreateUI(MyWindow wnd, RectTransform trans) {
        tab = trans;
        BuildLayout(wnd, tab,
            Grid(
                rows: [Px(PageLayout.HeaderHeight), Px(200f), 1],
                rowGap: PageLayout.Gap,
                children: [
                    Header("系统仪表盘",
                        summary: "残片余额 · 自选价格 · 短缺监控 · 最近补货",
                        objectName: "dashboard-header",
                        pos: (0, 0),
                        onBuilt: refs => header = refs),
                    Grid(
                        pos: (1, 0),
                        cols: [1, 1],
                        columnGap: PageLayout.Gap,
                        children: [
                            ContentCard(
                                pos: (0, 0), objectName: "dashboard-balance-card", strong: true,
                                rows: [Px(24f), 1],
                                children: [
                                    CardTitleNode("残片余额", onBuilt: text => txtBalanceTitle = text,
                                        pos: (0, 0), objectName: "dashboard-balance-title"),
                                    TextNode("", PageLayout.BodyFontSize,
                                        anchor: TextAnchor.UpperLeft, wrap: true,
                                        onBuilt: text => txtFragmentBalance = text,
                                        pos: (1, 0), objectName: "dashboard-balance-content"),
                                ]),
                            ContentCard(
                                pos: (0, 1), objectName: "dashboard-watchlist-card", strong: true,
                                rows: [Px(24f), 1],
                                children: [
                                    CardTitleNode("自选价格", onBuilt: text => txtWatchlistTitle = text,
                                        pos: (0, 0), objectName: "dashboard-watchlist-title"),
                                    TextNode("", PageLayout.BodyFontSize,
                                        anchor: TextAnchor.UpperLeft, wrap: true,
                                        onBuilt: text => txtWatchlistContent = text,
                                        pos: (1, 0), objectName: "dashboard-watchlist-content"),
                                ]),
                        ]),
                    Grid(
                        pos: (2, 0),
                        cols: [1, 1],
                        columnGap: PageLayout.Gap,
                        children: [
                            ContentCard(
                                pos: (0, 0), objectName: "dashboard-shortage-card",
                                rows: [Px(24f), Px(32f), 1],
                                children: [
                                    CardTitleNode("短缺物品", onBuilt: text => txtShortageTitle = text,
                                        pos: (0, 0), objectName: "dashboard-shortage-title"),
                                    ButtonNode("刷新", onClick: RefreshShortage,
                                        onBuilt: btn => btnRefreshShortage = btn,
                                        pos: (1, 0), objectName: "dashboard-shortage-refresh"),
                                    TextNode("", PageLayout.BodyFontSize,
                                        anchor: TextAnchor.UpperLeft, wrap: true,
                                        onBuilt: text => txtShortageContent = text,
                                        pos: (2, 0), objectName: "dashboard-shortage-content"),
                                ]),
                            ContentCard(
                                pos: (0, 1), objectName: "dashboard-replenish-card",
                                rows: [Px(24f), 1],
                                children: [
                                    CardTitleNode("最近补货", onBuilt: text => txtReplenishTitle = text,
                                        pos: (0, 0), objectName: "dashboard-replenish-title"),
                                    TextNode("", PageLayout.BodyFontSize,
                                        anchor: TextAnchor.UpperLeft, wrap: true,
                                        onBuilt: text => txtReplenishContent = text,
                                        pos: (1, 0), objectName: "dashboard-replenish-content"),
                                ]),
                        ]),
                ]));
    }

    public static void UpdateUI() {
        if (tab == null || !tab.gameObject.activeSelf) return;
        UpdateHeader();
        UpdateBalanceCard();
        UpdateWatchlistCard();
        UpdateShortageCard();
        UpdateReplenishCard();
    }

    private static void UpdateHeader() {
        if (header.Root == null) return;
        header.Title.text = "系统仪表盘".Translate().WithColor(Orange);
        header.Summary.text = "残片余额 · 自选价格 · 短缺监控 · 最近补货".WithColor(White);
    }

    private static void UpdateBalanceCard() {
        txtBalanceTitle.text = "残片余额".Translate().WithColor(Orange);
        long fragmentCount = centerItemCount[IFE残片];
        int listedCount = ExchangeManager.ListedItems.Count;
        long tradeCount = ExchangeManager.TotalTradeCount;
        var sb = new StringBuilder();
        sb.AppendLine($"{"数据中心残片库存".Translate()}：{"残片".Translate()} {fragmentCount:N0}".WithColor(White));
        sb.AppendLine($"{"交易所品种数".Translate()}：{listedCount}".WithColor(White));
        sb.AppendLine($"{"交易所总交易".Translate()}：{tradeCount:N0} 笔".WithColor(White));
        txtFragmentBalance.text = sb.ToString();
    }

    private static void UpdateWatchlistCard() {
        txtWatchlistTitle.text = "自选价格".Translate().WithColor(Orange);
        if (watchlist.Count == 0) {
            txtWatchlistContent.text = "暂无数据".Translate().WithColor(PageLayout.EmptyStateTextColor);
            return;
        }
        var sb = new StringBuilder();
        int count = 0;
        foreach (int itemId in watchlist) {
            if (count >= MaxWatchlistDisplay) {
                sb.AppendLine($"... 及 {watchlist.Count - count} 项".WithColor(PageLayout.EmptyStateTextColor));
                break;
            }
            ExchangeManager.ExchangeTicker ticker = ExchangeManager.GetTicker(itemId);
            ItemProto proto = LDB.items.Select(itemId);
            if (ticker == null || proto == null) continue;
            string name = proto.name;
            float change = ticker.DayOpenPrice > 0f
                ? (ticker.LastPrice - ticker.DayOpenPrice) / ticker.DayOpenPrice * 100f : 0f;
            string changeStr = change >= 0f
                ? $"+{change:F1}%".WithColor(Green) : $"{change:F1}%".WithColor(Red);
            sb.AppendLine($"{name}  {ticker.LastPrice:F1}  {changeStr}  (H:{ticker.DayHighPrice:F1} L:{ticker.DayLowPrice:F1})".WithColor(White));
            count++;
        }
        txtWatchlistContent.text = sb.ToString();
    }

    private static void UpdateShortageCard() {
        txtShortageTitle.text = "短缺物品".Translate().WithColor(Orange);
        txtShortageContent.text = "暂无短缺物品".Translate().WithColor(PageLayout.EmptyStateTextColor);
    }

    private static void UpdateReplenishCard() {
        txtReplenishTitle.text = "最近补货".Translate().WithColor(Orange);
        txtReplenishContent.text = "暂无补货记录".Translate().WithColor(PageLayout.EmptyStateTextColor);
    }

    private static void RefreshShortage() { UpdateUI(); }

    public static void Import(BinaryReader r) { r.ReadBlocks(); }
    public static void Export(BinaryWriter w) { w.WriteBlocks(); }
    public static void IntoOtherSave() { }
}