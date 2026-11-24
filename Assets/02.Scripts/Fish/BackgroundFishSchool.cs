using UnityEngine;

/// <summary>
/// 개별 물리 처리 없이 부모 대형 하나를 이동시키고 지형을 회피합니다.
/// 초기 생성 후에는 물고기 배열을 매 프레임 순회하지 않습니다.
/// </summary>
public sealed class BackgroundFishSchool : MonoBehaviour
{
    // 초기화와 생성이 성공한 무리만 갱신합니다.
    private bool _initialized;

    // 모든 자리와 물고기 몸체를 포함하는 월드 검사 반지름입니다.
    private float _radius;

    // 무리의 크기까지 고려한 중심 이동 범위입니다.
    private Vector2 _centerMin;
    private Vector2 _centerMax;

    // 배경의 이동 속도와 무리 전체에 적용할 경로 종류입니다.
    private float _speed;
    private BackgroundFishMovement _movement;

    // 물결 경로의 위상과 진행 방향을 보관해 벽에서 연속적으로 되돌아갑니다.
    private float _wavePhase;
    private float _waveAmplitude;
    private float _waveLength;
    private float _horizontalSign;

    // 경로 변경 거리, 최대 변경 각도와 각 회전 속도입니다.
    private Vector2 _travelDistance;
    private float _maxTurnAngle;
    private float _pathTurnSpeed;
    private float _visualTurnSpeed;

    // 현재 경로와 목표 경로의 각도 및 다음 방향 변경까지 남은 시간입니다.
    private float _heading;
    private float _targetHeading;
    private float _turnRemaining;
    private float _targetYaw;

    // 실제 지형 레이어와 벽에서 떨어져 유지할 거리입니다.
    private LayerMask _obstacleMask;
    private float _clearance;

    // 검증과 디버깅에 사용할 최초 생성 마릿수입니다.
    public int MemberCount { get; private set; }

