using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "ServingStaffUpgradeData", menuName = "Game/Sushi/Serving Staff Upgrade Data")]
public class ServingStaffUpgradeData : ScriptableObject
{
    // 0번이 1레벨이며 각 단계의 의상과 효과를 독립적으로 지정합니다.
    [Header("레벨별 파티클 및 의상")]
    [SerializeField] StaffLevelAppearance[] _levelAppearances = new StaffLevelAppearance[5];

    /// <summary>해당 강화 단계의 외형을 반환하며 미등록 단계는 기본 외형을 사용합니다.</summary>
    public StaffLevelAppearance GetAppearance(int level)
    {
        // 능력치에서 지원하지 않거나 외형이 등록되지 않은 단계는 거부합니다.
        if (level < 1 || level > MaxLevel || _levelAppearances == null ||
            level > _levelAppearances.Length) return null;
        // 1부터 시작하는 레벨을 배열 인덱스로 변환합니다.
        return _levelAppearances[level - 1];
    }

    // 서빙 직원 데이터를 구분할 표시 이름입니다.
    [SerializeField] string _staffName = "서빙 직원";
    // 직원을 사용할 수 있게 되는 플레이어 레벨입니다.
    [SerializeField, Min(1)] int _unlockPlayerLevel = 3;
    // 음료 제공 Active가 해금되는 강화 단계입니다.
    [FormerlySerializedAs("_drinkServingLevel")]
    [SerializeField, Min(1)] int _activeUnlockLevel = 5;
    // 직원 화면에 표시할 패시브 스킬 아이콘입니다.
    [SerializeField] Sprite _passiveIcon;
    // Inspector에서 한 줄로 입력하는 패시브 설명입니다.
    [SerializeField] string _passiveDescription = string.Empty;
    // 직원 화면에 표시할 Active 스킬 아이콘입니다.
    [FormerlySerializedAs("_specialIcon")]
    [SerializeField] Sprite _activeIcon;
    // Inspector에서 한 줄로 입력하는 Active 스킬 설명입니다.
    [FormerlySerializedAs("_specialDescription")]
    [SerializeField] string _activeDescription = string.Empty;
    // 0번이 1레벨이며, 1레벨을 0으로 설정하면 각 값이 기본 속도에 더할 누적 증가량입니다.
    [SerializeField] float[] _moveSpeeds = { 0f, 0.2f, 0.4f, 0.6f, 0.8f };
    // 0번은 1→2레벨이며 해금 시 기본 1레벨에는 비용이 없습니다.
    [SerializeField] float[] _upgradeCosts = { 100f, 200f, 400f, 800f };

    public string StaffName => _staffName;
    public StaffType Type => StaffType.Serving;
    public int UnlockPlayerLevel => _unlockPlayerLevel;
    public int ActiveUnlockLevel => _activeUnlockLevel;
    /// <summary>서빙 직원의 패시브 스킬 아이콘입니다.</summary>
    public Sprite PassiveIcon => _passiveIcon;
    /// <summary>서빙 직원의 패시브 설명입니다.</summary>
    public string PassiveDescription => _passiveDescription;
    /// <summary>서빙 직원의 Active 스킬 아이콘입니다.</summary>
    public Sprite ActiveIcon => _activeIcon;
    /// <summary>서빙 직원의 Active 스킬 설명입니다.</summary>
    public string ActiveDescription => _activeDescription;
    public int MaxLevel => _moveSpeeds == null ? 0 : _moveSpeeds.Length;

    /// <summary>해당 강화 단계의 이동속도 값을 조회하며 0 이상의 유한한 값만 허용합니다.</summary>
    public bool TryGetMoveSpeed(int level, out float speed)
    {
        // 조회 실패 시 사용할 기본값입니다.
        speed = 0f;
        // 속도 배열이 없거나 요청한 레벨이 범위를 벗어나면 실패합니다.
        if (_moveSpeeds == null || level < 1 || level > _moveSpeeds.Length) return false;
        // 1부터 시작하는 레벨을 배열 인덱스로 변환하여 조회합니다.
        float value = _moveSpeeds[level - 1];
        // 음수 또는 정상적인 수치가 아닌 값은 거부하고 0은 허용합니다.
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return false;
        // 검증된 값을 반환합니다.
        speed = value;
        return true;
    }

    /// <summary>기존 속도표를 변경하지 않고 1레벨 대비 이동속도 증가량을 반환합니다.</summary>
    public bool TryGetMoveSpeedBonus(int level, out float bonus)
    {
        // 조회 실패 시 사용할 기본값입니다.
        bonus = 0f;
        // 해당 레벨과 기준이 되는 1레벨의 속도를 검증합니다.
        if (!TryGetMoveSpeed(level, out float speed) || !TryGetMoveSpeed(1, out float baseSpeed))
            return false;
        // 기존 강화 표에서 직원에게 더할 누적 증가량을 계산합니다.
        float result = speed - baseSpeed;
        // 기본 속도보다 감소하는 강화 표는 적용하지 않습니다.
        if (result < 0f) return false;
        // 검증된 강화 증가량을 반환합니다.
        bonus = result;
        return true;
    }

    /// <summary>현재 강화 단계에서 음료 제공 Active가 해금되었는지 확인합니다.</summary>
    public bool CanServeDrink(int level)
    {
        // 자동 서빙이 시작되는 1레벨 이상에서만 Active를 허용합니다.
        return level >= Mathf.Max(1, _activeUnlockLevel) && TryGetMoveSpeed(level, out _);
    }

    /// <summary>현재 레벨에서 다음 단계로 강화하는 골드 비용을 검증합니다.</summary>
    public bool TryGetUpgradeCost(int level, out float cost)
    {
        // 해금된 기본 1레벨부터 최대 레벨 직전까지만 유료 강화할 수 있습니다.
        cost = 0f;
        if (level < 1 || level >= MaxLevel || _upgradeCosts == null ||
            _upgradeCosts.Length != MaxLevel - 1 || !TryGetMoveSpeed(level + 1, out _)) return false;
        cost = _upgradeCosts[level - 1];
        return cost > 0f && !float.IsInfinity(cost);
    }
}
