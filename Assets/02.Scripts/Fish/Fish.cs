using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 물고기의 평상시 이동, 피격, Passive 및 Aggressive 전투와 사망 흐름을 지휘합니다.
/// </summary>
public partial class Fish : MonoBehaviour
{
    // 런타임 능력치와 상태를 관리합니다.
    [SerializeField] private FishModel _model;

    // 아이콘의 실제 표시와 연출을 담당하는 View입니다.
    [SerializeField] private FishView _fishView;

    // 회피형의 플레이어 감지 알림 반경을 설정합니다.
    [SerializeField, Min(0f)] private float _evasiveDetectionRadius = 10f;

    // 물리 이동과 외형 회전을 담당합니다.
    [SerializeField] private FishMover _mover;

    // 공격 범위와 대상 레이어를 가진 기존 센서입니다.
    [SerializeField] private DamageableSensor _damageableSensor;

    // 물고기의 외형 애니메이션을 재생합니다.
    [SerializeField] private Animator _animator;

    // 몸의 앞쪽과 뒤쪽을 나타내는 기준점입니다.
    [SerializeField] private Transform _head;
    [SerializeField] private Transform _tail;

    // 실행용 추격 객체입니다. 설정값은 FishModel에서 읽습니다.
    private readonly FishChaser _chaser = new FishChaser();

    // 사망 후에도 상호작용 검색에 사용할 몸체 콜라이더입니다.
    [SerializeField] private Collider _interactionCollider;

    // 프리팹 Inspector에서 이 물고기의 원본 데이터를 연결합니다.
    [SerializeField] private FishData _data;

    // 모델의 피격 설정을 받아 실제 화면 연출을 처리합니다.
    [SerializeField] private FishHitFlash _hitFlash;

    // 그룹별 밝기 표현을 담당하는 컴포넌트입니다.
    [SerializeField] private FishVisual _visual;

    // 포획 중 숨긴 콜라이더의 원래 활성 상태입니다.
    private Collider[] _captureColliders;
    private bool[] _colliderEnabledBeforeCapture;

    // 사망 후 부유 이동에 사용하는 기준과 시간입니다.
    private Vector3 _deadFloatOrigin;
    private float _deadFloatElapsed;

    
    private FishSpawner _spawner;

    // 생성 시 받은 높이 범위는 전투가 끝난 뒤 복귀 목표로 사용합니다.
    private FishVerticalRange _verticalRange;

    // 원래 높이 영역에 도착하기 전까지 무리의 이동 명령을 차단합니다.
    private bool _isReturningToVerticalRange;

    // 주입받은 Hero의 실제 피해 여부를 확인할 모델입니다.
    private HeroRunModel _heroModel;

    // 감지 범위와 대상 활성 상태를 확인할 Hero의 실제 Transform입니다.
    private Transform _combatTarget;

    // 접근과 머리 회전의 목표로 사용할 플레이어의 실제 몸체 히트박스입니다.
    private Collider _heroHitbox;

    // 직전 감지 결과를 기억하여 Hero의 레이더 진입 순간만 구분합니다.
    private bool _wasHeroInDetectionRange;

    // 플레이어 거리 검사 간격입니다. 0이면 매 물리 스텝 검사합니다.
    [SerializeField, Min(0f)] private float _playerDetectionInterval = 0.1f;

    // 다음 거리 검사를 실행할 물리 시간입니다.
    private double _nextPlayerDetectionTime = double.NegativeInfinity;

    // 첫 검사 이후 한 번만 분산 간격을 적용합니다.
    private bool _needsDetectionStagger = true;

    // 모든 물고기가 공유하며 검사 슬롯을 순환 배정하는 순번입니다.
    private static uint s_detectionScheduleSequence;

    // 마지막 생존 피격 후 전투 상태를 유지할 남은 시간입니다.
    private float _combatRemaining;

    // 전투 중 외형이 바라볼 방향입니다.
    private Vector2 _combatDirection;

    // 물고기 중심에서 플레이어 히트박스 표면까지의 감속 시작 거리입니다. 정지 거리가 아닙니다.
    [FormerlySerializedAs("_combatStopDistance")]
    [Tooltip("이 거리 안에서 가까워질수록 접근 속도가 줄어듭니다. 공격 전에는 정지하지 않습니다.")]
    [SerializeField, Min(0f)] private float _combatSlowDistance = 0.5f;

    // 공격 쿨타임 중 감속 기준 거리 또는 머리 길이보다 추가로 벌릴 간격입니다.
    [SerializeField, Min(0f)] private float _combatRetreatDistance = 0.75f;

    // 피격 시 확정한 넉백 속도와 남은 시간입니다.
    private Vector2 _knockbackVelocity;
    private float _knockbackRemaining;
    private bool _isKnockedBack;

    // 사망 애니메이션을 재생한 상태인지 나타냅니다.
    private bool _deathStarted;

    // 데이터 연결과 별개로 런타임 초기화 완료 여부를 관리합니다.
    private bool _initialized;

    // 활성화된 Animator의 사망 파라미터 검증 결과를 이번 대여 동안 보관합니다.
    private bool _deathAnimationValidated;

    // 같은 씬의 같은 프리팹·컨트롤러·실패 사유는 한 번만 출력합니다.
    private static readonly HashSet<string> AnimationFailures = new HashSet<string>();

    // 스포너가 활성화 후 검증까지 성공한 개체만 패턴에 포함하도록 합니다.
    internal bool IsSpawnReady => _initialized && _deathAnimationValidated && isActiveAndEnabled;

    // Animator의 사망 상태 조건에 사용할 파라미터입니다.
    private static readonly int IsDeadHash = Animator.StringToHash("IsDead");

    /// <summary>
    /// 외형 컴포넌트에 소속 그룹의 밝기 적용을 요청합니다.
    /// </summary>
    public void SetBrightness(float brightness)
    {
        // 외형 컴포넌트 연결 누락을 알리고 적용을 중단합니다.
        if (_visual == null)
        {
            Debug.LogError("Fish의 Visual을 연결해 주세요.", this);
            return;
        }

        // 실제 렌더러 제어는 외형 컴포넌트에 위임합니다.
        _visual.SetBrightness(brightness);
    }

    /// <summary>
    /// 작살 적중을 모델에 전달하고 작살 담당자에게 처리 결과를 반환합니다.
    /// 피해 적용을 포함하므로 별도의 TakeDamage 호출과 함께 사용하지 않습니다.
    /// </summary>
    public FishHarpoonResult ReceiveHarpoonHit(float damage)
    {
        // 초기화와 활성 상태, 머리와 꼬리 기준점 연결을 확인합니다.
        if (!_initialized
            || !isActiveAndEnabled
            || !_mover.CanAlignBody(_head, _tail))
        {
            return FishHarpoonResult.Unavailable;
        }

        // 체력 판단, 상태 전환과 피해 적용은 모델에서 처리합니다.
        return _model.ReceiveHarpoonHit(damage);
    }

