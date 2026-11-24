using UnityEngine;

/// <summary>
/// 물고기의 타입과 기본 능력치, 초기 이동 방향을 보관합니다.
/// </summary>
[CreateAssetMenu(fileName = "FishData", menuName = "Fish/Data")]
public class FishData : ScriptableObject
{
    // 어종을 식별하는 고유 ID입니다.
    // 다른 어종의 FishData와 중복되지 않도록 설정합니다.
    [SerializeField] private string _fishId;

    // UI 등에 표시할 물고기 이름입니다.
    [SerializeField] private string _fishName;

    [SerializeField] string _depth;     // 물고기 수심

    // 플레이어에게 반응하는 기본 성향입니다.
    [SerializeField] private FishType _fishType;

    // Aggressive가 Hero를 처음 감지하는 월드 XYZ 반경입니다.
    [SerializeField, Min(0f)]
    [Tooltip("Aggressive의 Hero 감지 반경입니다. 0이면 선제 감지를 끕니다.")]
    private float _heroDetectionRadius = 10f;

    // 생성 시 적용할 최대 체력입니다.
    [SerializeField, Min(1f)] private float _maxHp = 10f;

    // 생성 시 적용할 공격력입니다.
    [SerializeField, Min(0f)] private float _damage = 1f;

    // 생성 시 적용할 이동 속도입니다.
    [SerializeField, Min(0f)] private float _moveSpeed = 3f;

    // 생성 직후 이동할 월드 XY 방향입니다.
    [SerializeField] private Vector2 _initialDirection = Vector2.left;



    public string Depth => _depth;
    // 저장 데이터나 포획 결과에서 어종을 식별할 때 사용합니다.
    public string FishId => _fishId;

    // 사용자에게 표시할 이름입니다.
    public string FishName => _fishName;

    // 작살 포획 시 저항 액션이 필요한 종류인지 설정합니다.
    [SerializeField] private FishResistanceType _resistanceType;

    // 외부에서 물고기의 저항 종류를 읽을 수 있게 제공합니다.
    public FishResistanceType ResistanceType => _resistanceType;

    public FishType Type => _fishType;

    // 감지 반경은 Aggressive에서만 사용하고 다른 타입은 선제 감지하지 않습니다.
    public float HeroDetectionRadius =>
        _fishType == FishType.Aggressive ? Mathf.Max(0f, _heroDetectionRadius) : 0f;
    public float MaxHp => _maxHp;
    public float Damage => _damage;
    public float MoveSpeed => _moveSpeed;
    public Vector2 InitialDirection => _initialDirection.normalized;
}