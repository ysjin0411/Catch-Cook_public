using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SushiMap", menuName = "Game/Sushi/Sushi Map")]
public class SushiMap : ScriptableObject
{
    [Header("전체 스시 목록")]
    [SerializeField] Sushi[] _sushis;

    [Header("기본 메뉴 주문 그룹")]
    [SerializeField] SushiOrderGroup[] _orderGroups;

    public IReadOnlyList<SushiOrderGroup> OrderGroups =>
        Array.AsReadOnly(_orderGroups ?? Array.Empty<SushiOrderGroup>());

    public IReadOnlyList<Sushi> Sushis =>
        Array.AsReadOnly(_sushis ?? Array.Empty<Sushi>());

    /// <summary>
    /// ID가 일치하는 스시를 반환하며 중복 ID는 조회 실패로 처리합니다.
    /// </summary>
    public Sushi GetSushi(int id)
    {
        // 등록된 스시가 없다면 조회에 실패합니다.
        if (_sushis == null) return null;

        // 조회한 스시를 임시로 보관합니다.
        Sushi result = null;

        // 전체 스시에서 해당 ID를 찾습니다.
        foreach (Sushi sushi in _sushis)
        {
            // 비어 있는 항목과 다른 ID는 제외합니다.
            if (sushi == null || sushi.Id != id) continue;

            // 같은 ID가 여러 번 등록되었다면 명확한 조회가 불가능합니다.
            if (result != null) return null;

            // ID가 일치하는 스시를 저장합니다.
            result = sushi;
        }

        // 유일하게 일치하는 스시 또는 조회 실패를 반환합니다.
        return result;
    }

    /// <summary>
    /// 에디터에서 전체 목록에 중복된 ID가 있는지 확인합니다.
    /// </summary>
    void OnValidate()
    {
        // 목록이 없다면 검사를 생략합니다.
        if (_sushis == null) return;

        // 검사한 ID를 보관합니다.
        HashSet<int> ids = new();

        // 전체 스시의 ID를 확인합니다.
        foreach (Sushi sushi in _sushis)
        {
            // 아직 지정하지 않은 항목은 제외합니다.
            if (sushi == null) continue;

            // 이미 등록된 ID라면 중복 오류를 표시합니다.
            if (!ids.Add(sushi.Id))
            {
                // 문제가 있는 Map과 ID를 에디터에 알립니다.
                Debug.LogError($"SushiMap에 중복된 스시 ID가 있습니다: {sushi.Id}", this);
            }
        }
    }
}

/// <summary>
/// 주문 가중치와 해당 그룹에 포함된 메뉴를 보관합니다.
/// </summary>
[Serializable]
public class SushiOrderGroup
{
    [SerializeField] string _groupName;

    [Min(0)]
    [SerializeField] int _weight = 1;

    [SerializeField] Sushi[] _menus;

    public string GroupName => _groupName;
    public int Weight => _weight;
    public IReadOnlyList<Sushi> Menus =>
        Array.AsReadOnly(_menus ?? Array.Empty<Sushi>());
}