    /// <summary>
    /// 작살 행동 중 꼬리를 접점에 붙이고 머리 방향을 정렬합니다.
    /// 작살 담당자가 LateUpdate에서 현재 접점과 월드 방향을 전달합니다.
    /// </summary>
    public void AlignToHarpoon(Vector3 point, Vector3 tailToHeadDirection)
    {
        // 체력이 높아도 작살 행동 상태이면 최초 연결 정렬을 허용합니다.
        if (!_initialized
            || !isActiveAndEnabled
            || _model.IsCollected
            || State != FishState.HarpoonAction)
        {
            return;
        }

        // 실제 회전과 위치 보정은 이동 담당자에게 맡깁니다.
        _mover.AlignToHarpoon(_head, _tail, point, tailToHeadDirection);
    }

    /// <summary>
    /// 작살 연결을 해제하고 새 적중 경로의 살아 있는 물고기를 전투로 복귀시킵니다.
    /// </summary>
    public void EndHarpoonAction()
    {
        // 모델에서 현재 상태를 검사하고 연결 해제와 상태 복귀를 처리합니다.
        _model.CancelCapture();
    }
    // 직접 배치 개체의 중복 소유와 퀘스트 등록 대상을 확인합니다.
    internal FishSpawner OwnerSpawner => _spawner;

    public FishData Data => _data;
    public FishType Type => Data.Type;
    public FishState State => _model.State;
    public Vector2 Position => _mover.Position;

    // Passive와 Aggressive는 같은 추격, 공격과 전투 회전을 사용합니다.
    private bool CanFight =>
        Type == FishType.Passive || Type == FishType.Aggressive;

    // 초기화가 완료된 평상시 이동 개체만 패턴을 따릅니다.
    // 패턴은 목표 명령만 전달하고 Fish가 현재 상태에 맞춰 물리 적용을 허용합니다.
    private Vector2 _groupMoveDirection;
    private float _groupMoveSpeed;
    private bool _hasGroupMoveCommand;
    private bool _wasFollowingGroup;

    // 생성된 자리와 소유 패턴을 기억하여 장애물을 발견했을 때만 보고합니다.
    private FishSpawnPattern _groupPattern;
    private int _groupSlotIndex = -1;

    public bool CanFollowGroup =>
        _initialized
        && isActiveAndEnabled
        && !_model.IsCollected
        && State == FishState.Moving
        && !_isKnockedBack
        && !_isReturningToVerticalRange;

    /// <summary>
    /// 모델의 상태, 피해, 포획 이동과 획득 알림을 구독합니다.
    /// </summary>
    private void OnEnable()
    {
        // 연결된 모델이 있을 때만 알림을 받습니다.
        if (_model == null)
        {
            return;
        }

        // 모델은 규칙을 판단하고 Fish는 실제 동작을 수행합니다.
        _model.StateChanged += OnStateChanged;
        _model.Damaged += OnDamaged;
        _model.CapturedMoveRequested += OnCapturedMoveRequested;
        _model.Collected += OnCollected;
        // 반격 처리와 별도로 피격 연출 알림을 받습니다.
        _model.DamageReceived += OnDamageReceived;

        // 재대여 시 사망 상태 머신과 뼈 자세를 생존 초기 상태로 되돌립니다.
        if (_resetAnimationOnEnable && _initialized)
        {
            _resetAnimationOnEnable = false;
            _animator.Rebind();

            // 첫 활성화로 준비된 파라미터를 확인하고 실패한 개체의 후속 처리를 중단합니다.
            if (!ValidateDeathAnimation())
            {
                _initialized = false;
                return;
            }

            // 검증된 Bool 파라미터만 초기화하여 이전 생애의 사망 상태를 해제합니다.
            _animator.SetBool(IsDeadHash, false);
            _animator.Update(0f);
        }

        // 런타임 초기화가 끝난 개체만 재활성화 시 상태를 복원합니다.
        if (_initialized && !_model.IsCollected)
        {
            OnStateChanged(_model.State);
        }

        // 재활성화 시 이전 표시를 초기화하고 현재 상태를 반영합니다.
        InitializeFishView();
    }

    /// <summary>
    /// 모델 알림 구독을 해제하고 이동과 공격을 중단합니다.
    /// </summary>
    private void OnDisable()
    {
        // 풀 반환 또는 비활성화 이후 이전 이동 명령을 실행하지 않습니다.
        ClearGroupMoveCommand();
        _wasFollowingGroup = false;

        // 중복 구독과 비활성 개체의 이벤트 실행을 방지합니다.
        if (_model != null)
        {
            _model.StateChanged -= OnStateChanged;
            _model.Damaged -= OnDamaged;
            _model.CapturedMoveRequested -= OnCapturedMoveRequested;
            _model.Collected -= OnCollected;
            // 비활성화 중 연출 요청을 받지 않도록 구독을 해제합니다.
            _model.DamageReceived -= OnDamageReceived;
        }

        // 비활성화 이전의 이동과 공격을 종료합니다.
        Stop();

        if (_damageableSensor != null)
        {
            _damageableSensor.enabled = false;
        }
        // Fish 컴포넌트만 꺼져도 몸체가 피격 색으로 남지 않게 합니다.
        if (_hitFlash != null)
        {
            _hitFlash.StopFlash();
        }

        // 재활성화 시 첫 접근을 새 감지로 처리하도록 기록을 초기화합니다.
        ResetPlayerDetection();

        // Fish 컴포넌트만 꺼지는 경우에도 UI와 발견 연출을 숨깁니다.
        if (_fishView != null)
        {
            _fishView.SetVisible(false);
        }
    }

