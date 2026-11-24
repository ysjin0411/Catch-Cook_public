using UnityEngine;

/// <summary>날씨 판정과 분리하여 스시 가게 바깥의 빗방울 Particle System을 제어합니다.</summary>
[DisallowMultipleComponent]
public sealed class SushiRainEffect : MonoBehaviour
{
    [Header("실외 비 파티클")]
    [SerializeField] ParticleSystem[] _rainSystems;

    [Header("날씨 시스템 연결 전 초기 상태")]
    [SerializeField] bool _startRaining = true;

    // 비의 목표 상태를 보관하여 오브젝트가 다시 활성화되어도 유지합니다.
    bool _isRaining;
    // 활성화 전에 전달된 날씨 요청이 초기 설정에 덮어쓰이지 않도록 구분합니다.
    bool _hasRequestedState;

    // 실제 잔여 파티클 수가 아니라 현재 비 생성 여부를 반환합니다.
    public bool IsRaining => _hasRequestedState ? _isRaining : _startRaining;

    /// <summary>최초 활성화에서는 초기 설정을, 이후에는 마지막 날씨 요청을 적용합니다.</summary>
    void OnEnable()
    {
        // 아직 외부 요청이 없다면 인스펙터의 초기 날씨를 사용합니다.
        if (!_hasRequestedState)
        {
            // 첫 활성화 이후에는 동일한 초기값을 반복해서 적용하지 않습니다.
            _isRaining = _startRaining;
            _hasRequestedState = true;
        }

        // 자식 파티클 시스템을 저장된 날씨 상태와 일치시킵니다.
        ApplyState();
    }

    /// <summary>비를 켜거나 새 빗방울 생성을 중단합니다. 남은 빗방울은 수명이 끝나면 사라집니다.</summary>
    public void SetRaining(bool isRaining)
    {
        // 같은 날씨 요청으로 이미 재생 중인 파티클을 다시 시작하지 않습니다.
        if (_hasRequestedState && _isRaining == isRaining) return;

        // 비활성화 중에 들어온 요청도 기억해 다음 활성화에서 반영합니다.
        _isRaining = isRaining;
        _hasRequestedState = true;

        // 활성 상태에서만 실제 파티클을 조작합니다.
        if (isActiveAndEnabled) ApplyState();
    }

    /// <summary>씬 전환 등 즉시 제거가 필요한 상황에서 비를 끄고 남아 있는 빗방울도 지웁니다.</summary>
    public void StopImmediately()
    {
        // 이후 다시 활성화되어도 비가 자동으로 켜지지 않도록 목표 상태를 저장합니다.
        _isRaining = false;
        _hasRequestedState = true;

        // 잔여 입자를 포함해 이 컴포넌트가 담당하는 비만 정리합니다.
        ClearParticles();
    }

    /// <summary>목표 날씨 상태에 따라 각 빗줄기 레이어의 방출을 변경합니다.</summary>
    void ApplyState()
    {
        // 프리팹 구성 중 참조가 아직 지정되지 않은 경우를 안전하게 처리합니다.
        if (_rainSystems == null) return;

        // 등록된 실외 비만 제어하여 벚꽃 파티클에는 영향을 주지 않습니다.
        foreach (ParticleSystem system in _rainSystems)
        {
            // 선택적으로 비워 둔 레이어는 건너뜁니다.
            if (system == null) continue;

            // 비가 켜질 때는 방출이 중단된 시스템만 재생합니다.
            if (_isRaining)
            {
                // 동일한 날씨가 반복 전달되어도 파티클의 재생 시간을 유지합니다.
                if (!system.isEmitting) system.Play(false);
            }
            else
            {
                // 이미 떨어지는 빗방울은 남겨 날씨 전환이 자연스럽게 보이게 합니다.
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    /// <summary>비활성화 시 화면에 남는 입자를 정리하되 요청된 날씨 상태는 유지합니다.</summary>
    void OnDisable()
    {
        // 씬을 떠나거나 루트를 숨길 때 월드 공간 입자까지 제거합니다.
        ClearParticles();
    }

    /// <summary>등록된 모든 비 레이어의 방출과 잔여 입자를 즉시 정리합니다.</summary>
    void ClearParticles()
    {
        // 참조가 없는 프리팹도 예외 없이 비활성화되게 합니다.
        if (_rainSystems == null) return;

        // 각 시스템을 자식 재귀 호출 없이 한 번씩 정리합니다.
        foreach (ParticleSystem system in _rainSystems)
        {
            // 연결된 시스템에만 정리 명령을 전달합니다.
            if (system != null)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
