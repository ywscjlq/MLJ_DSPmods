using BepInEx.Configuration;
using System.Collections.Generic;





using CommonAPI.Systems;





using FE.Logic.Economy;

using FE.Logic.DataCenter;





using FE.UI.Controls;





using FE.UI.Foundation.Window;





using FE.UI.MainPanel.Theme;





using UnityEngine;





using UnityEngine.UI;





using static FE.UI.Layout.GridDsl;





using static FE.Utils.Utils;





using static FE.Logic.DataCenter.PlayerInventoryAccess;





using static FE.UI.Foundation.RectTransformUtils;











namespace FE.UI.MainPanel.ResourceInteraction;











/// <summary>





/// 数据中心物品兑换与价格预览页面。





/// </summary>





public static class Exchange {





    private static RectTransform tab;





    private static PageLayout.HeaderRefs header;





    private static MyImageButton btnSelectedItem;





    private static Text txtPrice;





    private static Text txtInventory;





    private static Text txtStats;





    private static Text txtInfoTitle;





    private static Text txtActionTitle;





    private static Text txtMarketTitle;





    private static UIButton btnBuy1;





    private static UIButton btnBuy10;





    private static UIButton btnBuy100;





    private static UIButton btnSell1;





    private static UIButton btnSell10;





    private static UIButton btnSell100;
    private static UIButton btnBuyMax;
    private static UIButton btnSellMax;











    private static string lastStatus = "";
    private static float statusTimer;

    private static int selectedItemId = ExchangeManager.ListedItems.Count > 0 ? ExchangeManager.ListedItems[0] : 0;
    private static readonly HashSet<int> favoriteItems = new();











    public static void AddTranslations() {





        Register("交易所", "Exchange");





        Register("买1", "Buy 1");





        Register("买10", "Buy 10");





        Register("买100", "Buy 100");





        Register("卖1", "Sell 1");





        Register("卖10", "Sell 10");





        Register("卖100", "Sell 100");





        Register("当前价格", "Price");





        Register("库存", "Inventory");





        Register("当前标的", "Selected Listing", "当前标的");





        Register("快捷操作", "Quick Actions", "快捷操作");





        Register("市场概览", "Market Overview", "市场概览");





    }











    public static void LoadConfig(ConfigFile configFile) { }











