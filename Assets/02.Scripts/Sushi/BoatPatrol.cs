using UnityEngine;

/// <summary>
/// 중심점의 수평 전방 축을 따라 원의 지름을 왕복하는 배경 보트입니다.
/// </summary>
[DisallowMultipleComponent]
public class BoatPatrol : MonoBehaviour
{
    [Header("이동 범위")]
    [SerializeField] private Transform _centerPoint;
    [SerializeField, Min(0.01f)] private float _radius = 20f;

    [Header("왕복 전체 시간")]
    [SerializeField, Min(0.1f)] private float _roundTripSeconds = 20f;

    [Header("모델 방향 보정")]
    [Tooltip("모델의 앞쪽 축이 다르면 Y 각도로 보정합니다.")]
    [SerializeField] private float _yawOffset;

    private float _elapsed;

    /// <summary>중심점 설정을 검사하고 지름의 한쪽 끝에서 시작합니다.</summary>
    private void OnEnable()
    {
        // 보트와 독립적으로 배치된 중심점만 허용합니다.
        if (_centerPoint == null || _centerPoint.IsChildOf(transform))
        {
            // 잘못된 참조를 알리고 이동을 중단합니다.
            Debug.LogError("Center Point를 보트 외부의 오브젝트로 지정해주세요.", this);
            enabled = false;
            return;
        }

        // 활성화할 때마다 시작 지점으로 돌아갑니다.
        _elapsed = 0f;
        ApplyPose();
    }

    /// <summary>게임 시간에 맞춰 일정 속도로 왕복 이동합니다.</summary>
    private void Update()
    {
        // 실행 도중 중심점이 제거되면 이동을 중단합니다.
        if (_centerPoint == null)
        {
            enabled = false;
            return;
        }

        // 누적 시간을 한 주기 안으로 유지합니다.
        float duration = Mathf.Max(0.1f, _roundTripSeconds);
        _elapsed = Mathf.Repeat(_elapsed + Time.deltaTime, duration);

        // 현재 시간에 맞는 위치와 방향을 적용합니다.
        ApplyPose();
    }

    /// <summary>중심점의 전방을 수평면에 투영한 이동 축을 반환합니다.</summary>
    private Vector3 GetMoveAxis()
    {
        // 이동 축의 높이 성분을 제거하여 수면과 평행하게 만듭니다.
        Vector3 axis = Vector3.ProjectOnPlane(_centerPoint.forward, Vector3.up);

        // 중심점이 수직을 향하면 월드 Z축을 대신 사용합니다.
        return axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward;
    }

    /// <summary>지름 위의 위치와 수평 진행 방향을 보트 루트에 적용합니다.</summary>
    private void ApplyPose()
    {
        // 왕복 주기의 전반부와 후반부를 구분합니다.
        float phase = _elapsed / Mathf.Max(0.1f, _roundTripSeconds);
        bool movingForward = phase < 0.5f;

        // 위치 보간값을 0에서 1로 증가시킨 뒤 다시 0으로 줄입니다.
        float t = movingForward ? phase * 2f : (1f - phase) * 2f;
        float radius = Mathf.Max(0.01f, _radius);

        // 중심점의 높이를 유지하며 지름 위로 이동합니다.
        Vector3 axis = GetMoveAxis();
        transform.position = _centerPoint.position + axis * Mathf.Lerp(-radius, radius, t);

        // 끝점에서는 반대 방향으로 즉시 회전하고 모델의 앞쪽 축을 보정합니다.
        Vector3 direction = movingForward ? axis : -axis;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up)
            * Quaternion.Euler(0f, _yawOffset, 0f);
    }

    /// <summary>선택한 보트의 실제 이동 구간과 양 끝점을 표시합니다.</summary>
    private void OnDrawGizmosSelected()
    {
        // 중심점이 지정된 경우에만 경로를 그립니다.
        if (_centerPoint == null) return;

        // 수평 이동 축을 기준으로 양 끝점을 계산합니다.
        Vector3 offset = GetMoveAxis() * Mathf.Max(0.01f, _radius);
        Vector3 start = _centerPoint.position - offset;
        Vector3 end = _centerPoint.position + offset;

        // 이동 선분과 중심, 방향 전환 지점을 표시합니다.
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(_centerPoint.position, 0.5f);
        Gizmos.DrawWireSphere(start, 0.5f);
        Gizmos.DrawWireSphere(end, 0.5f);
    }
}
