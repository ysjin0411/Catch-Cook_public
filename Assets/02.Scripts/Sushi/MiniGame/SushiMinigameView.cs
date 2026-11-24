using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>카메라 화면과 미니게임 UI를 표시합니다. 음료 모드는 Controller의 입력·시간·판정 결과를 받아 표시합니다.</summary>
public sealed partial class SushiMinigameView : MonoBehaviour
{
    #region 인스펙터 연결
    [Header("미니게임 카메라")]
    [Tooltip("이 카메라의 위치·렌즈 설정을 복사한 표시 전용 카메라를 사용합니다. 원본 카메라의 출력은 변경하지 않습니다.")]
    [SerializeField] Camera _sourceCamera;
    [SerializeField, Range(256, 2048)] int _captureWidth = 1024;
    [InspectorName("결과 표시 시간 (초)")]
    [Tooltip("성공 또는 실패를 보여준 뒤 창 전체가 닫힐 때까지의 시간입니다. 시간 정지 중에도 실제 시간 기준으로 진행합니다.")]
    [SerializeField, Min(.1f)] float _resultDuration = 1f;
    [Header("화면과 설명")]
    [SerializeField] GameObject _panel;
    [SerializeField] RawImage _cameraImage;
    [SerializeField] AspectRatioFitter _cameraAspect;
    [SerializeField] TMP_Text _description, _cameraPlaceholder;
    [Header("타이밍 바")]
    [SerializeField] RectTransform _bar, _targetZone, _arrow;
    [SerializeField] Vector2 _targetRange = new(.38f, .72f);
    [SerializeField, Min(.05f)] float _oneWayDuration = 1.1f;
    [SerializeField] bool _showOnStart;
    [Header("임시 결과 표시")]
    [SerializeField] GameObject _resultRoot;
    [SerializeField] TMP_Text _resultText;
    [SerializeField] Image _resultBackground;
    [Header("성공·실패 안내")]
    [SerializeField] TMP_Text _completionHint;
    [SerializeField] Color _completionSuccessColor = new Color32(100, 220, 130, 255);
    [SerializeField] Color _completionFailureColor = new Color32(232, 76, 86, 255);
    #endregion

    #region 표시 상태
    Camera _displayCamera, _boundSource;
    RenderTexture _cameraTexture;
    Tween _arrowTween, _resultCloseTween;
    // 결과 안내의 크기와 회전 연출을 함께 관리합니다.
    Sequence _completionHintSequence;
    // 프리팹에 설정된 기본 안내 상태를 보관합니다.
    bool _completionHintCached;
    string _completionDefaultText;
    Color _completionDefaultColor;
    Vector3 _completionDefaultScale;
    Quaternion _completionDefaultRotation;
    bool _acceptTimingInput, _hasResult;
    int _openedFrame;
    InputAction _timingAction;
    public bool IsOpen => _panel != null && _panel.activeSelf;
    public Camera SourceCamera => _sourceCamera;
    /// <summary>판정 직후 한 번 전달합니다. true는 성공, false는 실패이며 취소·닫기는 결과를 전달하지 않습니다.</summary>
    public event System.Action<bool> OnResultDetermined;
    public float IndicatorPosition => _bar == null || _arrow == null ? 0 :
        Mathf.InverseLerp(_bar.rect.xMin, _bar.rect.xMax, _bar.InverseTransformPoint(_arrow.position).x);
    #endregion

    #region 초기화와 정리
    /// <summary>미니게임의 초기 표시 상태를 준비합니다.</summary>
    void Awake()
    {
        if (_panel != null) _panel.SetActive(false);
        ApplyUpdatedDrinkLayout();
        ResetResult();
    }

    /// <summary>테스트용 자동 표시 설정이 켜져 있을 때만 창을 엽니다.</summary>
    void Start() { if (_showOnStart) Show(); }

    /// <summary>다른 게임 액션맵의 활성 상태와 독립적으로 스페이스 입력을 연결합니다.</summary>
    void OnEnable()
    {
        _timingAction = new InputAction("MinigameTiming", InputActionType.Button, "<Keyboard>/space");
        _timingAction.performed += OnTimingPressed;
        _timingAction.Enable();
        SubscribeDrink();
    }

