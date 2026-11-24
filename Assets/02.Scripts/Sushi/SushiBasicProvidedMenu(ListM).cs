using System.Collections.Generic;

public partial class SushiListManager
{
    /// <summary>
    /// 기본 제공 Map을 읽어 런타임 메뉴 목록을 구성합니다.
    /// </summary>
    void Awake()
    {
        // 씬 초기화 시 기본 제공 목록을 구성합니다.
        RefreshBasicProvidedMenu();
    }

    /// <summary>
    /// 기본 제공 Map에 등록된 메뉴만 기본 제공 목록에 추가합니다.
    /// </summary>
    void RefreshBasicProvidedMenu()
    {
        // 이전에 구성한 목록을 비웁니다.
        _basicProvidedMenu.Clear();

        // Map이 연결되지 않았다면 빈 목록을 유지합니다.
        if (_basicMenuMap == null)
            return;

        // 메뉴 종류와 관계없이 기본 제공 Map의 항목을 읽습니다.
        foreach (Sushi menu in _basicMenuMap.Sushis)
        {
            // 비어 있거나 등록이 잘못된 항목과 중복 원본을 제외합니다.
            if (!IsBasicProvidedMenu(menu) ||
                _basicProvidedMenu.Contains(menu))
                continue;

            // 기본 제공 Map의 메뉴를 목록에 추가합니다.
            _basicProvidedMenu.Add(menu);
        }
    }

    /// <summary>
    /// 기본 제공 Map에 등록된 정확한 원본 메뉴인지 확인합니다.
    /// </summary>
    public bool IsBasicProvidedMenu(Sushi menu)
    {
        // ID뿐 아니라 원본 에셋도 일치해야 합니다.
        return menu != null &&
            _basicMenuMap != null &&
            _basicMenuMap.GetSushi(menu.Id) == menu;
    }

    /// <summary>
    /// 오늘의 메뉴로 선택할 수 있는 일반 스시인지 확인합니다.
    /// </summary>
    public bool IsSelectableTodayMenu(Sushi menu)
    {
        // 선택용 Map에 등록된 음식만 허용하고 기본 제공 메뉴는 제외합니다.
        return menu != null &&
            _sushiMap != null &&
            _sushiMap.GetSushi(menu.Id) == menu &&
            menu.Type == MenuType.Food &&
            !IsBasicProvidedMenu(menu);
    }

    /// <summary>
    /// 음료는 항상, 기본 제공 음식은 일반 스시 소진 후 주문 후보로 반환합니다.
    /// </summary>
    List<Sushi> GetBasicOrderCandidates()
    {
        // 현재 주문 가능한 기본 제공 메뉴를 보관합니다.
        List<Sushi> menus = new();

        // 새 손님에게 배정할 일반 스시가 남아 있는지 확인합니다.
        bool regularSoldOut = AvailableMenuCount == 0;

        // 기본 제공 Map에서 구성한 목록을 확인합니다.
        foreach (Sushi menu in _basicProvidedMenu)
        {
            // 유효하지 않거나 이미 추가한 메뉴는 제외합니다.
            if (!IsBasicProvidedMenu(menu) || menus.Contains(menu))
                continue;

            // 김초밥 같은 음식은 일반 스시 소진 후에만 주문할 수 있습니다.
            if (menu.Type != MenuType.Drink && !regularSoldOut)
                continue;

            // 현재 주문 가능한 기본 메뉴를 추가합니다.
            menus.Add(menu);
        }

        // 현재 조건을 만족한 메뉴 목록을 반환합니다.
        return menus;
    }

