using UnityEngine;

/// <summary>
/// 손님 좌석이 위치한 구역을 구분합니다.
/// </summary>
public enum ChairArea
{
    Back,   // 뒤쪽 구역
    Front   // 앞쪽 구역
}

public enum ChairState
{
    Empty,     // 사용하지 않는 좌석이며 예약 여부를 추가로 검사합니다.
    Seated,    // 손님이 앉아 주문하거나 식사 중인 좌석입니다.
    Uncleaned  // 식사가 끝나 청소가 필요한 좌석입니다.
}

public class Chair : MonoBehaviour, IInteractable, ISushiHoldInteraction
{
    [Header("좌석 구역")]
    [SerializeField] ChairArea _area = ChairArea.Back;

    [Header("도착 위치/앉는 위치")]
    [SerializeField] Transform _arrivePos;
    [SerializeField] Transform _seatPos;

    [Header("자리별 서빙/청소 영역 (월드 크기)")]
    [SerializeField] Transform _servingPoint;
    [SerializeField] Vector3 _servingAreaSize = new Vector3(1.2f, 1.5f, 6f);

    [Header("착석 시 손님의 월드 Y축 각도")]
    [SerializeField] float _seatedYaw = 0f;
    // 현재 좌석 상태이며 인스펙터에서 초기 청소 상태를 설정할 수 있습니다.
    [SerializeField] ChairState _state;
    // 이 좌석을 예약하거나 사용 중인 손님입니다.
    GuestNPC _guest;
    // 손님 퇴장 후에도 남는 고정된 청소 감지 영역입니다.
    [SerializeField] Collider _cleaningArea;
    // 손님이 실제 제공받은 메뉴이며 퇴장 후에도 잔반 종류를 유지합니다.
    [SerializeField] Sushi _servedMenu;
    // 기본 쟁반 위의 음식, 음료 및 잔반 표시를 담당합니다.
    [SerializeField] SushiVisualGroup _tableVisual;
    // 이 좌석을 청소하기 위해 연속 상호작용해야 하는 시간입니다.
    [SerializeField, Min(0.1f)] float _cleaningTime = 2f;
    // 한 작업자만 진행할 수 있는 청소 세션입니다.
    [SerializeField] SushiHoldSession _cleaningSession = new();
    // 플레이어가 청소를 완료하면 한 번 재생하는 좌석별 효과입니다.
    [SerializeField] ParticleSystem _cleaningCompleteParticles;

    public ChairArea Area => _area;
    public Transform ArrivePos => _arrivePos;
    public Transform SeatPos => _seatPos;
    public Transform ServingPoint => _servingPoint;
    public float SeatedYaw => _seatedYaw;
    public bool IsValid => _arrivePos != null && _seatPos != null && _servingPoint != null;
    public ChairState State => _state;
    public bool IsAvailable => IsValid && _state == ChairState.Empty && _guest == null;
    public Collider InteractionArea => _cleaningArea;
    public float CleaningTime => _cleaningTime;
    public float CleaningElapsed => _cleaningSession.Elapsed;
    public Sushi ServedMenu => _servedMenu;

    /// <summary>
    /// 식사가 끝나 잔반 청소가 필요한 상태인지 반환합니다.
    /// View에서 잔반 처리 아이콘의 표시 여부를 판단할 때 사용합니다.
    /// </summary>
    public bool IsUncleaned => _state == ChairState.Uncleaned;

    /// <summary>초기 좌석 상태에 맞춰 쓰레기와 청소 영역을 설정합니다.</summary>
    void Awake()
    {
        // 손님이 없는 초기 좌석도 자신의 기준점으로 청소 영역을 배치합니다.
        if (!TryConfigureInteractionArea(_cleaningArea as BoxCollider))
            Debug.LogError($"[좌석 영역 설정 실패] {name}: Serving Point와 청소 BoxCollider를 확인하세요.", this);
        // 인스펙터에 지정한 초기 상태를 유지합니다.
        RefreshVisual();
    }

