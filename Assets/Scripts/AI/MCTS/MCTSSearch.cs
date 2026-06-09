using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// =====================================================================
//  MCTSSearch — SO-ISMCTS (Single-Observer Information Set MCTS).
// ---------------------------------------------------------------------
//  Cowling et al. 2012. 기존 앙상블 PIMC(결정화별 독립 트리)의 한계
//  (얕은 트리, strategy fusion, budget 포화)를 극복:
//    - 트리를 하나로 공유, 매 iteration마다 결정화를 새로 샘플
//    - 엣지는 카드(액션) 기준 → 결정화가 달라도 일관
//    - availability-aware UCB: 그 수가 "합법이었던 횟수"로 탐색항 보정
//    - budget 전체가 한 트리에 모여 깊게 탐색 → budget이 실제로 효과
//
//  4단계: Selection(avail-UCB) → Expansion → Simulation(Rollout) → Backprop
// =====================================================================
public static class MCTSSearch
{
    public const float DefaultExplorationC = 0.7f;    // C 스윕 결과 정점(0.4·1.0보다 우월): 0.7 확정
    public const int   DefaultDeterminizations = 10;
    public const int   DefaultBudget = 1000;

    // ── 타이밍 누산기 (세션 전체 누적) ──────────────────────────────
    private static int  _decisionCount;
    private static long _totalIterations;   // 누적 계산(iteration) 횟수
    private static long _totalDetCalls;     // 누적 결정화 호출 횟수
    private static long _totalDecisionMs;
    private static long _totalDetMs;
    private static long _totalRolloutMs;
    private static long _minDecisionMs = long.MaxValue;
    private static long _maxDecisionMs;

    // ChooseCard 1회 동안 Iterate()가 누산 (ticks 단위)
    private static long _callRolloutTicks;

    private const int SummaryEvery = 10;   // N회마다 누적 평균 출력

    // 외부(GameManager 등)에서 새 판 시작 시 호출해 통계 초기화
    public static void ResetStats()
    {
        _decisionCount = 0;
        _totalIterations = _totalDetCalls = 0;
        _totalDecisionMs = _totalDetMs = _totalRolloutMs = 0;
        _minDecisionMs = long.MaxValue;
        _maxDecisionMs = 0;
    }

    // ── ISMCTS 노드: 카드(int 키) 기준 자식 ─────────────────────────
    private class IsNode
    {
        public Dictionary<int, IsNode> children = new Dictionary<int, IsNode>();
        public int   visits;
        public float reward;     // 누적 보상 (협력: 전원 동일)
        public int   avail;      // 이 수가 합법이었던 횟수 (availability)
    }

    private static int Key(Card c) => (int)c.suit * 100 + c.value;

