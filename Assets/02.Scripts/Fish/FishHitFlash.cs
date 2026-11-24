using UnityEngine;

/// <summary>
/// 물고기의 렌더러에 피격 색을 적용하고 일정 시간 동안 깜빡입니다.
/// </summary>
public class FishHitFlash : MonoBehaviour
{
    // 피격 연출을 적용할 물고기 몸체 렌더러입니다.
    [SerializeField] private Renderer _bodyRenderer;

    // 피격 후 깜빡임을 유지할 전체 시간입니다.
    [SerializeField, Min(0f)]
    private float _hitFlashDuration = 0.5f;

    // 피격 색과 원래 색을 전환하는 간격입니다.
    [SerializeField, Min(0.01f)]
    private float _hitFlashInterval = 0.08f;

    // 피격 시 몸체에 적용할 색입니다.
    [SerializeField]
    private Color _hitFlashColor = Color.white;

    // Shader Graph의 Reference와 일치해야 합니다.
    private static readonly int HitFlashId =
        Shader.PropertyToID("_HitFlash");

    // Shader Graph의 피격 색상과 발광 프로퍼티입니다.
    private static readonly int HitColorId =
        Shader.PropertyToID("_HitColor");

    // 이번 재생에 사용할 피격 색입니다.
    private Color _color;

    // 공유 머티리얼을 변경하지 않고 개체별 값을 전달합니다.
    private MaterialPropertyBlock _propertyBlock;

    // 현재 연출의 진행 상태입니다.
    private bool _playing;
    private float _elapsed;
    private float _duration;
    private float _interval;

    // 같은 값을 매 프레임 반복 적용하지 않도록 보관합니다.
    private float _lastAmount = -1f;

    /// <summary>
    /// 렌더러별 속성 전달에 사용할 블록을 준비합니다.
    /// </summary>
    private void Awake()
    {
        // 머티리얼 복제 없이 사용할 속성 블록을 생성합니다.
        _propertyBlock = new MaterialPropertyBlock();
    }

    /// <summary>
    /// 자신의 Inspector 설정으로 피격 연출을 시작하거나 다시 시작합니다.
    /// </summary>
    public void Play()
    {
        // 활성 상태인 연출 컴포넌트만 재생합니다.
        if (!isActiveAndEnabled)
        {
            return;
        }

        // 지속 시간이 0이면 진행 중인 연출도 종료합니다.
        if (_hitFlashDuration <= 0f)
        {
            StopFlash();
            return;
        }

        // 이번 재생에 사용할 설정을 저장합니다.
        _duration = _hitFlashDuration;
        _interval = Mathf.Max(0.01f, _hitFlashInterval);
        _color = _hitFlashColor;

        // 재피격 시 마지막 피격 시점부터 시간을 다시 계산합니다.
        _elapsed = 0f;
        _playing = true;

        // 이전 표시 상태와 관계없이 피격 색을 즉시 적용합니다.
        _lastAmount = -1f;
        ApplyAmount(1f);
    }

    /// <summary>
    /// 경과 시간에 따라 피격 색과 원래 색을 번갈아 적용합니다.
    /// </summary>
    private void Update()
    {
        // 재생 중인 피격 연출이 없으면 처리하지 않습니다.
        if (!_playing)
        {
            return;
        }

        // 게임 시간에 맞춰 연출 시간을 진행합니다.
        _elapsed += Time.deltaTime;

        // 전체 시간이 끝나면 원래 색으로 복원합니다.
        if (_elapsed >= _duration)
        {
            StopFlash();
            return;
        }

        // 짝수 구간은 피격 색, 홀수 구간은 원래 색을 표시합니다.
        int phase = Mathf.FloorToInt(_elapsed / _interval);
        float amount = phase % 2 == 0 ? 1f : 0f;

        ApplyAmount(amount);
    }

    /// <summary>
    /// 피격 연출을 종료하고 원래 색으로 복원합니다.
    /// </summary>
    public void StopFlash()
    {
        // 다음 갱신부터 깜빡임을 처리하지 않습니다.
        _playing = false;
        _elapsed = 0f;

        // 셰이더의 피격 혼합값을 0으로 되돌립니다.
        ApplyAmount(0f);
    }

    /// <summary>
    /// 물고기 몸체 렌더러에 피격 색상과 혼합값을 전달합니다.
    /// </summary>
    private void ApplyAmount(float amount)
    {
        // 참조가 없거나 이전과 같은 값이면 처리를 생략합니다.
        if (_propertyBlock == null
            || _bodyRenderer == null
            || Mathf.Approximately(_lastAmount, amount))
        {
            return;
        }

        // 기존 속성을 읽고 피격 연출에 필요한 값만 변경합니다.
        _bodyRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(HitFlashId, amount);
        _propertyBlock.SetColor(HitColorId, _color);

        // 공유 머티리얼을 수정하지 않고 이 몸체에만 적용합니다.
        _bodyRenderer.SetPropertyBlock(_propertyBlock);

        // 마지막 적용값을 저장합니다.
        _lastAmount = amount;
    }

    /// <summary>
    /// 비활성화 시 흰색이 남지 않도록 연출을 종료합니다.
    /// </summary>
    private void OnDisable()
    {
        // 다시 활성화되었을 때는 원래 색으로 시작합니다.
        StopFlash();
    }
}