using UnityEngine;

/// <summary>직원의 실제 음료 제공 상태를 읽어 방향, 애니메이션과 병 소품을 표시합니다.</summary>
[DisallowMultipleComponent]
public sealed class ServingDrinkPresentation : MonoBehaviour
{
    // 작업 판단은 직원에게 맡기고 연출에 필요한 참조만 보관합니다.
    [SerializeField] ServingNPC _staff;
    [SerializeField] Transform _visual;
    [SerializeField] Animator _animator;
    [SerializeField] GameObject _drinkProp;
    // 디버그 검증 때만 시작과 종료를 출력합니다.
    [SerializeField] bool _logTransitions;
    int _workLayer = -1;
    int _drinkHash;
    GuestNPC _shownGuest;
    bool _presenting;
    bool _ready;

    /// <summary>필수 참조와 별도 작업 레이어를 검증하고 소품을 숨깁니다.</summary>
    void Awake()
    {
        // 같은 루트의 직원을 기본 참조로 사용합니다.
        if (_staff == null) _staff = GetComponent<ServingNPC>();
        // 초기 병 소품은 숨깁니다.
        if (_drinkProp != null) _drinkProp.SetActive(false);
        // 이동과 서빙 로직에 영향을 주지 않고 잘못된 연출만 중지합니다.
        if (_staff == null || _visual == null || _animator == null)
        {
            // 누락된 참조를 알리고 비활성화합니다.
            Debug.LogError("직원 음료 연출의 Staff, Visual, Animator를 연결하세요.", this);
            enabled = false;
            return;
        }
        // 기본 이동 레이어와 분리된 작업 상태를 찾습니다.
        _workLayer = _animator.GetLayerIndex("SushiWork");
        _drinkHash = Animator.StringToHash("SushiWork.ServingDrink");
        // 필요한 상태가 없으면 기본 레이어를 건드리지 않습니다.
        if (_workLayer <= 0 || !_animator.HasState(_workLayer, _drinkHash))
        {
            // Animator 설정 오류를 표시합니다.
            Debug.LogError("직원 Animator에 SushiWork.ServingDrink 상태가 필요합니다.", this);
            enabled = false;
            return;
        }
        // 대기와 이동 중에는 작업 레이어를 숨깁니다.
        _ready = true;
        _animator.SetLayerWeight(_workLayer, 0f);
    }

    /// <summary>중앙에서 제공 중일 때만 손님을 바라보고 반복 애니메이션을 표시합니다.</summary>
    void LateUpdate()
    {
        // 초기화에 실패했다면 연출하지 않습니다.
        if (!_ready) return;
        // 접근 중인 목표와 실제 제공 중인 목표를 구분합니다.
        GuestNPC guest = _staff != null && _staff.isActiveAndEnabled ? _staff.ActiveDrinkGuest : null;
        // 완료·취소·퇴장 시 같은 프레임에 작업 레이어를 숨깁니다.
        if (guest == null || !guest.isActiveAndEnabled || !guest.IsWaitingForDrink ||
            !_animator.isActiveAndEnabled)
        {
            // 실제 타이머에는 관여하지 않고 표시만 정리합니다.
            ClearPresentation();
            return;
        }
        // 작업 시작 시에만 재생하여 매 프레임 클립을 되감지 않습니다.
        if (!_presenting || _shownGuest != guest)
        {
            // 작업 대상과 표시 상태를 기록합니다.
            _shownGuest = guest;
            _presenting = true;
            // 기존 클립을 정상 속도로 반복하고 병 소품을 켭니다.
            _animator.Play(_drinkHash, _workLayer, 0f);
            _animator.SetLayerWeight(_workLayer, 1f);
            if (_drinkProp != null) _drinkProp.SetActive(true);
            // 테스트 시 실제 작업 시작 위치를 확인합니다.
            if (_logTransitions) Debug.Log($"[직원 음료 연출] 시작: {guest.name}, 위치={_staff.transform.position}", this);
        }
        // 이동 코드가 정한 기본 방향 뒤에 손님 방향을 적용합니다.
        Transform target = guest.Seat != null && guest.Seat.SeatPos != null ? guest.Seat.SeatPos : guest.transform;
        Vector3 direction = target.position - _visual.position;
        direction.y = 0f;
        // 센서와 루트 대신 시각 모델만 수평 회전합니다.
        if (direction.sqrMagnitude > 0.0001f) _visual.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    /// <summary>작업 레이어와 병 소품을 숨기고 이전 대상을 해제합니다.</summary>
    void ClearPresentation()
    {
        // 종료 전이를 테스트 로그에 한 번만 기록합니다.
        if (_presenting && _logTransitions) Debug.Log("[직원 음료 연출] 종료", this);
        // 다음 작업이 처음부터 재생되도록 상태를 초기화합니다.
        _presenting = false;
        _shownGuest = null;
        // 기본 이동·운반 레이어가 다시 표시되게 합니다.
        if (_ready && _animator != null) _animator.SetLayerWeight(_workLayer, 0f);
        // 뼈 전체가 아닌 연결한 병 소품만 숨깁니다.
        if (_drinkProp != null && _drinkProp.activeSelf) _drinkProp.SetActive(false);
    }

    /// <summary>비활성화 후 작업 연출이 남지 않도록 정리합니다.</summary>
    void OnDisable()
    {
        // 완료와 취소에서 사용하는 동일한 정리 경로를 호출합니다.
        ClearPresentation();
    }
}
