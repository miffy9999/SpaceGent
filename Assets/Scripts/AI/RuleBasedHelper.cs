using System.Collections.Generic;

// =====================================================================
//  Rule-Based Helper — Hierarchical FSM
// ---------------------------------------------------------------------
//  도우미(담당자가 아닌 3명)의 카드 선택을 명시적 2층 state machine으로 분리.
//
//  Layer 1 (Outer / per-episode) — 어떤 전략 모드를 쓰는가
//    Phase1Task (WinAtLeast / WinFirst / WinLast / WinNone)
//    → 각 task마다 IHelperStrategy 구현체 1개.
//
//  Layer 2 (Inner / per-card) — 그 모드 안에서 지금 어떤 행동을 하는가
//    HelperAction (Throw / Burn / Block / Save)
//    → 트릭 진행 상황(HelperContext)을 보고 매번 결정.
//
//  카드 선택은 마지막 단계에서 HelperAction을 TrickManager의
//  Safest / Highest / Claiming 헬퍼로 변환.
//
//  설계 의도:
//   1. 매 결정의 reasoning을 명시 → TensorBoard로 비율 측정 가능
//      (예: WinLast에서 Burn 비율이 90%인지 확인 → 의도대로 작동했는지 검증)
//   2. 새 task type 추가 = 새 IHelperStrategy 1개만 작성
//   3. "왜 그 카드를 골랐는가"가 코드/로그에서 명확
// =====================================================================

// 하위 state — "이 카드를 어떤 의도로 내는가"
public enum HelperAction
{
    Throw,   // 담당자에게 트릭 양보 (가장 안 이길 카드)
    Burn,    // 큰 카드를 미리 소진 (가장 강한 카드)
    Block,   // 담당자가 이기지 못하게 차단 (이기는 카드 중 가장 낮은 것)
    Save,    // 카드 절약 (담당자가 이미 진 트릭에서 낭비 방지)
}

// 결정에 필요한 모든 상태 변수 — 결정 함수의 순수성(side-effect-free) 보장
public struct HelperContext
{
    public CrewAgent    helper;       // 결정하는 본인 (helper 또는 assignee 본인)
    public CrewAgent    assignee;     // 이번 에피소드 담당자
    public TrickManager trickManager;

    public bool isLeading;        // 트릭의 첫 카드를 내는 차례인가
    public bool assigneePlayed;   // 담당자가 이번 트릭에 이미 카드를 냈는가
    public bool assigneeWinning;  // 담당자의 카드가 현재 이기는 중인가
    public bool isLastTrick;      // 이번 트릭이 핸드의 마지막 트릭인가
    public bool isFirstTrick;     // 이번 트릭이 핸드의 첫 트릭인가 (WinFirst assignee용)
    public int  myCurrentWins;    // 본인의 현재 트릭 승리 수 (WinAtLeast assignee용)
    public int  winTarget;        // WinAtLeast의 N (그 외 task는 무의미, 0)

    // ── 업그레이드된 정책용 추가 필드 ──────────────────────────────────
    public int  remainingTricks;  // 이번 트릭 포함 남은 트릭 수 (WinAtLeast urgency 판정용)
    public bool isLastToPlay;     // 이번 트릭의 마지막 플레이어 차례인가
                                  //   true면 Block(정확히 이길 만큼만)이 항상 최적
                                  //   false면 후속 overtake 위험 → must-win 시 Burn 안전

    // [v3 카드 메모리] 이번 핸드에서 이미 플레이된 카드.
    //   Block 결정 시 "guaranteed winner" 판정에 사용.
    //   null이면 메모리 비활성 (기존 Claim 동작).
    public System.Collections.Generic.HashSet<Card> playedCardsInHand;
}

// 상위 state 인터페이스 — task type별 IHelperStrategy 구현
public interface IHelperStrategy
{
    HelperAction DecideAction(in HelperContext ctx);
}

