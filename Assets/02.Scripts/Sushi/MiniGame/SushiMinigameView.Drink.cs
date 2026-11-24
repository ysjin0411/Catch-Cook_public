using SushiPouringV3;
using TMPro;
using UnityEngine;

/// <summary>음료 Controller의 상태를 받아 기존 미니게임 창에 표시합니다.</summary>
[DefaultExecutionOrder(-100)]
public sealed partial class SushiMinigameView
{
    #region 음료 화면 연결
    // 입력·시간·판정은 이 Controller 하나만 담당합니다.
    [Header("음료 미니게임 연결")]
    [SerializeField] DrinkMinigameController _drinkController;
    // Ready와 Start 연출 및 Start 표시 이후의 시작 지연입니다.
    [SerializeField] DrinkReadyStartView _readyStartView;
    [SerializeField, Min(0f)] float _delayAfterStart = 0.5f;
    // 준비 상태와 게임 시간 기준의 시작 예약입니다.
    bool _drinkPreparing, _drinkStartScheduled;
    float _drinkStartTime;
    // 병 회전의 가속과 감속을 이어 주는 현재 각속도입니다.
    float _bottleAngularVelocity;
    // 계산 영역과 비율이 같은 패널, 실제 병 입구와 앞쪽 기준점입니다.
    [SerializeField] RectTransform _fluidPlane, _bottleMouth, _bottleDirection;
    // 병 그림과 발사 기준점을 함께 회전시키는 부모입니다.
    [SerializeField] RectTransform _bottlePivot;
    // 전체 게이지의 시작, 중앙, 끝을 통과하는 연속 회전 곡선의 Z 각도입니다.
    [SerializeField] float _bottleLowAngle = 120f;
    [SerializeField] float _bottleIdealAngle = 150f;
    [SerializeField] float _bottleHighAngle = 170f;
    // 초당 회전할 수 있는 최대 각도입니다.
    [SerializeField, Min(0f)] float _bottleRotationSpeed = 90f;
    [SerializeField] DrinkFluidRendererView _fluidRenderer;
    [SerializeField] TMP_Text _remainingTimeText;
    // 목표·허용선은 컵 기하학의 물량 변환을 그대로 사용합니다.
    [SerializeField] RectTransform _volumeTarget, _volumeMinimum, _volumeMaximum;
    [SerializeField] GameObject _drinkArtwork;
    FluidPourSession _drinkSession;
    FluidPourSession _lastCompletedDrink;
    FluidParticle[] _particles;
    bool _drinkSubscribed, _closingDrink;
    public FluidPourSession DrinkSession => _drinkSession;
    /// <summary>취소를 포함한 원래 결과를 전달합니다. 서빙·보상은 외부 담당자가 연결합니다.</summary>
    public event System.Action<FluidCompletion> OnDrinkCompleted;
    #endregion

    #region 구독과 생명주기
    /// <summary>Controller 이벤트를 한 번 구독하고 늦게 켜진 화면을 복구합니다.</summary>
    void SubscribeDrink()
    {
        if (_drinkController == null || _drinkSubscribed) return;
        _drinkController.Prepared += HandleDrinkPrepared;
        _drinkController.Completed += HandleDrinkCompleted;
        _drinkSubscribed = true;
        if (_drinkController.TryGetSession(out FluidPourSession session) && session.Result != FluidResult.Cancelled &&
            !object.ReferenceEquals(session, _lastCompletedDrink))
        {
            HandleDrinkPrepared(session);
            if (session.TryGetCompletion(out FluidCompletion result)) HandleDrinkCompleted(session, result);
        }
    }

    /// <summary>진행 중 라운드만 취소하고 구독·렌더 자원을 정리합니다.</summary>
    void ReleaseDrink()
    {
        CloseDrink();
        if (_drinkController != null && _drinkSubscribed)
        {
            _drinkController.Prepared -= HandleDrinkPrepared;
            _drinkController.Completed -= HandleDrinkCompleted;
        }
        _drinkSubscribed = false;
        if (_fluidRenderer != null) _fluidRenderer.ReleaseResources();
    }