    // ---------------------------------------------------------------
    // 진입점: self 손패 인덱스 반환 (CrewAgent가 카드 인덱스로 사용)
    // ---------------------------------------------------------------
    public static int ChooseCard(MCTSContext ctx, int budget = DefaultBudget,
                                  int determinizations = DefaultDeterminizations,
                                  float explorationC = DefaultExplorationC)
    {
        if (ctx == null || ctx.selfHand.Count == 0) return -1;
        if (ctx.legalActionsInSelfHand == null || ctx.legalActionsInSelfHand.Count == 0) return -1;
        if (ctx.legalActionsInSelfHand.Count == 1) return ctx.legalActionsInSelfHand[0];

        var rng  = new System.Random();
        var root = new IsNode();

        _callRolloutTicks = 0;
        long detTicks    = 0;
        int  detCalls    = 0;
        long freq = Stopwatch.Frequency;
        var totalSw = Stopwatch.StartNew();

        // SO-ISMCTS: 결정화를 K iteration마다 재샘플(신념 다양성 유지 + 비용 절감).
        //   매번 새로 뽑으면 정확하나 백트래킹 비용이 큼. K마다 갱신하면
        //   budget/K개 표본(예: 1000/5=200개)으로 충분히 다양 → 과적합 무시 가능하고
        //   결정화 호출이 K배 감소(속도↑). 재사용 구간엔 값싼 복제로 변형 방지.
        const int RedeterminizeEvery = 5;
        List<Card>[] hands = null;
        for (int it = 0; it < budget; it++)
        {
            if (it % RedeterminizeEvery == 0 || hands == null)
            {
                long t0 = Stopwatch.GetTimestamp();
                hands = Determinizer.Sample(
                    ctx.selfHand, ctx.selfIdx, ctx.playedCards, ctx.tableCards,
                    ctx.knownVoids, ctx.handSizes, rng, ctx.commReveals);
                detTicks += Stopwatch.GetTimestamp() - t0;
                detCalls++;
            }

            // 시뮬이 손패를 RemoveAt로 변형하므로 값싼 복제본으로 상태 구성
            var cloned = new List<Card>[hands.Length];
            for (int i = 0; i < hands.Length; i++) cloned[i] = new List<Card>(hands[i]);
            var state = ctx.BuildInitialState(cloned);
            Iterate(root, state, explorationC, rng);
        }

        totalSw.Stop();
        long totalMs   = totalSw.ElapsedMilliseconds;
        long detMs     = detTicks   * 1000L / freq;
        long rolloutMs = _callRolloutTicks * 1000L / freq;
        long treeMs    = System.Math.Max(0L, totalMs - detMs - rolloutMs);

        // 누산
        _decisionCount++;
        _totalIterations += budget;
        _totalDetCalls   += detCalls;
        _totalDecisionMs += totalMs;
        _totalDetMs      += detMs;
        _totalRolloutMs  += rolloutMs;
        if (totalMs < _minDecisionMs) _minDecisionMs = totalMs;
        if (totalMs > _maxDecisionMs) _maxDecisionMs = totalMs;

        // 결정당 로그
        float pDet  = totalMs > 0 ? detMs     * 100f / totalMs : 0f;
        float pRoll = totalMs > 0 ? rolloutMs * 100f / totalMs : 0f;
        float pTree = totalMs > 0 ? treeMs    * 100f / totalMs : 0f;
        float iterAvgMs = budget > 0 ? totalMs / (float)budget : 0f;
        UnityEngine.Debug.Log(
            $"[MCTS] P{ctx.selfIdx} 결정 #{_decisionCount} | {totalMs}ms | " +
            $"계산:{budget}회 Det호출:{detCalls}회 | " +
            $"Det:{detMs}ms({pDet:F0}%) Rollout:{rolloutMs}ms({pRoll:F0}%) Tree:{treeMs}ms({pTree:F0}%) | " +
            $"iter평균:{iterAvgMs:F3}ms");

        // N회 평균 요약
        if (_decisionCount % SummaryEvery == 0)
        {
            float avgTotal   = _totalDecisionMs / (float)_decisionCount;
            float avgDet     = _totalDetMs      / (float)_decisionCount;
            float avgRollout = _totalRolloutMs  / (float)_decisionCount;
            float avgTree    = avgTotal - avgDet - avgRollout;
            float aDet  = avgTotal > 0 ? avgDet     / avgTotal * 100f : 0f;
            float aRoll = avgTotal > 0 ? avgRollout / avgTotal * 100f : 0f;
            float aTree = avgTotal > 0 ? avgTree    / avgTotal * 100f : 0f;
            float aIter = budget   > 0 ? avgTotal   / budget          : 0f;
            float avgDetCalls = _totalDetCalls / (float)_decisionCount;
            UnityEngine.Debug.Log(
                $"[MCTS 평균 {_decisionCount}회] 결정:{avgTotal:F1}ms | " +
                $"총계산:{_totalIterations}회 Det총호출:{_totalDetCalls}회(결정당 {avgDetCalls:F1}회) | " +
                $"Det:{avgDet:F1}ms({aDet:F0}%) Rollout:{avgRollout:F1}ms({aRoll:F0}%) Tree:{avgTree:F1}ms({aTree:F0}%) | " +
                $"iter:{aIter:F3}ms | 범위:{_minDecisionMs}~{_maxDecisionMs}ms");
        }

        // 루트에서 가장 많이 방문된 카드 선택 → self 손패 인덱스로 매핑
        int bestKey = -1, bestVisits = -1;
        float bestMean = float.NegativeInfinity;
        foreach (var kv in root.children)
        {
            int v = kv.Value.visits;
            float mean = v > 0 ? kv.Value.reward / v : 0f;
            if (v > bestVisits || (v == bestVisits && mean > bestMean))
            { bestVisits = v; bestMean = mean; bestKey = kv.Key; }
        }

        if (bestKey >= 0)
        {
            // bestKey에 해당하는 self 손패의 합법 인덱스 찾기
            foreach (int idx in ctx.legalActionsInSelfHand)
                if (idx >= 0 && idx < ctx.selfHand.Count && Key(ctx.selfHand[idx]) == bestKey)
                    return idx;
        }
        return ctx.legalActionsInSelfHand[0];   // 폴백
    }

