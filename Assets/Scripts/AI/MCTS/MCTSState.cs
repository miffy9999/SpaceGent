using System.Collections.Generic;

// =====================================================================
//  MctsTask — 시뮬레이션용 태스크 (WinSpecificCard + 순서 토큰).
//    ownerIdx 플레이어가 target 카드가 든 트릭을 이기면 완료.
//    다른 사람이 그 트릭을 가져가면 실패(미션 실패).
// =====================================================================
public class MctsTask
{
    public int        ownerIdx;
    public Card       target;
    public OrderToken order;
    public bool       completed;
    public bool       failed;

    public MctsTask Clone() => new MctsTask
    {
        ownerIdx = ownerIdx, target = target, order = order,
        completed = completed, failed = failed
    };
}

// =====================================================================
//  MCTSState — 스페이스 크루 게임 상태 스냅샷 (다중 태스크 + 전역 규칙).
// ---------------------------------------------------------------------
//  Determinized state (모든 손패 알려짐). Clone/transition 가능.
//  실제 게임 인스턴스와 독립 — 시뮬레이션 전용.
//  규칙 판정은 MissionRules 공유 모듈을 사용해 실게임과 일치시킨다.
// =====================================================================
public class MCTSState
{
    // 4 player hands (인덱스는 GameManager.players와 동일)
    public List<Card>[] hands;

    // 현재 트릭 상태
    public List<Card> cardsOnTable   = new List<Card>();
    public List<int>  playersOnTable = new List<int>();
    public Card.Suit  leadSuit = Card.Suit.Rocket;   // 빈 상태 = Rocket

    public int currentPlayer;

    // 트릭 추적
    public int   trickNumber;        // 0-indexed
    public int   totalTricks;
    public int[] trickWinCounts;
    public int   firstTrickWinner = -1;
    public int   lastTrickWinner  = -1;

    // 태스크 (다중) + 전역 규칙
    public List<MctsTask>     tasks = new List<MctsTask>();
    public GlobalMissionRule  globalRule = GlobalMissionRule.None;
    public bool missionFailed;       // 즉시 실패(가로채기/순서/전역규칙 위반)

    // 순서 토큰 / 전역 규칙 보조 추적
    public int completedCount;       // 완수된 태스크 수 (순서 토큰 판정용)
    public int rocketWinsMax;        // 지금까지 트릭을 이긴 로켓의 최대값 (RocketsInOrder용)

    // 본인 시점(루트 플레이어) — 보상은 협력이라 전원 동일하지만, 참고용
    public int selfIdx;

    // ---------------------------------------------------------------
    // Clone (deep copy)
    // ---------------------------------------------------------------
    public MCTSState Clone()
    {
        var c = new MCTSState();
        c.hands = new List<Card>[hands.Length];
        for (int i = 0; i < hands.Length; i++)
            c.hands[i] = new List<Card>(hands[i]);
        c.cardsOnTable   = new List<Card>(cardsOnTable);
        c.playersOnTable = new List<int>(playersOnTable);
        c.leadSuit       = leadSuit;
        c.currentPlayer  = currentPlayer;
        c.trickNumber    = trickNumber;
        c.totalTricks    = totalTricks;
        c.trickWinCounts = (int[])trickWinCounts.Clone();
        c.firstTrickWinner = firstTrickWinner;
        c.lastTrickWinner  = lastTrickWinner;
        c.tasks = new List<MctsTask>(tasks.Count);
        foreach (var t in tasks) c.tasks.Add(t.Clone());
        c.globalRule      = globalRule;
        c.missionFailed   = missionFailed;
        c.completedCount  = completedCount;
        c.rocketWinsMax   = rocketWinsMax;
        c.selfIdx         = selfIdx;
        return c;
    }

    // ---------------------------------------------------------------
    // 합법 액션 (현재 플레이어 손패 인덱스). Follow-suit 강제.
    // ---------------------------------------------------------------
    public List<int> LegalActions()
    {
        var legal = new List<int>();
        if (currentPlayer < 0 || currentPlayer >= hands.Length) return legal;
        var hand = hands[currentPlayer];
        if (hand.Count == 0) return legal;

        if (cardsOnTable.Count == 0)
        {
            for (int i = 0; i < hand.Count; i++) legal.Add(i);
            return legal;
        }

        bool hasLeadSuit = false;
        for (int i = 0; i < hand.Count; i++)
            if (hand[i].suit == leadSuit) { hasLeadSuit = true; break; }

        for (int i = 0; i < hand.Count; i++)
            if (!hasLeadSuit || hand[i].suit == leadSuit) legal.Add(i);
        return legal;
    }

    // ---------------------------------------------------------------
    // 액션 적용 (in-place)
    // ---------------------------------------------------------------
    public void ApplyAction(int cardIdx)
    {
        var hand = hands[currentPlayer];
        var card = hand[cardIdx];
        hand.RemoveAt(cardIdx);

        cardsOnTable.Add(card);
        playersOnTable.Add(currentPlayer);

        if (cardsOnTable.Count == 1) leadSuit = card.suit;

        if (cardsOnTable.Count >= hands.Length)
            ResolveTrick();
        else
            currentPlayer = (currentPlayer + 1) % hands.Length;
    }

