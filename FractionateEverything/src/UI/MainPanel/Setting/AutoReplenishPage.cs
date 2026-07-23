using System;
using System.Collections.Generic;
using System.Linq;
using FE.Logic.Economy;
using FE.UI.Foundation.Window;
using UnityEngine;

namespace FE.UI.MainPanel.Setting;

/// <summary>
/// 自动补货设置页面 - 使用IMGUI渲染，精简v3.0.0适配版
/// </summary>
public static class AutoReplenishPage {
    private static AutoReplenishGUIHook hook;
    internal static bool pickingItem;
    internal static int pickTarget = 100;
    internal static bool popupPending;
    internal static float popupPendingX, popupPendingY;
    internal static bool popupOpened;
    internal static string statusText = "";
    internal static float statusTimer;
    private static string itemSearch = "";
    private static string addTargetStr = "100";

    /// <summary>通用入口 - 匹配 MainWindowPageDefinition 委托签名</summary>
    public static void CreateUI(MyWindow _, RectTransform designRoot) {
        if (hook == null) {
            var go = designRoot.gameObject;
            hook = go.GetComponent<AutoReplenishGUIHook>();
            if (hook == null) hook = go.AddComponent<AutoReplenishGUIHook>();
        }
    }

    public static void UpdateUI() { }

    internal sealed class AutoReplenishGUIHook : MonoBehaviour {
        private Vector2 scrollPos = Vector2.zero;
        private Vector2 logScrollPos = Vector2.zero;
        private string addItemIdStr = "";
        private const int RowH = 22;

        private void OnGUI() {
            if (!enabled) return;
            const float W = 580;
            float x = Screen.width / 2f - W / 2f - 80;
            float y = Screen.height / 2f - 280f;
            float w = W;

            DrawHeader(ref x, ref y, w);
            y = DrawSettings(x, y + 5, w);
            y = DrawItemList(x, y + 5, w);
            DrawLogs(x, y + 5, w);
        }

        private void DrawHeader(ref float x, ref float y, float w) {
            int lv = AutoReplenishManager.UnlockLevel;
            GUI.Label(new Rect(x, y, w, RowH),
                $"自动补货系统 (解锁等级: {lv})  碎片: {AutoReplenishManager.TotalBudget}");
            y += RowH;
            if (!string.IsNullOrEmpty(statusText)) {
                statusTimer -= Time.deltaTime;
                GUI.Label(new Rect(x, y, w, RowH), statusText);
                y += RowH;
                if (statusTimer <= 0) statusText = "";
            }
        }

        private float DrawSettings(float x, float y, float w) {
            // 预留
            string rs = GUI.TextField(new Rect(x, y, 60, RowH),
                AutoReplenishManager.ReserveFragmentCount.ToString(), 8);
            if (int.TryParse(rs, out int rv)) AutoReplenishManager.ReserveFragmentCount = rv;
            GUI.Label(new Rect(x + 64, y, 40, RowH), "预留");

            // 扫描
            if (GUI.Button(new Rect(x + 110, y, 50, RowH), "扫描")) {
                int added = AutoReplenishManager.ScanAndAddFrequentItems();
                statusText = $"扫描: +{added}项";
                statusTimer = 3f;
            }
            if (GUI.Button(new Rect(x + 164, y, 50, RowH), "补货")) {
                int cnt = AutoReplenishManager.ForceReplenishAll();
                statusText = $"强制补货: {cnt}件";
                statusTimer = 3f;
            }
            if (GUI.Button(new Rect(x + 218, y, 50, RowH), "去重")) {
                int cnt = AutoReplenishManager.RemoveDuplicates();
                statusText = $"去重: {cnt}项";
                statusTimer = 3f;
            }

            bool autoScan = GUI.Toggle(new Rect(x + w - 60, y, 60, RowH),
                AutoReplenishManager.AutoScanEnabled, "自动");
            AutoReplenishManager.AutoScanEnabled = autoScan;
            y += RowH + 2;

            // 添加
            GUI.Label(new Rect(x, y, 50, RowH), "添加:");
            string aId = GUI.TextField(new Rect(x + 50, y, 80, RowH), addItemIdStr, 8);
            addItemIdStr = aId;
            GUI.Label(new Rect(x + 134, y, 30, RowH), "x");
            string at = GUI.TextField(new Rect(x + 164, y, 50, RowH), addTargetStr, 8);
            addTargetStr = at;
            if (GUI.Button(new Rect(x + 218, y, 30, RowH), "添加")) {
                if (int.TryParse(addItemIdStr, out int aid) && aid > 0
                    && int.TryParse(addTargetStr, out int tgt) && tgt > 0) {
                    AutoReplenishManager.AddEntry(aid, tgt, true);
                    statusText = $"已添加 #{aid} x{tgt}";
                    statusTimer = 3f;
                    addItemIdStr = "";
                    addTargetStr = "100";
                }
            }
            y += RowH + 4;
            return y;
        }

