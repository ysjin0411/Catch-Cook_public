using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 물고기의 능력치, 행동 상태와 피해·포획·획득 규칙을 관리합니다.
/// </summary>
public class FishModel : MonoBehaviour, IDamageable, ICatchableFish, IDeadFish
{
    // 런타임 능력치입니다.
    [SerializeField] private float _maxHp;
    [SerializeField] private float _currentHp;
    [SerializeField] private float _damage;
    [SerializeField] private float _moveSpeed;

    // 현재 행동 상태입니다.
    [SerializeField] private FishState _state;

    // 현재 체력이 최대 체력의 이 비율 이하일 때 포획할 수 있습니다.
    [SerializeField, Range(0f, 100f)]
    private float _captureHpPercent = 30f;

    // 획득 성공 후 Hero가 가방에 지급할 아이템 정보입니다.
    [SerializeField] private ItemData _filletData;
    [SerializeField, Min(1)] private int _filletAmount = 1;
    // 기본 이동 속도에 더할 추격 속도입니다.
    [SerializeField, Min(0f)] private float _additionalChaseSpeed = 2f;

    // Hero와의 XYZ 거리 기준 최대 추격 거리입니다.
    [SerializeField, Min(0f)] private float _maxChaseDistance = 15f;

    // 마지막 생존 피격 이후 전투를 유지할 시간입니다.
    [SerializeField, Min(0.1f)] private float _combatDuration = 5f;

    // 공격 성공 후 다음 공격을 금지할 시간입니다. 이 시간에는 후퇴할 수 있습니다.
    [FormerlySerializedAs("_waitAfterAttack")]
    [Tooltip("공격으로 실제 피해를 준 뒤 다음 공격까지 기다리는 시간입니다. 단위는 초입니다.")]
    [SerializeField, Min(0f)] private float _cooltime = 1f;

    // 피격 시 뒤로 밀려날 거리와 소요 시간입니다.
    [SerializeField, Min(0f)] private float _knockbackDistance = 1f;
    [SerializeField, Min(0.01f)] private float _knockbackDuration = 0.2f;

    // 사망 위치를 중심으로 떠다닐 높이와 주기입니다.
    [SerializeField, Min(0f)] private float _deadFloatAmplitude = 0.15f;
    [SerializeField, Min(0.1f)] private float _deadFloatPeriod = 3f;

    // 실제 피해가 적용된 경우 사망 여부와 관계없이 알립니다.
    public event Action DamageReceived;

    // 실행 클래스는 모델의 설정을 읽기만 합니다.
    public float AdditionalChaseSpeed => Mathf.Max(0f, _additionalChaseSpeed);
    public float MaxChaseDistance => Mathf.Max(0f, _maxChaseDistance);
    public float CombatDuration => Mathf.Max(0.1f, _combatDuration);
    public float Cooltime => Mathf.Max(0f, _cooltime);
    public float KnockbackDistance => Mathf.Max(0f, _knockbackDistance);
    public float KnockbackDuration => Mathf.Max(0.01f, _knockbackDuration);
    public float DeadFloatAmplitude => Mathf.Max(0f, _deadFloatAmplitude);
    public float DeadFloatPeriod => Mathf.Max(0.1f, _deadFloatPeriod);

    // 초기화 여부와 중복 획득 방지 상태입니다.
    private bool _isInitialized;
    private bool _isCollected;

    // 포획 취소 시 복귀할 행동 상태입니다.
    private FishState _stateBeforeCapture;

    // FishData에서 전달받은 물고기의 저항 종류입니다.
    private FishResistanceType _resistanceType;

    // 이번 작살 적중이 포획 체력 조건을 만족했는지 나타냅니다.
    private bool _captureAllowed;

    // 실제 이동, 공격 중단과 연출은 구독자가 처리합니다.
    public event Action<FishState> StateChanged;
    public event Action Damaged;
    public event Action<Vector3> CapturedMoveRequested;
    public event Action Collected;

    public float MaxHp => _maxHp;
    public float CurrentHp => _currentHp;
    public float Damage => _damage;
    public float MoveSpeed => _moveSpeed;
    public FishState State => _state;
    public bool IsCollected => _isCollected;

    public ItemData FilletData => _filletData;
    public int FilletAmount => _filletAmount;

    // 사망했지만 아직 획득되지 않은 개체만 상호작용할 수 있습니다.
    public bool CanInteract =>
        _isInitialized
        && isActiveAndEnabled
        && !_isCollected
        && _state == FishState.Dead
        && _currentHp <= 0f;

    // 살아 있으며 포획 상태이고 아직 획득되지 않은 개체입니다.
    public bool IsCaptured =>
        _isInitialized
        && isActiveAndEnabled
        && !_isCollected
        && _state == FishState.HarpoonAction

