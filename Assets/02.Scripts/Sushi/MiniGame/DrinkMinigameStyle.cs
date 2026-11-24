using SushiPouringV3;
using UnityEngine;

/// <summary>음료를 따르는 잔과 받는 컵의 외형 종류입니다.</summary>
public enum DrinkVesselKind { Ade, Beer, Tea }

/// <summary>한 음료의 표시 색상, 따르는 잔 이미지 및 컵 계산 설정을 보관합니다.</summary>
[CreateAssetMenu(fileName = "DrinkStyle", menuName = "Game/Sushi/Drink Minigame Style")]
public sealed class DrinkMinigameStyle : ScriptableObject
{
    // 이름 문자열 대신 실제 메뉴 에셋으로 손님의 주문과 연결합니다.
    [SerializeField] Drink _menu;
    // 에이드 세 종류는 같은 컵 구조를 공유합니다.
    [SerializeField] DrinkVesselKind _vesselKind;
    // 기울어지는 용기에 표시할 해당 음료 이미지입니다.
    [SerializeField] Sprite _pourSprite;
    // 생성 이미지마다 다른 투명 여백을 보정할 UI 크기와 중심 위치입니다.
    [SerializeField] Vector2 _pourImageSize = new Vector2(240f, 240f);
    [SerializeField] Vector2 _pourImageOffset;
    // 물방울, 물줄기 및 컵 안 액체에 공통 적용할 색상입니다.
    [SerializeField] Color _liquidColor = Color.white;
    // 액체 표면의 밝은 반사광 색상입니다.
    [SerializeField] Color _highlightColor = Color.white;
    // 받는 컵의 실제 표시 모양과 맞춘 시뮬레이션 설정입니다.
    [SerializeField] FluidPourConfig _config;

    public Drink Menu => _menu;
    public DrinkVesselKind VesselKind => _vesselKind;
    public Sprite PourSprite => _pourSprite;
    public Vector2 PourImageSize => _pourImageSize;
    public Vector2 PourImageOffset => _pourImageOffset;
    public Color LiquidColor => _liquidColor;
    public Color HighlightColor => _highlightColor;
    public FluidPourConfig Config => _config;
}
