using UnityEngine;

public enum ServingWorkState
{
    Locked = 0,       // 미해금 또는 필수 설정 누락으로 작업할 수 없습니다.
    Waiting = 1,      // 영업 전 또는 일감 없음으로 대기합니다.
    ServingFood = 2,  // 완성 음식 수령과 손님에게 전달하는 과정을 수행합니다.
    Returning = 3,    // 대기점으로 복귀합니다.
    ServingDrink = 4, // 음료 주문자에게 접근하거나 음료를 제공 중입니다.
    WaitingToServeFood = 5 // 손님을 바라보며 음식 전달을 준비합니다.
}

[RequireComponent(typeof(Rigidbody), typeof(SushiCarrier))]
public class ServingNPC : MonoBehaviour
{
    [Header("이번 영업 실제 서빙 실적 (음식 및 음료)")]
    [SerializeField] int _servedCount;
    public int ServedCount => _servedCount;

    /// <summary>새 영업에서 사용할 해당 직원의 실제 전달 횟수를 초기화합니다.</summary>
    public void ResetBusinessStatistics()
    {
        // 직원 능력치와 이동 상태는 유지하고 영업 실적만 초기화합니다.
        _servedCount = 0;
    }

    // 이 직원의 고유 정보, 기본 속도와 강화 데이터를 연결합니다.
    [SerializeField] ServingStaffData _staffData;
    // 의상과 강화 이동 효과의 적용을 전담합니다.
    [SerializeField] StaffAppearanceView _appearanceView;
    // 새 직원 데이터 연결 전까지 기존 씬과 프리팹의 직렬화된 설정을 유지합니다.
    [SerializeField, HideInInspector] ServingStaffUpgradeData _upgradeData;
    // 일이 없을 때 직원 루트가 돌아갈 위치입니다.
    [SerializeField] Transform _standbyPoint;
    // 첫 번째 대기점과 함께 랜덤 복귀 선분을 만드는 두 번째 지점입니다.
    [SerializeField] Transform _standbyPoint2;
    // 이번 복귀가 끝날 때까지 유지할 목적지입니다.
    float _standbyTargetX;
    // 이동 중이나 도착 후 매 프레임 목적지를 다시 선택하지 않습니다.
    bool _standbySelected;
    // 직원 루트의 왼쪽 이동 한계입니다.
    [SerializeField] Transform _leftLimit;
    // 직원 루트의 오른쪽 이동 한계입니다.
    [SerializeField] Transform _rightLimit;
    // 실제 수령 및 제공 영역에 접촉할 직원 센서입니다.
    [SerializeField] SphereCollider _interactionSensor;
    // 방향을 변경할 시각 모델이며 루트와 센서는 회전시키지 않습니다.
    [SerializeField] Transform _visual;
    // 직원 전용 걷기와 Idle을 재생합니다.
    [SerializeField] Animator _animator;
    // 직원이 멈춰 있을 때도 이유를 확인할 수 있는 실제 작업 상태입니다.
    [SerializeField] ServingWorkState _workState;
    // 초기화 실패나 접촉 실패의 구체적인 이유입니다.
    [SerializeField] string _stateReason;
    // 현재 적용한 강화 단계입니다.
    [SerializeField] int _upgradeLevel;
    // 현재 적용된 초당 이동속도입니다.
    [SerializeField] float _moveSpeed;
    // 현재 음식 운반 또는 음료 제공 대상으로 선택한 손님입니다.
    [SerializeField] GuestNPC _target;
    [Header("음식 전달 전 대기")]
    [SerializeField, Min(0f)] float _foodServingDelay = 1f;
    // 음식 전달을 준비하는 동안 유지할 손님입니다.
    GuestNPC _foodTarget;
    // 현재 손님 앞에서 기다린 시간입니다.
    float _foodServingElapsed;
    // 물리 이동을 담당합니다.
    Rigidbody _rigidbody;
    // 한 번에 음식 한 개만 운반합니다.
    SushiCarrier _carrier;
    // 실제 주문과 영업 상태를 조회합니다.
    SushiManager _manager;
    // 완성 음식을 제공하는 요리 직원입니다.
    CookerNPC _cooker;
    // 시작 시 정한 직원 통로 바닥 높이입니다.
    float _floorY;
    // 시작 시 정한 서빙 통로의 깊이입니다.
    float _laneZ;
    // 강화 1레벨부터 자동 서빙을 허용합니다.
    bool _automaticServing;
    // 초기화 성공 여부입니다.
    bool _configured;
    // 현재 대상에게 음료를 제공하는 작업을 유지할지 나타냅니다.
    bool _drinkTask;
    // 연결된 Animator에 MoveSpeed 실수 파라미터가 있는지 나타냅니다.
    bool _hasMoveSpeedParameter;

