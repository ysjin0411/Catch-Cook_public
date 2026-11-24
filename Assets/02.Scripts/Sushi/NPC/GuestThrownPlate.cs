using UnityEngine;

/// <summary>
/// 수평 회전하며 플레이어 머리를 향해 이동한 뒤
/// 화면 중앙으로 튕기는 연출 전용 접시입니다.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class GuestThrownPlate : MonoBehaviour
{
    GuestReactionEffects _owner;
    Transform _head;
    Camera _camera;

    Vector3 _start;
    Vector3 _originalScale;

    float _headTime;
    float _screenTime;
    float _arc;
    float _spin;
    float _endScale;
    float _elapsed;
    float _yaw;
    float _meshRadius;

    bool _flying;
    bool _bouncing;

    /// <summary>
    /// 비행 목표와 구간별 시간, 회전 속도를 설정합니다.
    /// </summary>
    public void Launch(
        GuestReactionEffects owner,
        Transform head,
        Camera outputCamera,
        float headTime,
        float screenTime,
        float arc,
        float spin,
        float endScale)
    {
        // 비행 중 사용할 관리자와 목표를 보관합니다.
        _owner = owner;
        _head = head;
        _camera = outputCamera;

        // 발사 위치와 원래 접시 크기를 저장합니다.
        _start = transform.position;
        _originalScale = transform.localScale;

        // 0초 비행으로 나누기가 발생하지 않도록 시간을 제한합니다.
        _headTime = Mathf.Max(0.05f, headTime);
        _screenTime = Mathf.Max(0.05f, screenTime);

        // 비행 곡선과 회전, 화면 접근 시 확대 설정을 저장합니다.
        _arc = Mathf.Max(0f, arc);
        _spin = spin;
        _endScale = Mathf.Max(1f, endScale);

        // 카메라 near clip 앞에 도착점을 잡기 위해 메시 크기를 저장합니다.
        MeshFilter filter = GetComponent<MeshFilter>();

        _meshRadius = filter != null && filter.sharedMesh != null
            ? filter.sharedMesh.bounds.extents.magnitude
            : 0.12f;

        // 이전 비행 상태가 남지 않도록 진행 상태를 초기화합니다.
        _elapsed = 0f;
        _yaw = transform.eulerAngles.y;
        _bouncing = false;
        _flying = true;
    }

    /// <summary>
    /// Animator와 카메라 갱신 이후 현재 목표 위치를 따라 이동합니다.
    /// </summary>
    void LateUpdate()
    {
        // 발사되지 않은 접시는 이동하지 않습니다.
        if (!_flying) return;

        // 관리자나 출력 카메라가 사라지면 연출을 중단합니다.
        if (_owner == null ||
            !_owner.isActiveAndEnabled ||
            _camera == null ||
            !_camera.isActiveAndEnabled)
        {
            Destroy(gameObject);
            return;
        }

        // 현재 구간에서 경과한 시간을 계산합니다.
        _elapsed += Time.deltaTime;

        // 접시 면은 수평으로 유지하고 월드 Y축으로만 회전합니다.
        _yaw = Mathf.Repeat(
            _yaw + _spin * Time.deltaTime, 360f);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        // 머리에 도달하기 전에는 현재 머리 위치를 추적합니다.
        if (!_bouncing)
        {
            MoveToHead();
            return;
        }

        // 머리에 도달한 이후에는 화면 중앙으로 튕깁니다.
        MoveToScreen();
    }

    /// <summary>
    /// 움직이는 플레이어의 현재 머리 위치를 향해 이동합니다.
    /// </summary>
    void MoveToHead()
    {
        // 머리가 제거되거나 비활성화되면 접시를 정리합니다.
        if (_head == null || !_head.gameObject.activeInHierarchy)
        {
            _owner.ReleasePlate(gameObject);
            return;
        }

        // 머리까지의 비행 진행도를 계산합니다.
        float t = Mathf.Clamp01(_elapsed / _headTime);

        // 시작점과 현재 머리 위치 사이를 이동합니다.
        Vector3 position = Vector3.Lerp(
            _start, _head.position, t);

        // 중간 구간에서만 살짝 위로 올라가는 곡선을 더합니다.
        position += Vector3.up * (_arc * 4f * t * (1f - t));

        // 계산한 위치를 접시에 적용합니다.
        transform.position = position;

        // 머리에 도달하기 전에는 현재 구간을 유지합니다.
        if (t < 1f) return;

        // 머리에 닿은 위치를 화면 비행의 출발점으로 저장합니다.
        _start = transform.position;

        // 다음 구간의 시간을 초기화하고 튕김 상태로 변경합니다.
        _elapsed = 0f;
        _bouncing = true;
    }

    /// <summary>
    /// 머리에서 튕겨 실제 화면 중앙 앞으로 접근합니다.
    /// </summary>
    void MoveToScreen()
    {
        // 화면까지의 진행도와 가속되는 이동 비율을 계산합니다.
        float progress = Mathf.Clamp01(_elapsed / _screenTime);
        float eased = progress * progress;

        // 직교 카메라에서는 거리에 따른 확대가 없으므로 직접 크기를 늘립니다.
        float size = _camera.orthographic
            ? Mathf.Lerp(1f, _endScale, eased)
            : 1f;

        transform.localScale = _originalScale * size;

        // 현재 월드 스케일에서 접시가 차지하는 반지름을 계산합니다.
        Vector3 scale = transform.lossyScale;

        float maxScale = Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Abs(scale.y),
            Mathf.Abs(scale.z));

        float radius = _meshRadius * maxScale;

        // near clip에 먼저 잘리지 않도록 접시 크기만큼 앞에 도착점을 둡니다.
        float depth = _camera.nearClipPlane + radius + 0.03f;

        Vector3 endpoint = _camera.ViewportToWorldPoint(
            new Vector3(0.5f, 0.5f, depth));

        // 움직이는 카메라의 화면 중앙을 계속 따라갑니다.
        Vector3 position = Vector3.Lerp(
            _start, endpoint, eased);

        // 튕기는 구간에 짧은 위쪽 곡선을 더합니다.
        position += _camera.transform.up *
                    (0.15f * Mathf.Sin(Mathf.PI * progress));

        // 계산한 위치를 적용합니다.
        transform.position = position;

        // 화면에 도달하기 전에는 비행을 유지합니다.
        if (progress < 1f) return;

        // 충돌 처리가 중복되지 않도록 먼저 비행을 종료합니다.
        _flying = false;

        // 균열과 카메라 흔들림을 발생시킵니다.
        _owner.NotifyScreenHit();

        // 접시는 제거하고 화면 효과만 남깁니다.
        _owner.ReleasePlate(gameObject);
    }

    /// <summary>
    /// 외부에서 접시가 파괴되어도 관리자의 추적 목록을 정리합니다.
    /// </summary>
    void OnDestroy()
    {
        // 관리자가 먼저 제거된 경우에는 호출하지 않습니다.
        if (_owner != null)
        {
            _owner.ForgetPlate(gameObject);
        }
    }
}