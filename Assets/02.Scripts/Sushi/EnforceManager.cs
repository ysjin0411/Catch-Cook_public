using System;
using System.Collections.Generic;
using UnityEngine;

public enum StaffType
{
    Serving, // 서빙 직원입니다.
    Cooking  // 요리 직원입니다.
}

/// <summary>
/// 요리와 직원의 강화 조건을 검사하고 비용 지불 및 진행 정보 변경을 처리합니다.
/// </summary>
public class EnforceManager : MonoBehaviour
{
    [Header("매장 관리자")]
    // 영업 상태와 실제 인벤토리를 조회할 관리자입니다.
    [SerializeField] SushiManager _sushiManager;

    [Header("요리별 강화 설정")]
    // 요리 원본에 연결된 강화 설정을 조회합니다.
    [SerializeField] SushiUpgradeMap _sushiUpgradeMap;

    [Header("직원 레벨 표시")]
    // 실제 진행 정보가 연결되면 동기화할 서빙 직원 레벨입니다.
    [SerializeField, Min(1)] int _servingLevel = 1;

    // 실제 진행 정보가 연결되면 동기화할 요리 직원 레벨입니다.
    [SerializeField, Min(1)] int _cookingLevel = 1;

    // 플레이어의 실제 요리 및 직원 강화 정보를 보관합니다.
    SushiShopProgress _progress;

    // 비용 차감 이벤트를 통한 중복 강화 실행을 차단합니다.
    bool _isUpgrading;

    // 강화 시도가 끝난 뒤 현재 상태를 다시 표시하도록 알립니다.
    public event Action UpgradeStateChanged;

    public int ServingLevel =>
        _progress != null
            ? _progress.ServingLevel
            : Mathf.Max(1, _servingLevel);

    public int CookingLevel =>
        _progress != null
            ? _progress.CookingLevel
            : Mathf.Max(1, _cookingLevel);

    public SushiShopProgress Progress => _progress;

    /// <summary>View 또는 Presenter가 직원 종류별 현재 강화 진행 레벨을 조회합니다.</summary>
    public int GetStaffLevel(StaffType type)
    {
        // 기존 진행 정보 조회 프로퍼티를 사용하여 레벨의 저장 원본을 통일합니다.
        switch (type)
        {
            // 서빙 직원의 저장된 강화 단계를 반환합니다.
            case StaffType.Serving: return ServingLevel;
            // 요리 직원의 저장된 강화 단계를 반환합니다.
            case StaffType.Cooking: return CookingLevel;
            // 지원하지 않는 직원 종류는 유효한 레벨로 취급하지 않습니다.
            default: return 0;
        }
    }

    public bool CanUpgradeServing => CanUpgradeStaff(StaffType.Serving);
    public bool CanUpgradeCooking => CanUpgradeStaff(StaffType.Cooking);

    /// <summary>
    /// 플레이어의 실제 강화 진행 정보를 연결하고 직원 표시 레벨을 동기화합니다.
    /// </summary>
    public void BindProgress(SushiShopProgress progress)
    {
        // 공유 Config와 분리된 플레이어 진행 정보를 연결합니다.
        _progress = progress;

        // 직원 표시 레벨을 실제 진행 정보와 맞춥니다.
        _servingLevel = ServingLevel;
        _cookingLevel = CookingLevel;
    }

    /// <summary>
    /// 해당 요리의 현재 강화 레벨을 반환합니다.
    /// </summary>
    public int GetMenuLevel(Sushi sushi)
    {
        // 유효한 음식과 진행 정보가 있을 때 실제 레벨을 조회합니다.
        if (sushi != null &&
            sushi.Type == MenuType.Food &&
            _progress != null)
        {
            // 요리 ID에 기록된 현재 레벨을 반환합니다.
            return _progress.GetMenuLevel(sushi.Id);
        }

        // 진행 정보가 없거나 음식이 아니면 기본 레벨을 반환합니다.
        return 1;
    }

