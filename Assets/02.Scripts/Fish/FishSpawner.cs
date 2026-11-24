using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 그룹별 최초 생성 영역과 이동 높이를 설정하고 각 패턴의 실행을 관리합니다.
/// </summary>
[DefaultExecutionOrder(100)]
public partial class FishSpawner : MonoBehaviour
{
    [Serializable]
    private class SpawnEntry
    {
        // 반복 실행할 생성·행동 패턴입니다.
        [Tooltip("실행할 패턴")]
        public FishSpawnPattern Pattern;

        [Tooltip("실행할 패턴 횟수의 최소값")]
        // 최초 생성 시 선택할 패턴 실행 개수의 최솟값입니다.
        [Min(0)] public int MinCount = 1;

        [Tooltip("실행할 패턴 횟수의 최대값")]
        // 최초 생성 시 선택할 패턴 실행 개수의 최댓값입니다.
        [Min(0)] public int MaxCount = 1;

        [Tooltip("소실된 물고기의 리스폰 시간")]
        // 각 실행본에서 소실된 물고기를 보충하기까지 기다릴 시간입니다.
        [Min(0f)] public float RespawnDelay = 5f;
    }

    /// <summary>
    /// 중심 오브젝트와 반지름으로 물고기의 최초 생성 영역을 설정합니다.
    /// </summary>
    [Serializable]
    private class SpawnGroup
    {
        public string GroupName;

        // 이 그룹에서 최초 생성하거나 재생성한 물고기 전체에 적용할 단일 Layer입니다.
        [FishSpawnLayer] public int Layer = 7;

        // 이 그룹에서 생성하는 물고기의 밝기입니다.
        [Tooltip("1은 기본 밝기이며, 낮을수록 어두워집니다.")]
        [Range(0f, 1f)]
        public float Brightness = 1f;

        // 이 그룹의 중심이 되는 SpawnPointN 오브젝트를 연결합니다.
        [Tooltip("이 오브젝트를 중심으로 원이 정해집니다.")]
        public Transform SpawnPoint;

        [Tooltip("최초 생성 범위의 반지름")]
        // 월드 좌표 기준으로 최초 생성 위치를 정하는 반지름입니다.
        [Min(0.01f)] public float Radius = 5f;

        // -1은 기존 반경 전체를 사용하고, 0 이상은 무리 중심의 랜덤 범위만 제한합니다.
        public float CenterSpreadRadius = -1f;

        // 최초 생성 원과 별개로 적용할 최소 월드 높이입니다.
        [Tooltip("이 그룹 물고기가 이동할 수 있는 최소 월드 Y 좌표")]
        public float MinY = -5f;

        // 최초 생성 원과 별개로 적용할 최대 월드 높이입니다.
        [Tooltip("이 그룹 물고기가 이동할 수 있는 최대 월드 Y 좌표")]
        public float MaxY = 5f;

        [Tooltip("이 영역에서 처음 생성할 물고기 패턴들을 넣는 곳입니다.")]
        // 이 영역에서 실행할 물고기 생성 패턴입니다.
        public SpawnEntry[] Patterns;
    }

    // 생성되는 물고기에게 주입할 씬의 Hero입니다.
    [SerializeField] private Hero _hero;

    // 모든 그룹과 전투 이동에 공통으로 적용할 월드 XY 범위입니다.
    [SerializeField] private Vector2 _movementMin = new Vector2(-175f, -235f);
    [SerializeField] private Vector2 _movementMax = new Vector2(175f, -5f);

    // 전역 경계에 닿기 전에 무리의 방향을 바꾸기 시작할 거리입니다.
    [SerializeField, Min(0f)] private float _boundaryTurnDistance = 5f;

    // 입력 순서와 관계없이 같은 전역 범위를 모든 개체에게 전달합니다.
    public Vector2 MovementMin => Vector2.Min(_movementMin, _movementMax);
    public Vector2 MovementMax => Vector2.Max(_movementMin, _movementMax);
    public float BoundaryTurnDistance => Mathf.Max(0f, _boundaryTurnDistance);

