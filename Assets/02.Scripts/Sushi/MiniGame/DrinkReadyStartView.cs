using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>Ready와 Start를 연출하고 Start 표시 완료를 알립니다.</summary>
[DisallowMultipleComponent]
public sealed class DrinkReadyStartView : MonoBehaviour
{
    // 문구를 표시할 자식 텍스트입니다.
    [SerializeField] TMP_Text _label;
    // 문구의 등장, Ready 유지, 퇴장 시간입니다.
    [SerializeField, Min(0.01f)] float _fadeInTime = 0.2f;
    [SerializeField, Min(0f)] float _readyHoldTime = 0.6f;
    [SerializeField, Min(0.01f)] float _fadeOutTime = 0.15f;
    // 현재 트윈과 중복 이벤트 방지 상태입니다.
    Sequence _sequence;
    bool _startNotified;

    /// <summary>Start가 완전히 나타났을 때 한 번 발생합니다.</summary>
    internal event Action StartShown;

    /// <summary>Ready부터 Start까지 연출합니다.</summary>
    internal bool Play()
    {
        // 이전 연출을 정리하고 필수 참조를 검사합니다.
        Stop();
        if (!isActiveAndEnabled || _label == null) return false;
        // 자식 문구를 투명한 Ready 상태로 준비합니다.
        _label.gameObject.SetActive(true);
        _label.text = "Ready";
        _label.alpha = 0f;
        // 게임 일시정지와 함께 멈추는 연출을 구성합니다.
        _sequence = DOTween.Sequence().SetUpdate(false);
        _sequence.Append(_label.DOFade(1f, _fadeInTime));
        _sequence.AppendInterval(_readyHoldTime);
        _sequence.Append(_label.DOFade(0f, _fadeOutTime));
        _sequence.AppendCallback(ShowStartText);
        _sequence.Append(_label.DOFade(1f, _fadeInTime));
        _sequence.AppendCallback(NotifyStartShown);
        return true;
    }

    /// <summary>투명한 문구를 Start로 교체합니다.</summary>
    void ShowStartText()
    {
        // 다음 페이드인에 사용할 문구를 지정합니다.
        _label.text = "Start";
    }

    /// <summary>Start 표시 완료를 한 번만 통지합니다.</summary>
    void NotifyStartShown()
    {
        // 중복 통지를 막고 구독자에게 시점을 전달합니다.
        if (_startNotified) return;
        _startNotified = true;
        StartShown?.Invoke();
    }

    /// <summary>트윈을 중단하고 문구를 숨깁니다.</summary>
    internal void Stop()
    {
        // 콜백을 완료시키지 않고 현재 트윈만 제거합니다.
        if (_sequence != null) { _sequence.Kill(false); _sequence = null; }
        // 같은 오브젝트에 잘못 연결되어도 재진입 시 상태가 정리되어 있게 합니다.
        _startNotified = false;
        if (_label != null) { _label.alpha = 0f; _label.gameObject.SetActive(false); }
    }

    /// <summary>비활성화 이후 지연 콜백이 남지 않도록 정리합니다.</summary>
    void OnDisable()
    {
        // 진행 중인 연출을 취소합니다.
        Stop();
    }
}
