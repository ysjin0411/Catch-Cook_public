using System.Collections.Generic;
using UnityEngine;

/// <summary>오늘의 메뉴 확정 시 사용하는 재료의 일괄 차감을 담당합니다.</summary>
public static class SushiIngredientStock
{
    /// <summary>모든 재료를 검사하여 차감하고 도중에 실패하면 앞서 차감한 수량을 복구합니다.</summary>
    public static bool TryConsume(Inventory inventory, IReadOnlyDictionary<ItemData, int> requirements)
    {
        // 재료가 없는 요청은 차감할 필요가 없습니다.
        if (requirements == null) return false;
        if (requirements.Count == 0) return true;
        if (inventory == null) return false;

        // 모든 재료가 충분한지 먼저 검사하여 부분 차감을 줄입니다.
        foreach (var entry in requirements)
            if (entry.Key == null || entry.Value <= 0 || inventory.GetCount(entry.Key) < entry.Value)
                return false;

        // 외부 인벤토리 이벤트로 차감이 실패할 경우 복구할 항목을 기록합니다.
        List<KeyValuePair<ItemData, int>> removed = new();
        foreach (var entry in requirements)
        {
            // 한 항목이라도 실패하면 이번 요청에서 차감한 재료만 반환합니다.
            if (!inventory.TryRemove(entry.Key, entry.Value))
            {
                // 복구 실패는 데이터 변경 경로를 확인할 수 있도록 명시적으로 알립니다.
                foreach (var rollback in removed)
                    if (!inventory.TryAdd(rollback.Key, rollback.Value))
                        Debug.LogError("[메뉴 확정 재료 복구 실패] 인벤토리 변경 이벤트를 확인하세요.");
                return false;
            }

            // 정상적으로 차감된 항목을 복구 목록에 보관합니다.
            removed.Add(entry);
        }

        // 전체 재료 차감에 성공했습니다.
        return true;
    }
}
