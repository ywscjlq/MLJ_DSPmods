using System;
using System.Collections.Generic;

namespace FE.Logic.Fractionation.Affix;

/// <summary>
/// 反馈系统 — 分级通知与可视化支持
/// Kapp Ch10: 新手即时反馈，老手延迟通知，避免打断心流
/// Kapp Ch3: 心流理论 — 清晰及时反馈是心流核心条件
/// Kapp Ch2: 反馈 — 12游戏元素之一
/// </summary>
public static class FracAffixFeedback {
    
    #region 3.1 Before/After 对比
    
    /// <summary>生成词缀应用前后的数值对比文本</summary>
    public static string GetBeforeAfterText(
        float oldOutput, float newOutput,
        float oldSpeed, float newSpeed,
        float oldConsume, float newConsume) {
        var lines = new List<string>();
        lines.Add("=== 词缀效果变化 ===");
        lines.Add(string.Format("产出: {0:P0} → {1:P0} ({2:+#;-#})", oldOutput, newOutput, newOutput - oldOutput));
        lines.Add(string.Format("速度: {0:P0} → {1:P0} ({2:+#;-#})", oldSpeed, newSpeed, newSpeed - oldSpeed));
        lines.Add(string.Format("消耗: {0:P0} → {1:P0} ({2:+#;-#})", oldConsume, newConsume, newConsume - oldConsume));
        return string.Join("\n", lines);
    }
    
    #endregion
    
    #region 3.2 节律相位指示器
    
    /// <summary>获取当前潮汐相位描述</summary>
    public static string GetRhythmPhase(float period, float currentTime) {
        float timeInPhase = currentTime % period;
        float timeLeft = period - timeInPhase;
        string phaseLabel;
        
        if (timeInPhase < period * 0.4f)
            phaseLabel = string.Format("[{0:F0}s] 高潮期 (产出↑) {1:F0}s", period, timeInPhase);
        else if (timeLeft < 15f)
            phaseLabel = string.Format("[{0:F0}s] 潮汐将至... {1:F0}s", period, timeLeft);
        else if (timeLeft < period * 0.3f)
            phaseLabel = string.Format("[{0:F0}s] 高潮消退中 {1:F0}s", period, timeLeft);
        else if (timeInPhase < period * 0.1f)
            phaseLabel = string.Format("[{0:F0}s] 谷底蓄力 {1:F0}s", period, timeLeft);
        else
            phaseLabel = string.Format("[{0:F0}s] 低潮期 {1:F0}s", period, timeLeft);
        
        return phaseLabel;
    }
    
    #endregion
    
    #region 3.3 近失提示
    
    /// <summary>混沌惩罚近失提示 (Kapp Ch10: 用近失体验代替惩罚)</summary>
    public static string GetCloseCallText(float penaltySaved) {
        if (penaltySaved >= 0.5f) 
            return "功亏一篑！混沌风暴来袭...但你力挽狂澜，挽回半数以上损失！";
        if (penaltySaved >= 0.3f)
            return "差一点就突破了！混沌爆发中保留了30%积累！";
        return "混沌余波消散...下次会更强";
    }
    
    /// <summary>通知策略 (Kapp Ch10: 新手即时/老手延迟)</summary>
    public static bool ShouldNotifyNow(PlayerAffixLevel level) {
        if (level <= PlayerAffixLevel.Craftsman) return true;
        if (level <= PlayerAffixLevel.Grandmaster)
            return UnityEngine.Random.value < 0.5f;
        return UnityEngine.Random.value < 0.2f;
    }
    
    #endregion
}
