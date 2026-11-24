using System.Collections.Generic;
using UnityEngine;

public partial class SushiManager : MonoBehaviour
{
    #region 인스펙터 표시 필드
    [Header("참조")]
    [SerializeField] SushiListManager _sushiListManager;
    [SerializeField] Inventory _inventory;
    [SerializeField] PlayerProgress _playerProgress;        // 실제 서빙 경험치를 지급할 플레이어 성장 컴포넌트입니다.
    [SerializeField] CookerNPC _cookerNPC;
    [SerializeField] EnforceManager _enforceManager;        // 직원별 현재 강화 단계를 관리합니다.
    [SerializeField] ServingNPC _servingNPC;        // 하이어라키에 미리 배치한 서빙 직원입니다.

    [Header("초밥집 운영시간")]
    [SerializeField, Min(0f)] float _operationTime = 180f;
    [SerializeField] float _remainingOperationTime;

    [Header("운영비")]
    // 영업 중 1초마다 발생하는 운영비 N입니다. 실제 골드는 아직 차감하지 않습니다.
    [SerializeField, Min(0f), Tooltip("운영비 N (Gold/초): 영업 경과시간에 곱해 누적합니다.")]
    float _operationCostPerSecond = 1f;
    // 이번 영업에서 발생한 운영비의 누적 금액입니다.
    [SerializeField, Tooltip("이번 영업의 누적 운영비 (Gold). 영업 시작 시 초기화됩니다.")]
    float _accumulatedOperationCost;

    [Header("음식 대기 실패 페널티")]
    // 손님 한 명의 대기 실패로 추가되는 운영비입니다.
    [SerializeField, Min(0f), Tooltip("음식 대기 실패 1회당 추가 운영비 (Gold).")]
    float _waitingFailureCostPenalty = 10f;
    // 손님 한 명의 대기 실패로 줄어드는 영업시간입니다.
    [SerializeField, Min(0f), Tooltip("음식 대기 실패 1회당 영업시간 감소량 (초). 남은 시간을 초과해 감소하지 않습니다.")]
    float _waitingFailureTimePenalty = 5f;

    [Header("이번 영업 메뉴별 판매 내역 (김초밥 및 음료 포함)")]
    [SerializeField] List<MenuSalesResult> _menuSalesResults = new();

    [Header("이번 영업 누적 보상")]
    [SerializeField] float _totalSalesGold;
    [SerializeField] int _totalPlayerExperience;    

    [Header("영업 결과창")]
    [SerializeField] int _totalSpawnedGuestCount;
    [SerializeField] int _satisfiedGuestCount;
    [SerializeField] int _unservedGuestCount;
    [SerializeField] int _otherEndedGuestCount;

    #endregion

    #region 런타임 상태
    // 두 직원의 데이터, 이동 및 수령 영역 설정이 준비되었는지 나타냅니다.
    bool _staffReady;
    Chair[] _seats;
    bool _menuConfirmed;
    readonly Dictionary<GuestNPC, Chair> _reservedSeats = new();
    readonly List<Chair> _availableSeats = new();
    readonly HashSet<GuestNPC> _rewardedGuests = new();

    readonly Dictionary<GuestNPC, GuestVisit> _guestVisits = new();
    // 수령 이후에도 같은 손님의 주문 이벤트를 중복 접수하지 않습니다.
    readonly HashSet<GuestNPC> _acceptedOrders = new();
    #endregion

    #region 외부 조회
    public float TotalSalesGold => _totalSalesGold;
    public int TotalPlayerExperience => _totalPlayerExperience;
    public float RemainingOperationTime => _remainingOperationTime;
    // 결과 View 등에서 조회할 초당 운영비 N입니다.
    public float OperationCostPerSecond => _operationCostPerSecond;
    // 나중에 정산 시 차감할 이번 영업의 누적 운영비입니다.
    public float AccumulatedOperationCost => _accumulatedOperationCost;
    /// <summary>대기 실패 1회당 추가할 운영비 설정값입니다. 단위는 Gold입니다.</summary>
    public float WaitingFailureCostPenalty => Mathf.Max(0f, _waitingFailureCostPenalty);
    /// <summary>대기 실패 1회당 줄일 영업시간 설정값입니다. 실제 감소량은 남은 시간으로 제한됩니다.</summary>
    public float WaitingFailureTimePenalty => Mathf.Max(0f, _waitingFailureTimePenalty);
    public bool IsMenuConfirmed => _menuConfirmed;
    // 플레이어 레벨을 별도로 저장하지 않고 연결된 실제 성장 정보에서 조회합니다.
    public int CurrentPlayerLevel => _playerProgress != null ? Mathf.Max(1, _playerProgress.Level) : 1;
    public ServingNPC Serving => _servingNPC;
    public CookerNPC Cooker => _cookerNPC;
    // 영업이 끝나도 메뉴를 확정한 세션에서는 직원 구성을 바꾸지 않습니다.
    public bool CanManageStaff => Application.isPlaying && isActiveAndEnabled &&
        !_menuConfirmed && _staffReady;
    public bool IsOpen =>
        isActiveAndEnabled && _menuConfirmed && _remainingOperationTime > 0f;