    /// <summary>
    /// 기존 그룹의 생성 원, 높이, 간격과 외형 크기를 보존해 무리를 생성합니다.
    /// </summary>
    public bool Initialize(BackgroundFish prefab, Vector2[] offsets,
        FishSpawnArea area, FishVerticalRange height, Vector2 movementMin,
        Vector2 movementMax, float brightness, int layer, float speed,
        Vector2 initialDirection, BackgroundFishMovement movement, Vector2 travelDistance,
        float maxTurnAngle, float pathTurnSpeed, float visualTurnSpeed,
        LayerMask obstacleMask, float clearance,
        float diagonalAngle, float waveAmplitude, float waveLength)
    {
        // 부모 배율이 있으면 월드 단위 검사 크기와 자리 간격이 달라집니다.
        if (_initialized || (transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
        {
            Debug.LogError("배경 무리는 중복 초기화할 수 없으며 스포너 배율은 1이어야 합니다.", this);
            return false;
        }

        // 자리 배치의 가운데를 구해 지나치게 큰 회피 구체를 만들지 않습니다.
        Vector2 minOffset = offsets[0];
        Vector2 maxOffset = offsets[0];
        for (int i = 1; i < offsets.Length; i++)
        {
            minOffset = Vector2.Min(minOffset, offsets[i]);
            maxOffset = Vector2.Max(maxOffset, offsets[i]);
        }
        Vector2 offsetCenter = (minOffset + maxOffset) * 0.5f;

        // 회전 중에도 모든 자리와 수영 중 몸체를 포함하도록 반지름을 계산합니다.
        float farthestSquared = 0f;
        for (int i = 0; i < offsets.Length; i++)
            farthestSquared = Mathf.Max(farthestSquared, (offsets[i] - offsetCenter).sqrMagnitude);
        _radius = Mathf.Sqrt(farthestSquared) + prefab.BodyRadius;

        // 공통 이동과 지형 검사 설정을 한 번만 저장합니다.
        _speed = Mathf.Max(0.01f, speed);
        _movement = movement;
        _waveLength = Mathf.Max(1f, waveLength);
        _wavePhase = 0f;
        _travelDistance = new Vector2(Mathf.Max(0.1f, Mathf.Min(travelDistance.x, travelDistance.y)),
            Mathf.Max(0.1f, Mathf.Max(travelDistance.x, travelDistance.y)));
        _maxTurnAngle = Mathf.Clamp(maxTurnAngle, 0f, 90f);
        _pathTurnSpeed = Mathf.Max(1f, pathTurnSpeed);
        _visualTurnSpeed = Mathf.Max(1f, visualTurnSpeed);
        _obstacleMask = obstacleMask;
        _clearance = Mathf.Max(0.001f, clearance);

        // 무리 전체가 원래 그룹의 높이와 전역 이동 범위 안에 있도록 제한합니다.
        float margin = _radius + _clearance;
        _centerMin = new Vector2(movementMin.x + margin,
            Mathf.Max(movementMin.y, height.MinY) + margin);
        _centerMax = new Vector2(movementMax.x - margin,
            Mathf.Min(movementMax.y, height.MaxY) - margin);
        if (_centerMin.x > _centerMax.x || _centerMin.y > _centerMax.y)
        {
            Debug.LogError("배경 무리의 크기가 설정한 이동 범위보다 큽니다.", this);
            return false;
        }

        // 원래 생성 원에서 지형과 겹치지 않는 대형 중심을 찾습니다.
        if (!TryFindSpawnPosition(area, height, offsetCenter, out Vector3 position))
        {
            Debug.LogError("배경 생성 원에 무리 전체가 들어갈 빈 공간이 없습니다: " + name, this);
            return false;
        }

        // 최초 방향을 기준으로 자리 배치가 원래 월드 XY 방향을 유지하게 합니다.
        Vector2 direction = initialDirection.sqrMagnitude > 0.0001f
            ? initialDirection.normalized : Vector2.right;
        float sign = direction.x < 0f ? -1f : 1f;
        _horizontalSign = sign;
        if (_movement == BackgroundFishMovement.Horizontal || _movement == BackgroundFishMovement.Wave)
            direction = Vector2.right * sign;
        else if (_movement == BackgroundFishMovement.Diagonal)
        {
            // 수평을 기준으로 선택한 대각선의 양 끝을 왕복합니다.
            float angle = Mathf.Clamp(diagonalAngle, -75f, 75f) * Mathf.Deg2Rad;
            direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * sign;
        }
        // 생성 높이를 중심으로 물결 전체가 허용 수심 안에 들어오게 진폭을 제한합니다.
        _waveAmplitude = Mathf.Max(0f, Mathf.Min(waveAmplitude,
            Mathf.Min(position.y - _centerMin.y, _centerMax.y - position.y)));
        if (_movement == BackgroundFishMovement.Wave)
            direction = new Vector2(sign, sign * _waveAmplitude * 2f * Mathf.PI / _waveLength).normalized;
        _heading = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        _targetHeading = _heading;
        _targetYaw = sign * 90f;
        // 최초 생성부터 상승·하강 방향까지 반영한 자세를 사용합니다.
        Quaternion initialRotation = GetFacingRotation(direction);
        transform.SetPositionAndRotation(position, initialRotation);

        // 모든 자리에서 재사용할 초기 회전의 역회전을 한 번만 계산합니다.
        Quaternion inverseInitialRotation = Quaternion.Inverse(initialRotation);

        // 자식은 기존 프리팹 배율을 보존하며 부모의 이동과 회전을 공유합니다.
        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2 offset = offsets[i] - offsetCenter;
            BackgroundFish fish = Instantiate(prefab, transform, false);
            fish.name = prefab.name + "_" + i;
            // 부모의 초기 기울기와 관계없이 설정한 자리를 월드 XY에 유지합니다.
            Vector3 worldOffset = new Vector3(offset.x, offset.y, 0f);
            fish.transform.localPosition = inverseInitialRotation * worldOffset;
            fish.transform.localRotation = Quaternion.identity;

            // 기존 그룹 레이어를 유지하되 배경 프리팹에는 Collider가 없습니다.
            foreach (Transform child in fish.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
            fish.Initialize(brightness);
        }

        // 실제 생성 수를 기록하고 경로 변경 예약 후 갱신을 시작합니다.
        MemberCount = offsets.Length;
        ScheduleNextTurn();
        _initialized = true;
        return true;
    }

    /// <summary>
    /// 생성 시에만 원래 원형 영역 안에서 지형과 겹치지 않는 위치를 찾습니다.
    /// </summary>
    private bool TryFindSpawnPosition(FishSpawnArea area, FishVerticalRange height,
        Vector2 offsetCenter, out Vector3 position)
    {
        // 실패 시 반환할 위치는 원래 SpawnPoint입니다.
        position = area.Center;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            // 마지막 시도에서는 랜덤 오차 없이 원래 생성 중심도 확인합니다.
            Vector2 anchor = attempt == 63 ? area.CenterXY : area.GetRandomPosition();
            anchor = area.ClampPosition(anchor, height);
            Vector2 center = anchor + offsetCenter;

            // 범위를 벗어난 후보를 다른 위치로 강제로 옮기지 않고 제외합니다.
            if (center.x < _centerMin.x || center.x > _centerMax.x
                || center.y < _centerMin.y || center.y > _centerMax.y) continue;
            Vector3 candidate = new Vector3(center.x, center.y, area.Center.z);

            // 시작부터 겹친 Collider는 Sweep에서 놓칠 수 있어 따로 검사합니다.
            if (Physics.CheckSphere(candidate, _radius + _clearance,
                _obstacleMask, QueryTriggerInteraction.Ignore)) continue;
            position = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 개체별 순회 없이 무리 중심의 이동과 한 번의 지형 검사를 처리합니다.
    /// </summary>
    private void Update()
    {
        // 초기화 실패 또는 게임 일시 정지 중에는 이동하지 않습니다.
        float deltaTime = Time.deltaTime;
        if (!_initialized || deltaTime <= 0f) return;

        // 경로 판단은 무리 하나당 한 번만 수행합니다.
        _turnRemaining -= deltaTime;
        if (_movement == BackgroundFishMovement.RandomCurve && _turnRemaining <= 0f)
        {
            _targetHeading = _heading + Random.Range(-_maxTurnAngle, _maxTurnAngle);
            ScheduleNextTurn();
        }
        else if (_movement == BackgroundFishMovement.Diagonal && _turnRemaining <= 0f)
        {
            // 지정한 이동 거리를 진행하면 같은 직선을 따라 반대편으로 돌아갑니다.
            _heading += 180f;
            _targetHeading = _heading;
            ScheduleNextTurn();
        }
        _heading = Mathf.MoveTowardsAngle(_heading, _targetHeading, _pathTurnSpeed * deltaTime);
        float radians = _heading * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

        // 물결은 수평 진행 거리로 위상을 계산하여 프레임률과 무관한 진폭을 유지합니다.
        float requestedDistance = _speed * deltaTime;
        float phaseStep = 0f;
        if (_movement == BackgroundFishMovement.Wave)
        {
            float dx = _horizontalSign * requestedDistance;
            phaseStep = dx * 2f * Mathf.PI / _waveLength;
            float dy = _waveAmplitude * (Mathf.Sin(_wavePhase + phaseStep) - Mathf.Sin(_wavePhase));
            Vector2 step = new Vector2(dx, dy);
            requestedDistance = step.magnitude;
            direction = step.normalized;
        }

        // 요청 이동을 월드 범위까지로 제한합니다.
        Vector3 position = transform.position;
        float distance = LimitToBounds(position, direction, requestedDistance, out Vector2 normal);
        Vector3 displacementDirection = new Vector3(direction.x, direction.y, 0f);

        // 전체 이동 구간을 검사하므로 낮은 FPS에서도 벽까지의 거리만 이동합니다.
        if (Physics.SphereCast(position, _radius, displacementDirection, out RaycastHit hit,
            distance + _clearance, _obstacleMask, QueryTriggerInteraction.Ignore))
        {
            float safeDistance = Mathf.Max(0f, hit.distance - _clearance);
            if (safeDistance <= distance)
            {
                distance = safeDistance;
                normal = new Vector2(hit.normal.x, hit.normal.y);
                normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : -direction;
            }
        }
        transform.position = position + displacementDirection * distance;
        // 벽 때문에 일부 거리만 이동했다면 실제 진행한 만큼만 물결 위상을 소비합니다.
        if (_movement == BackgroundFishMovement.Wave && requestedDistance > 0f)
            _wavePhase = Mathf.Repeat(_wavePhase + phaseStep * (distance / requestedDistance), 2f * Mathf.PI);

        // 벽과 맵 경계에서는 대형 전체가 함께 반사 방향으로 이동합니다.
        if (normal.sqrMagnitude > 0.0001f)
        {
            direction = _movement == BackgroundFishMovement.RandomCurve
                ? Vector2.Reflect(direction, normal).normalized : -direction;
            // 물결은 현재 위상에서 되돌아가므로 벽 반전 때 높이가 튀지 않습니다.
            if (_movement == BackgroundFishMovement.Wave) _horizontalSign = -_horizontalSign;
            _heading = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            _targetHeading = _heading;
            ScheduleNextTurn();
        }

        // 좌우와 상승·하강 방향을 반영한 무리의 목표 자세를 계산합니다.
        Quaternion targetRotation = GetFacingRotation(direction);

        // 부모 하나만 부드럽게 회전시켜 모든 자식 물고기에 함께 적용합니다.
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            targetRotation, _visualTurnSpeed * deltaTime);
    }

    /// <summary>
    /// XY 이동 방향에 맞춰 좌우 회전과 상승·하강 기울기를 계산합니다.
    /// </summary>
    private Quaternion GetFacingRotation(Vector2 direction)
    {
        // 이동 방향이 없으면 현재 자세를 유지합니다.
        if (direction.sqrMagnitude < 0.0001f)
        {
            return transform.rotation;
        }

        // 수직에 가까운 이동에서는 기존 좌우 방향을 유지해 잦은 반전을 막습니다.
        if (direction.x > 0.05f)
        {
            _targetYaw = 90f;
        }
        else if (direction.x < -0.05f)
        {
            _targetYaw = -90f;
        }

        // 현재 바라보는 쪽을 기준으로 상승·하강 각도를 계산합니다.
        float facingSign = _targetYaw >= 0f ? 1f : -1f;
        float tilt = Mathf.Atan2(
            direction.y * facingSign,
            direction.x * facingSign) * Mathf.Rad2Deg;

        // 기존 모델의 좌우 회전 기준에 월드 Z축 기울기를 조합합니다.
        Quaternion yawRotation = Quaternion.Euler(0f, _targetYaw, 0f);
        Quaternion tiltRotation = Quaternion.AngleAxis(tilt, Vector3.forward);

        // 좌우 방향을 맞춘 모델을 이동 경사의 방향으로 기울입니다.
        return tiltRotation * yawRotation;
    }

    /// <summary>
    /// 무리 중심이 허용 범위를 넘지 않는 거리와 경계의 반사 방향을 구합니다.
    /// </summary>
    private float LimitToBounds(Vector3 position, Vector2 direction,
        float distance, out Vector2 normal)
    {
        // 경계가 없으면 요청 거리를 그대로 사용합니다.
        normal = Vector2.zero;
        float allowed = distance;
        if (Mathf.Abs(direction.x) > 0.0001f)
        {
            // X축 진행 방향의 경계까지 남은 경로 길이를 구합니다.
            float edge = direction.x > 0f ? _centerMax.x : _centerMin.x;
            float edgeDistance = Mathf.Max(0f, (edge - position.x) / direction.x);
            if (edgeDistance <= allowed)
            {
                allowed = edgeDistance;
                normal = direction.x > 0f ? Vector2.left : Vector2.right;
            }
        }
        if (Mathf.Abs(direction.y) > 0.0001f)
        {
            // Y축 경계가 더 가까우면 그 경계까지만 이동합니다.
            float edge = direction.y > 0f ? _centerMax.y : _centerMin.y;
            float edgeDistance = Mathf.Max(0f, (edge - position.y) / direction.y);
            if (edgeDistance <= allowed)
            {
                allowed = edgeDistance;
                normal = direction.y > 0f ? Vector2.down : Vector2.up;
            }
        }
        return allowed;
    }

    /// <summary>
    /// 기존 이동 거리와 속도를 이용해 다음 방향 변경 간격을 정합니다.
    /// </summary>
    private void ScheduleNextTurn()
    {
        // 이동 속도가 달라도 비슷한 거리를 진행한 후 방향을 변경합니다.
        _turnRemaining = Random.Range(_travelDistance.x, _travelDistance.y) / _speed;
    }

    /// <summary>
    /// 실행 중 선택한 무리의 실제 지형 검사 범위를 표시합니다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // 초기화된 대형의 회피 구체만 표시합니다.
        if (!_initialized) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _radius + _clearance);
    }
}
