using System;
using UnityEngine;

namespace SushiPouringV3
{
    /// <summary>컵 크기, 입자 움직임과 성능을 Inspector에서 조절하는 설정입니다.</summary>
    [Serializable]
    public sealed class FluidSimulationSettings
    {
        // 바닥부터 입구까지 등간격으로 측정한 왼쪽·오른쪽 내부 X 좌표입니다.
        [SerializeField] Vector2[] _cupProfile = new Vector2[0];
        public int ProfileCount => _cupProfile.Length;

        /// <summary>지정한 높이 표본의 좌우 경계를 값으로 반환합니다.</summary>
        public Vector2 GetProfile(int index)
        {
            // 배열 자체를 노출하지 않아 라운드 중 경계 변경을 방지합니다.
            return _cupProfile[index];
        }
        // 컵 바닥과 입구입니다. 반너비는 프로파일이 없는 기존 컵에서만 사용합니다.
        [SerializeField] double _bottom = -2.4d;
        [SerializeField] double _rim = 0.4d;
        [SerializeField, Min(0.001f)] double _bottomHalfWidth = 0.72d;
        [SerializeField, Min(0.001f)] double _topHalfWidth = 0.84d;
        // View가 계산 좌표를 화면에 대응시킬 표시 범위입니다.
        [SerializeField] double _viewMinX = -2.5d;
        [SerializeField] double _viewMaxX = 2.5d;
        [SerializeField] double _viewMinY = -3d;
        [SerializeField] double _viewMaxY = 3d;
        // 표시 범위 밖에서 입자를 회수할 여유 거리입니다.
        [SerializeField, Min(0f)] double _cullMargin = 0.3d;
        // 입자 하나의 면적, 경계 충돌 반지름과 이웃 계산 반경입니다.
        [SerializeField, Min(0.0001f)] double _particleArea = 0.00525d;
        [SerializeField, Min(0.001f)] double _particleRadius = 0.0275d;
        [SerializeField, Min(0.001f)] double _neighborRadius = 0.14d;
        // 입자 속력, 중력, 점성과 속도 감쇠입니다.
        [SerializeField, Min(0f)] double _emissionSpeed = 3.2d;
        [SerializeField, Min(0f)] double _gravity = 9.81d;
        [SerializeField, Min(0f)] double _viscosity = 1.8d;
        [SerializeField, Min(0f)] double _velocityDamping = 0.25d;
        // 컵에 들어온 입자에만 더하는 초당 속도 감쇠 계수입니다.
        [Tooltip("컵 내부에만 추가하는 속도 감쇠입니다. 0이면 추가 감쇠를 끕니다. 변경은 다음 라운드부터 적용됩니다.")]
        [SerializeField, Min(0f)] double _cupVelocityDamping = 2d;
        [SerializeField, Min(0.001f)] double _maximumSpeed = 12d;
        // 밀도 압력과 근거리 압력의 강도, 기준 밀도와 최대 인력입니다.
        [SerializeField, Min(0f)] double _stiffness = 70d;
        [SerializeField, Min(0f)] double _nearStiffness = 200d;
        [SerializeField, Min(0f)] double _restDensity = 1.6d;
        [SerializeField, Min(0f)] double _maximumAttraction = 8d;
        // 심한 겹침을 푸는 강도와 한 스텝의 위치 보정 한도입니다.
        [SerializeField, Range(0f, 1f)] double _separationStrength = 0.45d;
        [SerializeField, Min(0f)] double _maximumPullShift = 0.0015d;
        [SerializeField, Min(0.0001f)] double _maximumPushShift = 0.009d;
        // 같은 위치에 입자가 완전히 겹치지 않게 하는 발사 편차입니다.
        [SerializeField, Min(0f)] double _emissionSpread = 0.025d;
        // 기준 유량의 발사 방향에서 좌우로 퍼지는 최대 각도입니다. 단위는 도입니다.
        [SerializeField, Range(0f, 89f)] double _emissionHalfAngle = 4d;
        // 유량이 0에 가까울 때와 최대일 때의 기준 발사 속도 배수입니다.
        [SerializeField, Range(0f, 1f)] double _minimumEmissionSpeedMultiplier = 0.5d;
        [SerializeField, Min(1f)] double _maximumEmissionSpeedMultiplier = 1.5d;
        // 같은 유량 구간의 입구 반너비와 발사 각도에 함께 적용할 배수입니다.
        [SerializeField, Range(0f, 1f)] double _minimumEmissionSpreadMultiplier = 0.25d;
        [SerializeField, Min(1f)] double _maximumEmissionSpreadMultiplier = 2d;
        // 정확도와 계산 비용을 결정하는 최대 시간 간격과 밀도 반복 횟수입니다.
        [SerializeField, Min(0.0001f)] double _maximumStep = 1d / 120d;
        [SerializeField, Range(1, 8)] int _solverIterations = 2;
        // 잘못된 입자 크기로 지나친 메모리를 할당하지 않게 하는 제한입니다.
        [SerializeField, Min(1)] int _particleBudget = 2048;

