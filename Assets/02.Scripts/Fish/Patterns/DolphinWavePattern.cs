using UnityEngine;

/// <summary>
/// 돌고래 한 마리를 사인 곡선으로 이동시키고 장애물과 경계에서 방향을 전환합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "DolphinWavePattern",
    menuName = "Fish/Dolphin Wave Pattern")]
public sealed class DolphinWavePattern : FishSpawnPattern
{
    // 생성할 돌고래 프리팹입니다.
    [SerializeField] private Fish _fishPrefab;

    // 좌우 전진 속도입니다. FishData의 기본 이동 속도와 별도로 사용합니다.
    [SerializeField, Min(0.1f)] private float _horizontalSpeed = 2f;

    [Header("곡선 이동 높이")]
    // 곡선의 최저점부터 최고점까지의 전체 높이입니다.
    [SerializeField, Min(0f)]
    [Tooltip("최저점과 최고점의 높이 차이입니다. 4이면 중심 기준 위아래로 2씩 움직입니다.")]
    private float _waveHeight = 2f;

    // 위아래 파동을 한 번 완료하는 데 걸리는 시간입니다.
    [SerializeField, Min(0.1f)] private float _wavePeriod = 4f;

    // 이 실행본이 사용하는 스포너와 돌고래입니다.
    private FishSpawner _spawner;
    private Fish _fish;

    // 부모 클래스의 장애물 감지 API에 전달할 배열입니다.
    private Fish[] _members;

    // 현재 좌우 방향이며 오른쪽은 1, 왼쪽은 -1입니다.
    private float _horizontalDirection;

    // 현재 파동의 진행 각도와 실제 적용할 진폭입니다.
    private float _phase;
    private float _activeAmplitude;

    // 돌고래가 이동할 수 있는 실제 높이 범위입니다.
    private float _minY;
    private float _maxY;

    // 소멸 후 재생성까지의 지연 시간입니다.
    private float _respawnDelay;

    // 실행 여부와 다른 행동에서의 복귀를 관리합니다.
    private bool _running;
    private bool _restartRequired;

    // 스포너가 생성 전 물고기의 기본 데이터를 조회합니다.
    public override FishData Data =>
        _fishPrefab != null ? _fishPrefab.Data : null;

    // 패턴 실행 횟수와 실제 물고기 마릿수를 구분하기 위한 생성 예정 수입니다.
    public override int InitialMemberCount => 1;

