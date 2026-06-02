using System.Collections.Generic;
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
    public const float DefaultExplorationC = 0.7f;    // 보상이 [0,1]이라 sqrt2보다 작게
    public const int   DefaultDeterminizations = 10;
    public const int   DefaultBudget = 1000;

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

        // 표준 SO-ISMCTS: 매 iteration마다 결정화를 새로 샘플(신념 다양성 유지).
        //   고정 표본 풀을 재사용하면 그 표본들에 과적합(strategy fusion)되어
        //   budget↑가 오히려 성능을 떨어뜨림 → 매번 새로 뽑는다.
        for (int it = 0; it < budget; it++)
        {
            var hands = Determinizer.Sample(
                ctx.selfHand, ctx.selfIdx, ctx.playedCards, ctx.tableCards,
                ctx.knownVoids, ctx.handSizes, rng, ctx.commReveals);
            var state = ctx.BuildInitialState(hands);   // 새 표본이라 클론 불필요
            Iterate(root, state, explorationC, rng);
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

        // Simulation
        float reward = state.IsTerminal() ? state.Reward() : MCTSRollout.Rollout(state);

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
//  CommReveal — 통신 토큰으로 공개된 정보 (PIMC 신념 제약).
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
