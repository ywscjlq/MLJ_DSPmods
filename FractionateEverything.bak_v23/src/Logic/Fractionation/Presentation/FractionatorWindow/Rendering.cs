using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FE.Logic.Buildings;
using FE.Logic.Fractionation.Adjacency;
using FE.Logic.Fractionation.Affix;
using FE.Logic.Fractionation.Fractionators;
using FE.Logic.Fractionation.Growth;
using FE.Logic.Fractionation.FracRecipes;
using FE.Logic.Fractionation.Process;
using UnityEngine;
using UnityEngine.UI;
using static FE.Logic.Fractionation.Process.ProcessManager;
using static FE.Logic.Fractionation.FracRecipes.RecipeManager;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.PlayerInventoryAccess;

namespace FE.Logic.Fractionation.Presentation;

/// <summary>
/// 分馏塔窗口刷新、产物渲染和比例显示逻辑。
/// </summary>
public static partial class FractionatorWindow {
    private static string _lastAffixText = null;
    // 邻接检测帧缓存 (CheckAdjacency 每帧遍历所有分馏塔，只计算结果一次)
    private static int _adjCacheFrame = -1;
    private static int _adjCacheFractionatorId = 0;
    private static AdjacencyManager.AdjacencyInfo _adjCacheResult;
    // 流派锁定: Shift+1~4锁定流派, Shift+R解锁, E切换流派
    private static string _lastFactionLockKey = null;  // 上次绘制时的lock状态, 避免每帧重复
    private static void DoModWindowUpdate(UIFractionatorWindow src) {
        if (src.fractionatorId == 0 || src.factory == null) {
            if (src.active) src._Close();
            return;
        }

        FractionatorComponent fractionator = src.factorySystem.fractionatorPool[src.fractionatorId];
        if (fractionator.id != src.fractionatorId) {
            if (src.active) src._Close();
            return;
        }
        
        // 词缀刷新闪烁恢复
        if (_affixFlashUntilFrame > 0 && Time.frameCount >= _affixFlashUntilFrame) {
            _affixText.color = _affixOriginalStateColor;
            _affixFlashUntilFrame = 0;
        }

        bool hasFluid = fractionator.fluidId > 0;

        int buildingID = src.factory.entityPool[fractionator.entityId].protoId;
        ItemProto building = LDB.items.Select(buildingID);
        if (building == null) return;

        // 标题
        int level = building.Level();
        modWindow.titleText.text = level > 0 ? $"{building.name} +{level}" : building.name;

        // 电力
        PowerConsumerComponent powerConsumer = src.powerSystem.consumerPool[fractionator.pcId];
        int networkId = powerConsumer.networkId;
        PowerNetwork powerNetwork = src.powerSystem.netPool[networkId];
        float consumerRatio = powerNetwork != null && networkId > 0 ? (float)powerNetwork.consumerRatio : 0f;
        UpdatePowerDisplay(src, powerConsumer, consumerRatio);

        // 输入侧
        if (hasFluid) {
            ItemProto needProto = LDB.items.Select(fractionator.fluidId);
            if (needProto != null) {
                modWindow.needIcon.sprite = needProto.iconSprite;
                ((Behaviour)modWindow.needIcon).enabled = true;
            }
            modWindow.needCountText.text = fractionator.fluidInputCount.ToString();
            ((Behaviour)modWindow.needCountText).enabled = true;
            modWindow.inputTitleText.text = "流动输入".Translate();
            ((Behaviour)modWindow.inputTitleText).enabled = true;
            ((Behaviour)modWindow.speedText).enabled = true;
            int inputInc = fractionator.fluidInputCount > 0 && fractionator.fluidInputInc > 0
                ? fractionator.fluidInputInc / fractionator.fluidInputCount
                : 0;
            int inputArrowLevel = Cargo.fastIncArrowTable[Math.Min(inputInc, 10)];
            for (int i = 0; i < modWindow.needIncs.Length; i++)
                ((Behaviour)modWindow.needIncs[i]).enabled = (inputArrowLevel == i + 1);
        } else {
            ((Behaviour)modWindow.needIcon).enabled = false;
            for (int i = 0; i < modWindow.needIncs.Length; i++)
                ((Behaviour)modWindow.needIncs[i]).enabled = false;
            ((Behaviour)modWindow.needCountText).enabled = false;
            ((Behaviour)modWindow.inputTitleText).enabled = false;
            ((Behaviour)modWindow.speedText).enabled = false;
        }

        // 速率文字
        double fluidInputCountPerCargo = 1.0;
        if (fractionator.fluidInputCount == 0)
            fractionator.fluidInputCargoCount = 0f;
        else
            fluidInputCountPerCargo = fractionator.fluidInputCargoCount > 1e-4
                ? fractionator.fluidInputCount / (double)fractionator.fluidInputCargoCount
                : 4.0;
        double speed = consumerRatio
                       * (fractionator.fluidInputCargoCount < MaxBeltSpeed
                           ? fractionator.fluidInputCargoCount
                           : MaxBeltSpeed)
                       * fluidInputCountPerCargo
                       * 60.0;
        if (!fractionator.isWorking) speed = 0.0;
        modWindow.speedText.text = string.Format("次分馏每分".Translate(), Math.Round(speed));

        if (modWindow.productProbText != null) {
            ((Behaviour)modWindow.productProbText).enabled = false;
            modWindow.productProbText.gameObject.SetActive(false);
        }
        if (modWindow.oriProductProbText != null) {
            ((Behaviour)modWindow.oriProductProbText).enabled = false;
            modWindow.oriProductProbText.gameObject.SetActive(false);
        }

        if (modWindow.speedArrows != null) {
            for (int i = 0; i < modWindow.speedArrows.Length; i++) {
                if (modWindow.speedArrows[i] == null) continue;
                ((Behaviour)modWindow.speedArrows[i]).enabled = false;
            }
        }

        bool workingNow = hasFluid && fractionator.isWorking;
        byte outputFlags = fractionator.GetCurrentOutputFlags(src.factory);
        bool mainLit = workingNow && (outputFlags & OutputFlagMain) != 0;
        bool sideLit = workingNow && (outputFlags & OutputFlagSide) != 0;
        bool fluidLit = workingNow && ((outputFlags & OutputFlagFluid) != 0 || (!mainLit && !sideLit));

        SetArrowGroup(_mainArrows, hasFluid, mainLit ? modWindow.marqueeOnColor : modWindow.marqueeOffColor);
        SetArrowGroup(_sideArrows, hasFluid, sideLit ? modWindow.marqueeOnColor : modWindow.marqueeOffColor);
        SetArrowGroup(_fluidArrows, hasFluid, fluidLit ? modWindow.marqueeOnColor : modWindow.marqueeOffColor);

        if (modWindow.sepLine0 != null) ((Behaviour)modWindow.sepLine0).enabled = hasFluid;
        if (modWindow.sepLine1 != null) ((Behaviour)modWindow.sepLine1).enabled = hasFluid;
        if (modWindow.remindText != null) ((Behaviour)modWindow.remindText).enabled = !hasFluid;

        // 状态文字
        UpdateModStateText(src, fractionator, building, buildingID, consumerRatio);

        // 配方和产物区
        BaseRecipe recipe = GetRecipeForBuilding(buildingID, fractionator.fluidId);
        int lockedOutputId = buildingID == IFE转化塔
            ? fractionator.GetNormalizedLockedOutput(src.factory)
            : 0;

        float successBoost = building.SuccessBoost();
        int avgInc = fractionator.fluidInputCount > 0 ? fractionator.fluidInputInc / fractionator.fluidInputCount : 0;
        float pointsBonus = (float)MaxTableMilli(avgInc);

        float recipeSuccessRatio = 0f, mainOutputBonus = 1f, destroyRatio = 0f;
        if (recipe != null && RecipeGrowthQueries.IsUnlocked(recipe)) {
            recipeSuccessRatio = recipe.SuccessRatio * (1 + successBoost) * (1 + pointsBonus);
            mainOutputBonus = 1 + recipe.DoubleOutputRatio;
            destroyRatio = recipe.DestroyRatio;
        }

        UpdateUIElements(src, fractionator, recipe, recipeSuccessRatio, mainOutputBonus, destroyRatio, hasFluid,
            lockedOutputId);
        
        // 词缀显示 — 独立文本区, 不污染stateText
        _affixText.gameObject.SetActive(true);
        _affixText.color = _affixOriginalStateColor;
        // 建筑联动检测 (帧缓存: 同一帧内不重复计算)
        if (Time.frameCount != _adjCacheFrame || src.fractionatorId != _adjCacheFractionatorId) {
            _adjCacheResult = AdjacencyManager.CheckAdjacency(src.factory, src.fractionatorId);
            _adjCacheFrame = Time.frameCount;
            _adjCacheFractionatorId = src.fractionatorId;
        }
        CurrentAdjacencyBonus = _adjCacheResult.CalculateBonus();
        UpdateAffixDisplay(src, fractionator, building, buildingID, _adjCacheResult);
        

        
        // 词缀刷新处理
        if (_pendingAffixRefresh) {
            _pendingAffixRefresh = false;
            int cost = FracAffixManager.GetRefreshCost(src.factory.planetId, fractionator.id);
            if (TakeItemWithTip(IFE残片, cost, out _)) {
                uint seed = (uint)(src.factory.planetId * 7 + fractionator.id * 13 + DateTime.Now.Ticks);
                FracAffixManager.RefreshAffixes(src.factory.planetId, fractionator.id, 1, ref seed);
                // 立即重显示新词缀
                _affixText.text = "";
                UpdateAffixDisplay(src, fractionator, building, buildingID, _adjCacheResult);
                // 刷新闪烁反馈
                _affixOriginalStateColor = _affixText.color;
                _affixText.color = Color.yellow;
                _affixFlashUntilFrame = Time.frameCount + 4;
            } else {
                _affixText.text += "\n[!] 残片不足！需要 " + cost + " 残片";
            }
        }
    }

