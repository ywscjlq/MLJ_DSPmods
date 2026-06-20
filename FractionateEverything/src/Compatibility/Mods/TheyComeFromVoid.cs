using System;

namespace FE.Compatibility.Mods;

public static class TheyComeFromVoid {
    public const string GUID = "com.ckcz123.DSP_Battle";
    public static bool Enable;
    public static int GetRelicCount() => 0;
    public static int GetMeritRank() => 0;
    public static int GetSkillLevel() => 0;
    public static bool IsEventActive(int id) => false;
    public static int GetAssignedSkill() => 0;
    public static bool HasActiveEvent(int id) => false;
    public static int GetSkillLevelLeft() => 0;
    public static int GetSkillLevelRight() => 0;
    public static bool TryGetEventProgress(int id, out float progress) { progress = 0; return false; }
    public static int GetUnspentSkillPoints() => 0;
    public static int GetAssignedSkillPointCount() => 0;
    public static bool HasActiveEventChain() => false;
}

