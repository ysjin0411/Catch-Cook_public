using System;

namespace SushiPouringV3
{
    /// <summary>게이지 연타와 총 생성량을 관리하고 N초가 끝난 뒤 결과를 발행합니다.</summary>
    public sealed class FluidPourSession
    {
        // 성공 범위 경계의 부동소수점 오차만 보정하는 값입니다.
        const double VolumeEpsilon = 1e-10d;
        // 외부에서 수정할 수 없는 입자 솔버입니다.
        readonly FluidSimulation _fluid;
        // 아직 입자로 표현하지 않은 잔량과 발사 순번입니다.
        double _pendingVisualVolume;
        int _emissionIndex;
        // 다음 고정 물리 스텝까지 모인 시간입니다. 프레임의 작은 잔량을 버리지 않습니다.
        double _physicsRemainder;
        // 종료 시 남은 소량도 마지막으로 물이 생성됐을 때의 발사 세기로 표현합니다.
        FluidEmissionState _lastEmission;
        // 종료 당시 결과와 이벤트 전달 여부입니다.
        FluidCompletion _completion;
        bool _completionPublished;

        public FluidPourSettings Settings { get; }
        public FluidSimulationSettings SimulationSettings { get; }
        public FluidCupGeometry Cup { get; }
        public FluidResult Result { get; private set; } = FluidResult.Running;
        public bool IsRunning => Result == FluidResult.Running;
        public double CurrentGauge { get; private set; }
        public double GaugeRatio => CurrentGauge / Settings.MaximumGauge;
        public double Flow => IsRunning ? GetFlow(CurrentGauge) : 0d;
        public double GeneratedVolume { get; private set; }
        public double Elapsed { get; private set; }
        public double RemainingTime => Math.Max(0d, Settings.Duration - Elapsed);
        public double ExpectedGeneratedVolume => Settings.IdealFlow * Elapsed;
        public double VolumeDeviation => GeneratedVolume - ExpectedGeneratedVolume;
        public double DifferenceFromTarget => GeneratedVolume - Settings.TargetVolume;
        public double ContainedVolume { get; private set; }
        public double FlyingVolume { get; private set; }
        public double SpilledVolume => _fluid.Spilled;
        public double PendingVisualVolume => _pendingVisualVolume;
        public int ParticleCount => _fluid.Count;
        // 라운드 시작에 View가 한 번 할당할 입자 배열의 크기입니다.
        public int ParticleCapacity => _fluid.Capacity;
        // 입자 하나의 기준 물량입니다. 마지막 잔량 입자는 이보다 작을 수 있습니다.
        public double NominalParticleVolume => _fluid.Parcel;
        // View가 물줄기, 소리와 이펙트 세기에 사용할 현재 발사 상태입니다.
        public FluidEmissionState Emission => IsRunning ? CalculateEmission(Flow) : default;

        /// <summary>종료 시 송신 세션과 확정 결과를 라운드당 한 번 전달합니다.</summary>
        public event Action<FluidPourSession, FluidCompletion> Completed;

        /// <summary>기본 설정을 사용하는 독립적인 라운드를 만듭니다.</summary>
        public FluidPourSession() : this(new FluidPourSettings(), new FluidSimulationSettings())
        {
            // 설정을 받는 생성자에서 초기화합니다.
        }

        /// <summary>설정을 복사하고 노란 영역의 가운데에서 라운드를 시작합니다.</summary>
        public FluidPourSession(FluidPourSettings settings, FluidSimulationSettings simulationSettings)
        {
            // 시작한 라운드의 규칙을 이후 Inspector 변경으로부터 분리합니다.
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (simulationSettings == null) throw new ArgumentNullException(nameof(simulationSettings));
            Settings = settings.CreateSnapshot();
            SimulationSettings = simulationSettings.CreateSnapshot();
            Cup = new FluidCupGeometry(SimulationSettings);
            _fluid = new FluidSimulation(SimulationSettings, Cup, Settings.TargetVolume * Settings.MaximumFlowMultiplier);
            CurrentGauge = Settings.InitialGauge;
        }

        /// <summary>키를 새로 누른 입력 한 번만큼 게이지를 즉시 회복합니다.</summary>
        public void Tap()
        {
            // 길게 누르기 필터링은 FluidTapInput 또는 입력 연결 측에서 처리합니다.
            if (!IsRunning) return;
            CurrentGauge = Math.Min(Settings.MaximumGauge, CurrentGauge + Settings.GaugePerTap);
        }

