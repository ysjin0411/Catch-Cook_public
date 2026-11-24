using SushiPouringV3;
using UnityEngine;

/// <summary>손님 예약과 음료 미니게임, 실제 서빙 및 조작 제한을 연결합니다.</summary>
[DisallowMultipleComponent]
public sealed class SushiDrinkMinigameCoordinator : MonoBehaviour
{
    // 플레이어, 씬의 View와 기존 상호작용 센서입니다.
    [SerializeField] GroundHero _hero;
    [SerializeField] SushiMinigameView _view;
    [SerializeField] Collider _sensorCollider;
    // 음료 미니게임 실패 시 해당 손님의 남은 대기시간에서 차감할 초입니다.
    [SerializeField, Min(0f)] float _failureWaitingTimePenalty = 5f;
    // 시작 당시의 손님과 제공자입니다.
    GuestNPC _guest;
    SushiCarrier _provider;
    // 파괴된 손님 참조와 무관하게 정리할 진행 상태입니다.
    bool _busy;
    bool _finished;
    public bool IsBusy => _busy;
    internal event System.Action<GuestNPC> DrinkServed;

    /// <summary>같은 루트의 플레이어 참조를 준비합니다.</summary>
    void Awake()
    {
        // 명시적인 연결이 없을 때만 자동으로 찾습니다.
        if (_hero == null) _hero = GetComponent<GroundHero>();
    }

    /// <summary>손님을 예약하고 준비 연출부터 시작합니다.</summary>
    internal bool TryBegin(GuestNPC guest)
    {
        // 중복 실행과 필수 참조의 유효성을 검사합니다.
        if (_busy || !isActiveAndEnabled || _hero == null || !_hero.isActiveAndEnabled ||
            _view == null || !_view.isActiveAndEnabled || _view.IsOpen || guest == null) return false;
        // 실제 접촉 범위에서만 손님의 제공 권한을 확보합니다.
        if (!SushiInteractor.IsTouching(_sensorCollider, guest.InteractionArea)) return false;
        SushiCarrier provider = _hero.SushiCarrier;
        // 미니게임 진입점에서도 활성 상태와 빈손 조건을 검사합니다.
        if (provider == null || !provider.isActiveAndEnabled || provider.CurrentSushi != null) return false;
        if (!guest.TryReserveDrinkMinigame(provider)) return false;
        // 예약 직후부터 준비 시간을 포함해 이동과 일반 상호작용을 막습니다.
        _guest = guest; _provider = provider; _busy = true; _finished = false;
        _hero.IsDrinkMinigameOpen = true;
        _hero.SetMoveInput(Vector2.zero);
        _view.OnDrinkCompleted += HandleCompleted;
        try
        {
            // 화면을 열기 전에 주문에 맞는 색상, 양쪽 용기와 계산 설정을 적용합니다.
            if (!_view.TryConfigureDrink(guest.SelectedSushi))
            {
                // 설정 누락으로 시작하지 못하면 손님 예약과 조작 제한을 정리합니다.
                EndInteraction();
                return false;
            }
            // View가 Ready, Start와 시작 지연을 담당합니다.
            _view.Show();
            if (!_view.IsOpen) { EndInteraction(); return false; }
            return true;
        }
        catch
        {
            // 시작 오류에도 예약과 조작 제한을 남기지 않습니다.
            EndInteraction();
            throw;
        }
    }

    /// <summary>화면 종료와 손님, 주문 및 접촉 이탈을 검사합니다.</summary>
    void Update()
    {
        // 준비 또는 진행 중인 경우에만 검사합니다.
        if (!_busy) return;
        if (_hero == null || !_hero.isActiveAndEnabled || _view == null ||
            !_view.isActiveAndEnabled || !_view.IsOpen) { EndInteraction(); return; }
        // 판정 이후에는 결과 창이 닫힐 때까지 유지합니다.
        if (_finished) return;
        if (_guest == null || !_guest.IsDrinkMinigameValid(_provider) ||
            !SushiInteractor.IsTouching(_sensorCollider, _guest.InteractionArea)) EndInteraction();
    }

    /// <summary>
    /// 성공 시 기존 서빙을 처리하고, 실패 시 대기시간 패널티를 한 번 적용합니다.
    /// 취소에는 패널티를 적용하지 않습니다.
    /// </summary>
    void HandleCompleted(FluidCompletion result)
    {
        // 이미 종료했거나 결과를 처리한 미니게임의 중복 호출을 막습니다.
        if (!_busy || _finished) return;

        // 취소는 실패로 취급하지 않고 기존 정리 절차만 실행합니다.
        if (result.Result == FluidResult.Cancelled)
        {
            // 예약과 화면, 플레이어 조작 제한을 정리합니다.
            EndInteraction();
            // 취소 결과에는 패널티나 서빙을 적용하지 않습니다.
            return;
        }

        // 결과 처리를 확정하여 패널티와 서빙의 중복 적용을 막습니다.
        _finished = true;

        // 성공 이외의 결과는 실패 처리 후 결과 화면을 유지합니다.
        if (result.Result != FluidResult.Success)
        {
            // 실제 실패 판정일 때만 해당 손님에게 패널티를 요청합니다.
            if (_guest != null &&
                (result.Result == FluidResult.TooLittle || result.Result == FluidResult.TooMuch))
            {
                // 손님 내부에서 예약 유효성을 검사하고 N초를 차감합니다.
                _guest.ApplyDrinkMinigameFailurePenalty(_provider, _failureWaitingTimePenalty);
            }

            // 예약을 해제하지 않아 결과 표시 중에도 시간 정지가 유지됩니다.
            return;
        }

        // 성공 시에도 손님과 거리, 주문 조건을 다시 검사합니다.
        if (_guest == null || !SushiInteractor.IsTouching(_sensorCollider, _guest.InteractionArea) ||
            !_guest.CompleteDrinkMinigame(_provider))
        {
            // 서빙할 수 없다면 기존 정리 절차를 실행합니다.
            EndInteraction();
            // 서빙 성공 이벤트가 발생하지 않도록 종료합니다.
            return;
        }
        // 기존 튜토리얼 등의 구독자에게 서빙 성공을 알립니다.
        DrinkServed?.Invoke(_guest);
    }

    /// <summary>이벤트, 화면, 손님 예약과 플레이어 조작 제한을 정리합니다.</summary>
    void EndInteraction()
    {
        // Hide에서 발생하는 취소 이벤트의 재진입을 막습니다.
        if (!_busy) return;
        _busy = false;
        try
        {
            // 구독을 먼저 해제하고 화면을 닫습니다.
            if (_view != null) { _view.OnDrinkCompleted -= HandleCompleted; _view.Hide(); }
        }
        finally
        {
            // 화면 정리에 실패해도 손님의 대기와 플레이어 조작은 복구합니다.
            if (_guest != null) _guest.ReleaseDrinkMinigame(_provider);
            if (_hero != null) { _hero.IsDrinkMinigameOpen = false; _hero.SetMoveInput(Vector2.zero); }
            _guest = null; _provider = null; _finished = false;
        }
    }

    /// <summary>컴포넌트 비활성화 시 진행을 취소합니다.</summary>
    void OnDisable()
    {
        // 준비 중과 결과 표시 중에도 같은 정리 경로를 사용합니다.
        EndInteraction();
    }

    /// <summary>창 포커스를 잃으면 준비 구간까지 취소합니다.</summary>
    void OnApplicationFocus(bool focused)
    {
        // 아직 세션이 없는 Ready 연출도 취소합니다.
        if (!focused) EndInteraction();
    }
}
