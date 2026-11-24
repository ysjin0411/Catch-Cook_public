using System;
using System.Collections;
using UnityEngine;

/// <summary>분노 연출의 회전, 접시와 Animator 진입·종료를 관리합니다.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public sealed class GuestAngryReaction : MonoBehaviour
{
    [SerializeField] Animator _animator;
    [SerializeField, Min(0.01f)] float _turnDuration = 0.25f;
    [SerializeField, Min(0.01f)] float _blendInDuration = 0.15f;
    [SerializeField, Min(0.01f)] float _blendOutDuration = 0.15f;
    [SerializeField] float _palmHeight = 0.015f;

    static readonly int StartHash = Animator.StringToHash("AngryReaction");
    static readonly int ReadyHash = Animator.StringToHash("Angry.Ready");

    GuestReactionEffects _effects;
    Rigidbody _body;
    Transform _head, _hand, _middle;
    GameObject _heldPlate;
    Action _onFinished;
    int _layer = -1;
    bool _throwEntered, _releaseRequested, _released, _finishing;
    bool _oldRootMotion;
    AnimatorCullingMode _oldCulling;

    public bool IsPlaying { get; private set; }

    /// <summary>게스트 자신의 Animator와 Rigidbody를 찾습니다.</summary>
    void Awake()
    {
        // 게스트 자신의 컴포넌트만 찾으며 씬 전체를 검색하지 않습니다.
        _body = GetComponent<Rigidbody>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
    }

    /// <summary>스포너가 전달한 씬의 공용 효과 관리자를 보관합니다.</summary>
    public void Initialize(GuestReactionEffects effects)
    {
        // 프리팹에서 씬 객체를 찾지 않고 생성자가 준 참조를 사용합니다.
        _effects = effects;
    }

    /// <summary>유효한 참조를 확인하고 회전부터 분노 연출을 시작합니다.</summary>
    public bool TryPlay(Action onFinished)
    {
        // 중복 실행과 비활성 Animator를 차단합니다.
        if (IsPlaying || !isActiveAndEnabled || _animator == null ||
            !_animator.isActiveAndEnabled || !_animator.isHuman ||
            _animator.runtimeAnimatorController == null) return false;

        // 스포너가 전달한 관리자에서 플레이어와 카메라를 확인합니다.
        if (_effects == null || !_effects.TryGetTargets(out _head, out _))
        {
            Debug.LogWarning("스포너의 Reaction Effects와 효과 관리자 연결을 확인하세요.", this);
            return false;
        }

        // 설정한 레이어와 복귀 상태가 있는지 시작할 때만 검사합니다.
        _layer = _animator.GetLayerIndex("Angry");
        if (_layer < 0 || !_animator.HasState(_layer, ReadyHash))
        {
            Debug.LogWarning("Angry 레이어에 기본 상태 Ready를 설정하세요.", this);
            return false;
        }

        // 손 본은 이번 연출을 시작할 때 한 번만 가져옵니다.
        _hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        _middle = _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        if (_hand == null) return false;

        // 연출 후 복원할 Animator 설정을 저장합니다.
        _oldRootMotion = _animator.applyRootMotion;
        _oldCulling = _animator.cullingMode;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.SetLayerWeight(_layer, 0f);

        // 회전 중에도 NPC가 퇴장하지 않도록 연출 상태를 먼저 활성화합니다.
        _onFinished = onFinished;
        _throwEntered = _releaseRequested = _released = _finishing = false;
        IsPlaying = true;

        // 설정 누락 시에도 영구 대기하지 않도록 제한 시간을 둡니다.
        Invoke(nameof(HandleTimeout), 12f);
        StartCoroutine(BeginRoutine());
        return true;
    }

    /// <summary>플레이어 쪽으로 회전한 뒤 Trigger를 한 번 전달합니다.</summary>
    IEnumerator BeginRoutine()
    {
        // 자리 옆으로 나온 시점의 방향을 회전 시작값으로 사용합니다.
        Quaternion start = transform.rotation;
        float elapsed = 0f;

        // 회전하는 동안에만 프레임별 보간을 실행합니다.
        while (elapsed < _turnDuration)
        {
            // 목표나 Animator가 사라지면 정상 종료 경로로 정리합니다.
            if (_head == null || _animator == null || !_animator.isActiveAndEnabled)
            {
                Complete();
                yield break;
            }

            // 수평 방향만 사용해 몸이 위아래로 기울지 않게 합니다.
            Vector3 direction = _head.position - transform.position;
            direction.y = 0f;
            Quaternion target = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up) : start;

            // 회전의 시작과 끝을 부드럽게 보간합니다.
            elapsed += Time.deltaTime;
            float ratio = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _turnDuration));
            Quaternion rotation = Quaternion.Slerp(start, target, Mathf.SmoothStep(0f, 1f, ratio));
            if (_body != null) _body.rotation = rotation;
            transform.rotation = rotation;
            yield return null;
        }

        // 상태 전환은 Animator에 맡기고 시작 요청만 한 번 보냅니다.
        _animator.ResetTrigger(StartHash);
        _animator.SetTrigger(StartHash);

        // 상체 레이어가 갑자기 나타나지 않도록 잠깐만 가중치를 보간합니다.
        yield return BlendWeight(1f, _blendInDuration);
    }

    /// <summary>Throw 상태 진입 알림을 받아 손에 접시를 준비합니다.</summary>
    public void OnThrowEntered()
    {
        // 같은 상태 진입 알림으로 접시가 중복 생성되는 것을 막습니다.
        if (!IsPlaying || _finishing || _throwEntered) return;
        _throwEntered = true;

        // 생성된 접시는 LateUpdate에서 현재 손바닥 위치를 따라갑니다.
        if (_effects != null) _heldPlate = _effects.CreateHeldPlate(_animator);
    }

    /// <summary>Animation Event의 발사 요청을 이번 프레임에 예약합니다.</summary>
    public void RequestReleasePlate()
    {
        // 이미 발사했거나 연출이 끝났으면 중복 이벤트를 무시합니다.
        if (!IsPlaying || !_throwEntered || _released || _finishing) return;

        // Animator의 본 평가가 끝난 LateUpdate에서 실제로 발사합니다.
        _releaseRequested = true;
    }

    /// <summary>손에 든 접시를 배치하고 예약된 발사를 한 번 실행합니다.</summary>
    void LateUpdate()
    {
        // 손에 접시가 있는 짧은 구간에만 위치를 갱신합니다.
        if (_heldPlate == null) return;
        if (_hand == null || _effects == null || !_effects.isActiveAndEnabled)
        {
            Complete();
            return;
        }

        // 손목과 중지 시작점 사이를 손바닥 중심으로 근사합니다.
        Vector3 center = _middle != null
            ? Vector3.Lerp(_hand.position, _middle.position, 0.55f) : _hand.position;
        _heldPlate.transform.SetPositionAndRotation(
            center + Vector3.up * _palmHeight,
            Quaternion.Euler(0f, transform.eulerAngles.y, 0f));

        // 현재 프레임의 손 위치에서 발사하고 소유권을 효과 관리자에 넘깁니다.
        if (!_releaseRequested || _released) return;
        _released = true;
        _effects.Launch(_heldPlate);
        _heldPlate = null;
    }

    /// <summary>Throw에서 Ready로 전환을 완료하면 상체 레이어를 정리합니다.</summary>
    public void OnThrowExited()
    {
        // 정상 연출의 종료 알림만 한 번 처리합니다.
        if (!IsPlaying || !_throwEntered || _finishing) return;
        _finishing = true;

        // 진행 중인 보간이 겹치지 않게 정리하고 종료 보간을 시작합니다.
        StopAllCoroutines();
        StartCoroutine(FinishRoutine());
    }

    /// <summary>상체 레이어를 제거한 뒤 기존 NPC 퇴장 흐름을 호출합니다.</summary>
    IEnumerator FinishRoutine()
    {
        // Ready와 Base Idle의 자세 차이도 부드럽게 없앱니다.
        yield return BlendWeight(0f, _blendOutDuration);

        // 접시 비행과 관계없이 게스트 자신의 연출을 완료합니다.
        Complete();
    }

    /// <summary>진입 또는 종료하는 짧은 시간에만 레이어 가중치를 보간합니다.</summary>
    IEnumerator BlendWeight(float target, float duration)
    {
        // 현재 가중치에서 목표 가중치까지 보간합니다.
        float start = _animator.GetLayerWeight(_layer);
        float elapsed = 0f;
        while (elapsed < duration && _animator != null)
        {
            // 진행도에 완만한 곡선을 적용합니다.
            elapsed += Time.deltaTime;
            float ratio = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            _animator.SetLayerWeight(_layer, Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, ratio)));
            yield return null;
        }

        // 종료 프레임에서는 목표 가중치를 정확히 적용합니다.
        if (_animator != null) _animator.SetLayerWeight(_layer, target);
    }

    /// <summary>설정 누락으로 연출이 끝나지 않을 때 대기를 해제합니다.</summary>
    void HandleTimeout()
    {
        // Trigger, 전환 또는 Behaviour 연결 누락을 확인할 수 있게 알립니다.
        Debug.LogWarning("분노 연출 시간 초과: Trigger와 Throw Behaviour를 확인하세요.", this);
        Complete();
    }

    /// <summary>정리 후 저장해 둔 완료 콜백을 한 번 호출합니다.</summary>
    void Complete()
    {
        // 중복 종료와 중복 퇴장을 막습니다.
        if (!IsPlaying) return;
        Action finished = _onFinished;
        Cleanup();
        finished?.Invoke();
    }

    /// <summary>미발사 접시를 제거하고 연출 이전 Animator 설정을 복원합니다.</summary>
    void Cleanup()
    {
        // 이 컴포넌트에서 시작한 보간과 제한 시간 호출을 취소합니다.
        StopAllCoroutines();
        CancelInvoke(nameof(HandleTimeout));

        // 발사된 접시는 참조가 비워져 있으므로 정리하지 않습니다.
        if (_heldPlate != null)
        {
            if (_effects != null) _effects.ReleasePlate(_heldPlate);
            else Destroy(_heldPlate);
            _heldPlate = null;
        }

        // Animator 상태 변경 중 재진입하더라도 종료 처리를 반복하지 않습니다.
        bool wasPlaying = IsPlaying;
        IsPlaying = false;
        _onFinished = null;
        if (!wasPlaying || _animator == null) return;

        // 평소에는 상체 레이어가 기본 동작에 영향을 주지 않도록 합니다.
        _animator.ResetTrigger(StartHash);
        _animator.SetLayerWeight(_layer, 0f);

        // 중도 취소된 경우에만 다음 사용을 위해 대기 상태로 초기화합니다.
        if (!_finishing && _animator.HasState(_layer, ReadyHash))
            _animator.Play(ReadyHash, _layer, 0f);

        // 화면 밖 평가와 루트 모션 설정을 연출 이전 값으로 복원합니다.
        _animator.applyRootMotion = _oldRootMotion;
        _animator.cullingMode = _oldCulling;
    }

    /// <summary>비활성화 시 자원을 정리하고 NPC의 기존 중단 감지를 사용합니다.</summary>
    void OnDisable()
    {
        // 비활성화 중에는 퇴장 콜백을 직접 호출하지 않습니다.
        Cleanup();
    }
}
