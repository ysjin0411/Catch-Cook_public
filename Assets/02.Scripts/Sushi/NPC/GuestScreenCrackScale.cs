using UnityEngine;

/// <summary>구간별 랜덤 기본 배율과 연속 충돌 횟수에 따라 크랙 이미지의 크기를 조절합니다.</summary>
[DisallowMultipleComponent]
public sealed class GuestScreenCrackScale : MonoBehaviour
{
    [Header("크기를 변경할 이미지")]
    [SerializeField] RectTransform _crackImage;

    [Header("첫 충돌의 랜덤 배율 범위")]
    [SerializeField, Min(0.01f)] float _minScale = 0.8f;
    [SerializeField, Min(0.01f)] float _randomStartMaxScale = 1.2f;

    [Header("연속 충돌 시 크기 증가")]
    [SerializeField, Min(0.01f)] float _maxScale = 2f;
    [SerializeField, Min(0f)] float _scaleStep = 0.2f;

    // 반복 적용해도 원본 크기를 기준으로 계산하기 위한 초기 스케일입니다.
    Vector3 _baseScale;

    /// <summary>이미지 참조를 확인하고 배율을 적용하기 전의 스케일을 저장합니다.</summary>
    void Awake()
    {
        // 연결이 누락되었다면 설정 오류를 알리고 초기화를 중단합니다.
        if (_crackImage == null)
        {
            // Inspector에서 수정할 컴포넌트를 찾을 수 있도록 참조를 함께 전달합니다.
            Debug.LogError("크랙 이미지를 연결해 주세요.", this);
            return;
        }

        // Canvas가 아닌 자식 이미지의 원래 크기를 보관합니다.
        _baseScale = _crackImage.localScale;
    }

    /// <summary>랜덤 기본 배율에 연속 충돌 증가분을 더하고 허용 범위 안에서 적용합니다.</summary>
    public void ApplyHitCount(int hitCount, float randomValue)
    {
        // 이미지가 연결되지 않았다면 크기 변경을 생략합니다.
        if (_crackImage == null) return;

        // Inspector의 범위가 역전되어도 유효한 최대 배율을 사용합니다.
        float maxScale = Mathf.Max(_minScale, _maxScale);

        // 랜덤 시작 배율의 상한을 전체 허용 범위 안으로 제한합니다.
        float startMax = Mathf.Clamp(_randomStartMaxScale, _minScale, maxScale);

        // 관리자가 보관한 구간별 랜덤값을 실제 시작 배율로 변환합니다.
        float startScale = Mathf.Lerp(_minScale, startMax, Mathf.Clamp01(randomValue));

        // 첫 충돌 이후의 추가 충돌 횟수만 크기 증가에 반영합니다.
        int additionalHits = Mathf.Max(0, hitCount - 1);

        // 연속 충돌 증가분을 더하되 최대 크기를 넘지 않도록 제한합니다.
        float multiplier = Mathf.Clamp(
            startScale + additionalHits * _scaleStep, _minScale, maxScale);

        // 현재 크기에 반복해서 곱하지 않고 원래 스케일에 배율을 적용합니다.
        _crackImage.localScale = _baseScale * multiplier;
    }
}
