using HarmonyLib;

namespace FE.Logic.Fractionation.Presentation;

/// <summary>
/// 在物品提示中追加相关分馏配方入口。
/// </summary>
public static class UIItemTipPatch {
    /// <summary>
    /// 如果物品、配方的详情窗口最下面的制作方式有分馏配方，修改对应显示内容。
    /// 当前为 stub，原自定义渲染逻辑已由其他 UI 系统接管。
    /// </summary>
    [HarmonyPatch(typeof(UIRecipeEntry), nameof(UIRecipeEntry.SetRecipe))]
    [HarmonyPrefix]
    public static bool UIRecipeEntry_SetRecipe_Prefix(ref UIRecipeEntry __instance, RecipeProto recipe) {
        return true;
    }
}