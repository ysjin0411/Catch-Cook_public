using System;
using UnityEngine;

public enum GuestState
{
    Entering = 0,          // 입장 이동
    ArrivalIdle = 1,       // 착석 전 대기
    Ordering = 2,          // 주문
    Waiting = 3,           // 음식 대기
    Eating = 4,            // 식사
    LeavingIdle = 5,       // 퇴장 전 대기
    Leaving = 6,           // 퇴장 이동
    ApproachingSearch = 7, // 빈자리 대기 구역으로 이동
    SearchingSeat = 8,     // 빈자리 탐색
    Angry = 9             // 음식 대기 실패 후 분노 연출
}

[RequireComponent(typeof(GuestMover))]
[DefaultExecutionOrder(-100)]
public class GuestNPC : NPC, ISushiHoldInteraction
{
    [Header("자리 앉기 전 기다리는 시간")]
    // 해당 구역에 도착하여 빈자리를 기다릴 최대 시간입니다.
    [SerializeField, Min(0.1f)] float _seatWaitTime = 10f;
    // 왕복을 시작한 뒤 남은 빈자리 대기시간입니다.
    [SerializeField] float _remainingSeatWaitTime;
    // 좌석을 배정하거나 빈자리를 기다릴 구역입니다.
    ChairArea _seatArea;
    // 해당 구역의 첫 번째 의자 앞 위치입니다.
    Transform _searchFirstPoint;
    // 해당 구역의 가장 안쪽 의자 앞 위치입니다.
    Transform _searchInnerPoint;

    [Header("드링크 관련(주문음료/누적상호작용시간")]
    // 음료 제공자 한 명의 연속 상호작용 시간입니다.
    [SerializeField] SushiHoldSession _drinkSession = new();
    // 미니게임으로 음료를 제공할 예약자와 시작 당시의 주문입니다.
    SushiCarrier _minigameProvider;
    Sushi _minigameOrder;

    [Header("음식 대기 실패 연출")]
    [SerializeField] GuestAngryReaction _angryReaction;

    // 음식 제공과 식사 상태에 따른 파티클 연출 담당입니다.
    [SerializeField] GuestServingEffects _servingEffects;

    public ChairArea SeatArea => _seatArea;
    public float RemainingSeatWaitTime => _remainingSeatWaitTime;
    public bool IsSeekingSeat => _initialized &&
        (_state == GuestState.ApproachingSearch || _state == GuestState.SearchingSeat);
    public bool IsWaitingForDrink => IsWaiting && _selectedSushi != null &&
        _selectedSushi.Type == MenuType.Drink;
    public float DrinkElapsed => _drinkSession.Elapsed;
    [Header("이동")]
    // 손님의 이동 속도와 현재 통로의 Z좌표를 관리합니다.
    [SerializeField] GuestMover _mover;
    [SerializeField, Min(0.01f)] float _moveSpeed = 2f;
    [SerializeField, Min(0.001f)] float _arrivalDistance = 0.05f;

    [Header("이동 중 도착 높이 허용 오차")]
    [SerializeField, Min(0.01f)] float _arrivalHeightTolerance = 0.15f;

    [Header("착석 전 / 퇴장 전 대기시간")]
    [SerializeField, Min(0f)] float _arrivalIdleTime = 1f;
    [SerializeField, Min(0f)] float _leavingIdleTime = 1f;
    [SerializeField] float _remainingIdleTime;

    // 입장과 퇴장 시 사용할 월드 Y축 각도입니다.
    const float EnteringYaw = 90f;
    const float LeavingYaw = -90f;

    [Header("애니메이션")]
    [SerializeField] Animator _animator;

    // 착석 후 플레이어의 서빙 통로에 배치할 상호작용 영역입니다.
    [SerializeField] BoxCollider _interactableCollider;

    // Animator의 손님 상태 Parameter를 식별합니다.
    static readonly int GuestStateParameter = Animator.StringToHash("GuestState");
    // 착석할 때 바로 적용할 주문 애니메이션을 식별합니다.
    static readonly int OrderingAnimationState = Animator.StringToHash("Base Layer.Ordering");
    // 착석 전과 퇴장 전의 서 있는 Idle 애니메이션을 식별합니다.
    static readonly int ArrivalIdleAnimationState = Animator.StringToHash("Base Layer.ArrivalIdle");
    static readonly int LeavingIdleAnimationState = Animator.StringToHash("Base Layer.LeavingIdle");

    [Header("주문 및 대기 상태")]
    [SerializeField] Sushi _selectedSushi;
    [SerializeField] float _remainingWaitingTime;
    // 만족도의 최댓값입니다.
    const float MaxSatisfaction = 100f;
    // 현재 주문의 최대 대기시간입니다.
    float _maxWaitingTime;
    // 현재 손님의 만족도입니다.
    [SerializeField] float _satisfaction;
    [SerializeField] GuestState _state;

    [Header("주문 하는 시간")]
    [SerializeField, Min(0f)] float _orderingTime = 3f;
    [SerializeField] float _remainingOrderingTime;

    [Header("남은 식사시간")]
    [SerializeField] float _remainingEatingTime;

    SushiManager _sushiManager;
    Transform _destination;
    Transform _leftPoint;
    bool _initialized;
    Rigidbody _rigidbody;
    Transform _hips;
    bool _originalUseGravity;
    RigidbodyConstraints _originalConstraints;
    bool _pendingSeatAlignment;

    /// <summary>
    /// 손님이 선택한 주문 메뉴입니다. 메뉴가 정해지기 전에는 null입니다.
    /// </summary>
    public Sushi SelectedSushi => _selectedSushi;

    /// <summary>
    /// 이 손님에게 메뉴를 제공하여 실제 지급받은 판매금액입니다.
    /// 강화가 반영된 확정 금액이며, 서빙 성공 전에는 0입니다.
    /// </summary>
    public float ServedSaleGold { get; private set; }
    // 서빙 직원이 접촉할 기존 손님 서빙 영역입니다.
    public Collider InteractionArea => _interactableCollider;
    public Chair Seat { get; private set; }
    public float RemainingIdleTime => _remainingIdleTime;
    public float RemainingOrderingTime => _remainingOrderingTime;
    /// <summary>
    /// 현재 주문에서 남은 음식 대기시간입니다.
    /// 음식 대기 중에 감소하며, 서빙받거나 퇴장하면 0이 됩니다.
    /// </summary>
    public float RemainingWaitingTime => _remainingWaitingTime;

    /// <summary>
    /// 음식 대기를 시작할 때 저장한 현재 주문의 최대 대기시간입니다.
    /// </summary>
    public float MaxWaitingTime => _maxWaitingTime;
    // 첫 서빙 실습 손님은 음식을 받을 때까지 주문을 유지합니다.
    internal bool WaitForTutorialServing { get; set; }
    // 외부에서 조회할 수 있는 0~100 범위의 만족도입니다.
    public float Satisfaction => _satisfaction;
    // UI의 fillAmount에 사용할 0~1 범위의 만족도입니다.
    public float SatisfactionRatio => _satisfaction / MaxSatisfaction;
    public float RemainingEatingTime => _remainingEatingTime;
    public bool IsWaiting => _initialized && _state == GuestState.Waiting;
    public bool IsEating => _initialized && _state == GuestState.Eating;
    public bool IsOrdering => _initialized && _state == GuestState.Ordering;
    public GuestState State => _state;

