using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어의 음식 폐기 입력과 진행 시간을 관리합니다.
/// 실제 음식 제거는 SushiCarrier에 위임합니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(GroundHero), typeof(SushiCarrier))]
public class SushiFoodDiscarder : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] GroundHero _hero;
    [SerializeField] SushiCarrier _carrier;

    [Header("폐기 입력")]
    [SerializeField] InputActionReference _trashFoodAction;

    [Header("폐기 설정")]
    [SerializeField, Min(0.1f)] float _discardDuration = 2f;

    // 현재 폐기를 시작한 음식입니다.
    Sushi _discardTarget;

    // 이번 폐기에 누적된 시간입니다.
    float _discardElapsed;

    // 현재 G키를 유지하며 폐기를 진행하는지 나타냅니다.
    bool _isDiscarding;

    // 이 컴포넌트가 활성화한 액션만 비활성화하기 위한 기록입니다.
    bool _enabledAction;

    // 활성화 시 사용한 액션을 보관합니다.
    InputAction _action;

    /// <summary>현재 음식을 폐기하는 중인지 반환합니다.</summary>
    public bool IsDiscarding => _isDiscarding;

    /// <summary>현재 폐기에 누적된 시간을 초 단위로 반환합니다.</summary>
    public float DiscardElapsed => _discardElapsed;

    /// <summary>폐기에 필요한 시간을 초 단위로 반환합니다.</summary>
    public float DiscardDuration => Mathf.Max(0.1f, _discardDuration);

    /// <summary>폐기 진행도를 0~1 범위로 반환합니다.</summary>
    public float DiscardProgress =>
        Mathf.Clamp01(_discardElapsed / DiscardDuration);

    /// <summary>같은 오브젝트의 플레이어와 운반 컴포넌트를 준비합니다.</summary>
    void Awake()
    {
        // 인스펙터에서 연결하지 않았다면 같은 오브젝트에서 찾습니다.
        if (_hero == null) _hero = GetComponent<GroundHero>();
        if (_carrier == null) _carrier = GetComponent<SushiCarrier>();
    }

    /// <summary>폐기 상태를 초기화하고 사용할 입력 액션을 활성화합니다.</summary>
    void OnEnable()
    {
        // 이전 활성화에서 사용한 진행 시간을 제거합니다.
        CancelDiscard();

        // 연결된 TrashFood 액션을 보관합니다.
        _action = _trashFoodAction != null
            ? _trashFoodAction.action
            : null;

        // 액션이 연결되지 않았다면 입력을 처리하지 않습니다.
        if (_action == null) return;

        // 이미 활성화된 공용 액션의 소유권은 가져오지 않습니다.
        _enabledAction = !_action.enabled;

        // 아직 비활성화된 액션만 직접 활성화합니다.
        if (_enabledAction) _action.Enable();
    }

    /// <summary>G키 유지 시간을 누적하고 완료되면 음식을 폐기합니다.</summary>
    void Update()
    {
        // 입력이나 플레이어를 사용할 수 없으면 진행을 취소합니다.
        if (_action == null || !_action.enabled ||
            !Application.isFocused || Time.timeScale <= 0f ||
            _hero == null || !_hero.isActiveAndEnabled ||
            _carrier == null || !_carrier.isActiveAndEnabled)
        {
            CancelDiscard();
            return;
        }

        // 빈손이거나 키를 놓았다면 누적 시간을 초기화합니다.
        if (_carrier.CurrentSushi == null || !_action.IsPressed())
        {
            CancelDiscard();
            return;
        }

        // 폐기를 진행하지 않는 경우 새로 누른 입력으로만 시작합니다.
        if (!_isDiscarding)
        {
            // 계속 누른 상태에서 새 음식을 받아도 자동 시작하지 않습니다.
            if (!_action.WasPressedThisFrame()) return;

            // 현재 음식과 진행 상태를 저장합니다.
            _discardTarget = _carrier.CurrentSushi;
            _discardElapsed = 0f;
            _isDiscarding = true;
        }

        // 진행 중 음식이 바뀌었다면 이전 폐기 시간을 인계하지 않습니다.
        if (_carrier.CurrentSushi != _discardTarget)
        {
            CancelDiscard();
            return;
        }

        // 게임 시간 기준으로 누적하며 완료 시간을 넘지 않게 제한합니다.
        _discardElapsed = Mathf.Min(
            _discardElapsed + Time.deltaTime,
            DiscardDuration);

        // 필요한 시간에 도달하기 전까지는 음식을 유지합니다.
        if (_discardElapsed < DiscardDuration) return;

        // 실제 음식 제거는 기존 운반 컴포넌트에 요청합니다.
        _carrier.TryDiscard();

        // 완료 후에는 다음 입력을 기다리도록 진행 상태를 초기화합니다.
        CancelDiscard();
    }

    /// <summary>음식은 유지하고 현재 폐기 진행만 취소합니다.</summary>
    public void CancelDiscard()
    {
        // 현재 폐기 대상과 누적 시간을 모두 초기화합니다.
        _discardTarget = null;
        _discardElapsed = 0f;
        _isDiscarding = false;
    }

    /// <summary>창 포커스를 잃으면 진행 중인 폐기를 취소합니다.</summary>
    void OnApplicationFocus(bool focused)
    {
        // 다른 창으로 전환하기 전의 진행 시간을 이어가지 않습니다.
        if (!focused) CancelDiscard();
    }

    /// <summary>컴포넌트가 꺼지면 진행 상태와 직접 활성화한 입력을 정리합니다.</summary>
    void OnDisable()
    {
        // 비활성화 전에 진행 중이던 폐기를 취소합니다.
        CancelDiscard();

        // 직접 활성화했던 액션만 비활성화합니다.
        if (_enabledAction && _action != null)
            _action.Disable();

        // 다음 활성화에서 입력 상태를 다시 확인하도록 정리합니다.
        _enabledAction = false;
        _action = null;
    }
}