        public double Bottom => _bottom;
        public double Rim => _rim;
        public double BottomHalfWidth => _bottomHalfWidth;
        public double TopHalfWidth => _topHalfWidth;
        public double ViewMinX => _viewMinX;
        public double ViewMaxX => _viewMaxX;
        public double ViewMinY => _viewMinY;
        public double ViewMaxY => _viewMaxY;
        public double CullMargin => _cullMargin;
        public double ParticleArea => _particleArea;
        public double ParticleRadius => _particleRadius;
        public double NeighborRadius => _neighborRadius;
        public double EmissionSpeed => _emissionSpeed;
        public double Gravity => _gravity;
        public double Viscosity => _viscosity;
        public double VelocityDamping => _velocityDamping;
        public double CupVelocityDamping => _cupVelocityDamping;
        public double MaximumSpeed => _maximumSpeed;
        public double Stiffness => _stiffness;
        public double NearStiffness => _nearStiffness;
        public double RestDensity => _restDensity;
        public double MaximumAttraction => _maximumAttraction;
        public double SeparationStrength => _separationStrength;
        public double MaximumPullShift => _maximumPullShift;
        public double MaximumPushShift => _maximumPushShift;
        public double EmissionSpread => _emissionSpread;
        public double EmissionHalfAngle => _emissionHalfAngle;
        public double MinimumEmissionSpeedMultiplier => _minimumEmissionSpeedMultiplier;
        public double MaximumEmissionSpeedMultiplier => _maximumEmissionSpeedMultiplier;
        public double MinimumEmissionSpreadMultiplier => _minimumEmissionSpreadMultiplier;
        public double MaximumEmissionSpreadMultiplier => _maximumEmissionSpreadMultiplier;
        public double MaximumStep => _maximumStep;
        public int SolverIterations => _solverIterations;
        public int ParticleBudget => _particleBudget;

        /// <summary>현재 설정을 검증한 뒤 라운드 전용 복사본을 만듭니다.</summary>
        internal FluidSimulationSettings CreateSnapshot()
        {
            // 복사 전에 라운드에서 사용할 설정을 검증합니다.
            Validate();
            // 경계 배열도 복사해 원본 에셋과 라운드를 분리합니다.
            FluidSimulationSettings copy = (FluidSimulationSettings)MemberwiseClone();
            copy._cupProfile = (Vector2[])_cupProfile.Clone();
            return copy;
        }

