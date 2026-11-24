using UnityEngine;

/// <summary>직원 강화 외형 데이터에 따라 의상과 파티클을 적용합니다.</summary>
[DisallowMultipleComponent]
public sealed class StaffAppearanceView : MonoBehaviour
{
    [Header("의상 렌더러")]
    [Tooltip("강화 단계에 맞춰 연결된 의상 렌더러의 머티리얼을 교체합니다.")]
    [SerializeField] bool _applyClothing;
    [SerializeField] SkinnedMeshRenderer _outwear;
    [SerializeField] SkinnedMeshRenderer _pants;
    [Header("파티클 생성 위치")]
    [SerializeField] Transform _particleAnchor;

    // 기본 머티리얼을 보관하여 데이터가 비어 있을 때 원래 색상으로 복원합니다.
    Material _defaultOutwearMaterial;
    Material _defaultPantsMaterial;
    bool _initialized;
    // 이 컴포넌트가 생성한 효과만 교체하고 정리합니다.
    GameObject _particleInstance;

    /// <summary>비활성 NPC에서도 최초 적용 전에 기본 외형을 저장합니다.</summary>
    bool EnsureInitialized()
    {
        // 한 번 저장한 기본 머티리얼을 강화 머티리얼로 덮어쓰지 않습니다.
        if (_initialized) return true;
        // 연결 누락은 외형만 중단하고 직원 능력치에는 영향을 주지 않습니다.
        if (_particleAnchor == null)
        {
            // 설정할 컴포넌트를 콘솔에서 바로 찾을 수 있도록 연결합니다.
            Debug.LogWarning("[직원 외형] Particle Anchor를 연결하세요.", this);
            return false;
        }
        // 공유 머티리얼 참조만 보관하며 메시와 렌더러 활성 상태는 변경하지 않습니다.
        _defaultOutwearMaterial = _outwear != null ? _outwear.sharedMaterial : null;
        _defaultPantsMaterial = _pants != null ? _pants.sharedMaterial : null;
        _initialized = true;
        return true;
    }

    /// <summary>단계별 머티리얼과 파티클을 적용하며 연결되지 않은 의상 부위는 유지합니다.</summary>
    public void Apply(StaffLevelAppearance appearance)
    {
        // Awake보다 먼저 호출되더라도 기본 외형부터 확보합니다.
        if (!EnsureInitialized()) return;
        // 부위별로 독립 적용하여 바지를 연결하지 않은 요리사도 상의만 교체합니다.
        if (_applyClothing)
        {
            // 상의는 지정한 머티리얼을 사용하고 빈 데이터는 기본 머티리얼로 복원합니다.
            if (_outwear != null)
                _outwear.sharedMaterial = appearance != null && appearance.OutwearMaterial != null
                    ? appearance.OutwearMaterial : _defaultOutwearMaterial;
            // 바지 렌더러가 연결된 서빙직원만 바지 색상을 변경합니다.
            if (_pants != null)
                _pants.sharedMaterial = appearance != null && appearance.PantsMaterial != null
                    ? appearance.PantsMaterial : _defaultPantsMaterial;
        }
        // 재설정으로 위치가 바뀌어도 이전 잔상이 남지 않도록 효과를 교체합니다.
        ReplaceParticle(appearance != null ? appearance.ParticlePrefab : null);
    }

    /// <summary>이전 효과를 제거하고 지정된 파티클 프리팹을 앵커 아래에 생성합니다.</summary>
    void ReplaceParticle(GameObject prefab)
    {
        // 지연 파괴되는 프레임 동안 두 효과가 겹치지 않게 먼저 숨깁니다.
        if (_particleInstance != null)
        {
            // 자신이 소유한 인스턴스만 제거합니다.
            _particleInstance.SetActive(false);
            Destroy(_particleInstance);
            _particleInstance = null;
        }
        // 빈 프리팹은 파티클 없는 단계로 처리합니다.
        if (prefab == null) return;
        // 프리팹 스케일을 유지하고 앵커의 위치와 방향을 기준으로 배치합니다.
        _particleInstance = Instantiate(prefab, _particleAnchor, false);
        _particleInstance.transform.localPosition = Vector3.zero;
        _particleInstance.transform.localRotation = Quaternion.identity;
        _particleInstance.SetActive(true);
        // 루트가 묶음 오브젝트인 프리팹도 자식 입자들을 각각 재생합니다.
        foreach (ParticleSystem particle in _particleInstance.GetComponentsInChildren<ParticleSystem>(true))
        {
            // 신규 단계의 시뮬레이션을 깨끗한 상태로 시작합니다.
            particle.Clear(false);
            particle.Play(false);
        }
    }

    /// <summary>컴포넌트 제거 시 별도 앵커 아래에 생성한 파티클도 정리합니다.</summary>
    void OnDestroy()
    {
        // NPC 계층이 이미 파괴된 경우 중복 정리를 생략합니다.
        if (_particleInstance == null) return;
        // 남아 있는 자신 소유의 효과를 숨기고 제거합니다.
        _particleInstance.SetActive(false);
        Destroy(_particleInstance);
    }
}
