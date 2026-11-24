using System;
using System.Collections.Generic;
using UnityEngine;

public partial class SushiListManager : MonoBehaviour
{
    #region 인스펙터 표시 필드
    [Header("오늘의 메뉴 선택용 스시")]
    [SerializeField] SushiMap _sushiMap;

    [Header("기본 제공 메뉴: 음료 / 오이절임")]
    [SerializeField] SushiMap _basicMenuMap;

    [Header("참조")]
    [SerializeField] SushiManager _sushiManager;

    [Header("매장 성장 및 메뉴 해금")]
    [SerializeField] SushiSpawnDifficultyData _difficultyData;

    [Header("플레이어가 선택한 오늘의 메뉴")]
    [SerializeField] MenuSelection[] _todayMenu = System.Array.Empty<MenuSelection>();

    #endregion

    #region 런타임 상태 및 외부 조회
    // 기본 제공 Map에서 읽어 구성하는 런타임 목록입니다.
    readonly List<Sushi> _basicProvidedMenu = new();
    // 재료 차감 중 발생하는 인벤토리 이벤트의 재진입을 방지합니다.
    bool _settingTodayMenu;
    // 공개 메뉴 설정 API를 반복 호출해도 같은 영업의 재료를 다시 소비하지 않습니다.
    bool _dailyMenuCommitted;
    // 입장부터 서빙 또는 취소까지 한 손님에게 한 개만 배정합니다.
    readonly Dictionary<GuestNPC, GuestAllocation> _allocations = new();

    // UI가 변경 시 다시 조회할 수 있도록 알립니다.
    public event Action TodayMenuChanged;
    public IReadOnlyList<Sushi> BasicProvidedMenu => _basicProvidedMenu.AsReadOnly();
    public IReadOnlyList<MenuSelection> TodayMenu => Array.AsReadOnly(_todayMenu ?? Array.Empty<MenuSelection>());
    public int AvailableMenuCount => SumAvailableStock();
    // 연결된 매니저에서 현재 플레이어 레벨을 조회합니다.
    public int CurrentPlayerLevel => _sushiManager != null
        ? Mathf.Max(1, _sushiManager.CurrentPlayerLevel) : 1;
    // 오늘의 메뉴는 일반 초밥을 최소 한 종류 선택해야 합니다.
    public int MinimumTodayMenuCount => SushiSpawnDifficultyData.MinimumTodayMenuCount;
    // 현재 플레이어 레벨에서 선택할 수 있는 일반 초밥의 최대 종류 수입니다.
    public int TodayMenuLimit => _difficultyData != null
        ? _difficultyData.GetTodayMenuLimit(CurrentPlayerLevel) : 3;
    #endregion

    #region 메뉴 데이터 형식
    [Serializable]
    public class MenuSelection
    {
        // 확정 전 입력과 확정 후 판매 실적을 같은 메뉴 항목에서 관리합니다.
        public Sushi Menu;
        [Min(1)] public int Amount = 1;

        [SerializeField, Tooltip("실제 인벤토리 재료로 계산한 제작 가능 수량입니다.")]
        int _craftableCount;
        [SerializeField] int _planned;
        [SerializeField] int _sold;
        [SerializeField] int _remaining;
        [SerializeField] float _earnedGold;
        [SerializeField] int _earnedPlayerExperience;
        // 미완성 예약은 중복 배정 방지용이며 인스펙터에서 숨깁니다.
        [SerializeField, HideInInspector] int _reserved;

        public int CraftableCount => _craftableCount;
        public int Planned => _planned;
        public int Sold => _sold;
        public int Remaining => _remaining;
        public float EarnedGold => _earnedGold;
        public int EarnedPlayerExperience => _earnedPlayerExperience;
        public int Available => Mathf.Max(0, _remaining - _reserved);

        /// <summary>실제 인벤토리로 계산한 제작 가능 수량을 표시합니다.</summary>
        public void UpdateDisplay(int craftableCount)
        {
            // 입력 수량과 확정 실적은 바꾸지 않고 계산 결과만 갱신합니다.
            _craftableCount = Mathf.Max(-1, craftableCount);
        }