// =====================================================================
//  4개 task 전략 — 각자가 상황별로 하위 state(HelperAction)를 결정
// =====================================================================

// ── WinAtLeast ─────────────────────────────────────────────────────────
//  담당자가 N트릭 이상 이겨야 함.
//  도우미: 항상 양보 (담당자의 카드 가치를 최대화).
//  상황 변수와 무관하게 일관된 정책 — task 자체의 본질이 그러함.
public sealed class WinAtLeastHelperStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx) => HelperAction.Throw;
}

// ── WinFirst ──────────────────────────────────────────────────────────
//  담당자가 트릭 1을 이겨야 함.
//  도우미: 트릭 1에서는 양보, 이후 트릭은 task가 이미 확정되었으므로
//          어떤 카드든 무방 — 단순화로 계속 Throw.
//  ※ 구조적 천장: 트릭 1에서 담당자가 합법 winning card를 갖고 있어야 성공.
//    도우미 정책으로 해결 불가.
public sealed class WinFirstHelperStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx) => HelperAction.Throw;
}

// ── WinLast ───────────────────────────────────────────────────────────
//  담당자가 마지막 트릭을 이겨야 함.
//  핵심 통찰: 도우미의 큰 카드를 미리 소진해야 마지막 트릭에 작은 카드만 남음.
//
//  하위 state 분기:
//    마지막 트릭        → Throw (양보)
//    담당자 이기는 중    → Throw (자연스럽게 양보, 큰 카드 1트릭 보존)
//    그 외 (초반)       → Burn
//
//  [v3 롤백 2026-05-28] "assigneeWinning → Throw" 분기 복원:
//   v2에서 이 분기를 제거하고 항상 Burn 시도했으나 실측 회귀(75% → 70%).
//   가설: 분기 제거 시 도우미가 우연히 이기는 트릭이 늘어 담당자 mid-trick 보상 패턴이
//        깨졌을 가능성. v1 분기는 도우미-담당자 자연스러운 균형을 유지함.
public sealed class WinLastHelperStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx)
    {
        if (ctx.isLastTrick)     return HelperAction.Throw;
        if (ctx.assigneeWinning) return HelperAction.Throw;
        return HelperAction.Burn;
    }
}

// ── WinNone ───────────────────────────────────────────────────────────
//  담당자가 단 한 트릭도 이기면 안 됨.
//  도우미: 담당자가 이기는 트릭은 반드시 차단, 그 외엔 카드 절약.
//
//  하위 state 분기:
//    담당자 이미 냈고 이기는 중 → Block (담당자 카드를 뺏음)
//    담당자 이미 냈고 졌음        → Save (낭비 방지)
//    담당자 아직 미플레이 + 도우미가 리드 → Burn (큰 카드 선제 차단)
//    담당자 아직 미플레이 + 도우미가 미드 → Block (이길 수 있으면 이김)
//
//  ※ 잔존 문제: 이론상 도우미가 항상 차단 불가능 (예: 담당자가 잠수함 4 보유).
//    fast-fail 에피소드 truncation은 구조적이라 helper 품질로 해결 불가.
public sealed class WinNoneHelperStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx)
    {
        if (ctx.assigneePlayed)
            return ctx.assigneeWinning ? HelperAction.Block : HelperAction.Save;

        return ctx.isLeading ? HelperAction.Burn : HelperAction.Block;
    }
}

// =====================================================================
//  4개 task 전략 — Assignee (담당자) 시점
//  All-rule-based 시뮬레이션 모드에서 사용. 담당자가 task를 스스로 달성하는 방향.
//
//  사용 action 매핑 (도우미와 의미가 다름):
//    Throw  = 양보/절약 — 이번 트릭을 안 가져감 (가장 안 이길 카드)
//    Block  = 적극 획득 — 이기는 카드 중 가장 낮은 것 (카드 절약)
//    (Burn/Save는 assignee용으로는 거의 안 씀)
// =====================================================================

