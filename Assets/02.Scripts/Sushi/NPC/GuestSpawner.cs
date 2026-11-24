using System.Collections.Generic;
using UnityEngine;

public class GuestSpawner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] SushiManager _sushiManager;
    // 생성한 게스트에게 전달할 씬의 공용 효과 관리자입니다.
    [SerializeField] GuestReactionEffects _reactionEffects;
    [SerializeField] SushiListManager _sushiListManager;
    [SerializeField] SushiSpawnDifficultyData _difficultyData;

    [Header("손님 프리팹 및 출현 위치")]
    [SerializeField] GuestNPC[] _guestPrefabs;
    [SerializeField] Transform _spawnPoint;

    [Header("손님이 사용할 의자")]
    [SerializeField] Chair[] _chairs;

    [Header("손님 퇴장 위치")]
    [SerializeField] Transform _leftPoint;

    [Header("손님 입장 간격 범위 - 초 단위 (난이도 적용 전)")]
    [SerializeField, Min(0.01f)] float _minSpawnInterval = 3f;
    [SerializeField, Min(0.01f)] float _maxSpawnInterval = 5f;

    [Header("마감 몇 초 전부터 신규 입장을 중단할지 설정")]
    [SerializeField, Min(0f)] float _stopSpawningBeforeClose = 20f;
    
    [Header("현재 난이도 배율")]
    [SerializeField, Tooltip("Difficulty Data에서 조회하여 자동 갱신합니다. 배율은 연결된 에셋에서 설정하세요.")]
    float _currentDifficultyMultiplier = 1f;

    readonly List<GuestNPC> _spawnCandidates = new();
    readonly HashSet<GuestNPC> _awaitingGuests = new();
    float _spawnProgress;
    float _sampledSpawnInterval;
    bool _spawnScheduled;
    bool _seatsRegistered;

    public float CurrentDifficultyMultiplier => _currentDifficultyMultiplier;

    /// <summary>
    /// 스포너에 지정한 의자를 SushiManager의 예약 대상으로 등록합니다.
    /// </summary>
    void Awake()
    {
        // 좌석을 관리할 매니저가 없다면 초기화를 중단합니다.
        if (_sushiManager == null) return;

        // 등록한 자리의 예약과 반환은 매니저가 관리하도록 전달합니다.
        _seatsRegistered = _sushiManager.RegisterSeats(_chairs);

        // 이동할 자리가 없으면 인스펙터 설정을 확인할 수 있도록 알립니다.
        if (!_seatsRegistered)
            Debug.LogWarning("GuestSpawner의 의자 목록과 각 Chair의 ArrivePos, SeatPos, ServingPoint를 확인하세요.", this);

        // 퇴장 위치가 없으면 손님을 정상적으로 정리할 수 없음을 알립니다.
        if (_leftPoint == null)
            Debug.LogWarning("GuestSpawner에 자식 LeftPoint의 Transform을 연결하세요.", this);
    }

    /// <summary>
    /// 영업시간과 레벨별 난이도를 반영하여 무작위 간격으로 손님 입장을 시도합니다.
    /// </summary>
    void Update()
    {
        // 영업 시작 전에도 현재 플레이어 레벨에서 불러온 난이도를 표시합니다.
        RefreshDifficulty();

        // 손님 생성에 필요한 참조를 확인합니다.
        if (_sushiManager == null || _sushiListManager == null ||
            _spawnPoint == null || _leftPoint == null) return;

        // 이동 목적지를 등록하기 전에는 손님을 생성하지 않습니다.
        if (!_seatsRegistered) return;

        // 손님은 하나의 프리팹 목록에서 선택합니다.
        if (_guestPrefabs == null || _guestPrefabs.Length == 0) return;

        // 일반 메뉴 소진 여부와 관계없이 영업 종료 또는 마감 N초 전에만 입장을 중단합니다.
        if (!_sushiManager.IsOpen || _sushiManager.RemainingOperationTime <=
            Mathf.Max(0f, _stopSpawningBeforeClose)) return;

        // 첫 손님도 메뉴 확정 후 무작위 간격을 기다렸다가 생성합니다.
        if (!_spawnScheduled) ScheduleNextSpawn();

        // 레벨업으로 배율이 바뀌어도 이미 기다린 진행률은 보존합니다.
        _spawnProgress += Time.deltaTime * _currentDifficultyMultiplier / _sampledSpawnInterval;
        if (_spawnProgress < 1f) return;

        // 프레임이 지연되어도 한 프레임에 여러 손님을 몰아서 생성하지 않습니다.
        GuestNPC prefab = GetRandomGuestPrefab();
        if (prefab != null) TrySpawnGuest(prefab);

        // 입장을 시도할 때마다 다음 손님의 간격을 새로 뽑습니다.
        ScheduleNextSpawn();
    }

    /// <summary>스폰 간격 범위가 유효한 양수이고 최소값 이하로 뒤집히지 않도록 검사합니다.</summary>
    void OnValidate()
    {
        // 유효하지 않은 최소 간격은 기본값으로 복구합니다.
        if (!(_minSpawnInterval >= 0.01f) || float.IsInfinity(_minSpawnInterval))
            _minSpawnInterval = 3f;
        // 최대 간격은 유효한 최소 간격보다 작을 수 없습니다.
        if (!(_maxSpawnInterval >= _minSpawnInterval) || float.IsInfinity(_maxSpawnInterval))
            _maxSpawnInterval = _minSpawnInterval;
    }

    /// <summary>현재 플레이어 레벨에 맞는 스폰 배율을 갱신합니다.</summary>
    void RefreshDifficulty()
    {
        // 참조가 없을 때 사용할 기본 배율입니다.
        float multiplier = 1f;

        // 연결된 데이터와 실제 플레이어 레벨로 현재 성장 단계를 조회합니다.
        if (_difficultyData != null && _sushiManager != null)
        {
            // 현재 플레이어 레벨을 조회합니다.
            int playerLevel = _sushiManager.CurrentPlayerLevel;
            // 손님 입장 간격에 적용할 배율을 조회합니다.
            _difficultyData.TryGetSpawnRateMultiplier(playerLevel, out multiplier);
        }
        // 기존 스폰 진행률 계산에서 사용할 배율을 갱신합니다.
        _currentDifficultyMultiplier = multiplier;
    }

    /// <summary>다음 손님을 기다릴 기본 간격을 최소와 최대 사이에서 새로 선택합니다.</summary>
    void ScheduleNextSpawn()
    {
        // 런타임에 외부 코드로 잘못된 범위를 넣어도 양수 간격을 보장합니다.
        float minimum = _minSpawnInterval >= 0.01f && !float.IsInfinity(_minSpawnInterval)
            ? _minSpawnInterval : 3f;
        float maximum = _maxSpawnInterval >= minimum && !float.IsInfinity(_maxSpawnInterval)
            ? _maxSpawnInterval : minimum;
        // 뽑은 간격은 다음 입장 시도까지 유지하고 진행률을 초기화합니다.
        _sampledSpawnInterval = Random.Range(minimum, maximum);
        _spawnProgress = 0f;
        _spawnScheduled = true;
    }

    /// <summary>
    /// 배열에 등록된 유효한 손님 프리팹 중 하나를 무작위로 선택합니다.
    /// </summary>
    GuestNPC GetRandomGuestPrefab()
    {
        // 이전 선택에 사용한 후보 목록을 비웁니다.
        _spawnCandidates.Clear();

        // 등록된 프리팹 배열을 확인합니다.
        foreach (GuestNPC prefab in _guestPrefabs)
        {
            // 비어 있는 항목은 선택 대상에서 제외합니다.
            if (prefab == null) continue;

            // 생성 가능한 프리팹을 후보에 추가합니다.
            _spawnCandidates.Add(prefab);
        }

        // 생성 가능한 프리팹이 없다면 선택에 실패합니다.
        if (_spawnCandidates.Count == 0) return null;

        // 유효한 후보 중 하나를 같은 확률로 선택합니다.
        int index = Random.Range(0, _spawnCandidates.Count);

        // 선택한 손님 프리팹을 반환합니다.
        return _spawnCandidates[index];
    }

    /// <summary>
    /// 무작위 손님을 출현 지점에 생성하고 메뉴와 자리 관리자를 전달합니다.
    /// </summary>
    void TrySpawnGuest(GuestNPC prefab)
    {
        // 오늘의 메뉴와 기본 제공 메뉴를 합친 현재 주문 후보를 가져옵니다.
        IReadOnlyList<Sushi> todayMenu = _sushiListManager.GetOrderableMenu();

        // 주문할 메뉴가 없다면 손님을 생성하지 않습니다.
        if (todayMenu.Count == 0) return;

        // 생성 가능한 프리팹이 없다면 중단합니다.
        if (prefab == null) return;

        // 선택한 프리팹을 출현 지점의 위치와 회전으로 생성합니다.
        GuestNPC guest = Instantiate(prefab, _spawnPoint.position, _spawnPoint.rotation);

        // 게스트가 씬을 검색하지 않도록 공용 효과 관리자 참조를 전달합니다.
        if (guest.TryGetComponent(out GuestAngryReaction reaction))
        {
            // 생성 직후 초기화하여 연출 시작 전에 참조를 준비합니다.
            reaction.Initialize(_reactionEffects);
        }

        // 입장하는 손님 몫을 미리 확보하여 메뉴 수량보다 많이 생성하지 않습니다.
        if (!_sushiManager.TryAllocateGuest(guest, out _))
        {
            Destroy(guest.gameObject);
            return;
        }

        // 이동 시작 전에 주문 시작 이벤트를 구독합니다.
        guest.OrderStarted += HandleOrderStarted;

        // 메뉴 배정은 매니저가 보관하며 손님에게 이동 초기화 참조만 전달합니다.
        if (!guest.Initialize(_sushiManager, _leftPoint))
        {
            // 실패한 입장은 확보했던 수량도 반환합니다.
            _sushiManager.CancelOrder(guest);
            // 입장하지 못한 손님의 이벤트 연결을 해제합니다.
            guest.OrderStarted -= HandleOrderStarted;

            // 초기화에 실패한 손님은 총인원에 포함하지 않고 제거합니다.
            Destroy(guest.gameObject);
            return;
        }

        // 제거된 손님의 참조를 정리한 뒤 도착 대기 중인 손님을 보관합니다.
        _awaitingGuests.RemoveWhere(IsDestroyedGuest);
        _awaitingGuests.Add(guest);

        // 초기화 실패 객체를 제외한 실제 등장 손님만 매장 통계에 등록합니다.
        _sushiManager.RegisterSpawnedGuest(guest);
    }

    /// <summary>
    /// 이미 제거된 손님인지 확인합니다.
    /// </summary>
    static bool IsDestroyedGuest(GuestNPC guest)
    {
        // Unity 객체의 파괴 상태를 확인합니다.
        return guest == null;
    }

    /// <summary>
    /// 착석 후 주문시간이 끝난 손님의 주문을 SushiManager에 전달합니다.
    /// </summary>
    void HandleOrderStarted(GuestNPC guest)
    {
        // 제거된 손님은 처리하지 않습니다.
        if (guest == null) return;

        // 주문 알림 처리를 마친 손님의 구독과 보관 참조를 해제합니다.
        guest.OrderStarted -= HandleOrderStarted;
        _awaitingGuests.Remove(guest);

        // 주문 전달 대상이 유효한지 확인합니다.
        if (_sushiManager == null)
        {
            // 매니저 참조가 끊겨 주문이 전달되지 않는 원인을 알립니다.
            Debug.LogWarning($"[주문 전달 실패] {guest.name}: SushiManager가 없습니다.", this);
            return;
        }

        // 요리 NPC로의 주문 전달을 SushiManager에 요청합니다.
        _sushiManager.ReceiveOrder(guest);
    }

    /// <summary>
    /// 스포너 제거 시 손님들의 이벤트 연결을 해제합니다.
    /// </summary>
    void OnDestroy()
    {
        // 아직 도착하지 않은 손님들을 확인합니다.
        foreach (GuestNPC guest in _awaitingGuests)
        {
            // 이미 제거된 손님은 제외합니다.
            if (guest == null) continue;

            // 남아 있는 주문 이벤트 연결을 해제합니다.
            guest.OrderStarted -= HandleOrderStarted;
        }

        // 더 이상 사용하지 않는 참조를 정리합니다.
        _awaitingGuests.Clear();
    }
}
