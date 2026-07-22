using System;
using System.Collections.Generic;
using System.Linq;
using FE.Logic.Relic;
using FE.UI.Foundation.Window;
using UnityEngine;

namespace FE.UI.MainPanel.Setting;

/// <summary>
/// 遗物考古页面 — 发掘、查看、放置遗物 + 共鸣检测
/// </summary>
public static class RelicPage {
    // ── 布局状态 ──
    private static RelicGUIHook hook;
    private static Vector2 eraScroll, relicListScroll, activeSlotScroll;
    private static int selectedEra = -1; // -1 = 全部
    private static int selectedRelic = -1;
    private static string statusText = "";
    private static float statusTimer;
    private static readonly string[] eraNames = ["全部", "原始", "古代", "古典", "黄金"];
    private static readonly string[] eraColors = ["white", "#8B8B8B", "#4FC3F7", "#FFD54F", "#FF6F00"];

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

        const float W = 600, H = 400;
        float x = Screen.width / 2f - W / 2f - 100; // 靠左对齐
        float y = Screen.height / 2f - H / 2f;

        GUI.Box(new Rect(x, y, W, H), "🏛️ 协议遗物 · 考古发掘");

        // ── 时代选项卡 ──
        float tabY = y + 25;
        eraScroll = GUI.BeginScrollView(new Rect(x + 5, tabY, W - 120, 30), eraScroll,
            new Rect(0, 0, eraNames.Length * 80, 25));
        for (int i = 0; i < eraNames.Length; i++) {
            bool isSel = (i == 0 && selectedEra == -1) || (i > 0 && selectedEra == (i - 1));
            Color old = GUI.color;
            GUI.color = isSel ? Color.yellow : Color.gray;
            if (GUI.Button(new Rect(i * 80, 0, 75, 25), eraNames[i])) {
                selectedEra = i == 0 ? -1 : i - 1;
            }
            GUI.color = old;
        }
        GUI.EndScrollView();

        // ── 左侧：遗物列表 ──
        float listX = x + 5;
        float listY = tabY + 35;
        float listW = 220;
        float listH = H - 100;

        GUI.Box(new Rect(listX, listY, listW, listH), "可发掘遗物");
        relicListScroll = GUI.BeginScrollView(new Rect(listX, listY + 20, listW, listH - 25),
            relicListScroll, new Rect(0, 0, listW - 25, CountVisible() * 60));

        int idx = 0;
        foreach (var tpl in RelicManager.AllTemplates.OrderBy(t => (int)t.Era).ThenBy(t => t.Tier)) {
            if (selectedEra >= 0 && (int)tpl.Era != selectedEra) continue;
            var status = RelicManager.GetStatus(tpl.Id);
            float ty = idx * 60;
            Color oldBg = GUI.backgroundColor;
            bool selected = selectedRelic == tpl.Id;
            GUI.backgroundColor = selected ? Color.green : Color.gray;

            string eraColor = eraColors[(int)tpl.Era + 1];
            string label = $"<color={eraColor}>[{tpl.Era}]</color> {tpl.Name}\n" +
                           $"<size=10>消耗 {tpl.ExcavationCost} 碎片 | {tpl.Type}</size>";
            if (status == ExcavationStatus.Excavated) label += "\n<color=#4CAF50>✅ 已发掘</color>";

            if (GUI.Button(new Rect(2, ty, listW - 30, 55), label)) {
                selectedRelic = tpl.Id;
            }
            GUI.backgroundColor = oldBg;
            idx++;
        }
        GUI.EndScrollView();

        // ── 右侧：详情 + 操作 ──
        float detailX = listX + listW + 10;
        float detailW = W - listW - 20;
        GUI.Box(new Rect(detailX, listY, detailW, listH), "详情");