    /// <summary>
    /// 생성 범위와 이동 범위를 저장하고 돌고래 한 마리를 생성합니다.
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
            // 스포너에서 생성한 실행본만 사용하도록 안내합니다.
            Debug.LogError(
                "패턴 실행본에서 Spawn을 한 번만 호출해 주세요.",
                this);
            return false;
        }

        // 생성에 필요한 참조와 활성 프리팹을 검사합니다.
        if (spawner == null || area == null || verticalRange == null ||
            _fishPrefab == null || !_fishPrefab.gameObject.activeSelf ||
            Data == null)
        {
            // 생성 설정이 누락된 경우 실행을 중단합니다.
            Debug.LogError(
                "스포너, 범위, 활성 돌고래 프리팹과 Data를 확인해 주세요.",
                this);
            return false;
        }

        // 이동 설정이 유효한 값인지 검사합니다.
        if (!IsFinite(_horizontalSpeed) || _horizontalSpeed <= 0f ||
            !IsFinite(_waveHeight) || _waveHeight < 0f ||
            !IsFinite(_wavePeriod) || _wavePeriod <= 0f ||
            !IsFinite(respawnDelay))
        {
            // NaN이나 무한대가 이동 계산에 사용되지 않도록 거부합니다.
            Debug.LogError("돌고래 패턴의 이동 설정을 확인해 주세요.", this);
            return false;
        }

        // 그룹 높이와 전체 맵 높이의 교집합을 계산합니다.
        _minY = Mathf.Max(verticalRange.MinY, spawner.MovementMin.y);
        _maxY = Mathf.Min(verticalRange.MaxY, spawner.MovementMax.y);

        // 서로 겹치지 않는 이동 범위는 생성할 수 없습니다.
        if (_minY > _maxY)
        {
            // 스포너와 그룹의 높이 설정을 확인하도록 안내합니다.
            Debug.LogError("돌고래의 수직 이동 범위가 겹치지 않습니다.", this);
            return false;
        }

        // 생성과 재생성에서 사용할 외부 참조를 저장합니다.
        _spawner = spawner;
        Area = area;
        VerticalRange = verticalRange;
        _respawnDelay = Mathf.Max(0f, respawnDelay);
        _members = new Fish[1];

        // 생성 실패 시 스포너가 실행본을 정리하도록 알립니다.
        if (!SpawnFish()) return false;

        // 정상 생성된 실행본만 Tick을 처리합니다.
        // 새 실행에 이전 리스폰 예약이 남지 않도록 초기화합니다.
        ClearRespawnReservations();
        _running = true;
        return true;
    }

    /// <summary>
    /// 기존 스포너 API로 돌고래를 생성하고 파동 이동을 준비합니다.
    /// </summary>
    private bool SpawnFish()
    {
        // 최초 방향과 재생성 방향은 FishData의 X 방향을 따릅니다.
        _horizontalDirection = Data.InitialDirection.x < 0f ? -1f : 1f;

        // 기존 생성 범위 안에서 초기 위치를 선택합니다.
        Vector2 spawnXY =
            Area.ClampPosition(Area.GetRandomPosition(), VerticalRange);
        Vector3 position =
            new Vector3(spawnXY.x, spawnXY.y, Area.Center.z);

        // 기존 생성 과정에서 초기화, 밝기, 레이어와 충돌 설정을 적용합니다.
        if (!_spawner.SpawnFish(
            _fishPrefab,
            position,
            new Vector2(_horizontalDirection, 0f),
            VerticalRange,
            Brightness,
            SpawnLayer,
            out Fish spawnedFish))
            return false;

        // 생성한 돌고래를 소유 참조와 장애물 검사 배열에 등록합니다.
        _fish = spawnedFish;
        // 반환 즉시 이전 대여의 소유 참조를 해제합니다.
        _fish.Released += OnMemberReleased;
        _members[0] = spawnedFish;

        // 물리 감지 결과를 소유 패턴에 직접 전달하도록 연결합니다.
        _fish.BindGroupPattern(this, 0);

        // 이전 개체의 재생성 대기 상태를 초기화합니다.
        _restartRequired = false;

        // 실제 생성 위치에서 자연스럽게 파동을 시작합니다.
        PrepareWave();
        return true;
    }

    /// <summary>
    /// 실제 위치와 허용 높이에 맞춰 진폭과 시작 위상을 준비합니다.
    /// </summary>
    private void PrepareWave()
    {
        // 허용 높이보다 큰 파동이 생성되지 않도록 진폭을 제한합니다.
        float halfHeight = Mathf.Max(0f, (_maxY - _minY) * 0.5f);
        // 전체 높이의 절반을 Sin 계산용 진폭으로 사용하고 이동 가능 범위 안으로 제한합니다.
        _activeAmplitude = Mathf.Min(_waveHeight * 0.5f, halfHeight);

        // 진폭이 0이면 상하 이동 없이 좌우로만 전진합니다.
        if (_activeAmplitude <= 0.0001f)
        {
            // 진폭이 없는 경우 나눗셈을 생략합니다.
            _phase = 0f;
            return;
        }

        // 현재 높이에서 가능한 파동 중심을 구합니다.
        float currentY = _fish.Position.y;
        float centerY = Mathf.Clamp(
            currentY,
            _minY + _activeAmplitude,
            _maxY - _activeAmplitude);

        // 현재 위치를 유지하는 시작 위상을 역산합니다.
        float heightRatio = Mathf.Clamp(
            (currentY - centerY) / _activeAmplitude,
            -1f,
            1f);
        _phase = Mathf.Asin(heightRatio);

        // 위쪽에서 시작하면 우선 내려가도록 위상을 선택합니다.
        if (currentY > (_minY + _maxY) * 0.5f)
            _phase = Mathf.PI - _phase;

        // 위상이 계속 커지지 않도록 한 주기 안으로 정리합니다.
        _phase = Mathf.Repeat(_phase, Mathf.PI * 2f);
    }

    // 파동의 진행은 판단 주기와 별개로 매 물리 단계에 실행합니다.
    public override bool RequiresPhysicsPath => true;

    /// <summary>
    /// 저장된 벽 보고가 있으면 현재 위치에서 수평과 수직 진행을 모두 반전합니다.
    /// </summary>
    public override void Tick(float deltaTime)
    {
        // 소실되었거나 다른 행동이 제어 중인 개체는 경로를 바꾸지 않습니다.
        if (!_running || _fish == null || !_fish.CanFollowGroup)
            return;

        // 물고기 배열을 검사하지 않고 감지 단계에서 저장한 접촉만 소비합니다.
        if (TryGetObstacleTurn(_members, Vector2.right * _horizontalDirection,
            deltaTime, out _, out _))
        {
            // 이전 물리 속도를 제거하고 현재 위치를 새 파동의 시작점으로 사용합니다.
            _fish.Stop();
            _horizontalDirection = -_horizontalDirection;
            ReverseVerticalDirection();
            _restartRequired = false;
        }
    }
    /// <summary>
    /// 다른 행동과 소멸을 확인하고 장애물 반전 및 파동 이동을 처리합니다.
    /// </summary>
    public override void TickPhysicsPath(float deltaTime)
    {
        // 실행 전이거나 유효하지 않은 시간은 처리하지 않습니다.
        if (!_running || !IsFinite(deltaTime) || deltaTime <= 0f)
            return;

        // 획득이나 외부 파괴로 사라진 경우 재생성을 처리합니다.
        if (_fish == null)
        {
            // 소멸한 개체만 지연 후 다시 생성합니다.
            return;
        }

        // 시체가 남아 있는 동안에는 이동하거나 재생성하지 않습니다.
        if (_fish.State == FishState.Dead) return;

        // 전투, 포획, 넉백 등의 이동을 덮어쓰지 않습니다.
        if (!_fish.CanFollowGroup)
        {
            // 일반 이동으로 돌아오면 현재 위치에서 경로를 다시 준비합니다.
            _restartRequired = true;
            return;
        }

        // 다른 행동에서 복귀한 경우 실제 위치에 맞춰 파동을 연결합니다.
        if (_restartRequired)
        {
            // 이전 경로의 절대 위치로 끌려가지 않게 다시 시작합니다.
            PrepareWave();
            _restartRequired = false;
        }

        // 파동 실행에는 물리 단계 시간을 사용하여 프레임 사이의 높이 차이를 유지합니다.
        float angularSpeed = Mathf.PI * 2f / Mathf.Max(0.1f, _wavePeriod);

        // 변경된 방향으로 이번 물리 프레임의 이동을 요청합니다.
        MoveWave(deltaTime, angularSpeed);
    }

    /// <summary>
    /// 프레임 사이의 Sin 높이 차이로 이동하여 벽 충돌 후 위치가 튀지 않게 합니다.
    /// </summary>
    private void MoveWave(float deltaTime, float angularSpeed)
    {
        // 절대 시작 위치 대신 실제 현재 위치에서 다음 위치를 계산합니다.
        Vector2 current = _fish.Position;
        float nextPhase = _phase + angularSpeed * deltaTime;

        // 좌우는 일정한 속도로 진행하고 높이는 Sin의 차이만큼 이동합니다.
        float nextX =
            current.x + _horizontalDirection * _horizontalSpeed * deltaTime;
        float nextY = current.y + _activeAmplitude *
            (Mathf.Sin(nextPhase) - Mathf.Sin(_phase));

        // 전체 맵의 수평 경계를 넘으려 하면 좌우 방향을 바꿉니다.
        if ((_horizontalDirection > 0f && nextX > _spawner.MovementMax.x) ||
            (_horizontalDirection < 0f && nextX < _spawner.MovementMin.x))
        {
            // 반전한 방향으로 현재 위치에서 다시 전진합니다.
            _horizontalDirection = -_horizontalDirection;
            nextX = current.x +
                _horizontalDirection * _horizontalSpeed * deltaTime;
        }

        // 허용 높이를 벗어나려 하면 수직 진행 방향을 바꿉니다.
        if (nextY > _maxY || nextY < _minY)
        {
            // Sin 높이는 유지하고 Cos의 부호만 반전합니다.
            ReverseVerticalDirection();

            // 반전된 위상으로 이번 프레임의 높이 차이를 다시 구합니다.
            nextPhase = _phase + angularSpeed * deltaTime;
            nextY = current.y + _activeAmplitude *
                (Mathf.Sin(nextPhase) - Mathf.Sin(_phase));
        }

        // 경계 끝이나 큰 시간 간격에서도 목표가 허용 범위를 넘지 않게 합니다.
        Vector2 target = new Vector2(
            Mathf.Clamp(nextX, _spawner.MovementMin.x, _spawner.MovementMax.x),
            Mathf.Clamp(nextY, _minY, _maxY));

        // 기존 Rigidbody 이동 경로로 전달하여 충돌과 모델 회전을 유지합니다.
        if (!_fish.FollowPattern(target, deltaTime, faceMovement: true))
        {
            // 다른 행동이 개입했다면 복귀 후 실제 위치에서 다시 준비합니다.
            _restartRequired = true;
            return;
        }

        // 성공적으로 요청한 파동 진행도를 저장합니다.
        _phase = Mathf.Repeat(nextPhase, Mathf.PI * 2f);
    }

    /// <summary>
    /// 현재 Sin 높이는 유지하고 상승과 하강 방향을 반전합니다.
    /// </summary>
    private void ReverseVerticalDirection()
    {
        // Sin(π - θ)는 같고 Cos(π - θ)는 반대 부호라는 성질을 사용합니다.
        _phase = Mathf.Repeat(Mathf.PI - _phase, Mathf.PI * 2f);
    }

    /// <summary>
    /// 독립 예약 관리자가 요청한 돌고래의 빈자리를 재생성합니다.
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
    /// 일반 이동을 정지하고 재개 시 현재 위치에서 파동을 다시 준비합니다.
    /// </summary>
    public override void Pause()
    {
        // 전투나 포획 중인 개체의 이동은 변경하지 않습니다.
        if (_fish != null && _fish.CanFollowGroup)
            _fish.Stop();

        // 재개 시 이전 경로의 위치로 돌아가지 않게 합니다.
        _restartRequired = true;
    }

    /// <summary>
    /// 실행본이 소유한 돌고래를 제거하고 참조를 정리합니다.
    /// </summary>
    public override void Dispose()
    {
        // 원본 에셋에서는 런타임 개체를 정리하지 않습니다.
        if (!IsRuntime) return;

        // 이후 이동과 재생성을 중단합니다.
        _running = false;

        // 무리 해제 뒤에는 예약된 물고기가 다시 생성되지 않게 합니다.
        ClearRespawnReservations();

        // 이 실행본이 생성한 돌고래만 제거합니다.
        if (_fish != null)
        {
            // 남은 이동을 멈춘 뒤 오브젝트를 제거합니다.
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
    /// 이동 계산에 사용할 수 있는 유한한 값인지 확인합니다.
    /// </summary>
    private static bool IsFinite(float value)
    {
        // NaN과 무한대가 위치와 속도에 전달되지 않도록 검사합니다.
        return !float.IsNaN(value) && !float.IsInfinity(value);
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