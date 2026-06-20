using System.IO;
using FE.Logic.Fractionation.Process;
using FE.Logic.Fractionation.Presentation;
using static FE.Utils.Utils;

namespace FE.Logic.Fractionation;

/// <summary>
/// 隐藏彩蛋管理器 — 一次性里程碑事件。
/// 当前彩蛋：黑洞分馏（10亿分馏成功时触发）
/// </summary>
public static class EasterEggManager {
    private const long BlackHoleThreshold = 1_000_000_000L;
    private static bool _blackHoleTriggered;
    private static bool _blackHolePending;  // 待触发标志（避免在热路径中弹UI）

    /// <summary>黑洞彩蛋 buff：全分馏速度 +1%</summary>
    public static bool HasBlackholeBonus => _blackHoleTriggered;
    public static float BlackHoleSpeedBonus => HasBlackholeBonus ? 0.01f : 0f;

    /// <summary>每 tick 检查里程碑（由 EconomyManager.Tick 或等效 hook 调用）</summary>
    public static void CheckMilestones() {
        if (_blackHoleTriggered) return;

        long total = ProcessManager.totalFractionSuccesses;
        if (total >= BlackHoleThreshold && !_blackHolePending) {
            _blackHolePending = true;
            LogInfo($"[EasterEgg] 黑洞分馏触发！累计分馏数 {total} >= {BlackHoleThreshold}");
        }
    }

    /// <summary>处理待触发事件（在主线程安全位置调用）</summary>
    public static void ProcessPending() {
        if (!_blackHolePending) return;
        _blackHolePending = false;
        _blackHoleTriggered = true;

        // 暴走特效
        SatisfactionFX.TriggerRampage();

        // 弹大字 —— 分三阶段
        SatisfactionFX.EnqueueBigText("🌌 你把第 10 亿份物资丢进了分馏塔……", "A855F7", 3f);
        // 延迟 3 秒后第二条（通过 SatisfactionFX 的队列机制）
        SatisfactionFX.EnqueueBigText("空间扭曲了一下。什么都没出来。", "60A5FA", 2f);
        // 再延迟 2 秒后第三条
        SatisfactionFX.EnqueueBigText("但你觉得……它好像对你笑了一下。", "FFD700", 3f);

        LogInfo("[EasterEgg] ✅ 黑洞分馏彩蛋已解锁！全局分馏速度+1%");
    }

    /// <summary>获取彩蛋解锁状态文本（图鉴/统计用）</summary>
    public static string GetBlackHoleDescription() {
        if (!_blackHoleTriggered) return "???";
        return "你把第 10 亿份物资丢进了分馏塔。\n空间扭曲了一下。什么都没有出来。\n但你总觉得，有什么东西在另一个维度对你微笑。";
    }

    #region Save/Load

    public static void Export(BinaryWriter w) {
        w.Write(_blackHoleTriggered);
    }

    public static void Import(BinaryReader r) {
        _blackHoleTriggered = r.ReadBoolean();
        _blackHolePending = false;
    }

    public static void IntoOtherSave() {
        _blackHoleTriggered = false;
        _blackHolePending = false;
    }

    #endregion
}
