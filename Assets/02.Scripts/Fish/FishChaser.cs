using UnityEngine;

/// <summary>
/// 모델의 설정으로 접근 감속과 후퇴 명령을 계산하고 공격 쿨타임을 처리합니다.
/// </summary>
public class FishChaser
{
    // 외부에서 주입받은 추격 대상과 설정 제공 모델입니다.
    private Hero _hero;
    private FishModel _model;

    // Hero가 범위 밖에 있을 때 추격 목표를 보정할 공통 월드 XY 경계입니다.
    private Vector2 _movementMin;
    private Vector2 _movementMax;

    // 감지한 Hero를 즉시 추격 범위 밖으로 판정하지 않기 위한 최소 거리입니다.
    private float _minimumChaseDistance;

    // 기존 설정을 유지하면서 감지 반경 이상은 추격할 수 있게 합니다.
    private float ChaseDistance =>
        Mathf.Max(_model.MaxChaseDistance, _minimumChaseDistance);

    // 실제 공격 성공 후 남은 쿨타임입니다.
    private float _cooltimeRemaining;

    // 쿨타임 중에는 이동 여부와 별도로 공격을 금지합니다.
    public bool IsCoolingDown => _cooltimeRemaining > 0f;

    /// <summary>
    /// 추격 대상, 모델, 전역 이동 범위와 최소 추격 거리를 주입합니다.
    /// </summary>
    public void Initialize(
        Hero hero,
        FishModel model,
        Vector2 movementMin,
        Vector2 movementMax,
        float minimumChaseDistance)
    {
        // 대상 검색 없이 전달받은 참조만 사용합니다.
        _hero = hero;
        _model = model;

        // 추격 대상의 실제 거리와 별개로 이동 목표에 적용할 경계를 저장합니다.
        _movementMin = Vector2.Min(movementMin, movementMax);
        _movementMax = Vector2.Max(movementMin, movementMax);

        // 감지 반경이 기존 추격 거리보다 커도 추격을 시작할 수 있게 합니다.
        _minimumChaseDistance = Mathf.Max(0f, minimumChaseDistance);

        // 새 개체는 공격 쿨타임 없이 시작합니다.
        _cooltimeRemaining = 0f;
    }

    /// <summary>
    /// 플레이어와의 XYZ 거리가 모델에 설정된 최대 추격 거리를 넘었는지 확인합니다.
    /// </summary>
    public bool IsOutsideChaseRange(Vector3 position)
    {
        // 대상이나 설정이 없으면 거리 초과로 단정하지 않습니다.
        if (_hero == null || _model == null)
        {
            return false;
        }

        // 기존 추격 규칙과 동일하게 Z 차이도 포함하여 거리를 비교합니다.
        // 기존 설정과 감지 반경을 함께 반영한 최대 추격 거리를 사용합니다.
        float maxDistance = ChaseDistance;
        Vector3 displacement = _hero.transform.position - position;
        return displacement.sqrMagnitude > maxDistance * maxDistance;
    }

    /// <summary>
    /// 공격 후 쿨타임을 모두 마친 뒤 전투 시간 만료나 추격 거리 이탈에 따라 전투를 종료합니다.
    /// </summary>
    public bool ShouldEndCombat(float combatRemaining, Vector3 position)
    {
        // 공격 후 후퇴와 대기가 남아 있으면 일반 이동으로 복귀하지 않습니다.
        if (IsCoolingDown)
        {
            return false;
        }

        // 쿨타임이 끝난 경우에만 기존 시간 및 거리 종료 조건을 적용합니다.
        return combatRemaining <= 0f || IsOutsideChaseRange(position);
    }

    /// <summary>
    /// 전투 종료 시 공격 쿨타임을 초기화해 다음 전투에 남지 않게 합니다.
    /// </summary>
    public void ResetAttackCooltime()
    {
        // 이전 전투에서 남은 공격 쿨타임을 제거합니다.
        _cooltimeRemaining = 0f;
    }

