/// <summary>
/// 플레이어에 대한 물고기의 기본 반응 유형입니다.
/// </summary>
public enum FishType
{
    // 피해를 받기 전까지 평상시 이동 패턴을 유지
    Passive = 0,

    // 플레이어가 가까워지면 도망
    Evasive = 1,

    // 플레이어가 가까워지면 먼저 공격
    Aggressive = 2
}

/// <summary>
/// 물고기가 현재 수행 중인 행동 상태입니다.
/// </summary>
public enum FishState
{
    // 전달받은 평상시 이동 패턴을 실행합니다.
    Moving = 0,

    // 플레이어에게 반응하여 공격하거나 도망칩니다.
    Combat = 1,

    // 작살에 연결된 행동을 수행합니다.
    HarpoonAction = 2,

    // 사망하여 일반 행동을 중단합니다.
    Dead = 3
}
/// <summary>
/// 작살에 포획될 때 저항하는 물고기인지 구분합니다.
/// </summary>
public enum FishResistanceType
{
    // 저항 액션 없이 끌려가는 물고기입니다.
    None = 0,

    // 작살 담당자가 저항 액션을 실행해야 하는 물고기입니다.
    Resistant = 1
}

/// <summary>
/// 작살 담당자에게 이번 적중의 처리 방향을 전달합니다.
/// </summary>
public enum FishHarpoonResult
{
    // 적중 처리가 거부되었거나 피해로 사망하여 연결할 수 없습니다.
    Unavailable = 0,

    // 작살에 맞았지만 체력이 높아 끌고 올 수 없습니다.
    TooHealthy = 1,

    // 체력 조건을 만족한 무저항 물고기입니다.
    NonResistant = 2,

    // 체력 조건을 만족한 저항 물고기입니다.
    Resistant = 3
}