    /// <summary>화면을 표시하고 Ready와 Start 연출을 시작합니다.</summary>
    public void BeginDrink()
    {
        if (!_drinkPreparing || _drinkStartScheduled || !IsOpen) return;
        if (!_readyStartView.Play()) Hide();
    }

    void OpenDrink()
    {
        // 필수 연결과 중복 실행을 검사합니다.
        if (!Application.isPlaying || _drinkController == null || _readyStartView == null || _panel == null) return;
        if (_drinkPreparing || _drinkController.IsPlaying) return;
        // 이전 라운드를 정리한 후 이벤트를 연결합니다.
        CloseDrink(); StopArrow(); ResetResult();
        SubscribeDrink();
        // 준비 중에는 세션과 라운드 시간을 진행하지 않습니다.
        _drinkSession = null;
        _drinkController.enabled = false;
        _drinkPreparing = true;
        _drinkStartScheduled = false;
        // 컵과 병을 표시하고 이전 숫자를 지웁니다.
        _panel.SetActive(true);
        // 대기 중에도 실제 렌더러, 목표선, 게이지와 남은 시간을 초기화합니다.
        if (!_drinkController.PrepareRound()) { Hide(); return; }
        if (_drinkArtwork != null) _drinkArtwork.SetActive(true);
        if (_cameraPlaceholder != null) _cameraPlaceholder.gameObject.SetActive(false);
        SetDescription("F를 연속으로 눌러 노란 구간을 유지하세요.");
        _readyStartView.StartShown -= HandleStartShown;
        _readyStartView.StartShown += HandleStartShown;
        BeginDrink();
    }

    /// <summary>Start가 표시된 시점을 기준으로 게임 시작을 예약합니다.</summary>
    void HandleStartShown()
    {
        // 중복 이벤트와 닫힌 화면의 시작 예약을 거부합니다.
        if (!_drinkPreparing || _drinkStartScheduled || !IsOpen) return;
        _drinkStartScheduled = true;
        _drinkStartTime = Time.time + Mathf.Max(0f, _delayAfterStart);
    }

    /// <summary>닫기 도중 취소 이벤트가 창을 다시 켜지 않도록 정리합니다.</summary>
    void CloseDrink()
    {
        // 준비 도중 닫으면 연출과 지연 시작도 취소합니다.
        _drinkPreparing = false;
        _drinkStartScheduled = false;
        if (_readyStartView != null)
        {
            _readyStartView.StartShown -= HandleStartShown;
            _readyStartView.Stop();
        }
        // 취소 콜백에서 화면을 다시 열거나 닫는 것을 구분합니다.
        _closingDrink = true;
        try
        {
            // 닫힌 화면의 세션과 전용 Controller를 정지합니다.
            if (_drinkController != null) _drinkController.CancelRound();
            if (_drinkController != null && _drinkController.gameObject == gameObject) _drinkController.enabled = false;
        }
        finally
        {
            // 콜백 오류에도 닫기 상태를 복원합니다.
            _closingDrink = false;
        }
        // 이전 세션과 렌더링 결과를 비웁니다.
        _drinkSession = null;
        if (_fluidRenderer != null) _fluidRenderer.Clear();
    }

    /// <summary>새 세션의 UI·입자 버퍼·목표선을 준비합니다.</summary>
    void HandleDrinkPrepared(FluidPourSession session)
    {
        StopArrow(); ResetResult();
        _drinkSession = session;
        _particles = new FluidParticle[session.ParticleCapacity];
        if (_panel != null) _panel.SetActive(true);
        if (_drinkArtwork != null) _drinkArtwork.SetActive(true);
        if (_cameraPlaceholder != null) _cameraPlaceholder.gameObject.SetActive(false);
        SetDescription("F를 연속으로 눌러 노란 구간을 유지하세요.");
        SetTargetRange((float)(session.Settings.IdealGaugeMin / session.Settings.MaximumGauge),
            (float)(session.Settings.IdealGaugeMax / session.Settings.MaximumGauge));
        if (_fluidRenderer != null) _fluidRenderer.Prepare(session.ParticleCapacity, session.SimulationSettings);
        Canvas.ForceUpdateCanvases();
        // 시작 게이지에 맞춰 병을 배치한 뒤 발사 위치와 방향을 계산합니다.
        UpdateBottleTilt(true);
        UpdateEmitterPose();
        RefreshDrinkPresentation();
    }

