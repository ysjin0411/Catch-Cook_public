using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 물고기 스포너의 공용 풀 연결과 개체 반환을 담당합니다.
/// </summary>
public partial class FishSpawner
{
    // 미연결 시 같은 씬의 기존 풀을 사용하고 없으면 씬 전용 풀을 준비합니다.
    [SerializeField] private PoolManager _poolManager;

    /// <summary>
    /// 현재 씬의 풀을 확보하여 반복 생성되는 물고기 인스턴스를 재사용합니다.
    /// </summary>
    private void EnsureFishPool()
    {
        // Inspector에서 주입했거나 이미 찾은 풀을 우선합니다.
        if (_poolManager != null) return;

        // 총알 등 다른 스포너가 준비한 같은 씬의 공용 풀을 재사용합니다.
        foreach (PoolManager candidate in FindObjectsByType<PoolManager>(FindObjectsSortMode.None))
        {
            if (candidate.gameObject.scene != gameObject.scene) continue;
            _poolManager = candidate;
            return;
        }

        // 이동하는 스포너의 자식이 되지 않도록 씬 루트에 풀을 생성합니다.
        GameObject root = new GameObject("FishPool");
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        _poolManager = root.AddComponent<PoolManager>();
    }

    /// <summary>
    /// 물고기를 대여한 풀로 반환하고 직접 배치된 비풀링 개체는 제거합니다.
    /// </summary>
    public void DespawnFish(Fish fish)
    {
        // 이미 제거된 물고기는 반환하지 않습니다.
        if (fish == null) return;

        // 풀 반환 중 Fish.OnDespawn이 패턴 소유권과 충돌 참조를 정리합니다.
        if (_poolManager != null && _poolManager.Return(fish.gameObject)) return;

        // 프리뷰 등 풀 밖에서 생성된 물고기도 동일한 상태 정리를 거칩니다.
        fish.OnDespawn();
        Destroy(fish.gameObject);
    }

    /// <summary>
    /// 반환된 물고기의 콜라이더를 스포너의 충돌 관리 목록에서 제거합니다.
    /// </summary>
    public void UnregisterPooledFish(Fish fish)
    {
        // 살아 있는 물고기의 콜라이더만 목록에서 제거합니다.
        if (fish == null) return;
        foreach (Collider collider in fish.GetComponentsInChildren<Collider>(true))
        {
            _spawnedColliderLayers.Remove(collider);
        }
    }
}
