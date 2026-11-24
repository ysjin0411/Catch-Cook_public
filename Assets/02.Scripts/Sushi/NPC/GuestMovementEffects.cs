using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GuestMovementEffects : MonoBehaviour
{
    // 게스트 발밑에 배치된 먼지 오브젝트입니다.
    [SerializeField] GameObject _dustEffect;
    // 작은 물리 흔들림은 이동으로 판단하지 않습니다.
    [SerializeField, Min(0f)] float _minimumSpeed = 0.05f;
    // 실제 수평 속도를 확인할 게스트 본체입니다.
    Rigidbody _rigidbody;

    /// <summary>이동 판정에 필요한 참조를 준비하고 먼지를 끕니다.</summary>
    void Awake()
    {
        // 같은 오브젝트의 Rigidbody를 저장합니다.
        _rigidbody = GetComponent<Rigidbody>();
        // 생성 직후에는 먼지가 보이지 않도록 초기화합니다.
        SetDustActive(false);
    }

    /// <summary>게스트가 좌우로 이동하는 동안에만 먼지를 켭니다.</summary>
    void LateUpdate()
    {
        // 착석 중에는 제외하고 X축 속도의 크기로 좌우 이동을 판정합니다.
        bool isMoving = !_rigidbody.isKinematic && _rigidbody.useGravity &&
                        Mathf.Abs(_rigidbody.linearVelocity.x) > _minimumSpeed;
        // 현재 이동 여부를 먼지 오브젝트에 반영합니다.
        SetDustActive(isMoving);
    }

    /// <summary>컴포넌트 또는 게스트가 비활성화되면 먼지를 끕니다.</summary>
    void OnDisable()
    {
        // 비활성화 후에도 먼지만 남지 않도록 정리합니다.
        SetDustActive(false);
    }

    /// <summary>활성 상태가 달라질 때만 먼지를 켜거나 끕니다.</summary>
    void SetDustActive(bool active)
    {
        // 참조가 없거나 이미 같은 상태라면 변경을 생략합니다.
        if (_dustEffect == null || _dustEffect.activeSelf == active) return;
        // 먼지와 자식 이펙트의 활성 상태를 변경합니다.
        _dustEffect.SetActive(active);
    }
}