// ── WinAtLeast assignee ───────────────────────────────────────────────
//  N트릭 이상 이겨야 함.
//
//  [v4 — 현행] 4분기 결정:
//    needed ≤ 0 (이미 달성)             → Throw (카드 절약)
//    마지막 플레이어 차례                 → Block (정확히 이길 만큼만, 효율 우선)
//    needed ≥ remainingTricks (urgent)  → Burn (남은 트릭 다 이겨야 함, 안전 우선)
//    그 외 (여유)                        → Block (효율적)
//
//  근거: Block은 카드 절약하지만 후속 overtake에 취약. 남은 트릭에 여유가
//        있으면 Block의 효율이 우월, 여유 없으면 Burn으로 천장.
//
//  [v6 폐기 2026-05-28]: mid에서 항상 Burn 시도했으나 평균 wins -0.20, 성공률 -7%p.
//   가설("Burn으로 overtake 방지 → 평균 wins ↑")이 틀림.
//   실제: Burn이 초반 큰 카드 다 써서 후반 winning 못 함. 카드 낭비 손실 > overtake 이득.
//   → v4 urgency 기반 분기가 균형점. CHANGELOG v6 참고.
public sealed class WinAtLeastAssigneeStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx)
    {
        int needed = ctx.winTarget - ctx.myCurrentWins;
        if (needed <= 0)                        return HelperAction.Throw;
        if (ctx.isLastToPlay)                   return HelperAction.Block;
        if (needed >= ctx.remainingTricks)      return HelperAction.Burn;
        return HelperAction.Block;
    }
}

// ── WinFirst assignee ─────────────────────────────────────────────────
//  트릭 1을 이겨야 함. 트릭 1 이후는 task 확정 → 아무거나 OK.
//
//  [업그레이드] 위치별 분기:
//    트릭 1 + 마지막 플레이어 → Block (정확히 이길 만큼만, 효율적)
//    트릭 1 + 미드 플레이어   → Burn (후속 overtake 차단, 안전)
//    그 외                    → Throw
//
//  근거: 트릭 1에서 Block은 "이기는 카드 중 가장 낮은 것"이라 후속 플레이어가
//        한 단계 더 큰 카드로 overtake 가능. must-win 트릭에선 Burn으로 천장.
public sealed class WinFirstAssigneeStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx)
    {
        if (!ctx.isFirstTrick) return HelperAction.Throw;
        return ctx.isLastToPlay ? HelperAction.Block : HelperAction.Burn;
    }
}

// ── WinLast assignee ──────────────────────────────────────────────────
//  마지막 트릭을 이겨야 함. 초반: Throw로 큰 카드 보존, 마지막: 적극 획득.
//
//  [업그레이드] 마지막 트릭에서 위치별 분기 (WinFirst와 동일 논리):
//    마지막 트릭 + 마지막 플레이어 → Block
//    마지막 트릭 + 미드 플레이어   → Burn
//    그 외                         → Throw (보존)
public sealed class WinLastAssigneeStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx)
    {
        if (!ctx.isLastTrick) return HelperAction.Throw;
        return ctx.isLastToPlay ? HelperAction.Block : HelperAction.Burn;
    }
}

// ── WinNone assignee ──────────────────────────────────────────────────
//  단 한 트릭도 이기면 안 됨. 항상 안 이길 카드.
//  ※ 손패 구조상 이길 수밖에 없는 경우(예: 잠수함 4만 남음) → 즉시 실패.
public sealed class WinNoneAssigneeStrategy : IHelperStrategy
{
    public HelperAction DecideAction(in HelperContext ctx) => HelperAction.Throw;
}

