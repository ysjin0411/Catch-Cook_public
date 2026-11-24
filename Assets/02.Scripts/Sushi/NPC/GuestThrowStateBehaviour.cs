using UnityEngine;

/// <summary>Animator의 Throw 상태 진입과 종료를 게스트에게 알립니다.</summary>
public sealed class GuestThrowStateBehaviour : StateMachineBehaviour
{
    GuestAngryReaction _reaction;

    /// <summary>Throw 상태에 진입하면 손에 접시를 준비합니다.</summary>
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 이 Animator에 해당하는 게스트를 상태 진입 시 한 번 찾습니다.
        _reaction = animator.GetComponentInParent<GuestAngryReaction>();
        if (_reaction != null) _reaction.OnThrowEntered();
    }

    /// <summary>Throw 상태의 평가가 끝나면 연출 종료를 알립니다.</summary>
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 매 프레임 normalizedTime을 조회하지 않고 종료 알림을 사용합니다.
        if (_reaction != null) _reaction.OnThrowExited();
    }
}
