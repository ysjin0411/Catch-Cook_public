using TMPro;
using UnityEngine;

/// <summary>Sushi2의 목재 팻말을 실제 오늘의 메뉴와 동기화하는 표시 전용 컴포넌트입니다.</summary>
public sealed class Sushi2MenuBoard : MonoBehaviour
{
    [SerializeField] SushiListManager _menus;
    [SerializeField] Sushi _kimSushi;
    [SerializeField] Transform _slots;

    /// <summary>씬 제작 도구에서 기존 메뉴 데이터와 팻말 슬롯을 연결합니다.</summary>
    public void Configure(SushiListManager menus, Sushi kimSushi, Transform slots)
    {
        // 표시 전용 참조를 저장하고 현재 메뉴를 즉시 반영합니다.
        _menus = menus;
        _kimSushi = kimSushi;
        _slots = slots;
        Refresh();
    }

    /// <summary>활성화 시 메뉴 변경 알림을 구독합니다.</summary>
    void OnEnable()
    {
        // 재활성화에서도 중복 구독 없이 같은 데이터 소스를 사용합니다.
        if (_menus != null) _menus.TodayMenuChanged += Refresh;
        Refresh();
    }

    /// <summary>기본 메뉴의 초기화가 끝난 뒤 첫 표시를 갱신합니다.</summary>
    void Start()
    {
        // 다른 컴포넌트의 Awake 순서와 무관하게 초기 표시를 맞춥니다.
        Refresh();
    }

    /// <summary>비활성화 시 메뉴 변경 알림을 해제합니다.</summary>
    void OnDisable()
    {
        // 파괴된 팻말에 이벤트가 남지 않도록 구독을 해제합니다.
        if (_menus != null) _menus.TodayMenuChanged -= Refresh;
    }

    /// <summary>김초밥 고정 한 칸과 일반 메뉴 다섯 칸의 아이콘 및 수량을 갱신합니다.</summary>
    public void Refresh()
    {
        // 연결 전이나 잘못된 프리팹에서는 데이터 변경 없이 표시를 생략합니다.
        if (_slots == null || _menus == null || _slots.childCount != 6) return;
        // 첫 칸은 오늘의 메뉴 선택과 무관하게 김초밥과 무한 수량을 유지합니다.
        SetIcon(_slots.GetChild(0), _kimSushi);
        // 나머지 다섯 칸은 확정된 실제 메뉴 목록을 순서대로 표시합니다.
        for (int i = 0; i < 5; i++)
        {
            // 없는 슬롯은 빈 팻말의 더하기 기호로 돌립니다.
            Transform slot = _slots.GetChild(i + 1);
            SushiListManager.MenuSelection selection = i < _menus.TodayMenu.Count ? _menus.TodayMenu[i] : null;
            bool populated = selection != null && selection.Menu != null;
            slot.Find("MenuContent").gameObject.SetActive(populated);
            slot.Find("EmptyState").gameObject.SetActive(!populated);
            if (!populated) continue;
            // 남은 조리 수량과 계획 수량을 데이터 소스에서 그대로 읽습니다.
            SetIcon(slot, selection.Menu);
            slot.Find("MenuContent/RemainingCount").GetComponent<TMP_Text>().text = selection.Remaining.ToString();
            slot.Find("MenuContent/PlannedCount").GetComponent<TMP_Text>().text = selection.Planned.ToString();
        }
    }

    /// <summary>팻말의 아이콘만 변경하고 메뉴 원본이나 재고는 수정하지 않습니다.</summary>
    static void SetIcon(Transform slot, Sushi menu)
    {
        // 연결된 음식 에셋의 원래 아이콘을 사용합니다.
        var icon = slot.Find("MenuContent/Icon").GetComponent<UnityEngine.UI.Image>();
        icon.sprite = menu != null ? menu.Icon : null;
        icon.enabled = icon.sprite != null;
        icon.preserveAspect = true;
    }
}
