using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FE.UI.MainPanel.Setting;

/// <summary>
/// 遗物考古系统的GUI钩子 - 挂载在RectTransform上处理IMGUI事件
/// </summary>
public class RelicGUIHook : MonoBehaviour {
    private void OnGUI() {
        RelicPage.OnGUI();
    }
}
