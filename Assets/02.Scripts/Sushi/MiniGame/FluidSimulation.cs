using System;
using System.Collections.Generic;

namespace SushiPouringV3
{
    /// <summary>판정과 분리된 입자 이동, 밀도 압력과 컵 경계를 계산합니다.</summary>
    internal sealed class FluidSimulation
    {
        /// <summary>솔버 내부에서만 변경하는 입자 상태입니다.</summary>
        struct ParticleState
        {
            // 유실 입자를 배열에서 제거해도 유지되는 라운드 내 고유 번호입니다.
            internal int Id;
            // 현재 위치와 보정 전 위치입니다.
            internal double X, Y, OldX, OldY;
            // 현재 속도와 입자에 할당한 물량입니다.
            internal double VX, VY, Volume;
            // 컵 입구를 통과했는지 나타냅니다.
            internal bool Entered;
        }

        // 두 입자가 완전히 겹쳤을 때 나눗셈을 보호하는 수치 허용값입니다.
        const double MinimumDistance = 1e-7d;
        // 라운드에 고정된 설정과 컵 모양입니다.
        readonly FluidSimulationSettings _settings;
        readonly FluidCupGeometry _cup;
        // 물량 설정에서 필요한 크기를 계산한 입자와 밀도 버퍼입니다.
        readonly ParticleState[] _particles;
        readonly double[] _density;
        readonly double[] _nearDensity;
        // 주변 9개 셀만 조회하기 위한 재사용 공간 해시와 셀 내부 연결 목록입니다.
        readonly Dictionary<long, int> _cellHeads;
        readonly int[] _nextInCell;
        readonly int[] _cellX;
        readonly int[] _cellY;

        // 같은 이웃 검색을 사용하되 계산 단계별로 수행할 연산을 구분합니다.
        enum NeighborPass { Viscosity, Density, Pressure }

        internal int Count { get; private set; }
        internal int Capacity => _particles.Length;
        internal double Parcel { get; }
        internal double Emitted { get; private set; }
        internal double Spilled { get; private set; }

        /// <summary>최악의 생성량도 물량 손실 없이 표현할 수 있는 버퍼를 준비합니다.</summary>
        internal FluidSimulation(FluidSimulationSettings settings, FluidCupGeometry cup, double maximumVolume)
        {
            // 설정은 Session이 이미 검증한 전용 복사본입니다.
            _settings = settings;
            _cup = cup;
            Parcel = settings.ParticleArea / cup.CupArea;
            FluidNumber.RequirePositive(Parcel, nameof(Parcel));
            // 기본 입자들 외에 종료 시 마지막 잔량 입자 하나를 확보합니다.
            double required = Math.Ceiling(maximumVolume / Parcel) + 1d;
            if (!FluidNumber.IsFinite(required) || required > settings.ParticleBudget)
                throw new ArgumentException("최대 생성량에 필요한 입자가 예산을 넘습니다. Particle Budget 또는 Particle Area를 조절하세요.");
            int capacity = Math.Max(1, (int)required);
            _particles = new ParticleState[capacity];
            _density = new double[capacity];
            _nearDensity = new double[capacity];
            _cellHeads = new Dictionary<long, int>(capacity);
            _nextInCell = new int[capacity];
            _cellX = new int[capacity];
            _cellY = new int[capacity];
        }

        /// <summary>외부 표시용 입자 복사본을 반환합니다.</summary>
        internal FluidParticle GetParticle(int index)
        {
            // 내부 배열과 변경 가능한 입자는 공개하지 않습니다.
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            ParticleState p = _particles[index];
            return new FluidParticle(p.X, p.Y, p.VX, p.VY, p.Volume, p.Entered,
                _settings.ParticleRadius, p.Volume / Parcel, p.Id);
        }

        /// <summary>View가 재사용하는 배열에 현재 입자만 복사하고 유효한 개수를 반환합니다.</summary>
        internal int CopyParticles(FluidParticle[] destination)
        {
            // 부족한 배열에는 일부만 복사하지 않고 변경 전에 오류를 알립니다.
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Length < Count) throw new ArgumentException("입자 배열의 크기가 부족합니다.", nameof(destination));
            for (int i = 0; i < Count; i++) destination[i] = GetParticle(i);
            // 반환 개수 뒤쪽은 이전 프레임 값일 수 있으므로 View에서 사용하지 않습니다.
            return Count;
        }

