using System.IO;
using FE.Logic;
using FE.Logic.Buildings;
using FE.Logic.DarkFog;
using FE.Logic.Economy;
using FE.Logic.Fractionation;
using FE.Logic.Fractionation.Affix;
using FE.Logic.Fractionation.Growth;
using FE.Logic.Fractionation.Process;
using FE.Logic.Fractionation.FracRecipes;
using FE.Logic.Gacha;
using FE.Logic.DataCenter;
using FE.Logic.Progression;
using FE.Logic.Station;
using FE.Logic.VanillaRecipes;
using FE.UI.MainPanel;
using static FE.Utils.Utils;

namespace FE.Lifecycle;

public static class FeatureSaveRegistry {
    public static void Import(BinaryReader r) {
        r.ReadBlocks(
            ("Recipe", RecipeManager.Import),
            ("VanillaRecipes", VanillaRecipeManager.Import),
            ("RecipeGrowth", RecipeGrowthManager.Import),
            ("Building", BuildingManager.Import),
            ("Item", DataCenterInventory.Import),
            ("Process", ProcessManager.Import),
            ("Gacha", GachaManager.Import),
            ("Economy", EconomyManager.Import),
            ("UI", MainWindow.Import),
            ("Station", StationManager.Import),
            ("EasterEgg", EasterEggManager.Import),
            ("DailyDigest", DailyDigest.Import),
            ("ChaosAbyss", ChaosAbyss.Import)
        );
        TechManager.RequestLoadTimeRecipeBaselineApply();
        TechManager.TryApplyLoadTimeRecipeBaselines();
    }

    public static void Export(BinaryWriter w) {
        w.WriteBlocks(
            ("Recipe", RecipeManager.Export),
            ("VanillaRecipes", VanillaRecipeManager.Export),
            ("RecipeGrowth", RecipeGrowthManager.Export),
            ("Building", BuildingManager.Export),
            ("Item", DataCenterInventory.Export),
            ("Process", ProcessManager.Export),
            ("Gacha", GachaManager.Export),
            ("Economy", EconomyManager.Export),
            ("UI", MainWindow.Export),
            ("Station", StationManager.Export),
            ("EasterEgg", EasterEggManager.Export),
            ("DailyDigest", DailyDigest.Export),
            ("ChaosAbyss", ChaosAbyss.Export)
        );
    }

    public static void IntoOtherSave() {
        RecipeManager.IntoOtherSave();
        VanillaRecipeManager.IntoOtherSave();
        RecipeGrowthManager.IntoOtherSave();
        BuildingManager.IntoOtherSave();
        DataCenterInventory.IntoOtherSave();
        ProcessManager.IntoOtherSave();
        GachaManager.IntoOtherSave();
        EconomyManager.IntoOtherSave();
        DarkFogCombatManager.IntoOtherSave();
        MainWindow.IntoOtherSave();
        StationManager.IntoOtherSave();
        EasterEggManager.IntoOtherSave();
        DailyDigest.IntoOtherSave();
        ChaosAbyss.IntoOtherSave();
        TechManager.ResetTechUnlockFlags();
    }
}
