using UnityEngine;

/// <summary>
/// 생성 원과 독립적으로 물고기의 월드 Y 이동 범위를 보관합니다.
/// </summary>
public sealed class FishVerticalRange
{
    // 이동 가능한 최소 월드 Y 좌표입니다.
    public float MinY { get; }

    // 이동 가능한 최대 월드 Y 좌표입니다.
    public float MaxY { get; }

    /// <summary>
    /// 두 높이의 순서를 보정하여 월드 Y 범위를 초기화합니다.
    /// </summary>
    public FishVerticalRange(float minY, float maxY)
    {
        // 작은 값을 하한으로 사용합니다.
        MinY = Mathf.Min(minY, maxY);

        // 큰 값을 상한으로 사용합니다.
        MaxY = Mathf.Max(minY, maxY);
    }

    /// <summary>
    /// X 좌표를 유지하고 Y 좌표만 이동 범위 안으로 보정합니다.
    /// </summary>
    public Vector2 ClampPosition(Vector2 position)
    {
        // 수평 이동은 제한하지 않고 월드 높이만 보정합니다.
        return new Vector2(position.x, Mathf.Clamp(position.y, MinY, MaxY));
    }
}
