using System;
using System.Collections.Generic;
using UnityEngine;

public partial class SushiListManager
{
    /// <summary>새 손님에게 배정할 수 있는 일반 초밥의 총수량을 반환합니다.</summary>
    int SumAvailableStock()
    {
        // 메뉴 확정 전의 입력값이나 저장된 표시값은 판매 재고로 사용하지 않습니다.
        if (!_dailyMenuCommitted || _todayMenu == null) return 0;
        // 이미 다른 손님에게 예약한 수량은 새 배정 가능 수량에 포함하지 않습니다.
        int total = 0;
        foreach (MenuSelection selection in _todayMenu)
            if (selection != null) total += selection.Available;
        return total;
    }

    /// <summary>뷰 또는 ContextMenu의 수량 선택을 검증하고 오늘의 메뉴를 확정합니다.</summary>
    public bool SetTodayMenu(IReadOnlyList<MenuSelection> selections)
    {
        // 영업 중에는 수량을 재설정하지 않습니다.
        if (_settingTodayMenu || _spendingUpgradeIngredients || _dailyMenuCommitted || _sushiMap == null || selections == null || selections.Count == 0 ||
            (_sushiManager != null && _sushiManager.IsMenuConfirmed)) return false;
        // 예약이 존재하면 동일한 등록 목록만 확정하여 다른 요청의 예약 덮어쓰기를 막습니다.
        if (!_menuReservation.Matches(selections)) return false;
        // 일반 스시 소진 후 제공할 기본 음식이 등록되어 있는지 확인합니다.
        if (!_basicProvidedMenu.Exists(menu =>
            IsBasicProvidedMenu(menu) &&
            menu.Type == MenuType.Food))
        {
            // 기본 제공 Map에 김초밥 같은 대체 음식이 필요함을 알립니다.
            Debug.LogWarning("기본 제공 Map에 김초밥 같은 기본 음식을 등록하세요.", this);

            // 대체 음식이 없으면 오늘의 메뉴 확정을 중단합니다.
            return false;
        }
        // 인벤토리 변경 알림에서 같은 메뉴 확정이 재진입하지 못하게 합니다.
        _settingTodayMenu = true;
        try
        {
            Dictionary<Sushi, int> quantities = new();
            Dictionary<ItemData, int> requirements = new();
            int total = 0;
            foreach (MenuSelection entry in selections)
            {
                // 중복 선택한 종류는 수량을 합산합니다.
                if (entry == null || entry.Amount < 1) return false;
                // 기본 제공 음식과 음료를 유한 재고로 중복 등록하지 않습니다.
                if (!IsSelectableTodayMenu(entry.Menu))
                {
                    Debug.LogWarning("오늘의 메뉴에는 일반 초밥만 등록하세요. 김초밥과 음료는 기본 제공 메뉴로 자동 추가됩니다.", this);
                    return false;
                }
                // 일반 초밥의 재료 설정을 검증합니다.
                if (!TryGetIngredientAmounts(entry.Menu, out var ingredients)) return false;
                total = checked(total + entry.Amount);
                quantities.TryGetValue(entry.Menu, out int count);
                quantities[entry.Menu] = checked(count + entry.Amount);
                foreach (var ingredient in ingredients)
                {
                    requirements.TryGetValue(ingredient.Key, out int amount);
                    requirements[ingredient.Key] = checked(amount + checked(ingredient.Value * entry.Amount));
                }
            }
            // 중복 수량을 합친 일반 초밥의 실제 종류 수를 확인합니다.
            int selectedMenuCount = quantities.Count;
            // 현재 플레이어 레벨에서 허용하는 최대 종류 수를 조회합니다.
            int menuLimit = TodayMenuLimit;
            // 최소 또는 최대 종류 수를 벗어나면 재료를 차감하기 전에 거부합니다.
            if (selectedMenuCount < MinimumTodayMenuCount || selectedMenuCount > menuLimit)
            {
                // 현재 플레이어 레벨에서 선택 가능한 범위를 안내합니다.
                Debug.LogWarning(
                    $"오늘의 메뉴는 플레이어 {CurrentPlayerLevel}레벨에서 " +
                    $"{MinimumTodayMenuCount}~{menuLimit}종류까지 선택할 수 있습니다. " +
                    $"현재 선택: {selectedMenuCount}종류", this);
                // 기존 메뉴와 인벤토리를 유지하고 확정을 중단합니다.
                return false;
            }
            // 수량 전체에 필요한 재료를 실제 인벤토리와 비교합니다.
            Inventory inventory = _sushiManager != null ? _sushiManager.Inventory : null;
            foreach (var requirement in requirements)
                if (inventory == null || inventory.GetCount(requirement.Key) < requirement.Value)
                {
                    Debug.LogWarning($"오늘의 메뉴 재료 부족: {requirement.Key.ItemName} 필요 {requirement.Value}", this);
                    return false;
                }
            List<MenuSelection> menus = new();
            foreach (var entry in quantities)
            {
                // 입력과 확정 재고를 별도 목록으로 나누지 않고 같은 항목에 구성합니다.
                MenuSelection menu = new() { Menu = entry.Key, Amount = entry.Value };
                menu.ConfirmStock();
                menus.Add(menu);
            }
            // 전체 재료를 확정 시점에 한 번 차감한 뒤에만 판매 목록을 교체합니다.
            if (!SushiIngredientStock.TryConsume(inventory, requirements)) return false;
            _todayMenu = menus.ToArray();
            _allocations.Clear();
            // 확정된 메뉴의 재료를 다시 차감하지 않도록 잠급니다.
            _dailyMenuCommitted = true;
            // 실제 차감과 확정이 성공한 뒤에만 예약을 해제합니다. 실패 시 예약은 유지됩니다.
            _menuReservation.Clear();
            MenuReservationChanged?.Invoke();
            TodayMenuChanged?.Invoke();
            return total > 0;
        }
        catch (OverflowException)
        {
            // 정수 범위를 넘는 선택은 기존 메뉴를 유지한 채 거부합니다.
            return false;
        }
        finally
        {
            // 확정 성공 여부와 관계없이 다음 요청을 받을 수 있게 합니다.
            _settingTodayMenu = false;
        }
    }

