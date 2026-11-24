using UnityEngine;

/// <summary>
/// 해마 한 마리의 포물선 점프와 횟수 및 장애물에 따른 좌우 반전을 관리합니다.
/// </summary>
[CreateAssetMenu(fileName = "SeahorseJumpPattern", menuName = "Fish/Seahorse Jump Pattern")]
public class SeahorseJumpPattern : FishSpawnPattern
{
    // 생성할 해마 프리팹입니다.
    [SerializeField] private Fish _fishPrefab;

    // 시작 높이에서 정점까지 올라가는 상대 높이입니다.
    [SerializeField, Min(0f)] private float _jumpHeight = 1.5f;

    // 한 번 점프하는 동안 이동할 수평 거리입니다.
    [SerializeField, Min(0.1f)] private float _jumpDistance = 2f;

    // FishData의 일반 이동 속도와 독립적인 수평 점프 속도입니다.
    [SerializeField, Min(0.1f)] private float _horizontalSpeed = 2f;

    // 한 방향으로 완료할 점프 횟수입니다.
    [SerializeField, Min(1)] private int _jumpsBeforeTurn = 3;

    // 점프 완료 후 다음 점프까지 정지할 시간입니다.
    [SerializeField, Min(0f)] private float _jumpInterval;

    // 실행본이 사용하는 스포너와 소유 해마입니다.
    private FishSpawner _spawner;
    private Fish _fish;

    // 부모 클래스의 장애물 감지에 전달할 한 마리짜리 배열입니다.
    private Fish[] _members;

    // 현재 좌우 진행 방향과 같은 방향으로 완료한 점프 횟수입니다.
    private Vector2 _direction;
    private int _completedJumps;

    // 현재 점프의 기준 높이, 실제 높이, 진행 시간 및 전체 시간입니다.
    private float _baseY;
    private float _activeHeight;
    private float _jumpElapsed;
    private float _jumpDuration;

    // 점프 간 대기 및 소멸 후 재생성 대기 시간입니다.
    private float _waitRemaining;
    private float _respawnDelay;

    // 실행, 재생성 대기, 행동 복귀 및 정상 점프 완료를 구분합니다.
    private bool _running;
    private bool _restartRequired;
    private bool _nextJumpPending;

    // 스포너가 생성할 물고기의 기본 데이터를 조회할 때 사용합니다.
    public override FishData Data => _fishPrefab != null ? _fishPrefab.Data : null;

    // 패턴 실행 횟수와 실제 물고기 마릿수를 구분하기 위한 생성 예정 수입니다.
    public override int InitialMemberCount => 1;

    /// <summary>
    /// 생성과 이동 범위를 저장하고 실행본이 관리할 해마 한 마리를 생성합니다.
    /// </summary>
    public override bool Spawn(
        FishSpawner spawner,
        FishSpawnArea area,
        FishVerticalRange verticalRange,
        float respawnDelay)
    {
        // 원본 에셋 실행과 같은 실행본의 중복 초기화를 막습니다.
        if (!IsRuntime || _spawner != null)
        {
            Debug.LogError("패턴 실행본에서 Spawn을 한 번만 호출해 주세요.", this);
            return false;
        }

        // 생성에 필요한 참조와 활성 프리팹을 검사합니다.
        if (spawner == null || area == null || verticalRange == null
            || _fishPrefab == null || !_fishPrefab.gameObject.activeSelf || Data == null)
        {
            Debug.LogError("스포너, 범위, 활성 해마 프리팹과 Data를 확인해 주세요.", this);
            return false;
        }

        // 스포너가 제공한 범위와 재생성 지연 시간을 저장합니다.
        _spawner = spawner;
        Area = area;
        VerticalRange = verticalRange;
        _respawnDelay = Mathf.Max(0f, respawnDelay);
        _members = new Fish[1];

        // 생성 실패 시 스포너가 실행본을 정리하도록 알립니다.
        if (!SpawnFish())
        {
            return false;
        }

        // 생성이 완료된 실행본만 이동 처리를 시작합니다.
        // 새 실행에 이전 리스폰 예약이 남지 않도록 초기화합니다.
        ClearRespawnReservations();
        _running = true;
        return true;
    }

