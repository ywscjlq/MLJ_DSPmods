using System;
using System.Collections.Generic;
using System.Linq;
using FE.Logic.Relic;
using FE.UI.Foundation.Window;
using UnityEngine;
using static FE.Utils.Utils;
using static FE.Logic.DataCenter.PlayerInventoryAccess;

namespace FE.UI.MainPanel.Setting;

/// <summary>
/// 🎰 分馏老虎机 v2 — 消耗残片抽物品，有保底有统计
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

    // 统计
    private static int totalSpins;
    private static long totalSpent;
    private static long totalWon;
    private static int jackpotCount;
    private static int spinsSinceJackpot;

    // 奖池配置：(物品ID, 数量, 权重, 稀有度颜色)
    private static readonly (int id, int count, float weight, string color, string label)[] PrizePool = [
        // ⬜ 常见 ~48%
        (1101, 100, 16f, "#8B8B8B", "铁矿"),
        (1102, 80,  14f, "#8B8B8B", "铜矿"),
        (1106, 40,  12f, "#8B8B8B", "硅矿"),
        (1105, 20,  10f, "#8B8B8B", "钛矿"),

        // 🟦 不常见 ~28%
        (1108, 15,  8f, "#4FC3F7", "磁铁"),
        (1116, 10,  7f, "#4FC3F7", "石墨"),
        (1117, 5,   6f, "#4FC3F7", "钻石"),
        (1111, 20,  5f, "#4FC3F7", "铁块"),
        (1110, 8,   4f, "#4FC3F7", "齿轮"),

        // 🟨 稀有 ~17%
        (RelicItemIds.PlasmaByproduct, 3, 6f, "#FFD54F", "等离子副产物"),
        (RelicItemIds.EntropyByproduct, 2, 5f, "#FFD54F", "熵晶"),
        (1115, 3,  4f, "#FFD54F", "处理器"),
        (1119, 5,  3f, "#FFD54F", "有机晶体"),

        // 🟥 传说 ~7%
        (RelicItemIds.InfiniteCatalystByproduct, 1, 3f, "#FF6F00", "催化余烬"),
        (IFE记忆源点, 1, 2f, "#FF6F00", "记忆源点"),
    ];

    // 🟣 大奖 — 独立抽取
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
    private const int PityThreshold = 50; // 保底抽数

    // 保底递增系数：每抽一次未中大奖，大奖权重+0.15
    private const float PityRateIncrement = 0.15f;
    private const float BaseJackpotRate = 0.005f; // 基础0.5%

    public static void CreateUI(MyWindow wnd, RectTransform trans) {
        EnsureHook(trans.gameObject);
    }

    public static void UpdateUI() {
        if (resultTimer > 0) resultTimer -= Time.deltaTime;
        if (jackpotTimer > 0) jackpotTimer -= Time.deltaTime;
    }

    private static void EnsureHook(GameObject go) {
        hook = go.GetComponent<SlotMachineGUIHook>();
        if (hook == null) {
            hook = go.AddComponent<SlotMachineGUIHook>();
        }
    }

    public static void OnGUI() {
        if (hook == null) return;

        const float W = 580, H = 560;
        float x = Screen.width / 2f - W / 2f - 80;
        float y = Screen.height / 2f - H / 2f;

        GUI.Box(new Rect(x, y, W, H), "🎰 分馏老虎机 v2");

        float yOff = y + 25;

        // ── 数据行 ──
        long balance = GetItemTotalCount(CostItemId);
        GUI.Label(new Rect(x + 10, yOff, W - 20, 20),
            $"<color=#FFD700>⚡{balance}</color> 残片  |  <color=#FF6B35>{CostPerSpin}/次</color>  |  " +
            $"轮次:<color=#4FC3F7>{totalSpins}</color>  |  大奖:<color=#FF6F00>{jackpotCount}</color>");
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
        GUI.Box(new Rect(x + 260, yOff, 180, 22), "");
        GUI.Box(new Rect(x + 261, yOff + 1, 178 * pityProgress, 20), "");
        GUI.Label(new Rect(x + 265, yOff, 170, 22),
            $"保底: {spinsSinceJackpot}/{PityThreshold}", GUI.skin.label);
        yOff += 30;

        // ── 奖池展示 ──
        GUI.Label(new Rect(x + 10, yOff, 200, 18), "<color=#AAAAAA>🎁 奖池预览:</color>");
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
            GUI.Label(new Rect(5, itemY, poolW - 35, 20),
                $"<color={entry.color}>●</color> {entry.label} ×{entry.count}  ({pct})");
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
        // 大奖公告
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
        var rng = new System.Random(Guid.NewGuid().GetHashCode());
        var won = new Dictionary<int, int>();
        bool hitJackpot = false;
        string jackpotName = "";

        for (int i = 0; i < count; i++) {
            // 先判断大奖
            float currentJackpotRate = BaseJackpotRate + spinsSinceJackpot * PityRateIncrement / PityThreshold;
            currentJackpotRate = Mathf.Min(currentJackpotRate, 0.15f); // 保底最多15%
            bool isJackpot = rng.NextDouble() < currentJackpotRate;

            if (isJackpot) {
                hitJackpot = true;
                var (jpId, jpCount, jpName) = JackpotPool[rng.Next(JackpotPool.Length)];
                won.TryGetValue(jpId, out int existing);
                won[jpId] = existing + jpCount;
                jackpotName = jpName;
                spinsSinceJackpot = 0;
                jackpotCount++;
                continue;
            }

            // 普通抽奖
            float totalWeight = PrizePool.Sum(p => p.weight);
            float roll = (float)rng.NextDouble() * totalWeight;
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

        // 转盘动画文字
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

        // 结果文字
        string summary = string.Join(", ",
            topWins.Select(kv => $"{LDB.items.Select(kv.Key)?.Name ?? $"#{kv.Key}"}×{kv.Value}"));

        if (hitJackpot) {
            resultText = $"★ JACKPOT ★ {jackpotName} ！ ★";
            jackpotTimer = 5f;
        } else {
            resultText = $"🎉 {totalItems}个物品 {summary}";
        }
        resultTimer = 4f;

        recentWins.Insert(0, $"[{DateTime.Now:HH:mm}] {(hitJackpot ? "★JACKPOT★" : "")}{summary}");
        while (recentWins.Count > MaxRecentWins) recentWins.RemoveAt(recentWins.Count - 1);

        isSpinning = false;
    }
}
