using FE.Logic.Civilization.Technology;
using static FE.Logic.DataCenter.DataCenterInventory;
using static FE.Utils.Utils;

namespace FE.Logic.Civilization.Analysis;

/// <summary>
/// 将阶段基础协议完成后的检索机会转化为唯一的远古文明科技点进度。
/// 每次深层解析同步产出一枚记忆源点，作为稀缺检索货币的主要来源。
/// </summary>
public static class DeepAnalysisService {
    public static bool SubmitOpportunity(out bool awardedPoint) {
        awardedPoint = false;
        AnalysisProgressStore.DeepAnalysisProgress++;
        // 每次深层解析产出一枚记忆源点（稀缺检索货币）
        AddItemToModData(IFE记忆源点, 1);
        int cost = GetNextPointCost();
        if (AnalysisProgressStore.DeepAnalysisProgress < cost) {
            return true;
        }

        AnalysisProgressStore.DeepAnalysisProgress -= cost;
        AncientTechTreeState.AwardPoint();
        awardedPoint = true;
        return true;
    }

    public static int GetNextPointCost() => 2 + AncientTechTreeState.TotalPointsEarned / 3;
}