    // 그룹마다 이름, 영역, 실행할 패턴 목록을 설정합니다.
    [SerializeField] private SpawnGroup[] _waterDepthCategories;

    // 그룹 Layer가 달라도 원래 물고기끼리의 충돌 제외 규칙은 유지합니다.
    private readonly Dictionary<Collider, int> _spawnedColliderLayers = new Dictionary<Collider, int>();

    // 이 스포너가 소유한 실행용 패턴 복제본입니다.
    private readonly List<FishSpawnPattern> _runningPatterns =
        new List<FishSpawnPattern>();

    // 일반 패턴의 판단 주기와 연속 경로 실행을 관리합니다.
    private readonly FishPatternScheduler _patternScheduler = new FishPatternScheduler();

    // 리스폰은 패턴 판단과 독립된 시간 및 예약 목록을 사용합니다.
    private readonly FishRespawnScheduler _respawnScheduler = new FishRespawnScheduler();

    /// <summary>
    /// 그룹의 중심과 반지름으로 영역을 만들고 런타임 패턴을 생성합니다.
    /// </summary>
    private void SpawnInitialPatterns(int[][] spawnPlan, InitialSpawnSummary summary)
    {
        // 설정된 그룹이 없으면 생성하지 않습니다.
        if (_waterDepthCategories == null)
        {
            return;
        }

        // 등록된 그룹을 순서대로 처리합니다.
        for (int groupIndex = 0;
             groupIndex < _waterDepthCategories.Length;
             groupIndex++)
        {
            // 현재 그룹의 설정을 가져옵니다.
            SpawnGroup group = _waterDepthCategories[groupIndex];

            // 비어 있는 그룹은 건너뜁니다.
            if (group == null || group.Patterns == null)
            {
                continue;
            }

            // 중심 오브젝트가 없으면 이 그룹의 생성을 중단합니다.
            if (group.SpawnPoint == null)
            {
                Debug.LogError(
                    $"그룹 [{groupIndex}]의 SpawnPoint를 연결해주세요.",
                    this);

                continue;
            }

            // 시작 시점의 위치를 기준으로 그룹이 공유할 원형 영역을 만듭니다.
            FishSpawnArea area = new FishSpawnArea(
                group.SpawnPoint.position,
                group.Radius, group.CenterSpreadRadius);

            // 높이 설정이 유한한 좌표인지 검사합니다.
            if (float.IsNaN(group.MinY) || float.IsInfinity(group.MinY)
                || float.IsNaN(group.MaxY) || float.IsInfinity(group.MaxY))
            {
                // 잘못된 높이로 물고기를 생성하지 않도록 중단합니다.
                Debug.LogError($"그룹 [{groupIndex}]의 MinY와 MaxY를 확인해 주세요.", this);
                continue;
            }

            // 그룹의 원래 높이 범위와 전역 허용 범위가 겹치는 부분을 구합니다.
            float minY = Mathf.Max(Mathf.Min(group.MinY, group.MaxY), MovementMin.y);
            float maxY = Mathf.Min(Mathf.Max(group.MinY, group.MaxY), MovementMax.y);

            // 전역 범위 밖에만 존재하는 그룹은 불가능한 위치에 생성하지 않습니다.
            if (minY > maxY)
            {
                Debug.LogWarning(
                    $"그룹 [{groupIndex}]의 MinY/MaxY가 전역 이동 범위 밖이므로 생성을 건너뜁니다.",
                    this);
                continue;
            }

            // 패턴과 복귀 이동도 같은 유효 높이 범위를 공유합니다.
            FishVerticalRange verticalRange = new FishVerticalRange(minY, maxY);

            // 생성 원과 높이 범위가 겹치지 않으면 생성을 중단합니다.
            if (verticalRange.MaxY < area.Center.y - area.Radius
                || verticalRange.MinY > area.Center.y + area.Radius)
            {
                // 원과 높이 범위를 동시에 만족할 수 없는 설정을 알립니다.
                Debug.LogError(
                    $"그룹 [{groupIndex}]의 생성 원과 MinY/MaxY 범위가 겹치지 않습니다.",
                    this);
                continue;
            }

            // 그룹에 등록된 패턴 설정을 순서대로 처리합니다.
            for (int entryIndex = 0;
                 entryIndex < group.Patterns.Length;
                 entryIndex++)
            {
                // 현재 생성 설정을 가져옵니다.
                SpawnEntry entry = group.Patterns[entryIndex];

                // 패턴이 누락된 설정은 건너뜁니다.
                if (entry == null || entry.Pattern == null)
                {
                    Debug.LogError(
                        $"'{group.SpawnPoint.name}'의 Patterns[{entryIndex}]를 확인해주세요.",
                        this);

                    continue;
                }

                // 미리 추첨한 실행 횟수를 그대로 사용하여 총 예정 수와 실제 시도를 일치시킵니다.
                int spawnCount = spawnPlan[groupIndex][entryIndex];

                // 선택한 개수만큼 독립된 런타임 패턴을 만듭니다.
                for (int instanceIndex = 0;
                     instanceIndex < spawnCount;
                     instanceIndex++)
                {
                    // 원본 에셋과 실행 상태를 분리합니다.
                    FishSpawnPattern runtime = entry.Pattern.CreateRuntime();

                    // 생성과 반환 이벤트 전에 이 스포너의 예약 관리자를 연결합니다.
                    runtime.BindRespawnScheduler(_respawnScheduler);

                    // 최초 생성과 리스폰에 사용할 그룹 밝기를 저장합니다.
                    runtime.SetBrightness(group.Brightness);

                    // 패턴 실행본이 그룹 Layer를 기억해야 재생성에도 같은 값을 적용합니다.
                    if (!runtime.SetSpawnLayer(group.Layer))
                    {
                        runtime.Dispose();
                        Destroy(runtime);
                        continue;
                    }

                    // 같은 그룹의 원형 영역을 전달하여 생성합니다.
                    bool spawned = runtime.Spawn(
                        this,
                        area,
                        verticalRange,
                        Mathf.Max(0f, entry.RespawnDelay));

                    // 생성에 실패한 런타임과 부분 생성물을 정리합니다.
                    if (!spawned)
                    {
                        runtime.Dispose();
                        Destroy(runtime);
                        continue;
                    }

                    // 생성된 패턴을 이동 및 재생성 관리 대상으로 등록합니다.
                    _runningPatterns.Add(runtime);

                    // 부분 생성 후 폐기된 무리는 제외하고 전체 생성에 성공한 실행본만 집계합니다.
                    if (runtime is BackgroundFishPattern) summary.BackgroundSuccess += runtime.InitialMemberCount;
                    else summary.CatchableSuccess += runtime.InitialMemberCount;

                    // 배경 패턴은 최초 등록 시 제외하여 매 물리 프레임의 조건 검사도 없앱니다.
                    _patternScheduler.Register(runtime);
                }
            }
        }
    }