    /// <summary>
    /// 일반 메뉴가 남아 있으면 일반 메뉴 80%, 음료 20%로 주문을 배정합니다.
    /// 일반 메뉴가 소진되면 기본 메뉴의 그룹 가중치에 따라 주문을 배정합니다.
    /// </summary>
    public bool TryAllocateGuest(GuestNPC guest, out Sushi menu)
    {
        // 배정에 실패했을 때 반환할 기본값을 설정합니다.
        menu = null;
        // 손님이 없거나 오늘의 메뉴가 확정되지 않았다면 배정을 중단합니다.
        if (guest == null || !_dailyMenuCommitted) return false;
        // 이미 배정받은 손님인지 확인합니다.
        if (_allocations.TryGetValue(guest, out var existing))
        {
            // 기존에 배정한 메뉴를 반환합니다.
            menu = existing.Menu;
            // 같은 손님에게 재고를 중복 배정하지 않습니다.
            return true;
        }
        // 다른 손님에게 예약되지 않은 일반 메뉴의 총수량을 확인합니다.
        int available = AvailableMenuCount;
        // 일반 메뉴가 남아 있으면 음료만, 소진되면 오이절임도 후보에 포함합니다.
        List<Sushi> basicCandidates = GetBasicOrderCandidates();
        // 일반 메뉴와 기본 메뉴 모두 주문할 수 없다면 배정을 중단합니다.
        if (available == 0 && basicCandidates.Count == 0) return false;
        // 일반 메뉴가 있을 때 80%로 선택하며, 기본 메뉴가 없으면 일반 메뉴를 선택합니다.
        bool selectRegular = available > 0 &&
            (basicCandidates.Count == 0 || UnityEngine.Random.value < 0.8f);

        // 일반 메뉴가 선택되었다면 남은 수량에 비례해 개별 메뉴를 추첨합니다.
        if (selectRegular)
        {
            // 일반 메뉴의 전체 배정 가능 수량 안에서 추첨 위치를 정합니다.
            int index = UnityEngine.Random.Range(0, available);
            // 오늘의 메뉴별 배정 가능 수량을 확인합니다.
            foreach (MenuSelection selection in _todayMenu)
            {
                // 비어 있거나 배정 가능 수량이 없는 항목은 제외합니다.
                if (selection == null || selection.Available <= 0) continue;
                // 추첨 위치가 현재 메뉴의 수량 범위를 벗어나는지 확인합니다.
                if (index >= selection.Available)
                {
                    // 현재 메뉴의 수량을 빼서 다음 메뉴의 범위로 이동합니다.
                    index -= selection.Available;
                    // 다음 메뉴를 확인합니다.
                    continue;
                }
                // 추첨 위치에 해당하는 일반 메뉴를 선택합니다.
                menu = selection.Menu;
                // 선택한 일반 메뉴의 수량을 예약하고 손님에게 배정합니다.
                AllocateGuest(guest, menu, selection);
                // 일반 메뉴 배정 성공을 반환합니다.
                return true;
            }
            // 배정 가능한 일반 메뉴를 찾지 못했다면 실패를 반환합니다.
            return false;
        }
        // 기본 메뉴 그룹 가중치로 추첨하며, 일반 메뉴 소진 후에는 6:4가 적용됩니다.
        if (!TrySelectBasicOrder(basicCandidates, out menu)) return false;
        // 기본 제공 메뉴는 유한한 재고 예약 없이 원본만 배정합니다.
        AllocateGuest(guest, menu);
        // 손님에게 메뉴 배정을 완료했음을 반환합니다.
        return true;
    }

