using UnityEngine;

/// <summary>
/// 무리의 곡선 경로, 자리 배치와 자리별 리스폰을 관리합니다.
/// 몰려다니는 물고기떼 패턴 config입니다.
/// </summary>
[CreateAssetMenu(fileName = "SkunkTilefish_Group", menuName = "Fish/Group")]
public class SchoolOfFish : FishSpawnPattern
{
    // 생성할 물고기 프리팹입니다.
    [SerializeField] private Fish _fishPrefab;

    // 무리 중심 기준 자리 목록입니다. 배열 길이가 유지할 마릿수입니다.
    [SerializeField]
    private Vector2[] _spawnOffsets =
    {
        new Vector2(0f, 0f),
        new Vector2(-1f, 0.5f),
        new Vector2(-1f, -0.5f),
        new Vector2(-2f, 0f)
    };


    // 최초 생성 시 선택한 무리의 월드 Z입니다.
    // 같은 무리의 물고기와 이후 리스폰에 동일하게 사용합니다.
    private float _spawnZ;

    // 개별 물고기가 따라잡을 여유를 남기는 중심 속도 비율입니다.
    [SerializeField, Range(0.1f, 0.9f)]
    private float _groupSpeedRatio = 0.65f;

    // 지정된 무리 자리에서 벗어난 거리를 따라잡는 강도입니다.
    [SerializeField, Min(0.1f)]
    private float _followStrength = 3f;

    // 다음 곡선의 끝점까지 이동할 최소 거리입니다.
    [SerializeField, Min(0.1f)]
    private float _minTravelDistance = 3f;

    // 다음 곡선의 끝점까지 이동할 최대 거리입니다.
    [SerializeField, Min(0.1f)]
    private float _maxTravelDistance = 6f;

    // 현재 진행 방향을 기준으로 다음 경로가 꺾일 수 있는 최대 각도입니다.
    [SerializeField, Range(0f, 180f)]
    private float _maxTurnAngle = 60f;

    // 목적지까지의 거리에 비례한 곡선 제어점 거리입니다.
    [SerializeField, Range(0.1f, 0.6f)]
    private float _curveRatio = 0.4f;

    // 자리 배치 전체가 진행 방향을 따라 회전하는 초당 각도입니다.
    [SerializeField, Min(1f)]
    private float _formationTurnSpeed = 120f;

    // 실제 생성과 명령 전달을 요청할 스포너입니다.
    private FishSpawner _spawner;

    // 자리별 물고기 참조입니다. 리스폰 대기는 공통 예약 목록이 관리합니다.
    private Fish[] _members;

    // 스포너에서 전달받은 리스폰 대기 시간입니다.
    private float _respawnDelay;

    // 초기화에 성공하여 실행 중인지 나타냅니다.
    private bool _running;

    // 무리 중심의 위치와 이동 속도입니다.
    private Vector2 _center;
    private Vector2 _velocity;

    // 벽 반전 직후에는 대형 보정이 다시 벽 쪽으로 끌어당기지 않도록 합니다.
    private bool _escapingObstacle;

    // 현재 대형 각도와 최초 배치의 기준 각도입니다.
    private float _headingAngle;
    private float _initialHeadingAngle;

    // 곡선의 시작점, 두 제어점, 도착점입니다.
    private Vector2 _p0;
    private Vector2 _p1;
    private Vector2 _p2;
    private Vector2 _p3;

    // 현재 곡선의 진행률과 진행 속도 계산에 사용할 값입니다.
    private float _curveProgress;
    private float _curveScale;

    // 다음 곡선의 출발 방향으로 이어갈 진행 방향입니다.
    private Vector2 _travelDirection;

    // 패턴이 생성할 프리팹에 연결된 데이터를 읽습니다.
    public override FishData Data => _fishPrefab != null ? _fishPrefab.Data : null;

    // 패턴 실행 횟수와 실제 물고기 마릿수를 구분하기 위한 생성 예정 수입니다.
    public override int InitialMemberCount => _spawnOffsets != null ? _spawnOffsets.Length : 0;


