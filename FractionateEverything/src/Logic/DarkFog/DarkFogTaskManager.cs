using System;
using System.Collections.Generic;
using System.IO;
using static FE.Logic.Items.ItemManager;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.DataCenterInventory;

namespace FE.Logic.DarkFog;

/// <summary>
/// 黑雾支线任务类型。
/// </summary>
public enum EDarkFogTaskType
{
    BuildDefense,        // 建设防御设施
    ClearGroundBases,    // 清除地面基地
    ClearHives,          // 清除太空巢穴
    CollectResources,    // 收集黑雾掉落
    ResearchTech,        // 研究黑雾科技
    MineDarkFog,         // 采集黑雾矿物
}

/// <summary>
/// 单个黑雾任务定义。
/// </summary>
public sealed class DarkFogTask
{
    public int TaskId;
    public EDarkFogTaskType TaskType;
    public string NameKey;
    public string DescKey;
    public int RequiredCount;
    public int Progress;
    public bool Completed;
    public bool Claimed;
    public EDarkFogCombatStage RequiredStage;
    public int RewardFragmentCount;
    public int[] RewardItemIds;
    public int[] RewardItemCounts;

    public bool IsAvailable(EDarkFogCombatStage currentStage)
        => currentStage >= RequiredStage && !Completed && !Claimed;
}

/// <summary>
/// 黑雾支线任务管理器。
/// 每进入一个新黑雾阶段，生成一组对应任务。
/// 任务进度由其他模块通过 ReportProgress 上报。
/// </summary>
public static class DarkFogTaskManager
{
    private static readonly List<DarkFogTask> allTasks = new();
    private static int nextTaskId = 1;
    private static EDarkFogCombatStage lastGeneratedStage = EDarkFogCombatStage.Dormant;
    private static EDarkFogCombatStage lastCheckedStage = EDarkFogCombatStage.Dormant;

    public static IReadOnlyList<DarkFogTask> AllTasks => allTasks;

    /// <summary>
    /// 初始化/加载后调用，生成当前阶段的任务。
    /// </summary>
    public static void OnStageChanged(EDarkFogCombatStage newStage)
    {
        if (newStage > lastGeneratedStage)
        {
            GenerateTasksForStage(newStage);
            lastGeneratedStage = newStage;
        }
    }

    /// <summary>
    /// 外部模块上报任务进度。
    /// </summary>
    public static void ReportProgress(EDarkFogTaskType type, int amount = 1)
    {
        foreach (var task in allTasks)
        {
            if (task.Completed || task.Claimed) continue;
            if (task.TaskType == type)
            {
                task.Progress = Math.Min(task.RequiredCount, task.Progress + amount);
                if (task.Progress >= task.RequiredCount)
                {
                    task.Completed = true;
                }
            }
        }
    }

    /// <summary>
    /// 领取任务奖励。
    /// </summary>
    public static bool ClaimReward(int taskId)
    {
        var task = allTasks.Find(t => t.TaskId == taskId);
        if (task == null || !task.Completed || task.Claimed) return false;

        // 发放残片
        if (task.RewardFragmentCount > 0)
        {
            AddItemToModData(IFE残片, task.RewardFragmentCount, 0, true);
        }

        // 发放物品奖励
        if (task.RewardItemIds != null)
        {
            for (int i = 0; i < task.RewardItemIds.Length && i < task.RewardItemCounts.Length; i++)
            {
                AddItemToModData(task.RewardItemIds[i], task.RewardItemCounts[i], 0, true);
            }
        }

        task.Claimed = true;
        return true;
    }

    /// <summary>
    /// 在 Tick 中调用，检查阶段变化并管理任务生命周期。
    /// </summary>
    public static void Tick()
    {
        var currentStage = DarkFogCombatManager.GetCurrentStage();
        if (currentStage != lastCheckedStage)
        {
            OnStageChanged(currentStage);
            lastCheckedStage = currentStage;
        }
    }

