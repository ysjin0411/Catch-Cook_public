using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>실제 상호작용 진행을 읽어 모델 방향, 작업 애니메이션과 소품을 제어합니다.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class GroundHeroInteractionPresentation : MonoBehaviour
{
    private enum WorkKind { None, Cleaning, ServingDrink }

    [Header("플레이어 참조")]
    [SerializeField] private GroundHero _hero;
    [SerializeField] private SushiCarrier _carrier;
    [SerializeField] private SushiInteractor _interactor;
    [Header("이동 입력")]
    // InputHandler와 동일한 Move 액션을 연결합니다.
    [SerializeField] private InputActionReference _moveAction;
    [Header("시각 모델")]
    [SerializeField] private Transform _model;
    [SerializeField] private Animator _animator;
    [Header("음식 제공 후 방향 유지 시간")]
    [SerializeField, Min(0f)] private float _foodLookDuration = 0.5f;
    [Header("작업 연출")]
    [SerializeField] private string _workLayerName = "SushiWork";
    [SerializeField] private GameObject _cleaningRag;
    [SerializeField] private GameObject _drinkProp;
    [Header("실제 세션 진행 정보 - 확인용")]
    [SerializeField] private float _workDuration;
    [SerializeField] private float _workElapsed;

    private WorkKind _currentWork;
    private Component _workSource;
    private Transform _lookTarget;
    private Transform _foodLookTarget;
    private GuestNPC _foodGuest;
    private float _foodLookEndTime;
    private int _workLayerIndex = -1;
    private int _idleHash;
    private int _cleaningHash;
    private int _drinkHash;
    private RuntimeAnimatorController _boundController;
    private bool _initialized;
    // 이동 제한까지 반영된 최종 이동 명령이 있는지 저장합니다.
    private bool _isMoving;
    // 상호작용 종료 후 복귀할 마지막 이동 방향입니다.
    private Quaternion _movementRotation;

    /// <summary>참조와 Animator 상태를 검증하고 작업 레이어를 준비합니다.</summary>
    private void Awake()
    {
        // 동일 루트의 기존 기능을 참조하며 모델은 명시적으로 연결합니다.
        if (_hero == null) _hero = GetComponent<GroundHero>();
        if (_carrier == null) _carrier = GetComponent<SushiCarrier>();
        if (_interactor == null) _interactor = GetComponent<SushiInteractor>();
        SetPropVisible(_cleaningRag, false);
        SetPropVisible(_drinkProp, false);
        // 잘못된 연결은 기본 이동 레이어에 영향을 주기 전에 차단합니다.
        if (_hero == null || _carrier == null || _interactor == null ||
            _model == null || _animator == null || _animator.runtimeAnimatorController == null)
        {
            Debug.LogError("상호작용 연출의 플레이어, 모델, Animator 참조를 연결하세요.", this);
            enabled = false;
            return;
        }
        // 런타임 Controller 교체 시 기존 레이어 번호를 사용하지 않습니다.
        _boundController = _animator.runtimeAnimatorController;
        _workLayerIndex = _animator.GetLayerIndex(_workLayerName);
        _idleHash = Animator.StringToHash($"{_workLayerName}.Idle");
        _cleaningHash = Animator.StringToHash($"{_workLayerName}.Cleaning");
        _drinkHash = Animator.StringToHash($"{_workLayerName}.ServingDrink");
        if (_workLayerIndex <= 0 || !_animator.HasState(_workLayerIndex, _idleHash) ||
            !_animator.HasState(_workLayerIndex, _cleaningHash) ||
            !_animator.HasState(_workLayerIndex, _drinkHash))
        {
            Debug.LogError("SushiWork 레이어에 Idle, Cleaning, ServingDrink 상태가 필요합니다.", this);
            enabled = false;
            return;
        }
        // 검증된 별도 작업 레이어만 제어합니다.
        _initialized = true;
        _animator.SetLayerWeight(_workLayerIndex, 0f);
    }

    /// <summary>이동 상태와 음식 제공 이벤트를 구독하고 방향 정보를 초기화합니다.</summary>
    private void OnEnable()
    {
        // 참조와 Animator 초기화에 실패했다면 구독하지 않습니다.
        if (!_initialized) return;
        // 첫 이동 전에는 현재 모델 방향을 기본 방향으로 사용합니다.
        _movementRotation = _model.rotation;
        _isMoving = false;
        // 실제 이동 명령의 속도를 받아 상호작용에 의한 정지도 반영합니다.
        _hero.MoveSpeedChanged += OnMoveSpeedChanged;
        // 기존 음식 제공 이벤트 구독을 유지합니다.
        _carrier.FoodServed += OnFoodServed;
        // 이전 작업의 애니메이션과 소품 표시를 정리합니다.
        ClearPresentation();
    }

    /// <summary>이동 제한을 반영한 이동 속도로 현재 이동 여부를 갱신합니다.</summary>
    private void OnMoveSpeedChanged(float speed)
    {
        // SushiInteractor가 정지 입력을 전달하면 이동하지 않는 상태가 됩니다.
        _isMoving = speed > 0.0001f;
    }

    /// <summary>기존 세션의 실제 진행 시간과 타깃을 작업 연출에 반영합니다.</summary>
    private void Update()
    {
        // 키 취소 및 진행 판정은 기존 Interactor가 수행합니다.
        if (!CanPresent()) { ClearPresentation(); return; }
        if (TryReadWork(out WorkKind kind, out Component source, out Transform target,
            out float duration, out float elapsed))
        {
            // 새 작업 후 이전 음식 방향 유지가 다시 살아나지 않게 합니다.
            ClearFoodLook();
            _workDuration = duration;
            _workElapsed = elapsed;
            _lookTarget = target;
            SetWork(kind, source);
            return;
        }
        // 완료 또는 취소된 세션은 작업 레이어와 소품을 즉시 해제합니다.
        _workDuration = _workElapsed = 0f;
        SetWork(WorkKind.None, null);
        if (_foodGuest != null && _foodGuest.isActiveAndEnabled &&
            _foodLookTarget != null && _foodLookTarget.gameObject.activeInHierarchy &&
            Time.time < _foodLookEndTime)
        {
            // 실제 음식 수령자의 좌석만 설정 시간 동안 바라봅니다.
            _lookTarget = _foodLookTarget;
            return;
        }
        // 이후 이동 입력이 기존 방식으로 방향을 결정하도록 제어를 해제합니다.
        ClearFoodLook();
        _lookTarget = null;
    }

    /// <summary>
    /// 실제 작업 중에는 대상을 바라보고, 그 외에는 마지막 이동 방향을 유지합니다.
    /// 이동 방향이 바뀌면 보간 없이 즉시 회전합니다.
    /// </summary>
    private void LateUpdate()
    {
        // 연출에 필요한 참조나 활성 상태가 유효하지 않으면 표시를 정리합니다.
        if (!CanPresent())
        {
            ClearPresentation();
            return;
        }

        // 드링크 제공 또는 청소가 실제로 진행되는 동안만 대상을 바라봅니다.
        if (_currentWork != WorkKind.None)
        {
            // 작업 대상의 방향 기준점이 유효한지 확인합니다.
            if (_lookTarget == null || !_lookTarget.gameObject.activeInHierarchy) return;

            // 높이 차이를 제거하여 모델이 수평으로만 회전하도록 합니다.
            Vector3 workDirection = _lookTarget.position - _model.position;
            workDirection.y = 0f;

            // 방향을 계산할 수 있는 거리일 때 작업 대상을 즉시 바라봅니다.
            if (workDirection.sqrMagnitude > 0.0001f)
                _model.rotation = Quaternion.LookRotation(workDirection, Vector3.up);

            // 작업 중에는 이동 방향으로 덮어쓰지 않습니다.
            return;
        }

        // 이동이 허용된 상태에서만 새로운 이동 방향을 기록합니다.
        if (_isMoving && _moveAction != null && _moveAction.action != null &&
            _moveAction.action.enabled)
        {
            // 기존 Move 액션에서 좌우 방향을 읽습니다.
            float horizontal = _moveAction.action.ReadValue<Vector2>().x;

            // 좌우 입력이 있으면 즉시 적용할 월드 기준 회전을 저장합니다.
            if (Mathf.Abs(horizontal) > 0.0001f)
                _movementRotation = Quaternion.LookRotation(
                    Vector3.right * Mathf.Sign(horizontal), Vector3.up);
        }

        // 작업 종료 후 방향을 복원하고 기존 회전 보간으로 다시 틀어지는 것도 막습니다.
        _model.rotation = _movementRotation;
    }

    /// <summary>현재 참조, 활성 상태와 창 포커스로 연출 가능 여부를 판단합니다.</summary>
    private bool CanPresent()
    {
        // 서로 다른 Controller의 레이어를 실수로 변경하지 않습니다.
        return _initialized && Application.isFocused &&
            _hero != null && _hero.isActiveAndEnabled &&
            _carrier != null && _carrier.isActiveAndEnabled &&
            _interactor != null && _interactor.isActiveAndEnabled && _model != null &&
            _animator != null && _animator.isActiveAndEnabled &&
            _animator.runtimeAnimatorController == _boundController;
    }

    /// <summary>청소와 음료의 원본 설정 시간 및 실제 누적 시간을 읽습니다.</summary>
    private bool TryReadWork(out WorkKind kind, out Component source, out Transform target,
        out float duration, out float elapsed)
    {
        // 접촉만 하고 있거나 음식 운반 중이면 작업으로 표현하지 않습니다.
        kind = WorkKind.None;
        source = null;
        target = null;
        duration = elapsed = 0f;
        if (_carrier.CurrentSushi != null) return false;
        ISushiHoldInteraction active = _interactor.ActiveTarget;
        if (active is Chair chair && chair != null && chair.isActiveAndEnabled &&
            chair.State == ChairState.Uncleaned)
        {
            // 청소 타이머를 복제하지 않고 기존 세션에 맞춥니다.
            kind = WorkKind.Cleaning;
            source = chair;
            target = GetSeatTarget(chair, chair.transform);
            duration = chair.CleaningTime;
            elapsed = chair.CleaningElapsed;
        }
        else if (active is GuestNPC guest && guest != null && guest.isActiveAndEnabled &&
            guest.IsWaitingForDrink && guest.SelectedSushi != null &&
            guest.SelectedSushi.Type == MenuType.Drink)
        {
            // 손님이 주문한 음료별 상호작용 시간을 사용합니다.
            kind = WorkKind.ServingDrink;
            source = guest;
            target = GetSeatTarget(guest.Seat, guest.transform);
            duration = guest.SelectedSushi.DrinkInteractionTime;
            elapsed = guest.DrinkElapsed;
        }
        // 완료 또는 취소로 초기화된 세션은 연출하지 않습니다.
        return target != null && duration > 0f && !float.IsInfinity(duration) &&
            elapsed > 0f && elapsed < duration;
    }

    /// <summary>좌석 기준점을 우선하고 누락 시 의자 또는 손님 위치를 사용합니다.</summary>
    private Transform GetSeatTarget(Chair chair, Transform fallback)
    {
        // 자리 정보가 없는 경우에도 유효한 대체 방향을 사용합니다.
        if (chair == null) return fallback;
        return chair.SeatPos != null ? chair.SeatPos : chair.transform;
    }

    /// <summary>음식 제공 성공 시 실제 수령자의 좌석을 N초 동안 바라봅니다.</summary>
    private void OnFoodServed(GuestNPC guest)
    {
        // 잘못된 시간이나 비활성 상태에서는 연출을 시작하지 않습니다.
        if (!CanPresent() || guest == null || !(_foodLookDuration > 0f) ||
            float.IsInfinity(_foodLookDuration)) return;
        // 센서에서 다른 손님을 다시 탐색하지 않습니다.
        _foodGuest = guest;
        _foodLookTarget = GetSeatTarget(guest.Seat, guest.transform);
        _foodLookEndTime = Time.time + _foodLookDuration;
    }

    /// <summary>작업 종류나 대상이 바뀔 때만 클립과 소품을 전환합니다.</summary>
    private void SetWork(WorkKind kind, Component source)
    {
        // 같은 클립을 매 프레임 첫 프레임부터 재생하지 않습니다.
        if (_currentWork == kind && _workSource == source) return;
        _currentWork = kind;
        _workSource = source;
        SetPropVisible(_cleaningRag, kind == WorkKind.Cleaning);
        SetPropVisible(_drinkProp, kind == WorkKind.ServingDrink);
        if (!_initialized || _animator == null ||
            _animator.runtimeAnimatorController != _boundController) return;
        // 작업 시간 동안 정상 속도로 반복하고 종료 시 별도 레이어만 숨깁니다.
        int hash = kind == WorkKind.Cleaning ? _cleaningHash :
            kind == WorkKind.ServingDrink ? _drinkHash : _idleHash;
        _animator.Play(hash, _workLayerIndex, 0f);
        _animator.SetLayerWeight(_workLayerIndex, kind == WorkKind.None ? 0f : 1f);
    }

    /// <summary>소품의 활성 상태가 달라질 때만 변경합니다.</summary>
    private void SetPropVisible(GameObject prop, bool visible)
    {
        // 뼈와 모델 대신 연결된 소품만 켜고 끕니다.
        if (prop != null && prop.activeSelf != visible) prop.SetActive(visible);
    }

    /// <summary>음식 제공 후 방향 유지 정보를 초기화합니다.</summary>
    private void ClearFoodLook()
    {
        // 이전 대상과 타이머를 다음 작업에 넘기지 않습니다.
        _foodGuest = null;
        _foodLookTarget = null;
        _foodLookEndTime = 0f;
    }

    /// <summary>모든 작업 표시와 방향 유지 정보를 안전하게 정리합니다.</summary>
    private void ClearPresentation()
    {
        // 실제 음식과 세션 데이터는 소유 컴포넌트에 그대로 둡니다.
        ClearFoodLook();
        _lookTarget = null;
        _workDuration = _workElapsed = 0f;
        SetWork(WorkKind.None, null);
        SetPropVisible(_cleaningRag, false);
        SetPropVisible(_drinkProp, false);
        if (_initialized && _animator != null &&
            _animator.runtimeAnimatorController == _boundController)
            _animator.SetLayerWeight(_workLayerIndex, 0f);
    }

    /// <summary>이벤트 구독을 해제하고 이동 상태와 작업 표시를 정리합니다.</summary>
    private void OnDisable()
    {
        // 비활성화 이후 이동 상태 이벤트를 받지 않도록 해제합니다.
        if (_hero != null) _hero.MoveSpeedChanged -= OnMoveSpeedChanged;
        // 기존 음식 제공 이벤트 구독도 해제합니다.
        if (_carrier != null) _carrier.FoodServed -= OnFoodServed;
        // 이전 이동 상태가 다음 활성화에 남지 않도록 초기화합니다.
        _isMoving = false;
        // 작업 애니메이션과 소품을 정리합니다.
        ClearPresentation();
    }

    /// <summary>포커스 상실 시 작업 연출과 소품을 해제합니다.</summary>
    private void OnApplicationFocus(bool focused)
    {
        // 실제 세션 취소는 기존 SushiInteractor에 맡깁니다.
        if (!focused) ClearPresentation();
    }
}