    /// <summary>
    /// 물리 시간에 맞춰 예약 관리자와 패턴 스케줄러를 진행합니다.
    /// </summary>
    private void FixedUpdate()
    {
        // 반환된 자리의 예약만 처리하고 비활성화 중에는 두 시계 모두 멈춥니다.
        _respawnScheduler.Advance(Time.fixedDeltaTime);

        // 물고기의 감지 이후 실행하며 시간이 됐거나 요청된 패턴만 판단합니다.
        _patternScheduler.Advance(Time.fixedDeltaTime);
    }

    /// <summary>
    /// 스포너가 다시 활성화되면 독립적으로 갱신하는 배경 무리도 재개합니다.
    /// </summary>
    private void OnEnable()
    {
        // 최초 활성화에서는 목록이 비어 있으며, 이후 활성화에서만 기존 실행본을 재개합니다.
        foreach (FishSpawnPattern pattern in _runningPatterns)
        {
            pattern.Resume();
        }
    }

    /// <summary>
    /// 스포너가 비활성화되면 패턴이 담당하는 이동을 정지합니다.
    /// </summary>
    private void OnDisable()
    {
        // FixedUpdate도 중단되므로 리스폰 대기 시간 역시 멈춥니다.
        foreach (FishSpawnPattern pattern in _runningPatterns)
        {
            pattern.Pause();
        }
    }

