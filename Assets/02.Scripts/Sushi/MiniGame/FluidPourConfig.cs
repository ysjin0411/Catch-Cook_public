using UnityEngine;

namespace SushiPouringV3
{
    /// <summary>게임 밸런스와 유체 설정을 저장하는 음료 미니게임 에셋입니다.</summary>
    [CreateAssetMenu(fileName = "DrinkMinigameBalance", menuName = "Game/Sushi/Drink Minigame Balance")]
    public sealed class FluidPourConfig : ScriptableObject
    {
        // 진행 시간, 목표량, 게이지와 유량의 게임 규칙입니다.
        [SerializeField] FluidPourSettings _gameplay = new FluidPourSettings();
        // 컵과 입자의 움직임 및 계산 비용을 조절합니다.
        [SerializeField] FluidSimulationSettings _simulation = new FluidSimulationSettings();

        /// <summary>현재 Inspector 설정을 복사하여 독립적인 라운드를 만듭니다.</summary>
        public FluidPourSession CreateSession()
        {
            // 진행 중 설정 변경은 다음 라운드부터 적용됩니다.
            return new FluidPourSession(_gameplay, _simulation);
        }
    }
}
