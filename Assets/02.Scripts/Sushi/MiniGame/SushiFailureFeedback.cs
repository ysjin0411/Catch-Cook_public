using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 접시의 화면 충돌과 미니게임 실패에 비네트 연출을 적용합니다.
/// 미니게임 실패에는 카메라 흔들림도 함께 적용합니다.
/// </summary>
public sealed class SushiFailureFeedback : MonoBehaviour
{
    [Header("이벤트 연결")]
    [SerializeField] GuestReactionEffects _guestReactionEffects;
    [SerializeField] SushiMinigameView _minigameView;

    [Header("실패 전용 Volume")]
    [SerializeField] Volume _failureVolume;
    [SerializeField, Range(0f, 1f)] float _peakIntensity = 0.4f;
    [SerializeField, Min(0.01f)] float _fadeDuration = 0.3f;

    [Header("미니게임 실패 흔들림")]
    [SerializeField] CinemachineImpulseSource _impulseSource;
    [SerializeField, Min(0f)] float _shakeStrength = 0.3f;
    [SerializeField, Min(0.01f)] float _shakeDuration = 0.18f;

    Vignette _vignette;
    Coroutine _fadeRoutine;

    /// <summary>
    /// 실패 전용 Profile의 런타임 복사본에서 비네트를 가져옵니다.
    /// </summary>
    void Awake()
    {
        // Volume과 Profile이 연결되었는지 확인합니다.
        if (_failureVolume == null || _failureVolume.sharedProfile == null)
        {
            // Inspector에서 연결해야 할 항목을 안내합니다.
            Debug.LogError("실패 전용 Volume과 Profile을 연결하세요.", this);
            return;
        }

        // 원본 에셋 대신 런타임 Profile에서 비네트를 가져옵니다.
        if (!_failureVolume.profile.TryGet<Vignette>(out _vignette))
        {
            // Profile에 필요한 효과가 없으면 안내합니다.
            Debug.LogError("실패 Profile에 Vignette를 추가하세요.", this);
            return;
        }

        // 비네트를 활성화하고 평상시 강도를 0으로 초기화합니다.
        _vignette.active = true;
        _vignette.intensity.Override(0f);
    }

    /// <summary>
    /// 접시 충돌과 미니게임 결과 이벤트를 구독합니다.
    /// </summary>
    void OnEnable()
    {
        // 접시가 화면에 충돌하는 시점을 구독합니다.
        if (_guestReactionEffects != null)
        {
            _guestReactionEffects.ScreenHit += HandleScreenHit;
        }

        // 미니게임의 성공 또는 실패 판정을 구독합니다.
        if (_minigameView != null)
        {
            _minigameView.OnResultDetermined += HandleMinigameResult;
        }
    }

    /// <summary>
    /// 이벤트 구독을 해제하고 진행 중인 비네트를 정리합니다.
    /// </summary>
    void OnDisable()
    {
        // 접시 충돌 이벤트 구독을 해제합니다.
        if (_guestReactionEffects != null)
        {
            _guestReactionEffects.ScreenHit -= HandleScreenHit;
        }

        // 미니게임 판정 이벤트 구독을 해제합니다.
        if (_minigameView != null)
        {
            _minigameView.OnResultDetermined -= HandleMinigameResult;
        }

        // 진행 중인 비네트 코루틴을 중단합니다.
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }

        // 다음 활성화 때 효과가 남지 않도록 강도를 초기화합니다.
        if (_vignette != null)
        {
            _vignette.intensity.Override(0f);
        }
    }

    /// <summary>
    /// 접시 충돌의 기존 흔들림에 비네트만 추가합니다.
    /// </summary>
    void HandleScreenHit()
    {
        // 접시의 카메라 흔들림은 기존 코드가 처리합니다.
        PlayVignette();
    }

    /// <summary>
    /// 미니게임이 실패했을 때 흔들림과 비네트를 실행합니다.
    /// </summary>
    void HandleMinigameResult(bool success)
    {
        // 성공했을 때는 실패 연출을 실행하지 않습니다.
        if (success) return;

        // 연결된 Impulse Source로 흔들림을 발생시킵니다.
        if (_impulseSource != null &&
            _impulseSource.ImpulseDefinition != null)
        {
            // 이번 흔들림의 지속 시간을 설정합니다.
            _impulseSource.ImpulseDefinition.ImpulseDuration = _shakeDuration;

            // 설정한 세기로 카메라 흔들림 신호를 보냅니다.
            _impulseSource.GenerateImpulseWithForce(_shakeStrength);
        }

        // 카메라 흔들림과 함께 비네트를 표시합니다.
        PlayVignette();
    }

    /// <summary>
    /// 이전 비네트 연출을 중단하고 최대 강도부터 다시 실행합니다.
    /// </summary>
    public void PlayVignette()
    {
        // 비활성 상태이거나 비네트가 준비되지 않았다면 종료합니다.
        if (!isActiveAndEnabled || _vignette == null) return;

        // 연속 실패 시 기존 연출과 겹치지 않도록 중단합니다.
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
        }

        // 새로운 비네트 감소 연출을 시작합니다.
        _fadeRoutine = StartCoroutine(FadeVignette());
    }

    /// <summary>
    /// 실제 시간 기준으로 비네트 강도를 최대값에서 0까지 줄입니다.
    /// </summary>
    IEnumerator FadeVignette()
    {
        // 경과 시간과 0보다 큰 지속 시간을 준비합니다.
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, _fadeDuration);

        // 실패 직후부터 비네트가 선명하게 보이도록 설정합니다.
        _vignette.intensity.Override(_peakIntensity);

        // 지정한 시간 동안 강도를 점차 줄입니다.
        while (elapsed < duration)
        {
            // 다음 프레임까지 기다립니다.
            yield return null;

            // 시간 정지 중에도 연출이 끝나도록 실제 시간을 사용합니다.
            elapsed += Time.unscaledDeltaTime;

            // 경과 비율에 따라 비네트 강도를 0으로 줄입니다.
            _vignette.intensity.Override(
                Mathf.Lerp(_peakIntensity, 0f, elapsed / duration));
        }

        // 연출이 끝난 상태와 코루틴 참조를 정리합니다.
        _vignette.intensity.Override(0f);
        _fadeRoutine = null;
    }
}