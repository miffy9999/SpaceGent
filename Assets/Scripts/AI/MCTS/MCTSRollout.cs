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
    // ---------------------------------------------------------------
    // Rollout: terminal까지 진행. Reward 반환.
    // ---------------------------------------------------------------
    public static float Rollout(MCTSState state)
    {
        var s = state.Clone();
        int safety = 200;
        while (!s.IsTerminal() && safety-- > 0)
        {
            int action = Decide(s);
            if (action < 0)
            {
                // 합법 액션 없음 → 비정상 종료
                break;
            }
            s.ApplyAction(action);
        }
        return s.Reward();
    }

    // ---------------------------------------------------------------
    // 현재 상태에서 카드 결정 (시뮬레이션 전용 정책)
    // ---------------------------------------------------------------
    public static int Decide(MCTSState s)
    {
        var legal = s.LegalActions();
        if (legal.Count == 0) return -1;
        if (legal.Count == 1) return legal[0];

        bool isAssignee = (s.currentPlayer == s.assigneeIdx);
        Card target = s.targetCard;

        // ── WinSpecificCard 약식 롤아웃 정책 ─────────────────────────────
        //  목표 카드 X가 든 트릭을 담당자가 이겨야 함.
        if (target != null)
        {
            // 1) X가 이미 테이블에 깔림 → 담당자는 무조건 획득, 도우미는 절대 안 이김
            if (s.cardsOnTable.Contains(target))
                return isAssignee ? ClaimingIndex(s, legal) : SafestIndex(s, legal);

            var hand = s.hands[s.currentPlayer];
            int targetIdx = LegalIndexOf(hand, legal, target);

            // 2) 도우미가 X를 보유 → 담당자가 현재 이기는 중일 때만 X를 흘려줌(release)
            if (!isAssignee && targetIdx >= 0)
            {
                if (s.cardsOnTable.Count > 0 && s.CurrentWinnerPlayer() == s.assigneeIdx)
                    return targetIdx;                       // 담당자 트릭에 X 투입 → 완료
                int se = SafestExcluding(s, legal, target); // 아니면 X 보존
                if (se >= 0) return se;
                return targetIdx;                           // X밖에 못 낼 때(강제)
            }
        }

        // 3) 기본: 담당자는 카드 보존(낮은 카드), 도우미도 양보
        return SafestIndex(s, legal);
    }

    // 합법 액션 중 특정 카드의 인덱스 (없으면 -1)
    private static int LegalIndexOf(List<Card> hand, List<int> legal, Card card)
    {
        foreach (var i in legal)
            if (hand[i].Equals(card)) return i;
        return -1;
    }

    // SafestIndex와 동일하되 특정 카드(제외 대상)는 후보에서 뺀다.
    private static int SafestExcluding(MCTSState s, List<int> legal, Card exclude)
    {
        var hand = s.hands[s.currentPlayer];
        var filtered = new List<int>();
        foreach (var i in legal)
            if (!hand[i].Equals(exclude)) filtered.Add(i);
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