    public Inventory Inventory => _inventory;
    public EnforceManager Enforce => _enforceManager;

    public IReadOnlyList<MenuSalesResult> MenuSalesResults => _menuSalesResults.AsReadOnly();
    public int TotalSpawnedGuestCount => _totalSpawnedGuestCount;
    public int SatisfiedGuestCount => _satisfiedGuestCount;
    public int UnservedGuestCount => _unservedGuestCount;
    public int OtherEndedGuestCount => _otherEndedGuestCount;
    public int ActiveGuestCount => _guestVisits.Count;
    // 운영비는 누적만 하며 실제 정산 차감은 아직 적용하지 않습니다.
    public float NetProfit => _totalSalesGold;

    #endregion

    #region 정산 데이터 형식
    [System.Serializable]
    public class MenuSalesResult
    {
        [SerializeField] Sushi _menu;
        [SerializeField] int _soldCount;
        [SerializeField] float _earnedGold;
        [SerializeField] int _earnedPlayerExperience;

        public Sushi Menu => _menu;
        public int SoldCount => _soldCount;
        public float EarnedGold => _earnedGold;
        public int EarnedPlayerExperience => _earnedPlayerExperience;

        /// <summary>전체 정산에 사용할 메뉴별 판매 내역을 생성합니다.</summary>
        public MenuSalesResult(Sushi menu)
        {
            // 오늘의 메뉴와 기본 제공 메뉴 모두 원본별로 집계합니다.
            _menu = menu;
        }

        /// <summary>정상 판매 한 건의 수량과 지급 보상을 누적합니다.</summary>
        public void AddSale(float gold, int playerExperience)
        {
            // 조리 완료나 폐기는 판매 횟수에 포함하지 않습니다.
            _soldCount++;
            // 판매 시점마다 확정된 보상을 그대로 합산합니다.
            _earnedGold += gold;
            _earnedPlayerExperience += playerExperience;
        }
    }

    /// <summary>
    /// 손님 한 명의 방문 결과와 패널티 적용 여부를 보관합니다.
    /// </summary>
    sealed class GuestVisit
    {
        // 이 손님에게 음식이나 음료를 제공했는지 기록합니다.
        public bool WasServed;

        // 제공받은 음식을 먹고 정상적으로 식사를 마쳤는지 기록합니다.
        public bool FinishedEating;

        // 같은 방문에 대기 실패 패널티를 중복 적용하지 않도록 기록합니다.
        public bool WaitingFailurePenaltyApplied;
    }

    #endregion

    #region 영업 정산
    /// <summary>새 영업의 운영비, 메뉴 판매, 손님 및 직원 실적을 초기화합니다.</summary>
    void ResetBusinessStatistics()
    {
        // 새 영업의 운영비는 0 Gold부터 누적합니다.
        _accumulatedOperationCost = 0f;

        // 직전 영업의 보상과 메뉴별 판매 내역을 정리합니다.
        _totalSalesGold = 0f;
        _totalPlayerExperience = 0;
        _menuSalesResults.Clear();
        // 손님별 진행 기록과 누적 인원을 새 영업 기준으로 초기화합니다.
        _guestVisits.Clear();
        _totalSpawnedGuestCount = 0;
        _satisfiedGuestCount = 0;
        _unservedGuestCount = 0;
        _otherEndedGuestCount = 0;
        _rewardedGuests.Clear();
        // 직원 능력치와 별개인 이번 영업의 실적만 초기화합니다.
        if (_servingNPC != null) _servingNPC.ResetBusinessStatistics();
        if (_cookerNPC != null) _cookerNPC.ResetBusinessStatistics();
    }

    /// <summary>
    /// 영업 중 음식 대기에 실패한 손님 한 명당
    /// 설정된 운영비와 영업시간 페널티를 한 번 적용합니다.
    /// </summary>
    public void ApplyWaitingFailurePenalty(GuestNPC guest)
    {
        // 마감 이후에는 정산 금액을 변경하지 않습니다.
        // 아직 음식 대기 상태인 손님만 실패 패널티를 적용합니다.
        if (!IsOpen || guest == null || !guest.IsWaiting)
        {
            return;
        }

        // 이 매장에 실제로 등록된 손님의 방문 기록을 가져옵니다.
        if (!_guestVisits.TryGetValue(guest, out var visit))
        {
            return;
        }

        // 이미 서빙받았거나 패널티를 적용한 손님은 제외합니다.
        if (visit.WasServed || visit.WaitingFailurePenaltyApplied)
        {
            return;
        }

        // 같은 손님에게 다시 호출되어도 중복 적용되지 않도록 먼저 기록합니다.
        visit.WaitingFailurePenaltyApplied = true;

        // View에도 공개한 설정값만큼 이번 영업의 누적 운영비를 추가합니다.
        _accumulatedOperationCost += WaitingFailureCostPenalty;

        // 공개한 시간 페널티 설정값만큼 줄이되 남은 시간은 0초 이상으로 유지합니다.
        _remainingOperationTime = Mathf.Max(
            0f,
            _remainingOperationTime - WaitingFailureTimePenalty);
    }

