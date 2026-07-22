using System.IO;
using FE.Utils;
using FE.Logic.DarkFog;
using FE.Logic.Fractionation;
using HarmonyLib;

namespace FE.Logic.Economy;

/// <summary>
/// 动态经济系统的统一入口。
/// 这里只负责调度，不承担具体定价/报价/交易逻辑。
/// </summary>
public static class EconomyManager {
#if DEBUG
    private static int _diagTickCount = 0;
    private static System.Diagnostics.Stopwatch _diagSW = new System.Diagnostics.Stopwatch();
#endif

    public static void Init() {
        MarketValueManager.Init();
        FragmentExchangeManager.Init();
        ExchangeManager.Init();
        MarketBoardManager.Init();
        MarketLevelManager.Init();
    }

    public static void Tick() {
#if DEBUG
        _diagTickCount++;
        bool diag = _diagTickCount <= 500 && _diagTickCount % 50 == 0;

        if (diag) _diagSW.Restart();
#endif
        MarketValueManager.Tick();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] MktVal: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); _diagSW.Restart(); }
#endif
        FragmentExchangeManager.Tick();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] FragEx: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); _diagSW.Restart(); }
#endif
        ExchangeManager.Tick();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] Exch: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); _diagSW.Restart(); }
#endif
        MarketBoardManager.Tick();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] MktBd: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); _diagSW.Restart(); }
#endif
        AutoReplenishManager.Tick();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] AutoRp: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); _diagSW.Restart(); }
#endif
        DarkFogTaskManager.Tick();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] DkFog: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); _diagSW.Restart(); }
#endif
        MarketLevelManager.Tick();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] MktLv: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); _diagSW.Restart(); }
#endif
        EasterEggManager.CheckMilestones();
#if DEBUG
        if (diag) { UnityEngine.Debug.Log($"[FE diag#{_diagTickCount}] Egg: {_diagSW.Elapsed.TotalMilliseconds:F2}ms"); }
#endif
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMain), nameof(GameMain.FixedUpdate))]
    public static void GameMain_FixedUpdate_Postfix() {
        if (DSPGame.IsMenuDemo || GameMain.mainPlayer == null || !GameMain.isRunning) {
            return;
        }
        Tick();
    }

    public static void Import(BinaryReader r) {
        r.ReadBlocks(
            ("MarketValue", MarketValueManager.Import),
            ("AutoReplenish", AutoReplenishManager.Import),
            ("FragmentExchange", FragmentExchangeManager.Import),
            ("Exchange", ExchangeManager.Import),
            ("MarketBoard", MarketBoardManager.Import),
            ("DarkFogTask", DarkFogTaskManager.Import),
            ("DarkFogTech", DarkFogTechTree.Import),
            ("MarketLevel", MarketLevelManager.Import)
        );
    }

    public static void Export(BinaryWriter w) {
        w.WriteBlocks(
            ("MarketValue", MarketValueManager.Export),
            ("AutoReplenish", AutoReplenishManager.Export),
            ("FragmentExchange", FragmentExchangeManager.Export),
            ("Exchange", ExchangeManager.Export),
            ("MarketBoard", MarketBoardManager.Export),
            ("DarkFogTask", DarkFogTaskManager.Export),
            ("DarkFogTech", DarkFogTechTree.Export),
            ("MarketLevel", MarketLevelManager.Export)
        );
    }

    public static void IntoOtherSave() {
        MarketValueManager.IntoOtherSave();
        FragmentExchangeManager.IntoOtherSave();
        ExchangeManager.IntoOtherSave();
        MarketBoardManager.IntoOtherSave();
        DarkFogTaskManager.IntoOtherSave();
        MarketLevelManager.IntoOtherSave();
        DarkFogTechTree.IntoOtherSave();
    }
}