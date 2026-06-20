using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using FE.Logic.Economy;
using FE.Logic.DarkFog;
using UnityEngine;
using static FE.Logic.Items.ItemManager;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Logic.DataCenter.PlayerInventoryAccess;

namespace FE.Logic.Economy;

public static class AutoReplenishManager
{
    public const long CheckIntervalTicks = 60L;

    public class ReplenishEntry
    {
        public int ItemId;
        public int TargetCount;
        public bool Enabled;
    }

    private static readonly List<ReplenishEntry> entries = new();
    private static int reserveFragmentCount = 1000;
    private static int stopThreshold;
    private static string lastFailureReason = "";
    public enum ReplenishMode { Conservative, Aggressive }

    private static long lastCheckTick;
    private static long lastAutoScanTick;
    private static int autoScanCycleCount;
    private static bool autoScanEnabled;
    private static bool combatModeDetection = true;
    private static bool factoryFirstEnabled;
    private static long cycleCumulativeCost;
    private static long totalSellFragments;
    private static long totalBuyFragments;
    private static long lastMarketRefreshTick;
    private static bool inCombat;
    private static ReplenishMode currentMode = ReplenishMode.Conservative;
    private static int cycleBoughtCount;
    private static int cycleSkipCount;
    private static int maxStockSellThreshold = -1;
    private static float maxPriceMultiplier = 3.0f;
    private static int totalBudget;
    private static int batchIndex = 0;
    private const int TICK_BATCH_SIZE = 8;
    private static bool entriesNeedResort = true;
    private static string searchFilter = "";

    public struct ReplenishEvent
    {
        public int ItemId;
        public int BoughtCount;
        public int SoldCount;
        public int FragCost;
        public bool IsSell;
        public long Tick;
    }

    private static readonly Queue<ReplenishEvent> recentSellEvents = new();
    private static readonly Queue<ReplenishEvent> recentBuyEvents = new();
    private const int MaxRecentEvents = 40;

    public static IReadOnlyList<ReplenishEntry> Entries => entries;
    public static int StopThreshold { get => stopThreshold; set => stopThreshold = Math.Max(0, value); }
    public static int ReserveFragmentCount { get => reserveFragmentCount; set => reserveFragmentCount = Math.Max(0, value); }
    public static int EffectiveReserve
    {
        get
        {
            if (reserveFragmentCount > 0) return reserveFragmentCount;
            long frags = 0;
            if (centerItemCount != null && centerItemCount.Length > IFE残片)
                frags = centerItemCount[IFE残片];
            return (int)Math.Max(100, frags / 10L);
        }
    }
    public static string LastFailureReason => lastFailureReason;
    public static string DiagInfo = "";
    public static bool AutoScanEnabled { get => autoScanEnabled; set => autoScanEnabled = value; }
    public static ReplenishMode Mode { get => currentMode; set => currentMode = value; }
    public static int CycleBoughtCount => cycleBoughtCount;
    public static int CycleSkipCount => cycleSkipCount;
    public static int MaxStockSellThreshold { get => maxStockSellThreshold; set => maxStockSellThreshold = Math.Max(1, value); }
    public static long TotalSellFragments => totalSellFragments;
    public static long TotalBuyFragments => totalBuyFragments;
    public static float MaxPriceMultiplier { get => maxPriceMultiplier; set => maxPriceMultiplier = Math.Max(1.0f, Math.Min(value, 10.0f)); }
    public static int TotalBudget { get => totalBudget; set => totalBudget = Math.Max(0, value); }
    public static long EffectiveBudget
    {
        get
        {
            if (totalBudget > 0) return totalBudget;
            long frags = 0;
            if (centerItemCount != null && centerItemCount.Length > IFE残片)
                frags = centerItemCount[IFE残片];
            return (long)(frags * 0.7 * MarketLevelManager.MilestoneBudgetMultiplier);
        }
    }
    public static string SearchFilter { get => searchFilter; set => searchFilter = value ?? ""; }
    public static bool InCombat => inCombat;
    public static bool CombatModeDetection { get => combatModeDetection; set => combatModeDetection = value; }
    public static bool FactoryFirst { get => factoryFirstEnabled; set => factoryFirstEnabled = value; }
    public static long EffectiveCheckInterval => inCombat ? CheckIntervalTicks / 6L : CheckIntervalTicks;
    public static int UnlockLevel
    {
        get
        {
            long minute = GameMain.gameTick / 3600L;
            if (minute < 10) return 0;
            if (minute < 60) return 1;
            if (minute < 180) return 2;
            return 3;
        }
    }

