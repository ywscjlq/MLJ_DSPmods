using System;
using System.IO;
using static FE.Utils.Utils;

namespace FE.Logic.Fractionation.Process;

/// <summary>
/// 分馏域状态存档/读档（从ProcessManager拆分）
/// </summary>
public static partial class ProcessManager {
    public static void Export(BinaryWriter w) {
        w.WriteBlocks(
            ("TotalFractionSuccesses", bw => bw.Write(totalFractionSuccesses)),
            ("PeakFractionSuccessesPerMinute", bw => bw.Write(peakFractionSuccessesPerMinute))
        );
    }

    public static void Import(BinaryReader r) {
        ResetFractionRateWindow();
        r.ReadBlocks(
            ("TotalFractionSuccesses", br => totalFractionSuccesses = Math.Max(0, br.ReadInt64())),
            ("PeakFractionSuccessesPerMinute", br => peakFractionSuccessesPerMinute = Math.Max(0, br.ReadInt64()))
        );
    }

    public static void IntoOtherSave() {
        totalFractionSuccesses = 0;
        peakFractionSuccessesPerMinute = 0;
        ResetFractionRateWindow();
    }
}
