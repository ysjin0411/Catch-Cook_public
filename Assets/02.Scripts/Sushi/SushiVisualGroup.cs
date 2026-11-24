using UnityEngine;

public enum SushiVisualPhase
{
    Empty,      // 음식과 식사 후 표시를 모두 숨깁니다.
    Carried,    // 손에서 운반하는 메뉴의 한 접시를 표시합니다.
    Served,     // 완성 테이블 또는 손님 앞에 메뉴의 한 접시를 표시합니다.
    Remaining   // 해당 메뉴에 연결한 잔반 또는 빈 잔을 표시합니다.
}

[DisallowMultipleComponent]
public class SushiVisualGroup : MonoBehaviour
{
    [Header("음식과 잔반 생성 위치")]
    // 항상 활성 상태로 유지할 자식 표시 지점입니다. 접시 구성은 메뉴 프리팹이 담당합니다.
    [SerializeField] Transform _spawnPoint;

    // 현재 생성한 접시 또는 잔반의 원본 프리팹입니다.
    GameObject _displayedPrefab;
    // 이 표시 그룹이 소유한 표시 인스턴스입니다.
    GameObject _instance;

    /// <summary>운반·제공 단계에는 한 접시를, 식사 후에는 메뉴의 RemainingPrefab을 표시합니다.</summary>
    public void Show(Sushi menu, SushiVisualPhase phase)
    {
        // 빈 상태와 메뉴가 없는 상태에서는 표시할 프리팹을 선택하지 않습니다.
        GameObject prefab = null;

        // 음식과 음료 모두 같은 상태 전환을 사용하며 외형은 메뉴 데이터에서 가져옵니다.
        if (menu != null)
        {
            // 식사 완료 시 음식은 잔반으로, 음료는 각 메뉴에 연결한 빈 잔으로 바꿉니다.
            if (phase == SushiVisualPhase.Remaining) prefab = menu.RemainingPrefab;
            // 조리 완료·운반·서빙에서는 동일한 한 접시 또는 음료 프리팹을 사용합니다.
            else if (phase == SushiVisualPhase.Carried || phase == SushiVisualPhase.Served)
                prefab = menu.VisualPrefab;
        }

        // 같은 외형을 이미 표시하고 있으면 매 프레임 다시 생성하지 않습니다.
        if (prefab != null && prefab == _displayedPrefab && _instance != null) return;

        // 이전 외형은 즉시 숨겨 음식과 잔반이 한 프레임 동안 겹치지 않게 합니다.
        ClearFood();

        // 청소 완료·빈손 상태이거나 프리팹이 없는 메뉴는 빈 표시를 유지합니다.
        if (prefab == null) return;

        // 지정한 표시 지점을 사용하고, 미지정 시 이 그룹의 Transform을 기준으로 합니다.
        Transform parent = _spawnPoint != null ? _spawnPoint : transform;

        // 표시 지점을 기준으로 접시 전체를 생성하며 메뉴 프리팹의 크기는 유지합니다.
        _instance = Instantiate(prefab, parent, false);
        // 프리팹에 저장된 위치 대신 조리대·손·좌석의 표시 기준점을 사용합니다.
        _instance.transform.localPosition = Vector3.zero;
        // 표시 기준점의 방향을 따라가도록 프리팹의 로컬 회전을 초기화합니다.
        _instance.transform.localRotation = Quaternion.identity;

        // 다음 갱신에서 같은 프리팹을 중복 생성하지 않도록 원본을 보관합니다.
        _displayedPrefab = prefab;
    }

    /// <summary>이 그룹이 생성한 접시 또는 잔반만 제거하고 표시 지점은 유지합니다.</summary>
    void ClearFood()
    {
        // Destroy가 프레임 끝에 처리되어도 이전 외형은 즉시 보이지 않게 합니다.
        if (_instance != null)
        {
            // 그룹과 무관한 테이블·손·다른 좌석 오브젝트는 변경하지 않습니다.
            _instance.SetActive(false);
            // 이 그룹이 생성한 표시 인스턴스만 정리합니다.
            Destroy(_instance);
        }

        // 다음 표시 요청이 새 외형을 생성할 수 있도록 인스턴스 캐시를 초기화합니다.
        _instance = null;
        // 제거한 인스턴스의 원본 프리팹 참조도 함께 비웁니다.
        _displayedPrefab = null;
    }

    /// <summary>표시 그룹이 비활성화되면 생성한 접시와 잔반을 정리합니다.</summary>
    void OnDisable()
    {
        // 주문·재고·좌석 상태는 유지하고 이 그룹이 소유한 표시만 정리합니다.
        ClearFood();
    }
}
