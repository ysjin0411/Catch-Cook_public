using UnityEngine;

/// <summary>
/// 물고기 렌더러에 그룹별 밝기를 적용합니다.
/// </summary>
public class FishVisual : MonoBehaviour
{
    // 밝기를 적용할 몸체 렌더러입니다.
    [SerializeField] private Renderer _bodyRenderer;

    // Shader Graph의 Brightness Reference와 일치하는 식별자입니다.
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");

    // 렌더러별 셰이더 값을 전달하는 속성 블록입니다.
    private MaterialPropertyBlock _propertyBlock;

    /// <summary>
    /// 기존 렌더러 속성을 유지하면서 소속 그룹의 밝기를 적용합니다.
    /// </summary>
    public void SetBrightness(float brightness)
    {
        // 렌더러 연결이 누락되었다면 오류를 알리고 중단합니다.
        if (_bodyRenderer == null)
        {
            Debug.LogError("FishVisual의 Body Renderer를 연결해 주세요.", this);
            return;
        }

        // 최초 호출 시 속성 블록을 생성합니다.
        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        // 피격 효과 등 기존 렌더러 속성을 가져옵니다.
        _bodyRenderer.GetPropertyBlock(_propertyBlock);

        // 그룹 밝기를 0~1 범위로 제한하여 설정합니다.
        _propertyBlock.SetFloat(BrightnessId, Mathf.Clamp01(brightness));

        // 변경한 속성을 이 렌더러에 적용합니다.
        _bodyRenderer.SetPropertyBlock(_propertyBlock);
    }
}
