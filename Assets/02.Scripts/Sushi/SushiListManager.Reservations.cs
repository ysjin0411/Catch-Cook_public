using System;
using System.Collections.Generic;

public partial class SushiListManager
{
    // 준비 중 등록 데이터의 단일 원본이며, UI를 닫아도 유지합니다.
    readonly SushiMenuReservation _menuReservation = new();
    // 강화 비용 처리 중에는 예약 변경과 메뉴 확정을 차단합니다.
    bool _spendingUpgradeIngredients;
    public event Action MenuReservationChanged;
    public IReadOnlyDictionary<int, MenuSelection> ReservedMenu => _menuReservation.Snapshot();
    public bool CanEditMenuReservation => !_settingTodayMenu && !_dailyMenuCommitted &&
        !_spendingUpgradeIngredients && _sushiManager != null && !_sushiManager.IsMenuConfirmed;

    /// <summary>예약된 메뉴를 슬롯 순서의 확정 요청 복사본으로 반환합니다.</summary>
    public IReadOnlyList<MenuSelection> GetReservedMenuSelections()
    {
        // 슬롯 순서는 예약 클래스의 정렬된 복사본에서 유지됩니다.
        var result = new List<MenuSelection>();
        foreach (var entry in ReservedMenu) result.Add(entry.Value);
        return result;
    }

    /// <summary>실제 보유량에서 메뉴 예약량을 뺀 사용 가능 재료 수량을 반환합니다.</summary>
    public int GetAvailableIngredientCount(ItemData item, int excludedSlot = -1)
    {
        // 편집 대상 슬롯을 지정하면 해당 슬롯의 기존 예약은 다시 사용할 수 있습니다.
        Inventory inventory = _sushiManager != null ? _sushiManager.Inventory : null;
        if (item == null || inventory == null) return 0;
        return (int)Math.Max(0L, inventory.GetCount(item) - _menuReservation.GetReserved(item, excludedSlot));
    }

    /// <summary>해당 슬롯에 등록할 수 있는 메뉴의 최대 수량을 반환합니다.</summary>
    public int GetMaximumReservableQuantity(int slot, Sushi menu)
    {
        // 슬롯은 일반 메뉴 전용 0 기반 인덱스이며 기본 제공 메뉴는 제외합니다.
        if (!CanEditMenuReservation || slot < 0 || slot >= TodayMenuLimit ||
            !IsSelectableTodayMenu(menu) || _menuReservation.ContainsMenu(menu, slot) ||
            !TryGetIngredientAmounts(menu, out var ingredients) || ingredients.Count == 0) return 0;
        // 공유 재료와 편집 대상의 이전 예약을 모두 고려합니다.
        int maximum = int.MaxValue;
        foreach (var entry in ingredients)
            maximum = Math.Min(maximum, GetAvailableIngredientCount(entry.Key, slot) / entry.Value);
        return maximum;
    }

    /// <summary>메뉴 등록 또는 수량 변경을 검증한 뒤 예약을 교체합니다. 실패하면 기존 예약을 유지합니다.</summary>
    public bool TryReserveMenu(int slot, Sushi menu, int amount)
    {
        // 실제 인벤토리는 변경하지 않고 전체 수량을 먼저 검증합니다.
        if (amount <= 0 || amount > GetMaximumReservableQuantity(slot, menu) ||
            !TryGetIngredientAmounts(menu, out var ingredients)) return false;
        // 최대 수량 검사를 통과했으므로 재료별 곱은 실제 보유량 범위에 있습니다.
        var requirements = new Dictionary<ItemData, int>();
        foreach (var entry in ingredients) requirements.Add(entry.Key, checked(entry.Value * amount));
        _menuReservation.Set(slot, menu, amount, requirements);
        // 인벤토리 이벤트 대신 예약 변경 이벤트로 화면을 갱신합니다.
        MenuReservationChanged?.Invoke();
        return true;
    }

    /// <summary>등록한 메뉴와 해당 재료 예약을 삭제합니다.</summary>
    public bool TryRemoveReservedMenu(int slot)
    {
        // 확정 또는 강화 처리 중에는 예약을 변경하지 않습니다.
        if (!CanEditMenuReservation || !_menuReservation.Remove(slot)) return false;
        MenuReservationChanged?.Invoke();
        return true;
    }

    /// <summary>영업 준비를 명시적으로 취소할 때 모든 메뉴 예약을 해제합니다.</summary>
    public bool TryClearMenuReservations()
    {
        // 메뉴판을 닫는 동작과 준비 취소를 구분하여 호출합니다.
        if (!CanEditMenuReservation) return false;
        _menuReservation.Clear();
        MenuReservationChanged?.Invoke();
        return true;
    }

    /// <summary>강화 비용 처리 동안 예약 변경과 확정을 잠급니다.</summary>
    internal bool TryBeginIngredientSpend()
    {
        // 메뉴 확정 또는 다른 강화와 재진입하지 않습니다.
        if (!CanEditMenuReservation) return false;
        _spendingUpgradeIngredients = true;
        return true;
    }

    /// <summary>강화 비용 처리가 끝나면 예약 변경 잠금을 해제합니다.</summary>
    internal void EndIngredientSpend()
    {
        // 강화 호출자의 finally에서 반드시 실행합니다.
        _spendingUpgradeIngredients = false;
    }
}
