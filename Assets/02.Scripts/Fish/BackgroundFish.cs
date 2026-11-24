using UnityEngine;

/// <summary>
/// 배경 전용 물고기의 밝기와 수영 애니메이션을 초기화합니다.
/// 개체별 갱신과 물리 처리는 배경 무리 관리자에게 맡깁니다.
/// </summary>
public sealed class BackgroundFish : MonoBehaviour
{
    // 생성된 실제 크기의 몸체와 수영 동작을 감싸는 월드 반지름입니다.
    [SerializeField, Min(0.01f)] private float _bodyRadius = 0.5f;

    // 모든 Variant가 공유하는 기본 수영 애니메이션 속도입니다.
    [SerializeField, Min(0.01f)] private float _animationSpeed = 1f;

    // 보이지 않는 물고기의 애니메이션 계산을 생략합니다.
    [SerializeField] private bool _cullAnimation = true;

    // 기존 물고기 셰이더와 동일한 밝기 프로퍼티입니다.
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");

    // 무리의 회피 범위에 포함할 실제 몸체 반지름입니다.
    public float BodyRadius => Mathf.Max(0.01f, _bodyRadius);

    /// <summary>
    /// 외형을 생성할 때 한 번만 밝기와 애니메이션 설정을 적용합니다.
    /// </summary>
    public void Initialize(float brightness)
    {
        // 머티리얼 복제를 피하고 기존 렌더러 속성을 보존합니다.
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer body in GetComponentsInChildren<Renderer>(true))
        {
            // 각 렌더러의 속성 블록에 그룹 밝기만 덮어씁니다.
            block.Clear();
            body.GetPropertyBlock(block);
            block.SetFloat(BrightnessId, Mathf.Clamp01(brightness));
            body.SetPropertyBlock(block);
        }

        // 외형 애니메이션만 사용하고 루트 이동은 무리에서 처리합니다.
        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
        {
            animator.applyRootMotion = false;
            animator.speed = Mathf.Max(0.01f, _animationSpeed);
            animator.cullingMode = _cullAnimation
                ? AnimatorCullingMode.CullCompletely
                : AnimatorCullingMode.AlwaysAnimate;
        }
    }

    /// <summary>
    /// 선택한 배경 물고기의 실제 회피 반지름을 표시합니다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // 프리팹에 보존된 원래 배율을 포함한 월드 반지름을 표시합니다.
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, BodyRadius);
    }
}
