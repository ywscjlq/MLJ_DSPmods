using System.Collections.Generic;
using FE.Logic.Fractionation.Growth;
using FE.Logic.Fractionation.FracRecipes;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.PlayerInventoryAccess;
using FE.Logic.Economy;
using UnityEngine;

namespace FE.Logic.Gacha;

/// <summary>
/// 抽取执行、保底推进与奖励结算逻辑。
/// </summary>
public static partial class GachaService {
    public static List<GachaResult> Draw(int poolId, int resourceItemId, int count) {
        if (count <= 0) {
            return [];
        }

        EnsurePoolsFresh();

        var results = new List<GachaResult>(count);
        if (!GachaPool.IsDrawPool(poolId)) {
            return results;
        }

        GachaPool pool = GetPool(poolId);
        if (pool == null || !GachaPool.CanUseDrawResource(poolId, resourceItemId)) {
            return results;
        }

        int totalCost = GetDrawMatrixCost(poolId, count);
        if (!TakeItemWithTip(resourceItemId, totalCost, out _)) {
            return results;
        }

        for (int i = 0; i < count; i++) {
            bool hardPity = GachaManager.IsHardPity(poolId);
            GachaRarity rarity = RollRarity(pool, GachaManager.GetCurrentSRate(poolId, pool.RateS), hardPity);
            int itemId = hardPity ? GetHardPityItem(poolId, pool) : pool.PickRandom(rarity, rng);
            GachaFocusMatchType focusMatchType = GetFocusMatchType(poolId, itemId);
            GachaRewardResolution reward = ResolveReward(poolId, itemId);

            GachaManager.RecordDraw(poolId, rarity == GachaRarity.S);
            GachaManager.AddPoolPoints(GachaPool.PoolIdGrowth, 1);
            results.Add(new GachaResult(itemId, rarity, focusMatchType, reward.RewardType, reward.RewardItemId,
                reward.RewardCount, wasHardPity: hardPity));
        }

        return results;
    }

    private static int GetHardPityItem(int poolId, GachaPool pool) {
        if (pool.PoolS.Count > 0) {
            return pool.PoolS[rng.Next(pool.PoolS.Count)];
        }

        return poolId switch {
            GachaPool.PoolIdOpeningLine => IFE残片,
            GachaPool.PoolIdProtoLoop => IFE分馏塔定向原胚,
            _ => IFE残片,
        };
    }

    private static GachaRewardResolution ResolveReward(int poolId, int itemId) {
        if (GachaPool.IsRecipePool(poolId)) {
            return ResolveRecipeReward(itemId);
        }

        AddItemToModData(itemId, 1, 0, false);
        return new GachaRewardResolution(GachaRewardType.ItemGranted, itemId, 1);
    }

    private static GachaRewardResolution ResolveRecipeReward(int inputId) {
        if (inputId <= 0) {
            return new GachaRewardResolution(GachaRewardType.None, 0, 0);
        }

        EnsureRecipeRewardIndex();

        if (!recipeRewardIndex.TryGetValue(inputId, out BaseRecipe recipe)) {
            AddItemToModData(inputId, 1, 0, false);
            return new GachaRewardResolution(GachaRewardType.ItemGranted, inputId, 1);
        }

        bool wasLocked = !RecipeGrowthQueries.IsUnlocked(recipe);
        RecipeGrowthResult growthResult =
            RecipeGrowthExecutor.ApplyDrawReward(recipe, RecipeGrowthManager.BuildContext(manual: true));

        if (growthResult.FragmentReward > 0) {
            int fragmentReward = growthResult.FragmentReward;
            AddItemToModData(IFE残片, fragmentReward, 0, true);
            return new GachaRewardResolution(GachaRewardType.DuplicateRecipeFragments, IFE残片, fragmentReward);
        }

        return new GachaRewardResolution(wasLocked ? GachaRewardType.RecipeUnlock : GachaRewardType.RecipeUpgrade, 0,
            RecipeGrowthQueries.GetLevel(recipe));
    }

    private static void EnsureRecipeRewardIndex() {
        int recipeCount = RecipeManager.AllRecipes.Count;
        if (recipeRewardIndexRecipeCount == recipeCount) {
            return;
        }

        recipeRewardIndex.Clear();
        foreach (BaseRecipe recipe in RecipeManager.AllRecipes) {
            if (!IsOpeningLineRecipe(recipe) || recipeRewardIndex.ContainsKey(recipe.InputID)) {
                continue;
            }

            recipeRewardIndex.Add(recipe.InputID, recipe);
        }

        recipeRewardIndexRecipeCount = recipeCount;
    }

    private static GachaRarity RollRarity(GachaPool pool, float currentSRate, bool forceS) {
        if (forceS) {
            return GachaRarity.S;
        }

        double value = rng.NextDouble();
        if (value < currentSRate) {
            return GachaRarity.S;
        }

        value -= currentSRate;
        if (value < pool.RateA) {
            return GachaRarity.A;
        }

        value -= pool.RateA;
        if (value < pool.RateB) {
            return GachaRarity.B;
        }

        return GachaRarity.C;
    }

    /// <summary>
    /// 选取数据中心中库存最高的可消耗物品作为抽卡资源。
    /// 排除矩阵、基础矿物、黑雾专属材料。
    /// </summary>
    public static int SelectHighestStockCostItem()
    {
        const int minItemId = 1000;  // 跳过基础物品
        int bestItemId = IFE残片;
        long bestStock = 0;

        if (centerItemCount == null) return bestItemId;

        for (int i = minItemId; i < centerItemCount.Length && i < 12000; i++)
        {
            long stock = centerItemCount[i];
            if (stock <= 100) continue;

            var proto = LDB.items.Select(i);
            if (proto == null) continue;
            if (proto.ID == I沙土 || proto.ID == IFE残片) continue;
            // 排除矩阵类
            if (proto.Type == EItemType.Matrix) continue;
            // 排除黑雾专属（高价值保留）
            if (proto.UnlockKey == -2) continue;

            if (stock > bestStock)
            {
                bestStock = stock;
                bestItemId = i;
            }
        }

        return bestItemId;
    }

    /// <summary>
    /// 根据物品价值和抽卡次数计算实际消耗数量。
    /// </summary>
    public static int CalculateDrawCost(int resourceItemId, int drawCount)
    {
        // 基础消耗：1 单位/抽（沿用原版定价逻辑）
        // 如果资源物品是 IFE残片，保持原价
        if (resourceItemId == IFE残片 || resourceItemId <= 0)
            return GetDrawMatrixCost(0, drawCount);

        // 非残片资源：根据价值折算，价值越高所需数量越少
        float baseValue = 1.0f;
        try {
            baseValue = FE.Logic.Economy.MarketValueManager.GetBaseValue(resourceItemId);
        } catch { baseValue = 1.0f; }

        if (baseValue <= 0f) baseValue = 1f;
        int baseCost = GetDrawMatrixCost(0, drawCount);
        int adjustedCost = Mathf.Max(1, (int)(baseCost * 5f / baseValue));
        return adjustedCost;
    }

}
