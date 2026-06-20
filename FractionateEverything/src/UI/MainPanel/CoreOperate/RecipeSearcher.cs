using System;
using System.Collections.Generic;
using System.Linq;
using FE.Logic.Fractionation.FracRecipes;
using FE.Logic.Fractionation.Growth;

namespace FE.UI.MainPanel.CoreOperate;

public static class RecipeSearcher
{
    public enum SortMode { Default, ByLevel, BySuccessRate, ByOutputValue }
    public enum FilterCategory { All, Mineral, Component, HighTech, DarkFog, Enhancement }

    public struct Query
    {
        public string SearchText;
        public FilterCategory Category;
        public bool OnlyUnlocked;
        public SortMode Sort;
        public bool Ascending;
    }

    private sealed class RecipeEntry
    {
        public int Id;
        public BaseRecipe Recipe;
        public string[] SearchTokens;
        public int Level;
        public float SuccessRate;
        public float OutputValue;
        public bool IsValid;
    }

    private static readonly Dictionary<string, List<int>> nameIndex = new();
    private static readonly Dictionary<int, RecipeEntry> recipeById = new();
    private static int nextId;

    public static void BuildIndex(IEnumerable<BaseRecipe> allRecipes)
    {
        nameIndex.Clear();
        recipeById.Clear();
        nextId = 0;

        foreach (var r in allRecipes)
        {
            int id = nextId++;
            var tokens = new HashSet<string>();

            // 搜索标签：配方类型名 + 输入物品名 + 输出物品名
            var inputProto = LDB.items.Select(r.InputID);
            if (inputProto != null)
            {
                tokens.Add(inputProto.Name);
                tokens.Add(inputProto.name);
            }
            foreach (var o in r.OutputMain)
            {
                var proto = LDB.items.Select(o.OutputID);
                if (proto != null) { tokens.Add(proto.Name); tokens.Add(proto.name); }
            }
            foreach (var o in r.OutputAppend)
            {
                var proto = LDB.items.Select(o.OutputID);
                if (proto != null) { tokens.Add(proto.Name); tokens.Add(proto.name); }
            }
            tokens.RemoveWhere(string.IsNullOrEmpty);

            var entry = new RecipeEntry
            {
                Id = id,
                Recipe = r,
                SearchTokens = tokens.ToArray(),
                Level = RecipeGrowthQueries.GetLevel(r),
                SuccessRate = r.SuccessRatio,
                OutputValue = CalcOutputValue(r),
                IsValid = true,
            };
            recipeById[id] = entry;

            foreach (var t in tokens)
            {
                if (!nameIndex.ContainsKey(t))
                    nameIndex[t] = new List<int>();
                nameIndex[t].Add(id);
            }
        }
    }

    public static List<int> Search(Query q)
    {
        var results = new HashSet<int>();

        foreach (var kv in recipeById)
        {
            if (kv.Value.IsValid)
                results.Add(kv.Key);
        }

        if (!string.IsNullOrEmpty(q.SearchText))
        {
            var lower = q.SearchText.ToLowerInvariant();
            var matched = new HashSet<int>();
            foreach (var kv in nameIndex)
            {
                if (kv.Key.IndexOf(lower, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (int id in kv.Value)
                        matched.Add(id);
                }
            }
            results.IntersectWith(matched);
        }

        if (q.OnlyUnlocked)
        {
            var unlocked = new HashSet<int>();
            foreach (int id in results)
            {
                if (recipeById.TryGetValue(id, out var e) && e.Level > 0)
                    unlocked.Add(id);
            }
            results = unlocked;
        }

        if (q.Sort == SortMode.Default)
        {
            return results.ToList();
        }

        var sorted = results.ToList();
        sorted.Sort((a, b) =>
        {
            if (!recipeById.TryGetValue(a, out var ea) || !recipeById.TryGetValue(b, out var eb))
                return 0;

            int cmp = q.Sort switch
            {
                SortMode.ByLevel => ea.Level.CompareTo(eb.Level),
                SortMode.BySuccessRate => ea.SuccessRate.CompareTo(eb.SuccessRate),
                SortMode.ByOutputValue => ea.OutputValue.CompareTo(eb.OutputValue),
                _ => 0,
            };
            return q.Ascending ? cmp : -cmp;
        });
        return sorted;
    }

    public static BaseRecipe GetRecipe(int searchId)
    {
        return recipeById.TryGetValue(searchId, out var e) ? e.Recipe : null;
    }

    public static int GetRecipeLevel(int searchId)
    {
        return recipeById.TryGetValue(searchId, out var e) ? e.Level : 0;
    }

    private static float CalcOutputValue(BaseRecipe r)
    {
        float total = 0f;
        foreach (var o in r.OutputMain)
            total += o.OutputCount * o.SuccessRatio;
        foreach (var o in r.OutputAppend)
            total += o.OutputCount * o.SuccessRatio;
        return total;
    }
}
