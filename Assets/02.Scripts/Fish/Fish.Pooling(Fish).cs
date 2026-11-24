using System;
using UnityEngine;

/// <summary>
/// 물고기의 풀 대여·반환과 재사용 전 상태 복원을 담당합니다.
/// </summary>
public partial class Fish : IPoolable
{
    // 반환된 개체를 이전 패턴의 자리에서 즉시 해제하는 알림입니다.
    public event Action<Fish> Released;

    // 최초 대여 시 기록한 프리팹의 콜라이더와 레이어 설정입니다.
    private Collider[] _poolColliders;
    private bool[] _poolColliderEnabled;
    private bool[] _poolColliderTriggers;
    private Transform[] _poolTransforms;
    private int[] _poolLayers;
    private AnimatorCullingMode _poolAnimatorCulling;
    private bool _poolDefaultsCached;
    private bool _poolReleased;
    private bool _resetAnimationOnEnable;

    /// <summary>
    /// 풀에서 비활성인 루트는 허용하고 그 아래 실제 모델 계층의 활성 설정을 검사합니다.
    /// </summary>
    private bool IsSpawnHierarchyEnabled(Transform target)
    {
        // 비활성 대여 중에도 원래 꺼져 있는 자식 모델은 잘못된 설정으로 판단합니다.
        while (target != null && target != transform)
        {
            if (!target.gameObject.activeSelf) return false;
            target = target.parent;
        }

        // 검사 대상이 이 물고기의 루트 또는 자식이어야 합니다.
        return target == transform;
    }

    /// <summary>
    /// 비활성 상태에서 다음 대여에 필요한 물리·충돌·표시 상태를 준비합니다.
    /// </summary>
    public void OnSpawn()
    {
        // 새 대여가 이전 무리의 경로와 접촉을 사용하지 않게 합니다.
        BindGroupPattern(null, -1);

        // 이전 대여가 비정상 종료됐어도 새 생애에 구독과 보고 기록을 넘기지 않습니다.
        if (_spawner != null) _spawner.UnregisterFishQuest(this);

        // Awake 전에도 참조와 프리팹의 원래 설정을 확보합니다.
        CachePoolDefaults();

        // 이전 사용 중의 작업과 상태 알림 구독을 정리합니다.
        OnDisable();
        StopAllCoroutines();
        CancelInvoke();
        _initialized = false;
        // 이전 대여의 Animator 검증 결과를 다음 생애에 넘기지 않습니다.
        _deathAnimationValidated = false;
        _poolReleased = false;
        _captureColliders = null;
        _colliderEnabledBeforeCapture = null;

        // 이전 그룹의 레이어가 새 그룹의 충돌 설정에 섞이지 않도록 복원합니다.
        for (int i = 0; i < _poolTransforms.Length; i++)
        {
            if (_poolTransforms[i] != null) _poolTransforms[i].gameObject.layer = _poolLayers[i];
        }

        // 사망·포획으로 변경된 Trigger와 활성 상태를 원래 값으로 되돌립니다.
        for (int i = 0; i < _poolColliders.Length; i++)
        {
            if (_poolColliders[i] == null) continue;
            _poolColliders[i].isTrigger = _poolColliderTriggers[i];
            _poolColliders[i].enabled = _poolColliderEnabled[i];
        }

        // 포획 고정과 이전 이동 영역·접촉 정보를 초기화합니다.
        if (_mover != null) _mover.ResetForSpawn();

        // 애니메이션 상태 머신은 활성화될 때 초기 상태로 다시 시작합니다.
        if (_animator != null) _animator.cullingMode = _poolAnimatorCulling;
        _resetAnimationOnEnable = true;
    }