    // ---------------------------------------------------------------
    // 트릭 해결: 승자 결정 → 카운트 → 태스크/전역규칙 판정 → 다음 트릭
    // ---------------------------------------------------------------
    private void ResolveTrick()
    {
        int winnerPos = MissionRules.WinnerPosition(cardsOnTable, leadSuit);
        int winner    = playersOnTable[winnerPos];
        trickWinCounts[winner]++;

        if (trickNumber == 0) firstTrickWinner = winner;

        bool allEmpty = true;
        for (int i = 0; i < hands.Length; i++)
            if (hands[i].Count > 0) { allEmpty = false; break; }
        if (allEmpty) lastTrickWinner = winner;

        // ── 태스크 판정 (다중) ──────────────────────────────────────
        //   이번 트릭에 target이 든 태스크: 승자==owner → 완료, 아니면 미션 실패.
        //   순서 토큰: 완료 시 순서 위반이면 미션 실패.
        foreach (var t in tasks)
        {
            if (t.completed || t.failed) continue;
            if (!cardsOnTable.Contains(t.target)) continue;

            if (winner == t.ownerIdx)
            {
                if (!IsOrderValid(t)) { missionFailed = true; return; }
                t.completed = true;
                completedCount++;
            }
            else
            {
                t.failed      = true;   // 남이 내 목표 트릭을 가져감
                missionFailed = true;
                return;
            }
        }

        // ── 전역 규칙 판정 (트릭 단위) ───────────────────────────────
        if (globalRule != GlobalMissionRule.None)
        {
            if (MissionRules.TrickViolatesGlobalRule(
                    globalRule, cardsOnTable, playersOnTable, winner,
                    trickWinCounts, rocketWinsMax))
            {
                missionFailed = true;
                return;
            }
            int rv = MissionRules.WinningRocketValue(cardsOnTable, playersOnTable, winner);
            if (rv > rocketWinsMax) rocketWinsMax = rv;
        }

        // 다음 트릭
        cardsOnTable.Clear();
        playersOnTable.Clear();
        leadSuit = Card.Suit.Rocket;
        currentPlayer = winner;
        trickNumber++;
    }

    // 순서 토큰 유효성 (완료 직전 호출). MissionManager.IsOrderTokenValid의 시뮬 버전.
    private bool IsOrderValid(MctsTask task)
    {
        switch (task.order)
        {
            case OrderToken.None:
            case OrderToken.Arrow1:
                return true;

            case OrderToken.N1:
            case OrderToken.N2:
            case OrderToken.N3:
            case OrderToken.N4:
            case OrderToken.N5:
                return completedCount == (int)task.order - 1;

            case OrderToken.Omega:
                // 나 자신·Omega 제외 모든 태스크가 이미 완료/실패여야 함
                foreach (var t in tasks)
                {
                    if (t == task || t.order == OrderToken.Omega) continue;
                    if (!t.completed && !t.failed) return false;
                }
                return true;

            case OrderToken.Arrow2: return PrevArrowDone(OrderToken.Arrow1);
            case OrderToken.Arrow3: return PrevArrowDone(OrderToken.Arrow2);
            case OrderToken.Arrow4: return PrevArrowDone(OrderToken.Arrow3);
            default: return true;
        }
    }

    private bool PrevArrowDone(OrderToken prev)
    {
        foreach (var t in tasks)
            if (t.order == prev && t.completed) return true;
        return false;
    }

    // ---------------------------------------------------------------
    // Terminal: 미션 실패 / 전 태스크 완료 / 핸드 끝
    // ---------------------------------------------------------------
    public bool IsTerminal()
    {
        if (missionFailed) return true;
        if (AllTasksDone()) return true;
        for (int i = 0; i < hands.Length; i++)
            if (hands[i].Count > 0) return false;
        return true;
    }

    private bool AllTasksDone()
    {
        if (tasks.Count == 0) return false;
        foreach (var t in tasks) if (!t.completed) return false;
        return true;
    }

    // ---------------------------------------------------------------
    // Reward (협력): 완수 비율(0..1). 전원 완수=1.0, 실패=완수분만.
    //   target은 36색 카드라 핸드 끝이면 모든 태스크가 결판남.
    //   부분 완수도 신호로 흘려 탐색이 "더 많이 완수"를 선호하도록.
    //   태스크 없는 0E 미션: 미션 실패 아니면 1.0.
    // ---------------------------------------------------------------
    public float Reward()
    {
        if (tasks.Count == 0)
            return missionFailed ? 0f : 1f;

        int done = 0;
        foreach (var t in tasks) if (t.completed) done++;
        if (done == tasks.Count && !missionFailed) return 1f;
        return (float)done / tasks.Count;
    }

    // ---------------------------------------------------------------
    // 보조: 현재 테이블에서 이기고 있는 플레이어 (빈 테이블이면 -1)
    // ---------------------------------------------------------------
    public int CurrentWinnerPlayer()
    {
        int pos = MissionRules.WinnerPosition(cardsOnTable, leadSuit);
        return pos < 0 ? -1 : playersOnTable[pos];
    }

    public bool Beats(Card a, Card b) => MissionRules.Beats(a, b, leadSuit);
    public int  WinStrength(Card c)   => MissionRules.WinStrength(c, leadSuit);

    // 특정 플레이어가 아직 완료 안 한 태스크의 target들 (롤아웃 정책용)
    public IEnumerable<Card> PendingTargetsOf(int playerIdx)
    {
        foreach (var t in tasks)
            if (t.ownerIdx == playerIdx && !t.completed && !t.failed && t.target != null)
                yield return t.target;
    }

    // 어떤 플레이어든 아직 완료 안 한 target이 이번 트릭 테이블에 있나 → 그 owner
    public int OwnerOfTargetOnTable()
    {
        foreach (var t in tasks)
            if (!t.completed && !t.failed && t.target != null && cardsOnTable.Contains(t.target))
                return t.ownerIdx;
        return -1;
    }
}
