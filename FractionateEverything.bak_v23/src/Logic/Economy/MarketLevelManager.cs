using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FE.Logic.Economy;

public static class MarketLevelManager
{
    #region 常量表
    private static readonly int[] quotaTable = { 0, 30, 50, 80, 110, 150, 200, 250, 280, 300, 300 };
    private static readonly float[] spreadTable = { 0, 0.08f, 0.07f, 0.06f, 0.052f, 0.048f, 0.044f, 0.04f, 0.036f, 0.032f, 0.03f };
    private static readonly float[] refreshTable = { 0, 15f, 15f, 25f, 25f, 40f, 40f, 60f, 60f, 60f, 60f };
    private static readonly float[] instantTable = { 0, 0.02f, 0.02f, 0.015f, 0.015f, 0.01f, 0.01f, 0.005f, 0.005f, 0.003f, 0f };
    private static readonly int[] discoveryDaysTable = { 0, 7, 6, 5, 4, 3, 2, 2, 2, 2, 2 };
    #endregion

    #region 状态字段
    private static int currentLevel;
    private static float dynaGlobeRatio;
    private static System.Random rng = new System.Random();
    private static Dictionary<int, int> boughtThisCycle = new Dictionary<int, int>();
    private static Dictionary<int, long> firstListedTick = new Dictionary<int, long>();
    private static int heatBonus;
    private static int personalitySeed;
    private static float personalityQuotaFactor = 1.0f;
    private static float personalityEventFactor = 1.0f;
    private static int tradesSinceLastRefresh;
    private static string currentEvent;
    private static int eventRemainingTicks;
    private static int eventItemId;
    private static float eventPriceFactor = 1.0f;
    private static long lastRefreshTick;
    private static bool initialized;
    // 里程碑奖励
    public static float MilestoneBudgetMultiplier = 1.0f;
    public static int MilestoneTicksRemaining = 0;
    #endregion

    #region 公开属性
    public static int Level => currentLevel;
    public static float DynaGlobeRatio => dynaGlobeRatio;
    public static int HeatBonus => heatBonus;
    public static string CurrentEvent => currentEvent;
    public static int EventRemainingTicks => eventRemainingTicks;
    public static int EventItemId => eventItemId;
    public static float EventPriceFactor => eventPriceFactor;
    public static string PersonalityStyle { get; private set; } = "理性";
    #endregion

    #region 核心计算
    public static void Init()
    {
        boughtThisCycle.Clear();
        firstListedTick.Clear();
        currentLevel = 0;
        heatBonus = 0;
        tradesSinceLastRefresh = 0;
        currentEvent = null;
        eventRemainingTicks = 0;
        lastRefreshTick = GameMain.gameTick;
        if (personalitySeed == 0)
        {
            personalitySeed = (int)(DateTime.Now.Ticks % 10000);
            rng = new System.Random(personalitySeed);
            personalityQuotaFactor = 0.90f + (float)rng.NextDouble() * 0.20f;
            personalityEventFactor = 0.80f + (float)rng.NextDouble() * 0.40f;
            if (personalityQuotaFactor < 0.95f && personalityEventFactor < 0.95f)
                PersonalityStyle = "冷血";
            else if (personalityQuotaFactor > 1.05f && personalityEventFactor > 1.05f)
                PersonalityStyle = "狂热";
            else if (personalityEventFactor > 1.15f)
                PersonalityStyle = "神经质";
            else if (personalityQuotaFactor < 0.95f)
                PersonalityStyle = "谨慎";
            else if (personalityQuotaFactor > 1.05f)
                PersonalityStyle = "激进";
            else
                PersonalityStyle = "理性";
        }
        initialized = true;
    }

    public static void Tick()
    {
        if (!initialized) { Init(); return; }
        long now = GameMain.gameTick;
        float refreshSec = refreshTable[Math.Min(currentLevel, 10)];
        long refreshTicks = (long)(refreshSec * 60L);
        if (refreshTicks <= 0) refreshTicks = 3600L;

        if (now - lastRefreshTick >= refreshTicks)
        {
            RecalculateLevel();
            ResetCycle();
            RollEvent();
            if (tradesSinceLastRefresh >= 3)
                heatBonus = Math.Min(15, heatBonus + 2);
            else
                heatBonus = Math.Max(0, heatBonus - 1);
            tradesSinceLastRefresh = 0;
            lastRefreshTick = now;
        }
    }

