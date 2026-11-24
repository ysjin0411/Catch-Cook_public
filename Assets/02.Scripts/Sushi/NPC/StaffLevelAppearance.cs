using System;
using UnityEngine;

/// <summary>직원 강화 한 단계의 파티클과 의상 머티리얼입니다.</summary>
[Serializable]
public sealed class StaffLevelAppearance
{
    [Tooltip("미지정하면 파티클을 표시하지 않습니다.")]
    [SerializeField] GameObject _particlePrefab;
    [Tooltip("미지정하면 기본 상의 머티리얼을 사용합니다.")]
    [SerializeField] Material _outwearMaterial;
    [Tooltip("서빙직원용 바지 머티리얼입니다. 요리사는 사용하지 않습니다.")]
    [SerializeField] Material _pantsMaterial;

    public GameObject ParticlePrefab => _particlePrefab;
    public Material OutwearMaterial => _outwearMaterial;
    public Material PantsMaterial => _pantsMaterial;
}