    /// <summary>초기화에 성공하여 실제 입장한 손님을 한 번 등록합니다.</summary>
    public void RegisterSpawnedGuest(GuestNPC guest)
    {
        // 영업 전 객체와 중복 등록은 전체 등장 인원에서 제외합니다.
        if (guest == null || !_menuConfirmed || _guestVisits.ContainsKey(guest)) return;
        // 실제 입장한 손님의 서빙과 식사 완료 상태를 추적합니다.
        _guestVisits.Add(guest, new GuestVisit());
        _totalSpawnedGuestCount++;
    }

    /// <summary>서빙받은 손님이 식사를 끝냈다는 사실을 기록합니다.</summary>
    public void NotifyGuestFinishedEating(GuestNPC guest)
    {
        // 정상 등록과 서빙 성공을 거친 손님만 식사 완료로 기록합니다.
        if (guest == null || !_guestVisits.TryGetValue(guest, out var visit) || !visit.WasServed) return;
        // 만족 퇴장 수는 출구 도착 시 증가시키고 여기서는 식사 상태만 기록합니다.
        visit.FinishedEating = true;
    }

    /// <summary>출구에 도착한 손님을 식사 결과에 따라 한 번 집계합니다.</summary>
    public void NotifyGuestExited(GuestNPC guest)
    {
        // 파괴 중인 Unity 참조와 중복 퇴장 요청을 안전하게 처리합니다.
        if (ReferenceEquals(guest, null) || !_guestVisits.TryGetValue(guest, out var visit)) return;
        // 실제 퇴장한 손님은 매장에 남은 인원에서 제외합니다.
        _guestVisits.Remove(guest);
        // 식사 완료, 미서빙 퇴장과 그 외 종료를 구분합니다.
        if (visit.FinishedEating) _satisfiedGuestCount++;
        else if (!visit.WasServed) _unservedGuestCount++;
        else _otherEndedGuestCount++;
    }

    /// <summary>정상 퇴장 없이 제거된 손님을 별도 종료로 정리합니다.</summary>
    public void NotifyGuestRemoved(GuestNPC guest)
    {
        // 정상 퇴장 시 이미 제거된 기록은 다시 집계하지 않습니다.
        if (ReferenceEquals(guest, null) || !_guestVisits.Remove(guest)) return;
        // 경로 오류나 강제 제거를 미서빙 정상 퇴장에 섞지 않습니다.
        _otherEndedGuestCount++;
    }

    /// <summary>전체 정산과 오늘의 메뉴별 판매 수량 및 보상을 함께 기록합니다.</summary>
    void RecordMenuSales(Sushi menu, float gold, int playerExperience)
    {
        // 이전에 판매한 같은 원본 메뉴의 정산 항목을 찾습니다.
        MenuSalesResult result = null;
        foreach (MenuSalesResult entry in _menuSalesResults)
        {
            // 다른 메뉴의 실적은 변경하지 않습니다.
            if (entry.Menu != menu) continue;
            result = entry;
            break;
        }
        // 처음 판매한 김초밥과 음료도 전체 메뉴 정산에 자동 추가합니다.
        if (result == null)
        {
            // 최초 판매한 메뉴의 독립된 실적을 생성합니다.
            result = new MenuSalesResult(menu);
            _menuSalesResults.Add(result);
        }
        // 실제 지급 보상을 전체 메뉴 실적과 일반 초밥 표시값에 반영합니다.
        result.AddSale(gold, playerExperience);
        if (_sushiListManager != null) _sushiListManager.RecordSale(menu, gold, playerExperience);
    }
    #endregion

    /// <summary>
    /// 영업 대기 상태와 이번 영업의 누적 보상을 초기화합니다.
    /// </summary>
    void Awake()
    {
        // 아직 오늘의 메뉴가 확정되지 않은 상태로 시작합니다.
        _menuConfirmed = false;

        // 영업을 시작하기 전에는 남은 운영시간을 0으로 표시합니다.
        _remainingOperationTime = 0f;

        // 영업 전에는 누적 운영비가 발생하지 않습니다.
        _accumulatedOperationCost = 0f;

        // 이번 영업에서 발생한 매출을 초기화합니다.
        _totalSalesGold = 0f;

        // 이번 영업에서 발생한 경험치를 초기화합니다.
        _totalPlayerExperience = 0;

        // 중복 보상 방지를 위한 기록을 초기화합니다.
        _rewardedGuests.Clear();
    }