    /// <summary>이 서빙 직원의 고유 정보와 기본 능력치입니다.</summary>
    public ServingStaffData StaffData => _staffData;
    /// <summary>직원 데이터의 강화 설정이며 미연결된 기존 프리팹은 이전 설정을 사용합니다.</summary>
    public ServingStaffUpgradeData UpgradeData => _staffData != null ? _staffData.UpgradeData : _upgradeData;
    /// <summary>직원 데이터에 지정한 플레이어 해금 레벨입니다.</summary>
    public int UnlockPlayerLevel => _staffData != null ? _staffData.UnlockPlayerLevel :
        (_upgradeData != null ? _upgradeData.UnlockPlayerLevel : int.MaxValue);
    /// <summary>현재 이 NPC에 적용된 강화 레벨입니다.</summary>
    public int UpgradeLevel => _upgradeLevel;
    public SushiCarrier Carrier => _carrier;
    public float MoveSpeed => _moveSpeed;
    public bool AutomaticServing => _automaticServing;
    public ServingWorkState WorkState => _workState;
    public string StateReason => _stateReason;
    /// <summary>중앙에 도착하여 실제 음료 제공 시간을 누적 중인 손님입니다.</summary>
    public GuestNPC ActiveDrinkGuest { get; private set; }
    public bool CanServeDrink => isActiveAndEnabled && _automaticServing &&
        UpgradeData != null && UpgradeData.CanServeDrink(_upgradeLevel);

    /// <summary>직원 고유 속도를 반영하며 데이터 미연결 상태에서는 기존 강화 속도를 조회합니다.</summary>
    public bool TryGetMoveSpeed(int level, out float speed)
    {
        // 새 직원 데이터가 있으면 그 데이터의 검증과 계산 결과만 사용합니다.
        if (_staffData != null) return _staffData.TryGetMoveSpeed(level, out speed);
        // 아직 새 데이터로 전환하지 않은 기존 프리팹의 속도를 유지합니다.
        speed = 0f;
        return _upgradeData != null && _upgradeData.TryGetMoveSpeed(level, out speed);
    }

