using System;
using System.Collections.Generic;
using System.Linq;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 词缀效能指数 — 比较型成就
/// Kapp Ch10: "比较型成就更能利用反馈提升内生动机...与分数/排行榜挂钩"
/// 玩家的词缀效果 ÷ 同阶平均值 = 效能指数（>1.0=高于平均，<1.0=低于平均）
/// </summary>
public static class FracAffixPowerIndex {
    /// <summary>计算单个词缀实例的效能指数</summary>
    public static float Compute(AffixRarity rarity, float actualValue, int templateId) {
        var tmpl = FracAffixTemplates.GetById(templateId);
        if (tmpl.Id == 0) return 1.0f;
        float avgVal = (tmpl.BaseMinValue + tmpl.BaseMaxValue) / 2f * FracAffixTemplates.GetRarityScale(rarity);
        return avgVal > 0 ? actualValue / avgVal : 1.0f;
    }
    
    /// <summary>计算一组词缀的综合效能指数</summary>
    public static float ComputeOverall(List<FracAffixInstance> affixes) {
        if (affixes == null || affixes.Count == 0) return 1.0f;
        return affixes.Average(a => Compute(a.Rarity, a.Value, a.AffixId));
    }
    
    /// <summary>获取效能评级文本 (Kapp Ch10: 非数字，文字增强胜任感)</summary>
    public static string GetRating(float index) => index switch {
        >= 1.5f => "S+ 传说级",
        >= 1.3f => "S  超凡",
        >= 1.15f => "A  卓越",
        >= 1.0f => "B  优良",
        >= 0.85f => "C  普通",
        >= 0.7f => "D  欠佳",
        _ => "E  微弱"
    };
    
    /// <summary>生成比较型成就描述</summary>
    public static string GetComparisonDesc(List<FracAffixInstance> affixes) {
        float idx = ComputeOverall(affixes);
        int count = affixes?.Count ?? 0;
        return $"词缀效能: {idx:F2} ({GetRating(idx)}) | 词缀×{count}";
    }
}
