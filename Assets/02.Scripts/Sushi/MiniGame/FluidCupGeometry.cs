using System;

namespace SushiPouringV3
{
    /// <summary>컵의 공통 경계와 면적 기반 수위를 제공하는 읽기 전용 기하학입니다.</summary>
    public sealed class FluidCupGeometry
    {
        readonly double[] _left, _right, _areas;
        readonly double _step;
        readonly double _safeBottom;
        public double Bottom { get; }
        public double Rim { get; }
        public double BottomHalfWidth { get; }
        public double TopHalfWidth { get; }
        public double CupArea => _areas[_areas.Length - 1];

        /// <summary>설정의 경계 표본을 복사하고 누적 면적과 입자 바닥을 계산합니다.</summary>
        public FluidCupGeometry(FluidSimulationSettings settings)
        {
            // 입력을 검증하고 라운드의 높이를 보관합니다.
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            Bottom = settings.Bottom;
            Rim = settings.Rim;
            BottomHalfWidth = settings.BottomHalfWidth;
            TopHalfWidth = settings.TopHalfWidth;
            // 기존 설정은 두 표본의 사다리꼴로 호환합니다.
            int count = Math.Max(2, settings.ProfileCount);
            _left = new double[count]; _right = new double[count]; _areas = new double[count];
            _step = (Rim - Bottom) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                // 프로파일이 있으면 비대칭 경계를 그대로 사용합니다.
                double half = i == 0 ? BottomHalfWidth : TopHalfWidth;
                _left[i] = settings.ProfileCount > 0 ? settings.GetProfile(i).x : -half;
                _right[i] = settings.ProfileCount > 0 ? settings.GetProfile(i).y : half;
                // 선형 경계 사이의 면적을 사다리꼴 적분으로 누적합니다.
                if (i > 0) _areas[i] = _areas[i - 1] + _step *
                    (_right[i - 1] - _left[i - 1] + _right[i] - _left[i]) * .5d;
            }
            // 입자 전체가 들어갈 수 있는 최초 높이를 찾아 바닥 끼임을 방지합니다.
            double radius = settings.ParticleRadius;
            _safeBottom = Rim - radius;
            bool found = false;
            for (int i = 0; i <= 2048; i++)
            {
                double y = Bottom + radius + (Rim - Bottom - 2d * radius) * i / 2048d;
                GetCenterBounds(y, radius, out double left, out double right);
                if (left > right) continue;
                _safeBottom = y; found = true; break;
            }
            // 지나치게 좁은 컵은 잘못된 위치로 입자를 보내지 않고 거부합니다.
            if (!found) throw new ArgumentException("컵 내부가 입자 지름보다 좁습니다.");
        }

        /// <summary>등간격 경계 배열을 지정한 높이에서 선형 보간합니다.</summary>
        double Sample(double[] values, double y)
        {
            // 배열 끝에서 범위를 벗어나지 않도록 보간 구간을 제한합니다.
            double t = Math.Max(0d, Math.Min(values.Length - 1d, (y - Bottom) / _step));
            int index = Math.Min(values.Length - 2, (int)t);
            return values[index] + (values[index + 1] - values[index]) * (t - index);
        }

        /// <summary>높이별 왼쪽 내부 경계를 반환합니다.</summary>
        public double LeftAt(double y)
        {
            // 물리와 표시가 공유하는 표본을 조회합니다.
            return Sample(_left, y);
        }

        /// <summary>높이별 오른쪽 내부 경계를 반환합니다.</summary>
        public double RightAt(double y)
        {
            // 물리와 표시가 공유하는 표본을 조회합니다.
            return Sample(_right, y);
        }

        /// <summary>높이별 내부 너비의 절반을 반환합니다.</summary>
        public double HalfWidthAt(double y)
        {
            // 비대칭 컵도 좌우 경계 차이로 너비를 계산합니다.
            return (RightAt(y) - LeftAt(y)) * .5d;
        }

        /// <summary>입자를 둘러싼 사각형 전체가 들어가는 중심 X 범위를 구합니다.</summary>
        void GetCenterBounds(double y, double radius, out double left, out double right)
        {
            // 원을 감싸는 사각형을 사용해 급한 바닥 곡선에서도 관통을 방지합니다.
            double low = Math.Max(Bottom, y - radius), high = Math.Min(Rim, y + radius);
            left = Math.Max(LeftAt(low), LeftAt(high));
            right = Math.Min(RightAt(low), RightAt(high));
            // 선형 구간의 극값은 끝점에 있으므로 겹치는 모든 표본을 검사합니다.
            int first = Math.Max(0, (int)Math.Ceiling((low - Bottom) / _step));
            int last = Math.Min(_left.Length - 1, (int)Math.Floor((high - Bottom) / _step));
            for (int i = first; i <= last; i++)
            {
                left = Math.Max(left, _left[i]);
                right = Math.Min(right, _right[i]);
            }
            // 중심에서 입자 가장자리까지의 여유를 확보합니다.
            left += radius; right -= radius;
        }

