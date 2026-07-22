using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static FE.Logic.Items.ItemManager;

namespace FE.Logic.Economy;

/// <summary>
/// v2.5 AI 智能交易分析引擎。
/// 追踪价格历史、线性回归预测、生成买卖信号。
/// </summary>
public static class MarketAIAnalyzer
{
    private const int MAX_HISTORY = 64;        // 每个物品最多保留64个历史点
    private const int MIN_POINTS_FOR_PRED = 4; // 最少4点才做预测
    private const float SIGNAL_CONFIDENCE_BUY = 0.65f;  // 买入置信阈值
    private const float SIGNAL_CONFIDENCE_SELL = 0.70f; // 卖出置信阈值

    public enum SignalType { Buy, Sell, Neutral }

    public struct PricePoint
    {
        public long Tick;
        public float LastPrice;
        public float BidPrice;
        public float AskPrice;
    }

    public struct TradeSignal
    {
        public int ItemId;
        public string ItemName;
        public SignalType Type;
        public float Confidence;     // 0.0 - 1.0
        public float CurrentPrice;
        public float PredictedPrice;
        public float PriceChange;    // 相对于 DayOpenPrice 的涨跌幅
        public string Reason;
        public long GeneratedTick;

        public string Summary =>
            Type switch
            {
                SignalType.Buy => $"📈 [{ItemName}] 建议买入 (置信{Confidence:P0})",
                SignalType.Sell => $"📉 [{ItemName}] 建议卖出 (置信{Confidence:P0})",
                _ => $"[{ItemName}] 持有观望"
            };
    }

    // priceHistory[itemId] = list of price snapshots
    public static bool Enabled = true;  // v2.5 AI 总开关
    private static readonly Dictionary<int, List<PricePoint>> priceHistory = new();
    private static bool initialized;
    private static long lastRecordTick;

    public static IReadOnlyDictionary<int, List<PricePoint>> PriceHistory => priceHistory;

    /// <summary>初始化：加载已有价格数据</summary>
    public static void Init()
    {
        priceHistory.Clear();
        initialized = true;
    }

    /// <summary>
    /// 记录当前价格快照。每次 RefreshTickers 后调用。
    /// </summary>
    public static void RecordPrices()
    {
        if (!initialized) Init();

        long now = GameMain.gameTick;
        if (now == lastRecordTick) return; // 防重复
        lastRecordTick = now;

        foreach (var kv in ExchangeManager.AllTickers)
        {
            int itemId = kv.Key;
            var ticker = kv.Value;
            if (ticker.LastPrice <= 0f) continue;

            if (!priceHistory.TryGetValue(itemId, out var history))
            {
                history = new List<PricePoint>();
                priceHistory[itemId] = history;
            }

            history.Add(new PricePoint
            {
                Tick = now,
                LastPrice = ticker.LastPrice,
                BidPrice = ticker.BidPrice,
                AskPrice = ticker.AskPrice
            });

            // 裁剪历史
            while (history.Count > MAX_HISTORY)
                history.RemoveAt(0);
        }
    }

    /// <summary>简单线性回归：y = slope * x + intercept</summary>
    private static bool LinearRegression(List<PricePoint> points, int recentCount,
        out float slope, out float intercept, out float rSquared)
    {
        slope = 0f;
        intercept = 0f;
        rSquared = 0f;

        if (points.Count < MIN_POINTS_FOR_PRED) return false;

        int count = Math.Min(recentCount, points.Count);
        int start = points.Count - count;

        float n = count;
        float sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;

        for (int i = start; i < points.Count; i++)
        {
            float x = i - start;          // 用索引当自变量（等距）
            float y = points[i].LastPrice;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
            sumY2 += y * y;
        }

        float denominator = n * sumX2 - sumX * sumX;
        if (Math.Abs(denominator) < 0.0001f) return false;

        slope = (n * sumXY - sumX * sumY) / denominator;
        intercept = (sumY - slope * sumX) / n;

        // R^2 计算
        float yMean = sumY / n;
        float ssTot = sumY2 - n * yMean * yMean;
        float ssRes = 0f;
        for (int i = start; i < points.Count; i++)
        {
            float x = i - start;
            float yPred = slope * x + intercept;
            float diff = points[i].LastPrice - yPred;
            ssRes += diff * diff;
        }
        rSquared = ssTot > 0.0001f ? 1f - ssRes / ssTot : 0f;

        return true;
    }

