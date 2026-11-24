using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>음료 제공과 청소처럼 연속 상호작용이 필요한 대상을 정의합니다.</summary>
public interface ISushiHoldInteraction
{
    /// <summary>현재 제공자가 연속 상호작용을 진행할 수 있는지 확인합니다.</summary>
    bool CanHoldInteract(SushiCarrier provider);
    /// <summary>실제 접촉 상태에서 시간을 누적하고 완료 성공 여부를 반환합니다.</summary>
    bool TickHoldInteract(SushiCarrier provider, Collider sensor, float deltaTime);
    /// <summary>해당 제공자의 연속 상호작용을 취소합니다.</summary>
    void CancelHoldInteract(SushiCarrier provider);
}

[System.Serializable]
public class SushiHoldSession
{
    // 한 대상의 진행 시간을 소유하는 제공자 한 명입니다.
    [SerializeField] SushiCarrier _provider;
    // 현재 제공자가 연속으로 상호작용한 시간입니다.
    [SerializeField] float _elapsed;

    public float Elapsed => _elapsed;

    /// <summary>다른 제공자의 진행 시간을 가져가지 않고 작업할 수 있는지 확인합니다.</summary>
    public bool IsAvailableFor(SushiCarrier provider)
    {
        // 비활성화된 제공자의 잠금은 다음 상호작용 전에 정리합니다.
        if (_provider != null && !_provider.isActiveAndEnabled) Reset();
        return provider != null && provider.isActiveAndEnabled &&
            (_provider == null || _provider == provider);
    }

    /// <summary>한 제공자의 유효한 시간만 누적하고 완료 여부를 반환합니다.</summary>
    public bool Tick(SushiCarrier provider, float deltaTime, float duration)
    {
        // 유효하지 않은 시간이나 다른 제공자의 요청은 처리하지 않습니다.
        if (!IsAvailableFor(provider) || !(deltaTime > 0f) || float.IsInfinity(deltaTime) ||
            !(duration > 0f) || float.IsInfinity(duration)) return false;
        // 다른 제공자가 시작하면 기존 제공자의 진행 시간을 이어받지 않습니다.
        if (_provider != provider)
        {
            _provider = provider;
            _elapsed = 0f;
        }
        _elapsed = Mathf.Min(duration, _elapsed + deltaTime);
        return _elapsed >= duration;
    }

    /// <summary>요청한 제공자가 진행 중이던 작업만 취소합니다.</summary>
    public void Cancel(SushiCarrier provider)
    {
        // 다른 작업자의 진행 시간은 유지합니다.
        if (_provider == provider) Reset();
    }