    private static void UpdateModStateText(UIFractionatorWindow src,
        FractionatorComponent fractionator, ItemProto building, int buildingID, float consumerRatio) {

        int fluidOutputMax = building.FluidOutputMax();
        int productOutputMax = building.ProductOutputMax();
        List<ProductOutputInfo> products = fractionator.products(src.factory);

        if (fractionator.isWorking) {
            if (buildingID == IFE交互塔
                && fractionator.belt0 > 0
                && fractionator.belt1 <= 0
                && fractionator.belt2 <= 0) {
                modWindow.stateText.text = "交互模式".Translate();
                modWindow.stateText.color = modWindow.workNormalColor;
            } else if (fractionator.fluidInputCount > 0) {
                if (consumerRatio == 1f) {
                    modWindow.stateText.text = "正常运转".Translate();
                    modWindow.stateText.color = modWindow.workNormalColor;
                } else if (consumerRatio > 0.1f) {
                    modWindow.stateText.text = "电力不足".Translate();
                    modWindow.stateText.color = modWindow.powerLowColor;
                } else {
                    modWindow.stateText.text = "停止运转".Translate();
                    modWindow.stateText.color = modWindow.powerOffColor;
                }
            }
        } else {
            if (fractionator.fluidId == 0) {
                modWindow.stateText.text = "待机".Translate();
                modWindow.stateText.color = modWindow.idleColor;
            } else if (fractionator.fluidOutputCount >= fluidOutputMax) {
                modWindow.stateText.text = "原料堆积".Translate();
                modWindow.stateText.color = modWindow.workStoppedColor;
            } else if (products.Any(p => p.count >= productOutputMax)) {
                modWindow.stateText.text = building.EnableFluidEnhancement()
                    ? "分馏永动".Translate()
                    : "产物堆积".Translate();
                modWindow.stateText.color = modWindow.workStoppedColor;
            } else if (fractionator.fluidInputCount == 0) {
                modWindow.stateText.text = "缺少原材料".Translate();
                modWindow.stateText.color = modWindow.workStoppedColor;
            } else {
                modWindow.stateText.text = "搬运模式".Translate();
                modWindow.stateText.color = modWindow.workStoppedColor;
            }
        }
    }