    /// <summary>
    /// 강화 데이터 목록에서 대상 요리가 일치하는 설정을 찾습니다.
    /// </summary>
    SushiUpgradeData GetData(Sushi sushi)
    {
        // 대상 요리와 강화 데이터 목록이 필요합니다.
        if (sushi == null || _sushiUpgradeMap == null)
            return null;

        // 일치하는 설정을 보관합니다.
        SushiUpgradeData result = null;

        // 등록된 강화 데이터들을 확인합니다.
        foreach (SushiUpgradeData data in _sushiUpgradeMap.Datas)
        {
            // 비어 있거나 다른 요리의 설정은 건너뜁니다.
            if (data == null || data.TargetSushi != sushi)
                continue;

            // 같은 요리의 설정이 중복 등록되어 있으면 조회에 실패합니다.
            if (result != null)
                return null;

            // 대상 요리가 일치하는 설정을 저장합니다.
            result = data;
        }

        // 찾은 설정을 반환하며, 없으면 null을 반환합니다.
        return result;
    }

    /// <summary>
    /// 영업 상태와 현재 골드를 기준으로 강화 비용 지불 가능 여부를 검사합니다.
    /// </summary>
    bool CanPayUpgradeCost(float cost)
    {
        // 진행 정보와 관리자가 연결되어 있어야 합니다.
        if (_progress == null || _sushiManager == null)
            return false;

        // 중복 실행과 강화할 수 없는 영업 상태를 거부합니다.
        if (_isUpgrading ||
            !isActiveAndEnabled ||
            !_sushiManager.CanManageStaff)
            return false;

        // 비용은 유효한 양수여야 합니다.
        if (!(cost > 0f) || float.IsInfinity(cost))
            return false;

        // 실제 플레이어 인벤토리를 조회합니다.
        Inventory inventory = _sushiManager.Inventory;

        // 유효한 골드를 비용 이상 보유했는지 반환합니다.
        return inventory != null &&
            !float.IsNaN(inventory.Gold) &&
            !float.IsInfinity(inventory.Gold) &&
            inventory.Gold >= cost;
    }

    /// <summary>
    /// 기본 제공 메뉴는 원본 보상을, 일반 스시는 강화된 판매 보상을 반환합니다.
    /// </summary>
    public bool TryGetReward(
        Sushi sushi,
        out float gold,
        out int experience)
    {
        // 조회 실패 시 사용할 기본값입니다.
        gold = 0f;
        experience = 0;

        // 실제 메뉴 Map에 등록된 원본인지 확인합니다.
        if (_sushiManager == null ||
            !_sushiManager.IsRegisteredMenu(sushi))
            return false;

        // 음료와 김초밥은 강화 데이터를 사용하지 않습니다.
        if (_sushiManager.IsBasicProvidedMenu(sushi))
        {
            // 원본에 설정된 골드와 경험치를 그대로 사용합니다.
            gold = sushi.Gold;
            experience = sushi.PlayerExperience;

            // 지급할 보상이 유효한지 확인합니다.
            return gold >= 0f &&
                !float.IsInfinity(gold) &&
                experience >= 0;
        }

        // 일반 스시로 사용할 수 없는 데이터는 제외합니다.
        if (!_sushiManager.IsSelectableTodayMenu(sushi))
            return false;

        // 일반 스시의 강화 데이터를 조회합니다.
        SushiUpgradeData data = GetData(sushi);

        // 현재 강화 레벨의 판매 골드와 원본 경험치를 반환합니다.
        return data != null &&
            data.TryGetReward(
                sushi,
                GetMenuLevel(sushi),
                out gold,
                out experience);
    }

