using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// EnforceManager의 기본 인스펙터와 직원 강화 테스트 버튼을 표시합니다.
/// </summary>
[CustomEditor(typeof(EnforceManager))]
public class EnforceManagerEditor : Editor
{
    /// <summary>
    /// 기본 직렬화 필드와 직원별 강화 버튼을 구성합니다.
    /// </summary>
    public override VisualElement CreateInspectorGUI()
    {
        // 기본 필드와 테스트 버튼을 담을 최상위 요소를 생성합니다.
        var root = new VisualElement();

        // 기존 EnforceManager의 인스펙터 필드를 그대로 표시합니다.
        InspectorElement.FillDefaultInspector(root, serializedObject, this);

        // 버튼이 실제 강화 비용을 사용하는 테스트임을 안내합니다.
        root.Add(new HelpBox(
            "플레이 모드에서 실행하세요. 실제 골드를 소비하며 기존 강화 조건이 적용됩니다.",
            HelpBoxMessageType.Info));

        // 서빙 직원 강화 버튼을 추가합니다.
        root.Add(new Button(() =>
        {
            // 기존 강화 API로 서빙 직원 강화를 요청합니다.
            UpgradeStaff(StaffType.Serving);
        })
        {
            text = "서빙 직원 강화레벨 1 증가"
        });

        // 요리 직원 강화 버튼을 추가합니다.
        root.Add(new Button(() =>
        {
            // 기존 강화 API로 요리 직원 강화를 요청합니다.
            UpgradeStaff(StaffType.Cooking);
        })
        {
            text = "요리 직원 강화레벨 1 증가"
        });

        // 구성한 인스펙터를 Unity에 반환합니다.
        return root;
    }

    /// <summary>
    /// 플레이 모드에서 선택한 직원의 실제 강화를 실행하고 결과를 출력합니다.
    /// </summary>
    private void UpgradeStaff(StaffType type)
    {
        // 현재 인스펙터가 표시 중인 강화 관리자를 가져옵니다.
        var manager = target as EnforceManager;

        // 대상 컴포넌트가 제거되었다면 실행을 종료합니다.
        if (manager == null)
            return;

        // 실행 중인 게임에서만 강화하도록 제한합니다.
        if (!Application.isPlaying)
        {
            // 편집 모드에서는 플레이 모드 실행을 안내합니다.
            Debug.LogWarning("직원 강화 테스트는 플레이 모드에서 실행하세요.", manager);

            // 실제 강화 요청을 중단합니다.
            return;
        }

        // 비용 지불과 능력치 변경은 기존 관리자의 강화 로직에 위임합니다.
        bool success = manager.TryUpgrade(type);

        // 로그에 표시할 직원 이름을 선택합니다.
        string staffName = type == StaffType.Serving ? "서빙 직원" : "요리 직원";

        // 강화 성공 여부와 현재 레벨을 출력합니다.
        Debug.Log(
            $"{staffName} 강화: {success}, 현재 {manager.GetStaffLevel(type)}레벨",
            manager);
    }
}