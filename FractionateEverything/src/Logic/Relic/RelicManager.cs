using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FE.Logic.DarkFog;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.DataCenterInventory;

namespace FE.Logic.Relic;

public static class RelicManager {
    // ======================== 遗物模板库 ========================
    private static readonly Dictionary<int, RelicTemplate> _templates = new();

    static RelicManager() { InitTemplates(); }

    private static void InitTemplates() {
        var t = _templates;
        // ── 原始时代 (Primitive, 发掘成本: 200 IF/碎片) ──
        t[1] = new(1, "燧石核心", "远古燧石工具，能加速基础分馏", RelicEra.Primitive, RelicType.Core, 200, 0) { SpeedMultiplier = 1.15f };
        t[2] = new(2, "陶土导管", "陶制管道系统优化流体输送", RelicEra.Primitive, RelicType.Catalyst, 200, 1) { SuccessBonus = 0.05f };
        t[3] = new(3, "青铜涡盘", "青铜锻造的涡流增强装置", RelicEra.Primitive, RelicType.Glyph, 300, 2) { SpeedMultiplier = 1.1f, SuccessBonus = 0.03f };
        t[4] = new(4, "日晷罗盘", "利用恒星磁场提升定向精度", RelicEra.Primitive, RelicType.Lens, 300, 3) { SpeedMultiplier = 1.2f };

        // ── 古代 (Ancient, 发掘成本: 500 IF/碎片) ──
        t[5] = new(5, "水晶谐振器", "水晶结构在特定频率下增强分离效率", RelicEra.Ancient, RelicType.Catalyst, 500, 0) { SpeedMultiplier = 1.25f };
        t[6] = new(6, "星图碎片", "残破的星图指引了更优的轨道设计", RelicEra.Ancient, RelicType.Glyph, 600, 1) { SuccessBonus = 0.08f };
        t[7] = new(7, "重力井核心", "微型重力场约束粒子运动轨迹", RelicEra.Ancient, RelicType.Core, 700, 2) { SpeedMultiplier = 1.3f, SuccessBonus = 0.05f };
        t[8] = new(8, "等离子透镜", "高温等离子体折射提升分离纯度", RelicEra.Ancient, RelicType.Lens, 800, 3, true) { ByproductChance = 0.15f, ByproductItemId = 1120 };

        // ── 古典 (Classical, 发掘成本: 1500 IF/碎片) ──
        t[9] = new(9, "量子纠缠器", "量子态同步实现超距协同", RelicEra.Classical, RelicType.Core, 1500, 0) { SpeedMultiplier = 1.4f };
        t[10] = new(10, "戴森环残片", "戴森球技术的早期原型遗骸", RelicEra.Classical, RelicType.Lens, 1800, 1) { SpeedMultiplier = 1.5f };
        t[11] = new(11, "暗流驱动", "利用暗物质流体增强循环动力", RelicEra.Classical, RelicType.Catalyst, 2000, 2) { SpeedMultiplier = 1.35f, SuccessBonus = 0.1f };
        t[12] = new(12, "熵减协议", "逆转局部的熵增为有序能量", RelicEra.Classical, RelicType.Glyph, 2500, 3, true) { SuccessBonus = 0.2f, ByproductChance = 0.1f, ByproductItemId = 1121 };

        // ── 黄金 (Golden, 发掘成本: 5000 IF/碎片) ──
        t[13] = new(13, "奇点核心", "微型黑洞的稳定化能量提取", RelicEra.Golden, RelicType.Core, 5000, 0) { SpeedMultiplier = 1.8f, SuccessBonus = 0.1f };
        t[14] = new(14, "创世透镜", "重组粒子结构的终极装置", RelicEra.Golden, RelicType.Lens, 6000, 1) { SpeedMultiplier = 2.0f };
        t[15] = new(15, "无尽催化环", "理论上永续的催化循环", RelicEra.Golden, RelicType.Catalyst, 7000, 2) { SpeedMultiplier = 1.6f, SuccessBonus = 0.15f, ByproductChance = 0.2f, ByproductItemId = 1122 };
        t[16] = new(16, "终焉符文", "失落文明的最终技术结晶", RelicEra.Golden, RelicType.Glyph, 10000, 3, true) { SpeedMultiplier = 2.2f, SuccessBonus = 0.25f };
    }

    // ======================== 运行时状态 ========================
    private static readonly List<RelicInstance> _discovered = new();
    public static IReadOnlyList<RelicInstance> Discovered => _discovered;
    private static readonly Dictionary<(int planetId, int slotIndex), int> _placement = new();

    // ======================== 考古发掘 ========================
    public static bool CanExcavate(int templateId) {
        if (!_templates.TryGetValue(templateId, out var tpl)) return false;
        if (_discovered.Any(d => d.TemplateId == templateId)) return false;
        return HasEnoughFragments(tpl.ExcavationCost);
    }

