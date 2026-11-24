using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "CookerStaffUpgradeData", menuName = "Game/Sushi/Cooker Staff Upgrade Data")]
public class CookerStaffUpgradeData : ScriptableObject
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

    // 요리 직원 데이터를 구분할 표시 이름입니다.
    [SerializeField] string _staffName = "요리 직원";
    // 요리 직원이 해금되는 플레이어 레벨입니다.
    [SerializeField, Min(1)] int _unlockPlayerLevel = 1;
    // 추가 조리 가속 Active가 해금되는 강화 단계입니다.
    [FormerlySerializedAs("_passiveUnlockLevel")]
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
    // Active 해금 후 기본 강화로 줄인 조리시간에 추가로 곱하는 비율입니다.
    [FormerlySerializedAs("_passiveTimeMultiplier")]
    [SerializeField, Range(0.05f, 1f)] float _activeTimeMultiplier = 0.5f;
    // 0번이 1레벨인 기본 조리시간 감소율입니다.
    [SerializeField] float[] _cookTimeReductions = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
    // 0번은 1→2레벨이며 해금 시 기본 1레벨에는 비용이 없습니다.
    [SerializeField] float[] _upgradeCosts = { 150f, 300f, 600f, 1200f };

    public string StaffName => _staffName;
    public StaffType Type => StaffType.Cooking;
    public int UnlockPlayerLevel => _unlockPlayerLevel;
    public int ActiveUnlockLevel => _activeUnlockLevel;
    /// <summary>추가 가속 효과의 속도 배율을 UI에 표시합니다.</summary>
    public float ActiveSpeedPercent => !float.IsNaN(_activeTimeMultiplier) && _activeTimeMultiplier >= 0.05f && _activeTimeMultiplier <= 1f ? 100f / _activeTimeMultiplier : 0f;
    /// <summary>조리 직원의 패시브 스킬 아이콘입니다.</summary>
    public Sprite PassiveIcon => _passiveIcon;
    /// <summary>조리 직원의 패시브 설명입니다.</summary>
    public string PassiveDescription => _passiveDescription;
    /// <summary>조리 직원의 Active 스킬 아이콘입니다.</summary>
    public Sprite ActiveIcon => _activeIcon;
    /// <summary>조리 직원의 Active 스킬 설명입니다.</summary>
    public string ActiveDescription => _activeDescription;
    public int MaxLevel => _cookTimeReductions == null ? 0 : _cookTimeReductions.Length;

    /// <summary>기본 Passive 강화와 Active를 반영한 유효한 조리시간 배율을 반환합니다.</summary>
    public bool TryGetCookTimeMultiplier(int level, out float multiplier)
    {
        // 등록되지 않은 단계와 유효하지 않은 감소율은 적용하지 않습니다.
        multiplier = 1f;
        if (_cookTimeReductions == null || level < 1 || level > _cookTimeReductions.Length)
            return false;
        float reduction = _cookTimeReductions[level - 1];
        if (float.IsNaN(reduction) || reduction < 0f || reduction >= 1f) return false;
        // 두 효과를 더하지 않고 곱하여 조리시간이 음수가 되지 않게 합니다.
        multiplier = 1f - reduction;
        if (level >= Mathf.Max(1, _activeUnlockLevel))
        {
            if (float.IsNaN(_activeTimeMultiplier) || _activeTimeMultiplier < 0.05f ||
                _activeTimeMultiplier > 1f) return false;
            multiplier *= _activeTimeMultiplier;
        }
        return true;
    }

    /// <summary>현재 레벨에서 다음 단계 강화에 필요한 골드를 검증합니다.</summary>
    public bool TryGetUpgradeCost(int level, out float cost)
    {
        // 最大レベルには次段階の費用を要求しません。
        cost = 0f;
        if (level < 1 || level >= MaxLevel || _upgradeCosts == null ||
            _upgradeCosts.Length != MaxLevel - 1 || !TryGetCookTimeMultiplier(level + 1, out _)) return false;
        cost = _upgradeCosts[level - 1];
        return cost > 0f && !float.IsInfinity(cost);
    }
}
