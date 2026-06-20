using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FE.Logic.Economy;

namespace FE.UI.MainPanel.ResourceInteraction;

/// <summary>
/// 市场走势图渲染器。
/// 在给定区域内用 Texture2D 绘制折线图，展示物品市场价格历史。
/// 自动缓存纹理，仅在数据变化时重绘。
/// </summary>
public static class MarketTrendRenderer
{
    private const int MarginLeft = 45;
    private const int MarginBottom = 22;
    private const int MarginTop = 8;
    private const int MarginRight = 8;

    private static readonly Color BgColor = new(0.15f, 0.15f, 0.15f);
    private static readonly Color GridColor = new(0.25f, 0.25f, 0.25f);

    /// <summary>每条折线的颜色（外部图例引用）</summary>
    public static readonly Color[] LineColors =
    {
        new(0.2f, 0.8f, 1.0f), // 浅蓝
        new(1.0f, 0.6f, 0.2f), // 橙
        new(0.4f, 1.0f, 0.4f), // 浅绿
        new(1.0f, 0.3f, 0.3f), // 红
        new(1.0f, 0.8f, 0.2f), // 黄
        new(0.8f, 0.5f, 1.0f), // 紫
    };

    private static Texture2D cachedTexture;
    private static int cachedVersion = -1;
    private static Rect cachedArea;
    private static int[] cachedItemIds;
    private static string cachedLabel = "";

    /// <summary>
    /// 渲染走势图。应放在 OnGUI 中调用。
    /// </summary>
    public static void Render(Rect area, IList<int> itemIds, string label)
    {
        if (itemIds == null || itemIds.Count == 0) return;

        int currentVersion = MarketValueManager.RefreshVersion;
        bool needRebuild = cachedTexture == null
            || currentVersion != cachedVersion
            || cachedArea != area
            || !SameItems(cachedItemIds, itemIds)
            || cachedLabel != label;

        if (needRebuild)
        {
            RebuildTexture(area, itemIds, label, currentVersion);
        }

        if (cachedTexture != null)
        {
            GUI.DrawTexture(area, cachedTexture);
        }
    }

    private static void RebuildTexture(Rect area, IList<int> itemIds, string label, int version)
    {
        cachedVersion = version;
        cachedArea = area;
        cachedItemIds = itemIds.ToArray();
        cachedLabel = label;

        int w = (int)area.width;
        int h = (int)area.height;
        if (w <= 0 || h <= 0) return;

        if (cachedTexture != null)
            UnityEngine.Object.Destroy(cachedTexture);

        cachedTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        cachedTexture.filterMode = FilterMode.Point;

        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = BgColor;

        int plotL = MarginLeft;
        int plotR = w - MarginRight;
        int plotT = MarginTop;
        int plotB = h - MarginBottom;
        int plotW = plotR - plotL;
        int plotH = plotB - plotT;

        if (plotW <= 1 || plotH <= 1)
        {
            cachedTexture.SetPixels32(pixels);
            cachedTexture.Apply();
            return;
        }

        // 收集数据 & Y 轴范围
        var allData = new List<MarketTrendRecorder.DataPoint[]>();
        float yMin = float.MaxValue, yMax = float.MinValue;

        foreach (int itemId in itemIds)
        {
            var pts = MarketTrendRecorder.GetHistory(itemId);
            if (pts.Length == 0) continue;
            allData.Add(pts);
            foreach (var p in pts)
            {
                if (p.Multiplier < yMin) yMin = p.Multiplier;
                if (p.Multiplier > yMax) yMax = p.Multiplier;
            }
        }

        if (allData.Count == 0)
        {
            cachedTexture.SetPixels32(pixels);
            cachedTexture.Apply();
            return;
        }

        float yRange = yMax - yMin;
        if (yRange < 0.01f) yRange = 0.5f;
        yMin -= yRange * 0.1f;
        yMax += yRange * 0.1f;

        // 网格线
        for (int i = 0; i <= 4; i++)
        {
            float ratio = i / 4f;
            int y = plotB - (int)(ratio * plotH);
            for (int x = plotL; x < plotR; x++)
                SetPixelSafe(pixels, w, h, x, y, GridColor);
        }

        // Y 轴刻度标记
        for (int i = 0; i <= 4; i++)
        {
            float ratio = i / 4f;
            int y = plotB - (int)(ratio * plotH);
            for (int dx = 0; dx < 5; dx++)
                SetPixelSafe(pixels, w, h, plotL - 1 - dx, y, new Color(0.6f, 0.6f, 0.6f));
        }

        // 绘制每条折线
        for (int idx = 0; idx < allData.Count; idx++)
        {
            var pts = allData[idx];
            if (pts.Length < 1) continue;
            Color lineColor = LineColors[idx % LineColors.Length];

            long tickMin = pts[0].Tick;
            long tickMax = pts[pts.Length - 1].Tick;
            long tickRange = tickMax - tickMin;
            if (tickRange < 1) tickRange = 1;

            for (int i = 0; i < pts.Length - 1; i++)
            {
                float x1 = plotL + (float)(pts[i].Tick - tickMin) / tickRange * plotW;
                float y1 = plotB - (float)((pts[i].Multiplier - yMin) / (yMax - yMin)) * plotH;
                float x2 = plotL + (float)(pts[i + 1].Tick - tickMin) / tickRange * plotW;
                float y2 = plotB - (float)((pts[i + 1].Multiplier - yMin) / (yMax - yMin)) * plotH;

                DrawLine(pixels, w, h, (int)x1, (int)y1, (int)x2, (int)y2, lineColor);
            }
        }

        cachedTexture.SetPixels32(pixels);
        cachedTexture.Apply();
    }

    private static void DrawLine(Color32[] pixels, int w, int h, int x0, int y0, int x1, int y1, Color color)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            SetPixelSafe(pixels, w, h, x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void SetPixelSafe(Color32[] pixels, int w, int h, int x, int y, Color color)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        pixels[y * w + x] = color;
    }

    private static bool SameItems(int[] a, IList<int> b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Count) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>
    /// 获取当前 Y 轴范围（用于在外部画刻度标签）。
    /// </summary>
    public static (float min, float max) GetYRange(IList<int> itemIds)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (int id in itemIds)
        {
            var pts = MarketTrendRecorder.GetHistory(id);
            foreach (var p in pts)
            {
                if (p.Multiplier < min) min = p.Multiplier;
                if (p.Multiplier > max) max = p.Multiplier;
            }
        }
        if (min > max) return (0.5f, 2.0f);
        float range = max - min;
        if (range < 0.01f) range = 0.5f;
        return (min - range * 0.1f, max + range * 0.1f);
    }

    /// <summary>
    /// 释放缓存的纹理。
    /// </summary>
    public static void Cleanup()
    {
        if (cachedTexture != null)
        {
            UnityEngine.Object.Destroy(cachedTexture);
            cachedTexture = null;
        }
    }
}