    /// <summary>
    /// 프리팹의 데이터를 사용하고 초기 방향, Hero와 소유 스포너를 주입받습니다.
    /// </summary>
    public bool Initialize(
        Vector2 initialDirection,
        Hero hero,
        FishSpawner spawner)
    {
        // 중복 초기화와 비활성 컴포넌트 설정은 거절하되 풀의 비활성 루트 초기화는 허용합니다.
        if (_initialized
            || _data == null
            || spawner == null
            || _model == null
            || _mover == null
            || !_model.enabled
            || !_mover.enabled
            || !enabled
            || !_mover.IsReady)
        {
            Debug.LogError(
                "Fish의 Data, Model, Mover와 활성 상태를 확인해주세요.",
                this);

            return false;
        }

        // 작살 회수 거리와 실제 이동 위치가 같은 루트를 기준으로 해야 합니다.
        if (_model.transform != transform || _mover.transform != transform)
        {
            Debug.LogError(
                "Fish, FishModel, FishMover를 같은 루트에 배치해주세요.",
                this);

            return false;
        }

        // 사망 이후에도 상호작용으로 검색할 몸체 콜라이더가 필요합니다.
        if (_interactionCollider == null
            || !(_interactionCollider is BoxCollider
                || _interactionCollider is CapsuleCollider)
            || !IsSpawnHierarchyEnabled(_interactionCollider.transform)
            || !_mover.OwnsCollider(_interactionCollider))
        {
            Debug.LogError(
                "Interaction Collider에 같은 Rigidbody 소속의 Box 또는 Capsule을 연결해주세요.",
                this);

            return false;
        }

        // 비활성 대여 중에는 런타임 파라미터를 조회하지 않고 참조 설정만 검사합니다.
        _deathAnimationValidated = false;
        if (!ValidateAnimationReferences())
        {
            return false;
        }

        // 튜토리얼처럼 이미 활성화된 개체는 이 자리에서 런타임 검증을 완료합니다.
        if (_animator.isActiveAndEnabled)
        {
            _animator.Rebind();
            if (!ValidateDeathAnimation())
            {
                return false;
            }
        }

        // Passive와 Aggressive 전투에 필요한 참조를 확인합니다.
        if (CanFight)
        {
            if (hero == null
                || _damageableSensor == null
                || !_mover.CanAlignBody(_head, _tail))
            {
                Debug.LogError(
                    "Passive/Aggressive의 Hero, 센서와 Visual 아래 Head/Tail을 확인해주세요.",
                    this);

                return false;
            }

            // 실제 공격 성공 여부를 확인할 Hero 모델을 보관합니다.
            _heroModel = hero.GetComponentInChildren<HeroRunModel>(true);
            if (_heroModel == null)
            {
                Debug.LogError("Hero에 HeroRunModel이 필요합니다.", this);
                return false;
            }

            // 상호작용용 트리거를 제외하고 실제 플레이어 몸체 히트박스를 보관합니다.
            _heroHitbox = FindHeroHitbox(hero);

            // 잘못된 루트 좌표로 추격하지 않도록 히트박스가 없으면 초기화를 중단합니다.
            if (_heroHitbox == null)
            {
                // 실제 몸체 콜라이더 연결 문제를 확인할 수 있도록 알립니다.
                Debug.LogError("플레이어의 실제 몸체 히트박스가 필요합니다.", this);
                return false;
            }

            // 감지한 Hero를 바로 거리 초과로 잃지 않도록 감지 반경도 전달합니다.
            _chaser.Initialize(
                hero,
                _model,
                spawner.MovementMin,
                spawner.MovementMax,
                _data.HeroDetectionRadius);


            // 실제 전투 진입 전에는 공격 센서를 사용하지 않습니다.
            _damageableSensor.enabled = false;
        }

        // 회피형도 근접 감지를 할 수 있도록 모든 타입에 플레이어를 연결합니다.
        _combatTarget = hero != null ? hero.transform : null;

        // 플레이어 모델이 있으면 감지 시 활성 상태와 생존 여부를 확인합니다.
        _heroModel = hero != null
            ? hero.GetComponentInChildren<HeroRunModel>(true)
            : null;

        // 실행 상태를 초기화합니다.
        _spawner = spawner;
        _deathStarted = false;
        _isKnockedBack = false;
        _knockbackRemaining = 0f;
        _deadFloatElapsed = 0f;
        _combatDirection = initialDirection;
        _combatRemaining = 0f;

        // 새 개체는 이전 감지 기록 없이 Hero의 첫 접근을 감지합니다.
        ResetPlayerDetection();

        // 일반 이동, 넉백과 전투가 공유할 전역 이동 범위를 주입합니다.
        _mover.SetMovementBounds(
            spawner.MovementMin, spawner.MovementMax, spawner.BoundaryTurnDistance);

        // 새 개체는 일반 높이 제한을 적용하고 복귀 상태 없이 시작합니다.
        _isReturningToVerticalRange = false;
        _mover.SetVerticalRangeEnabled(true);

        // 프리팹에서 연결한 원본 데이터를 런타임 모델에 전달합니다.
        _model.Initialize(_data);

        // 최초 물리 이동과 외형 방향을 설정합니다.
        _mover.Stop();
        _mover.SetFacing(initialDirection);

        // 애니메이션은 루트 이동을 변경하지 않고 생존 상태로 시작합니다.
        _animator.applyRootMotion = false;
        _animator.speed = 1f;
        // 비활성 풀 대여 중에는 OnEnable에서 파라미터를 초기화합니다.
        if (_deathAnimationValidated) _animator.SetBool(IsDeadHash, false);

        // 비활성 초기화는 풀 사용 여부와 관계없이 다음 OnEnable에서 검증을 마칩니다.
        _resetAnimationOnEnable = !_deathAnimationValidated;

        // 모든 준비가 끝난 뒤에만 행동 갱신을 허용합니다.
        _initialized = true;

        // 활성 상태면 즉시 표시하고 비활성 풀 초기화라면 OnEnable에서 표시합니다.
        InitializeFishView();

        // 완전히 초기화된 개체를 스포너의 이번 생애 퀘스트 관리에 등록합니다.
        _spawner.RegisterFishQuest(this, _model);
        return true;
    }

    /// <summary>
    /// 활성화 전에도 검사할 수 있는 Animator 참조와 프리팹 설정을 검증합니다.
    /// </summary>
    private bool ValidateAnimationReferences()
    {
        // 단락 평가로 뭉쳐 있던 조건을 분리하여 실제 실패 사유를 기록합니다.
        string reason;
        if (_animator == null) reason = "Animator 참조 없음";
        else if (!_animator.enabled) reason = "Animator 컴포넌트 비활성";
        else if (!IsSpawnHierarchyEnabled(_animator.transform)) reason = "Animator 자식 계층 비활성 또는 소속 불일치";
        else if (_animator.runtimeAnimatorController == null) reason = "Animator Controller 없음";
        else return true;

        // 참조가 없을 때도 안전하게 진단하고 초기화를 중단합니다.
        LogAnimationFailure(reason, "참조 검사");
        return false;
    }

    /// <summary>
    /// 활성화와 Rebind 이후 사망 파라미터를 검사하고 스포너가 읽을 결과를 저장합니다.
    /// </summary>
    private bool ValidateDeathAnimation()
    {
        // 이전 검증 결과를 재사용하지 않고 현재 Animator 상태를 검사합니다.
        _deathAnimationValidated = false;
        if (!ValidateAnimationReferences()) return false;

        // 비활성 상태를 성공으로 처리하여 잘못된 개체가 스폰 집계에 포함되지 않게 합니다.
        if (!_animator.isActiveAndEnabled)
        {
            LogAnimationFailure("Animator 활성화 실패",
                $"activeInHierarchy={_animator.gameObject.activeInHierarchy}, "
                + $"isInitialized={_animator.isInitialized}");
            return false;
        }

        // 실제 Bool 파라미터 존재 여부를 검사하여 활성화 후 결과로 보관합니다.
        _deathAnimationValidated = HasDeathParameter();
        return _deathAnimationValidated;
    }

    /// <summary>
    /// 같은 씬의 동일 프리팹·컨트롤러·실패 사유에 대해 진단을 한 번만 출력합니다.
    /// </summary>
    private void LogAnimationFailure(string reason, string details)
    {
        // 비활성 대여와 참조 누락 상황에서도 이름을 안전하게 구성합니다.
        string prefabName = gameObject.name.Replace("(Clone)", "");
        string controllerName = _animator != null && _animator.runtimeAnimatorController != null
            ? _animator.runtimeAnimatorController.name : "(없음)";
        string key = $"{gameObject.scene.handle}/{prefabName}/{controllerName}/{reason}";

        // 반복 생성 실패가 같은 오류를 수백 번 출력하지 않도록 중복을 제거합니다.
        if (!AnimationFailures.Add(key)) return;

        // 다음 Windows 빌드에서 실패 조건과 런타임 상태를 비교할 수 있도록 기록합니다.
        Debug.LogError(
            $"[Fish Animator 초기화 실패] prefab={prefabName}, controller={controllerName}, "
            + $"reason={reason}, {details}", this);
    }

