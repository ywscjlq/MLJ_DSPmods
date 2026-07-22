using System.Collections.Generic;

namespace FE.Logic.Fractionation.FracRecipes.Runtime;

/// <summary>
/// 保存文明成就投影到分馏热路径的固定配方加成。
/// </summary>
public static class RecipeModifierCache {
    private static readonly Dictionary<ERecipe, float> successRateBonusByType = [];
    private static float allRecipeSuccessRateBonus;

    /// <summary>活跃遗物带来的副产物条目（物品ID → 概率）</summary>
    private static readonly Dictionary<int, float> relicByproductEntries = [];

    public static void Reset() {
        successRateBonusByType.Clear();
        allRecipeSuccessRateBonus = 0f;
        relicByproductEntries.Clear();
    }

    /// <summary>清空副产物条目，用于重新注册前防累积</summary>
    public static void ClearRelicByproducts() {
        relicByproductEntries.Clear();
    }

    public static void AddSuccessRateBonus(ERecipe recipeType, float bonus) {
        if (bonus <= 0f) {
            return;
        }
        successRateBonusByType.TryGetValue(recipeType, out float current);
        successRateBonusByType[recipeType] = current + bonus;
    }

    public static void AddAllRecipeSuccessRateBonus(float bonus) {
        if (bonus > 0f) {
            allRecipeSuccessRateBonus += bonus;
        }
    }

    public static float GetSuccessRateBonus(BaseRecipe recipe) {
        if (recipe == null) {
            return allRecipeSuccessRateBonus;
        }
        successRateBonusByType.TryGetValue(recipe.RecipeType, out float typeBonus);
        return allRecipeSuccessRateBonus + typeBonus;
    }

    /// <summary>注册一个遗物副产物产出</summary>
    public static void AddRelicByproduct(int itemId, float chance) {
        if (itemId <= 0 || chance <= 0f) return;
        relicByproductEntries.TryGetValue(itemId, out float current);
        relicByproductEntries[itemId] = current + chance;
    }

    /// <summary>获取所有活跃遗物带来的副产物条目（只读）</summary>
    public static IReadOnlyDictionary<int, float> GetRelicByproducts() => relicByproductEntries;
}
