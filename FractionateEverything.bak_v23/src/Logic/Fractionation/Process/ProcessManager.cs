using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using FE.Logic.Buildings;
using FE.Logic.Fractionation.Affix;
using FE.Logic.Fractionation.Fractionators;
using FE.Logic.Fractionation.Growth;
using FE.Logic.Fractionation.FracRecipes;
using FE.Logic.Items;
using FE.UI.MainPanel.ProgressTask;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Utils.Utils;
using static FE.Logic.Station.ProliferatorPool;

namespace FE.Logic.Fractionation.Process;

public static partial class ProcessManager {
    private delegate void FractionatorUpdateHandler(ref FractionatorComponent fractionator,
        PlanetFactory factory, float power, SignData[] signPool, int[] productRegister, int[] consumeRegister,
        ref uint result);

    private static readonly FractionatorUpdateHandler[] updateHandlersByBuildingOffset = [
        UpdateInteractionTower, UpdateMineralReplicationTower, UpdatePointAggregateTower,
        UpdateConversionTower, UpdateRectificationTower,
    ];

    public static void AddTranslations() {
        Register("交互模式", "Interaction mode"); Register("原料堆积", "Fluid overflow");
        Register("搬运模式", "Transport mode"); Register("缺少精华", "Lack of fragments", "缺少残片");
        Register("分馏永动", "Frac forever"); Register("无配方", "No recipe");
        Register("主产物", "Main product"); Register("副产物", "Append product");
        Register("流动", "Flow"); Register("损毁", "Destroy");
        Register("流动输入", "Flow input"); Register("流动输出", "Flow output");
        Register("配方强化", "Recipe enhancement"); Register("单锁", "Single Lock");
        Register("单锁产物数目", "Single-lock output count"); Register("未锁定", "Not locked");
        Register("右键设为单锁", "Right-click to lock this output"); Register("右键清除单锁", "Right-click to clear single lock");
        Register("已锁定单路产物：{0}", "Locked output: {0}"); Register("已清除单路锁定", "Single lock cleared");
        Register("锁定产物无效，已清除", "Locked output invalid, cleared");
    }

    #region Field

    public static int MaxOutputTimes = 2;
    public static readonly List<ProductOutputInfo> emptyOutputs = [];
    // ── 分馏处理核心常量 ──
    private const float MinPowerForProcessing = 0.1f;       // 低于此功率不处理
    private const float DefaultFluidInputPerCargo = 4f;      // 输入堆叠数未知时的兜底值
    private const double BaseProgressRate = 500.0 / 3.0;    // 基础进度速率 / tick
    private const double ProgressRoundUpBias = 0.75;         // 进度计算向上取整偏置
    private const int MaxProgressValue = 300000;             // 进度上限
    private const int ProgressPerBatch = 10000;              // 每批次消耗的进度
    private const float FragmentDropRate = 0.02f;            // 残片掉落概率
    // ──────────────────────────
    private const int ZeroPressureInternalStackCap = 8;
    public const byte OutputFlagMain = 1 << 0;
    public const byte OutputFlagSide = 1 << 1;
    public const byte OutputFlagFluid = 1 << 2;
    [ThreadStatic]
    public static int CurrentRecipeProductId;
    public static long totalFractionSuccesses;
    private const int FractionRateWindowSeconds = 60;
    private static readonly long[] fractionSuccessBuckets = new long[FractionRateWindowSeconds];
    private static long currentFractionRateSecond = -1;
    private static long currentFractionSuccessesPerMinute;
    public static long peakFractionSuccessesPerMinute;

    public static byte GetCurrentOutputFlags(this FractionatorComponent fractionator, PlanetFactory factory) {
        if (factory == null) return 0;
        return fractionator.GetExtraState(factory).CurrentOutputFlags;
    }

    private static void SetCurrentOutputFlags(PlanetFactory factory,
        FractionatorOutputState.FractionatorExtraState extraState, bool main, bool side, bool fluid) {
        if (factory == null) return;
        byte flags = 0;
        if (main) flags |= OutputFlagMain; if (side) flags |= OutputFlagSide; if (fluid) flags |= OutputFlagFluid;
        if (extraState.CurrentOutputFlags == flags) return;
        extraState.CurrentOutputFlags = flags;
    }

    #endregion

    #region 分馏塔处理逻辑

    public static double MaxTableMilli(int fluidInputIncAvg) {
        int avgPoint = Math.Max(0, Math.Min(fluidInputIncAvg, 10));
        double ratioAcc = Cargo.accTableMilli[avgPoint];
        double ratioInc = Cargo.incTableMilli[avgPoint] * incTableFixedRatio[avgPoint];
        return ratioAcc > ratioInc ? ratioAcc : ratioInc;
    }

    public static uint InternalUpdateWithModDispatch(ref FractionatorComponent fractionator,
        PlanetFactory factory, float power, SignData[] signPool, int[] productRegister, int[] consumeRegister) {
        long perfStart = GetFractionatorPerfTimestamp();
        int buildingID = factory.entityPool[fractionator.entityId].protoId;
        int handlerIndex = buildingID - IFE交互塔;
        if (handlerIndex >= 0 && handlerIndex < updateHandlersByBuildingOffset.Length) {
            try {
                FracAffixManager.SetCurrentBuilding(factory.planetId, fractionator.id);
                FracAffixManager.TryTriggerLightning(factory.planetId, fractionator.id);
                uint result = 0;
                updateHandlersByBuildingOffset[handlerIndex](ref fractionator, factory, power, signPool,
                    productRegister, consumeRegister, ref result);
                if (result != 0) {
                    if (productRegister[0] > 0) { CurrentRecipeProductId = productRegister[0]; FracAffixManager.LastFractionProductCount = productRegister[0]; }
                    FracAffixManager.TickAffixes(factory.planetId, fractionator.id);
                }
                FracAffixManager.ClearCurrentBuilding();
                return result;
            } finally {
                FracAffixManager.ClearCurrentBuilding();
                RecordFractionatorPerf(FractionatorPerfUpdateFe, buildingID, GetFractionatorPerfElapsed(perfStart));
            }
        }
        try { return fractionator.InternalUpdate(factory, power, signPool, productRegister, consumeRegister); }
        finally { RecordFractionatorPerf(FractionatorPerfUpdateVanilla, buildingID, GetFractionatorPerfElapsed(perfStart)); }
    }

