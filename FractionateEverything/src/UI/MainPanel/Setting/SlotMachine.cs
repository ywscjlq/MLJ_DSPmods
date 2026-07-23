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
/// 🎰 老虎机—消耗残片抽物品，简单粗暴
/// </summary>
public static class SlotMachine {
    private static SlotMachineGUIHook hook;
    private static Vector2 scrollPos;
    private static string resultText = "";
    private static float resultTimer;
    private static readonly List<string> recentWins = [];
    private static int batchSize = 1;
    private static bool isSpinning;

    // 奖池配置： (物品ID, 数量, 权重)
    private static readonly (int id, int count, float weight)[] PrizePool = [
        // 常见 ~50%
        (1101, 100, 20f),   // 铁矿
        (1104, 50, 18f),    // 铜矿
        (1106, 30, 15f),    // 硅矿
        (1105, 20, 12f),    // 钛矿

        // 不常见 ~30%
        (1108, 10, 10f),    // 磁铁
        (1109, 8,  8f),     // 石墨
        (1110, 5,  7f),     // 钻石
        (1115, 3,  5f),     // 处理器

        // 稀有 ~15%
        (RelicItemIds.PlasmaByproduct, 2, 6f),
        (RelicItemIds.EntropyByproduct, 2, 5f),
        (RelicItemIds.InfiniteCatalystByproduct, 1, 4f),

        // 传说 ~5%
        (IFE残片, 1, 3f),
        (IFE记忆源点, 1, 2f),
    ];

    private const int CostItemId = IFE残片;
    private const int CostPerSpin = 20;
    private const int MaxRecentWins = 10;

    public static void CreateUI(MyWindow wnd, RectTransform trans) {
        EnsureHook(trans.gameObject);
    }

    public static void UpdateUI() {
        if (resultTimer > 0) resultTimer -= Time.deltaTime;
    }

    private static void EnsureHook(GameObject go) {
        hook = go.GetComponent<SlotMachineGUIHook>();
        if (hook == null) {
            hook = go.AddComponent<SlotMachineGUIHook>();
        }
    }

    public static void OnGUI() {
        if (hook == null) return;

        const float W = 540, H = 480;
        float x = Screen.width / 2f - W / 2f - 80;
        float y = Screen.height / 2f - H / 2f;

        GUI.Box(new Rect(x, y, W, H), "🎰 分馏老虎机");

        float yOff = y + 25;

        // ── 残片余额 ──
        long balance = GetItemTotalCount(CostItemId);
        GUI.Label(new Rect(x + 10, yOff, 300, 20),
            $"🎰 残片余额: <color=#FFD700>{balance}</color>  | 每次消耗: <color=#FF6B35>{CostPerSpin}</color>");
        yOff += 22;

        // ── 批量选择 ──
        int[] batchOptions = [1, 10, 100];
        for (int bi = 0; bi < batchOptions.Length; bi++) {
            int b = batchOptions[bi];
            bool isActive = batchSize == b;
            if (GUI.Toggle(new Rect(x + 10 + bi * 80, yOff, 70, 20),
                isActive, $"{b}次", GUI.skin.button)) {
                batchSize = b;
            }
        }
        yOff += 28;

        // ── 奖池展示 ──
        GUI.Label(new Rect(x + 10, yOff, 200, 18), "<color=#AAAAAA>🎁 奖池预览（权重）:</color>");
        yOff += 18;

        float poolX = x + 10;
        float poolY = yOff;
        float poolW = W - 20;
        float poolH = 140;
        GUI.Box(new Rect(poolX, poolY, poolW, poolH), "");
        scrollPos = GUI.BeginScrollView(
            new Rect(poolX, poolY + 2, poolW, poolH - 4),
            scrollPos,
            new Rect(0, 0, poolW - 25, PrizePool.Length * 22));

        float totalWeight = PrizePool.Sum(p => p.weight);
        float itemY = 0;
        foreach (var entry in PrizePool) {
            string name = LDB.items.Select(entry.id)?.Name ?? $"#{entry.id}";
            int rarity = entry.weight >= 10 ? 0 : entry.weight >= 5 ? 1 : entry.weight >= 3 ? 2 : 3;
            string color = rarity switch { 0 => "#8B8B8B", 1 => "#4FC3F7", 2 => "#FFD54F", 3 => "#FF6F00", _ => "#FFF" };
            GUI.Label(new Rect(5, itemY, poolW - 35, 20),
                $"<color={color}>●</color> {name} ×{entry.count}  ({(entry.weight / totalWeight) * 100:F1}%)");
            itemY += 22;
        }
        GUI.EndScrollView();
        yOff += poolH + 6;

        // ── 旋转按钮 ──
        int totalCost = batchSize * CostPerSpin;
        bool canSpin = balance >= totalCost && !isSpinning;
        string btnLabel = isSpinning ? "🎰 旋转中..." : $"🎰 旋转! (消耗{totalCost})";

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
        yOff += 30;

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

        var won = new Dictionary<int, int>();
        float totalWeight = PrizePool.Sum(p => p.weight);
        var rng = new System.Random(Guid.NewGuid().GetHashCode());

        for (int i = 0; i < count; i++) {
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
        }

        // 发奖
        int totalItems = 0;
        foreach (var kvp in won) {
            AddItemToPackage(kvp.Key, kvp.Value);
            totalItems += kvp.Value;
        }

        // 记录
        var topWins = won.OrderByDescending(kv => kv.Value).Take(3).ToList();
        string summary = string.Join(", ",
            topWins.Select(kv => $"{LDB.items.Select(kv.Key)?.Name ?? $"#{kv.Key}"}×{kv.Value}"));
        resultText = $"🎉 获得 {totalItems} 个物品！ {summary}";
        resultTimer = 4f;

        recentWins.Insert(0, $"[{DateTime.Now:HH:mm}] {resultText}");
        while (recentWins.Count > MaxRecentWins) recentWins.RemoveAt(recentWins.Count - 1);

        isSpinning = false;
    }
}