        /// <summary>담긴 입자를 가까운 내부 위치로 보정하여 곡면에서 수평으로 과도하게 밀리지 않게 합니다.</summary>
        public void Constrain(ref double x, ref double y, double radius)
        {
            // 원래 위치를 보존하여 수평·수직 이동을 합친 보정 거리를 비교합니다.
            double originalX = x, originalY = y;
            double top = Rim - radius;
            double bestY = Math.Max(_safeBottom, Math.Min(top, y));
            GetCenterBounds(bestY, radius, out double left, out double right);
            // 이미 내부에 있는 입자는 미세한 위치 보정도 하지 않습니다.
            if (bestY == y && left <= x && x <= right) return;
            // 좁은 구간에서도 항상 유효한 초기 후보를 확보합니다.
            if (left > right)
            {
                // 생성 시 검증한 바닥 높이에서 입자가 들어갈 범위를 구합니다.
                bestY = _safeBottom;
                GetCenterBounds(bestY, radius, out left, out right);
            }
            // 기존 수평 보정을 상한으로 두어 그보다 더 멀리 입자를 옮기지 않습니다.
            double bestX = Math.Max(left, Math.Min(right, x));
            double dx = bestX - originalX, dy = bestY - originalY;
            double bestDistanceSquared = dx * dx + dy * dy;
            double stride = (top - _safeBottom) / 128d;
            // 전체 높이를 표본 조사하여 곡면 위쪽의 더 가까운 내부 위치도 찾습니다.
            for (int i = 0; i <= 128; i++)
            {
                // 수직 거리만으로 더 먼 후보는 내부 경계를 조회하기 전에 제외합니다.
                ConsiderConstraintPosition(originalX, originalY, _safeBottom + i * stride,
                    radius, ref bestX, ref bestY, ref bestDistanceSquared);
            }
            // 가장 가까운 표본 주변을 좁혀 프레임마다 표본 간격만큼 뛰는 현상을 줄입니다.
            for (int i = 0; i < 14; i++)
            {
                // 같은 중심의 위·아래 후보를 비교한 뒤 다음 탐색 간격을 줄입니다.
                stride *= 0.5d;
                double center = bestY;
                ConsiderConstraintPosition(originalX, originalY, Math.Max(_safeBottom, center - stride),
                    radius, ref bestX, ref bestY, ref bestDistanceSquared);
                ConsiderConstraintPosition(originalX, originalY, Math.Min(top, center + stride),
                    radius, ref bestX, ref bestY, ref bestDistanceSquared);
            }
            // 선택한 내부 위치의 두 좌표를 함께 반영하여 곡면 방향으로 보정합니다.
            x = bestX;
            y = bestY;
        }

        /// <summary>지정한 높이의 유효한 내부 위치가 현재 후보보다 가까우면 보정 후보를 갱신합니다.</summary>
        void ConsiderConstraintPosition(double originalX, double originalY, double candidateY,
            double radius, ref double bestX, ref double bestY, ref double bestDistanceSquared)
        {
            // 높이 차이만으로 현재 최소 거리 이상이면 비싼 프로파일 조회를 생략합니다.
            double dy = candidateY - originalY;
            if (dy * dy >= bestDistanceSquared) return;
            // 입자 전체가 들어갈 수 없는 높이는 후보에서 제외합니다.
            GetCenterBounds(candidateY, radius, out double left, out double right);
            if (left > right) return;
            // 해당 높이에서 원래 위치에 가장 가까운 X 좌표를 구합니다.
            double candidateX = Math.Max(left, Math.Min(right, originalX));
            double dx = candidateX - originalX;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared) return;
            // 더 짧은 이동으로 경계를 만족하는 후보만 채택합니다.
            bestX = candidateX;
            bestY = candidateY;
            bestDistanceSquared = distanceSquared;
        }

        /// <summary>누적 내부 면적을 역산해 지정 물량의 수위를 구합니다.</summary>
        public double YAtVolume(double volume)
        {
            // 과다 물량의 목표선이 컵 위로 벗어나지 않도록 표시 범위를 제한합니다.
            FluidNumber.RequireNonNegative(volume, nameof(volume));
            double target = Math.Min(1d, volume) * CupArea;
            int index = Array.BinarySearch(_areas, target);
            if (index >= 0) return Bottom + index * _step;
            index = Math.Max(0, Math.Min(_areas.Length - 2, ~index - 1));
            // 구간 내부의 선형 너비를 적분한 이차식을 이분 탐색으로 역산합니다.
            double low = 0d, high = _step;
            double width = _right[index] - _left[index];
            double slope = (_right[index + 1] - _left[index + 1] - width) / _step;
            for (int i = 0; i < 40; i++)
            {
                double middle = (low + high) * .5d;
                double area = _areas[index] + width * middle + slope * middle * middle * .5d;
                if (area < target) low = middle; else high = middle;
            }
            // 전체 높이와 현재 구간의 높이를 합칩니다.
            return Bottom + index * _step + (low + high) * .5d;
        }
    }
}
