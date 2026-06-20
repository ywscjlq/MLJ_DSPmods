using System;
using System.Collections.Generic;
using System.Linq;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 词缀排行榜 — 社交型成就
/// Kapp Ch6: 社交者玩家 — "喜欢与他人互动和比较"
/// Kapp Ch10: 比较型成就 — "排行榜+社会认可提升驱动力"
/// 输出JSON格式供UI/文件存储
/// </summary>
public static class FracAffixLeaderboard {
    public struct LeaderboardEntry {
        public string category;
        public float score;
        public string description;
    }
    
    /// <summary>获取玩家当前所有排名数据</summary>
    public static List<LeaderboardEntry> GetPlayerRanks(List<FracAffixInstance> affixes) {
        var entries = new List<LeaderboardEntry>();
        
        // 词缀效能排名
        float powerIdx = FracAffixPowerIndex.ComputeOverall(affixes);
        entries.Add(new LeaderboardEntry {
            category = "词缀效能",
            score = powerIdx,
            description = FracAffixPowerIndex.GetRating(powerIdx)
        });
        
        // 收集完成度
        entries.Add(new LeaderboardEntry {
            category = "图鉴完成度",
            score = FracAffixDex.CompletionRate * 100f,
            description = $"{FracAffixDex.UniqueCollected}/{FracAffixDex.TotalAffixes}"
        });
        
        // 流派精通
        foreach (var f in new[] { AffixFaction.Speed, AffixFaction.Output, AffixFaction.Consumption, AffixFaction.Chaos }) {
            entries.Add(new LeaderboardEntry {
                category = $"精通-{f}",
                score = 0, // 由PlayerFactionMastery定量
                description = PlayerFactionMastery.GetMasteryTier(f)
            });
        }
        
        // 玩家等级
        var level = FracAffixManager.GetPlayerLevel();
        entries.Add(new LeaderboardEntry {
            category = "词缀师",
            score = FracAffixManager.GetPlayerLevelProgress() * 100f,
            description = $"{level} (经验{FracAffixManager.GetPlayerLevelProgress():P0})"
        });
        
        return entries;
    }
    
    /// <summary>生成排行榜JSON字符串</summary>
    public static string ToJson(List<FracAffixInstance> affixes) {
        var ranks = GetPlayerRanks(affixes);
        var items = ranks.Select(r =>
            $@"  {{""category"":""{r.category}"",""score"":{r.score:F2},""desc"":""{r.description}""}}"
        );
        return "[\n" + string.Join(",\n", items) + "\n]";
    }
}