// =====================================================================
//  상위 dispatcher — task type을 IHelperStrategy로 라우팅
//  + HelperAction을 실제 손패 인덱스로 변환
//  + action 분포 통계 누적 (TensorBoard 송출용)
// =====================================================================
public static class RuleBasedHelper
{
    // Helper 전략 (도우미용)
    private static readonly Dictionary<MissionManager.Phase1Task, IHelperStrategy> HelperStrategies =
        new Dictionary<MissionManager.Phase1Task, IHelperStrategy>
        {
            { MissionManager.Phase1Task.WinAtLeast, new WinAtLeastHelperStrategy() },
            { MissionManager.Phase1Task.WinFirst,   new WinFirstHelperStrategy()   },
            { MissionManager.Phase1Task.WinLast,    new WinLastHelperStrategy()    },
            { MissionManager.Phase1Task.WinNone,    new WinNoneHelperStrategy()    },
        };

    // Assignee 전략 (담당자용, all-rule-based 시뮬레이션)
    private static readonly Dictionary<MissionManager.Phase1Task, IHelperStrategy> AssigneeStrategies =
        new Dictionary<MissionManager.Phase1Task, IHelperStrategy>
        {
            { MissionManager.Phase1Task.WinAtLeast, new WinAtLeastAssigneeStrategy() },
            { MissionManager.Phase1Task.WinFirst,   new WinFirstAssigneeStrategy()   },
            { MissionManager.Phase1Task.WinLast,    new WinLastAssigneeStrategy()    },
            { MissionManager.Phase1Task.WinNone,    new WinNoneAssigneeStrategy()    },
        };

    // 에피소드별 action 카운트
    public static int CountThrow, CountBurn, CountBlock, CountSave, CountTotal;

    public static void ResetEpisodeStats()
    {
        CountThrow = CountBurn = CountBlock = CountSave = CountTotal = 0;
    }

    public struct Decision
    {
        public HelperAction action;
        public int cardIndex;
    }

    // 도우미 결정 (기존 진입점)
    public static Decision Decide(in HelperContext ctx, MissionManager.Phase1Task task)
        => DecideInternal(in ctx, task, HelperStrategies);

    // 담당자 결정 (all-rule-based 시뮬레이션용)
    public static Decision DecideAssignee(in HelperContext ctx, MissionManager.Phase1Task task)
        => DecideInternal(in ctx, task, AssigneeStrategies);

    private static Decision DecideInternal(in HelperContext ctx, MissionManager.Phase1Task task,
                                            Dictionary<MissionManager.Phase1Task, IHelperStrategy> table)
    {
        IHelperStrategy strat = table.TryGetValue(task, out var s)
            ? s : table[MissionManager.Phase1Task.WinAtLeast];

        HelperAction action = strat.DecideAction(in ctx);
        int idx = ResolveCard(in ctx, action);

        CountTotal++;
        switch (action)
        {
            case HelperAction.Throw: CountThrow++; break;
            case HelperAction.Burn:  CountBurn++;  break;
            case HelperAction.Block: CountBlock++; break;
            case HelperAction.Save:  CountSave++;  break;
        }
        return new Decision { action = action, cardIndex = idx };
    }

    // ── HelperAction → 손패 인덱스 변환 ─────────────────────────────────
    //   Throw/Save : 가장 안 이길 카드 (Safest)
    //   Burn       : 가장 강한 카드 (Highest)        — 실패 시 Safest로 fallback
    //   Block      : 가장 낮은 이기는 카드 (Claiming) — 실패 시 Safest로 fallback
    //
    //   ※ 리드 상황에서 Block은 ClaimingLegalCardIndex 내부에서
    //     HighestLegalCardIndex로 자동 위임됨 (TrickManager 구현 참고).
    private static int ResolveCard(in HelperContext ctx, HelperAction action)
    {
        var tm = ctx.trickManager;
        var h  = ctx.helper;
        int safe = tm.SafestLegalCardIndex(h);

        switch (action)
        {
            case HelperAction.Throw:
            case HelperAction.Save:
            {
                // [v4 Opponent Modeling] 도우미가 lead할 때 담당자 void 회피 lead.
                //   조건: 본인이 도우미(담당자 아님) + 리드 위치 + 담당자 존재
                //   담당자가 winning 가능한 suit로 lead → 담당자 성공률 ↑
                if (ctx.helper != ctx.assignee && ctx.isLeading && ctx.assignee != null)
                {
                    int leadIdx = tm.SafestLeadForAssignee(ctx.helper, ctx.assignee);
                    if (leadIdx >= 0) return leadIdx;
                }
                return safe;
            }

            case HelperAction.Burn:
            {
                int burn = tm.HighestLegalCardIndex(h);
                return burn >= 0 ? burn : safe;
            }

            case HelperAction.Block:
            {
                // [v3] 카드 메모리가 있으면 SmartClaim(보장된 winning 우선) 사용.
                //      없으면 기존 Claim과 동일 동작.
                int claim = tm.SmartClaimCardIndex(h, ctx.playedCardsInHand);
                return claim >= 0 ? claim : safe;
            }
        }
        return safe;
    }
}

