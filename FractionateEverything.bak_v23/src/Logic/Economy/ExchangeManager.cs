using System;


using System.Collections.Generic;


using System.IO;


using System.Linq;


using UnityEngine;


using FE.Logic.DataCenter;


using static FE.Logic.DataCenter.DataCenterInventory;


using static FE.Utils.Utils;


using static FE.Logic.DataCenter.PlayerInventoryAccess;


using Random = System.Random;





namespace FE.Logic.Economy;





/// <summary>


/// 股票式交易所。


/// 使用 MarketValue 作为锚点，但价格存在独立波动与玩家交易冲击。


/// </summary>


public static class ExchangeManager {


    /// <summary>


    /// 交易所行情状态。


    /// </summary>


    public sealed class ExchangeTicker {


        public int ItemId;


        public float LastPrice;


        public float BidPrice;


        public float AskPrice;


        public float DayOpenPrice;


        public float DayHighPrice;


        public float DayLowPrice;


        public long LastTradeTick;


        public int NetPlayerVolume;


        public int RecentPlayerBuyVolume;


        public int RecentPlayerSellVolume;


    }





    private static readonly Random rng = new(20260403);





    private static readonly Dictionary<int, ExchangeTicker> tickers = [];


    private static long lastRefreshTick;


    private static int lastRefreshVersion = -1;


    public static long TotalTradeCount;

    public static IReadOnlyDictionary<int, ExchangeTicker> AllTickers => tickers;




    private static List<int> _cachedItems;


    /// <summary>


    /// 所有可参与经济的物品（缓存，不限33种）


    /// </summary>


    public static IReadOnlyList<int> ListedItems {


        get {


            if (_cachedItems == null) {


                RebuildListedItemsCache();


            }


            return _cachedItems;


        }


    }




    /// <summary>重建 ListedItems 缓存，仅包含可参与经济的有效物品</summary>


    private static void RebuildListedItemsCache() {


        _cachedItems = new List<int>();


        for (int i = 1; i < 12000; i++) {


            if (LDB.items == null || !LDB.items.Exist(i)) continue;


            if (!MarketValueManager.CanParticipateInEconomy(i)) continue;


            _cachedItems.Add(i);


        }


    }





    public static void Init() {


        _cachedItems = null;  // 重置缓存，以获取最新注册的物品


        tickers.Clear();


        foreach (int itemId in ListedItems) {


            if (LDB.items == null || !LDB.items.Exist(itemId)) {


                continue;


            }


            float baseVal = MarketValueManager.GetValue(itemId);


            float mid = Math.Max(1f, baseVal > 0f ? baseVal : 0.5f);


            tickers[itemId] = new ExchangeTicker {


                ItemId = itemId,


                LastPrice = mid,


                BidPrice = mid * (1f - MarketLevelManager.GetSpread() / 2f),


                AskPrice = mid * (1f + MarketLevelManager.GetSpread() / 2f),


                DayOpenPrice = mid,


                DayHighPrice = mid,


                DayLowPrice = mid,


                LastTradeTick = 0L,


            };


        }


        lastRefreshTick = 0L;


        lastRefreshVersion = MarketValueManager.RefreshVersion;


    }





    public static void Tick() {


        if (tickers.Count == 0) {


            Init();


            return;


        }





        bool shouldRefresh = MarketValueManager.RefreshVersion != lastRefreshVersion


                             || GameMain.gameTick - lastRefreshTick >= 3600L;


        if (!shouldRefresh) {


            return;


        }


        RefreshTickers();


    }





    public static void HandleMarketValueRefreshed() {


        RefreshTickers();


    }





    public static ExchangeTicker GetTicker(int itemId) {


        if (tickers.TryGetValue(itemId, out ExchangeTicker ticker)) {


            return ticker;


        }


        // 未注册的物品：动态创建默认行情（仅内存，不持久化）


        if (LDB.items != null && LDB.items.Exist(itemId)) {


            float baseValue = MarketValueManager.GetValue(itemId);


                        // R6: 初始价格加 ±30% 随机偏移，模拟市场发现阶段
            float priceBase = Math.Max(1f, baseValue > 0f ? baseValue : 0.5f);
            float discoverOffset = 0.7f + (float)rng.NextDouble() * 0.6f; // 0.7~1.3
            float defaultPrice = priceBase * discoverOffset;


            ticker = new ExchangeTicker {


                ItemId = itemId,


                LastPrice = defaultPrice,


                BidPrice = defaultPrice * (1f - MarketLevelManager.GetSpread() / 2f),


                AskPrice = defaultPrice * (1f + MarketLevelManager.GetSpread() / 2f),


            };
            MarketLevelManager.RegisterItemListed(itemId);


            tickers[itemId] = ticker;


        }


        return ticker;


    }





    public static bool IsListed(int itemId) {


        return LDB.items != null && LDB.items.Exist(itemId);


    }





    /// <summary>

    /// ⼿静默购买（不弹提示框），用于自动补货。

    /// </summary>

    public static bool TryBuySilent(int itemId, int count, int reserveFragments, out int bought)