    /// <summary>창을 연 프레임을 제외하고 새 입력 한 번만 임시 판정으로 전달합니다.</summary>
    void OnTimingPressed(InputAction.CallbackContext context)
    {
        if (_drinkController == null && Time.frameCount > _openedFrame) SubmitTiming();
    }
    /// <summary>원본 카메라가 움직이거나 렌즈 설정이 바뀌면 표시 전용 카메라에 반영합니다.</summary>
    void LateUpdate()
    {
        if (!IsOpen || !Application.isPlaying) return;
        if (_drinkController != null) RefreshDrinkPresentation();
        if (_sourceCamera == null) { ReleaseCamera(); if (_drinkController == null) UpdatePlaceholder(); return; }
        if (_displayCamera == null || _boundSource != _sourceCamera)
        {
            ReleaseCamera(); CreateCamera(); if (_drinkController == null) UpdatePlaceholder();
        }
        if (_displayCamera != null)
        {
            _displayCamera.CopyFrom(_sourceCamera);
            _displayCamera.transform.SetPositionAndRotation(_sourceCamera.transform.position, _sourceCamera.transform.rotation);
            _displayCamera.targetTexture = _cameraTexture;
            _displayCamera.rect = new Rect(0, 0, 1, 1);
            _displayCamera.enabled = true;
        }
    }

    /// <summary>창이나 씬이 비활성화되면 연출과 임시 카메라·렌더 텍스처를 정리합니다.</summary>
    void OnDisable() { ReleaseDrink(); _timingAction?.Dispose(); _timingAction = null; ResetResult(); StopArrow(); ReleaseCamera(); if (_panel != null) _panel.SetActive(false); }
    #endregion

    #region 외부 표시 함수
    /// <summary>설명과 카메라 화면을 표시하고 화살표 왕복 연출을 시작합니다.</summary>

    public void Show()
    {
        if (_panel == null) return;
        if (_drinkController != null) { OpenDrink(); return; }
        ResetResult(); _openedFrame = Time.frameCount;
        _panel.SetActive(true); SetTargetRange(_targetRange.x, _targetRange.y);
        CreateCamera(); UpdatePlaceholder(); StartArrow();
    }

    /// <summary>현재 표시를 닫으며 카메라 원본이나 게임 운영 상태는 변경하지 않습니다.</summary>

    public void Hide()
    {
        CloseDrink(); ResetResult(); StopArrow(); ReleaseCamera();
        if (_panel != null) _panel.SetActive(false);
    }

    /// <summary>Inspector 연결 대신 외부에서 표시할 카메라를 지정할 수 있습니다.</summary>
    public void SetCamera(Camera camera)
    {
        ReleaseCamera(); _sourceCamera = camera;
        if (IsOpen) { CreateCamera(); UpdatePlaceholder(); }
    }

    /// <summary>설명 문구만 전달받아 표시합니다.</summary>
    public void SetDescription(string text) { if (_description != null) _description.text = text ?? ""; }

    /// <summary>표시와 임시 판정에 사용하는 노란 구간의 범위를 0~1로 지정합니다.</summary>
    public void SetTargetRange(float minimum, float maximum)
    {
        _targetRange = new Vector2(Mathf.Clamp01(Mathf.Min(minimum, maximum)), Mathf.Clamp01(Mathf.Max(minimum, maximum)));
        if (_targetZone == null) return;
        float displayMin = _drinkController != null ? (1f - FinalTargetWidth / FinalGaugeWidth) * .5f : _targetRange.x;
        float displayMax = _drinkController != null ? 1f - displayMin : _targetRange.y;
        _targetZone.anchorMin = new Vector2(displayMin, 0);
        _targetZone.anchorMax = new Vector2(displayMax, 1);
        _targetZone.offsetMin = _targetZone.offsetMax = Vector2.zero;
    }
    #endregion

