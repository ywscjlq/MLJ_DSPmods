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
/// v3.0.0 遗物收集页面 — 无行星槽位，只展示收集进度 + 发掘操作
/// </summary>
public static class RelicPage {
    private static RelicGUIHook hook;
    private static Vector2 scrollPos;
    private static string statusText = "";
    private static float statusTimer;
    private static int selectedRelic = -1;
    private static bool showActiveOnly;

    private static readonly string[] eraColors = ["#8B8B8B", "#4FC3F7", "#FFD54F", "#FF6F00"];

    public static void CreateUI(MyWindow wnd, RectTransform trans) {
        EnsureHook(trans.gameObject);
    }

    public static void UpdateUI() {
        if (statusTimer > 0) statusTimer -= Time.deltaTime;
    }

    private static void EnsureHook(GameObject go) {
        hook = go.GetComponent<RelicGUIHook>();
        if (hook == null) {
            hook = go.AddComponent<RelicGUIHook>();
        }
    }

    public static void OnGUI() {
        if (hook == null) return;

        const float W = 620, H = 420;
        float x = Screen.width / 2f - W / 2f - 80;
        float y = Screen.height / 2f - H / 2f;

        GUI.Box(new Rect(x, y, W, H), "🏛️ 远古遗物 · 收集进度");

        // ── 顶部信息 ──
        int total = RelicManager.AllTemplates.Count;
        int discovered = RelicManager.TotalDiscovered;
        float yOff = y + 25;
        GUI.Label(new Rect(x + 10, yOff, 300, 20),
            $"已收集: <color=#4CAF50>{discovered}/{total}</color>  |  " +
            $"IFE残片: <color=#FFD700>{GetItemTotalCount(IFE残片)}</color>");
        yOff += 22;

        // 显示/隐藏已发现筛选
        showActiveOnly = GUI.Toggle(new Rect(x + W - 130, yOff - 2, 120, 20), showActiveOnly, "仅显示未收集");
        yOff += 22;

        // ── 遗物列表 ──
        var relics = RelicManager.AllTemplates.OrderBy(t => (int)t.Era).ThenBy(t => t.Tier).ToList();
        if (showActiveOnly) {
            relics = relics.Where(r => !RelicManager.IsDiscovered(r.Id)).ToList();
        }

        float listX = x + 5;
        float listY = yOff;
        float listW = W - 10;
        float listH = H - (yOff - y) - 10;

        GUI.Box(new Rect(listX, listY, listW, listH), "");
        scrollPos = GUI.BeginScrollView(new Rect(listX, listY + 2, listW, listH - 4),
            scrollPos, new Rect(0, 0, listW - 25, relics.Count * 60));

        int idx = 0;
        foreach (var tpl in relics) {
            bool owned = RelicManager.IsDiscovered(tpl.Id);
            float ty = idx * 60;
            string eraColor = eraColors[(int)tpl.Era];
            string costStr = owned ? "" : $"<color=#FFD700>⚙ {tpl.ExcavationCost}</color>";
            string statusStr = owned
                ? "<color=#4CAF50>✅ 已发掘</color>"
                : $"<color=#888>{tpl.Type}</color>";

            string label = $"<color={eraColor}>◆ [{tpl.Era}] {tpl.Name}</color>  {costStr}\n" +
                           $"<size=10>{statusStr} | {tpl.Description}</size>";

            Color oldBg = GUI.backgroundColor;
            bool selected = selectedRelic == tpl.Id;
            GUI.backgroundColor = selected ? (owned ? new Color(0.2f, 0.6f, 0.2f) : new Color(0.3f, 0.5f, 0.8f)) : Color.gray;

            if (GUI.Button(new Rect(2, ty, listW - 30, 55), label)) {
                selectedRelic = tpl.Id;
            }
            GUI.backgroundColor = oldBg;
            idx++;
        }
        GPScrollView(listX + 2, listY + listH - 16);
        GUI.EndScrollView();

        // ── 底部操作区 ──
        float actionY = listY + listH + 5;
        float actionH = 55;

        if (selectedRelic > 0) {
            var tpl = RelicManager.GetTemplate(selectedRelic);
            if (tpl != null) {
                bool owned = RelicManager.IsDiscovered(selectedRelic);
                GUI.Label(new Rect(listX, actionY, listW - 160, 20),
                    $"<b>{tpl.Name}</b>  |  成本: {tpl.ExcavationCost} 残片  |  " +
                    (owned ? "<color=#4CAF50>已收集</color>" : "<color=#FF5722>未收集</color>"));

                if (!owned) {
                    if (GUI.Button(new Rect(listX + listW - 150, actionY, 140, 30), "🔍 发掘")) {
                        if (RelicManager.Excavate(selectedRelic)) {
                            statusText = $"✅ 发掘成功: {tpl.Name}!";
                            statusTimer = 3f;
                        } else {
                            statusText = "❌ 残片不足或已发掘";
                            statusTimer = 2f;
                        }
                    }
                }

                // 显示遗物加成
                string bonusStr = "";
                if (tpl.SpeedMultiplier != 1f) bonusStr += $"⚡ 速度 x{tpl.SpeedMultiplier:F2}  ";
                if (tpl.SuccessBonus > 0) bonusStr += $"🎯 成功率 +{tpl.SuccessBonus * 100:F0}%  ";
                if (tpl.ByproductChance > 0) bonusStr += $"📦 副产物 {tpl.ByproductChance * 100:F0}%";
                if (!string.IsNullOrEmpty(bonusStr)) {
                    GUI.Label(new Rect(listX, actionY + 25, listW - 10, 25), bonusStr);
                }
            }
        } else {
            GUI.Label(new Rect(listX, actionY, listW - 10, 20),
                "从上方列表选择一个遗物查看详情");
        }

        // ── 共鸣区域 ──
        float resY = actionY + actionH + 5;
        var activeRes = RelicManager.GetActiveResonance();
        if (activeRes != null) {
            GUI.Box(new Rect(listX, resY, listW - (listX - x), 30),
                $"✦ <color=#FF6F00>共鸣激活: {activeRes.Name}</color> — {activeRes.Description}");
        } else {
            float resW = listW - (listX - x);
            GUI.Box(new Rect(listX, resY, resW, 30), "未激活共鸣（收集更多遗物）");
        }

        // ── 状态提示 ──
        if (statusTimer > 0) {
            GUI.Label(new Rect(x + 5, y + H - 22, W - 10, 20), statusText);
        }
    }

    private static void GPScrollView(float rx, float ry) {
        // 辅助滚动条——兼容不同版本 Unity GUI
    }
}