    /// <summary>
    /// 비활성화 전에 현재 사용의 작업·구독·소유 참조를 해제합니다.
    /// </summary>
    public void OnDespawn()
    {
        // 모델 상태와 소유 스포너 참조를 비우기 전에 퀘스트 생애를 종료합니다.
        if (_spawner != null) _spawner.UnregisterFishQuest(this);

        // 중복 반환 알림으로 다음 패턴의 상태를 변경하지 않게 합니다.
        if (_poolReleased) return;
        _poolReleased = true;

        // 행동과 피격 연출을 중단하고 모델 이벤트 구독을 해제합니다.
        OnDisable();
        StopAllCoroutines();
        CancelInvoke();
        _initialized = false;
        // 이전 대여의 Animator 검증 결과를 다음 생애에 넘기지 않습니다.
        _deathAnimationValidated = false;

        // 소유 스포너의 충돌 참조 목록에서 이 물고기를 제거합니다.
        if (_spawner != null) _spawner.UnregisterPooledFish(this);

        // 포획·추격·영역 복귀에 사용하던 이전 대여의 참조를 비웁니다.
        RestoreCaptureColliders();
        if (_mover != null) _mover.ResetForSpawn();
        _chaser.Initialize(null, null, Vector2.zero, Vector2.zero, 0f);
        _spawner = null;
        _verticalRange = null;
        _heroModel = null;
        _combatTarget = null;
        _heroHitbox = null;
        _isReturningToVerticalRange = false;
        // 감지 기록과 예약을 함께 초기화하여 다음 대여의 첫 검사를 보장합니다.
        ResetPlayerDetection();
        _combatRemaining = 0f;
        _knockbackVelocity = Vector2.zero;

        // 이전 패턴의 소유 참조를 즉시 해제하여 같은 프레임 재대여도 안전하게 합니다.
        NotifyReleased();
    }

    /// <summary>
    /// 풀 반환을 거치지 않고 직접 파괴된 개체의 퀘스트 연결을 정리합니다.
    /// </summary>
    private void OnDestroy()
    {
        // 정상 반환 시에는 이미 제거된 기록이므로 중복 호출도 안전합니다.
        if (_spawner != null) _spawner.UnregisterFishQuest(this);

        // 직접 파괴된 개체도 소유 패턴에 알리되 정상 반환의 알림은 중복하지 않습니다.
        if (!_poolReleased)
        {
            _poolReleased = true;
            NotifyReleased();
        }
    }

    /// <summary>
    /// 이전 대여의 구독을 비우고 소유 패턴에 반환 또는 파괴를 한 번 알립니다.
    /// </summary>
    private void NotifyReleased()
    {
        // 반환 또는 파괴된 객체가 이전 패턴에 접촉을 보고하지 않게 합니다.
        BindGroupPattern(null, -1);

        // 알림 중 다른 대여가 시작되더라도 새 구독을 지우지 않도록 먼저 분리합니다.
        Action<Fish> released = Released;
        Released = null;

        // 구독자가 있을 때만 이전 대여의 종료를 알립니다.
        if (released != null)
        {
            released(this);
        }
    }

    /// <summary>
    /// 획득 또는 패턴 종료 시 물고기를 소유 스포너의 풀로 반환합니다.
    /// </summary>
    public void Release()
    {
        // 이미 반환된 개체는 다시 반환하거나 파괴하지 않습니다.
        if (_poolReleased) return;

        // 스포너가 생성한 개체는 대여한 풀로 돌려보냅니다.
        if (_spawner != null)
        {
            _spawner.DespawnFish(this);
            return;
        }

        // 직접 배치한 테스트 개체는 동일한 정리 후 기존처럼 제거합니다.
        OnDespawn();
        Destroy(gameObject);
    }

    /// <summary>
    /// 최초 대여 시 변경 전 프리팹의 충돌 설정을 한 번만 저장합니다.
    /// </summary>
    private void CachePoolDefaults()
    {
        // 같은 인스턴스의 초기 설정은 한 번만 기록합니다.
        if (_poolDefaultsCached) return;
        _poolDefaultsCached = true;

        // Awake가 아직 호출되지 않아도 이동기를 준비합니다.
        if (_mover != null) _mover.EnsureReady();

        // 사망 시 변경되는 콜라이더 속성을 저장합니다.
        _poolColliders = GetComponentsInChildren<Collider>(true);
        _poolColliderEnabled = new bool[_poolColliders.Length];
        _poolColliderTriggers = new bool[_poolColliders.Length];
        for (int i = 0; i < _poolColliders.Length; i++)
        {
            _poolColliderEnabled[i] = _poolColliders[i].enabled;
            _poolColliderTriggers[i] = _poolColliders[i].isTrigger;
        }

        // 스폰 그룹에 따라 달라지는 자식 레이어의 원래 값을 저장합니다.
        _poolTransforms = GetComponentsInChildren<Transform>(true);
        _poolLayers = new int[_poolTransforms.Length];
        for (int i = 0; i < _poolTransforms.Length; i++)
        {
            _poolLayers[i] = _poolTransforms[i].gameObject.layer;
        }

        // 사망 시 AlwaysAnimate로 변경되는 설정도 재대여 시 복원합니다.
        if (_animator != null) _poolAnimatorCulling = _animator.cullingMode;
    }
}