    /// <summary>
    /// 일반 스시의 다음 강화 단계에 필요한 골드를 조회합니다.
    /// </summary>
    public bool TryGetMenuUpgradeCost(Sushi sushi, out float cost)
    {
        // 조회 실패 시 사용할 기본값입니다.
        cost = 0f;

        // 선택용 Map의 일반 스시만 강화 대상으로 허용합니다.
        if (_sushiManager == null ||
            !_sushiManager.IsSelectableTodayMenu(sushi))
            return false;

        // 해당 스시의 강화 데이터와 현재 레벨을 조회합니다.
        SushiUpgradeData data = GetData(sushi);
        int level = GetMenuLevel(sushi);

        // 다음 단계의 비용과 보상이 모두 유효한지 확인합니다.
        return data != null &&
            data.TryGetUpgradeCost(level, out cost) &&
            data.TryGetReward(sushi, level + 1, out _, out _);
    }

    /// <summary>
    /// 다음 강화 단계의 골드와 재료별 필요 수량을 조회합니다.
    /// </summary>
    public bool TryGetMenuUpgradeRequirements(
        Sushi sushi,
        out float goldCost,
        out Dictionary<ItemData, int> requirements)
    {
        // 조회 실패 시 사용할 기본값을 준비합니다.
        goldCost = 0f;
        requirements = new Dictionary<ItemData, int>();

        // 등록 여부, 음식 여부, 비용과 다음 보상을 검증합니다.
        if (!TryGetMenuUpgradeCost(sushi, out goldCost))
            return false;

        // 해당 요리의 강화 설정을 조회합니다.
        SushiUpgradeData data = GetData(sushi);

        // 현재 레벨에 해당하는 강화 단계를 조회합니다.
        if (data == null ||
            !data.TryGetStep(
                GetMenuLevel(sushi),
                out SushiUpgradeData.UpgradeStep step))
            return false;

        // 같은 재료의 필요 수량을 합산해서 반환합니다.
        return step.TryGetRequirements(out requirements);
    }

    /// <summary>
    /// 영업 상태, 레벨, 골드와 모든 강화 재료 조건을 만족하는지 반환합니다.
    /// </summary>
    public bool CanUpgradeMenu(Sushi sushi)
    {
        // 다음 강화 단계의 유효한 비용을 조회합니다.
        if (!TryGetMenuUpgradeRequirements(
                sushi,
                out float goldCost,
                out Dictionary<ItemData, int> requirements))
            return false;

        // 강화 가능한 상태이며 골드가 충분한지 확인합니다.
        if (!CanPayUpgradeCost(goldCost))
            return false;

        // 모든 강화 재료를 오늘의 메뉴 예약을 제외한 사용 가능량과 비교합니다.
        foreach (KeyValuePair<ItemData, int> requirement in requirements)
        {
            // 한 종류라도 부족하면 강화할 수 없습니다.
            if (_sushiManager.GetAvailableIngredientCount(requirement.Key) < requirement.Value)
                return false;
        }

        // 모든 강화 조건을 만족합니다.
        return true;
    }

    /// <summary>
    /// 비용을 지불하고 해당 요리의 레벨을 한 단계 증가시킵니다.
    /// </summary>
    public bool TryUpgradeMenu(Sushi sushi)
    {
        // 버튼 표시 이후 조건이 바뀔 수 있으므로 다시 검사합니다.
        if (!CanUpgradeMenu(sushi))
            return false;

        // 이번 강화에 사용할 실제 비용을 조회합니다.
        if (!TryGetMenuUpgradeRequirements(
                sushi,
                out float goldCost,
                out Dictionary<ItemData, int> requirements))
            return false;

        // 인벤토리 변경 이벤트 전에 대상 정보를 확보합니다.
        int nextLevel = GetMenuLevel(sushi) + 1;
        Inventory inventory = _sushiManager.Inventory;
        SushiShopProgress progress = _progress;

        // 골드 변경 이벤트부터 반환 완료까지 메뉴 예약 변경과 확정을 함께 차단합니다.
        if (!_sushiManager.TryBeginIngredientSpend()) return false;

        // 비용 지불 및 반환 도중의 중복 강화를 차단합니다.
        _isUpgrading = true;

        try
        {
            // 기존 인벤토리 API로 골드를 지불합니다.
            if (!inventory.TrySpendGold(goldCost))
                return false;

            // 골드 변경 이벤트 후에도 예약 제외 수량을 다시 검사한 뒤 강화 재료를 소모합니다.
            if (!CanConsumeUnreservedIngredients(requirements) || !SushiIngredientStock.TryConsume(inventory, requirements))
            {
                // 재료 차감 실패 시 먼저 지불한 골드를 반환합니다.
                if (!inventory.TryAddGold(goldCost))
                {
                    // 골드 반환 실패를 기록합니다.
                    Debug.LogError("[요리 강화] 골드 반환 실패.", this);
                }

                // 비용 지불에 실패했으므로 레벨을 변경하지 않습니다.
                return false;
            }

            // 비용 지불에 성공한 요리의 레벨만 증가시킵니다.
            progress.SetMenuLevel(sushi.Id, nextLevel);

            // 요리 강화에 성공했습니다.
            return true;
        }
        finally
        {
            // 비용 반환까지 끝난 뒤 메뉴 등록과 확정을 다시 허용합니다.
            _sushiManager.EndIngredientSpend();
            // 성공 여부와 관계없이 중복 실행 차단을 해제합니다.
            _isUpgrading = false;

            // 완료 또는 비용 반환 후의 최종 상태를 표시하도록 알립니다.
            UpgradeStateChanged?.Invoke();
        }
    }

