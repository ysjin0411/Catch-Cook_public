using TMPro;
using UnityEngine;

/// <summary>
/// 기존 이름 영역과 정렬을 유지하면서, 영역보다 긴 음식 이름만 왼쪽으로 반복 이동합니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.UI.RectMask2D))]
public sealed class SushiDishNameMarquee : MonoBehaviour
{
    // 고정 표시 영역 안에서 움직일 기존 이름 텍스트입니다.
    [SerializeField] TMP_Text _text;
    // 초당 이동하는 Canvas 기준 거리입니다.
    [SerializeField, Min(1f)] float _speed = 35f;
    // 첫 글자를 읽을 수 있도록 이동 전에 기다리는 시간입니다.
    [SerializeField, Min(0f)] float _startDelay = 1f;
    // 마지막 글자를 읽을 수 있도록 이동 후에 기다리는 시간입니다.
    [SerializeField, Min(0f)] float _endDelay = 1f;
    // 화면에서 지정한 원래 폰트 크기입니다.
    [SerializeField] float _fontSize = 28f;
    // 화면에서 지정한 가로·세로 정렬입니다.
    [SerializeField] TextAlignmentOptions _alignment = TextAlignmentOptions.Center;
    // 기존 텍스트 여백을 유지합니다.
    [SerializeField] Vector4 _margin;
    // 글자가 보이는 고정 영역입니다.
    RectTransform _viewport;
    // 마지막 배치에 사용한 이름입니다.
    string _lastText;
    // 마지막 배치에 사용한 폰트입니다.
    TMP_FontAsset _lastFont;
    // 마지막 배치에 사용한 영역 크기입니다.
    Vector2 _lastSize;
    // 마지막 글자를 표시하기 위해 이동할 거리입니다.
    float _distance;
    // 현재 반복 구간에서 지난 시간입니다.
    float _elapsed;
    // 데이터나 스타일 변경 후 배치를 다시 계산할지 나타냅니다.
    bool _dirty = true;
    // 기존 View가 연결한 텍스트를 그대로 제공합니다.
    public TMP_Text Text => _text;

    /// <summary>일반 텍스트와 슬라이드 영역 모두에서 기존 이름 텍스트를 찾습니다.</summary>
    public static TMP_Text FindText(Transform parent, string name)
    {
        // 최초에는 직접 자식이며, 연결 후에는 같은 이름의 표시 영역 안에 있습니다.
        Transform target = parent.Find(name);
        if (target == null) return null;
        var marquee = target.GetComponent<SushiDishNameMarquee>();
        return marquee != null ? marquee.Text : target.GetComponent<TMP_Text>();
    }

    /// <summary>기존 레이아웃 코드가 움직이는 글자 대신 고정 표시 영역을 배치하도록 합니다.</summary>
    public static RectTransform LayoutRect(RectTransform textRect)
    {
        // 연결되지 않은 이름은 기존 RectTransform을 그대로 사용합니다.
        if (textRect == null) return null;
        var marquee = textRect.parent != null
            ? textRect.parent.GetComponent<SushiDishNameMarquee>() : null;
        return marquee != null && marquee.Text != null &&
            marquee.Text.rectTransform == textRect
            ? (RectTransform)marquee.transform : textRect;
    }

