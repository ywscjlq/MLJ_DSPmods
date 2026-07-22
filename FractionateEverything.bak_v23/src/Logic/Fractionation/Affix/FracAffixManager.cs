using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FE.Logic.Fractionation.Process;
using FE.Logic.Buildings;
using FE.Logic.DataCenter;
using static FE.Logic.DataCenter.PlayerInventoryAccess;
using static FE.Utils.Utils;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 分馏塔词缀管理器
/// 词缀羁绊（统一替代旧流派共鸣/冲突/连锁三系统）+ 彗星 + 成长/任务/节律 + 三选一刷新
/// </summary>
public static class FracAffixManager {
    private static readonly ConcurrentDictionary<(int planetId, int fractionatorId), List<FracAffixInstance>> instanceAffixes = new();
    
    public const int MaxAffixCount = 5;
    private const int BaseRefreshCost = 100;
    public const int ChoiceCount = 3;
    public const int MUTANT_AFFIX_ID = 900;
    public const int TWIN_AFFIX_ID = 901;
    private const int PityThreshold = 7;
    
    private static readonly ConcurrentDictionary<(int,int), long> cometStacks = new();
    private static readonly ConcurrentDictionary<(int,int), long> burstQueue = new();
    private const long BurstQueueMax = 200;
    [ThreadStatic]
    public static int LastFractionProductCount = 1;
    [ThreadStatic]
    public static float CurrentResonanceBurstScale = 1.0f;
    private static readonly long[] CometMilestones = { 20, 40, 80, 160, 250, 400, 640 };
    private static readonly float[] CometBonuses = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.75f, 1.0f };
    
    private static int playerTotalRefreshes = 0;
    private static int playerFactionSwitches = 0;
    private static int playerMaxCometMilestone = 0;
    
    public static PlayerAffixLevel GetPlayerLevel() {
        int exp = FracAffixProgression.CalculateExp(playerTotalRefreshes, playerFactionSwitches, playerMaxCometMilestone);
        return FracAffixProgression.GetLevel(exp);
    }
    
    public static AffixFaction GetDominantFaction(int planetId, int fractionatorId) {
        var affixes = GetAffixes(planetId, fractionatorId);
        if (affixes == null || affixes.Count == 0) return AffixFaction.Speed;
        var counts = new int[4];
        foreach (var a in affixes) {
            var t = FracAffixTemplates.GetById(a.AffixId);
            counts[(int)t.Faction]++;
        }
        int best = 0, bestIdx = 0;
        for (int i = 0; i < 4; i++) { if (counts[i] > best) { best = counts[i]; bestIdx = i; } }
        return (AffixFaction)bestIdx;
    }
    
    // ============================================================
    // 🆕 词缀羁绊系统（统一替代旧 共鸣/冲突/连锁）
    // ============================================================
    
    /// <summary>单条羁绊定义</summary>
    public struct AffixBondDef {
        public int Id;
        public string Label;        // 展示名 e.g. "⚡⚡📦速产协议"
        public string Condition;    // 条件描述 e.g. "速度≥2 + 产出≥1"
        public string Effect;       // 效果描述 e.g. "产出+20%，消耗×2"
        public Func<int[], int[], int, float> Evaluate; // (factionCounts, chainHits, flywheelStacks) → scalar bonus
        public string BonusTarget;  // "output" / "speed" / "consume" / "chaos" / "burst" / "remain" / "append"
    }
    
    /// <summary>羁绊计算结果</summary>
    public struct AffixBondResults {
        public float speedBonus;
        public float outputBonus;
        public float consumeReduce;
        public float chaosOutputBoost;
        public float chaosPenaltyReduce;
        public bool chaosImmunity;
        public float burstQueueScale;
        public float flywheelOutputBonus;
        public float flywheelSpeedBonus;
        public float remainInputBonus;
        public float appendRatioBonus;
        public float extraConsumeReduce;
        public float conflictChaosPenaltyReduction;
        public float chainBonus;
        public string[] activeLabels;
    }
    
    /// <summary>一条羁绊的预览信息（活跃 or 差一点）</summary>
    public struct BondPreview {
        public string Label;
        public bool Active;         // true=已激活, false=差一点
        public int MissingFaction;  // -1=不缺流派, 否则缺哪派
    }
    
    // 预计算 factionCounts 和 chainHits
    private static void CountFactions(IReadOnlyList<FracAffixInstance> affixes, FracAffixTemplate[] tmpls,
        out int[] counts, out bool[] chainHits) {
        counts = new int[4];
        chainHits = new bool[4]; // index 0=chain1(IDs 1,2), 1=chain2(IDs 10,11), 2=chain3(IDs 20,21), 3=chain4(IDs 30,31)
        bool h1 = false, h2 = false, h10 = false, h11 = false, h20 = false, h21 = false, h30 = false, h31 = false;
        for (int i = 0; i < affixes.Count; i++) {
            counts[(int)tmpls[i].Faction]++;
            switch (affixes[i].AffixId) {
                case 1: h1 = true; break; case 2: h2 = true; break;
                case 10: h10 = true; break; case 11: h11 = true; break;
                case 20: h20 = true; break; case 21: h21 = true; break;
                case 30: h30 = true; break; case 31: h31 = true; break;
            }
        }
        chainHits[0] = h1 && h2;
        chainHits[1] = h10 && h11;
        chainHits[2] = h20 && h21;
        chainHits[3] = h30 && h31;
    }
    
    /// <summary>评估当前词缀列表触发的所有羁绊</summary>
    private static AffixBondResults EvaluateBonds(IReadOnlyList<FracAffixInstance> affixes,
        FracAffixTemplate[] tmpls, int planetId, int fractionatorId) {
        
        CountFactions(affixes, tmpls, out int[] fc, out bool[] ch);
        int spd = fc[0], out_ = fc[1], con = fc[2], cha = fc[3];
        int flyStacks = _flywheelCounts.TryGetValue((planetId, fractionatorId), out int fs) ? Math.Min(fs, FlywheelMaxStacks) : 0;
        
        var result = new AffixBondResults {
            appendRatioBonus = 1f,
            burstQueueScale = 1.0f,
        };
        var labels = new List<string>();
        
        // ── 流派共鸣（同流派计数）──
        if (spd >= 5) { result.speedBonus += 0.40f; result.burstQueueScale = 3.0f; labels.Add("⚡极速领域 5/5"); }
        else if (spd >= 3) { result.speedBonus += 0.20f; result.burstQueueScale = 1.5f; labels.Add("⚡超频共振 3/5"); }
        else if (spd >= 2) { result.speedBonus += 0.10f; labels.Add("⚡加速共鸣 2/5"); }
        
        if (out_ >= 5) { result.outputBonus += 0.40f; labels.Add("📦量子富饶 5/5"); }
        else if (out_ >= 3) { result.outputBonus += 0.20f; labels.Add("📦丰收共鸣 3/5"); }
        else if (out_ >= 2) { result.outputBonus += 0.10f; labels.Add("📦增产共鸣 2/5"); }
        
        if (con >= 5) { result.consumeReduce += 0.50f; labels.Add("💧永恒闭环 5/5"); }
        else if (con >= 3) { result.consumeReduce += 0.30f; labels.Add("💧永续之轮 3/5"); }
        else if (con >= 2) { result.consumeReduce += 0.15f; labels.Add("💧节能共鸣 2/5"); }
        
        if (cha >= 5) { result.chaosOutputBoost += 1.0f; result.chaosImmunity = true; labels.Add("🌌深渊主宰 5/5"); }
        else if (cha >= 3) { result.chaosOutputBoost += 0.5f; result.chaosPenaltyReduce += 0.30f; labels.Add("🌌混沌核心 3/5"); }
        else if (cha >= 2) { result.chaosOutputBoost += 0.3f; labels.Add("🌌混沌共鸣 2/5"); }
        
        // ── 流派冲突（跨流派混搭）──
        int activeFactions = (spd > 0 ? 1 : 0) + (out_ > 0 ? 1 : 0) + (con > 0 ? 1 : 0) + (cha > 0 ? 1 : 0);
        if (activeFactions >= 2) {
            if (spd > 0 && out_ > 0) {
                result.flywheelOutputBonus = flyStacks * FlywheelOutputPerStack;
                result.flywheelSpeedBonus = flyStacks * FlywheelSpeedPerStack;
                labels.Add($"⚡📦飞轮 x{flyStacks}");
            }
            if (spd > 0 && con > 0) {
                result.remainInputBonus = Math.Min((spd + con) * 0.05f, 0.5f);
                labels.Add($"⚡💧永动 +{result.remainInputBonus * 100:F0}%");
            }
            if (out_ > 0 && con > 0) {
                result.appendRatioBonus = Math.Min(1f + (out_ + con) * 0.10f, 3f);
                labels.Add($"📦💧闭环 x{result.appendRatioBonus:F1}");
            }
            if (con > 0 && cha > 0) {
                result.extraConsumeReduce = cha * 0.05f;
                result.conflictChaosPenaltyReduction = con * 0.10f;
                labels.Add($"💧🌌熵减");
            }
        }
        
        // ── 3件套：速产协议 ⚡⚡📦 ──
        if (spd >= 2 && out_ >= 1) {
            result.outputBonus += 0.20f;
            labels.Add("⚡⚡📦速产协议 +20%");
        }
        
        // ── 连锁（固定ID配对）──
        if (ch[0]) { result.chainBonus += 0.5f; labels.Add("⚡超频共振✓"); }
        if (ch[1]) { result.chainBonus += 1.0f; labels.Add("📦量子纠缠✓"); }
        if (ch[2]) { result.chainBonus += 0.5f; labels.Add("💧永续引擎✓"); }
        if (ch[3]) { result.chainBonus += 0.5f; labels.Add("🌌混沌献祭✓"); }
        
        result.activeLabels = labels.ToArray();
        return result;
    }
    
    /// <summary>
    /// 获取羁绊预览：当前词缀 + 假如加入一张候选词缀后的变化
    /// 用于三选一UI显示
    /// </summary>
    public static (AffixBondResults current, AffixBondResults withCandidate, BondPreview[] hints)
        GetBondPreview(int planetId, int fractionatorId, FracAffixTemplate candidate = default) {
        
        var affixes = GetAffixes(planetId, fractionatorId);
        var list = affixes?.ToList() ?? new List<FracAffixInstance>();
        var tmpls = list.Select(a => FracAffixTemplates.GetById(a.AffixId)).ToArray();
        
        var current = EvaluateBonds(list, tmpls, planetId, fractionatorId);
        
        AffixBondResults withCandidate;
        BondPreview[] hints;
        
        bool hasCandidate = candidate.Id > 0 || candidate.NameKey != null;
        if (hasCandidate) {
            // 模拟加入候选词缀
            var simList = new List<FracAffixInstance>(list);
            simList.Add(new FracAffixInstance(candidate.Id, AffixRarity.Silver, 1f));
            var simTmpls = simList.Select(a => FracAffixTemplates.GetById(a.AffixId)).ToArray();
            withCandidate = EvaluateBonds(simList, simTmpls, planetId, fractionatorId);
            
            // 生成提示：什么羁绊会触发
            var hintList = new List<BondPreview>();
            CountFactions(list, tmpls, out int[] fc, out bool[] ch);
            CountFactions(simList, simTmpls, out int[] fc2, out bool[] ch2);
            
            string[] factionNames = { "⚡速度", "📦产出", "💧消耗", "🌌混沌" };
            for (int fi = 0; fi < 4; fi++) {
                if (fc2[fi] >= 2 && fc[fi] < 2)
                    hintList.Add(new BondPreview { Label = $"{factionNames[fi]}共鸣 2/5", Active = false, MissingFaction = -1 });
                if (fc2[fi] >= 3 && fc[fi] < 3)
                    hintList.Add(new BondPreview { Label = $"{factionNames[fi]}共鸣 3/5", Active = false, MissingFaction = -1 });
            }
            if (fc2[0] >= 2 && fc2[1] >= 1 && !(fc[0] >= 2 && fc[1] >= 1))
                hintList.Add(new BondPreview { Label = "⚡⚡📦速产协议", Active = false, MissingFaction = -1 });
            
            // 连锁提示
            CheckChainHint(ch, ch2, 0, "⚡超频共振", hintList);
            CheckChainHint(ch, ch2, 1, "📦量子纠缠", hintList);
            CheckChainHint(ch, ch2, 2, "💧永续引擎", hintList);
            CheckChainHint(ch, ch2, 3, "🌌混沌献祭", hintList);
            
            hints = hintList.ToArray();
        } else {
            withCandidate = current;
            hints = Array.Empty<BondPreview>();
        }
        
        return (current, withCandidate, hints);
    }
    
    private static void CheckChainHint(bool[] oldHits, bool[] newHits, int idx, string label, List<BondPreview> list) {
        if (!oldHits[idx] && newHits[idx])
            list.Add(new BondPreview { Label = $"连锁: {label}", Active = false, MissingFaction = -1 });
    }
    
    public struct FactionSummary {
        public string factionName;
        public int factionCount;
        public string resonanceLabel;
        public float resonanceValue;
    }
    
    /// <summary>获取主流派摘要 (UI显示用)</summary>
    public static FactionSummary GetFactionSummary(int planetId, int fractionatorId) {
        var affixes = GetAffixes(planetId, fractionatorId);
        if (affixes == null || affixes.Count == 0)
            return new FactionSummary { factionName = "", factionCount = 0, resonanceLabel = "", resonanceValue = 0f };
        var tmpls = affixes.Select(a => FracAffixTemplates.GetById(a.AffixId)).ToArray();
        var bonds = EvaluateBonds(affixes, tmpls, planetId, fractionatorId);
        
        var counts = new int[4];
        string[] names = { "⚡速度", "📦产出", "💧消耗", "🌌混沌" };
        foreach (var t in tmpls) counts[(int)t.Faction]++;
        int best = 0, bestIdx = 0;
        for (int i = 0; i < 4; i++) { if (counts[i] > best) { best = counts[i]; bestIdx = i; } }
        return new FactionSummary {
            factionName = names[bestIdx],
            factionCount = best,
            resonanceLabel = bonds.activeLabels.Length > 0 ? bonds.activeLabels[0] : "",
            resonanceValue = best switch { >= 5 => 5, >= 3 => 3, >= 2 => 2, _ => 0 }
        };
    }
    
    /// <summary>获取词缀的连锁提示 (UI用)</summary>
    public static string GetChainHint(int affixId, int planetId, int fractionatorId) {
        // 只有固定ID配对才算连锁
        var partnerMap = new Dictionary<int, (int pid, string tag, string label)> {
            {1, (2, "⚡","超频共振")}, {2, (1, "⚡","超频共振")},
            {10, (11, "📦","量子纠缠")}, {11, (10, "📦","量子纠缠")},
            {20, (21, "💧","永续引擎")}, {21, (20, "💧","永续引擎")},
            {30, (31, "🌌","混沌献祭")}, {31, (30, "🌌","混沌献祭")},
        };
        if (!partnerMap.TryGetValue(affixId, out var info)) return "";
        var affixes = GetAffixes(planetId, fractionatorId);
        bool hasPartner = false;
        foreach (var a in affixes) if (a.AffixId == info.pid) { hasPartner = true; break; }
        if (hasPartner) return $"{info.tag}{info.label}✓";
        var tmpl = FracAffixTemplates.GetById(info.pid);
        return $"{info.tag}缺{info.label}[{tmpl.NameKey ?? "?"}]";
    }
    
    // ── 飞轮系统 (保留，供羁绊使用) ──
    private static readonly ConcurrentDictionary<(int, int), int> _flywheelCounts = new();
    private const int FlywheelMaxStacks = 100;
    private const float FlywheelOutputPerStack = 0.005f;
    private const float FlywheelSpeedPerStack = 0.003f;
    
    public static void TickFlywheel(int planetId, int fractionatorId) {
        var key = (planetId, fractionatorId);
        _flywheelCounts.AddOrUpdate(key, 1, (_, old) => old >= FlywheelMaxStacks ? FlywheelMaxStacks : old + 1);
    }
    public static void ClearFlywheel(int planetId, int fractionatorId) {
        _flywheelCounts.TryRemove((planetId, fractionatorId), out _);
    }
    public static int GetFlywheelStacks(int planetId, int fractionatorId) {
        return _flywheelCounts.TryGetValue((planetId, fractionatorId), out int s) ? s : 0;
    }
    
    // ============================================================
    //  闪电赌局 (保留)
    // ============================================================
    private static readonly ConcurrentDictionary<(int, int), float> _lightningEndTimes = new();
    private static readonly ConcurrentDictionary<(int, int), float> _lightningCooldownEndTimes = new();
    private const float LightningDuration = 3f;
    private const float LightningCooldown = 5f;
    private const float LightningTriggerChancePerStack = 0.03f;
    private static readonly ConcurrentDictionary<(int,int), float> _powerReductionCache = new();

    public static float GetPowerReduction(int planetId, int fractionatorId) {
        return _powerReductionCache.TryGetValue((planetId, fractionatorId), out float v) ? v : 0f;
    }
    public static void SetPowerReduction(int planetId, int fractionatorId, float value) {
        if (value <= 0.001f) _powerReductionCache.TryRemove((planetId, fractionatorId), out _);
        else _powerReductionCache[(planetId, fractionatorId)] = value;
    }

    public static float GetLightningSpeedMultiplier(int planetId, int fractionatorId) {
        float now = UnityEngine.Time.time;
        if (_lightningEndTimes.TryGetValue((planetId, fractionatorId), out float end) && now < end) return 10f;
        if (_lightningCooldownEndTimes.TryGetValue((planetId, fractionatorId), out float cooldownEnd) && now < cooldownEnd) return 0.1f;
        return 1f;
    }
    public static bool IsLightningActive(int planetId, int fractionatorId) {
        float now = UnityEngine.Time.time;
        return _lightningEndTimes.TryGetValue((planetId, fractionatorId), out float end) && now < end;
    }
    public static void TriggerLightning(int planetId, int fractionatorId) {
        float now = UnityEngine.Time.time;
        _lightningEndTimes[(planetId, fractionatorId)] = now + LightningDuration;
        _lightningCooldownEndTimes[(planetId, fractionatorId)] = now + LightningDuration + LightningCooldown;
        FE.Logic.Fractionation.Presentation.SatisfactionFX.EnqueueBigText("⚡闪电赌局！速度×10！", "00BFFF", 1.5f);
    }
    public static bool TryTriggerLightning(int planetId, int fractionatorId) {
        if (GetLightningSpeedMultiplier(planetId, fractionatorId) != 1f) return false;
        if (!instanceAffixes.TryGetValue((planetId, fractionatorId), out var affixes) || affixes.Count == 0) return false;
        int speed = 0, chaos = 0;
        foreach (var a in affixes) {
            var f = FracAffixTemplates.GetById(a.AffixId).Faction;
            if (f == AffixFaction.Speed) speed++;
            else if (f == AffixFaction.Chaos) chaos++;
        }
        if (speed == 0 || chaos == 0) return false;
        float chance = (speed + chaos) * LightningTriggerChancePerStack;
        uint seed = (uint)(UnityEngine.Time.time * 10000 + planetId * 7919 + fractionatorId);
        if (FE.Utils.Utils.GetRandDouble(ref seed) < chance) { TriggerLightning(planetId, fractionatorId); return true; }
        return false;
    }
    public static void ClearLightning(int planetId, int fractionatorId) {
        _lightningEndTimes.TryRemove((planetId, fractionatorId), out _);
        _lightningCooldownEndTimes.TryRemove((planetId, fractionatorId), out _);
    }
    
    // ============================================================
    //  薛定谔 (保留)
    // ============================================================
    [ThreadStatic]
    public static float CurrentSchrödingerMultiplier = 1f;
    public static void SetSchrödingerIfActive(int planetId, int fractionatorId) {
        CurrentSchrödingerMultiplier = 1f;
        if (!instanceAffixes.TryGetValue((planetId, fractionatorId), out var affixes) || affixes.Count == 0) return;
        int output = 0, chaos = 0;
        foreach (var a in affixes) {
            var f = FracAffixTemplates.GetById(a.AffixId).Faction;
            if (f == AffixFaction.Output) output++;
            else if (f == AffixFaction.Chaos) chaos++;
        }
        if (output > 0 && chaos > 0)
            CurrentSchrödingerMultiplier = 1f + output * 0.5f + chaos * 0.3f;
    }
    public static int ApplySchrödinger(int successCount, ref uint seed) {
        if (CurrentSchrödingerMultiplier <= 1.001f) return successCount;
        if (FE.Utils.Utils.GetRandDouble(ref seed) < 0.5) {
            int multiplied = (int)(successCount * CurrentSchrödingerMultiplier);
            return multiplied > 0 ? multiplied : 1;
        }
        return 0;
    }
    
    // ============================================================
    //  Save/Load
    // ============================================================
    public static void Import(BinaryReader r) {
        try {
        instanceAffixes.Clear();
        refreshCounts.Clear();
        cometStacks.Clear();
        factionLocks.Clear();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) {
            int planetId = r.ReadInt32();
            int fractionatorId = r.ReadInt32();
            int affixCount = r.ReadInt32();
            int storedRefreshCount = r.ReadInt32();
            var list = new List<FracAffixInstance>();
            for (int j = 0; j < affixCount; j++) {
                int id = r.ReadInt32();
                int rarity = r.ReadInt32();
                float val = r.ReadSingle();
                float growthAcc = r.ReadSingle();
                int fracCount = r.ReadInt32();
                bool questDone = r.ReadBoolean();
                list.Add(new FracAffixInstance(id, (AffixRarity)rarity, val) {
                    GrowthAccum = growthAcc,
                    FractionCount = fracCount,
                    QuestComplete = questDone
                });
            }
            instanceAffixes[(planetId, fractionatorId)] = list;
            if (storedRefreshCount > 0)
                refreshCounts.TryAdd((planetId, fractionatorId), storedRefreshCount);
            try { long cs = r.ReadInt64(); if (cs > 0) cometStacks[(planetId, fractionatorId)] = cs; }
            catch (Exception ex) { LogWarning($"[Import] Comet: {ex.Message}"); }
            try { byte lb = r.ReadByte(); if (lb >= 1 && lb <= 4) factionLocks[(planetId, fractionatorId)] = (AffixFaction)(lb - 1); }
            catch (Exception ex) { LogWarning($"[Import] Faction: {ex.Message}"); }
        }
        try { int fw = r.ReadInt32(); for (int fi = 0; fi < fw; fi++) { int p=r.ReadInt32(); int f=r.ReadInt32(); int v=r.ReadInt32(); if(v>0)_flywheelCounts[(p,f)]=v; } }
        catch (Exception ex) { LogWarning($"[Import] Flywheel: {ex.Message}"); }
        try { playerTotalRefreshes = r.ReadInt32(); playerFactionSwitches = r.ReadInt32(); playerMaxCometMilestone = r.ReadInt32(); }
        catch (Exception ex) { LogWarning($"[Import] Player: {ex.Message}"); }
        try { ChaosAbyss.TotalSealBonus = r.ReadSingle(); } catch (Exception ex) { LogWarning($"[Import] Seal: {ex.Message}"); }
        } catch (Exception ex) { LogError($"[Import] Fatal: {ex.Message}"); }
    }
    
    public static void Export(BinaryWriter w) {
        w.Write(instanceAffixes.Count);
        foreach (var kv in instanceAffixes) {
            w.Write(kv.Key.planetId); w.Write(kv.Key.fractionatorId);
            w.Write(kv.Value.Count);
            w.Write(GetRefreshCount(kv.Key.planetId, kv.Key.fractionatorId));
            foreach (var affix in kv.Value) {
                w.Write(affix.AffixId); w.Write((int)affix.Rarity); w.Write(affix.Value);
                w.Write(affix.GrowthAccum); w.Write(affix.FractionCount); w.Write(affix.QuestComplete);
            }
            cometStacks.TryGetValue((kv.Key.planetId, kv.Key.fractionatorId), out long cs); w.Write(cs);
            var key = (kv.Key.planetId, kv.Key.fractionatorId);
            factionLocks.TryGetValue(key, out var lf);
            w.Write(lf != default(AffixFaction) ? (byte)((int)lf + 1) : (byte)0);
        }
        w.Write(_flywheelCounts.Count);
        foreach (var fkv in _flywheelCounts) { w.Write(fkv.Key.Item1); w.Write(fkv.Key.Item2); w.Write(fkv.Value); }
        w.Write(playerTotalRefreshes); w.Write(playerFactionSwitches); w.Write(playerMaxCometMilestone);
        w.Write(ChaosAbyss.TotalSealBonus);
    }
    
    public static void IntoOtherSave() {
        instanceAffixes.Clear(); refreshCounts.Clear(); cometStacks.Clear(); factionLocks.Clear();
    }
    
    // ============================================================
    //  Affix lifecycle
    // ============================================================
    public static IReadOnlyList<FracAffixInstance> GetAffixes(int planetId, int fractionatorId) {
        if (instanceAffixes.TryGetValue((planetId, fractionatorId), out var list)) return list;
        return Array.Empty<FracAffixInstance>();
    }
    
    public static void GenerateAffixes(int planetId, int fractionatorId, int buildingLevel, ref uint seed) {
        int count = Math.Min(buildingLevel / 2 + 2, MaxAffixCount);
        if (count < 1) count = 1;
        var list = new List<FracAffixInstance>();
        for (int i = 0; i < count; i++) {
            var tmpl = FracAffixTemplates.RollByWeight(ref seed);
            var rarity = FracAffixTemplates.RollRarity(ref seed);
            list.Add(new FracAffixInstance(tmpl.Id, rarity, FracAffixTemplates.GetValue(tmpl, rarity)));
        }
        instanceAffixes.TryAdd((planetId, fractionatorId), list);
    }
    
    public static void EnsureInitialized(int planetId, int fractionatorId) {
        if (instanceAffixes.ContainsKey((planetId, fractionatorId))) return;
        uint seed = (uint)(UnityEngine.Time.time * 10000 + planetId * 17 + fractionatorId * 31);
        GenerateAffixes(planetId, fractionatorId, 1, ref seed);
    }
    
    // ============================================================
    //  Refresh & Choice
    // ============================================================
    private static readonly ConcurrentDictionary<(int, int), int> refreshCounts = new();
    private static readonly ConcurrentDictionary<(int, int), AffixFaction> factionLocks = new();
    public const float FactionLockCostMultiplier = 2.5f;
    
    public static AffixFaction? GetLockedFaction(int planetId, int fractionatorId) {
        if (factionLocks.TryGetValue((planetId, fractionatorId), out var f)) return f;
        return null;
    }
    public static void LockFaction(int planetId, int fractionatorId, AffixFaction faction) {
        factionLocks[(planetId, fractionatorId)] = faction; OnPlayerFactionSwitch();
    }
    public static void UnlockFaction(int planetId, int fractionatorId) {
        factionLocks.TryRemove((planetId, fractionatorId), out _);
    }
    private static int GetRefreshCount(int planetId, int fractionatorId) {
        refreshCounts.TryGetValue((planetId, fractionatorId), out int c); return c;
    }
    
    public static int GetRefreshCost(int planetId, int fractionatorId) {
        var affixes = GetAffixes(planetId, fractionatorId);
        if (affixes.Count == 0) return BaseRefreshCost;
        float cost = (float)(BaseRefreshCost * Math.Pow(1.5, GetRefreshCount(planetId, fractionatorId)));
        if (factionLocks.ContainsKey((planetId, fractionatorId))) cost *= FactionLockCostMultiplier;
        return (int)cost;
    }
    
    public static List<FracAffixInstance> RollChoices(int planetId, int fractionatorId, ref uint seed) {
        var choices = new List<FracAffixInstance>();
        int rc = GetRefreshCount(planetId, fractionatorId);
        bool pityTriggered = (rc > 0 && rc % PityThreshold == 0);
        var playerLevel = GetPlayerLevel();
        var maxRarity = FracAffixProgression.GetMaxRarity(playerLevel);
        
        for (int i = 0; i < ChoiceCount; i++) {
            var lockedFaction = GetLockedFaction(planetId, fractionatorId);
            var tmpl = lockedFaction.HasValue
                ? FracAffixTemplates.RollByWeightFilteredByFaction(ref seed, playerLevel, lockedFaction.Value)
                : FracAffixTemplates.RollByWeightFiltered(ref seed, playerLevel);
            AffixRarity rarity;
            if (pityTriggered && i == 0) {
                rarity = (AffixRarity)Math.Min((int)(GetRandDouble(ref seed) < 0.3 ? AffixRarity.Orange : AffixRarity.Purple), (int)maxRarity);
            } else {
                rarity = (AffixRarity)Math.Min((int)FracAffixTemplates.RollRarity(ref seed), (int)maxRarity);
            }
            choices.Add(new FracAffixInstance(tmpl.Id, rarity, FracAffixTemplates.GetValue(tmpl, rarity)));
        }
        
        double uRoll = GetRandDouble(ref seed);
        if (uRoll < 0.02f) {
            choices[0] = new FracAffixInstance(MUTANT_AFFIX_ID, AffixRarity.Purple, 1.8f);
            FracAffixAchievementLog.CheckUnexpected(choices[0]);
        } else if (uRoll < 0.07f) {
            choices[0] = new FracAffixInstance(TWIN_AFFIX_ID, AffixRarity.Blue, 2.0f);
            FracAffixAchievementLog.CheckUnexpected(choices[0]);
        }
        return choices;
    }
    
    public static void SelfSelectAffix(int planetId, int fractionatorId, int targetId, int replaceIndex) {
        var key = (planetId, fractionatorId);
        _bonusCache.TryRemove(key, out _);
        if (!instanceAffixes.TryGetValue(key, out var list)) return;
        if (replaceIndex < 0 || replaceIndex >= list.Count) return;
        var tmpl = FracAffixTemplates.GetById(targetId);
        list[replaceIndex] = new FracAffixInstance(targetId, AffixRarity.Purple, FracAffixTemplates.GetValue(tmpl, AffixRarity.Purple));
    }
    
    public static void SelectAffix(int planetId, int fractionatorId, int choiceIndex, List<FracAffixInstance> choices) {
        if (choices == null || choiceIndex < 0 || choiceIndex >= choices.Count) return;
        var key = (planetId, fractionatorId);
        _bonusCache.TryRemove(key, out _);
        if (!instanceAffixes.TryGetValue(key, out var list)) return;
        var chosen = choices[choiceIndex];
        if (list.Count < MaxAffixCount) { list.Add(chosen); } else {
            int replaceIdx = 0;
            int lowestRarity = (int)list[0].Rarity;
            for (int i = 1; i < list.Count; i++) { int ri = (int)list[i].Rarity; if (ri < lowestRarity) { lowestRarity = ri; replaceIdx = i; } }
            list[replaceIdx] = chosen;
        }
    }
    
    public static (int refreshed, int untilPity) GetPityProgress(int planetId, int fractionatorId) {
        int rc = GetRefreshCount(planetId, fractionatorId);
        int untilPity = PityThreshold - (rc % PityThreshold);
        if (untilPity == PityThreshold) untilPity = 0;
        return (rc, untilPity);
    }
    
    public static void RefreshAffixes(int planetId, int fractionatorId, int buildingLevel, ref uint seed) {
        var key = (planetId, fractionatorId);
        instanceAffixes.TryRemove(key, out _); _bonusCache.TryRemove(key, out _);
        int newCount = GetRefreshCount(planetId, fractionatorId) + 1;
        refreshCounts.AddOrUpdate(key, newCount, (_, _) => newCount);
        GenerateAffixes(planetId, fractionatorId, buildingLevel, ref seed);
    }
    
    public static void RemoveAffixes(int planetId, int fractionatorId) {
        var key = (planetId, fractionatorId);
        instanceAffixes.TryRemove(key, out _); refreshCounts.TryRemove(key, out _);
        cometStacks.TryRemove(key, out _); burstQueue.TryRemove(key, out _); _bonusCache.TryRemove(key, out _);
        ClearFlywheel(planetId, fractionatorId); ClearLightning(planetId, fractionatorId);
        FE.Logic.Fractionation.Presentation.SatisfactionFX.BreakCombo(planetId, fractionatorId);
    }
    
    public static void SetAffixes(int planetId, int fractionatorId, List<FracAffixInstance> affixes) {
        var key = (planetId, fractionatorId);
        bool hasRare = false;
        if (affixes != null && affixes.Count > 0) {
            instanceAffixes[key] = new List<FracAffixInstance>(affixes);
            // 词缀掉落通知：稀有以上词缀触发大字提示
            foreach (var a in affixes) {
                if (a.Rarity >= AffixRarity.Purple) { hasRare = true; break; }
            }
            if (hasRare) {
                FE.Logic.Fractionation.Presentation.SatisfactionFX.EnqueueBigText(
                    $"🔮 分馏塔获得稀有词缀! ×{affixes.Count}", "A855F7", 2.5f);
            }
        } else {
            instanceAffixes.TryRemove(key, out _);
        }
        _bonusCache.TryRemove(key, out _);
    }
    
    // ============================================================
    //  融合 (保留)
    // ============================================================
    public static FracAffixInstance? FuseAffixes(FracAffixInstance a, FracAffixInstance b,
        AffixFaction targetFaction, ref uint seed, out string message) {
        message = "";
        if (a.Rarity != b.Rarity) { message = "词缀稀有度不同"; return null; }
        int cost = GetFuseCost(a.Rarity);
        if (DataCenter.DataCenterInventory.GetFragmentMinCount() < cost) { message = $"残片不足 {cost}"; return null; }
        if (!TakeItemWithTip(IFE残片, cost, out _)) { message = "扣除失败"; return null; }
        AffixRarity newRarity = a.Rarity;
        bool upgraded = false;
        if (GetRandDouble(ref seed) < 0.05 && newRarity < AffixRarity.Mythic) { newRarity++; upgraded = true; FracAffixAchievementLog.Unlock("fusion_upgrade", "融合升华", $"{a.Rarity}→{newRarity}"); }
        var available = FracAffixTemplates.GetAvailableForFaction(targetFaction);
        if (available.Count == 0) { message = "目标流派无可用词缀"; return null; }
        var tmpl = available[(int)(GetRandDouble(ref seed) * available.Count)];
        float val = FracAffixTemplates.GetValue(tmpl, newRarity);
        float bonus = (FracAffixTemplates.GetById(a.AffixId).Faction == targetFaction && FracAffixTemplates.GetById(b.AffixId).Faction == targetFaction) ? 1.1f : 1.0f;
        FracAffixDex.MarkCollected(tmpl.Id);
        message = upgraded ? $"🎉 稀有度提升！{a.Rarity}→{newRarity}" : "融合成功";
        return new FracAffixInstance(tmpl.Id, newRarity, val * bonus);
    }
    
    public static int GetFuseCost(AffixRarity rarity) => rarity switch {
        AffixRarity.Silver => 100, AffixRarity.Green => 200, AffixRarity.Blue => 400,
        AffixRarity.Purple => 800, AffixRarity.Orange => 1600, AffixRarity.Mythic => 3200, _ => 100,
    };
    
    // ============================================================
    //  Comet (保留)
    // ============================================================
    public static void TickCometStacks(int planetId, int fractionatorId) {
        var key = (planetId, fractionatorId);
        cometStacks[key] = cometStacks.TryGetValue(key, out long v) ? v + 1 : 1;
    }
    public static float GetCometBonus(int planetId, int fractionatorId) {
        if (!cometStacks.TryGetValue((planetId, fractionatorId), out long stacks)) return 0f;
        for (int i = CometMilestones.Length - 1; i >= 0; i--) {
            if (stacks >= CometMilestones[i]) return CometBonuses[i];
        }
        return 0f;
    }
    public static long GetCometStacks(int planetId, int fractionatorId) {
        cometStacks.TryGetValue((planetId, fractionatorId), out long s); return s;
    }
    
    // ============================================================
    //  Burst Queue (保留)
    // ============================================================
    public static void EnqueueBurst(int planetId, int fractionatorId, long count = 1, int itemId = 0) {
        var key = (planetId, fractionatorId);
        long effectiveMax = (long)(BurstQueueMax * CurrentResonanceBurstScale);
        if (!burstQueue.TryGetValue(key, out long current)) { burstQueue[key] = count > effectiveMax ? effectiveMax : count; return; }
        long nv = current + count;
        if (nv > effectiveMax) { long overflow = nv - effectiveMax; if (itemId > 0 && overflow > 0) DataCenterInventory.AddItemToModData(itemId, (int)overflow, 0, false, true); burstQueue[key] = effectiveMax; }
        else burstQueue[key] = nv;
    }
    public static long DequeueBurst(int planetId, int fractionatorId, long maxCount) {
        var key = (planetId, fractionatorId);
        if (!burstQueue.TryGetValue(key, out long pending)) return 0;
        long taken = pending < maxCount ? pending : maxCount;
        long remaining = pending - taken;
        if (remaining <= 0) burstQueue.TryRemove(key, out _); else burstQueue[key] = remaining;
        return taken;
    }
    public static long GetBurstQueueCount(int planetId, int fractionatorId) {
        return burstQueue.TryGetValue((planetId, fractionatorId), out long v) ? v : 0;
    }
    public static bool IsBurstQueueFull(int planetId, int fractionatorId) {
        return GetBurstQueueCount(planetId, fractionatorId) >= (long)(BurstQueueMax * CurrentResonanceBurstScale);
    }
    
    // ============================================================
    //  Player Level (保留)
    // ============================================================
    public static float GetPlayerLevelProgress() {
        var level = GetPlayerLevel();
        return FracAffixProgression.GetProgress(level, FracAffixProgression.CalculateExp(playerTotalRefreshes, playerFactionSwitches, playerMaxCometMilestone));
    }
    public static void OnPlayerRefresh() { playerTotalRefreshes++; }
    public static void OnPlayerFactionSwitch() { playerFactionSwitches++; }
    public static void OnPlayerCometProgress(int milestone) { if (milestone > playerMaxCometMilestone) playerMaxCometMilestone = milestone; }
    
    // ============================================================
    //  SetCurrentBuilding (热路径 — 使用羁绊系统)
    // ============================================================
    [ThreadStatic]
    private static (int planetId, int fractionatorId)? currentBuilding;
    private static readonly ConcurrentDictionary<(int, int), (int hash, float spd, float outB, float consR, float chaoO, float chaoP, float ampM, float chain, float comet, float rhythm)> _bonusCache = new();
    private static string _currentConflictLabel = "";
    public static string GetCurrentConflictLabel() => _currentConflictLabel;
    
    public static void SetCurrentBuilding(int planetId, int fractionatorId) {
        currentBuilding = (planetId, fractionatorId);
        EnsureInitialized(planetId, fractionatorId);
        var affixes = GetAffixes(planetId, fractionatorId);
        if (affixes == null || affixes.Count == 0) { ClearCurrentBonuses(); _bonusCache.TryRemove((planetId, fractionatorId), out _); return; }
        
        int fastHash = affixes.Count * 1000000;
        for (int hi = 0; hi < affixes.Count && hi < 5; hi++) fastHash ^= affixes[hi].AffixId * (1000000 + hi * 10000);
        var cacheKey = (planetId, fractionatorId);
        float burstThrottle = IsBurstQueueFull(planetId, fractionatorId) ? 0.5f : 1.0f;
        if (_bonusCache.TryGetValue(cacheKey, out var cached) && cached.hash == fastHash) {
            ProcessManager.CurrentAffixSuccessBonus = cached.outB * burstThrottle;
            ProcessManager.CurrentAffixCacheBonus = cached.outB * cached.ampM;
            ProcessManager.CurrentAffixEnergyReduction = cached.consR;
            ProcessManager.CurrentAffixStackBonus = cached.spd * burstThrottle * cached.rhythm * cached.comet;
            ProcessManager.CurrentAffixChaosPenalty = cached.chaoP;
            ProcessManager.CurrentAdjacencyBonus = cached.chaoO * cached.comet;
            return;
        }
        
        var tmpls = new FracAffixTemplate[affixes.Count];
        for (int ti = 0; ti < affixes.Count; ti++) tmpls[ti] = FracAffixTemplates.GetById(affixes[ti].AffixId);
        
        // ---- 基础效果累积 ----
        float speedBonus = 0f, outputBonus = 0f, consumeReduce = 0f, chaosOutput = 0f, chaosPenalty = 0f, ampMultiplier = 1.0f, _chaosStormInvert = 0f;
        for (int ai = 0; ai < affixes.Count; ai++) {
            var affix = affixes[ai]; var tmpl = tmpls[ai];
            float baseVal = affix.Value + affix.GrowthAccum;
            if (affix.QuestComplete) baseVal *= 3.5f;
            switch (tmpl.Type) {
                case AffixType.Basic: case AffixType.Growth: case AffixType.Quest:
                    switch (tmpl.Faction) {
                        case AffixFaction.Speed: speedBonus += baseVal; break;
                        case AffixFaction.Output: outputBonus += baseVal; break;
                        case AffixFaction.Consumption: consumeReduce += baseVal; break;
                        case AffixFaction.Chaos: chaosOutput += baseVal; chaosPenalty += ((tmpl.ChaosPenaltyMin + tmpl.ChaosPenaltyMax) * 0.5f) * baseVal; break;
                    } break;
                case AffixType.TradeOff:
                    switch (tmpl.Faction) {
                        case AffixFaction.Speed: speedBonus += baseVal; chaosPenalty += 0.10f; break;
                        case AffixFaction.Output: outputBonus += baseVal; chaosPenalty += 0.10f; break;
                        case AffixFaction.Chaos: chaosOutput += baseVal; break;
                    } break;
                case AffixType.Amplifier: ampMultiplier *= (1f + baseVal); break;
                case AffixType.RuleChanger: if (tmpl.Id == 49) _chaosStormInvert = affix.Value; break;
            }
        }
        
        // ---- 统一羁绊评估（替代旧的共鸣/冲突/连锁） ----
        var bonds = EvaluateBonds(affixes, tmpls, planetId, fractionatorId);
        speedBonus += bonds.speedBonus;
        outputBonus += bonds.outputBonus;
        consumeReduce += bonds.consumeReduce;
        chaosOutput += bonds.chaosOutputBoost;
        float chaosPenaltyMult = bonds.chaosImmunity ? 0f : (1f - bonds.chaosPenaltyReduce);
        CurrentResonanceBurstScale = bonds.burstQueueScale;
        
        float chainBonus = bonds.chainBonus;
        speedBonus += bonds.flywheelSpeedBonus;
        outputBonus += bonds.flywheelOutputBonus;
        consumeReduce += bonds.extraConsumeReduce;
        if (bonds.remainInputBonus > 0f) ProcessManager.CurrentAffixRemainInputBonus = bonds.remainInputBonus;
        if (bonds.appendRatioBonus > 1.001f) ProcessManager.CurrentAffixAppendRatio = bonds.appendRatioBonus;
        _currentConflictLabel = bonds.activeLabels.Length > 0 ? string.Join(" | ", bonds.activeLabels) : "";
        
        // ---- 彗星 ----
        float cometBonus = GetCometBonus(planetId, fractionatorId);
        
        // ---- 节律 ----
        float rhythmMultiplier = 1.0f;
        float now = UnityEngine.Time.time;
        for (int ri = 0; ri < affixes.Count; ri++) {
            var tmpl = tmpls[ri];
            if (tmpl.Type == AffixType.Rhythm && tmpl.RhythmPeriod > 0) {
                float phase = (now % tmpl.RhythmPeriod) / tmpl.RhythmPeriod;
                rhythmMultiplier *= phase < 0.4f ? tmpl.RhythmHighMultiplier : tmpl.RhythmLowMultiplier;
            }
        }
        
        float lightningSpdMul = GetLightningSpeedMultiplier(planetId, fractionatorId);
        bool isLightning = IsLightningActive(planetId, fractionatorId);
        
        // ---- 总合成 ----
        float finalOut = (outputBonus + chaosOutput) * rhythmMultiplier * cometBonus * burstThrottle + chainBonus;
        float finalSpd = (speedBonus + EasterEggManager.BlackHoleSpeedBonus) * rhythmMultiplier * cometBonus * burstThrottle * lightningSpdMul;
        float finalCons = consumeReduce;
        float finalChao = chaosPenalty * (chaosPenaltyMult - bonds.conflictChaosPenaltyReduction) * 0.8f;
        if (isLightning) finalChao = 0f;
        if (finalChao < 0f) finalChao = 0f;
        if (_chaosStormInvert > 0f) finalChao *= (1f - 2f * _chaosStormInvert);
        
        ProcessManager.CurrentAffixSuccessBonus = finalOut + ChaosAbyss.TotalSealBonus;
        ProcessManager.CurrentAffixCacheBonus = outputBonus * ampMultiplier;
        ProcessManager.CurrentAffixEnergyReduction = finalCons;
        SetPowerReduction(planetId, fractionatorId, finalCons);
        ProcessManager.CurrentAffixStackBonus = finalSpd;
        ProcessManager.CurrentAffixChaosPenalty = finalChao;
        ProcessManager.CurrentAdjacencyBonus = chaosOutput * cometBonus;
        
        if (FE.Logic.Fractionation.Presentation.SatisfactionFX.IsRampage) {
            ProcessManager.CurrentAffixSuccessBonus *= 5f;
            ProcessManager.CurrentAffixStackBonus *= 5f;
            ProcessManager.CurrentAffixCacheBonus *= 5f;
        }
        
        _bonusCache[cacheKey] = (fastHash, finalSpd, finalOut, finalCons, chaosOutput, finalChao, ampMultiplier, chainBonus, cometBonus, rhythmMultiplier);
    }
    
    public static void TickAffixes(int planetId, int fractionatorId) {
        if (!currentBuilding.HasValue) return;
        TickCometStacks(planetId, fractionatorId);
        TickFlywheel(planetId, fractionatorId);
        EnqueueBurst(planetId, fractionatorId, LastFractionProductCount, ProcessManager.CurrentRecipeProductId);
        var key = (planetId, fractionatorId);
        if (!instanceAffixes.TryGetValue(key, out var list)) return;
        bool dirty = false;
        var tmpls = new FracAffixTemplate[list.Count];
        for (int ti = 0; ti < list.Count; ti++) tmpls[ti] = FracAffixTemplates.GetById(list[ti].AffixId);
        for (int i = 0; i < list.Count; i++) {
            var affix = list[i]; var tmpl = tmpls[i];
            affix.FractionCount++;
            if (tmpl.Type == AffixType.Growth && tmpl.GrowthPerFraction > 0) {
                affix.GrowthAccum += tmpl.GrowthPerFraction * (affix.FractionCount < 200 ? 1.5f : 1.0f); dirty = true;
            } else if (tmpl.Type == AffixType.Quest && tmpl.QuestThreshold > 0 && affix.FractionCount >= tmpl.QuestThreshold && !affix.QuestComplete) {
                affix.QuestComplete = true; affix.GrowthAccum = 3.5f; dirty = true;
            }
            list[i] = affix;
        }
        if (dirty) instanceAffixes[key] = list;
    }
    
    public static bool CheckQuantumTunnel(int planetId, int fractionatorId) {
        foreach (var a in GetAffixes(planetId, fractionatorId))
            if (a.AffixId == 48 && a.Value > 0) {
                uint seed = (uint)(UnityEngine.Time.time * 10000 + planetId * 1001 + fractionatorId + a.AffixId);
                return GetRandDouble(ref seed) < a.Value;
            }
        return false;
    }
    
    public static bool CheckChaosStorm(int planetId, int fractionatorId) {
        foreach (var a in GetAffixes(planetId, fractionatorId))
            if (a.AffixId == 49 && a.Value > 0) {
                uint seed = (uint)(UnityEngine.Time.time * 10000 + planetId * 2003 + fractionatorId + a.AffixId);
                return GetRandDouble(ref seed) < a.Value;
            }
        return false;
    }
    
    public static void ClearCurrentBuilding() { ClearCurrentBonuses(); }
    
    private static void ClearCurrentBonuses() {
        currentBuilding = null;
        ProcessManager.CurrentAffixSuccessBonus = 0f; ProcessManager.CurrentAffixCacheBonus = 0f;
        ProcessManager.CurrentAffixEnergyReduction = 0f; ProcessManager.CurrentAffixStackBonus = 0f;
        ProcessManager.CurrentAffixChaosPenalty = 0f; ProcessManager.CurrentAdjacencyBonus = 0f;
        ProcessManager.CurrentAffixRemainInputBonus = 0f; ProcessManager.CurrentAffixAppendRatio = 1f;
        CurrentResonanceBurstScale = 1.0f; _currentConflictLabel = "";
    }
}