    /// <summary>
    /// 생성 범위 안에 해마를 만들고 초기 방향과 점프 상태를 설정합니다.
    /// </summary>
    private bool SpawnFish()
    {
        // 최초 방향과 재생성 방향은 데이터의 X 방향으로 결정합니다.
        _direction = Data.InitialDirection.x < 0f ? Vector2.left : Vector2.right;

        // 생성 원과 수직 이동 범위를 만족하는 월드 위치를 구합니다.
        Vector2 spawnXY = Area.ClampPosition(Area.GetRandomPosition(), VerticalRange);
        Vector3 position = new Vector3(spawnXY.x, spawnXY.y, Area.Center.z);

        // 기존 생성 API를 통해 초기화, 밝기, 레이어 및 충돌 관계를 적용합니다.
        if (!_spawner.SpawnFish(_fishPrefab, position, _direction,
            VerticalRange, Brightness, SpawnLayer, out Fish spawnedFish))
        {
            return false;
        }

        // 생성한 개체를 소유 참조와 장애물 감지 배열에 등록합니다.
        _fish = spawnedFish;
        // 반환 즉시 이전 대여의 소유 참조를 해제합니다.
        _fish.Released += OnMemberReleased;
        _members[0] = spawnedFish;

        // 물리 감지 결과를 소유 패턴에 직접 전달하도록 연결합니다.
        _fish.BindGroupPattern(this, 0);

        // 이전 개체의 대기, 횟수 및 점프 예약을 초기화합니다.
        _restartRequired = false;
        _nextJumpPending = false;
        _waitRemaining = 0f;
        _completedJumps = 0;

        // 스포너가 보정한 실제 생성 위치에서 첫 점프를 준비합니다.
        BeginJump();
        return true;
    }

    // 포물선의 진행은 판단 주기와 별개로 매 물리 단계에 실행합니다.
    public override bool RequiresPhysicsPath => true;

    /// <summary>
    /// 벽을 감지하면 기존 점프를 취소하고 현재 위치에서 반대 방향의 점프를 시작합니다.
    /// </summary>
    public override void Tick(float deltaTime)
    {
        // 소실되었거나 다른 행동이 제어 중이면 점프 방향을 변경하지 않습니다.
        if (!_running || _fish == null || !_fish.CanFollowGroup)
            return;

        // 수평 벽뿐 아니라 상승 및 하강을 가로막는 접촉 보고도 소비합니다.
        if (!TryGetObstacleTurn(_members, _direction, deltaTime, out _, out _))
            return;

        // 접촉 당시 실제 상승 중이었다면 하강 점프, 하강 중이었다면 상승 점프로 바꿉니다.
        float verticalSign = ObstacleApproachDirection.y > 0f ? -1f : 1f;
        _direction = -_direction;
        _fish.Stop();

        // 이전 점프의 진행도와 대기 예약을 버려 벽 쪽의 기존 목표를 다시 쫓지 않습니다.
        _completedJumps = 0;
        _waitRemaining = 0f;
        _nextJumpPending = false;
        _restartRequired = false;
        BeginJump(verticalSign);
    }
    /// <summary>
    /// 행동 상태와 소멸을 확인하고 점프, 대기 또는 재생성을 진행합니다.
    /// </summary>
    public override void TickPhysicsPath(float deltaTime)
    {
        // 준비되지 않은 실행본과 잘못된 시간은 처리하지 않습니다.
        if (!_running || deltaTime <= 0f || float.IsNaN(deltaTime)
            || float.IsInfinity(deltaTime))
        {
            return;
        }

        // 획득 또는 외부 파괴로 소멸한 해마는 지연 후 다시 생성합니다.
        if (_fish == null)
        {
            return;
        }

        // 시체가 남아 있는 동안에는 이동과 재생성을 진행하지 않습니다.
        if (_fish.State == FishState.Dead)
        {
            return;
        }

        // 전투, 포획, 넉백 등 다른 행동의 이동을 덮어쓰지 않습니다.
        if (!_fish.CanFollowGroup)
        {
            _restartRequired = true;
            return;
        }

        // 다른 행동에서 복귀한 경우에만 횟수와 대기를 초기화합니다.
        if (_restartRequired)
        {
            _restartRequired = false;
            _nextJumpPending = false;
            _completedJumps = 0;
            _waitRemaining = 0f;
            BeginJump();
        }

        // 정상 완료 후에는 누적 횟수를 유지하면서 다음 점프를 준비합니다.
        if (_nextJumpPending)
        {
            // 대기 중에는 이전 점프의 속도가 남지 않도록 정지합니다.
            if (_waitRemaining > 0f)
            {
                _waitRemaining = Mathf.Max(0f, _waitRemaining - deltaTime);
                _fish.Stop();
                return;
            }

            // 이전 점프가 물리에 반영된 실제 도착 위치에서 시작합니다.
            _nextJumpPending = false;
            BeginJump();
        }

        // 현재 점프의 다음 경로 위치를 계산하고 이동을 요청합니다.
        TickJump(deltaTime);
    }