    /// <summary>영업 전에 직원 전용 데이터, 이동 평면과 실제 필수 참조를 검증합니다.</summary>
    public bool Configure(SushiManager manager, CookerNPC cooker, int level)
    {
        // 실패 이유를 남겨 아무 설명 없이 대기하는 상태를 방지합니다.
        if (UpgradeData == null) return FailConfigure("ServingStaffData와 강화 데이터를 연결하세요.");
        if (!TryGetMoveSpeed(level, out float speed))
            return FailConfigure("서빙 강화 단계와 이동속도 배열을 확인하세요.");
        if (manager == null || cooker == null) return FailConfigure("SushiManager와 CookerNPC를 연결하세요.");
        if (_standbyPoint == null || _leftLimit == null || _rightLimit == null)
            return FailConfigure("대기점과 좌우 이동 한계 Transform을 연결하세요.");
        if (_interactionSensor == null || _visual == null)
            return FailConfigure("직원 SphereCollider 센서와 Visual을 연결하세요.");
        if (_leftLimit.position.x >= _rightLimit.position.x ||
            _standbyPoint.position.x < _leftLimit.position.x ||
            _standbyPoint.position.x > _rightLimit.position.x)
            return FailConfigure("좌우 이동 한계와 대기점의 X좌표를 확인하세요.");
        // 다른 이동 스크립트가 위치를 다시 덮어쓰지 못하게 설정 오류를 알립니다.
        Mover sharedMover = GetComponent<Mover>();
        GuestMover guestMover = GetComponent<GuestMover>();
        if ((sharedMover != null && sharedMover.enabled) || (guestMover != null && guestMover.enabled))
            return FailConfigure("직원의 Mover 또는 GuestMover를 제거하거나 비활성화하세요.");
        foreach (Collider body in GetComponentsInChildren<Collider>(true))
            if (body != _interactionSensor && body.enabled && !body.isTrigger)
                return FailConfigure("플레이어를 밀지 않도록 직원 몸체의 일반 Collider를 비활성화하세요.");

        CancelDrinkTask();
        // 재설정 시 이전 손님의 음식 제공 진행을 이어받지 않습니다.
        ResetFoodTask();
        _manager = manager;
        _cooker = cooker;
        _rigidbody = GetComponent<Rigidbody>();
        _carrier = GetComponent<SushiCarrier>();
        _upgradeLevel = level;
        _moveSpeed = speed;
        _automaticServing = true;
        _floorY = _standbyPoint.position.y;
        _laneZ = _standbyPoint.position.z;
        // 이 직원만 코드로 움직이며 중력과 물리 충돌로 밀리지 않습니다.
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.constraints = RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        _interactionSensor.isTrigger = true;
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
        _hasMoveSpeedParameter = false;
        if (_animator != null)
        {
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
                if (parameter.name == "MoveSpeed" && parameter.type == AnimatorControllerParameterType.Float)
                    _hasMoveSpeedParameter = true;
        }
        // 설정된 직원 레벨을 걷기 애니메이션 재생 속도에 반영합니다.
        ApplyWalkAnimationSpeed();
        // 시작부터 대기 위치와 Idle 방향을 맞춥니다.
        // 최초 대기 위치를 복귀 목적지로 지정합니다.
        _standbyTargetX = _standbyPoint.position.x;
        // 영업 시작 직후에는 새 랜덤 좌표를 선택하지 않습니다.
        _standbySelected = true;
        transform.position = _standbyPoint.position;
        _rigidbody.position = _standbyPoint.position;
        _configured = true;
        MoveToX(_standbyPoint.position.x);
        // 현재 해금 상태와 대기의 세부 원인을 함께 표시합니다.
        bool locked = manager.CurrentPlayerLevel < UnlockPlayerLevel;
        SetState(locked ? ServingWorkState.Locked : ServingWorkState.Waiting,
            locked ? "플레이어 레벨이 직원 해금 조건보다 낮습니다." :
            "영업 시작을 기다립니다.");
        // 대기 위치와 방향 설정 후 외형을 적용하여 이전 위치의 잔상을 남기지 않습니다.
        if (_appearanceView != null)
            _appearanceView.Apply(UpgradeData.GetAppearance(level));
        return true;
    }