    private static void UpdateInteractionTower(ref FractionatorComponent f, PlanetFactory factory, float power, SignData[] signPool, int[] pr, int[] cr, ref uint r) => InternalUpdate<BuildingTrainRecipe>(ref f, factory, power, signPool, pr, cr, ref r, ERecipe.BuildingTrain);
    private static void UpdateMineralReplicationTower(ref FractionatorComponent f, PlanetFactory factory, float power, SignData[] signPool, int[] pr, int[] cr, ref uint r) => InternalUpdate<MineralCopyRecipe>(ref f, factory, power, signPool, pr, cr, ref r, ERecipe.MineralCopy);
    private static void UpdatePointAggregateTower(ref FractionatorComponent f, PlanetFactory factory, float power, SignData[] signPool, int[] pr, int[] cr, ref uint r) => InternalUpdate<PointAggregateRecipe>(ref f, factory, power, signPool, pr, cr, ref r, ERecipe.PointAggregate);
    private static void UpdateConversionTower(ref FractionatorComponent f, PlanetFactory factory, float power, SignData[] signPool, int[] pr, int[] cr, ref uint r) => InternalUpdate<ConversionRecipe>(ref f, factory, power, signPool, pr, cr, ref r, ERecipe.Conversion);
    private static void UpdateRectificationTower(ref FractionatorComponent f, PlanetFactory factory, float power, SignData[] signPool, int[] pr, int[] cr, ref uint r) => InternalUpdate<RectificationRecipe>(ref f, factory, power, signPool, pr, cr, ref r, ERecipe.Rectification);

