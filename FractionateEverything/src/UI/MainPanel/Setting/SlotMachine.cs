using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using FE.Logic.Civilization.Protocols;
using FE.Logic.Relic;
using FE.UI.Foundation.Window;
using HarmonyLib;
using UnityEngine;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.PlayerInventoryAccess;

namespace FE.UI.MainPanel.Setting;

/// <summary>
/// 🎰 分馏老虎机 v4 — 遗物抽卡+副物轮转+协议门控+成就追踪
/// </summary>
public static class SlotMachine {
    private static SlotMachineGUIHook hook;
    private static Vector2 scrollPos;
    private static string resultText = "";
    private static float resultTimer;
    private static float jackpotTimer;
    private static readonly List<string> recentWins = [];
    private static int batchSize = 1;
    private static bool isSpinning;
    private static string reelText = "🎰 🎰 🎰";
    private static uint rngSeed;
    private static int mode; // 0=普通抽奖, 1=副物轮转

    // ── ConfigEntry ──
    private static ConfigEntry<bool> EnableAutoRaffleEntry;
    private static ConfigEntry<int> AutoRaffleThresholdEntry;

    // ── 统计 (可存档) ──
    private static int totalSpins;
    private static long totalSpent;
    private static long totalWon;
    private static int jackpotCount;
    private static int spinsSinceJackpot;
    private static int relicsExcavated;   // 老虎机挖出的遗物数
    private static int byproductRotations; // 副物轮转次数

    // ── 副物轮转状态 ──
    private static int selectedSourceByproduct = RelicItemIds.PlasmaByproduct;
    private static readonly int[] ByproductIds = [
        RelicItemIds.PlasmaByproduct,
        RelicItemIds.EntropyByproduct,
        RelicItemIds.InfiniteCatalystByproduct,
    ];
    private static readonly string[] ByproductNames = ["等离子副产物", "熵　晶", "催化余烬"];

    // ═══════════════════════════════════════════════
    //  奖池
    // ═══════════════════════════════════════════════

    private static readonly (int id, int count, float weight, string color, int stars, string label)[] PrizePool = [
        // ⬜ 灰 常见 ~33%
        (1101, 100, 14f, "#8B8B8B", 1, "铁矿"),
        (1102, 80,  12f, "#8B8B8B", 1, "铜矿"),
        (1106, 40,  10f, "#8B8B8B", 1, "硅矿"),
        (1105, 20,  8f,  "#8B8B8B", 1, "钛矿"),

        // 🟦 蓝 不常见 ~20%
        (1108, 15,  6f, "#4FC3F7", 2, "磁铁"),
        (1116, 10,  5f, "#4FC3F7", 2, "石墨"),
        (1111, 20,  5f, "#4FC3F7", 2, "铁块"),

        // 🟨 黄 稀有 ~15%
        (1117, 5,   4f, "#FFD54F", 3, "钻石"),
        (1110, 8,   4f, "#FFD54F", 3, "齿轮"),
        (1119, 5,   3f, "#FFD54F", 3, "有机晶体"),

        // 🟠 橙 史诗 ~12%
        (1115, 3,  4f, "#FF6F00", 4, "处理器"),
        (RelicItemIds.PlasmaByproduct, 2, 3f, "#FF6F00", 4, "等离子副产物"),
        (RelicItemIds.EntropyByproduct, 1, 2f, "#FF6F00", 4, "熵晶"),

        // 🟣 紫 传说 ~8%
        (RelicItemIds.InfiniteCatalystByproduct, 1, 2f, "#CE93D8", 5, "催化余烬"),
        (IFE残片, 10, 2f, "#CE93D8", 5, "残片×10"),
        (IFE记忆源点, 1, 1f, "#CE93D8", 5, "记忆源点"),

        // 🔴 遗物抽卡 ~5% — 协议门控，直接调用RelicManager.Excavate
        (-1, 0, 4f, "#FF1744", 6, "随机遗物发掘"),
        (-2, 0, 1f, "#D50000", 7, "指定遗物发掘"),
    ];

