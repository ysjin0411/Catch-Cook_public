using UnityEngine;

/// <summary>
/// 배경 무리 부모가 계산할 이동 경로의 종류입니다.
/// </summary>
public enum BackgroundFishMovement
{
    Horizontal = 0,
    RandomCurve = 1,
    Diagonal = 2,
    Wave = 3
}

/// <summary>
/// 기존 스포너의 배치 설정으로 배경 무리를 생성하고 수명을 관리합니다.
/// 배경 이동은 FixedUpdate의 패턴 순회에서 제외합니다.
/// </summary>
[CreateAssetMenu(fileName = "BackgroundFishPattern", menuName = "Fish/Background Group")]
public sealed class BackgroundFishPattern : FishSpawnPattern
{
    // 공통 BlueTang 배경 프리팹 또는 어종별 Variant입니다.
    [SerializeField] private BackgroundFish _fishPrefab;

    // 기존 이동 속도와 초기 방향을 재사용할 원본 물고기 데이터입니다.
    [SerializeField] private FishData _data;

    // 기존 패턴의 자리 간격과 마릿수를 그대로 보존합니다.
    [SerializeField] private Vector2[] _spawnOffsets = { Vector2.zero };

    // 이전 에셋의 곡선 설정을 보존하는 호환 필드입니다.
    [SerializeField, HideInInspector] private bool _curvedMovement;

    // 모든 이동 방식은 개체가 아닌 무리 부모에서 한 번만 계산합니다.
    [SerializeField] private BackgroundFishMovement _movement;

    // 양수는 북동-남서, 음수는 남동-북서 방향의 왕복 경로입니다.
    [SerializeField, Range(-75f, 75f)] private float _diagonalAngle = 30f;

    // 수평 물결의 높이 진폭과 한 주기의 수평 이동 거리입니다.
    [SerializeField, Min(0f)] private float _waveAmplitude = 1.5f;
    [SerializeField, Min(1f)] private float _waveLength = 24f;

    // 원본 이동 속도에 적용할 기존 무리 속도 비율입니다.
    [SerializeField, Range(0.01f, 1f)] private float _groupSpeedRatio = 0.3f;

    // 방향 변경 사이에 진행할 최소·최대 거리입니다.
    [SerializeField] private Vector2 _travelDistance = new Vector2(3f, 6f);

    // 다음 방향의 최대 변경 각도와 경로 회전 속도입니다.
    [SerializeField, Range(0f, 90f)] private float _maxTurnAngle = 60f;
    [SerializeField, Min(1f)] private float _pathTurnSpeed = 25f;

    // 부모 대형이 좌우를 바꿀 때 사용하는 회전 속도입니다.
    [SerializeField, Min(1f)] private float _visualTurnSpeed = 180f;

    // 실제 지형만 검사하며 Trigger는 무리에서 제외합니다.
    [SerializeField] private LayerMask _obstacleMask = 1 << 8;

    // 지형과 대형 사이에 남길 여유 거리입니다.
    [SerializeField, Min(0.001f)] private float _wallClearance = 0.05f;

    // 이 패턴 실행본이 생성한 무리 하나입니다.
    private BackgroundFishSchool _school;

    // 스포너의 공통 데이터 계약을 기존 원본 데이터로 충족합니다.
    public override FishData Data => _data;

    // 패턴 실행 횟수와 실제 물고기 마릿수를 구분하기 위한 생성 예정 수입니다.
    public override int InitialMemberCount => _spawnOffsets != null ? _spawnOffsets.Length : 0;

    // 배경은 자체 Update에서 무리 단위로 이동하므로 물리 Tick이 필요하지 않습니다.
    public override bool RequiresFixedTick => false;

    /// <summary>
    /// 기존 생성 영역과 높이 범위를 보존하여 배경 무리를 만듭니다.
    /// </summary>
    public override bool Spawn(FishSpawner spawner, FishSpawnArea area,
        FishVerticalRange verticalRange, float respawnDelay)
    {
        // 원본 에셋 실행, 중복 생성 및 누락된 설정을 거부합니다.
        if (!IsRuntime || _school != null || spawner == null || area == null
            || verticalRange == null || _fishPrefab == null || _data == null
            || _spawnOffsets == null || _spawnOffsets.Length == 0
            || _obstacleMask.value == 0)
        {
            Debug.LogError("배경 패턴의 프리팹, 데이터, 배치와 지형 레이어를 확인하세요.", this);
            return false;
        }

        // 기존 스포너가 넘긴 영역과 높이를 그대로 기억합니다.
        Area = area;
        VerticalRange = verticalRange;
        GameObject root = new GameObject(name + "_School");
        root.transform.SetParent(spawner.transform, false);
        root.layer = SpawnLayer;
        _school = root.AddComponent<BackgroundFishSchool>();

        // 배경은 포획되지 않으므로 개체별 소멸 검사와 리스폰 타이머가 없습니다.
        return _school.Initialize(_fishPrefab, _spawnOffsets, area, verticalRange,
            spawner.MovementMin, spawner.MovementMax, Brightness, SpawnLayer,
            _data.MoveSpeed * _groupSpeedRatio, _data.InitialDirection,
            _curvedMovement && _movement == BackgroundFishMovement.Horizontal
                ? BackgroundFishMovement.RandomCurve : _movement,
            _travelDistance, _maxTurnAngle, _pathTurnSpeed,
            _visualTurnSpeed, _obstacleMask, _wallClearance,
            _diagonalAngle, _waveAmplitude, _waveLength);
    }

    /// <summary>
    /// 배경 이동은 무리의 Update가 처리하므로 물리 Tick에서는 수행하지 않습니다.
    /// </summary>
    public override void Tick(float deltaTime)
    {
        // RequiresFixedTick이 false이므로 스포너의 물리 갱신 목록에 등록되지 않습니다.
    }

    /// <summary>
    /// 스포너가 정지될 때 배경 무리의 이동도 함께 중단합니다.
    /// </summary>
    public override void Pause()
    {
        // 물리 속도를 갖지 않으므로 무리 갱신만 중단하면 멈춥니다.
        if (_school != null) _school.enabled = false;
    }

    /// <summary>
    /// 스포너가 다시 활성화되면 배경 무리의 이동을 재개합니다.
    /// </summary>
    public override void Resume()
    {
        // 기존 위치와 경로를 보존하고 갱신만 다시 켭니다.
        if (_school != null) _school.enabled = true;
    }

    /// <summary>
    /// 패턴이 소유한 배경 무리와 자식 외형을 함께 제거합니다.
    /// </summary>
    public override void Dispose()
    {
        // 원본 에셋에는 실행 중인 무리가 없으므로 정리하지 않습니다.
        if (!IsRuntime) return;
        if (_school != null) Destroy(_school.gameObject);
        _school = null;
    }
}