        /// <summary>검증된 입력 수량을 확정하여 해당 메뉴의 재고와 실적을 초기화합니다.</summary>
        internal void ConfirmStock()
        {
            // Amount를 나중에 바꾸어도 이미 확정한 조리 수량은 달라지지 않습니다.
            _planned = _remaining = Amount;
            // 새로운 확정 항목은 미완성 예약과 판매 실적을 초기화합니다.
            _reserved = _sold = _earnedPlayerExperience = 0;
            _earnedGold = 0f;
        }

        /// <summary>손님 한 명에게 아직 완성되지 않은 메뉴 한 개를 예약합니다.</summary>
        internal void Reserve()
        {
            // 예약 단계에서는 남은 조리 수량을 차감하지 않습니다.
            _reserved++;
        }

        /// <summary>최초 조리 완료 시 미완성 예약과 남은 조리 수량을 차감합니다.</summary>
        internal bool CompletePreparation()
        {
            // 유효한 미완성 예약이 있는 경우에만 완료를 처리합니다.
            if (_reserved <= 0 || _remaining <= 0) return false;
            // 완성 후 서빙하거나 폐기할 때 이 수량은 다시 변경하지 않습니다.
            _reserved--;
            _remaining--;
            return true;
        }

        /// <summary>주문이 끝나면 아직 완성되지 않은 예약만 반환합니다.</summary>
        internal void ReleaseReservation(bool prepared)
        {
            // 완성한 음식의 예약은 조리 완료 시 이미 해제했습니다.
            if (prepared) return;
            // 미완성 주문만 다른 손님에게 다시 배정할 수 있습니다.
            _reserved--;
        }

        /// <summary>정상 판매 수량과 판매 시점에 지급한 보상을 누적합니다.</summary>
        internal void RecordSale(float gold, int playerExperience)
        {
            // 판매 실적은 남은 조리 수량과 독립적으로 집계합니다.
            _sold++;
            // 실제 지급한 보상을 메뉴별로 누적합니다.
            _earnedGold += gold;
            _earnedPlayerExperience += playerExperience;
        }
    }

    sealed class GuestAllocation
    {
        // 일반 초밥은 입력과 실적을 함께 관리하는 오늘의 메뉴 항목을 연결합니다.
        public MenuSelection Selection;
        // 기본 제공 메뉴도 재고 객체 없이 주문 원본을 보관합니다.
        public Sushi Menu;
        // 조리 완료 후에는 남은 조리 수량과 예약을 다시 변경하지 않습니다.
        public bool Prepared;
    }

    #endregion

    /// <summary>
    /// 스시 하나에 필요한 재료를 검증하고 아이템별 요구량을 합산합니다.
    /// </summary>
    /// <returns>스시와 재료 데이터가 유효하면 true를 반환합니다.</returns>
    internal bool TryGetIngredientAmounts(Sushi sushi, out Dictionary<ItemData, int> ingredientAmounts)
    {
        // 스시 1개에 필요한 아이템별 수량을 보관할 공간을 준비합니다.
        ingredientAmounts = new Dictionary<ItemData, int>();

        // 두 Map 중 하나에 등록된 실제 메뉴인지 확인합니다.
        if (!IsRegisteredMenu(sushi))
        {
            // 잘못된 검사 대상을 알려줍니다.
            Debug.LogWarning("제작 검사 실패: SushiMap에 등록된 스시가 필요합니다.", this);
            return false;
        }

        // 음료는 재료 없이 손님 앞에서 제공하므로 빈 요구량으로 검증을 마칩니다.
        if (sushi.Type == MenuType.Drink) return true;

        // 음식의 요구 재료가 없는 데이터는 설정 누락으로 처리합니다.
        if (sushi.Ingredients.Count == 0)
        {
            // 재료 설정을 확인하도록 안내합니다.
            Debug.LogWarning($"[{sushi.Name}] 요구 재료가 설정되지 않았습니다.", this);
            return false;
        }

        // 스시에 등록된 요구 재료를 순회합니다.
        foreach (Sushi.IngredientRequirement ingredient in sushi.Ingredients)
        {
            // 비어 있는 재료와 유효하지 않은 요구량을 검사합니다.
            if (ingredient == null || ingredient.Item == null || ingredient.Amount <= 0)
            {
                // 잘못된 재료를 누락한 채 가능 판정하지 않도록 중단합니다.
                Debug.LogWarning($"[{sushi.Name}] 요구 재료 설정을 확인하세요.", this);
                return false;
            }

            // 같은 아이템에 이미 등록된 요구량을 가져옵니다.
            ingredientAmounts.TryGetValue(ingredient.Item, out int current);

            // 요구량을 합산할 수 없는 범위라면 잘못된 계산을 막습니다.
            if (current > int.MaxValue - ingredient.Amount)
            {
                // 수량 범위를 초과한 재료 설정을 알려줍니다.
                Debug.LogWarning($"[{sushi.Name}] 요구 재료 수량이 int 범위를 초과합니다.", this);
                return false;
            }

            // 중복 등록된 재료도 스시 1개의 총 요구량으로 합산합니다.
            ingredientAmounts[ingredient.Item] = current + ingredient.Amount;
        }

        // 모든 요구 재료의 검증이 완료됐음을 반환합니다.
        return true;
    }