    public static void InternalUpdate<T>(ref FractionatorComponent __instance,
        PlanetFactory factory, float power, SignData[] signPool, int[] productRegister, int[] consumeRegister,
        ref uint __result, ERecipe recipeType) where T : BaseRecipe {
        long perfStageStart = GetFractionatorPerfTimestamp();
        long perfDetailStart = perfStageStart;
        int entityId = __instance.entityId;
        int buildingID = factory.entityPool[entityId].protoId;
        bool isInteractionTower = buildingID == IFE交互塔;
        bool isMineralReplicationTower = buildingID == IFE矿物复制塔;
        bool isPointAggregateTower = buildingID == IFE点数聚集塔;
        bool isConversionTower = buildingID == IFE转化塔;
        bool enableMassEnergyFission = isMineralReplicationTower && MineralReplicationTower.EnableMassEnergyFission;
        FractionatorOutputState.FractionatorExtraState extraState = __instance.GetExtraState(factory);
        List<ProductOutputInfo> products = extraState.Products;
        ProductOutputBuffer outputBuffer = extraState.ScratchOutputs;
        int fluidId = __instance.fluidId;
        BaseRecipe recipe = extraState.GetRecipe(recipeType, fluidId);
        RecordFractionatorPerfDetail(FractionatorPerfDetailPrepareStateRecipe, GetFractionatorPerfElapsed(perfDetailStart));
        perfDetailStart = GetFractionatorPerfTimestamp();
        ProductOutputInfo product0 = null;
        if (recipe == null) {
            bool needReset = !extraState.TryGetRuntimeSchema(recipeType, fluidId, null, __instance.productId, out _)
                             || products.Count > 0 || __instance.productId != fluidId || __instance.productOutputCount != 0;
            if (needReset) {
                products.Clear(); extraState.InvalidateFullProductCache();
                __instance.productId = fluidId; __instance.productOutputCount = 0; __instance.produceProb = 0.01f;
                signPool[entityId].iconId0 = 0; signPool[entityId].iconType = 0U;
            }
            if (isConversionTower) __instance.SetLockedOutput(factory, __instance.NormalizeLockedOutput(factory, __instance.GetLockedOutput(factory)));
            if (needReset) extraState.MarkRuntimeSchema(recipeType, fluidId, null, __instance.productId, null);
        } else if (!extraState.TryGetRuntimeSchema(recipeType, fluidId, recipe, __instance.productId, out product0)) {
            int expectedCount = recipe.OutputMain.Count + recipe.OutputAppend.Count;
            int firstProductId = recipe.OutputMain.Count > 0 ? recipe.OutputMain[0].OutputID : recipe.InputID;
            bool needReset = __instance.productId != firstProductId || products.Count != expectedCount || !MatchesRecipeOutputs(products, recipe);
            if (needReset) {
                products.Clear(); extraState.InvalidateFullProductCache();
                __instance.productId = firstProductId; __instance.productOutputCount = 0; __instance.produceProb = 0.01f;
                signPool[entityId].iconId0 = (uint)__instance.fluidId; signPool[entityId].iconType = 1U;
                foreach (var info in recipe.OutputMain) products.Add(new(true, info.OutputID, 0));
                foreach (var info in recipe.OutputAppend) products.Add(new(false, info.OutputID, 0));
                if (isConversionTower) __instance.SetLockedOutput(factory, __instance.NormalizeLockedOutput(factory, __instance.GetLockedOutput(factory)));
            }
            int pid = __instance.productId;
            product0 = products.Count > 0 && products[0].itemId == pid ? products[0] : FindProduct(products, pid);
            extraState.MarkRuntimeSchema(recipeType, fluidId, recipe, pid, product0);
        }
        RecordFractionatorPerfDetail(FractionatorPerfDetailPrepareSchema, GetFractionatorPerfElapsed(perfDetailStart));
        perfDetailStart = GetFractionatorPerfTimestamp();
        int product0Id = __instance.productId;
        if (product0 != null && product0.count != __instance.productOutputCount) {
            product0.count = __instance.productOutputCount; extraState.InvalidateFullProductCache();
        }
        RecordFractionatorPerfDetail(FractionatorPerfDetailPrepareProduct, GetFractionatorPerfElapsed(perfDetailStart));
        if (power < MinPowerForProcessing) { __result = 0; RecordFractionatorPerfStage(FractionatorPerfStagePrepare, GetFractionatorPerfElapsed(perfStageStart)); return; }
        perfDetailStart = GetFractionatorPerfTimestamp();
        long perfConfigStart = perfDetailStart;
        float fluidInputCountPerCargo = 1.0f;
        if (__instance.fluidInputCount == 0) __instance.fluidInputCargoCount = 0f;
        else fluidInputCountPerCargo = __instance.fluidInputCargoCount > 0.0001 ? __instance.fluidInputCount / __instance.fluidInputCargoCount : DefaultFluidInputPerCargo;
        FractionatorRuntimeConfig runtimeConfig = GetRuntimeConfig(buildingID);
        int maxStack = runtimeConfig.MaxStack;
        float plrRatio = runtimeConfig.PlrRatio;
        float buildingSuccessBoost = runtimeConfig.SuccessBoost;
        bool enableFracForever = runtimeConfig.EnableFluidEnhancement;
        int fluidInputCargoMax = BaseFracFluidInputCargoMax;
        int productOutputMax = runtimeConfig.ProductOutputMax;
        int fluidOutputMax = runtimeConfig.FluidOutputMax;
        bool moveDirectly = recipe == null || !RecipeGrowthQueries.IsUnlocked(recipe);
        RecipeGrowthContext growthContext = default;
        bool growthContextReady = false;
        bool producedMainThisTick = false, producedSideThisTick = false, producedFluidThisTick = false;
        bool hasFullProduct = extraState.HasFullProduct(productOutputMax);
        bool needRecheckFullProduct = false;
        int consumedInputThisTick = 0, successCountThisTick = 0, fragmentRewardThisTick = 0;
        List<ProductOutputInfo> productRegisterDeltas = null;
        RecordFractionatorPerfDetail(FractionatorPerfDetailPrepareConfig, GetFractionatorPerfElapsed(perfConfigStart));
        RecordFractionatorPerfStage(FractionatorPerfStagePrepare, GetFractionatorPerfElapsed(perfStageStart));
        perfStageStart = GetFractionatorPerfTimestamp(); perfDetailStart = perfStageStart;
        if (__instance.fluidInputCount > 0 && (!hasFullProduct || enableFracForever) && __instance.fluidOutputCount < fluidOutputMax) {
            __instance.progress += (int)(power * BaseProgressRate * (__instance.fluidInputCargoCount < MaxBeltSpeed ? __instance.fluidInputCargoCount : MaxBeltSpeed) * fluidInputCountPerCargo + ProgressRoundUpBias);
            if (__instance.progress > MaxProgressValue) __instance.progress = MaxProgressValue;
            if (isPointAggregateTower && PointAggregateTower.EnableVoidSpray) AddIncToItem(__instance.fluidInputCount, ref __instance.fluidInputInc);
            if (enableMassEnergyFission && __instance.fluidInputCount > 0) {
                int pointsPerItem = MineralReplicationTower.EnableZeroPressureCycle ? 40 : 25;
                int poolTarget = __instance.fluidInputCount * 15;
                int pool = __instance.GetFissionPointPool(factory);
                if (pool <= 0) {
                    int needed = poolTarget - pool;
                    int toConsume = (needed + pointsPerItem - 1) / pointsPerItem;
                    int avail = __instance.fluidInputCount;
                    int consumed = Math.Min(toConsume, avail);
                    if (consumed > 0) {
                        int avgInc = __instance.fluidInputInc > 0 && __instance.fluidInputCount > 0 ? __instance.fluidInputInc / __instance.fluidInputCount : 0;
                        __instance.fluidInputCount -= consumed; if (__instance.fluidInputCount < 0) __instance.fluidInputCount = 0;
                        __instance.fluidInputCargoCount -= (float)consumed / fluidInputCountPerCargo; if (__instance.fluidInputCargoCount < 0f) __instance.fluidInputCargoCount = 0f;
                        __instance.fluidInputInc -= avgInc * consumed; if (__instance.fluidInputInc < 0) __instance.fluidInputInc = 0;
                        pool += consumed * pointsPerItem; __instance.SetFissionPointPool(factory, pool);
                    }
                }
                if (__instance.fluidInputCount > 0) {
                    int avgInc = __instance.fluidInputInc / __instance.fluidInputCount;
                    if (avgInc < 10) {
                        int toUse = Math.Min(pool, (10 - avgInc) * __instance.fluidInputCount);
                        if (toUse > 0) { __instance.fluidInputInc += toUse; pool -= toUse; __instance.SetFissionPointPool(factory, pool); }
                    }
                }
            }
            int batchCount = Math.Min(__instance.progress / ProgressPerBatch, __instance.fluidInputCount);
            if (batchCount > 0) {
                __instance.progress -= batchCount * ProgressPerBatch;
                int fluidInputIncAvg = __instance.fluidInputInc <= 0 || __instance.fluidInputCount <= 0 ? 0 : __instance.fluidInputInc / __instance.fluidInputCount;
                if (!__instance.incUsed) __instance.incUsed = fluidInputIncAvg > 0;
                bool isForcedPassthrough = moveDirectly || (enableFracForever && hasFullProduct);
                bool quantumTunnel = !isForcedPassthrough && FracAffixManager.CheckQuantumTunnel(factory.planetId, __instance.id);
                FractionationBatchResult batchResult;
                if (isForcedPassthrough) {
                    outputBuffer.Clear();
                    batchResult = new FractionationBatchResult { InputRemoveCount = batchCount, ConsumedRegisterCount = 0, SuccessCount = 0, DestroyedCount = 0, PassThroughCount = batchCount };
                    __instance.fluidInputInc -= fluidInputIncAvg * batchCount; if (__instance.fluidInputInc < 0) __instance.fluidInputInc = 0;
                } else if (quantumTunnel) {
                    outputBuffer.Clear();
                    var mainOutput = recipe.OutputMain[0];
                    int baseCount = (int)mainOutput.OutputCount;
                    float fractional = mainOutput.OutputCount - baseCount;
                    int totalCount = BaseRecipe.RollBinomialApprox(ref __instance.seed, batchCount, fractional) + batchCount * baseCount;
                    if (totalCount > 0) { outputBuffer.Add(true, mainOutput.OutputID, totalCount); mainOutput.OutputTotalCount += totalCount; }
                    batchResult = new FractionationBatchResult { InputRemoveCount = batchCount, ConsumedRegisterCount = batchCount, SuccessCount = batchCount, DestroyedCount = 0, PassThroughCount = 0 };
                    __instance.fluidInputInc -= fluidInputIncAvg * batchCount; if (__instance.fluidInputInc < 0) __instance.fluidInputInc = 0;
                } else {
                    float pointsBonus = (float)MaxTableMilli(fluidInputIncAvg) * plrRatio;
                    float successBoost = buildingSuccessBoost + Achievements.GetSuccessRateBonus();
                    if (isConversionTower) ConversionRecipe.CurrentLockedOutputId = __instance.GetLockedOutput(factory);
                    perfDetailStart = GetFractionatorPerfTimestamp();
                    try {
                        FracAffixManager.SetSchrödingerIfActive(factory.planetId, __instance.id);
                        batchResult = recipe.GetOutputsBatchFast(ref __instance.seed, pointsBonus, successBoost, batchCount, fluidInputIncAvg, ref __instance.fluidInputInc, outputBuffer);
                    } finally { if (isConversionTower) ConversionRecipe.CurrentLockedOutputId = 0; }
                    RecordFractionatorPerfDetail(FractionatorPerfDetailProcessGetOutputs, GetFractionatorPerfElapsed(perfDetailStart));
                }
                if (isConversionTower && ConversionTower.EnableCausalTracing && batchResult.DestroyedCount > 0) {
                    int saved = BaseRecipe.RollBinomialApprox(ref __instance.seed, batchResult.DestroyedCount, 0.5f);
                    if (saved > 0) { batchResult.InputRemoveCount -= saved; batchResult.ConsumedRegisterCount -= saved; __instance.fluidInputInc += fluidInputIncAvg * saved; }
                }
                __instance.fractionSuccess = batchResult.HasOutput;
                if (batchResult.InputRemoveCount > 0) {
                    __instance.fluidInputCount -= batchResult.InputRemoveCount; if (__instance.fluidInputCount < 0) __instance.fluidInputCount = 0;
                    __instance.fluidInputCargoCount -= batchResult.InputRemoveCount / fluidInputCountPerCargo; if (__instance.fluidInputCargoCount < 0f) __instance.fluidInputCargoCount = 0f;
                }
                if (batchResult.PassThroughCount > 0) {
                    __instance.fluidOutputCount += batchResult.PassThroughCount; __instance.fluidOutputTotal += batchResult.PassThroughCount;
                    __instance.fluidOutputInc += fluidInputIncAvg * batchResult.PassThroughCount; producedFluidThisTick = true;
                }
                if (batchResult.SuccessCount > 0) {
                    perfDetailStart = GetFractionatorPerfTimestamp();
                    successCountThisTick += batchResult.SuccessCount;
                    __instance.productOutputTotal += batchResult.SuccessCount;
                    for (int i = 0; i < outputBuffer.Count; i++) {
                        var p = outputBuffer[i];
                        int itemID = p.itemId, itemCount = p.count;
                        if (p.isMainOutput) producedMainThisTick = true; else producedSideThisTick = true;
                        AddProductRegisterDelta(ref productRegisterDeltas, itemID, itemCount);
                        if (itemID == product0Id) {
                            if (product0 != null) {
                                product0.count += itemCount; __instance.productOutputCount = product0.count;
                                NotifyProductCountIncreased(extraState, product0.count, productOutputMax, ref hasFullProduct);
                            } else {
                                var fb = FindProduct(products, itemID);
                                if (fb != null) { fb.count += itemCount; NotifyProductCountIncreased(extraState, fb.count, productOutputMax, ref hasFullProduct); }
                                else { products.Add(new ProductOutputInfo(p.isMainOutput, itemID, itemCount)); NotifyProductCountIncreased(extraState, itemCount, productOutputMax, ref hasFullProduct); }
                            }
                        } else {
                            var target = FindProduct(products, itemID);
                            if (target != null) { target.count += itemCount; NotifyProductCountIncreased(extraState, target.count, productOutputMax, ref hasFullProduct); }
                            else { products.Add(new ProductOutputInfo(p.isMainOutput, itemID, itemCount)); NotifyProductCountIncreased(extraState, itemCount, productOutputMax, ref hasFullProduct); }
                        }
                    }
                    RecordFractionatorPerfDetail(FractionatorPerfDetailProcessMergeOutputs, GetFractionatorPerfElapsed(perfDetailStart));
                    fragmentRewardThisTick += BaseRecipe.RollBinomialApprox(ref __instance.seed, batchResult.SuccessCount, FragmentDropRate);
                }
                consumedInputThisTick += batchResult.ConsumedRegisterCount;
            }
        } else __instance.fractionSuccess = false;

        RecordFractionatorPerfStage(FractionatorPerfStageProcess, GetFractionatorPerfElapsed(perfStageStart));
        perfStageStart = GetFractionatorPerfTimestamp(); perfDetailStart = perfStageStart;
        FlushProcessingDeltas(recipe, buildingID, fluidId, consumedInputThisTick, successCountThisTick,
            fragmentRewardThisTick, productRegisterDeltas, productRegister, consumeRegister, ref growthContext, ref growthContextReady);
        RecordFractionatorPerfDetail(FractionatorPerfDetailFlushDeltas, GetFractionatorPerfElapsed(perfDetailStart));
        SetCurrentOutputFlags(factory, extraState, producedMainThisTick, producedSideThisTick, producedFluidThisTick);
        RecordFractionatorPerfStage(FractionatorPerfStageFlushDeltas, GetFractionatorPerfElapsed(perfStageStart));
        perfStageStart = GetFractionatorPerfTimestamp();

        // 零压循环
        if (isMineralReplicationTower && MineralReplicationTower.EnableZeroPressureCycle) {
            int zpStack = Math.Min(MineralReplicationTower.MaxStack, ZeroPressureInternalStackCap);
            int fiTarget = MaxBeltSpeed * zpStack, foTarget = 2 * zpStack;
            bool hasFoBelt = __instance.belt1 > 0 && __instance.isOutput1 || __instance.belt2 > 0 && __instance.isOutput2;
            if (!hasFoBelt) {
                int moveCount = Math.Max(0, __instance.fluidOutputCount - foTarget);
                if (moveCount > 0) {
                    int avgInc = __instance.fluidOutputCount > 0 ? __instance.fluidOutputInc / __instance.fluidOutputCount : 0;
                    __instance.fluidInputCount += moveCount; __instance.fluidInputCargoCount = Math.Min(fluidInputCargoMax, __instance.fluidInputCargoCount + (float)moveCount / fluidInputCountPerCargo);
                    __instance.fluidInputInc += avgInc * moveCount; __instance.fluidOutputCount -= moveCount; __instance.fluidOutputInc -= avgInc;
                }
            }
            if (recipe != null) {
                var mainProduct = FindProduct(products, fluidId, true);
                if (mainProduct != null && mainProduct.count > 0) {
                    int incPer = recipe.GetOutputInc(fluidId);
                    int toOut = Math.Min(mainProduct.count, Math.Max(0, foTarget - __instance.fluidOutputCount));
                    if (toOut > 0) { __instance.fluidOutputCount += toOut; __instance.fluidOutputInc += incPer * toOut; mainProduct.count -= toOut; extraState.InvalidateFullProductCache(); needRecheckFullProduct = needRecheckFullProduct || hasFullProduct && mainProduct.count < productOutputMax; if (mainProduct.itemId == product0Id) __instance.productOutputCount = mainProduct.count; }
                    if (mainProduct.count > 0) {
                        int toIn = Math.Min(mainProduct.count, Math.Max(0, fiTarget - __instance.fluidInputCount));
                        if (toIn > 0) { __instance.fluidInputCount += toIn; __instance.fluidInputCargoCount = Math.Min(fluidInputCargoMax, __instance.fluidInputCargoCount + (float)toIn / fluidInputCountPerCargo); __instance.fluidInputInc += incPer * toIn; mainProduct.count -= toIn; extraState.InvalidateFullProductCache(); needRecheckFullProduct = needRecheckFullProduct || hasFullProduct && mainProduct.count < productOutputMax; if (mainProduct.itemId == product0Id) __instance.productOutputCount = mainProduct.count; }
                    }
                }
            }
        }
        RecordFractionatorPerfStage(FractionatorPerfStageZeroPressure, GetFractionatorPerfElapsed(perfStageStart));
        perfStageStart = GetFractionatorPerfTimestamp();
        CargoTraffic cargoTraffic = factory.cargoTraffic;
        // belt1
        if (__instance.belt1 > 0) {
            if (__instance.isOutput1) ProcessBeltOutputFluid(ref __instance, __instance.belt1, buildingID, enableFracForever, maxStack, cargoTraffic, fluidInputCountPerCargo);
            else ProcessBeltInput(ref __instance, __instance.belt1, factory, cargoTraffic, fluidInputCountPerCargo, fluidInputCargoMax, MaxOutputTimes, ref fluidId, ref recipe, ref products, ref extraState, recipeType, entityId, signPool);
        }
        // belt2
        if (__instance.belt2 > 0) {
            if (__instance.isOutput2) ProcessBeltOutputFluid(ref __instance, __instance.belt2, buildingID, enableFracForever, maxStack, cargoTraffic, fluidInputCountPerCargo);
            else ProcessBeltInput(ref __instance, __instance.belt2, factory, cargoTraffic, fluidInputCountPerCargo, fluidInputCargoMax, MaxOutputTimes, ref fluidId, ref recipe, ref products, ref extraState, recipeType, entityId, signPool);
        }
        RecordFractionatorPerfStage(FractionatorPerfStageFluidBelts, GetFractionatorPerfElapsed(perfStageStart));
        perfStageStart = GetFractionatorPerfTimestamp();
        // 自动补料：输出堵了（BurstQueue有货）且输入不够 → 从数据中心拉
        if (fluidId > 0 && __instance.isWorking && __instance.fluidInputCargoCount < fluidInputCargoMax
                && FracAffixManager.GetBurstQueueCount(factory.planetId, __instance.id) > 0) {
            int needSlots = fluidInputCargoMax - (int)__instance.fluidInputCargoCount;
            int needTotal = needSlots * (int)fluidInputCountPerCargo;
            if (needTotal > 0) {
                int pulled = TakeItemFromModData(fluidId, needTotal, out int pulledInc);
                if (pulled > 0) {
                    __instance.fluidInputCount += pulled;
                    __instance.fluidInputInc += pulledInc;
                    __instance.fluidInputCargoCount += (float)pulled / fluidInputCountPerCargo;
                    if (__instance.fluidInputCargoCount > fluidInputCargoMax) __instance.fluidInputCargoCount = fluidInputCargoMax;
                }
            }
        }
        RecordFractionatorPerfStage(FractionatorPerfStageFluidBelts, GetFractionatorPerfElapsed(perfStageStart));
        perfStageStart = GetFractionatorPerfTimestamp();
        bool interactionMode = false;
        if (__instance.belt0 > 0) {
            if (__instance.isOutput0) {
                if (products.Count > 0) {
                    int productStack = maxStack;
                    int lockedOutputId = isConversionTower && ConversionTower.EnableSingleLock ? __instance.GetNormalizedLockedOutput(factory) : 0;
                    var product = SelectProductForBeltOutput(products, productStack, lockedOutputId, out bool flushNonLockedProduct);
                    if (product != null && product.count > 0) {
                        int toSend = product.count >= productStack ? productStack : product.count;
                        if (cargoTraffic.TryInsertItemAtHead(__instance.belt0, product.itemId, (byte)toSend,
                                (byte)(toSend * (recipe?.GetOutputInc(product.itemId) ?? 0)))) {
                            product.count -= toSend; if (toSend < productStack) product.count = 0;
                            extraState.InvalidateFullProductCache();
                            needRecheckFullProduct = needRecheckFullProduct || hasFullProduct && product.count < productOutputMax;
                            if (ReferenceEquals(product, product0)) __instance.productOutputCount = product.count;
                            TryOutputBurstToBelt(factory.planetId, __instance.id, __instance.belt0, cargoTraffic, productStack, recipe);
                        } else {
                            // 传送带满了 → 直接扔进 BurstQueue
                            product.count -= toSend; if (toSend < productStack) product.count = 0;
                            FracAffixManager.EnqueueBurst(factory.planetId, __instance.id, toSend, product.itemId);
                            extraState.InvalidateFullProductCache();
                            if (ReferenceEquals(product, product0)) __instance.productOutputCount = product.count;
                        }
                    }
                }
            } else if (isInteractionTower && __instance.belt1 <= 0 && __instance.belt2 <= 0 && AreAllProductsEmpty(products)) {
                interactionMode = true;
                int interactionItemId = cargoTraffic.TryPickItemAtRear(__instance.belt0, 0, ItemManager.needs, out byte stack, out byte inc);
                if (interactionItemId > 0) {
                    AddItemToModData(interactionItemId, stack, inc);
                    __instance.fluidId = interactionItemId; __instance.productId = interactionItemId;
                    __instance.produceProb = 0.01f; signPool[entityId].iconId0 = (uint)__instance.fluidId; signPool[entityId].iconType = 1U;
                }
            }
        }
        RecordFractionatorPerfStage(FractionatorPerfStageProductBelt, GetFractionatorPerfElapsed(perfStageStart));
        perfStageStart = GetFractionatorPerfTimestamp();
        if (interactionMode) __instance.isWorking = true;
        else {
            if (__instance.fluidInputCount == 0 && __instance.fluidOutputCount == 0 && AreAllProductsEmpty(products)) {
                __instance.fluidId = 0; __instance.productId = 0; products.Clear(); hasFullProduct = false;
                extraState.InvalidateFullProductCache(); signPool[entityId].iconId0 = 0; signPool[entityId].iconType = 0U;
                if (isConversionTower && !ConversionTower.EnableSingleLock) __instance.SetLockedOutput(factory, 0);
            }
            if (needRecheckFullProduct) hasFullProduct = extraState.HasFullProduct(productOutputMax, true);
            __instance.isWorking = __instance.fluidInputCount > 0 && !hasFullProduct && __instance.fluidOutputCount < fluidOutputMax && !moveDirectly;
        }
        __result = __instance.isWorking ? 1U : 0U;
        RecordFractionatorPerfStage(FractionatorPerfStageFinalize, GetFractionatorPerfElapsed(perfStageStart));
    }

