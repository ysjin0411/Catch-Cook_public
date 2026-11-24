using UnityEngine;

/// <summary>날씨 판정과 분리하여 먼바다 번개, 구름 섬광, 수면 반사를 함께 재생합니다.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class SushiStormLightning : MonoBehaviour
{
    [Header("이 컴포넌트가 담당하는 시각 효과")]
    [SerializeField] GameObject _boltRoot;
    [SerializeField] Renderer _skyRenderer;
    [SerializeField] Renderer[] _cloudRenderers;
    [SerializeField] Renderer _waterReflection;
    [SerializeField] Renderer _localGlow;

    [Header("재생 간격")]
    [SerializeField] Vector2 _intervalSeconds = new Vector2(7,13);
    [SerializeField, Min(0)] float _firstStrikeDelay = 2;
    [SerializeField] bool _previewInEditor = true;

    [Header("빛의 강도")]
    [SerializeField, Range(0,1)] float _skyStrength = .065f;
    [SerializeField, Range(0,2)] float _cloudStrength = .32f;
    [SerializeField, Range(0,3)] float _reflectionStrength = .85f;
    [SerializeField, Range(0,2)] float _glowStrength = .35f;

    // 게임의 전역 난수 상태를 바꾸지 않는 전용 난수를 사용합니다.
    System.Random _random;
    MaterialPropertyBlock _block;
    Renderer[] _boltRenderers;
    float[] _boltIntensities;
    double _nextStrike;
    double _strikeStarted;
    bool _isStriking;
    float _lastStrength = -1;

    // 같은 하늘을 비추는 번개끼리 밝기를 덮어쓰지 않도록 활성 연출을 공유합니다.
    static readonly System.Collections.Generic.HashSet<SushiStormLightning> ActiveStrikes = new System.Collections.Generic.HashSet<SushiStormLightning>();
    static readonly int FlashPosition = Shader.PropertyToID("_FlashPosition");

    // 반복 호출에서 프로퍼티 이름을 다시 변환하지 않습니다.
    static readonly int FlashStrength = Shader.PropertyToID("_FlashStrength");
    static readonly int Intensity = Shader.PropertyToID("_Intensity");

    /// <summary>활성화 시 편집 미리보기 또는 런타임의 첫 번개를 준비합니다.</summary>
    void OnEnable()
    {
        // 다른 날씨에서 돌아오면 이전 번개 진행 상태를 초기화합니다.
        _random = new System.Random(GetInstanceID());
        _block = new MaterialPropertyBlock();
        // 번개 선의 기본 강도를 보관해 발광 코어와 외곽 번짐의 비율을 유지합니다.
        _boltRenderers = _boltRoot != null ? _boltRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        _boltIntensities = new float[_boltRenderers.Length];
        for (int i = 0; i < _boltRenderers.Length; i++)
            _boltIntensities[i] = _boltRenderers[i].sharedMaterial != null ? _boltRenderers[i].sharedMaterial.GetFloat(Intensity) : 1;
        _isStriking = false;
        _lastStrength = -1;
        ActiveStrikes.Add(this);
        _nextStrike = Time.timeAsDouble + _firstStrikeDelay;
        ApplyStrength(!Application.isPlaying && _previewInEditor ? .65f : 0);
    }

    /// <summary>플레이 중에만 일정 간격의 번개와 짧은 이중 섬광을 진행합니다.</summary>
    void Update()
    {
        // 편집 모드에서는 임의로 번개를 반복하거나 씬을 변경하지 않습니다.
        if (!Application.isPlaying) return;
        double now = Time.timeAsDouble;
        if (!_isStriking && now >= _nextStrike) TriggerStrike();
        if (!_isStriking) return;

        // 첫 섬광 뒤 잠깐 어두워졌다가 두 번째 섬광이 사라집니다.
        float elapsed = (float)(now - _strikeStarted);
        float strength;
        if (elapsed < .07f) strength = 1 - elapsed / .07f;
        else if (elapsed < .12f) strength = 0;
        else if (elapsed < .28f) strength = .8f * (1 - (elapsed - .12f) / .16f);
        else
        {
            // 다음 번개 시간만 예약하고 모든 빛을 정상 상태로 복구합니다.
            _isStriking = false;
            float minimum = Mathf.Max(.5f,_intervalSeconds.x);
            float maximum = Mathf.Max(minimum,_intervalSeconds.y);
            _nextStrike = now + minimum + _random.NextDouble() * (maximum-minimum);
            strength = 0;
        }
        ApplyStrength(strength);
    }

    /// <summary>외부 날씨 연출이나 인스펙터 메뉴에서 번개 한 번을 요청합니다.</summary>
    [ContextMenu("Preview / Trigger Lightning")]
    public void TriggerStrike()
    {
        // 편집 모드는 정지된 비교 장면을 보여주고, 플레이 모드는 시간에 따라 진행합니다.
        if (!Application.isPlaying)
        {
            ApplyStrength(.65f);
            return;
        }
        _strikeStarted = Time.timeAsDouble;
        _isStriking = true;
        ApplyStrength(1);
    }

    /// <summary>날씨 전환이나 편집 미리보기 종료 시 섬광을 즉시 제거합니다.</summary>
    [ContextMenu("Clear Lightning")]
    public void ClearLightning()
    {
        // 예약된 진행 상태와 모든 시각 효과를 동시에 초기화합니다.
        _isStriking = false;
        _nextStrike = Time.timeAsDouble + Mathf.Max(.5f,_intervalSeconds.x);
        ApplyStrength(0);
    }

    /// <summary>Rainy 루트가 꺼질 때 남아 있는 섬광을 정리합니다.</summary>
    void OnDisable()
    {
        // 공유 머티리얼은 수정하지 않고 이 렌더러의 속성만 복구합니다.
        ActiveStrikes.Remove(this);
        ClearLightning();
    }

    /// <summary>번개와 하늘, 구름, 수면에 같은 섬광 강도를 적용합니다.</summary>
    void ApplyStrength(float strength)
    {
        // 밝기가 바뀐 프레임에만 렌더러 프로퍼티를 갱신합니다.
        strength = Mathf.Clamp01(strength);
        if (Mathf.Approximately(_lastStrength,strength)) return;
        _lastStrength = strength;
        if (_boltRoot != null) _boltRoot.SetActive(strength > .06f);
        // 번개 선 자체도 섬광 파형에 맞춰 서서히 사라지게 합니다.
        if (_boltRenderers != null)
        {
            for (int i = 0; i < _boltRenderers.Length; i++)
                SetProperty(_boltRenderers[i],Intensity,strength*_boltIntensities[i]);
        }
        ApplySharedFlash(_skyRenderer, false);
        if (_cloudRenderers != null)
        {
            // 등록된 Rainy 구름에만 섬광을 전달합니다.
            foreach (Renderer cloud in _cloudRenderers)
                ApplySharedFlash(cloud, true);
        }
        SetProperty(_waterReflection,Intensity,strength*_reflectionStrength);
        SetProperty(_localGlow,Intensity,strength*_glowStrength);
    }

    /// <summary>공유 하늘과 구름에 현재 가장 밝은 번개의 강도와 위치를 적용합니다.</summary>
    void ApplySharedFlash(Renderer target, bool isCloud)
    {
        // 다른 씬 또는 다른 구름에 연결된 번개는 합산 대상에서 제외합니다.
        if (target == null) return;
        float strongest = 0;
        Vector3 position = Vector3.zero;
        foreach (SushiStormLightning strike in ActiveStrikes)
        {
            if (strike == null || !strike.isActiveAndEnabled) continue;
            bool sharesTarget = isCloud
                ? strike._cloudRenderers != null && System.Array.IndexOf(strike._cloudRenderers, target) >= 0
                : strike._skyRenderer == target;
            if (!sharesTarget) continue;

            // 겹치는 번개를 더해 과노출시키지 않고 가장 강한 섬광을 선택합니다.
            float contribution = Mathf.Max(0, strike._lastStrength) * (isCloud ? strike._cloudStrength : strike._skyStrength);
            if (contribution <= strongest) continue;
            strongest = contribution;
            position = strike._localGlow != null ? strike._localGlow.transform.position : strike.transform.position;
        }

        // 다른 효과의 속성은 유지하고 현재 번개 주변으로 구름 섬광의 중심을 옮깁니다.
        if (_block == null) _block = new MaterialPropertyBlock();
        target.GetPropertyBlock(_block);
        _block.SetFloat(FlashStrength, strongest);
        if (isCloud) _block.SetVector(FlashPosition, new Vector4(position.x, position.y, position.z, 1));
        target.SetPropertyBlock(_block);
    }

    /// <summary>기존 프로퍼티 블록을 유지하면서 담당하는 값 하나를 변경합니다.</summary>
    void SetProperty(Renderer target,int property,float value)
    {
        // 누락된 선택적 효과는 건너뛰며 머티리얼 복제를 만들지 않습니다.
        if (target == null) return;
        if (_block == null) _block = new MaterialPropertyBlock();
        target.GetPropertyBlock(_block);
        _block.SetFloat(property,value);
        target.SetPropertyBlock(_block);
    }
}