    /// <summary>
    /// 현재 보유 재료로 해당 스시를 최대 몇 개 제작할 수 있는지 계산합니다.
    /// </summary>
    int GetCraftableCount(Dictionary<ItemData, int> ingredients)
    {
        // 요구 재료가 없으면 제작할 수 없는 것으로 처리합니다.
        if (ingredients == null || ingredients.Count == 0) return 0;

        // 조회 복사본을 만들지 않고 연결된 실제 인벤토리를 사용합니다.
        Inventory inventory = _sushiManager != null ? _sushiManager.Inventory : null;
        if (inventory == null) return 0;

        // 재료별 제작 가능 개수 중 최솟값을 찾기 위한 기준값입니다.
        int craftableCount = int.MaxValue;

        // 스시 1개에 필요한 재료들을 확인합니다.
        foreach (var ingredient in ingredients)
        {
            // 실제 인벤토리에서 해당 재료의 현재 보유량을 직접 가져옵니다.
            int owned = inventory.GetCount(ingredient.Key);

            // 해당 재료만 기준으로 만들 수 있는 개수를 계산합니다.
            int count = owned / ingredient.Value;

            // 가장 부족한 재료가 실제 제작 가능 개수를 결정합니다.
            craftableCount = Mathf.Min(craftableCount, count);
        }

        // 모든 재료를 고려한 최대 제작 가능 개수를 반환합니다.
        return craftableCount;
    }

    /// <summary>
    /// 오늘의 메뉴 재고 변경 이벤트를 구독합니다.
    /// </summary>
    void OnEnable()
    {
        // 판매, 예약 및 취소로 재고가 바뀔 때 표시값을 갱신합니다.
        TodayMenuChanged += RefreshTodayMenuDisplay;
    }

    /// <summary>
    /// 비활성화 시 재고 변경 이벤트 구독을 해제합니다.
    /// </summary>
    void OnDisable()
    {
        // 재활성화할 때 구독이 중복되지 않도록 해제합니다.
        TodayMenuChanged -= RefreshTodayMenuDisplay;
    }

    /// <summary>
    /// 실제 인벤토리 기준으로 선택한 메뉴의 제작 가능 수량을 갱신합니다.
    /// </summary>
    public void RefreshTodayMenuDisplay()
    {
        // 선택한 메뉴 배열이 없으면 갱신할 대상이 없습니다.
        if (_todayMenu == null) return;

        // 확정 수량과 실적은 유지하고 같은 항목의 제작 가능 수량만 갱신합니다.
        foreach (MenuSelection selection in _todayMenu)
        {
            // Inspector에 비어 있는 항목이 있으면 건너뜁니다.
            if (selection == null) continue;

            // 잘못된 메뉴에는 이전에 계산한 표시값을 남기지 않습니다.
            if (!IsRegisteredMenu(selection.Menu))
            {
                selection.UpdateDisplay(0);
                continue;
            }

            // 음료는 재료가 필요 없다는 표시값을 사용합니다.
            int craftableCount = -1;
            if (selection.Menu.Type != MenuType.Drink)
            {
                // 음식의 재료 설정이 잘못되었다면 제작 가능량을 초기화합니다.
                if (!TryGetIngredientAmounts(selection.Menu, out var ingredients))
                {
                    selection.UpdateDisplay(0);
                    continue;
                }

                // 별도 불러오기 없이 실제 인벤토리의 최신 보유 재료로 계산합니다.
                craftableCount = GetCraftableCount(ingredients);
            }

            // 플레이어의 입력 수량은 유지하고 계산용 표시값만 갱신합니다.
            selection.UpdateDisplay(craftableCount);
        }
    }

