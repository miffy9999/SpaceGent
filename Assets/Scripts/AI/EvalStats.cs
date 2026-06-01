using System.Collections.Generic;
using System.Text;
using UnityEngine;

// =====================================================================
//  EvalStats — 미션 성공률 누적 집계 (평가 하니스).
// ---------------------------------------------------------------------
//  미션이 끝날 때마다 Record()로 결과를 모아, LogEvery 회마다 Unity 콘솔에
//  누적 성공률을 출력한다. RuleBased vs MCTS vs RL을 같은 잣대로 비교.
//
//  사용: GameManager.evaluationLogging = true 로 켜면 자동 집계.
//  결과 예:
//    [Eval] 미션 200회 | 정책=MCTS
//      전체            : 138/200 = 69.0%
//      태스크수별: t1 95% t2 80% t3 62% ...
// =====================================================================
public static class EvalStats
{
    public static int LogEvery = 50;     // 몇 미션마다 콘솔 로그

    private static int total, success;
    private static string currentPolicy = "?";

    // 태스크 개수(난이도)별 집계
    private static readonly Dictionary<int, int> countByTasks   = new Dictionary<int, int>();
    private static readonly Dictionary<int, int> successByTasks = new Dictionary<int, int>();

    // 태스크 완수 합계 (평균 완수율 = 민감한 비교 지표)
    private static long sumCompleted, sumTotalTasks;

    // ---------------------------------------------------------------
    // 미션 1회 결과 기록. policy 라벨이 바뀌면(정책 전환) 누적 리셋.
    //   completedTasks/totalTasks: 평균 완수율 집계용 (전부-아니면-전무보다 민감).
    // ---------------------------------------------------------------
    public static void Record(string policy, int taskCount, bool ok,
                              int completedTasks = 0, int totalTasks = 0)
    {
        if (policy != currentPolicy)
        {
            // 정책이 바뀌면 비교 오염 방지 위해 리셋하고 새로 집계
            Reset();
            currentPolicy = policy;
        }

        total++;
        if (ok) success++;
        sumCompleted  += completedTasks;
        sumTotalTasks += totalTasks;

        if (!countByTasks.ContainsKey(taskCount)) { countByTasks[taskCount] = 0; successByTasks[taskCount] = 0; }
        countByTasks[taskCount]++;
        if (ok) successByTasks[taskCount]++;

        if (total % LogEvery == 0) Log();
    }

    public static void Reset()
    {
        total = success = 0;
        sumCompleted = sumTotalTasks = 0;
        countByTasks.Clear();
        successByTasks.Clear();
    }

    // 강제 출력 (현재까지 누적)
    public static void Log()
    {
        if (total == 0) { Debug.Log("[Eval] 집계된 미션 없음"); return; }

        float rate = (float)success / total;
        float avgComplete = sumTotalTasks > 0 ? (float)sumCompleted / sumTotalTasks : 0f;
        var sb = new StringBuilder();
        sb.AppendLine($"[Eval] 미션 {total}회 | 정책={currentPolicy}");
        sb.AppendLine($"  미션 성공률     : {success}/{total} = {rate:P1}");
        sb.AppendLine($"  평균 태스크 완수 : {sumCompleted}/{sumTotalTasks} = {avgComplete:P1}  (민감 비교 지표)");

        // 태스크 개수별 (난이도 프록시) 오름차순
        var keys = new List<int>(countByTasks.Keys);
        keys.Sort();
        var line = new StringBuilder("  태스크수별 성공 : ");
        foreach (int k in keys)
        {
            int n = countByTasks[k];
            int s = successByTasks[k];
            float r = n > 0 ? (float)s / n : 0f;
            line.Append($"t{k}={s}/{n}({r:P0}) ");
        }
        sb.AppendLine(line.ToString().TrimEnd());

        Debug.Log(sb.ToString());
    }
}
