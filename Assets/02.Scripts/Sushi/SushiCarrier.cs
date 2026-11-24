using System;
using UnityEngine;

[DisallowMultipleComponent]
public class SushiCarrier : MonoBehaviour
{
    // 현재 운반 중인 완성 요리입니다. 비어 있으면 null입니다.
    [SerializeField] Sushi _currentSushi;

    // 같은 메뉴를 다른 손님에게 제공할 때 남은 주문을 인계할 원래 주문자입니다.
    GuestNPC _sourceGuest;

    // 외부에서 운반 중인 메뉴를 읽을 수 있도록 제공합니다.
    public Sushi CurrentSushi => _currentSushi;

    /// <summary>음식 제공 성공 시 실제 제공받은 손님을 알립니다.</summary>
    public event Action<GuestNPC> FoodServed;

    /// <summary>
    /// 완성 요리와 원래 주문자를 함께 받아 운반합니다.
    /// </summary>
    public bool TryReceive(Sushi sushi, GuestNPC sourceGuest)
    {
        // 비활성 상태이거나 전달할 요리가 없으면 받지 않습니다.
        if (!isActiveAndEnabled || sushi == null) return false;
        // 음료는 손님 앞에서 직접 제공하므로 운반 슬롯에 넣지 않습니다.
        if (sushi.Type != MenuType.Food) return false;

        // 이미 운반 중인 요리가 있으면 덮어쓰지 않습니다.
        if (_currentSushi != null) return false;

        // 수령한 완성 요리의 메뉴 데이터를 보관합니다.
        _currentSushi = sushi;

        // 서빙 대상은 메뉴로 판단하고 이 정보는 남은 주문의 인계에만 사용합니다.
        _sourceGuest = sourceGuest;
        GameSfx.PlayAt(GameSoundId.PlatePickup, transform.position);

        // 요리를 정상적으로 받았음을 반환합니다.
        return true;
    }

    /// <summary>
    /// 운반 중인 요리를 손님에게 제공하고 성공한 경우에만 비웁니다.
    /// </summary>
    public bool TryServeTo(GuestNPC guest)
    {
        // 운반 기능과 서빙 대상이 유효한지 확인합니다.
        if (!isActiveAndEnabled || guest == null) return false;

        // 운반 중인 요리가 없다면 서빙하지 않습니다.
        if (_currentSushi == null) return false;

        // 손님에게 주문 비교와 식사 상태 전환을 요청합니다.
        if (!guest.TryServe(_currentSushi, _sourceGuest)) return false;

        // 서빙에 성공했으므로 운반 중인 요리를 제거합니다.
        _currentSushi = null;
        _sourceGuest = null;

        // 연출은 실제 제공 성공 후 운반 정보가 정리된 상태에서 시작합니다.
        FoodServed?.Invoke(guest);

        // 요리가 정상적으로 전달되었음을 반환합니다.
        return true;
    }

    /// <summary>
    /// 현재 운반 중인 음식을 버리고, 실제로 버렸다면 true를 반환합니다.
    /// </summary>
    public bool TryDiscard()
    {
        // 들고 있는 음식이 없다면 버리지 않고 실패를 반환합니다.
        if (_currentSushi == null) return false;

        // 운반 중인 음식 참조를 비워 빈손 상태로 변경합니다.
        _currentSushi = null;
        _sourceGuest = null;

        // 음식을 정상적으로 버렸음을 반환합니다.
        return true;
    }
}