    /// <summary>
    /// 원형 영역 안에 무리 중심을 정하고 각 자리의 물고기를 생성합니다.
    /// </summary>
    public override bool Spawn(
    FishSpawner spawner,
    FishSpawnArea area,
    FishVerticalRange verticalRange,
    float respawnDelay)
    {
        // 원본 에셋 실행과 실행본의 중복 생성을 방지합니다.
        if (!IsRuntime || _members != null)
        {
            Debug.LogError("새 실행용 패턴에서 Spawn을 호출해주세요.", this);
            return false;
        }

        // 생성에 필요한 참조와 자리 목록을 검사합니다.
        if (area == null
            || verticalRange == null
            || spawner == null
            || _fishPrefab == null
            || !_fishPrefab.gameObject.activeSelf
            || Data == null
            || _spawnOffsets == null
            || _spawnOffsets.Length == 0)
        {
            Debug.LogError(
                "스포너, 활성 프리팹, FishData, 자리 목록을 확인해주세요.",
                this);

            return false;
        }

        // 최초 생성 위치를 결정할 원형 영역을 저장합니다.
        Area = area;

        // 패턴 경로와 무리 자리에 사용할 높이 제한을 저장합니다.
        VerticalRange = verticalRange;

        // 생성 요청 대상과 재생성 대기 시간을 저장합니다.
        _spawner = spawner;
        _respawnDelay = Mathf.Max(0f, respawnDelay);

        // 원 내부에서 무리의 초기 중심 위치를 선택합니다.
        _center = Area.ClampPosition(Area.GetRandomPosition(), VerticalRange);

        // 그룹 중심 오브젝트의 Z를 생성 깊이로 사용합니다.
        _spawnZ = Area.Center.z;

        // 최초 이동 속도와 진행 방향을 준비합니다.
        _velocity = Vector2.zero;
        _travelDirection = Data.InitialDirection;

        // 초기 방향이 지정되지 않았다면 오른쪽으로 시작합니다.
        if (_travelDirection.sqrMagnitude < 0.0001f)
        {
            _travelDirection = Vector2.right;
        }

        // 최초 방향을 대형 회전의 기준으로 사용합니다.
        _headingAngle = Mathf.Atan2(
            _travelDirection.y,
            _travelDirection.x) * Mathf.Rad2Deg;

        _initialHeadingAngle = _headingAngle;

        // 같은 인덱스가 같은 물고기 자리를 나타내도록 배열을 준비합니다.
        _members = new Fish[_spawnOffsets.Length];

        // 각 자리의 물고기를 생성하고 실패하면 스포너에서 정리하도록 반환합니다.
        for (int i = 0; i < _members.Length; i++)
        {
            if (!SpawnMember(i, true))
            {
                return false;
            }
        }

        // 최초 곡선을 구성하고 패턴 갱신을 시작합니다.
        SelectCurve();
        // 새 실행에 이전 리스폰 예약이 남지 않도록 초기화합니다.
        ClearRespawnReservations();
        _running = true;
        return true;
    }

    /// <summary>
    /// 무리 중심과 각 자리의 리스폰을 갱신하고 이동 명령을 전달합니다.
    /// </summary>
    public override void Tick(float deltaTime)
    {
        // 준비되지 않았거나 정지된 실행본은 갱신하지 않습니다.
        if (!_running || deltaTime <= 0f)
        {
            return;
        }

        // 무리가 구조물에 막히면 충돌면에서 멀어지는 방향으로 곡선을 다시 만듭니다.
        if (TryGetObstacleTurn(
            _members, _travelDirection, deltaTime, out int memberIndex, out Vector2 normal))
        {
            // 막힌 물고기의 실제 자리를 기준으로 가상 무리 중심을 복원합니다.
            _center = VerticalRange.ClampPosition(
                _center + _members[memberIndex].Position - GetSlotPosition(memberIndex));

            // 벽에 접근하던 실제 XY 방향을 완전히 반전합니다.
            _travelDirection = -ObstacleApproachDirection;

            // 방금 감지한 벽에서 새 경로를 시작하므로 과거 판단 간격만큼 앞서 이동하지 않습니다.
            deltaTime = Mathf.Min(deltaTime, Time.fixedDeltaTime);

            // 회피 직후에는 무작위 각도를 더하지 않고 구조물 반대편으로 경로를 만듭니다.
            SelectCurve(true);
        }

        MoveCenter(deltaTime);


        for (int i = 0; i < _members.Length; i++)
        {
            // 빈자리는 리스폰 예약이 관리하며 이 반복문은 이동 가능한 개체만 처리합니다.
            Fish fish = _members[i];
            if (fish == null || !fish.CanFollowGroup)
            {
                continue;
            }

            // 현재 대형에서 이 물고기가 있어야 할 자리를 구합니다.
            Vector2 targetPosition = GetSlotPosition(i);

            // 자기 자리에서 떨어진 만큼 따라잡기 위한 속도를 계산합니다.
            Vector2 correctionVelocity =
                (targetPosition - fish.Position) * _followStrength;

            // 무리 중심의 이동 속도에 자리 보정 속도를 더합니다.
            Vector2 desiredVelocity = _escapingObstacle
                ? _velocity
                : _velocity + correctionVelocity;

            // 방향과 속도를 전달하고 실제 물리 이동은 이동기에 맡깁니다.
            _spawner.Follow(
                fish,
                desiredVelocity.normalized,
                desiredVelocity.magnitude,
                deltaTime);
        }
    }