    /// <summary>
    /// 현재 유지 중인 GameManager에서 인벤토리와 성장 정보를 연결합니다.
    /// </summary>
    void Start()
    {
        // 유지 중인 GameManager의 실제 인벤토리를 우선 사용합니다.
        if (GameManager.Instance != null)
            _inventory = GameManager.Instance.Inventory;
        // 인벤토리 값이 변경되면 메뉴의 제작 가능 수량만 자동으로 갱신합니다.
        if (_inventory != null) _inventory.Changed += HandleInventoryChanged;
        // 진행 정보와 재료 차감 경로를 준비한 뒤 직원의 기본 레벨을 적용합니다.
        PrepareShopServices();
        // 실제 레벨을 읽고 메뉴 확정 전에 직원의 해금 및 강화 상태를 적용합니다.
        LoadPlayerLevel();
        // 시작 시에도 실제 인벤토리를 기준으로 메뉴 표시를 준비합니다.
        HandleInventoryChanged();
    }

    /// <summary>실제 인벤토리 변경 시 메뉴 제작 가능 수량을 자동으로 갱신합니다.</summary>
    void HandleInventoryChanged()
    {
        // 보유 아이템과 골드를 복사하지 않고 메뉴 계산 결과만 갱신합니다.
        if (_sushiListManager != null) _sushiListManager.RefreshTodayMenuDisplay();
    }

    /// <summary>
    /// 실제 플레이어 레벨을 불러오고 메뉴 확정 전 직원 상태에 반영합니다.
    /// </summary>
    public bool LoadPlayerLevel()
    {
        // 편집 중에는 런타임 성장 정보를 조회하지 않습니다.
        if (!Application.isPlaying) return false;
        PlayerProgress progress = _playerProgress;
        // 씬 전환으로 유지되는 실제 플레이어의 성장 정보를 사용합니다.
        if (GameManager.Instance != null)
            progress = GameManager.Instance.PlayerProgress;
        if (progress == null)
        {
            Debug.LogWarning("PlayerProgress 연결을 확인하세요.", this);
            return false;
        }
        // 실제 성장 정보를 연결하되 레벨업 이벤트로 직원 구성을 변경하지 않습니다.
        _playerProgress = progress;
        // 영업 중 재조회에서는 직원 구성을 유지합니다.
        if (!_menuConfirmed && (_enforceManager == null ||
            !TryApplyStaffLevels(_enforceManager.ServingLevel, _enforceManager.CookingLevel)))
            Debug.LogWarning("직원 데이터와 하이어라키 연결을 확인하세요.", this);
        return true;
    }

    /// <summary>메뉴 확정 전에 두 전용 데이터의 강화 능력치와 해금을 적용합니다.</summary>
    public bool TryApplyStaffLevels(int servingLevel, int cookingLevel)
    {
        // 영업 중인 직원 구성은 변경하지 않습니다.
        if (!Application.isPlaying || !isActiveAndEnabled || _menuConfirmed) return false;
        if (_servingNPC == null || _cookerNPC == null || _cookerNPC.InteractionArea == null)
            return false;
        ServingStaffUpgradeData servingData = _servingNPC.UpgradeData;
        CookerStaffUpgradeData cookingData = _cookerNPC.UpgradeData;
        // 직원별 기본 능력치까지 반영한 유효한 단계만 적용합니다.
        if (servingData == null || cookingData == null ||
            !_servingNPC.TryGetMoveSpeed(servingLevel, out _) ||
            !_cookerNPC.TryGetCookTimeMultiplier(cookingLevel, out _)) return false;
        if (!_servingNPC.Configure(this, _cookerNPC, servingLevel) ||
            !_cookerNPC.ApplyUpgradeLevel(cookingLevel)) return false;
        // 초기화에 성공한 서빙 직원의 행동 컴포넌트를 활성화합니다.
        _servingNPC.enabled = true;
        // 영업 준비 시점의 플레이어 레벨로 서빙 직원의 해금을 반영합니다.
        _servingNPC.gameObject.SetActive(CurrentPlayerLevel >= _servingNPC.UnlockPlayerLevel);
        // 영업 준비 시점의 플레이어 레벨로 요리사의 해금을 반영합니다.
        _cookerNPC.gameObject.SetActive(CurrentPlayerLevel >= _cookerNPC.UnlockPlayerLevel);
        // 두 직원의 초기 설정이 완료되었음을 기록합니다.
        _staffReady = true;
        return true;
    }

