using UnityEngine;

/// <summary>직원의 고유 정보와 표시 데이터를 정의하며 현재 강화 레벨은 별도로 관리합니다.</summary>
public abstract class StaffData : ScriptableObject
{
    // 저장 및 화면 선택에서 사용할 직원 고유 ID입니다.
    [SerializeField, Min(1)] int _staffId = 1;
    // 직원 목록과 상세 화면에 표시할 이름입니다.
    [SerializeField] string _staffName;
    // 직원 상세 화면에 표시할 설명입니다.
    [SerializeField, TextArea(3, 5)] string _description;
    // 직원 목록과 상세 화면에 표시할 초상화입니다.
    [SerializeField] Sprite _portrait;
    // 이 직원을 사용할 수 있게 되는 플레이어 레벨입니다.
    [SerializeField, Min(1)] int _unlockPlayerLevel = 1;

    /// <summary>개별 직원을 구분하는 고유 ID입니다.</summary>
    public int StaffId => _staffId;
    /// <summary>직원의 표시 이름입니다.</summary>
    public string StaffName => _staffName;
    /// <summary>직원의 상세 설명입니다.</summary>
    public string Description => _description;
    /// <summary>직원의 초상화입니다.</summary>
    public Sprite Portrait => _portrait;
    /// <summary>직원이 해금되는 플레이어 레벨입니다.</summary>
    public int UnlockPlayerLevel => _unlockPlayerLevel;
    /// <summary>직원이 담당하는 업무 종류입니다.</summary>
    public abstract StaffType Type { get; }
}