    /// <summary>
    /// 빈자리 전체를 검사하지 않고 만료된 예약의 물고기만 재생성합니다.
    /// </summary>
    public override void RespawnSlot(int slotIndex)
    {
        // 종료된 무리나 이미 채워진 자리는 재생성하지 않습니다.
        if (!_running || _members == null || slotIndex < 0 || slotIndex >= _members.Length
            || _members[slotIndex] != null)
            return;

        // 현재 대형의 빈자리에 기존 풀 대여 경로로 물고기를 보충합니다.
        if (!SpawnMember(slotIndex))
        {
            Pause();
            _running = false;
            ClearRespawnReservations();
            return;
        }

        // 새 개체가 오래된 속도 명령으로 움직이지 않게 즉시 판단을 예약합니다.
        RequestDecision();
    }

    /// <summary>
    /// 곡선을 따라 중심을 이동시키고 대형 방향을 갱신합니다.
    /// </summary>
    private void MoveCenter(float deltaTime)
    {
        // 중심은 개별 물고기보다 느리게 이동합니다.
        float speed = Mathf.Max(0f, Data.MoveSpeed) * _groupSpeedRatio;
        float remainingTime = deltaTime;
        Vector2 previous = _center;

        // 곡선 끝에 도착해도 남은 시간은 다음 곡선에 사용합니다.
        while (remainingTime > 0f && speed > 0f)
        {
            if (_curveProgress >= 1f)
            {
                SelectCurve();
            }

            // 곡선의 순간 속도가 설정 속도를 넘지 않도록 진행률을 구합니다.
            float progressPerSecond = speed / _curveScale;
            float timeToEnd = (1f - _curveProgress) / progressPerSecond;
            float step = Mathf.Min(remainingTime, timeToEnd);

            _curveProgress = Mathf.Min(1f, _curveProgress + progressPerSecond * step);

            remainingTime -= step;

            // 끝에 도착했을 때 부동소수점 오차 없이 완료 처리합니다.
            if (step >= timeToEnd)
            {
                _curveProgress = 1f;
            }

            float t = _curveProgress;
            float u = 1f - t;

            // 네 점으로 구성된 3차 베지어 곡선의 위치를 계산합니다.
            _center =
                u * u * u * _p0
                + 3f * u * u * t * _p1
                + 3f * u * t * t * _p2
                + t * t * t * _p3;

            // 접선은 이 위치에서 곡선이 향하는 방향입니다.
            Vector2 tangent =
                3f * u * u * (_p1 - _p0)
                + 6f * u * t * (_p2 - _p1)
                + 3f * t * t * (_p3 - _p2);

            if (tangent.sqrMagnitude > 0.0001f)
            {
                _travelDirection = tangent.normalized;
            }
        }

        // 실제 이동량으로 물고기에게 전달할 중심 속도를 계산합니다.
        _velocity = (_center - previous) / deltaTime;

        if (_velocity.sqrMagnitude > 0.0001f)
        {
            float targetAngle = Mathf.Atan2(
                _velocity.y, _velocity.x) * Mathf.Rad2Deg;

            // 자리 배치가 한 번에 뒤집히지 않도록 회전 속도를 제한합니다.
            _headingAngle = Mathf.MoveTowardsAngle(
                _headingAngle,
                targetAngle,
                _formationTurnSpeed * deltaTime);
        }
    }