    /// <summary>새 물량 계산 없이 Controller가 확정한 결과만 표시합니다.</summary>
    void HandleDrinkCompleted(FluidPourSession session, FluidCompletion result)
    {
        if (!object.ReferenceEquals(session, _drinkSession) || object.ReferenceEquals(session, _lastCompletedDrink) || _hasResult) return;
        _lastCompletedDrink = session;
        if (result.Result == FluidResult.Cancelled)
        {
            if (!_closingDrink) Hide();
        }
        else
        {
            ShowResult(result.Result == FluidResult.Success);
            if (_resultText != null) _resultText.text = result.Result == FluidResult.Success ? "성공" :
                result.Result == FluidResult.TooLittle ? "실패 · 부족" : "실패 · 초과";
        }
        // TODO: 같은 손님·주문이 유효한지 검사한 뒤 서빙 처리를 연결해야 합니다.
        OnDrinkCompleted?.Invoke(result);
    }
    #endregion

    #region 좌표와 표시 갱신
    /// <summary>Controller.Update 전에 병 입구를 계산 좌표로 변환합니다.</summary>
    void Update()
    {
        // 준비 중 화면 종료와 Start 이후 대기 시간을 검사합니다.
        if (_drinkPreparing)
        {
            if (!IsOpen) { Hide(); return; }
            if (!_drinkStartScheduled || Time.timeScale <= 0f || Time.time < _drinkStartTime) return;
            // 실제 라운드 시작 전에 연출과 구독을 정리합니다.
            _drinkPreparing = false;
            _drinkStartScheduled = false;
            _readyStartView.StartShown -= HandleStartShown;
            _readyStartView.Stop();
            _drinkController.enabled = true;
            // 준비 화면에서 사용하던 세션을 그대로 진행하고 안내 문구만 바꿉니다.
            if (!_drinkController.StartPreparedRound()) { Hide(); return; }
            SetDescription("F를 연속으로 눌러 노란 구간을 유지하세요.");
            return;
        }
        // 라운드가 시작된 후에만 실제 병 입구를 전달합니다.
        if (_drinkSession == null) return;
        if (!IsOpen) { Hide(); return; }
        // 병의 회전을 먼저 반영하여 물이 현재 병 방향으로 나오게 합니다.
        UpdateBottleTilt();
        UpdateEmitterPose();
    }