    /// <summary>
    /// 预测未来 horizon 个周期后的价格。
    /// 用最近 N 个点做线性回归，外推。
    /// </summary>
    public static float PredictPrice(int itemId, int horizon = 3)
    {
        if (!priceHistory.TryGetValue(itemId, out var points) || points.Count < MIN_POINTS_FOR_PRED)
            return -1f;

        if (!LinearRegression(points, Math.Min(points.Count, 16), out float slope, out float intercept, out _))
            return -1f;

        // 外推 horizon 步
        return slope * (points.Count - 1 + horizon) + intercept;
    }

    /// <summary>获取价格变化率（相对于 DayOpenPrice）</summary>
    private static float GetPriceChangeRatio(ExchangeManager.ExchangeTicker ticker)
    {
        if (ticker.DayOpenPrice <= 0f) return 0f;
        return (ticker.LastPrice - ticker.DayOpenPrice) / ticker.DayOpenPrice;
    }

    /// <summary>计算成交量（通过历史记录的点数变化估算活跃度）</summary>
    private static int GetRecentActivity(int itemId, int windowPoints = 8)
    {
        if (!priceHistory.TryGetValue(itemId, out var points) || points.Count < 2)
            return 0;

        int count = Math.Min(windowPoints, points.Count);
        int start = points.Count - count;
        int changes = 0;
        for (int i = start + 1; i < points.Count; i++)
        {
            if (Math.Abs(points[i].LastPrice - points[i - 1].LastPrice) > 0.01f)
                changes++;
        }
        return changes;
    }

    /// <summary>对单个物品生成交易信号</summary>
    public static TradeSignal AnalyzeItem(int itemId)
    {
        var signal = new TradeSignal
        {
            ItemId = itemId,
            ItemName = LDB.items.Select(itemId)?.Name ?? $"#{itemId}",
            Type = SignalType.Neutral,
            GeneratedTick = GameMain.gameTick
        };

        var ticker = ExchangeManager.GetTicker(itemId);
        if (ticker == null || ticker.LastPrice <= 0f || ticker.DayOpenPrice <= 0f)
        {
            signal.Reason = "缺乏行情数据";
            return signal;
        }

        signal.CurrentPrice = ticker.LastPrice;
        signal.PriceChange = GetPriceChangeRatio(ticker);

        // 获取价格历史
        if (!priceHistory.TryGetValue(itemId, out var points) || points.Count < MIN_POINTS_FOR_PRED)
        {
            signal.Reason = "历史数据不足";
            return signal;
        }

        // 线性回归预测
        if (!LinearRegression(points, Math.Min(points.Count, 16), out float slope, out float intercept, out float rSquared))
        {
            signal.Reason = "回归计算失败";
            return signal;
        }

        int nextX = points.Count; // 下一个点的 x
        signal.PredictedPrice = slope * nextX + intercept;

        float baseValue = MarketValueManager.GetValue(itemId);
        float changeRatio = signal.PriceChange;

        // --- 买入信号评估 ---
        float buyScore = 0f;
        var buyReasons = new List<string>();

        // 条件1: 当前价远低于开盘价（跌超30%）
        if (changeRatio < -0.30f)
        {
            buyScore += 0.35f;
            buyReasons.Add($"暴跌{changeRatio:P0}");
        }
        else if (changeRatio < -0.15f)
        {
            buyScore += 0.20f;
            buyReasons.Add($"下跌{changeRatio:P0}");
        }

        // 条件2: 预测上涨
        if (signal.PredictedPrice > signal.CurrentPrice * 1.05f)
        {
            float predictedGain = (signal.PredictedPrice - signal.CurrentPrice) / signal.CurrentPrice;
            buyScore += Math.Min(0.30f, predictedGain * 2f);
            buyReasons.Add($"预测涨{predictedGain:P0}");
        }

        // 条件3: 线性拟合质量好（趋势明确）
        if (rSquared > 0.6f && slope > 0)
        {
            buyScore += 0.15f * rSquared;
            buyReasons.Add($"趋势置信 R^2={rSquared:P0}");
        }

        // 条件4: 活跃度高
        int activity = GetRecentActivity(itemId);
        if (activity >= 4)
        {
            buyScore += 0.10f;
            buyReasons.Add("高活跃度");
        }

        // --- 卖出信号评估 ---
        float sellScore = 0f;
        var sellReasons = new List<string>();

        // 条件1: 暴涨（超过开盘价40%）
        if (changeRatio > 0.40f)
        {
            sellScore += 0.35f;
            sellReasons.Add($"暴涨{changeRatio:P0}");
        }
        else if (changeRatio > 0.20f)
        {
            sellScore += 0.20f;
            sellReasons.Add($"上涨{changeRatio:P0}");
        }

        // 条件2: 预测下跌
        if (signal.PredictedPrice < signal.CurrentPrice * 0.95f)
        {
            float predictedDrop = (signal.CurrentPrice - signal.PredictedPrice) / signal.CurrentPrice;
            sellScore += Math.Min(0.30f, predictedDrop * 2f);
            sellReasons.Add($"预测跌{predictedDrop:P0}");
        }

        // 条件3: 线性拟合质量好（下跌趋势明确）
        if (rSquared > 0.6f && slope < 0)
        {
            sellScore += 0.15f * rSquared;
            sellReasons.Add($"下跌趋势R^2={rSquared:P0}");
        }

        // --- 决定信号 ---
        if (buyScore >= SIGNAL_CONFIDENCE_BUY && buyScore > sellScore)
        {
            signal.Type = SignalType.Buy;
            signal.Confidence = Math.Min(buyScore, 1f);
            signal.Reason = string.Join(", ", buyReasons);
        }
        else if (sellScore >= SIGNAL_CONFIDENCE_SELL && sellScore > buyScore)
        {
            signal.Type = SignalType.Sell;
            signal.Confidence = Math.Min(sellScore, 1f);
            signal.Reason = string.Join(", ", sellReasons);
        }
        else
        {
            signal.Type = SignalType.Neutral;
            signal.Confidence = Math.Max(buyScore, sellScore);
            signal.Reason = "方向不明朗";
        }

        return signal;
    }

