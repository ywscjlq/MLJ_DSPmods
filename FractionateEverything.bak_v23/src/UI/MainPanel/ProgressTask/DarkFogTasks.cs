using FE.UI.Foundation.Window;
using FE.UI.Layout;
using UnityEngine;
using static FE.UI.Layout.GridDsl;
using static FE.Utils.Utils;

namespace FE.UI.MainPanel.ProgressTask;

/// <summary>
/// 黑雾支线任务页面（UI 存根，功能通过 EconomyManager.DarkFogTaskManager 在后台运行）。
/// </summary>
public static class DarkFogTasks
{
    private static RectTransform tab;

    public static void CreateUI(MyWindow wnd, RectTransform trans)
    {
        BuildLayout(wnd, trans,
            Grid(
                children: [
                    Grid(pos: (0, 0), objectName: "darkfog-task-root", onBuilt: root =>
                    {
                        tab = root;
                        FE.Logic.DarkFog.DarkFogTechTree.Init();
                    }),
                ]
            )
        );
    }

    public static void UpdateUI()
    {
        // UI 存根：功能数据在后台通过 EconomyManager.Tick 驱动
        // 完整 UI 需参考 DSP UI 框架 API 实现
    }

    public static void LoadConfig(BepInEx.Configuration.ConfigFile configFile) { }
}
