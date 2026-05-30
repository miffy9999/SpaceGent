using System.Collections.Generic;

// =====================================================================
//  Rule-Based Helper — WinSpecificCard 협력 정책
// ---------------------------------------------------------------------
//  스페이스 크루 task는 단일 종류뿐이다:
//    "지정된 카드 X(targetCard)가 포함된 트릭을 담당자 A가 이겨라."
//  색깔 카드 36장은 전부 분배되므로 X는 항상 누군가의 손에 있고,
//  X가 나오는 트릭의 승자가 A인지로 성공/실패가 즉시 결판난다.
//
//  협력 구조:
//    - A가 X 보유          → A가 이길 수 있는 트릭에서 X를 냄
//    - 도우미 H가 X 보유    → A가 이기는(이길) 트릭에 X를 흘려줌(release)
//    - 모든 도우미          → X가 깔린 트릭을 절대 이기면 안 됨 (이기면 즉시 실패)
//
//  결정은 명시적 HelperAction으로 표현해 TensorBoard로 분포를 측정한다.
// =====================================================================

// "이 카드를 어떤 의도로 내는가"
public enum HelperAction
{
    Throw,      // 트릭 양보 (가장 안 이길 카드)
    Win,        // 트릭 획득 (이기는 카드 중 가장 낮은 것 / SmartClaim)
    PlayTarget, // 타깃 카드 X를 냄 (release/획득 트리거)
}

// 결정에 필요한 모든 상태 변수 — 결정 함수의 순수성(side-effect-free) 보장
public struct HelperContext
{
    public CrewAgent    helper;       // 결정하는 본인 (도우미 또는 담당자 본인)
    public CrewAgent    assignee;     // 이번 에피소드 담당자
    public TrickManager trickManager;

    public bool isLeading;        // 트릭의 첫 카드를 내는 차례인가
    public bool assigneePlayed;   // 담당자가 이번 트릭에 이미 카드를 냈는가
    public bool assigneeWinning;  // 담당자의 카드가 현재 이기는 중인가
    public bool isLastTrick;      // 이번 트릭이 핸드의 마지막 트릭인가
    public bool isFirstTrick;     // 이번 트릭이 핸드의 첫 트릭인가
    public bool isLastToPlay;     // 이번 트릭의 마지막 플레이어 차례인가

    // ── WinSpecificCard 전용 ───────────────────────────────────────────
    public Card targetCard;       // 담당자가 든 트릭을 이겨야 하는 대상 카드
    public bool iHoldTarget;      // 본인이 targetCard를 손에 들고 있는가
    public bool targetOnTable;    // targetCard가 현재 트릭 테이블에 깔려 있는가

    // [카드 메모리] 이번 핸드에서 이미 플레이된 카드 (SmartClaim/guaranteed 판정용)
    public HashSet<Card> playedCardsInHand;
}

// =====================================================================
//  Dispatcher — 도우미/담당자 결정 + HelperAction을 손패 인덱스로 변환
//  + action 분포 통계 누적 (TensorBoard 송출용)
// =====================================================================
public static class RuleBasedHelper
{
    // 에피소드별 action 카운트
    public static int CountThrow, CountWin, CountPlayTarget, CountTotal;

    public static void ResetEpisodeStats()
    {
        CountThrow = CountWin = CountPlayTarget = CountTotal = 0;
    }

    public struct Decision
    {
        public HelperAction action;
        public int cardIndex;
    }

    // ── 도우미(담당자가 아닌 3명) 결정 ──────────────────────────────────
    public static Decision Decide(in HelperContext ctx)
    {
        HelperAction action = DecideHelperAction(in ctx);
        return Finalize(in ctx, action);
    }

    // ── 담당자(assignee) 결정 — all-rule-based 시뮬레이션용 ─────────────
    public static Decision DecideAssignee(in HelperContext ctx)
    {
        HelperAction action = DecideAssigneeAction(in ctx);
        return Finalize(in ctx, action);
    }

    // ── 상위 state: 도우미 의도 결정 ────────────────────────────────────
    //  X 보유          → 담당자가 이기는 중이면 PlayTarget, 아니면 Throw(보존)
    //  X 미보유 + 테이블에 X → Throw (절대 안 이김)
    //  X 미보유 + 그 외      → Throw (양보 / 담당자 void 회피 lead는 ResolveCard에서)
    private static HelperAction DecideHelperAction(in HelperContext ctx)
    {
        if (ctx.iHoldTarget)
        {
            if (ctx.assigneePlayed && ctx.assigneeWinning) return HelperAction.PlayTarget;
            return HelperAction.Throw;
        }
        return HelperAction.Throw;
    }

    // ── 상위 state: 담당자 의도 결정 ────────────────────────────────────
    //  테이블에 X        → Win (반드시 이 트릭을 가져감)
    //  X 보유            → 리드+보장승리거나 마지막 트릭이면 PlayTarget, 아니면 Throw(보존)
    //  X 미보유          → Throw (강한 카드 보존, X가 나올 트릭을 노림)
    private static HelperAction DecideAssigneeAction(in HelperContext ctx)
    {
        if (ctx.targetOnTable) return HelperAction.Win;

        if (ctx.iHoldTarget)
        {
            var tm = ctx.trickManager;
            bool guaranteed = ctx.isLeading
                && tm.IsGuaranteedWinner(ctx.targetCard, ctx.helper, ctx.playedCardsInHand);
            if (guaranteed || ctx.isLastTrick) return HelperAction.PlayTarget;
            return HelperAction.Throw;
        }
        return HelperAction.Throw;
    }