    private static void AddProductRegisterDelta(ref List<ProductOutputInfo> deltas, int itemId, int count) {
        if (count <= 0) return;
        deltas ??= [];
        var d = FindProduct(deltas, itemId);
        if (d == null) deltas.Add(new ProductOutputInfo(false, itemId, count));
        else d.count += count;
    }

    private static void FlushProcessingDeltas(BaseRecipe recipe, int buildingID, int fluidId, int consumedInputCount,
        int successCount, int fragmentRewardCount, List<ProductOutputInfo> productRegisterDeltas,
        int[] productRegister, int[] consumeRegister, ref RecipeGrowthContext growthContext, ref bool growthContextReady) {
        if (consumedInputCount > 0) Interlocked.Add(ref consumeRegister[fluidId], consumedInputCount);
        if (productRegisterDeltas != null) foreach (var d in productRegisterDeltas) Interlocked.Add(ref productRegister[d.itemId], d.count);
        if (successCount > 0) { RecordFractionSuccess(successCount); BuildingGrowthService.AddBuildingExp(buildingID, successCount); }
        if (successCount > 0 && recipe != null && RecipeGrowthQueries.CanApplyProcessingProgress(recipe)) {
            if (!growthContextReady) { growthContext = RecipeGrowthManager.BuildContext(); growthContextReady = true; }
            RecipeGrowthExecutor.ApplyProcessingProgress(recipe, successCount, successCount, growthContext);
        }
        if (fragmentRewardCount > 0) AddItemToModData(IFE残片, fragmentRewardCount, 0, false);
    }

