using System;
using System.Linq;
using FE.Logic.Fractionation.Fractionators;
using UnityEngine;
using static FE.Utils.Utils;

namespace FE.Logic.Fractionation.Process;

/// <summary>
/// 分馏塔运行参数、增产倍率和核心更新热路径。
/// </summary>
public static partial class ProcessManager {
    // partial 类跨文件静态字段初始化顺序不稳定，不能用另一个文件里的 handler 数组决定长度。
    private const int FractionatorBuildingTypeCount = IFE精馏塔 - IFE交互塔 + 1;
    /// <summary>原版分馏塔最大堆叠数（vanilla max stack），作为运行参数兜底倍率</summary>
    private const int VanillaFractionatorMaxStack = 12;
    /// <summary>流动输出缓存为产物输出的 1/4</summary>
    private const int FluidOutputRatioDivisor = 4;

    public static readonly int MaxLevel = 12;
    public static readonly float[] ReinforcementBonusArr = new float[MaxLevel + 1];
    public static readonly float[] ReinforcementSuccessRatioArr = new float[MaxLevel + 1];
    private static double[] incTableFixedRatio = [];
    public static int BaseFracFluidOutputMax = 20;
    public static int BaseFracProductOutputMax = 20;
    public static int BaseFracFluidInputCargoMax = 40;
    public static int MaxBeltSpeed = 30;
    [ThreadStatic]
    public static float CurrentAffixSuccessBonus = 0f;  // 词缀成功率
    [ThreadStatic]
    public static float CurrentAffixCacheBonus = 0f;     // 词缀缓存加成
    [ThreadStatic]
    public static float CurrentAffixEnergyReduction = 0f; // 词缀节能
    [ThreadStatic]
    public static float CurrentAffixStackBonus = 0f;      // 词缀堆叠加成
    [ThreadStatic]
    public static float CurrentAffixChaosPenalty = 0f;    // 词缀混沌惩罚
    [ThreadStatic]
    public static float CurrentAdjacencyBonus = 0f;       // 建筑联动加成
    [ThreadStatic]
    public static float CurrentAffixRemainInputBonus = 0f;  // 流派冲突: 永动协议
    [ThreadStatic]
    public static float CurrentAffixAppendRatio = 1f;       // 流派冲突: 闭环工艺

    /// <summary>
    /// 单次分馏更新使用的运行参数快照。
    /// </summary>
    internal struct FractionatorRuntimeConfig {
        public int MaxStack;
        public int ProductOutputMax;
        public int FluidOutputMax;
        public float PlrRatio;
        public float SuccessBoost;
        public bool EnableFluidEnhancement;
    }

    private static readonly FractionatorRuntimeConfig[] runtimeConfigsByBuildingOffset =
        new FractionatorRuntimeConfig[FractionatorBuildingTypeCount];

    // 词缀反馈缓存（按建筑类型ID存档，供 UI 线程精确读取，消除 ThreadStatic 竞态）
    private static readonly System.Collections.Generic.Dictionary<int, CachedAffixFeedback> affixFeedbackCache = new();
    internal struct CachedAffixFeedback { public float NetBoost; public float NetDelta; }

    internal static CachedAffixFeedback GetAffixFeedback(int buildingID) {
        return affixFeedbackCache.TryGetValue(buildingID, out var v) ? v : default;
    }

    static ProcessManager() {
        //强化成功率
        int index = 0;
        float ratio = 0.5f;
        for (int loopCount = 1; index < ReinforcementSuccessRatioArr.Length - 1 && ratio > 0; loopCount++) {
            for (int j = 0; j < loopCount && index < ReinforcementSuccessRatioArr.Length - 1; j++) {
                ReinforcementSuccessRatioArr[index++] = ratio;
            }
            ratio -= 0.05f;
        }
        //强化加成
        for (int i = 1; i < ReinforcementBonusArr.Length; i++) {
            ReinforcementBonusArr[i] = i < 10
                ? 0.001f * i * i + 0.019f * i
                : 0.003f * i * i - 0.019f * i + 0.18f;
        }
    }

    public static void Init() {
        //获取传送带的最大速度，以此决定循环的最大次数以及缓存区大小
        //游戏逻辑帧只有60，就算传送带再快，也只能取放一个槽位的物品，也就是最多4个，再多也取不到
        //所以下面均以60/s的传送带速率作为极限值考虑
        MaxBeltSpeed = (from item in LDB.items.dataArray
            where item.Type == EItemType.Logistics && item.prefabDesc.isBelt
            select item.prefabDesc.beltSpeed * 6).Prepend(0).Max();
        MaxBeltSpeed = Math.Min(60, MaxBeltSpeed);
        MaxOutputTimes = (int)Math.Ceiling(MaxBeltSpeed / 15.0);
        float ratio = MaxBeltSpeed / 30.0f;
        PrefabDesc desc = LDB.models.Select(M分馏塔).prefabDesc;
        BaseFracFluidInputCargoMax = (int)(desc.fracFluidInputMax * ratio);
        BaseFracProductOutputMax = (int)(desc.fracProductOutputMax * ratio * VanillaFractionatorMaxStack / FluidOutputRatioDivisor);
        BaseFracFluidOutputMax = (int)(desc.fracFluidOutputMax * ratio * VanillaFractionatorMaxStack / FluidOutputRatioDivisor);

        // 增产剂表在游戏静态数据加载后才可靠，不能放到类型静态初始化阶段读取。
        incTableFixedRatio = new double[Cargo.incTableMilli.Length];
        //增产剂的增产效果修复，因为增产点数对于增产的加成不是线性的，但对于加速的加成是线性的
        for (int i = 1; i < Cargo.incTableMilli.Length; i++) {
            incTableFixedRatio[i] = Cargo.accTableMilli[i] / Cargo.incTableMilli[i];
        }
        RefreshFractionatorRuntimeConfig();
    }

