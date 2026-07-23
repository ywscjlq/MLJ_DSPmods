using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using FE.Logic.Relic;
using FE.UI.Foundation.Window;
using HarmonyLib;
using UnityEngine;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.PlayerInventoryAccess;

namespace FE.UI.MainPanel.Setting;

/// <summary>
/// 🎰 分馏老虎机 v3 — 消耗残片抽物品，有保底+自动抽+存档+弹窗
/// 设计借鉴: fe2.2 TicketRaffle (多池/价值经济/自动抽/Config持久化)
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

    // ── ConfigEntry 持久化字段 ──
    private static ConfigEntry<bool> EnableAutoRaffleEntry;
    private static ConfigEntry<int> AutoRaffleThresholdEntry;

    // ── 统计 (可存档) ──
    private static int totalSpins;
    private static long totalSpent;
    private static long totalWon;
    private static int jackpotCount;
    private static int spinsSinceJackpot;

    // ── 奖池 ──
    private static readonly (int id, int count, float weight, string color, int stars, string label)[] PrizePool = [
        // ⬜ 灰 常见 ~43%
        (1101, 100, 16f, "#8B8B8B", 1, "铁矿"),
        (1102, 80,  14f, "#8B8B8B", 1, "铜矿"),
        (1106, 40,  12f, "#8B8B8B", 1, "硅矿"),
        (1105, 20,  10f, "#8B8B8B", 1, "钛矿"),

        // 🟦 蓝 不常见 ~24%
        (1108, 15,  8f, "#4FC3F7", 2, "磁铁"),
        (1116, 10,  7f, "#4FC3F7", 2, "石墨"),
        (1117, 5,   6f, "#4FC3F7", 2, "钻石"),
        (1111, 20,  5f, "#4FC3F7", 2, "铁块"),

        // 🟨 黄 稀有 ~17%
        (RelicItemIds.PlasmaByproduct, 3, 6f, "#FFD54F", 3, "等离子副产物"),
        (RelicItemIds.EntropyByproduct, 2, 5f, "#FFD54F", 3, "熵晶"),
        (1110, 8,   4f, "#FFD54F", 3, "齿轮"),
        (1119, 5,   3f, "#FFD54F", 3, "有机晶体"),

        // 🟠 橙 史诗 ~11%
        (1115, 3,  4f, "#FF6F00", 4, "处理器"),
        (RelicItemIds.InfiniteCatalystByproduct, 1, 3f, "#FF6F00", 4, "催化余烬"),

        // 🟣 紫 传说 ~5%
        (IFE残片, 5, 2f, "#CE93D8", 5, "残片"),
        (IFE记忆源点, 1, 2f, "#CE93D8", 5, "记忆源点"),
    ];

    // 🟣 大奖 — 独立抽取，可多抽中
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
        w.Write(1); // version
        w.Write(totalSpins);
        w.Write(totalSpent);
        w.Write(totalWon);
        w.Write(jackpotCount);
        w.Write(spinsSinceJackpot);
    }

    public static void Import(BinaryReader r) {
        int version = r.ReadInt32();
        if (version == 1) {
            totalSpins = r.ReadInt32();
            totalSpent = r.ReadInt64();
            totalWon = r.ReadInt64();
            jackpotCount = r.ReadInt32();
            spinsSinceJackpot = r.ReadInt32();
        }
    }

    public static void IntoOtherSave() {
        totalSpins = 0;
        totalSpent = 0;
        totalWon = 0;
        jackpotCount = 0;
        spinsSinceJackpot = 0;
    }

    // ═══════════════════════════════════════════════
    //  Harmony — 后台自动抽奖
    // ═══════════════════════════════════════════════

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMain), "FixedUpdate")]
    public static void GameMain_FixedUpdate_Postfix(GameMain __instance) {
        if (!__instance._running || __instance._paused) return;
        if (EnableAutoRaffleEntry == null || !EnableAutoRaffleEntry.Value) return;

        // 每6tick检测一次 (≈0.1s)
        if (__instance.timei % 6 != 0) return;

        long balance = GetItemTotalCount(CostItemId);
        int threshold = AutoRaffleThresholdEntry?.Value ?? 500;
        if (balance < threshold) return;

        // 自动抽10次（不弹窗）
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
    //  DSP 种子随机 (借鉴 TicketRaffle → RandomUtils)
    // ═══════════════════════════════════════════════

    private static double GetRandDouble() {
        rngSeed = (uint)((rngSeed % 2147483646U + 1U) * 48271UL % int.MaxValue) - 1U;
        return rngSeed / 2147483646.0;
    }

    private static int GetRandInt(int min, int max) {
        return (int)(GetRandDouble() * (max - min)) + min;
    }

    // ═══════════════════════════════════════════════
    //  GUI
    // ═══════════════════════════════════════════════

    public static void OnGUI() {
        if (hook == null) return;
        if (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout) {
            // 每帧更新种子（只在抽奖时消耗）
        }

        const float W = 580, H = 600;
        float x = Screen.width / 2f - W / 2f - 80;
        float y = Screen.height / 2f - H / 2f;

        GUI.Box(new Rect(x, y, W, H), "🎰 分馏老虎机 v3");

        float yOff = y + 25;

        // ── 数据行 ──
        long balance = GetItemTotalCount(CostItemId);
        GUI.Label(new Rect(x + 10, yOff, W - 20, 20),
            $"<color=#FFD700>⚡{balance}</color> 残片  |  <color=#FF6B35>{CostPerSpin}/次</color>  |  " +
            $"轮次:<color=#4FC3F7>{totalSpins}</color>  |  大奖:<color=#FF6F00>{jackpotCount}</color>  |  " +
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
        GUI.Box(new Rect(x + 250, yOff, 190, 22), "");
        GUI.Box(new Rect(x + 251, yOff + 1, 188 * pityProgress, 20),
            (spinsSinceJackpot >= PityThreshold ? "#FF6F00" : "#4FC3F7"));
        GUI.Label(new Rect(x + 255, yOff, 180, 22),
            $"保底: {spinsSinceJackpot}/{PityThreshold}", GUI.skin.label);
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
        GUI.Label(new Rect(x + 10, yOff, 200, 18),
            "<color=#AAAAAA>🎁 奖池预览</color> (欢迎：谢谢惠顾犯大吴疆土)");
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
            string stars = new string('★', entry.stars) + new string('☆', 5 - entry.stars);
            GUI.Label(new Rect(5, itemY, poolW - 35, 20),
                $"<color={entry.color}>{stars}</color> {entry.label} ×{entry.count}  ({pct})");
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

        // ── 结果展示 ──
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

        // ── 最近记录 ──
        if (recentWins.Count > 0) {
            GUI.Label(new Rect(x + 10, yOff, 200, 18), "<color=#888888>📜 最近记录:</color>");
            yOff += 18;
            foreach (string record in recentWins.Take(5)) {
                GUI.Label(new Rect(x + 15, yOff, W - 25, 18), record);
                yOff += 18;
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  抽奖逻辑
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
        // 用 GameMain tick 初始化种子 (DSP 风格)
        rngSeed = (uint)(GameMain.gameTick * 2654435761U + 777);

        var won = new Dictionary<int, int>();
        bool hitJackpot = false;
        string jackpotName = "";
        int emptyCount = 0;

        for (int i = 0; i < count; i++) {
            // 5% "谢谢惠顾" — 空奖增加刺激感
            if (GetRandDouble() < EmptyRollRate) {
                emptyCount++;
                spinsSinceJackpot++;
                continue;
            }

            // 先判断大奖
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
                    won.TryGetValue(entry.id, out int existing);
                    won[entry.id] = existing + entry.count;
                    break;
                }
            }
            spinsSinceJackpot++;
        }

        // 发奖
        int totalItems = 0;
        foreach (var kvp in won) {
            AddItemToPackage(kvp.Key, kvp.Value);
            totalItems += kvp.Value;
        }
        totalWon += totalItems;

        // 转盘动画
        var topWins = won.OrderByDescending(kv => kv.Value).Take(3).ToList();
        if (topWins.Count > 0) {
            var first = topWins[0];
            string firstName = LDB.items.Select(first.Key)?.Name ?? $"#{first.Key}";
            reelText = $"{firstName} ×{first.Value} ✨";
            if (topWins.Count > 1) {
                string secondName = LDB.items.Select(topWins[1].Key)?.Name ?? $"#{topWins[1].Key}";
                reelText = $"{firstName}  |  {secondName}";
            }
        } else {
            reelText = "🎰 🎰 🎰";
        }

        // ── 结果文字 ──
        string summary = string.Join(", ",
            topWins.Select(kv => $"{LDB.items.Select(kv.Key)?.Name ?? $"#{kv.Key}"}×{kv.Value}"));

        if (hitJackpot) {
            resultText = $"★ JACKPOT ★ {jackpotName} ！ ★";
            jackpotTimer = 5f;
        } else if (emptyCount == count) {
            resultText = "😅 全部谢谢惠顾，下次再来！";
        } else {
            resultText = $"🎉 {totalItems}个物品 {summary}";
        }
        resultTimer = 4f;

        recentWins.Insert(0, $"[{DateTime.Now:HH:mm}] {(hitJackpot ? "★JACK★ " : "")}{summary}");
        while (recentWins.Count > MaxRecentWins) recentWins.RemoveAt(recentWins.Count - 1);

        // ── 弹窗 ──
        if (showPopup && (hitJackpot || count >= 10)) {
            var sb = new StringBuilder();
            sb.AppendLine($"抽奖 {count} 次，获得 {totalItems} 个物品：");
            sb.AppendLine();
            foreach (var kvp in won.OrderByDescending(k => k.Value)) {
                string name = LDB.items.Select(kvp.Key)?.Name ?? $"#{kvp.Key}";
                sb.AppendLine($"  {name} ×{kvp.Value}");
            }
            if (emptyCount > 0) {
                sb.AppendLine($"  (谢谢惠顾 ×{emptyCount})");
            }
            UIMessageBox.Show("🎰 抽奖结果",
                sb.ToString().TrimEnd('\n'),
                "确定", UIMessageBox.INFO);
        }

        isSpinning = false;
    }
}
