using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// SushiManager의 기본 인스펙터와 영업 및 성장 테스트 버튼을 표시합니다.
/// </summary>
[CustomEditor(typeof(SushiManager))]
public class SushiManagerEditor : Editor
{
    /// <summary>
    /// 기본 필드와 플레이어 레벨 증가 및 영업 종료 버튼을 구성합니다.
    /// </summary>
    public override VisualElement CreateInspectorGUI()
    {
        // 기본 필드와 테스트 버튼을 담을 최상위 요소를 생성합니다.
        var root = new VisualElement();

        // 기존 SushiManager의 직렬화 필드를 그대로 표시합니다.
        InspectorElement.FillDefaultInspector(root, serializedObject, this);

        // 각 테스트를 실행할 수 있는 조건을 안내합니다.
        root.Add(new HelpBox(
            "플레이 모드 전용입니다.\n" +
            "레벨 증가는 메뉴 확정 전, 영업 종료는 메뉴 확정 후에 실행됩니다.",
            HelpBoxMessageType.Info));

        // 플레이어 레벨을 한 단계 올리는 버튼을 추가합니다.
        root.Add(new Button(IncreasePlayerLevel)
        {
            text = "플레이어 레벨 1 증가"
        });

        // 기존 영업 종료 처리를 실행하는 버튼을 추가합니다.
        root.Add(new Button(EndBusiness)
        {
            text = "영업 종료"
        });

        // 구성한 인스펙터를 Unity에 반환합니다.
        return root;
    }

    /// <summary>
    /// 플레이 모드 여부와 현재 인스펙터의 대상 관리자를 확인합니다.
    /// </summary>
    private bool TryGetManager(out SushiManager manager)
    {
        // 현재 인스펙터가 표시하는 관리자를 가져옵니다.
        manager = target as SushiManager;

        // 대상이 제거되었다면 실행하지 않습니다.
        if (manager == null)
            return false;

        // 플레이 모드에서는 대상 관리자를 사용할 수 있습니다.
        if (Application.isPlaying)
            return true;

        // 편집 모드에서는 테스트 실행 조건을 안내합니다.
        Debug.LogWarning("테스트 버튼은 플레이 모드에서 실행하세요.", manager);

        // 편집 모드에서의 실행을 차단합니다.
        return false;
    }

    /// <summary>
    /// 기존 ContextMenu와 동일한 플레이어 레벨 증가 처리를 실행합니다.
    /// </summary>
    private void IncreasePlayerLevel()
    {
        // 실행 가능한 상태인지 확인하고 관리자를 가져옵니다.
        if (!TryGetManager(out SushiManager manager))
            return;

        // 레벨 증가와 직원 상태 갱신을 기존 메서드에 위임합니다.
        manager.TestIncreasePlayerLevel();
    }

    /// <summary>
    /// 기존 ContextMenu와 동일한 영업 종료 처리를 실행합니다.
    /// </summary>
    private void EndBusiness()
    {
        // 실행 가능한 상태인지 확인하고 관리자를 가져옵니다.
        if (!TryGetManager(out SushiManager manager))
            return;

        // 운영시간 종료와 누적 실적 출력을 기존 메서드에 위임합니다.
        manager.EndBusiness();
    }
}
