using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SushiShopProgress : MonoBehaviour
{
    [Serializable]
    public class MenuLevel
    {
        // 저장 및 불러오기에서도 유지되는 메뉴 고유 ID입니다.
        public int MenuId;
        // 해당 요리의 현재 강화 레벨입니다.
        public int Level = 1;
    }

    [Serializable]
    public class SaveData
    {
        // 저장 데이터 형식을 구분합니다.
        public int Version = 1;
        // 해금 여부와 분리된 직원의 현재 능력치 레벨입니다.
        public int ServingLevel = 1;
        public int CookingLevel = 1;
        // 1레벨인 기본 메뉴는 목록에 없어도 1레벨로 취급합니다.
        public List<MenuLevel> MenuLevels = new();
    }

    // GameManager 하위에서 유지할 서빙 직원의 강화 레벨입니다.
    [SerializeField, Min(1)] int _servingLevel = 1;
    // GameManager 하위에서 유지할 요리 직원의 강화 레벨입니다.
    [SerializeField, Min(1)] int _cookingLevel = 1;
    // 공유 SO와 분리한 플레이어별 요리 강화 정보입니다.
    [SerializeField] List<MenuLevel> _menuLevels = new();

    public int ServingLevel => Mathf.Max(1, _servingLevel);
    public int CookingLevel => Mathf.Max(1, _cookingLevel);

    /// <summary>한 번도 강화하지 않은 요리도 기본 1레벨로 조회합니다.</summary>
    public int GetMenuLevel(int menuId)
    {
        // 원본 Sushi 에셋에는 플레이어의 현재 레벨을 기록하지 않습니다.
        if (_menuLevels != null)
            foreach (MenuLevel entry in _menuLevels)
                if (entry != null && entry.MenuId == menuId)
                    return Mathf.Clamp(entry.Level, 1, SushiUpgradeData.MaxLevel);
        return 1;
    }

    /// <summary>비용 및 능력치 검증을 마친 직원 강화 결과를 기록합니다.</summary>
    internal void SetStaffLevel(StaffType type, int level)
    {
        // 해금 전에도 0레벨을 사용하지 않습니다.
        if (type == StaffType.Serving) _servingLevel = Mathf.Max(1, level);
        else if (type == StaffType.Cooking) _cookingLevel = Mathf.Max(1, level);
    }

    /// <summary>비용 차감에 성공한 요리 강화 결과를 해당 ID에 기록합니다.</summary>
    internal void SetMenuLevel(int menuId, int level)
    {
        // 외부 SO를 수정하지 않고 현재 플레이어의 목록만 변경합니다.
        if (_menuLevels == null) _menuLevels = new List<MenuLevel>();
        foreach (MenuLevel entry in _menuLevels)
            if (entry != null && entry.MenuId == menuId)
            {
                entry.Level = Mathf.Clamp(level, 1, SushiUpgradeData.MaxLevel);
                return;
            }
        _menuLevels.Add(new MenuLevel { MenuId = menuId, Level = level });
    }

    /// <summary>기존 저장 시스템에 전달할 독립된 JSON 스냅샷을 만듭니다.</summary>
    public string ExportJson()
    {
        // 저장용 목록과 현재 진행 목록이 같은 참조를 공유하지 않도록 복사합니다.
        SaveData save = new() { ServingLevel = ServingLevel, CookingLevel = CookingLevel };
        if (_menuLevels != null)
            foreach (MenuLevel entry in _menuLevels)
                if (entry != null) save.MenuLevels.Add(new MenuLevel { MenuId = entry.MenuId, Level = entry.Level });
        return JsonUtility.ToJson(save);
    }

    /// <summary>검증된 저장 데이터를 영업 전 관리자 경로에서만 반영합니다.</summary>
    internal bool TryImportJson(string json)
    {
        // 검증이 끝나기 전에는 현재 진행 상태를 바꾸지 않습니다.
        SaveData save;
        try { save = JsonUtility.FromJson<SaveData>(json); }
        catch (ArgumentException) { return false; }
        if (save == null || save.Version != 1 || save.ServingLevel < 1 ||
            save.CookingLevel < 1 || save.MenuLevels == null) return false;
        HashSet<int> ids = new();
        foreach (MenuLevel entry in save.MenuLevels)
            if (entry == null || entry.MenuId < 0 || !ids.Add(entry.MenuId) ||
                entry.Level < 1 || entry.Level > SushiUpgradeData.MaxLevel) return false;
        _servingLevel = save.ServingLevel;
        _cookingLevel = save.CookingLevel;
        _menuLevels = save.MenuLevels;
        return true;
    }
}
