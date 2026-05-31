using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MCTSSearch — PIMC (Perfect Information Monte Carlo) 메인 알고리즘.
// ---------------------------------------------------------------------
//  Phase 1 단순 구조:
//    1. determinization N개 샘플링
//    2. 각 샘플마다 (budget / N) iterations 의 MCTS
//    3. action별 누적 visit/reward 합산
//    4. 가장 많이 방문된 액션 반환
//
//  표준 4단계 MCTS:
//    Selection (UCB1) → Expansion → Simulation (Rollout) → Backpropagation
//
//  Phase 1 단순화:
//    - 시뮬레이션은 MCTSRollout 사용
//    - tree-level entropy/PUCT 없음 (순수 UCB1)
//    - 정해진 iteration budget (시간 대신)
// =====================================================================
public static class MCTSSearch
{
    public const float DefaultExplorationC = 1.41f;   // sqrt(2)
    public const int   DefaultDeterminizations = 10;  // 샘플 분포 수
    public const int   DefaultBudget = 500;           // 총 iteration 수

    // ---------------------------------------------------------------
    // 진입점: 현재 게임 상태에서 카드 인덱스 결정 (self의 손패 인덱스)
    // ---------------------------------------------------------------
    public static int ChooseCard(MCTSContext ctx, int budget = DefaultBudget,
                                  int determinizations = DefaultDeterminizations,
                                  float explorationC = DefaultExplorationC)
    {
        if (ctx == null || ctx.selfHand.Count == 0) return -1;
        if (ctx.legalActionsInSelfHand == null || ctx.legalActionsInSelfHand.Count == 0) return -1;
        if (ctx.legalActionsInSelfHand.Count == 1) return ctx.legalActionsInSelfHand[0];

        // action별 visit/reward 누적 (다중 determinization 평균)
        var actionVisits = new Dictionary<int, int>();
        var actionReward = new Dictionary<int, float>();
        foreach (var a in ctx.legalActionsInSelfHand)
        {
            actionVisits[a] = 0;
            actionReward[a] = 0f;
        }

        int iterationsPerDeterminization = Mathf.Max(1, budget / Mathf.Max(1, determinizations));
        var rng = new System.Random();

        for (int d = 0; d < determinizations; d++)
        {
            // 새로운 손패 분포 샘플링
            var hands = Determinizer.Sample(
                ctx.selfHand, ctx.selfIdx,
                ctx.playedCards, ctx.tableCards,
                ctx.knownVoids, ctx.handSizes, rng,
                ctx.commReveals);

            // 초기 상태 구성
            var rootState = ctx.BuildInitialState(hands);

            // MCTS 트리 탐색
            var root = new MCTSNode(null, -1, -1, rootState.LegalActions());
            for (int it = 0; it < iterationsPerDeterminization; it++)
            {
                Iterate(root, rootState, explorationC);
            }

            // 이 determinization의 결과를 누적
            foreach (var child in root.children)
            {
                int act = child.actionFromParent;
                if (!actionVisits.ContainsKey(act)) continue;
                actionVisits[act] += child.visits;
                actionReward[act] += child.totalReward;
            }
        }

        // visit 가장 많은 액션
        int bestAction = -1;
        int bestVisits = -1;
        float bestMean = float.NegativeInfinity;
        foreach (var kv in actionVisits)
        {
            // 1순위: visits, 2순위: mean reward (tiebreak)
            int v = kv.Value;
            float mean = v > 0 ? actionReward[kv.Key] / v : 0f;
            if (v > bestVisits || (v == bestVisits && mean > bestMean))
            {
                bestVisits = v;
                bestMean = mean;
                bestAction = kv.Key;
            }
        }

        // 유효성 검증: bestAction이 self.legalActions에 있어야 함
        if (bestAction < 0 || !ctx.legalActionsInSelfHand.Contains(bestAction))
        {
            // 폴백: 첫 합법 액션
            bestAction = ctx.legalActionsInSelfHand[0];
        }
        return bestAction;
    }

    // ---------------------------------------------------------------
    // 1 iteration: Selection → Expansion → Simulation → Backprop
    // ---------------------------------------------------------------
    private static void Iterate(MCTSNode root, MCTSState rootState, float c)
    {
        var node = root;
        var state = rootState.Clone();

        // ── Selection: 완전 확장 + 비 terminal 경로 ──────────────────
        while (node.IsFullyExpanded() && !state.IsTerminal() && node.children.Count > 0)
        {
            node = node.SelectChildUCB(c);
            state.ApplyAction(node.actionFromParent);
        }

        // ── Expansion: 미확장 액션 하나 추가 ─────────────────────────
        if (!state.IsTerminal() && node.untriedActions.Count > 0)
        {
            int randIdx = Random.Range(0, node.untriedActions.Count);
            int action = node.untriedActions[randIdx];
            node.untriedActions.RemoveAt(randIdx);

            int playerBefore = state.currentPlayer;
            state.ApplyAction(action);

            var child = new MCTSNode(node, action, playerBefore, state.LegalActions());
            node.children.Add(child);
            node = child;
        }

        // ── Simulation: rollout으로 terminal까지 ────────────────────
        float reward;
        if (state.IsTerminal())
        {
            reward = state.Reward();
        }
        else
        {
            reward = MCTSRollout.Rollout(state);
        }

        // ── Backprop ────────────────────────────────────────────────
        node.Backpropagate(reward);
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
