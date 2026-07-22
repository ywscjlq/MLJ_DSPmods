using System;
using System.Collections.Generic;
using FE.Logic.Items;
using FE.UI.Foundation.Window;
using FE.UI.MainPanel.Archive;
using FE.UI.MainPanel.CoreOperate;
using FE.UI.MainPanel.DrawGrowth;
using FE.UI.MainPanel.ProgressTask;
using FE.UI.MainPanel.ResourceInteraction;
using FE.UI.MainPanel.Setting;
using UnityEngine;

namespace FE.UI.MainPanel;

/// <summary>
/// FE 主面板页面注册、分类顺序和可见性过滤中心。
/// </summary>
public static class MainWindowPageRegistry {
    public const string CoreOperateCategoryName = "生产管理";
    public const string ResourceInteractionCategoryName = "资源管理";
    public const string DrawGrowthCategoryName = "抽取成长";
    public const string ProgressTaskCategoryName = "任务成就";
    public const string ArchiveCategoryName = "图鉴档案";
    public const string SystemSettingCategoryName = "系统设置";
    private static readonly string[] categoryOrder = [
        CoreOperateCategoryName,
        DrawGrowthCategoryName,
        ResourceInteractionCategoryName,
        ProgressTaskCategoryName,
        ArchiveCategoryName,
        SystemSettingCategoryName,
    ];

    private static readonly MainWindowPageDefinition[] allPages = [
        // ── 生产管理（始终可见）──
        new(CoreOperateCategoryName, "分馏配方", FracRecipeOperate.CreateUI, FracRecipeOperate.UpdateUI),
        new(CoreOperateCategoryName, "原版配方", VanillaRecipeOperate.CreateUI, VanillaRecipeOperate.UpdateUI),
        new(CoreOperateCategoryName, "建筑操作", BuildingOperate.CreateUI, BuildingOperate.UpdateUI),

        // ── 资源管理（分阶段解锁）──
        new(ResourceInteractionCategoryName, "物品交互", ItemInteraction.CreateUI, ItemInteraction.UpdateUI),
        new(ResourceInteractionCategoryName, "市场总览", ResourceOverview.CreateUI, ResourceOverview.UpdateUI, minStageIndex: 2),
        new(ResourceInteractionCategoryName, "市场板", MarketBoard.CreateUI, MarketBoard.UpdateUI, minStageIndex: 3),
        new(ResourceInteractionCategoryName, "交易所", Exchange.CreateUI, Exchange.UpdateUI, minStageIndex: 2),
        new(ResourceInteractionCategoryName, "残片兑换", FragmentExchange.CreateUI, FragmentExchange.UpdateUI),

        // ── 抽取成长（白糖后解锁）──
        new(DrawGrowthCategoryName, "开线抽取", TicketRaffle.CreateRecipeUI, TicketRaffle.UpdateUI, minStageIndex: 5),
        new(DrawGrowthCategoryName, "原胚抽取", TicketRaffle.CreateProtoUI, TicketRaffle.UpdateUI, minStageIndex: 5),
        new(DrawGrowthCategoryName, "成长规划", LimitedTimeStore.CreateRecipeUI, LimitedTimeStore.UpdateUI, minStageIndex: 5),
        new(DrawGrowthCategoryName, "流派聚焦", LimitedTimeStore.CreateProtoUI, LimitedTimeStore.UpdateUI, minStageIndex: 5),
        new(DrawGrowthCategoryName, "抽取总览", TicketExchange.CreateUI, TicketExchange.UpdateUI, minStageIndex: 5),

        // ── 任务成就（始终可见）──
        new(ProgressTaskCategoryName, "主线任务", MainTask.CreateUI, MainTask.UpdateUI),
        new(ProgressTaskCategoryName, "循环任务", RecurringTask.CreateUI, RecurringTask.UpdateUI),
        new(ProgressTaskCategoryName, "成就系统", Achievements.CreateUI, Achievements.UpdateUI),
        new(ProgressTaskCategoryName, "黑雾任务", DarkFogTasks.CreateUI, DarkFogTasks.UpdateUI, minStageIndex: 3),

        // ── 图鉴档案（始终可见）──
        new(ArchiveCategoryName, "配方图鉴", RecipeGallery.CreateUI, RecipeGallery.UpdateUI),
        new(ArchiveCategoryName, "分馏统计", FracStatistic.CreateUI, FracStatistic.UpdateUI),
        new(ArchiveCategoryName, "开发日记", DevelopmentDiary.CreateUI, DevelopmentDiary.UpdateUI),

        // ── 系统设置（始终可见）──
        new(SystemSettingCategoryName, "系统仪表盘", DashboardPage.CreateUI, DashboardPage.UpdateUI),
        new(SystemSettingCategoryName, "自动补货", AutoReplenishPage.CreateUI, AutoReplenishPage.UpdateUI),
        new(SystemSettingCategoryName, "杂项设置", Miscellaneous.CreateUI, Miscellaneous.UpdateUI),
        new(SystemSettingCategoryName, "沙盒模式", SandboxMode.CreateUI, SandboxMode.UpdateUI, sandboxOnly: true),
    ];