    /// <summary>이 좌석의 기준점과 월드 크기를 손님 서빙 또는 좌석 청소 콜라이더에 적용합니다.</summary>
    public bool TryConfigureInteractionArea(BoxCollider area)
    {
        // 배치에 필요한 참조와 양수 크기가 모두 설정되어 있어야 합니다.
        if (area == null || _servingPoint == null || _servingAreaSize.x <= 0f ||
            _servingAreaSize.y <= 0f || _servingAreaSize.z <= 0f) return false;

        // 손님이 바라보는 방향과 관계없이 통로의 월드 축에 맞춥니다.
        area.transform.rotation = Quaternion.identity;
        Vector3 scale = area.transform.lossyScale;
        if (Mathf.Abs(scale.x) <= Mathf.Epsilon || Mathf.Abs(scale.y) <= Mathf.Epsilon ||
            Mathf.Abs(scale.z) <= Mathf.Epsilon) return false;

        // 부모 크기를 보정해 서빙 영역과 청소 영역이 동일한 공간을 사용하게 합니다.
        area.center = area.transform.InverseTransformPoint(_servingPoint.position);
        area.size = new Vector3(_servingAreaSize.x / Mathf.Abs(scale.x),
            _servingAreaSize.y / Mathf.Abs(scale.y), _servingAreaSize.z / Mathf.Abs(scale.z));
        area.isTrigger = true;
        return true;
    }

    /// <summary>빈 좌석을 이동 중인 손님에게 중복 없이 예약합니다.</summary>
    public bool TryReserve(GuestNPC guest)
    {
        // 실제 착석 전에도 다른 손님이 같은 좌석을 선택하지 못하게 합니다.
        if (guest == null || !IsAvailable) return false;
        _guest = guest;
        return true;
    }

    /// <summary>좌석을 예약한 손님의 실제 착석을 반영합니다.</summary>
    public bool TrySeat(GuestNPC guest)
    {
        // 다른 손님이나 더러운 좌석의 착석 요청은 거부합니다.
        if (guest == null || _guest != guest || _state != ChairState.Empty) return false;
        _state = ChairState.Seated;
        RefreshVisual();
        return true;
    }

    /// <summary>식사를 끝낸 손님이 사용한 자리에 쓰레기를 남깁니다.</summary>
    public void MarkUncleaned(GuestNPC guest)
    {
        // 해당 좌석을 실제로 사용한 손님만 상태를 변경합니다.
        if (_guest != guest || _state != ChairState.Seated) return;
        _state = ChairState.Uncleaned;
        RefreshVisual();
    }

    /// <summary>예약자를 반환하되 식사 후의 더러운 상태는 유지합니다.</summary>
    public void Release(GuestNPC guest)
    {
        // 다른 예약자의 좌석은 반환하지 않습니다.
        if (_guest != guest) return;
        _guest = null;
        // 음식을 받지 못하고 떠난 손님은 쓰레기를 남기지 않습니다.
        if (_state == ChairState.Seated)
        {
            _state = ChairState.Empty;
            _servedMenu = null;
        }
        RefreshVisual();
    }

    /// <summary>청소 가능한 좌석 중 가장 가까운 스시 작업 대상일 때 플레이어 상호작용을 허용합니다.</summary>
    public bool CanInteract(Hero interactor)
    {
        // 빈손인 지상 플레이어만 청소 대상으로 선택할 수 있습니다.
        if (!(interactor is GroundHero hero) || !hero.isActiveAndEnabled) return false;
        if (!CanHoldInteract(hero.SushiCarrier)) return false;

        // 음식 및 음료 제공과 같은 선택기를 사용해 가장 가까운 작업을 고릅니다.
        SushiInteractionSelector selector = hero.GetComponent<SushiInteractionSelector>();
        // 선택기를 사용하지 않는 기존 씬은 원래 청소 조건을 유지합니다.
        return selector == null || selector.IsNearestTarget(this);
    }

    /// <summary>단발 입력은 청소를 완료하지 않고 연속 처리는 SushiInteractor에 맡깁니다.</summary>
    public void Interact(Hero interactor)
    {
        // 기존 GroundHero의 한 번 누르기로 청소 시간을 건너뛰지 않습니다.
        if (!CanInteract(interactor)) return;
    }