    #region 화살표 연출
    /// <summary>DOTween의 일정한 속도로 왼쪽과 오른쪽을 반복 왕복합니다.</summary>
    public void StartArrow()
    {
        if (_drinkController != null) { if (!IsOpen) Show(); return; }
        ResetResult(); StopArrow(); if (_bar == null || _arrow == null || !IsOpen) return;
        Canvas.ForceUpdateCanvases();
        float half = TravelHalfWidth();
        _arrow.anchoredPosition = new Vector2(-half, _arrow.anchoredPosition.y);
        if (!Application.isPlaying) return;
        _acceptTimingInput = true;
        _arrowTween = _arrow.DOAnchorPosX(half, Mathf.Max(.05f, _oneWayDuration))
            .SetEase(Ease.Linear).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
    }

    /// <summary>화살표를 현재 위치에 멈추고 새 입력을 막습니다. 이 함수 자체는 결과를 판정하지 않습니다.</summary>
    public void StopArrow() { _acceptTimingInput = false; _arrowTween?.Kill(false); _arrowTween = null; }
    float TravelHalfWidth() => _bar != null ? Mathf.Max(0, (_bar.rect.width - (_arrow != null ? _arrow.rect.width : 0)) * .5f) : 0;
    #endregion

    #region 임시 타이밍 판정과 결과
    /// <summary>화살표 중심이 실제 노란 구간 안인지 한 번 판정하고 결과를 1초간 표시합니다.</summary>
    public void SubmitTiming()
    {
        if (_drinkController != null || !Application.isPlaying || !IsOpen || !_acceptTimingInput || _hasResult) return;
        float position = IndicatorPosition;
        StopArrow();
        bool success = position >= _targetRange.x - .00001f && position <= _targetRange.y + .00001f;
        ShowResult(success);
    }

    /// <summary>성공·실패 임시 UI를 표시하고 실제 시간 기준으로 창 전체를 닫습니다.</summary>
    [SerializeField] UIVisibilityEffectView _resultStampEffect;
    [SerializeField] Image _resultStamp;
    [SerializeField] Sprite _successStampSprite;
    [SerializeField] Sprite _failureStampSprite;
    void ShowResult(bool success)
    {
        _hasResult = true;
        // 판정 결과에 맞는 안내 문구와 색상을 표시합니다.
        ShowCompletionHint(success);
        if (_resultText != null) _resultText.text = success ? "성공" : "실패";
        if (_resultBackground != null) _resultBackground.color = Color.clear;
        if (_resultStamp != null)
        {
            _resultStamp.sprite = success ? _successStampSprite : _failureStampSprite;
            _resultStamp.material = null;
            _resultStamp.color = Color.white;
        }
        if (_resultRoot != null) _resultRoot.SetActive(true);
        if (_resultStampEffect != null) { _resultStampEffect.HideImmediate(); _resultStampEffect.Show(); }
        _resultCloseTween?.Kill(false);
        _resultCloseTween = DOVirtual.DelayedCall(Mathf.Max(1.2f, _resultDuration), Hide).SetUpdate(true);
        // TODO: 담당 Presenter에서 구독하여 성공·실패에 따른 운영 처리를 연결합니다.
        OnResultDetermined?.Invoke(success);
    }

    /// <summary>다시 열기·닫기·테스트 재시작 시 이전 결과와 예약된 닫기를 정리합니다.</summary>
    void ResetResult()
    {
        _resultCloseTween?.Kill(false); _resultCloseTween = null;
        _hasResult = false;
        if (_resultStampEffect != null) _resultStampEffect.HideImmediate();
        if (_resultRoot != null) _resultRoot.SetActive(false);
        // 게임 시작과 종료 시 결과 연출을 중단하고 기본 안내를 복원합니다.
        ResetCompletionHint();
    }

