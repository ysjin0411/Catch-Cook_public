using UnityEngine;

/// <summary>접촉 중인 음식 제공, 음료 제공, 청소 후보 중 가장 가까운 좌석을 선택합니다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(GroundHero))]
public class SushiInteractionSelector : MonoBehaviour
{
    [Header("플레이어 참조")]
    [SerializeField] GroundHero _hero;
    [Header("기존 InteractableSensor와 동일한 감지 설정")]
    [SerializeField] Collider _sensorCollider;
    [SerializeField] LayerMask _interactionLayers;

    // 반복 조회 시 배열을 재사용하고 후보가 많아질 때만 확장합니다.
    Collider[] _hits = new Collider[32];

    /// <summary>같은 오브젝트의 플레이어 참조를 준비합니다.</summary>
    void Awake()
    {
        // 명시적으로 연결한 참조가 있으면 유지합니다.
        if (_hero == null) _hero = GetComponent<GroundHero>();
    }

    /// <summary>요청 대상이 현재 접촉 중인 유효한 스시 작업 후보 중 가장 가까운지 검사합니다.</summary>
    public bool IsNearestTarget(Component requestedTarget)
    {
        // 비활성 플레이어와 연결되지 않은 센서는 상호작용하지 않습니다.
        if (!isActiveAndEnabled || requestedTarget == null || _hero == null ||
            !_hero.isActiveAndEnabled || _sensorCollider == null ||
            !_sensorCollider.enabled || !_sensorCollider.gameObject.activeInHierarchy)
            return false;

        // 음식 수령과 빈손 작업 모두 같은 운반 컴포넌트를 사용합니다.
        SushiCarrier carrier = _hero.SushiCarrier;
        if (carrier == null || !carrier.isActiveAndEnabled) return false;

        // 센서를 감싸는 박스로 후보를 수집하고 실제 접촉은 아래에서 재검사합니다.
        Bounds bounds = _sensorCollider.bounds;
        int count = CollectContacts(bounds);
        Component nearest = null;
        float nearestDistance = float.PositiveInfinity;
        int nearestId = int.MaxValue;

        // 호출마다 상태를 다시 확인하여 같은 프레임에 서빙이 끝난 대상도 즉시 제외합니다.
        for (int i = 0; i < count; i++)
        {
            // 일반 충돌체와 플레이어 자신의 콜라이더는 후보에서 제외합니다.
            Collider hit = _hits[i];
            if (hit == null || !hit.isTrigger || hit.transform.IsChildOf(_hero.transform))
                continue;

            // 박스의 외곽만 겹친 후보를 제외하고 기존 작업과 같은 실제 접촉 검사를 합니다.
            if (!SushiInteractor.IsTouching(_sensorCollider, hit)) continue;

            // 공용 센서와 동일하게 콜라이더 자신 또는 부모의 상호작용 대상을 찾습니다.
            Component target = hit.GetComponentInParent(typeof(IInteractable));
            if (!TryGetInteractionPosition(target, carrier, out Vector3 position)) continue;

            // 영역 표면 대신 좌석 기준점을 비교하므로 영역 안에서도 거리를 구분합니다.
            float distance = (position - bounds.center).sqrMagnitude;
            int targetId = target.GetInstanceID();

            // 정확히 같은 거리에서는 식별자로 순서를 고정해 물리 조회 순서에 의존하지 않습니다.
            if (distance > nearestDistance ||
                (distance == nearestDistance && targetId >= nearestId)) continue;

            // 가장 가까운 후보와 동률 비교 정보를 함께 갱신합니다.
            nearest = target;
            nearestDistance = distance;
            nearestId = targetId;
        }

        // 공용 센서에는 선택된 스시 작업 대상 하나만 상호작용 가능하다고 알립니다.
        return nearest == requestedTarget;
    }

    /// <summary>센서 주변 후보를 재사용 배열에 수집하며 배열이 가득 차면 누락 없이 다시 조회합니다.</summary>
    int CollectContacts(Bounds bounds)
    {
        // 후보가 배열에 모두 들어갈 때까지 확장하므로 혼잡한 좌석에서도 가까운 대상을 빠뜨리지 않습니다.
        while (true)
        {
            // 감지 레이어는 공용 센서와 같게 사용하며 트리거도 수집합니다.
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, _hits,
                Quaternion.identity, _interactionLayers, QueryTriggerInteraction.Collide);
            if (count < _hits.Length) return count;

            // 매번 새 배열을 만들지 않고 기존 용량이 부족한 경우에만 확장합니다.
            System.Array.Resize(ref _hits, _hits.Length * 2);
        }
    }

    /// <summary>손님과 의자의 기본 작업 조건을 검사하고 해당 좌석의 거리 기준점을 반환합니다.</summary>
    bool TryGetInteractionPosition(Component target, SushiCarrier carrier, out Vector3 position)
    {
        // 유효한 대상이 없는 경우에도 출력값을 초기화합니다.
        position = default;
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        if (target is Behaviour behaviour && !behaviour.isActiveAndEnabled) return false;

        // CanInteract를 다시 호출하지 않아 선택 검사가 재귀적으로 반복되는 것을 방지합니다.
        if (target is GuestNPC guest)
        {
            // 메뉴 일치, 대기 상태 및 음료 제공 조건은 기존 손님에게 맡깁니다.
            if (!guest.CanReceiveFrom(carrier) || guest.Seat == null ||
                guest.Seat.ServingPoint == null) return false;
            position = guest.Seat.ServingPoint.position;
            return true;
        }

        // 청소도 손님과 같은 좌석 기준점을 사용해 작업 종류 간 거리 비교를 일치시킵니다.
        if (target is Chair chair)
        {
            // 빈손 여부, 잔반 상태와 작업 소유권은 기존 의자에게 맡깁니다.
            if (!chair.CanHoldInteract(carrier) || chair.ServingPoint == null) return false;
            position = chair.ServingPoint.position;
            return true;
        }

        // 요리 수령 등 다른 상호작용 대상의 선택 규칙은 변경하지 않습니다.
        return false;
    }
}