    /// <summary>현재 작업자가 빈손으로 이 좌석을 청소할 수 있는지 검사합니다.</summary>
    public bool CanHoldInteract(SushiCarrier provider)
    {
        // 직원의 자동 청소는 요청 범위가 아니므로 플레이어만 허용합니다.
        return isActiveAndEnabled && _state == ChairState.Uncleaned && _guest == null &&
            _cleaningArea != null && _cleaningArea.enabled && provider != null &&
            provider.CurrentSushi == null && provider.GetComponent<GroundHero>() != null &&
            _cleaningSession.IsAvailableFor(provider);
    }

    /// <summary>실제 영역에서 청소 시간을 연속으로 채우면 좌석을 비웁니다.</summary>
    public bool TickHoldInteract(SushiCarrier provider, Collider sensor, float deltaTime)
    {
        // 거리 이탈이나 상태 변경은 현재 작업자의 진행을 취소합니다.
        if (!CanHoldInteract(provider) || !SushiInteractor.IsTouching(sensor, _cleaningArea))
        {
            CancelHoldInteract(provider);
            return false;
        }
        if (!_cleaningSession.Tick(provider, deltaTime, _cleaningTime)) return false;
        // Empty 변경과 예약 가능 상태는 같은 시점에 반영됩니다.
        _state = ChairState.Empty;
        _servedMenu = null;
        _cleaningSession.Reset();
        RefreshVisual();
        GameSfx.PlayAt(GameSoundId.SeatCleaned, transform.position);
        // 실제 청소 완료로 Empty가 된 순간에만 별빛을 재생합니다.
        PlayCleaningCompleteEffect();
        return true;
    }

    /// <summary>청소 완료 별빛을 자식 파티클까지 처음부터 한 번 재생합니다.</summary>
    void PlayCleaningCompleteEffect()
    {
        // 효과가 없는 좌석은 기존 청소 동작만 수행합니다.
        if (_cleaningCompleteParticles == null) return;

        // 이전 재생의 입자를 제거하여 재사용 시 효과가 겹치지 않게 합니다.
        _cleaningCompleteParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 좌석별 효과를 활성화하고 큰 별과 작은 별을 함께 재생합니다.
        _cleaningCompleteParticles.gameObject.SetActive(true);
        _cleaningCompleteParticles.Play(true);
    }

    /// <summary>해당 작업자의 미완료 청소를 취소합니다.</summary>
    public void CancelHoldInteract(SushiCarrier provider)
    {
        // 다른 작업자의 진행 시간은 변경하지 않습니다.
        _cleaningSession.Cancel(provider);
    }

    /// <summary>정상 제공된 메뉴를 좌석에 보관하고 식사 중 표시를 시작합니다.</summary>
    public void ShowServedMeal(GuestNPC guest, Sushi menu)
    {
        // 예약자가 아닌 손님이나 착석하지 않은 좌석의 표시를 바꾸지 않습니다.
        if (guest == null || _guest != guest || _state != ChairState.Seated || menu == null)
            return;
        _servedMenu = menu;
        RefreshVisual();
    }

    /// <summary>좌석 상태와 제공 메뉴에 맞춰 내용물과 청소 영역을 갱신합니다.</summary>
    void RefreshVisual()
    {
        // 손님이 좌석을 반환한 후에만 청소할 수 있습니다.
        bool uncleaned = _state == ChairState.Uncleaned;
        if (_cleaningArea != null)
            _cleaningArea.enabled = uncleaned && _guest == null;
        if (_tableVisual == null) return;

        // 좌석이 보관한 메뉴를 사용하므로 손님이 파괴되어도 잔반 종류가 유지됩니다.
        if (uncleaned)
            _tableVisual.Show(_servedMenu, SushiVisualPhase.Remaining);
        // 착석만 했거나 아직 서빙받지 않은 손님에게는 내용물을 표시하지 않습니다.
        else if (_state == ChairState.Seated && _servedMenu != null)
            _tableVisual.Show(_servedMenu, SushiVisualPhase.Served);
        else
            _tableVisual.Show(null, SushiVisualPhase.Empty);
    }

    /// <summary>좌석이 비활성화되면 진행 중인 청소를 취소합니다.</summary>
    void OnDisable()
    {
        // 좌석이 비활성화되면 남아 있는 청소 완료 입자를 제거합니다.
        if (_cleaningCompleteParticles != null)
            _cleaningCompleteParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 다시 활성화했을 때 이전의 청소 시간을 이어받지 않습니다.
        _cleaningSession.Reset();
    }
}
