using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>플레이어 레벨별 손님 방문 빈도와 메뉴 종류 수 해금 조건을 관리합니다.</summary>
[CreateAssetMenu(fileName = "SushiSpawnDifficulty", menuName = "Game/Sushi/Spawn Difficulty")]
public sealed class SushiSpawnDifficultyData : ScriptableObject
{
    [Serializable]
    public sealed class LevelSetting
    {
        // 해당 레벨 이상에서 적용할 난이도입니다.
        [Min(1)] public int MinimumPlayerLevel = 1;

        // 값이 클수록 무작위로 뽑은 손님 입장 간격이 짧아집니다.
        [Min(0.01f)] public float SpawnRateMultiplier = 1f;
    }

    [Header("플레이어 레벨별 스폰 배율")]
    [FormerlySerializedAs("_grades")]
    [SerializeField] LevelSetting[] _levels = Array.Empty<LevelSetting>();

    [Header("오늘의 메뉴 해금 조건")]
    // 네 번째 메뉴 슬롯을 해금할 플레이어 레벨입니다.
    [FormerlySerializedAs("_fourthMenuUnlockGrade")]
    [SerializeField, Min(2)] int _fourthMenuUnlockLevel = 3;
    // 다섯 번째 메뉴 슬롯을 해금할 플레이어 레벨입니다.
    [FormerlySerializedAs("_fifthMenuUnlockGrade")]
    [SerializeField, Min(2)] int _fifthMenuUnlockLevel = 5;

    // 일반 초밥을 최소 한 종류는 선택해야 합니다.
    public const int MinimumTodayMenuCount = 1;
    // 첫 플레이어 레벨에서 선택할 수 있는 최대 종류 수입니다.
    const int InitialTodayMenuLimit = 3;
    // 모든 슬롯을 해금한 뒤 선택할 수 있는 최대 종류 수입니다.
    const int MaximumTodayMenuLimit = 5;

    /// <summary>현재 레벨 이하에서 기준 레벨이 가장 높은 유효 설정을 반환합니다.</summary>
    bool TryGetLevelSetting(int playerLevel, out LevelSetting setting)
    {
        // 적용 가능한 설정이 없을 때 사용할 기본 상태입니다.
        setting = null;
        int selectedLevel = 0;

        // 설정 배열이 없으면 기본값을 사용하도록 실패를 반환합니다.
        if (_levels == null) return false;

        // 최소 플레이어 레벨을 1로 보정합니다.
        int currentLevel = Mathf.Max(1, playerLevel);

        // 배열 순서와 관계없이 현재 레벨 이하의 최고 단계를 찾습니다.
        foreach (LevelSetting level in _levels)
        {
            // 기준 레벨과 배율이 유효하지 않은 설정은 제외합니다.
            if (level == null || level.MinimumPlayerLevel < 1 ||
                level.MinimumPlayerLevel > currentLevel || level.MinimumPlayerLevel <= selectedLevel ||
                !(level.SpawnRateMultiplier >= 0.01f) || float.IsInfinity(level.SpawnRateMultiplier)) continue;

            // 현재까지 찾은 가장 높은 단계의 값을 보관합니다.
            selectedLevel = level.MinimumPlayerLevel;
            setting = level;
        }

        // 현재 레벨에 적용한 난이도 설정이 있는지 반환합니다.
        return setting != null;
    }

    /// <summary>현재 플레이어 레벨에 적용할 손님 방문 빈도 배율을 반환합니다.</summary>
    public bool TryGetSpawnRateMultiplier(int playerLevel, out float spawnRateMultiplier)
    {
        // 설정이 없으면 기존처럼 기본 배율 1을 사용합니다.
        spawnRateMultiplier = 1f;

        // 레벨에 적용할 설정이 없으면 기본값을 유지합니다.
        if (!TryGetLevelSetting(playerLevel, out LevelSetting setting)) return false;

        // 선택한 레벨 설정의 배율을 반환합니다.
        spawnRateMultiplier = setting.SpawnRateMultiplier;
        return true;
    }

    /// <summary>플레이어 레벨에 따라 선택할 수 있는 오늘의 메뉴 최대 종류 수를 반환합니다.</summary>
    public int GetTodayMenuLimit(int playerLevel)
    {
        // 최초에는 일반 초밥을 최대 세 종류까지 선택할 수 있습니다.
        int limit = InitialTodayMenuLimit;

        // 런타임에도 네 번째 메뉴는 최소 2레벨부터 해금되게 보정합니다.
        int fourthLevel = Mathf.Max(2, _fourthMenuUnlockLevel);

        // 다섯 번째 메뉴는 네 번째 메뉴보다 높은 레벨에서 해금되게 보정합니다.
        int fifthLevel = Mathf.Max(fourthLevel + 1, _fifthMenuUnlockLevel);

        // 네 번째 슬롯의 해금 조건을 만족하면 최대 종류 수를 늘립니다.
        if (playerLevel >= fourthLevel) limit++;

        // 다섯 번째 슬롯의 해금 조건을 만족하면 최대 종류 수를 늘립니다.
        if (playerLevel >= fifthLevel) limit++;

        // 최대 다섯 종류까지만 선택할 수 있게 반환합니다.
        return Mathf.Min(limit, MaximumTodayMenuLimit);
    }

    /// <summary>메뉴 슬롯이 순차적으로 해금되도록 인스펙터 설정값을 보정합니다.</summary>
    void OnValidate()
    {
        // 1레벨에서는 기본 세 종류를 유지합니다.
        _fourthMenuUnlockLevel = Mathf.Max(2, _fourthMenuUnlockLevel);

        // 다섯 번째 슬롯은 네 번째 슬롯보다 높은 레벨에서 해금합니다.
        _fifthMenuUnlockLevel = Mathf.Max(_fourthMenuUnlockLevel + 1, _fifthMenuUnlockLevel);
    }
}
