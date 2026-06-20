using FE.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using static FE.Logic.Items.ItemManager;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Logic.DataCenter.PlayerInventoryAccess;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Logic.DataCenter.PlayerInventoryAccess;

namespace FE.Logic.DarkFog;

/// <summary>
/// 黑雾科技定义。
/// </summary>
public enum EDarkFogTechId
{
    FogFractionation1 = 0,
    FogFractionation2,
    FogLogistics,
    DataCenterExpansion,
    FogRefinement,
    FogCombatBoost,
}

/// <summary>
/// 单个黑雾科技。
/// </summary>
public sealed class DarkFogTech
{
    public EDarkFogTechId TechId;
    public string NameKey;
    public string DescKey;
    public bool Researched;
    /// <summary>消耗物品ID数组</summary>
    public int[] CostItemIds;
    /// <summary>消耗物品数量数组</summary>
    public int[] CostItemCounts;
    /// <summary>前置科技（可为 null）</summary>
    public EDarkFogTechId[] Prerequisites;
    /// <summary>黑雾任务阶段需求</summary>
    public EDarkFogCombatStage RequiredStage;
}

/// <summary>
/// 黑雾专属科技树，独立于原版科技页面。
/// </summary>
public static class DarkFogTechTree
{
    private static readonly List<DarkFogTech> allTechs = [];
    private static bool initialized;

    public static IReadOnlyList<DarkFogTech> AllTechs => allTechs;

    public static void Init()
    {
        if (initialized) return;
        allTechs.Clear();

        allTechs.Add(new DarkFogTech
        {
            TechId = EDarkFogTechId.FogFractionation1,
            NameKey = "黑雾分馏 I",
            DescKey = "黑雾材料分馏效率 +25%",
            CostItemIds = [I能量碎片],
            CostItemCounts = [200],
            Prerequisites = null,
            RequiredStage = EDarkFogCombatStage.GroundSuppression,
        });

        allTechs.Add(new DarkFogTech
        {
            TechId = EDarkFogTechId.FogFractionation2,
            NameKey = "黑雾分馏 II",
            DescKey = "黑雾材料分馏效率 +50%（累计）",
            CostItemIds = [I硅基神经元],
            CostItemCounts = [100],
            Prerequisites = [EDarkFogTechId.FogFractionation1],
            RequiredStage = EDarkFogCombatStage.GroundSuppression,
        });

        allTechs.Add(new DarkFogTech
        {
            TechId = EDarkFogTechId.FogLogistics,
            NameKey = "黑雾物流",
            DescKey = "物流交互站可以运输黑雾物品",
            CostItemIds = [I物质重组器],
            CostItemCounts = [50],
            Prerequisites = null,
            RequiredStage = EDarkFogCombatStage.StellarHunt,
        });

        allTechs.Add(new DarkFogTech
        {
            TechId = EDarkFogTechId.DataCenterExpansion,
            NameKey = "数据中心扩容",
            DescKey = "数据中心黑雾物品堆叠上限 x2",
            CostItemIds = [I负熵奇点],
            CostItemCounts = [20],
            Prerequisites = [EDarkFogTechId.FogLogistics],
            RequiredStage = EDarkFogCombatStage.StellarHunt,
        });

        allTechs.Add(new DarkFogTech
        {
            TechId = EDarkFogTechId.FogRefinement,
            NameKey = "黑雾精炼",
            DescKey = "精馏塔解锁黑雾专属蒸馏路线",
            CostItemIds = [I核心素],
            CostItemCounts = [10],
            Prerequisites = [EDarkFogTechId.FogFractionation2],
            RequiredStage = EDarkFogCombatStage.Singularity,
        });

        allTechs.Add(new DarkFogTech
        {
            TechId = EDarkFogTechId.FogCombatBoost,
            NameKey = "战斗强化",
            DescKey = "黑雾地面基地清剿效率 +50%",
            CostItemIds = [I核心素, I负熵奇点],
            CostItemCounts = [10, 10],
            Prerequisites = [EDarkFogTechId.FogRefinement],
            RequiredStage = EDarkFogCombatStage.Singularity,
        });

        initialized = true;
    }

    /// <summary>
    /// 检查科技是否可研究。
    /// </summary>
    public static bool CanResearch(EDarkFogTechId techId)
    {
        var tech = allTechs.Find(t => t.TechId == techId);
        if (tech == null || tech.Researched) return false;

        // 检查当前黑雾阶段
        if (DarkFogCombatManager.GetCurrentStage() < tech.RequiredStage)
            return false;

        // 检查前置
        if (tech.Prerequisites != null)
        {
            foreach (var prereq in tech.Prerequisites)
            {
                var p = allTechs.Find(t => t.TechId == prereq);
                if (p == null || !p.Researched) return false;
            }
        }

        // 检查消耗
        for (int i = 0; i < tech.CostItemIds.Length; i++)
        {
            if (GetItemTotalCount(tech.CostItemIds[i]) < tech.CostItemCounts[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// 研究科技。消耗物品并标记为已研究。
    /// </summary>
    public static bool Research(EDarkFogTechId techId)
    {
        if (!CanResearch(techId)) return false;
        var tech = allTechs.Find(t => t.TechId == techId);
        if (tech == null) return false;

        // 消耗物品
        for (int i = 0; i < tech.CostItemIds.Length; i++)
        {
            if (!TakeItemWithTip(tech.CostItemIds[i], tech.CostItemCounts[i], out _))
                return false;
        }

        tech.Researched = true;
        ApplyTechEffect(techId);
        return true;
    }

    /// <summary>
    /// 应用科技效果。
    /// </summary>
    private static void ApplyTechEffect(EDarkFogTechId techId)
    {
        // 标记效果由各使用方在运行时查询 DarkFogTechTree.IsResearched()
        // 这里只打标记，具体效果在各模块中使用时检查
    }

    public static bool IsResearched(EDarkFogTechId techId)
    {
        var tech = allTechs.Find(t => t.TechId == techId);
        return tech?.Researched ?? false;
    }

    /// <summary>
    /// 获取分馏效率加成倍率（黑雾分馏 I/II）。
    /// </summary>
    public static float GetDarkFogFractionationMultiplier()
    {
        float m = 1.0f;
        if (IsResearched(EDarkFogTechId.FogFractionation1)) m *= 1.25f;
        if (IsResearched(EDarkFogTechId.FogFractionation2)) m *= 1.50f;
        return m;
    }

    // ── 存档 ──

    public static void Import(BinaryReader r)
    {
        r.ReadBlocks(
            ("Techs", br =>
            {
                Init();
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var id = (EDarkFogTechId)br.ReadInt32();
                    bool researched = br.ReadBoolean();
                    var tech = allTechs.Find(t => t.TechId == id);
                    if (tech != null) tech.Researched = researched;
                }
            })
        );
    }

    public static void Export(BinaryWriter w)
    {
        w.WriteBlocks(
            ("Techs", bw =>
            {
                bw.Write(allTechs.Count);
                foreach (var t in allTechs)
                {
                    bw.Write((int)t.TechId);
                    bw.Write(t.Researched);
                }
            })
        );
    }

    public static void IntoOtherSave()
    {
        initialized = false;
        allTechs.Clear();
    }
}