    /// <summary>전체 게이지에 따른 연속 목표 각도를 부드럽게 따라가며 시작 시에는 즉시 배치합니다.</summary>
    private void UpdateBottleTilt(bool immediate = false)
    {
        // 세션과 회전 대상이 준비된 경우에만 처리합니다.
        if (_drinkSession == null || _bottlePivot == null) return;
        // 일시정지 시 SmoothDamp의 시간 나눗셈을 피하고 현재 각속도를 유지합니다.
        if (!immediate && Time.deltaTime <= 0f) return;

        // 전체 게이지를 하나의 연속 곡선으로 변환해 노란 구간에서도 회전하게 합니다.
        float ratio = Mathf.Clamp01((float)_drinkSession.GaugeRatio);
        // 가운데 게이지에서 Ideal 각도를 통과하도록 이차 베지어 제어점을 계산합니다.
        float control = 2f * _bottleIdealAngle - (_bottleLowAngle + _bottleHighAngle) * .5f;
        float first = Mathf.LerpUnclamped(_bottleLowAngle, control, ratio);
        float second = Mathf.LerpUnclamped(control, _bottleHighAngle, ratio);
        float targetAngle = Mathf.LerpUnclamped(first, second, ratio);
        // 시작할 때는 즉시 배치하고, 진행 중에는 부드럽게 회전합니다.
        // 새 라운드에서는 이전 회전 속도를 가져오지 않습니다.
        if (immediate) _bottleAngularVelocity = 0f;
        float angle = immediate
            ? targetAngle
            : Mathf.SmoothDampAngle(_bottlePivot.localEulerAngles.z,
                targetAngle, ref _bottleAngularVelocity, .12f, Mathf.Max(0f, _bottleRotationSpeed), Time.deltaTime);

        // 병과 입구·방향 기준점을 함께 회전합니다.
        _bottlePivot.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>패널의 실제 변환을 역산해 입구 위치·방향만 전달합니다.</summary>
    void UpdateEmitterPose()
    {
        if (_drinkSession == null || _fluidPlane == null || _bottleMouth == null || _bottleDirection == null) return;
        Vector2 a = PlaneToSimulation(_bottleMouth.position);
        // 모든 게이지에서 컵 입구 중앙을 향하게 하며 오른쪽 초과는 유량만 증가시킵니다.
        Vector2 direction = new Vector2(-a.x, (float)_drinkSession.Cup.Rim - a.y).normalized;
        if (direction.sqrMagnitude > .000001f)
            _drinkController.SetEmitterPose(new FluidEmitterPose(a.x, a.y, direction.x, direction.y));
    }

    /// <summary>월드 좌표를 패널 로컬 좌표를 거쳐 시뮬레이션 좌표로 바꿉니다.</summary>
    Vector2 PlaneToSimulation(Vector3 world)
    {
        Vector3 p = _fluidPlane.InverseTransformPoint(world);
        Rect r = _fluidPlane.rect;
        FluidSimulationSettings s = _drinkSession.SimulationSettings;
        return new Vector2(Mathf.LerpUnclamped((float)s.ViewMinX, (float)s.ViewMaxX, (p.x-r.xMin)/r.width),
            Mathf.LerpUnclamped((float)s.ViewMinY, (float)s.ViewMaxY, (p.y-r.yMin)/r.height));
    }

    /// <summary>Controller 진행 후 최신 입자를 그리고 게이지·시간·총 생성량을 표시합니다.</summary>
    void RefreshDrinkPresentation()
    {
        if (_drinkSession == null) return;
        if (_arrow != null && _bar != null)
        {
            Vector3 p = _bar.InverseTransformPoint(_arrow.position);
            p.x = Mathf.Lerp(_bar.rect.xMin, _bar.rect.xMax, DrinkGaugeDisplayPosition((float)_drinkSession.GaugeRatio));
            _arrow.position = _bar.TransformPoint(p);
        }

        if (_remainingTimeText != null) _remainingTimeText.SetText("{0:1}초", (float)_drinkSession.RemainingTime);
        PlaceVolumeLine(_volumeTarget, _drinkSession.Settings.TargetVolume);
        PlaceVolumeLine(_volumeMinimum, _drinkSession.Settings.MinimumSuccessVolume);
        PlaceVolumeLine(_volumeMaximum, _drinkSession.Settings.MaximumSuccessVolume);
        if (_particles != null && _fluidRenderer != null)
            _fluidRenderer.Render(_particles, _drinkSession.CopyParticles(_particles));
    }

    /// <summary>현재 컵의 수위와 해당 높이의 내부 너비에 목표선을 맞춥니다.</summary>
    void PlaceVolumeLine(RectTransform line, double volume)
    {
        // 좌표를 계산할 세션과 UI가 준비된 경우에만 처리합니다.
        if (line == null || _fluidPlane == null || _drinkSession == null) return;
        // 이번 라운드의 표시 범위와 컵 기하학을 가져옵니다.
        FluidSimulationSettings s = _drinkSession.SimulationSettings;
        double height = _drinkSession.Cup.YAtVolume(volume);
        // 계산 좌표를 FluidPlane의 정규화 좌표로 변환합니다.
        float y = (float)((height - s.ViewMinY) / (s.ViewMaxY - s.ViewMinY));
        float left = (float)((_drinkSession.Cup.LeftAt(height) - s.ViewMinX) / (s.ViewMaxX - s.ViewMinX));
        float right = (float)((_drinkSession.Cup.RightAt(height) - s.ViewMinX) / (s.ViewMaxX - s.ViewMinX));
        // FluidPlane의 직접 자식인 선을 현재 컵 폭에 맞춥니다.
        line.anchorMin = new Vector2(left, y);
        line.anchorMax = new Vector2(right, y);
        // 기존 선 두께를 유지하면서 앵커 기준으로 정렬합니다.
        line.anchoredPosition = Vector2.zero;
        line.sizeDelta = new Vector2(0f, line.sizeDelta.y);
    }
    #endregion
}
