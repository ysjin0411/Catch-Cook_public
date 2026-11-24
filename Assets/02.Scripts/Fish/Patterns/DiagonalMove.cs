using UnityEngine;

/// <summary>
/// 두 마리의 물고기가 지정한 대각선 구간을 왕복하며,
/// 자리별 소실과 리스폰을 관리합니다.
/// </summary>
[CreateAssetMenu(fileName = "DiagonalMove", menuName = "Fish/Diagonal Move")]
public class DiagonalMove : FishSpawnPattern
{
    /// <summary>
    /// 대각선 왕복 경로의 축을 구분합니다.
    /// </summary>
    public enum DiagonalAxis
    {
        NorthWestSouthEast,
        NorthEastSouthWest
    }

    // 생성할 물고기 프리팹입니다.
    [SerializeField] private Fish _fishPrefab;

    // 무리 중심을 기준으로 두 물고기가 유지할 자리입니다.
    [SerializeField]
    private Vector2[] _spawnOffsets =
    {
        new Vector2(-0.75f, 0f),
        new Vector2(0.75f, 0f)
    };

    // 왕복할 대각선 축입니다.
    [SerializeField]
    private DiagonalAxis _axis = DiagonalAxis.NorthWestSouthEast;

    // 두 끝점 사이의 거리입니다. 한 번 왕복하면 이 거리의 두 배를 이동합니다.
    [SerializeField, Min(0.1f)]
    private float _travelDistance = 12f;

    // 처음에 북쪽 끝점을 향할지 여부입니다.
    [SerializeField]
    private bool _startTowardNorth = true;

    // 남쪽 끝점에서 북쪽 끝점을 향하는 정규화된 경로 방향입니다.
    private Vector2 _pathAxis;

    // 최초 생성 시 확정한 경로의 기준점입니다.
    private Vector2 _pathOrigin;

    // 기준점에서 각 끝점까지의 부호 있는 거리입니다.
    private float _pathMin;
    private float _pathMax;

    // 기준점에서 현재 무리 중심까지의 부호 있는 거리입니다.
    private float _pathPosition;

    // 북쪽으로 진행하면 1, 남쪽으로 진행하면 -1입니다.
    private int _travelSign;

    // 개별 물고기가 자기 자리를 따라잡을 여유를 남기는 중심 속도 비율입니다.
    [SerializeField, Range(0.1f, 0.9f)]
    private float _groupSpeedRatio = 0.65f;

    // 자기 자리에서 떨어진 거리를 보정하는 강도입니다.
    [SerializeField, Min(0.1f)]
    private float _followStrength = 3f;

    // 실제 생성과 이동 명령을 전달할 스포너입니다.
    private FishSpawner _spawner;

    // 자리별 물고기 참조입니다.
    private Fish[] _members;

    // 자리별 리스폰 대기 여부와 남은 시간입니다.

    // 현재 무리 중심과 실제 중심 이동 속도입니다.
    private Vector2 _center;
    private Vector2 _velocity;

    // 무리 중심의 현재 대각선 진행 방향입니다.
    private Vector2 _direction;

    // 최초 선택한 무리의 Z입니다. 리스폰에도 같은 값을 사용합니다.
    private float _spawnZ;

    // 물고기가 소실된 후 다시 생성하기까지 기다릴 시간입니다.
    private float _respawnDelay;