// =====================================================================
//  EvaluationStats — all-rule-based 시뮬레이션 결과 집계
//  학습이 아닌 평가 모드에서 task별 성공률을 콘솔에 주기적으로 출력.
//  MissionManager.OnHandEnded에서 매 에피소드 RecordEpisode 호출.
// =====================================================================
public static class EvaluationStats
{
    public static int totalEpisodes;
    public static int logEveryN = 100;

    private static readonly Dictionary<MissionManager.Phase1Task, int> taskCount =
        new Dictionary<MissionManager.Phase1Task, int>();
    private static readonly Dictionary<MissionManager.Phase1Task, int> taskSuccess =
        new Dictionary<MissionManager.Phase1Task, int>();
    // 진단용: task별 담당자 평균 트릭 승리 수 누적
    //   WinAtLeast 성공률 천장이 win_target 때문인지 정책 때문인지 판별.
    //   평균 wins < win_target → win_target 낮춰야 / 평균 ≥ win_target인데 성공률 낮음 → 정책 결함
    private static readonly Dictionary<MissionManager.Phase1Task, int> taskWinsSum =
        new Dictionary<MissionManager.Phase1Task, int>();

    public static void RecordEpisode(MissionManager.Phase1Task task, bool success, int assigneeWins)
    {
        totalEpisodes++;
        if (!taskCount.ContainsKey(task))   taskCount[task]   = 0;
        if (!taskSuccess.ContainsKey(task)) taskSuccess[task] = 0;
        if (!taskWinsSum.ContainsKey(task)) taskWinsSum[task] = 0;
        taskCount[task]++;
        if (success) taskSuccess[task]++;
        taskWinsSum[task] += assigneeWins;

        if (totalEpisodes % logEveryN == 0) LogStats();
    }

    public static void LogStats()
    {
        var sb = new System.Text.StringBuilder();
        string mode = "Rule";
        var mm = MissionManager.Instance;
        if (mm != null && mm.overrideMctsAssignee)
            mode = $"MCTS(b={mm.overrideMctsBudget},d={mm.overrideMctsDeterminizations})";
        sb.AppendLine($"[Eval] Episode {totalEpisodes} [{mode}] — 누적:");
        foreach (var kv in taskCount)
        {
            int s = taskSuccess.TryGetValue(kv.Key, out var v) ? v : 0;
            int w = taskWinsSum.TryGetValue(kv.Key, out var ws) ? ws : 0;
            float rate    = kv.Value > 0 ? (float)s / kv.Value : 0f;
            float avgWins = kv.Value > 0 ? (float)w / kv.Value : 0f;
            sb.AppendLine($"  {kv.Key,-12} : {s,5}/{kv.Value,-5} = {rate:P1}   (담당자 평균 wins: {avgWins:F2})");
        }
        UnityEngine.Debug.Log(sb.ToString());
    }

    public static void Reset()
    {
        totalEpisodes = 0;
        taskCount.Clear();
        taskSuccess.Clear();
        taskWinsSum.Clear();
    }
}