    // ── 特定遗物发掘池（协议完成数随机的遗物） ──
    private static readonly (int templateId, string name)[] DirectExcavationPool = [
        (1, "燧石核心"), (2, "陶土导管"), (3, "青铜涡盘"), (4, "日晷罗盘"),
        (5, "水晶谐振器"), (6, "星图碎片"), (7, "重力井核心"), (8, "等离子透镜"),
        (9, "量子纠缠器"), (10, "戴森环残片"), (11, "暗流驱动"), (12, "熵减协议"),
        (13, "奇点核心"), (14, "创世透镜"), (15, "无尽催化环"), (16, "终焉符文"),
    ];

    // 🟣 大奖池
    private static readonly (int id, int count, string name)[] JackpotPool = [
        (IFE记忆源点, 10, "记忆源点×10"),
        (IFE残片, 500, "残片×500"),
        (1119, 100, "有机晶体×100"),
        (1115, 50, "处理器×50"),
        (1117, 100, "钻石×100"),
        (1116, 200, "石墨×200"),
    ];

    private const int CostItemId = IFE残片;
    private const int CostPerSpin = 20;
    private const int CostByproductRotation = 20; // 副物轮转消耗残片
    private const int MaxRecentWins = 10;
    private const int PityThreshold = 50;
    private const float PityRateIncrement = 0.15f;
    private const float BaseJackpotRate = 0.005f;
    private const float EmptyRollRate = 0.05f;

    // ═══════════════════════════════════════════════
    //  Config / Save
    // ═══════════════════════════════════════════════

    public static void LoadConfig(ConfigFile configFile) {
        EnableAutoRaffleEntry = configFile.Bind("SlotMachine", "EnableAutoRaffle", false,
            "启用后台自动抽奖(每帧检测,残片≥阈值时自动抽取10次)");
        AutoRaffleThresholdEntry = configFile.Bind("SlotMachine", "AutoRaffleThreshold", 500,
            "自动抽奖触发阈值(残片≥此数量时)");
    }

    public static void Export(BinaryWriter w) {
        w.Write(2); // version
        w.Write(totalSpins);
        w.Write(totalSpent);
        w.Write(totalWon);
        w.Write(jackpotCount);
        w.Write(spinsSinceJackpot);
        w.Write(relicsExcavated);
        w.Write(byproductRotations);
    }

    public static void Import(BinaryReader r) {
        int version = r.ReadInt32();
        if (version >= 1) {
            totalSpins = r.ReadInt32();
            totalSpent = r.ReadInt64();
            totalWon = r.ReadInt64();
            jackpotCount = r.ReadInt32();
            spinsSinceJackpot = r.ReadInt32();
        }
        if (version >= 2) {
            relicsExcavated = r.ReadInt32();
            byproductRotations = r.ReadInt32();
        }
    }

    public static void IntoOtherSave() {
        totalSpins = 0;
        totalSpent = 0;
        totalWon = 0;
        jackpotCount = 0;
        spinsSinceJackpot = 0;
        relicsExcavated = 0;
        byproductRotations = 0;
    }

