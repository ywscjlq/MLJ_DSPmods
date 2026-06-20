// Feature registration is handled inline:
// - DailyDigest.OnSaveLoaded() → EconomyManager first tick
// - EasterEggManager.CheckMilestones() → EconomyManager.Tick()
// - EasterEggManager.ProcessPending() → EconomyManager.Tick()
// - BlackHoleSpeedBonus → FracAffixManager.SetCurrentBuilding finalSpd
// - Loading tips → FractionatorWindow.Rendering
// - Affix fusion (F key) → FractionatorWindow.Rendering
// - Save/Load → FeatureSaveRegistry