    /// <summary>
    /// 스포너가 소멸하면 소유한 물고기와 실행용 패턴을 정리합니다.
    /// </summary>
    private void OnDestroy()
    {
        // 패턴 정리보다 먼저 모든 퀘스트 이벤트 연결과 관리 기록을 종료합니다.
        ClearFishQuestEntries();

        // 생성된 물고기를 정리한 뒤 패턴 복제본을 제거합니다.
        foreach (FishSpawnPattern pattern in _runningPatterns)
        {
            pattern.Dispose();
            Destroy(pattern);
        }

        // 제거한 실행본의 참조를 비웁니다.
        _runningPatterns.Clear();
        _patternScheduler.Clear();
        _respawnScheduler.Clear();
        // Collider 쌍의 충돌 제외는 개체 파괴 시 Unity가 정리하며 참조만 해제합니다.
        _spawnedColliderLayers.Clear();
    }

    /// <summary>
    /// 전달받은 위치에 물고기를 생성하고 초기 상태를 설정합니다.
    /// </summary>
    public bool SpawnFish(
        Fish prefab,
        Vector3 position,
        Vector2 initialDirection,
        FishVerticalRange verticalRange,
        float brightness,
        out Fish fish)
    {
        // 기존 호출자는 프리팹에 지정된 Layer를 유지합니다.
        int layer = prefab != null ? prefab.gameObject.layer : 7;
        return SpawnFish(prefab, position, initialDirection, verticalRange, brightness, layer, out fish);
    }

