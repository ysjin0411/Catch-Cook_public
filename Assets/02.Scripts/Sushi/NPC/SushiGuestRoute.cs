using System.Collections.Generic;
using UnityEngine;

public class SushiGuestRoute : MonoBehaviour
{
    [Header("3층으로 이동하는 경사로")]
    [SerializeField] Transform _stairBottomPoint;
    [SerializeField] Transform _stairTopPoint;

    /// <summary>좌석이 있는 손님의 입장 및 퇴장 경로를 구성합니다.</summary>
    public bool TryBuildRoute(Chair chair, Transform leftPoint, bool leaving, List<Transform> route)
    {
        // 기존 호출자는 좌석 정보를 통해 같은 층별 경로를 사용합니다.
        if (chair == null || !chair.IsValid) return false;
        return TryBuildAreaRoute(chair.Area, leaving ? leftPoint : chair.ArrivePos, leaving, route);
    }

    /// <summary>좌석을 예약하지 못한 손님도 층별 경사로를 거쳐 대기하거나 퇴장합니다.</summary>
    public bool TryBuildAreaRoute(ChairArea area, Transform destination, bool leaving, List<Transform> route)
    {
        // 모든 경로는 유효한 최종 목적지와 저장 목록을 필요로 합니다.
        if (route == null) return false;
        route.Clear();
        if (destination == null) return false;
        if (area == ChairArea.Front)
        {
            if (_stairBottomPoint == null || _stairTopPoint == null) return false;
            // 입장은 아래에서 위로, 퇴장은 위에서 아래로 이동합니다.
            route.Add(leaving ? _stairTopPoint : _stairBottomPoint);
            route.Add(leaving ? _stairBottomPoint : _stairTopPoint);
        }
        route.Add(destination);
        return true;
    }
}