    private static void UpdatePowerDisplay(UIFractionatorWindow src,
        PowerConsumerComponent powerConsumer, float consumerRatio) {
        src.powerServedSB ??= new StringBuilder("         W     %", 20);

        long powerPerMin = (long)((double)(powerConsumer.requiredEnergy * 60) * (double)consumerRatio + 0.5);
        StringBuilderUtility.WriteKMG(src.powerServedSB, 8, powerPerMin);
        StringBuilderUtility.WriteUInt(src.powerServedSB, 12, 3, (uint)(consumerRatio * 100f));

        if (consumerRatio == 1f) {
            src.powerText.text = src.powerServedSB.ToString();
            src.powerIcon.color = src.powerNormalIconColor;
            src.powerText.color = src.powerNormalColor;
        } else if (consumerRatio > 0.1f) {
            src.powerText.text = src.powerServedSB.ToString();
            src.powerIcon.color = src.powerLowIconColor;
            src.powerText.color = src.powerLowColor;
        } else {
            src.powerText.text = "未供电".Translate();
            src.powerIcon.color = Color.clear;
            src.powerText.color = src.powerOffColor;
        }

        if (_modPowerText != null) {
            _modPowerText.text = src.powerText.text;
            _modPowerText.color = src.powerText.color;
        }
        if (_modPowerIcon != null) {
            _modPowerIcon.color = src.powerIcon.color;
        }
    }

    private static BaseRecipe GetRecipeForBuilding(int buildingID, int fluidId) {
        return buildingID switch {
            IFE交互塔 => GetRecipe<BuildingTrainRecipe>(ERecipe.BuildingTrain, fluidId),
            IFE矿物复制塔 => GetRecipe<MineralCopyRecipe>(ERecipe.MineralCopy, fluidId),
            IFE点数聚集塔 => GetRecipe<PointAggregateRecipe>(ERecipe.PointAggregate, fluidId),
            IFE转化塔 => GetRecipe<ConversionRecipe>(ERecipe.Conversion, fluidId),
            IFE精馏塔 => GetRecipe<RectificationRecipe>(ERecipe.Rectification, fluidId),
            _ => null
        };
    }

