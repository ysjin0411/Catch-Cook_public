using System.Collections.Generic;
using UnityEngine;

public partial class SushiManager
{
    /// <summary>ListManager가 보관한 메뉴 예약을 확정하고 영업을 시작합니다.</summary>
    public bool ConfirmReservedTodayMenu()
    {
        // 화면의 별도 목록 대신 예약 원본의 복사본으로 기존 확정 절차를 실행합니다.
        return _sushiListManager != null && ConfirmTodayMenu(_sushiListManager.GetReservedMenuSelections());
    }

    /// <summary>메뉴 예약을 제외하고 강화에 사용할 수 있는 재료 수량을 반환합니다.</summary>
    public int GetAvailableIngredientCount(ItemData item)
    {
        // 목록 관리자가 없으면 예약을 검증할 수 없으므로 사용을 허용하지 않습니다.
        return _sushiListManager != null ? _sushiListManager.GetAvailableIngredientCount(item) : 0;
    }

    /// <summary>요리 강화 비용 처리 중 메뉴 예약과 확정을 잠급니다.</summary>
    internal bool TryBeginIngredientSpend()
    {
        // 모든 요리 강화가 같은 목록 관리자의 잠금을 사용합니다.
        return _sushiListManager != null && _sushiListManager.TryBeginIngredientSpend();
    }

    /// <summary>요리 강화 비용 처리의 잠금을 해제합니다.</summary>
    internal void EndIngredientSpend()
    {
        // 씬 종료 중 목록 관리자가 사라진 경우에는 접근하지 않습니다.
        if (_sushiListManager != null) _sushiListManager.EndIngredientSpend();
    }

    /// <summary>공통 매니저에 강화 진행 정보와 요리사의 결과 전달 대상을 연결합니다.</summary>
    void PrepareShopServices()
    {
        // 다른 팀 GameManager 코드를 수정하지 않고 컴포넌트로 확장합니다.
        GameObject owner = GameManager.Instance != null ? GameManager.Instance.gameObject : gameObject;
        SushiShopProgress progress = owner.GetComponent<SushiShopProgress>();
        if (progress == null) progress = owner.AddComponent<SushiShopProgress>();
        if (_enforceManager != null) _enforceManager.BindProgress(progress);
        // 재료는 메뉴 확정 시 차감하므로 요리사는 결과 전달 대상만 연결합니다.
        if (_cookerNPC != null) _cookerNPC.BindManager(this);
    }

    /// <summary>판매할 종류와 수량을 검증하고 영업을 시작합니다.</summary>
    public bool ConfirmTodayMenu(IReadOnlyList<SushiListManager.MenuSelection> selectedMenu)
    {
        // 확정된 영업의 수량은 중간에 변경하지 않습니다.
        if (!Application.isPlaying || _menuConfirmed || !_staffReady || _cookerNPC == null ||
            !_cookerNPC.isActiveAndEnabled || _sushiListManager == null || !(_operationTime > 0f) ||
            float.IsInfinity(_operationTime) || !_sushiListManager.SetTodayMenu(selectedMenu)) return false;
        // 직원 재설정 때가 아니라 새 영업 확정 때에만 실적을 초기화합니다.
        ResetBusinessStatistics();
        // 플레이어 레벨을 1~10으로 제한하고, 정수 나눗셈으로 두 레벨마다 비용 단계를 1~5로 구합니다.
        int costTier = (Mathf.Clamp(CurrentPlayerLevel, 1, 10) + 1) / 2;
        // 기존 운영비를 3으로 나눈 뒤 소수점 둘째 자리까지만 남겨 초당 운영비로 설정합니다.
        _operationCostPerSecond = Mathf.Floor(costTier * 100f / 3f) / 100f;
        _remainingOperationTime = _operationTime;
        _menuConfirmed = true;
        return true;
    }

    /// <summary>손님을 생성하는 시점에 판매 가능한 메뉴 한 개를 확보합니다.</summary>
    public bool TryAllocateGuest(GuestNPC guest, out Sushi menu)
    {
        // 영업 종료 후에는 신규 배정을 받지 않습니다.
        menu = null;
        return IsOpen && _sushiListManager != null && _sushiListManager.TryAllocateGuest(guest, out menu);
    }

    /// <summary>입장하면서 확보한 메뉴를 주문 시점에 조회합니다.</summary>
    public bool TryGetGuestMenu(GuestNPC guest, out Sushi menu)
    {
        // 주문 전에 매진되어도 자신의 확보 수량은 그대로 유지됩니다.
        menu = null;
        return _sushiListManager != null && _sushiListManager.TryGetGuestMenu(guest, out menu);
    }

    /// <summary>강화 요청에 사용한 요리가 실제 맵에 등록된 원본인지 확인합니다.</summary>
    public bool IsRegisteredMenu(Sushi sushi)
    {
        // UI가 임의로 생성한 동일 ID 데이터의 강화를 막습니다.
        return _sushiListManager != null && _sushiListManager.IsRegisteredMenu(sushi);
    }

    /// <summary>
    /// 기본 제공 Map에 등록된 메뉴인지 확인합니다.
    /// </summary>
    public bool IsBasicProvidedMenu(Sushi sushi)
    {
        // 메뉴 목록 관리자를 통해 기본 제공 여부를 조회합니다.
        return _sushiListManager != null &&
            _sushiListManager.IsBasicProvidedMenu(sushi);
    }

    /// <summary>
    /// 오늘의 메뉴로 선택할 수 있는 일반 스시인지 확인합니다.
    /// </summary>
    public bool IsSelectableTodayMenu(Sushi sushi)
    {
        // 메뉴 목록 관리자를 통해 일반 스시 여부를 조회합니다.
        return _sushiListManager != null &&
            _sushiListManager.IsSelectableTodayMenu(sushi);
    }

    /// <summary>조리가 끝난 주문을 준비 완료로 기록하고 추가 재료는 차감하지 않습니다.</summary>
    public void NotifyCookingCompleted(GuestNPC guest)
    {
        // 요리 완성 시 남은 조리 수량을 차감합니다.
        if (_sushiListManager != null) _sushiListManager.MarkPrepared(guest);
    }

}
