using UnityEngine;

/// <summary>
/// 물리 이동과 진행 방향 및 외형 회전을 담당합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FishMover : MonoBehaviour
{
    // 외형 전체를 담는 자식입니다. 로컬 +Z가 머리 방향입니다.
    [SerializeField] private Transform _visual;

    // 이동 속도가 바뀔 수 있는 초당 최대 변화량입니다.
    [SerializeField, Min(0.1f)] private float _acceleration = 8f;

    // 모델이 좌우 방향을 바꾸는 초당 회전 각도입니다.
    [SerializeField, Min(1f)] private float _visualTurnSpeed = 360f;

    // 전투 중 Head 방향을 Hero에게 맞추는 초당 최대 회전 각도입니다.
    [SerializeField, Min(1f)] private float _combatTurnSpeed = 360f;

    // 방향 전환 중 Z축을 음수로 기울일 최대 각도입니다.
    [SerializeField, Range(0f, 30f)] private float _turnTiltAngle = 10f;

    // Z축 기울기가 변하고 원래대로 돌아오는 초당 각도입니다.
    [SerializeField, Min(1f)] private float _tiltSpeed = 60f;

    // 일반 수영에서 유지할 모델의 초기 월드 X각도입니다.
    private float _swimPitch;

    // Euler 값을 매번 읽지 않고 직접 관리하는 모델의 Y각도입니다.
    private float _swimYaw;

    // 방향 전환 중 적용할 Z축 기울기입니다.
    private float _swimTilt;

    // 좌우 Y회전과 별개로 Hero의 높이를 바라보는 월드 Z축 기울기입니다.
    private float _combatAimAngle;

    // 곡선 패턴에서 이동 방향의 상하 각도까지 모델 회전에 반영할지 나타냅니다.
    private bool _facePatternMovement;

    // 방향 전환을 일으키는 구조물의 Platform 레이어 번호입니다.
    private int _platformLayer;

    // 몸체 Collider 전방에서 Platform을 감지할 여유 거리입니다.
    [SerializeField, Min(0.1f)] private float _obstacleCheckDistance = 5f;

    // 실제 접촉 및 경계 반응과 별개로 전방 Sweep 검사 간격을 설정합니다.
    [SerializeField, Min(0.02f)] private float _obstacleProbeInterval = 0.1f;

    // 다음 전방 검사를 실행할 물리 시간입니다.
    private double _nextObstacleProbeTime;

    // 실제 벽 검사 결과를 Scene 뷰에 표시할지 설정합니다.
    [SerializeField] private bool _showObstacleGizmos = true;

    // 마지막 벽 검사의 시작점, 방향과 전체 거리를 저장합니다.
    private Vector3 _obstacleGizmoOrigin;
    private Vector3 _obstacleGizmoDirection;
    private float _obstacleGizmoDistance;

    // 회피 대상으로 선택한 벽의 접촉 지점과 XY 법선을 저장합니다.
    private Vector3 _obstacleGizmoHitPoint;
    private Vector3 _obstacleGizmoNormal;
    private bool _obstacleGizmoHit;

    // 검사가 중단되면 오래된 표시를 숨기기 위한 시간입니다.
    private float _obstacleGizmoTime = float.NegativeInfinity;

    // 물리 충돌로 속도가 줄어들기 전에 요청한 XY 이동 속도입니다.
    private Vector2 _requestedVelocity;

    // 벽 보고 시 실제 XY 접근 방향을 제공합니다.
    public Vector2 RequestedVelocity => _requestedVelocity;

    // 다음 패턴 갱신에서 한 번 소비할 구조물 접촉면의 방향입니다.
    private Vector2 _obstacleNormal;
    private bool _hasObstacleContact;

    // 마지막 유효한 단위 이동 방향입니다. 정지 요청 중에도 감속 방향을 유지합니다.
    private Vector2 _headingDirection = Vector2.right;

    // 같은 이동 명령의 정규화를 반복하지 않도록 원본 방향을 보관합니다.
    private Vector2 _headingInput = Vector2.right;

    // 실제 물리 이동을 담당하는 컴포넌트입니다.
    private Rigidbody _rigidbody;

    // 생성 원과 독립적으로 적용하는 월드 Y 이동 범위입니다.
    private FishVerticalRange _verticalRange;

    // 전투와 복귀 이동 중에는 높이 제한을 잠시 해제합니다.
    private bool _verticalRangeEnabled = true;

    // 스포너에서 주입받는 전역 XY 경계이며 Combat 중에도 항상 적용합니다.
    private Vector2 _movementMin;
    private Vector2 _movementMax;
    private float _boundaryTurnDistance;
    private bool _hasMovementBounds;

    // 수직 이동에 가까울 때 좌우 회전이 떨리지 않도록 방향을 유지합니다.
    private bool _facesRight = true;

    // 포획 또는 사망으로 직접 위치를 제어 중인지 나타냅니다.
    private bool _bodyHeld;

    // 포획 취소 시 복원할 원래 물리 설정입니다.
    private bool _wasKinematic;
    private RigidbodyInterpolation _previousInterpolation;



    public bool IsReady => _rigidbody != null && _visual != null;   // 움직일 준비완료상태
    public Vector3 WorldPosition => _rigidbody.position;        // 추격 거리 계산에 사용하는 Rigidbody의 월드 XYZ 위치입니다.
    public Vector2 Position => (Vector2)_rigidbody.position;     // 보간된 외형 위치가 아닌 Rigidbody의 실제 XY 위치입니다.


    /// <summary>
    /// 물리 이동 컴포넌트와 방향 전환 대상 레이어를 준비합니다.
    /// </summary>
    private void Awake()
    {
        // 풀에서 먼저 준비한 참조와 초기 자세를 다시 덮어쓰지 않습니다.
        EnsureReady();
    }

    /// <summary>
    /// Awake 이전의 풀 대여에서도 물리 참조와 초기 외형 자세를 한 번만 준비합니다.
    /// </summary>
    public void EnsureReady()
    {
        // 최초 초기화에서 저장한 자세를 다음 대여 때 다시 읽지 않습니다.
        if (_rigidbody != null) return;

        // 물리 컴포넌트 참조는 무버 내부에서만 관리합니다.
        _rigidbody = GetComponent<Rigidbody>();

        // Inspector의 이전 마스크 설정과 관계없이 Platform만 방향 전환에 사용합니다.
        _platformLayer = LayerMask.NameToLayer("Platform");

        // 모델의 초기 X각도를 일반 수영 중 유지할 기준으로 저장합니다.
        if (_visual != null)
        {
            _swimPitch = _visual.eulerAngles.x;
            _swimYaw = _visual.eulerAngles.y;
        }
    }

    /// <summary>
    /// 스포너가 지정한 전역 이동 범위와 사전 방향 전환 거리를 저장합니다.
    /// </summary>
    public void SetMovementBounds(Vector2 min, Vector2 max, float turnDistance)
    {
        // 좌표 순서를 정리하고 모든 생존 이동에 사용할 경계를 활성화합니다.
        _movementMin = Vector2.Min(min, max);
        _movementMax = Vector2.Max(min, max);
        _boundaryTurnDistance = Mathf.Max(0f, turnDistance);
        _hasMovementBounds = true;
    }

    /// <summary>
    /// 명령이 없는 프레임에도 외부 물리 힘으로 전역 범위를 벗어나지 않게 합니다.
    /// </summary>
    private void FixedUpdate()
    {
        // 사망 부유와 작살 포획 중의 외부 위치 제어는 기존 흐름을 따릅니다.
        if (!_hasMovementBounds || _rigidbody == null
            || _bodyHeld || _rigidbody.isKinematic)
        {
            return;
        }

        // 현재 물리 속도에도 전역 경계를 적용하고 Z 이동을 차단합니다.
        Vector2 velocity = (Vector2)_rigidbody.linearVelocity;
        LimitMovementBounds(ref velocity, Time.fixedDeltaTime);
        _rigidbody.linearVelocity = new Vector3(velocity.x, velocity.y, 0f);
    }

    /// <summary>
    /// 생성 원과 독립적으로 적용할 월드 Y 이동 범위를 설정합니다.
    /// </summary>
    public void SetVerticalRange(FishVerticalRange verticalRange)
    {
        // 그룹에서 만든 높이 제한을 일반 이동과 넉백에 공유합니다.
        _verticalRange = verticalRange;
    }

    /// <summary>
    /// 저장된 높이 범위는 유지하면서 실제 Y 이동 제한만 켜거나 끕니다.
    /// </summary>
    public void SetVerticalRangeEnabled(bool enabled)
    {
        // Combat과 복귀 이동에서도 원래 높이 범위 자체는 보관합니다.
        _verticalRangeEnabled = enabled;
    }

    /// <summary>
    /// 다음 물리 프레임의 경로 목표를 향해 가속 없이 이동하고 기존 경계 제한을 적용합니다.
    /// </summary>
    public bool MoveAlongPattern(Vector2 targetPosition, float deltaTime, bool faceMovement = false)
    {
        // 물리 이동이 불가능하거나 외부에서 위치를 제어 중이면 중단합니다.
        if (_rigidbody == null || _rigidbody.isKinematic || _bodyHeld || deltaTime <= 0f)
        {
            return false;
        }

        // 잘못된 좌표와 시간으로 물리 속도가 손상되지 않도록 검사합니다.
        if (float.IsNaN(targetPosition.x) || float.IsInfinity(targetPosition.x)
            || float.IsNaN(targetPosition.y) || float.IsInfinity(targetPosition.y)
            || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
        {
            return false;
        }

        // 다음 목표까지 필요한 속도를 계산하고 기존 경계 및 충돌 기록을 적용합니다.
        // 이 패턴에서만 상하 진행 방향을 바라보도록 선택합니다.
        _facePatternMovement = faceMovement;

        // 실제 목표 위치로 이동할 속도를 계산합니다.
        Vector2 velocity = (targetPosition - Position) / deltaTime;
        ApplyVelocity(velocity, deltaTime);

        // 패턴이 진행 시간을 갱신할 수 있도록 이동 요청 성공을 알립니다.
        return true;
    }

    /// <summary>
    /// 유효한 명령의 이동 방향을 즉시 반영하고 속력은 가속도에 맞춰 변경합니다.
    /// </summary>
    public void Move(Vector2 direction, float speed, float deltaTime)
    {
        // 물리 이동이 가능한 상태와 유효한 시간인지 확인합니다.
        if (_rigidbody == null
            || _rigidbody.isKinematic
            || deltaTime <= 0f)
        {
            return;
        }

        // 방향이 없는 명령은 정지 요청으로 처리합니다.
        bool hasDirection = direction.sqrMagnitude >= 0.0001f;
        float targetSpeed = hasDirection ? Mathf.Max(0f, speed) : 0f;

        // 유효한 이동 요청의 방향이 바뀐 경우에만 단위 방향을 갱신합니다.
        if (hasDirection && targetSpeed > 0f && !direction.Equals(_headingInput))
        {
            // 작은 방향 변화도 놓치지 않도록 원본 벡터를 정확히 비교하여 보관합니다.
            _headingInput = direction;

            // 각도 왕복 변환 없이 단위 방향을 저장하며 외형 회전과 분리합니다.
            _headingDirection = direction.normalized;
        }

        // 현재 속도의 크기를 가져와 가속도 계산에 사용합니다.
        Vector2 currentVelocity = (Vector2)_rigidbody.linearVelocity;

        // 방향과 별개로 이동 속도의 크기만 서서히 변경합니다.
        float nextSpeed = Mathf.MoveTowards(
            currentVelocity.magnitude,
            targetSpeed,
            Mathf.Max(0f, _acceleration) * deltaTime);

        // 저장된 방향으로 이동하며 방향 없는 명령에서도 기존 방향으로 감속합니다.
        ApplyVelocity(_headingDirection * nextSpeed, deltaTime);
    }

    /// <summary>
    /// 실제 머리 방향과 목표 방향의 일치 정도에 따라 전투 접근 속도를 조절합니다.
    /// </summary>
    public void MoveCombat(
        Transform head,
        Transform tail,
        Vector2 direction,
        float speed,
        float deltaTime)
    {
        // 외부에서 몸을 제어하거나 물리 이동이 불가능하면 중단합니다.
        if (_rigidbody == null || _rigidbody.isKinematic || _bodyHeld || deltaTime <= 0f)
        {
            return;
        }

        // 정지 명령이거나 유효한 머리 방향이 없으면 이전 속도를 즉시 제거합니다.
        if (direction.sqrMagnitude < 0.0001f || speed <= 0f || !CanAlignBody(head, tail))
        {
            Stop();
            return;
        }

        // 실제 꼬리에서 머리로 향하는 축을 화면의 XY 평면에 투영합니다.
        Vector3 bodyAxis = head.position - tail.position;
        Vector2 planarAxis = new Vector2(bodyAxis.x, bodyAxis.y);

        // 화면 안쪽을 바라보는 회전 중에는 이동을 멈추고 회전을 기다립니다.
        if (planarAxis.sqrMagnitude < 0.0001f)
        {
            Stop();
            return;
        }

        // 60도 이상 어긋나면 정지하고 15도 이내에서는 전체 추격 속도를 사용합니다.
        float angle = Vector2.Angle(planarAxis, direction);
        float facingRatio = Mathf.InverseLerp(60f, 15f, angle);

        // 머리가 목표를 향하지 않는 동안 이전 프레임의 이동 속도도 제거합니다.
        if (facingRatio <= 0f)
        {
            Stop();
            return;
        }

        // 추격기가 정한 최대 이동량을 넘지 않도록 가속 없이 제한된 속도를 적용합니다.
        ApplyVelocity(direction.normalized * speed * facingRatio, deltaTime);
    }

    /// <summary>
    /// 초기 이동 방향과 모델의 좌우 방향을 설정하고 기울기를 초기화합니다.
    /// </summary>
    public void SetFacing(Vector2 direction)
    {
        // 유효한 방향이 없으면 오른쪽을 초기 방향으로 사용합니다.
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector2.right;
        }

        // 풀 재사용을 포함해 초기 이동 명령과 단위 방향을 함께 설정합니다.
        _headingInput = direction;
        _headingDirection = direction.normalized;

        // 수직 방향으로 생성되면 기존 좌우 방향을 유지합니다.
        if (Mathf.Abs(direction.x) > 0.01f)
        {
            _facesRight = direction.x > 0f;
        }

        // 생성할 때는 회전 연출 없이 초기 방향을 바로 적용합니다.
        _swimYaw = _facesRight ? 90f : -90f;
        _swimTilt = 0f;
        _combatAimAngle = 0f;

        // 초기 X각도를 유지하고 좌우 방향만 초기화합니다.
        if (_visual != null)
        {
            _visual.rotation = Quaternion.Euler(_swimPitch, _swimYaw, 0f);
        }
    }

    /// <summary>
    /// 실제 이동 방향에 맞춰 모델의 좌우 회전과 Z축 기울기를 갱신합니다.
    /// </summary>
    public void UpdateFacing()
    {
        // 필요한 참조가 준비된 경우에만 처리합니다.
        if (_visual == null || _rigidbody == null)
        {
            return;
        }

        // 현재 물리 이동 방향을 모델 회전의 기준으로 사용합니다.
        Vector2 velocity = (Vector2)_rigidbody.linearVelocity;

        // 정지 중에도 남아 있는 회전과 기울기 복귀를 처리합니다.
        UpdateSwimRotation(velocity, Time.deltaTime);
    }

    /// <summary>
    /// 초기 X각도를 유지하며 Y축으로 회전하고,
    /// 회전 중 Z축을 음수로 기울였다가 원래대로 복귀시킵니다.
    /// </summary>
    private void UpdateSwimRotation(Vector2 direction, float deltaTime)
    {
        // 외부에서 몸체 자세를 제어하거나 시간이 흐르지 않으면 종료합니다.
        if (_visual == null || _bodyHeld || deltaTime <= 0f)
        {
            return;
        }

        // 수직 이동이나 정지 중에는 마지막 좌우 방향을 유지합니다.
        if (Mathf.Abs(direction.x) > 0.01f)
        {
            _facesRight = direction.x > 0f;
        }

        // 진행 방향에 해당하는 목표 Y각도를 계산합니다.
        float targetYaw = _facesRight ? 90f : -90f;

        // 모델의 Y각도를 목표 방향으로 부드럽게 회전시킵니다.
        _swimYaw = Mathf.MoveTowardsAngle(
            _swimYaw, targetYaw, Mathf.Max(1f, _visualTurnSpeed) * deltaTime);

        // 남은 회전각을 0에서 1 사이의 비율로 변환합니다.
        float remainingRatio = Mathf.Clamp01(
            Mathf.Abs(Mathf.DeltaAngle(_swimYaw, targetYaw)) / 180f);

        // 회전 시작과 끝에서는 0, 중간에서는 최대 음수 기울기를 만듭니다.
        float targetTilt =
            -Mathf.Sin(remainingRatio * Mathf.PI) * Mathf.Clamp(_turnTiltAngle, 0f, 30f);

        // 기울기를 부드럽게 변경하고 회전 종료 후에는 0으로 복귀시킵니다.
        _swimTilt = Mathf.MoveTowards(
            _swimTilt, targetTilt, Mathf.Max(1f, _tiltSpeed) * deltaTime);

        // 정지 중에는 마지막으로 바라보던 기울기를 유지합니다.
        float targetAim = _combatAimAngle;

        // 실제 이동 중이라면 패턴 종류와 관계없이 이동 방향을 바라봅니다.
        if (direction.sqrMagnitude > 0.0001f)
        {
            // 수평 이동량 대비 수직 이동량으로 위아래 기울기를 계산합니다.
            float slope = Mathf.Atan2(direction.y, Mathf.Abs(direction.x))
                * Mathf.Rad2Deg;

            // 왼쪽을 보는 모델은 같은 높이 방향을 향하도록 Z축 회전 부호를 반전합니다.
            targetAim = _facesRight ? slope : -slope;
        }

        // 일반 수영의 회전 속도로 이동 방향의 기울기를 부드럽게 따라갑니다.
        _combatAimAngle = Mathf.MoveTowards(
            _combatAimAngle, targetAim,

            Mathf.Max(1f, _visualTurnSpeed) * deltaTime);

        // 기존 수영 자세에 남아 있는 조준 기울기를 합쳐 복귀 시 자세가 튀지 않게 합니다.
        _visual.rotation = Quaternion.AngleAxis(_combatAimAngle, Vector3.forward)
            * Quaternion.Euler(_swimPitch, _swimYaw, _swimTilt);
    }

    /// <summary>
    /// 동적 Rigidbody에 남아 있는 이동과 회전 속도를 제거합니다.
    /// </summary>
    public void Stop()
    {
        // 초기화 이전이나 물리 이동이 고정된 상태에서는 속도를 쓰지 않습니다.
        if (_rigidbody == null || _rigidbody.isKinematic)
        {
            return;
        }

        // 정지 이전 행동에서 발생한 구조물 접촉은 다음 행동에 넘기지 않습니다.
        _requestedVelocity = Vector2.zero;
        _obstacleNormal = Vector2.zero;
        _hasObstacleContact = false;

        // 이전 행동의 이동과 회전이 남지 않도록 정지합니다.
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// 요청한 방향과 속도를 향해 가속하며 XY 이동합니다.
    /// </summary>
    public void MoveFree(Vector2 direction, float speed, float deltaTime)
    {
        // 물리 이동이 가능한 경우에만 처리합니다.
        if (_rigidbody == null || deltaTime <= 0f)
        {
            return;
        }

        // 요청한 방향과 속도로 목표 속도를 만듭니다.
        Vector2 desiredVelocity =
            direction.normalized * Mathf.Max(0f, speed);

        // 현재 XY 속도에서 목표 속도로 가속합니다.
        Vector2 velocity = (Vector2)_rigidbody.linearVelocity;

        velocity = Vector2.MoveTowards(
            velocity,
            desiredVelocity,
            _acceleration * deltaTime);

        // 다른 이동과 동일하게 월드 Y 제한을 적용합니다.
        ApplyVelocity(velocity, deltaTime);
    }

    /// <summary>
    /// 넉백처럼 즉시 변경해야 하는 XY 속도를 적용합니다.
    /// </summary>
    public void SetVelocity(Vector2 velocity)
    {
        // 현재 넉백 호출이 이루어지는 물리 시간 간격을 사용합니다.
        ApplyVelocity(velocity, Time.fixedDeltaTime);
    }

    /// <summary>
    /// 전역 XY 경계와 현재 활성화된 그룹 Y 제한을 적용해 이동 속도를 보정합니다.
    /// </summary>
    private void ApplyVelocity(Vector2 velocity, float deltaTime)
    {
        // 물리 이동이 가능하고 유효한 시간 간격인 경우에만 처리합니다.
        if (_rigidbody == null
            || _rigidbody.isKinematic
            || deltaTime <= 0f)
        {
            return;
        }

        // 높이 제한이 연결된 경우에만 Y 위치와 속도를 보정합니다.
        if (_verticalRangeEnabled && _verticalRange != null)
        {
            // 충돌 등 외부 요인으로 범위를 벗어난 현재 높이를 보정합니다.
            Vector3 position = _rigidbody.position;
            float clampedY = Mathf.Clamp(
                position.y,
                _verticalRange.MinY,
                _verticalRange.MaxY);

            // 현재 X와 Z를 유지하며 범위 밖의 Y만 복원합니다.
            if (position.y != clampedY)
            {
                position.y = clampedY;
                _rigidbody.position = position;
            }

            // 다음 물리 갱신에서 도달할 높이를 범위 안으로 제한합니다.
            float nextY = Mathf.Clamp(
                position.y + velocity.y * deltaTime,
                _verticalRange.MinY,
                _verticalRange.MaxY);

            // 경계까지 남은 거리만 이동하도록 Y 속도를 보정합니다.
            velocity.y = (nextY - position.y) / deltaTime;
        }

        // Combat과 복귀 중에도 맵의 전역 XY 경계는 항상 적용합니다.
        LimitMovementBounds(ref velocity, deltaTime);

        // 경계 보정이 끝난 실제 이동 요청을 저장합니다.
        _requestedVelocity = velocity;

        // X 속도는 유지하고 보정된 Y 속도를 적용하며 Z 속도는 제거합니다.
        _rigidbody.linearVelocity = new Vector3(
            velocity.x,
            velocity.y,
            0f);
    }

    /// <summary>
    /// 전역 경계 접근을 기록하고 현재 위치와 다음 이동 속도를 허용 범위로 제한합니다.
    /// </summary>
    private void LimitMovementBounds(ref Vector2 velocity, float deltaTime)
    {
        // 스포너에서 범위를 받기 전에는 기존 물리 위치를 변경하지 않습니다.
        if (!_hasMovementBounds || deltaTime <= 0f)
        {
            return;
        }

        // 경계에 접근 중인 방향을 보정 전에 기록하여 무리가 미리 회피하게 합니다.
        if (TryGetBoundaryNormal(velocity, out Vector2 normal))
        {
            _obstacleNormal = normal;
            _hasObstacleContact = true;
        }

        // 외부 힘으로 이미 범위를 벗어났다면 XY만 안쪽으로 복구합니다.
        Vector3 position = _rigidbody.position;
        Vector3 clamped = new Vector3(
            Mathf.Clamp(position.x, _movementMin.x, _movementMax.x),
            Mathf.Clamp(position.y, _movementMin.y, _movementMax.y),
            position.z);
        if (position != clamped)
        {
            _rigidbody.position = clamped;
        }

        // 다음 물리 프레임에도 범위를 넘지 않도록 각 축의 속도를 제한합니다.
        float nextX = Mathf.Clamp(
            clamped.x + velocity.x * deltaTime, _movementMin.x, _movementMax.x);
        float nextY = Mathf.Clamp(
            clamped.y + velocity.y * deltaTime, _movementMin.y, _movementMax.y);
        velocity = new Vector2(
            (nextX - clamped.x) / deltaTime, (nextY - clamped.y) / deltaTime);
    }

    /// <summary>
    /// 전역 경계 쪽으로 접근할 때 사전 전환 거리 안의 안쪽 법선을 계산합니다.
    /// </summary>
    private bool TryGetBoundaryNormal(Vector2 velocity, out Vector2 normal)
    {
        // 경계를 받지 않았거나 정지한 개체는 전환을 요청하지 않습니다.
        normal = Vector2.zero;
        if (!_hasMovementBounds || velocity.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        // 빠른 이동에서도 한 물리 프레임 전에 경계를 감지하도록 여유를 더합니다.
        Vector2 position = Position;
        float lookAhead = _boundaryTurnDistance + velocity.magnitude * Time.fixedDeltaTime;

        // 접근 중인 축만 선택하여 안쪽으로 이동할 때 다시 반전되지 않게 합니다.
        if (velocity.x < 0f && position.x <= _movementMin.x + lookAhead)
            normal.x = 1f;
        else if (velocity.x > 0f && position.x >= _movementMax.x - lookAhead)
            normal.x = -1f;
        if (velocity.y < 0f && position.y <= _movementMin.y + lookAhead)
            normal.y = 1f;
        else if (velocity.y > 0f && position.y >= _movementMax.y - lookAhead)
            normal.y = -1f;

        // 모서리는 두 축을 합친 법선을 전달해 안쪽으로 함께 방향을 바꿉니다.
        normal = normal.normalized;
        return normal.sqrMagnitude > 0.0001f;
    }

    /// <summary>
    /// 지정한 방향으로 X각도를 유지하며 수영 회전 연출을 적용합니다.
    /// </summary>
    public void TurnTowards(Vector2 direction, float deltaTime)
    {
        // 일반 수영과 동일한 회전 규칙으로 방향을 갱신합니다.
        UpdateSwimRotation(direction, deltaTime);
    }

    /// <summary>
    /// 기존 물리 설정을 저장하고 외부 위치 제어를 위해 몸체를 고정합니다.
    /// </summary>
    public void FreezeBody()
    {
        // 반복 호출로 최초 복원 정보를 덮어쓰지 않습니다.
        if (_rigidbody == null || _bodyHeld)
        {
            return;
        }

        // 고정 이전의 물리 모드와 보간 설정을 저장합니다.
        _wasKinematic = _rigidbody.isKinematic;
        _previousInterpolation = _rigidbody.interpolation;

        // 기존 속도를 제거하고 직접 위치를 제어하도록 전환합니다.
        Stop();
        _rigidbody.isKinematic = true;
        _rigidbody.interpolation = RigidbodyInterpolation.None;
        _bodyHeld = true;
    }

    /// <summary>
    /// 포획 전에 사용하던 물리 설정으로 복원합니다.
    /// </summary>
    public void RestoreBody()
    {
        // 직접 위치 제어를 시작했던 경우에만 복원합니다.
        if (_rigidbody == null || !_bodyHeld)
        {
            return;
        }

        // 저장된 물리 설정을 되돌립니다.
        _rigidbody.isKinematic = _wasKinematic;
        _rigidbody.interpolation = _previousInterpolation;
        _bodyHeld = false;

        // 복원 후 정지 상태에서 다음 행동을 시작합니다.
        Stop();
    }

    /// <summary>
    /// 포획 또는 사망 중 물고기 루트를 지정한 월드 위치로 이동합니다.
    /// </summary>
    public void SetHeldPosition(Vector3 position)
    {
        // 일반 물리 이동 중에는 직접 위치를 덮어쓰지 않습니다.
        if (_rigidbody == null || !_bodyHeld)
        {
            return;
        }

        // 유효하지 않은 외부 좌표를 차단합니다.
        if (float.IsNaN(position.x) || float.IsInfinity(position.x)
            || float.IsNaN(position.y) || float.IsInfinity(position.y)
            || float.IsNaN(position.z) || float.IsInfinity(position.z))
        {
            return;
        }

        // 물리 위치와 화면 위치를 같은 XYZ 좌표에 맞춥니다.
        _rigidbody.position = position;
        transform.position = position;
    }

    /// <summary>
    /// Head와 Tail이 외형 아래에 있는 유효한 정렬 기준인지 확인합니다.
    /// </summary>
    public bool CanAlignBody(Transform head, Transform tail)
    {
        // 정렬할 외형과 두 기준점이 필요합니다.
        if (_visual == null || head == null || tail == null)
        {
            return false;
        }

        // 외형을 돌릴 때 두 기준점도 함께 회전해야 합니다.
        if (head == _visual || tail == _visual
            || !head.IsChildOf(_visual)
            || !tail.IsChildOf(_visual))
        {
            return false;
        }

        // 두 기준점이 겹치면 몸의 방향을 계산할 수 없습니다.
        return (head.position - tail.position).sqrMagnitude > 0.0001f;
    }

    /// <summary>
    /// 좌우 전환은 Y축으로 처리하고 Head의 상하 조준을 분리해 뒤집힘을 방지합니다.
    /// </summary>
    public void TurnHeadTowards(
        Transform head,
        Transform tail,
        Vector3 targetPosition,
        float deltaTime)
    {
        // 유효한 기준점과 외형 제어 권한이 있을 때만 회전합니다.
        if (_rigidbody == null || !CanAlignBody(head, tail) || _bodyHeld || deltaTime <= 0f)
        {
            return;
        }

        // 회전의 영향을 받지 않는 Rigidbody 중심에서 Hero로 향하는 XY 방향을 구합니다.
        Vector3 bodyPosition = _rigidbody.position;
        Vector2 targetDirection = new Vector2(
            targetPosition.x - bodyPosition.x,
            targetPosition.y - bodyPosition.y);
        if (targetDirection.sqrMagnitude < 0.0001f)
        {
            // 목표와 몸통 중심이 겹치면 마지막 자세를 유지합니다.
            return;
        }

        // 수직 부근에서는 기존 방향을 유지하고 반대쪽으로 충분히 벗어나야 전환합니다.
        const float facingThreshold = 0.15f;
        float horizontalDirection = targetDirection.normalized.x;
        if (_facesRight && horizontalDirection < -facingThreshold)
        {
            // 오른쪽을 보던 상태에서 목표가 확실히 왼쪽으로 이동했습니다.
            _facesRight = false;
        }
        else if (!_facesRight && horizontalDirection > facingThreshold)
        {
            // 왼쪽을 보던 상태에서 목표가 확실히 오른쪽으로 이동했습니다.
            _facesRight = true;
        }

        // 반대편 Hero는 몸을 Z축으로 뒤집지 않고 Y축 180도 전환으로 바라봅니다.
        float targetYaw = _facesRight ? 90f : -90f;
        float angularStep = Mathf.Max(1f, _combatTurnSpeed) * deltaTime;
        _swimYaw = Mathf.MoveTowardsAngle(_swimYaw, targetYaw, angularStep);

        // 현재 외형 회전을 제거해 애니메이션이 적용된 실제 Head/Tail 축을 구합니다.
        Vector3 localBodyAxis = Quaternion.Inverse(_visual.rotation)
            * (head.position - tail.position);

        // 초기 X자세와 목표 좌우 방향을 기준으로 기울이지 않은 몸 축을 구합니다.
        Vector3 levelBodyAxis = Quaternion.Euler(_swimPitch, targetYaw, 0f)
            * localBodyAxis;
        Vector2 levelDirection = new Vector2(levelBodyAxis.x, levelBodyAxis.y);
        if (levelDirection.sqrMagnitude < 0.0001f)
        {
            // 몸 축의 평면 투영이 사라지면 현재 좌우 방향을 기준으로 사용합니다.
            levelDirection = _facesRight ? Vector2.right : Vector2.left;
        }

        // 상하 조준은 90도 안으로 제한하여 배가 위아래로 뒤집히지 않게 합니다.
        float targetAim = Mathf.Clamp(
            Vector2.SignedAngle(levelDirection, targetDirection), -90f, 90f);
        _combatAimAngle = Mathf.MoveTowards(_combatAimAngle, targetAim, angularStep);

        // 일반 수영의 작은 회전 연출은 전투 진입 시 부드럽게 해제합니다.
        _swimTilt = Mathf.MoveTowards(_swimTilt, 0f, angularStep);

        // Y축 좌우 전환 자세에 월드 Z축 상하 조준을 합쳐 외형 전체에 적용합니다.
        _visual.rotation = Quaternion.AngleAxis(_combatAimAngle, Vector3.forward)
            * Quaternion.Euler(_swimPitch, _swimYaw, _swimTilt);
    }
    /// <summary>
    /// Tail에서 Head로 향하는 몸의 축을 지정한 월드 방향에 맞춥니다.
    /// </summary>
    public void AlignBodyAxis(
        Transform head,
        Transform tail,
        Vector3 headDirection)
    {
        // 유효한 기준점과 방향이 있을 때만 회전합니다.
        if (!CanAlignBody(head, tail)
            || headDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // 실제 꼬리에서 머리로 향하는 축과 목표 방향의 차이를 구합니다.
        Vector3 currentAxis = head.position - tail.position;
        Quaternion correction = Quaternion.FromToRotation(
            currentAxis,
            headDirection.normalized);

        // 물리 루트 대신 외형 전체만 회전합니다.
        _visual.rotation = correction * _visual.rotation;
    }

    /// <summary>
    /// 구조물과 처음 접촉한 면을 이동 패턴에 전달할 수 있도록 기록합니다.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        // 최초 접촉도 지속 접촉과 동일한 규칙으로 검사합니다.
        RecordObstacleContact(collision);
    }

    /// <summary>
    /// 구조물에 계속 막혀 있는 동안 유효한 접촉면을 다시 확인합니다.
    /// </summary>
    private void OnCollisionStay(Collision collision)
    {
        // 최초 접촉 이후 진행 방향이 바뀐 경우에도 막힘을 감지합니다.
        RecordObstacleContact(collision);
    }

    /// <summary>
    /// 이동을 가로막는 구조물의 XY 접촉면 중 가장 정면에 있는 방향을 기록합니다.
    /// </summary>
    private void RecordObstacleContact(Collision collision)
    {
        // 직접 위치 제어 중이거나 이동 요청이 없으면 일반 회피를 요청하지 않습니다.
        if (_rigidbody == null
            || _rigidbody.isKinematic
            || _requestedVelocity.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Platform 레이어에 속한 구조물과의 충돌만 처리합니다.
        Collider other = collision.collider;
        if (other == null
            || other.gameObject.layer != _platformLayer)
        {
            return;
        }

        // 캐릭터와 물고기는 구조물 회피 대상에서 제외합니다.
        if (other.GetComponentInParent<Fish>() != null
            || other.GetComponentInParent<Hero>() != null)
        {
            return;
        }

        // 물리 해석 후 속도 대신 충돌 전 요청 방향으로 정면 충돌을 판정합니다.
        Vector2 direction = _requestedVelocity.normalized;

        // 여러 접촉점 중 진행을 가장 강하게 가로막는 면을 선택합니다.
        for (int i = 0; i < collision.contactCount; i++)
        {
            // Z 방향의 면을 제외할 수 있도록 접촉 법선을 XY로 투영합니다.
            Vector3 contactNormal = collision.GetContact(i).normal;
            Vector2 normal = new Vector2(contactNormal.x, contactNormal.y);
            if (normal.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            // 면에서 멀어지는 이동이나 거의 평행한 이동은 방향 전환에서 제외합니다.
            normal.Normalize();
            float opposition = Vector2.Dot(direction, normal);
            if (opposition >= -0.01f)
            {
                continue;
            }

            // 이미 기록한 면보다 정면에 있는 접촉만 교체합니다.
            if (!_hasObstacleContact
                || opposition < Vector2.Dot(direction, _obstacleNormal))
            {
                _obstacleNormal = normal;
                _hasObstacleContact = true;
            }
        }
    }

    /// <summary>
    /// 저장된 장애물 정보를 소비하고, 정보가 없으면 전방 Platform을 미리 검사합니다.
    /// </summary>
    public bool TryConsumeObstacleNormal(out Vector2 normal)
    {
        // 실제 충돌이나 맵 경계에서 기록한 정보를 확보합니다.
        normal = _obstacleNormal;
        bool hasContact = _hasObstacleContact;

        // 저장된 정보를 다음 호출에서 중복 사용하지 않도록 초기화합니다.
        _obstacleNormal = Vector2.zero;
        _hasObstacleContact = false;

        // 저장된 접촉 다음으로 전역 경계 접근과 실제 Platform 전방을 검사합니다.
        return hasContact
            || TryGetBoundaryNormal(_requestedVelocity, out normal)
            || TryDetectPlatformAhead(out normal);
    }

    /// <summary>
    /// 몸체 Collider 전방의 설정 거리 안에서 가장 가까운 Platform 법선을 반환합니다.
    /// </summary>
    private bool TryDetectPlatformAhead(out Vector2 normal)
    {
        // 장애물을 찾지 못했을 때 사용할 기본값입니다.
        normal = Vector2.zero;

        // 이동 가능한 몸체와 유효한 이동 요청이 있을 때만 검사합니다.
        if (_rigidbody == null
            || _rigidbody.isKinematic
            || _platformLayer < 0
            || _requestedVelocity.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        // 직전 이동 요청을 기준으로 전방 방향을 구합니다.
        // 다음 예약 시점 전에는 비용이 큰 전방 검사만 생략합니다.
        if (Time.fixedTimeAsDouble < _nextObstacleProbeTime)
        {
            return false;
        }

        // 검사 간격은 최소 한 물리 단계이며 밀린 검사를 몰아서 실행하지 않습니다.
        float probeInterval = Mathf.Max(Time.fixedDeltaTime, _obstacleProbeInterval);
        _nextObstacleProbeTime = Time.fixedTimeAsDouble + probeInterval;

        // 현재 이동 요청 방향으로 전방을 검사합니다.
        Vector2 forward = _requestedVelocity.normalized;

        // 여유 거리에 한 물리 프레임의 이동 거리를 더해 빠른 접근에 대비합니다.
        float distance = Mathf.Max(0.1f, _obstacleCheckDistance)
            + _requestedVelocity.magnitude * probeInterval;

        // 실제 검사가 실행되는 시점의 월드 중심, 방향과 거리를 저장합니다.
        _obstacleGizmoOrigin = _rigidbody.worldCenterOfMass;
        _obstacleGizmoDirection = new Vector3(forward.x, forward.y, 0f);
        _obstacleGizmoDistance = distance;

        // 이번 검사 결과와 실행 시간을 초기화합니다.
        _obstacleGizmoHit = false;
        _obstacleGizmoTime = Time.time;

        // 몸체 Collider 모양으로 전방을 검사하며 Trigger는 제외합니다.
        RaycastHit[] hits = _rigidbody.SweepTestAll(
            new Vector3(forward.x, forward.y, 0f),
            distance,
            QueryTriggerInteraction.Ignore);

        // 결과 순서에 의존하지 않고 가장 가까운 장애물을 선택합니다.
        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        // 감지 결과에서 실제로 이동을 막는 Platform을 찾습니다.
        foreach (RaycastHit hit in hits)
        {
            // 자기 몸체와 Platform 이외의 대상은 제외합니다.
            Collider other = hit.collider;
            if (other == null
                || other.attachedRigidbody == _rigidbody
                || other.gameObject.layer != _platformLayer)
            {
                continue;
            }

            // 기존 충돌 처리와 동일하게 물고기와 Hero는 제외합니다.
            if (other.GetComponentInParent<Fish>() != null
                || other.GetComponentInParent<Hero>() != null)
            {
                continue;
            }

            // XY 이동을 막는 면인지 판단하기 위해 법선을 변환합니다.
            Vector2 candidate = new Vector2(hit.normal.x, hit.normal.y);
            if (candidate.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            // 진행을 막지 않는 면과 더 먼 장애물은 제외합니다.
            candidate.Normalize();
            if (Vector2.Dot(forward, candidate) >= -0.01f
                || hit.distance >= nearestDistance)
            {
                continue;
            }

            // 가장 가까운 장애물의 법선을 기존 무리 회피 처리에 전달합니다.
            nearestDistance = hit.distance;
            normal = candidate;
            found = true;

            // 실제 회피 대상으로 선택된 가장 가까운 벽의 결과를 저장합니다.
            _obstacleGizmoHitPoint = hit.point;
            _obstacleGizmoNormal = new Vector3(candidate.x, candidate.y, 0f);
            _obstacleGizmoHit = true;
        }

        // 전방에 진행을 막는 Platform이 있는지 반환합니다.
        return found;
    }

    /// <summary>
    /// 지정한 물고기 콜라이더가 이 무버의 Rigidbody에 속하는지 확인합니다.
    /// </summary>
    public bool OwnsCollider(Collider body)
    {
        // 다른 Rigidbody 소속 콜라이더를 잘못 연결하지 않게 합니다.
        return _rigidbody != null
            && body != null
            && (body.attachedRigidbody == _rigidbody
                // 최초 비활성 대여 시에는 물리 등록 전이므로 가장 가까운 Rigidbody를 검사합니다.
                || (!body.gameObject.activeInHierarchy
                    && body.GetComponentInParent<Rigidbody>(true) == _rigidbody));
    }

    /// <summary>
    /// 현재 위치에서 이동할 경로가 지정한 대상 콜라이더와 만나는지 확인합니다.
    /// </summary>
    public bool WillTouchBody(Collider target, Vector3 displacement)
    {
        // 이동 경로나 검사 대상이 없으면 경로 검사를 생략합니다.
        if (_rigidbody == null
            || target == null
            || displacement.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        // 물고기의 활성 콜라이더 형태로 이동 구간 전체를 검사합니다.
        RaycastHit[] hits = _rigidbody.SweepTestAll(
            displacement.normalized,
            displacement.magnitude,
            QueryTriggerInteraction.Collide);

        // 공격체나 다른 물체가 아닌 주입받은 Hero 본체만 인정합니다.
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == target)
            {
                return true;
            }
        }

        // 이동 경로에서 Hero 본체와 만나지 않았습니다.
        return false;
    }

    /// <summary>
    /// 꼬리에서 머리로 향하는 축을 작살 방향에 맞추고 꼬리를 작살 끝에 붙입니다.
    /// </summary>
    public void AlignToHarpoon(
        Transform head,
        Transform tail,
        Vector3 point,
        Vector3 tailToHeadDirection)
    {
        // 작살 행동으로 물리 이동이 중단된 상태에서만 정렬합니다.
        if (_rigidbody == null || !_bodyHeld)
        {
            return;
        }

        // 머리와 꼬리가 Visual 아래의 유효한 기준점인지 확인합니다.
        if (!CanAlignBody(head, tail))
        {
            return;
        }

        // 잘못된 좌표나 길이가 없는 방향을 Transform에 적용하지 않습니다.
        if (!IsFiniteVector(point)
            || !IsFiniteVector(tailToHeadDirection)
            || tailToHeadDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // 현재 물고기의 꼬리에서 머리로 향하는 방향을 계산합니다.
        Vector3 currentDirection = head.position - tail.position;

        // 현재 몸 방향을 요청한 방향으로 맞추는 회전량을 계산합니다.
        Quaternion correction = Quaternion.FromToRotation(
            currentDirection.normalized,
            tailToHeadDirection.normalized);

        // 외형 전체를 회전해 작살과 몸의 축을 맞춥니다.
        _visual.rotation = correction * _visual.rotation;

        // 회전 후 꼬리가 작살 접점에 오도록 루트 위치를 보정합니다.
        Vector3 nextPosition = transform.position + point - tail.position;

        // 기존 함수로 Rigidbody와 Transform의 위치를 함께 맞춥니다.
        SetHeldPosition(nextPosition);
    }

    /// <summary>
    /// 벡터의 모든 성분이 유효한 유한 숫자인지 확인합니다.
    /// </summary>
    private static bool IsFiniteVector(Vector3 value)
    {
        // NaN과 무한대가 위치나 회전 계산으로 들어가는 것을 차단합니다.
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
    /// <summary>
    /// 물리 보간 지연 없이 꼬리 위치를 작살 끝에 맞춥니다.
    /// </summary>
    public void AttachTailToPoint(Transform tail, Vector3 point)
    {
        // 작살 포획으로 고정된 Rigidbody에만 위치를 적용합니다.
        if (_rigidbody == null
            || !_rigidbody.isKinematic
            || tail == null)
        {
            return;
        }

        // 꼬리와 작살 끝 사이의 차이만큼 루트 전체를 이동합니다.
        Vector3 nextPosition = transform.position + point - tail.position;

        // 물리 위치와 화면의 Transform을 같은 위치로 맞춥니다.
        _rigidbody.position = nextPosition;
        transform.position = nextPosition;
    }
    /// <summary>
    /// 다음 대여를 위해 물리 고정, 이동 범위와 이전 접촉 정보를 초기화합니다.
    /// </summary>
    public void ResetForSpawn()
    {
        // 최초 비활성 대여에서도 Rigidbody 참조를 준비합니다.
        EnsureReady();

        // 작살 또는 사망으로 고정된 물리 설정과 남은 속도를 복원합니다.
        RestoreBody();
        Stop();

        // 정지 함수가 Kinematic 상태에서 생략한 접촉 정보도 명시적으로 비웁니다.
        _requestedVelocity = Vector2.zero;
        _obstacleNormal = Vector2.zero;
        _hasObstacleContact = false;
        _verticalRange = null;
        _hasMovementBounds = false;
        _verticalRangeEnabled = true;
        _facePatternMovement = false;

        // 다음 Initialize의 SetFacing이 새 이동 방향을 지정하도록 기울기를 정리합니다.
        _swimTilt = 0f;
        _combatAimAngle = 0f;
        // 새 대여마다 첫 검사 시점을 분산하고 이전 검사 표시를 제거합니다.
        float probeInterval = Mathf.Max(Time.fixedDeltaTime, _obstacleProbeInterval);
        _nextObstacleProbeTime = Time.fixedTimeAsDouble + Random.Range(0f, probeInterval);
        _obstacleGizmoTime = float.NegativeInfinity;
    }
    /// <summary>
    /// 실제 전방 벽 검사의 방향, 거리, 접촉 지점과 법선을 표시합니다.
    /// 선은 검사 경로이며 Collider 전체의 검사 부피는 아닙니다.
    /// </summary>
    private void OnDrawGizmos()
    {
        // 플레이 중이며 표시 옵션과 컴포넌트가 활성화된 경우만 그립니다.
        if (!Application.isPlaying || !_showObstacleGizmos || !isActiveAndEnabled)
        {
            return;
        }

        // 검사가 중단된 경우 이전 결과가 계속 남지 않도록 합니다.
        if (Time.time - _obstacleGizmoTime > Mathf.Max(0.1f, Mathf.Max(_obstacleProbeInterval, Time.fixedDeltaTime) + Time.fixedDeltaTime * 2f))
        {
            return;
        }

        // 다른 기즈모에 영향을 주지 않도록 기존 설정을 보관합니다.
        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;

        // 저장한 월드 좌표를 그대로 사용합니다.
        Gizmos.matrix = Matrix4x4.identity;

        // 벽 감지 성공은 빨간색, 미감지는 초록색으로 표시합니다.
        Gizmos.color = _obstacleGizmoHit ? Color.red : Color.green;

        // 실제 검사 방향과 전체 검사 거리를 선으로 표시합니다.
        Vector3 end = _obstacleGizmoOrigin
            + _obstacleGizmoDirection * _obstacleGizmoDistance;
        Gizmos.DrawLine(_obstacleGizmoOrigin, end);

        // 검사 시작점과 최대 거리 지점을 표시합니다.
        Gizmos.DrawWireSphere(_obstacleGizmoOrigin, 0.06f);
        Gizmos.DrawWireSphere(end, 0.08f);

        // 벽 감지에 성공했을 때만 접촉 지점과 법선을 표시합니다.
        if (_obstacleGizmoHit)
        {
            // 노란색 구로 실제 벽 접촉 지점을 표시합니다.
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_obstacleGizmoHitPoint, 0.08f);

            // 하늘색 선으로 회피 계산에 사용하는 XY 법선을 표시합니다.
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_obstacleGizmoHitPoint, _obstacleGizmoNormal * 0.7f);
        }

        // 이후 기즈모가 기존 설정을 사용하도록 복원합니다.
        Gizmos.color = previousColor;
        Gizmos.matrix = previousMatrix;
    }
    /// <summary>
    /// 새 물리 검사를 실행하지 않고 이전 접촉 기록만 비웁니다.
    /// </summary>
    public void ClearObstacleContact()
    {
        // 방향 전환 대기 중의 오래된 법선을 다음 회피에 사용하지 않습니다.
        _obstacleNormal = Vector2.zero;
        _hasObstacleContact = false;
    }
}