    /// <summary>
    /// 히트박스 표면으로의 감속 접근과 몸체 중심에서 멀어지는 쿨타임 후퇴를 계산합니다.
    /// </summary>
    public bool TryGetMove(
        Vector3 position,
        Collider targetHitbox,
        float deltaTime,
        float slowDistance,
        float retreatTargetDistance,
        out Vector2 direction,
        out float speed)
    {
        // 추격할 수 없는 경우에는 정지 명령을 반환합니다.
        direction = Vector2.zero;
        speed = 0f;

        // 유효한 대상과 실제 몸체 히트박스, 모델, 갱신 시간이 필요합니다.
        if (_hero == null
            || !_hero.isActiveAndEnabled
            || targetHitbox == null
            || !targetHitbox.enabled
            || !targetHitbox.gameObject.activeInHierarchy
            || _model == null
            || deltaTime <= 0f)
        {
            return false;
        }

        // 공격 후 대기 시간을 갱신하고 대기 중 후퇴 이동은 허용합니다.
        if (_cooltimeRemaining > 0f)
        {
            // 공격 금지 시간을 줄이되 이동 계산은 계속하여 후퇴를 허용합니다.
            _cooltimeRemaining = Mathf.Max(0f, _cooltimeRemaining - deltaTime);
        }

        // 추격 범위 밖으로 물러나더라도 쿨타임 중에는 후퇴와 대기를 끝까지 처리합니다.
        if (!IsCoolingDown && IsOutsideChaseRange(position))
        {
            return false;
        }

        // 물고기 중심에서 실제 몸체 히트박스의 가장 가까운 표면점을 구합니다.
        Vector3 target = targetHitbox.ClosestPoint(position);

        // 표면이 맵 경계 밖에 있더라도 기존 이동 허용 범위를 유지합니다.
        target.x = Mathf.Clamp(target.x, _movementMin.x, _movementMax.x);
        target.y = Mathf.Clamp(target.y, _movementMin.y, _movementMax.y);

        // 실제 추격 이동은 보정된 목표와의 XY 차이로 계산합니다.
        Vector2 planarDisplacement =
            new Vector2(target.x - position.x, target.y - position.y);

        // 모델의 추격 속도와 접근 감속을 시작할 기준 거리를 계산합니다.
        float chaseSpeed = Mathf.Max(0f, _model.MoveSpeed + _model.AdditionalChaseSpeed);
        float slowdownDistance = Mathf.Max(0f, slowDistance);

        // 공격 후 대기 중에는 실제 플레이어 위치를 기준으로 거리를 벌립니다.
        if (IsCoolingDown)
        {
            // 중심 간 거리로 계산하던 후퇴 규칙을 실제 몸체 중심에 적용합니다.
            Vector3 bodyCenter = targetHitbox.bounds.center;

            // 발 쪽 루트 대신 몸체 중심에서 멀어지는 방향으로 후퇴합니다.
            Vector2 away = new Vector2(
                position.x - bodyCenter.x,
                position.y - bodyCenter.y);
            float distance = away.magnitude;
            float remainingRetreat = Mathf.Max(
                0f, Mathf.Max(0f, retreatTargetDistance) - distance);

            // 중심이 겹친 경우에도 후퇴 방향을 결정할 수 있도록 기본 방향을 둡니다.
            direction = distance > 0.0001f ? away / distance : Vector2.right;

            // 설정한 간격까지 물러난 뒤에는 남은 대기 시간 동안 정지합니다.
            speed = Mathf.Min(chaseSpeed, remainingRetreat / deltaTime);
            return true;
        }

        // 물고기 중심과 추격 목표 사이의 거리를 계산합니다.
        float approachDistance = planarDisplacement.magnitude;

        // 목표가 중심과 겹쳤을 때는 이동 방향을 만들지 않습니다.
        direction = approachDistance > 0.0001f
            ? planarDisplacement / approachDistance
            : Vector2.zero;

        // 감속 기준 거리 밖에서는 1, 히트박스 표면에서는 0이 되는 비율을 계산합니다.
        float distanceRatio = slowdownDistance > 0.0001f
            ? Mathf.Clamp01(approachDistance / slowdownDistance)
            : 1f;

        // 근접 시 추격 속도를 줄이되 공격 전에 거리 조건만으로 정지하지 않습니다.
        float approachSpeed = chaseSpeed * Mathf.Lerp(0.2f, 1f, distanceRatio);

        // 한 프레임에 목표 표면을 넘어가지 않도록 최종 속도를 제한합니다.
        speed = Mathf.Min(approachSpeed, approachDistance / deltaTime);

        // 실제 공격 성공 여부와 쿨타임 시작은 Fish에서 처리합니다.
        return true;
    }

    /// <summary>
    /// 실제 공격 성공 시 모델에 설정된 공격 쿨타임을 시작합니다.
    /// </summary>
    public void BeginAttackCooltime()
    {
        // 설정은 모델에서 읽고 남은 시간만 추격 객체에 보관합니다.
        _cooltimeRemaining = _model != null ? _model.Cooltime : 0f;
    }
}