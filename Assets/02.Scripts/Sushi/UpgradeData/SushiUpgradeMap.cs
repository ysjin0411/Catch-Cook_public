using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 요리별 강화 데이터 목록을 보관합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "SushiUpgradeMap",
    menuName = "Game/Sushi/Sushi Upgrade Map")]
public class SushiUpgradeMap : ScriptableObject
{
    // 요리별 강화 데이터 에셋을 등록합니다.
    [SerializeField] SushiUpgradeData[] _upgradeDatas =
        Array.Empty<SushiUpgradeData>();

    // 등록된 목록을 읽기 전용으로 제공합니다.
    public IReadOnlyList<SushiUpgradeData> Datas =>
        Array.AsReadOnly(
            _upgradeDatas ?? Array.Empty<SushiUpgradeData>());
}
