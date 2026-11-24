using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 요리 하나의 단계별 강화 비용과 레벨별 판매 골드 배율을 보관합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "SushiUpgradeData",
    menuName = "Game/Sushi/Sushi Upgrade Data")]
public class SushiUpgradeData : ScriptableObject
{
    [Header("강화 대상 요리")]
    // 이 강화 설정을 적용할 요리입니다.
    [SerializeField] Sushi _targetSushi;

    // 외부에서 강화 대상 요리를 확인할 수 있습니다.
    public Sushi TargetSushi => _targetSushi;

    // 기존 진행 정보 저장 구조와 동일한 최대 레벨입니다.
    public const int MaxLevel = 5;

    /// <summary>
    /// 요리 강화에 필요한 아이템과 소모 수량을 보관합니다.
    /// </summary>
    [Serializable]
    public sealed class MaterialRequirement
    {
        // 요리 강화에 사용할 아이템입니다.
        [SerializeField] ItemData _item;

        // 해당 단계에서 소모할 아이템 수량입니다.
        [SerializeField, Min(1)] int _amount = 1;

        // 강화에 필요한 아이템을 외부에 제공합니다.
        public ItemData Item => _item;
        // 강화에 필요한 수량을 외부에 제공합니다.
        public int Amount => _amount;
    }

    [Serializable]
    public sealed class UpgradeStep
    {
        // 해당 강화 단계에서 차감할 골드입니다.
        [SerializeField, Min(1f)] float _goldCost = 100f;

        // 해당 강화 단계에서 차감할 아이템과 수량입니다.
        [SerializeField]
        MaterialRequirement[] _materials =
            Array.Empty<MaterialRequirement>();

        public float GoldCost => _goldCost;

        public IReadOnlyList<MaterialRequirement> Materials =>
            Array.AsReadOnly(
                _materials ?? Array.Empty<MaterialRequirement>());

        /// <summary>
        /// 비용 설정을 검증하고 같은 재료의 필요 수량을 합산합니다.
        /// </summary>
        public bool TryGetRequirements(
            out Dictionary<ItemData, int> requirements)
        {
            // 인벤토리 API에 전달할 재료별 필요 수량입니다.
            requirements = new Dictionary<ItemData, int>();

            // 골드 비용은 유효한 양수여야 합니다.
            if (!(_goldCost > 0f) || float.IsInfinity(_goldCost))
                return false;

            // null 배열은 설정 오류로 처리합니다.
            if (_materials == null)
                return false;

            // 등록된 모든 강화 재료를 검사합니다.
            foreach (MaterialRequirement requirement in _materials)
            {
                // 미지정 재료와 잘못된 수량은 거부합니다.
                if (requirement == null ||
                    requirement.Item == null ||
                    requirement.Amount <= 0)
                    return false;

                // 동일한 재료가 앞서 등록되었는지 확인합니다.
                requirements.TryGetValue(
                    requirement.Item, out int previousAmount);

                // 합산 수량이 정수 범위를 넘지 않도록 검사합니다.
                if (previousAmount > int.MaxValue - requirement.Amount)
                    return false;

                // 같은 재료가 여러 번 등록되면 수량을 합산합니다.
                requirements[requirement.Item] =
                    previousAmount + requirement.Amount;
            }

            // 빈 재료 목록은 골드만 사용하는 강화로 허용합니다.
            return true;
        }
    }

    [Header("강화 단계: 0번은 1→2, 3번은 4→5")]
    [SerializeField]
    UpgradeStep[] _steps =
    {
        new UpgradeStep(),
        new UpgradeStep(),
        new UpgradeStep(),
        new UpgradeStep()
    };

    [Header("레벨별 판매 골드 배율: 0번은 1레벨")]
    [SerializeField]
    float[] _goldMultipliers =
    {
        1f, 1.05f, 1.10f, 1.15f, 1.20f
    };

    /// <summary>
    /// 현재 레벨에서 다음 레벨로 강화할 때 사용할 단계를 조회합니다.
    /// </summary>
    public bool TryGetStep(int currentLevel, out UpgradeStep step)
    {
        // 조회 실패 시 사용할 기본값입니다.
        step = null;

        // 최대 레벨과 잘못된 단계 배열을 거부합니다.
        if (currentLevel < 1 ||
            currentLevel >= MaxLevel ||
            _steps == null ||
            _steps.Length != MaxLevel - 1)
            return false;

        // 현재 레벨에 대응하는 강화 단계를 조회합니다.
        UpgradeStep candidate = _steps[currentLevel - 1];

        // 단계와 비용 설정이 유효한지 검사합니다.
        if (candidate == null ||
            !candidate.TryGetRequirements(out _))
            return false;

        // 검증한 강화 단계를 반환합니다.
        step = candidate;
        return true;
    }

    /// <summary>
    /// 다음 강화 단계의 골드 비용과 판매 골드 배율 설정을 검증합니다.
    /// </summary>
    public bool TryGetUpgradeCost(int level, out float cost)
    {
        // 조회 실패 시 사용할 기본값입니다.
        cost = 0f;

        // 비용 설정과 다음 레벨의 판매 골드 배율을 확인합니다.
        if (!TryGetStep(level, out UpgradeStep step) ||
            !TryGetGoldMultiplier(level + 1, out _))
            return false;

        // 해당 강화 단계의 골드 비용을 반환합니다.
        cost = step.GoldCost;
        return true;
    }

    /// <summary>
    /// 해당 레벨의 판매 골드 배율을 검증하고 반환합니다.
    /// </summary>
    public bool TryGetGoldMultiplier(
        int level,
        out float multiplier)
    {
        // 조회 실패 시 사용할 기본 배율입니다.
        multiplier = 1f;

        // 레벨과 배율 배열의 크기를 확인합니다.
        if (level < 1 ||
            level > MaxLevel ||
            _goldMultipliers == null ||
            _goldMultipliers.Length != MaxLevel)
            return false;

        // 해당 레벨의 판매 골드 배율을 조회합니다.
        multiplier = _goldMultipliers[level - 1];

        // NaN, 무한대와 1보다 작은 배율을 거부합니다.
        return multiplier >= 1f && !float.IsInfinity(multiplier);
    }

    /// <summary>
    /// 판매 골드에 강화 배율을 적용하고 경험치는 요리 원본 값을 반환합니다.
    /// </summary>
    public bool TryGetReward(
        Sushi sushi,
        int level,
        out float gold,
        out int experience)
    {
        // 계산 실패 시 사용할 기본값입니다.
        gold = 0f;
        experience = 0;

        // 음식 여부, 기본 보상과 판매 골드 배율 설정을 확인합니다.
        if (sushi == null ||
            sushi.Type != MenuType.Food ||
            !(sushi.Gold >= 0f) ||
            float.IsInfinity(sushi.Gold) ||
            sushi.PlayerExperience < 0 ||
            !TryGetGoldMultiplier(level, out float goldMultiplier))
            return false;

        // 판매 골드 계산 결과가 반환 자료형의 범위를 넘는지 확인합니다.
        double scaledGold = (double)sushi.Gold * goldMultiplier;

        // 표현할 수 없는 보상은 거부합니다.
        if (scaledGold > float.MaxValue)
            return false;

        // 최종 판매 골드를 저장합니다.
        gold = (float)scaledGold;

        // 경험치는 강화 레벨과 관계없이 요리 원본 값을 사용합니다.
        experience = sushi.PlayerExperience;

        // 보상 계산에 성공했습니다.
        return true;
    }
}
