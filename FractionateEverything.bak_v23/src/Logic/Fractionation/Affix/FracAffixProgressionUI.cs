namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 词缀等级进度UI数据提供者
/// Kapp Ch3: 支架式教学 — 需要视觉指示让玩家感知"最近发展区"
/// Kapp Ch2: 级别 — "挑战-技能平衡"需要玩家可见的进度指示
/// </summary>
public static class FracAffixProgressionUI {
    /// <summary>获取UI显示文本</summary>
    public static string GetLevelDisplay() {
        var level = FracAffixManager.GetPlayerLevel();
        var name = level switch {
            PlayerAffixLevel.Apprentice => "学徒",
            PlayerAffixLevel.Craftsman => "工匠",
            PlayerAffixLevel.Master => "大师",
            PlayerAffixLevel.Grandmaster => "宗师",
            PlayerAffixLevel.Mythic => "神话",
            _ => "???"
        };
        int unlocked = FracAffixProgression.GetUnlockCount(level);
        int total = FracAffixTemplates.All.Count;
        return $"词缀师等级: {name} ({unlocked}/{total} 已解锁)";
    }
    
    /// <summary>获取经验条进度 (0.0~1.0)</summary>
    public static float GetExpProgress() {
        return FracAffixManager.GetPlayerLevelProgress();
    }
    
    /// <summary>获取下一级预览</summary>
    public static string GetNextLevelPreview() {
        var level = FracAffixManager.GetPlayerLevel();
        return FracAffixProgression.GetNextLevelPreview(level);
    }
    
    /// <summary>获取当前可用词缀列表 (含下一阶解锁预览)</summary>
    public static string[] GetUnlockPreview() {
        var level = FracAffixManager.GetPlayerLevel();
        var all = FracAffixTemplates.All;
        var lines = new System.Collections.Generic.List<string>();
        foreach (var t in all) {
            string status = t.UnlockLevel <= (int)level ? "✅" : "🔒";
            lines.Add($"{status} [{t.Faction}] {t.NameKey} (Lv.{t.UnlockLevel})");
        }
        return lines.ToArray();
    }
}
