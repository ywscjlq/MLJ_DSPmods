using System.Linq;
using UnityEngine;

namespace FE.UI.MainPanel.CoreOperate;

public static class RecipeFilterBar
{
    public static RecipeSearcher.Query CurrentQuery = new();
    public static string SearchFieldText = "";

    private static readonly string[] SortLabels = { "默认", "等级", "成功率", "产出" };

    public static void Render(Rect area, System.Action onChanged)
    {
        float y = area.y;
        float h = 32f;
        float gap = 8f;
        Rect searchRect = new(area.x, y, Mathf.Min(260, area.width * 0.35f), h);
        var text = GUI.TextField(searchRect, SearchFieldText, 40);
        if (text != SearchFieldText)
        {
            SearchFieldText = text;
            CurrentQuery.SearchText = text;
            onChanged?.Invoke();
        }

        float btnX = searchRect.xMax + gap;

        // 排序按钮
        int sortIdx = (int)CurrentQuery.Sort;
        string label = SortLabels[sortIdx];
        string arrow = CurrentQuery.Ascending ? "▲" : "▼";
        bool isDefault = CurrentQuery.Sort == RecipeSearcher.SortMode.Default;
        string btnText = isDefault ? "排序▼" : $"{label}{arrow}";

        if (GUI.Button(new Rect(btnX, y, 72, h), btnText))
        {
            CurrentQuery.Sort = (RecipeSearcher.SortMode)((sortIdx + 1) % SortLabels.Length);
            if (CurrentQuery.Sort != RecipeSearcher.SortMode.Default)
                CurrentQuery.Ascending = false;
            onChanged?.Invoke();
        }

        // 升降序切换
        if (!isDefault)
        {
            btnX += 76;
            if (GUI.Button(new Rect(btnX, y, 32, h), CurrentQuery.Ascending ? "↑" : "↓"))
            {
                CurrentQuery.Ascending = !CurrentQuery.Ascending;
                onChanged?.Invoke();
            }
        }

        // 仅解锁
        btnX += 40;
        bool unlockToggle = GUI.Toggle(new Rect(btnX, y, 64, h), CurrentQuery.OnlyUnlocked, "已解锁");
        if (unlockToggle != CurrentQuery.OnlyUnlocked)
        {
            CurrentQuery.OnlyUnlocked = unlockToggle;
            onChanged?.Invoke();
        }
    }

    public static void Reset()
    {
        CurrentQuery = new RecipeSearcher.Query();
        SearchFieldText = "";
    }
}