    private static void RecalculateLevel()
    {
        // Safe fallback: level based on ExchangeManager tick count (each tick = 15-60s)
        // Higher levels require more total uptime
        // L1=0min, L2=10min, L3=30min, L4=1h, L5=2h, L6=4h, L7=8h, L8=14h, L9=22h, L10=32h
        long totalTicks = GameMain.gameTick;
        float hours = (float)totalTicks / 60f / 60f;
        if (hours < 0) hours = 0;

        int newLevel;
        if (hours < 0.17f) newLevel = 1;      // 0-10min
        else if (hours < 0.5f) newLevel = 2;  // 10-30min
        else if (hours < 1f) newLevel = 3;    // 30min-1h
        else if (hours < 2f) newLevel = 4;    // 1-2h
        else if (hours < 4f) newLevel = 5;    // 2-4h
        else if (hours < 8f) newLevel = 6;    // 4-8h
        else if (hours < 14f) newLevel = 7;   // 8-14h
        else if (hours < 22f) newLevel = 8;   // 14-22h
        else if (hours < 32f) newLevel = 9;   // 22-32h
        else newLevel = 10;                    // 32h+

        if (newLevel > currentLevel)
        {
            currentLevel = newLevel;
            MilestoneBudgetMultiplier = 1.20f;       // 升级奖励: +20%预算持续3个市场周期
            MilestoneTicksRemaining = (int)(refreshTable[Math.Min(currentLevel, 10)] * 60 * 3);
        }
        dynaGlobeRatio = hours;
    }

    private static void RollEvent()
    {
        currentEvent = null;
        eventRemainingTicks = 0;
        eventPriceFactor = 1.0f;
        if (currentLevel < 2) return;
        float baseProb = currentLevel * 0.015f * personalityEventFactor;  // +50% 事件频率
        if (rng.NextDouble() < baseProb)
        {
            int roll = rng.Next(6);
            switch (roll)
            {
                case 0: currentEvent = "淘金热"; eventPriceFactor = 1.5f; eventRemainingTicks = (int)(refreshTable[Math.Min(currentLevel,10)] * 60 * 5); break;
                case 1: currentEvent = "恐慌抛售"; eventPriceFactor = 0.7f; eventRemainingTicks = (int)(refreshTable[Math.Min(currentLevel,10)] * 60 * 3); break;
                case 2: if (currentLevel >= 7) { currentEvent = "大单求购"; eventPriceFactor = 1.3f; eventRemainingTicks = (int)(refreshTable[Math.Min(currentLevel,10)] * 60 * 2); } break;
                case 3: currentEvent = "黑天鹅"; eventPriceFactor = rng.NextDouble() < 0.5f ? 2.0f : 0.5f; eventRemainingTicks = (int)(refreshTable[Math.Min(currentLevel,10)] * 60 * 1); break;
                case 4: currentEvent = "市场快报"; eventRemainingTicks = 1; break;
                case 5: currentEvent = "丰收季"; eventPriceFactor = 0.65f; eventRemainingTicks = (int)(refreshTable[Math.Min(currentLevel,10)] * 60 * 6); break;
            }
            if (currentEvent != null && (roll != 2 || eventRemainingTicks > 0))
            {
                var items = ExchangeManager.ListedItems;
                if (items != null && items.Count > 0)
                    eventItemId = items[rng.Next(items.Count)];
            }
        }
    }

    public static void ResetCycle()
    {
        boughtThisCycle.Clear();
        // 里程碑奖励倒计时
        if (MilestoneTicksRemaining > 0)
        {
            MilestoneTicksRemaining--;
            if (MilestoneTicksRemaining <= 0)
                MilestoneBudgetMultiplier = 1.0f;
        }
    }
    #endregion

    #region 配额
    public static int GetQuota(int itemId)
    {
        int baseQuota = quotaTable[Math.Min(currentLevel, 10)];
        return Math.Max(1, (int)(baseQuota * personalityQuotaFactor));
    }