    public event Action<GuestNPC> OrderStarted;

    /// <summary>
    /// 이동, 애니메이션, 엉덩이 본과 원래 물리 설정을 준비합니다.
    /// </summary>
    void Awake()
    {
        // Inspector에서 지정하지 않았다면 같은 오브젝트에서 찾습니다.
        if (_servingEffects == null)
        {
            _servingEffects = GetComponent<GuestServingEffects>();
        }

        // 지정하지 않았다면 같은 오브젝트에서 손님 전용 이동 컴포넌트를 찾습니다.
        if (_mover == null) _mover = GetComponent<GuestMover>();

        // 지정하지 않았다면 자식을 포함해 Animator를 찾습니다.
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        // 순간이동과 착석 중 위치 고정에 사용할 Rigidbody를 가져옵니다.
        _rigidbody = GetComponent<Rigidbody>();

        // 퇴장 이동 때 복원할 원래 물리 설정을 저장합니다.
        _originalUseGravity = _rigidbody.useGravity;
        _originalConstraints = _rigidbody.constraints;

        // 이동 중인 손님은 서빙 대상으로 감지되지 않게 합니다.
        if (_interactableCollider != null) _interactableCollider.enabled = false;

        // Humanoid 캐릭터의 엉덩이 정렬에 사용할 본을 가져옵니다.
        if (_animator != null && _animator.isHuman)
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
    }

    /// <summary>
    /// 손님의 상태와 상호작용 영역, 애니메이션을 갱신합니다.
    /// Eating 진입 시 동전을 한 번 재생하고, Eating 동안 하트를 유지합니다.
    /// </summary>
    void ChangeState(GuestState nextState)
    {
        // 다른 상태에서 Eating으로 진입하는 순간인지 변경 전에 확인합니다.
        bool enteredEating =
            _state != GuestState.Eating && nextState == GuestState.Eating;

        // 게스트의 현재 논리 상태를 저장합니다.
        _state = nextState;

        // 메뉴 타입과 관계없이 현재 상태에 맞춰 파티클을 제어합니다.
        if (_servingEffects != null)
        {
            // Ordering 동안 원 표시를 반복하고, 벗어나면 즉시 숨깁니다.
            _servingEffects.SetOrdering(_state == GuestState.Ordering);

            // Eating 동안 하트를 유지하고, 벗어나면 추가 방출을 멈춥니다.
            _servingEffects.SetEating(_state == GuestState.Eating);

            // Eating에 진입한 순간에만 동전을 한 번 재생합니다.
            if (enteredEating)
            {
                // 음식과 음료 구분 없이 동전 파티클을 재생합니다.
                _servingEffects.PlayServingCoins();
            }
        }

        // 음식을 기다릴 때만 서빙 영역을 활성화합니다.
        if (_interactableCollider != null)
        {
            _interactableCollider.enabled =
                nextState == GuestState.Waiting;
        }

        // Animator가 없다면 논리 상태만 변경합니다.
        if (_animator == null) return;

        // 기본적으로 논리 상태와 동일한 값을 Animator에 전달합니다.
        int animationState = (int)nextState;

        // 빈자리 탐색 상태에서는 기존 걷기 애니메이션을 사용합니다.
        if (nextState == GuestState.ApproachingSearch ||
            nextState == GuestState.SearchingSeat)
        {
            animationState = (int)GuestState.Entering;
        }

        // 분노 중 기본 컨트롤러는 서 있는 상태를 유지합니다.
        // 실제 분노와 던지기는 Animator의 Angry 레이어에서 재생합니다.
        if (nextState == GuestState.Angry)
        {
            animationState = (int)GuestState.LeavingIdle;
        }

        // 계산한 상태값을 기존 Animator Controller에 전달합니다.
        _animator.SetInteger(GuestStateParameter, animationState);
    }