    // 초기화에 성공하여 실행 중인지 나타냅니다.
    private bool _running;

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
        // 원본 에셋 실행과 같은 실행본의 중복 초기화를 방지합니다.
        if (!IsRuntime || _spawner != null)
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
            || _spawnOffsets.Length != 2)
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

        // 두 자리와 이동 범위를 고려하여 대각선 왕복 경로를 준비합니다.
        if (!InitializePath())
        {
            // 유효한 경로가 없으면 물고기를 생성하지 않습니다.
            return false;
        }

        // 이동 전에는 중심 속도를 0으로 초기화합니다.
        _velocity = Vector2.zero;

        // 동일한 인덱스가 동일한 자리를 나타내도록 배열을 준비합니다.
        _members = new Fish[_spawnOffsets.Length];

        // 각 자리의 물고기를 생성합니다.
        for (int i = 0; i < _members.Length; i++)
        {
            // 실패하면 스포너가 Dispose를 호출하여 일부 생성물도 정리합니다.
            if (!SpawnMember(i, true))
            {
                return false;
            }
        }

        // 모든 자리의 생성이 끝나면 갱신을 시작합니다.
        // 새 실행에 이전 리스폰 예약이 남지 않도록 초기화합니다.
        ClearRespawnReservations();
        _running = true;
        return true;
    }

    /// <summary>
    /// 중심의 대각선 왕복 이동과 자리별 리스폰을 갱신하고 추종 명령을 전달합니다.
    /// </summary>
    public override void Tick(float deltaTime)
    {
        // 준비되지 않은 실행본과 유효하지 않은 시간은 처리하지 않습니다.
        if (!_running || deltaTime <= 0f)
        {
            return;
        }

        // 무리 진행을 막는 구조물이 있으면 실제 물고기 위치를 기준으로 되돌아갑니다.
        if (TryGetObstacleTurn(
            _members, _direction, deltaTime, out int memberIndex, out _))
        {
            // 막힌 물고기의 자리 오프셋을 빼서 실제 무리 중심을 추정합니다.
            Vector2 actualCenter =
                _members[memberIndex].Position - _spawnOffsets[memberIndex];

            // 중심을 기존 경로에 투영하여 장애물 접촉으로 경로가 옆으로 밀리지 않게 합니다.
            _pathPosition = Mathf.Clamp(
                Vector2.Dot(actualCenter - _pathOrigin, _pathAxis), _pathMin, _pathMax);
            _center = _pathOrigin + _pathAxis * _pathPosition;

            // 대각선 축은 유지하고 진행 방향만 반전합니다.
            _travelSign = -_travelSign;
            _direction = _pathAxis * _travelSign;
        }

        // 물고기들의 목표 자리를 계산하기 전에 중심을 이동시킵니다.
        MoveCenter(deltaTime);


        // 각 자리의 이동 가능한 물고기만 갱신합니다.
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

            // 자리에서 떨어진 만큼 따라잡을 보정 속도를 계산합니다.
            Vector2 correctionVelocity =
                (targetPosition - fish.Position) * _followStrength;

            // 중심의 이동 속도에 자리 보정 속도를 더합니다.
            Vector2 desiredVelocity = _velocity + correctionVelocity;

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
    /// 두 자리 전체가 생성 원과 이동 범위 안에 들어가는 대각선 왕복 경로를 구성합니다.
    /// </summary>
    private bool InitializePath()
    {
        // 선택한 대각선에서 남쪽 끝점부터 북쪽 끝점을 향하는 방향을 정합니다.
        _pathAxis = (_axis == DiagonalAxis.NorthWestSouthEast
            ? new Vector2(-1f, 1f) : new Vector2(1f, 1f)).normalized;

        // 두 자리 전체가 이동 범위 안에 있도록 무리 중심의 허용 범위를 구합니다.
        Vector2 minOffset = Vector2.Min(_spawnOffsets[0], _spawnOffsets[1]);
        Vector2 maxOffset = Vector2.Max(_spawnOffsets[0], _spawnOffsets[1]);
        Vector2 centerMin = _spawner.MovementMin - minOffset;
        Vector2 centerMax = _spawner.MovementMax - maxOffset;
        centerMin.y = Mathf.Max(centerMin.y, VerticalRange.MinY - minOffset.y);
        centerMax.y = Mathf.Min(centerMax.y, VerticalRange.MaxY - maxOffset.y);

        // 두 자리가 들어갈 공간이 없으면 설정 오류를 알리고 중단합니다.
        if (centerMin.x >= centerMax.x || centerMin.y >= centerMax.y)
        {
            Debug.LogError("두 물고기의 간격과 그룹 이동 범위를 확인해주세요.", this);
            return false;
        }

        // 생성 원의 가장자리 때문에 두 자리가 겹쳐 보정되지 않도록 후보를 재선택합니다.
        for (int attempt = 0; attempt < 64; attempt++)
        {
            // 첫 후보는 Spawn에서 선택한 위치, 마지막 후보는 생성 원의 중심을 사용합니다.
            Vector2 candidate = attempt == 0 ? _center
                : attempt == 63 ? Area.CenterXY : Area.GetRandomPosition();

            // 두 자리의 중심 좌표가 맵과 그룹의 높이 범위 안에 있는지 확인합니다.
            if (candidate.x < centerMin.x || candidate.x > centerMax.x
                || candidate.y < centerMin.y || candidate.y > centerMax.y)
            {
                continue;
            }

            // 최초 생성 시 두 물고기 모두 생성 원 안에 있어야 합니다.
            bool fitsSpawnArea = true;
            for (int i = 0; i < _spawnOffsets.Length; i++)
            {
                // 개별 위치를 잘라 간격을 변경하는 대신 대형 전체가 들어가는지 검사합니다.
                if ((candidate + _spawnOffsets[i] - Area.CenterXY).sqrMagnitude
                    > Area.Radius * Area.Radius)
                {
                    fitsSpawnArea = false;
                    break;
                }
            }

            // 두 자리가 들어가지 않는 후보는 사용하지 않습니다.
            if (!fitsSpawnArea)
            {
                continue;
            }

            // 각 경계와 대각선이 만나는 위치를 경로상의 부호 있는 거리로 환산합니다.
            float xA = (centerMin.x - candidate.x) / _pathAxis.x;
            float xB = (centerMax.x - candidate.x) / _pathAxis.x;
            float yA = (centerMin.y - candidate.y) / _pathAxis.y;
            float yB = (centerMax.y - candidate.y) / _pathAxis.y;

            // 설정한 편도 거리 안에서 맵과 수직 경계까지 경로를 제한합니다.
            float halfDistance = Mathf.Max(0.1f, _travelDistance) * 0.5f;
            _pathMin = Mathf.Max(-halfDistance, Mathf.Max(Mathf.Min(xA, xB), Mathf.Min(yA, yB)));
            _pathMax = Mathf.Min(halfDistance, Mathf.Min(Mathf.Max(xA, xB), Mathf.Max(yA, yB)));

            // 왕복 구간이 거의 없는 후보는 제외합니다.
            if (_pathMax - _pathMin < 0.1f)
            {
                continue;
            }

            // 유효한 후보를 고정 경로의 기준점으로 저장하고 최초 진행 방향을 정합니다.
            _center = candidate;
            _pathOrigin = candidate;
            _pathPosition = 0f;
            _travelSign = _startTowardNorth ? 1 : -1;
            _direction = _pathAxis * _travelSign;
            return true;
        }

        // 생성 원과 이동 범위에 두 자리를 배치하지 못했다면 잘못된 위치에 생성하지 않습니다.
        Debug.LogError("대각선 두 마리의 생성 공간이 부족합니다. 생성 반경과 이동 범위를 확인해주세요.", this);
        return false;
    }

    /// <summary>
    /// 대각선 경로의 끝점 사이에서 무리 중심을 이동시키고 끝점에서 방향을 반전합니다.
    /// </summary>
    private void MoveCenter(float deltaTime)
    {
        // 유효한 시간이 아니면 중심을 이동하지 않습니다.
        if (deltaTime <= 0f)
        {
            return;
        }

        // 개별 물고기가 자기 자리를 따라잡을 여유를 남겨 중심 속도를 정합니다.
        float speed = Mathf.Max(0f, Data.MoveSpeed) * _groupSpeedRatio;
        float targetPosition = _travelSign > 0 ? _pathMax : _pathMin;

        // 끝점을 넘어가지 않도록 경로 위의 위치를 이동시킵니다.
        Vector2 previousCenter = _center;
        _pathPosition = Mathf.MoveTowards(_pathPosition, targetPosition, speed * deltaTime);
        _center = _pathOrigin + _pathAxis * _pathPosition;

        // 자리 추종에는 이번 갱신의 실제 중심 이동 속도를 사용합니다.
        _velocity = (_center - previousCenter) / deltaTime;

        // 끝점에 도착하면 다음 갱신부터 반대 끝점을 향합니다.
        if (Mathf.Abs(_pathPosition - targetPosition) <= 0.0001f)
        {
            // 장애물 검사와 리스폰에도 새 진행 방향을 전달합니다.
            _travelSign = -_travelSign;
            _direction = _pathAxis * _travelSign;
        }
    }

    /// <summary>
    /// 무리 중심에 자리 오프셋을 더한 목표 위치를 반환합니다.
    /// </summary>
    private Vector2 GetSlotPosition(int index)
    {
        // 경로에서 검증한 대형의 자리를 구하고 높이 오차를 제한합니다.
        return VerticalRange.ClampPosition(_center + _spawnOffsets[index]);
    }

    /// <summary>
    /// 무리 자리에 물고기를 생성하고 최초 생성 시에만 생성 영역 안으로 보정합니다.
    /// </summary>
    private bool SpawnMember(int index, bool isInitialSpawn = false)
    {
        // 최초 생성과 리스폰 모두 현재 대형의 자리를 사용합니다.
        Vector2 slotPosition = GetSlotPosition(index);

        // 최초 생성에서만 각 물고기의 위치를 생성 원 안으로 보정합니다.
        if (isInitialSpawn)
        {
            // 자리 오프셋 때문에 원 밖에 놓인 위치를 안쪽으로 제한합니다.
            slotPosition = Area.ClampPosition(slotPosition, VerticalRange);
        }

        // 자리의 XY와 최초 선택한 무리의 Z를 결합합니다.
        Vector3 position = new Vector3(
            slotPosition.x,
            slotPosition.y,
            _spawnZ);

        // 계산한 자리와 진행 방향을 전달하여 물고기를 생성합니다.
        if (!_spawner.SpawnFish(
            _fishPrefab,
            position,
            _direction,
            VerticalRange,
            Brightness,
            SpawnLayer,
            out Fish fish))
        {
            // 생성에 실패한 자리는 성공으로 처리하지 않습니다.
            return false;
        }

        // 생성한 물고기를 등록하고 해당 자리의 대기를 종료합니다.
        _members[index] = fish;
        // 반환 즉시 이전 대여의 소유 참조를 해제합니다.
        fish.Released += OnMemberReleased;

        // 개별 감지 결과를 이 무리의 정확한 자리로 전달하도록 연결합니다.
        fish.BindGroupPattern(this, index);

        return true;
    }

    /// <summary>
    /// 패턴이 담당하는 물고기들의 평상시 이동을 일시 정지합니다.
    /// </summary>
    public override void Pause()
    {
        // 초기화 이전에는 정지할 물고기가 없습니다.
        if (_members == null)
        {
            return;
        }

        // 다른 행동이 제어하는 물고기의 이동에는 개입하지 않습니다.
        foreach (Fish fish in _members)
        {
            if (fish != null && fish.CanFollowGroup)
            {
                fish.Stop();
            }
        }

        // 일시 정지 전의 중심 속도를 초기화합니다.
        _velocity = Vector2.zero;
    }

    /// <summary>
    /// 실행을 종료하고 이 패턴이 소유한 물고기와 참조를 정리합니다.
    /// </summary>
    public override void Dispose()
    {
        // 원본 에셋에는 실행 종료 처리를 하지 않습니다.
        if (!IsRuntime)
        {
            return;
        }

        // 이후의 이동과 리스폰 갱신을 중단합니다.
        _running = false;

        // 무리 해제 뒤에는 예약된 물고기가 다시 생성되지 않게 합니다.
        ClearRespawnReservations();

        // 일부만 생성된 경우에도 등록된 물고기를 모두 정리합니다.
        if (_members != null)
        {
            foreach (Fish fish in _members)
            {
                if (fish != null)
                {
                    fish.Stop();
                    fish.Release();
                }
            }
        }

        // 실행 중 사용한 참조와 중심 속도를 초기화합니다.
        _members = null;
        _spawner = null;
        _velocity = Vector2.zero;
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