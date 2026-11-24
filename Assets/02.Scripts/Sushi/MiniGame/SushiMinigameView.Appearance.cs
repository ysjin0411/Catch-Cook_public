using UnityEngine;

/// <summary>손님이 주문한 음료에 맞춰 미니게임 표시 설정을 적용합니다.</summary>
public sealed partial class SushiMinigameView
{
    [Header("최종 미니게임 화면 리소스")]
    [SerializeField] Sprite _finalMinigameBackground, _finalMinigameBoard, _finalMinigameTextSlot, _finalMinigameArrow;
    [SerializeField] Sprite _finalMinigameRounded;
    const float FinalGaugeWidth = 480f, FinalTargetWidth = 140f;

    // 표시만 수정하며 입력, 물량 계산, 판정 및 실제 음료 설정은 그대로 사용합니다.
    void ApplyUpdatedDrinkLayout()
    {
        if (_panel == null || _drinkArtwork == null) return;
        var window = _panel.transform.Find("CameraWindow");
        var inner = window != null ? window.Find("WindowInner") : null;
        if (inner == null) return;
        void Place(Transform target, float x, float y, float width, float height)
        {
            if (!(target is RectTransform rect)) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        }
        void Picture(Transform target, Sprite sprite)
        {
            if (target == null || sprite == null) return;
            var image = target.GetComponent<UnityEngine.UI.Image>();
            if (image == null) return;
            image.enabled = true; image.sprite = sprite; image.type = UnityEngine.UI.Image.Type.Simple;
            image.color = Color.white; image.preserveAspect = false; image.raycastTarget = false;
        }
        void Text(TMPro.TMP_Text text, float size, Color color)
        {
            if (text == null) return;
            text.fontSize = size; text.enableAutoSizing = false; text.color = color;
            text.alignment = TMPro.TextAlignmentOptions.Midline; text.margin = Vector4.zero;
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            text.overflowMode = TMPro.TextOverflowModes.Overflow; text.raycastTarget = false;
        }
        void Rounded(UnityEngine.UI.Image image, Color color)
        {
            if (image == null) return;
            if (_finalMinigameRounded != null) image.sprite = _finalMinigameRounded;
            image.enabled = true; image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2.2f; image.color = color; image.raycastTarget = false;
        }
        var cream = new Color32(253, 247, 228, 255);
        Place(window, 0, 0, 560, 926); Place(inner, 0, 0, 560, 926);
        Picture(window, _finalMinigameBackground);
        var backdropOverlay = _panel.transform.Find("Backdrop");
        if (backdropOverlay != null && backdropOverlay.TryGetComponent<UnityEngine.UI.Image>(out var dim))
        { dim.color = new Color(0f, 0f, 0f, .5f); dim.raycastTarget = true; }
        var legacyStart = inner.Find("StartDisplay");
        if (legacyStart != null) legacyStart.gameObject.SetActive(false);
        if (inner.TryGetComponent<UnityEngine.UI.Image>(out var innerImage)) innerImage.enabled = false;
        var title = inner.Find("DrinkTitle");
        Place(title, 0, 406, 480, 44);
        if (title != null && title.TryGetComponent<TMPro.TMP_Text>(out var heading))
        { heading.text = "완벽하게 음료를 따르자!"; Text(heading, 30, new Color32(116, 76, 65, 255)); }
        var header = inner.Find("InstructionHeader");
        Place(header, 0, 329, 480, 60); Picture(header, _finalMinigameTextSlot);
        if (_description != null) { Place(_description.transform, 0, 0, 460, 52); Text(_description, 24, cream); }
        var viewport = inner.Find("CameraViewport");
        Place(viewport, 0, 29, 484, 500); Picture(viewport, _finalMinigameBoard);
        Place(_drinkArtwork.transform, 0, 0, 484, 500);
        if (_drinkArtwork.TryGetComponent<UnityEngine.UI.Image>(out var artworkImage)) artworkImage.enabled = false;
        // Maintain the existing simulation coordinate system and vessel geometry.
        Place(_fluidPlane, 0, 4, 400, 480);
        var timer = _drinkArtwork.transform.Find("TimerBackground");
        if (timer != null)
        {
            if (timer.TryGetComponent<UnityEngine.UI.HorizontalLayoutGroup>(out var group)) group.enabled = false;
            if (timer.TryGetComponent<UnityEngine.UI.ContentSizeFitter>(out var fitter)) fitter.enabled = false;
            Place(timer, 0, 205, 120, 50);
            if (timer.TryGetComponent<UnityEngine.UI.Image>(out var timerImage)) timerImage.color = Color.clear;
            Place(timer.Find("TimerIcon"), -29, 0, 26, 32);
            if (timer.Find("TimerIcon").TryGetComponent<UnityEngine.UI.Image>(out var icon)) { icon.color = cream; icon.preserveAspect = true; }
            Place(_remainingTimeText != null ? _remainingTimeText.transform : null, 18, 0, 48, 40);
            Text(_remainingTimeText, 24, cream);
        }
        _completionSuccessColor = new Color32(0, 232, 136, 255);
        _completionFailureColor = new Color32(255, 89, 89, 255);
        if (_completionHint != null)
        {
            var parent = _completionHint.transform.parent;
            var backdrop = parent.Find("CompletionHintBackground");
            if (backdrop == null)
            {
                var go = new GameObject("CompletionHintBackground", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                go.transform.SetParent(parent, false); backdrop = go.transform;
            }
            Place(backdrop, 0, -212, 400, 46); Picture(backdrop, _finalMinigameTextSlot);
            backdrop.SetAsLastSibling(); _completionHint.transform.SetAsLastSibling();
            Place(_completionHint.transform, 0, -212, 388, 46);
            _completionHint.text = "제한 시간 뒤 총 생성량으로 판정됩니다.";
            Text(_completionHint, 22, new Color32(234, 201, 179, 255));
        }
        if (_bar != null)
        {
            var track = _bar.name == "Track" ? _bar : _bar.Find("Track");
            var barRoot = _bar.name == "Track" ? _bar.parent : _bar;
            Place(barRoot, 0, -256, 480, 20); Place(track, 0, 0, 480, 20);
            if (barRoot.TryGetComponent<UnityEngine.UI.Image>(out var background)) background.color = Color.clear;
            Rounded(track != null ? track.GetComponent<UnityEngine.UI.Image>() : null, new Color32(111, 60, 49, 255));
            Rounded(_targetZone != null ? _targetZone.GetComponent<UnityEngine.UI.Image>() : null, new Color32(239, 177, 0, 255));
            var targetInner = _targetZone != null ? _targetZone.Find("TargetInner") : null;
            if (targetInner != null && targetInner.TryGetComponent<UnityEngine.UI.Image>(out var fill)) fill.color = Color.clear;
            SetTargetRange(_targetRange.x, _targetRange.y);
            Place(_arrow, 0, -22.5f, 36, 25);
            var outline = _arrow != null ? _arrow.Find("Outline") : null;
            Place(outline, 0, 0, 36, 25); Picture(outline, _finalMinigameArrow);
            var arrowFill = _arrow != null ? _arrow.Find("Fill") : null;
            if (arrowFill != null) arrowFill.gameObject.SetActive(false);
        }
        if (_resultRoot != null) Place(_resultRoot.transform, 0, 0, 560, 926);
        if (_resultStamp != null) { Place(_resultStamp.transform, 175, -350, 180, 176); _resultStamp.preserveAspect = true; }
        if (_resultText != null) _resultText.gameObject.SetActive(false);
    }

    // The 140 px display band maps exactly to the original ideal gauge bounds.
    // This changes only the presentation, not tap input, flow, or success thresholds.
    float DrinkGaugeDisplayPosition(float value)
    {
        float left = (1f - FinalTargetWidth / FinalGaugeWidth) * .5f, right = 1f - left;
        if (value < _targetRange.x) return Mathf.Lerp(0f, left, Mathf.InverseLerp(0f, _targetRange.x, value));
        if (value > _targetRange.y) return Mathf.Lerp(right, 1f, Mathf.InverseLerp(_targetRange.y, 1f, value));
        return Mathf.Lerp(left, right, Mathf.InverseLerp(_targetRange.x, _targetRange.y, value));
    }

    // 음료 다섯 종류의 스타일 에셋을 등록합니다.
    [SerializeField] DrinkMinigameStyle[] _drinkStyles;
    // 에이드, 맥주, 차의 용기 연결 묶음을 등록합니다.
    [SerializeField] DrinkVesselView[] _drinkVessels;

    /// <summary>화면을 열기 전에 음료 색상, 용기와 시뮬레이션 설정을 적용합니다.</summary>
    public bool TryConfigureDrink(Sushi menu)
    {
        // 열린 화면이나 준비 중인 라운드의 설정을 교체하지 않습니다.
        if (IsOpen || _drinkPreparing || _drinkController == null ||
            _fluidRenderer == null || _drinkStyles == null || _drinkVessels == null)
            return false;

        // 현재 주문과 동일한 메뉴 에셋을 가진 설정을 찾습니다.
        DrinkMinigameStyle selectedStyle = null;
        foreach (DrinkMinigameStyle style in _drinkStyles)
        {
            // null 메뉴와 누락된 스타일이 서로 일치하는 것으로 처리하지 않습니다.
            if (style == null || style.Menu == null || style.Menu != menu) continue;
            // 일치한 설정 하나를 사용합니다.
            selectedStyle = style;
            break;
        }

        // 필수 에셋이 빠진 음료는 이전 음료의 모습으로 시작하지 않습니다.
        if (selectedStyle == null || selectedStyle.Config == null || selectedStyle.PourSprite == null)
        {
            // Inspector에서 누락된 메뉴 설정을 찾을 수 있도록 알립니다.
            Debug.LogWarning("주문 음료의 Style, Config, Pour Sprite 연결을 확인하세요.", this);
            return false;
        }

        // 해당 용기 종류의 실제 UI 연결 묶음을 찾습니다.
        DrinkVesselView selectedVessel = null;
        foreach (DrinkVesselView vessel in _drinkVessels)
        {
            // 준비된 동일 종류의 연결 묶음만 선택합니다.
            if (vessel == null || !vessel.IsConfigured || vessel.Kind != selectedStyle.VesselKind) continue;
            // 찾은 묶음은 아래에서 표시 상태를 변경할 때 사용합니다.
            selectedVessel = vessel;
            break;
        }

        // UI 연결 또는 컨트롤러 설정 변경에 실패하면 화면을 시작하지 않습니다.
        if (selectedVessel == null || !_drinkController.TrySetConfig(selectedStyle.Config)) return false;

        // 선택한 종류만 켜서 이전 컵과 현재 컵이 겹치지 않게 합니다.
        foreach (DrinkVesselView vessel in _drinkVessels)
        {
            // 누락된 배열 요소는 건너뜁니다.
            if (vessel != null) vessel.SetVisible(vessel == selectedVessel);
        }

        // 음료별 원본 색이 포함된 따르는 잔 이미지를 연결합니다.
        selectedVessel.SetPourStyle(selectedStyle);
        // 기존 회전 및 발사 좌표 계산이 선택한 용기의 기준점을 사용하게 합니다.
        _bottlePivot = selectedVessel.Pivot;
        _bottleMouth = selectedVessel.Mouth;
        _bottleDirection = selectedVessel.Direction;
        // 선택한 용기에 맞춘 세 단계 회전값을 사용합니다.
        _bottleLowAngle = selectedVessel.Angles.x;
        _bottleIdealAngle = selectedVessel.Angles.y;
        _bottleHighAngle = selectedVessel.Angles.z;
        // 준비 연출부터 이전 라운드의 회전이 남지 않도록 기준 각도로 배치합니다.
        _bottlePivot.localRotation = Quaternion.Euler(0f, 0f, _bottleIdealAngle);
        // 공중 물방울과 컵 안 액체에 동일한 색을 전달합니다.
        _fluidRenderer.SetColors(selectedStyle.LiquidColor, selectedStyle.HighlightColor);
        // 설정 준비가 완료되었음을 호출자에게 알립니다.
        return true;
    }
}
