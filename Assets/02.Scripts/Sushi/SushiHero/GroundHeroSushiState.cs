using UnityEngine;

public enum SushiActionState
{
    Idle,          // 이동 여부와 관계없이 진행 중인 스시 작업이 없습니다.
    CarryingFood,  // 완성 음식 한 개를 운반합니다.
    ServingDrink,  // 손님에게 음료를 제공하는 시간을 누적합니다.
    Cleaning       // 좌석을 청소하는 시간을 누적합니다.
}

[DisallowMultipleComponent]
public class GroundHeroSushiState : MonoBehaviour
{
    // 현재 보유 음식의 원본 데이터를 관리합니다.
    [SerializeField] SushiCarrier _carrier;
    // 실제 길게 누르기 작업을 관리합니다.
    [SerializeField] SushiInteractor _interactor;
    // 인스펙터에서 확인할 현재 스시 작업 상태입니다.
    [SerializeField] SushiActionState _currentState;

    public SushiActionState CurrentState => _currentState;

    /// <summary>같은 루트에 있는 운반 및 상호작용 참조를 준비합니다.</summary>
    void Awake()
    {
        // 기존 컴포넌트가 관리하는 실제 작업 정보를 읽습니다.
        if (_carrier == null) _carrier = GetComponent<SushiCarrier>();
        if (_interactor == null) _interactor = GetComponent<SushiInteractor>();
    }

    /// <summary>활성화 시 현재 보유 음식과 작업 상태를 반영합니다.</summary>
    void OnEnable()
    {
        // 이전 비활성화 전의 상태를 그대로 표시하지 않습니다.
        RefreshState();
    }

    /// <summary>프레임의 운반 및 상호작용 처리가 끝난 뒤 상태를 갱신합니다.</summary>
    void LateUpdate()
    {
        // 이동은 기존 GroundHero가 처리하고 스시 작업만 분류합니다.
        RefreshState();
    }

    /// <summary>원본 운반 데이터와 실제 작업 진행 시간으로 상태를 판단합니다.</summary>
    void RefreshState()
    {
        // 실제 음식이 있는 동안은 정지 중에도 운반 상태입니다.
        if (_carrier != null && _carrier.isActiveAndEnabled && _carrier.CurrentSushi != null)
        {
            _currentState = SushiActionState.CarryingFood;
            return;
        }
        // 접촉 중이기만 하거나 키를 누르지 않은 상태는 작업으로 보지 않습니다.
        _currentState = SushiActionState.Idle;
        if (_interactor == null || !_interactor.isActiveAndEnabled) return;
        ISushiHoldInteraction target = _interactor.ActiveTarget;

        // 파괴된 손님 참조와 실제로 진행되지 않은 음료 작업은 제외합니다.
        if (target is GuestNPC guest && guest != null &&
            guest.IsWaitingForDrink && guest.DrinkElapsed > 0f)
            _currentState = SushiActionState.ServingDrink;
        // 완료되거나 취소된 청소는 진행 시간이 0이므로 대기로 돌아갑니다.
        else if (target is Chair chair && chair != null &&
                 chair.State == ChairState.Uncleaned && chair.CleaningElapsed > 0f)
            _currentState = SushiActionState.Cleaning;
    }

    /// <summary>플레이어가 비활성화되면 표시용 작업 상태를 초기화합니다.</summary>
    void OnDisable()
    {
        // 운반 음식 자체는 SushiCarrier가 관리하므로 여기서는 변경하지 않습니다.
        _currentState = SushiActionState.Idle;
    }
}
