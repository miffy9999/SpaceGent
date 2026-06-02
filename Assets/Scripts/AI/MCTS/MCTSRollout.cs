using System.Collections.Generic;

// =====================================================================
//  MCTSRollout — 시뮬레이션 정책 (Rollout / Default Policy)
// ---------------------------------------------------------------------
//  MCTSState만 보고 카드 결정. 실제 게임 인스턴스(TrickManager) 비의존.
//
//  Rule-based v4의 핵심 패턴 이식 (단순화 버전):
//    - 담당자: Block(이길 수 있으면 lowest beating, 못 이기면 Throw)
//    - 도우미: Throw(가장 안 이길 카드, fallback lowest)
//
//  Phase 1에선 단순 휴리스틱 사용. Phase 2에서 더 정교한 정책으로 교체 가능.
// =====================================================================
public static class MCTSRollout
{
    // 마지막 N장(≈2트릭) 이하가 남으면 휴리스틱 대신 완전탐색으로 정확히 푼다.
    public const int EndgameCards = 8;

    // ---------------------------------------------------------------
    // Rollout: 휴리스틱으로 진행하다 엔드게임에 들어가면 정확 솔버로 마무리.
    // ---------------------------------------------------------------
    public static float Rollout(MCTSState state)
    {
        var s = state.Clone();
        int safety = 200;
        while (!s.IsTerminal() && safety-- > 0)
        {
            if (RemainingCards(s) <= EndgameCards)
                return ExactSolve(s);   // 결정화=완전정보, 협력 → 정확 가치

            int action = Decide(s);
            if (action < 0) break;
            s.ApplyAction(action);
        }
        return s.Reward();
    }

    private static int RemainingCards(MCTSState s)
    {
        int n = 0;
        foreach (var h in s.hands) n += h.Count;
        return n;
    }

    // ---------------------------------------------------------------
    // 엔드게임 정확 솔버: 협력(전원 같은 보상 최대화)이므로 모든 플레이어의
    //   수를 우리가 통제해 달성 가능한 최대 보상을 반환(완전탐색 + 1.0 컷오프).
    //   결정화 상태라 모든 손패를 알고, follow-suit가 분기를 크게 줄인다.
    // ---------------------------------------------------------------
    private static float ExactSolve(MCTSState s)
    {
        if (s.IsTerminal()) return s.Reward();

        var legal = s.LegalActions();
        if (legal.Count == 0) return s.Reward();

        float best = -1f;
        foreach (int idx in legal)
        {
            var c = s.Clone();
            c.ApplyAction(idx);
            float v = ExactSolve(c);
            if (v > best) best = v;
            if (best >= 1f) break;   // 더 좋을 수 없음
        }
        return best;
    }

    // ---------------------------------------------------------------
    // 현재 상태에서 카드 결정 (시뮬레이션 전용 정책 — 다중 태스크 협력)
    //  규칙:
    //   1) 이번 트릭에 누군가의 미완료 목표 카드가 깔림
    //        - 내가 그 owner → 이기러 감(Claim)
    //        - 아니면        → 안 이김(Safest)  ← 가로채기 방지
    //   2) 깔린 목표 없음 → 내 목표 카드는 보존하고 가장 안 이길 카드(Safest)
    //        (목표는 내가 이길 수 있는 트릭에서 쓰려고 아껴 둠)
    // ---------------------------------------------------------------
    public static int Decide(MCTSState s)
    {
        var legal = s.LegalActions();
        if (legal.Count == 0) return -1;
        if (legal.Count == 1) return legal[0];

        int cur = s.currentPlayer;
        var hand = s.hands[cur];

        // 1) 테이블에 미완료 목표가 깔려 있으면 owner만 이기러 간다
        int ownerOnTable = s.OwnerOfTargetOnTable();
        if (ownerOnTable >= 0)
            return (cur == ownerOnTable) ? ClaimingIndex(s, legal) : SafestIndex(s, legal);

        // 2) 동료가 지금 이기는 중이고 내가 그 동료의 목표를 들고 있으면 → 투입(완료)
        //    단, 그 목표가 현재 최강을 안 이겨야(=동료가 계속 이김) 안전하다.
        int releaseIdx = TryReleaseTeammateTarget(s, legal);
        if (releaseIdx >= 0) return releaseIdx;

        // 3) 내가 리드이고 내 목표가 "보장 승리"면 그 목표를 리드해 바로 완료를 노린다
        int leadIdx = TryLeadOwnTargetIfWinning(s, legal);
        if (leadIdx >= 0) return leadIdx;

        // 4) 미완료 목표 카드(내 것 + 동료 것 전부)는 함부로 버리지 않는다.
        //    동료 목표를 throwaway로 내면 낭비/가로채기로 이어져 미션 실패 위험.
        //    목표가 아닌 카드 중 Safest를 우선 선택, 없으면 일반 Safest.
        var pendingTargets = new HashSet<Card>();
        foreach (var t in s.tasks)
            if (!t.completed && !t.failed && t.target != null) pendingTargets.Add(t.target);

        if (pendingTargets.Count > 0)
        {
            int se = SafestExcluding(s, legal, pendingTargets);
            if (se >= 0) return se;
        }
        return SafestIndex(s, legal);
    }