    public static void Init()
    {
        lastCheckTick = GameMain.gameTick > 0L ? GameMain.gameTick : 0L;
        recentSellEvents.Clear();
        recentBuyEvents.Clear();
        FetchAndApplyAIStrategy();
    }

    public static void Tick()
    {
        if (GameMain.mainPlayer == null) return;

        if (combatModeDetection)
        {
            bool wasInCombat = inCombat;
            inCombat = DarkFogCombatManager.GetAliveGroundBaseCount() > 0
                    || DarkFogCombatManager.GetAliveHiveCount() > 0
                    || DarkFogCombatManager.HasActiveEventChain();
            if (inCombat && !wasInCombat)
            {
                lastFailureReason = "[战斗] 进入战斗状态，加速补货";
            }
            else if (!inCombat && wasInCombat)
            {
                lastFailureReason = "[战斗] 战斗结束，恢复正常节奏";
            }
        }

        if (GameMain.gameTick - lastCheckTick < EffectiveCheckInterval) return;
        lastCheckTick = GameMain.gameTick;

        RecordSnapshot();

        if (lastMarketRefreshTick != MarketValueManager.LastRefreshTick)
        {
            lastMarketRefreshTick = MarketValueManager.LastRefreshTick;
            cycleCumulativeCost = 0;
        }

        cycleBoughtCount = 0;
        cycleSkipCount = 0;

        if (GameMain.gameTick - lastAutoScanTick > 600L)
        {
            lastAutoScanTick = GameMain.gameTick;
            autoScanCycleCount++;
            ScanAndAddFrequentItems();
            if (autoScanCycleCount % 10 == 0)
                ScanAndAddAllMarketItems();
        }

        if (entriesNeedResort) { SortEntries(); entriesNeedResort = false; }
        int totalEntries = entries.Count;
        if (totalEntries == 0) return;

        int processed = 0;
        int idx = batchIndex % totalEntries;

        while (processed < TICK_BATCH_SIZE && processed < totalEntries)
        {
            var entry = entries[idx];
            idx = (idx + 1) % totalEntries;

            if (!entry.Enabled || entry.ItemId <= 0 || entry.TargetCount <= 0) { processed++; continue; }

            var tk = ExchangeManager.GetTicker(entry.ItemId);

            long stock = centerItemCount[entry.ItemId];
            int need = entry.TargetCount - (int)stock;
            if (need > 0)
            {
#if DEBUG
                Debug.Log($"[FE-补货] {entry.ItemId} need={need} stock={stock} target={entry.TargetCount}");
#endif
                if (tk == null) {
#if DEBUG
                    Debug.Log($"[FE-补货] {entry.ItemId} 无行情数据ticker");
#endif
                    DiagInfo = string.Format("[{0}] 无行情数据", entry.ItemId); cycleSkipCount++; goto sell_part; }
                long estimatedCost = (long)Math.Ceiling(tk.LastPrice * need);
                if (cycleCumulativeCost + estimatedCost > EffectiveBudget)
                {
                    DiagInfo = string.Format("[预算] {0} 累计{1}+{2} > 预算{3}",
                        entry.ItemId, cycleCumulativeCost, estimatedCost, EffectiveBudget);
#if DEBUG
                    Debug.Log($"[FE-补货] 预算不足: {entry.ItemId} 累计{cycleCumulativeCost}+{estimatedCost} > 预算{EffectiveBudget}");
#endif
                    lastFailureReason = DiagInfo;
                    cycleSkipCount++;
                    goto sell_part;
                }
                float baseValue = MarketValueManager.GetValue(entry.ItemId);
                if (baseValue > 0f && tk.AskPrice > baseValue * maxPriceMultiplier)
                {
                    string itemName = LDB.items.Select(entry.ItemId)?.Name ?? $"#{entry.ItemId}";
                    DiagInfo = string.Format("[限价] {0}({4}) Ask={1:F2} > base={2:F2}*{3}",
                        entry.ItemId, tk.AskPrice, baseValue, maxPriceMultiplier, itemName);
#if DEBUG
                    Debug.Log($"[FE-补货] 限价跳过: {entry.ItemId}({itemName}) Ask={tk.AskPrice:F2} > base={baseValue:F2}*{maxPriceMultiplier}");
#endif
                    lastFailureReason = DiagInfo;
                    cycleSkipCount++;
                    goto sell_part;
                }
                if (ExchangeManager.TryBuySilent(entry.ItemId, need, 0, out int bought))
                {
#if DEBUG
                    Debug.Log($"[FE-补货] 买入 {entry.ItemId} x{need} cost={estimatedCost}");
#endif
                    cycleCumulativeCost += estimatedCost;
                    totalBuyFragments += estimatedCost;
                    cycleBoughtCount++;
                    lock (recentBuyEvents)
                    {
                        recentBuyEvents.Enqueue(new ReplenishEvent
                        {
                            ItemId = entry.ItemId,
                            BoughtCount = need,
                            FragCost = (int)estimatedCost,
                            IsSell = false,
                            Tick = GameMain.gameTick,
                        });
                        while (recentBuyEvents.Count > MaxRecentEvents)
                            recentBuyEvents.Dequeue();
                    }
                }
                else
                {
                    cycleSkipCount++;
                    string failName = LDB.items.Select(entry.ItemId)?.Name ?? $"#{entry.ItemId}";
#if DEBUG
                    Debug.Log($"[FE-补货] 买入失败 {entry.ItemId}({failName}) need={need}");
#endif
                    DiagInfo = string.Format("[id={0}({5})] tkr={1} p={2:F2} frags={3} need={4}",
                        entry.ItemId,
                        tk != null ? "Y" : "N",
                        tk?.AskPrice ?? 0f,
                        GetItemTotalCount(IFE残片),
                        need,
                        failName);
                    lastFailureReason = string.Format("[id={0}({2})] p={1:F2} need={3}", entry.ItemId, tk?.AskPrice ?? 0f, failName, need);
                }
                sell_part: ;
            }
            long curStock = centerItemCount[entry.ItemId];
            long sellTh = maxStockSellThreshold > 0 ? maxStockSellThreshold : Math.Max(entry.TargetCount, 1000L);
            sellTh = Math.Max(sellTh, 200L);
            if (curStock > sellTh)
            {
                int canSell = (int)(curStock - sellTh);
                if (canSell > 0)
                {
                    var tkr = tk;
                    if (tkr == null) tkr = ExchangeManager.GetTicker(entry.ItemId);
                    if (tkr == null || (tkr.DayOpenPrice > 0f && tkr.AskPrice < tkr.DayOpenPrice * 0.3f))
                        goto next_entry;
                    int taken = TakeItemFromModData(entry.ItemId, canSell, out _);
                    if (taken > 0)
                    {
                        long frags = (long)Math.Floor(tkr.BidPrice * taken);
                        if (frags > 0)
                        {
                            AddItemToModData(IFE残片, (int)Math.Min(int.MaxValue, frags));
                            {
                                var allTickers = ExchangeManager.AllTickers;
                                if (allTickers != null && allTickers.Count > 1 && GetRandInt(1, 100) <= 15) {
                                    int targetIdx = GetRandInt(0, allTickers.Count - 1);
                                    int curIdx = 0;
                                    int bonusId = 0;
                                    foreach (var kv in allTickers) {
                                        if (curIdx == targetIdx) { bonusId = kv.Key; break; }
                                        curIdx++;
                                    }
                                    if (bonusId == entry.ItemId && allTickers.Count > 1) {
                                        foreach (var kv in allTickers) {
                                            if (kv.Key != entry.ItemId) { bonusId = kv.Key; break; }
                                        }
                                    }
                                    if (bonusId != entry.ItemId) {
                                        int bonusCount = GetRandInt(1, 3);
                                        AddItemToModData(bonusId, bonusCount, 0, false);
                                        AddBonusRecord(bonusId, bonusCount, false, "exchange");
                                    }
                                }
                            }
                            totalSellFragments += frags;
                            lock (recentSellEvents)
                            {
                                recentSellEvents.Enqueue(new ReplenishEvent
                                {
                                    ItemId = entry.ItemId,
                                    SoldCount = taken,
                                    FragCost = (int)Math.Min(int.MaxValue, frags),
                                    IsSell = true,
                                    Tick = GameMain.gameTick,
                                });
                                while (recentSellEvents.Count > MaxRecentEvents)
                                    recentSellEvents.Dequeue();
                            }
                        }
                    }
                }
            }
            next_entry:
            processed++;
        }
        batchIndex = (batchIndex + processed) % totalEntries;
    }