    private static void RecordFractionSuccess(int count) {
        totalFractionSuccesses += count;
        long second = GameMain.gameTick >= 0 ? GameMain.gameTick / 60L : 0L;
        AdvanceFractionRateWindow(second);
        int bucketIndex = (int)(second % FractionRateWindowSeconds);
        fractionSuccessBuckets[bucketIndex] += count;
        currentFractionSuccessesPerMinute += count;
        if (currentFractionSuccessesPerMinute > peakFractionSuccessesPerMinute) peakFractionSuccessesPerMinute = currentFractionSuccessesPerMinute;
    }

    private static void AdvanceFractionRateWindow(long second) {
        if (currentFractionRateSecond < 0) { currentFractionRateSecond = second; return; }
        if (second <= currentFractionRateSecond) return;
        long delta = second - currentFractionRateSecond;
        if (delta >= FractionRateWindowSeconds) {
            Array.Clear(fractionSuccessBuckets, 0, fractionSuccessBuckets.Length);
            currentFractionSuccessesPerMinute = 0; currentFractionRateSecond = second; return;
        }
        for (long bs = currentFractionRateSecond + 1; bs <= second; bs++) {
            int bi = (int)(bs % FractionRateWindowSeconds);
            long bv = fractionSuccessBuckets[bi];
            if (bv > 0) { currentFractionSuccessesPerMinute -= bv; if (currentFractionSuccessesPerMinute < 0) currentFractionSuccessesPerMinute = 0; fractionSuccessBuckets[bi] = 0; }
        }
        currentFractionRateSecond = second;
    }