    public static void RefreshFractionatorRuntimeConfig() {
        SetRuntimeConfig(IFE交互塔, InteractionTower.MaxStack, InteractionTower.PlrRatio,
            InteractionTower.SuccessBoost, InteractionTower.EnableFluidEnhancement);
        SetRuntimeConfig(IFE矿物复制塔, MineralReplicationTower.MaxStack, MineralReplicationTower.PlrRatio,
            MineralReplicationTower.SuccessBoost, MineralReplicationTower.EnableFluidEnhancement);
        SetRuntimeConfig(IFE点数聚集塔, PointAggregateTower.MaxStack, PointAggregateTower.PlrRatio,
            PointAggregateTower.SuccessBoost, PointAggregateTower.EnableFluidEnhancement);
        SetRuntimeConfig(IFE转化塔, ConversionTower.MaxStack, ConversionTower.PlrRatio,
            ConversionTower.SuccessBoost, ConversionTower.EnableFluidEnhancement);
        SetRuntimeConfig(IFE精馏塔, RectificationTower.MaxStack, RectificationTower.PlrRatio,
            RectificationTower.SuccessBoost, RectificationTower.EnableFluidEnhancement);
    }

    private static void SetRuntimeConfig(int buildingID, int maxStack, float plrRatio, float successBoost,
        bool enableFluidEnhancement) {

        int index = buildingID - IFE交互塔;
        if (index < 0 || index >= runtimeConfigsByBuildingOffset.Length) {
            return;
        }
        runtimeConfigsByBuildingOffset[index] = new FractionatorRuntimeConfig {
            MaxStack = maxStack,
            ProductOutputMax = BaseFracProductOutputMax * maxStack,
            FluidOutputMax = BaseFracFluidOutputMax * Math.Max(1, maxStack / FluidOutputRatioDivisor),
            PlrRatio = plrRatio,
            SuccessBoost = successBoost,
            EnableFluidEnhancement = enableFluidEnhancement,
        };
    }

    internal static FractionatorRuntimeConfig GetRuntimeConfig(int buildingID) {
        int index = buildingID - IFE交互塔;
        if (index >= 0 && index < runtimeConfigsByBuildingOffset.Length) {
            FractionatorRuntimeConfig config = runtimeConfigsByBuildingOffset[index];
            if (config.MaxStack > 0) {
                config.SuccessBoost += CurrentAffixSuccessBonus - CurrentAffixChaosPenalty + CurrentAdjacencyBonus;
                config.MaxStack += (int)Math.Round(CurrentAffixStackBonus);
                config.ProductOutputMax += (int)(config.ProductOutputMax * CurrentAffixCacheBonus);
                config.FluidOutputMax += (int)(config.FluidOutputMax * CurrentAffixCacheBonus);
                config.PlrRatio *= (1f + CurrentAffixEnergyReduction);  // 节能模式：降低等效功耗
                // 为 UI 线程缓存当前词缀反馈值，消除 ThreadStatic 跨线程竞态
                float netSuccess = CurrentAffixSuccessBonus + config.SuccessBoost - CurrentAffixChaosPenalty + CurrentAdjacencyBonus;
                float cacheMult = 1f + CurrentAffixCacheBonus;
                float successMult = 1f + netSuccess;
                float stackMult = 1f + CurrentAffixStackBonus / Mathf.Max(1f, config.MaxStack);
                float netBoost = 100f * cacheMult * successMult * stackMult;
                float netDelta = ((1f + CurrentAffixCacheBonus) * (1f + CurrentAffixSuccessBonus - CurrentAffixChaosPenalty + CurrentAdjacencyBonus) * (1f + CurrentAffixStackBonus / Mathf.Max(1f, config.MaxStack)) - 1f) * 100f;
                affixFeedbackCache[buildingID] = new CachedAffixFeedback { NetBoost = netBoost, NetDelta = netDelta };
                return config;
            }
        }

        var fallback = new FractionatorRuntimeConfig {
            MaxStack = 3,
            ProductOutputMax = BaseFracProductOutputMax * VanillaFractionatorMaxStack / FluidOutputRatioDivisor,
            FluidOutputMax = BaseFracFluidOutputMax,
            PlrRatio = 1.0f,
            SuccessBoost = CurrentAffixSuccessBonus - CurrentAffixChaosPenalty,
            EnableFluidEnhancement = false,
        };
        return fallback;
    }
}