    /// <summary>작업 완료, 대상 상태 변경 또는 제거 시 진행을 모두 정리합니다.</summary>
    public void Reset()
    {
        // 다음 제공자가 새 작업을 시작할 수 있도록 소유권과 시간을 비웁니다.
        _provider = null;
        _elapsed = 0f;
    }
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public class SushiInteractor : MonoBehaviour
{
    // 음료 제공과 청소를 수행하는 지상 플레이어입니다.
    [SerializeField] GroundHero _hero;
    // 기존 InputHandler에서 사용하는 것과 동일한 상호작용 액션입니다.
    [SerializeField] InputActionReference _interactAction;
    // 기존 플레이어의 상호작용 대상 탐색 컴포넌트입니다.
    [SerializeField] InteractableSensor _sensor;
    // 실제 영역 겹침을 검사할 플레이어의 센서 Collider입니다.
    [SerializeField] Collider _sensorCollider;
    // 현재 누르기를 유지하며 작업 중인 대상입니다.
    ISushiHoldInteraction _target;
    // 플레이어 음료 제공을 미니게임으로 분기하는 연결 담당입니다.
    [SerializeField] SushiDrinkMinigameCoordinator _drinkMinigame;
    // 종료 직후 기존 키 누름이 일반 작업을 재개하지 않도록 합니다.
    bool _waitForInteractRelease;
    // 서빙 완료 이벤트를 구독한 실제 운반 컴포넌트입니다.
    SushiCarrier _subscribedCarrier;

    // 상태 컴포넌트가 현재 진행 중인 길게 누르기 작업을 읽는 참조입니다.
    public ISushiHoldInteraction ActiveTarget => _target;
    internal event System.Action<ISushiHoldInteraction> HoldInteractionCompleted;

    /// <summary>같은 플레이어 오브젝트에서 기본 참조를 준비합니다.</summary>
    void Awake()
    {
        // 입력 액션과 센서 Collider는 인스펙터에서 명시적으로 연결합니다.
        if (_hero == null) _hero = GetComponent<GroundHero>();
        if (_sensor == null) _sensor = GetComponent<InteractableSensor>();
        // 명시적인 연결이 없으면 같은 플레이어 루트에서 찾습니다.
        if (_drinkMinigame == null) _drinkMinigame = GetComponent<SushiDrinkMinigameCoordinator>();
    }

    /// <summary>음식 서빙 완료를 구독하여 같은 입력의 중복 사용을 방지합니다.</summary>
    void OnEnable()
    {
        // 명시적인 연결이 없으면 같은 오브젝트에서 플레이어를 찾습니다.
        if (_hero == null) _hero = GetComponent<GroundHero>();
        // 구독 해제 시에도 같은 컴포넌트를 사용하도록 보관합니다.
        _subscribedCarrier = _hero != null ? _hero.SushiCarrier : null;
        // 운반 컴포넌트가 연결된 경우에만 서빙 완료를 구독합니다.
        if (_subscribedCarrier != null)
            _subscribedCarrier.FoodServed += HandleFoodServed;
    }

    /// <summary>음식 서빙에 사용한 키를 놓을 때까지 다음 작업을 차단합니다.</summary>
    void HandleFoodServed(GuestNPC guest)
    {
        // 서빙 직후 빈손이 되어도 같은 입력으로 음료를 시작하지 않게 합니다.
        _waitForInteractRelease = true;
        // 이전 연속 상호작용이 남아 있으면 함께 정리합니다.
        CancelInteraction();
    }

    /// <summary>
    /// 상호작용 키를 새로 누른 순간 대상을 선택하고,
    /// 키를 유지하는 동안 해당 작업을 진행하며 실제 작업 중에만 이동을 막습니다.
    /// </summary>
    void Update()
    {
        // 준비와 결과 표시까지 기존 Hold 경로를 쉬게 합니다.
        if (_drinkMinigame != null && _drinkMinigame.IsBusy)
        {
            CancelInteraction();
            return;
        }
        // 플레이어와 입력 액션을 사용할 수 없으면 기존 작업을 취소합니다.
        if (_hero == null || !_hero.isActiveAndEnabled ||
            _interactAction == null || _interactAction.action == null ||
            !_interactAction.action.enabled || !Application.isFocused)
        {
            CancelInteraction();
            return;
        }

        // 이번 프레임에 확인할 상호작용 액션을 가져옵니다.
        InputAction action = _interactAction.action;
        // 미니게임 종료 후 키를 놓은 다음의 새 입력부터 허용합니다.
        if (_waitForInteractRelease)
        {
            if (!action.IsPressed()) _waitForInteractRelease = false;
            return;
        }

        // 상호작용 키를 놓으면 작업을 취소하고 기존 이동 입력을 허용합니다.
        if (!action.IsPressed())
        {
            CancelInteraction();
            return;
        }

        // 작업에 필요한 참조가 유효하지 않으면 기존 작업을 취소합니다.
        if (_hero.SushiCarrier == null ||
            !_hero.SushiCarrier.isActiveAndEnabled ||
            _sensor == null || _sensorCollider == null)
        {
            CancelInteraction();
            return;
        }

        // 상호작용 키를 새로 누른 프레임에만 작업 대상을 선택합니다.
        if (action.WasPressedThisFrame())
        {
            // 빠르게 키를 다시 누른 경우에도 이전 작업 시간을 이어받지 않습니다.
            CancelInteraction();

            // 키를 누른 순간 범위 안에 있는 연속 상호작용 대상을 저장합니다.
            var selected = _sensor.FindNearestInteractable(_hero);
            // 플레이어의 음료 제공만 미니게임으로 전환합니다.
            if (selected is GuestNPC guest && guest.IsWaitingForDrink)
            {
                // 요리를 운반 중인 상태에서는 음료 미니게임을 시작하지 않습니다.
                if (_hero.SushiCarrier.CurrentSushi != null) return;
                // 시작에 성공하면 키를 놓은 뒤의 새로운 입력부터 허용합니다.
                if (_drinkMinigame != null && _drinkMinigame.TryBegin(guest))
                    _waitForInteractRelease = true;
                // 연결 실패 시에도 Hold로 음료를 제공하지 않습니다.
                return;
            }
            // 청소 등 다른 작업은 기존 Hold 경로를 유지합니다.
            _target = selected as ISushiHoldInteraction;
        }

        // 선택된 대상이 없으면 키를 유지해도 새 대상을 자동으로 찾지 않습니다.
        if (_target == null) return;

        // 인터페이스 참조에 남아 있는 파괴된 Unity 대상도 확인합니다.
        Component targetComponent = _target as Component;

        // 대상이 사라졌거나 작업할 수 없는 상태가 되면 작업을 취소합니다.
        if (targetComponent == null ||
            !_target.CanHoldInteract(_hero.SushiCarrier))
        {
            CancelInteraction();
            return;
        }

        // 처음 선택한 대상에서 실제 접촉 상태를 검사하며 작업 시간을 누적합니다.
        bool completed = _target.TickHoldInteract(
            _hero.SushiCarrier, _sensorCollider, Time.deltaTime);

        // 완료 후에는 키를 계속 누르고 있어도 다음 작업을 자동으로 시작하지 않습니다.
        if (completed)
        {
            var completedTarget = _target;
            CancelInteraction();
            HoldInteractionCompleted?.Invoke(completedTarget);
            return;
        }

        // 접촉 이탈 등으로 실제 작업이 진행되지 않으면 작업을 취소합니다.
        if (!IsWorkInProgress())
        {
            CancelInteraction();
            return;
        }

        // 실제 드링크 제공 또는 청소가 진행되는 동안에만 이동을 멈춥니다.
        _hero.SetMoveInput(Vector2.zero);
    }

    /// <summary>대상 변경, 키 해제 또는 비활성화 시 현재 작업을 취소합니다.</summary>
    void CancelInteraction()
    {
        // 파괴된 Unity 대상을 인터페이스 참조만으로 호출하지 않습니다.
        Component component = _target as Component;
        if (component != null && _hero != null)
            _target.CancelHoldInteract(_hero.SushiCarrier);
        _target = null;
    }

    /// <summary>컴포넌트 비활성화 시 서빙 이벤트 구독과 진행 중인 작업을 정리합니다.</summary>
    void OnDisable()
    {
        // 구독 당시의 운반 컴포넌트에서 이벤트를 해제합니다.
        if (_subscribedCarrier != null)
            _subscribedCarrier.FoodServed -= HandleFoodServed;
        // 다음 활성화에서 다시 연결할 수 있도록 참조를 비웁니다.
        _subscribedCarrier = null;
        // 키 해제 이벤트를 받지 못해도 대상의 잠금을 반환합니다.
        CancelInteraction();
    }

    /// <summary>창 포커스를 잃으면 연속 입력을 취소합니다.</summary>
    void OnApplicationFocus(bool focused)
    {
        // 다른 창에서 누른 시간을 작업에 누적하지 않습니다.
        if (!focused) CancelInteraction();
    }

    /// <summary>플레이어와 직원이 사용할 실제 센서 및 대상 영역의 접촉을 확인합니다.</summary>
    public static bool IsTouching(Collider sensor, Collider area)
    {
        // 별도 접촉 검사 스크립트 없이 두 작업이 같은 검사를 사용합니다.
        if (sensor == null || area == null || !sensor.enabled || !area.enabled ||
            !sensor.gameObject.activeInHierarchy || !area.gameObject.activeInHierarchy)
            return false;
        return Physics.ComputePenetration(sensor, sensor.transform.position,
            sensor.transform.rotation, area, area.transform.position,
            area.transform.rotation, out _, out _);
    }

    /// <summary>현재 대상에서 드링크 제공 또는 청소가 실제로 진행 중인지 확인합니다.</summary>
    bool IsWorkInProgress()
    {
        // 청소 대상이면 미완료 상태이고 실제 청소 시간이 누적되었는지 확인합니다.
        if (_target is Chair chair && chair != null)
        {
            // 청소 시작 전이나 완료된 상태는 이동 제한 대상에서 제외합니다.
            return chair.isActiveAndEnabled &&
                   chair.State == ChairState.Uncleaned &&
                   chair.CleaningElapsed > 0f &&
                   chair.CleaningElapsed < chair.CleaningTime;
        }

        // 손님이면 현재 음료를 기다리고 실제 제공 시간이 누적되었는지 확인합니다.
        if (_target is GuestNPC guest && guest != null)
        {
            // 음식 주문이나 제공 시작 전, 완료된 상태는 제외합니다.
            return guest.isActiveAndEnabled &&
                   guest.IsWaitingForDrink &&
                   guest.SelectedSushi != null &&
                   guest.SelectedSushi.Type == MenuType.Drink &&
                   guest.DrinkElapsed > 0f &&
                   guest.DrinkElapsed < guest.SelectedSushi.DrinkInteractionTime;
        }

        // 드링크 제공과 청소 외에는 이동을 제한하지 않습니다.
        return false;
    }
}
