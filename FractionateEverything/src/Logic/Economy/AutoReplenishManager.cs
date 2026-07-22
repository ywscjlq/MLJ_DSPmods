using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Logic.DataCenter.PlayerInventoryAccess;

namespace FE.Logic.Economy;

public static class AutoReplenishManager {
    public const long CheckIntervalTicks = 60L;

    public class ReplenishEntry {
        public int ItemId;
        public int TargetCount;
        public bool Enabled;
    }

    private static readonly List<ReplenishEntry> entries = [];
    private static long lastCheckTick;
    private static long lastMarketRefreshTick;
    private static long effectiveCheckInterval = CheckIntervalTicks;
    public static long EffectiveCheckInterval => effectiveCheckInterval;
    private static bool autoScanInitialized;
    private static bool combatModeDetection;

    private static int _unlockLevel = 1;
    public static int UnlockLevel => _unlockLevel;
    public static bool AutoScanEnabled = true;
    private static bool aiStrategyApplied;
    private static long reserveFragmentCount;
    public static long ReserveFragmentCount { get => reserveFragmentCount; set => reserveFragmentCount = Math.Max(0, value); }
    private static long totalBuyFragments;
    public static long TotalBuyFragments => totalBuyFragments;
    private static long totalSellFragments;
    public static long TotalSellFragments => totalSellFragments;
    private static long cycleCumulativeCost;
    private static int cycleBoughtCount;
    private static int cycleSkipCount;
    private static int _currentBudgetIndex;
    public static int CurrentBudgetIndex => _currentBudgetIndex;
    public static string[] BudgetOptions = ["1万", "10万", "100万", "1000万", "不限制"];

    public static long TotalBudget {
        get {
            long[] vs = [10000, 100000, 1000000, 10000000, long.MaxValue];
            return vs[Math.Min(_currentBudgetIndex, vs.Length - 1)];
        }
    }

    public static string[] BudgetOptionStrings => BudgetOptions;

    public static long EffectiveBudget => Math.Min(TotalBudget, centerItemCount != null && centerItemCount.Length > IFE残片 ? centerItemCount[IFE残片] : long.MaxValue);

    private static float _modeIndex;
    public static int ModeIndex { get => (int)_modeIndex; set => _modeIndex = value; }
    public static string[] ModeOptions = ["保守", "激进"];

    public static IReadOnlyList<ReplenishEntry> Entries => entries;

    private static string aiSuggestion = "";
    public static string AiSuggestion => aiSuggestion;

    private static string statusText = "";
    public static string StatusText => statusText;
    private static float statusTimer;

    public static void Init() {
        lastCheckTick = GameMain.gameTick > 0L ? GameMain.gameTick : 0L;
        LogInfo("[AutoReplenish] Init, stage=" + _unlockLevel);
    }

    public static void Tick() {
        if (GameMain.mainPlayer == null) return;

        if (GameMain.gameTick - lastCheckTick < EffectiveCheckInterval) return;

        lastCheckTick = GameMain.gameTick;

        if (entries.Count == 0) {
            if (AutoScanEnabled && !autoScanInitialized) {
                autoScanInitialized = true;
                ScanCommonItems();
            }
            return;
        }

        int processed = 0;
        foreach (var entry in entries) {
            if (processed++ > 7) break;
            if (!entry.Enabled || entry.ItemId <= 0 || entry.TargetCount <= 0) continue;

            long stock = centerItemCount != null && entry.ItemId >= 0 && entry.ItemId < centerItemCount.Length ? centerItemCount[entry.ItemId] : 0;
            int need = entry.TargetCount - (int)Math.Min(stock, int.MaxValue);

            if (need > 0) {
                long fragCost = (long)need * 100;
                if (cycleCumulativeCost + fragCost > EffectiveBudget) { cycleSkipCount++; continue; }
                if (fragCost > 0 && centerItemCount != null && centerItemCount.Length > IFE残片 && centerItemCount[IFE残片] >= fragCost) {
                    TakeItemFromModData(IFE残片, fragCost, out _);
                    AddItemToModData(entry.ItemId, need, 0, true);
                    totalBuyFragments += fragCost;
                    cycleCumulativeCost += fragCost;
                    cycleBoughtCount++;
                    LogInfo("[AutoReplenish] 买入 " + (LDB.items.Select(entry.ItemId)?.Name ?? "#" + entry.ItemId) + " x" + need + ", 花费" + fragCost + "碎片");
                }
            }

            long sellThreshold = entry.TargetCount * 3L;
            if (stock > sellThreshold) {
                long canSell = stock - sellThreshold;
                if (canSell > 0) {
                    long income = canSell * 50;
                    AddItemToModData(IFE残片, income, 0, true);
                    TakeItemFromModData(entry.ItemId, canSell, out _);
                    totalSellFragments += income;
                    LogInfo("[AutoReplenish] 卖出 " + (LDB.items.Select(entry.ItemId)?.Name ?? "#" + entry.ItemId) + " x" + canSell + ", 收入" + income + "碎片");
                }
            }
        }
    }

