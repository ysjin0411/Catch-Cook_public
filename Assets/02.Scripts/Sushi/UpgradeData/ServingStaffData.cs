using UnityEngine;

/// <summary>서빙 직원의 기본 이동속도와 강화 데이터를 정의합니다.</summary>
[CreateAssetMenu(fileName = "ServingStaffData", menuName = "Game/Sushi/Staff/Serving Staff Data")]
public sealed class ServingStaffData : StaffData
{
    // 직원 고유의 1레벨 이동속도입니다.
    [SerializeField, Min(0f)] float _baseMoveSpeed = 2.2f;
    // 기존 속도표의 1레벨 대비 증가량, 비용과 패시브를 제공하는 데이터입니다.
    [SerializeField] ServingStaffUpgradeData _upgradeData;

    /// <summary>서빙 업무를 담당합니다.</summary>
    public override StaffType Type => StaffType.Serving;
    /// <summary>강화 증가량을 적용하기 전의 기본 이동속도입니다.</summary>
    public float BaseMoveSpeed => _baseMoveSpeed;
    /// <summary>이 직원에게 적용할 강화 데이터입니다.</summary>
    public ServingStaffUpgradeData UpgradeData => _upgradeData;
    /// <summary>강화 데이터에 등록된 최대 레벨입니다.</summary>
    public int MaxLevel => _upgradeData != null ? _upgradeData.MaxLevel : 0;

    /// <summary>기본 속도에 강화 표의 1레벨 대비 증가량을 더한 이동속도를 반환합니다.</summary>
    public bool TryGetMoveSpeed(int level, out float speed)
    {
        // 조회 실패 시 사용할 기본값입니다.
        speed = 0f;
        // 직원의 기본 속도가 0 이상의 유한한 값인지 검사합니다.
        if (float.IsNaN(_baseMoveSpeed) || float.IsInfinity(_baseMoveSpeed) || _baseMoveSpeed < 0f)
            return false;
        // 기존 에셋의 최종 속도표를 유지하면서 강화 증가량만 조회합니다.
        if (_upgradeData == null || !_upgradeData.TryGetMoveSpeedBonus(level, out float bonus))
            return false;
        // 직원 고유 속도와 해당 레벨의 강화 증가량을 합산합니다.
        float result = _baseMoveSpeed + bonus;
        // 합산 과정에서 유효하지 않은 값이 발생하면 적용하지 않습니다.
        if (float.IsNaN(result) || float.IsInfinity(result) || result < 0f)
            return false;
        // 검증된 이동속도를 반환합니다.
        speed = result;
        return true;
    }
}
