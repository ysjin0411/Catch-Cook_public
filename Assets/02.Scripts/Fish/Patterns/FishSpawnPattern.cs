using UnityEngine;

/// <summary>
/// 스포너가 서로 다른 물고기 패턴을 같은 방식으로 실행하기 위한 공통 규약입니다.
/// </summary>
public abstract class FishSpawnPattern : ScriptableObject
{
    // 지속 접촉이나 여러 물고기의 동시 충돌로 방향이 반복 반전되는 것을 막습니다.
    [SerializeField, Min(0.1f)] private float _obstacleTurnInterval = 0.5f;

    // 경로 판단 주기는 실제 물리 이동 주기와 독립적으로 설정합니다.
    [SerializeField, Min(0.02f)] private float _decisionInterval = 0.1f;

    // 리스폰 예약은 스포너가 소유한 관리자에 위임합니다.
    private FishRespawnScheduler _respawnScheduler;

    // 접촉 이벤트를 무리당 하나로 합치고 같은 벽에서 반복 반전하지 않게 합니다.
    private double _runtimeClock;
    private double _nextObstacleTurnTime;
    private int _pendingObstacleSlot = -1;
    private Vector2 _pendingObstacleNormal;
    private Vector2 _pendingObstacleDirection;
    private float _pendingOpposition;

    public float DecisionInterval => Mathf.Max(0.02f, _decisionInterval);
    public bool DecisionRequested { get; private set; }
    public bool CanProbeObstacles => _runtimeClock >= _nextObstacleTurnTime;
    public virtual bool RequiresPhysicsPath => false;

    /// <summary>
    /// 생성 전에 스포너의 독립 리스폰 관리자를 연결합니다.
    /// </summary>
    public void BindRespawnScheduler(FishRespawnScheduler scheduler)
    {
        // 실행본마다 자신을 생성한 스포너의 관리자만 사용합니다.
        _respawnScheduler = scheduler;
    }

    /// <summary>
    /// 이 패턴의 재생성 예약과 보류 중인 접촉을 취소합니다.
    /// </summary>
    protected void ClearRespawnReservations()
    {
        // 관리자가 없는 생성 실패 경로에서도 정리할 수 있습니다.
        if (_respawnScheduler != null)
            _respawnScheduler.Cancel(this);

        // 이전 실행의 벽 정보나 회피 대기가 새 실행에 적용되지 않게 합니다.
        _pendingObstacleSlot = -1;
        _nextObstacleTurnTime = 0d;
    }

    /// <summary>
    /// 반환된 개체의 자리 번호를 독립 리스폰 관리자에 예약합니다.
    /// </summary>
    protected void ScheduleRespawn(int slotIndex, float delay)
    {
        // 반환된 물고기 객체는 예약에 보관하지 않습니다.
        if (_respawnScheduler != null)
            _respawnScheduler.Schedule(this, slotIndex, delay);
    }

    /// <summary>
    /// 각 구체 패턴이 만료된 자리의 생성 방법을 구현합니다.
    /// </summary>
    public virtual void RespawnSlot(int slotIndex)
    {
        // 배경 전용 패턴은 이 관리자의 재생성을 사용하지 않습니다.
    }

    /// <summary>
    /// 판단 Tick과 별개로 연속 파동 및 점프 경로를 실행합니다.
    /// </summary>
    public virtual void TickPhysicsPath(float deltaTime)
    {
        // 일반 무리는 Fish에 저장한 속도 명령을 사용하므로 별도 경로 실행이 없습니다.
    }

    /// <summary>
    /// 판단 주기와 무관하게 회피 대기용 실행 시간을 진행합니다.
    /// </summary>
    public void AdvanceRuntimeClock(float deltaTime)
    {
        // 호출자는 유효한 물리 단계 시간만 전달합니다.
        _runtimeClock += deltaTime;
    }

    /// <summary>
    /// 다음 스케줄러 실행에서 정기 시각보다 먼저 경로를 갱신하도록 요청합니다.
    /// </summary>
    public void RequestDecision()
    {
        // 여러 개체의 요청을 하나의 플래그로 합칩니다.
        DecisionRequested = true;
    }

    /// <summary>
    /// 현재 처리할 판단 요청을 소비합니다.
    /// </summary>
    public void ConsumeDecisionRequest()
    {
        // Tick 도중 새 요청이 들어오면 다음 실행에 남도록 호출 전에 초기화합니다.
        DecisionRequested = false;
    }

    /// <summary>
    /// 물고기가 보고한 벽 중 실제 이동 방향을 가장 강하게 막는 접촉을 보관합니다.
    /// </summary>
    public void ReportObstacle(int slotIndex, Vector2 normal, Vector2 movement)
    {
        // 반전 대기 중이거나 정지한 개체의 보고는 경로 변경에 사용하지 않습니다.
        if (!CanProbeObstacles || slotIndex < 0 || movement.sqrMagnitude < 0.0001f)
            return;

        // 패턴의 수평 방향 대신 실제 XY 이동을 사용하여 천장과 바닥도 감지합니다.
        Vector2 direction = movement.normalized;
        float opposition = Vector2.Dot(direction, normal);
        if (opposition >= -0.01f)
            return;

        // 동일 물리 단계의 여러 접촉 중 가장 정면인 정보를 선택합니다.
        if (_pendingObstacleSlot < 0 || opposition < _pendingOpposition)
        {
            _pendingObstacleSlot = slotIndex;
            _pendingObstacleNormal = normal;
            _pendingObstacleDirection = direction;
            _pendingOpposition = opposition;
        }

        // 접촉 콜백에서 경로를 직접 실행하지 않고 판단만 예약합니다.
        RequestDecision();
    }