    /// <summary>직원의 강화 레벨에 따라 걷기 애니메이션 재생 속도를 2~2.5배로 설정합니다.</summary>
    void ApplyWalkAnimationSpeed()
    {
        // Animator 또는 강화 데이터가 없으면 적용하지 않습니다.
        if (_animator == null || UpgradeData == null) return;

        // 최대 레벨이 1 이하인 경우에도 계산 가능한 기준을 확보합니다.
        int maxLevel = Mathf.Max(1, UpgradeData.MaxLevel);
        // 현재 강화 레벨을 유효한 범위로 제한합니다.
        int level = Mathf.Clamp(_upgradeLevel, 1, maxLevel);
        // 1레벨은 0, 최대 레벨은 1이 되도록 진행 비율을 계산합니다.
        float ratio = maxLevel > 1 ? (float)(level - 1) / (maxLevel - 1) : 0f;
        // 레벨 진행 비율에 따라 2~2.5 사이의 재생 속도를 계산합니다.
        float animationSpeed = Mathf.Lerp(2f, 2.5f, ratio);
        // Walk 상태에 연결한 전용 파라미터에 계산 결과를 전달합니다.
        _animator.SetFloat("WalkAnimationSpeed", animationSpeed);
    }

    /// <summary>설정 실패를 인스펙터와 로그에 남깁니다.</summary>
    bool FailConfigure(string reason)
    {
        // 임시로 이동을 강행하지 않고 누락된 실제 설정을 알립니다.
        _configured = false;
        CancelDrinkTask();
        // 설정 실패 시 이전 음식 대기도 정리합니다.
        ResetFoodTask();
        SetState(ServingWorkState.Locked, reason);
        // 재설정 실패 시 이전 프레임의 걷기 표시를 남기지 않습니다.
        if (_animator != null && _hasMoveSpeedParameter) _animator.SetFloat("MoveSpeed", 0f);
        Debug.LogWarning($"[서빙 직원 설정 실패] {reason}", this);
        return false;
    }

    /// <summary>음식은 손님 앞에서 일정 시간 기다린 후 전달하고 음료는 기존 방식으로 제공합니다.</summary>
    void FixedUpdate()
    {
        // 초기화 실패는 Locked와 StateReason으로 표시하며 작업을 진행하지 않습니다.
        if (!_configured || _manager == null || _cooker == null)
        {
            // 유효하지 않은 작업의 대기시간을 다음 작업에 넘기지 않습니다.
            ResetFoodTask();
            return;
        }
        if (_manager.CurrentPlayerLevel < UnlockPlayerLevel)
        {
            // 비활성화 제어와 별개로 해금되지 않은 직원의 작업도 차단합니다.
            ResetFoodTask();
            CancelDrinkTask();
            MoveToX(_rigidbody.position.x);
            SetState(ServingWorkState.Locked, "플레이어 레벨이 직원 해금 조건보다 낮습니다.");
            return;
        }
        if (!_manager.IsMenuConfirmed)
        {
            // 영업 준비 상태에서는 진행 중인 음식 대기도 취소합니다.
            ResetFoodTask();
            CancelDrinkTask();
            MoveToX(_standbyPoint.position.x);
            SetState(ServingWorkState.Waiting, "영업 시작을 기다립니다.");
            return;
        }
        // 음식을 들면 다른 음식을 추가 수령하거나 음료를 동시에 제공하지 않습니다.
        if (_carrier.CurrentSushi != null)
        {
            CancelDrinkTask();
            UpdateFoodTask();
            return;
        }
        // 음식이 없어졌다면 이전 음식 대상과 대기시간을 정리합니다.
        ResetFoodTask();
        // 시작한 음료 작업은 완료 또는 취소까지 유지하여 진행 시간이 흔들리지 않습니다.
        if (_drinkTask && _target != null && CanServeDrink && _target.CanHoldInteract(_carrier))
        {
            UpdateDrinkTask();
            return;
        }
        CancelDrinkTask();
        GuestNPC foodTarget = null;
        if (_cooker.TryPeekCompletedFood(out Sushi food))
            foodTarget = _manager.FindMostUrgentGuest(food);
        GuestNPC drinkTarget = null;
        if (CanServeDrink) drinkTarget = _manager.FindMostUrgentDrink(_carrier);
        // 빈손일 때는 지금 제공 가능한 음식과 음료 중 더 급한 일을 선택합니다.
        if (drinkTarget != null && (foodTarget == null ||
            drinkTarget.RemainingWaitingTime < foodTarget.RemainingWaitingTime))
        {
            _target = drinkTarget;
            _drinkTask = true;
            UpdateDrinkTask();
            return;
        }
        if (foodTarget != null)
        {
            _target = foodTarget;
            SetState(ServingWorkState.ServingFood, "쿠커에게 완성 음식을 받으러 갑니다.");
            // 실제 수령 직전에 목록을 다시 검사해 플레이어와 중복 수령하지 않습니다.
            if (MoveToArea(_cooker.InteractionArea)) _cooker.TryInteract(_carrier);
            return;
        }
        ReturnToStandby();
    }