    private static void UpdateUIElements(UIFractionatorWindow src,
        FractionatorComponent fractionator, BaseRecipe recipe,
        float recipeSuccessRatio, float mainOutputBonus, float destroyRatio, bool hasFluid, int lockedOutputId) {

        List<ProductOutputInfo> products = fractionator.products(src.factory);
        bool sandboxMode = GameMain.sandboxToolsEnabled;
        ConversionRecipe conversionRecipe = recipe as ConversionRecipe;
        bool showLockControls = src.factory.entityPool[fractionator.entityId].protoId == IFE转化塔
                                && ConversionTower.EnableSingleLock
                                && conversionRecipe != null
                                && conversionRecipe.SupportsLockedOutput;

        foreach (var slot in mainSlots)
            if (slot != null) {
                slot.go.SetActive(false);
                SetSlotLocked(slot, false);
            }
        foreach (var slot in sideSlots)
            if (slot != null) {
                slot.go.SetActive(false);
                SetSlotLocked(slot, false);
            }
        if (fluidSlot != null) fluidSlot.go.SetActive(false);

        int fractionatorId = src.fractionatorId;
        bool hasCachedWidth = widthByFractionatorId.TryGetValue(fractionatorId, out float cachedWidth);

        if (!hasFluid) {
            ApplyWindowSizeKeepingTopLeft(hasCachedWidth ? cachedWidth : 0f, AddHeight);
            if (_mainArrowText != null) _mainArrowText.gameObject.SetActive(false);
            if (_sideArrowText != null) _sideArrowText.gameObject.SetActive(false);
            if (_fluidArrowText != null) _fluidArrowText.gameObject.SetActive(false);
            if (fluidRightText != null) fluidRightText.gameObject.SetActive(false);
            UpdateLockStatusUI(fractionator, recipe as ConversionRecipe, lockedOutputId, showLockControls);
            if (modWindow.oriProductBox != null) modWindow.oriProductBox.SetActive(false);
            if (modWindow.oriProductIcon != null) ((Behaviour)modWindow.oriProductIcon).enabled = false;
            if (modWindow.oriProductCountText != null) ((Behaviour)modWindow.oriProductCountText).enabled = false;
            if (modWindow.oriProductIncs != null) {
                for (int i = 0; i < modWindow.oriProductIncs.Length; i++)
                    if (modWindow.oriProductIncs[i] != null)
                        ((Behaviour)modWindow.oriProductIncs[i]).enabled = false;
            }
            return;
        }

        int mainCount = 0, sideCount = 0;
        float mainSuccessSum = 0f;
        ConversionRecipe.LockedOutputPlan lockedPlan = default;
        bool singleLockActive = showLockControls
                                && lockedOutputId != 0
                                && conversionRecipe != null
                                && conversionRecipe.TryGetLockedOutputPlan(lockedOutputId, out lockedPlan);

        if (recipe != null && RecipeGrowthQueries.IsUnlocked(recipe)) {
            foreach (var output in recipe.OutputMain) {
                if (mainCount >= MaxMainSlots) break;
                var pInfo = products.Find(p => p.itemId == output.OutputID && p.isMainOutput);
                float ratio = singleLockActive
                    ? (output.OutputID == lockedPlan.OutputID ? recipeSuccessRatio : 0f)
                    : recipeSuccessRatio * output.SuccessRatio;
                FillSlot(mainSlots[mainCount], output, pInfo?.count ?? 0,
                    ratio,
                    singleLockActive || output.ShowSuccessRatio || sandboxMode, ProductSlotKind.Main);
                SetSlotLocked(mainSlots[mainCount], singleLockActive && output.OutputID == lockedPlan.OutputID);
                mainSuccessSum += ratio;
                mainCount++;
            }
            foreach (var output in recipe.OutputAppend) {
                if (sideCount >= MaxSideSlots) break;
                var pInfo = products.Find(p => p.itemId == output.OutputID && !p.isMainOutput);
                float ratio = singleLockActive
                    ? (output.OutputID == lockedPlan.OutputID ? recipeSuccessRatio : 0f)
                    : recipeSuccessRatio * output.SuccessRatio;
                FillSlot(sideSlots[sideCount], output, pInfo?.count ?? 0,
                    ratio,
                    singleLockActive || output.ShowSuccessRatio || sandboxMode, ProductSlotKind.Side);
                SetSlotLocked(sideSlots[sideCount], singleLockActive && output.OutputID == lockedPlan.OutputID);
                sideCount++;
            }
        }

        if (_mainArrowText != null) {
            _mainArrowText.gameObject.SetActive(mainCount > 0);
            _mainArrowText.text = "主产物".Translate();
            _mainArrowText.color = ProbColor;
        }
        if (_sideArrowText != null) {
            _sideArrowText.gameObject.SetActive(sideCount > 0);
            _sideArrowText.text = "副产物".Translate();
            _sideArrowText.color = ProbColor;
        }
        if (_fluidArrowText != null) {
            _fluidArrowText.gameObject.SetActive(true);
            _fluidArrowText.text = "流动输出".Translate();
            _fluidArrowText.color = ProbColor;
        }
        if (modWindow.oriProductBox != null) modWindow.oriProductBox.SetActive(false);
        UpdateLockStatusUI(fractionator, conversionRecipe, lockedOutputId, showLockControls);

        // 流体输出右侧信息
        if (fluidRightText != null) {
            fluidRightText.gameObject.SetActive(true);
            if (recipe == null) {
                fluidRightText.text =
                    $"<color=#{ColorUtility.ToHtmlStringRGBA(DestroyColor)}>{"配方不存在".Translate()}</color>";
            } else if (!RecipeGrowthQueries.IsUnlocked(recipe)) {
                fluidRightText.text =
                    $"<color=#{ColorUtility.ToHtmlStringRGBA(DestroyColor)}>{"配方未解锁".Translate()}</color>";
            } else {
                int recipeLevel = RecipeGrowthQueries.GetLevel(recipe);
                bool hasDestroy = destroyRatio > 0f;
                string destroyStr = hasDestroy ? destroyRatio.FormatP() : "";
                fluidRightText.text = recipeLevel > 0
                    ? $"{"配方强化".Translate()} +{recipeLevel}\n<color=#{ColorUtility.ToHtmlStringRGBA(DestroyColor)}>{destroyStr}</color>"
                    : (hasDestroy ? $"<color=#{ColorUtility.ToHtmlStringRGBA(DestroyColor)}>{destroyStr}</color>" : "");
            }
        }

        // 动态布局： 无副产物时流动输出上移， 窗口高度缩小
        bool hasSideProducts = sideCount > 0;
        float fluidY = hasSideProducts ? FluidY : SideY;
        float actualAddHeight = hasSideProducts ? AddHeight : AddHeight - 60f;

        int visibleSlotCount = Mathf.Max(mainCount, sideCount);
        float targetAddWidth = Mathf.Max(0, visibleSlotCount - 1) * SlotSpacing;

        if (visibleSlotCount > 0) {
            widthByFractionatorId[fractionatorId] = targetAddWidth;
        } else if (hasCachedWidth) {
            targetAddWidth = cachedWidth;
        }

        ApplyWindowSizeKeepingTopLeft(targetAddWidth, actualAddHeight);
        RefreshLayoutX(fluidY);

        if (fractionator.fluidId > 0) {
            float fluidRatio = Mathf.Clamp01(1f - mainSuccessSum);
            FillFluidSlot(fluidSlot, fractionator.fluidId, fractionator.fluidOutputCount, fluidRatio);
            int fluidInc = fractionator.fluidOutputCount > 0 && fractionator.fluidOutputInc > 0
                ? fractionator.fluidOutputInc / fractionator.fluidOutputCount
                : 0;
            int arrowLevel = Cargo.fastIncArrowTable[Math.Min(fluidInc, 10)];
            if (fluidSlot?.incArrows != null) {
                for (int i = 0; i < fluidSlot.incArrows.Length; i++)
                    if (fluidSlot.incArrows[i] != null)
                        ((Behaviour)fluidSlot.incArrows[i]).enabled = (arrowLevel >= i + 1);
            }
        }
    }