    /// <summary>요리 강화 실행 직전에 모든 재료가 메뉴 예약을 침범하지 않는지 확인합니다.</summary>
    bool CanConsumeUnreservedIngredients(IReadOnlyDictionary<ItemData, int> requirements)
    {
        // 비용 조회 이후의 인벤토리 변경까지 반영한 최신 사용 가능량을 검사합니다.
        foreach (var requirement in requirements)
            if (_sushiManager.GetAvailableIngredientCount(requirement.Key) < requirement.Value) return false;
        return true;
    }

    /// <summary>
    /// 해금된 직원의 다음 강화 단계 골드 비용을 조회합니다.
    /// </summary>
    public bool TryGetStaffUpgradeCost(
        StaffType type,
        out float cost)
    {
        // 조회 실패 시 사용할 기본값입니다.
        cost = 0f;

        // 직원과 플레이어 상태를 조회할 관리자가 필요합니다.
        if (_sushiManager == null)
            return false;

        // 서빙 직원의 해금 조건과 다음 강화 비용을 확인합니다.
        if (type == StaffType.Serving &&
            _sushiManager.Serving != null)
        {
            // 서빙 직원의 기존 강화 설정을 조회합니다.
            ServingStaffUpgradeData data =
                _sushiManager.Serving.UpgradeData;

            // 해금된 직원의 유효한 다음 단계 비용을 반환합니다.
            return data != null &&
                _sushiManager.CurrentPlayerLevel >= _sushiManager.Serving.UnlockPlayerLevel &&
                _sushiManager.Serving.TryGetMoveSpeed(ServingLevel + 1, out _) &&
                data.TryGetUpgradeCost(ServingLevel, out cost);
        }

        // 요리 직원의 해금 조건과 다음 강화 비용을 확인합니다.
        if (type == StaffType.Cooking &&
            _sushiManager.Cooker != null)
        {
            // 요리 직원의 기존 강화 설정을 조회합니다.
            CookerStaffUpgradeData data =
                _sushiManager.Cooker.UpgradeData;

            // 해금된 직원의 유효한 다음 단계 비용을 반환합니다.
            return data != null &&
                _sushiManager.CurrentPlayerLevel >= _sushiManager.Cooker.UnlockPlayerLevel &&
                _sushiManager.Cooker.TryGetCookTimeMultiplier(CookingLevel + 1, out _) &&
                data.TryGetUpgradeCost(CookingLevel, out cost);
        }

        // 대상 직원이 없거나 지원하지 않는 종류이면 실패합니다.
        return false;
    }