    // 마지막으로 소비한 접촉 당시의 실제 진행 방향입니다.
    protected Vector2 ObstacleApproachDirection { get; private set; }

    // 원본 에셋과 실제 실행용 복제본을 구분합니다.
    public bool IsRuntime { get; private set; }

    // 패턴이 생성하는 물고기의 기본 데이터입니다.
    public abstract FishData Data { get; }

    // 한 번의 패턴 실행으로 생성할 개체 수이며, 스포너의 최초 생성 집계에 사용합니다.
    public abstract int InitialMemberCount { get; }

    // 기존 패턴은 물리 갱신을 유지하고 배경 전용 패턴만 이를 재정의합니다.
    public virtual bool RequiresFixedTick => true;

    // 최초 생성과 리스폰에서 사용할 그룹 밝기입니다.
    public float Brightness { get; private set; } = 1f;

    // 이 실행본의 최초 생성과 모든 재생성에서 사용할 단일 Layer입니다.
    public int SpawnLayer { get; private set; } = 7;

    /// <summary>
    /// 그룹 Layer를 실행본에 보관하고 잘못된 레이어 번호를 거부합니다.
    /// </summary>
    public bool SetSpawnLayer(int layer)
    {
        // 원본 SO에는 플레이 중인 그룹의 Layer를 저장하지 않습니다.
        if (!IsRuntime || layer < 0 || layer > 31 || string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
        {
            Debug.LogError("런타임 패턴과 등록된 단일 Layer를 확인하세요.", this);
            return false;
        }

        // 새 개체를 만들 때마다 이 값을 스포너에 전달합니다.
        SpawnLayer = layer;
        return true;
    }

    /// <summary>
    /// 패턴 실행본에 소속 그룹의 밝기를 저장합니다.
    /// </summary>
    public void SetBrightness(float brightness)
    {
        // 원본 패턴 에셋에 그룹별 실행 설정을 기록하지 않도록 차단합니다.
        if (!IsRuntime)
        {
            Debug.LogError("밝기는 패턴 실행본에만 설정할 수 있습니다.", this);
            return;
        }

        // 생성과 리스폰에서 계속 사용할 밝기를 저장합니다.
        Brightness = Mathf.Clamp01(brightness);
    }


    // 생성 원과 독립적으로 적용되는 월드 Y 이동 범위입니다.
    public FishVerticalRange VerticalRange { get; protected set; }

    // 각 런타임 패턴이 최초 생성 위치를 정할 때 사용하는 원형 영역입니다.
    public FishSpawnArea Area { get; protected set; }

    /// <summary>
    /// 무리 진행을 가로막는 접촉 하나를 선택하고 연속 방향 전환 간격을 적용합니다.
    /// </summary>
    protected bool TryGetObstacleTurn(
        Fish[] members,
        Vector2 direction,
        float deltaTime,
        out int memberIndex,
        out Vector2 normal)
    {
        // 저장된 접촉만 소비하며 물고기 전체나 물리 공간을 다시 검사하지 않습니다.
        memberIndex = _pendingObstacleSlot;
        normal = _pendingObstacleNormal;
        Vector2 approach = _pendingObstacleDirection;
        _pendingObstacleSlot = -1;

        // 보고 이후 반환되거나 전투로 전환된 개체의 접촉은 버립니다.
        if (!CanProbeObstacles || members == null || memberIndex < 0
            || memberIndex >= members.Length || members[memberIndex] == null
            || !members[memberIndex].CanFollowGroup)
            return false;

        // 실제 접근 방향을 새 경로의 반전 기준으로 제공하고 연속 반전을 제한합니다.
        ObstacleApproachDirection = approach;
        _nextObstacleTurnTime = _runtimeClock + Mathf.Max(0.1f, _obstacleTurnInterval);
        return true;
    }

    /// <summary>
    /// 원본 설정을 보존하면서 독립적인 실행용 패턴을 생성합니다.
    /// </summary>
    public FishSpawnPattern CreateRuntime()
    {
        // 같은 에셋을 여러 Element에서 사용해도 실행 상태가 섞이지 않게 합니다.
        FishSpawnPattern runtime = Instantiate(this);
        runtime.IsRuntime = true;

        return runtime;
    }

    /// <summary>
    /// 스포너, 최초 생성 원과 이동 높이 범위를 전달받아 물고기를 생성합니다.
    /// </summary>
    public abstract bool Spawn(
        FishSpawner spawner,
        FishSpawnArea area,
        FishVerticalRange verticalRange,
        float respawnDelay);

    /// <summary>
    /// 주기 또는 회피 요청에 따라 경로와 목표 이동 명령을 갱신합니다.
    /// </summary>
    public abstract void Tick(float deltaTime);

    /// <summary>
    /// 패턴이 담당하는 일반 이동을 일시 정지합니다.
    /// </summary>
    public abstract void Pause();

    /// <summary>
    /// 자체 갱신하는 패턴이 스포너의 재활성화 시 실행을 재개하도록 합니다.
    /// </summary>
    public virtual void Resume()
    {
        // 정지 중의 접촉을 버리고 현재 위치를 기준으로 이동 명령을 갱신합니다.
        _pendingObstacleSlot = -1;
        RequestDecision();
    }

    /// <summary>
    /// 패턴이 생성하고 소유한 물고기를 정리합니다.
    /// </summary>
    public abstract void Dispose();
}