    public static RelicInstance Excavate(int templateId) {
        if (!_templates.TryGetValue(templateId, out var tpl)) return null;
        if (_discovered.Any(d => d.TemplateId == templateId)) return null;
        if (!TakeFragments(tpl.ExcavationCost)) return null;

        var relic = new RelicInstance(templateId);
        _discovered.Add(relic);

        if (tpl.RequiresDangerousDig) {
            LogInfo($"[Relic] ⚠️ 危险发掘 {tpl.Name}，触发黑雾高度警觉");
            DarkFogCombatManager.GetRelicCount(); // 调用引用来确保命名空间正确解析
        }

        LogInfo($"[Relic] ✅ 发掘完成: {tpl.Name}，消耗{tpl.ExcavationCost}碎片");
        return relic;
    }

    // ======================== 遗物放置 ========================
    public static bool AssignRelic(int templateId, int planetId, int slotIndex) {
        var relic = _discovered.FirstOrDefault(d => d.TemplateId == templateId);
        if (relic == null) return false;
        if (slotIndex < 0 || slotIndex > 3) return false;

        // 如果该遗物已在别处放置，解除旧放置
        if (relic.AssignedPlanet >= 0) {
            _placement.Remove((relic.AssignedPlanet, relic.AssignedSlot));
        }

        relic.AssignedPlanet = planetId;
        relic.AssignedSlot = slotIndex;
        _placement[(planetId, slotIndex)] = templateId;
        return true;
    }

    public static int GetRelicAtSlot(int planetId, int slotIndex) {
        if (_placement.TryGetValue((planetId, slotIndex), out int tid)) return tid;
        return -1;
    }

    // ======================== 共鸣检测 ========================
    public static ResonanceDefinition GetActiveResonance(int planetId) {
        var relics = new List<int>();
        for (int i = 0; i < 4; i++) {
            int tid = GetRelicAtSlot(planetId, i);
            if (tid > 0) relics.Add(tid);
        }
        return DetectResonance(relics);
    }

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

    private static ResonanceDefinition DetectResonance(List<int> relicIds) {
        foreach (var res in _resonances) {
            bool match = res.RequiredRelicIds.All(id => relicIds.Contains(id));
            if (match) return res;
        }
        return null;
    }

    // ======================== 加成计算（核心钩子） ========================
    public static (float speedMult, float successBonus) GetBonuses(int planetId) {
        float speedMult = 1f;
        float successBonus = 0f;

        for (int i = 0; i < 4; i++) {
            int tid = GetRelicAtSlot(planetId, i);
            if (_templates.TryGetValue(tid, out var tpl)) {
                speedMult += tpl.SpeedMultiplier - 1f;
                successBonus += tpl.SuccessBonus;
            }
        }

        var res = GetActiveResonance(planetId);
        if (res != null) {
            speedMult += res.SpeedBonus - 1f;
            successBonus += res.SuccessBonus;
        }

        return (speedMult, successBonus);
    }

    /// <summary>全局遗物+共鸣加成（用于ProcessManager无行星上下文时的注入）</summary>
    /// <remarks>只计算玩家已发现的遗物对应的共鸣</remarks>
    public static (float speedBonus, float successBonus) GetGlobalProcessingBonus() {
        float spd = 0f, suc = 0f;
        var discoveredIds = _discovered.Select(d => d.TemplateId).ToHashSet();
        foreach (var res in _resonances) {
            if (res.RequiredRelicIds.All(id => discoveredIds.Contains(id))) {
                spd += res.SpeedBonus;
                suc += res.SuccessBonus;
            }
        }
        return (spd, suc);
    }

    // ======================== 计算碎片 ========================
    private static bool HasEnoughFragments(int cost) {
        return GetModDataItemCount(1099) >= cost;
    }

    private static bool TakeFragments(int cost) {
        if (cost <= 0) return true;
        long taken = TakeItemFromModData(1099, cost, out _);
        return taken >= cost;
    }

    // ======================== 存档 ========================
    public static void Export(BinaryWriter w) {
        w.Write(_discovered.Count);
        foreach (var r in _discovered) {
            w.Write(r.TemplateId);
            w.Write(r.AssignedPlanet);
            w.Write(r.AssignedSlot);
        }
    }

    public static void Import(BinaryReader r) {
        _discovered.Clear();
        _placement.Clear();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) {
            var relic = new RelicInstance(r.ReadInt32()) {
                AssignedPlanet = r.ReadInt32(),
                AssignedSlot = r.ReadInt32()
            };
            _discovered.Add(relic);
            if (relic.AssignedPlanet >= 0 && relic.AssignedSlot >= 0) {
                _placement[(relic.AssignedPlanet, relic.AssignedSlot)] = relic.TemplateId;
            }
        }
    }

    public static void IntoOtherSave() {
        _discovered.Clear();
        _placement.Clear();
    }

    // ======================== 查询 ========================
    public static RelicTemplate GetTemplate(int id) {
        _templates.TryGetValue(id, out var t);
        return t;
    }

    public static RelicEra GetTemplateEra(int id) =>
        _templates.TryGetValue(id, out var t) ? t.Era : RelicEra.Primitive;

    public static ExcavationStatus GetStatus(int id) {
        if (_discovered.Any(d => d.TemplateId == id)) return ExcavationStatus.Excavated;
        if (_templates.ContainsKey(id)) return ExcavationStatus.Available;
        return ExcavationStatus.Locked;
    }

    public static IEnumerable<RelicTemplate> AllTemplates => _templates.Values;
    public static int DiscoveredCount => _discovered.Count;
    public static int TotalTemplateCount => _templates.Count;
}
