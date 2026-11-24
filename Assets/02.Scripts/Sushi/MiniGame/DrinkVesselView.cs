using UnityEngine;

/// <summary>용기 한 종류의 이미지와 발사 기준점을 묶어 전환합니다.</summary>
public sealed class DrinkVesselView : MonoBehaviour
{
    // 이 연결 묶음이 담당하는 용기 종류입니다.
    [SerializeField] DrinkVesselKind _kind;
    // 이 컴포넌트는 항상 켜진 별도 바인딩 오브젝트에 배치합니다.
    [SerializeField] GameObject _pourRoot, _cupFrontRoot;
    // 컵 뒤쪽 그림은 선택 사항이며 없으면 투명 단면으로 표시합니다.
    [SerializeField] GameObject _cupBackRoot;
    // 따르는 잔 이미지와 함께 회전할 기준점들입니다.
    [SerializeField] UnityEngine.UI.Image _pourImage;
    [SerializeField] RectTransform _pivot, _mouth, _direction;
    // 기존 미니게임과 같은 낮음, 적정, 높음 순서의 기울기입니다.
    [SerializeField] Vector3 _angles = new Vector3(120f, 150f, 170f);

    public DrinkVesselKind Kind => _kind;
    public RectTransform Pivot => _pivot;
    public RectTransform Mouth => _mouth;
    public RectTransform Direction => _direction;
    public Vector3 Angles => _angles;
    public bool IsConfigured => _pourRoot != null && _cupFrontRoot != null &&
        _pourImage != null && _pivot != null && _mouth != null && _direction != null;

    /// <summary>이 종류의 따르는 잔과 받는 컵을 함께 표시하거나 숨깁니다.</summary>
    public void SetVisible(bool visible)
    {
        // 따르는 잔 전체를 전환합니다.
        if (_pourRoot != null) _pourRoot.SetActive(visible);
        // 액체 앞에 표시하는 컵 윤곽을 전환합니다.
        if (_cupFrontRoot != null) _cupFrontRoot.SetActive(visible);
        // 뒤쪽 이미지가 연결된 경우에만 함께 전환합니다.
        if (_cupBackRoot != null) _cupBackRoot.SetActive(visible);
    }

    /// <summary>같은 컵 종류에서 음료별 따르는 잔 이미지를 교체합니다.</summary>
    public void SetPourStyle(DrinkMinigameStyle style)
    {
        // 연결 누락 또는 빈 이미지는 처리하지 않습니다.
        if (_pourImage == null || style == null || style.PourSprite == null) return;
        // 해당 음료의 따르는 잔 이미지를 선택합니다.
        _pourImage.sprite = style.PourSprite;
        // 이전 색 곱셈이 음료 원래 색상을 변경하지 않도록 초기화합니다.
        _pourImage.color = Color.white;
        // 이미지별 패딩을 보정해 같은 Mouth 위치에 잔 입구를 맞춥니다.
        _pourImage.rectTransform.sizeDelta = style.PourImageSize;
        _pourImage.rectTransform.anchoredPosition = style.PourImageOffset;
    }
}
