using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.DataCenterInventory;
using FE.Logic.Fractionation.FracRecipes.Runtime;

namespace FE.Logic.Relic;

/// <summary>
/// v3.0.0 原生遗物系统：协议完成概率发掘遗物碎片，集碎片得遗物→全局加成→RecipeModifierCache
/// 去掉行星槽位、去掉打黑雾、去掉1099→改用IFE残片(8161)
/// </summary>
public static class RelicManager {
    // ======================== 遗物模板库 ========================
    private static readonly Dictionary<int, RelicTemplate> _templates = new();
    /// <summary>所有遗物模板的只读列表</summary>
    public static List<RelicTemplate> AllTemplates => [.. _templates.Values];
    /// <summary>按ID获取模板</summary>
    public static RelicTemplate GetTemplate(int templateId)
        => _templates.TryGetValue(templateId, out var t) ? t : null;
    // 按时代划分的发掘成本（v3.0.0经济适配：残片有限，价格暴降）
    private static readonly Dictionary<RelicEra, int> _excavationCosts = new() {
        [RelicEra.Primitive] = 3,
        [RelicEra.Ancient] = 5,
        [RelicEra.Classical] = 10,
        [RelicEra.Golden] = 20,
    };

    static RelicManager() { InitTemplates(); }

    private static void InitTemplates() {
        var t = _templates;
        int pc(int era) => _excavationCosts[(RelicEra)era];
        // ── 原始时代 (Primitive, 3碎片) ──
        t[1] = new(1, "燧石核心", "远古燧石工具，能加速基础分馏", RelicEra.Primitive, RelicType.Core, pc(0), 0) { SpeedMultiplier = 1.15f };
        t[2] = new(2, "陶土导管", "陶制管道系统优化流体输送", RelicEra.Primitive, RelicType.Catalyst, pc(0), 1) { SuccessBonus = 0.05f };
        t[3] = new(3, "青铜涡盘", "青铜锻造的涡流增强装置", RelicEra.Primitive, RelicType.Glyph, pc(0), 2) { SpeedMultiplier = 1.1f, SuccessBonus = 0.03f };
        t[4] = new(4, "日晷罗盘", "利用恒星磁场提升定向精度", RelicEra.Primitive, RelicType.Lens, pc(0), 3) { SpeedMultiplier = 1.2f };

        // ── 古代 (Ancient, 5碎片) ──
        t[5] = new(5, "水晶谐振器", "水晶结构在特定频率下增强分离效率", RelicEra.Ancient, RelicType.Catalyst, pc(1), 0) { SpeedMultiplier = 1.25f };
        t[6] = new(6, "星图碎片", "残破的星图指引了更优的轨道设计", RelicEra.Ancient, RelicType.Glyph, pc(1), 1) { SuccessBonus = 0.08f };
        t[7] = new(7, "重力井核心", "微型重力场约束粒子运动轨迹", RelicEra.Ancient, RelicType.Core, pc(1), 2) { SpeedMultiplier = 1.3f, SuccessBonus = 0.05f };
        t[8] = new(8, "等离子透镜", "高温等离子体折射提升分离纯度", RelicEra.Ancient, RelicType.Lens, pc(1), 3, true) { ByproductChance = 0.15f, ByproductItemId = RelicItemIds.PlasmaByproduct };

        // ── 古典 (Classical, 10碎片) ──
        t[9] = new(9, "量子纠缠器", "量子态同步实现超距协同", RelicEra.Classical, RelicType.Core, pc(2), 0) { SpeedMultiplier = 1.4f };
        t[10] = new(10, "戴森环残片", "戴森球技术的早期原型遗骸", RelicEra.Classical, RelicType.Lens, pc(2), 1) { SpeedMultiplier = 1.5f };
        t[11] = new(11, "暗流驱动", "利用暗物质流体增强循环动力", RelicEra.Classical, RelicType.Catalyst, pc(2), 2) { SpeedMultiplier = 1.35f, SuccessBonus = 0.1f };
        t[12] = new(12, "熵减协议", "逆转局部的熵增为有序能量", RelicEra.Classical, RelicType.Glyph, pc(2), 3, true) { SuccessBonus = 0.2f, ByproductChance = 0.1f, ByproductItemId = RelicItemIds.EntropyByproduct };

        // ── 黄金 (Golden, 20碎片) ──
        t[13] = new(13, "奇点核心", "微型黑洞的稳定化能量提取", RelicEra.Golden, RelicType.Core, pc(3), 0) { SpeedMultiplier = 1.8f, SuccessBonus = 0.1f };
        t[14] = new(14, "创世透镜", "重组粒子结构的终极装置", RelicEra.Golden, RelicType.Lens, pc(3), 1) { SpeedMultiplier = 2.0f };
        t[15] = new(15, "无尽催化环", "理论上永续的催化循环", RelicEra.Golden, RelicType.Catalyst, pc(3), 2) { SpeedMultiplier = 1.6f, SuccessBonus = 0.15f, ByproductChance = 0.2f, ByproductItemId = RelicItemIds.InfiniteCatalystByproduct };
        t[16] = new(16, "终焉符文", "失落文明的最终技术结晶", RelicEra.Golden, RelicType.Glyph, pc(3), 3, true) { SpeedMultiplier = 2.2f, SuccessBonus = 0.25f };
    }

