using System.Collections;
using UnityEngine;

/// <summary>
/// 로컬 Y축을 두 각도 사이에서 왕복 회전합니다.
/// 편도 이동 시간과 각 끝점에서 유지할 시간을 설정할 수 있습니다.
/// </summary>
[DisallowMultipleComponent]
public class LighthouseBeamRotation : MonoBehaviour
{
    [Header("로컬 Y축 회전 범위")]
    // 왕복 회전을 시작할 각도입니다.
    [SerializeField] float _startY = 190f;

    // 반대편 끝점의 각도입니다.
    [SerializeField] float _endY = 230f;

    [Header("시간 설정")]
    // 한 끝점에서 반대쪽 끝점까지 이동하는 시간입니다.
    [SerializeField, Min(0.01f)] float _oneWayDuration = 3f;

    // 각 끝점에 도착한 후 해당 각도를 유지하는 시간입니다.
    [SerializeField, Min(0f)] float _endpointHoldDuration = 1f;

    // 회전 중 유지할 초기 X축과 Z축 각도입니다.
    Vector3 _initialEulerAngles;

    // 활성화된 동안 실행할 왕복 회전 코루틴입니다.
    Coroutine _rotationRoutine;

    /// <summary>
    /// Y축 회전 중 유지할 초기 로컬 회전값을 저장합니다.
    /// </summary>
    void Awake()
    {
        // 이미지에 설정된 X축과 Z축 기울기를 보관합니다.
        _initialEulerAngles = transform.localEulerAngles;
    }

    /// <summary>
    /// 활성화되면 시작 각도에서 왕복 회전을 시작합니다.
    /// </summary>
    void OnEnable()
    {
        // 시작 위치를 N1으로 맞춥니다.
        ApplyYRotation(_startY);

        // N1에서 N2로 이동하는 순서로 반복을 시작합니다.
        _rotationRoutine = StartCoroutine(RotateLoop());
    }

    /// <summary>
    /// 비활성화되면 실행 중인 회전 코루틴을 종료합니다.
    /// </summary>
    void OnDisable()
    {
        // 실행 중인 코루틴이 없으면 종료할 작업이 없습니다.
        if (_rotationRoutine == null) return;

        // 컴포넌트만 꺼져도 회전이 계속되지 않도록 중단합니다.
        StopCoroutine(_rotationRoutine);

        // 다음 활성화에서 새 코루틴을 시작하도록 참조를 비웁니다.
        _rotationRoutine = null;
    }

    /// <summary>
    /// 두 끝점 사이의 이동과 도착 후 대기를 반복합니다.
    /// </summary>
    IEnumerator RotateLoop()
    {
        // 컴포넌트가 비활성화되어 코루틴이 종료될 때까지 반복합니다.
        while (true)
        {
            // N1에서 N2까지 지정한 편도 시간 동안 이동합니다.
            yield return RotateTo(_startY, _endY);

            // N2에 도착한 상태를 지정한 시간 동안 유지합니다.
            if (_endpointHoldDuration > 0f)
                yield return new WaitForSeconds(_endpointHoldDuration);

            // N2에서 N1까지 같은 편도 시간 동안 이동합니다.
            yield return RotateTo(_endY, _startY);

            // N1에 도착한 상태를 지정한 시간 동안 유지합니다.
            if (_endpointHoldDuration > 0f)
                yield return new WaitForSeconds(_endpointHoldDuration);
        }
    }

    /// <summary>
    /// 시작 각도에서 목표 각도까지 일정한 각속도로 회전합니다.
    /// </summary>
    IEnumerator RotateTo(float fromY, float toY)
    {
        // 잘못된 설정으로 이동 시간이 0 이하가 되는 것을 방지합니다.
        float duration = Mathf.Max(0.01f, _oneWayDuration);

        // 이번 편도 이동의 경과 시간을 초기화합니다.
        float elapsed = 0f;

        // 이동 시작 각도를 정확하게 적용합니다.
        ApplyYRotation(fromY);

        // 설정한 시간이 될 때까지 프레임마다 각도를 갱신합니다.
        while (elapsed < duration)
        {
            // 다음 프레임까지 기다려 실제 경과 시간을 반영합니다.
            yield return null;

            // 경과 시간이 편도 시간을 넘지 않도록 제한합니다.
            elapsed = Mathf.Min(elapsed + Time.deltaTime, duration);

            // 경과 비율에 따라 두 각도 사이를 일정한 속도로 이동합니다.
            float currentY = Mathf.Lerp(fromY, toY, elapsed / duration);

            // 계산한 Y축 회전만 적용합니다.
            ApplyYRotation(currentY);
        }
    }

    /// <summary>
    /// 초기 X축과 Z축 각도를 유지하면서 로컬 Y축 회전을 적용합니다.
    /// </summary>
    void ApplyYRotation(float angleY)
    {
        // 기존 이미지 기울기는 유지하고 Y축 각도만 변경합니다.
        transform.localRotation = Quaternion.Euler(
            _initialEulerAngles.x,
            angleY,
            _initialEulerAngles.z);
    }
}