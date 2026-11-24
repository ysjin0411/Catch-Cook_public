using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>UI에서 읽을 수 있는 조리 주문 정보를 정의합니다.</summary>
public interface ICookingOrderInfo
{
    /// <summary>주문한 음식의 원본 데이터를 반환합니다.</summary>
    Sushi Sushi { get; }

    /// <summary>주문한 음식의 아이콘을 반환합니다.</summary>
    Sprite Icon { get; }

    /// <summary>주문 접수 당시 강화 효과가 반영된 총 조리시간입니다.</summary>
    float TotalCookTime { get; }

    /// <summary>현재 남은 조리시간입니다.</summary>
    float RemainingCookTime { get; }

    /// <summary>조리 진행률을 0부터 1 사이로 반환합니다.</summary>
    float CookingProgress { get; }

    /// <summary>조리가 완료되었는지 반환합니다.</summary>
    bool IsCompleted { get; }

    /// <summary>현재 실제로 수령 가능한 주문인지 반환합니다.</summary>
    bool IsReceivable { get; }
}

public enum CookerState
{
    Idle,   // 대기중
    Cooking // 요리중
}

public class CookerNPC : NPC
{
    [Header("이번 영업 실제 조리 완료 실적")]
    [SerializeField] int _completedCookingCount;
    public int CompletedCookingCount => _completedCookingCount;

    /// <summary>새 영업에서 사용할 해당 직원의 조리 완료 횟수를 초기화합니다.</summary>
    public void ResetBusinessStatistics()
    {
        // 조리 능력치와 강화 단계는 유지하고 완료 실적만 초기화합니다.
        _completedCookingCount = 0;
    }

    // 이 직원의 고유 정보, 기본 조리 배율과 강화 데이터를 연결합니다.
    [SerializeField] CookerStaffData _staffData;
    // 의상과 강화 오오라의 적용을 전담합니다.
    [SerializeField] StaffAppearanceView _appearanceView;
    // 새 직원 데이터 연결 전까지 기존 씬과 프리팹의 직렬화된 설정을 유지합니다.
    [SerializeField, HideInInspector] CookerStaffUpgradeData _upgradeData;
    // 서빙 직원이 접촉하여 요리를 수령할 기존 영역입니다.
    [SerializeField] Collider _interactableCollider;
    // 새 주문의 원본 조리시간에 곱할 강화 배율입니다.
    float _cookTimeMultiplier = 1f;
    // 현재 NPC에 적용된 강화 단계이며 적용 전에는 0입니다.
    [SerializeField] int _upgradeLevel;
    /// <summary>현재 이 요리 직원에게 실제 적용된 강화 레벨입니다.</summary>
    public int UpgradeLevel => _upgradeLevel;
    // 이 쿠커의 주문을 관리하는 스시 매니저입니다.
    SushiManager _manager;

    /// <summary>영업 시작 전에 조리 결과를 전달할 주문 관리자를 연결합니다.</summary>
    public void BindManager(SushiManager manager)
    {
        // 이미 조리 중일 때 주문 관리자 연결을 바꾸지 않습니다.
        if (_cookingOrders.Count > 0 || _completedOrders.Count > 0) return;
        // 메뉴 확정 시 재료를 차감하므로 조리 결과 전달 대상만 보관합니다.
        _manager = manager;
    }

    /// <summary>이 조리 직원의 고유 정보와 기본 능력치입니다.</summary>
    public CookerStaffData StaffData => _staffData;
    /// <summary>직원 데이터의 강화 설정이며 미연결된 기존 프리팹은 이전 설정을 사용합니다.</summary>
    public CookerStaffUpgradeData UpgradeData => _staffData != null ? _staffData.UpgradeData : _upgradeData;
    /// <summary>직원 데이터에 지정한 플레이어 해금 레벨입니다.</summary>
    public int UnlockPlayerLevel => _staffData != null ? _staffData.UnlockPlayerLevel :
        (_upgradeData != null ? _upgradeData.UnlockPlayerLevel : int.MaxValue);
    public Collider InteractionArea => _interactableCollider;
    public bool HasReceivableOrder => FindReceivableOrderIndex() >= 0;
    public float CookTimeMultiplier => _cookTimeMultiplier;