    // ── HelperAction → 손패 인덱스 + 통계 ───────────────────────────────
    private static Decision Finalize(in HelperContext ctx, HelperAction action)
    {
        int idx = ResolveCard(in ctx, action);

        CountTotal++;
        switch (action)
        {
            case HelperAction.Throw:      CountThrow++;      break;
            case HelperAction.Win:        CountWin++;        break;
            case HelperAction.PlayTarget: CountPlayTarget++; break;
        }
        return new Decision { action = action, cardIndex = idx };
    }

    private static int ResolveCard(in HelperContext ctx, HelperAction action)
    {
        var tm = ctx.trickManager;
        var h  = ctx.helper;
        int safe = tm.SafestLegalCardIndex(h);

        switch (action)
        {
            case HelperAction.PlayTarget:
            {
                int ti = HandIndexOf(h, ctx.targetCard);
                if (ti >= 0 && tm.IsValidPlay(h, h.hand[ti])) return ti;
                goto case HelperAction.Throw;   // 합법 아니면 Throw로 폴백
            }

            case HelperAction.Win:
            {
                int w = tm.SmartClaimCardIndex(h, ctx.playedCardsInHand);
                return w >= 0 ? w : safe;
            }

            case HelperAction.Throw:
            default:
            {
                // 도우미가 리드 + X 미보유 → 담당자 void 회피 lead
                if (h != ctx.assignee && ctx.isLeading && ctx.assignee != null && !ctx.iHoldTarget)
                {
                    int leadIdx = tm.SafestLeadForAssignee(h, ctx.assignee);
                    if (leadIdx >= 0 && !h.hand[leadIdx].Equals(ctx.targetCard)) return leadIdx;
                }
                // X를 들고 양보할 때는 X를 내지 않도록 회피
                if (ctx.iHoldTarget && safe >= 0 && h.hand[safe].Equals(ctx.targetCard))
                {
                    int alt = LowestNonTargetLegal(tm, h, ctx.targetCard);
                    if (alt >= 0) return alt;
                }
                return safe;
            }
        }
    }

    // 손패에서 특정 카드의 인덱스 (없으면 -1)
    private static int HandIndexOf(CrewAgent h, Card card)
    {
        if (card == null) return -1;
        for (int i = 0; i < h.hand.Count; i++)
            if (h.hand[i].Equals(card)) return i;
        return -1;
    }

    // 합법 카드 중 targetCard를 제외하고 값이 가장 낮은(=잘 안 이길) 카드 인덱스.
    //   WinStrength가 TrickManager 내부 private이라, 값 기준 근사(로켓은 뒤로).
    private static int LowestNonTargetLegal(TrickManager tm, CrewAgent h, Card target)
    {
        int bestIdx = -1, bestScore = int.MaxValue;
        for (int i = 0; i < h.hand.Count; i++)
        {
            Card c = h.hand[i];
            if (target != null && c.Equals(target)) continue;
            if (!tm.IsValidPlay(h, c)) continue;
            int score = (c.suit == Card.Suit.Rocket ? 100 : 0) + c.value;
            if (score < bestScore) { bestScore = score; bestIdx = i; }
        }
        return bestIdx;
    }
}

// =====================================================================
//  EvaluationStats — all-rule-based 시뮬레이션 결과 집계
//  학습이 아닌 평가 모드에서 성공률을 콘솔에 주기적으로 출력.
//  타깃 보유자(담당자/도우미)별로 분리 집계 → 낮은 성공률이 구조적 천장인지
//  정책 결함인지 판별 (담당자가 저가 타깃을 들면 구조적으로 어려움).
// =====================================================================
public static class EvaluationStats
{
    public static int totalEpisodes;
    public static int logEveryN = 100;

    private static int countByAssignee, successByAssignee;   // 타깃을 담당자가 보유
    private static int countByHelper,   successByHelper;     // 타깃을 도우미가 보유

    public static void RecordEpisode(bool success, bool targetHeldByAssignee)
    {
        totalEpisodes++;
        if (targetHeldByAssignee)
        {
            countByAssignee++;
            if (success) successByAssignee++;
        }
        else
        {
            countByHelper++;
            if (success) successByHelper++;
        }

        if (totalEpisodes % logEveryN == 0) LogStats();
    }

    public static void LogStats()
    {
        string mode = "Rule";
        var mm = MissionManager.Instance;
        if (mm != null && mm.overrideMctsAssignee)
            mode = $"MCTS(b={mm.overrideMctsBudget},d={mm.overrideMctsDeterminizations})";

        int total = countByAssignee + countByHelper;
        int success = successByAssignee + successByHelper;
        float rate   = total > 0 ? (float)success / total : 0f;
        float rateA  = countByAssignee > 0 ? (float)successByAssignee / countByAssignee : 0f;
        float rateH  = countByHelper   > 0 ? (float)successByHelper   / countByHelper   : 0f;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Eval] Episode {totalEpisodes} [{mode}] WinSpecificCard 누적:");
        sb.AppendLine($"  전체           : {success,5}/{total,-5} = {rate:P1}");
        sb.AppendLine($"  타깃=담당자보유 : {successByAssignee,5}/{countByAssignee,-5} = {rateA:P1}");
        sb.AppendLine($"  타깃=도우미보유 : {successByHelper,5}/{countByHelper,-5} = {rateH:P1}");
        UnityEngine.Debug.Log(sb.ToString());
    }

    public static void Reset()
    {
        totalEpisodes = 0;
        countByAssignee = successByAssignee = 0;
        countByHelper = successByHelper = 0;
    }
}