    private static void SortEntries()
    {
        entries.Sort((a, b) =>
        {
            bool aBuilding = IsBuildingItem(a.ItemId);
            bool bBuilding = IsBuildingItem(b.ItemId);
            if (aBuilding != bBuilding) return bBuilding.CompareTo(aBuilding);
            return a.ItemId.CompareTo(b.ItemId);
        });
    }

    private static float GetConsumptionRate(int itemId)
    {
        if (!stockHistory.TryGetValue(itemId, out var list) || list.Count < 2)
            return 0f;

        int sampleCount = Math.Min(list.Count, 10);
        int startIdx = list.Count - sampleCount;

        long startStock = list[startIdx].Stock;
        long endStock = list[list.Count - 1].Stock;
        long totalTicks = list[list.Count - 1].Tick - list[startIdx].Tick;

        if (totalTicks <= 0 || endStock <= 0)
            return 0f;

        long consumed = startStock - endStock;
        if (consumed <= 0)
            return 0f;

        return (float)consumed / totalTicks;
    }

    public static ReplenishEvent[] GetRecentEvents()
    {
        lock (recentSellEvents)
        {
            var all = new List<ReplenishEvent>();
            all.AddRange(recentSellEvents);
            all.AddRange(recentBuyEvents);
            all.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            return all.ToArray();
        }
    }