        /// <summary>전달된 시간만큼 진행하며 마지막 구간은 정확히 남은 시간만 사용합니다.</summary>
        public void Advance(double deltaTime, FluidEmitterPose emitter)
        {
            // 입력 검증이 끝나기 전에는 상태를 변경하지 않습니다.
            FluidNumber.RequireNonNegative(deltaTime, nameof(deltaTime));
            if (!emitter.IsValid) throw new ArgumentException("병 입구 정보가 유효하지 않습니다.", nameof(emitter));
            // 좌표 변환 오류가 입자 생성 후 공간 해시 예외로 이어지기 전에 거부합니다.
            _fluid.ValidateEmitter(emitter);
            if (deltaTime == 0d) return;
            if (!IsRunning)
            {
                // 종료 후에는 생성량이나 결과를 바꾸지 않고 남은 입자만 계산합니다.
                AdvanceFluidOnly(deltaTime);
                return;
            }
            // N초를 넘는 프레임에서도 N초 이후의 물은 생성하지 않습니다.
            double duration = Math.Min(deltaTime, RemainingTime);
            bool completesRound = deltaTime >= RemainingTime;
            double remaining = duration;
            while (remaining > 0d)
            {
                // 물리는 프레임 길이와 무관한 고정 간격으로 실행합니다.
                double untilTick = SimulationSettings.MaximumStep - _physicsRemainder;
                bool reachesTick = remaining >= untilTick;
                double step = reachesTick ? untilTick : remaining;
                bool lastStep = remaining <= untilTick;
                // 게임 물량은 이 구간까지 정확히 누적하고 입자는 다음 물리 스텝에 참여시킵니다.
                StepRound(step, emitter, completesRound && lastStep);
                ConsumePhysicsTime(step, reachesTick);
                remaining = lastStep ? 0d : remaining - step;
            }
            // 라운드 시간은 작은 스텝의 반복 덧셈 대신 프레임 단위로 갱신합니다.
            Elapsed = completesRound ? Settings.Duration : Elapsed + duration;
            if (completesRound) Finish(EvaluateResult());
            // 종료 시각을 넘긴 프레임의 남은 시간도 기존 입자의 운동에는 반영합니다.
            if (deltaTime > duration) AdvanceFluidOnly(deltaTime - duration);
            RefreshMeasurements();
            // 콜백에서 UI를 닫아도 내부 갱신을 중간에 끊지 않도록 마지막에 알립니다.
            PublishCompletion();
        }

        /// <summary>현재 라운드를 취소하고 종료 결과를 즉시 알립니다.</summary>
        public void Cancel()
        {
            // 이미 끝난 결과는 취소로 덮어쓰지 않습니다.
            if (!IsRunning) return;
            Finish(FluidResult.Cancelled);
            PublishCompletion();
        }

        /// <summary>액체 렌더러가 사용할 입자 복사본을 반환합니다.</summary>
        public FluidParticle GetParticle(int index)
        {
            // 외부에서 내부 입자 배열을 변경하지 못하게 합니다.
            return _fluid.GetParticle(index);
        }

        /// <summary>재사용 배열에 현재 입자를 복사합니다. 같은 메인 스레드에서 갱신 후 호출합니다.</summary>
        public int CopyParticles(FluidParticle[] destination)
        {
            // 매 프레임 배열을 새로 만들지 않고 값만 복사합니다.
            return _fluid.CopyParticles(destination);
        }

        /// <summary>지정한 게이지의 발사 상태를 미리 조회합니다. 세션 상태는 변경하지 않습니다.</summary>
        public FluidEmissionState GetEmission(double gauge)
        {
            // 종료 상태와 무관한 밸런스 미리보기이며 현재 상태는 Emission으로 조회합니다.
            return CalculateEmission(GetFlow(gauge));
        }