    /// <summary>
    /// 获取 Top N 买入机会（按置信度排序）
    /// </summary>
    public static List<TradeSignal> GetTopBuySignals(int count = 5)
    {
        var signals = new List<TradeSignal>();
        foreach (var kv in priceHistory)
        {
            var signal = AnalyzeItem(kv.Key);
            if (signal.Type == SignalType.Buy)
                signals.Add(signal);
        }
        signals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        if (signals.Count > count)
            signals.RemoveRange(count, signals.Count - count);
        return signals;
    }

    /// <summary>
    /// 获取 Top N 卖出机会
    /// </summary>
    public static List<TradeSignal> GetTopSellSignals(int count = 5)
    {
        var signals = new List<TradeSignal>();
        foreach (var kv in priceHistory)
        {
            var signal = AnalyzeItem(kv.Key);
            if (signal.Type == SignalType.Sell)
                signals.Add(signal);
        }
        signals.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        if (signals.Count > count)
            signals.RemoveRange(count, signals.Count - count);
        return signals;
    }


    /// <summary>生成 Unicode 迷你走势图 (▁▂▃▄▅▆▇█)</summary>
    public static string GetSparkline(int itemId, int width = 24)
    {
        if (!priceHistory.TryGetValue(itemId, out var points) || points.Count < 2)
            return string.Empty;

        int start = Math.Max(0, points.Count - width);
        int count = points.Count - start;
        float min = float.MaxValue, max = float.MinValue;
        for (int i = start; i < points.Count; i++)
        {
            float p = points[i].LastPrice;
            if (p < min) min = p;
            if (p > max) max = p;
        }

        if (max - min < 0.01f) return new string('─', count);

        char[] bars = { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };
        var sb = new System.Text.StringBuilder();
        for (int i = start; i < points.Count; i++)
        {
            float t = (points[i].LastPrice - min) / (max - min);
            int idx = (int)(t * 7);
            if (idx < 0) idx = 0; else if (idx > 7) idx = 7;
            sb.Append(bars[idx]);
        }
        return sb.ToString();
    }

    /// <summary>导出价格历史到 CSV（调试用）</summary>

    public static void ExportCSV(string path = "fe_price_history.csv")
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine("ItemId,Name,Tick,LastPrice,BidPrice,AskPrice");
        foreach (var kv in priceHistory)
        {
            string name = LDB.items.Select(kv.Key)?.Name ?? $"#{kv.Key}";
            foreach (var pt in kv.Value)
                sw.WriteLine($"{kv.Key},{name},{pt.Tick},{pt.LastPrice:F2},{pt.BidPrice:F2},{pt.AskPrice:F2}");
        }
    }
}
