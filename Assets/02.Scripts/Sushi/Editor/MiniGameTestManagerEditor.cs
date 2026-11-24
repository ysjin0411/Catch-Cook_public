using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

/// <summary>음료 미니게임 테스트 설정과 직접 누르는 실행 버튼을 표시합니다.</summary>
[CustomEditor(typeof(MiniGameTestManager))]
public sealed class MiniGameTestManagerEditor : Editor
{
    /// <summary>기본 참조 필드 아래에 음료별 실행 버튼 5개를 구성합니다.</summary>
    public override VisualElement CreateInspectorGUI()
    {
        // 기본 필드와 버튼을 담을 인스펙터 루트를 생성합니다.
        var root = new VisualElement();

        // 기존 View 및 스타일 연결 필드를 그대로 표시합니다.
        InspectorElement.FillDefaultInspector(root, serializedObject, this);

        // 실행 조건과 화면 테스트의 범위를 안내합니다.
        root.Add(new HelpBox(
            "플레이 모드에서 실행하세요. 현재 미니게임을 닫은 뒤 다른 음료를 테스트할 수 있습니다.\n" +
            "손님 서빙 처리와 플레이어 이동 잠금은 실행하지 않습니다.",
            HelpBoxMessageType.Info));

        // 현재 인스펙터의 테스트 컴포넌트를 가져옵니다.
        var manager = (MiniGameTestManager)target;

        // 각 버튼은 매니저의 기존 검증 및 미니게임 실행 메서드에 위임합니다.
        root.Add(new Button(manager.TestLemon) { text = "레몬 에이드 테스트" });
        root.Add(new Button(manager.TestGreenGrape) { text = "청포도 에이드 테스트" });
        root.Add(new Button(manager.TestGrapefruit) { text = "자몽 에이드 테스트" });
        root.Add(new Button(manager.TestBeer) { text = "맥주 테스트" });
        root.Add(new Button(manager.TestTea) { text = "차 테스트" });

        // 완성한 인스펙터를 Unity에 반환합니다.
        return root;
    }
}