    /// <summary>음식 손님에게 접근한 뒤 일정 시간 바라보고 실제 음식 전달을 시도합니다.</summary>
    void UpdateFoodTask()
    {
        // 손님이 떠났거나 먼저 서빙받았다면 현재 음식을 받을 손님을 다시 찾습니다.
        if (_foodTarget == null || !_foodTarget.CanReceiveFrom(_carrier))
        {
            ResetFoodTask();
            _foodTarget = _manager.FindMostUrgentGuest(_carrier.CurrentSushi);
        }

        // 받을 손님이 없다면 기존 정책대로 음식을 폐기하고 복귀합니다.
        if (_foodTarget == null)
        {
            _target = null;
            _carrier.TryDiscard();
            ReturnToStandby();
            return;
        }

        // 인스펙터에도 유지 중인 실제 음식 제공 대상을 표시합니다.
        _target = _foodTarget;
        if (!MoveToArea(_foodTarget.InteractionArea))
        {
            // 접촉 영역을 벗어났다면 도착 후 처음부터 다시 기다립니다.
            _foodServingElapsed = 0f;
            SetState(ServingWorkState.ServingFood, "손님에게 음식을 운반합니다.");
            return;
        }

        // 이동을 멈춘 뒤 모델만 손님을 바라보게 하고 접촉 중인 시간을 누적합니다.
        FaceFoodTarget();
        _foodServingElapsed += Time.fixedDeltaTime;
        SetState(ServingWorkState.WaitingToServeFood, "손님을 바라보며 음식 전달을 준비합니다.");
        if (_foodServingElapsed < Mathf.Max(0f, _foodServingDelay)) return;

        // 실제 음식 전달 성공에 대해서만 해당 직원의 실적을 증가시킵니다.
        bool served = _foodTarget.TryInteract(_carrier);
        // 음식 전달에 성공한 경우에만 실적과 다음 복귀 위치를 갱신합니다.
        if (served)
        {
            // 실제 음식 전달 횟수를 증가시킵니다.
            _servedCount++;
            // 다음 복귀 시 새로운 랜덤 좌표를 선택하도록 합니다.
            _standbySelected = false;
        }

        // 이번 대기를 끝내고 다음 물리 프레임에 다음 작업을 선택합니다.
        ResetFoodTask();
        _target = null;
        SetState(ServingWorkState.ServingFood,
            served ? "음식 전달을 완료했습니다." : "음식 전달 대상을 다시 확인합니다.");
    }