    /// <summary>
    /// 결과 문구를 튀어나오게 표시한 뒤 좌우로 가볍게 기울이고 정지시킵니다.
    /// </summary>
    void ShowCompletionHint(bool success)
    {
        // 안내 텍스트가 연결되지 않았다면 표시를 생략합니다.
        if (_completionHint == null) return;

        // 이전 연출을 중단하고 크기와 회전을 기본 상태로 맞춥니다.
        ResetCompletionHint();

        // 애니메이션을 적용할 RectTransform을 가져옵니다.
        RectTransform target = _completionHint.rectTransform;

        // 판정 결과에 맞는 안내 문구를 설정합니다.
        _completionHint.text = success
            ? "성공!"
            : "실패! 손님의 인내심이 깎입니다.";

        // 성공과 실패를 구분할 수 있도록 글자 색상을 설정합니다.
        _completionHint.color = success
            ? _completionSuccessColor
            : _completionFailureColor;

        // 작은 크기에서 튀어나오도록 시작 크기를 설정합니다.
        target.localScale = _completionDefaultScale * 0.2f;

        // 기본 회전을 기준으로 흔들릴 각도를 준비합니다.
        Vector3 baseAngles = _completionDefaultRotation.eulerAngles;

        // 시간 정지 중에도 재생되는 결과 연출을 생성합니다.
        _completionHintSequence = DOTween.Sequence();
        _completionHintSequence.SetUpdate(true);

        // 원래 크기를 살짝 넘어갔다 돌아오는 탄력 있는 등장을 적용합니다.
        _completionHintSequence.Append(
            target.DOScale(_completionDefaultScale, 0.24f).SetEase(Ease.OutBack));

        // 등장한 문장을 오른쪽으로 살짝 기울입니다.
        _completionHintSequence.Append(
            target.DOLocalRotate(baseAngles + new Vector3(0f, 0f, 5f), 0.07f)
                .SetEase(Ease.OutSine));

        // 반대 방향으로 기울여 흔들리는 느낌을 줍니다.
        _completionHintSequence.Append(
            target.DOLocalRotate(baseAngles + new Vector3(0f, 0f, -4f), 0.1f)
                .SetEase(Ease.InOutSine));

        // 흔들림의 폭을 줄이며 다시 오른쪽으로 움직입니다.
        _completionHintSequence.Append(
            target.DOLocalRotate(baseAngles + new Vector3(0f, 0f, 2f), 0.08f)
                .SetEase(Ease.InOutSine));

        // 원래 각도로 복귀한 뒤 결과 문구를 유지합니다.
        _completionHintSequence.Append(
            target.DOLocalRotate(baseAngles, 0.08f).SetEase(Ease.OutSine));
    }

    /// <summary>
    /// 진행 중인 결과 연출을 중단하고 프리팹의 기본 안내 상태를 복원합니다.
    /// </summary>
    void ResetCompletionHint()
    {
        // 안내 텍스트가 연결되지 않았다면 초기화를 생략합니다.
        if (_completionHint == null) return;

        // 크기와 회전을 제어할 RectTransform을 가져옵니다.
        RectTransform target = _completionHint.rectTransform;

        // 첫 초기화에서 프리팹에 설정된 기본 상태를 저장합니다.
        if (!_completionHintCached)
        {
            // 게임 진행 중 보여줄 원래 문구와 색상을 저장합니다.
            _completionDefaultText = _completionHint.text;
            _completionDefaultColor = _completionHint.color;

            // 결과 연출이 끝난 뒤 돌아올 크기와 회전을 저장합니다.
            _completionDefaultScale = target.localScale;
            _completionDefaultRotation = target.localRotation;

            // 결과 문구를 기본 상태로 다시 저장하지 않도록 표시합니다.
            _completionHintCached = true;
        }

        // 이전 연출을 중단하여 복원한 상태가 다시 변경되지 않게 합니다.
        if (_completionHintSequence != null)
        {
            // 완료 위치로 이동하지 않고 현재 연출을 중단합니다.
            _completionHintSequence.Kill(false);

            // 종료한 연출의 참조를 비웁니다.
            _completionHintSequence = null;
        }

        // 기본 안내 문구와 색상을 복원합니다.
        _completionHint.text = _completionDefaultText;
        _completionHint.color = _completionDefaultColor;

        // 연출로 변경된 크기와 회전을 복원합니다.
        target.localScale = _completionDefaultScale;
        target.localRotation = _completionDefaultRotation;

        // 미니게임 진행 중 기본 안내가 보이도록 활성화합니다.
        _completionHint.gameObject.SetActive(true);
    }
    #endregion

