using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 词缀稀有度 (六阶: 银->绿->蓝->紫->橙->红神话)
/// 映射LoL Arena Silver/Gold/Prismatic/Star
/// </summary>
public enum AffixRarity {
    Silver = 0,
    Green = 1,
    Blue = 2,
    Purple = 3,
    Orange = 4,
    Mythic = 5,
}

/// <summary>
/// 流派 (4大派系)
/// </summary>
public enum AffixFaction {
    Speed = 0,
    Output = 1,
    Consumption = 2,
    Chaos = 3,
}

/// <summary>
/// 玩家词缀等级 (五阶支架式渐进解锁)
/// </summary>
public enum PlayerAffixLevel {
    Apprentice = 0,
    Craftsman = 1,
    Master = 2,
    Grandmaster = 3,
    Mythic = 4,
}

/// <summary>
/// 词缀类型标签
/// </summary>
public enum AffixType {
    Basic = 0,
    Growth = 1,
    Quest = 2,
    TradeOff = 3,
    Amplifier = 4,
    RuleChanger = 5,
    Rhythm = 6,
    Chain = 7,
}

/// <summary>
/// 单个词缀实例
/// </summary>
[Serializable]
public struct FracAffixInstance {
    public int AffixId;
    public AffixRarity Rarity;
    public float Value;
    public float GrowthAccum;
    public int FractionCount;
    public bool QuestComplete;

    public FracAffixInstance(int id, AffixRarity rarity, float value) {
        AffixId = id; Rarity = rarity; Value = value;
        GrowthAccum = 0f; FractionCount = 0; QuestComplete = false;
    }

    public readonly string Name => FracAffixTemplates.GetById(AffixId).NameKey ?? AffixId.ToString();

    public readonly string EffectDesc {
        get {
            var tmpl = FracAffixTemplates.GetById(AffixId);
            if (tmpl.Id == 0) return "";
            float displayVal = Value + GrowthAccum;
            string sign = displayVal >= 0 ? "+" : "";
            string label = tmpl.EffectLabel ?? "";
            return $"{sign}{displayVal:P0}{label}";
        }
    }
}

/// <summary>
/// 词缀模板定义
/// </summary>
[Serializable]
public struct FracAffixTemplate {
    public int Id;
    public string NameKey;
    public AffixFaction Faction;
    public AffixType Type;
    public float BaseMinValue;
    public float BaseMaxValue;
    public float PerRarityScale;
    public float ChaosPenaltyMin;
    public float ChaosPenaltyMax;
    public int CostPerRefresh;
    public string EffectLabel;
    public int DropWeight;
    public int QuestThreshold;
    public float GrowthPerFraction;
    public int ChainPairId;
    public float RhythmPeriod;
    public float RhythmHighMultiplier;
    public float RhythmLowMultiplier;
    public int UnlockLevel;
}