    // ======================== 运行时状态 ========================
    private static readonly List<int> _discoveredIds = new();  // 仅存ID，不再有行星槽位
    public static IReadOnlyList<int> DiscoveredIds => _discoveredIds;
    public static int TotalDiscovered => _discoveredIds.Count;
    public static bool IsDiscovered(int templateId) => _discoveredIds.Contains(templateId);

    // ======================== 遗物发掘 ========================
    /// <summary>根据协议完成数，返回当前最大可发掘的时代</summary>
    public static RelicEra MaxEraForProtocols(int completedProtocols) {
        if (completedProtocols >= 10) return RelicEra.Golden;
        if (completedProtocols >= 5)  return RelicEra.Classical;
        if (completedProtocols >= 2)  return RelicEra.Ancient;
        return RelicEra.Primitive;
    }

    /// <summary>尝试发掘一个指定遗物</summary>
    public static bool Excavate(int templateId, int completedProtocols = 999) {
        if (!_templates.TryGetValue(templateId, out var tpl)) return false;
        if (_discoveredIds.Contains(templateId)) return false;
        if ((int)tpl.Era > (int)MaxEraForProtocols(completedProtocols)) return false; // 时代未解锁
        if (TakeItemFromModData(IFE残片, tpl.ExcavationCost, out _) < tpl.ExcavationCost) return false;

        _discoveredIds.Add(templateId);
        LogInfo($"[Relic] ✅ 发掘: {tpl.Name}，消耗{tpl.ExcavationCost}残片");

        ApplyActiveBonuses();
        return true;
    }

    /// <summary>尝试发掘一个随机未发现的遗物（玩家不能指定）</summary>
    public static bool TryExcavateRandom(Random random, out int templateId, int completedProtocols = 999) {
        templateId = -1;
        RelicEra maxEra = MaxEraForProtocols(completedProtocols);
        var available = _templates.Values
            .Where(t => !_discoveredIds.Contains(t.Id))
            .Where(t => (int)t.Era <= (int)maxEra)
            .OrderByDescending(t => t.Era)
            .ThenBy(_ => random.Next())
            .ToList();
        if (available.Count == 0) return false;

        int chosenIdx = random.Next(available.Count);
        var chosen = available[chosenIdx];
        if (Excavate(chosen.Id, completedProtocols)) {
            templateId = chosen.Id;
            return true;
        }
        return false;
    }

    /// <summary>根据已完成的协议数量，免费概率发掘一次（协议完成时调用）</summary>
    public static bool TryFreeExcavation(Random random, int completedProtocols = 999) {
        if (_discoveredIds.Count >= _templates.Count) return false;
        return TryExcavateRandom(random, out _, completedProtocols);
    }