    public static void AddEntry(int itemId, int targetCount, bool enabled)
    {
        entries.Add(new ReplenishEntry { ItemId = itemId, TargetCount = targetCount, Enabled = enabled });
        entriesNeedResort = true;
    }

    public static int RemoveDuplicates()
    {
        int removed = 0;
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(entries[i].ItemId))
            {
                entries.RemoveAt(i);
                removed++;
            }
        }
        if (removed > 0) entriesNeedResort = true;
        return removed;
    }

    public static void RemoveEntry(int itemId)
    {
        entries.RemoveAll(e => e.ItemId == itemId);
        entriesNeedResort = true;
    }

    public static void Clear()
    {
        entries.Clear();
        recentSellEvents.Clear();
        recentBuyEvents.Clear();
        entriesNeedResort = true;
    }

    public static void Import(BinaryReader r)
    {
        r.ReadBlocks(
            ("Entries", br =>
            {
                int count = br.ReadInt32();
                entries.Clear();
                for (int i = 0; i < count; i++)
                {
                    var e = new ReplenishEntry
                    {
                        ItemId = br.ReadInt32(),
                        TargetCount = br.ReadInt32(),
                        Enabled = br.ReadBoolean(),
                    };
                    entries.Add(e);
                }
            }),
            ("Settings", br =>
            {
                reserveFragmentCount = br.ReadInt32();
                autoScanEnabled = br.ReadBoolean();
                combatModeDetection = br.ReadBoolean();
                factoryFirstEnabled = br.ReadBoolean();
                currentMode = br.ReadInt32() == 1 ? ReplenishMode.Aggressive : ReplenishMode.Conservative;
                totalBudget = br.ReadInt32();
                maxPriceMultiplier = br.ReadSingle();
                cycleCumulativeCost = br.ReadInt64();
            })
        );
    }

    public static void Export(BinaryWriter w)
    {
        w.WriteBlocks(
            ("Entries", bw =>
            {
                bw.Write(entries.Count);
                foreach (var e in entries)
                {
                    bw.Write(e.ItemId);
                    bw.Write(e.TargetCount);
                    bw.Write(e.Enabled);
                }
            }),
            ("Settings", bw =>
            {
                bw.Write(reserveFragmentCount);
                bw.Write(autoScanEnabled);
                bw.Write(combatModeDetection);
                bw.Write(factoryFirstEnabled);
                bw.Write(currentMode == ReplenishMode.Aggressive ? 1 : 0);
                bw.Write(totalBudget);
                bw.Write(maxPriceMultiplier);
                bw.Write(cycleCumulativeCost);
            })
        );
    }

    public static int ScanAndAddFrequentItems()
    {
        int added = 0;
        int maxId = Math.Min(centerItemCount.Length, 3000);
        for (int itemId = 1; itemId < maxId; itemId++)
        {
            if (!LDB.items.Exist(itemId)) continue;

            var ticker = ExchangeManager.GetTicker(itemId);
            if (ticker == null || ticker.AskPrice <= 0f) continue;

            float value = MarketValueManager.GetValue(itemId);
            if (value <= 0f && !IsBuildingItem(itemId)) continue;

            long stock = GetItemTotalCount(itemId);
            int baseCount = (int)Math.Max(stock, 20);
            int target = Math.Min(baseCount, 1000);
            if (target < 60) target = 60;
            if (target > 9999) target = 9999;

            var existing = entries.FirstOrDefault(e => e.ItemId == itemId);
            if (existing != null)
            {
                if (existing.TargetCount != target)
                    existing.TargetCount = target;
                continue;
            }
            entries.Add(new ReplenishEntry { ItemId = itemId, TargetCount = target, Enabled = true });
            added++;
        }
        return added;
    }

    public static int ForceReplenishAll()
    {
        int count = 0;
        foreach (var e in entries)
        {
            if (!e.Enabled || e.ItemId <= 0 || e.TargetCount <= 0) continue;
            long stock = centerItemCount[e.ItemId];
            int need = e.TargetCount - (int)stock;
            if (need > 0 && ExchangeManager.TryBuySilent(e.ItemId, need, 0, out int boughtFr))
                count++;
        }
        return count;
    }

    internal static void RecordSnapshot()
    {
        long tick = GameMain.gameTick;
        foreach (var entry in entries)
        {
            if (!entry.Enabled || entry.ItemId <= 0) continue;
            var list = GetOrCreateHistory(entry.ItemId);
            list.Add(new StockPoint { Tick = tick, Stock = centerItemCount[entry.ItemId] });
            if (list.Count > 20) list.RemoveAt(0);
        }
    }

    public static float GetPriceRatio(int itemId)
    {
        var t = ExchangeManager.GetTicker(itemId);
        if (t == null || t.DayOpenPrice <= 0f) return 1f;
        return t.AskPrice / t.DayOpenPrice;
    }

    private static bool IsBuildingItem(int itemId)
    {
        var item = LDB.items.Select(itemId);
        if (item == null) return false;
        if (item.BuildMode != 0) return true;
        if (item.Type == EItemType.Production) return true;
        if (item.IsEntity) return true;
        return item.CanBuild;
    }

    private struct StockPoint { public long Tick; public long Stock; }

    private static readonly Dictionary<int, List<StockPoint>> stockHistory = new();

    private static List<StockPoint> GetOrCreateHistory(int itemId)
    {
        if (!stockHistory.TryGetValue(itemId, out var list))
        {
            list = new List<StockPoint>();
            stockHistory[itemId] = list;
        }
        return list;
    }

    public static void IntoOtherSave() { Clear(); totalSellFragments = 0; totalBuyFragments = 0; }

    private static void ScanAndAddAllMarketItems() {
        foreach (int itemId in ExchangeManager.ListedItems) {
            if (!entries.Any(e => e.ItemId == itemId)) entries.Add(new ReplenishEntry { ItemId = itemId, TargetCount = 100, Enabled = true });
        }
    }

    // === AI Strategy Integration (Qwen local model + JSON cache) ===
    private static bool aiStrategyApplied;

    public static void FetchAndApplyAIStrategy()
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                long fragments = 0;
                if (centerItemCount != null && centerItemCount.Length > 1099)
                    fragments = centerItemCount[1099];
                int stage = UnlockLevel;

                string pluginDir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string cachePath = System.IO.Path.Combine(pluginDir, "ai_strategy_cache.json");

                if (System.IO.File.Exists(cachePath))
                {
                    string cacheJson = System.IO.File.ReadAllText(cachePath);
                    int idx0 = cacheJson.IndexOf("\"strategies\"");
                    if (idx0 >= 0)
                    {
                        int stageIdx = cacheJson.IndexOf("\"" + stage + "\"", idx0);
                        if (stageIdx >= 0)
                        {
                            int objStart = cacheJson.IndexOf('{', stageIdx);
                            if (objStart >= 0)
                            {
                                int depth = 0, objEnd = objStart;
                                for (int i = objStart; i < cacheJson.Length; i++)
                                {
                                    if (cacheJson[i] == '{') depth++;
                                    else if (cacheJson[i] == '}') { depth--; if (depth == 0) { objEnd = i; break; } }
                                }
                                string strategyJson = cacheJson.Substring(objStart, objEnd - objStart + 1);
                                ApplyStrategy(strategyJson, fragments, stage);
                                return;
                            }
                        }
                    }
                    LogWarning("[AI] 缓存格式异常，尝试API...");
                }

                LogInfo("[AI] 正在向本地Qwen请求策略 (碎片=" + fragments + ", 阶段=" + stage + ")...");

                var prompt = "你是戴森球计划AI策略顾问。玩家当前状态：" + fragments + " IFE碎片，阶段" + stage +
                    "。请基于万物分馏mod规则(Bid=Mid*0.96, Ask=Mid*1.04, 60秒刷新, 建筑优先)生成最优自动补货策略。" +
                    "输出纯JSON(无markdown)：{\"meta\":{\"strategy_name\":\"策略名\",\"budget_ratio\":0.7,\"target_fragments_reserve\":200,\"advice\":\"说明\"}," +
                    "\"auto_replenish\":[{\"item\":\"物品名\",\"target\":数字,\"limit_mult\":2.0,\"sell_mult\":3.0,\"reason\":\"理由\"}]}。给出8-12个关键物品。";

                var reqBody = "{\"messages\":[{\"role\":\"user\",\"content\":\"" + EscJson(prompt) + "\"}]," +
                    "\"max_tokens\":2000,\"temperature\":0.7,\"chat_template_kwargs\":{\"enable_thinking\":false}}";

                using var client = new WebClient { Encoding = Encoding.UTF8 };
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                var resp = client.UploadString("http://127.0.0.1:8080/v1/chat/completions", reqBody);

                var content = ExtJsonStr(resp, "content");
                if (string.IsNullOrWhiteSpace(content)) { LogWarning("[AI] Qwen返回空内容"); return; }
                content = CleanMd(content);

                ApplyStrategy(content, fragments, stage);
            }
            catch (WebException ex) { LogWarning("[AI] Qwen未运行(" + ex.Message + ")，跳过"); }
            catch (Exception ex) { LogWarning("[AI] 异常: " + ex.Message); }
        });
    }

    static void ApplyStrategy(string json, long fragments, int stage)
    {
        var metaName = ExtJsonStr(json, "strategy_name");
        var advice = ExtJsonStr(json, "advice");
        var budgetRatio = ExtJsonFloat(json, "budget_ratio");
        var reserve = ExtJsonInt(json, "target_fragments_reserve");

        var items = ExtJsonArr(json, "auto_replenish");
        if (items.Count == 0) { LogWarning("[AI] 策略空列表"); return; }

        foreach (var it in items)
        {
            var nm = ExtJsonStr(it, "item");
            var tg = ExtJsonInt(it, "target");
            if (string.IsNullOrEmpty(nm) || tg <= 0) continue;
            int id = 0;
            for (int i = 1; i < Math.Min(centerItemCount?.Length ?? 0, 3000); i++)
            {
                if (LDB.items.Exist(i))
                {
                    var p = LDB.items.Select(i);
                    if ((p?.Name?.Translate() ?? "").Contains(nm)) { id = i; break; }
                }
            }
            if (id <= 0) continue;
            var ex = entries.Find(e => e.ItemId == id);
            if (ex != null) ex.TargetCount = tg;
            else entries.Add(new ReplenishEntry { ItemId = id, TargetCount = tg, Enabled = true });
        }
        if (budgetRatio > 0f && budgetRatio <= 1f) totalBudget = (int)(fragments * budgetRatio);
        if (reserve > 0) reserveFragmentCount = reserve;
        aiStrategyApplied = true;
        LogInfo("[AI] 策略已应用: " + (metaName ?? "默认") + ", " + items.Count + "个物品, 预算=" + totalBudget + ", 储备=" + reserveFragmentCount);
        if (!string.IsNullOrEmpty(advice)) LogInfo("[AI] 建议: " + advice);
    }

    static string EscJson(string s) { return (s ?? "").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r",""); }
    static string ExtJsonStr(string j, string k) { int i=j.IndexOf("\""+k+"\""); if(i<0)return null; i+=k.Length+2; while(i<j.Length&&(j[i]==':'||j[i]==' '))i++; if(i>=j.Length||j[i]!='"')return null; int e=i+1; while(e<j.Length&&(j[e]!='"'||j[e-1]=='\\'))e++; return j.Substring(i+1,e-i-1).Replace("\\\"","\""); }
    static float ExtJsonFloat(string j, string k) { var s=ExtJsonStr(j,k); if(s!=null&&float.TryParse(s,out float r))return r; int i=j.IndexOf("\""+k+"\"");if(i<0)return 0f;i+=k.Length+2;while(i<j.Length&&(j[i]==':'||j[i]==' '))i++;int e=i;while(e<j.Length&&(char.IsDigit(j[e])||j[e]=='.'||j[e]=='-'))e++;float.TryParse(j.Substring(i,e-i),out float r2);return r2; }
    static int ExtJsonInt(string j, string k) { int i=j.IndexOf("\""+k+"\"");if(i<0)return 0;i+=k.Length+2;while(i<j.Length&&(j[i]==':'||j[i]==' '))i++;int e=i;while(e<j.Length&&(char.IsDigit(j[e])||j[e]=='-'))e++;int.TryParse(j.Substring(i,e-i),out int r);return r; }
    static List<string> ExtJsonArr(string j, string k) { var r=new List<string>();int i=j.IndexOf("\""+k+"\"");if(i<0)return r;i+=k.Length+2;while(i<j.Length&&(j[i]==':'||j[i]==' '))i++;if(i>=j.Length||j[i]!='[')return r;int d=0,s=-1;for(;i<j.Length;i++){if(j[i]=='{'){if(d==0)s=i;d++;}else if(j[i]=='}'){d--;if(d==0&&s>=0){r.Add(j.Substring(s,i-s+1));s=-1;}}else if(j[i]==']'&&d==0)break;}return r; }
    static string CleanMd(string t) { if(t.Contains("```json"))t=t.Substring(t.IndexOf("```json")+7);if(t.Contains("```"))t=t.Substring(0,t.LastIndexOf("```"));return t.Trim(); }
}