    internal bool TryAllocatePracticeGuest(GuestNPC guest, Sushi menu)
    {
        if (guest == null || menu == null || !_dailyMenuCommitted || _allocations.ContainsKey(guest)) return false;
        // 기본 메뉴로 실습할 때는 플레이어가 등록한 판매 재고를 사용하지 않는다.
        if (IsBasicProvidedMenu(menu))
        {
            AllocateGuest(guest, menu);
            return true;
        }

        foreach (MenuSelection selection in _todayMenu)
        {
            if (selection == null || selection.Menu != menu || selection.Available <= 0) continue;
            AllocateGuest(guest, menu, selection);
            return true;
        }
        return false;
    }

    void AllocateGuest(GuestNPC guest, Sushi menu, MenuSelection selection = null)
    {
        selection?.Reserve();
        _allocations.Add(guest, new GuestAllocation { Selection = selection, Menu = menu });
        if (selection != null) TodayMenuChanged?.Invoke();
    }

    /// <summary>이미 입장한 손님이 배정받은 메뉴를 주문 시 조회합니다.</summary>
    public bool TryGetGuestMenu(GuestNPC guest, out Sushi menu)
    {
        // 입장 중 재고가 바뀌어도 자신의 한 개는 보장됩니다.
        menu = null;
        if (guest == null || !_allocations.TryGetValue(guest, out var entry)) return false;
        menu = entry.Menu;
        return true;
    }

    /// <summary>최초 조리 완료 시 남은 조리 수량을 차감하고 완료 상태를 기록합니다.</summary>
    public void MarkPrepared(GuestNPC guest)
    {
        // 같은 주문의 완료 알림을 반복 받아도 수량을 다시 차감하지 않습니다.
        if (guest == null || !_allocations.TryGetValue(guest, out var entry) || entry.Prepared) return;
        // 일반 초밥만 유한한 조리 수량을 차감합니다.
        if (entry.Selection != null && !entry.Selection.CompletePreparation()) return;
        // 김초밥도 완료 상태는 기록하지만 유한 수량은 사용하지 않습니다.
        entry.Prepared = true;
        TodayMenuChanged?.Invoke();
    }

