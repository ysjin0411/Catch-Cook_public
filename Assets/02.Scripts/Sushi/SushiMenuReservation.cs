using System.Collections.Generic;

/// <summary>메뉴 준비 단계의 슬롯별 선택과 재료 예약량을 보관합니다. 실제 인벤토리는 변경하지 않습니다.</summary>
internal sealed class SushiMenuReservation
{
    // 외부에서 원본 선택이나 요구량을 변경하지 못하도록 내부에만 보관합니다.
    readonly SortedDictionary<int, SushiListManager.MenuSelection> _selections = new();
    readonly Dictionary<int, Dictionary<ItemData, int>> _requirements = new();
    internal int Count => _selections.Count;

    /// <summary>화면이 자유롭게 읽을 수 있는 슬롯별 선택 복사본을 반환합니다.</summary>
    internal IReadOnlyDictionary<int, SushiListManager.MenuSelection> Snapshot()
    {
        // 변경 가능한 MenuSelection은 항목까지 복사하여 내부 상태를 보호합니다.
        var result = new SortedDictionary<int, SushiListManager.MenuSelection>();
        foreach (var entry in _selections)
            result.Add(entry.Key, new SushiListManager.MenuSelection { Menu = entry.Value.Menu, Amount = entry.Value.Amount });
        return result;
    }

    /// <summary>특정 슬롯을 제외한 재료 예약 총량을 반환합니다.</summary>
    internal long GetReserved(ItemData item, int excludedSlot = -1)
    {
        // 수량 편집에서는 편집 대상의 이전 예약을 제외합니다.
        long total = 0;
        foreach (var entry in _requirements)
            if (entry.Key != excludedSlot && entry.Value.TryGetValue(item, out int amount)) total += amount;
        return total;
    }

    /// <summary>다른 슬롯에 같은 요리가 등록되어 있는지 검사합니다.</summary>
    internal bool ContainsMenu(Sushi menu, int excludedSlot)
    {
        // 동일 요리의 중복 슬롯 등록을 차단합니다.
        foreach (var entry in _selections)
            if (entry.Key != excludedSlot && entry.Value.Menu == menu) return true;
        return false;
    }

    /// <summary>검증된 슬롯 선택과 총 요구량을 함께 교체합니다.</summary>
    internal void Set(int slot, Sushi menu, int amount, Dictionary<ItemData, int> requirements)
    {
        // 호출자가 넘긴 컬렉션도 복사하여 예약값의 외부 변경을 방지합니다.
        _selections[slot] = new SushiListManager.MenuSelection { Menu = menu, Amount = amount };
        _requirements[slot] = new Dictionary<ItemData, int>(requirements);
    }

    /// <summary>슬롯의 메뉴와 재료 예약을 함께 제거합니다.</summary>
    internal bool Remove(int slot)
    {
        // 존재하지 않는 슬롯은 변경하지 않습니다.
        if (!_selections.Remove(slot)) return false;
        _requirements.Remove(slot);
        return true;
    }

    /// <summary>예약 목록을 비웁니다. 실제 재료의 차감이나 반환은 하지 않습니다.</summary>
    internal void Clear()
    {
        // 확정 성공 또는 명시적인 준비 취소 시 두 목록을 함께 정리합니다.
        _selections.Clear();
        _requirements.Clear();
    }

    /// <summary>기존 목록 전달 API가 현재 예약과 같은 메뉴 수량을 확정하는지 검사합니다.</summary>
    internal bool Matches(IReadOnlyList<SushiListManager.MenuSelection> selections)
    {
        // 예약이 없으면 기존 Presenter와 Inspector의 직접 확정 경로를 허용합니다.
        if (_selections.Count == 0) return true;
        if (selections == null || selections.Count != _selections.Count) return false;
        var seen = new HashSet<Sushi>();
        foreach (var selection in selections)
        {
            // 누락·중복·다른 수량으로 기존 예약을 덮어쓰는 요청을 거부합니다.
            if (selection == null || selection.Menu == null || !seen.Add(selection.Menu)) return false;
            bool found = false;
            foreach (var entry in _selections.Values)
                if (entry.Menu == selection.Menu && entry.Amount == selection.Amount) found = true;
            if (!found) return false;
        }
        return true;
    }
}