    public static void AddEntry(int itemId, int targetCount, bool enabled) {
        var existing = entries.FirstOrDefault(e => e.ItemId == itemId);
        if (existing != null) {
            existing.TargetCount = Math.Max(targetCount, existing.TargetCount);
            existing.Enabled = enabled;
            return;
        }
        entries.Add(new ReplenishEntry { ItemId = itemId, TargetCount = targetCount, Enabled = enabled });
    }

    public static void RemoveEntry(int itemId) {
        entries.RemoveAll(e => e.ItemId == itemId);
    }

    public static void ClearEntries() {
        entries.Clear();
        autoScanInitialized = false;
    }

    public static void ForceCheck() {
        lastCheckTick = 0;
    }

    public static void ScanCommonItems() {
        if (LDB.items == null) return;
        int maxId = LDB.items.dataArray.Length;
        int added = 0;
        for (int id = 1; id < maxId; id++) {
            if (!LDB.items.Exist(id)) continue;
            long stock = GetItemTotalCount(id);
            if (stock <= 0 && !IsBuildingItem(id)) continue;
            int tg = Math.Max((int)Math.Min(Math.Max(stock, 20), 9999), 60);
            var ex = entries.FirstOrDefault(e => e.ItemId == id);
            if (ex != null) { if (ex.TargetCount != tg) { ex.TargetCount = tg; added++; } }
            else { entries.Add(new ReplenishEntry { ItemId = id, TargetCount = tg, Enabled = true }); added++; }
            if (added > 50) break;
        }
        if (added > 0)
            LogInfo($"[AutoReplenish] 自动扫描添加 {added} 个物品");
    }

    public static List<ReplenishEntry> SearchEntries(string filter) {
        if (string.IsNullOrEmpty(filter)) return new List<ReplenishEntry>(entries);
        return entries.Where(e => {
            var item = LDB.items.Select(e.ItemId);
            return item != null && (item.Name.Contains(filter) || item.ID.ToString() == filter);
        }).ToList();
    }

    public static void FetchAndApplyAIStrategy() {
        try {
            long frags = centerItemCount != null && centerItemCount.Length > IFE残片 ? centerItemCount[IFE残片] : 0;
            var cli = new WebClient { Encoding = Encoding.UTF8 };
            string prompt = $"你是戴森球计划AI策略顾问。玩家：{frags}IFE碎片，阶段{_unlockLevel}。给出auto_replenish配置(JSON): strategy_name, advice, budget_ratio(0-1), target_fragments(预留碎片数量), conservative(是否保守模式)。只输出纯JSON，不要markdown。";
            var req = $"{{\"messages\":[{{\"role\":\"user\",\"content\":\"{EscJson(prompt)}\"}}],\"max_tokens\":2048}}";
            var resp = cli.UploadString("http://127.0.0.1:8080/v1/chat/completions", req);
            var choices = JObject.Parse(resp)["choices"];
            if (choices != null && choices.HasValues) {
                string jsonContent = choices[0]["message"]?.Value<string>("content") ?? "";
                int os = jsonContent.IndexOf('{'), oe = jsonContent.LastIndexOf('}') + 1;
                if (os >= 0 && oe > os) {
                    ApplyStrategy(jsonContent.Substring(os, oe - os), frags, _unlockLevel);
                    aiStrategyApplied = true;
                }
            }
        } catch (System.Exception ex) {
            LogInfo($"[AutoReplenish] AI策略请求失败: {ex.Message}");
        }
    }