    /// <summary>
    /// 이동 입력과 Rigidbody에 남아 있는 속도를 정지합니다.
    /// </summary>
    void StopMovement()
    {
        // Mover에 남아 있는 수평 이동 입력을 제거합니다.
        _mover.MoveHorizontal(0f, 0f);

        // 순간이동 후 이전 속도로 움직이지 않도록 정지합니다.
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 캐릭터 루트와 Rigidbody를 지정한 위치 및 방향으로 순간이동합니다.
    /// </summary>
    void TeleportRoot(Vector3 position, Quaternion rotation)
    {
        // 자식 본의 월드 위치 계산에도 즉시 반영합니다.
        transform.SetPositionAndRotation(position, rotation);

        // 물리 오브젝트의 위치와 방향도 같은 값으로 맞춥니다.
        _rigidbody.position = position;
        _rigidbody.rotation = rotation;

        // 입장, 착석 및 퇴장 위치에 맞춰 손님의 이동 평면도 갱신합니다.
        _mover.SetMovementPlane(position.z);
    }

    /// <summary>배정받은 메뉴를 확인하고 좌석이 없으면 해당 구역에서 빈자리를 기다립니다.</summary>
    public bool Initialize(SushiManager sushiManager, Transform leftPoint)
    {
        // 필수 참조와 애니메이션 골격을 검증합니다.
        if (_initialized || !isActiveAndEnabled || sushiManager == null || leftPoint == null) return false;
        if (_animator == null || !_animator.isActiveAndEnabled ||
            _animator.runtimeAnimatorController == null || _hips == null)
        {
            Debug.LogError($"[손님 초기화 실패] {name}: Animator, Controller, Humanoid Hips를 확인하세요.", this);
            return false;
        }
        // 목록 복사 없이 매니저가 이 손님에게 실제 메뉴를 배정했는지 확인합니다.
        if (!sushiManager.TryGetGuestMenu(this, out _)) return false;
        // 좌석이 없어도 입장 및 대기 상태를 유지할 수 있게 참조부터 보관합니다.
        _sushiManager = sushiManager;
        _leftPoint = leftPoint;
        bool reserved = sushiManager.TryReserveSeat(this, out Chair seat);
        Seat = seat;
        if (reserved)
        {
            if (seat == null || !seat.IsValid)
            {
                sushiManager.ReleaseSeat(this);
                Seat = null;
                return false;
            }
            _seatArea = seat.Area;
            _destination = seat.ArrivePos;
        }
        else
        {
            if (!sushiManager.TryChooseWaitingArea(out _seatArea,
                out _searchFirstPoint, out _searchInnerPoint)) return false;
            _destination = _searchFirstPoint;
            sushiManager.EnqueueSeatWaiter(this);
        }
        // 이전 입력과 상태를 초기화하고 선택한 구역의 깊이로 진입합니다.
        _selectedSushi = null;
        // 새 손님 초기화 시 이전 판매금액을 제거합니다.
        ServedSaleGold = 0f;
        WaitForTutorialServing = false;
        _remainingOrderingTime = 0f;
        _remainingWaitingTime = 0f;
        // 이전 주문의 최대 대기시간과 만족도를 초기화합니다.
        _maxWaitingTime = 0f;
        _satisfaction = 0f;
        _remainingEatingTime = 0f;
        _remainingIdleTime = 0f;
        _remainingSeatWaitTime = 0f;
        _pendingSeatAlignment = false;
        _drinkSession.Reset();
        Vector3 spawnPosition = transform.position;
        spawnPosition.z = _destination.position.z;
        TeleportRoot(spawnPosition, Quaternion.Euler(0f, EnteringYaw, 0f));
        _initialized = true;
        ChangeState(reserved ? GuestState.Entering : GuestState.ApproachingSearch);
        return true;
    }

    /// <summary>입장, 대기 구역 접근 및 퇴장 중 현재 목적지까지 이동합니다.</summary>
    void FixedUpdate()
    {
        // 이동 상태인 손님만 처리합니다.
        if (!_initialized) return;
        // 빈자리 대기 왕복은 일반 입장 경로와 분리하여 처리합니다.
        if (_state == GuestState.SearchingSeat)
        {
            UpdateSeatSearchMovement();
            return;
        }
        if (_state != GuestState.Entering && _state != GuestState.Leaving &&
            _state != GuestState.ApproachingSearch) return;

        // 목적지가 제거되었다면 더 이상 이동할 수 없으므로 손님을 정리합니다.
        if (_destination == null)
        {
            // 이동 실패 원인을 확인할 수 있도록 알립니다.
            Debug.LogWarning($"[손님 이동 실패] {name}: 목적지가 없습니다.", this);

            // 주문과 자리 반환은 OnDestroy에서 처리합니다.
            Destroy(gameObject);
            return;
        }

        // 현재 위치와 목적지 사이의 X축 차이를 계산합니다.
        float offsetX = _destination.position.x - transform.position.x;
        float distanceX = Mathf.Abs(offsetX);

        // 목적지의 높이에 실제로 도달했는지 검사합니다.
        float distanceY = Mathf.Abs(_destination.position.y - transform.position.y);

        // 수평 거리와 높이의 허용 오차를 함께 확인합니다.
        bool arrived = distanceX <= Mathf.Max(0.001f, _arrivalDistance) &&
            distanceY <= Mathf.Max(0.01f, _arrivalHeightTolerance);

        // 목적지에 도착했다면 현재 상태에 맞는 다음 행동으로 전환합니다.
        if (arrived)
        {
            // Y축 속도를 유지하면서 수평 이동을 정지합니다.
            _mover.MoveHorizontal(0f, 0f);

            // 대기할 구역에 도착한 시점부터 왕복 제한시간을 계산합니다.
            if (_state == GuestState.ApproachingSearch)
            {
                StopMovement();
                _remainingSeatWaitTime = Mathf.Max(0.1f, _seatWaitTime);
                _destination = _searchInnerPoint;
                ChangeState(GuestState.SearchingSeat);
                return;
            }
            // 의자 앞에 도착했다면 착석 전 Idle을 시작합니다.
            if (_state == GuestState.Entering)
            {
                // 도착 위치에 정렬하고 설정한 시간 동안 대기합니다.
                BeginArrivalIdle();
                return;
            }

            // 퇴장 위치에 도착한 손님을 제거합니다.
            // 식사 결과는 퇴장 준비가 아니라 실제 출구 도착 시 집계합니다.
            if (_sushiManager != null) _sushiManager.NotifyGuestExited(this);
            Destroy(gameObject);
            return;
        }

        // 한 번의 물리 이동으로 목적지를 지나치지 않도록 속도를 제한합니다.
        float speed = Mathf.Min(Mathf.Max(0f, _moveSpeed), distanceX / Time.fixedDeltaTime);

        // 목적지가 있는 방향으로 수평 이동을 요청합니다.
        _mover.MoveHorizontal(Mathf.Sign(offsetX), speed);
    }

    /// <summary>
    /// 의자 앞에서 정지하고 설정한 시간 동안 Idle을 재생합니다.
    /// </summary>
    void BeginArrivalIdle()
    {
        // 도착한 손님의 이동 입력과 속도를 정지합니다.
        StopMovement();

        // 의자 앞의 정확한 위치와 입장 방향으로 정렬합니다.
        TeleportRoot(Seat.ArrivePos.position, Quaternion.Euler(0f, EnteringYaw, 0f));

        // 착석 전 대기시간을 설정합니다.
        _remainingIdleTime = Mathf.Max(0f, _arrivalIdleTime);

        // 논리 상태와 Animator Parameter를 착석 전 Idle로 변경합니다.
        ChangeState(GuestState.ArrivalIdle);

        // 도착 즉시 서 있는 Idle의 처음부터 재생합니다.
        _animator.Play(ArrivalIdleAnimationState, 0, 0f);
    }

    /// <summary>
    /// 착석 전 또는 퇴장 전 Idle 시간을 계산하고 다음 행동으로 전환합니다.
    /// </summary>
    void UpdateIdleTime()
    {
        // 남은 대기시간을 음수가 되지 않도록 감소시킵니다.
        _remainingIdleTime = Mathf.Max(0f, _remainingIdleTime - Time.deltaTime);

        // 시간이 남아 있으면 Idle을 유지합니다.
        if (_remainingIdleTime > 0f) return;

        // 착석 전 대기가 끝났다면 착석을 준비합니다.
        if (_state == GuestState.ArrivalIdle)
        {
            // Ordering 자세에서 엉덩이를 맞추도록 요청합니다.
            BeginSeating();
            return;
        }

        // 퇴장 전 대기가 끝났다면 걷기 이동을 시작합니다.
        StartLeaving();
    }

    /// <summary>
    /// 물리 이동을 고정하고 의자에 설정된 방향으로 착석을 준비합니다.
    /// </summary>
    void BeginSeating()
    {
        // 예약한 빈 좌석만 실제 착석 상태로 전환합니다.
        if (Seat == null || !Seat.TrySeat(this))
        {
            Debug.LogError($"[착석 실패] {name}: 좌석 예약 상태를 확인하세요.", this);
            Destroy(gameObject);
            return;
        }
        // 착석 직전 이동과 속도를 정지합니다.
        StopMovement();

        // 앉아 있는 동안 중력과 충돌로 위치가 밀리지 않도록 고정합니다.
        _rigidbody.useGravity = false;
        _rigidbody.constraints = RigidbodyConstraints.FreezeAll;

        // 예약한 의자의 착석 위치와 월드 Y축 각도를 적용합니다.
        TeleportRoot(Seat.SeatPos.position, Quaternion.Euler(0f, Seat.SeatedYaw, 0f));

        // 주문 상태와 Animator Parameter를 함께 변경합니다.
        ChangeState(GuestState.Ordering);

        // 서 있는 자세와 섞이지 않도록 Ordering의 처음부터 재생합니다.
        _animator.Play(OrderingAnimationState, 0, 0f);

        // 애니메이션 평가 후 앉은 자세의 엉덩이 위치를 보정합니다.
        _pendingSeatAlignment = true;
    }

    /// <summary>
    /// Ordering 자세가 적용된 후 엉덩이를 SeatPos에 맞추고 주문시간을 시작합니다.
    /// </summary>
    void LateUpdate()
    {
        // 착석 정렬을 요청한 프레임에만 처리합니다.
        if (!_pendingSeatAlignment) return;

        // 주문 자세가 실제로 적용되기 전에는 엉덩이 위치를 보정하지 않습니다.
        if (_animator.GetCurrentAnimatorStateInfo(0).fullPathHash != OrderingAnimationState)
            return;

        // 앉은 자세의 엉덩이와 SeatPos 사이의 차이를 계산합니다.
        Vector3 offset = Seat.SeatPos.position - _hips.position;

        // 루트 전체를 보정해 엉덩이를 SeatPos에 맞춥니다.
        TeleportRoot(transform.position + offset, transform.rotation);

        // 착석한 좌석의 기준점으로 자신의 서빙 콜라이더를 배치합니다.
        if (Seat == null || !Seat.TryConfigureInteractionArea(_interactableCollider))
        {
            // 잘못된 감지 영역으로 영업을 진행하지 않도록 설정 누락을 알립니다.
            Debug.LogError($"[서빙 영역 설정 실패] {name}: 콜라이더와 좌석의 Serving Point를 확인하세요.", this);
            Destroy(gameObject);
            return;
        }

        // 정렬을 한 번만 실행하도록 요청을 해제합니다.
        _pendingSeatAlignment = false;

        // 착석 정렬이 끝난 시점부터 주문시간을 시작합니다.
        StartOrder();
    }

    /// <summary>
    /// 주문할 메뉴를 선택하고 Ordering 상태의 유지시간을 설정합니다.
    /// </summary>
    void StartOrder()
    {
        // 초기화가 끝난 주문 상태의 손님만 처리합니다.
        if (!IsOrdering) return;

        // 입장 시 매니저가 확보한 이 손님의 메뉴를 조회합니다.
        if (!_sushiManager.TryGetGuestMenu(this, out _selectedSushi))
        {
            // 확보한 판매 수량이 없는 손님은 중복 주문하지 않고 퇴장합니다.
            BeginLeaving();
            return;
        }

        // 설정값을 손님별 남은 주문시간에 복사합니다.
        _remainingOrderingTime = Mathf.Max(0f, _orderingTime);

        // 주문하는 동안에는 음식 대기시간을 계산하지 않습니다.
        _remainingWaitingTime = 0f;

        // 주문 동작의 시작과 유지시간을 확인합니다.
        Debug.Log($"[주문 중] {name}: {_remainingOrderingTime}초", this);
    }

    /// <summary>
    /// 주문을 확정하고 음식 대기 상태로 전환한 뒤 기존 경로로 주문을 전달합니다.
    /// </summary>
    void CompleteOrder()
    {
        // 주문 중인 손님만 완료할 수 있습니다.
        if (!IsOrdering) return;

        // 완료된 주문시간을 정리합니다.
        _remainingOrderingTime = 0f;

        // 주문 완료 시점부터 음식 대기시간과 만족도를 설정합니다.
        BeginWaiting();

        // 주문 확정과 음식 대기시간을 확인합니다.
        Debug.Log($"[주문 확정] {name}: {_selectedSushi.Name} (ID {_selectedSushi.Id}), 대기 {_remainingWaitingTime}초, 만족도 {_satisfaction}", this);

        // 전달 경로가 연결되지 않은 경우 원인을 알립니다.
        if (OrderStarted == null)
            Debug.LogWarning($"[주문 전달 실패] {name}: OrderStarted 구독자가 없습니다.", this);

        // 스포너가 매니저에 전달할 주문 시작 이벤트를 발생시킵니다.
        OrderStarted?.Invoke(this);
    }

    /// <summary>
    /// 메뉴의 대기시간을 저장하고 손님의 만족도를 초기화합니다.
    /// </summary>
    void BeginWaiting()
    {
        // 메뉴의 대기시간을 현재 주문의 최대 대기시간으로 저장합니다.
        _maxWaitingTime = Mathf.Max(0f, _selectedSushi.WaitingTime);

        // 최대 대기시간부터 음식 대기를 시작합니다.
        _remainingWaitingTime = _maxWaitingTime;

        // 0초인 메뉴는 나눗셈 없이 만족도 0으로 시작합니다.
        _satisfaction = _maxWaitingTime > 0f ? MaxSatisfaction : 0f;

        // 주문 이벤트 수신자가 대기 상태와 만족도를 확인할 수 있도록 전환합니다.
        ChangeState(GuestState.Waiting);
    }

    /// <summary>
    /// 음식 대기시간과 만족도를 갱신하고,
    /// 시간이 끝나면 분노 연출을 시작합니다.
    /// </summary>
    void UpdateWaiting()
    {
        if (WaitForTutorialServing) return;
        // 유효한 예약 동안 준비 연출과 결과 표시까지 대기시간, 만족도를 유지합니다.
        if (_minigameProvider != null)
        {
            if (IsDrinkMinigameValid(_minigameProvider)) return;
            // 주문이나 제공자가 바뀐 예약은 폐기하고 일반 대기를 재개합니다.
            ReleaseDrinkMinigame(_minigameProvider);
        }
        // 남은 대기시간을 감소시키되 음수가 되지 않게 합니다.
        _remainingWaitingTime = Mathf.Max(
            0f, _remainingWaitingTime - Time.deltaTime);

        // 남은 시간의 비율로 만족도를 계산합니다.
        _satisfaction = _maxWaitingTime > 0f
            ? Mathf.Clamp01(_remainingWaitingTime / _maxWaitingTime)
              * MaxSatisfaction
            : 0f;

        // 대기시간이나 만족도가 남아 있다면 계속 기다립니다.
        if (_remainingWaitingTime > 0f || _satisfaction > 0f)
        {
            return;
        }

        // 음식 대기 실패가 확정된 게스트의 분노 연출을 시작합니다.
        BeginAngryLeaving();
    }

    /// <summary>
    /// 착석 전후 Idle, 주문, 음식 대기 및 식사시간을 계산합니다.
    /// </summary>
    void Update()
    {
        // 초기화가 끝난 손님만 상태를 처리합니다.
        if (!_initialized) return;

        // 앉은 자세의 엉덩이 정렬 전에는 주문시간을 계산하지 않습니다.
        if (_pendingSeatAlignment) return;

        // 분노 연출 중에는 다른 상태의 타이머를 진행하지 않습니다.
        if (_state == GuestState.Angry)
        {
            // 비활성화 등으로 연출이 취소됐어도 게스트가 계속 남지 않게 합니다.
            if (_angryReaction == null || !_angryReaction.IsPlaying)
            {
                FinishAngryLeaving();
            }

            // 분노 상태에서는 아래의 일반 상태 처리를 실행하지 않습니다.
            return;
        }

        // 해당 구역의 왕복 대기 중에는 주문 및 식사 타이머를 계산하지 않습니다.
        if (_state == GuestState.SearchingSeat)
        {
            UpdateSeatSearch();
            return;
        }

        // 착석 전과 퇴장 전에는 Idle 타이머만 계산합니다.
        if (_state == GuestState.ArrivalIdle || _state == GuestState.LeavingIdle)
        {
            // 대기시간이 끝나면 착석 또는 퇴장 이동으로 전환합니다.
            UpdateIdleTime();

            // 전환한 프레임에는 다음 상태의 시간을 추가로 차감하지 않습니다.
            return;
        }

        // 주문 중이라면 주문 동작의 남은 시간을 계산합니다.
        if (IsOrdering)
        {
            // 남은 주문시간이 음수가 되지 않도록 감소시킵니다.
            _remainingOrderingTime = Mathf.Max(0f, _remainingOrderingTime - Time.deltaTime);

            // 주문시간이 끝나면 Waiting 전환과 주문 전달을 처리합니다.
            if (_remainingOrderingTime <= 0f) CompleteOrder();

            // 전환된 프레임에는 음식 대기시간을 추가로 차감하지 않습니다.
            return;
        }

        // 음식 대기 중이라면 남은 대기시간과 만족도를 계산합니다.
        if (IsWaiting)
        {
            // 시간과 만족도가 모두 소진되면 기존 퇴장을 시작합니다.
            UpdateWaiting();

            // 같은 프레임에 식사시간을 추가로 처리하지 않습니다.
            return;
        }

        // 식사 중이 아니라면 식사시간을 계산하지 않습니다.
        if (!IsEating) return;

        // 남은 식사시간이 음수가 되지 않도록 감소시킵니다.
        _remainingEatingTime = Mathf.Max(0f, _remainingEatingTime - Time.deltaTime);

        // 식사시간이 남아 있다면 식사를 유지합니다.
        if (_remainingEatingTime > 0f) return;

        // 식사를 마치고 퇴장을 시작합니다.
        FinishEating();
    }

    /// <summary>
    /// 정상 서빙 시 보상을 기록하고 해당 스시의 식사시간으로 식사를 시작합니다.
    /// </summary>
    public bool TryServe(Sushi sushi)
    {
        // 직접 서빙 테스트도 동일한 메뉴 및 보상 검사를 사용합니다.
        return TryServe(sushi, null);
    }

    /// <summary>
    /// 운반 요리를 제공하고 다른 손님의 같은 메뉴였다면 남은 주문을 인계합니다.
    /// </summary>
    public bool TryServe(Sushi sushi, GuestNPC sourceGuest)
    {
        // 음료는 길게 누르기 경로로만 완료하며 음식만 즉시 운반 서빙합니다.
        if (sushi == null || sushi.Type != MenuType.Food) return false;
        return CompleteServing(sushi, sourceGuest);
    }

    /// <summary>메뉴 제공 성공 시 공통 보상, 식사 전환 및 주문 정리를 실행합니다.</summary>
    bool CompleteServing(Sushi sushi, GuestNPC sourceGuest)
    {
        // 음식을 기다리는 손님과 유효한 스시 데이터인지 확인합니다.
        if (!IsWaiting || sushi == null || _selectedSushi == null) return false;

        // 주문한 스시와 전달받은 스시의 ID를 비교합니다.
        if (sushi.Id != _selectedSushi.Id) return false;

        // 보상을 지급할 매니저가 없으면 서빙을 중단합니다.
        if (_sushiManager == null) return false;

        // 실제 보상 지급에 성공한 경우에만 확정 판매금액을 받습니다.
        if (!_sushiManager.RecordServingReward(this, _selectedSushi, out float paidGold))
        {
            // 지급 실패 시 기존 대기 상태를 유지합니다.
            return false;
        }

        // 식사 상태로 전환하기 전에 View가 읽을 확정 판매금액을 저장합니다.
        ServedSaleGold = paidGold;

        // 음식과 음료 모두 제공 성공 후에만 진행 중인 음료 작업을 비웁니다.
        _drinkSession.Reset();
        // 정상 제공되었으므로 음식 대기시간을 정리합니다.
        _remainingWaitingTime = 0f;

        // 원본 식사시간을 손님별 남은 시간에 복사합니다.
        _remainingEatingTime = Mathf.Max(0f, _selectedSushi.EatingTime);

        // 대기 상태에서 식사 상태로 전환합니다.
        ChangeState(GuestState.Eating);

        // 보상 지급과 서빙 성공 후에만 좌석에 음식 또는 가득 찬 음료를 표시합니다.
        if (Seat != null) Seat.ShowServedMeal(this, _selectedSushi);

        // 같은 메뉴를 주문한 손님 사이에서도 남은 조리 수량을 보존합니다.
        _sushiManager.CompleteServing(this, sourceGuest, _selectedSushi);

        // 서빙 성공과 적용된 식사시간을 확인하도록 알립니다.
        Debug.Log($"[식사 시작] {name}: {sushi.Name}, 식사 {_remainingEatingTime}초", this);
        return true;
    }

    /// <summary>
    /// 식사를 완료하고 의자 앞에서 퇴장 전 Idle을 시작합니다.
    /// </summary>
    public bool FinishEating()
    {
        // 식사 중인 손님만 식사를 완료할 수 있습니다.
        if (!IsEating) return false;
        // 음식과 음료를 다 소비한 좌석에는 청소가 필요한 쓰레기를 남깁니다.
        if (Seat != null) Seat.MarkUncleaned(this);

        // 외부에서 먼저 완료한 경우에도 남은 식사시간을 정리합니다.
        _remainingEatingTime = 0f;

        // 식사 완료를 런타임에서 확인하도록 알립니다.
        Debug.Log($"[식사 완료] {name}: 퇴장합니다.", this);

        // 의자 앞에 순간이동하고 Idle이 끝난 뒤 퇴장 이동을 시작합니다.
        // 식사 완료 사실만 기록하고 만족 퇴장 수는 출구에서 증가시킵니다.
        if (_sushiManager != null) _sushiManager.NotifyGuestFinishedEating(this);
        BeginLeaving();
        return true;
    }

    /// <summary>
    /// 플레이어 운반을 생략하고 이 손님의 완성된 요리로 서빙을 테스트합니다.
    /// </summary>
    [ContextMenu("테스트/서빙 완료")]
    void TestCompleteServing()
    {
        // 실행 중이며 음식을 기다리는 손님만 테스트합니다.
        if (!Application.isPlaying || !IsWaiting) return;

        // 완성된 주문을 확인할 매니저가 있는지 검사합니다.
        if (_sushiManager == null) return;

        // 해당 손님의 완성된 주문이 있을 때만 서빙을 실행합니다.
        if (!_sushiManager.TryServeCompletedOrder(this))
        {
            // 조리 미완료 등으로 서빙할 수 없는 상태를 알립니다.
            Debug.LogWarning("이 손님에게 서빙할 완성된 요리가 없습니다.", this);
        }
    }

    /// <summary>
    /// 음식 대기, 식사 또는 분노 연출을 마친 게스트의 퇴장을 준비합니다.
    /// </summary>
    void BeginLeaving()
    {
        // 분노 연출이 끝난 경우에도 기존 퇴장 처리를 사용할 수 있게 합니다.
        if (!IsWaiting &&
            !IsEating &&
            _state != GuestState.Angry)
        {
            return;
        }

        // 진행 중이던 음료 제공 작업을 취소합니다.
        _drinkSession.Reset();

        // 이전 이동 입력과 Rigidbody 속도를 정지합니다.
        StopMovement();

        // 주문과 식사 관련 타이머를 정리합니다.
        _remainingOrderingTime = 0f;
        _remainingWaitingTime = 0f;
        _remainingEatingTime = 0f;

        // 일반적인 퇴장에 사용할 Idle 시간을 설정합니다.
        _remainingIdleTime = Mathf.Max(0f, _leavingIdleTime);

        // 서빙 대상에서 제외하고 퇴장 준비 상태로 전환합니다.
        ChangeState(GuestState.LeavingIdle);

        // 남아 있는 주문을 취소합니다.
        if (_sushiManager != null)
        {
            _sushiManager.CancelOrder(this);
        }

        // 좌석 정보가 사라졌다면 기존 파괴 시 정리 경로로 넘깁니다.
        if (Seat == null || Seat.ArrivePos == null)
        {
            Destroy(gameObject);
            return;
        }

        // 의자 앞에서 기존 퇴장 방향을 바라보게 합니다.
        TeleportRoot(
            Seat.ArrivePos.position,
            Quaternion.Euler(0f, LeavingYaw, 0f));

        // 사용하던 좌석의 예약을 반환합니다.
        if (_sushiManager != null)
        {
            _sushiManager.ReleaseSeat(this);
        }

        // 기존 Animator Controller의 서 있는 자세를 재생합니다.
        _animator.Play(LeavingIdleAnimationState, 0, 0f);

        // 출구가 없다면 이동할 수 없으므로 게스트를 정리합니다.
        if (_leftPoint == null)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 좌석을 반환하고 현재 통로를 따라 퇴장 위치로 이동합니다.
    /// </summary>
    void StartLeaving()
    {
        // 퇴장 전 대기가 끝난 손님만 이동을 시작합니다.
        if (_state != GuestState.LeavingIdle) return;

        if (_leftPoint == null)
        {
            Debug.LogWarning($"[손님 퇴장 실패] {name}: 퇴장 위치를 확인하세요.", this);
            Destroy(gameObject);
            return;
        }

        _destination = _leftPoint;

        // 퇴장 준비가 끝난 손님의 자리 예약을 해제합니다.
        if (_sushiManager != null) _sushiManager.ReleaseSeat(this);

        // 더 이상 좌석을 사용하지 않도록 참조를 정리합니다.
        Seat = null;

        // 착석 전에 사용하던 물리 설정으로 복원합니다.
        _rigidbody.constraints = _originalConstraints;
        _rigidbody.useGravity = _originalUseGravity;

        // 기존 FixedUpdate의 퇴장 이동을 시작합니다.
        ChangeState(GuestState.Leaving);
    }

    /// <summary>
    /// 비활성화되면 수평 이동 입력을 정지합니다.
    /// </summary>
    void OnDisable()
    {
        // 비활성화된 손님은 이전 미니게임 예약을 유지하지 않습니다.
        _minigameProvider = null;
        _minigameOrder = null;
        // 키 해제를 받지 못한 음료 작업의 소유권을 정리합니다.
        _drinkSession.Reset();
        // Y축 속도를 유지하면서 수평 이동 입력을 초기화합니다.
        if (_mover != null) _mover.MoveHorizontal(0f, 0f);

        // 손님 로직만 비활성화되어도 재생 중인 효과를 즉시 정리합니다.
        if (_servingEffects != null)
        {
            _servingEffects.ResetEffects();
        }
    }



    /// <summary>
    /// 손님이 제거되면 남아 있는 주문과 자리 예약을 정리합니다.
    /// </summary>
    void OnDestroy()
    {
        // 관리자가 먼저 제거되었다면 처리하지 않습니다.
        if (_sushiManager == null) return;
        // 정상 출구 도착으로 이미 정리된 손님은 중복 집계하지 않습니다.
        _sushiManager.NotifyGuestRemoved(this);
        // 식사 중 외부에서 제거되어도 소비 중이던 자리의 쓰레기를 보존합니다.
        if (IsEating && Seat != null) Seat.MarkUncleaned(this);
        _sushiManager.RemoveSeatWaiter(this);

        // 외부에서 손님을 제거한 경우에도 조리를 취소합니다.
        _sushiManager.CancelOrder(this);

        // 이미 반환했더라도 안전하게 자리 예약을 정리합니다.
        _sushiManager.ReleaseSeat(this);
    }

    /// <summary>
    /// 음식 또는 음료를 받을 수 있는 손님 중 가장 가까운 스시 작업 대상만 선택합니다.
    /// </summary>
    public override bool CanInteract(Hero interactor)
    {
        // NPC와 히어로의 기본 활성 상태를 검사합니다.
        if (!base.CanInteract(interactor)) return false;

        // 지상 히어로도 직원과 동일한 기본 메뉴 및 대기 상태 검사를 사용합니다.
        if (!(interactor is GroundHero groundHero)) return false;
        if (!CanReceiveFrom(groundHero.SushiCarrier)) return false;

        // 스시 전용 선택기가 있는 플레이어는 가까운 대상만 공용 센서의 후보로 노출합니다.
        SushiInteractionSelector selector = groundHero.GetComponent<SushiInteractionSelector>();
        // 선택기를 사용하지 않는 기존 씬은 원래 상호작용 조건을 유지합니다.
        return selector == null || selector.IsNearestTarget(this);
    }

    /// <summary>
    /// 플레이어 또는 직원의 운반 요리가 이 손님의 주문과 일치하는지 확인합니다.
    /// </summary>
    public bool CanReceiveFrom(SushiCarrier carrier)
    {
        // 음식 대기 중인 활성 손님만 서빙받을 수 있습니다.
        if (!isActiveAndEnabled || !IsWaiting || _selectedSushi == null) return false;

        // 운반 컴포넌트가 연결되어 있고 활성화되어 있어야 합니다.
        if (carrier == null || !carrier.isActiveAndEnabled) return false;
        // 음료는 빈손으로 연속 상호작용할 수 있는지 검사합니다.
        if (_selectedSushi.Type == MenuType.Drink) return CanHoldInteract(carrier);

        // 현재 운반 중인 메뉴를 가져옵니다.
        Sushi carriedSushi = carrier.CurrentSushi;

        // 운반 중인 요리가 없으면 상호작용하지 않습니다.
        if (carriedSushi == null) return false;

        // 기존 TryServe와 동일하게 메뉴 ID를 비교합니다.
        return carriedSushi.Id == _selectedSushi.Id;
    }

    /// <summary>
    /// 히어로가 운반 중인 요리를 받아 기존 서빙 처리를 실행합니다.
    /// </summary>
    protected override void OnInteract(Hero interactor)
    {
        // 처리 직전에도 손님과 운반 요리의 상태를 다시 확인합니다.
        if (!CanInteract(interactor)) return;

        // CanInteract에서 확인한 지상 히어로를 가져옵니다.
        GroundHero groundHero = (GroundHero)interactor;

        // 서빙 성공 시에만 운반 중인 요리를 비우도록 요청합니다.
        TryInteract(groundHero.SushiCarrier);
    }

    /// <summary>
    /// 운반자의 요리를 받아 기존 주문 인계, 보상 지급 및 식사 처리를 실행합니다.
    /// </summary>
    public bool TryInteract(SushiCarrier carrier)
    {
        // 전달 직전에도 대기 상태와 메뉴 ID를 재검사합니다.
        if (!CanReceiveFrom(carrier)) return false;
        // 한 번 누르기로 음료 제공 시간을 건너뛰지 않습니다.
        if (_selectedSushi.Type != MenuType.Food) return false;
        // 성공한 경우에만 운반 중인 요리를 비우는 기존 처리를 사용합니다.
        return carrier.TryServeTo(this);
    }

    /// <summary>빈자리가 생기면 먼저 기다린 손님에게 배정하고 대기시간 초과 시 퇴장합니다.</summary>
    void UpdateSeatSearch()
    {
        // 영업 종료 후에는 새 주문을 위한 착석을 시작하지 않습니다.
        if (!_sushiManager.IsOpen)
        {
            BeginLeavingWithoutSeat();
            return;
        }
        if (_sushiManager.TryReserveSeat(this, _seatArea, out Chair seat))
        {
            Seat = seat;
            _sushiManager.RemoveSeatWaiter(this);
            StopMovement();
            _destination = seat.ArrivePos;
            // 왕복 도중 어느 방향에서 빈자리를 찾더라도 목적지 쪽으로 걷습니다.
            TeleportRoot(transform.position, Quaternion.Euler(0f,
                seat.ArrivePos.position.x >= transform.position.x ? EnteringYaw : LeavingYaw, 0f));
            ChangeState(GuestState.Entering);
            return;
        }
        // 왕복을 시작한 시점부터 실제 경과 시간을 차감합니다.
        _remainingSeatWaitTime = Mathf.Max(0f, _remainingSeatWaitTime - Time.deltaTime);
        if (_remainingSeatWaitTime <= 0f) BeginLeavingWithoutSeat();
    }

    /// <summary>선택한 구역의 첫 의자와 안쪽 의자 사이를 수평으로 왕복합니다.</summary>
    void UpdateSeatSearchMovement()
    {
        // 의자 참조가 제거되면 잘못된 목적지에 계속 머무르지 않고 퇴장합니다.
        if (_searchFirstPoint == null || _searchInnerPoint == null || _destination == null)
        {
            BeginLeavingWithoutSeat();
            return;
        }
        // 의자가 하나뿐인 구역은 제자리 걷기 대신 Idle로 대기합니다.
        if (Mathf.Abs(_searchFirstPoint.position.x - _searchInnerPoint.position.x) <= _arrivalDistance)
        {
            StopMovement();
            _animator.SetInteger(GuestStateParameter, (int)GuestState.ArrivalIdle);
            return;
        }
        float offset = _destination.position.x - transform.position.x;
        if (Mathf.Abs(offset) <= _arrivalDistance)
        {
            _destination = _destination == _searchFirstPoint ? _searchInnerPoint : _searchFirstPoint;
            offset = _destination.position.x - transform.position.x;
        }
        // 이동 평면은 유지하고 목적지 방향으로만 몸을 돌립니다.
        Quaternion rotation = Quaternion.Euler(0f, offset >= 0f ? EnteringYaw : LeavingYaw, 0f);
        transform.rotation = rotation;
        _rigidbody.rotation = rotation;
        _mover.MoveHorizontal(Mathf.Sign(offset),
            Mathf.Min(_moveSpeed, Mathf.Abs(offset) / Time.fixedDeltaTime));
    }

    /// <summary>빈자리 대기가 끝나면 좌석 없이 퇴장 전 Idle과 기존 퇴장 이동을 시작합니다.</summary>
    void BeginLeavingWithoutSeat()
    {
        // 같은 손님을 여러 번 퇴장 처리하지 않습니다.
        if (!IsSeekingSeat) return;
        StopMovement();
        _sushiManager.RemoveSeatWaiter(this);
        _remainingSeatWaitTime = 0f;
        _remainingIdleTime = Mathf.Max(0f, _leavingIdleTime);
        ChangeState(GuestState.LeavingIdle);
        _animator.Play(LeavingIdleAnimationState, 0, 0f);
    }

    /// <summary>빈손인 제공자가 이 손님의 음료 작업을 맡을 수 있는지 검사합니다.</summary>
    public bool CanHoldInteract(SushiCarrier provider)
    {
        // 플레이어가 예약한 손님에게 직원이나 Hold가 중복 제공하지 못하게 합니다.
        if (_minigameProvider != null) return false;
        // 직원은 패시브 해금 이후에만 음료를 제공할 수 있습니다.
        if (!isActiveAndEnabled || !IsWaitingForDrink || provider == null ||
            !provider.isActiveAndEnabled || provider.CurrentSushi != null) return false;
        ServingNPC staff = provider.GetComponent<ServingNPC>();
        if (staff != null && !staff.CanServeDrink) return false;
        return _drinkSession.IsAvailableFor(provider);
    }

    /// <summary>다른 제공자와 충돌하지 않을 때 미니게임을 예약합니다.</summary>
    internal bool TryReserveDrinkMinigame(SushiCarrier provider)
    {
        // 기존 Hold 권한과 빈손, 주문 대기 조건을 먼저 검사합니다.
        if (_minigameProvider != null || !CanHoldInteract(provider)) return false;
        // 화면 준비 전부터 손님의 대기시간을 정지할 소유권을 저장합니다.
        _minigameProvider = provider;
        _minigameOrder = _selectedSushi;
        return true;
    }

    /// <summary>시작 당시의 제공자와 주문이 여전히 유효한지 검사합니다.</summary>
    internal bool IsDrinkMinigameValid(SushiCarrier provider)
    {
        // 손님과 제공자의 생명주기, 빈손 조건을 검사합니다.
        if (!isActiveAndEnabled || !IsWaitingForDrink || provider == null ||
            !provider.isActiveAndEnabled || provider.CurrentSushi != null) return false;
        // 변경된 주문이나 다른 제공자에게 결과를 적용하지 않습니다.
        return _minigameProvider == provider && _minigameOrder != null && _selectedSushi == _minigameOrder;
    }

    /// <summary>성공한 예약자의 음료를 기존 보상과 서빙 경로로 처리합니다.</summary>
    internal bool CompleteDrinkMinigame(SushiCarrier provider)
    {
        // 결과 시점에도 원래 손님과 주문인지 다시 검사합니다.
        if (!IsDrinkMinigameValid(provider)) return false;
        // 예약을 정리하고 실제 보상과 식사 전환은 기존 메서드에 맡깁니다.
        Sushi order = _minigameOrder;
        ReleaseDrinkMinigame(provider);
        return CompleteServing(order, null);
    }

    /// <summary>
    /// 유효한 음료 미니게임 예약에 실패 패널티를 적용합니다.
    /// 남은 대기시간과 만족도를 갱신하고, 예약에 의한 시간 정지는 유지합니다.
    /// </summary>
    internal void ApplyDrinkMinigameFailurePenalty(SushiCarrier provider, float penaltyTime)
    {
        // 시작 당시의 손님 주문과 제공자가 유효할 때만 적용합니다.
        if (!IsDrinkMinigameValid(provider)) return;

        // 0 이하 또는 유효하지 않은 수치는 패널티로 사용하지 않습니다.
        if (penaltyTime <= 0f || float.IsNaN(penaltyTime) || float.IsInfinity(penaltyTime)) return;

        // 남은 대기시간에서 패널티를 차감하되 0초 미만으로 내려가지 않습니다.
        _remainingWaitingTime = Mathf.Max(0f, _remainingWaitingTime - penaltyTime);

        // 기존 대기시간 계산과 동일한 비율로 만족도도 즉시 갱신합니다.
        _satisfaction = _maxWaitingTime > 0f
            ? Mathf.Clamp01(_remainingWaitingTime / _maxWaitingTime) * MaxSatisfaction
            : 0f;
    }

    /// <summary>해당 제공자의 예약만 해제하고 일반 대기를 재개합니다.</summary>
    internal void ReleaseDrinkMinigame(SushiCarrier provider)
    {
        // 다른 플레이어의 예약은 해제하지 않습니다.
        if (_minigameProvider != provider) return;
        // 타이머를 초기화하지 않고 다음 프레임부터 기존 남은 시간을 사용합니다.
        _minigameProvider = null;
        _minigameOrder = null;
    }

    /// <summary>손님의 실제 영역에서 메뉴별 시간을 채우면 음료 제공을 완료합니다.</summary>
    public bool TickHoldInteract(SushiCarrier provider, Collider sensor, float deltaTime)
    {
        // 접촉 이탈과 대상 상태 변경은 현재 제공자의 진행을 취소합니다.
        if (!CanHoldInteract(provider) || !SushiInteractor.IsTouching(sensor, InteractionArea))
        {
            CancelHoldInteract(provider);
            return false;
        }
        if (!_drinkSession.Tick(provider, deltaTime, _selectedSushi.DrinkInteractionTime)) return false;
        // 실제 보상 지급에 성공한 경우에만 Eating으로 전환합니다.
        return CompleteServing(_selectedSushi, null);
    }

    /// <summary>해당 제공자의 음료 작업을 취소하고 다른 제공자의 진행은 유지합니다.</summary>
    public void CancelHoldInteract(SushiCarrier provider)
    {
        // 미완료 시간은 다른 플레이어나 직원에게 인계하지 않습니다.
        _drinkSession.Cancel(provider);
    }

    /// <summary>
    /// 음식 대기 실패 시 운영 패널티를 적용하고,
    /// 주문을 취소한 뒤 자리 옆에서 분노 연출을 시작합니다.
    /// </summary>
    void BeginAngryLeaving()
    {
        // 음식 대기 중인 게스트만 대기 실패를 처리합니다.
        if (!IsWaiting) return;

        // 직접 연결하지 않았다면 같은 루트에서 연출 컴포넌트를 찾습니다.
        if (_angryReaction == null)
        {
            _angryReaction = GetComponent<GuestAngryReaction>();
        }

        // 진행 중이던 음료 제공 작업을 취소합니다.
        _drinkSession.Reset();

        // 더 이상 사용하지 않을 주문 관련 시간을 초기화합니다.
        _remainingWaitingTime = 0f;
        _remainingOrderingTime = 0f;
        _remainingEatingTime = 0f;
        _satisfaction = 0f;

        // 대기 실패가 확정된 시점에 매장 운영 상태를 갱신합니다.
        if (_sushiManager != null)
        {
            // 아직 Waiting 상태일 때 손님당 패널티를 한 번 적용합니다.
            _sushiManager.ApplyWaitingFailurePenalty(this);

            // 해당 손님의 남은 주문을 취소합니다.
            _sushiManager.CancelOrder(this);
        }

        // 남아 있는 물리 이동 입력을 제거합니다.
        StopMovement();

        // Waiting을 벗어나면서 서빙 영역도 비활성화합니다.
        ChangeState(GuestState.Angry);

        // 좌석이 사라졌다면 기존 OnDestroy 정리 경로를 사용합니다.
        if (Seat == null || Seat.ArrivePos == null)
        {
            Destroy(gameObject);
            return;
        }

        // 현재 방향을 유지하며 자리 옆으로 순간이동합니다.
        // 플레이어를 향한 부드러운 회전은 연출 컴포넌트가 처리합니다.
        TeleportRoot(Seat.ArrivePos.position, transform.rotation);

        // 상체 연출 아래에서 유지할 서 있는 Idle을 재생합니다.
        _animator.Play(LeavingIdleAnimationState, 0, 0f);

        // 회전과 던지기를 마치면 기존 퇴장 메서드를 호출합니다.
        bool started = _angryReaction != null &&
                       _angryReaction.TryPlay(FinishAngryLeaving);

        // 연출 연결이 누락되어도 게스트는 정상적으로 퇴장합니다.
        if (!started)
        {
            FinishAngryLeaving();
        }
    }

    /// <summary>
    /// 던지기 마무리 후 좌석을 반환하고 기존 퇴장 이동을 시작합니다.
    /// </summary>
    void FinishAngryLeaving()
    {
        // 이미 다른 상태로 넘어갔다면 중복 퇴장을 막습니다.
        if (_state != GuestState.Angry) return;

        // 기존 메서드가 주문과 좌석, 퇴장 방향을 정리하도록 합니다.
        BeginLeaving();

        // 분노와 던지기를 마쳤으므로 추가적인 퇴장 Idle은 생략합니다.
        if (_state == GuestState.LeavingIdle)
        {
            _remainingIdleTime = 0f;

            // 기존 경로 구성과 물리 설정 복원 후 퇴장 이동을 시작합니다.
            StartLeaving();
        }
    }
}