    #region 카메라 화면 표시
    /// <summary>원본 출력에 영향을 주지 않는 표시용 카메라와 렌더 텍스처를 만듭니다.</summary>
    void CreateCamera()
    {
        if (!Application.isPlaying || _sourceCamera == null || _cameraImage == null || _displayCamera != null) return;
        float aspect = Mathf.Max(.1f, _sourceCamera.aspect);
        int width = Mathf.Clamp(_captureWidth, 256, 2048), height = Mathf.Clamp(Mathf.RoundToInt(width / aspect), 128, 4096);
        _cameraTexture = new RenderTexture(width, height, 24) { name = "MinigameCameraTexture", hideFlags = HideFlags.HideAndDontSave };
        _cameraTexture.Create();
        var cameraObject = new GameObject("MinigameDisplayCamera") { hideFlags = HideFlags.HideAndDontSave };
        _displayCamera = cameraObject.AddComponent<Camera>();
        _boundSource = _sourceCamera;
        _displayCamera.CopyFrom(_sourceCamera);
        _displayCamera.transform.SetPositionAndRotation(_sourceCamera.transform.position, _sourceCamera.transform.rotation);
        _displayCamera.targetTexture = _cameraTexture; _displayCamera.rect = new Rect(0, 0, 1, 1);
        _displayCamera.enabled = true;
        _cameraImage.texture = _cameraTexture; _cameraImage.enabled = true;
        if (_cameraAspect != null) _cameraAspect.aspectRatio = (float)width / height;
    }

    /// <summary>카메라가 지정되지 않았으면 연결 안내만 표시합니다.</summary>
    void UpdatePlaceholder()
    {
        if (_cameraPlaceholder != null) _cameraPlaceholder.gameObject.SetActive(_cameraTexture == null);
        if (_cameraImage != null) _cameraImage.enabled = _cameraTexture != null;
    }

    /// <summary>이 View가 만든 자원만 해제하고 원본 카메라는 그대로 둡니다.</summary>
    void ReleaseCamera()
    {
        _boundSource = null;
        if (_cameraImage != null) _cameraImage.texture = null;
        if (_displayCamera != null) { _displayCamera.targetTexture = null; Destroy(_displayCamera.gameObject); _displayCamera = null; }
        if (_cameraTexture != null) { _cameraTexture.Release(); Destroy(_cameraTexture); _cameraTexture = null; }
    }
    #endregion

    #region ContextMenu 테스트
    /// <summary>기존 창 열기 함수를 호출하여 임시 UI를 확인합니다.</summary>
    [ContextMenu("테스트/창 열기")]
    void TestShow() => Show();

    /// <summary>기존 창 닫기 함수로 UI와 임시 카메라를 정리합니다.</summary>
    [ContextMenu("테스트/창 닫기")]
    void TestHide() => Hide();

    /// <summary>화살표 왕복을 처음부터 시작합니다.</summary>
    [ContextMenu("테스트/화살표 시작")]
    void TestStartArrow() => StartArrow();

    /// <summary>화살표를 현재 위치에서 정지합니다.</summary>
    [ContextMenu("테스트/화살표 정지")]
    void TestStopArrow() => StopArrow();

    /// <summary>창을 열고 성공 결과·이벤트·자동 닫기를 확인합니다.</summary>
    [ContextMenu("테스트/성공 결과 테스트")]
    void TestSuccessResult() => TestResult(true);

    /// <summary>창을 열고 실패 결과·이벤트·자동 닫기를 확인합니다.</summary>
    [ContextMenu("테스트/실패 결과 테스트")]
    void TestFailureResult() => TestResult(false);

    /// <summary>테스트에서만 결과를 지정하며 실제 입력 판정 함수에는 영향을 주지 않습니다.</summary>
    void TestResult(bool success)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        Show();
        StopArrow();
        ShowResult(success);
    }

    /// <summary>결과 표시와 이벤트 테스트는 플레이 중에만 실행합니다.</summary>
    [ContextMenu("테스트/성공 결과 테스트", true)]
    [ContextMenu("테스트/실패 결과 테스트", true)]
    bool CanTestResult() => Application.isPlaying && isActiveAndEnabled;
    /// <summary>화살표 테스트는 창이 열린 플레이 모드에서만 실행합니다.</summary>
    [ContextMenu("테스트/화살표 시작", true)]
    [ContextMenu("테스트/화살표 정지", true)]
    bool CanTestArrow() => Application.isPlaying && IsOpen && _drinkController == null;
    #endregion
}