    /// <summary>
    /// 같은 요리를 기다리는 손님 중 남은 대기시간이 가장 적은 손님을 반환합니다.
    /// </summary>
    public GuestNPC FindMostUrgentGuest(Sushi sushi)
    {
        // 운반할 요리가 없으면 대상도 없습니다.
        if (sushi == null) return null;
        GuestNPC target = null;
        float shortestTime = float.PositiveInfinity;
        // 이 매니저에 좌석을 예약한 손님만 서빙 대상으로 검사합니다.
        foreach (GuestNPC guest in _reservedSeats.Keys)
        {
            if (guest == null || !guest.isActiveAndEnabled || !guest.IsWaiting ||
                guest.SelectedSushi == null || guest.SelectedSushi.Id != sushi.Id) continue;
            // 상호작용 영역이 활성화된 손님에게만 접근합니다.
            Collider area = guest.InteractionArea;
            if (area == null || !area.enabled || !area.gameObject.activeInHierarchy) continue;
            // 가장 급한 손님을 선택하며 매 물리 프레임 대상의 변화를 반영합니다.
            if (guest.RemainingWaitingTime < shortestTime)
            {
                shortestTime = guest.RemainingWaitingTime;
                target = guest;
            }
        }
        return target;
    }

    /// <summary>
    /// 플레이어 레벨을 한 단계 올린 뒤 변경된 레벨을 조회하고 직원 상태에 반영합니다.
    /// </summary>
    [ContextMenu("테스트/플레이어 레벨 1 증가")]
    public void TestIncreasePlayerLevel()
    {
        // 기존과 동일하게 영업 준비 중에만 레벨업을 허용합니다.
        if (!Application.isPlaying || _menuConfirmed) return;

        // 조회 함수를 호출하지 않고 실제 레벨업 대상의 참조만 확보합니다.
        PlayerProgress progress = GameManager.Instance != null
            ? GameManager.Instance.PlayerProgress
            : _playerProgress;

        // 성장 컴포넌트가 없으면 레벨업을 중단합니다.
        if (progress == null)
        {
            Debug.LogWarning("PlayerProgress 연결을 확인하세요.", this);
            return;
        }

        // 기존 성장 API를 통해 레벨을 한 단계 증가시킵니다.
        int previousLevel = progress.Level;
        progress.LevelUp();

        // 최대 레벨이나 성장 데이터 누락으로 증가하지 않았다면 중단합니다.
        if (progress.Level == previousLevel)
        {
            Debug.LogWarning(
                "최대 레벨 또는 성장 데이터 설정을 확인하세요.", this);
            return;
        }

        // 레벨업 성공 후 한 번만 조회하여 표시와 직원 상태를 갱신합니다.
        LoadPlayerLevel();
    }

    /// <summary>
    /// 씬에서 제거될 때 인벤토리 변경 이벤트의 구독을 정리합니다.
    /// </summary>
    void OnDestroy()
    {
        // 유지되는 인벤토리가 이전 씬의 메뉴 매니저를 호출하지 않도록 해제합니다.
        if (_inventory != null) _inventory.Changed -= HandleInventoryChanged;
    }

    /// <summary>
    /// 도착 위치와 착석 위치가 설정된 의자를 영업 시작 전에 등록합니다.
    /// </summary>
    public bool RegisterSeats(IReadOnlyList<Chair> chairs)
    {
        // 영업 확정 후에는 예약에 사용 중인 좌석 구성을 변경하지 않습니다.
        if (_menuConfirmed || _reservedSeats.Count > 0 || chairs == null) return false;

        // 외부 배열과 분리된 유효한 좌석 목록을 준비합니다.
        List<Chair> seats = new();

        // 스포너에 등록된 의자를 확인합니다.
        foreach (Chair seat in chairs)
        {
            // 이동, 착석, 서빙 기준점이 누락되었거나 중복 등록된 의자는 제외합니다.
            if (seat == null || !seat.IsValid || seats.Contains(seat)) continue;

            // 자리 예약에 사용할 목적지를 추가합니다.
            seats.Add(seat);
        }

        // 사용할 수 있는 좌석이 없으면 등록에 실패합니다.
        if (seats.Count == 0) return false;

        // 스포너의 원본 배열이 변경되어도 현재 좌석 구성을 유지합니다.
        _seats = seats.ToArray();
        return true;
    }

    /// <summary>
    /// 영업 경과시간에 맞춰 운영시간과 누적 운영비를 갱신합니다.
    /// </summary>
    void Update()
    {
        // 영업 중이 아니라면 시간 계산을 중단합니다.
        if (!IsOpen) return;

        // 마감 프레임에도 남은 영업시간까지만 계산하여 초과 운영비를 방지합니다.
        float elapsedTime = Mathf.Min(Time.deltaTime, _remainingOperationTime);

        // 실제 영업 중 경과한 시간만큼 남은 시간을 감소시킵니다.
        _remainingOperationTime = Mathf.Max(0f, _remainingOperationTime - elapsedTime);

        // 음수나 유효하지 않은 N값은 운영비 누적에서 제외합니다.
        if (!(_operationCostPerSecond >= 0f) || float.IsInfinity(_operationCostPerSecond)) return;

        // 경과시간 × N을 Gold 단위로 누적하고 실제 인벤토리는 변경하지 않습니다.
        _accumulatedOperationCost += elapsedTime * _operationCostPerSecond;
    }