/// <summary>
/// 预定义词缀库 (34词缀 vFinal)
/// </summary>
public static class FracAffixTemplates {
    public static readonly List<FracAffixTemplate> All = new() {
        new() { Id=1, NameKey="加速齿轮", Faction=AffixFaction.Speed, Type=AffixType.Basic, EffectLabel="分馏速度", BaseMinValue=0.10f, BaseMaxValue=0.18f, CostPerRefresh=60, DropWeight=10, UnlockLevel=0 },
        new() { Id=2, NameKey="超频核心", Faction=AffixFaction.Speed, Type=AffixType.Chain, EffectLabel="分馏速度", BaseMinValue=0.14f, BaseMaxValue=0.22f, CostPerRefresh=70, DropWeight=8, ChainPairId=1, UnlockLevel=1 },
        new() { Id=3, NameKey="时间翘曲", Faction=AffixFaction.Speed, Type=AffixType.Basic, EffectLabel="分馏速度", BaseMinValue=0.08f, BaseMaxValue=0.15f, CostPerRefresh=55, DropWeight=10, UnlockLevel=0 },
        new() { Id=4, NameKey="脉冲加速", Faction=AffixFaction.Speed, Type=AffixType.Basic, EffectLabel="分馏速度", BaseMinValue=0.06f, BaseMaxValue=0.12f, CostPerRefresh=50, DropWeight=12, UnlockLevel=0 },
        new() { Id=5, NameKey="光速引擎", Faction=AffixFaction.Speed, Type=AffixType.Basic, EffectLabel="分馏速度", BaseMinValue=0.16f, BaseMaxValue=0.28f, CostPerRefresh=80, DropWeight=5, UnlockLevel=0 },
        new() { Id=6, NameKey="快子注入", Faction=AffixFaction.Speed, Type=AffixType.Basic, EffectLabel="分馏速度", BaseMinValue=0.05f, BaseMaxValue=0.10f, CostPerRefresh=45, DropWeight=14, UnlockLevel=0 },
        new() { Id=7, NameKey="无限进化", Faction=AffixFaction.Speed, Type=AffixType.Growth, EffectLabel="速度成长", BaseMinValue=0f, BaseMaxValue=0f, CostPerRefresh=120, DropWeight=15, GrowthPerFraction=0.0005f, UnlockLevel=1 },
        new() { Id=10, NameKey="增产模块", Faction=AffixFaction.Output, Type=AffixType.Basic, EffectLabel="额外产出", BaseMinValue=0.05f, BaseMaxValue=0.12f, CostPerRefresh=60, DropWeight=10, UnlockLevel=0 },
        new() { Id=11, NameKey="量子复制", Faction=AffixFaction.Output, Type=AffixType.Chain, EffectLabel="额外产出", BaseMinValue=0.08f, BaseMaxValue=0.16f, CostPerRefresh=70, DropWeight=8, ChainPairId=10, UnlockLevel=1 },
        new() { Id=12, NameKey="精密校准", Faction=AffixFaction.Output, Type=AffixType.Basic, EffectLabel="成功率", BaseMinValue=0.03f, BaseMaxValue=0.08f, CostPerRefresh=55, DropWeight=10, UnlockLevel=0 },
        new() { Id=13, NameKey="双倍输出", Faction=AffixFaction.Output, Type=AffixType.Basic, EffectLabel="额外产出", BaseMinValue=0.12f, BaseMaxValue=0.24f, CostPerRefresh=80, DropWeight=5, UnlockLevel=0 },
        new() { Id=14, NameKey="催化加速", Faction=AffixFaction.Output, Type=AffixType.Basic, EffectLabel="产出倍率", BaseMinValue=0.04f, BaseMaxValue=0.10f, CostPerRefresh=50, DropWeight=12, UnlockLevel=0 },
        new() { Id=15, NameKey="超级浓缩", Faction=AffixFaction.Output, Type=AffixType.Basic, EffectLabel="额外产出", BaseMinValue=0.18f, BaseMaxValue=0.30f, CostPerRefresh=90, DropWeight=3, UnlockLevel=0 },
        new() { Id=16, NameKey="量子累积", Faction=AffixFaction.Output, Type=AffixType.Growth, EffectLabel="产出成长", BaseMinValue=0f, BaseMaxValue=0f, CostPerRefresh=120, DropWeight=15, GrowthPerFraction=0.0005f, UnlockLevel=1 },
        new() { Id=20, NameKey="回收协议", Faction=AffixFaction.Consumption, Type=AffixType.Basic, EffectLabel="消耗降低", BaseMinValue=0.10f, BaseMaxValue=0.20f, CostPerRefresh=55, DropWeight=10, UnlockLevel=0 },
        new() { Id=21, NameKey="无限循环", Faction=AffixFaction.Consumption, Type=AffixType.Chain, EffectLabel="消耗降低", BaseMinValue=0.12f, BaseMaxValue=0.24f, CostPerRefresh=65, DropWeight=8, ChainPairId=20, UnlockLevel=1 },
        new() { Id=22, NameKey="节能核心", Faction=AffixFaction.Consumption, Type=AffixType.Basic, EffectLabel="能耗降低", BaseMinValue=0.08f, BaseMaxValue=0.18f, CostPerRefresh=50, DropWeight=12, UnlockLevel=0 },
        new() { Id=23, NameKey="无损转换", Faction=AffixFaction.Consumption, Type=AffixType.Basic, EffectLabel="不消耗率", BaseMinValue=0.03f, BaseMaxValue=0.10f, CostPerRefresh=60, DropWeight=10, UnlockLevel=0 },
        new() { Id=24, NameKey="物质守恒", Faction=AffixFaction.Consumption, Type=AffixType.Basic, EffectLabel="消耗降低", BaseMinValue=0.06f, BaseMaxValue=0.14f, CostPerRefresh=55, DropWeight=10, UnlockLevel=0 },
        new() { Id=25, NameKey="永动核心", Faction=AffixFaction.Consumption, Type=AffixType.Basic, EffectLabel="消耗降低", BaseMinValue=0.15f, BaseMaxValue=0.30f, CostPerRefresh=80, DropWeight=4, UnlockLevel=0 },
        new() { Id=26, NameKey="熵减屏障", Faction=AffixFaction.Consumption, Type=AffixType.Growth, EffectLabel="惩罚降低", BaseMinValue=0f, BaseMaxValue=0f, CostPerRefresh=110, DropWeight=15, GrowthPerFraction=0.0004f, UnlockLevel=1 },
        new() { Id=27, NameKey="燃烧协议", Faction=AffixFaction.Consumption, Type=AffixType.TradeOff, EffectLabel="消耗/速度", BaseMinValue=0.15f, BaseMaxValue=0.30f, ChaosPenaltyMin=0.05f, ChaosPenaltyMax=0.10f, CostPerRefresh=70, DropWeight=8, UnlockLevel=1 },
        new() { Id=28, NameKey="物质反馈", Faction=AffixFaction.Consumption, Type=AffixType.RuleChanger, EffectLabel="消耗转产出", BaseMinValue=0.10f, BaseMaxValue=0.20f, CostPerRefresh=90, DropWeight=6, UnlockLevel=2 },
        new() { Id=29, NameKey="脉冲回收", Faction=AffixFaction.Consumption, Type=AffixType.Rhythm, EffectLabel="脉冲回收", BaseMinValue=1.0f, BaseMaxValue=1.0f, CostPerRefresh=75, DropWeight=7, RhythmPeriod=60f, RhythmHighMultiplier=1.5f, RhythmLowMultiplier=0.5f, UnlockLevel=1 },
        new() { Id=30, NameKey="孤注一掷", Faction=AffixFaction.Chaos, Type=AffixType.Basic, EffectLabel="产出倍率", BaseMinValue=0.5f, BaseMaxValue=0.5f, ChaosPenaltyMin=0.15f, ChaosPenaltyMax=0.15f, CostPerRefresh=30, DropWeight=14, UnlockLevel=1 },
        new() { Id=31, NameKey="混沌吸收", Faction=AffixFaction.Chaos, Type=AffixType.Chain, EffectLabel="产出倍率", BaseMinValue=0.8f, BaseMaxValue=1.2f, CostPerRefresh=45, DropWeight=12, ChainPairId=30, UnlockLevel=2 },
        new() { Id=32, NameKey="概率之环", Faction=AffixFaction.Chaos, Type=AffixType.Basic, EffectLabel="产出倍率", BaseMinValue=1.5f, BaseMaxValue=2.5f, ChaosPenaltyMin=0.10f, ChaosPenaltyMax=0.20f, CostPerRefresh=55, DropWeight=6, UnlockLevel=2 },
        new() { Id=33, NameKey="熵增引擎", Faction=AffixFaction.Chaos, Type=AffixType.Basic, EffectLabel="产出倍率", BaseMinValue=1.5f, BaseMaxValue=2.5f, ChaosPenaltyMin=0.25f, ChaosPenaltyMax=0.40f, CostPerRefresh=50, DropWeight=8, UnlockLevel=2 },
        new() { Id=40, NameKey="萃取任务", Faction=AffixFaction.Speed, Type=AffixType.Quest, EffectLabel="(任务中)", BaseMinValue=0f, BaseMaxValue=0f, CostPerRefresh=100, DropWeight=4, QuestThreshold=5000, UnlockLevel=2 },
        new() { Id=41, NameKey="钢铁雄心", Faction=AffixFaction.Output, Type=AffixType.Quest, EffectLabel="(任务中)", BaseMinValue=0f, BaseMaxValue=0f, CostPerRefresh=100, DropWeight=4, QuestThreshold=10000, UnlockLevel=2 },
        new() { Id=42, NameKey="命运共鸣", Faction=AffixFaction.Chaos, Type=AffixType.Quest, EffectLabel="(任务中)", BaseMinValue=0f, BaseMaxValue=0f, CostPerRefresh=100, DropWeight=3, QuestThreshold=3000, UnlockLevel=2 },
        new() { Id=43, NameKey="过载驱动", Faction=AffixFaction.Speed, Type=AffixType.TradeOff, EffectLabel="速度/消耗", BaseMinValue=2.5f, BaseMaxValue=2.5f, ChaosPenaltyMin=0.15f, ChaosPenaltyMax=0.15f, CostPerRefresh=90, DropWeight=4, UnlockLevel=2 },
        new() { Id=44, NameKey="末日机制", Faction=AffixFaction.Output, Type=AffixType.TradeOff, EffectLabel="产出/归零", BaseMinValue=3f, BaseMaxValue=3f, ChaosPenaltyMin=0.10f, ChaosPenaltyMax=0.10f, CostPerRefresh=90, DropWeight=4, UnlockLevel=2 },
        new() { Id=45, NameKey="时间借贷", Faction=AffixFaction.Speed, Type=AffixType.TradeOff, EffectLabel="爆速/冷却", BaseMinValue=2f, BaseMaxValue=2f, CostPerRefresh=70, DropWeight=5, UnlockLevel=2 },
        new() { Id=46, NameKey="增幅共鸣", Faction=AffixFaction.Output, Type=AffixType.Amplifier, EffectLabel="成长增幅", BaseMinValue=0.75f, BaseMaxValue=0.75f, CostPerRefresh=100, DropWeight=3, UnlockLevel=3 },
        new() { Id=47, NameKey="彗星加速", Faction=AffixFaction.Speed, Type=AffixType.Amplifier, EffectLabel="彗星加速", BaseMinValue=0.33f, BaseMaxValue=0.33f, CostPerRefresh=80, DropWeight=4, UnlockLevel=3 },
        new() { Id=48, NameKey="量子隧道", Faction=AffixFaction.Chaos, Type=AffixType.RuleChanger, EffectLabel="直传率", BaseMinValue=0.15f, BaseMaxValue=0.15f, CostPerRefresh=100, DropWeight=3, UnlockLevel=3 },
        new() { Id=49, NameKey="混沌风暴", Faction=AffixFaction.Chaos, Type=AffixType.RuleChanger, EffectLabel="惩罚反转", BaseMinValue=0.50f, BaseMaxValue=0.50f, CostPerRefresh=100, DropWeight=2, UnlockLevel=3 },
        new() { Id=50, NameKey="潮汐节律", Faction=AffixFaction.Output, Type=AffixType.Rhythm, EffectLabel="潮汐产出", BaseMinValue=1.5f, BaseMaxValue=1.5f, CostPerRefresh=80, DropWeight=6, RhythmPeriod=30f, RhythmHighMultiplier=1.5f, RhythmLowMultiplier=0.7f, UnlockLevel=1 },
        new() { Id=51, NameKey="脉冲过载", Faction=AffixFaction.Speed, Type=AffixType.Rhythm, EffectLabel="脉冲速度", BaseMinValue=3f, BaseMaxValue=3f, CostPerRefresh=90, DropWeight=5, RhythmPeriod=60f, RhythmHighMultiplier=3f, RhythmLowMultiplier=0.5f, UnlockLevel=2 },
        new() { Id=52, NameKey="混沌相位", Faction=AffixFaction.Chaos, Type=AffixType.Rhythm, EffectLabel="相位x2", BaseMinValue=2f, BaseMaxValue=2f, CostPerRefresh=100, DropWeight=4, RhythmPeriod=120f, RhythmHighMultiplier=2f, RhythmLowMultiplier=1.5f, UnlockLevel=3 },
        // ── 意外型成就专用模板 (仅通过 mutation/twin roll 出现) ──
        new() { Id=900, NameKey="ƎⱯꟼ", Faction=AffixFaction.Chaos, Type=AffixType.TradeOff, EffectLabel="突变·双流派", BaseMinValue=1.8f, BaseMaxValue=1.8f, CostPerRefresh=200, DropWeight=0, UnlockLevel=4 },
        new() { Id=901, NameKey="双子", Faction=AffixFaction.Output, Type=AffixType.Basic, EffectLabel="双子·双倍", BaseMinValue=2.0f, BaseMaxValue=2.0f, CostPerRefresh=200, DropWeight=0, UnlockLevel=2 },
    };