    /// <summary>
    /// 독립 예약 관리자가 요청한 해마의 빈자리를 재생성합니다.
    /// </summary>
    public override void RespawnSlot(int slotIndex)
    {
        // 하나뿐인 자리가 비었고 실행 중일 때만 재생성합니다.
        if (!_running || slotIndex != 0 || _fish != null)
            return;

        // 실패 시 남은 예약을 취소하여 반복 생성을 중단합니다.
        if (!SpawnFish())
        {
            _running = false;
            ClearRespawnReservations();
            Debug.LogError("예약된 물고기 재생성에 실패하여 패턴을 정지합니다.", this);
            return;
        }

        // 새 경로는 다음 물리 단계에서 현재 위치를 기준으로 실행합니다.
        RequestDecision();
    }

    /// <summary>
    /// 현재 위치를 기준으로 허용되는 점프 높이와 전체 점프 시간을 준비합니다.
    /// </summary>
    private void BeginJump(float verticalSign = 1f)
    {
        // 새 점프는 실제 현재 위치에서 시작하여 이전 포물선으로 끌려가지 않게 합니다.
        _baseY = _fish.Position.y;

        // 회피 점프는 위 또는 아래의 남은 공간에 맞춰 높이와 부호를 결정합니다.
        float minY = Mathf.Max(VerticalRange.MinY, _spawner.MovementMin.y);
        float maxY = Mathf.Min(VerticalRange.MaxY, _spawner.MovementMax.y);
        float clearance = verticalSign < 0f ? _baseY - minY : maxY - _baseY;
        _activeHeight = (verticalSign < 0f ? -1f : 1f)
            * Mathf.Min(Mathf.Max(0f, _jumpHeight), Mathf.Max(0f, clearance));

        // 기존 진행도는 사용하지 않고 새 점프 시간을 계산합니다.
        _jumpDuration = Mathf.Max(0.1f, _jumpDistance) / Mathf.Max(0.1f, _horizontalSpeed);
        _jumpElapsed = 0f;
    }