    // 동료(현재 트릭 승자)의 미완료 목표를 내가 들고 있으면, 승자를 안 바꾸는 선에서 투입.
    private static int TryReleaseTeammateTarget(MCTSState s, List<int> legal)
    {
        if (s.cardsOnTable.Count == 0) return -1;
        int winner = s.CurrentWinnerPlayer();
        if (winner < 0 || winner == s.currentPlayer) return -1;

        var hand = s.hands[s.currentPlayer];

        // 현재 테이블 최강 카드
        Card best = s.cardsOnTable[0];
        for (int i = 1; i < s.cardsOnTable.Count; i++)
            if (s.Beats(s.cardsOnTable[i], best)) best = s.cardsOnTable[i];

        foreach (var t in s.tasks)
        {
            if (t.completed || t.failed) continue;
            if (t.ownerIdx != winner) continue;
            foreach (int i in legal)
                if (hand[i].Equals(t.target) && !s.Beats(hand[i], best))   // 승자 안 바뀜
                    return i;
        }
        return -1;
    }

    // 리드 상황에서 내 목표 카드를 내면 반드시 이기는 경우(다른 손패 모두 알고 판정) → 그 목표 리드.
    private static int TryLeadOwnTargetIfWinning(MCTSState s, List<int> legal)
    {
        if (s.cardsOnTable.Count != 0) return -1;   // 리드만
        int cur = s.currentPlayer;
        var hand = s.hands[cur];

        foreach (var t in s.tasks)
        {
            if (t.completed || t.failed) continue;
            if (t.ownerIdx != cur) continue;
            foreach (int i in legal)
                if (hand[i].Equals(t.target) && LeadGuaranteesWin(s, hand[i]))
                    return i;
        }
        return -1;
    }

    // 결정화 상태(모든 손패 알려짐)에서 lead 카드를 내면 아무도 못 이기는가.
    private static bool LeadGuaranteesWin(MCTSState s, Card lead)
    {
        int cur = s.currentPlayer;
        for (int p = 0; p < s.hands.Length; p++)
        {
            if (p == cur) continue;
            var h = s.hands[p];
            if (lead.suit == Card.Suit.Rocket)
            {
                foreach (var c in h)
                    if (c.suit == Card.Suit.Rocket && c.value > lead.value) return false;
            }
            else
            {
                bool hasLead = false;
                foreach (var c in h) if (c.suit == lead.suit) { hasLead = true; break; }
                if (hasLead)
                {
                    foreach (var c in h)
                        if (c.suit == lead.suit && c.value > lead.value) return false;
                }
                else
                {
                    // 리드 슈트 없음 → 로켓으로 트럼프 가능
                    foreach (var c in h) if (c.suit == Card.Suit.Rocket) return false;
                }
            }
        }
        return true;
    }

    // SafestIndex와 동일하되 제외 집합의 카드는 후보에서 뺀다.
    private static int SafestExcluding(MCTSState s, List<int> legal, HashSet<Card> exclude)
    {
        var hand = s.hands[s.currentPlayer];
        var filtered = new List<int>();
        foreach (var i in legal)
            if (!exclude.Contains(hand[i])) filtered.Add(i);
        if (filtered.Count == 0) return -1;
        return SafestIndex(s, filtered);
    }

    // ---------------------------------------------------------------
    // SafestIndex: 가장 안 이길 카드 (rule-based v4 SafestLegalCardIndex 이식)
    //   1차: 현재 winning을 안 이기는 카드 중 WinStrength 최솟값
    //   2차: 모든 합법 카드가 winning이면 그중 WinStrength 최솟값
    // ---------------------------------------------------------------
    private static int SafestIndex(MCTSState s, List<int> legal)
    {
        var hand = s.hands[s.currentPlayer];

        if (s.cardsOnTable.Count > 0)
        {
            // 현재 최강 카드
            Card best = s.cardsOnTable[0];
            for (int i = 1; i < s.cardsOnTable.Count; i++)
                if (s.Beats(s.cardsOnTable[i], best)) best = s.cardsOnTable[i];

            int loseIdx = -1, loseScore = int.MaxValue;
            foreach (var i in legal)
            {
                var c = hand[i];
                if (s.Beats(c, best)) continue;
                int sc = s.WinStrength(c);
                if (sc < loseScore) { loseScore = sc; loseIdx = i; }
            }
            if (loseIdx >= 0) return loseIdx;
        }

        int bestIdx = -1, bestScore = int.MaxValue;
        foreach (var i in legal)
        {
            int sc = s.WinStrength(hand[i]);
            if (sc < bestScore) { bestScore = sc; bestIdx = i; }
        }
        return bestIdx;
    }

    // ---------------------------------------------------------------
    // HighestIndex: WinStrength 최댓값
    // ---------------------------------------------------------------
    private static int HighestIndex(MCTSState s, List<int> legal)
    {
        var hand = s.hands[s.currentPlayer];
        int bestIdx = -1, bestScore = int.MinValue;
        foreach (var i in legal)
        {
            int sc = s.WinStrength(hand[i]);
            if (sc > bestScore) { bestScore = sc; bestIdx = i; }
        }
        return bestIdx;
    }

    // ---------------------------------------------------------------
    // ClaimingIndex: 현재 best를 이기는 카드 중 WinStrength 최솟값.
    //   못 이기면 → SafestIndex 폴백.
    //   리드 상황이면 HighestIndex (비교 대상 없음).
    // ---------------------------------------------------------------
    private static int ClaimingIndex(MCTSState s, List<int> legal)
    {
        if (s.cardsOnTable.Count == 0) return HighestIndex(s, legal);

        var hand = s.hands[s.currentPlayer];
        Card best = s.cardsOnTable[0];
        for (int i = 1; i < s.cardsOnTable.Count; i++)
            if (s.Beats(s.cardsOnTable[i], best)) best = s.cardsOnTable[i];

        int idx = -1, score = int.MaxValue;
        foreach (var i in legal)
        {
            var c = hand[i];
            if (!s.Beats(c, best)) continue;
            int sc = s.WinStrength(c);
            if (sc < score) { score = sc; idx = i; }
        }
        if (idx >= 0) return idx;
        return SafestIndex(s, legal);
    }
}