    private static readonly ConcurrentDictionary<int, FracAffixTemplate> _byId = new();

    public static FracAffixTemplate GetById(int id) {
        if (!_byId.TryGetValue(id, out var tmpl)) {
            foreach (var t in All) { _byId.TryAdd(t.Id, t); }
            _byId.TryGetValue(id, out tmpl);
        }
        return tmpl;
    }

    public static float GetRarityScale(AffixRarity rarity) => rarity switch {
        AffixRarity.Silver => 1.0f,
        AffixRarity.Green => 1.3f,
        AffixRarity.Blue => 1.7f,
        AffixRarity.Purple => 2.2f,
        AffixRarity.Orange => 2.8f,
        AffixRarity.Mythic => 3.5f,
        _ => 1.0f
    };

    public static float GetValue(FracAffixTemplate tmpl, AffixRarity rarity) {
        float scale = GetRarityScale(rarity);
        float mid = (tmpl.BaseMinValue + tmpl.BaseMaxValue) * 0.5f;
        return mid * scale;
    }

    public static AffixRarity RollRarity(ref uint seed) {
        double r = FE.Utils.Utils.GetRandDouble(ref seed);
        if (r < 0.02) return AffixRarity.Mythic;
        if (r < 0.07) return AffixRarity.Orange;
        if (r < 0.17) return AffixRarity.Purple;
        if (r < 0.35) return AffixRarity.Blue;
        if (r < 0.60) return AffixRarity.Green;
        return AffixRarity.Silver;
    }