    /// <summary>
    /// 주문 가능한 기본 메뉴의 그룹을 가중치로 선택하고 그룹 안에서는 균등 추첨합니다.
    /// </summary>
    bool TrySelectBasicOrder(IReadOnlyList<Sushi> candidates, out Sushi menu)
    {
        // 선택 실패 시 반환할 기본값을 설정합니다.
        menu = null;

        // 기본 Map 또는 주문 후보가 없다면 선택할 수 없습니다.
        if (_basicMenuMap == null || candidates == null || candidates.Count == 0)
            return false;

        // 주문 가능 여부를 빠르게 조회할 수 있도록 구성합니다.
        HashSet<Sushi> orderableMenus = new(candidates);

        // 실제 추첨에 참여할 그룹의 메뉴와 가중치를 보관합니다.
        List<List<Sushi>> groupMenus = new();
        List<int> groupWeights = new();

        // 가중치를 합산할 때 int 범위를 넘지 않도록 long을 사용합니다.
        long totalWeight = 0;

        // Map에 설정된 주문 그룹을 확인합니다.
        foreach (SushiOrderGroup group in _basicMenuMap.OrderGroups)
        {
            // 비어 있거나 가중치가 없는 그룹은 제외합니다.
            if (group == null || group.Weight <= 0) continue;

            // 해당 그룹에서 현재 주문 가능한 메뉴를 보관합니다.
            List<Sushi> eligibleMenus = new();

            // 그룹에 등록된 메뉴를 확인합니다.
            foreach (Sushi candidate in group.Menus)
            {
                // 빈 항목, 주문 불가능한 메뉴, 그룹 내부 중복은 제외합니다.
                if (candidate == null || !orderableMenus.Contains(candidate) ||
                    eligibleMenus.Contains(candidate)) continue;

                // 주문 가능한 메뉴를 그룹 후보에 추가합니다.
                eligibleMenus.Add(candidate);
            }

            // 주문 가능한 메뉴가 없는 그룹은 추첨에서 제외합니다.
            if (eligibleMenus.Count == 0) continue;

            // 그룹의 메뉴와 가중치를 같은 인덱스에 보관합니다.
            groupMenus.Add(eligibleMenus);
            groupWeights.Add(group.Weight);

            // 유효한 그룹의 가중치를 합산합니다.
            totalWeight += group.Weight;
        }

        // 추첨 가능한 그룹이 없다면 실패합니다.
        if (totalWeight == 0) return false;

        // Random.value가 1이어도 유효한 마지막 추첨 위치 안에 있도록 제한합니다.
        long ticket = System.Math.Min(
            (long)(UnityEngine.Random.value * (double)totalWeight), totalWeight - 1);

        // 각 그룹의 가중치 구간을 순서대로 확인합니다.
        for (int i = 0; i < groupMenus.Count; i++)
        {
            // 현재 그룹의 구간 밖이면 다음 그룹으로 이동합니다.
            if (ticket >= groupWeights[i])
            {
                // 지나간 그룹의 가중치를 차감합니다.
                ticket -= groupWeights[i];

                // 다음 그룹을 확인합니다.
                continue;
            }

            // 선택된 그룹에서 메뉴 하나를 동일한 확률로 추첨합니다.
            List<Sushi> selectedMenus = groupMenus[i];
            int menuIndex = UnityEngine.Random.Range(0, selectedMenus.Count);

            // 선택한 메뉴를 반환합니다.
            menu = selectedMenus[menuIndex];

            // 메뉴 선택 성공을 알립니다.
            return true;
        }

        // 선택 결과가 없으면 실패합니다.
        return false;
    }

    /// <summary>
    /// 현재 주문 가능한 일반 스시와 기본 제공 메뉴를 반환합니다.
    /// </summary>
    public IReadOnlyList<Sushi> GetOrderableMenu()
    {
        // 일반 스시와 기본 제공 메뉴를 합칠 목록입니다.
        List<Sushi> menus = new();

        // 오늘의 메뉴가 확정되었다면 배정 가능한 일반 스시를 추가합니다.
        if (_dailyMenuCommitted && _todayMenu != null)
        {
            // 확정한 메뉴별 남은 배정 수량을 확인합니다.
            foreach (MenuSelection selection in _todayMenu)
            {
                // 유효한 항목이며 남은 수량이 있는 메뉴만 추가합니다.
                if (selection != null && selection.Available > 0)
                    menus.Add(selection.Menu);
            }
        }

        // 주문 조건을 만족한 음료와 김초밥을 추가합니다.
        menus.AddRange(GetBasicOrderCandidates());

        // 최종 주문 후보 목록을 반환합니다.
        return menus;
    }
}