    {

        bought = 0;

        if (count <= 0 || !tickers.TryGetValue(itemId, out ExchangeTicker ticker))

            return false;



        long price = (long)Math.Ceiling(ticker.LastPrice * count);

        if (price <= 0L || price > int.MaxValue)

            return false;



        // 检查保留残片

        long fragments = FE.Logic.DataCenter.DataCenterInventory.centerItemCount[IFE残片];

        if (fragments < price + reserveFragments)

            return false;



        // 静默扣除残片

        if (TakeItemFromModData(IFE残片, (int)price, out _) == 0)

            return false;



        AddItemToModData(itemId, count, 0, true, skipMultiplier: true);

        ApplyTradeImpact(ticker, count, isBuy: true);

        // 自动补货交易意外收获：15% 概率获得随机奖励（排除本次交易物品）
        if (GetRandInt(1, 100) <= 15 && tickers.Count > 1) {
            int bonusIdx = GetRandInt(0, tickers.Count - 1);
            int bonusId = tickers.Keys.ElementAt(bonusIdx);
            if (bonusId == itemId) {
                bonusIdx = (bonusIdx + 1) % tickers.Count;
                bonusId = tickers.Keys.ElementAt(bonusIdx);
            }
            if (bonusId != itemId) {
                int bonusCount = GetRandInt(1, 3);
                AddItemToModData(bonusId, bonusCount, 0, false);
                DataCenterInventory.AddBonusRecord(bonusId, bonusCount, false, "exchange");
            }
        }

        // 配额检查 + 消耗
        MarketLevelManager.TryConsumeQuota(itemId, count);
        MarketLevelManager.RecordTrade(itemId, count, isBuy: true);

        bought = count;

        return true;

    }



    public static bool TryBuy(int itemId, int count) {


        if (count <= 0 || !tickers.TryGetValue(itemId, out ExchangeTicker ticker)) {


            return false;


        }





        long price = (long)Math.Ceiling(ticker.LastPrice * count);


        if (price <= 0L || price > int.MaxValue) {


            return false;


        }


        if (!TakeItemWithTip(IFE残片, (int)price, out _)) {


            return false;


        }





        AddItemToModData(itemId, count, 0, true);


        ApplyTradeImpact(ticker, count, isBuy: true);


        // 交易意外收获：15% 概率获得随机奖励（排除本次交易物品）


        if (GetRandInt(1, 100) <= 15 && tickers.Count > 1) {


            int bonusIdx = GetRandInt(0, tickers.Count - 1);


            int bonusId = tickers.Keys.ElementAt(bonusIdx);


            if (bonusId == itemId) {


                bonusIdx = (bonusIdx + 1) % tickers.Count;


                bonusId = tickers.Keys.ElementAt(bonusIdx);


            }


            if (bonusId != itemId) {


                int bonusCount = GetRandInt(1, 3);


                AddItemToModData(bonusId, bonusCount, 0, false);


            DataCenterInventory.AddBonusRecord(bonusId, bonusCount, false, "exchange");


            }


        }


        TotalTradeCount++;


        return true;


    }





    public static bool TrySell(int itemId, int count) {


        if (count <= 0 || !tickers.TryGetValue(itemId, out ExchangeTicker ticker)) {


            return false;


        }


        if (!TakeItemWithTip(itemId, count, out _)) {


            return false;


        }





        long fragments = (long)Math.Floor(ticker.BidPrice * count);


        if (fragments > 0) {


            File.AppendAllText("fe_frag_log.txt", string.Format("[{0:HH:mm:ss}] Sell {1} +{2}\n", DateTime.Now, itemId, (int)Math.Min(int.MaxValue, fragments)));
            AddItemToModData(IFE残片, (int)Math.Min(int.MaxValue, fragments), 0, true);


        }


        ApplyTradeImpact(ticker, count, isBuy: false);


        // 交易意外收获：15% 概率获得随机奖励（排除本次交易物品）


        if (GetRandInt(1, 100) <= 15 && tickers.Count > 1) {


            int bonusIdx = GetRandInt(0, tickers.Count - 1);


            int bonusId = tickers.Keys.ElementAt(bonusIdx);


            if (bonusId == itemId) {


                bonusIdx = (bonusIdx + 1) % tickers.Count;


                bonusId = tickers.Keys.ElementAt(bonusIdx);


            }


            if (bonusId != itemId) {


                int bonusCount = GetRandInt(1, 3);


                AddItemToModData(bonusId, bonusCount, 0, false);


            DataCenterInventory.AddBonusRecord(bonusId, bonusCount, false, "exchange");


            }


        }


        TotalTradeCount++;


        return true;


    }