    // ═══════════════════════════════════════════════
    //  Harmony — 后台自动抽奖
    // ═══════════════════════════════════════════════

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMain), "FixedUpdate")]
    public static void GameMain_FixedUpdate_Postfix(GameMain __instance) {
        if (!__instance._running || __instance._paused) return;
        if (EnableAutoRaffleEntry == null || !EnableAutoRaffleEntry.Value) return;
        if (__instance.timei % 6 != 0) return;

        long balance = GetItemTotalCount(CostItemId);
        int threshold = AutoRaffleThresholdEntry?.Value ?? 500;
        if (balance < threshold || mode != 0) return; // 仅普通模式自动抽

        int autoCount = 10;
        int totalCost = autoCount * CostPerSpin;
        if (!TakeItemWithTip(CostItemId, totalCost, out _, false)) return;

        totalSpins += autoCount;
        totalSpent += totalCost;
        SpinInternal(autoCount, false);
    }

    // ═══════════════════════════════════════════════
    //  UI 生命周期
    // ═══════════════════════════════════════════════

    public static void CreateUI(MyWindow wnd, RectTransform trans) {
        EnsureHook(trans.gameObject);
    }

    public static void UpdateUI() {
        if (resultTimer > 0) resultTimer -= Time.deltaTime;
        if (jackpotTimer > 0) jackpotTimer -= Time.deltaTime;
    }

    private static void EnsureHook(GameObject go) {
        hook = go.GetComponent<SlotMachineGUIHook>();
        if (hook == null) hook = go.AddComponent<SlotMachineGUIHook>();
    }

    // ═══════════════════════════════════════════════
    //  DSP 种子随机
    // ═══════════════════════════════════════════════

    private static double GetRandDouble() {
        rngSeed = (uint)((rngSeed % 2147483646U + 1U) * 48271UL % int.MaxValue) - 1U;
        return rngSeed / 2147483646.0;
    }

    private static int GetRandInt(int min, int max) {
        return (int)(GetRandDouble() * (max - min)) + min;
    }

    // ═══════════════════════════════════════════════
    //  协议门控辅助
    // ═══════════════════════════════════════════════

    private static int GetCompletedProtocolCount() {
        try {
            return ProtocolCatalog.All?.Count(p => ProtocolProgressStore.IsComplete(p.RecipeKey)) ?? 0;
        } catch {
            return 0;
        }
    }

    private static int GetProtocolTier(int completedProtocols) {
        if (completedProtocols >= 10) return 4; // 黄金
        if (completedProtocols >= 5)  return 3; // 古典
        if (completedProtocols >= 2)  return 2; // 古代
        return 1; // 原始
    }

    /// <summary>根据协议完成数返回可用遗物池(未发现且时代解锁)</summary>
    private static List<RelicTemplate> GetAvailableRelics(int completedProtocols) {
        return RelicManager.AllTemplates
            .Where(t => !RelicManager.IsDiscovered(t.Id))
            .Where(t => (int)t.Era <= GetProtocolTier(completedProtocols))
            .ToList();
    }

    // ═══════════════════════════════════════════════
    //  GUI
    // ═══════════════════════════════════════════════

    public static void OnGUI() {
        if (hook == null || !hook.gameObject.activeInHierarchy) return;

        const float W = 620, H = 680;
        float x = Screen.width / 2f - W / 2f - 80;
        float y = Screen.height / 2f - H / 2f;

        GUI.Box(new Rect(x, y, W, H), "🎰 分馏老虎机 v4");

        float yOff = y + 25;

        // ── 模式切换 ──
        string[] modeNames = ["🎲 抽奖", "🔄 副物轮转"];
        for (int mi = 0; mi < modeNames.Length; mi++) {
            bool isActive = mode == mi;
            if (GUI.Toggle(new Rect(x + 10 + mi * 120, yOff, 110, 24),
                isActive, modeNames[mi], GUI.skin.button)) {
                mode = mi;
            }
        }

        // 协议信息
        int completedProtocols = GetCompletedProtocolCount();
        int tier = GetProtocolTier(completedProtocols);
        string[] tierNames = ["原始", "古代", "古典", "黄金"];
        GUI.Label(new Rect(x + W - 200, yOff, 190, 22),
            $"<color=#AAFFAA>协议完成: {completedProtocols}</color>  |  <color=#FFD700>{tierNames[tier - 1]}时代</color>");
        yOff += 30;

        if (mode == 0) DrawNormalMode(ref x, ref y, ref yOff, W);
        else DrawByproductMode(ref x, ref y, ref yOff, W);
    }

    // ═══════════════════════════════════════════════
    //  普通抽奖 GUI
    // ═══════════════════════════════════════════════

    private static void DrawNormalMode(ref float x, ref float y, ref float yOff, float W) {
        int completedProtocols = GetCompletedProtocolCount();
        int tier = GetProtocolTier(completedProtocols);

        // ── 数据行 ──
        long balance = GetItemTotalCount(CostItemId);
        GUI.Label(new Rect(x + 10, yOff, W - 20, 20),
            $"<color=#FFD700>⚡{balance}</color> 残片  |  <color=#FF6B35>{CostPerSpin}/次</color>  |  " +
            $"轮次:<color=#4FC3F7>{totalSpins}</color>  |  大奖:<color=#FF6F00>{jackpotCount}</color>  |  " +
            $"遗物:<color=#FF1744>{relicsExcavated}</color>  |  " +
            $"净赚:<color=#{(totalWon > totalSpent ? "4CAF50" : "FF5252")}>{totalWon - totalSpent}</color>");
        yOff += 22;

        // ── 转盘动画 ──
        float reelX = x + 20, reelW = W - 40;
        GUI.Box(new Rect(reelX, yOff, reelW, 50), "");
        GUI.Label(new Rect(reelX + 5, yOff + 2, reelW - 10, 46),
            $"<size=28><color=#FFD700>{reelText}</color></size>", GUI.skin.label);
        yOff += 56;

        // ── 批量选择 ──
        int[] batchOptions = [1, 10, 100];
        for (int bi = 0; bi < batchOptions.Length; bi++) {
            int b = batchOptions[bi];
            bool isActive = batchSize == b;
            if (GUI.Toggle(new Rect(x + 10 + bi * 80, yOff, 70, 22),
                isActive, $"{b}次", GUI.skin.button)) {
                batchSize = b;
            }
        }

        // 保底进度条
        float pityProgress = Mathf.Min(1f, (float)spinsSinceJackpot / PityThreshold);
        GUI.Box(new Rect(x + 250, yOff, 210, 22), "");
        GUI.Box(new Rect(x + 251, yOff + 1, 208 * pityProgress, 20),
            (spinsSinceJackpot >= PityThreshold ? "#FF6F00" : "#4FC3F7"));
        GUI.Label(new Rect(x + 255, yOff, 200, 22),
            $"保底: {spinsSinceJackpot}/{PityThreshold}  | 遗物发掘: {relicsExcavated}", GUI.skin.label);
        yOff += 30;

        // ── 自动抽奖开关 ──
        if (EnableAutoRaffleEntry != null) {
            bool autoVal = EnableAutoRaffleEntry.Value;
            bool newAuto = GUI.Toggle(new Rect(x + 10, yOff, 200, 22),
                autoVal, " 后台自动抽奖", GUI.skin.toggle);
            if (newAuto != autoVal) EnableAutoRaffleEntry.Value = newAuto;

            GUI.Label(new Rect(x + 210, yOff, 150, 22),
                $"阈值: {AutoRaffleThresholdEntry?.Value ?? 500}", GUI.skin.label);
        }
        yOff += 26;

        // ── 奖池展示 ──
        int tierDisplay = Mathf.Min(tier, 4); // 1~4
        string[] tierColors = ["#8B8B8B", "#4FC3F7", "#FFD54F", "#FF6F00"];
        GUI.Label(new Rect(x + 10, yOff, 400, 18),
            $"<color=#AAAAAA>🎁 奖池预览</color>  <color={tierColors[tierDisplay-1]}>当前可发掘时代: {tierDisplay}★</color>");
        yOff += 18;

        float poolX = x + 10;
        float poolY = yOff;
        float poolW = W - 20;
        float poolH = 120;
        GUI.Box(new Rect(poolX, poolY, poolW, poolH), "");
        scrollPos = GUI.BeginScrollView(
            new Rect(poolX, poolY + 2, poolW, poolH - 4),
            scrollPos,
            new Rect(0, 0, poolW - 25, PrizePool.Length * 22));

        float totalWeight = PrizePool.Sum(p => p.weight);
        float itemY = 0;
        foreach (var entry in PrizePool) {
            string pct = $"{(entry.weight / totalWeight) * 100:F1}%";
            string stars = new string('★', Mathf.Min(entry.stars, 5)) + new string('☆', 5 - Mathf.Min(entry.stars, 5));
            string label = entry.label;
            if (entry.id == -1) label = $"遗物(随机) — {GetAvailableRelics(completedProtocols).Count}个可用";
            if (entry.id == -2) label = "遗物(指定) — 直接发掘指定遗物";
            GUI.Label(new Rect(5, itemY, poolW - 35, 20),
                $"<color={entry.color}>{stars}</color> {label}  ({pct})");
            itemY += 22;
        }
        GUI.EndScrollView();
        yOff += poolH + 6;

        // ── 旋转按钮 ──
        int totalCost = batchSize * CostPerSpin;
        bool canSpin = balance >= totalCost && !isSpinning;
        string btnLabel = isSpinning ? "🌀 旋转中..." : $"🎰 旋转! (消耗{totalCost})";

        if (GUI.Button(new Rect(x + W / 2 - 100, yOff, 200, 40), btnLabel) && canSpin) {
            isSpinning = true;
            DoSpin(batchSize);
        }
        yOff += 46;

        // ── 结果+记录 ──
        DrawResults(x, yOff, W);
    }

    // ═══════════════════════════════════════════════
    //  副物轮转 GUI
    // ═══════════════════════════════════════════════

    private static void DrawByproductMode(ref float x, ref float y, ref float yOff, float W) {
        GUI.Label(new Rect(x + 10, yOff, W - 20, 20),
            "<color=#FFD54F>🔄 副产物轮转</color> — 消耗一种副产物+残片，随机转换为另一种副产物");
        yOff += 24;

        // 副产物存量
        GUI.Label(new Rect(x + 10, yOff, 400, 20), "副产物存量:");
        yOff += 18;

        for (int i = 0; i < ByproductIds.Length; i++) {
            long count = GetItemTotalCount(ByproductIds[i]);
            bool isSelected = selectedSourceByproduct == ByproductIds[i];
            string marker = isSelected ? "▶" : " ";
            if (GUI.Toggle(new Rect(x + 15, yOff, 280, 22),
                isSelected, $" {marker} {ByproductNames[i]} ×{count}", GUI.skin.toggle)) {
                selectedSourceByproduct = ByproductIds[i];
            }
            yOff += 22;
        }
        yOff += 6;

        // 轮转按钮
        long selCount = GetItemTotalCount(selectedSourceByproduct);
        long balance = GetItemTotalCount(CostItemId);
        bool canRotate = selCount >= 1 && balance >= CostByproductRotation && !isSpinning;

        // 目标预览
        int[] destIds = ByproductIds.Where(id => id != selectedSourceByproduct).ToArray();
        string destNames = string.Join(" / ", destIds.Select(id => {
            int idx = Array.IndexOf(ByproductIds, id);
            return ByproductNames[idx];
        }));
        GUI.Label(new Rect(x + 10, yOff, W - 20, 20),
            $"<color=#888888>目标: {destNames}</color>");
        yOff += 22;

        string btnLabel2 = isSpinning ? "🌀 轮转中..." : $"🔄 轮转! (消耗1×{ByproductNames[Array.IndexOf(ByproductIds, selectedSourceByproduct)]} + {CostByproductRotation}残片)";
        if (GUI.Button(new Rect(x + W / 2 - 100, yOff, 200, 40), btnLabel2) && canRotate) {
            isSpinning = true;
            DoByproductRotation(batchSize);
        }
        yOff += 46;

        // 批量按钮
        yOff += 4;
        int[] bOpts = [1, 5, 10];
        for (int bi = 0; bi < bOpts.Length; bi++) {
            int b = bOpts[bi];
            bool isActive = batchSize == b;
            if (GUI.Toggle(new Rect(x + 10 + bi * 80, yOff, 70, 22),
                isActive, $"{b}次", GUI.skin.button)) {
                batchSize = b;
            }
        }
        yOff += 30;

        // 统计
        GUI.Label(new Rect(x + 10, yOff, W - 20, 20),
            $"<color=#888888>副物轮转次数: {byproductRotations}</color>");
        yOff += 22;

        DrawResults(x, yOff, W);
    }

    // ═══════════════════════════════════════════════
    //  结果展示(共用)
    // ═══════════════════════════════════════════════

    private static void DrawResults(float x, float yOff, float W) {
        if (resultTimer > 0) {
            float pulse = Mathf.Abs(Mathf.Sin(Time.time * 3f));
            string alpha = ((int)(pulse * 255)).ToString("X2");
            GUI.Label(new Rect(x + 10, yOff, W - 20, 25),
                $"<color=#FFD700{alpha}>🎉 {resultText}</color>");
        }
        if (jackpotTimer > 0) {
            float jpPulse = Mathf.Abs(Mathf.Sin(Time.time * 6f));
            string jpAlpha = ((int)(jpPulse * 255)).ToString("X2");
            GUI.Label(new Rect(x + 10, yOff + 22, W - 20, 25),
                $"<color=#FF0000{jpAlpha}><size=16>★ JACKPOT! ★</size></color>");
        }
        yOff += 48;

        if (recentWins.Count > 0) {
            GUI.Label(new Rect(x + 10, yOff, 200, 18), "<color=#888888>📜 最近记录:</color>");
            yOff += 18;
            foreach (string record in recentWins.Take(5)) {
                GUI.Label(new Rect(x + 15, yOff, 600, 18), record);
                yOff += 18;
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  普通抽奖逻辑
    // ═══════════════════════════════════════════════

    private static void DoSpin(int count) {
        int totalCost = count * CostPerSpin;
        if (!TakeItemWithTip(CostItemId, totalCost, out _)) {
            resultText = "❌ 残片不够！";
            resultTimer = 2f;
            isSpinning = false;
            return;
        }
        totalSpins += count;
        totalSpent += totalCost;
        SpinInternal(count, true);
    }

    private static void SpinInternal(int count, bool showPopup) {
        rngSeed = (uint)(GameMain.gameTick * 2654435761U + 777);

        var won = new Dictionary<int, int>();
        bool hitJackpot = false;
        string jackpotName = "";
        int emptyCount = 0;
        int relicCount = 0;
        int completedProtocols = GetCompletedProtocolCount();

        for (int i = 0; i < count; i++) {
            // 5% 谢谢惠顾
            if (GetRandDouble() < EmptyRollRate) {
                emptyCount++;
                spinsSinceJackpot++;
                continue;
            }

            // 大奖判断
            float currentJackpotRate = BaseJackpotRate + spinsSinceJackpot * PityRateIncrement / PityThreshold;
            currentJackpotRate = Mathf.Min(currentJackpotRate, 0.15f);
            bool isJackpot = GetRandDouble() < currentJackpotRate;

            if (isJackpot) {
                hitJackpot = true;
                var (jpId, jpCount, jpName) = JackpotPool[GetRandInt(0, JackpotPool.Length)];
                won.TryGetValue(jpId, out int existing);
                won[jpId] = existing + jpCount;
                jackpotName = jpName;
                spinsSinceJackpot = 0;
                jackpotCount++;
                continue;
            }

            // 普通抽奖
            float totalWeight = PrizePool.Sum(p => p.weight);
            float roll = (float)GetRandDouble() * totalWeight;
            float cumulative = 0;
            foreach (var entry in PrizePool) {
                cumulative += entry.weight;
                if (roll <= cumulative) {
                    if (entry.id == -1) {
                        // 随机遗物发掘
                        var available = GetAvailableRelics(completedProtocols);
                        if (available.Count > 0) {
                            var chosen = available[GetRandInt(0, available.Count)];
                            if (RelicManager.Excavate(chosen.Id, completedProtocols)) {
                                relicCount++;
                                won.TryGetValue(IFE残片, out int ex);
                                won[IFE残片] = ex; // 遗物本身是加成不是物品
                                // 但遗物的发掘消耗了残片, 展示时显示
                            }
                        }
                    } else if (entry.id == -2) {
                        // 指定遗物发掘 — 从DirectExcavationPool随机取一个可用的
                        var available = DirectExcavationPool
                            .Where(d => !RelicManager.IsDiscovered(d.templateId))
                            .Where(d => {
                                var t = RelicManager.GetTemplate(d.templateId);
                                return t != null && (int)t.Era <= GetProtocolTier(completedProtocols);
                            })
                            .ToList();
                        if (available.Count > 0) {
                            var chosen = available[GetRandInt(0, available.Count)];
                            if (RelicManager.Excavate(chosen.templateId, completedProtocols)) {
                                relicCount++;
                            }
                        }
                    } else {
                        won.TryGetValue(entry.id, out int existing);
                        won[entry.id] = existing + entry.count;
                    }
                    break;
                }
            }
            spinsSinceJackpot++;
        }

        relicsExcavated += relicCount;

        // 发奖（仅限物品，遗物已通过Excavate处理）
        int totalItems = 0;
        foreach (var kvp in won) {
            AddItemToPackage(kvp.Key, kvp.Value);
            totalItems += kvp.Value;
        }
        totalWon += totalItems;

        // 动画+记录
        var topWins = won.OrderByDescending(kv => kv.Value).Take(3).ToList();
        string summary;
        if (relicCount > 0) {
            summary = $"🏛️ 发掘{relicCount}个遗物！";
            if (topWins.Count > 0) {
                var first = topWins[0];
                string fn = LDB.items.Select(first.Key)?.Name ?? $"#{first.Key}";
                summary += $" + {fn}×{first.Value}";
            }
        } else if (hitJackpot) {
            summary = $"★JACKPOT★ {jackpotName}";
        } else if (emptyCount == count) {
            summary = "💨 全是谢谢惠顾…";
        } else if (topWins.Count > 0) {
            var first = topWins[0];
            string fn = LDB.items.Select(first.Key)?.Name ?? $"#{first.Key}";
            summary = $"{totalItems}个物品 最大奖: {fn}×{first.Value}";
        } else {
            summary = "💨 什么也没有…";
        }

        // 转盘动画
        if (relicCount > 0) {
            reelText = $"🏛️ 遗物发掘 ×{relicCount}";
        } else if (topWins.Count > 0) {
            var first = topWins[0];
            string fn = LDB.items.Select(first.Key)?.Name ?? $"#{first.Key}";
            reelText = $"{fn} ×{first.Value} ✨";
            if (topWins.Count > 1) {
                string sn = LDB.items.Select(topWins[1].Key)?.Name ?? $"#{topWins[1].Key}";
                reelText = $"{fn}  |  {sn}";
            }
        } else {
            reelText = "💨";
        }

        resultText = summary;
        resultTimer = 4f;

        if (hitJackpot) jackpotTimer = 5f;

        recentWins.Insert(0, $"[{DateTime.Now:HH:mm}] {summary}");
        while (recentWins.Count > MaxRecentWins) recentWins.RemoveAt(recentWins.Count - 1);

        // 弹窗
        if (showPopup && (count >= 10 || relicCount > 0 || hitJackpot)) {
            var sb = new StringBuilder();
            if (relicCount > 0) sb.AppendLine($"🏛️ 发掘遗物 ×{relicCount}");
            foreach (var kvp in won.Take(10)) {
                string n = LDB.items.Select(kvp.Key)?.Name ?? $"#{kvp.Key}";
                sb.AppendLine($"  {n} ×{kvp.Value}");
            }
            if (won.Count > 10) sb.AppendLine($"  ...及其他{won.Count - 10}种");
            if (emptyCount > 0) sb.AppendLine($"  (谢谢惠顾 ×{emptyCount})");

            string title = hitJackpot ? "★ JACKPOT ★" : (relicCount > 0 ? "🏛️ 发掘结果" : "🎰 抽奖结果");
            UIMessageBox.Show(title, sb.ToString().TrimEnd('\n'), "确定", UIMessageBox.INFO);
        }

        isSpinning = false;
    }

    // ═══════════════════════════════════════════════
    //  副物轮转逻辑
    // ═══════════════════════════════════════════════

    private static void DoByproductRotation(int count) {
        // 检查是否有足够副产物
        long srcCount = GetItemTotalCount(selectedSourceByproduct);
        long balance = GetItemTotalCount(CostItemId);
        if (srcCount < count || balance < count * CostByproductRotation) {
            resultText = "❌ 副产物或残片不够！";
            resultTimer = 2f;
            isSpinning = false;
            return;
        }

        // 消耗副产物 + 残片
        if (!TakeItemWithTip(selectedSourceByproduct, count, out _, false)) {
            isSpinning = false;
            return;
        }
        if (!TakeItemWithTip(CostItemId, count * CostByproductRotation, out _, false)) {
            // 退还副产物
            AddItemToPackage(selectedSourceByproduct, count);
            isSpinning = false;
            return;
        }

        rngSeed = (uint)(GameMain.gameTick * 2654435761U + 888);

        var won = new Dictionary<int, int>();
        int[] destIds = ByproductIds.Where(id => id != selectedSourceByproduct).ToArray();

        for (int i = 0; i < count; i++) {
            int chosenId = destIds[GetRandInt(0, destIds.Length)];
            // 产出1~3个
            int amount = 1 + GetRandInt(0, 3);
            won.TryGetValue(chosenId, out int existing);
            won[chosenId] = existing + amount;
        }

        // 发奖
        int totalItems = 0;
        foreach (var kvp in won) {
            AddItemToPackage(kvp.Key, kvp.Value);
            totalItems += kvp.Value;
        }
        byproductRotations += count;

        // 结果展示
        string srcName = ByproductNames[Array.IndexOf(ByproductIds, selectedSourceByproduct)];
        var top = won.OrderByDescending(kv => kv.Value).First();
        int topIdx = Array.IndexOf(ByproductIds, top.Key);
        string topName = topIdx >= 0 ? ByproductNames[topIdx] : $"#{top.Key}";
        resultText = $"🔄 {srcName}→{topName} ×{top.Value} (共{totalItems}个)";
        reelText = $"{srcName} → {topName}";
        resultTimer = 4f;

        recentWins.Insert(0, $"[{DateTime.Now:HH:mm}] {resultText}");
        while (recentWins.Count > MaxRecentWins) recentWins.RemoveAt(recentWins.Count - 1);

        isSpinning = false;
    }
}