    private static void FillFluidSlot(ProductSlot slot, int itemId, int count, float ratio) {
        if (slot == null) return;
        slot.go.SetActive(true);
        slot.kind = ProductSlotKind.Fluid;
        if (slot.button != null) slot.button.data = itemId;
        ItemProto itemProto = LDB.items.Select(itemId);
        if (itemProto != null && slot.icon != null) slot.icon.sprite = itemProto.iconSprite;
        if (slot.countText != null) slot.countText.text = count.ToString();
        if (slot.probText != null) {
            slot.probText.text = ratio.FormatP();
            slot.probText.color = ProbColor;
        }
    }

    private static void FillSlot(ProductSlot slot, OutputInfo output, int count, float ratio, bool showRatio,
        ProductSlotKind kind) {
        slot.go.SetActive(true);
        slot.kind = kind;
        if (slot.button != null) slot.button.data = output.OutputID;
        ItemProto itemProto = LDB.items.Select(output.OutputID);
        if (itemProto != null && slot.icon != null) slot.icon.sprite = itemProto.iconSprite;
        if (slot.countText != null) slot.countText.text = count.ToString();
        if (slot.probText != null) {
            slot.probText.text = showRatio ? ratio.FormatP() : "???";
            slot.probText.color = ProbColor;
        }
    }

    private static void SetArrowGroup(Image[] arrows, bool enabled, Color color) {
        if (arrows == null) return;
        for (int i = 0; i < arrows.Length; i++) {
            Image arrow = arrows[i];
            if (arrow == null) continue;
            arrow.color = color;
            ((Behaviour)arrow).enabled = enabled;
        }
    }
    
    #region 词缀显示
    
    private static bool _pendingAffixRefresh = false;
    private static int _affixFlashUntilFrame = 0;
    private static Color _affixOriginalStateColor = Color.white;
    private static List<FracAffixInstance> _pendingChoices = null;
    private static bool _choiceGeneratedThisFrame = false;
    // 词缀显示缓存: 避免每帧全量重建StringBuilder
    private static int _cachedAffixVersion = -1;
    private static string _cachedAffixBody = null;
    private static int _cachedAffixCombo = -1;
    
