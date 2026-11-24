using UnityEngine;

/// <summary>
/// 물고기의 최초 생성 위치를 결정하는 고정된 XY 원형 영역입니다.
/// </summary>
public sealed class FishSpawnArea
{
    // 원의 월드 좌표 중심입니다.
    public Vector3 Center { get; }

    // 월드 좌표 기준 반지름입니다.
    public float Radius { get; }

    // 무리 전체의 허용 반경과 별도로 중심점의 무작위 이동 폭을 제한합니다.
    private readonly float _centerSpreadRadius;

    // XY 계산에 사용하는 중심입니다.
    public Vector2 CenterXY => new Vector2(Center.x, Center.y);

    /// <summary>
    /// 중심과 반지름으로 원형 영역을 초기화합니다.
    /// </summary>
    public FishSpawnArea(Vector3 center, float radius, float centerSpreadRadius = -1f)
    {
        // 생성 시점의 중심 좌표를 저장합니다.
        Center = center;

        // 반지름이 0 이하가 되지 않도록 보정합니다.
        Radius = Mathf.Max(0.01f, radius);
        // 음수는 기존 동작을 유지하며, 지정한 경우 무리 자리가 원 가장자리에 압축되지 않게 합니다.
        _centerSpreadRadius = centerSpreadRadius < 0f ? Radius : Mathf.Clamp(centerSpreadRadius, 0f, Radius);
    }

    /// <summary>
    /// 원 내부에서 무작위 XY 위치를 반환합니다.
    /// </summary>
    public Vector2 GetRandomPosition()
    {
        // 원 내부의 무작위 상대 좌표를 반지름에 맞게 확장합니다.
        Vector2 offset = Random.insideUnitCircle * _centerSpreadRadius;

        // 상대 좌표를 월드 좌표로 변환합니다.
        return CenterXY + offset;
    }

    /// <summary>
    /// 주어진 XY 위치를 원 내부 또는 경계로 제한합니다.
    /// </summary>
    public Vector2 ClampPosition(Vector2 position)
    {
        // 중심으로부터의 상대 좌표를 계산합니다.
        Vector2 offset = position - CenterXY;

        // 중심으로부터의 거리를 반지름 이하로 제한합니다.
        return CenterXY + Vector2.ClampMagnitude(offset, Radius);
    }

    /// <summary>
    /// 주어진 위치를 최초 생성 원과 월드 높이 범위의 교집합 안으로 보정합니다.
    /// </summary>
    public Vector2 ClampPosition(Vector2 position, FishVerticalRange verticalRange)
    {
        // 원 내부의 기존 보정 결과를 기준으로 생성 위치를 계산합니다.
        Vector2 clamped = ClampPosition(position);

        // 생성 원과 이동 높이 범위가 공유하는 세로 구간을 계산합니다.
        float minY = Mathf.Max(Center.y - Radius, verticalRange.MinY);
        float maxY = Mathf.Min(Center.y + Radius, verticalRange.MaxY);

        // 생성 가능한 세로 구간으로 높이를 보정합니다.
        clamped.y = Mathf.Clamp(clamped.y, minY, maxY);

        // 높이 보정 후에도 원 내부에 있도록 해당 높이의 가로 반폭을 계산합니다.
        float offsetY = clamped.y - Center.y;
        float halfWidth = Mathf.Sqrt(
            Mathf.Max(0f, Radius * Radius - offsetY * offsetY));

        // 같은 높이에서 원 밖으로 나가지 않도록 X 좌표를 보정합니다.
        clamped.x = Mathf.Clamp(
            clamped.x,
            Center.x - halfWidth,
            Center.x + halfWidth);

        // 최초 생성 원과 이동 높이를 모두 만족하는 위치를 반환합니다.
        return clamped;
    }
}