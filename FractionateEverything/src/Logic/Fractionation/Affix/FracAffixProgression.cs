using System;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 玩家词缀等级进度系统
/// Kapp Ch3: 支架式教学 — 最近发展区+渐进公开
/// Kapp Ch3: SDT胜任感 — "对挑战的渴望和对精通程度的感知"
/// 经验 = 累计刷新×1 + 流派切换×2 + 彗星层数×5
/// </summary>
public static class FracAffixProgression {
    /// <summary>经验阈值: 学徒→工匠→大师→宗师→神话</summary>
    private static readonly int[] ExpThresholds = { 0, 100, 300, 600, 1000 };

    /// <summary>各等级解锁的词缀数量</summary>
    private static readonly int[] UnlockCounts = { 20, 28, 34, 37, 38 };

    /// <summary>各等级允许的最高稀有度</summary>
    private static readonly AffixRarity[] MaxRarities = {
        AffixRarity.Blue,       // 学徒 — 蓝色以下
        AffixRarity.Purple,     // 工匠 — 紫色以下
        AffixRarity.Orange,     // 大师 — 橙色以下
        AffixRarity.Orange,     // 宗师 — 橙色以下
        AffixRarity.Mythic,     // 神话 — 全开放
    };

    /// <summary>计算总经验值</summary>
    public static int CalculateExp(int refreshCount, int factionSwitchCount, int cometMilestone) {
        return refreshCount * 1 + factionSwitchCount * 2 + cometMilestone * 5;
    }

    /// <summary>根据经验值确定玩家等级</summary>
    public static PlayerAffixLevel GetLevel(int totalExp) {
        for (int i = ExpThresholds.Length - 1; i >= 0; i--) {
            if (totalExp >= ExpThresholds[i])
                return (PlayerAffixLevel)i;
        }
        return PlayerAffixLevel.Apprentice;
    }

    /// <summary>获取当前等级解锁的词缀数量</summary>
    public static int GetUnlockCount(PlayerAffixLevel level) {
        return UnlockCounts[(int)level];
    }

    /// <summary>获取当前等级允许的最高稀有度</summary>
    public static AffixRarity GetMaxRarity(PlayerAffixLevel level) {
        return MaxRarities[(int)level];
    }

    /// <summary>获取当前等级的经验进度(0.0 ~ 1.0)</summary>
    public static float GetProgress(PlayerAffixLevel level, int totalExp) {
        int idx = (int)level;
        int currentMin = ExpThresholds[idx];
        int nextMin = (idx < ExpThresholds.Length - 1) ? ExpThresholds[idx + 1] : ExpThresholds[idx] + 500;
        if (nextMin <= currentMin) return 1.0f;
        float ratio = (float)(totalExp - currentMin) / (nextMin - currentMin);
        return ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
    }

    /// <summary>获取下一等级的预览(解锁数量/最高稀有度)</summary>
    public static string GetNextLevelPreview(PlayerAffixLevel level) {
        if (level >= PlayerAffixLevel.Mythic) return "已达最高等级";
        var next = (PlayerAffixLevel)((int)level + 1);
        int newCount = GetUnlockCount(next);
        var newRarity = GetMaxRarity(next);
        return $"下一阶: {next} | 解锁{newCount}张词缀 | 稀有度上限{newRarity}";
    }
}