    /// <summary>
    /// 현재 무리 위치와 진행 방향을 기준으로 다음 이동 곡선을 생성합니다.
    /// </summary>
    private void SelectCurve(bool avoidingObstacle = false)
    {
        // 탈출 경로 전체에서 자리 보정을 중단하고 반대 방향으로 먼저 거리를 확보합니다.
        _escapingObstacle = avoidingObstacle;
        // 현재 무리 중심을 새 곡선의 시작점으로 사용합니다.
        _p0 = VerticalRange.ClampPosition(_center);

        // 진행 방향이 없는 경우 기본 방향을 사용합니다.
        Vector2 forward = _travelDirection.sqrMagnitude > 0.0001f
            ? _travelDirection.normalized
            : Vector2.right;

        // 현재 진행 방향의 각도를 계산합니다.
        float currentAngle = Mathf.Atan2(forward.y, forward.x);

        // 회피 직후에는 반대 방향을 유지하고 평상시에만 무작위 각도를 적용합니다.
        float turnAngle = avoidingObstacle
            ? 0f
            : Random.Range(-_maxTurnAngle, _maxTurnAngle) * Mathf.Deg2Rad;

        // 변화량을 적용한 다음 이동 방향을 계산합니다.
        float nextAngle = currentAngle + turnAngle;
        Vector2 direction = new Vector2(
            Mathf.Cos(nextAngle),
            Mathf.Sin(nextAngle));

        // 설정 순서가 뒤바뀌어도 유효한 이동 거리 범위를 계산합니다.
        float minDistance = Mathf.Max(
            0.1f,
            Mathf.Min(_minTravelDistance, _maxTravelDistance));

        float maxDistance = Mathf.Max(
            minDistance,
            Mathf.Max(_minTravelDistance, _maxTravelDistance));

        // 생성 영역과 독립적으로 다음 끝점까지의 거리를 선택합니다.
        float distance = Random.Range(minDistance, maxDistance);

        // 현재 위치에서 다음 방향으로 떨어진 끝점을 설정합니다.
        _p3 = VerticalRange.ClampPosition(_p0 + direction * distance);

        // 수직 방향으로만 진행하다 경계에 막힌 경우 수평 경로를 선택합니다.
        if ((_p3 - _p0).sqrMagnitude < 0.0001f)
        {
            // 이전 수평 방향을 유지하며 길이가 있는 경로를 확보합니다.
            direction = forward.x < 0f ? Vector2.left : Vector2.right;
            _p3 = _p0 + direction * distance;
        }

        // 이동 거리를 기준으로 곡선 제어점의 길이를 계산합니다.
        float handleLength = distance * _curveRatio;

        // 곡선에 좌우 변화를 주기 위한 수직 방향을 계산합니다.
        Vector2 side = new Vector2(-direction.y, direction.x);

        // 출발 지점에서는 이전 진행 방향을 이어가도록 설정합니다.
        _p1 = VerticalRange.ClampPosition(_p0 + forward * handleLength);

        // 도착 지점 앞에 좌우 변화가 있는 제어점을 설정합니다.
        _p2 = VerticalRange.ClampPosition(
            _p3 - direction * handleLength
            + side * Random.Range(-handleLength, handleLength));

        // 벽 회피는 임의의 휘어짐 없이 완전히 반대 방향의 직선 경로로 만듭니다.
        if (avoidingObstacle)
        {
            _p1 = Vector2.Lerp(_p0, _p3, 1f / 3f);
            _p2 = Vector2.Lerp(_p0, _p3, 2f / 3f);
        }

        // 새 곡선의 시작부터 이동하도록 진행도를 초기화합니다.
        _curveProgress = 0f;

        // 기존 이동 계산에서 사용할 곡선 크기를 계산합니다.
        _curveScale = Mathf.Max(
            0.001f,
            3f * Mathf.Max(
                Vector2.Distance(_p0, _p1),
                Mathf.Max(
                    Vector2.Distance(_p1, _p2),
                    Vector2.Distance(_p2, _p3))));
    }

    /// <summary>
    /// 무리의 회전을 반영한 각 물고기의 목표 위치를 반환합니다.
    /// </summary>
    private Vector2 GetSlotPosition(int index)
    {
        // 초기 방향에서 현재 방향까지의 회전량을 계산합니다.
        float angle = (_headingAngle - _initialHeadingAngle) * Mathf.Deg2Rad;

        // 자리 오프셋 회전에 사용할 삼각함수 값을 계산합니다.
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        // 해당 물고기의 기본 자리 오프셋을 가져옵니다.
        Vector2 offset = _spawnOffsets[index];

        // 회전한 오프셋을 무리 중심에 더한 뒤 높이만 보정합니다.
        return VerticalRange.ClampPosition(_center + new Vector2(
            offset.x * cos - offset.y * sin,
            offset.x * sin + offset.y * cos));
    }

