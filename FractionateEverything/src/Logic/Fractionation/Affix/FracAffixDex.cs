using System;
using System.Collections.Generic;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 词缀图鉴 — 收集追踪
/// Kapp Ch6: 探险者玩家类型 — 喜欢探索游戏边界和收集稀有物品
/// Kapp Ch10: 完成型成就 — 全收集奖励
/// </summary>
public static class FracAffixDex {
    private static HashSet<int> _collected = new();
    
    public static int UniqueCollected => _collected.Count;
    public static int TotalAffixes => FracAffixTemplates.All.Count;
    public static float CompletionRate => (float)UniqueCollected / TotalAffixes;
    
    /// <summary>标记某词缀为已收集</summary>
    public static bool MarkCollected(int templateId) {
        bool isNew = _collected.Add(templateId);
        if (isNew) FracAffixAchievementLog.CheckCollection(UniqueCollected);
        return isNew;
    }
    
    /// <summary>图鉴评级</summary>
    public static string GetDexTitle() {
        float rate = CompletionRate;
        if (rate >= 1.0f) return "全图鉴制霸！";
        if (rate >= 0.8f) return "词缀猎人 ★★★★";
        if (rate >= 0.5f) return "词缀收藏家 ★★★";
        if (rate >= 0.3f) return "词缀学徒 ★★";
        return "初识词缀 ★";
    }
    
    /// <summary>全收集奖励描述</summary>
    public static string GetCompletionBonus() {
        if (CompletionRate >= 1.0f)
            return "全图鉴奖励: 所有词缀基础效果x1.2 (全局加成)";
        return string.Format("图鉴进度: {0}/{1} ({2:P0})", UniqueCollected, TotalAffixes, CompletionRate);
    }
}
