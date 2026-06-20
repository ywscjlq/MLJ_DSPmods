using System.IO;
using FE.Logic.Fractionation.Process;
using static FE.Utils.Utils;

namespace FE.Logic;

/// <summary>
/// 分馏日报 — 每次加载存档时简报上次下线以来的分馏概况。
/// 利用总成功次数差值生成报告，给玩家持续的正反馈。
/// </summary>
public static class DailyDigest {
    private static long _lastTotal;
    private static long _lastTick;
    private static bool _initialized;
    private static bool _pendingShow;

    /// <summary>存档加载后调用，初始化基线</summary>
    public static void OnSaveLoaded() {
        long current = ProcessManager.totalFractionSuccesses;
        long currentTick = GameMain.gameTick;

        if (!_initialized) {
            // 首次加载，只记录不显示
            _lastTotal = current;
            _lastTick = currentTick;
            _initialized = true;
            return;
        }

        long diff = current - _lastTotal;
        if (diff > 0) {
            _pendingShow = true;
            // 计算两次加载间的游戏时长（秒）
            long tickDiff = currentTick - _lastTick;
            double realSeconds = tickDiff > 0 ? tickDiff / 60.0 : 0;
            double perMinute = realSeconds > 0 ? diff / realSeconds * 60 : 0;

            LogInfo($"[DailyDigest] 距上次上线: 分馏+{diff}次, 平均{perMinute:F0}/min");
        }

        _lastTotal = current;
        _lastTick = currentTick;
    }

    /// <summary>获取日报文本（由 UI 在适当时机调用）</summary>
    public static string GetDigestText() {
        if (!_pendingShow) return null;

        long diff = ProcessManager.totalFractionSuccesses - _lastTotal;
        if (diff <= 0) return null;

        long currentTick = GameMain.gameTick;
        long tickDiff = currentTick - _lastTick;
        double realSeconds = tickDiff > 0 ? tickDiff / 60.0 : 0.01;
        double perMinute = diff / realSeconds * 60;

        // 生成速度评级
        string speedRank = perMinute switch {
            > 100000 => "🚀 光速",
            > 50000 => "⚡ 超音速",
            > 10000 => "🏃 疾跑",
            > 1000 => "🚶 散步",
            _ => "🐢 龟速"
        };

        long peak = ProcessManager.peakFractionSuccessesPerMinute;

        string text = $"📋 <color=#60A5FA>分馏简报</color>\n"
                    + $"上次下线至今：<color=#4ADE80>+{diff:N0}</color> 次分馏成功\n"
                    + $"平均速度：{perMinute:F0}/min ({speedRank})\n"
                    + $"历史峰值：{peak:N0}/min\n"
                    + $"\n<color=#9CA3AF>「比昨天又多转了一点。」</color>";

        _pendingShow = false;
        return text;
    }

    /// <summary>主动标记已显示（如果外部负责 UI 弹窗）</summary>
    public static void MarkShown() {
        _pendingShow = false;
    }

    #region Save/Load

    public static void Export(BinaryWriter w) {
        w.Write(_lastTotal);
        w.Write(_lastTick);
        w.Write(_initialized);
    }

    public static void Import(BinaryReader r) {
        _lastTotal = r.ReadInt64();
        _lastTick = r.ReadInt64();
        _initialized = r.ReadBoolean();
        _pendingShow = false; // 不持久化待显示状态
    }

    public static void IntoOtherSave() {
        _lastTotal = 0;
        _lastTick = 0;
        _initialized = false;
        _pendingShow = false;
    }

    #endregion
}
