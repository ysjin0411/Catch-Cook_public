using UnityEngine;

/// <summary>다음에 수령할 수 있는 완성 메뉴 하나를 요리사 옆 테이블에 표시합니다.</summary>
[DisallowMultipleComponent]
public sealed class ServingTablePresentation : MonoBehaviour
{
    // 완성 음식의 수령 순서를 조회할 요리사입니다.
    [SerializeField] CookerNPC _cooker;
    // 가장 먼저 수령할 음식의 프리팹을 표시할 그룹입니다.
    [SerializeField] SushiVisualGroup _foodVisual;

    /// <summary>활성화 시 현재 수령 가능한 메뉴를 표시에 반영합니다.</summary>
    void OnEnable()
    {
        // 첫 프레임에도 실제 완료 주문과 음식 표시를 일치시킵니다.
        RefreshFoodVisual();
    }

    /// <summary>조리 완료, 수령, 주문 취소 결과에 맞춰 음식 표시를 갱신합니다.</summary>
    void LateUpdate()
    {
        // 표시 그룹 내부에서 같은 프리팹을 유지하므로 중복 생성하지 않습니다.
        RefreshFoodVisual();
    }

    /// <summary>실제 수령과 동일한 기준으로 다음 메뉴를 조회하여 표시합니다.</summary>
    void RefreshFoodVisual()
    {
        // 표시 그룹이 연결되지 않았다면 조회와 표시를 생략합니다.
        if (_foodVisual == null) return;

        // 조회에 실패하면 빈 테이블로 표시할 기본 메뉴를 준비합니다.
        Sushi menu = null;

        // 목록을 소비하지 않고 실제 수령 가능한 가장 앞의 메뉴만 조회합니다.
        if (_cooker != null && _cooker.isActiveAndEnabled)
            _cooker.TryPeekCompletedFood(out menu);

        // 수령 가능한 음식이 없으면 생성된 내용물만 제거합니다.
        SushiVisualPhase phase = menu != null ? SushiVisualPhase.Served : SushiVisualPhase.Empty;
        _foodVisual.Show(menu, phase);
    }

    /// <summary>완성 테이블 표시 기능이 비활성화되면 음식 표시를 비웁니다.</summary>
    void OnDisable()
    {
        // 완료 주문 데이터와 테이블 자체는 유지합니다.
        if (_foodVisual != null) _foodVisual.Show(null, SushiVisualPhase.Empty);
    }
}