    /// <summary>직원 고유 배율을 반영하며 데이터 미연결 상태에서는 기존 강화 배율을 조회합니다.</summary>
    public bool TryGetCookTimeMultiplier(int level, out float multiplier)
    {
        // 새 직원 데이터가 있으면 그 데이터의 검증과 계산 결과만 사용합니다.
        if (_staffData != null) return _staffData.TryGetCookTimeMultiplier(level, out multiplier);
        // 아직 새 데이터로 전환하지 않은 기존 프리팹의 조리 배율을 유지합니다.
        multiplier = 1f;
        return _upgradeData != null && _upgradeData.TryGetCookTimeMultiplier(level, out multiplier);
    }

    /// <summary>
    /// 요리 직원의 강화 단계에 맞춰 새 주문의 조리시간 배율을 설정합니다.
    /// </summary>
    public bool ApplyUpgradeLevel(int level)
    {
        // 직원 고유 능력치와 강화 효과를 함께 검증합니다.
        if (!TryGetCookTimeMultiplier(level, out float multiplier)) return false;
        // 원본 Sushi 에셋은 변경하지 않고 주문에 적용할 배율을 보관합니다.
        _cookTimeMultiplier = multiplier;
        // 유효한 능력치 적용에 성공한 경우에만 조회용 레벨을 갱신합니다.
        _upgradeLevel = level;
        // 현재 강화 단계의 외형을 표시 컴포넌트에 전달합니다.
        if (_appearanceView != null)
            _appearanceView.Apply(UpgradeData != null ? UpgradeData.GetAppearance(level) : null);
        return true;
    }

    [Serializable]
    public class CookingOrder : ICookingOrderInfo
    {
        [SerializeField] GuestNPC _guest;
        [SerializeField] Sushi _sushi;

        // 주문을 접수할 때 확정된 총 조리시간입니다.
        [SerializeField] float _totalCookTime;
        [SerializeField] float _remainingCookTime;

        public GuestNPC Guest => _guest;
        public Sushi Sushi => _sushi;
        public float RemainingCookTime => _remainingCookTime;
        public bool IsCompleted => _remainingCookTime <= 0f;

        /// <summary>주문 접수 당시 강화 효과를 반영한 총 조리시간을 반환합니다.</summary>
        public float TotalCookTime => _totalCookTime;

        /// <summary>음식 데이터의 아이콘을 반환하며, 데이터가 없으면 null입니다.</summary>
        public Sprite Icon => _sushi != null ? _sushi.Icon : null;

        /// <summary>총 조리시간과 남은 시간으로 실제 조리 진행률을 반환합니다.</summary>
        public float CookingProgress
        {
            get
            {
                // 조리시간이 0인 주문은 즉시 완료된 것으로 표시합니다.
                if (_totalCookTime <= 0f) return 1f;

                // 남은 시간의 비율을 완료된 시간의 비율로 변환합니다.
                return Mathf.Clamp01(1f - _remainingCookTime / _totalCookTime);
            }
        }

        /// <summary>완료된 음식이 현재 손님의 주문과 일치하여 수령 가능한지 반환합니다.</summary>
        public bool IsReceivable
        {
            get
            {
                // 기존 쿠커의 주문 유효성 검사와 수령 조건을 사용합니다.
                return IsCompleted &&
                       IsValidOrder(this) &&
                       _sushi.Type == MenuType.Food &&
                       _sushi == _guest.SelectedSushi;
            }
        }

        /// <summary>
        /// 메뉴와 조리 진행도를 유지한 채 주문을 기다리는 손님을 변경합니다.
        /// </summary>
        internal void ReassignGuest(GuestNPC guest)
        {
            // 같은 메뉴 간 서빙으로 바뀐 남은 주문의 담당 손님을 저장합니다.
            _guest = guest;
        }

        /// <summary>
        /// 주문 데이터와 강화 효과가 반영된 총 조리시간 및 남은 시간을 초기화합니다.
        /// </summary>
        public CookingOrder(GuestNPC guest, Sushi sushi, float cookTime)
        {
            // 주문자와 원본 스시 데이터를 보관합니다.
            _guest = guest;
            _sushi = sushi;

            // 주문 접수 시 전달된 실제 조리시간을 저장합니다.
            _totalCookTime = Mathf.Max(0f, cookTime);

            // 새 주문은 전체 조리시간만큼 남은 상태에서 시작합니다.
            _remainingCookTime = _totalCookTime;
        }