    /// <summary>
    /// 查询当前可用（未完成、未领取）的任务列表。
    /// </summary>
    public static List<DarkFogTask> GetActiveTasks()
    {
        var currentStage = DarkFogCombatManager.GetCurrentStage();
        return allTasks.FindAll(t => t.IsAvailable(currentStage));
    }

    /// <summary>
    /// 查询已完成的未领取任务。
    /// </summary>
    public static List<DarkFogTask> GetCompletedTasks()
    {
        return allTasks.FindAll(t => t.Completed && !t.Claimed);
    }

    // ── 任务生成 ──

    private static void GenerateTasksForStage(EDarkFogCombatStage stage)
    {
        switch (stage)
        {
            case EDarkFogCombatStage.Signal:
                GenerateSignalStageTasks();
                break;
            case EDarkFogCombatStage.GroundSuppression:
                GenerateGroundStageTasks();
                break;
            case EDarkFogCombatStage.StellarHunt:
                GenerateStellarStageTasks();
                break;
            case EDarkFogCombatStage.Singularity:
                GenerateSingularityStageTasks();
                break;
        }
    }

    private static void GenerateSignalStageTasks()
    {
        AddTask("黑雾侦查", "打开战斗模式，发现黑雾信号", EDarkFogTaskType.BuildDefense, 1, EDarkFogCombatStage.Signal, 100, [I能量碎片], [20]);
        AddTask("初建防御", "在任意星球建设防御设施", EDarkFogTaskType.BuildDefense, 3, EDarkFogCombatStage.Signal, 150, [I物质重组器], [5]);
    }

    private static void GenerateGroundStageTasks()
    {
        AddTask("地面清扫", "摧毁地面基地", EDarkFogTaskType.ClearGroundBases, 3, EDarkFogCombatStage.GroundSuppression, 300, [I硅基神经元], [10]);
        AddTask("资源采集", "采集黑雾掉落资源", EDarkFogTaskType.CollectResources, 200, EDarkFogCombatStage.GroundSuppression, 200, [I能量碎片], [50]);
        AddTask("科技研究", "研究一项黑雾科技", EDarkFogTaskType.ResearchTech, 1, EDarkFogCombatStage.GroundSuppression, 250, [IFE残片], [500]);
    }

    private static void GenerateStellarStageTasks()
    {
        AddTask("巢穴清剿", "清除太空巢穴", EDarkFogTaskType.ClearHives, 2, EDarkFogCombatStage.StellarHunt, 500, [I负熵奇点], [10]);
        AddTask("大规模采集", "采集黑雾高级材料", EDarkFogTaskType.CollectResources, 500, EDarkFogCombatStage.StellarHunt, 400, [I核心素], [5]);
        AddTask("太空采矿", "在行星上采集黑雾矿物", EDarkFogTaskType.MineDarkFog, 1000, EDarkFogCombatStage.StellarHunt, 600, [IFE残片], [1000]);
    }

    private static void GenerateSingularityStageTasks()
    {
        AddTask("终焉之战", "清除所有太空巢穴", EDarkFogTaskType.ClearHives, 5, EDarkFogCombatStage.Singularity, 1000, [I核心素], [20]);
        AddTask("资源采集·终", "采集黑雾终极材料", EDarkFogTaskType.CollectResources, 1000, EDarkFogCombatStage.Singularity, 800, [IFE残片], [2000]);
        AddTask("全面防御", "在5颗行星上建设防御", EDarkFogTaskType.BuildDefense, 5, EDarkFogCombatStage.Singularity, 1200, [I负熵奇点], [20]);
    }

    private static void AddTask(string nameKey, string descKey, EDarkFogTaskType type, int reqCount,
        EDarkFogCombatStage stage, int fragReward, int[] itemIds, int[] itemCounts)
    {
        allTasks.Add(new DarkFogTask
        {
            TaskId = nextTaskId++,
            NameKey = nameKey,
            DescKey = descKey,
            TaskType = type,
            RequiredCount = reqCount,
            Progress = 0,
            Completed = false,
            Claimed = false,
            RequiredStage = stage,
            RewardFragmentCount = fragReward,
            RewardItemIds = itemIds,
            RewardItemCounts = itemCounts,
        });
    }