        && _captureAllowed;

    public GameObject _heroSensorCollider => throw new NotImplementedException();

    /// <summary>
    /// 원본 데이터로 런타임 능력치와 최초 상태를 설정합니다.
    /// </summary>
    public void Initialize(FishData data)
    {
        // 유효한 원본 데이터가 있어야 초기화할 수 있습니다.
        if (data == null)
        {
            return;
        }

        // 원본 능력치를 개체별 런타임 값으로 복사합니다.
        _maxHp = Mathf.Max(1f, data.MaxHp);

        // 물고기 데이터의 저항 종류를 저장하고 이전 포획 허용 여부를 초기화합니다.
        _resistanceType = data.ResistanceType;
        _captureAllowed = false;
        _currentHp = _maxHp;
        _damage = Mathf.Max(0f, data.Damage);
        _moveSpeed = Mathf.Max(0f, data.MoveSpeed);

        // 생성된 개체는 평상시 이동 상태로 시작합니다.
        _state = FishState.Moving;
        _stateBeforeCapture = FishState.Moving;
        _isCollected = false;
        _isInitialized = true;
    }

    /// <summary>
    /// 일반 행동 상태를 변경합니다. 포획과 사망은 전용 처리로 진입합니다.
    /// </summary>
    public void  ChangeState(FishState nextState)
    {
        // 사망·포획·획득 상태를 일반 행동 요청으로 덮어쓰지 않습니다.
        if (!_isInitialized
            || _isCollected
            || _state == FishState.Dead
            || _state == FishState.HarpoonAction)
        {
            return;
        }

        // 일반 행동 전환에서는 이동과 전투 상태만 허용합니다.
        if (nextState != FishState.Moving
            && nextState != FishState.Combat)
        {
            return;
        }

        // 상태 저장과 알림은 공통 메서드에 맡깁니다.
        SetState(nextState);
    }

    /// <summary>
    /// 작살 피해를 적용한 후 남은 체력과 저항 종류에 따른 결과를 반환합니다.
    /// 피해 적용을 포함하므로 같은 적중의 피해를 외부에서 중복 적용하면 안 됩니다.
    /// </summary>
    public FishHarpoonResult ReceiveHarpoonHit(float damage)
    {
        // 살아 있는 활성 물고기만 새로운 작살 적중을 처리합니다.
        if (!_isInitialized
            || !isActiveAndEnabled
            || _isCollected
            || _state == FishState.Dead
            || _state == FishState.HarpoonAction
            || _currentHp <= 0f)
        {
            return FishHarpoonResult.Unavailable;
        }

        // 음수나 잘못된 피해 값은 처리하지 않습니다.
        if (damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage))
        {
            return FishHarpoonResult.Unavailable;
        }

        // 이번 피해의 결과를 확인하기 전까지 포획을 허용하지 않습니다.
        _captureAllowed = false;

        // 연결이 해제되면 전투 상태로 복귀하도록 저장합니다.
        _stateBeforeCapture = FishState.Combat;

        // 체력 조건과 관계없이 일반 행동을 중단하고 작살 행동으로 전환합니다.
        SetState(FishState.HarpoonAction);

        // 기존 피해 처리로 체력 감소, 피격 효과와 사망을 반영합니다.
        // HarpoonAction 상태이므로 일반 피격 넉백은 실행되지 않습니다.
        TakeDamage(damage);

        // 이번 공격으로 죽었다면 살아 있는 포획을 진행하지 않습니다.
        if (_state == FishState.Dead)
        {
            _captureAllowed = false;
            return FishHarpoonResult.Unavailable;
        }

        // Inspector에 설정된 퍼센트를 실제 체력 기준으로 환산합니다.
        float captureHp =
            _maxHp * Mathf.Clamp(_captureHpPercent, 0f, 100f) / 100f;

        // 피해를 받은 후 남은 체력이 기준 이하인지 판단합니다.
        _captureAllowed = _currentHp <= captureHp;

        // 피해를 받은 뒤에도 체력이 높으면 끌고 오지 않도록 알립니다.
        if (!_captureAllowed)
        {
            return FishHarpoonResult.TooHealthy;
        }

        // 저항 액션의 실행과 결과 처리는 작살 담당자에게 맡깁니다.
        if (_resistanceType == FishResistanceType.Resistant)
        {
            return FishHarpoonResult.Resistant;
        }

