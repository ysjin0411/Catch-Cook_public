using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 스포너의 반환 이벤트를 예약하고 만료된 자리만 재생성합니다.
/// </summary>
public sealed class FishRespawnScheduler
{
    // 반환된 물고기 객체 대신 패턴과 자리 번호만 보관합니다.
    private struct Reservation
    {
        public FishSpawnPattern Owner;
        public int SlotIndex;
        public double DueTime;
    }

    // 가장 빠른 예약을 끝에 두어 매 갱신에 한 항목만 확인합니다.
    private readonly List<Reservation> _reservations = new List<Reservation>();
    private double _clock;

    /// <summary>
    /// 동일한 패턴의 같은 자리를 중복 예약하지 않고 재생성 시각을 등록합니다.
    /// </summary>
    public void Schedule(FishSpawnPattern owner, int slotIndex, float delay)
    {
        // 유효하지 않은 소유자와 대기 시간은 등록하지 않습니다.
        if (owner == null || slotIndex < 0 || float.IsNaN(delay) || float.IsInfinity(delay))
            return;

        // 중복 확인은 물고기 반환 시에만 수행합니다.
        for (int i = 0; i < _reservations.Count; i++)
        {
            if (ReferenceEquals(_reservations[i].Owner, owner)
                && _reservations[i].SlotIndex == slotIndex)
                return;
        }

        // 활성 상태로 진행된 스포너 시간에 지연을 더합니다.
        Reservation reservation = new Reservation
        {
            Owner = owner,
            SlotIndex = slotIndex,
            DueTime = _clock + Mathf.Max(0f, delay)
        };

        // 동일 시각의 예약은 등록된 순서대로 꺼내도록 삽입합니다.
        int index = _reservations.Count;
        while (index > 0 && _reservations[index - 1].DueTime <= reservation.DueTime)
            index--;
        _reservations.Insert(index, reservation);
    }

    /// <summary>
    /// 시간이 된 예약만 실행하며 비활성화 중에는 이 메서드를 호출하지 않습니다.
    /// </summary>
    public void Advance(float deltaTime)
    {
        // 잘못된 시간으로 예약 시계가 손상되지 않게 합니다.
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;
        _clock += deltaTime;

        // 가장 빠른 예약이 아직 대기 중이면 전체 처리를 끝냅니다.
        while (_reservations.Count > 0)
        {
            int last = _reservations.Count - 1;
            Reservation reservation = _reservations[last];
            if (reservation.DueTime > _clock)
                break;

            // 콜백에서 다른 예약을 취소할 수 있도록 현재 예약을 먼저 제거합니다.
            _reservations.RemoveAt(last);
            if (reservation.Owner != null)
                reservation.Owner.RespawnSlot(reservation.SlotIndex);
        }
    }

    /// <summary>
    /// 종료한 패턴의 예약만 취소합니다.
    /// </summary>
    public void Cancel(FishSpawnPattern owner)
    {
        // 제거로 인덱스가 바뀌지 않도록 뒤에서부터 처리합니다.
        for (int i = _reservations.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_reservations[i].Owner, owner))
                _reservations.RemoveAt(i);
        }
    }

    /// <summary>
    /// 스포너 종료 시 남은 예약과 시간을 초기화합니다.
    /// </summary>
    public void Clear()
    {
        // 다른 실행으로 이전 예약이 넘어가지 않게 합니다.
        _reservations.Clear();
        _clock = 0d;
    }
}