        if (selectedRelic > 0) {
            var tpl = RelicManager.GetTemplate(selectedRelic);
            if (tpl != null) {
                string eraColor = eraColors[(int)tpl.Era + 1];
                float dy = listY + 25;
                GUI.Label(new Rect(detailX + 5, dy, detailW - 10, 20),
                    $"<color={eraColor}><b>{tpl.Name}</b></color> ({tpl.Type})");
                dy += 22;
                GUI.Label(new Rect(detailX + 5, dy, detailW - 10, 40), tpl.Description);
                dy += 40;
                GUI.Label(new Rect(detailX + 5, dy, detailW - 10, 20),
                    $"发掘成本: <color=#FFD700>{tpl.ExcavationCost} IF碎片</color>");
                dy += 22;
                if (tpl.SpeedMultiplier != 1f)
                    GUI.Label(new Rect(detailX + 5, dy, detailW - 10, 20), $"⚡ 速度: x{tpl.SpeedMultiplier:F2}");
                dy += 18;
                if (tpl.SuccessBonus > 0)
                    GUI.Label(new Rect(detailX + 5, dy, detailW - 10, 20), $"🎯 成功率: +{tpl.SuccessBonus * 100:F0}%");
                dy += 18;
                if (tpl.ByproductChance > 0)
                    GUI.Label(new Rect(detailX + 5, dy, detailW - 10, 20), $"📦 副产物: {tpl.ByproductChance * 100:F0}%");
                dy += 22;

                var status = RelicManager.GetStatus(selectedRelic);
                if (status == ExcavationStatus.Available) {
                    if (GUI.Button(new Rect(detailX + 5, dy, 120, 30), "🔍 发掘")) {
                        var relic = RelicManager.Excavate(selectedRelic);
                        if (relic != null) {
                            statusText = $"发掘成功: {tpl.Name}!";
                            statusTimer = 3f;
                        } else {
                            statusText = "碎片不足!";
                            statusTimer = 2f;
                        }
                    }
                } else if (status == ExcavationStatus.Excavated) {
                    GUI.Label(new Rect(detailX + 5, dy, detailW - 10, 30),
                        "<color=#4CAF50>✅ 已发掘完成</color>");
                }
            }
        }

        // ── 已激活遗物插槽 ──
        float slotY = listY + listH + 10;
        GUI.Box(new Rect(x + 5, slotY, W - 10, 50), "已放置遗物 (最多4个)");
        int planetId = GameMain.localPlanet?.id ?? -1;
        if (planetId > 0) {
            float sx = x + 10;
            for (int i = 0; i < 4; i++) {
                int tid = RelicManager.GetRelicAtSlot(planetId, i);
                string slotLabel = tid > 0 ? $"<color=#4FC3F7>⬡ {RelicManager.GetTemplate(tid)?.Name ?? $"#{tid}"}</color>"
                                           : $"⬡ 空位 {i + 1}";
                if (GUI.Button(new Rect(sx, slotY + 20, 140, 22), slotLabel)) {
                    // 点击打开放置选择器（简化：自动分配第一个未放置的遗物）
                    if (tid <= 0) {
                        foreach (var r in RelicManager.Discovered) {
                            if (r.AssignedPlanet < 0) {
                                RelicManager.AssignRelic(r.TemplateId, planetId, i);
                                statusText = $"放置 {RelicManager.GetTemplate(r.TemplateId)?.Name}";
                                statusTimer = 2f;
                                break;
                            }
                        }
                    }
                }
                sx += 145;
            }
        }

        // ── 共鸣状态 ──
        if (planetId > 0) {
            var res = RelicManager.GetActiveResonance(planetId);
            if (res != null) {
                float resY = slotY + 55;
                GUI.Box(new Rect(x + 5, resY, W - 10, 35),
                    $"<color=#FF6F00>✦ 共鸣激活: {res.Name}</color> — {res.Description}");
            }
        }

        // ── 状态文字 ──
        if (statusTimer > 0) {
            GUI.Label(new Rect(x + 5, y + H - 25, W - 10, 25), statusText);
        }
    }

    private static int CountVisible() {
        int selected = selectedEra;
        return RelicManager.AllTemplates.Count(t => selected < 0 || (int)t.Era == selected);
    }
}
