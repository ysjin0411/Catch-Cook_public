using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SushiCarrier))]
public class SushiCarryPresentation : MonoBehaviour
{
    // 플레이어와 직원이 공통으로 사용하는 음식 운반 정보입니다.
    [SerializeField] SushiCarrier _carrier;
    // 손 소켓에 미리 배치한 음식 표시 그룹입니다.
    [SerializeField] SushiVisualGroup _handVisual;
    // 운반 자세를 덮어쓸 모델의 Animator입니다.
    [SerializeField] Animator _animator;
    // 운반 클립과 팔 전용 마스크를 설정한 별도 레이어 이름입니다.
    [SerializeField] string _carryLayerName = "Upper";
    // 운반 레이어의 가중치가 0과 1 사이를 전환하는 데 걸리는 시간입니다.
    [SerializeField, Min(0f)] float _blendDuration = 0.15f;
    // 마지막으로 손에 표시한 메뉴입니다.
    Sushi _displayedMenu;
    // 현재 Animator Controller의 운반 레이어 번호입니다.
    int _carryLayerIndex = -1;
    // Controller가 교체됐을 때 레이어 번호를 다시 찾기 위한 참조입니다.
    RuntimeAnimatorController _boundController;

    /// <summary>운반 정보와 시각 모델의 기본 참조를 준비합니다.</summary>
    void Awake()
    {
        // 플레이어와 서빙 직원의 서로 다른 루트에서 같은 코드를 사용합니다.
        if (_carrier == null) _carrier = GetComponent<SushiCarrier>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
    }

    /// <summary>활성화 시 현재 음식과 운반 자세를 초기화합니다.</summary>
    void OnEnable()
    {
        // 오브젝트의 초기 활성 상태를 현재 운반 데이터에 맞춥니다.
        RefreshHandVisual();
        RefreshLayerBinding();
        if (_carryLayerIndex > 0) _animator.SetLayerWeight(_carryLayerIndex, 0f);
    }

    /// <summary>음식이 바뀔 때 표시를 갱신하고 운반 자세를 부드럽게 전환합니다.</summary>
    void LateUpdate()
    {
        // 실제 수령 전에는 팔을 들지 않으며 음식 보유 여부로만 판단합니다.
        Sushi menu = GetCarriedMenu();
        if (menu != _displayedMenu) RefreshHandVisual();
        RefreshLayerBinding();
        if (_carryLayerIndex <= 0) return;
        float target = menu == null ? 0f : 1f;
        float current = _animator.GetLayerWeight(_carryLayerIndex);
        float weight = _blendDuration > 0f
            ? Mathf.MoveTowards(current, target, Time.deltaTime / _blendDuration)
            : target;
        _animator.SetLayerWeight(_carryLayerIndex, weight);
    }

    /// <summary>현재 활성 운반 컴포넌트에서 실제 보유 음식을 가져옵니다.</summary>
    Sushi GetCarriedMenu()
    {
        // 표시용 상태를 원본 음식 데이터와 별도로 저장하지 않습니다.
        if (_carrier == null || !_carrier.isActiveAndEnabled) return null;
        return _carrier.CurrentSushi;
    }

    /// <summary>현재 음식에 맞춰 손의 내용물만 갱신합니다.</summary>
    void RefreshHandVisual()
    {
        // 수령, 전달, 버리기가 모두 같은 표시 갱신 경로를 사용합니다.
        _displayedMenu = GetCarriedMenu();
        if (_handVisual != null) _handVisual.Show(_displayedMenu, SushiVisualPhase.Carried);
    }

    /// <summary>Controller 변경 시 운반 레이어를 다시 찾고 Base Layer는 보호합니다.</summary>
    void RefreshLayerBinding()
    {
        // Animator가 없어도 음식 오브젝트 표시는 사용할 수 있습니다.
        RuntimeAnimatorController controller = _animator == null ? null : _animator.runtimeAnimatorController;
        if (controller == _boundController) return;
        _boundController = controller;
        _carryLayerIndex = -1;
        if (controller == null || string.IsNullOrEmpty(_carryLayerName)) return;
        // 0번 기본 이동 레이어의 가중치는 변경하지 않습니다.
        int index = _animator.GetLayerIndex(_carryLayerName);
        if (index > 0) _carryLayerIndex = index;
    }

    /// <summary>비활성화 시 음식 표시와 운반 레이어를 정리합니다.</summary>
    void OnDisable()
    {
        // 보유 음식 데이터는 그대로 두고 화면 표시만 정리합니다.
        _displayedMenu = null;
        if (_handVisual != null) _handVisual.Show(null, SushiVisualPhase.Empty);
        if (_animator != null && _animator.runtimeAnimatorController == _boundController &&
            _carryLayerIndex > 0) _animator.SetLayerWeight(_carryLayerIndex, 0f);
    }
}