    public static void CreateUI(MyWindow wnd, RectTransform trans) {





        tab = trans;





        BuildLayout(wnd, tab,





            Grid(





                rows: [Px(PageLayout.HeaderHeight), Px(190f), 1],





                rowGap: PageLayout.Gap,





                children: [





                    Header("交易所", objectName: "exchange-header", pos: (0, 0), onBuilt: refs => header = refs),





                    Grid(





                        pos: (1, 0),





                        cols: [2, 3],





                        rows: [1, Px(6f), 1],





                        columnGap: PageLayout.Gap,





                        children: [





                            ContentCard(





                                pos: (0, 0),





                                objectName: "exchange-info-card",





                                strong: true,





                                rows: [Px(24f), 1],





                                children: [





                                    CardTitleNode("当前标的", onBuilt: text => txtInfoTitle = text,





                                        pos: (0, 0), objectName: "exchange-info-title"),





                                    Grid(





                                        pos: (1, 0),





                                        rows: [1, 1, 1],





                                        cols: [Px(50f), 1],





                                        rowGap: PageLayout.InnerGap,





                                        columnGap: 10f,





                                        children: [





                                            ImageButtonNode(size: 40f,





                                                onBuilt: btn => btnSelectedItem = btn.WithClickEvent(





                                                    () => OpenItemPicker(46f), () => ToggleFavorite(selectedItemId)),





                                                pos: (0, 0), span: (2, 1), objectName: "exchange-selected-item"),





                                            TextNode("", 13, onBuilt: text => txtPrice = text,





                                                pos: (0, 1), objectName: "exchange-price"),





                                            TextNode("", 13, onBuilt: text => txtInventory = text,





                                                pos: (1, 1), objectName: "exchange-inventory"),





                                        ]),





                                ]),





                            ContentCard(





                                pos: (0, 1),





                                objectName: "exchange-action-card",





                                strong: true,





                                rows: [Px(24f), 1],





                                children: [





                                    CardTitleNode("快捷操作", onBuilt: text => txtActionTitle = text,





                                        pos: (0, 0), objectName: "exchange-action-title"),





                                    Grid(





                                        pos: (1, 0),





                                        rows: [1, 1, 1],





                                        cols: [1, 1, 1, 1],





                                        rowGap: PageLayout.InnerGap,





                                        columnGap: PageLayout.InnerGap,





                                        children: [





                                            ButtonNode("买1", onClick: () => Trade(true, 1),





                                                onBuilt: btn => btnBuy1 = btn,





                                                pos: (0, 0), objectName: "exchange-buy-1"),





                                            ButtonNode("买10", onClick: () => Trade(true, 10),





                                                onBuilt: btn => btnBuy10 = btn,





                                                pos: (0, 1), objectName: "exchange-buy-10"),





                                            ButtonNode("买100", onClick: () => Trade(true, 100),





                                                onBuilt: btn => btnBuy100 = btn,





                                                pos: (0, 2), objectName: "exchange-buy-100"),

                                            ButtonNode("买最大", onClick: () => Trade(true, Mathf.FloorToInt((float)GetItemTotalCount(IFE残片) / ExchangeManager.GetTicker(selectedItemId).LastPrice)),
                                                onBuilt: btn => btnBuyMax = btn,
                                                pos: (0, 3), objectName: "exchange-buy-max"),




                                            ButtonNode("卖1", onClick: () => Trade(false, 1),





                                                onBuilt: btn => btnSell1 = btn,





                                                pos: (1, 0), objectName: "exchange-sell-1"),





                                            ButtonNode("卖10", onClick: () => Trade(false, 10),





                                                onBuilt: btn => btnSell10 = btn,





                                                pos: (1, 1), objectName: "exchange-sell-10"),





                                            ButtonNode("卖100", onClick: () => Trade(false, 100),





                                                onBuilt: btn => btnSell100 = btn,





                                                pos: (1, 2), objectName: "exchange-sell-100"),

                                            ButtonNode("卖最大", onClick: () => Trade(false, (int)GetItemTotalCount(selectedItemId)),
                                                onBuilt: btn => btnSellMax = btn,
                                                pos: (1, 3), objectName: "exchange-sell-max"),




                                            ButtonNode("<< 加入补货清单", fontSize: 11,
                                                onClick: () => {
                                                    AutoReplenishManager.AddEntry(selectedItemId, 100, true);
                                                    lastStatus = "已加入补货清单"; statusTimer = 4f;
                                                },
                                                pos: (2, 0), span: (1, 4),
                                                objectName: "exchange-add-to-replenish"),
                                        ]),





                                ]),





                        ]),





                    ContentCard(





                        pos: (2, 0),





                        objectName: "exchange-market-card",





                        rows: [Px(24f), 1],





                        children: [





                            CardTitleNode("市场概览", onBuilt: text => txtMarketTitle = text,





                                pos: (0, 0), objectName: "exchange-market-title"),





                            TextNode("", 13, anchor: TextAnchor.UpperLeft, wrap: true,





                                onBuilt: text => txtStats = text,





                                pos: (1, 0), objectName: "exchange-market-stats"),





                        ]),





                ]));





    }











    private static float aiLastUpdateTime = -99f;
    private static string aiCachedText = "";

