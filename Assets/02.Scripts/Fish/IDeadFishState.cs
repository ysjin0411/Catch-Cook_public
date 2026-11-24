using UnityEngine;

public interface IDeadFish
{
    GameObject _heroSensorCollider { get; }

    // 만약 사망상태 일시 히어로 센서 콜라이더를 활성화 시킨다.
    void ChangeFishState();
}
