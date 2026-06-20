using System;
using System.Collections.Generic;
using System.IO;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 混沌深渊 — 连续混沌惩罚递增机制
/// Kapp Ch2: 兴趣曲线 — "逐渐升高的挑战保持玩家投入，巅峰后给予reward"
/// Kapp Ch3: 变比率强化 — 风险递增+随机回报保持多巴胺循环
/// </summary>
public static class ChaosAbyss {
    /// <summary>追踪每个分馏塔的连续混沌使用次数</summary>
    private static readonly Dictionary<(int, int), int> _abyssDepth = new();
    
    /// <summary>累计全局封印奖励 (读档/存档)</summary>
    public static float TotalSealBonus = 0f;
    
    /// <summary>深渊层数阈值</summary>
    private static readonly int[] DepthThresholds = { 3, 7, 13, 21 };
    
    /// <summary>记录混沌触发</summary>
    public static void RecordChaos(int planetId, int fractionatorId) {
        var key = (planetId, fractionatorId);
        if (!_abyssDepth.ContainsKey(key)) _abyssDepth[key] = 0;
        _abyssDepth[key]++;
    }
    
    /// <summary>获取当前深渊层数</summary>
    public static int GetDepth(int planetId, int fractionatorId) {
        var key = (planetId, fractionatorId);
        return _abyssDepth.TryGetValue(key, out int d) ? d : 0;
    }
    
    /// <summary>获取深渊加成倍率 (层数越高奖励越大，风险也越大)</summary>
    public static float GetDepthBonus(int planetId, int fractionatorId) {
        int depth = GetDepth(planetId, fractionatorId);
        int tier = 0;
        for (int i = 0; i < DepthThresholds.Length; i++)
            if (depth >= DepthThresholds[i]) tier = i + 1;
        // 深渊奖励: 每层额外+10%混沌产出
        return 1.0f + tier * 0.10f;
    }
    
    /// <summary>深渊风险: 每层+5%混沌惩罚</summary>
    public static float GetDepthRisk(int planetId, int fractionatorId) {
        int depth = GetDepth(planetId, fractionatorId);
        int tier = 0;
        for (int i = 0; i < DepthThresholds.Length; i++)
            if (depth >= DepthThresholds[i]) tier = i + 1;
        return 1.0f + tier * 0.05f;
    }
    
    /// <summary>获取深渊层数描述</summary>
    public static string GetDepthLabel(int depth) {
        return depth switch {
            < 3 => "浅层混沌",
            < 7 => "深渊Ⅰ — 涟漪",
            < 13 => "深渊Ⅱ — 暗涌",
            < 21 => "深渊Ⅲ — 狂潮",
            _ => "深渊Ⅳ — 无尽混沌"
        };
    }
    
    /// <summary>封印可用性检查: 深渊层数≥3时可以封印</summary>
    public static bool CanSeal(int planetId, int fractionatorId) {
        return GetDepth(planetId, fractionatorId) >= 3;
    }
    
    /// <summary>执行封印: 清空深渊层数，获得封印奖励 (累积到 TotalSealBonus)</summary>
    public static float ExecuteSeal(int planetId, int fractionatorId) {
        var key = (planetId, fractionatorId);
        int depth = GetDepth(planetId, fractionatorId);
        _abyssDepth[key] = 0;
        // 封印奖励: 层数×0.5%永久全局产出加成
        float reward = depth * 0.005f;
        TotalSealBonus += reward;
        return reward;
    }
    
    /// <summary>预览封印奖励 (不消耗)</summary>
    public static float PreviewSealReward(int planetId, int fractionatorId) {
        return GetDepth(planetId, fractionatorId) * 0.005f;
    }
    
    #region Save/Load
    public static void Export(BinaryWriter w) {
        w.Write(_abyssDepth.Count);
        foreach (var kv in _abyssDepth) {
            w.Write(kv.Key.Item1);
            w.Write(kv.Key.Item2);
            w.Write(kv.Value);
        }
        w.Write(TotalSealBonus);
    }
    
    public static void Import(BinaryReader r) {
        _abyssDepth.Clear();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) {
            int planetId = r.ReadInt32();
            int fracId = r.ReadInt32();
            int depth = r.ReadInt32();
            _abyssDepth[(planetId, fracId)] = depth;
        }
        TotalSealBonus = r.ReadSingle();
    }
    
    public static void IntoOtherSave() {
        _abyssDepth.Clear();
        TotalSealBonus = 0f;
    }
    #endregion
}