    // ── 存档 I/O ──

    public static void Import(BinaryReader r)
    {
        r.ReadBlocks(
            ("Tasks", br =>
            {
                allTasks.Clear();
                nextTaskId = 1;
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    allTasks.Add(new DarkFogTask
                    {
                        TaskId = br.ReadInt32(),
                        TaskType = (EDarkFogTaskType)br.ReadInt32(),
                        NameKey = br.ReadString(),
                        DescKey = br.ReadString(),
                        RequiredCount = br.ReadInt32(),
                        Progress = br.ReadInt32(),
                        Completed = br.ReadBoolean(),
                        Claimed = br.ReadBoolean(),
                        RequiredStage = (EDarkFogCombatStage)br.ReadInt32(),
                        RewardFragmentCount = br.ReadInt32(),
                        RewardItemIds = ReadRewardItemIds(br),
                        RewardItemCounts = ReadRewardItemCounts(br),
                    });
                }
            }),
            ("NextTaskId", br => nextTaskId = br.ReadInt32()),
            ("GenStage", br => lastGeneratedStage = (EDarkFogCombatStage)br.ReadInt32()),
            ("CheckStage", br => lastCheckedStage = (EDarkFogCombatStage)br.ReadInt32())
        );
    }

    public static void Export(BinaryWriter w)
    {
        w.WriteBlocks(
            ("Tasks", bw =>
            {
                bw.Write(allTasks.Count);
                foreach (var t in allTasks)
                {
                    bw.Write(t.TaskId);
                    bw.Write((int)t.TaskType);
                    bw.Write(t.NameKey ?? "");
                    bw.Write(t.DescKey ?? "");
                    bw.Write(t.RequiredCount);
                    bw.Write(t.Progress);
                    bw.Write(t.Completed);
                    bw.Write(t.Claimed);
                    bw.Write((int)t.RequiredStage);
                    bw.Write(t.RewardFragmentCount);
                    // 奖励物品列表
                    if (t.RewardItemIds != null) {
                        bw.Write(t.RewardItemIds.Length);
                        for (int ri = 0; ri < t.RewardItemIds.Length; ri++) {
                            bw.Write(t.RewardItemIds[ri]);
                            bw.Write(t.RewardItemCounts != null && ri < t.RewardItemCounts.Length ? t.RewardItemCounts[ri] : 0);
                        }
                    } else {
                        bw.Write(0);
                    }
                }
            }),
            ("NextTaskId", bw => bw.Write(nextTaskId)),
            ("GenStage", bw => bw.Write((int)lastGeneratedStage)),
            ("CheckStage", bw => bw.Write((int)lastCheckedStage))
        );
    }

    public static void IntoOtherSave()
    {
        allTasks.Clear();
        nextTaskId = 1;
        lastGeneratedStage = EDarkFogCombatStage.Dormant;
        lastCheckedStage = EDarkFogCombatStage.Dormant;
    }

    private static int[] ReadRewardItemIds(System.IO.BinaryReader br)
    {
        int len = br.ReadInt32();
        if (len <= 0) return null;
        var ids = new int[len];
        for (int i = 0; i < len; i++) {
            ids[i] = br.ReadInt32();
            br.ReadInt32(); // skip count in ID stream
        }
        return ids;
    }

    private static int[] ReadRewardItemCounts(System.IO.BinaryReader br)
    {
        int len = br.ReadInt32();
        if (len <= 0) return null;
        var counts = new int[len];
        for (int i = 0; i < len; i++) {
            br.ReadInt32(); // skip ID
            counts[i] = br.ReadInt32();
        }
        return counts;
    }
}