    private static readonly IReadOnlyList<MainWindowCategoryDefinition>[,,] categoryCache = BuildCategoryCache();

    public static IReadOnlyList<string> CategoryOrder => categoryOrder;
    public static IReadOnlyList<MainWindowPageDefinition> AllPages => allPages;

    public static IReadOnlyList<MainWindowCategoryDefinition> GetCategories(FEMainPanelType panelType, bool sandboxMode,
        bool includeAllPages = false) {
        int panelIndex = panelType switch {
            FEMainPanelType.Legacy => 0,
            FEMainPanelType.Analysis => 1,
            _ => -1,
        };
        if (panelIndex < 0) {
            return BuildCategories(panelType, sandboxMode, includeAllPages);
        }

        return categoryCache[panelIndex, sandboxMode ? 1 : 0, includeAllPages ? 1 : 0];
    }

    private static IReadOnlyList<MainWindowCategoryDefinition>[,,] BuildCategoryCache() {
        var cache = new IReadOnlyList<MainWindowCategoryDefinition>[2, 2, 2];
        cache[0, 0, 0] = BuildCategories(FEMainPanelType.Legacy, false, false);
        cache[0, 1, 0] = BuildCategories(FEMainPanelType.Legacy, true, false);
        cache[0, 0, 1] = BuildCategories(FEMainPanelType.Legacy, false, true);
        cache[0, 1, 1] = BuildCategories(FEMainPanelType.Legacy, true, true);
        cache[1, 0, 0] = BuildCategories(FEMainPanelType.Analysis, false, false);
        cache[1, 1, 0] = BuildCategories(FEMainPanelType.Analysis, true, false);
        cache[1, 0, 1] = BuildCategories(FEMainPanelType.Analysis, false, true);
        cache[1, 1, 1] = BuildCategories(FEMainPanelType.Analysis, true, true);
        return cache;
    }

    private static IReadOnlyList<MainWindowCategoryDefinition> BuildCategories(FEMainPanelType panelType,
        bool sandboxMode,
        bool includeAllPages) {
        var categories = new List<MainWindowCategoryDefinition>(categoryOrder.Length);
        foreach (string categoryName in categoryOrder) {
            List<MainWindowPageDefinition> pages = [];
            foreach (MainWindowPageDefinition page in allPages) {
                if (page.CategoryName != categoryName) {
                    continue;
                }

                if (!includeAllPages && !page.IsEnabledFor(panelType, sandboxMode)) {
                    continue;
                }

                pages.Add(page);
            }

            if (pages.Count > 0 || includeAllPages) {
                categories.Add(new(categoryName, pages));
            }
        }

        return categories;
    }
}

/// <summary>
/// 分馏主面板页面分类定义。
/// </summary>
public sealed class MainWindowCategoryDefinition(string categoryName, IReadOnlyList<MainWindowPageDefinition> pages) {
    public string CategoryName { get; } = categoryName;
    public IReadOnlyList<MainWindowPageDefinition> Pages { get; } = pages;
}

/// <summary>
/// 分馏主面板页面定义。
/// 支持按进度阶段（矩阵层级）门控显示。
/// </summary>
public sealed class MainWindowPageDefinition(
    string categoryName,
    string subpageName,
    Action<MyWindow, RectTransform> createUI,
    Action updateUI,
    bool sandboxOnly = false,
    int minStageIndex = 0) {
    public string CategoryName { get; } = categoryName;
    public string SubpageName { get; } = subpageName;
    public Action<MyWindow, RectTransform> CreateUI { get; } = createUI;
    public Action UpdateUI { get; } = updateUI;
    public bool SandboxOnly { get; } = sandboxOnly;
    public int MinStageIndex { get; } = minStageIndex;

    public bool IsEnabledFor(FEMainPanelType panelType, bool sandboxMode) {
        if (SandboxOnly && !sandboxMode) {
            return false;
        }

        // 按矩阵阶段门控：当前阶段 < 最低需求阶段时隐藏
        if (MinStageIndex > 0) {
            int currentStage = ItemManager.GetCurrentProgressStageIndex();
            if (currentStage < MinStageIndex) {
                return false;
            }
        }

        return panelType switch {
            FEMainPanelType.Legacy or FEMainPanelType.Analysis => true,
            _ => false,
        };
    }
}