    /// <summary>음식은 쿠커에게 요청하고 음료는 현장에서 제공하도록 대기시킵니다.</summary>
    public bool ReceiveOrder(GuestNPC guest)
    {
        // 이 매니저의 좌석에서 실제 주문한 손님만 처리합니다.
        if (guest == null || !guest.IsWaiting || !_reservedSeats.ContainsKey(guest) ||
            guest.SelectedSushi == null || !TryGetGuestMenu(guest, out Sushi menu) ||
            guest.SelectedSushi != menu) return false;
        if (_acceptedOrders.Contains(guest)) return false;
        // 음료에 조리 완료나 운반 대기 주문을 생성하지 않습니다.
        if (menu.Type != MenuType.Drink && (_cookerNPC == null || !_cookerNPC.ReceiveOrder(guest, menu)))
            return false;
        _acceptedOrders.Add(guest);
        GameSfx.PlayAt(GameSoundId.Order, guest.transform.position);
        return true;
    }

    /// <summary>
    /// 플레이어 수령과 운반을 생략하고 완성된 주문으로 서빙을 테스트합니다.
    /// </summary>
    public bool TryServeCompletedOrder(GuestNPC guest)
    {
        // 음식 대기 중인 손님과 요리사가 유효한지 확인합니다.
        if (guest == null || !guest.IsWaiting || _cookerNPC == null) return false;

        // 이 매니저에 자리가 예약된 손님인지 확인합니다.
        if (!_reservedSeats.ContainsKey(guest)) return false;

        // 해당 손님에게 완성된 주문이 있는지 확인하고 한 건을 꺼냅니다.
        if (!_cookerNPC.TryTakeCompletedOrder(guest, out Sushi sushi)) return false;

        // 완성된 스시 데이터를 손님에게 전달해 식사를 시작합니다.
        return guest.TryServe(sushi);
    }

    /// <summary>
    /// 정상 서빙 보상을 플레이어에게 한 번 지급하고 이번 영업의 실적에 기록합니다.
    /// 성공한 경우 실제 지급된 판매금액을 반환합니다.
    /// </summary>
    /// <param name="guest">메뉴를 제공받는 손님입니다.</param>
    /// <param name="sushi">손님이 주문한 원본 메뉴입니다.</param>
    /// <param name="paidGold">지급 성공 시 확정 판매금액이며, 실패 시 0입니다.</param>
    /// <returns>보상 지급과 실적 기록의 성공 여부입니다.</returns>
    public bool RecordServingReward(GuestNPC guest, Sushi sushi, out float paidGold)
    {
        // 어느 검사에서 실패하더라도 반환 금액은 0으로 유지합니다.
        paidGold = 0f;

        // 손님과 스시 데이터가 유효한지 확인합니다.
        if (guest == null || sushi == null) return false;

        // 이 매니저가 자리를 배정한 손님인지 확인합니다.
        if (!_reservedSeats.ContainsKey(guest)) return false;

        // 아직 음식을 기다리는 손님에게 제공하는 경우에만 보상을 지급합니다.
        if (!guest.IsWaiting) return false;

        // 보상은 실제 주문한 원본 데이터로 계산합니다.
        if (guest.SelectedSushi != sushi) return false;

        // 실제 지급 대상과 성장 데이터가 없으면 음식도 소비하지 않습니다.
        if (_inventory == null || _playerProgress == null || _playerProgress.MaxLevel <= 0)
        {
            Debug.LogWarning("[서빙 실패] Inventory와 PlayerProgress 연결을 확인하세요.", this);
            return false;
        }

        // 보상과 누적값이 지원 범위를 넘지 않는지 지급 전에 검사합니다.
        if (_enforceManager == null ||
            !_enforceManager.TryGetReward(sushi, out float gold, out int experience)) return false;
        // 최대 레벨에서 지급하지 않는 경험치는 정산에도 누적하지 않습니다.
        int awardedExperience = _playerProgress.IsMaxLevel ? 0 : experience;
        if (!_inventory.CanAddGold(gold) || float.IsInfinity(_totalSalesGold + gold) ||
            _totalPlayerExperience > int.MaxValue - awardedExperience) return false;

        // 같은 손님의 보상을 두 번 누적하지 않습니다.
        if (!_rewardedGuests.Add(guest)) return false;

        // 기존 인벤토리 API로 실제 보유 골드에 판매금액을 추가합니다.
        if (!_inventory.TryAddGold(gold))
        {
            _rewardedGuests.Remove(guest);
            return false;
        }

        // 최대 레벨에서는 경험치 누적을 생략하며 나머지는 기존 성장 API를 사용합니다.
        if (awardedExperience > 0 && !_playerProgress.AddExperience(awardedExperience))
        {
            // 경험치 지급 실패 시 이번 골드 지급도 되돌려 부분 지급을 막습니다.
            _inventory.TrySpendGold(gold);
            _rewardedGuests.Remove(guest);
            Debug.LogWarning("[서빙 실패] 플레이어 경험치 데이터를 확인하세요.", this);
            return false;
        }

        // 정상 서빙한 스시의 판매금액을 누적합니다.
        _totalSalesGold += gold;

        // 정상 서빙한 스시의 플레이어 경험치를 누적합니다.
        _totalPlayerExperience += awardedExperience;
        // 손님별 서빙 성공은 식사 완료와 실제 퇴장과 구분하여 기록합니다.
        if (_guestVisits.TryGetValue(guest, out var visit)) visit.WasServed = true;
        // 메뉴 판매 실적은 보상 지급 성공 경로에서만 한 번 누적합니다.
        RecordMenuSales(sushi, gold, awardedExperience);
        // 지급과 실적 기록이 모두 끝난 실제 판매금액을 전달합니다.
        paidGold = gold;
        return true;
    }

