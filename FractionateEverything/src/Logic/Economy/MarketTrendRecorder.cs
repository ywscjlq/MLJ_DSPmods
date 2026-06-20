using System.Collections.Generic;
using System.Linq;
using FE.Logic.Economy;

namespace FE.Logic.Economy;

public static class MarketTrendRecorder
{
    public const int MaxDataPoints = 60;

    public struct DataPoint
    {
        public long Tick;
        public float Multiplier;
        public float Value;
    }

    private static readonly Dictionary<int, Queue<DataPoint>> histories = new();
    private static int lastKnownVersion = -1;
    private static int[] trackedItems = System.Array.Empty<int>();

    public static void Init()
    {
        histories.Clear();
        lastKnownVersion = MarketValueManager.RefreshVersion;
        // 自动获取当前热门物品
        trackedItems = MarketValueManager.GetTopMarketItems(6, descending: true).ToArray();
        RecordSnapshot();
    }

    public static void Tick()
    {
        if (trackedItems.Length == 0) return;
        int version = MarketValueManager.RefreshVersion;
        if (version == lastKnownVersion) return;
        lastKnownVersion = version;
        RecordSnapshot();
    }

    private static void RecordSnapshot()
    {
        long tick = GameMain.gameTick;
        foreach (int itemId in trackedItems)
        {
            var dp = new DataPoint
            {
                Tick = tick,
                Multiplier = MarketValueManager.GetMultiplier(itemId),
                Value = MarketValueManager.GetValue(itemId),
            };

            if (!histories.TryGetValue(itemId, out var q))
            {
                q = new Queue<DataPoint>(MaxDataPoints);
                histories[itemId] = q;
            }

            if (q.Count >= MaxDataPoints)
                q.Dequeue();
            q.Enqueue(dp);
        }
    }

    public static DataPoint[] GetHistory(int itemId)
    {
        return histories.TryGetValue(itemId, out var q)
            ? q.ToArray()
            : System.Array.Empty<DataPoint>();
    }

    public static bool HasData(int itemId) => histories.ContainsKey(itemId);
    public static IReadOnlyList<int> TrackedItems => trackedItems;
}