    private static void ResetFractionRateWindow() {
        Array.Clear(fractionSuccessBuckets, 0, fractionSuccessBuckets.Length);
        currentFractionRateSecond = -1; currentFractionSuccessesPerMinute = 0;
    }

    private static ProductOutputInfo FindProduct(List<ProductOutputInfo> products, int itemId, bool mainOnly = false) {
        foreach (var p in products) { if (p.itemId != itemId) continue; if (mainOnly && !p.isMainOutput) continue; return p; }
        return null;
    }

    private static ProductOutputInfo SelectByNormalOutputPriority(ProductOutputInfo bestSide, ProductOutputInfo bestMain, int stack) {
        var p = bestSide;
        if (p == null || p.count < stack) { if (bestMain != null && (p == null || bestMain.count > p.count)) p = bestMain; }
        return p;
    }

    private static ProductOutputInfo SelectProductForBeltOutput(List<ProductOutputInfo> products, int productStack, int lockedOutputId, out bool flushNonLocked) {
        ProductOutputInfo bestSide = null, bestMain = null, bestNonSide = null, bestNonMain = null;
        foreach (var p in products) {
            if (p.count <= 0) continue;
            if (p.isMainOutput) {
                if (bestMain == null || p.count > bestMain.count) bestMain = p;
                if (lockedOutputId != 0 && p.itemId != lockedOutputId && (bestNonMain == null || p.count > bestNonMain.count)) bestNonMain = p;
            } else {
                if (bestSide == null || p.count > bestSide.count) bestSide = p;
                if (lockedOutputId != 0 && p.itemId != lockedOutputId && (bestNonSide == null || p.count > bestNonSide.count)) bestNonSide = p;
            }
        }
        var nonLocked = SelectByNormalOutputPriority(bestNonSide, bestNonMain, productStack);
        if (nonLocked != null) { flushNonLocked = true; return nonLocked; }
        flushNonLocked = false;
        return SelectByNormalOutputPriority(bestSide, bestMain, productStack);
    }