    /// <summary>
    /// Animator에 사망 전환용 Bool 파라미터가 있는지 확인합니다.
    /// </summary>
    private bool HasDeathParameter()
    {
        // 이름뿐 아니라 파라미터 타입도 함께 검사합니다.
        AnimatorControllerParameter[] parameters = _animator.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.nameHash == IsDeadHash
                && parameter.type == AnimatorControllerParameterType.Bool)
            {
                return true;
            }
        }

        // 잘못된 컨트롤러 설정은 생성 시점에 알립니다.
        LogAnimationFailure(
            "IsDead Bool 파라미터 없음",
            $"activeInHierarchy={_animator.gameObject.activeInHierarchy}, "
            + $"isInitialized={_animator.isInitialized}, parameterCount={parameters.Length}");
        return false;
    }

    /// <summary>
    /// 사망 부유, Aggressive 감지와 전투 가능한 개체의 넉백 및 추격을 갱신합니다.
    /// </summary>
    private void FixedUpdate()
    {
        // 초기화 이전과 획득 완료 이후에는 행동하지 않습니다.
        if (!_initialized || _model.IsCollected)
        {
            return;
        }

        // 회피형도 검사할 수 있도록 CanFight의 조기 반환 이전에 감지합니다.
        UpdatePlayerDetection();

        // 감지로 바뀐 상태를 반영한 뒤 벽 보고와 저장된 일반 이동 명령을 처리합니다.
        UpdateGroupMovement(Time.fixedDeltaTime);

        // 사망 개체는 획득되기 전까지 사망 위치 주변에서 떠다닙니다.
        if (State == FishState.Dead)
        {
            // 모델에 설정한 주기로 부유 위상을 계산합니다.
            _deadFloatElapsed += Time.fixedDeltaTime;
            float phase =
                _deadFloatElapsed * Mathf.PI * 2f / _model.DeadFloatPeriod;

            // 사망 위치를 기준으로 설정한 높이만큼 위아래로 이동합니다.
            Vector3 position = _deadFloatOrigin
                + Vector3.up * Mathf.Sin(phase) * _model.DeadFloatAmplitude;

            _mover.SetHeldPosition(position);
            return;
        }

        // 포획 중에는 작살이 전달하는 위치 명령만 받습니다.
        if (State == FishState.HarpoonAction)
        {
            return;
        }

        // Passive와 Aggressive에게 공통 전투 흐름을 적용합니다.
        if (!CanFight)
        {
            return;
        }

        // 소유 스포너가 정지되면 추격과 공격도 중단합니다.
        if (_spawner == null || !_spawner.isActiveAndEnabled)
        {
            Stop();
            _damageableSensor.enabled = false;
            return;
        }


        // 공격 대기와 넉백 중에도 마지막 피격 이후의 전투 시간을 줄입니다.
        if (State == FishState.Combat)
        {
            _combatRemaining = Mathf.Max(0f, _combatRemaining - Time.fixedDeltaTime);

            // 전투 시간이 끝나거나 Hero가 멀어져도 공격 후 쿨타임을 마칠 때까지 복귀를 미룹니다.
            if (_chaser.ShouldEndCombat(_combatRemaining, _mover.WorldPosition))
            {
                ChangeState(FishState.Moving);
            }
        }

        // 원래 높이 영역에 도착할 때까지 무리 대신 복귀 이동을 수행합니다.
        if (State == FishState.Moving && _isReturningToVerticalRange)
        {
            ReturnToVerticalRange(Time.fixedDeltaTime);
            return;
        }

        // 넉백은 일반 이동과 추격보다 우선합니다.
        if (_isKnockedBack)
        {
            if (_knockbackRemaining > 0f)
            {
                // 마지막 프레임에도 목표 거리 이상 밀리지 않게 조정합니다.
                float step = Mathf.Min(
                    Time.fixedDeltaTime,
                    _knockbackRemaining);

                _mover.SetVelocity(
                    _knockbackVelocity * (step / Time.fixedDeltaTime));

                _knockbackRemaining =
                    Mathf.Max(0f, _knockbackRemaining - step);

                return;
            }

            // 마지막 넉백 이동이 반영된 다음 물리 갱신에서 종료합니다.
            _isKnockedBack = false;
            _mover.Stop();
        }

        // 피격 또는 Hero 감지로 전투에 들어가기 전에는 공격하지 않습니다.
        if (State != FishState.Combat)
        {
            _damageableSensor.enabled = false;
            return;
        }

        // Hero가 사라지거나 사망하면 추격과 공격을 중단합니다.
        if (_heroModel == null
            || !_heroModel.isActiveAndEnabled
            || _heroModel.CurrentHealth <= 0f)
        {
            Stop();
            _damageableSensor.enabled = false;
            return;
        }

        // 접근 감속은 Inspector에 설정한 거리에서 시작합니다.
        float slowDistance = Mathf.Max(0f, _combatSlowDistance);

        // 후퇴 간격에는 머리 길이도 반영하여 큰 물고기가 충분히 거리를 벌리게 합니다.
        float headReach = Vector3.Distance(_mover.WorldPosition, _head.position);
        float retreatTargetDistance = Mathf.Max(slowDistance, headReach)
            + Mathf.Max(0f, _combatRetreatDistance);
        // 실제 몸체 히트박스를 전달하여 가장 가까운 표면으로 접근합니다.
        bool canChase = _chaser.TryGetMove(
            _mover.WorldPosition,
            _heroHitbox,
            Time.fixedDeltaTime,
            slowDistance,
            retreatTargetDistance,
            out Vector2 direction,
            out float speed);

        // 이번 이동 계산에서 쿨타임이 끝났다면 공격을 다시 시도하기 전에 전투 종료를 확인합니다.
        if (_chaser.ShouldEndCombat(_combatRemaining, _mover.WorldPosition))
        {
            // 종료가 미뤄졌던 전투는 추가 공격이나 접근 없이 일반 이동으로 복귀합니다.
            ChangeState(FishState.Moving);
            return;
        }

        // 추격할 수 없으면 정지하고, 공격 후 대기 중에는 공격 센서만 중단합니다.
        _damageableSensor.enabled = canChase && !_chaser.IsCoolingDown;
        if (!canChase)
        {
            _mover.Stop();
            return;
        }

        // 같은 위치에 도달했을 때도 마지막 유효한 방향을 유지합니다.
        if (direction.sqrMagnitude > 0.0001f)
        {
            _combatDirection = direction;
        }

        // 기존 센서를 호출하고 Hero에게 실제 피해가 발생했는지 확인합니다.
        float healthBefore = _heroModel.CurrentHealth;
        // 일반 공격 메서드는 enabled를 검사하지 않으므로 쿨타임을 직접 확인합니다.
        if (!_chaser.IsCoolingDown)
        {
            // 공격 가능 상태에서만 플레이어에게 피해를 시도합니다.
            // 기존 공격력을 유지하되 방어력을 차감한 뒤에도 최소 1의 피해가 남도록 보정합니다.
            float attackDamage = Mathf.Max(_model.Damage, _heroModel.Defense + 1f);

            // 기존 센서로 공격하여 체력 감소, 쿨타임과 피격 연출 경로를 유지합니다.
            _damageableSensor.Attack(attackDamage);
        }

        // 피해를 준 경우에만 공격 후 대기를 시작합니다.
        if (_heroModel.CurrentHealth < healthBefore)
        {
            _chaser.BeginAttackCooltime();
            _damageableSensor.enabled = false;
            _mover.Stop();
            return;
        }

        // 쿨타임 중에는 머리를 플레이어 쪽에 둔 채 뒤로 물러납니다.
        if (_chaser.IsCoolingDown)
        {
            // 후퇴에는 접근용 머리 방향 제한을 적용하지 않습니다.
            _mover.SetVelocity(direction * speed);
            return;
        }

        // 접근할 때는 머리가 목표 방향으로 돌아온 정도에 맞춰 속도를 조절합니다.
        _mover.MoveCombat(_head, _tail, direction, speed, Time.fixedDeltaTime);
    }

    /// <summary>
    /// 감지 기록과 예약을 초기화하여 다음 유효한 물리 스텝에 즉시 검사합니다.
    /// </summary>
    private void ResetPlayerDetection()
    {
        // 이전 생애나 감지 중단 이전의 범위 진입 기록을 제거합니다.
        _wasHeroInDetectionRange = false;

        // 첫 거리 검사는 예약 대기 없이 바로 실행합니다.
        _nextPlayerDetectionTime = double.NegativeInfinity;

        // 첫 검사 이후 예약에만 분산 간격을 적용합니다.
        _needsDetectionStagger = true;
    }

    /// <summary>
    /// 첫 검사 이후에는 다음 검사 시점을 분산하고 이후에는 설정된 간격으로 예약합니다.
    /// </summary>
    private void ScheduleNextPlayerDetection(double currentTime, float interval)
    {
        // 간격이 0이면 기존처럼 매 물리 스텝 검사합니다.
        if (interval <= 0f)
        {
            // 다음 호출도 즉시 검사할 수 있도록 현재 시간으로 예약합니다.
            _nextPlayerDetectionTime = currentTime;

            // 이후 주기 검사를 켜면 다시 분산 예약을 적용합니다.
            _needsDetectionStagger = true;
            return;
        }

        // 이미 분산된 개체는 정상 검사 간격을 유지합니다.
        if (!_needsDetectionStagger)
        {
            // 밀린 검사를 몰아서 실행하지 않고 현재 시점부터 예약합니다.
            _nextPlayerDetectionTime = currentTime + interval;
            return;
        }

        // 이번 예약에서만 최초 분산 간격을 적용합니다.
        _needsDetectionStagger = false;

        // 감지 간격 안에 배정할 수 있는 물리 스텝 수를 계산합니다.
        float fixedStep = Time.fixedDeltaTime;
        int slotCount = Mathf.Max(1, Mathf.CeilToInt(interval / fixedStep));

        // 공유 순번을 사용하여 슬롯을 순환 배정합니다.
        uint sequence = s_detectionScheduleSequence;
        s_detectionScheduleSequence = unchecked(sequence + 1u);
        int slotIndex = (int)(sequence % (uint)slotCount);

        // 첫 검사 이후 최소 한 물리 스텝부터 설정 간격까지 분산합니다.
        double firstDelay = System.Math.Min(
            (slotIndex + 1) * (double)fixedStep, interval);

        // 두 번째 검사만 분산하고 이후에는 정상 간격을 사용합니다.
        _nextPlayerDetectionTime = currentTime + firstDelay;
    }

    /// <summary>
    /// 공격형과 회피형의 범위 진입을 판정하고 알림 및 전투 시작을 처리합니다.
    /// </summary>
    private void UpdatePlayerDetection()
    {
        // 감지 가능한 타입과 물고기, 스포너, 플레이어 상태를 확인합니다.
        bool canDetect =
            (Type == FishType.Aggressive || Type == FishType.Evasive)
            && State != FishState.Dead
            && State != FishState.HarpoonAction
            && _model.CurrentHp > 0f
            && _spawner != null
            && _spawner.isActiveAndEnabled
            && _combatTarget != null
            && _combatTarget.gameObject.activeInHierarchy;

        // 연결된 플레이어 모델의 비활성 또는 사망 여부를 반영합니다.
        if (_heroModel != null)
        {
            canDetect = canDetect
                && _heroModel.isActiveAndEnabled
                && _heroModel.CurrentHealth > 0f;
        }

        // 감지가 불가능하면 다음 유효한 접근을 새 진입으로 판단합니다.
        if (!canDetect)
        {
            ResetPlayerDetection();
            return;
        }

        // 물리 시간 기준으로 거리 검사 예약을 확인합니다.
        double currentTime = Time.fixedTimeAsDouble;
        float interval = Mathf.Max(0f, _playerDetectionInterval);

        // 대기 중에는 이전 감지 결과를 보존하고 거리 계산만 생략합니다.
        if (interval > 0f && currentTime < _nextPlayerDetectionTime)
        {
            return;
        }

        // 첫 검사 직후의 예약을 분산하고 이후에는 정상 간격을 유지합니다.
        ScheduleNextPlayerDetection(currentTime, interval);

        // 공격형은 데이터의 반경을, 회피형은 별도 설정한 반경을 사용합니다.
        float radius = Type == FishType.Aggressive
            ? Mathf.Max(0f, Data.HeroDetectionRadius)
            : Mathf.Max(0f, _evasiveDetectionRadius);

        // 실제 물고기 위치와 플레이어의 XYZ 거리를 비교합니다.
        Vector3 displacement = _combatTarget.position - _mover.WorldPosition;
        bool isInRange =
            radius > 0f
            && displacement.sqrMagnitude <= radius * radius;

        // 범위 진입 순간을 구분하고 다음 판정을 위해 현재 결과를 저장합니다.
        bool enteredRange = isInRange && !_wasHeroInDetectionRange;
        _wasHeroInDetectionRange = isInRange;

        // 범위에 머무르거나 벗어난 경우에는 알림을 재생하지 않습니다.
        if (!enteredRange)
        {
            return;
        }

        // 공격형은 평상시 이동 중 새로 감지했을 때만 전투를 시작합니다.
        if (Type == FishType.Aggressive && State == FishState.Moving)
        {
            ChangeState(FishState.Combat);
        }

        // 감지 시작을 한 번 표시하고 연출의 자동 종료는 View에 맡깁니다.
        if (_fishView != null)
        {
            _fishView.ShowDetection();
        }
    }

    /// <summary>
    /// 원래 높이 범위의 가장 가까운 경계까지 헤엄쳐 복귀하고 높이 제한을 복원합니다.
    /// </summary>
    private void ReturnToVerticalRange(float deltaTime)
    {
        // 높이 범위가 없으면 복귀할 필요 없이 일반 무리 이동을 허용합니다.
        if (_verticalRange == null)
        {
            _isReturningToVerticalRange = false;
            _mover.SetVerticalRangeEnabled(true);
            return;
        }

        // 현재 높이에서 가장 가까운 원래 영역의 높이를 구합니다.
        float currentY = _mover.Position.y;
        float targetY = Mathf.Clamp(currentY, _verticalRange.MinY, _verticalRange.MaxY);
        float distanceY = targetY - currentY;

        // 영역에 도착하면 높이 제한을 복원하고 다음 갱신부터 무리를 따릅니다.
        if (Mathf.Abs(distanceY) <= 0.01f)
        {
            _isReturningToVerticalRange = false;
            _mover.SetVerticalRangeEnabled(true);
            _mover.Stop();
            return;
        }

        // 기본 속도로 복귀하되 마지막 이동에서 목표 높이를 지나치지 않게 합니다.
        float speed = Mathf.Min(
            Mathf.Max(0f, _model.MoveSpeed),
            Mathf.Abs(distanceY) / deltaTime);
        Vector2 direction = distanceY > 0f ? Vector2.up : Vector2.down;

        // 남은 거리로 제한한 속도를 바로 적용해 경계를 지나 왕복하지 않게 합니다.
        _mover.SetVelocity(direction * speed);
    }

    /// <summary>
    /// 물고기의 월드 Y 이동 범위를 이동기에 전달합니다.
    /// </summary>
    public void SetVerticalRange(FishVerticalRange verticalRange)
    {
// 전투 후 돌아갈 원래 높이 범위를 Fish에도 보관합니다.
        _verticalRange = verticalRange;

        // 실제 물리 이동을 담당하는 객체에 높이 제한을 전달합니다.
        _mover.SetVerticalRange(verticalRange);
    }

    /// <summary>
    /// 일반 무리 이동 중 발생한 구조물 접촉면을 한 번 반환합니다.
    /// </summary>
    public bool TryConsumeObstacleNormal(out Vector2 normal)
    {
        // 초기화 전에 이동기가 없더라도 안전하게 실패를 반환합니다.
        normal = Vector2.zero;
        if (_mover == null)
        {
            return false;
        }

        // 전투나 포획 중 생긴 기록도 소비하여 이전 접촉이 남지 않게 합니다.
        bool hasContact = _mover.TryConsumeObstacleNormal(out normal);

        // 현재 패턴을 따라가는 물고기의 접촉만 방향 전환에 사용합니다.
        return CanFollowGroup && hasContact;
    }

    /// <summary>
    /// 일반 이동이 가능한 상태에서 패턴의 다음 목표로 이동하며 일반 Follow의 속도 제한은 적용하지 않습니다.
    /// </summary>
    public bool FollowPattern(Vector2 targetPosition, float deltaTime, bool faceMovement = false)
    {
        // 사망, 포획, 전투, 넉백 및 수직 범위 복귀 중에는 패턴 이동을 받지 않습니다.
        if (!CanFollowGroup || _mover == null)
        {
            return false;
        }

        // 물리 이동과 경계 제한은 이동 컴포넌트에 위임합니다.
        // 연속 경로와 저장된 일반 무리 명령이 같은 몸체를 동시에 움직이지 않게 합니다.
        ClearGroupMoveCommand();
        return _mover.MoveAlongPattern(targetPosition, deltaTime, faceMovement);
    }

    /// <summary>
    /// 패턴의 이동 명령을 저장하고 실제 적용은 물리 갱신에 맡깁니다.
    /// </summary>
    public void Follow(Vector2 direction, float speed, float deltaTime)
    {
        // 일반 무리 이동을 허용하지 않는 상태에는 명령을 남기지 않습니다.
        if (!CanFollowGroup)
        {
            ClearGroupMoveCommand();
            return;
        }

        // 잘못된 명령이 이후 물리 프레임에 반복 적용되지 않게 합니다.
        if (float.IsNaN(direction.x) || float.IsInfinity(direction.x)
            || float.IsNaN(direction.y) || float.IsInfinity(direction.y)
            || float.IsNaN(speed) || float.IsInfinity(speed))
        {
            ClearGroupMoveCommand();
            return;
        }

        // 물리 속도는 여기서 변경하지 않고 목표 방향과 기본 속도 상한만 저장합니다.
        _groupMoveDirection = direction.sqrMagnitude >= 0.0001f ? direction.normalized : Vector2.zero;
        _groupMoveSpeed = Mathf.Clamp(speed, 0f, Mathf.Max(0f, _model.MoveSpeed));
        _hasGroupMoveCommand = true;
    }

    /// <summary>
    /// 플레이어 모델에 연결된 콜라이더 중 상호작용용 트리거를 제외한 실제 몸체를 찾습니다.
    /// </summary>
    private Collider FindHeroHitbox(Hero hero)
    {
        // 탐색할 플레이어와 실제 피해 수신 모델이 필요합니다.
        if (hero == null || _heroModel == null)
        {
            return null;
        }

        // 초기화 시 한 번만 활성 플레이어의 자식 콜라이더를 탐색합니다.
        Collider[] colliders = hero.GetComponentsInChildren<Collider>();

        // 현재 플레이어의 단일 몸체 콜라이더를 선택합니다.
        foreach (Collider candidate in colliders)
        {
            // 비활성 콜라이더와 상호작용용 트리거는 제외합니다.
            if (!candidate.enabled || candidate.isTrigger)
            {
                continue;
            }

            // 실제 피해 수신 모델이 일치하는 플레이어 몸체만 선택합니다.
            if (candidate.GetComponentInParent<HeroRunModel>() != _heroModel)
            {
                continue;
            }

            // 실제 몸체 히트박스를 반환하여 반복 검색을 피합니다.
            return candidate;
        }

        // 유효한 실제 몸체가 없으면 초기화 실패로 처리합니다.
        return null;
    }

    /// <summary>
    /// 전투 중에는 실제 머리 축을 Hero에게 맞추고 일반 이동에는 수영 회전을 적용합니다.
    /// </summary>
    private void LateUpdate()
    {
        // 초기화 전과 피격 넉백, 사망 및 포획 중에는 기존 자세 제어를 유지합니다.
        if (!_initialized
            || _model.IsCollected
            || _isKnockedBack
            || State == FishState.Dead
            || State == FishState.HarpoonAction)
        {
            return;
        }

        // 전투 중에는 정지하거나 공격 후 대기 중이어도 Hero를 바라봅니다.
        if (CanFight && State == FishState.Combat)
        {
            // 실제 몸체 히트박스가 활성 상태인 경우에만 전투 회전을 갱신합니다.
            if (_heroHitbox != null
                && _heroHitbox.enabled
                && _heroHitbox.gameObject.activeInHierarchy)
            {
                // 접근 계산과 동일하게 Rigidbody 중심에서 가장 가까운 표면점을 구합니다.
                Vector3 targetPosition = _heroHitbox.ClosestPoint(_mover.WorldPosition);

                // 가장 가까운 히트박스 표면을 향해 머리 방향을 갱신합니다.
                _mover.TurnHeadTowards(
                    _head, _tail, targetPosition, Time.deltaTime);
            }

            // 일반 수영의 X 고정 회전이 전투 자세를 덮어쓰지 않도록 종료합니다.
            return;
        }

        // 무리 이동과 원래 높이 영역으로 복귀하는 동안에는 수영 회전을 사용합니다.
        if (CanFollowGroup || _isReturningToVerticalRange)
        {
            _mover.UpdateFacing();
        }
    }

    /// <summary>
    /// 생존 피격 시 전투 가능한 개체를 전투로 전환하고 모델 설정에 따라 넉백합니다.
    /// </summary>
    private void OnDamaged()
    {
        // 초기화된 전투 가능한 생존 개체에만 반격과 넉백을 적용합니다.
        if (!_initialized
            || _model.IsCollected
            || !CanFight
            || !isActiveAndEnabled
            || State == FishState.Dead
            || State == FishState.HarpoonAction)
        {
            return;
        }

        // 최초 생존 피격부터 전투 상태로 전환합니다.
        if (State == FishState.Moving)
        {
            ChangeState(FishState.Combat);
        }

        // 넉백 설정과 관계없이 추가 피해를 받을 때마다 전투 시간을 갱신합니다.
        _combatRemaining = _model.CombatDuration;

        // 현재 Head에서 Tail로 향하는 방향을 XY로 계산합니다.
        Vector3 backward = _tail.position - _head.position;
        Vector2 direction = new Vector2(backward.x, backward.y);

        // 유효한 방향과 넉백 거리가 있어야 이동합니다.
        if (direction.sqrMagnitude < 0.0001f
            || _model.KnockbackDistance <= 0f)
        {
            return;
        }

        // 시간과 거리는 모델에서 읽고 실행 중인 속도만 Fish에 보관합니다.
        _knockbackRemaining = _model.KnockbackDuration;
        _knockbackVelocity =
            direction.normalized
            * (_model.KnockbackDistance / _knockbackRemaining);

        _isKnockedBack = true;

        // 넉백 중 기존 이동 속도와 접촉 공격을 중단합니다.
        _damageableSensor.enabled = false;
        _mover.Stop();
    }

    /// <summary>
    /// 초기화된 물고기의 일반 행동 상태 변경을 모델에 요청합니다.
    /// </summary>
    public void ChangeState(FishState nextState)
    {
        // 프리팹에 데이터만 연결된 상태에서는 상태 변경을 요청하지 않습니다.
        if (!_initialized)
        {
            return;
        }

        // 실제 상태 전환 가능 여부는 모델에서 판단합니다.
        _model.ChangeState(nextState);
    }

    /// <summary>
    /// 이동과 진행 중인 넉백을 정지합니다.
    /// </summary>
    public void Stop()
    {
        // 정지 뒤에 이전 무리 명령으로 다시 움직이지 않게 합니다.
        ClearGroupMoveCommand();

        // 이전 행동의 넉백 정보를 초기화합니다.
        _isKnockedBack = false;
        _knockbackRemaining = 0f;

        // 초기화 이전 호출도 안전하게 처리합니다.
        if (_mover != null)
        {
            _mover.Stop();
        }
    }

    /// <summary>
    /// 모델 상태에 따라 포획, 물리 복원, 사망 부유와 애니메이션 조건을 적용합니다.
    /// </summary>
    private void OnStateChanged(FishState state)
    {
        // 활성화 후 Animator 검증까지 완료된 개체만 상태와 애니메이션을 갱신합니다.
        if (!_initialized || !_deathAnimationValidated)
        {
            return;
        }

        // 모델의 상태 변경을 받아 Presenter 역할로 UI 표시를 갱신합니다.
        ApplyFishViewState(state);

        // 사망 또는 포획으로 감지가 중단되면 다음 접근을 새 진입으로 판단합니다.
        if (state == FishState.Dead || state == FishState.HarpoonAction)
        {
            ResetPlayerDetection();
        }

        // 이전 행동의 이동과 넉백을 종료합니다.
        Stop();

        // 전투 진입 시 시간을 시작하고 다른 상태에서는 남은 시간을 정리합니다.
        _combatRemaining = state == FishState.Combat ? _model.CombatDuration : 0f;

        // Moving으로 돌아왔을 때만 원래 높이 영역 밖인지 확인합니다.
        float currentY = _mover.Position.y;
        _isReturningToVerticalRange =
            state == FishState.Moving
            && _verticalRange != null
            && (currentY < _verticalRange.MinY || currentY > _verticalRange.MaxY);

        // Combat과 복귀 중에는 Y 제한을 해제하고, 일반 수영에서만 적용합니다.
        _mover.SetVerticalRangeEnabled(
            state == FishState.Moving && !_isReturningToVerticalRange);

        // 이전 전투의 공격 대기가 다음 피격 전투에 남지 않게 정리합니다.
        if (state == FishState.Moving)
        {
            _chaser.ResetAttackCooltime();
        }

        // 공격 재개 여부는 전투 갱신에서 판단합니다.
        if (_damageableSensor != null)
        {
            _damageableSensor.enabled = false;
        }

        // 각 프리팹의 컨트롤러가 사망 클립으로 전환하도록 상태만 전달합니다.
        _animator.SetBool(IsDeadHash, state == FishState.Dead);

        // 포획 중에는 충돌을 중단하고 외부 위치 명령만 받습니다.
        if (state == FishState.HarpoonAction)
        {
            SuspendCaptureColliders();
            _mover.FreezeBody();
            return;
        }

        // 포획 취소 또는 포획 중 사망 시 원래 콜라이더 상태를 복원합니다.
        RestoreCaptureColliders();

        // 생존 상태로 돌아오면 일반 물리 이동 설정을 복원합니다.
        if (state != FishState.Dead)
        {
            _mover.RestoreBody();
            return;
        }

        // 사망 위치와 부유 시간은 최초 사망 시 한 번만 설정합니다.
        if (_deathStarted)
        {
            return;
        }

        _deathStarted = true;
        _deadFloatOrigin = _mover.WorldPosition;
        _deadFloatElapsed = 0f;
        _mover.FreezeBody();

        // 공격과 일반 충돌에 사용하던 콜라이더를 중단합니다.
        foreach (Collider body in GetComponentsInChildren<Collider>(true))
        {
            body.enabled = false;
        }

        // 사체는 상호작용 검색에 남기되 물리적으로 밀지 않게 합니다.
        _interactionCollider.isTrigger = true;
        _interactionCollider.enabled = true;

        // 화면 밖에서도 사망 전환과 클립 재생을 계속 진행합니다.
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.speed = 1f;
    }

    /// <summary>
    /// 포획 시작 시 콜라이더 활성 상태를 저장하고 충돌을 중단합니다.
    /// </summary>
    private void SuspendCaptureColliders()
    {
        // 반복 알림으로 원래 활성 상태를 덮어쓰지 않습니다.
        if (_captureColliders != null)
        {
            return;
        }

        // 자식에 포함된 콜라이더의 개별 활성 상태를 저장합니다.
        _captureColliders = GetComponentsInChildren<Collider>(true);
        _colliderEnabledBeforeCapture = new bool[_captureColliders.Length];

        // 포획 중에는 물리 반응과 일반 공격 검색에서 제외합니다.
        for (int i = 0; i < _captureColliders.Length; i++)
        {
            _colliderEnabledBeforeCapture[i] = _captureColliders[i].enabled;
            _captureColliders[i].enabled = false;
        }
    }

    /// <summary>
    /// 포획 시작 전에 사용하던 콜라이더 활성 상태를 복원합니다.
    /// </summary>
    private void RestoreCaptureColliders()
    {
        // 포획 이력이 없으면 복원할 설정이 없습니다.
        if (_captureColliders == null)
        {
            return;
        }

        // 원래 꺼져 있던 콜라이더까지 켜지 않도록 개별 복원합니다.
        for (int i = 0; i < _captureColliders.Length; i++)
        {
            if (_captureColliders[i] != null)
            {
                _captureColliders[i].enabled =
                    _colliderEnabledBeforeCapture[i];
            }
        }

        // 복원 완료 후 저장 정보를 비웁니다.
        _captureColliders = null;
        _colliderEnabledBeforeCapture = null;
    }

    /// <summary>
    /// 모델이 전달한 포획 위치를 실제 물리 루트에 적용합니다.
    /// </summary>
    private void OnCapturedMoveRequested(Vector3 position)
    {
        // 비활성화 또는 포획 종료 이후의 요청은 실행하지 않습니다.
        if (!isActiveAndEnabled || !_model.IsCaptured)
        {
            return;
        }

        // 실제 Rigidbody 위치 변경은 무버가 담당합니다.
        _mover.SetHeldPosition(position);
    }

    /// <summary>
    /// 모델이 확정한 획득 결과에 따라 물고기 루트를 제거합니다.
    /// </summary>
    private void OnCollected()
    {
        // 획득 처리와 풀 반환 전에 UI와 진행 중인 발견 연출을 숨깁니다.
        if (_fishView != null)
        {
            _fishView.SetVisible(false);
        }

        // 사망으로 이벤트가 해제된 사체도 풀 반환 전에 수집을 한 번 보고합니다.
        if (_spawner != null) _spawner.NotifyFishQuestCollected(this);
        // 같은 프레임에 이동이나 공격이 이어지지 않도록 중단합니다.
        Stop();

        if (_damageableSensor != null)
        {
            _damageableSensor.enabled = false;
        }

        // 삭제 전 추가 상호작용 검색에서도 제외합니다.
        foreach (Collider body in GetComponentsInChildren<Collider>(true))
        {
            body.enabled = false;
        }

        // 아이템 지급은 Hero가 담당하므로 물고기는 풀에 반환합니다.
        Release();
    }

    /// <summary>
    /// 활성 물고기의 View를 연결하고 생성 또는 재사용 시 현재 표시를 적용합니다.
    /// </summary>
    private void InitializeFishView()
    {
        // 비활성 풀 초기화 중에는 표시하지 않고 OnEnable까지 기다립니다.
        if (!_initialized || !isActiveAndEnabled)
        {
            return;
        }

        // Inspector 연결이 없으면 비활성 자식까지 포함해 View를 찾습니다.
        if (_fishView == null)
        {
            _fishView = GetComponentInChildren<FishView>(true);
        }

        // View가 없는 물고기는 UI 처리를 생략합니다.
        if (_fishView == null)
        {
            return;
        }

        // 이전 사용의 표시를 초기화하고 따라갈 물고기 루트를 연결합니다.
        _fishView.BindTarget(transform);

        // 초기화 당시의 상태와 물고기 타입을 표시합니다.
        ApplyFishViewState(_model.State);
    }

    /// <summary>
    /// 모델의 행동 상태와 타입을 해석해 View에 표시 명령을 전달합니다.
    /// </summary>
    private void ApplyFishViewState(FishState state)
    {
        // 연결된 View가 없으면 표시 처리를 생략합니다.
        if (_fishView == null)
        {
            return;
        }

        // 초기화된 활성 물고기 중 살아 있고 획득되지 않은 경우만 표시합니다.
        bool visible =
            _initialized
            && isActiveAndEnabled
            && !_model.IsCollected
            && _model.CurrentHp > 0f
            && state != FishState.Dead;

        // 전체 UI 표시 여부를 전달하며 숨김 시 발견 연출도 중단합니다.
        _fishView.SetVisible(visible);

        // 공격형 물고기인지 판단하여 상시 아이콘 표시 여부를 전달합니다.
        _fishView.SetAggressiveVisible(
            visible && _data.Type == FishType.Aggressive);

        // 포획 중에는 상시 아이콘은 유지하되 진행 중인 발견 알림을 취소합니다.
        if (state == FishState.HarpoonAction)
        {
            _fishView.HideDetection();
        }
    }

    /// <summary>
    /// 피격 효과를 재생하고 수동형 물고기의 생존 피격을 알림으로 표시합니다.
    /// </summary>
    private void OnDamageReceived()
    {
        // 초기화 전, 비활성 상태 또는 획득 이후의 알림은 처리하지 않습니다.
        if (!_initialized
            || !isActiveAndEnabled
            || _model.IsCollected)
        {
            return;
        }

        // 피격 색상 효과가 연결된 경우에만 재생합니다.
        if (_hitFlash != null)
        {
            _hitFlash.Play();
        }

        // View가 없는 물고기는 피격 효과까지만 처리합니다.
        if (_fishView == null)
        {
            return;
        }

        // 피해 이벤트는 사망 상태 변경보다 먼저 발생하므로 체력을 확인합니다.
        if (_model.CurrentHp <= 0f)
        {
            // 치명적인 피해에는 발견 알림 대신 전체 UI를 숨깁니다.
            _fishView.SetVisible(false);
            return;
        }

        // 수동형은 접근 감지 대신 실제 생존 피격을 알림으로 표시합니다.
        if (Type == FishType.Passive)
        {
            _fishView.ShowDetection();
        }
    }
    /// <summary>
    /// 생성된 물고기를 소유 패턴의 자리에 연결하고 첫 이동 판단을 요청합니다.
    /// </summary>
    public void BindGroupPattern(FishSpawnPattern pattern, int slotIndex)
    {
        // 다른 대여에서 저장된 명령과 복귀 상태를 초기화합니다.
        ClearGroupMoveCommand();
        _wasFollowingGroup = false;
        _groupPattern = pattern;
        _groupSlotIndex = slotIndex;

        // 리스폰된 물고기도 다음 정기 주기까지 기다리지 않고 새 명령을 받습니다.
        if (_groupPattern != null)
            _groupPattern.RequestDecision();
    }

    /// <summary>
    /// 다른 행동의 물리 속도를 건드리지 않고 일반 무리 명령만 해제합니다.
    /// </summary>
    private void ClearGroupMoveCommand()
    {
        // 전투나 작살이 적용한 속도를 여기서 정지시키지 않습니다.
        _hasGroupMoveCommand = false;
        _groupMoveDirection = Vector2.zero;
        _groupMoveSpeed = 0f;
    }

    /// <summary>
    /// 패턴 판단과 독립적으로 벽을 보고하고 저장된 일반 이동 명령을 실행합니다.
    /// </summary>
    private void UpdateGroupMovement(float deltaTime)
    {
        // 다른 행동이나 스포너 비활성화 중에는 기존 무리 명령을 해제합니다.
        if (!CanFollowGroup || _spawner == null || !_spawner.isActiveAndEnabled || _mover == null)
        {
            ClearGroupMoveCommand();
            _wasFollowingGroup = false;
            return;
        }

        // 전투와 넉백에서 복귀한 경우 현재 위치를 기준으로 새 판단을 요청합니다.
        if (_groupPattern != null)
        {
            if (!_wasFollowingGroup)
                _groupPattern.RequestDecision();

            // 전방 검사는 이동기의 독립 주기를 따르며 반전 대기 중에는 생략합니다.
            if (_groupPattern.CanProbeObstacles)
            {
                Vector2 approach = _mover.RequestedVelocity;
                if (_mover.TryConsumeObstacleNormal(out Vector2 normal))
                    _groupPattern.ReportObstacle(_groupSlotIndex, normal, approach);
            }
            else
            {
                // 대기 중의 옛 접촉이 다음 반전을 유발하지 않게 기록만 버립니다.
                _mover.ClearObstacleContact();
            }
        }
        _wasFollowingGroup = true;

        // 실제 가속과 경계 제한은 패턴 간격 대신 물리 시간으로 실행합니다.
        if (_hasGroupMoveCommand)
            _mover.Move(_groupMoveDirection, _groupMoveSpeed, deltaTime);
    }
}
