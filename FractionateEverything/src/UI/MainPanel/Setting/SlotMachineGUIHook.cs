using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FE.UI.MainPanel.Setting;

/// <summary>
/// 分馏老虎机GUI钩子
/// </summary>
public class SlotMachineGUIHook : MonoBehaviour {
    private void OnGUI() {
        SlotMachine.OnGUI();
    }
}