        /// <summary>
        /// 이 주문의 남은 요리시간을 경과 시간만큼 감소시킵니다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            // 남은 시간이 음수가 되지 않도록 제한합니다.
            _remainingCookTime = Mathf.Max(
                0f,
                _remainingCookTime - Mathf.Max(0f, deltaTime));
        }
    }

    [Header("요리사 상태")]
    [SerializeField] CookerState _state;

    [Header("요리 애니메이션")]
    [SerializeField] Animator _animator;
    // 초당 변경할 레이어 Weight이며 5이면 완전 전환에 약 0.2초가 걸립니다.
    [SerializeField, Min(0.01f)] float _animationBlendSpeed = 5f;

    // 상체 조리 동작을 재생하는 Animator 레이어 이름입니다.
    const string UpperBodyLayerName = "UpperBody";
    // 상체 레이어를 찾지 못한 경우 -1을 유지합니다.
    int _upperBodyLayerIndex = -1;

    [Header("현재 조리 중인 주문")]
    [SerializeField] List<CookingOrder> _cookingOrders = new();

    [Header("조리 완료 - 수령 대기")]
    [SerializeField] List<CookingOrder> _completedOrders = new();

    readonly List<CookingOrder> _completionBuffer = new();

    public CookerState State => _state;
    public int CookingOrderCount => _cookingOrders.Count;
    public int CompletedOrderCount => _completedOrders.Count;

    // 읽기 전용 목록을 한 번 생성한 뒤 재사용합니다.
    IReadOnlyList<ICookingOrderInfo> _cookingOrdersView;
    IReadOnlyList<ICookingOrderInfo> _completedOrdersView;

    /// <summary>현재 조리 중인 주문들을 읽기 전용으로 제공합니다.</summary>
    public IReadOnlyList<ICookingOrderInfo> CookingOrders
    {
        get
        {
            // 원본 목록의 변화를 반영하면서 외부의 추가와 삭제를 막습니다.
            if (_cookingOrdersView == null)
                _cookingOrdersView = _cookingOrders.AsReadOnly();

            // UI에는 조회용 인터페이스로 주문 정보를 제공합니다.
            return _cookingOrdersView;
        }
    }

    /// <summary>조리가 끝나 수령 대기 목록에 있는 주문들을 읽기 전용으로 제공합니다.</summary>
    public IReadOnlyList<ICookingOrderInfo> CompletedOrders
    {
        get
        {
            // 매번 새 목록을 만들지 않고 읽기 전용 목록을 재사용합니다.
            if (_completedOrdersView == null)
                _completedOrdersView = _completedOrders.AsReadOnly();

            // UI에서는 IsReceivable로 현재 수령 가능한 주문을 구분합니다.
            return _completedOrdersView;
        }
    }

    /// <summary>
    /// Animator를 준비하고 실제 주문 목록에 맞춰 초기 상태와 상체 동작을 설정합니다.
    /// </summary>
    void Awake()
    {
        // 지정하지 않았다면 자식을 포함해 Animator를 찾습니다.
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        // 인스펙터의 초기 상태보다 실제 주문 목록을 우선합니다.
        RefreshState();

        // Animator가 없으면 연결 누락을 알립니다.
        if (_animator == null)
        {
            // Inspector에서 Animator를 연결하도록 안내합니다.
            Debug.LogWarning("CookerNPC에 Animator가 연결되어 있지 않습니다.", this);
            return;
        }

        // 상체 레이어의 인덱스를 한 번 찾아 보관합니다.
        _upperBodyLayerIndex = _animator.GetLayerIndex(UpperBodyLayerName);

        // 상체 레이어가 없으면 이름을 확인하도록 알립니다.
        if (_upperBodyLayerIndex < 0)
        {
            // Animator에 UpperBody 레이어가 있어야 조리 동작을 적용할 수 있습니다.
            Debug.LogWarning("Cooker Animator에서 UpperBody 레이어를 찾지 못했습니다.", this);
            return;
        }

        // 첫 화면부터 실제 조리 상태에 맞는 상체 반영 비율을 결정합니다.
        float initialWeight = _state == CookerState.Cooking ? 1f : 0f;

        // 초기 상태에는 블렌딩을 기다리지 않고 바로 맞춥니다.
        _animator.SetLayerWeight(_upperBodyLayerIndex, initialWeight);
    }

    /// <summary>
    /// 요리 상태에 따라 상체 Cooking 동작의 반영 비율을 부드럽게 변경합니다.
    /// </summary>
    void UpdateCookingAnimation()
    {
        // Animator와 상체 레이어가 준비된 경우에만 처리합니다.
        if (_animator == null || _upperBodyLayerIndex < 0) return;

        // 조리 중이면 Cooking을 적용하고 Idle이면 상체 덮어쓰기를 해제합니다.
        float targetWeight = _state == CookerState.Cooking ? 1f : 0f;

        // 현재 상체 레이어의 반영 비율을 가져옵니다.
        float currentWeight = _animator.GetLayerWeight(_upperBodyLayerIndex);

        // 프레임 시간에 비례해 목표 비율까지 서서히 변경합니다.
        float nextWeight = Mathf.MoveTowards(
            currentWeight,
            targetWeight,
            Mathf.Max(0.01f, _animationBlendSpeed) * Time.deltaTime);

        // 변경한 비율을 상체 레이어에 적용합니다.
        _animator.SetLayerWeight(_upperBodyLayerIndex, nextWeight);
    }

    /// <summary>
    /// 전달받은 주문을 즉시 병렬 조리 대상으로 추가합니다.
    /// </summary>
    public bool ReceiveOrder(GuestNPC guest, Sushi sushi)
    {
        // 주문자와 스시 데이터가 유효한지 확인합니다.
        if (guest == null || sushi == null) return false;
        // 음료는 손님 앞에서 직접 제공하므로 조리 목록에 넣지 않습니다.
        if (sushi.Type != MenuType.Food) return false;

        // 메뉴 확정과 손님 배정을 거치지 않은 음식이 무료로 조리되는 것을 막습니다.
        if (_manager == null || !_manager.TryGetGuestMenu(guest, out Sushi allocatedMenu) ||
            allocatedMenu != sushi) return false;

        // 비활성화된 요리사가 주문을 받아 시간이 멈추는 원인을 알립니다.
        if (!isActiveAndEnabled)
        {
            // 씬의 요리사 활성 상태를 확인하도록 안내합니다.
            Debug.LogWarning($"[요리 접수 실패] {name}: 요리사가 비활성화되어 있습니다.", this);
            return false;
        }

        // 음식 대기 중인 손님이 실제로 선택한 스시인지 확인합니다.
        if (!guest.IsWaiting || guest.SelectedSushi != sushi)
        {
            // 잘못된 시점이나 데이터로 주문한 경우를 알립니다.
            Debug.LogWarning($"[요리 접수 실패] {guest.name}: 대기 상태 또는 선택한 스시가 다릅니다.", this);
            return false;
        }

        // 강화 효과를 반영한 시간을 주문마다 독립적으로 보관합니다.
        float cookTime = sushi.CookTime * _cookTimeMultiplier;
        // 동일 손님의 중복 이벤트가 재료와 조리를 중복 생성하지 못하게 합니다.
        foreach (CookingOrder existing in _cookingOrders)
            if (existing.Guest == guest) return false;
        foreach (CookingOrder existing in _completedOrders)
            if (existing.Guest == guest) return false;
        CookingOrder order = new(guest, sushi, cookTime);
        _cookingOrders.Add(order);

        // 새 주문 접수 직후 요리 중 상태로 갱신합니다.
        RefreshState();

        // 요리 접수와 시간을 런타임에서 확인하도록 알립니다.
        Debug.Log($"[요리 접수] {guest.name}: {sushi.Name} (ID {sushi.Id}), 조리 {cookTime}초", this);
        return true;
    }

    /// <summary>
    /// 모든 주문의 시간을 동시에 감소시키고 완료된 주문을 수령 대기로 이동합니다.
    /// </summary>
    void Update()
    {
        // 이번 프레임에 완료된 주문을 모을 임시 목록을 비웁니다.
        _completionBuffer.Clear();

        // 모든 주문에 같은 경과 시간을 적용합니다.
        float deltaTime = Time.deltaTime;

        // 취소 주문을 안전하게 제거하도록 뒤에서부터 확인합니다.
        for (int i = _cookingOrders.Count - 1; i >= 0; i--)
        {
            // 현재 주문을 가져옵니다.
            CookingOrder order = _cookingOrders[i];

            // 손님이 퇴장했거나 데이터가 제거된 주문은 취소합니다.
            if (!IsValidOrder(order))
            {
                // 유효하지 않은 주문만 조리 목록에서 제거합니다.
                _cookingOrders.RemoveAt(i);
                continue;
            }

            // 다른 주문과 독립적으로 남은 시간을 감소시킵니다.
            order.Tick(deltaTime);

            // 완료 이벤트가 원본 목록을 변경해도 안전하도록 처리를 미룹니다.
            if (order.IsCompleted) _completionBuffer.Add(order);
        }

        // 같은 프레임에 완료된 주문은 먼저 접수된 순서부터 처리합니다.
        for (int i = _completionBuffer.Count - 1; i >= 0; i--)
        {
            // 자동 완료와 테스트 완료에 동일한 함수를 사용합니다.
            CompleteCooking(_completionBuffer[i]);
        }

        // 실제 조리 목록을 기준으로 상태를 갱신합니다.
        RefreshState();

        // 갱신된 상태에 따라 상체 Cooking 동작을 부드럽게 적용합니다.
        UpdateCookingAnimation();
    }

    /// <summary>
    /// 손님이 여전히 해당 주문을 기다리는지 확인합니다.
    /// </summary>
    static bool IsValidOrder(CookingOrder order)
    {
        // 데이터와 주문자가 남아 있고 아직 음식 대기 중이어야 합니다.
        return order != null &&
            order.Guest != null &&
            order.Sushi != null &&
            order.Guest.IsWaiting;
    }

    /// <summary>
    /// 조리가 끝난 주문을 수령 대기 목록으로 이동합니다.
    /// </summary>
    void CompleteCooking(CookingOrder order)
    {
        // 이미 완료되거나 취소된 조리는 중복 완료하지 않습니다.
        if (order == null || !_cookingOrders.Contains(order)) return;
        if (!IsValidOrder(order))
        {
            // 메뉴 확정 시 차감한 재료는 취소해도 반환하지 않습니다.
            _cookingOrders.Remove(order);
            RefreshState();
            return;
        }
        // 재료 추가 차감 없이 조리 완료 주문을 수령 대기로 옮깁니다.
        _cookingOrders.Remove(order);
        order.Tick(order.RemainingCookTime);
        _completedOrders.Add(order);
        // 서빙 여부나 이후 폐기 여부와 관계없이 실제 완성한 음식만 집계합니다.
        _completedCookingCount++;
        if (_manager != null) _manager.NotifyCookingCompleted(order.Guest);
        RefreshState();
        Debug.Log($"[요리 완료] {order.Guest.name}: {order.Sushi.Name}", this);
    }

    /// <summary>
    /// 가장 먼저 접수된 유효한 조리 주문 하나를 즉시 완료합니다.
    /// </summary>
    [ContextMenu("테스트/요리 완료")]
    void TestCompleteCooking()
    {
        // 실행 중인 요리사에서만 테스트를 허용합니다.
        if (!Application.isPlaying || !isActiveAndEnabled) return;

        // 먼저 접수된 주문부터 확인합니다.
        while (_cookingOrders.Count > 0)
        {
            // 가장 먼저 남아 있는 주문을 가져옵니다.
            CookingOrder order = _cookingOrders[0];

            // 이미 퇴장한 손님의 주문은 정리합니다.
            if (!IsValidOrder(order))
            {
                // 무효 주문을 제외하고 다음 주문을 확인합니다.
                _cookingOrders.RemoveAt(0);
                continue;
            }

            // 주문 하나만 완료하고 테스트를 종료합니다.
            CompleteCooking(order);
            return;
        }

        // 유효한 조리 주문이 없는 상태로 갱신합니다.
        RefreshState();

        // 완료할 주문이 없는 상태를 알립니다.
        Debug.LogWarning("조리 중인 주문이 없습니다.", this);
    }

    /// <summary>
    /// 해당 손님의 완성된 주문 한 건을 수령 대기 목록에서 꺼냅니다.
    /// </summary>
    public bool TryTakeCompletedOrder(GuestNPC guest, out Sushi sushi)
    {
        // 수령 실패 시 반환할 기본값을 설정합니다.
        sushi = null;

        // 아직 음식 대기 중인 손님인지 확인합니다.
        if (guest == null || !guest.IsWaiting) return false;

        // 먼저 보관된 완성 주문부터 확인합니다.
        for (int i = 0; i < _completedOrders.Count; i++)
        {
            // 현재 완성 주문을 가져옵니다.
            CookingOrder order = _completedOrders[i];

            // 해당 손님의 선택 메뉴와 일치하는 주문만 찾습니다.
            if (!IsValidOrder(order) ||
                order.Guest != guest ||
                order.Sushi != guest.SelectedSushi) continue;

            // 호출자가 전달할 스시 데이터를 보관합니다.
            sushi = order.Sushi;

            // 같은 완성 요리를 다시 수령하지 못하도록 제거합니다.
            _completedOrders.RemoveAt(i);
            return true;
        }

        // 해당 손님의 완성된 주문이 없다면 실패합니다.
        return false;
    }

    /// <summary>
    /// 해당 손님의 조리 중인 주문과 수령 대기 주문을 모두 취소합니다.
    /// </summary>
    public void CancelOrders(GuestNPC guest)
    {
        // 파괴 중인 Unity 객체도 취소할 수 있도록 실제 참조만 확인합니다.
        if (ReferenceEquals(guest, null)) return;

        // 제거 중 인덱스가 밀리지 않도록 뒤에서부터 확인합니다.
        for (int i = _cookingOrders.Count - 1; i >= 0; i--)
        {
            // 해당 손님에게 접수된 주문인지 확인합니다.
            if (_cookingOrders[i] == null ||
                !ReferenceEquals(_cookingOrders[i].Guest, guest)) continue;

            // 다른 손님의 조리는 유지하고 해당 주문만 제거합니다.
            _cookingOrders.RemoveAt(i);
        }

        // 이미 완성되었더라도 해당 손님의 수령 대기 주문을 정리합니다.
        _completedOrders.RemoveAll(
            order => order == null || ReferenceEquals(order.Guest, guest));

        // 취소 후 남아 있는 조리를 기준으로 상태를 갱신합니다.
        RefreshState();
    }

    /// <summary>
    /// 남아 있는 조리 주문 수에 따라 요리사 상태를 갱신합니다.
    /// </summary>
    void RefreshState()
    {
        // 수령 대기 요리만 남았다면 대기중이며 실제 조리 주문이 있으면 요리중입니다.
        _state = _cookingOrders.Count > 0 ? CookerState.Cooking : CookerState.Idle;
    }

    /// <summary>
    /// 빈손인 지상 히어로가 수령할 완성 요리가 있을 때만
    /// 요리사와 상호작용할 수 있도록 합니다.
    /// </summary>
    public override bool CanInteract(Hero interactor)
    {
        // 요리사와 히어로의 기본 활성 상태를 확인합니다.
        if (!base.CanInteract(interactor)) return false;

        // 지상 히어로도 직원과 같은 수령 조건을 사용합니다.
        if (!(interactor is GroundHero groundHero)) return false;
        return CanReceiveBy(groundHero.SushiCarrier);
    }

    /// <summary>
    /// 플레이어 또는 직원의 운반 기능으로 완성 요리를 수령할 수 있는지 검사합니다.
    /// </summary>
    public bool CanReceiveBy(SushiCarrier carrier)
    {
        // 요리사와 운반자가 모두 활성 상태여야 합니다.
        if (!isActiveAndEnabled || carrier == null || !carrier.isActiveAndEnabled)
            return false;

        // 이미 요리를 들고 있다면 추가 수령을 막습니다.
        if (carrier.CurrentSushi != null) return false;

        // 실제로 수령 가능한 완성 요리가 있는지 확인합니다.
        return FindReceivableOrderIndex() >= 0;
    }

    /// <summary>
    /// 완성 요리 한 개를 히어로에게 전달하고,
    /// 수령에 성공한 경우에만 완료 목록에서 제거합니다.
    /// </summary>
    protected override void OnInteract(Hero interactor)
    {
        // 실행 직전에도 히어로와 완료 요리의 상태를 확인합니다.
        if (!CanInteract(interactor)) return;

        // 검사한 히어로도 직원과 동일한 수령 함수를 사용합니다.
        GroundHero groundHero = (GroundHero)interactor;
        TryInteract(groundHero.SushiCarrier);
    }

    /// <summary>
    /// 완성 요리를 운반자에게 전달하고 성공한 주문만 수령 목록에서 제거합니다.
    /// </summary>
    public bool TryInteract(SushiCarrier carrier)
    {
        // 전달 직전에도 빈손 및 유효한 완성 주문을 검사합니다.
        if (!CanReceiveBy(carrier)) return false;
        // 이번에 전달할 완성 요리의 위치를 찾습니다.
        int orderIndex = FindReceivableOrderIndex();

        // 전달 가능한 요리가 없다면 중단합니다.
        if (orderIndex < 0) return false;

        // 완료 목록에서 전달할 주문 정보를 가져옵니다.
        CookingOrder order = _completedOrders[orderIndex];

        // 히어로가 실제로 받았는지 확인하고 실패하면 목록을 유지합니다.
        if (!carrier.TryReceive(order.Sushi, order.Guest)) return false;

        // 히어로에게 전달된 요리만 완료 목록에서 제거합니다.
        _completedOrders.RemoveAt(orderIndex);
        return true;
    }

    /// <summary>
    /// 다른 손님의 같은 메뉴로 서빙했을 때 남은 주문을 원래 주문자에게 인계합니다.
    /// </summary>
    public bool TransferOutstandingOrder(GuestNPC recipient, GuestNPC sourceGuest, Sushi sushi)
    {
        // 원래 주문자가 아직 같은 메뉴를 기다릴 때만 인계가 필요합니다.
        if (recipient == null || sourceGuest == null || recipient == sourceGuest ||
            sushi == null || !sourceGuest.IsWaiting || sourceGuest.SelectedSushi == null ||
            sourceGuest.SelectedSushi.Id != sushi.Id) return false;

        // 조리 중인 주문을 먼저 넘기고, 없다면 이미 완성된 주문을 넘깁니다.
        return TryTransferOrder(_cookingOrders, recipient, sourceGuest, sushi) ||
            TryTransferOrder(_completedOrders, recipient, sourceGuest, sushi);
    }

    /// <summary>
    /// 목록에서 해당 메뉴의 주문 하나를 찾아 시간 초기화 없이 인계합니다.
    /// </summary>
    static bool TryTransferOrder(List<CookingOrder> orders, GuestNPC recipient,
        GuestNPC sourceGuest, Sushi sushi)
    {
        // 수령된 요리 한 개에 대응하는 남은 주문 한 개만 넘깁니다.
        foreach (CookingOrder order in orders)
        {
            // 제공받은 손님에게 남아 있는 같은 메뉴 주문만 찾습니다.
            if (order == null || order.Guest != recipient || order.Sushi == null ||
                order.Sushi.Id != sushi.Id) continue;

            // 조리 시간은 그대로 유지하고 주문자를 변경합니다.
            order.ReassignGuest(sourceGuest);
            return true;
        }

        // 취소되었거나 이미 수령된 경우에는 인계할 주문이 없습니다.
        return false;
    }

    /// <summary>
    /// 현재 수령할 수 있는 완성 요리를 찾아 완료 목록의 인덱스를 반환합니다.
    /// 수령 가능한 요리가 없다면 -1을 반환합니다.
    /// </summary>
    int FindReceivableOrderIndex()
    {
        // 완료 목록에 들어온 순서대로 수령 가능한 요리를 찾습니다.
        for (int i = 0; i < _completedOrders.Count; i++)
        {
            // 검사할 주문을 가져옵니다.
            CookingOrder order = _completedOrders[i];

            // 손님과 메뉴가 유효하고 손님이 아직 기다리는지 확인합니다.
            if (!IsValidOrder(order)) continue;
            // 운반자에게 전달 가능한 것은 완성된 음식뿐입니다.
            if (order.Sushi.Type != MenuType.Food) continue;

            // 조리가 끝난 요리만 수령할 수 있습니다.
            if (!order.IsCompleted) continue;

            // 현재 손님이 주문한 원본 메뉴와 일치하는지 확인합니다.
            if (order.Sushi != order.Guest.SelectedSushi) continue;

            // 가장 먼저 찾은 유효한 완성 요리의 위치를 반환합니다.
            return i;
        }

        // 수령할 수 있는 완성 요리가 없습니다.
        return -1;
    }

    /// <summary>요리를 소비하지 않고 현재 수령 가능한 음식 메뉴만 조회합니다.</summary>
    public bool TryPeekCompletedFood(out Sushi food)
    {
        // 실제 수령 직전에는 TryInteract에서 다시 목록을 검사합니다.
        food = null;
        if (!isActiveAndEnabled) return false;
        int index = FindReceivableOrderIndex();
        if (index < 0) return false;
        food = _completedOrders[index].Sushi;
        return true;
    }
}
