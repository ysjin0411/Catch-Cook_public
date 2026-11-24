using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 패턴 판단은 지정 주기 또는 요청 시에만 실행하고 연속 경로 실행은 따로 갱신합니다.
/// </summary>
public sealed class FishPatternScheduler
{
    // 각 무리는 독립된 판단 시각과 누적 시간을 가집니다.
    private sealed class Entry
    {
        public FishSpawnPattern Pattern;
        public double LastDecision;
        public double NextDecision;
        public bool FirstDecision = true;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private double _clock;

    /// <summary>
    /// 최초 판단을 예약하고 이후 판단 시점을 무리별로 분산합니다.
    /// </summary>
    public void Register(FishSpawnPattern pattern)
    {
        // 배경 전용 패턴은 기존 독립 실행을 유지합니다.
        if (pattern == null || !pattern.RequiresFixedTick)
            return;

        // 실제 경과 시간은 이 무리가 등록된 시점부터 계산합니다.
        _entries.Add(new Entry
        {
            Pattern = pattern,
            LastDecision = _clock,
            NextDecision = _clock
        });
    }

    /// <summary>
    /// 실행 시각이 됐거나 회피 요청이 있는 무리만 판단하고 경로 실행을 진행합니다.
    /// </summary>
    public void Advance(float deltaTime)
    {
        // 잘못된 시간은 경로와 예약 시계에 적용하지 않습니다.
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;
        _clock += deltaTime;

        // 이 순회에서는 물고기 전체가 아닌 무리별 실행 시각만 확인합니다.
        foreach (Entry entry in _entries)
        {
            FishSpawnPattern pattern = entry.Pattern;
            if (pattern == null)
                continue;

            // 회피 대기 시간은 판단 횟수와 독립된 물리 시간으로 진행합니다.
            pattern.AdvanceRuntimeClock(deltaTime);
            if (_clock >= entry.NextDecision || pattern.DecisionRequested)
            {
                // 조기 갱신에서도 마지막 판단 이후 실제 경과 시간만 전달합니다.
                float elapsed = (float)(_clock - entry.LastDecision);
                pattern.ConsumeDecisionRequest();
                pattern.Tick(elapsed);
                entry.LastDecision = _clock;

                // 최초 판단 이후 첫 간격을 분산하고 이후에는 설정된 주기를 유지합니다.
                float interval = pattern.DecisionInterval;
                entry.NextDecision = _clock + (entry.FirstDecision
                    ? Random.Range(Mathf.Min(deltaTime, interval), interval)
                    : interval);
                entry.FirstDecision = false;
            }

            // 파동과 점프의 진행만 매 물리 단계에 실행하며 판단 Tick은 재호출하지 않습니다.
            if (pattern.RequiresPhysicsPath)
                pattern.TickPhysicsPath(deltaTime);
        }
    }

    /// <summary>
    /// 스포너 종료 시 실행본 참조를 해제합니다.
    /// </summary>
    public void Clear()
    {
        // 파괴된 패턴이 다음 실행에서 사용되지 않게 합니다.
        _entries.Clear();
        _clock = 0d;
    }
}