    // ---------------------------------------------------------------
    // 1 iteration (SO-ISMCTS)
    // ---------------------------------------------------------------
    private static void Iterate(IsNode root, MCTSState state, float c, System.Random rng)
    {
        var path = new List<IsNode> { root };
        var node = root;

        while (!state.IsTerminal())
        {
            var legal = state.LegalMoveCards();
            if (legal.Count == 0) break;

            // 이 노드를 지나가므로, 합법인 기존 자식들의 availability 증가
            foreach (var mv in legal)
                if (node.children.TryGetValue(Key(mv), out var ch)) ch.avail++;

            // 미확장 합법 수 수집
            Card expand = null;
            foreach (var mv in legal)
                if (!node.children.ContainsKey(Key(mv))) { expand = mv; break; }

            if (expand != null)
            {
                // Expansion: 미확장 수 하나 추가 (여러 개면 랜덤)
                var untried = new List<Card>();
                foreach (var mv in legal)
                    if (!node.children.ContainsKey(Key(mv))) untried.Add(mv);
                expand = untried[rng.Next(untried.Count)];

                var child = new IsNode { avail = 1 };
                node.children[Key(expand)] = child;
                state.ApplyCard(expand);
                node = child; path.Add(node);
                break;   // 확장 후 시뮬레이션으로
            }

            // Selection: availability-aware UCB
            Card best = legal[0];
            float bestScore = float.NegativeInfinity;
            foreach (var mv in legal)
            {
                var ch = node.children[Key(mv)];
                float exploit = ch.visits > 0 ? ch.reward / ch.visits : 0f;
                float explore = c * Mathf.Sqrt(Mathf.Log(Mathf.Max(1, ch.avail)) / Mathf.Max(1, ch.visits));
                float score = exploit + explore;
                if (score > bestScore) { bestScore = score; best = mv; }
            }
            state.ApplyCard(best);
            node = node.children[Key(best)];
            path.Add(node);
        }

        // Simulation — rollout 시간 누산
        long rt0 = Stopwatch.GetTimestamp();
        float reward = state.IsTerminal() ? state.Reward() : MCTSRollout.Rollout(state);
        _callRolloutTicks += Stopwatch.GetTimestamp() - rt0;

        // Backprop
        foreach (var n in path) { n.visits++; n.reward += reward; }
    }
}

// =====================================================================
//  MCTSContext — MCTSSearch에 전달하는 입력 묶음.
//  실제 게임(MissionManager/TrickManager)에서 데이터를 모아 전달.
// =====================================================================
public class MCTSContext
{
    public int selfIdx;                                  // 본인 player 인덱스
    public List<Card> selfHand;                          // 본인 손패
    public List<int>  legalActionsInSelfHand;            // 본인 손패 기준 합법 인덱스
    public HashSet<Card> playedCards;                    // 핸드 누적 played
    public List<Card>  tableCards;                       // 현재 트릭 테이블 카드
    public List<int>   tablePlayers;                     // 현재 트릭에서 카드 낸 플레이어들
    public Card.Suit   leadSuit;
    public int currentPlayer;                            // (= selfIdx, 본인 차례에만 MCTS 호출)
    public int trickNumber;                              // 0-indexed
    public int totalTricks;
    public int[] trickWinCounts;
    public int firstTrickWinner;
    public int lastTrickWinner;                          // 보통 -1 (마지막 트릭 진행 중에만 set)

    // 다중 태스크 + 전역 규칙
    public List<MctsTask>    tasks = new List<MctsTask>();
    public GlobalMissionRule globalRule = GlobalMissionRule.None;
    public int completedCount;       // 이미 완수된 태스크 수 (순서 토큰 판정 시작값)
    public int rocketWinsMax;        // 이미 이긴 트릭의 로켓 최대값 (RocketsInOrder)

    public HashSet<Card.Suit>[] knownVoids;              // [playerCount] 각자 void suit
    public int[] handSizes;                              // 각 플레이어가 가질 카드 수
    public List<CommReveal> commReveals;                 // 통신으로 공개된 카드/위치 (신념)

    // ---------------------------------------------------------------
    // determinization 결과 hands로 초기 MCTSState 구성
    // ---------------------------------------------------------------
    public MCTSState BuildInitialState(List<Card>[] hands)
    {
        var clonedTasks = new List<MctsTask>(tasks.Count);
        foreach (var t in tasks) clonedTasks.Add(t.Clone());

        var s = new MCTSState
        {
            hands           = hands,
            cardsOnTable    = new List<Card>(tableCards ?? new List<Card>()),
            playersOnTable  = new List<int>(tablePlayers ?? new List<int>()),
            leadSuit        = leadSuit,
            currentPlayer   = currentPlayer,
            trickNumber     = trickNumber,
            totalTricks     = totalTricks,
            trickWinCounts  = (int[])trickWinCounts.Clone(),
            firstTrickWinner = firstTrickWinner,
            lastTrickWinner  = lastTrickWinner,
            tasks           = clonedTasks,
            globalRule      = globalRule,
            completedCount  = completedCount,
            rocketWinsMax   = rocketWinsMax,
            selfIdx         = selfIdx,
        };
        return s;
    }
}

// =====================================================================
//  CommReveal — 통신 토큰으로 공개된 정보 (결정화 신념 제약).
//   playerIdx 가 card 를 보유함이 공개됨.
//   hasPosition=true 면 그 무늬에서의 위치(최고/유일/최저)도 공개(데드존은 false).
// =====================================================================
public class CommReveal
{
    public int  playerIdx;
    public Card card;
    public CommunicationToken.RevealPosition position;
    public bool hasPosition;   // 데드존이면 false (정확한 카드만 알고 위치는 모름)
}