        /// <summary>유량에서 속도, 입구 폭과 발사 각도를 계산합니다.</summary>
        FluidEmissionState CalculateEmission(double flow)
        {
            // 물이 나오지 않는 상태는 View에서도 발사 이펙트를 끌 수 있게 모두 0으로 표시합니다.
            if (flow <= 0d) return default;
            double maximum = Settings.MaximumFlowMultiplier;
            // 평균 유량의 미세한 계산 오차가 최대 유량을 넘어 보간 구간을 바꾸지 않게 합니다.
            double relative = Math.Min(maximum, flow / Settings.IdealFlow);
            // 기준 유량 아래와 위를 각각 보간하여 노란 구간에서는 기존 설정을 그대로 사용합니다.
            double ratio = relative <= 1d ? relative :
                (relative - 1d) / (maximum - 1d);
            // 노란 구간 오른쪽 경계에서 갑자기 튀지 않고 초과 정도에 따라 부드럽게 퍼집니다.
            if (relative > 1d) ratio = ratio * ratio * (3d - 2d * ratio);
            double speedMultiplier = relative <= 1d
                ? SimulationSettings.MinimumEmissionSpeedMultiplier + (1d - SimulationSettings.MinimumEmissionSpeedMultiplier) * ratio
                : 1d + (SimulationSettings.MaximumEmissionSpeedMultiplier - 1d) * ratio;
            double spreadMultiplier = relative <= 1d
                ? SimulationSettings.MinimumEmissionSpreadMultiplier + (1d - SimulationSettings.MinimumEmissionSpreadMultiplier) * ratio
                : 1d + (SimulationSettings.MaximumEmissionSpreadMultiplier - 1d) * ratio;
            return new FluidEmissionState(flow, Math.Min(1d, relative / maximum),
                SimulationSettings.EmissionSpeed * speedMultiplier,
                SimulationSettings.EmissionSpread * spreadMultiplier,
                SimulationSettings.EmissionHalfAngle * spreadMultiplier);
        }

        /// <summary>종료한 경우에만 확정 결과를 반환합니다.</summary>
        public bool TryGetCompletion(out FluidCompletion completion)
        {
            // false일 때 반환된 기본 구조체는 사용하지 않습니다.
            completion = _completion;
            return !IsRunning;
        }

        /// <summary>게이지 위치에 해당하는 초당 유량을 반환합니다.</summary>
        double GetFlow(double gauge)
        {
            // 읽기 전용 조회에서도 비정상 숫자는 거부합니다.
            FluidNumber.RequireNonNegative(gauge, nameof(gauge));
            gauge = Math.Min(gauge, Settings.MaximumGauge);
            if (gauge < Settings.IdealGaugeMin)
            {
                // 왼쪽 끝 유량에서 기준 유량까지 선형으로 증가합니다.
                double ratio = gauge / Settings.IdealGaugeMin;
                return Settings.IdealFlow * (Settings.MinimumFlowMultiplier + (1d - Settings.MinimumFlowMultiplier) * ratio);
            }
            if (gauge <= Settings.IdealGaugeMax) return Settings.IdealFlow;
            // 오른쪽 끝까지 기준 유량에서 최대 유량으로 증가합니다.
            double right = (gauge - Settings.IdealGaugeMax) / (Settings.MaximumGauge - Settings.IdealGaugeMax);
            return Settings.IdealFlow * (1d + (Settings.MaximumFlowMultiplier - 1d) * right);
        }

        /// <summary>게이지가 감소하는 구간의 물량을 정확히 누적합니다.</summary>
        double IntegrateFlow(double duration)
        {
            // 감소량이 0인 밸런스 설정도 정상적으로 지원합니다.
            if (Settings.GaugeDecayPerSecond == 0d) return GetFlow(CurrentGauge) * duration;
            double generated = 0d;
            double remaining = duration;
            while (remaining > 0d)
            {
                // 게이지가 바닥에 도달한 뒤에는 최소 유량이 유지됩니다.
                if (CurrentGauge <= 0d)
                {
                    generated += GetFlow(0d) * remaining;
                    break;
                }
                // 직선 유량의 경계에서 구간을 나누어 정확한 사다리꼴 면적을 구합니다.
                double boundary = CurrentGauge > Settings.IdealGaugeMax ? Settings.IdealGaugeMax :
                    CurrentGauge > Settings.IdealGaugeMin ? Settings.IdealGaugeMin : 0d;
                double timeToBoundary = (CurrentGauge - boundary) / Settings.GaugeDecayPerSecond;
                bool reachesBoundary = remaining >= timeToBoundary;
                double step = reachesBoundary ? timeToBoundary : remaining;
                double next = reachesBoundary ? boundary : CurrentGauge - Settings.GaugeDecayPerSecond * step;
                generated += (GetFlow(CurrentGauge) + GetFlow(next)) * 0.5d * step;
                CurrentGauge = Math.Max(0d, next);
                remaining = reachesBoundary ? Math.Max(0d, remaining - step) : 0d;
            }
            return generated;
        }