    /// <summary>루트와 센서는 유지하면서 시각 모델만 음식 제공 대상의 수평 방향으로 회전합니다.</summary>
    void FaceFoodTarget()
    {
        // 대상이나 시각 모델이 없다면 방향을 변경하지 않습니다.
        if (_foodTarget == null || _visual == null) return;

        // 구역별 높이 차이로 모델이 위아래로 기울지 않게 합니다.
        Vector3 direction = _foodTarget.transform.position - _visual.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return;

        // 상호작용 센서의 위치가 바뀌지 않도록 모델만 돌립니다.
        _visual.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    /// <summary>진행 중인 음식 제공 대상과 전달 전 대기시간을 초기화합니다.</summary>
    void ResetFoodTask()
    {
        // 다음 손님이 이전 손님의 대기시간을 이어받지 않도록 정리합니다.
        _foodTarget = null;
        _foodServingElapsed = 0f;
    }

    /// <summary>손님 영역에서 정지한 채 메뉴별 음료 제공 시간을 채웁니다.</summary>
    void UpdateDrinkTask()
    {
        // 매 물리 프레임 실제 제공 상태를 다시 확인합니다.
        ActiveDrinkGuest = null;
        // 실제 접촉 판정에 사용하는 서빙 영역 중앙까지 접근합니다.
        SetState(ServingWorkState.ServingDrink, "손님의 서빙 영역 중앙으로 이동합니다.");
        if (_target == null || !_target.CanHoldInteract(_carrier))
        {
            // 사라졌거나 다른 제공자가 맡은 주문은 정리합니다.
            CancelDrinkTask();
            return;
        }
        if (!MoveToDrinkCenter(_target))
        {
            // 실제 접촉을 유지하지 못한 시간은 누적하지 않습니다.
            _target.CancelHoldInteract(_carrier);
            return;
        }
        if (_target.TickHoldInteract(_carrier, _interactionSensor, Time.fixedDeltaTime))
        {
            // 해당 직원이 음료 제공까지 성공한 경우에만 한 번 집계합니다.
            _servedCount++;
            // 다음 복귀 시 새로운 랜덤 좌표를 선택하도록 합니다.
            _standbySelected = false;
            // 완료된 음료 작업과 대상 참조를 정리합니다.
            CancelDrinkTask();
            // 이번 프레임의 음료 처리를 종료합니다.
            return;
        }
        // 실제로 시간이 누적되는 동안만 연출 대상을 공개합니다.
        if (_target.CanHoldInteract(_carrier) && _target.DrinkElapsed > 0f &&
            _target.DrinkElapsed < _target.SelectedSushi.DrinkInteractionTime)
            ActiveDrinkGuest = _target;
    }

    /// <summary>
    /// 직원 센서를 손님의 실제 서빙 영역 중심 X에 맞추고,
    /// 도착 후 접촉이 확인되면 음료 제공을 허용합니다.
    /// </summary>
    bool MoveToDrinkCenter(GuestNPC guest)
    {
        // 손님이 사라졌다면 이동과 제공을 중단합니다.
        if (guest == null) return false;

        // 의자 위치 대신 실제 음료 제공 판정에 사용하는 영역을 가져옵니다.
        Collider area = guest.InteractionArea;

        // 센서나 대상 영역이 비활성 상태라면 접촉을 검사하지 않습니다.
        if (_interactionSensor == null || !_interactionSensor.enabled ||
            !_interactionSensor.gameObject.activeInHierarchy ||
            area == null || !area.enabled || !area.gameObject.activeInHierarchy)
        {
            // 유효한 작업 영역이 없으므로 현재 위치에서 멈춥니다.
            MoveToX(_rigidbody.position.x);
            _stateReason = "직원 센서 또는 손님 서빙 영역이 없거나 비활성입니다.";
            return false;
        }

        // 직원 루트와 센서 중심 사이의 월드 X 간격을 보정합니다.
        float sensorOffsetX =
            _interactionSensor.bounds.center.x - _rigidbody.position.x;
        // 센서 중심이 서빙 영역 중심과 일치할 직원 루트의 목적지입니다.
        float targetX = area.bounds.center.x - sensorOffsetX;

        // 이동 가능한 통로 밖이라면 잘못된 위치에서 제공하지 않습니다.
        if (targetX < _leftLimit.position.x || targetX > _rightLimit.position.x)
        {
            // 이동 한계 설정 문제를 실제 목적지와 함께 표시합니다.
            MoveToX(_rigidbody.position.x);
            _stateReason =
                $"음료 서빙 목적지가 이동 한계 밖입니다. 대상: {guest.name}, X: {targetX:F3}";
            return false;
        }

        // 이동 예약 전에 현재 물리 위치의 도착 여부를 확인합니다.
        bool arrived = Mathf.Abs(_rigidbody.position.x - targetX) <= 0.001f;
        // 기존 이동 함수로 직원 통로의 Y/Z를 유지하며 접근합니다.
        MoveToX(targetX);
        // 이동 중에는 음료 제공 시간을 누적하지 않습니다.
        if (!arrived)
        {
            _stateReason = "손님의 서빙 영역 중앙으로 이동합니다.";
            return false;
        }

        // 중앙에 도착했더라도 실제 센서 접촉까지 확인해야 제공할 수 있습니다.
        if (!SushiInteractor.IsTouching(_interactionSensor, area))
        {
            // 목적지 도착과 접촉 실패를 구분해 설정 문제를 확인할 수 있게 합니다.
            _stateReason =
                $"서빙 영역 중앙 도착 후 접촉 실패. 대상: {guest.name}, " +
                "직원 센서와 서빙 영역의 높이·깊이·크기를 확인하세요.";
            return false;
        }

        // 도착과 접촉이 모두 확인되면 걷기 애니메이션을 정지합니다.
        if (_animator != null && _hasMoveSpeedParameter) _animator.SetFloat("MoveSpeed", 0f);
        // 호출한 UpdateDrinkTask에서 음료 제공 시간을 누적하도록 허용합니다.
        _stateReason = "서빙 영역 중앙에서 음료를 제공합니다.";
        return true;
    }

    /// <summary>현재 음료 작업만 취소하고 대상의 잠금을 반환합니다.</summary>
    void CancelDrinkTask()
    {
        // 완료·취소·비활성화 시 작업 연출을 종료합니다.
        ActiveDrinkGuest = null;
        // 음식 전달 대상에게는 음료 취소 요청을 보내지 않습니다.
        if (_drinkTask && _target != null && _carrier != null)
            _target.CancelHoldInteract(_carrier);
        _drinkTask = false;
        _target = null;
    }

    /// <summary>두 대기점 사이에서 임의 위치를 한 번 선택해 복귀하고 대기합니다.</summary>
    void ReturnToStandby()
    {
        // 복귀 시작 시에만 선분 위의 목적지를 선택합니다.
        if (!_standbySelected)
        {
            // 두 번째 지점이 없는 기존 설정은 첫 번째 지점으로 복귀합니다.
            float startX = _standbyPoint.position.x;
            float endX = _standbyPoint2 != null ? _standbyPoint2.position.x : startX;
            // 이동 한계와 겹치는 구간에서 추첨해 경계에 선택 확률이 몰리지 않게 합니다.
            float minimumX = Mathf.Clamp(Mathf.Min(startX, endX), _leftLimit.position.x, _rightLimit.position.x);
            float maximumX = Mathf.Clamp(Mathf.Max(startX, endX), _leftLimit.position.x, _rightLimit.position.x);
            // 기존 X축 통로 위에서 연속적인 랜덤 좌표를 저장합니다.
            _standbyTargetX = Random.Range(minimumX, maximumX);
            _standbySelected = true;
        }
        // 선택한 위치는 이동 중과 대기 중에 그대로 유지합니다.
        MoveToX(_standbyTargetX);
        // 실제 물리 위치로 도착 여부를 판단합니다.
        SetState(Mathf.Abs(_rigidbody.position.x - _standbyTargetX) <= 0.01f ?
            ServingWorkState.Waiting : ServingWorkState.Returning, "");
    }

    /// <summary>실제 접촉 전까지 접근하고 접촉했다면 정지하여 상호작용합니다.</summary>
    bool MoveToArea(Collider area)
    {
        if (area == null || !area.enabled || !area.gameObject.activeInHierarchy)
        {
            MoveToX(_rigidbody.position.x);
            // 작업 종류를 바꾸지 않고 접근 실패의 이유만 표시합니다.
            _stateReason = "대상 상호작용 Collider가 없거나 비활성입니다.";
            return false;
        }
        if (SushiInteractor.IsTouching(_interactionSensor, area))
        {
            // 넓은 영역에 닿은 이후에도 중앙까지 계속 걸어가지 않습니다.
            MoveToX(_rigidbody.position.x);
            return true;
        }
        float offsetX = _interactionSensor.bounds.center.x - _rigidbody.position.x;
        float targetX = Mathf.Clamp(area.bounds.center.x - offsetX,
            _leftLimit.position.x, _rightLimit.position.x);
        MoveToX(targetX);
        if (Mathf.Abs(_rigidbody.position.x - targetX) <= 0.01f)
            _stateReason = $"목표 X에는 도착했지만 접촉하지 못했습니다. 센서 Y/Z와 이동 한계를 확인하세요. 대상: {area.name}";
        return false;
    }

    /// <summary>좌우로만 이동하고 정지 중에는 시각 모델의 Y각도를 180도로 맞춥니다.</summary>
    void MoveToX(float destinationX)
    {
        float currentX = _rigidbody.position.x;
        float targetX = Mathf.Clamp(destinationX, _leftLimit.position.x, _rightLimit.position.x);
        float nextX = Mathf.MoveTowards(currentX, targetX, _moveSpeed * Time.fixedDeltaTime);
        bool moving = Mathf.Abs(nextX - currentX) > 0.0001f;
        // 루트와 센서는 회전하지 않으며 실제 모델만 좌우 또는 Idle 방향으로 돌립니다.
        _visual.rotation = Quaternion.Euler(0f, moving ? (nextX > currentX ? 90f : -90f) : 180f, 0f);
        if (_animator != null && _hasMoveSpeedParameter)
            _animator.SetFloat("MoveSpeed", moving ? Mathf.Abs(nextX - currentX) / Time.fixedDeltaTime : 0f);
        _rigidbody.MovePosition(new Vector3(nextX, _floorY, _laneZ));
    }

    /// <summary>직원의 실제 작업 상태와 정지 이유를 갱신합니다.</summary>
    void SetState(ServingWorkState state, string reason)
    {
        // 현재 작업 상태를 기록합니다.
        _workState = state;
        // 현재 상태의 원인을 기록합니다.
        _stateReason = reason;
    }

    /// <summary>ContextMenu에서 직원의 실제 정지 조건을 출력합니다.</summary>
    [ContextMenu("디버그/서빙 상태 출력")]
    void PrintServingState()
    {
        // 편집 중에는 초기화 전이므로 플레이 상태도 함께 알립니다.
        bool menuConfirmed = _manager != null && _manager.IsMenuConfirmed;
        bool receivable = _cooker != null && _cooker.HasReceivableOrder;
        Debug.Log($"[서빙 직원] Playing={Application.isPlaying}, Active={isActiveAndEnabled}, " +
            $"Configured={_configured}, Level={_upgradeLevel}, Menu={menuConfirmed}, " +
            $"Receivable={receivable}, TimeScale={Time.timeScale}, State={_workState}, {_stateReason}", this);
    }

    /// <summary>직원이 비활성화되면 진행 중인 음식 대기와 음료 작업을 취소합니다.</summary>
    void OnDisable()
    {
        // 작업 중인 손님에게 잠금이 남지 않도록 반환합니다.
        CancelDrinkTask();
        // 재활성화 시 이전 손님의 음식 대기시간을 이어받지 않습니다.
        ResetFoodTask();
        // 비활성 직원이 이전 작업 중인 상태로 표시되지 않게 합니다.
        bool locked = !_configured || (_manager != null && UpgradeData != null &&
            _manager.CurrentPlayerLevel < UnlockPlayerLevel);
        SetState(locked ? ServingWorkState.Locked : ServingWorkState.Waiting,
            "직원 오브젝트가 비활성 상태입니다.");
    }
}