    /// <summary>
    /// 신규 손님 입장을 중단하고 현재까지 누적된 매출과 경험치를 출력합니다.
    /// </summary>
    [ContextMenu("테스트/영업 종료")]
    public void EndBusiness()
    {
        // 실행 중에만 영업종료를 처리합니다.
        if (!Application.isPlaying) return;

        // 영업을 시작하기 전이라면 종료하지 않습니다.
        if (!_menuConfirmed) return;

        // 운영시간을 종료하여 신규 손님 생성을 중단합니다.
        _remainingOperationTime = 0f;

        // 호출 시점까지 누적된 매출과 플레이어 경험치를 출력합니다.
        Debug.Log(
            $"[영업 종료] 누적 매출: {_totalSalesGold} Gold / " +
            $"누적 플레이어 경험치: {_totalPlayerExperience}",
            this);
    }

    /// <summary>
    /// 서빙된 손님의 남은 주문을 원래 주문자에게 인계하고 추가 주문을 정리합니다.
    /// </summary>
    public void CompleteServing(GuestNPC recipient, GuestNPC sourceGuest, Sushi sushi)
    {
        // 모든 메뉴의 판매 수량을 서빙 성공 시 한 번만 확정합니다.
        _acceptedOrders.Remove(recipient);
        if (_sushiListManager != null) _sushiListManager.MarkSold(recipient, sourceGuest);
        if (recipient != null) GameSfx.PlayAt(GameSoundId.Served, recipient.transform.position);
        // 음료는 쿠커에 조리 및 운반 주문이 없으므로 주문 인계를 생략합니다.
        if (sushi == null || sushi.Type == MenuType.Drink) return;
        // 요리사가 제거되었다면 인계할 주문이 없습니다.
        if (_cookerNPC == null) return;

        // 다른 손님의 요리로 서빙했다면 해당 손님에게 남은 조리를 넘깁니다.
        _cookerNPC.TransferOutstandingOrder(recipient, sourceGuest, sushi);

        // 제공받은 손님의 불필요한 추가 주문만 취소합니다.
        _cookerNPC.CancelOrders(recipient);
    }

    /// <summary>
    /// 퇴장하거나 제거된 손님의 남은 조리와 수령 대기 주문을 취소합니다.
    /// </summary>
    public void CancelOrder(GuestNPC guest)
    {
        // 파괴 중인 손님도 취소할 수 있도록 실제 참조와 요리사를 확인합니다.
        if (ReferenceEquals(guest, null)) return;
        _acceptedOrders.Remove(guest);
        if (_sushiListManager != null) _sushiListManager.CancelAllocation(guest);
        if (_cookerNPC == null) return;

        // 해당 손님의 주문만 요리사에게 취소 요청합니다.
        _cookerNPC.CancelOrders(guest);
    }

    /// <summary>대기 중인 손님의 우선권을 지키며 예약 가능한 빈 좌석을 선택합니다.</summary>
    public bool TryReserveSeat(GuestNPC guest, out Chair seat)
    {
        // 신규 손님은 두 구역에서 예약할 수 있는 후보를 확인합니다.
        return TryReserveSeatInternal(guest, false, ChairArea.Back, out seat);
    }

    /// <summary>손님의 예약을 반환하되 Uncleaned 상태는 청소할 때까지 유지합니다.</summary>
    public void ReleaseSeat(GuestNPC guest)
    {
        // 파괴 중인 Unity 객체도 자신의 좌석만 반환할 수 있습니다.
        if (ReferenceEquals(guest, null)) return;
        if (_reservedSeats.TryGetValue(guest, out Chair chair) && chair != null)
            chair.Release(guest);
        _reservedSeats.Remove(guest);
        _rewardedGuests.Remove(guest);
    }

    // 구역별로 먼저 기다린 손님의 예약 우선권을 유지하는 입장 순서입니다.
    readonly List<GuestNPC> _seatWaiters = new();

    /// <summary>같은 구역에서 기다리던 손님에게 다른 구역으로 이동하지 않고 좌석을 배정합니다.</summary>
    public bool TryReserveSeat(GuestNPC guest, ChairArea area, out Chair seat)
    {
        // 왕복 중인 손님은 처음 정한 구역의 빈자리만 사용합니다.
        return TryReserveSeatInternal(guest, true, area, out seat);
    }

