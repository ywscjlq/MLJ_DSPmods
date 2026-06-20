using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.UI;

namespace FE.Logic.Fractionation.Presentation;

/// <summary>
/// 爽感特效系统 (线程安全版)
/// 所有UI操作通过队列延迟到主线程执行
/// </summary>
public static class SatisfactionFX {
    // ── 线程安全队列: 主线程UI操作 ──
    private static readonly ConcurrentQueue<System.Action> _mainThreadActions = new();
    
    /// <summary>在主线程处理队列 (每帧由 Update 调用)</summary>
    public static void ProcessMainThreadQueue() {
        while (_mainThreadActions.TryDequeue(out var action)) {
            try { action(); } catch (System.Exception e) { Debug.LogWarning($"[SatisfactionFX] UI error: {e.Message}"); }
        }
    }

    // ── 连击系统 (纯数值, 线程安全) ──
    private static readonly ConcurrentDictionary<(int, int), int> _comboCounts = new();
    
    public static int RecordFrac(int planetId, int fractionatorId) {
        var key = (planetId, fractionatorId);
        // 用 TryGetValue + 赋值替代 AddOrUpdate 避免 lambda 委托分配
        if (_comboCounts.TryGetValue(key, out int c)) {
            int next = c + 1;
            _comboCounts[key] = next;
            return next;
        }
        _comboCounts.TryAdd(key, 1);
        return 1;
    }
    
    public static void BreakCombo(int planetId, int fractionatorId) {
        _comboCounts.TryRemove((planetId, fractionatorId), out _);
    }
    
    public static int GetCombo(int planetId, int fractionatorId) {
        return _comboCounts.TryGetValue((planetId, fractionatorId), out int c) ? c : 0;
    }
    
    public static string ComboColor(int combo) {
        return combo switch {
            >= 100 => "#FFD700", >= 50 => "#A855F7", >= 20 => "#60A5FA", >= 10 => "#4ADE80", _ => "#9CA3AF"
        };
    }
    
    // ── ⚡词缀暴走 (线程安全, 仅读写float) ──
    private static float _rampageEndTime = 0f;
    public static bool IsRampage => Time.time < _rampageEndTime;
    
    public static void TriggerRampage() {
        _rampageEndTime = Time.time + 10f;
        _flashEndTime = Time.time + 10f;
        _flashIntensity = 1f;
        EnqueueBigText("⚡⚡⚡ 词缀暴走 x5! ⚡⚡⚡", "FF0000", 2f);
    }
    
    // ── 💰残片雨 (线程安全) ──
    private static float _rainEndTime = 0f;
    public static bool IsFragmentRain => Time.time < _rainEndTime;
    
    public static void TriggerFragmentRain() {
        _rainEndTime = Time.time + 5f;
        EnqueueBigText("💰💰💰 残片雨! x10 💰💰💰", "FFD700", 1.5f);
    }
    
    // ── 红色边框闪烁 (主线程, 由 Update 调用) ──
    private static float _flashEndTime = 0f;
    private static float _flashIntensity = 0f;
    private static Image _flashOverlay = null;
    
    public static void UpdateFlash() {
        if (Time.time >= _flashEndTime) {
            if (_flashOverlay != null) { Object.Destroy(_flashOverlay.gameObject); _flashOverlay = null; _flashIntensity = 0f; }
            return;
        }
        if (_flashOverlay == null) {
            var uiRoot = GameObject.Find("UI Root") ?? GameObject.Find("OverlayCanvas");
            if (uiRoot == null) return;
            var go = new GameObject("RampageFlash");
            go.transform.SetParent(uiRoot.transform, false);
            _flashOverlay = go.AddComponent<Image>();
            _flashOverlay.color = new Color(1f, 0f, 0f, 0f);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(4000f, 2000f);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _flashOverlay.raycastTarget = false;
        }
        float t = (Time.time % 0.6f) / 0.6f;
        float alpha = (t < 0.5f ? t * 2f : 2f - t * 2f) * 0.25f;
        _flashOverlay.color = new Color(1f, 0f, 0f, alpha);
    }
    
    // ── 屏幕震动 (暂禁用: 与镜头控制冲突，保留空壳供外部调用) ──
    public static void UpdateShake() { }
    
    // ── 大字入队 (线程安全: 仅入队, 不创建UI) ──
    public static void EnqueueBigText(string text, string colorHex, float duration = 1.5f) {
        _mainThreadActions.Enqueue(() => ShowBigTextInternal(text, colorHex, duration));
    }
    
    /// <summary>实际创建大字UI (必须在主线程调用)</summary>
    private static void ShowBigTextInternal(string text, string colorHex, float duration) {
        var uiRoot = GameObject.Find("UI Root") ?? GameObject.Find("OverlayCanvas");
        if (uiRoot == null) return;
        var go = new GameObject("SatisfactionBigText");
        go.transform.SetParent(uiRoot.transform, false);
        var textComp = go.AddComponent<Text>();
        textComp.text = $"<color=#{colorHex}>{text}</color>";
        textComp.fontSize = 36;
        textComp.fontStyle = FontStyle.Bold;
        textComp.alignment = TextAnchor.MiddleCenter;
        textComp.horizontalOverflow = HorizontalWrapMode.Overflow;
        textComp.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(600f, 100f);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        var auto = go.AddComponent<BigTextAnimator>();
        auto.duration = duration;
    }
}

public class BigTextAnimator : MonoBehaviour {
    public float duration = 1.5f;
    private float _elapsed = 0f;
    private Text _text;
    private RectTransform _rt;

    void Start() { _text = GetComponent<Text>(); _rt = GetComponent<RectTransform>(); transform.localScale = Vector3.one * 0.5f; }
    void Update() {
        _elapsed += Time.deltaTime;
        float t = _elapsed / duration;
        if (t >= 1f) { Destroy(gameObject); return; }
        float scale = t < 0.3f ? Mathf.Lerp(0.5f, 1.2f, t / 0.3f) : Mathf.Lerp(1.2f, 1.0f, (t - 0.3f) / 0.7f);
        transform.localScale = Vector3.one * scale;
        if (_rt != null) _rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, 80f, t));
        if (_text != null && t > 0.5f) { var c = _text.color; _text.color = new Color(c.r, c.g, c.b, Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f)); }
    }
}
