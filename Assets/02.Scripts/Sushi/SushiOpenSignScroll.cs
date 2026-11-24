using UnityEngine;

/// <summary>3D 전광판의 OPEN 메시를 위로 내보내고 아래에서 다시 올리는 표시 연출을 담당합니다.</summary>
[DisallowMultipleComponent]
public sealed class SushiOpenSignScroll : MonoBehaviour
{
    [Header("스크롤 전용 재질을 사용하는 LED 메시")]
    [SerializeField] MeshRenderer _letters;

    [Header("반복 시간 (초)")]
    [SerializeField, Min(0f)] float _holdDuration = 2.5f;
    [SerializeField, Min(0.01f)] float _exitDuration = 0.65f;
    [SerializeField, Min(0f)] float _blankDuration = 0.25f;
    [SerializeField, Min(0.01f)] float _enterDuration = 0.65f;

    [Header("전광판 메시의 로컬 좌표 기준")]
    [SerializeField, Min(0.001f)] float _windowHalfHeight = 0.30f;
    [SerializeField, Min(0.001f)] float _travelDistance = 0.61f;

    // 재질 복제 없이 각 전광판의 표시 상태를 독립적으로 전달합니다.
    static readonly int OffsetId = Shader.PropertyToID("_ScrollOffset");
    static readonly int WindowId = Shader.PropertyToID("_WindowHalfHeight");
    MaterialPropertyBlock _properties;
    float _elapsed;

    /// <summary>전광판이 활성화되면 OPEN이 온전히 표시된 상태에서 반복을 시작합니다.</summary>
    void OnEnable()
    {
        // 재활성화 시 이전 주기의 중간 위치가 남지 않게 초기화합니다.
        _elapsed = 0f;
        ApplyOffset(0f);
    }

    /// <summary>게임 시간에 맞춰 정지, 퇴장, 공백, 입장 단계를 반복합니다.</summary>
    void Update()
    {
        // 인스펙터 값이 잘못 입력되어도 나눗셈이 0이 되지 않게 제한합니다.
        float hold = Mathf.Max(0f, _holdDuration);
        float exit = Mathf.Max(0.01f, _exitDuration);
        float blank = Mathf.Max(0f, _blankDuration);
        float enter = Mathf.Max(0.01f, _enterDuration);
        float cycle = hold + exit + blank + enter;

        // 일시 정지 시 연출도 함께 멈추며, 긴 실행에서도 시간 값이 커지지 않게 합니다.
        _elapsed = Mathf.Repeat(_elapsed + Time.deltaTime, cycle);
        float phase = _elapsed - hold;
        float offset;

        // 읽을 수 있는 시간 동안 중앙에 글자를 유지합니다.
        if (phase < 0f) offset = 0f;
        // 위쪽으로 부드럽게 이동하며 표시창 바깥 부분은 셰이더에서 자릅니다.
        else if (phase < exit) offset = Mathf.SmoothStep(0f, _travelDistance, phase / exit);
        // 완전히 사라진 위치에서 짧게 대기합니다.
        else if (phase < exit + blank) offset = _travelDistance;
        // 보이지 않는 아래쪽으로 되돌린 뒤 같은 위쪽 방향으로 들어옵니다.
        else offset = Mathf.SmoothStep(-_travelDistance, 0f, (phase - exit - blank) / enter);

        // 이동량만 전달하고 프레임 및 배경 메시의 위치는 유지합니다.
        ApplyOffset(offset);
    }

    /// <summary>공유 재질을 변경하지 않고 이 전광판의 이동량과 표시창 크기를 전달합니다.</summary>
    void ApplyOffset(float offset)
    {
        // 연결되지 않은 프리팹 편집 중에는 렌더러 접근을 생략합니다.
        if (_letters == null) return;

        // 블록은 최초 한 번만 생성하며 기존에 설정된 다른 속성도 보존합니다.
        if (_properties == null) _properties = new MaterialPropertyBlock();
        _letters.GetPropertyBlock(_properties);
        _properties.SetFloat(OffsetId, offset);
        _properties.SetFloat(WindowId, Mathf.Max(0.001f, _windowHalfHeight));
        _letters.SetPropertyBlock(_properties);
    }

    /// <summary>컴포넌트를 끄면 기본 OPEN 표시로 복원합니다.</summary>
    void OnDisable()
    {
        // 편집 화면이나 연출을 끈 상태에 중간 프레임이 남지 않게 합니다.
        ApplyOffset(0f);
    }
}
