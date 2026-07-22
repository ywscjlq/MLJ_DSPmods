using System;
using System.Reflection;
using BepInEx.Bootstrap;
// using static FE.Utils.Utils;

namespace FE.Compatibility.Mods;

public static class GenesisBook {
    public const string GUID = "org.LoShin.GenesisBook";
    public static bool Enable;
    public static Assembly assembly;

    public static void Compatible() {
        Enable = Chainloader.PluginInfos.TryGetValue(GUID, out BepInEx.PluginInfo pluginInfo);
        if (!Enable || pluginInfo == null) return;
        assembly = pluginInfo.Instance.GetType().Assembly;
        // CheckPlugins.LogInfo("GenesisBook Compat finish.");
    }
}