    public static void UpdateUI() {





        if (tab == null || !tab.gameObject.activeSelf) {





            return;





        }











        if (!ExchangeManager.IsListed(selectedItemId) && ExchangeManager.ListedItems.Count > 0) {





            selectedItemId = ExchangeManager.ListedItems[0];





        }





        ExchangeManager.ExchangeTicker ticker = ExchangeManager.GetTicker(selectedItemId);





        ItemProto item = LDB.items.Select(selectedItemId);





        header.Title.text = "交易所".Translate().WithColor(Orange);





        header.Summary.text = item == null ? string.Empty
                : $"{(favoriteItems.Contains(selectedItemId) ? "★ " : "")}当前标的：{item.name}".WithColor(White);





        txtInfoTitle.text = "当前标的".Translate().WithColor(Orange);





        txtActionTitle.text = "快捷操作".Translate().WithColor(Orange);





        txtMarketTitle.text = "市场概览".Translate().WithColor(Orange);





        btnSelectedItem.Proto = item;





        btnSelectedItem.SetCount(GetItemTotalCount(selectedItemId));











        if (ticker == null || item == null) {





            txtPrice.text = "";





            txtInventory.text = "";





            txtStats.text = "";





            return;





        }











        txtPrice.text =





            $"{"当前价格".Translate()}：{ticker.LastPrice:F1}\n买入 {ticker.AskPrice:F1}    卖出 {ticker.BidPrice:F1}";





        txtInventory.text =





            $"{"库存".Translate()}：残片 {GetItemTotalCount(IFE残片)}";










        string arrowMark = ticker.LastPrice > ticker.DayOpenPrice
            ? "\u2191".WithColor(Green)  // ↑
            : ticker.LastPrice < ticker.DayOpenPrice
                ? "\u2193".WithColor(new Color(1f, 0.2f, 0.2f))  // ↓
                : "\u2192".WithColor(Gray);  // →

        txtStats.text =
            $"日内开盘 {ticker.DayOpenPrice:F1}\n最新价格 {ticker.LastPrice:F1} {arrowMark}\n日高 / 日低 {ticker.DayHighPrice:F1} / {ticker.DayLowPrice:F1}\n净成交量 {ticker.NetPlayerVolume:F1}";





        // 额外物品显示（显示最近记录，区分来源）

        var bonuses = DataCenterInventory.RecentBonuses;

        if (bonuses.Count > 0)

        {

            var sb = new System.Text.StringBuilder();

            int shownExchange = 0, shownRandom = 0;

            for (int idx = bonuses.Count - 1; idx >= 0; idx--)

            {

                var b = bonuses[idx];

                if (b.Source == "exchange" && shownExchange < 5)

                {

                    var proto = LDB.items.Select(b.ItemId);

                    string name = proto?.Name ?? $"#{b.ItemId}";

                    if (shownExchange == 0) sb.AppendLine();

                    sb.Append("  <color=#E0B000>交易所");

                    sb.Append(name);

                    sb.Append(" x");

                    sb.Append(b.BonusCount);
                    sb.Append("</color>");

                    shownExchange++;

                }

                else if (b.Source == "random" && shownRandom < 3)

                {

                    var proto = LDB.items.Select(b.ItemId);

                    string name = proto?.Name ?? $"#{b.ItemId}";

                    if (shownRandom == 0) { sb.AppendLine(); sb.Append("<color=#B060C0>  【分流】</color>"); }

                    sb.Append(name);

                    sb.Append(" x");

                    sb.Append(b.BonusCount);

                    shownRandom++;

                }

            }

            if (sb.Length > 0)

                txtStats.text += sb.ToString();


        // 自选物品价格
        if (favoriteItems.Count > 0)
        {
            txtStats.text += "\n\n--- 自选 ---".WithColor(Orange);
            foreach (int favId in favoriteItems)
            {
                var favItem = LDB.items.Select(favId);
                if (favItem == null) continue;
                var favTicker = ExchangeManager.GetTicker(favId);
                if (favTicker == null) continue;
                txtStats.text += $"\n{favItem.Name}  {favTicker.LastPrice:F1}  (B{favTicker.BidPrice:F1} / A{favTicker.AskPrice:F1})";
            }
        }

        // v2.5: 走势图（当前选中物品）
        string spark = MarketAIAnalyzer.GetSparkline(selectedItemId, 28);
        if (spark.Length > 0)
            txtStats.text += $"\n\n走势 {spark}";

        // v2.5: AI 智能交易建议（2.5秒重算 + 始终显示）
        if (MarketAIAnalyzer.Enabled)
        {
            if (Time.time - aiLastUpdateTime > 2.5f)
            {
                aiLastUpdateTime = Time.time;
                var aiSb = new System.Text.StringBuilder();
                aiSb.Append("\n\n--- AI 建议 ---".WithColor(new Color(0.4f, 0.9f, 0.4f)));
                var buySignals = MarketAIAnalyzer.GetTopBuySignals(3);
                if (buySignals.Count > 0)
                {
                    foreach (var sig in buySignals)
                    {
                        var sigItem = LDB.items.Select(sig.ItemId);
                        string sigName = sigItem?.Name ?? $"#{sig.ItemId}";
                        aiSb.Append($"\n  BUY  {sigName} ({sig.Confidence:P0}) {sig.Reason}");
                    }
                    var sellSignals = MarketAIAnalyzer.GetTopSellSignals(2);
                    foreach (var sig in sellSignals)
                    {
                        var sigItem = LDB.items.Select(sig.ItemId);
                        string sigName = sigItem?.Name ?? $"#{sig.ItemId}";
                        aiSb.Append($"\n  SELL {sigName} ({sig.Confidence:P0}) {sig.Reason}");
                    }
                }
                else
                {
                    int points = MarketAIAnalyzer.PriceHistory?.Count ?? 0;
                    aiSb.Append(points == 0
                        ? "\n  等待首次市场刷新..."
                        : $"\n  数据收集中... ({points} 物品已追踪, 需≥4个数据点/物品)");
                }
                aiCachedText = aiSb.ToString();
            }
            if (!string.IsNullOrEmpty(aiCachedText))
                txtStats.text += aiCachedText;
        }
        }



        btnBuy1.SetText($"{"买1".Translate()} ({Mathf.CeilToInt(ticker.AskPrice)})");





        btnBuy10.SetText($"{"买10".Translate()} ({Mathf.CeilToInt(ticker.AskPrice * 10f)})");





        btnBuy100.SetText($"{"买100".Translate()} ({Mathf.CeilToInt(ticker.AskPrice * 100f)})");





        btnSell1.SetText($"{"卖1".Translate()} ({Mathf.FloorToInt(ticker.BidPrice)})");





        btnSell10.SetText($"{"卖10".Translate()} ({Mathf.FloorToInt(ticker.BidPrice * 10f)})");





        btnSell100.SetText($"{"卖100".Translate()} ({Mathf.FloorToInt(ticker.BidPrice * 100f)})");





        btnSell1.button.interactable = GetItemTotalCount(selectedItemId) >= 1;





        btnSell10.button.interactable = GetItemTotalCount(selectedItemId) >= 10;





        btnSell100.button.interactable = GetItemTotalCount(selectedItemId) >= 100;



        if (btnBuyMax != null) btnBuyMax.button.interactable = ticker.AskPrice > 0f && GetItemTotalCount(IFE残片) >= ticker.AskPrice;
        if (btnSellMax != null) btnSellMax.button.interactable = GetItemTotalCount(selectedItemId) >= 1;

        if (statusTimer > 0) statusTimer -= Time.deltaTime;
        if (statusTimer > 0 && header.Summary != null)
            header.Summary.text = lastStatus.WithColor(Orange);



    }