    /// <summary>
    /// 인스펙터에 입력한 오늘의 메뉴 종류와 수량을 검증하여 영업을 시작합니다.
    /// </summary>
    [ContextMenu("오늘의 메뉴 확정")]
    void ConfirmSelectedTodayMenu()
    {
        // 편집 모드에서는 런타임 재고와 영업 상태를 변경하지 않습니다.
        if (!Application.isPlaying)
        {
            Debug.LogWarning("플레이 모드에서 오늘의 메뉴를 확정하세요.", this);
            return;
        }

        // 메뉴 확정과 인벤토리 조회를 담당하는 매니저를 확인합니다.
        if (_sushiManager == null)
        {
            Debug.LogWarning("SushiManager를 연결하세요.", this);
            return;
        }

        // 확정된 영업의 메뉴와 수량은 다시 설정하지 않습니다.
        if (_sushiManager.IsMenuConfirmed)
        {
            Debug.LogWarning("이미 오늘의 메뉴가 확정되었습니다.", this);
            return;
        }

        // 입력한 메뉴가 없다면 확정을 중단합니다.
        if (_todayMenu == null || _todayMenu.Length == 0)
        {
            Debug.LogWarning("오늘의 메뉴에 음식 종류와 수량을 입력하세요.", this);
            return;
        }

        // 실제 인벤토리에서 제작 가능 수량만 계산하며 목록과 골드는 복사하지 않습니다.
        RefreshTodayMenuDisplay();

        // Inspector 입력과 분리된 요청 목록을 준비합니다.
        var selections = new List<MenuSelection>(_todayMenu.Length);
        foreach (MenuSelection entry in _todayMenu)
        {
            // 빈 항목과 잘못된 수량은 누락하지 않고 입력 오류로 처리합니다.
            if (entry == null || entry.Menu == null || entry.Amount <= 0)
            {
                Debug.LogWarning("오늘의 메뉴의 종류와 1개 이상의 수량을 확인하세요.", this);
                return;
            }

            // 표시용 재고는 전달하지 않고 플레이어의 선택값만 복사합니다.
            selections.Add(new MenuSelection { Menu = entry.Menu, Amount = entry.Amount });
        }

        // 기존 공통 API에서 재료 총합과 직원 상태를 검증하고 영업을 시작합니다.
        if (!_sushiManager.ConfirmTodayMenu(selections))
        {
            Debug.LogWarning("오늘의 메뉴 확정 실패: 재료, 수량, 메뉴 등록 및 직원 설정을 확인하세요.", this);
            return;
        }

        // 확정된 실제 재고를 표시하고 종류별 잔여 수량을 출력합니다.
        RefreshTodayMenuDisplay();
        PrintTodayMenu();
    }

    /// <summary>
    /// 기본 제공 메뉴를 제외하고 오늘의 메뉴로 선택할 수 있는 일반 초밥을 반환합니다.
    /// </summary>
    public IReadOnlyList<Sushi> GetAvailableMenu()
    {
        // Map이 없다면 빈 목록을 반환합니다.
        if (_sushiMap == null) return System.Array.Empty<Sushi>();

        // 김초밥과 음료는 자동 제공하므로 오늘의 메뉴 선택 후보에서 제외합니다.
        List<Sushi> menus = new();
        foreach (Sushi menu in _sushiMap.Sushis)
            if (IsSelectableTodayMenu(menu)) menus.Add(menu);
        return menus;
    }

}