    private static bool MatchesRecipeOutputs(List<ProductOutputInfo> products, BaseRecipe recipe) {
        int expected = recipe.OutputMain.Count + recipe.OutputAppend.Count;
        if (products.Count != expected) return false;
        int idx = 0;
        for (int i = 0; i < recipe.OutputMain.Count; i++, idx++) { var p = products[idx]; if (!p.isMainOutput || p.itemId != recipe.OutputMain[i].OutputID) return false; }
        for (int i = 0; i < recipe.OutputAppend.Count; i++, idx++) { var p = products[idx]; if (p.isMainOutput || p.itemId != recipe.OutputAppend[i].OutputID) return false; }
        return true;
    }

    private static void NotifyProductCountIncreased(FractionatorOutputState.FractionatorExtraState es, int count, int max, ref bool full) {
        es.InvalidateFullProductCache();
        if (count >= max) { full = true; es.MarkFullProductCache(max); }
    }

    private static bool AreAllProductsEmpty(List<ProductOutputInfo> products) {
        foreach (var p in products) if (p.count > 0) return false;
        return true;
    }

    private static int GetFluidOutputStackToMove(FractionatorComponent f, int preferred) {
        if (f.fluidOutputCount >= preferred) return preferred;
        return f.fluidInputCount == 0 ? f.fluidOutputCount : 0;
    }

    private static int GetFluidOutputIncAvg(FractionatorComponent f, int bid, int stack) {
        if (stack <= 0 || f.fluidOutputCount <= 0) return 0;
        if (bid == IFE点数聚集塔) return f.fluidOutputInc >= 4 * stack ? 4 : 0;
        return f.fluidOutputInc / f.fluidOutputCount;
    }

    private static void RemoveFluidOutput(ref FractionatorComponent f, int stack, int incAvg) {
        f.fluidOutputCount -= stack; f.fluidOutputInc -= incAvg * stack;
        if (f.fluidOutputCount <= 0) { f.fluidOutputCount = 0; f.fluidOutputInc = 0; }
        else if (f.fluidOutputInc < 0) f.fluidOutputInc = 0;
    }