        /// <summary>입구 좌표와 최초 이동 범위가 공간 해시의 좌표 범위 안인지 검사합니다.</summary>
        internal void ValidateEmitter(FluidEmitterPose emitter)
        {
            // 발사 편차와 한 스텝 이동·위치 보정이 사용하는 여유 범위를 확보합니다.
            double step = _settings.MaximumStep;
            double margin = _settings.EmissionSpread * _settings.MaximumEmissionSpreadMultiplier +
                _settings.EmissionSpeed * _settings.MaximumEmissionSpeedMultiplier * step +
                _settings.Gravity * step * step +
                _settings.MaximumPushShift * Capacity * _settings.SolverIterations;
            // 이웃 셀의 +/-1 조회까지 정수 범위 안에 남아야 합니다.
            double min = ((double)int.MinValue + 1d) * _settings.NeighborRadius + margin;
            double max = ((double)int.MaxValue - 1d) * _settings.NeighborRadius - margin;
            if (!FluidNumber.IsFinite(margin) || min >= max ||
                emitter.X < min || emitter.X > max || emitter.Y < min || emitter.Y > max)
                throw new ArgumentOutOfRangeException(nameof(emitter), "병 입구가 유체 계산 좌표 범위를 벗어났습니다. 화면 좌표 변환을 확인하세요.");
        }

        /// <summary>판정용 생성량에 이미 포함된 물을 입자로 표현합니다.</summary>
        internal void Emit(FluidEmitterPose emitter, double volume, int emissionIndex, FluidEmissionState emission)
        {
            // 계산한 최악의 버퍼 크기를 초과하면 내부 규칙 오류입니다.
            if (Count >= Capacity) throw new InvalidOperationException("라운드 입자 예산을 초과했습니다.");
            FluidNumber.RequirePositive(volume, nameof(volume));
            // 황금각으로 재현 가능한 편차를 주고 크기는 Inspector 설정을 따릅니다.
            double goldenAngle = Math.PI * (3d - Math.Sqrt(5d));
            double jitter = Math.Sin(emissionIndex * goldenAngle) * emission.HalfWidth;
            double angle = Math.Sin((emissionIndex + 0.5d) * goldenAngle) * emission.HalfAngleDegrees * Math.PI / 180d;
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            // 입구 폭은 발사 방향에 수직으로, 속도는 중심 방향을 회전시켜 적용합니다.
            _particles[Count++] = new ParticleState
            {
                Id = emissionIndex,
                X = emitter.X - emitter.DirectionY * jitter,
                Y = emitter.Y + emitter.DirectionX * jitter,
                VX = (emitter.DirectionX * cosine - emitter.DirectionY * sine) * emission.Speed,
                VY = (emitter.DirectionX * sine + emitter.DirectionY * cosine) * emission.Speed,
                Volume = volume
            };
            Emitted += volume;
        }

        /// <summary>컵 내부량과 공중량을 입자 상태에서 합산합니다.</summary>
        internal void Measure(out double contained, out double flying)
        {
            // 이 값들은 액체 표시용이며 최종 성공 판정에는 사용하지 않습니다.
            contained = 0d;
            flying = 0d;
            for (int i = 0; i < Count; i++)
            {
                if (_particles[i].Entered) contained += _particles[i].Volume;
                else flying += _particles[i].Volume;
            }
        }

        /// <summary>설정한 최대 간격 이하의 시간만큼 입자를 계산합니다.</summary>
        internal void Step(double dt)
        {
            // Session이 큰 프레임을 나누어 전달한 유효한 시간입니다.
            FluidNumber.RequirePositive(dt, nameof(dt));
            ApplyViscosity(dt);
            for (int i = 0; i < Count; i++)
            {
                // 중력으로 예측 위치를 만들고 속도 복원용 원래 위치를 보관합니다.
                _particles[i].OldX = _particles[i].X;
                _particles[i].OldY = _particles[i].Y;
                _particles[i].VY -= _settings.Gravity * dt;
                _particles[i].X += _particles[i].VX * dt;
                _particles[i].Y += _particles[i].VY * dt;
            }
            for (int iteration = 0; iteration < _settings.SolverIterations; iteration++)
            {
                // 보정할 때마다 밀도가 달라지므로 다시 계산합니다.
                CalculateDensity();
                RelaxDensity(dt);
                for (int i = 0; i < Count; i++) ResolveCup(i);
            }
            // 위치 보정 결과로 속도를 복원하고 화면 밖 입자를 회수합니다.
            UpdateVelocitiesAndSpills(dt);
        }

