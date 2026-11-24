using UnityEngine;

/// <summary>손님의 평지 수평 이동과 앞뒤 통로의 Z좌표 고정을 담당합니다.</summary>
[RequireComponent(typeof(Rigidbody))]
public class GuestMover : MonoBehaviour
{
    // 손님의 물리 이동을 처리합니다.
    Rigidbody _rigidbody;
    // 현재 수평 이동 입력입니다.
    float _horizontalInput;
    // 초당 수평 이동 속도입니다.
    float _moveSpeed;
    // 손님이 이동할 통로의 월드 Z좌표입니다.
    float _movementPlaneZ;

    /// <summary>Rigidbody를 준비하고 최초 이동 통로를 설정합니다.</summary>
    void Awake()
    {
        // 같은 오브젝트의 Rigidbody를 가져옵니다.
        _rigidbody = GetComponent<Rigidbody>();
        // 현재 위치를 최초 이동 통로로 저장합니다.
        _movementPlaneZ = _rigidbody.position.z;
        // 충돌로 손님이 앞뒤 통로를 벗어나지 않도록 제한합니다.
        _rigidbody.constraints |= RigidbodyConstraints.FreezePositionZ;
    }

    /// <summary>수평 이동 방향과 초당 이동 속도를 설정합니다.</summary>
    public void MoveHorizontal(float horizontal, float moveSpeed)
    {
        // 수평 입력을 허용 범위로 제한합니다.
        _horizontalInput = Mathf.Clamp(horizontal, -1f, 1f);
        // 이동 속도가 음수가 되지 않도록 보정합니다.
        _moveSpeed = Mathf.Max(0f, moveSpeed);
    }

    /// <summary>위치 변경 이후 유지할 이동 통로의 Z좌표를 설정합니다.</summary>
    public void SetMovementPlane(float worldZ)
    {
        // 다음 물리 갱신부터 사용할 통로를 저장합니다.
        _movementPlaneZ = worldZ;
    }

    /// <summary>수직 물리를 유지하면서 수평 이동과 통로 고정을 처리합니다.</summary>
    void FixedUpdate()
    {
        // 착석 중에는 GuestNPC의 위치 고정 처리를 유지합니다.
        if (_rigidbody.isKinematic || !_rigidbody.useGravity) return;
        // 중력에 의한 수직 속도를 보존하기 위해 현재 속도를 가져옵니다.
        Vector3 velocity = _rigidbody.linearVelocity;
        // 이동 입력과 속도를 수평 방향에 적용합니다.
        velocity.x = _horizontalInput * _moveSpeed;
        // 통로를 벗어나는 깊이 방향 이동을 차단합니다.
        velocity.z = 0f;
        // 수직 속도를 유지한 채 변경한 속도를 적용합니다.
        _rigidbody.linearVelocity = velocity;
        // 현재 물리 위치를 가져옵니다.
        Vector3 position = _rigidbody.position;
        // 이미 지정된 통로에 있다면 위치 보정을 생략합니다.
        if (position.z == _movementPlaneZ) return;
        // 통로의 Z좌표로 위치를 보정합니다.
        position.z = _movementPlaneZ;
        // 보정한 위치를 적용합니다.
        _rigidbody.position = position;
    }
}
