using System;
using UnityEngine;

namespace SushiPouringV3
{
    /// <summary>Inspector에서 조절하고 라운드 시작 시 복사하는 게임 규칙입니다.</summary>
    [Serializable]
    public sealed class FluidPourSettings
    {
        // 한 판의 진행 시간이며 이 시간이 끝나야 성공 또는 실패를 판정합니다.
        [SerializeField, Min(0.01f)] double _duration = 10d;
        // 컵 전체 용량을 1로 보았을 때 목표 생성량과 허용하는 절대 오차입니다.
        [SerializeField, Range(0f, 1f)] double _targetVolume = 0.70d;
        [SerializeField, Min(0f)] double _volumeTolerance = 0.05d;
        // 체력바처럼 사용하는 게이지의 최대값입니다.
        [SerializeField, Min(0.01f)] double _maximumGauge = 100d;
        // 노란 영역의 경계이며 시작값은 두 값의 가운데로 계산합니다.
        [SerializeField, Min(0f)] double _idealGaugeMin = 40d;
        [SerializeField, Min(0f)] double _idealGaugeMax = 60d;
        // 초당 자동 감소량과 새 입력 한 번마다 회복하는 양입니다.
        [SerializeField, Min(0f)] double _gaugeDecayPerSecond = 45d;
        [SerializeField, Min(0.01f)] double _gaugePerTap = 12d;
        // 맨 왼쪽과 맨 오른쪽 유량을 기준 유량의 배수로 지정합니다.
        [SerializeField, Range(0f, 1f)] double _minimumFlowMultiplier = 0d;
        [SerializeField, Min(1f)] double _maximumFlowMultiplier = 2d;

        public double Duration => _duration;
        public double TargetVolume => _targetVolume;
        public double VolumeTolerance => _volumeTolerance;
        public double MaximumGauge => _maximumGauge;
        public double IdealGaugeMin => _idealGaugeMin;
        public double IdealGaugeMax => _idealGaugeMax;
        public double InitialGauge => (_idealGaugeMin + _idealGaugeMax) * 0.5d;
        public double GaugeDecayPerSecond => _gaugeDecayPerSecond;
        public double GaugePerTap => _gaugePerTap;
        public double MinimumFlowMultiplier => _minimumFlowMultiplier;
        public double MaximumFlowMultiplier => _maximumFlowMultiplier;
        public double IdealFlow => _targetVolume / _duration;
        public double MinimumSuccessVolume => _targetVolume - _volumeTolerance;
        public double MaximumSuccessVolume => _targetVolume + _volumeTolerance;

        /// <summary>Inspector와 기본 라운드에서 사용할 초기 설정을 만듭니다.</summary>
        public FluidPourSettings()
        {
            // 필드 선언의 기본값을 사용하고 라운드 생성 시 검증합니다.
        }

        /// <summary>코드 또는 테스트에서 명시적인 밸런스 설정을 만듭니다.</summary>
        public FluidPourSettings(double duration, double targetVolume, double volumeTolerance,
            double maximumGauge = 100d, double idealGaugeMin = 40d, double idealGaugeMax = 60d,
            double gaugeDecayPerSecond = 45d, double gaugePerTap = 12d,
            double minimumFlowMultiplier = 0d, double maximumFlowMultiplier = 2d)
        {
            // 전달받은 규칙을 저장하고 잘못된 조합을 즉시 검사합니다.
            _duration = duration;
            _targetVolume = targetVolume;
            _volumeTolerance = volumeTolerance;
            _maximumGauge = maximumGauge;
            _idealGaugeMin = idealGaugeMin;
            _idealGaugeMax = idealGaugeMax;
            _gaugeDecayPerSecond = gaugeDecayPerSecond;
            _gaugePerTap = gaugePerTap;
            _minimumFlowMultiplier = minimumFlowMultiplier;
            _maximumFlowMultiplier = maximumFlowMultiplier;
            Validate();
        }

        /// <summary>진행 중 Inspector 수정이 현재 라운드에 영향을 주지 않게 복사합니다.</summary>
        internal FluidPourSettings CreateSnapshot()
        {
            // 현재 클래스는 숫자 필드만 가지므로 값 복사로 완전히 분리됩니다.
            Validate();
            return (FluidPourSettings)MemberwiseClone();
        }

        /// <summary>유효한 게이지 범위, 물량과 시간인지 검사합니다.</summary>
        void Validate()
        {
            // 잘못된 설정을 자동 보정하여 숨기지 않고 명확히 알립니다.
            FluidNumber.RequirePositive(_duration, nameof(Duration));
            FluidNumber.RequirePositive(_maximumGauge, nameof(MaximumGauge));
            FluidNumber.RequirePositive(_gaugePerTap, nameof(GaugePerTap));
            FluidNumber.RequireNonNegative(_gaugeDecayPerSecond, nameof(GaugeDecayPerSecond));
            if (!FluidNumber.IsFinite(_idealGaugeMin) || !FluidNumber.IsFinite(_idealGaugeMax) ||
                !(_idealGaugeMin > 0d && _idealGaugeMin < _idealGaugeMax && _idealGaugeMax < _maximumGauge))
                throw new ArgumentException("노란 구간은 0 < 하한 < 상한 < 최대 게이지여야 합니다.");
            if (_gaugePerTap > _maximumGauge)
                throw new ArgumentException("입력당 회복량은 최대 게이지를 넘을 수 없습니다.");
            if (!FluidNumber.IsFinite(_targetVolume) || _targetVolume <= 0d || _targetVolume > 1d ||
                !FluidNumber.IsFinite(_volumeTolerance) || _volumeTolerance < 0d ||
                _volumeTolerance >= _targetVolume || _targetVolume + _volumeTolerance > 1d)
                throw new ArgumentException("물량은 컵 용량 1 기준이며 허용 범위는 0보다 크고 1 이하여야 합니다.");
            if (!FluidNumber.IsFinite(_minimumFlowMultiplier) || _minimumFlowMultiplier < 0d ||
                _minimumFlowMultiplier > 1d || !FluidNumber.IsFinite(_maximumFlowMultiplier) ||
                _maximumFlowMultiplier < 1d)
                throw new ArgumentException("최소 유량 배수는 0~1, 최대 유량 배수는 1 이상이어야 합니다.");
            FluidNumber.RequirePositive(IdealFlow * _maximumFlowMultiplier, "MaximumFlow");
        }
    }
}
