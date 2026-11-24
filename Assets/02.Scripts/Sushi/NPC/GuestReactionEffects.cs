using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 게스트와 독립적으로 접시의 생성·정리와
/// 화면 균열 및 카메라 흔들림을 관리합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuestReactionEffects : MonoBehaviour
{
    [Header("생성할 프리팹")]
    [SerializeField] GameObject _platePrefab;
    [SerializeField] CanvasGroup _crackPrefab;

    [Header("현재 플레이어와 출력 카메라")]
    [SerializeField] Transform _playerHead;
    [SerializeField] Camera _outputCamera;

    [Header("접시 비행")]
    [SerializeField, Min(0.05f)] float _headFlightTime = 0.4f;
    [SerializeField, Min(0.05f)] float _screenFlightTime = 0.35f;
    [SerializeField, Min(0f)] float _arcHeight = 0.18f;
    [SerializeField] float _spinSpeed = 1080f;

    [Header("직교 카메라에서 화면 접근 시 확대 비율")]
    [SerializeField, Min(1f)] float _orthographicEndScale = 3f;

    [Header("균열이 완전히 사라질 때까지의 시간")]
    [SerializeField, Min(0.01f)] float _crackLifetime = 3f;

    [Header("카메라 흔들림")]
    [SerializeField] CinemachineImpulseSource _impulseSource;
    [SerializeField] CameraController _cameraController;
    [SerializeField, Min(0f)] float _shakeStrength = 0.3f;
    [SerializeField, Min(0.01f)] float _shakeDuration = 0.18f;

    /// <summary>접시가 실제로 발사됐을 때 구독자에게 알립니다.</summary>
    public event Action PlateThrown;
    public event Action ScreenHit;

    readonly HashSet<GameObject> _plates = new();

    CanvasGroup _visibleCrack;
    float _crackElapsed;

    [Header("크랙 연속 충돌 판정")]
    [SerializeField, Min(0f)] float _comboWindow = 2f;

    // 크랙이 파괴되어도 유지할 마지막 충돌 시각입니다.
    float _lastScreenHitTime = float.NegativeInfinity;

    // 이번 구간의 연속 충돌 횟수입니다.
    int _consecutiveScreenHits;

    // 이번 구간에서 공통으로 사용할 0~1 사이의 랜덤값입니다.
    float _crackScaleRandom;

    // 현재 표시 중인 크랙의 크기 조절 컴포넌트입니다.
    GuestScreenCrackScale _visibleCrackScale;

    /// <summary>충돌 간격으로 연속 여부를 판단하고 새 구간의 랜덤값을 정합니다.</summary>
    void RegisterScreenHit()
    {
        // 기존 페이드와 동일한 게임 시간을 기준으로 판단합니다.
        float now = Time.time;

        // 이전 충돌이 있고 제한 시간 안에 다시 맞았는지 확인합니다.
        bool isConsecutive = _consecutiveScreenHits > 0 &&
            now - _lastScreenHitTime <= _comboWindow;

        // 연속 충돌이면 기존 랜덤값을 유지하면서 횟수를 증가시킵니다.
        if (isConsecutive)
        {
            // 추가 충돌에 해당하는 크기 증가분을 누적합니다.
            _consecutiveScreenHits++;
        }
        else
        {
            // 연속이 끊겼다면 첫 충돌부터 다시 시작합니다.
            _consecutiveScreenHits = 1;

            // 새 구간의 기본 크기에 사용할 랜덤 비율을 정합니다.
            _crackScaleRandom = UnityEngine.Random.value;
        }

        // 다음 충돌의 연속 여부를 판단할 기준 시각을 저장합니다.
        _lastScreenHitTime = now;
    }

    /// <summary>
    /// 현재 플레이어의 머리와 실제 화면을 출력하는 카메라를 조회합니다.
    /// </summary>
    public bool TryGetTargets(out Transform head, out Camera outputCamera)
    {
        // 머리를 직접 지정하지 않았다면 현재 지상 플레이어에서 찾습니다.
        if (_playerHead == null)
        {
            GroundHero hero = FindFirstObjectByType<GroundHero>();

            // 지상 플레이어의 Humanoid Animator를 가져옵니다.
            Animator animator = hero != null
                ? hero.GetComponentInChildren<Animator>()
                : null;

            // Humanoid 골격의 머리 본을 목표로 사용합니다.
            if (animator != null && animator.isHuman)
            {
                _playerHead = animator.GetBoneTransform(
                    HumanBodyBones.Head);
            }
        }

        // 카메라를 지정하지 않았다면 MainCamera를 사용합니다.
        if (_outputCamera == null)
        {
            _outputCamera = Camera.main;
        }

        // 호출자에게 현재 참조를 전달합니다.
        head = _playerHead;
        outputCamera = _outputCamera;

        // 활성 상태의 목표와 카메라, 접시 프리팹이 있어야 시작할 수 있습니다.
        return isActiveAndEnabled &&
               head != null &&
               head.gameObject.activeInHierarchy &&
               outputCamera != null &&
               outputCamera.isActiveAndEnabled &&
               _platePrefab != null;
    }

    /// <summary>
    /// 게스트 손바닥 추정면적의 1.5배가 되는 접시를 생성합니다.
    /// </summary>
    public GameObject CreateHeldPlate(Animator guestAnimator)
    {
        // 접시와 Humanoid 골격이 준비되지 않았다면 생성하지 않습니다.
        if (_platePrefab == null ||
            guestAnimator == null ||
            !guestAnimator.isHuman)
        {
            return null;
        }

        // 손바닥의 길이와 너비를 계산할 본을 가져옵니다.
        Transform hand = guestAnimator.GetBoneTransform(
            HumanBodyBones.RightHand);

        Transform middle = guestAnimator.GetBoneTransform(
            HumanBodyBones.RightMiddleProximal);

        Transform index = guestAnimator.GetBoneTransform(
            HumanBodyBones.RightIndexProximal);

        Transform little = guestAnimator.GetBoneTransform(
            HumanBodyBones.RightLittleProximal);

        // 손목 본이 없으면 손에 배치할 수 없습니다.
        if (hand == null) return null;

        // 손가락 본이 없는 골격에서는 생성한 프리팹의 기본 지름을 사용합니다.
        float diameter = 0.239f;

        // 손가락 본이 있으면 손바닥 면적을 근사합니다.
        if (middle != null && index != null && little != null)
        {
            float length = Vector3.Distance(
                hand.position, middle.position);

            float width = Vector3.Distance(
                index.position, little.position);

            float palmArea = length * width;

            // 면적 비율을 원의 지름으로 환산합니다.
            if (palmArea > 0.000001f)
            {
                diameter = 2f * Mathf.Sqrt(
                    palmArea * 1.5f / Mathf.PI);
            }
        }

        // 게스트의 수명과 손 본의 스케일에서 독립하도록 부모 없이 생성합니다.
        GameObject plate = Instantiate(_platePrefab);

        // 씬 전환 시 정리할 수 있도록 생성한 접시를 추적합니다.
        _plates.Add(plate);

        // 실제 메시 지름을 기준으로 접시 크기를 맞춥니다.
        MeshFilter filter = plate.GetComponent<MeshFilter>();

        if (filter != null &&
            filter.sharedMesh != null &&
            filter.sharedMesh.bounds.size.x > 0f)
        {
            float scale = diameter / filter.sharedMesh.bounds.size.x;
            plate.transform.localScale = Vector3.one * scale;
        }

        // 생성한 접시를 게스트 연출 컴포넌트에 전달합니다.
        return plate;
    }

    /// <summary>
    /// 접시에게 플레이어 머리와 화면까지의 독립적인 비행을 시작시킵니다.
    /// </summary>
    public bool Launch(GameObject plate)
    {
        // 발사 직전에 목표가 사라졌다면 접시만 정리합니다.
        if (plate == null ||
            !TryGetTargets(out Transform head, out Camera outputCamera))
        {
            ReleasePlate(plate);
            return false;
        }

        // 프리팹에 비행 컴포넌트가 없다면 발사할 때 추가합니다.
        GuestThrownPlate flight = plate.GetComponent<GuestThrownPlate>();

        if (flight == null)
        {
            flight = plate.AddComponent<GuestThrownPlate>();
        }

        // 손님과 별개로 비행을 진행할 정보를 전달합니다.
        flight.Launch(
            this,
            head,
            outputCamera,
            _headFlightTime,
            _screenFlightTime,
            _arcHeight,
            _spinSpeed,
            _orthographicEndScale);

        // 발사에 성공한 경우에만 Presenter 등 구독자에게 알립니다.
        PlateThrown?.Invoke();

        return true;
    }

    /// <summary>
    /// 화면 충돌 시 균열을 표시하고 카메라를 흔듭니다.
    /// </summary>
    public void NotifyScreenHit()
    {
        // 여러 접시가 충돌해도 화면 균열은 한 장을 재사용합니다.
        if (_crackPrefab != null)
        {
            // 화면에 실제로 도착한 시점을 기준으로 연속 횟수를 갱신합니다.
            RegisterScreenHit();

            // 아직 표시 중인 균열이 없다면 새로 생성합니다.
            if (_visibleCrack == null)
            {
                _visibleCrack = Instantiate(_crackPrefab);

                // 생성 시 한 번만 프리팹의 크기 조절 컴포넌트를 가져옵니다.
                _visibleCrackScale = _visibleCrack.GetComponent<GuestScreenCrackScale>();
            }

            // 균열의 표시 상태와 수명을 초기화합니다.
            // 크랙을 다시 생성했어도 관리자가 기억한 구간의 크기를 이어서 적용합니다.
            if (_visibleCrackScale != null)
            {
                // 연속 횟수와 구간별 랜덤값을 함께 전달합니다.
                _visibleCrackScale.ApplyHitCount(_consecutiveScreenHits, _crackScaleRandom);
            }

            // 충돌한 크랙을 다시 선명하게 표시합니다.
            _visibleCrack.alpha = 1f;
            _visibleCrack.blocksRaycasts = false;
            _visibleCrack.interactable = false;
            _crackElapsed = 0f;
        }

        // ImpulseSource가 연결되어 있다면 해당 소스로 흔들림을 발생시킵니다.
        if (_impulseSource != null &&
            _impulseSource.ImpulseDefinition != null)
        {
            _impulseSource.ImpulseDefinition.ImpulseDuration =
                _shakeDuration;

            _impulseSource.GenerateImpulseWithForce(_shakeStrength);
        }
        else if (_cameraController != null)
        {
            // 별도 ImpulseSource가 없으면 기존 카메라 컨트롤러를 사용합니다.
            _cameraController.ShakeCamera(
                _shakeStrength, _shakeDuration);
        }

        // Notify listeners after the screen impact effects, even without a crack prefab.
        ScreenHit?.Invoke();
    }

    /// <summary>
    /// 화면 균열을 설정한 N초에 걸쳐 서서히 사라지게 합니다.
    /// </summary>
    void Update()
    {
        // 표시 중인 균열이 없으면 처리하지 않습니다.
        if (_visibleCrack == null) return;

        // 균열 표시 후 경과 시간을 계산합니다.
        _crackElapsed += Time.deltaTime;

        // 전체 수명에서 얼마나 진행되었는지 계산합니다.
        float ratio = Mathf.Clamp01(
            _crackElapsed / Mathf.Max(0.01f, _crackLifetime));

        // 처음에는 선명하고 끝에는 투명하도록 변경합니다.
        _visibleCrack.alpha =
            1f - Mathf.SmoothStep(0f, 1f, ratio);

        // 수명이 끝난 균열 오브젝트를 제거합니다.
        if (ratio >= 1f)
        {
            Destroy(_visibleCrack.gameObject);
            _visibleCrack = null;
            // 사라진 크랙의 컴포넌트 참조만 비우고 연속 충돌 기록은 유지합니다.
            _visibleCrackScale = null;
        }
    }

    /// <summary>
    /// 사용을 마친 접시를 추적 목록에서 제거하고 파괴합니다.
    /// </summary>
    public void ReleasePlate(GameObject plate)
    {
        // 이미 사라진 접시는 다시 제거하지 않습니다.
        if (plate == null) return;

        // 관리 목록에서 먼저 제외합니다.
        _plates.Remove(plate);

        // 실제 접시 오브젝트를 제거합니다.
        Destroy(plate);
    }

    /// <summary>
    /// 외부에서 파괴된 접시를 추적 목록에서도 제외합니다.
    /// </summary>
    public void ForgetPlate(GameObject plate)
    {
        // 접시의 OnDestroy에서 호출하므로 다시 Destroy하지 않습니다.
        _plates.Remove(plate);
    }

    /// <summary>
    /// 관리자 비활성화나 씬 전환 시 남은 연출을 정리합니다.
    /// </summary>
    void OnDisable()
    {
        // 파괴 콜백에 의한 목록 변경을 피하도록 복사본을 순회합니다.
        foreach (GameObject plate in new List<GameObject>(_plates))
        {
            // 아직 남아 있는 접시만 제거합니다.
            if (plate != null)
            {
                Destroy(plate);
            }
        }

        // 접시 추적 목록을 비웁니다.
        _plates.Clear();

        // 표시 중인 균열도 함께 제거합니다.
        if (_visibleCrack != null)
        {
            Destroy(_visibleCrack.gameObject);
        }

        // 이전 균열 참조를 정리합니다.
        _visibleCrack = null;

        // 비활성화 이후에는 새로운 연속 충돌 구간으로 시작합니다.
        _visibleCrackScale = null;
        _consecutiveScreenHits = 0;
        _lastScreenHitTime = float.NegativeInfinity;
        _crackScaleRandom = 0f;
    }
}