    private static void ToggleFavorite(int itemId)
    {
        if (itemId <= 0) return;
        if (favoriteItems.Contains(itemId))
            favoriteItems.Remove(itemId);
        else
            favoriteItems.Add(itemId);
    }

    private static void OpenItemPicker(float y) {





        float popupX = tab.anchoredPosition.x - tab.rect.width / 2;





        float popupY = tab.anchoredPosition.y + tab.rect.height / 2 - y;





        UIItemPickerExtension.Popup(new(popupX, popupY), item => {





            if (item != null && ExchangeManager.IsListed(item.ID)) {





                selectedItemId = item.ID;





            }





        }, true, item => item != null && ExchangeManager.IsListed(item.ID));





    }











    private static void Trade(bool isBuy, int count) {





        bool changed = isBuy





            ? ExchangeManager.TryBuy(selectedItemId, count)





            : ExchangeManager.TrySell(selectedItemId, count);





        if (changed) {





            UpdateUI();





        }





    }







    /// <summary>

    /// 快捷购买/卖出（F1-F6 快捷键）

    /// </summary>

    public static void QuickBuy(int slot)

    {

        switch (slot)

        {

            case 0: if (btnBuy1 != null && btnBuy1.button.interactable) btnBuy1.button.onClick.Invoke(); break;

            case 1: if (btnSell1 != null && btnSell1.button.interactable) btnSell1.button.onClick.Invoke(); break;

            case 2: if (btnBuy10 != null && btnBuy10.button.interactable) btnBuy10.button.onClick.Invoke(); break;

            case 3: if (btnSell10 != null && btnSell10.button.interactable) btnSell10.button.onClick.Invoke(); break;

            case 4: if (btnBuy100 != null && btnBuy100.button.interactable) btnBuy100.button.onClick.Invoke(); break;

            case 5: if (btnSell100 != null && btnSell100.button.interactable) btnSell100.button.onClick.Invoke(); break;

        }

    }

}