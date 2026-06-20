using UnityEngine;

namespace FE.UI.MainPanel.Archive;

/// <summary>
/// 分馏箴言 — 加载时随机显示的金句。
/// 文案来自 dev diary 伊卡洛斯的经典吐槽。
/// </summary>
public static class LoadingTips {
    private static readonly string[] Tips = [
        "「只要转得够快，你就能拥有一切。」",
        "「主脑说不能分馏的东西，恰恰是最值得分馏的。」",
        "「蓝图是萌新用的，真正的分馏师只需要运气。」",
        "「我现在的日常：抽卡、收货、在沙滩行星晒太阳。」",
        "「如果黑洞也能转一下的话……」",
        "「分馏的本质不是分离，是创造。」",
        "「萌泪说这个不能转。——但是转了。」",
        "「每一座分馏塔都是一个小型宇宙。」",
        "「当传送带够长的时候，分馏塔就是永动机。」",
        "「数据中心存了三十亿铁块——够重建三个银河系了。」",
        "「分馏教父的日常：看着数字跳动，等待下一次十连。」",
        "「混沌流派不是赌博，是信仰。」",
        "「主脑：伊卡洛斯，你在干什么？我：在转动银河系。」",
        "「等价交换？不，分馏是无中生有。」",
        "「十连不出金？说明你转得还不够快。」",
        "「主脑说我的操作违反了三条物理定律——那是上周的版本。」",
    ];

    private static int _lastIndex = -1;

    /// <summary>随机获取一条箴言（连续两次不重复）</summary>
    public static string GetRandomTip() {
        int idx;
        do {
            idx = Random.Range(0, Tips.Length);
        } while (idx == _lastIndex && Tips.Length > 1);
        _lastIndex = idx;
        return Tips[idx];
    }

    /// <summary>获取带颜色前缀的箴言文本（UI 显示用）</summary>
    public static string GetFormattedTip() {
        return $"<color=#60A5FA>💬 分馏箴言</color> {GetRandomTip()}";
    }
}
