using System;
using System.Collections.Generic;
using UnityEngine;
using FE.Logic.Fractionation.Affix;
using static FE.Utils.Utils;

namespace FE.Logic.Fractionation.Adjacency;

/// <summary>
/// 分馏塔建筑联动加成系统
/// 检测相邻FE建筑并计算协同加成
/// </summary>
public static class AdjacencyManager
{
    private const float AdjacentThreshold = 5.5f;
    private const float SameTypeBonusPerNeighbor = 0.05f;
    private const float ComplementaryBonusPerNeighbor = 0.08f;
    private const float ClusterBonus = 0.10f;
    private const float AffixSynergyBonus = 0.12f;  // 同流派词缀跨塔联动额外加成
    private const int MaxNeighbors = 4;

    // 帧缓存：避免同一帧内重复遍历词缀
    private static readonly Dictionary<(int, int), (int frame, AffixFaction faction)> _mainFactionCache = new();
    private static readonly int[] _factionCountsBuf = new int[4];

    public struct AdjacencyInfo
    {
        public int SameTypeCount;
        public int ComplementaryCount;
        /// <summary>相邻塔中同流派词缀匹配数 (跨塔联动)</summary>
        public int AffixSynergyCount;
        public int TotalNeighbors => SameTypeCount + ComplementaryCount;
        public bool IsCluster => TotalNeighbors >= 2;

        public float CalculateBonus()
        {
            float bonus = SameTypeCount * SameTypeBonusPerNeighbor
                        + ComplementaryCount * ComplementaryBonusPerNeighbor
                        + AffixSynergyCount * AffixSynergyBonus;
            if (IsCluster) bonus += ClusterBonus;
            return Math.Min(bonus, 0.8f); // 上限提至80%
        }

        public string Describe()
        {
            if (TotalNeighbors == 0 && AffixSynergyCount == 0) return "";
            float bonus = CalculateBonus();
            var parts = new List<string>();
            if (SameTypeCount > 0) parts.Add($"同调x{SameTypeCount}");
            if (ComplementaryCount > 0) parts.Add($"互补x{ComplementaryCount}");
            if (AffixSynergyCount > 0) parts.Add($"词缀联动x{AffixSynergyCount}");
            if (IsCluster) parts.Add("集群");
            return string.Join("·", parts) + $" +{bonus * 100:F0}%";
        }
    }

    private static bool IsFEProcessingTower(int protoId)
    {
        return protoId >= 8021 && protoId <= 8025;
    }

    /// <summary>
    /// 获取指定分馏塔的主流词缀流派 (帧缓存，返回 true 表示有明确主流派)
    /// </summary>
    private static bool TryGetMainFaction(int planetId, int fracId, out AffixFaction faction)
    {
        faction = AffixFaction.Speed;
        var key = (planetId, fracId);
        int frame = Time.frameCount;
        if (_mainFactionCache.TryGetValue(key, out var cached) && cached.frame == frame)
        {
            faction = cached.faction;
            return faction != AffixFaction.Speed || cached.frame > 0; // Speed=0可能代表无明确主流派
        }

        var affixes = FracAffixManager.GetAffixes(planetId, fracId);
        if (affixes == null || affixes.Count < 2)
        {
            _mainFactionCache[key] = (frame, AffixFaction.Speed);
            return false;
        }

        Array.Clear(_factionCountsBuf, 0, 4);
        foreach (var a in affixes)
        {
            var t = FracAffixTemplates.GetById(a.AffixId);
            _factionCountsBuf[(int)t.Faction]++;
        }
        int best = 0, bestIdx = 0;
        for (int i = 0; i < 4; i++)
        {
            if (_factionCountsBuf[i] > best) { best = _factionCountsBuf[i]; bestIdx = i; }
        }
        if (best < 2) // 不足2件同流派不算主流派
        {
            _mainFactionCache[key] = (frame, AffixFaction.Speed);
            return false;
        }
        var result = (AffixFaction)bestIdx;
        _mainFactionCache[key] = (frame, result);
        faction = result;
        return true;
    }

    public static AdjacencyInfo CheckAdjacency(PlanetFactory factory, int fractionatorId)
    {
        var info = new AdjacencyInfo();
        if (factory == null || factory.entityPool == null || factory.factorySystem == null) return info;

        var fracPool = factory.factorySystem.fractionatorPool;
        if (fracPool == null || fracPool.Length == 0) return info;
        int myEntityId = fracPool[fractionatorId].entityId;
        if (myEntityId <= 0 || myEntityId >= factory.entityCursor) return info;

        Vector3 myPos = factory.entityPool[myEntityId].pos;
        int myProtoId = factory.entityPool[myEntityId].protoId;

        // 获取自己的主流派 (缓存，本帧仅算一次)
        bool hasMyFaction = TryGetMainFaction(factory.planetId, fractionatorId, out var myFaction);

        int neighborCount = 0;
        // 遍历分馏塔池查找相邻塔
        for (int i = 0; i < fracPool.Length && neighborCount < MaxNeighbors; i++)
        {
            if (i == fractionatorId) continue;
            if (fracPool[i].id != i) continue; // 有效分馏塔

            int otherEntityId = fracPool[i].entityId;
            if (otherEntityId <= 0 || otherEntityId >= factory.entityCursor) continue;
            int otherProtoId = factory.entityPool[otherEntityId].protoId;
            if (!IsFEProcessingTower(otherProtoId)) continue;

            float dist = Vector3.Distance(myPos, factory.entityPool[otherEntityId].pos);
            if (dist > AdjacentThreshold) continue;

            neighborCount++;
            if (otherProtoId == myProtoId)
                info.SameTypeCount++;
            else
                info.ComplementaryCount++;

            // 跨塔词缀流派匹配检测 (使用帧缓存)
            if (hasMyFaction && TryGetMainFaction(factory.planetId, i, out var otherFaction)
                && otherFaction == myFaction)
            {
                info.AffixSynergyCount++;
            }
        }

        return info;
    }
}
