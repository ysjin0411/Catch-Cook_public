using UnityEngine;

/// <summary>
/// 음식 제공 시 동전과 식사 중 하트 파티클을 제어합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuestServingEffects : MonoBehaviour
{
    // 음식 제공 성공 시 한 번 재생할 동전 파티클입니다.
    [SerializeField] ParticleSystem _coinBurst;

    // Ordering 동안 반복 재생하는 머리 위 원 표시입니다.
    [SerializeField] ParticleSystem _orderingDots;


    // 식사 중 반복해서 재생할 하트 파티클입니다.
    [SerializeField] ParticleSystem _eatingHearts;

    /// <summary>
    /// 파티클 재생과 자동 비활성화 설정을 준비합니다.
    /// </summary>
    void Awake()
    {
        // 동전은 한 번, 하트는 반복 재생하도록 설정합니다.
        Configure(_coinBurst, false);
        Configure(_eatingHearts, true);

        // 손님 생성 시에는 모든 효과를 숨깁니다.
        ResetEffects();
    }

    /// <summary>
    /// 반복 여부와 재생 종료 후 자동 비활성화를 설정합니다.
    /// </summary>
    void Configure(ParticleSystem particles, bool loop)
    {
        // 연결되지 않은 파티클은 건너뜁니다.
        if (particles == null) return;

        // 설정 변경 전에 기존 입자와 재생 상태를 정리합니다.
        particles.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);

        // 활성화와 재생을 코드에서 명시적으로 제어합니다.
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = loop;

        // 마지막 입자가 사라지면 해당 자식 오브젝트를 끕니다.
        main.stopAction = ParticleSystemStopAction.Disable;
    }

    /// <summary>
    /// 제공 성공을 나타내는 동전 파티클을 처음부터 재생합니다.
    /// </summary>
    internal void PlayServingCoins()
    {
        // 비활성 상태이거나 파티클이 없으면 재생하지 않습니다.
        if (!isActiveAndEnabled || _coinBurst == null) return;

        // 이전 재생 흔적을 지우고 오브젝트를 활성화합니다.
        _coinBurst.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);
        _coinBurst.gameObject.SetActive(true);

        // 설정된 Burst 개수만큼 동전을 방출합니다.
        _coinBurst.Play(true);
    }

    /// <summary>
    /// 식사 시작 시 하트를 재생하고 종료 시 추가 방출을 멈춥니다.
    /// </summary>
    internal void SetEating(bool eating)
    {
        // 하트 파티클이 연결되지 않았다면 건너뜁니다.
        if (_eatingHearts == null) return;

        // 식사 중에는 하트를 반복 재생합니다.
        if (eating)
        {
            // 중복 호출로 이미 재생 중인 효과를 초기화하지 않습니다.
            if (!isActiveAndEnabled || _eatingHearts.isPlaying) return;

            // 새 식사에서는 이전 입자를 제거하고 다시 시작합니다.
            _eatingHearts.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            _eatingHearts.gameObject.SetActive(true);
            _eatingHearts.Play(true);
            return;
        }

        // 식사가 끝나면 새 하트만 멈추고 기존 하트는 자연스럽게 사라집니다.
        // 마지막 입자가 사라지면 Stop Action에 의해 자동 비활성화됩니다.
        if (_eatingHearts.gameObject.activeSelf)
        {
            _eatingHearts.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting);
        }
    }

    /// <summary>
    /// Ordering 동안 원 표시를 반복 재생하고 다른 상태에서는 즉시 숨깁니다.
    /// </summary>
    internal void SetOrdering(bool ordering)
    {
        // 연결된 주문 파티클이 없는 손님은 기존 동작을 유지합니다.
        if (_orderingDots == null) return;

        // 주문을 마쳤거나 효과 컴포넌트가 꺼졌다면 자식 입자까지 정리합니다.
        if (!ordering || !isActiveAndEnabled)
        {
            ClearAndHide(_orderingDots);
            return;
        }

        // 같은 상태로 다시 호출되어도 반복 재생을 처음부터 시작하지 않습니다.
        if (_orderingDots.isPlaying) return;

        // 숨겨 둔 효과를 켜고 자식 원들을 함께 반복 재생합니다.
        _orderingDots.gameObject.SetActive(true);
        _orderingDots.Play(true);
    }

    /// <summary>
    /// 손님 제거 또는 재사용을 위해 모든 효과를 즉시 정리합니다.
    /// </summary>
    internal void ResetEffects()
    {
        // 두 효과의 입자와 활성 상태를 함께 초기화합니다.
        ClearAndHide(_coinBurst);
        ClearAndHide(_eatingHearts);
        // 주문 표시도 함께 제거해 비활성화 후 잔여 입자가 남지 않게 합니다.
        ClearAndHide(_orderingDots);
    }

    /// <summary>
    /// 지정한 파티클의 입자를 지우고 오브젝트를 비활성화합니다.
    /// </summary>
    void ClearAndHide(ParticleSystem particles)
    {
        // 연결되지 않은 파티클은 건너뜁니다.
        if (particles == null) return;

        // 다음 활성화 때 이전 입자가 남지 않게 정리합니다.
        particles.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);

        // 사용하지 않는 파티클 오브젝트를 끕니다.
        particles.gameObject.SetActive(false);
    }

    /// <summary>
    /// 손님 또는 효과 컴포넌트가 비활성화되면 잔여 효과를 정리합니다.
    /// </summary>
    void OnDisable()
    {
        // 부모 비활성화와 오브젝트 재사용에도 입자가 남지 않게 합니다.
        ResetEffects();
    }
}