        /// <summary>유체 계산을 할 수 있는 설정 조합인지 검사합니다.</summary>
        internal void Validate()
        {
            // 프로파일은 두 점 이상이며 모든 높이에서 양의 내부 폭을 가져야 합니다.
            if (_cupProfile == null || _cupProfile.Length == 1)
                throw new ArgumentException("컵 경계는 비어 있거나 두 개 이상의 표본이어야 합니다.");
            foreach (Vector2 edge in _cupProfile)
                if (!FluidNumber.IsFinite(edge.x) || !FluidNumber.IsFinite(edge.y) || edge.x >= edge.y)
                    throw new ArgumentException("컵 경계의 왼쪽 X는 오른쪽 X보다 작아야 합니다.");
            // 컵과 화면은 정상적인 크기를 가져야 합니다.
            if (!FluidNumber.IsFinite(_bottom) || !FluidNumber.IsFinite(_rim) || _rim <= _bottom ||
                !FluidNumber.IsFinite(_viewMinX) || !FluidNumber.IsFinite(_viewMaxX) || _viewMaxX <= _viewMinX ||
                !FluidNumber.IsFinite(_viewMinY) || !FluidNumber.IsFinite(_viewMaxY) || _viewMaxY <= _viewMinY)
                throw new ArgumentException("컵 높이와 표시 범위의 최댓값은 최솟값보다 커야 합니다.");
            FluidNumber.RequirePositive(_bottomHalfWidth, nameof(BottomHalfWidth));
            FluidNumber.RequirePositive(_topHalfWidth, nameof(TopHalfWidth));
            FluidNumber.RequirePositive(_particleArea, nameof(ParticleArea));
            FluidNumber.RequirePositive(_particleRadius, nameof(ParticleRadius));
            FluidNumber.RequirePositive(_neighborRadius, nameof(NeighborRadius));
            FluidNumber.RequirePositive(_maximumSpeed, nameof(MaximumSpeed));
            FluidNumber.RequirePositive(_maximumStep, nameof(MaximumStep));
            FluidNumber.RequirePositive(_maximumPushShift, nameof(MaximumPushShift));
            if (_topHalfWidth < _bottomHalfWidth ||
                _particleRadius >= _bottomHalfWidth || _particleRadius >= (_rim - _bottom) * 0.5d ||
                _neighborRadius <= _particleRadius || _solverIterations < 1 || _solverIterations > 8 || _particleBudget < 1)
                throw new ArgumentException("컵 너비, 입자 크기, 반복 횟수와 입자 예산을 확인하세요.");
            // 0으로 꺼 둘 수 있는 효과도 음수 또는 비정상 숫자는 거부합니다.
            FluidNumber.RequireNonNegative(_cullMargin, nameof(CullMargin));
            FluidNumber.RequireNonNegative(_emissionSpeed, nameof(EmissionSpeed));
            FluidNumber.RequireNonNegative(_gravity, nameof(Gravity));
            FluidNumber.RequireNonNegative(_viscosity, nameof(Viscosity));
            FluidNumber.RequireNonNegative(_velocityDamping, nameof(VelocityDamping));
            // 컵 내부 전용 감쇠도 음수와 비정상 숫자를 허용하지 않습니다.
            FluidNumber.RequireNonNegative(_cupVelocityDamping, nameof(CupVelocityDamping));
            FluidNumber.RequireNonNegative(_stiffness, nameof(Stiffness));
            FluidNumber.RequireNonNegative(_nearStiffness, nameof(NearStiffness));
            FluidNumber.RequireNonNegative(_restDensity, nameof(RestDensity));
            FluidNumber.RequireNonNegative(_maximumAttraction, nameof(MaximumAttraction));
            FluidNumber.RequireNonNegative(_maximumPullShift, nameof(MaximumPullShift));
            FluidNumber.RequireNonNegative(_emissionSpread, nameof(EmissionSpread));
            // 유량 증가가 속도와 퍼짐 감소로 역전되지 않도록 배수의 범위를 검사합니다.
            ValidateEmissionMultipliers(_minimumEmissionSpeedMultiplier, _maximumEmissionSpeedMultiplier);
            ValidateEmissionMultipliers(_minimumEmissionSpreadMultiplier, _maximumEmissionSpreadMultiplier);
            FluidNumber.RequireNonNegative(_emissionHalfAngle, nameof(EmissionHalfAngle));
            FluidNumber.RequireNonNegative(_emissionSpread * _maximumEmissionSpreadMultiplier, "MaximumEmissionSpread");
            FluidNumber.RequireNonNegative(_emissionSpeed * _maximumEmissionSpeedMultiplier, "MaximumEmissionSpeed");
            if (_emissionHalfAngle * _maximumEmissionSpreadMultiplier >= 90d)
                throw new ArgumentException("최대 발사 반각은 90도 미만이어야 합니다.");
            if (!FluidNumber.IsFinite(_separationStrength) || _separationStrength < 0d || _separationStrength > 1d)
                throw new ArgumentException("겹침 보정 강도는 0~1이어야 합니다.");
        }

        /// <summary>약한 유량과 강한 유량에 적용할 배수를 검사합니다.</summary>
        static void ValidateEmissionMultipliers(double minimum, double maximum)
        {
            // 기준 유량의 배수 1을 가운데에 두고 단조롭게 증가하게 합니다.
            if (!FluidNumber.IsFinite(minimum) || minimum < 0d || minimum > 1d ||
                !FluidNumber.IsFinite(maximum) || maximum < 1d)
                throw new ArgumentException("발사 배수는 최소 0~1, 최대 1 이상이어야 합니다.");
        }
    }
}
