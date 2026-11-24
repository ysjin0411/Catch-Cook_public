using System;

namespace SushiPouringV3
{
    /// <summary>라운드의 진행 상태와 종료 이유입니다.</summary>
    public enum FluidResult
    {
        Running,   // 정해진 시간이 아직 끝나지 않았습니다.
        Success,   // 목표량의 허용 범위 안입니다.
        TooLittle, // 목표량보다 부족합니다.
        TooMuch,   // 목표량보다 많습니다.
        Cancelled  // 외부에서 취소했습니다.
    }

    /// <summary>종료 당시 값을 보관하는 변경 불가능한 결과입니다.</summary>
    public readonly struct FluidCompletion
    {
        public FluidResult Result { get; }
        public double GeneratedVolume { get; }
        public double TargetVolume { get; }
        public double Elapsed { get; }
        public double DifferenceFromTarget => GeneratedVolume - TargetVolume;

        /// <summary>종료 당시의 결과와 물량을 복사합니다.</summary>
        internal FluidCompletion(FluidResult result, double generated, double target, double elapsed)
        {
            // 이후 화면의 입자가 움직여도 판정 값은 유지합니다.
            Result = result;
            GeneratedVolume = generated;
            TargetVolume = target;
            Elapsed = elapsed;
        }
    }

    /// <summary>View에 제공하는 입자 상태의 읽기 전용 복사본입니다.</summary>
    public readonly struct FluidParticle
    {
        public int Id { get; }
        public double X { get; }
        public double Y { get; }
        public double VelocityX { get; }
        public double VelocityY { get; }
        public double Volume { get; }
        // 물리 경계 계산에 쓰는 반지름입니다. 셰이더의 합침 반경과는 별개입니다.
        public double Radius { get; }
        // 기본 입자 물량에 대한 비율입니다. 마지막 잔량을 같은 크기로 과장하지 않게 제공합니다.
        public double VolumeRatio { get; }
        public bool IsInCup { get; }

        /// <summary>내부 입자를 외부에서 변경할 수 없는 값으로 복사합니다.</summary>
        internal FluidParticle(double x, double y, double vx, double vy, double volume, bool inCup,
            double radius, double volumeRatio, int id)
        {
            // 내부 배열 참조를 전달하지 않습니다.
            X = x;
            Y = y;
            VelocityX = vx;
            VelocityY = vy;
            Volume = volume;
            IsInCup = inCup;
            Radius = radius;
            VolumeRatio = volumeRatio;
            Id = id;
        }
    }

    /// <summary>View의 물줄기 연출과 내부 발사가 공유하는 읽기 전용 발사 상태입니다.</summary>
    public readonly struct FluidEmissionState
    {
        // 컵 용량을 1로 보았을 때 초당 생성량입니다.
        public double Flow { get; }
        // 최대 유량을 1로 정규화한 세기입니다. 종료되거나 유량이 없으면 0입니다.
        public double Strength { get; }
        // 계산 좌표 단위의 초기 속력과 입구의 반너비입니다.
        public double Speed { get; }
        public double HalfWidth { get; }
        // 중심 발사 방향의 좌우 최대 각도이며 단위는 도입니다.
        public double HalfAngleDegrees { get; }

        /// <summary>이미 검증된 설정에서 계산한 발사 상태를 복사합니다.</summary>
        internal FluidEmissionState(double flow, double strength, double speed, double halfWidth, double halfAngle)
        {
            // 값 복사로 전달하여 View가 내부 발사 설정을 변경할 수 없게 합니다.
            Flow = flow;
            Strength = strength;
            Speed = speed;
            HalfWidth = halfWidth;
            HalfAngleDegrees = halfAngle;
        }
    }

    /// <summary>유체 계산 좌표로 전달하는 병 입구 위치와 방향입니다.</summary>
    public readonly struct FluidEmitterPose
    {
        public double X { get; }
        public double Y { get; }
        public double DirectionX { get; }
        public double DirectionY { get; }
        public bool IsValid => DirectionX != 0d || DirectionY != 0d;

        /// <summary>좌표를 검사하고 발사 방향을 정규화합니다.</summary>
        public FluidEmitterPose(double x, double y, double directionX, double directionY)
        {
            // NaN, 무한대와 길이가 0인 방향을 거부합니다.
            double length = Math.Sqrt(directionX * directionX + directionY * directionY);
            if (!FluidNumber.IsFinite(x) || !FluidNumber.IsFinite(y) ||
                !FluidNumber.IsFinite(length) || length <= 0d)
                throw new ArgumentException("유효한 병 입구 위치와 0이 아닌 방향이 필요합니다.");
            X = x;
            Y = y;
            DirectionX = directionX / length;
            DirectionY = directionY / length;
        }
    }

    /// <summary>키 유지와 새 누름을 구분하고 시작 키가 중복 입력되는 것을 막습니다.</summary>
    public sealed class FluidTapInput
    {
        // 시작에 사용한 키를 놓기 전에는 입력을 받지 않습니다.
        bool _waitingForRelease = true;
        // 직전 호출의 키 상태입니다.
        bool _wasHeld;

        /// <summary>새 라운드의 입력 해제 대기 상태로 돌아갑니다.</summary>
        public void Reset()
        {
            // 이전 라운드의 누르기 상태를 인계하지 않습니다.
            _waitingForRelease = true;
            _wasHeld = false;
        }

        /// <summary>키를 새로 누른 호출에서만 true를 반환합니다.</summary>
        public bool Read(bool held)
        {
            // 시작 키를 놓으면 다음 누름부터 허용합니다.
            if (_waitingForRelease)
            {
                _waitingForRelease = held;
                _wasHeld = held;
                return false;
            }
            // 계속 누르고 있는 호출에서는 회복을 발생시키지 않습니다.
            bool pressed = held && !_wasHeld;
            _wasHeld = held;
            return pressed;
        }
    }

    /// <summary>내부 숫자 검증을 공통으로 제공합니다.</summary>
    internal static class FluidNumber
    {
        /// <summary>NaN과 무한대를 제외한 숫자인지 검사합니다.</summary>
        internal static bool IsFinite(double value)
        {
            // Unity에 의존하지 않는 숫자 검사입니다.
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>유한한 양수인지 검사합니다.</summary>
        internal static void RequirePositive(double value, string name)
        {
            // 잘못된 설정은 계산을 시작하기 전에 거부합니다.
            if (!IsFinite(value) || value <= 0d) throw new ArgumentOutOfRangeException(name);
        }

        /// <summary>유한한 0 이상의 값인지 검사합니다.</summary>
        internal static void RequireNonNegative(double value, string name)
        {
            // 0이 유효한 설정에서도 음수와 비정상 숫자는 거부합니다.
            if (!IsFinite(value) || value < 0d) throw new ArgumentOutOfRangeException(name);
        }
    }
}