        /// <summary>한 구간의 게이지, 생성량과 실제 입자를 갱신합니다.</summary>
        void StepRound(double dt, FluidEmitterPose emitter, bool flush)
        {
            // 생성량은 컵 안에 남은 양이나 입자 충돌 결과와 독립적입니다.
            double amount = IntegrateFlow(dt);
            // 작은 계산 구간의 평균 유량으로 발사 세기를 정합니다. 마지막 잔량은 이전 세기를 유지합니다.
            if (amount > 0d) _lastEmission = CalculateEmission(amount / dt);
            GeneratedVolume += amount;
            _pendingVisualVolume += amount;
            while (_pendingVisualVolume >= _fluid.Parcel || (flush && _pendingVisualVolume > 0d))
            {
                // 종료할 때는 기본 입자보다 작은 마지막 잔량까지 표현합니다.
                double parcel = Math.Min(_pendingVisualVolume, _fluid.Parcel);
                _fluid.Emit(emitter, parcel, _emissionIndex++, _lastEmission);
                _pendingVisualVolume = Math.Max(0d, _pendingVisualVolume - parcel);
            }
        }

        /// <summary>게임 구간에서 모은 시간을 반영하고 고정 간격이 완성되면 물리를 계산합니다.</summary>
        void ConsumePhysicsTime(double duration, bool reachesTick)
        {
            // 매우 작은 시간은 모아 두며, 위치 차이를 작은 dt로 나누는 계산을 피합니다.
            if (!reachesTick)
            {
                _physicsRemainder += duration;
                return;
            }
            _physicsRemainder = 0d;
            _fluid.Step(SimulationSettings.MaximumStep);
        }

        /// <summary>남은 입자만 움직이고 라운드의 확정 상태는 유지합니다.</summary>
        void AdvanceFluidOnly(double duration)
        {
            // 진행 중과 동일한 시간 누산기를 유지해 종료 경계에서도 고정 간격을 보존합니다.
            while (duration > 0d)
            {
                double untilTick = SimulationSettings.MaximumStep - _physicsRemainder;
                bool reachesTick = duration >= untilTick;
                double step = reachesTick ? untilTick : duration;
                bool lastStep = duration <= untilTick;
                ConsumePhysicsTime(step, reachesTick);
                duration = lastStep ? 0d : duration - step;
            }
            RefreshMeasurements();
        }

        /// <summary>화면에서 조회할 컵 내부량과 공중량을 갱신합니다.</summary>
        void RefreshMeasurements()
        {
            // 이 측정값은 최종 성공 판정에 사용하지 않습니다.
            _fluid.Measure(out double contained, out double flying);
            ContainedVolume = contained;
            FlyingVolume = flying;
        }

        /// <summary>N초 종료 후 목표량의 허용 범위와 최종 생성량을 비교합니다.</summary>
        FluidResult EvaluateResult()
        {
            // 계산 오차 외에 별도의 숨은 허용 범위를 더하지 않습니다.
            if (GeneratedVolume < Settings.MinimumSuccessVolume - VolumeEpsilon) return FluidResult.TooLittle;
            if (GeneratedVolume > Settings.MaximumSuccessVolume + VolumeEpsilon) return FluidResult.TooMuch;
            return FluidResult.Success;
        }

        /// <summary>종료 상태와 결과 데이터를 한 번 확정합니다.</summary>
        void Finish(FluidResult result)
        {
            // 이후 입자 변화나 취소 호출은 이미 확정한 결과에 영향을 주지 않습니다.
            if (!IsRunning) return;
            Result = result;
            _completion = new FluidCompletion(result, GeneratedVolume, Settings.TargetVolume, Elapsed);
        }

        /// <summary>종료 이벤트를 중복 없이 전달합니다.</summary>
        void PublishCompletion()
        {
            // 콜백이 재진입하더라도 같은 결과를 다시 발행하지 않습니다.
            if (IsRunning || _completionPublished) return;
            _completionPublished = true;
            Action<FluidPourSession, FluidCompletion> handler = Completed;
            if (handler != null) handler(this, _completion);
        }
    }
}