    /// <summary>
    /// 무리 자리에 물고기를 생성하고 최초 생성 시에만 생성 영역 안으로 보정합니다.
    /// </summary>
    private bool SpawnMember(int index, bool isInitialSpawn = false)
    {
        // 최초 생성과 리스폰 모두 현재 대형의 같은 자리를 사용합니다.
        Vector2 slotPosition = GetSlotPosition(index);

        // 최초 생성에서만 각 물고기의 위치를 생성 원 안으로 보정합니다.
        if (isInitialSpawn)
        {
            // 자리 오프셋 때문에 원 밖에 놓인 위치를 안쪽으로 제한합니다.
            slotPosition = Area.ClampPosition(slotPosition, VerticalRange);
        }

        // XY 자리 좌표와 최초 선택한 Z를 결합합니다.
        Vector3 position = new Vector3(
            slotPosition.x,
            slotPosition.y,
            _spawnZ);

        // 계산한 자리와 진행 방향을 전달하여 물고기를 생성합니다.
        if (!_spawner.SpawnFish(
            _fishPrefab,
            position,
            _travelDirection,
            VerticalRange,
            Brightness,
            SpawnLayer,
            out Fish fish))
        {
            // 생성에 실패한 자리는 성공으로 처리하지 않습니다.
            return false;
        }

        // 생성된 물고기를 등록하고 해당 자리의 대기 상태를 초기화합니다.
        _members[index] = fish;
        // 반환 즉시 이전 대여의 소유 참조를 해제합니다.
        fish.Released += OnMemberReleased;

        // 개별 감지 결과를 이 무리의 정확한 자리로 전달하도록 연결합니다.
        fish.BindGroupPattern(this, index);

        return true;
    }

    /// <summary>
    /// 현재 패턴을 따르는 물고기들의 일반 이동을 정지합니다.
    /// </summary>
    public override void Pause()
    {
        if (_members == null)
        {
            return;
        }

        // 전투나 작살 행동이 제어하는 이동에는 간섭하지 않습니다.
        foreach (Fish fish in _members)
        {
            if (fish != null && fish.CanFollowGroup)
            {
                fish.Stop();
            }
        }

        _velocity = Vector2.zero;
    }

    /// <summary>
    /// 실행을 종료하고 패턴이 소유한 물고기를 정리합니다.
    /// </summary>
    public override void Dispose()
    {
        // 원본 에셋에는 실행 종료 처리를 하지 않습니다.
        if (!IsRuntime)
        {
            return;
        }

        _running = false;

        // 무리 해제 뒤에는 예약된 물고기가 다시 생성되지 않게 합니다.
        ClearRespawnReservations();

        if (_members == null)
        {
            return;
        }

        // 일부만 생성된 경우에도 등록된 물고기는 모두 정리합니다.
        foreach (Fish fish in _members)
        {
            if (fish != null)
            {
                fish.Stop();
                fish.Release();
            }
        }

        // 실행 중에 사용한 참조를 해제합니다.
        _members = null;
        _spawner = null;
    }
    /// <summary>
    /// 풀에 반환된 물고기를 현재 패턴의 소유 자리에서 즉시 해제합니다.
    /// </summary>
    private void OnMemberReleased(Fish fish)
    {
        // 이전 대여의 반환 이벤트 연결을 해제합니다.
        fish.Released -= OnMemberReleased;
        if (_members == null)
        {
            return;
        }

        // 반환 이벤트가 발생했을 때만 이 무리 안에서 해당 자리를 찾습니다.
        for (int i = 0; i < _members.Length; i++)
        {
            // 반환된 개체가 소유했던 자리만 처리합니다.
            if (!ReferenceEquals(_members[i], fish))
            {
                continue;
            }

            // 재대여된 객체가 이전 무리에서 사용되지 않게 참조를 즉시 해제합니다.
            _members[i] = null;

            // 무리 해제 중 반환은 예약하지 않고 정상 실행 중 소실만 예약합니다.
            if (_running)
            {
                ScheduleRespawn(i, _respawnDelay);
            }

            // 하나의 개체는 이 무리의 한 자리만 소유합니다.
            return;
        }
    }
}