    static void ApplyStrategy(string json, long frags, int stage) {
        try {
            JObject obj = JObject.Parse(json);
            float ratio = obj.Value<float>("budget_ratio");
            if (ratio > 0f) {
                _currentBudgetIndex = ratio < 0.3f ? 0 : ratio < 0.6f ? 1 : ratio < 0.9f ? 2 : 3;
            }
            long target = obj.Value<long>("target_fragments");
            if (target > 0) reserveFragmentCount = target;
            bool conservative = obj.Value<bool>("conservative");
            _modeIndex = conservative ? 0 : 1;
            string advice = obj.Value<string>("advice");
            if (!string.IsNullOrEmpty(advice)) aiSuggestion = advice;
            LogInfo($"[AutoReplenish] AI策略已应用: budgetIdx={_currentBudgetIndex}, mode={_modeIndex}, reserve={reserveFragmentCount}");
        } catch (System.Exception ex) {
            LogInfo($"[AutoReplenish] AI策略解析失败: {ex.Message}");
        }
    }

    static string EscJson(string s) {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    public static void Import(BinaryReader r) {
        int count = Math.Min(r.ReadInt32(), 500);
        entries.Clear();
        for (int i = 0; i < count; i++) {
            var e = new ReplenishEntry { ItemId = r.ReadInt32(), TargetCount = r.ReadInt32(), Enabled = r.ReadBoolean() };
            entries.Add(e);
        }
        _currentBudgetIndex = Math.Min(r.ReadInt32(), BudgetOptions.Length - 1);
        _modeIndex = r.ReadSingle();
        combatModeDetection = r.ReadBoolean();
        AutoScanEnabled = r.ReadBoolean();
        reserveFragmentCount = r.ReadInt64();
        totalBuyFragments = r.ReadInt64();
        totalSellFragments = r.ReadInt64();
        _unlockLevel = r.ReadInt32();
    }

    public static void Export(BinaryWriter w) {
        w.Write(entries.Count);
        foreach (var e in entries) {
            w.Write(e.ItemId);
            w.Write(e.TargetCount);
            w.Write(e.Enabled);
        }
        w.Write(_currentBudgetIndex);
        w.Write(_modeIndex);
        w.Write(combatModeDetection);
        w.Write(AutoScanEnabled);
        w.Write(reserveFragmentCount);
        w.Write(totalBuyFragments);
        w.Write(totalSellFragments);
        w.Write(_unlockLevel);
    }

    public static void IntoOtherSave() {
        entries.Clear();
        reserveFragmentCount = 0;
        totalBuyFragments = totalSellFragments = 0;
        autoScanInitialized = false;
        aiStrategyApplied = false;
    }

    public static int ScanAndAddFrequentItems() {
        int prev = entries.Count;
        ScanCommonItems();
        return entries.Count - prev;
    }

    public static int ForceReplenishAll() {
        lastCheckTick = 0;
        cycleSkipCount = 0;
        Tick();
        return cycleBoughtCount;
    }

    public static int RemoveDuplicates() {
        int before = entries.Count;
        entries.RemoveAll(e => e.TargetCount <= 0 || e.ItemId <= 0);
        return before - entries.Count;
    }

    public static int CycleBoughtCount => cycleBoughtCount;
    public static int CycleSkipCount => cycleSkipCount;
    public static string SearchFilter = "";
    public static bool CombatModeDetection { get => combatModeDetection; set => combatModeDetection = value; }

    static bool IsBuildingItem(int id) {
        var item = LDB.items.Select(id);
        return item != null && item.GridIndex >= 0;
    }

    public class RecentEvent {
        public long Tick;
        public int ItemId;
        public int SoldCount, BoughtCount;
        public long FragCost;
        public bool IsSell => SoldCount > 0;
    }
    private static readonly List<RecentEvent> recentEvents = [];
    public static List<RecentEvent> RecentEvents => recentEvents;
    public static List<RecentEvent> GetRecentEvents() => new(recentEvents);

    // The page references TotalBudget as a field, not property - add setter
    // public static long TotalBudget { get; set; } -- already exists as property above
}