        // 체력 조건을 만족한 무저항 물고기임을 알립니다.
        return FishHarpoonResult.NonResistant;
    }
    public void ChangeFishState()
    {
        
    }

    /// <summary>
    /// 피해를 적용하고 사망 또는 생존 피격 사실을 알립니다.
    /// </summary>
    public void TakeDamage(float damage)
    {
        // 유효하지 않은 피해와 사망·획득 이후의 피해는 무시합니다.
        if (!_isInitialized
            || _isCollected
            || _state == FishState.Dead
            || _currentHp <= 0f
            || damage <= 0f
            || float.IsNaN(damage)
            || float.IsInfinity(damage))
        {
            return;
        }

        // 피해를 현재 체력에 반영합니다.
        _currentHp = Mathf.Max(0f, _currentHp - damage);

        // 치명적인 피해에도 피격 연출을 재생합니다.
        DamageReceived?.Invoke();

        // 포획 중 생존 피격은 일반 추격이나 넉백을 발생시키지 않습니다.
        if (_state == FishState.HarpoonAction)
        {
            return;
        }

        // 포획 중이어도 체력이 0이 되면 사망 상태로 전환합니다.
        if (_currentHp <= 0f)
        {
            SetState(FishState.Dead);
            return;
        }

        

        // 살아남은 일반 개체의 반격과 넉백을 요청합니다.
        Damaged?.Invoke();
    }

    /// <summary>
    /// 피해 적용 후 생존 체력이 포획 기준 이하이면 포획을 시작합니다.
    /// </summary>
    public bool TryBeginCapture()
    {
        // 사망, 획득 또는 중복 포획 요청은 거절합니다.
        if (!_isInitialized
            || !isActiveAndEnabled
            || _isCollected
            || _state == FishState.Dead
            || _state == FishState.HarpoonAction
            || _currentHp <= 0f)
        {
            return false;
        }

        // Inspector의 비율을 실제 체력 값으로 환산합니다.
        float captureHp =
            _maxHp * Mathf.Clamp(_captureHpPercent, 0f, 100f) / 100f;

        // 경계값은 허용하고 기준 초과는 거절합니다.
        if (_currentHp > captureHp)
        {
            return false;
        }

        // 포획 취소 시 복귀할 상태를 저장합니다.
        _stateBeforeCapture = _state;

        // 기존 포획 경로도 체력 조건을 통과했다면 포획 대상으로 인정합니다.
        _captureAllowed = true;

        // Fish가 알림을 받아 자체 이동과 공격을 중단합니다.
        SetState(FishState.HarpoonAction);
        return true;
    }

    /// <summary>
    /// 포획 중 전달받은 월드 위치로 이동하도록 제어부에 요청합니다.
    /// </summary>
    public void MoveCaptured(Vector3 position)
    {
        // 포획이 끝났거나 획득된 개체는 이동시키지 않습니다.
        if (!IsCaptured)
        {
            return;
        }

        // 실제 Transform과 Rigidbody 조작은 FishMover가 수행합니다.
        CapturedMoveRequested?.Invoke(position);
    }

    /// <summary>
    /// 획득 없이 포획을 취소하고 현재 체력에 맞는 상태로 복귀합니다.
    /// </summary>
    public void CancelCapture()
    {
        // 이미 종료된 포획은 다시 처리하지 않습니다.
        if (!_isInitialized
            || _isCollected
            || _state != FishState.HarpoonAction)
        {
            return;
        }

        // 생존하지 못한 경우에는 일반 행동 대신 사망 상태로 전환합니다.
        if (_currentHp <= 0f)
        {
            SetState(FishState.Dead);
            return;
        }

        // 포획 직전의 전투 여부에 맞춰 행동을 복구합니다.
        FishState nextState = _stateBeforeCapture == FishState.Combat
            ? FishState.Combat
            : FishState.Moving;

        SetState(nextState);
    }

    /// <summary>
    /// 사망 또는 생존 포획 상태에서 획득을 한 번만 확정합니다.
    /// </summary>
    public bool TryCollect()
    {
        // 일반 생존 개체와 중복 획득 요청은 거절합니다.
        if (_isCollected || (!CanInteract && !IsCaptured))
        {
            return false;
        }

        // 이벤트에서 다시 호출해도 실패하도록 먼저 획득을 확정합니다.
        _isCollected = true;

        // 실제 오브젝트 제거는 Fish가 처리하고 아이템 지급은 Hero가 처리합니다.
        Collected?.Invoke();
        return true;
    }

    /// <summary>
    /// 내부에서 확정한 상태를 저장하고 변경 사실을 알립니다.
    /// </summary>
    private void SetState(FishState nextState)
    {
        // 같은 상태의 중복 알림을 방지합니다.
        if (_state == nextState)
        {
            return;
        }

        // 구독자가 최신 상태를 읽을 수 있도록 먼저 저장합니다.
        _state = nextState;
        StateChanged?.Invoke(_state);
    }

    
}