    /// <summary>좌석 상태, 예약자와 대기 순서를 함께 검사해 한 좌석만 예약합니다.</summary>
    bool TryReserveSeatInternal(GuestNPC guest, bool restrictArea, ChairArea area, out Chair seat)
    {
        seat = null;
        if (!IsOpen || guest == null || _seats == null) return false;
        if (_reservedSeats.TryGetValue(guest, out seat))
            return seat != null && seat.IsValid && (!restrictArea || seat.Area == area);
        _availableSeats.Clear();
        foreach (Chair candidate in _seats)
        {
            if (candidate == null || !candidate.IsAvailable ||
                (restrictArea && candidate.Area != area)) continue;
            GuestNPC first = GetFirstSeatWaiter(candidate.Area);
            // 새 손님이 방금 청소된 자리를 먼저 빼앗지 못하도록 합니다.
            if (first != null && first != guest) continue;
            _availableSeats.Add(candidate);
        }
        if (_availableSeats.Count == 0) return false;
        Chair selected = _availableSeats[Random.Range(0, _availableSeats.Count)];
        if (!selected.TryReserve(guest)) return false;
        seat = selected;
        _reservedSeats.Add(guest, selected);
        return true;
    }

    /// <summary>현재 구역에서 빈자리를 기다리는 첫 번째 유효한 손님을 찾습니다.</summary>
    GuestNPC GetFirstSeatWaiter(ChairArea area)
    {
        // 제거되거나 다른 상태로 넘어간 손님은 우선권에서 제외합니다.
        foreach (GuestNPC guest in _seatWaiters)
            if (guest != null && guest.isActiveAndEnabled && guest.IsSeekingSeat &&
                guest.SeatArea == area) return guest;
        return null;
    }

    /// <summary>만석인 손님을 한 번만 빈자리 대기 순서에 추가합니다.</summary>
    public void EnqueueSeatWaiter(GuestNPC guest)
    {
        // 같은 손님이 여러 번 우선권을 차지하지 않게 합니다.
        if (guest != null && !_seatWaiters.Contains(guest)) _seatWaiters.Add(guest);
    }

    /// <summary>착석, 대기 포기 또는 제거된 손님을 대기 순서에서 제외합니다.</summary>
    public void RemoveSeatWaiter(GuestNPC guest)
    {
        // Unity 파괴 중인 참조도 동일한 목록에서 제거합니다.
        _seatWaiters.Remove(guest);
    }

    /// <summary>등록된 좌석이 있는 구역과 그 구역의 양 끝 의자 도착 위치를 반환합니다.</summary>
    public bool TryChooseWaitingArea(out ChairArea area, out Transform first, out Transform inner)
    {
        area = ChairArea.Back;
        first = null;
        inner = null;
        _availableSeats.Clear();
        if (_seats == null) return false;
        foreach (Chair chair in _seats)
            if (chair != null && chair.IsValid) _availableSeats.Add(chair);
        if (_availableSeats.Count == 0) return false;
        // 두 구역 중 유효한 좌석이 있는 구역에서만 대기합니다.
        area = _availableSeats[Random.Range(0, _availableSeats.Count)].Area;
        foreach (Chair chair in _availableSeats)
        {
            if (chair.Area != area) continue;
            if (first == null || chair.ArrivePos.position.x < first.position.x) first = chair.ArrivePos;
            if (inner == null || chair.ArrivePos.position.x > inner.position.x) inner = chair.ArrivePos;
        }
        return first != null && inner != null;
    }

    internal Chair FindNearestCleanableSeat(SushiCarrier provider)
    {
        if (_seats == null || provider == null || provider.CurrentSushi != null) return null;

        Chair nearest = null;
        float nearestDistance = float.PositiveInfinity;
        Vector3 position = provider.transform.position;
        foreach (Chair seat in _seats)
        {
            if (seat == null || !seat.CanHoldInteract(provider)) continue;
            float distance = (seat.ServingPoint.position - position).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearest = seat;
            nearestDistance = distance;
        }
        return nearest;
    }

    /// <summary>제공 가능한 음료 주문 중 남은 대기시간이 가장 짧은 손님을 반환합니다.</summary>
    public GuestNPC FindMostUrgentDrink(SushiCarrier provider)
    {
        GuestNPC target = null;
        float shortest = float.PositiveInfinity;
        foreach (GuestNPC guest in _reservedSeats.Keys)
        {
            // 다른 제공자가 작업 중인 음료와 비활성 영역은 제외합니다.
            if (guest == null || !guest.CanHoldInteract(provider)) continue;
            Collider area = guest.InteractionArea;
            if (area == null || !area.enabled || !area.gameObject.activeInHierarchy) continue;
            if (guest.RemainingWaitingTime < shortest)
            {
                shortest = guest.RemainingWaitingTime;
                target = guest;
            }
        }
        return target;
    }
}
