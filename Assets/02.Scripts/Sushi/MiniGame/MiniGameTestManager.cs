using UnityEngine;

/// <summary>음료 미니게임 5종을 인스펙터 버튼으로 실행하는 테스트 전용 컴포넌트입니다.</summary>
public sealed class MiniGameTestManager : MonoBehaviour
{
    [Header("테스트할 미니게임")]
    // 씬에 배치된 음료 미니게임 View입니다.
    [SerializeField] SushiMinigameView _drinkView;

    [Header("음료별 스타일")]
    // 실제 미니게임 View에도 등록되어 있는 스타일 에셋입니다.
    [SerializeField] DrinkMinigameStyle _lemonStyle;
    [SerializeField] DrinkMinigameStyle _greenGrapeStyle;
    [SerializeField] DrinkMinigameStyle _grapefruitStyle;
    [SerializeField] DrinkMinigameStyle _beerStyle;
    [SerializeField] DrinkMinigameStyle _teaStyle;

#if UNITY_EDITOR
    /// <summary>레몬 에이드 미니게임을 실행합니다.</summary>
    public void TestLemon()
    {
        // 레몬 에이드 메뉴로 테스트를 시작합니다.
        OpenMinigame(_lemonStyle);
    }

    /// <summary>청포도 에이드 미니게임을 실행합니다.</summary>
    public void TestGreenGrape()
    {
        // 청포도 에이드 메뉴로 테스트를 시작합니다.
        OpenMinigame(_greenGrapeStyle);
    }

    /// <summary>자몽 에이드 미니게임을 실행합니다.</summary>
    public void TestGrapefruit()
    {
        // 자몽 에이드 메뉴로 테스트를 시작합니다.
        OpenMinigame(_grapefruitStyle);
    }

    /// <summary>맥주 미니게임을 실행합니다.</summary>
    public void TestBeer()
    {
        // 맥주 메뉴로 테스트를 시작합니다.
        OpenMinigame(_beerStyle);
    }

    /// <summary>차 미니게임을 실행합니다.</summary>
    public void TestTea()
    {
        // 차 메뉴로 테스트를 시작합니다.
        OpenMinigame(_teaStyle);
    }

    /// <summary>실행 조건을 확인하고 지정한 음료의 미니게임을 엽니다.</summary>
    void OpenMinigame(DrinkMinigameStyle style)
    {
        // 입력과 시간 처리가 가능한 플레이 모드에서만 실행합니다.
        if (!Application.isPlaying)
        {
            // 편집 모드에서는 사용 방법을 안내합니다.
            Debug.LogWarning("플레이 모드에서 실행해 주세요.", this);
            return;
        }

        // View의 입력과 표시 갱신이 가능한지 확인합니다.
        if (_drinkView == null || !_drinkView.isActiveAndEnabled)
        {
            // 누락되거나 비활성화된 View 연결을 안내합니다.
            Debug.LogWarning("활성 상태의 Drink View를 연결해 주세요.", this);
            return;
        }

        // 진행 중인 실제 주문이나 다른 테스트를 덮어쓰지 않습니다.
        if (_drinkView.IsOpen)
        {
            // 기존 미니게임 종료 후 다시 실행하도록 안내합니다.
            Debug.LogWarning("현재 미니게임을 닫은 후 실행해 주세요.", this);
            return;
        }

        // 테스트할 스타일과 메뉴 연결을 확인합니다.
        if (style == null || style.Menu == null)
        {
            // 누락된 음료 에셋 연결을 안내합니다.
            Debug.LogWarning("음료 Style과 해당 Style의 Menu를 확인해 주세요.", this);
            return;
        }

        // View에 등록된 메뉴를 기준으로 용기와 유체 설정을 준비합니다.
        if (!_drinkView.TryConfigureDrink(style.Menu))
        {
            // View 내부의 스타일 및 용기 연결을 확인하도록 안내합니다.
            Debug.LogWarning("음료 설정에 실패했습니다. View의 Drink Styles와 Drink Vessels를 확인해 주세요.", this);
            return;
        }

        // 기존 Ready와 Start 연출을 거쳐 미니게임을 실행합니다.
        _drinkView.Show();
    }
#endif
}