    /// <summary>
    /// 포물선의 다음 위치로 이동하고 완료 횟수에 따라 좌우 방향을 바꿉니다.
    /// </summary>
    private void TickJump(float deltaTime)
    {
        // 마지막 물리 프레임에서 점프 완료 시간을 넘지 않도록 제한합니다.
        float step = Mathf.Min(deltaTime, _jumpDuration - _jumpElapsed);
        float nextElapsed = _jumpElapsed + step;
        float progress = Mathf.Clamp01(nextElapsed / _jumpDuration);

        // 양 끝은 0이고 정점은 1인 포물선 높이를 계산합니다.
        float heightRatio = 4f * progress * (1f - progress);

        // X는 현재 위치부터 진행시켜 벽 반전 후 이전 목표를 다시 쫓지 않습니다.
        float nextX = _fish.Position.x
            + _direction.x * Mathf.Max(0.1f, _horizontalSpeed) * step;
        float nextY = _baseY + _activeHeight * heightRatio;

        // 상태 변경 등으로 이동을 거절하면 복귀 후 새 점프를 준비합니다.
        if (!_fish.FollowPattern(new Vector2(nextX, nextY), deltaTime))
        {
            _restartRequired = true;
            return;
        }

        // 이동을 요청한 경로 위치에 맞춰 시간을 갱신합니다.
        _jumpElapsed = nextElapsed;
        if (_jumpElapsed < _jumpDuration)
        {
            return;
        }

        // 지정 횟수에 도달하면 누적 횟수를 초기화하고 방향을 반전합니다.
        _completedJumps++;
        if (_completedJumps >= Mathf.Max(1, _jumpsBeforeTurn))
        {
            _completedJumps = 0;
            _direction = -_direction;
        }

        // 실제 도착이 반영될 다음 물리 프레임에 새 점프를 준비하도록 예약합니다.
        _nextJumpPending = true;
        _waitRemaining = Mathf.Max(0f, _jumpInterval);
    }

    /// <summary>
    /// 일반 이동만 멈추고 재개 시 현재 위치에서 새 점프를 시작하도록 예약합니다.
    /// </summary>
    public override void Pause()
    {
        // 다른 행동의 이동 속도를 덮어쓰지 않도록 일반 이동 중일 때만 멈춥니다.
        if (_fish != null && _fish.CanFollowGroup)
        {
            _fish.Stop();
        }

        // 스포너가 다시 활성화되면 현재 위치에서 경로를 재설정합니다.
        _restartRequired = true;
    }

    /// <summary>
    /// 실행본이 소유한 해마를 제거하고 실행 상태와 참조를 정리합니다.
    /// </summary>
    public override void Dispose()
    {
        // 원본 에셋에는 실행 중 개체 정리를 수행하지 않습니다.
        if (!IsRuntime)
        {
            return;
        }

        // 이후 Tick에서 생성과 이동을 수행하지 않도록 중단합니다.
        _running = false;

        // 무리 해제 뒤에는 예약된 물고기가 다시 생성되지 않게 합니다.
        ClearRespawnReservations();
        if (_fish != null)
        {
            // 이 실행본이 소유한 해마만 정지하고 제거합니다.
            _fish.Stop();
            _fish.Release();
        }

        // 소유 개체와 외부 참조를 해제합니다.
        _fish = null;
        _members = null;
        _spawner = null;
        Area = null;
        VerticalRange = null;
    }
    /// <summary>
    /// 풀에 반환된 물고기를 현재 패턴의 소유 자리에서 즉시 해제합니다.
    /// </summary>
    private void OnMemberReleased(Fish fish)
    {
        // 이전 대여의 구독을 해제하고 현재 소유 개체의 반환인지 확인합니다.
        fish.Released -= OnMemberReleased;
        if (!ReferenceEquals(_fish, fish))
        {
            return;
        }

        // 반환 즉시 이동 및 장애물 감지에서 이전 객체 참조를 제거합니다.
        _fish = null;
        if (_members != null)
        {
            _members[0] = null;
        }

        // 패턴 해제 중에는 예약하지 않고 정상 소실일 때만 재생성을 예약합니다.
        if (_running)
        {
            ScheduleRespawn(0, _respawnDelay);
        }
    }
}
