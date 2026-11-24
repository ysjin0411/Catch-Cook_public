using UnityEngine;

/// <summary>조리 직원의 기본 조리시간 배율과 강화 데이터를 정의합니다.</summary>
[CreateAssetMenu(fileName = "CookerStaffData", menuName = "Game/Sushi/Staff/Cooker Staff Data")]
public sealed class CookerStaffData : StaffData
{
    // 음식의 원본 조리시간에 적용할 직원 고유 배율입니다.
    [SerializeField, Min(0.01f)] float _baseCookTimeMultiplier = 1f;
    // 레벨별 조리시간 감소율, 비용과 패시브를 제공하는 데이터입니다.
    [SerializeField] CookerStaffUpgradeData _upgradeData;

    /// <summary>조리 업무를 담당합니다.</summary>
    public override StaffType Type => StaffType.Cooking;
    /// <summary>강화 효과를 적용하기 전의 기본 조리시간 배율입니다.</summary>
    public float BaseCookTimeMultiplier => _baseCookTimeMultiplier;
    /// <summary>이 직원에게 적용할 강화 데이터입니다.</summary>
    public CookerStaffUpgradeData UpgradeData => _upgradeData;
    /// <summary>강화 데이터에 등록된 최대 레벨입니다.</summary>
    public int MaxLevel => _upgradeData != null ? _upgradeData.MaxLevel : 0;

    /// <summary>직원 고유 배율과 강화 및 패시브를 반영한 최종 조리시간 배율을 반환합니다.</summary>
    public bool TryGetCookTimeMultiplier(int level, out float multiplier)
    {
        // 조회 실패 시 사용할 기본값입니다.
        multiplier = 1f;
        // 직원의 기본 배율이 유효한 양수인지 검사합니다.
        if (float.IsNaN(_baseCookTimeMultiplier) || float.IsInfinity(_baseCookTimeMultiplier) ||
            _baseCookTimeMultiplier <= 0f) return false;
        // 해당 레벨의 강화 및 패시브 배율을 조회합니다.
        if (_upgradeData == null ||
            !_upgradeData.TryGetCookTimeMultiplier(level, out float upgradeMultiplier)) return false;
        // 직원 고유 배율과 강화 효과를 곱합니다.
        float result = _baseCookTimeMultiplier * upgradeMultiplier;
        // 유효한 양수 조리시간 배율만 적용합니다.
        if (float.IsNaN(result) || float.IsInfinity(result) || result <= 0f) return false;
        // 검증된 최종 배율을 반환합니다.
        multiplier = result;
        return true;
    }
}
