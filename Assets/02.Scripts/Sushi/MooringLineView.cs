using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class MooringLineView : MonoBehaviour
{
    [SerializeField] private Transform _dockPoint; // 부두 쪽 연결점입니다.
    [SerializeField] private Transform _boatPoint; // 보트 쪽 연결점입니다.
    [SerializeField, Range(2, 64)] private int _pointCount = 24; // 곡선의 점 개수입니다.
    [SerializeField, Min(0f)] private float _sag = 0.15f; // 중앙의 처짐 거리입니다.
    private LineRenderer _lineRenderer; // 로프 렌더러입니다.
    private Vector3[] _positions; // 재사용할 좌표 배열입니다.

    /// <summary>렌더러를 가져오고 월드 좌표를 사용하도록 초기화합니다.</summary>
    private void Awake()
    {
        // 같은 오브젝트의 렌더러를 가져옵니다.
        _lineRenderer = GetComponent<LineRenderer>();
        // 서로 다른 부모의 연결점을 월드 좌표로 연결합니다.
        _lineRenderer.useWorldSpace = true;
        // 마지막 점과 첫 점을 다시 연결하지 않습니다.
        _lineRenderer.loop = false;
    }

    /// <summary>보트 이동 후 연결점 위치에 맞춰 로프를 갱신합니다.</summary>
    private void LateUpdate()
    {
        // 연결점이 없으면 잘못된 위치에 줄을 표시하지 않습니다.
        if (_dockPoint == null || _boatPoint == null)
        {
            // 로프 표시를 끕니다.
            _lineRenderer.enabled = false;
            // 연결점이 준비될 때까지 갱신을 중단합니다.
            return;
        }

        // 연결점이 준비되면 로프를 표시합니다.
        _lineRenderer.enabled = true;
        // 현재 연결점 위치로 곡선을 그립니다.
        DrawLine();
    }

    /// <summary>양 끝을 연결점에 고정하고 중앙이 아래로 처지는 곡선을 그립니다.</summary>
    private void DrawLine()
    {
        // 시작점과 끝점을 표현하도록 최소 두 점을 보장합니다.
        int count = Mathf.Max(2, _pointCount);
        // 점 개수가 바뀌었을 때만 배열을 다시 만듭니다.
        if (_positions == null || _positions.Length != count)
        {
            // 좌표 저장 공간을 준비합니다.
            _positions = new Vector3[count];
        }

        // 부두의 월드 위치를 읽습니다.
        Vector3 start = _dockPoint.position;
        // 보트의 월드 위치를 읽습니다.
        Vector3 end = _boatPoint.position;
        // 시작점부터 끝점까지 각 좌표를 계산합니다.
        for (int i = 0; i < count; i++)
        {
            // 현재 점의 진행 비율을 계산합니다.
            float t = i / (float)(count - 1);
            // 두 연결점 사이의 직선상 위치를 구합니다.
            Vector3 position = Vector3.Lerp(start, end, t);
            // 양 끝에서 0, 중앙에서 1인 처짐 비율을 구합니다.
            float sagFactor = 4f * t * (1f - t);
            // 중앙이 월드 아래 방향으로 처지도록 보정합니다.
            position += Vector3.down * (_sag * sagFactor);
            // 계산한 위치를 배열에 저장합니다.
            _positions[i] = position;
        }

        // 렌더러의 점 개수를 맞춥니다.
        _lineRenderer.positionCount = count;
        // 모든 좌표를 한 번에 전달합니다.
        _lineRenderer.SetPositions(_positions);
    }
}