        /// <summary>가까운 입자끼리 속도를 교환하여 점성을 표현합니다.</summary>
        void ApplyViscosity(double dt)
        {
            // 전체 입자 쌍 대신 같은 셀과 인접한 셀의 입자만 비교합니다.
            BuildNeighborGrid();
            VisitNeighbors(NeighborPass.Viscosity, dt);
        }

        /// <summary>주변 입자 사이의 거리로 밀도를 구합니다.</summary>
        void CalculateDensity()
        {
            // 이전 계산 값을 지우고 현재 위치의 밀도를 누적합니다.
            Array.Clear(_density, 0, Count);
            Array.Clear(_nearDensity, 0, Count);
            // 위치 예측과 이전 반복의 보정을 반영한 셀을 다시 만듭니다.
            BuildNeighborGrid();
            VisitNeighbors(NeighborPass.Density, 0d);
        }

        /// <summary>밀도 압력과 최소 간격으로 입자 위치를 보정합니다.</summary>
        void RelaxDensity(double dt)
        {
            // 바로 앞 밀도 단계와 같은 이웃 후보를 사용합니다. 다음 반복에서 셀을 다시 계산합니다.
            VisitNeighbors(NeighborPass.Pressure, dt);
        }

        /// <summary>현재 입자 위치로 공간 해시를 갱신합니다.</summary>
        void BuildNeighborGrid()
        {
            // 컨테이너와 배열을 재사용하므로 정상 진행 중 프레임별 할당이 없습니다.
            _cellHeads.Clear();
            double size = _settings.NeighborRadius;
            for (int i = 0; i < Count; i++)
            {
                // 음수 좌표도 올바른 셀에 들어가도록 정수 절삭 대신 Floor를 사용합니다.
                int x = checked((int)Math.Floor(_particles[i].X / size));
                int y = checked((int)Math.Floor(_particles[i].Y / size));
                _cellX[i] = x;
                _cellY[i] = y;
                long key = CellKey(x, y);
                _nextInCell[i] = _cellHeads.TryGetValue(key, out int head) ? head : -1;
                _cellHeads[key] = i;
            }
        }

        /// <summary>부호를 포함한 두 셀 좌표를 충돌 없는 64비트 키로 묶습니다.</summary>
        static long CellKey(int x, int y)
        {
            // 상위 32비트는 X, 하위 32비트는 Y에 할당합니다.
            return ((long)x << 32) | (uint)y;
        }

        /// <summary>주변 셀의 각 입자 쌍을 한 번씩 방문하여 해당 단계의 계산을 수행합니다.</summary>
        void VisitNeighbors(NeighborPass pass, double dt)
        {
            // 셀 너비와 이웃 반경이 같으므로 주변 3×3 셀에 모든 초기 이웃 후보가 있습니다.
            double radius = _settings.NeighborRadius;
            double radiusSquared = radius * radius;
            for (int i = 0; i < Count; i++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (!_cellHeads.TryGetValue(CellKey(_cellX[i] + x, _cellY[i] + y), out int head)) continue;
                for (int j = head; j >= 0; j = _nextInCell[j])
                {
                    // 같은 쌍에 힘과 밀도를 두 번 적용하지 않습니다.
                    if (j <= i) continue;
                    ApplyPair(i, j, pass, dt, radius, radiusSquared);
                }
            }
        }