    /// <summary>
    /// 직원의 해금, 레벨, 골드와 영업 상태가 강화 조건을 만족하는지 반환합니다.
    /// </summary>
    public bool CanUpgradeStaff(StaffType type)
    {
        // 해금 여부와 다음 단계 비용을 확인합니다.
        if (!TryGetStaffUpgradeCost(type, out float cost))
            return false;

        // 영업 상태와 골드 조건을 검사합니다.
        return CanPayUpgradeCost(cost);
    }

    /// <summary>
    /// 골드를 지불하고 선택한 직원의 능력치와 진행 정보를 강화합니다.
    /// </summary>
    public bool TryUpgrade(StaffType type)
    {
        // 현재 직원 강화 조건을 다시 검사합니다.
        if (!CanUpgradeStaff(type))
            return false;

        // 실제 지불할 비용을 조회합니다.
        if (!TryGetStaffUpgradeCost(type, out float cost))
            return false;

        // 강화 전 직원 레벨과 실제 데이터 참조를 확보합니다.
        int serving = ServingLevel;
        int cooking = CookingLevel;
        Inventory inventory = _sushiManager.Inventory;
        SushiShopProgress progress = _progress;

        // 인벤토리 이벤트를 통한 중복 강화를 차단합니다.
        _isUpgrading = true;

        try
        {
            // 기존 인벤토리 API로 직원 강화 비용을 지불합니다.
            if (!inventory.TrySpendGold(cost))
                return false;

            // 선택한 직원만 한 단계 올린 목표 레벨을 계산합니다.
            int nextServing =
                serving + (type == StaffType.Serving ? 1 : 0);
            int nextCooking =
                cooking + (type == StaffType.Cooking ? 1 : 0);

            // 실제 직원 능력치에 목표 레벨을 적용합니다.
            if (!_sushiManager.TryApplyStaffLevels(
                    nextServing, nextCooking))
            {
                // 적용 실패 시 기존 직원 능력치로 복구합니다.
                if (!_sushiManager.TryApplyStaffLevels(serving, cooking))
                    Debug.LogError("[직원 강화] 기존 능력치 복구 실패.", this);

                // 지불한 골드를 반환합니다.
                if (!inventory.TryAddGold(cost))
                    Debug.LogError("[직원 강화] 골드 반환 실패.", this);

                // 진행 정보의 레벨은 올리지 않습니다.
                return false;
            }

            // 선택한 직원의 실제 강화 레벨을 진행 정보에 기록합니다.
            progress.SetStaffLevel(
                type,
                type == StaffType.Serving ? nextServing : nextCooking);

            // 인스펙터 표시 레벨을 실제 진행 정보와 동기화합니다.
            BindProgress(progress);

            // 직원 강화에 성공했습니다.
            return true;
        }
        finally
        {
            // 성공 여부와 관계없이 중복 실행 차단을 해제합니다.
            _isUpgrading = false;

            // 직원 강화 처리 후의 최종 상태를 표시하도록 알립니다.
            UpgradeStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// ContextMenu에서 실제 유료 서빙 직원 강화를 실행합니다.
    /// </summary>
    [ContextMenu("테스트/서빙 직원 강화레벨 1 증가")]
    void TestUpgradeServing()
    {
        // 실제 강화 API로 서빙 직원을 강화합니다.
        bool success = TryUpgrade(StaffType.Serving);

        // 실행 결과와 현재 레벨을 출력합니다.
        Debug.Log(
            $"서빙 직원 강화: {success}, 현재 {ServingLevel}레벨",
            this);
    }

    /// <summary>
    /// ContextMenu에서 실제 유료 요리 직원 강화를 실행합니다.
    /// </summary>
    [ContextMenu("테스트/요리 직원 강화레벨 1 증가")]
    void TestUpgradeCooking()
    {
        // 실제 강화 API로 요리 직원을 강화합니다.
        bool success = TryUpgrade(StaffType.Cooking);

        // 실행 결과와 현재 레벨을 출력합니다.
        Debug.Log(
            $"요리 직원 강화: {success}, 현재 {CookingLevel}레벨",
            this);
    }
}