    /// <summary>같은 음식 주문끼리 바꿔 서빙한 경우 남은 조리 상태도 맞바꿉니다.</summary>
    public void MarkSold(GuestNPC recipient, GuestNPC sourceGuest)
    {
        // 쿠커의 남은 주문 인계와 같은 방향으로 준비 여부를 이동합니다.
        if (recipient == null || !_allocations.TryGetValue(recipient, out var entry)) return;
        if (sourceGuest != null && sourceGuest != recipient &&
            _allocations.TryGetValue(sourceGuest, out var source) && source.Menu == entry.Menu)
        {
            bool prepared = source.Prepared;
            source.Prepared = entry.Prepared;
            entry.Prepared = prepared;
        }
        // 남아 있는 미완성 예약만 해제하며 판매 보상은 지급 경로에서 기록합니다.
        entry.Selection?.ReleaseReservation(entry.Prepared);
        _allocations.Remove(recipient);
        TodayMenuChanged?.Invoke();
    }

    /// <summary>퇴장 또는 초기화 실패 시 그 손님에게 배정된 수량만 정리합니다.</summary>
    public void CancelAllocation(GuestNPC guest)
    {
        // 중복 취소와 파괴 중 객체도 안전하게 처리합니다.
        if (ReferenceEquals(guest, null) || !_allocations.TryGetValue(guest, out var entry)) return;
        // 완성된 음식의 Remaining은 복구하지 않고 미완성 예약만 반환합니다.
        entry.Selection?.ReleaseReservation(entry.Prepared);
        _allocations.Remove(guest);
        TodayMenuChanged?.Invoke();
    }

    /// <summary>오늘의 메뉴에 해당하는 정상 판매 수량과 지급 보상을 기록합니다.</summary>
    public void RecordSale(Sushi menu, float gold, int playerExperience)
    {
        // 메뉴 확정 전에는 표시용 입력 항목에 판매 실적을 기록하지 않습니다.
        if (!_dailyMenuCommitted || _todayMenu == null) return;
        // 기본 제공 메뉴에는 유한 판매 재고가 없으므로 일반 초밥만 찾아 기록합니다.
        foreach (MenuSelection selection in _todayMenu)
        {
            // 다른 메뉴의 실적은 변경하지 않습니다.
            if (selection == null || selection.Menu != menu) continue;
            // 판매 시점의 보상을 기록하고 인스펙터 표시값을 갱신합니다.
            selection.RecordSale(gold, playerExperience);
            TodayMenuChanged?.Invoke();
            return;
        }
    }

    /// <summary>
    /// 두 Map 중 하나에 등록된 정확한 원본 메뉴인지 확인합니다.
    /// </summary>
    public bool IsRegisteredMenu(Sushi sushi)
    {
        // 원본 데이터가 없으면 등록된 메뉴가 아닙니다.
        if (sushi == null)
            return false;

        // 기본 제공 Map에 등록되어 있으면 유효한 메뉴입니다.
        if (IsBasicProvidedMenu(sushi))
            return true;

        // 일반 스시 Map에도 같은 원본이 등록되어 있는지 확인합니다.
        return _sushiMap != null &&
            _sushiMap.GetSushi(sushi.Id) == sushi;
    }

    /// <summary>메뉴별 확정 수량, 판매 수량, 남은 조리 수량과 보상을 출력합니다.</summary>
    [ContextMenu("테스트/오늘의 메뉴 현황 출력")]
    public void PrintTodayMenu()
    {
        // UI도 TodayMenu와 TodayMenuChanged를 이용해 같은 항목의 입력과 실적을 조회합니다.
        var text = new System.Text.StringBuilder("[오늘의 메뉴 현황]");
        foreach (MenuSelection selection in TodayMenu)
        {
            // 빈 메뉴 입력은 출력에서 제외합니다.
            if (selection == null || selection.Menu == null) continue;
            text.AppendLine($"\n{selection.Menu.Name}: 입력 {selection.Amount}, 제작 가능 {selection.CraftableCount}, 확정 {selection.Planned}, 판매 {selection.Sold}, 남은 조리 {selection.Remaining}, 골드 {selection.EarnedGold}, 경험치 {selection.EarnedPlayerExperience}");
        }
        Debug.Log(text.ToString(), this);
    }
}