    /// <summary>统一的传送带输入处理</summary>
    private static void ProcessBeltInput(ref FractionatorComponent __instance, int beltId,
        PlanetFactory factory, CargoTraffic cargoTraffic, float fluidInputCountPerCargo,
        int fluidInputCargoMax, int maxOutputTimes, ref int fluidId, ref BaseRecipe recipe,
        ref List<ProductOutputInfo> products, ref FractionatorOutputState.FractionatorExtraState extraState,
        ERecipe recipeType, int entityId, SignData[] signPool) {
        if (beltId <= 0 || __instance.fluidInputCargoCount >= fluidInputCargoMax) return;
        if (fluidId > 0) {
            for (int i = 0; i < maxOutputTimes && __instance.fluidInputCargoCount < fluidInputCargoMax; i++) {
                if (cargoTraffic.TryPickItemAtRear(beltId, fluidId, null, out byte stack, out byte inc) > 0) {
                    __instance.fluidInputCount += stack; __instance.fluidInputInc += inc; __instance.fluidInputCargoCount++;
                } else break;
            }
        } else {
            int needId = cargoTraffic.TryPickItemAtRear(beltId, 0, null, out byte stack2, out byte inc2);
            if (needId <= 0) return;
            __instance.fluidInputCount += stack2; __instance.fluidInputInc += inc2; __instance.fluidInputCargoCount++;
            __instance.fluidId = needId; fluidId = needId;
            recipe = extraState.GetRecipe(recipeType, needId);
            if (recipe == null) { __instance.productId = needId; __instance.produceProb = 0.01f; signPool[entityId].iconId0 = 0; signPool[entityId].iconType = 0U; }
            else {
                __instance.productId = recipe.OutputMain.Count > 0 ? recipe.OutputMain[0].OutputID : recipe.InputID;
                __instance.produceProb = 0.01f; signPool[entityId].iconId0 = (uint)__instance.fluidId; signPool[entityId].iconType = 1U;
                foreach (var info in recipe.OutputMain) products.Add(new(true, info.OutputID, 0));
                foreach (var info in recipe.OutputAppend) products.Add(new(false, info.OutputID, 0));
                extraState.InvalidateFullProductCache();
            }
            for (int i = 1; i < maxOutputTimes && __instance.fluidInputCargoCount < fluidInputCargoMax; i++) {
                if (cargoTraffic.TryPickItemAtRear(beltId, needId, null, out byte stack3, out byte inc3) > 0) {
                    __instance.fluidInputCount += stack3; __instance.fluidInputInc += inc3; __instance.fluidInputCargoCount++;
                } else break;
            }
        }
    }

    /// <summary>统一的传送带输出处理(flow)</summary>
    private static void ProcessBeltOutputFluid(ref FractionatorComponent __instance, int beltId,
        int buildingID, bool enableFluidEnhancement, int maxStack, CargoTraffic cargoTraffic, float fluidInputCountPerCargo) {
        if (beltId <= 0 || __instance.fluidOutputCount <= 0) return;
        TryOutputFluidToBelt(ref __instance, buildingID, enableFluidEnhancement, maxStack, cargoTraffic, beltId, fluidInputCountPerCargo);
    }

    /// <summary>BurstQueue 优先供传送带</summary>
    private static void TryOutputBurstToBelt(int planetId, int fractionatorId, int beltId,
        CargoTraffic cargoTraffic, int productStack, BaseRecipe recipe) {
        long available = FracAffixManager.GetBurstQueueCount(planetId, fractionatorId);
        if (available <= 0) return;
        int itemId = CurrentRecipeProductId;
        if (itemId <= 0) return;
        long toMove = available < productStack ? available : productStack;
        if (cargoTraffic.TryInsertItemAtHead(beltId, itemId, (byte)toMove,
                (byte)(toMove * (recipe?.GetOutputInc(itemId) ?? 0))))
            FracAffixManager.DequeueBurst(planetId, fractionatorId, toMove);
    }

    private static void TryOutputFluidToBelt(ref FractionatorComponent fractionator, int buildingID,
        bool enableFluidEnhancement, int fluidStack, CargoTraffic cargoTraffic, int beltId, float fluidInputCountPerCargo) {
        if (beltId <= 0 || fractionator.fluidOutputCount <= 0) return;
        if (enableFluidEnhancement) {
            for (int i = 0; i < MaxOutputTimes && fractionator.fluidOutputCount > 0; i++) {
                int stack = GetFluidOutputStackToMove(fractionator, fluidStack);
                if (stack <= 0) break;
                int avgInc = GetFluidOutputIncAvg(fractionator, buildingID, stack);
                if (!cargoTraffic.TryInsertItemAtHead(beltId, fractionator.fluidId, (byte)stack, (byte)Math.Min(255, avgInc * stack))) break;
                RemoveFluidOutput(ref fractionator, stack, avgInc);
            }
            return;
        }
        var cargoPath = cargoTraffic.GetCargoPath(cargoTraffic.beltPool[beltId].segPathId);
        if (cargoPath == null) return;
        int preferredStack = Mathf.Max(1, Mathf.RoundToInt(fluidInputCountPerCargo));
        for (int i = 0; i < MaxOutputTimes && fractionator.fluidOutputCount > 0; i++) {
            int stack = GetFluidOutputStackToMove(fractionator, preferredStack);
            if (stack <= 0) break;
            int avgInc = GetFluidOutputIncAvg(fractionator, buildingID, stack);
            if (!cargoPath.TryUpdateItemAtHeadAndFillBlank(fractionator.fluidId, Mathf.CeilToInt((float)(fluidInputCountPerCargo / stack - 0.1)), (byte)stack, (byte)Math.Min(255, avgInc * stack))) break;
            RemoveFluidOutput(ref fractionator, stack, avgInc);
        }
    }

    #endregion

    #region IModCanSave

    public static void Export(BinaryWriter w) {
        w.WriteBlocks(("TotalFractionSuccesses", bw => bw.Write(totalFractionSuccesses)), ("PeakFractionSuccessesPerMinute", bw => bw.Write(peakFractionSuccessesPerMinute)));
    }

    public static void Import(BinaryReader r) {
        ResetFractionRateWindow();
        r.ReadBlocks(("TotalFractionSuccesses", br => totalFractionSuccesses = Math.Max(0, br.ReadInt64())), ("PeakFractionSuccessesPerMinute", br => peakFractionSuccessesPerMinute = Math.Max(0, br.ReadInt64())));
    }

    public static void IntoOtherSave() {
        totalFractionSuccesses = 0; peakFractionSuccessesPerMinute = 0;
        ResetFractionRateWindow(); ResetSacrificeBoostState();
    }

    #endregion
}
