using System.Collections.Generic;

namespace FE.Logic.Relic;

/// <summary>遗物时代层级</summary>
public enum RelicEra {
    Primitive = 0,
    Ancient = 1,
    Classical = 2,
    Golden = 3,
}

/// <summary>遗物类型</summary>
public enum RelicType {
    Catalyst,
    Lens,
    Core,
    Glyph,
}

/// <summary>共鸣组合的类型</summary>
public enum ResonanceType {
    None,
    Binary,
    Triad,
    Cross,
}

/// <summary>遗物模板定义（v3.0.0：不再有行星槽位）</summary>
public class RelicTemplate {
    public int Id { get; }
    public string Name { get; }
    public string Description { get; }
    public RelicEra Era { get; }
    public RelicType Type { get; }
    public int ExcavationCost { get; }
    public int Tier { get; }
    public bool RequiresDangerousDig { get; }

    public float SpeedMultiplier { get; set; } = 1f;
    public float SuccessBonus { get; set; } = 0f;
    public float ByproductChance { get; set; } = 0f;
    public int ByproductItemId { get; set; } = -1;

    public RelicTemplate(int id, string name, string desc, RelicEra era, RelicType type,
                         int cost, int tier, bool dangerous = false) {
        Id = id;
        Name = name;
        Description = desc;
        Era = era;
        Type = type;
        ExcavationCost = cost;
        Tier = tier;
        RequiresDangerousDig = dangerous;
    }
}

/// <summary>共鸣组合定义</summary>
public class ResonanceDefinition {
    public ResonanceType Type { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int[] RequiredRelicIds { get; set; }
    public float SpeedBonus { get; set; }
    public float SuccessBonus { get; set; }
    public int ExtraByproductId { get; set; } = -1;
    public float ExtraByproductChance { get; set; }
}