        private float DrawItemList(float x, float y, float w) {
            var allEntries = AutoReplenishManager.Entries;
            scrollPos = GUI.BeginScrollView(new Rect(x, y, w, Mathf.Min(allEntries.Count * RowH + 10, 300)),
                scrollPos, new Rect(0, 0, w - 20, allEntries.Count * RowH));

            int drawn = 0;
            for (int i = 0; i < allEntries.Count; i++) {
                var e = allEntries[i];
                var p = LDB.items.Select(e.ItemId);
                if (p == null) continue;

                float iy = drawn * RowH;
                bool en = GUI.Toggle(new Rect(0, iy, 20, RowH), e.Enabled, "");
                if (en != e.Enabled) e.Enabled = en;
                GUI.Label(new Rect(22, iy, 40, RowH), $"{e.ItemId}");
                GUI.Label(new Rect(64, iy, 120, RowH), p.Name ?? $"#{e.ItemId}");
                string ts = GUI.TextField(new Rect(190, iy, 50, RowH), e.TargetCount.ToString(), 8);
                if (int.TryParse(ts, out int tv) && tv != e.TargetCount) e.TargetCount = tv;

                long stock = 0;
                if (FE.Logic.DataCenter.DataCenterInventory.centerItemCount != null &&
                    e.ItemId < FE.Logic.DataCenter.DataCenterInventory.centerItemCount.Length) {
                    stock = FE.Logic.DataCenter.DataCenterInventory.centerItemCount[e.ItemId];
                }
                GUI.Label(new Rect(246, iy, 60, RowH), $"库存:{stock}");
                if (GUI.Button(new Rect(w - 60, iy, 40, RowH), "删除")) {
                    AutoReplenishManager.RemoveEntry(e.ItemId);
                    break;
                }
                drawn++;
            }
            GUI.EndScrollView();
            return y + Mathf.Min(drawn * RowH + 10, 300) + 5;
        }

        private void DrawLogs(float x, float y, float w) {
            long cumSell = AutoReplenishManager.TotalSellFragments;
            long cumBuy = AutoReplenishManager.TotalBuyFragments;
            GUI.Label(new Rect(x, y, w, RowH),
                $"累计: 卖+{cumSell}f / 买-{cumBuy}f  |  净: {cumSell - cumBuy}f");
            y += RowH;

            var evts = AutoReplenishManager.GetRecentEvents();
            logScrollPos = GUI.BeginScrollView(new Rect(x, y, w, 150),
                logScrollPos, new Rect(0, 0, w - 16, (evts.Count + 1) * RowH));
            float iy = 0;
            foreach (var ev in evts) {
                long t = ev.Tick;
                int sec = (int)(t / 60 % 60);
                int min = (int)(t / 3600 % 60);
                int hr = (int)(t / 216000);
                var p = LDB.items.Select(ev.ItemId);
                string en = p?.Name ?? $"#{ev.ItemId}";
                string action = ev.IsSell ? $"卖出 x{ev.SoldCount} +{ev.FragCost}f"
                    : $"买入 x{ev.BoughtCount} -{ev.FragCost}f";
                GUI.Label(new Rect(0, iy, w - 16, RowH),
                    $"  [{hr:D2}:{min:D2}:{sec:D2}] {en} {action}");
                iy += RowH;
            }
            GUI.EndScrollView();

            if (popupPending && pickingItem) {
                popupPending = false;
                Rect pr = new(popupPendingX, popupPendingY, 300, 500);
                PopupItemSelector(pr);
            }
        }

        private void PopupItemSelector(Rect rect) {
            GUI.Box(rect, "");
            float px = rect.x + 5, py = rect.y + 5, pw = rect.width - 10;
            string sf = GUI.TextField(new Rect(px, py, pw - 60, RowH), itemSearch, 30);
            itemSearch = sf;
            if (GUI.Button(new Rect(px + pw - 55, py, 50, RowH), "确定")) {
                popupOpened = false;
                pickingItem = false;
            }

            float ly = py + RowH + 5;
            GUI.BeginGroup(new Rect(px, ly, pw, rect.height - ly - 10));
            float iy = 0;
            int shown = 0;
            int maxId = Mathf.Min(FE.Logic.DataCenter.DataCenterInventory.centerItemCount?.Length ?? 2000, 2000);
            for (int id = 1; id < maxId && shown < 50; id++) {
                if (!LDB.items.Exist(id)) continue;
                var p = LDB.items.Select(id);
                if (p == null) continue;
                if (!string.IsNullOrEmpty(itemSearch) &&
                    p.Name.IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (GUI.Button(new Rect(0, iy, pw - 10, RowH), $"{id}: {p.Name}")) {
                    AutoReplenishManager.AddEntry(p.ID, pickTarget, true);
                    statusText = $"已添加: {p.Name} x{pickTarget}";
                    statusTimer = 5f;
                    pickingItem = false;
                    popupOpened = false;
                }
                iy += RowH;
                shown++;
            }
            GUI.EndGroup();
        }
    }
}