        /// <summary>거리 범위 안의 한 입자 쌍에 점성, 밀도 또는 압력 보정을 적용합니다.</summary>
        void ApplyPair(int i, int j, NeighborPass pass, double dt, double radius, double radiusSquared)
        {
                // 공중 물줄기는 발사 각도와 중력만 따르게 하여 입구에서 압력으로 폭발하지 않게 합니다.
                // 컵에 들어온 입자끼리만 점성과 밀도 압력을 계산합니다.
                if (!_particles[i].Entered || !_particles[j].Entered) return;
                // 비싼 제곱근 계산 전에 이웃 반경 밖의 쌍을 제외합니다.
                double dx = _particles[j].X - _particles[i].X;
                double dy = _particles[j].Y - _particles[i].Y;
                double squared = dx * dx + dy * dy;
                if (squared >= radiusSquared) return;
                double distance = Math.Sqrt(squared);
                double q = 1d - distance / radius;
                if (pass == NeighborPass.Density)
                {
                    // 가까운 이웃일수록 밀도와 근거리 밀도 기여가 큽니다.
                    _density[i] += q * q;
                    _density[j] += q * q;
                    _nearDensity[i] += q * q * q;
                    _nearDensity[j] += q * q * q;
                    return;
                }
                if (pass == NeighborPass.Viscosity)
                {
                    // 동일한 양의 반대 방향 속도 교환으로 점성을 표현합니다.
                    double mix = Math.Min(0.5d, _settings.Viscosity * dt * q);
                    double vx = (_particles[j].VX - _particles[i].VX) * mix;
                    double vy = (_particles[j].VY - _particles[i].VY) * mix;
                    _particles[i].VX += vx; _particles[j].VX -= vx;
                    _particles[i].VY += vy; _particles[j].VY -= vy;
                    return;
                }
                // 완전히 겹친 입자는 고정된 축으로 분리하여 0으로 나누지 않습니다.
                if (distance < MinimumDistance) { dx = MinimumDistance; dy = 0d; distance = MinimumDistance; }
                double pressure = _settings.Stiffness * ((_density[i] + _density[j]) * 0.5d - _settings.RestDensity);
                pressure = Math.Max(-_settings.MaximumAttraction, pressure);
                double near = _settings.NearStiffness * (_nearDensity[i] + _nearDensity[j]) * 0.5d;
                // 두 입자에 같은 크기의 반대 방향 보정을 나누어 적용합니다.
                double shift = dt * dt * (pressure * q + near * q * q) * 0.5d;
                shift = Math.Max(shift, (_settings.ParticleRadius * 2d - distance) * _settings.SeparationStrength);
                shift = Math.Max(-_settings.MaximumPullShift, Math.Min(_settings.MaximumPushShift, shift));
                double x = dx / distance * shift;
                double y = dy / distance * shift;
                _particles[i].X -= x;
                _particles[j].X += x;
                _particles[i].Y -= y;
                _particles[j].Y += y;
        }

        /// <summary>입구를 통과한 입자를 컵의 곡선 경계 안에 유지합니다.</summary>
        void ResolveCup(int index)
        {
            // 배열 원소를 직접 수정하며 컵 옆에서 떨어지는 입자는 끌어들이지 않습니다.
            ref ParticleState p = ref _particles[index];
            double radius = _settings.ParticleRadius;
            if (!p.Entered && p.Y <= _cup.Rim && p.OldY > _cup.Rim)
            {
                // 현재 위치가 아니라 이동 선분이 입구 높이를 통과한 위치를 검사합니다.
                double fraction = (p.OldY - _cup.Rim) / (p.OldY - p.Y);
                double crossingX = p.OldX + (p.X - p.OldX) * fraction;
                p.Entered = crossingX > _cup.LeftAt(_cup.Rim) + radius && crossingX < _cup.RightAt(_cup.Rim) - radius;
            }
            if (!p.Entered) return;
            // 공통 곡선 경계로 입자 전체를 컵 안에 유지합니다.
            _cup.Constrain(ref p.X, ref p.Y, radius);
        }

        /// <summary>보정된 속도를 갱신하고 컵 밖 입자가 컵 바닥 높이에 도달하면 회수합니다.</summary>
        void UpdateVelocitiesAndSpills(double dt)
        {
            // 삭제한 자리에 마지막 입자를 옮기므로 역순으로 처리합니다.
            for (int i = Count - 1; i >= 0; i--)
            {
                // 위치 보정까지 반영한 실제 이동 속도를 복원합니다.
                ref ParticleState p = ref _particles[i];
                p.VX = (p.X - p.OldX) / dt;
                p.VY = (p.Y - p.OldY) / dt;
                double speed = Math.Sqrt(p.VX * p.VX + p.VY * p.VY);
                // 공중 입자는 기존 감쇠를 유지하고 컵에 들어온 입자에만 추가합니다.
                double damping = _settings.VelocityDamping + (p.Entered ? _settings.CupVelocityDamping : 0d);
                // 시간 기반 지수 감쇠로 물리 스텝 크기에 따른 감쇠 차이를 방지합니다.
                double scale = Math.Exp(-damping * dt) *
                    Math.Min(1d, _settings.MaximumSpeed / Math.Max(_settings.MaximumSpeed, speed));
                // 물량은 유지하고 좌우·상하 속도만 동일한 비율로 줄입니다.
                p.VX *= scale;
                p.VY *= scale;
                // 컵 밖 물방울은 좌우 경계를 지나도 즉시 삭제하지 않고 바닥 높이까지 유지합니다.
                // 컵 입구를 다시 통과한 물방울은 ResolveCup에서 내부 상태로 돌아가므로 보존됩니다.
                if (p.Entered || p.Y > _cup.Bottom) continue;
                // 표현상 유실량만 기록하며 게임의 총 생성량에는 영향을 주지 않습니다.
                Spilled += p.Volume;
                _particles[i] = _particles[--Count];
            }
        }
    }
}