    // ======================== 共鸣系统 ========================
    private static readonly ResonanceDefinition[] _resonances = {
        new() {
            Type = ResonanceType.Binary, Name = "原始共鸣", Description = "双塔基础协同",
            RequiredRelicIds = [1, 2], SpeedBonus = 1.1f
        },
        new() {
            Type = ResonanceType.Binary, Name = "等离子共鸣", Description = "高温等离子体共振",
            RequiredRelicIds = [7, 8], SpeedBonus = 1.2f, SuccessBonus = 0.1f
        },
        new() {
            Type = ResonanceType.Triad, Name = "三角循环", Description = "三塔无限循环加速",
            RequiredRelicIds = [5, 9, 13], SpeedBonus = 1.5f, SuccessBonus = 0.15f
        },
        new() {
            Type = ResonanceType.Cross, Name = "十字星阵", Description = "四塔终极共振",
            RequiredRelicIds = [13, 14, 15, 16], SpeedBonus = 2.5f, SuccessBonus = 0.4f
        }
    };

    /// <summary>计算当前活跃的共鸣加成（基于已发现的遗物）</summary>
    private static List<ResonanceDefinition> GetActiveResonances() {
        var active = new List<ResonanceDefinition>();
        foreach (var res in _resonances) {
            if (res.RequiredRelicIds.All(id => _discoveredIds.Contains(id))) {
                active.Add(res);
            }
        }
        return active;
    }

    /// <summary>获取第一个活跃共鸣（用于UI展示）</summary>
    public static ResonanceDefinition GetActiveResonance() {
        var active = GetActiveResonances();
        return active.Count > 0 ? active[0] : null;
    }

    /// <summary>计算当前所有发现遗物+共鸣的总加成（用于UI显示）</summary>
    public static (float totalSpeedBonus, float totalSuccessBonus) GetTotalBonuses() {
        float spd = 0f, suc = 0f;
        foreach (int tid in _discoveredIds) {
            if (_templates.TryGetValue(tid, out var tpl)) {
                spd += tpl.SpeedMultiplier - 1f;
                suc += tpl.SuccessBonus;
            }
        }
        foreach (var res in GetActiveResonances()) {
            spd += res.SpeedBonus - 1f;
            suc += res.SuccessBonus;
        }
        return (spd, suc);
    }

    // ======================== 加成应用（核心：写入RecipeModifierCache） ========================
    /// <summary>将所有已发现遗物的加成应用到RecipeModifierCache</summary>
    public static void ApplyActiveBonuses() {
        var (spd, suc) = GetTotalBonuses();
        // 以all-recipe成功率加成形式注入
        // 遗物提供的 SpeedMultiplier 和 SuccessBonus 统一映射为全局成功率加成
        RecipeModifierCache.AddAllRecipeSuccessRateBonus(suc);
        LogInfo($"[Relic] 全局加成已更新: 成功率+{suc * 100:F0}% (来自{_discoveredIds.Count}/{_templates.Count}个遗物)");

        // 注册遗物副产物产出
        foreach (int tid in _discoveredIds) {
            if (_templates.TryGetValue(tid, out var tpl) && tpl.ByproductChance > 0f && tpl.ByproductItemId > 0) {
                RecipeModifierCache.AddRelicByproduct(tpl.ByproductItemId, tpl.ByproductChance);
                LogInfo($"[Relic] 副产物注册: ID={tpl.ByproductItemId} 概率={tpl.ByproductChance:F2} (来自{tpl.Name})");
            }
        }
    }

    /// <summary>初始化/加载存档后调用，恢复遗物加成到RecipeModifierCache</summary>
    public static void ReapplyBonusesFromSave() {
        // 先清空旧加成再重新计算
        // RecipeModifierCache不支持增量清除，用覆盖方式：从OngoingRecipesState重置
        ApplyActiveBonuses();
    }

    // ======================== 存档 ========================
    public static void Export(BinaryWriter w) {
        w.Write(_discoveredIds.Count);
        foreach (int id in _discoveredIds) {
            w.Write(id);
        }
    }

    public static void Import(BinaryReader r) {
        _discoveredIds.Clear();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) {
            _discoveredIds.Add(r.ReadInt32());
        }
        ReapplyBonusesFromSave();
    }

    public static void IntoOtherSave() {
        // 跨存档继承：设计上遗物不跨档继承（收集状态保留在Civilization进度中）
    }
}