    private static int _totalWeight = -1;
    public static int TotalWeight {
        get {
            if (_totalWeight < 0) { _totalWeight = 0; foreach (var t in All) _totalWeight += t.DropWeight; }
            return _totalWeight;
        }
    }

    // Pre-computed: level -> available templates + total weight (computed once, zero-allocation for roll)
    private static readonly List<FracAffixTemplate>[] _byLevel;
    private static readonly int[] _byLevelTotalWeight;
    // Pre-computed: [level][faction] -> templates + total weight (for faction-locked refresh)
    private static readonly List<FracAffixTemplate>[,] _byLevelFaction;
    private static readonly int[,] _byLevelFactionTotalWeight;

    static FracAffixTemplates() {
        _byLevel = new List<FracAffixTemplate>[5];
        _byLevelTotalWeight = new int[5];
        _byLevelFaction = new List<FracAffixTemplate>[5, 4];
        _byLevelFactionTotalWeight = new int[5, 4];
        for (int lvl = 0; lvl < 5; lvl++) {
            var list = new List<FracAffixTemplate>();
            int tw = 0;
            var factionLists = new List<FracAffixTemplate>[4];
            for (int f = 0; f < 4; f++) { factionLists[f] = new List<FracAffixTemplate>(); }
            foreach (var t in All) {
                if (t.UnlockLevel <= lvl) {
                    list.Add(t);
                    tw += t.DropWeight;
                    factionLists[(int)t.Faction].Add(t);
                }
            }
            _byLevel[lvl] = list;
            _byLevelTotalWeight[lvl] = tw;
            for (int f = 0; f < 4; f++) {
                _byLevelFaction[lvl, f] = factionLists[f];
                int ftw = 0;
                foreach (var t in factionLists[f]) ftw += t.DropWeight;
                _byLevelFactionTotalWeight[lvl, f] = ftw;
            }
        }
    }

