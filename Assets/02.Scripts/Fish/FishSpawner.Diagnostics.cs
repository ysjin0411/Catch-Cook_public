using UnityEngine;

public partial class FishSpawner
{
    /// <summary>한 번의 최초 패턴 생성에서 예정 수와 성공 수를 보관합니다.</summary>
    private sealed class InitialSpawnSummary
    {
        // 이번 실행에서 추첨된 일반 물고기와 배경 물고기의 예정 마릿수입니다.
        internal int CatchableTotal, BackgroundTotal;
        // 전체 생성에 성공하여 유지된 패턴의 개체 수입니다.
        internal int CatchableSuccess, BackgroundSuccess;
        // 패턴 누락 등으로 예정 마릿수를 계산할 수 없는 설정의 수입니다.
        internal int InvalidEntries;
    }

    /// <summary>최초 생성 수량을 확정하고 생성이 끝나면 성공·실패 마릿수를 한 번 출력합니다.</summary>
    private void Start()
    {
        // 직접 배치한 개체의 기존 초기화는 유지하며 패턴 스폰 집계와 구분합니다.
        InitializeSceneFish();
        var summary = new InitialSpawnSummary();
        int[][] plan = BuildInitialSpawnPlan(summary);
        try
        {
            // 같은 계획을 실행하므로 집계를 위해 랜덤 수량을 다시 추첨하지 않습니다.
            SpawnInitialPatterns(plan, summary);
        }
        finally
        {
            // 중간 오류로 생성을 완료하지 못해도 예정 수와 완료 수의 차이를 남깁니다.
            LogInitialSpawnSummary(summary);
        }
    }

    /// <summary>모든 그룹의 실행 횟수를 미리 추첨하여 생성 불가능한 그룹도 예정 수에 포함합니다.</summary>
    private int[][] BuildInitialSpawnPlan(InitialSpawnSummary summary)
    {
        // 설정이 없는 스포너도 0마리 요약을 출력할 수 있도록 빈 계획을 반환합니다.
        int groupCount = _waterDepthCategories != null ? _waterDepthCategories.Length : 0;
        var plan = new int[groupCount][];
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            // 비어 있는 그룹은 마릿수를 알 수 없으므로 별도 설정 오류로 구분합니다.
            SpawnGroup group = _waterDepthCategories[groupIndex];
            if (group == null || group.Patterns == null)
            {
                summary.InvalidEntries++;
                plan[groupIndex] = new int[0];
                continue;
            }

            // 그룹별 실행 횟수 배열은 원래 패턴 슬롯 순서를 유지합니다.
            plan[groupIndex] = new int[group.Patterns.Length];
            for (int entryIndex = 0; entryIndex < group.Patterns.Length; entryIndex++)
            {
                // 누락된 패턴은 임의로 1마리라고 계산하지 않습니다.
                SpawnEntry entry = group.Patterns[entryIndex];
                if (entry == null || entry.Pattern == null || entry.Pattern.InitialMemberCount <= 0)
                {
                    summary.InvalidEntries++;
                    continue;
                }

                // 기존과 같이 양 끝값을 포함하며 음수와 뒤집힌 범위를 보정합니다.
                int minimum = Mathf.Max(0, Mathf.Min(entry.MinCount, entry.MaxCount));
                int maximum = Mathf.Max(0, Mathf.Max(entry.MinCount, entry.MaxCount));
                int executions = minimum + Random.Range(-1, maximum - minimum) + 1;
                plan[groupIndex][entryIndex] = executions;

                // 무리 하나의 개체 수를 곱해 실행 횟수가 아닌 실제 마릿수로 기록합니다.
                int members = executions * entry.Pattern.InitialMemberCount;
                if (entry.Pattern is BackgroundFishPattern) summary.BackgroundTotal += members;
                else summary.CatchableTotal += members;
            }
        }

        // 생성 코드와 디버그 출력이 공유할 확정 계획입니다.
        return plan;
    }

    /// <summary>총 예정 수, 생성 성공 수, 실패 수를 일반·배경·합계로 출력합니다.</summary>
    private void LogInitialSpawnSummary(InitialSpawnSummary summary)
    {
        // 실패 후 정리된 부분 생성물과 실행하지 못한 예정 개체는 실패에 포함합니다.
        int catchableFailed = summary.CatchableTotal - summary.CatchableSuccess;
        int backgroundFailed = summary.BackgroundTotal - summary.BackgroundSuccess;
        int total = summary.CatchableTotal + summary.BackgroundTotal;
        int success = summary.CatchableSuccess + summary.BackgroundSuccess;
        string message = $"[FishSpawner 최초 패턴 생성] {gameObject.scene.name}/{name}\n" +
            $"일반 물고기 | 총 {summary.CatchableTotal}마리 / 성공 {summary.CatchableSuccess}마리 / 실패 {catchableFailed}마리\n" +
            $"배경 물고기 | 총 {summary.BackgroundTotal}마리 / 성공 {summary.BackgroundSuccess}마리 / 실패 {backgroundFailed}마리\n" +
            $"합계 | 총 {total}마리 / 성공 {success}마리 / 실패 {total - success}마리";

        // 수량을 계산할 수 없는 누락 설정은 숫자 합계와 섞지 않고 따로 표시합니다.
        if (summary.InvalidEntries > 0)
            message += $"\n수량 산정 불가 설정: {summary.InvalidEntries}개";

        // 생성 실패나 설정 오류가 있으면 Console에서 쉽게 찾도록 경고로 출력합니다.
        if (total != success || summary.InvalidEntries > 0) Debug.LogWarning(message, this);
        else Debug.Log(message, this);
    }
}