    private static void RefreshTickers() {


        foreach (ExchangeTicker ticker in tickers.Values) {


            float baseVal = MarketValueManager.GetValue(ticker.ItemId);


            float anchor = Math.Max(1f, baseVal > 0f ? baseVal : 0.5f);


            float impact = Mathf.Clamp(ticker.NetPlayerVolume * 0.0035f, -0.15f, 0.15f);


            float randomShock = 1f + ((float)rng.NextDouble() * 0.06f - 0.03f);


            float target = anchor * (1f + impact) * randomShock;


            float newMid = ticker.LastPrice * 0.70f + target * 0.30f;


            float minPrice = Math.Max(1f, anchor * 0.50f);


            float maxPrice = Math.Max(minPrice, anchor * 1.50f);


            newMid = Mathf.Clamp(newMid, minPrice, maxPrice);





            ticker.LastPrice = newMid;


            ticker.BidPrice = Math.Max(1f, newMid * (1f - MarketLevelManager.GetSpread() / 2f));


            ticker.AskPrice = Math.Max(ticker.BidPrice, newMid * (1f + MarketLevelManager.GetSpread() / 2f));


            ticker.DayHighPrice = Math.Max(ticker.DayHighPrice, newMid);


            ticker.DayLowPrice = ticker.DayLowPrice <= 0f ? newMid : Math.Min(ticker.DayLowPrice, newMid);


            ticker.NetPlayerVolume = Mathf.RoundToInt(ticker.NetPlayerVolume * 0.60f);


            ticker.RecentPlayerBuyVolume = Mathf.RoundToInt(ticker.RecentPlayerBuyVolume * 0.50f);


            ticker.RecentPlayerSellVolume = Mathf.RoundToInt(ticker.RecentPlayerSellVolume * 0.50f);


        }





        lastRefreshTick = GameMain.gameTick;


        lastRefreshVersion = MarketValueManager.RefreshVersion;

        // v2.5: AI 价格历史追踪
        MarketAIAnalyzer.RecordPrices();

    }





    private static void ApplyTradeImpact(ExchangeTicker ticker, int count, bool isBuy) {


        ticker.LastTradeTick = GameMain.gameTick;


        if (isBuy) {


            ticker.RecentPlayerBuyVolume += count;


            ticker.NetPlayerVolume += count;


        } else {


            ticker.RecentPlayerSellVolume += count;


            ticker.NetPlayerVolume -= count;


        }





        float impactMagnitude = Math.Min(0.12f, 0.01f + 0.02f * (float)Math.Sqrt(count));


        float factor = isBuy ? 1f + impactMagnitude : 1f - impactMagnitude;


        ticker.LastPrice = Math.Max(1f, ticker.LastPrice * factor);


        ticker.BidPrice = Math.Max(1f, ticker.LastPrice * (1f - MarketLevelManager.GetSpread() / 2f));


        ticker.AskPrice = Math.Max(ticker.BidPrice, ticker.LastPrice * (1f + MarketLevelManager.GetSpread() / 2f));


        ticker.DayHighPrice = Math.Max(ticker.DayHighPrice, ticker.LastPrice);


        ticker.DayLowPrice =


            ticker.DayLowPrice <= 0f ? ticker.LastPrice : Math.Min(ticker.DayLowPrice, ticker.LastPrice);


    }





    public static void Import(BinaryReader r) {


        r.ReadBlocks(


            ("Tickers", br => {


                tickers.Clear();


                int count = br.ReadInt32();


                for (int i = 0; i < count; i++) {


                    var ticker = new ExchangeTicker {


                        ItemId = br.ReadInt32(),


                        LastPrice = br.ReadSingle(),


                        BidPrice = br.ReadSingle(),


                        AskPrice = br.ReadSingle(),


                        DayOpenPrice = br.ReadSingle(),


                        DayHighPrice = br.ReadSingle(),


                        DayLowPrice = br.ReadSingle(),


                        LastTradeTick = br.ReadInt64(),


                        NetPlayerVolume = br.ReadInt32(),


                        RecentPlayerBuyVolume = br.ReadInt32(),


                        RecentPlayerSellVolume = br.ReadInt32(),


                    };


                    tickers[ticker.ItemId] = ticker;


                }


            }),


            ("RefreshMeta", br => {


                lastRefreshTick = br.ReadInt64();


                lastRefreshVersion = br.ReadInt32();


            }),


            ("TradeStats", br => TotalTradeCount = Math.Max(0L, br.ReadInt64()))


        );


    }





    public static void Export(BinaryWriter w) {


        w.WriteBlocks(


            ("Tickers", bw => {


                bw.Write(tickers.Count);


                foreach (ExchangeTicker ticker in tickers.Values.OrderBy(t => t.ItemId)) {


                    bw.Write(ticker.ItemId);


                    bw.Write(ticker.LastPrice);


                    bw.Write(ticker.BidPrice);


                    bw.Write(ticker.AskPrice);


                    bw.Write(ticker.DayOpenPrice);


                    bw.Write(ticker.DayHighPrice);


                    bw.Write(ticker.DayLowPrice);


                    bw.Write(ticker.LastTradeTick);


                    bw.Write(ticker.NetPlayerVolume);


                    bw.Write(ticker.RecentPlayerBuyVolume);


                    bw.Write(ticker.RecentPlayerSellVolume);


                }


            }),


            ("RefreshMeta", bw => {


                bw.Write(lastRefreshTick);


                bw.Write(lastRefreshVersion);


            }),


            ("TradeStats", bw => bw.Write(TotalTradeCount))


        );


    }





    public static void IntoOtherSave() {


        Init();


        TotalTradeCount = 0;


    }


}


