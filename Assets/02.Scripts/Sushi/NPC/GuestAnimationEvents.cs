using UnityEngine;

/// <summary>Visual의 Animation Event를 루트의 연출 컴포넌트에 전달합니다.</summary>
[DisallowMultipleComponent]
public sealed class GuestAnimationEvents : MonoBehaviour
{
    GuestAngryReaction _reaction;

    /// <summary>같은 게스트의 루트 연출 컴포넌트를 한 번 찾습니다.</summary>
    void Awake()
    {
        // 씬 검색 없이 자신의 부모 계층에서만 찾습니다.
        _reaction = GetComponentInParent<GuestAngryReaction>();
    }

    /// <summary>던지기 클립의 Animation Event에서 발사 요청을 전달합니다.</summary>
    public void ReleasePlate()
    {
        // 실제 손 위치 계산과 발사는 루트 컴포넌트에 맡깁니다.
        if (_reaction != null) _reaction.RequestReleasePlate();
    }
}