    public static int GetRemainingQuota(int itemId)
    {
        int total = GetQuota(itemId);
        boughtThisCycle.TryGetValue(itemId, out int used);
        return Math.Max(0, total - used);
    }

    public static bool TryConsumeQuota(int itemId, int count)
    {
        int rem = GetRemainingQuota(itemId);
        if (count > rem) return false;
        boughtThisCycle.TryGetValue(itemId, out int used);
        boughtThisCycle[itemId] = used + count;
        return true;
    }

    public static float GetPenaltyPriceMultiplier(int itemId)
    {
        int rem = GetRemainingQuota(itemId);
        return rem <= 0 ? 1.5f : 1.0f;
    }
    #endregion

    #region 价差
    public static float GetSpread()
    {
        return spreadTable[Math.Min(currentLevel, 10)];
    }

    public static float GetRefreshIntervalSeconds()
    {
        return refreshTable[Math.Min(currentLevel, 10)];
    }

    public static float GetInstantAdjust()
    {
        return instantTable[Math.Min(currentLevel, 10)];
    }
    #endregion

    #region 发现期
    public static void RegisterItemListed(int itemId)
    {
        if (!firstListedTick.ContainsKey(itemId))
            firstListedTick[itemId] = GameMain.gameTick;
    }

    public static float GetDiscoveryPenalty(int itemId)
    {
        if (!firstListedTick.TryGetValue(itemId, out long firstTick)) return 0f;
        int discoveryDays = discoveryDaysTable[Math.Min(currentLevel, 10)];
        long elapsedTicks = Math.Max(0, GameMain.gameTick - firstTick);
        long totalTicks = discoveryDays * 86400L * 60L;
        if (totalTicks <= 0) return 0f;
        if (elapsedTicks >= totalTicks) return 0f;
        float fraction = 1.0f - (float)elapsedTicks / (float)totalTicks;
        return 0.15f * fraction * fraction;
    }
    #endregion

    #region 交易记录
    public static void RecordTrade(int itemId, int count, bool isBuy)
    {
        tradesSinceLastRefresh++;
        if (isBuy)
        {
            boughtThisCycle.TryGetValue(itemId, out int used);
            boughtThisCycle[itemId] = used + count;
        }
    }
    #endregion

    #region 存档
    public static void Export(BinaryWriter bw)
    {
        bw.Write(currentLevel);
        bw.Write(dynaGlobeRatio);
        bw.Write(heatBonus);
        bw.Write(personalitySeed);
        bw.Write(personalityQuotaFactor);
        bw.Write(personalityEventFactor);
        bw.Write(tradesSinceLastRefresh);
        bw.Write(currentEvent ?? "");
        bw.Write(eventRemainingTicks);
        bw.Write(eventItemId);
        bw.Write(eventPriceFactor);
        bw.Write(lastRefreshTick);
        bw.Write(boughtThisCycle.Count);
        foreach (var kv in boughtThisCycle) { bw.Write(kv.Key); bw.Write(kv.Value); }
        bw.Write(firstListedTick.Count);
        foreach (var kv in firstListedTick) { bw.Write(kv.Key); bw.Write(kv.Value); }
    }

    public static void Import(BinaryReader br)
    {
        currentLevel = br.ReadInt32();
        dynaGlobeRatio = br.ReadSingle();
        heatBonus = br.ReadInt32();
        personalitySeed = br.ReadInt32();
        personalityQuotaFactor = br.ReadSingle();
        personalityEventFactor = br.ReadSingle();
        tradesSinceLastRefresh = br.ReadInt32();
        currentEvent = br.ReadString();
        eventRemainingTicks = br.ReadInt32();
        eventItemId = br.ReadInt32();
        eventPriceFactor = br.ReadSingle();
        lastRefreshTick = br.ReadInt64();
        int count = br.ReadInt32();
        boughtThisCycle.Clear();
        for (int i = 0; i < count; i++) boughtThisCycle[br.ReadInt32()] = br.ReadInt32();
        count = br.ReadInt32();
        firstListedTick.Clear();
        for (int i = 0; i < count; i++) firstListedTick[br.ReadInt32()] = br.ReadInt64();
        Init();
        initialized = true;
    }

    public static void IntoOtherSave() { Init(); }
    #endregion
}