    /// <summary>기존 TMP 참조를 보존하고 고정 표시 영역을 한 번만 생성하여 연결합니다.</summary>
    public static void Configure(TMP_Text text)
    {
        // 이름 텍스트가 없는 화면은 처리하지 않습니다.
        if (text == null) return;
        RectTransform rect = text.rectTransform;
        var marquee = rect.parent != null
            ? rect.parent.GetComponent<SushiDishNameMarquee>() : null;
        if (marquee == null)
        {
            // 같은 이름과 배치를 가진 부모 창을 만들고 기존 텍스트를 그 안으로 옮깁니다.
            var window = new GameObject(text.name, typeof(RectTransform),
                typeof(UnityEngine.UI.RectMask2D));
            var viewport = (RectTransform)window.transform;
            viewport.SetParent(rect.parent, false);
            viewport.SetSiblingIndex(rect.GetSiblingIndex());
            viewport.anchorMin = rect.anchorMin;
            viewport.anchorMax = rect.anchorMax;
            viewport.pivot = rect.pivot;
            viewport.sizeDelta = rect.sizeDelta;
            viewport.anchoredPosition3D = rect.anchoredPosition3D;
            viewport.localRotation = rect.localRotation;
            viewport.localScale = rect.localScale;
            window.layer = text.gameObject.layer;
            rect.SetParent(viewport, false);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            marquee = window.AddComponent<SushiDishNameMarquee>();
        }

        // 화면별 원래 크기와 정렬을 받아 사용하며 중앙 정렬로 강제하지 않습니다.
        marquee._text = text;
        marquee._viewport = (RectTransform)marquee.transform;
        bool changed = marquee._fontSize != text.fontSize ||
            marquee._alignment != text.alignment || marquee._margin != text.margin;
        marquee._fontSize = text.fontSize;
        marquee._alignment = text.alignment;
        marquee._margin = text.margin;
        marquee._dirty |= changed;

        // 글자를 줄이거나 말줄임표로 자르지 않고 부모 창에서만 가립니다.
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.maskable = true;
        text.raycastTarget = false;
    }

    /// <summary>다시 열린 화면은 이름의 처음부터 재생합니다.</summary>
    void OnEnable()
    {
        // 복제된 카드도 자신의 표시 영역을 사용합니다.
        _viewport = (RectTransform)transform;
        _dirty = true;
    }

    /// <summary>이름이나 영역이 바뀌면 너비를 갱신하고 긴 이름만 이동합니다.</summary>
    void LateUpdate()
    {
        // 표시할 텍스트가 없거나 숨겨진 경우에는 진행하지 않습니다.
        if (_text == null || !_text.isActiveAndEnabled) return;
        Vector2 size = _viewport.rect.size;
        if (size.x <= 0f || size.y <= 0f) return;

        // 너비 측정은 텍스트·폰트·표시 영역이 변경된 경우에만 수행합니다.
        if (_dirty || _lastText != _text.text || _lastFont != _text.font ||
            (_lastSize - size).sqrMagnitude > 0.01f)
            RefreshLayout(size);
        if (_distance <= 0f) return;

        // 시작 대기 → 왼쪽 이동 → 끝 대기를 반복하며 게임 일시정지에도 동작합니다.
        _elapsed += Time.unscaledDeltaTime;
        float duration = _distance / Mathf.Max(1f, _speed);
        float start = Mathf.Max(0f, _startDelay);
        float cycle = start + duration + Mathf.Max(0f, _endDelay);
        float progress = Mathf.Clamp01(((_elapsed % cycle) - start) / duration);
        _text.rectTransform.anchoredPosition = new Vector2(-_distance * progress, 0f);
    }

    /// <summary>기존 크기와 정렬로 한 줄의 실제 너비를 측정하고 이동 거리를 계산합니다.</summary>
    void RefreshLayout(Vector2 size)
    {
        // 짧은 이름과 긴 이름 모두 화면의 원래 크기·정렬·여백을 사용합니다.
        _text.enableAutoSizing = false;
        _text.fontSize = _fontSize;
        _text.alignment = _alignment;
        _text.margin = _margin;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.overflowMode = TextOverflowModes.Overflow;

        // TMP가 계산한 여백 포함 너비가 창을 넘는 만큼만 이동합니다.
        float width = Mathf.Ceil(_text.GetPreferredValues(
            _text.text, Mathf.Infinity, Mathf.Infinity).x);
        _distance = Mathf.Max(0f, width - size.x);

        // 글자 영역만 확장하며 고정 창의 위치·크기와 텍스트 정렬은 유지합니다.
        RectTransform rect = _text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(Mathf.Max(size.x, width), size.y);
        rect.anchoredPosition = Vector2.zero;

        // 이름이 바뀌거나 화면 크기가 달라지면 첫 글자부터 다시 시작합니다.
        _lastText = _text.text;
        _lastFont = _text.font;
        _lastSize = size;
        _elapsed = 0f;
        _dirty = false;
    }
}