    private static void UpdateAffixDisplay(UIFractionatorWindow src, FractionatorComponent fractionator,
        ItemProto building, int buildingID, AdjacencyManager.AdjacencyInfo adjInfo = default) {
        int planetId = src.factory.planetId;
        FracAffixManager.EnsureInitialized(planetId, fractionator.id);
        var affixes = FracAffixManager.GetAffixes(planetId, fractionator.id);
        
        if (affixes == null || affixes.Count == 0) {
            _pendingChoices = null;
            _cachedAffixBody = null;
            _cachedAffixCombo = -1;
            if (!string.IsNullOrEmpty(_lastAffixText)) { _affixText.text = ""; _lastAffixText = null; }
            return;
        }
        
        // 帧跳过: 仅每8帧重建词缀文本, 但有刷新/选择时立即重建
        int combo = SatisfactionFX.GetCombo(planetId, fractionator.id);
        bool needFullRebuild = _pendingAffixRefresh || (_pendingChoices != null) 
                               || _cachedAffixBody == null || (Time.frameCount % 8 == 0);
        
        string affixNewText;
        if (needFullRebuild || combo != _cachedAffixCombo) {
        
        var sb = new System.Text.StringBuilder();
        sb.Append("───────");
        
        // 每个词缀各一行 (图标+名称+数值+效果说明)
        // 效果说明自动根据模板字段生成: 流派·类型·效果标签·成长速率
        foreach (var affix in affixes) {
            string pfx = affix.Rarity switch { AffixRarity.Mythic=>"🌟", AffixRarity.Orange=>"★", AffixRarity.Purple=>"◆", AffixRarity.Blue=>"◇", AffixRarity.Green=>"○", _=>"·" };
            // 稀有度颜色
            string rarityColor = affix.Rarity switch {
                AffixRarity.Mythic=>"#FFD700", AffixRarity.Orange=>"#FF8C00", AffixRarity.Purple=>"#A855F7",
                AffixRarity.Blue=>"#60A5FA", AffixRarity.Green=>"#4ADE80", _=>"#9CA3AF"
            };
            float val = affix.Value + affix.GrowthAccum;
            if (affix.QuestComplete) val *= 3.5f;
            string valStr = $"+{val*100:F0}%";
            string chainHint = FracAffixManager.GetChainHint(affix.AffixId, planetId, fractionator.id);
            
            // 从模板提取效果说明
            var tmpl = FracAffixTemplates.GetById(affix.AffixId);
            string factionTag = tmpl.Faction switch { AffixFaction.Speed=>"⚡速度", AffixFaction.Output=>"📦产出", AffixFaction.Consumption=>"💧消耗", AffixFaction.Chaos=>"🌌混沌", _=>"" };
            string typeTag = tmpl.Type switch { AffixType.Growth=>"·成长", AffixType.Chain=>"·连锁", AffixType.Quest=>"·任务", AffixType.TradeOff=>"·置换", AffixType.Amplifier=>"·增幅", _=>"" };
            string effectInfo = tmpl.EffectLabel ?? "";
            // 成长型额外显示每分馏成长速率
            string growthHint = "";
            if (tmpl.Type == AffixType.Growth && tmpl.GrowthPerFraction > 0)
                growthHint = $" 每分馏+{tmpl.GrowthPerFraction*100:F2}%";
            
            sb.Append($"\n {pfx}<color={rarityColor}>{affix.Name}</color>  <color={rarityColor}>{valStr}</color>  [{factionTag}{typeTag} {effectInfo}{growthHint}]");
            if (!string.IsNullOrEmpty(chainHint)) sb.Append($"  {chainHint}");
        }
        
        // Bonus + 流派共鸣紧凑一行
        var bp = new List<string>();
        // 词缀师等级
        sb.Append($"\n{FE.Logic.Fractionation.Affix.FracAffixProgressionUI.GetLevelDisplay()}");
        if (CurrentAffixCacheBonus > 0.001f) bp.Add($"缓{CurrentAffixCacheBonus*100:F0}%");
        if (CurrentAffixEnergyReduction > 0.001f) bp.Add($"节{CurrentAffixEnergyReduction*100:F0}%");
        if (CurrentAffixStackBonus > 0.5f) bp.Add($"叠{Mathf.RoundToInt(CurrentAffixStackBonus)}");
        int refreshCost = FracAffixManager.GetRefreshCost(planetId, fractionator.id);
        string bonusLine = bp.Count > 0 ? $"[{string.Join("|",bp)}] " : "";
        sb.Append($"\n[R={refreshCost}残片] {bonusLine}");
        var fs = FracAffixManager.GetFactionSummary(planetId, fractionator.id);
        if (fs.factionCount >= 2) sb.Append($"🔗{fs.resonanceLabel} ");
        if (adjInfo.TotalNeighbors > 0 || adjInfo.AffixSynergyCount > 0) sb.Append($"+{adjInfo.Describe()}");
        // 流派冲突标签
        string conflictLbl = FE.Logic.Fractionation.Affix.FracAffixManager.GetCurrentConflictLabel();
        if (!string.IsNullOrEmpty(conflictLbl)) sb.Append($"\n<color=#F59E0B>[冲突] {conflictLbl}</color>");
        // ⚡闪电状态显示
        if (FE.Logic.Fractionation.Affix.FracAffixManager.IsLightningActive(planetId, fractionator.id))
            sb.Append($"\n<color=#00BFFF>⚡⚡⚡ 闪电赌局 速度×10 惩罚免疫 ⚡⚡⚡</color>");
        else {
            float spdMul = FE.Logic.Fractionation.Affix.FracAffixManager.GetLightningSpeedMultiplier(planetId, fractionator.id);
            if (spdMul < 0.5f)
                sb.Append($"\n<color=#888888>⚡冷却中... 速度×{spdMul:F1}</color>");
        }
        
        // 累计统计一行: 总成功分馏次数 + 各词缀累计分馏
        long totalFracOnBuilding = 0;
        foreach (var a in affixes) totalFracOnBuilding += a.FractionCount;
        if (totalFracOnBuilding > 0) {
            sb.Append($"\n📊 累计分馏 {totalFracOnBuilding} 次");
            // 每个词缀单独显示分馏次数（紧凑）
            sb.Append(" [");
            bool first = true;
            foreach (var a in affixes) {
                if (!first) sb.Append("|");
                first = false;
                sb.Append($"{a.Name.Substring(0, Math.Min(a.Name.Length, 4))}{a.FractionCount}");
            }
            sb.Append("]");
        }
        
        // 🔥 连击显示 (先保存体到缓存, 再追加连击行)
        _cachedAffixBody = sb.ToString();
        if (combo >= 5) {
            string comboColor = SatisfactionFX.ComboColor(combo);
            sb.Append($"\n🔥 <color={comboColor}>连击 x{combo}</color>");
        }
        
        // 三选一 (带流派共鸣预览)
        if (_pendingChoices == null && !_choiceGeneratedThisFrame) {
            uint seed = (uint)(Time.frameCount ^ (fractionator.id * 7919));
            _pendingChoices = FracAffixManager.RollChoices(planetId, fractionator.id, ref seed);
            _choiceGeneratedThisFrame = true;
        }
        
        if (_pendingChoices != null && _pendingChoices.Count == 3) {
            var (refreshed, untilPity) = FracAffixManager.GetPityProgress(planetId, fractionator.id);
            
            // 统计当前各流派数量（用于预览共鸣）
            int[] curFactionCounts = new int[4];
            foreach (var a in affixes) {
                var t = FracAffixTemplates.GetById(a.AffixId);
                curFactionCounts[(int)t.Faction]++;
            }
            string[] factionNames = { "⚡速度", "📦产出", "💧消耗", "🌌混沌" };
            
            // 计算当前各流派总加成（用于对比预览）
            float[] curFactionTotals = new float[4];
            foreach (var a in affixes) {
                var t = FracAffixTemplates.GetById(a.AffixId);
                float baseVal = a.Value + a.GrowthAccum;
                if (a.QuestComplete) baseVal *= 3.5f;
                if (t.Type == AffixType.Basic || t.Type == AffixType.Growth || t.Type == AffixType.TradeOff || t.Type == AffixType.Chain) {
                    curFactionTotals[(int)t.Faction] += baseVal;
                }
            }
            
            // 融合提示：2+词缀时可融合
            if (affixes.Count >= 2) {
                int fuseCost = FracAffixManager.GetFuseCost(affixes[0].Rarity);
                sb.Append($"\n<color=#888888>[N]融合 {fuseCost}残片</color>");
            }
            sb.Append("\n─选─");
            for (int i = 0; i < 3; i++) {
                var c = _pendingChoices[i];
                var tmpl = FracAffixTemplates.GetById(c.AffixId);
                string cv = $"+{c.Value*100:F0}%";
                string pfx = c.Rarity switch { AffixRarity.Mythic=>"🌟", AffixRarity.Orange=>"★", AffixRarity.Purple=>"◆", AffixRarity.Blue=>"◇", AffixRarity.Green=>"○", _=>"·" };
                string rarityColor = c.Rarity switch {
                    AffixRarity.Mythic=>"#FFD700", AffixRarity.Orange=>"#FF8C00", AffixRarity.Purple=>"#A855F7",
                    AffixRarity.Blue=>"#60A5FA", AffixRarity.Green=>"#4ADE80", _=>"#9CA3AF"
                };
                // 选项附带效果标签
                string choiceEffect = tmpl.EffectLabel ?? "";
                // 流派共鸣预览: 选了之后该流派达到几件
                int fIdx = (int)tmpl.Faction;
                int newCount = curFactionCounts[fIdx] + 1;
                string resonanceMark = newCount >= 2 ? $" ✅{factionNames[fIdx]}×{newCount}" : "";
                // 效果实时对比: 当前值 → 选后值
                float currentTotal = curFactionTotals[fIdx];
                float newTotal = currentTotal + c.Value;
                string compareStr = currentTotal > 0.001f
                    ? $"  <color=#888888>{currentTotal*100:F0}%→{newTotal*100:F0}%</color>"
                    : $"  <color=#888888>+{newTotal*100:F0}%</color>";
                sb.Append($"\n [{i+1}] {pfx}<color={rarityColor}>{c.Name}</color>  <color={rarityColor}>{cv}</color>  ({choiceEffect}{resonanceMark}){compareStr}");
            }
            string pityStr = untilPity > 0 ? $"保底{untilPity}" : "必橙!";
            sb.Append($"\n [X] 自选  {pityStr}");
            // 快捷键帮助行
            sb.Append($"\n<color=#888888>[1-3]选 [R]刷新 [X]自选 [N]融合</color>");
            // 分馏箴言：仅在无待选词缀时显示一条
            // sb.Append($"\n<color=#666688>{FE.UI.MainPanel.Archive.LoadingTips.GetRandomTip()}</color>");
        }
        
        affixNewText = sb.ToString();
        _cachedAffixCombo = combo;
        } else {
            // 缓存命中: 复用已构建词缀体 + 仅刷新连击行
            affixNewText = _cachedAffixBody ?? "";
            if (combo >= 5)
                affixNewText += $"\n🔥 <color={SatisfactionFX.ComboColor(combo)}>连击 x{combo}</color>";
            _cachedAffixCombo = combo;
        }
        
        // 流派锁定状态行 (始终追加, 不受缓存影响)
        {
            var lf = FracAffixManager.GetLockedFaction(src.factory.planetId, fractionator.id);
            if (lf.HasValue) {
                var names = new[] { "⚡速度", "📦产出", "🛢消耗", "🌌混沌" };
                affixNewText += $"\n🔒 锁定: {names[(int)lf.Value]} (Shift+R解锁)";
            } else {
                affixNewText += "\n💡 Shift+1~4 锁流派 (×2.5消费)";
            }
        }
        
        // 键盘快捷键始终检测 (不受帧跳过影响)
        if (_pendingChoices != null && _pendingChoices.Count == 3) {
            if (Input.GetKeyDown(KeyCode.Alpha1)) { FracAffixManager.SelectAffix(planetId,fractionator.id,0,_pendingChoices); _pendingChoices=null; _choiceGeneratedThisFrame=false; }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { FracAffixManager.SelectAffix(planetId,fractionator.id,1,_pendingChoices); _pendingChoices=null; _choiceGeneratedThisFrame=false; }
            if (Input.GetKeyDown(KeyCode.Alpha3)) { FracAffixManager.SelectAffix(planetId,fractionator.id,2,_pendingChoices); _pendingChoices=null; _choiceGeneratedThisFrame=false; }
            if (Input.GetKeyDown(KeyCode.X)) {
                var level = FracAffixManager.GetPlayerLevel();
                var available = FracAffixTemplates.GetAvailableForLevel(level);
                if (available.Count > 0) {
                    uint xseed = (uint)(Time.frameCount * 7919 + fractionator.id);
                    int tid = available[(int)(FE.Utils.Utils.GetRandDouble(ref xseed) * available.Count)].Id;
                    FracAffixManager.SelfSelectAffix(planetId,fractionator.id,tid,affixes.Count>0?affixes.Count-1:0);
                }
                _pendingChoices=null; _choiceGeneratedThisFrame=false; }
        }
        if (Input.GetKeyDown(KeyCode.N) && affixes.Count >= 2) {
            var affixList = affixes.ToList();
            var a0 = affixList[0]; var a1 = affixList[1];
            var t0 = FracAffixTemplates.GetById(a0.AffixId);
            var t1 = FracAffixTemplates.GetById(a1.AffixId);
            var dominantFaction = FracAffixManager.GetDominantFaction(planetId, fractionator.id);
            uint nseed = (uint)(Time.frameCount * 7919 + fractionator.id);
            int cost = FracAffixManager.GetFuseCost(affixList[0].Rarity);
            var result = FracAffixManager.FuseAffixes(affixList[0], affixList[1], dominantFaction, ref nseed, out string msg);
            if (result.HasValue) {
                affixList[0] = result.Value;
                affixList.RemoveAt(1);
                FracAffixManager.SetAffixes(planetId, fractionator.id, affixList);
                var newT = FracAffixTemplates.GetById(result.Value.AffixId);
                SatisfactionFX.EnqueueBigText($"🔮 {t0.NameKey}+{t1.NameKey}→{newT.NameKey} (-{cost}残片)", "A855F7", 2.5f);
            } else {
                SatisfactionFX.EnqueueBigText($"❌ {msg}", "FF4444", 2f);
            }
            _pendingChoices = null;
            _choiceGeneratedThisFrame = false;
        }
        // 流派锁定按键 (Shift+1~4锁定, Shift+R解锁)
        {
            if (Input.GetKey(KeyCode.LeftShift)) {
                for (int f = 0; f < 4; f++) {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + f)) {
                        FracAffixManager.LockFaction(src.factory.planetId, fractionator.id, (AffixFaction)f);
                        _pendingChoices = null; _choiceGeneratedThisFrame = false;
                        break;
                    }
                }
                if (Input.GetKeyDown(KeyCode.R)) {
                    FracAffixManager.UnlockFaction(src.factory.planetId, fractionator.id);
                    _pendingChoices = null; _choiceGeneratedThisFrame = false;
                }
            }
        }
        if (Input.GetKeyDown(KeyCode.R) && !Input.GetKey(KeyCode.LeftShift)) {
            _pendingAffixRefresh = true; _pendingChoices = null; _choiceGeneratedThisFrame = false;
        }
        
        if (affixNewText != _lastAffixText) { _affixText.text = affixNewText; _lastAffixText = affixNewText; }
    }
    
    #endregion
}