    public static List<FracAffixTemplate> GetAvailableForLevel(PlayerAffixLevel level) {
        return _byLevel[(int)level];
    }

    /// <summary>获取指定流派的词缀模板（用于融合系统）</summary>
    public static List<FracAffixTemplate> GetAvailableForFaction(AffixFaction faction) {
        var result = new List<FracAffixTemplate>();
        foreach (var t in All) {
            if (t.Faction == faction)
                result.Add(t);
        }
        return result;
    }

    public static FracAffixTemplate RollByWeightFiltered(ref uint seed, PlayerAffixLevel level) {
        int idx = (int)level;
        var available = _byLevel[idx];
        if (available.Count == 0) return All[0];
        int tw = _byLevelTotalWeight[idx];
        int roll = (int)(Utils.Utils.GetRandDouble(ref seed) * tw);
        int cum = 0;
        foreach (var t in available) {
            cum += t.DropWeight;
            if (roll < cum) return t;
        }
        return available[available.Count - 1];
    }

    /// <summary>流派锁定刷新——仅从指定流派的已解锁词缀中按权重roll</summary>
    public static FracAffixTemplate RollByWeightFilteredByFaction(ref uint seed, PlayerAffixLevel level, AffixFaction faction) {
        int lidx = (int)level, fidx = (int)faction;
        var available = _byLevelFaction[lidx, fidx];
        if (available.Count == 0) return RollByWeightFiltered(ref seed, level); // 防守: 该流派无模板则退化为全池
        int tw = _byLevelFactionTotalWeight[lidx, fidx];
        int roll = (int)(Utils.Utils.GetRandDouble(ref seed) * tw);
        int cum = 0;
        foreach (var t in available) {
            cum += t.DropWeight;
            if (roll < cum) return t;
        }
        return available[available.Count - 1];
    }

    public static FracAffixTemplate RollByWeight(ref uint seed) {
        int tw = TotalWeight;
        int roll = (int)(Utils.Utils.GetRandDouble(ref seed) * tw);
        int cum = 0;
        foreach (var t in All) {
            cum += t.DropWeight;
            if (roll < cum) return t;
        }
        return All[All.Count - 1];
    }
}
