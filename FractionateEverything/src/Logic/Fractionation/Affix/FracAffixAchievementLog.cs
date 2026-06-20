using System;
using System.Collections.Generic;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 成就日志 — Xbox Live风格永久型存档列表
/// Kapp Ch10: "存档列表不损伤内生动机...数字奖品可能削弱自主意识"
/// Kapp Ch10: "永久型成就(存档列表)在长时间跨度中不会减弱成就感受"
/// </summary>
public static class FracAffixAchievementLog {
    /// <summary>成就条目</summary>
    public struct AchievementEntry {
        public string Id;           // 成就唯一标识
        public string Title;        // 标题
        public string Description;  // 描述
        public DateTime UnlockedAt; // 解锁时间
    }
    
    private static readonly List<AchievementEntry> _unlocked = new();
    private static readonly HashSet<string> _unlockedIds = new(); // O(1)去重，防止热路径卡顿
    
    /// <summary>已解锁成就列表 (只读)</summary>
    public static IReadOnlyList<AchievementEntry> Unlocked => _unlocked;
    
    /// <summary>解锁成就 (O(1)重复检测)</summary>
    public static bool Unlock(string id, string title, string desc) {
        if (!_unlockedIds.Add(id)) return false; // O(1) 替代 O(n) List.Exists
        _unlocked.Add(new AchievementEntry { Id = id, Title = title, Description = desc, UnlockedAt = DateTime.Now });
        return true;
    }
    
    // ── 预定义成就 ──
    
    /// <summary>检查并解锁比较型成就</summary>
    public static void CheckPowerIndex(List<FracAffixInstance> affixes) {
        float idx = FracAffixPowerIndex.ComputeOverall(affixes);
        if (idx >= 1.5f) Unlock("power_splus", "传说词缀师", "词缀效能达到S+评级");
        else if (idx >= 1.3f) Unlock("power_s", "超凡词缀", "词缀效能达到S评级");
    }
    
    /// <summary>检查意外型成就</summary>
    public static void CheckUnexpected(FracAffixInstance affix) {
        // 突变词缀
        if (affix.AffixId == FracAffixManager.MUTANT_AFFIX_ID)
            Unlock("unexpected_mutant", "基因突变", "获得一张双流派突变词缀");
        // 双子词缀
        if (affix.AffixId == FracAffixManager.TWIN_AFFIX_ID)
            Unlock("unexpected_twin", "孪生之星", "获得一张双倍效果双子词缀");
    }
    
    /// <summary>检查混沌近失成就 (Kapp Ch10: 用近失代替惩罚)</summary>
    public static void CheckCloseCall(float penaltySaved) {
        if (penaltySaved >= 0.3f) Unlock("chaos_nearmiss", "功亏一篑", "混沌惩罚时保存了30%以上积累");
        if (penaltySaved >= 0.5f) Unlock("chaos_survivor", "混沌幸存者", "混沌爆发中存活且挽回半数损失");
    }
    
    /// <summary>检查收集成就</summary>
    public static void CheckCollection(int uniqueCount) {
        if (uniqueCount >= 10) Unlock("collect_10", "词缀收藏家", "收集10张不同词缀");
        if (uniqueCount >= 20) Unlock("collect_20", "词缀图鉴Ⅰ", "收集20张不同词缀");
        if (uniqueCount >= 30) Unlock("collect_30", "词缀图鉴Ⅱ", "收集30张不同词缀");
        if (uniqueCount >= 38) Unlock("collect_all", "全图鉴制霸", "收集全部38张词缀");
    }
}