    /// <summary>
    /// 물고기 인스턴스 전체에 그룹 Layer를 적용하고 이동 및 표시 정보를 초기화합니다.
    /// </summary>
    public bool SpawnFish(
        Fish prefab,
        Vector3 position,
        Vector2 initialDirection,
        FishVerticalRange verticalRange,
        float brightness,
        int layer,
        out Fish fish)
    {
        // 생성 실패 시 반환할 기본값을 설정합니다.
        fish = null;

        // 생성과 초기화에 필요한 프리팹과 데이터를 검사합니다.
        if (prefab == null || prefab.Data == null || verticalRange == null || layer < 0 || layer > 31 || string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
        {
            // 누락된 설정을 확인할 수 있도록 오류를 출력합니다.
            Debug.LogError(
                "Fish 프리팹, Data와 높이 범위를 확인해 주세요.",
                this);

            // 생성에 필요한 정보가 없으므로 중단합니다.
            return false;
        }

        // 초기 위치도 이동 높이 범위를 만족하도록 Y만 보정합니다.
        Vector2 spawnXY = verticalRange.ClampPosition(
            new Vector2(position.x, position.y));

        // 최초 생성과 리스폰 위치에도 전역 XY 범위를 적용하고 Z는 유지합니다.
        position.x = Mathf.Clamp(spawnXY.x, MovementMin.x, MovementMax.x);
        position.y = Mathf.Clamp(spawnXY.y, MovementMin.y, MovementMax.y);

        // 기존 씬의 공용 풀을 확보하고 사용 가능한 물고기를 대여합니다.
        EnsureFishPool();
        try
        {
            // OnSpawn 이후, 활성화 전에 이번 사용의 데이터와 위치를 초기화합니다.
            GameObject rented = _poolManager.Get(
                prefab.gameObject, position, prefab.transform.rotation,
                initialize: instance =>
                {
                    // 초기화 실패는 풀 매니저가 회수·폐기하도록 예외로 전달합니다.
                    Fish candidate = instance.GetComponent<Fish>();
                    if (candidate == null || !candidate.Initialize(initialDirection, _hero, this))
                        throw new InvalidOperationException("풀 물고기 초기화에 실패했습니다.");

                    // 활성화 전 새 그룹의 높이 제한과 밝기를 적용합니다.
                    candidate.SetVerticalRange(verticalRange);
                    candidate.SetBrightness(brightness);
                });

            // 콜라이더가 활성화된 뒤 기존 물고기 간 충돌 제외 규칙을 적용합니다.
            fish = rented.GetComponent<Fish>();

            // OnEnable 검증 실패 개체는 기존 반환 경로로 구독까지 정리하고 실패로 집계합니다.
            if (!fish.IsSpawnReady)
            {
                DespawnFish(fish);
                fish = null;
                return false;
            }

            // 활성화 후 검증을 통과한 물고기만 충돌 레이어를 적용합니다.
            ApplySpawnLayer(fish, layer);
            return true;
        }
        catch (Exception exception)
        {
            // 활성화 이후 설정 실패도 대여 개체가 남지 않게 정리합니다.
            if (fish != null) DespawnFish(fish);
            fish = null;
            Debug.LogException(exception, this);
            return false;
        }
    }

    /// <summary>
    /// 생성한 개체의 Layer를 변경하고 원래 물고기끼리 무시하던 충돌을 유지합니다.
    /// </summary>
    private void ApplySpawnLayer(Fish fish, int layer)
    {
        // Layer 변경 전 콜라이더별 원래 Layer를 기억합니다.
        Collider[] colliders = fish.GetComponentsInChildren<Collider>(true);
        int[] sourceLayers = new int[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            sourceLayers[i] = colliders[i].gameObject.layer;
        }

        // 피격·포획 콜라이더를 포함한 모든 자식에 같은 그룹 Layer를 적용합니다.
        foreach (Transform child in fish.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }

        // 사라진 개체의 참조가 재생성마다 누적되지 않도록 제거합니다.
        List<Collider> removed = new List<Collider>();
        foreach (var entry in _spawnedColliderLayers)
        {
            if (entry.Key == null) removed.Add(entry.Key);
        }
        foreach (Collider collider in removed) _spawnedColliderLayers.Remove(collider);

        for (int i = 0; i < colliders.Length; i++)
        {
            foreach (var entry in _spawnedColliderLayers)
            {
                // 같은 개체의 콜라이더는 원래 하나의 몸체이므로 쌍별 설정에서 제외합니다.
                if (entry.Key.transform.IsChildOf(fish.transform)) continue;

                // 원래 제외하던 물고기 쌍만 개별 제외하며 전역 지형 충돌표는 바꾸지 않습니다.
                if (Physics.GetIgnoreLayerCollision(sourceLayers[i], entry.Value) &&
                    !Physics.GetIgnoreLayerCollision(colliders[i].gameObject.layer, entry.Key.gameObject.layer))
                {
                    Physics.IgnoreCollision(colliders[i], entry.Key, true);
                }
            }
            _spawnedColliderLayers[colliders[i]] = sourceLayers[i];
        }
    }

    /// <summary>
    /// 패턴이 계산한 이동 방향과 속도를 물고기에 전달합니다.
    /// </summary>
    public void Follow(
        Fish fish,
        Vector2 direction,
        float speed,
        float deltaTime)
    {
        // 제거된 물고기에는 명령을 전달하지 않습니다.
        if (fish == null)
        {
            return;
        }

        // 물고기에 방향과 속도를 전달하여 이동하게 합니다.
        fish.Follow(direction, speed, deltaTime);
    }
}