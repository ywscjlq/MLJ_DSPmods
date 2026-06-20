using System;
using System.Collections.Generic;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 流派精通追踪 — 功力导向成就
/// Kapp Ch10: "功力导向的玩家更愿意面对错误...功力导向比战绩导向更利于长期投入"
/// Kapp Ch3: SDT胜任感 — "对挑战的渴望+对精通程度的感知"
/// </summary>
public static class PlayerFactionMastery {
    // 每流派使用次数
    private static readonly Dictionary<AffixFaction, int> _factionUsage = new() {
        {AffixFaction.Speed, 0}, {AffixFaction.Output, 0}, {AffixFaction.Consumption, 0}, {AffixFaction.Chaos, 0}
    };
    
    // 每流派最大彗星层数
    private static readonly Dictionary<AffixFaction, int> _factionMaxComet = new() {
        {AffixFaction.Speed, 0}, {AffixFaction.Output, 0}, {AffixFaction.Consumption, 0}, {AffixFaction.Chaos, 0}
    };
    
    // 每流派连击计数
    private static AffixFaction? _lastFaction = null;
    private static int _consecutiveCount = 0;
    private static readonly Dictionary<AffixFaction, int> _factionMaxConsecutive = new() {
        {AffixFaction.Speed, 0}, {AffixFaction.Output, 0}, {AffixFaction.Consumption, 0}, {AffixFaction.Chaos, 0}
    };
    
    /// <summary>记录流派使用 (每次应用词缀后调用)</summary>
    public static void RecordUsage(AffixFaction faction, int cometMilestone) {
        _factionUsage[faction]++;
        if (cometMilestone > _factionMaxComet[faction])
            _factionMaxComet[faction] = cometMilestone;
        
        if (_lastFaction == faction) { _consecutiveCount++; }
        else { _consecutiveCount = 1; _lastFaction = faction; }
        if (_consecutiveCount > _factionMaxConsecutive[faction])
            _factionMaxConsecutive[faction] = _consecutiveCount;
    }
    
    /// <summary>获取流派精通评级 (Kapp Ch10: 功力导向=自我比较，非战绩导向=他人比较)</summary>
    public static string GetMasteryTier(AffixFaction faction) {
        int usage = _factionUsage[faction];
        int combo = _factionMaxConsecutive[faction];
        int comet = _factionMaxComet[faction];
        int score = usage + combo * 3 + comet * 5;
        return score switch {
            >= 200 => "流派传奇",
            >= 120 => "流派大师",
            >= 60 => "流派熟手",
            >= 20 => "流派学徒",
            _ => "初识此道"
        };
    }
    
    /// <summary>获取全流派精通摘要</summary>
    public static string GetFullSummary() {
        var parts = new List<string>();
        foreach (var f in new[] { AffixFaction.Speed, AffixFaction.Output, AffixFaction.Consumption, AffixFaction.Chaos }) {
            parts.Add($"[{f}] {GetMasteryTier(f)} ({_factionUsage[f]}次)");
        }
        return string.Join(" | ", parts);
    }
}
