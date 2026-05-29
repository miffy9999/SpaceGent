using System.Collections.Generic;

// =====================================================================
//  MCTSState — The Crew Phase1_CoopSingle 게임 상태의 스냅샷.
// ---------------------------------------------------------------------
//  Determinized state (모든 손패 알려짐). Clone/transition 가능.
//  실제 게임 인스턴스(TrickManager/MissionManager)와 독립 — 시뮬레이션 전용.
// =====================================================================
public class MCTSState
{
    // 4 player hands (인덱스는 GameManager.players와 동일)
    public List<Card>[] hands;

    // 현재 트릭 상태
    public List<Card> cardsOnTable = new List<Card>();
    public List<int>  playersOnTable = new List<int>();
    public Card.Suit  leadSuit = Card.Suit.Rocket;   // 빈 상태 = Sub로 초기화

    // 차례 (player index)
    public int currentPlayer;

    // 트릭 추적
    public int trickNumber;          // 0-indexed, 현재 진행 중인 트릭
    public int totalTricks;          // 보통 10 (4인) / 13 (3인)
    public int[] trickWinCounts;
    public int firstTrickWinner = -1;
    public int lastTrickWinner  = -1;

    // Task 정보
    public int assigneeIdx;
    public MissionManager.Phase1Task taskType;
    public int winTarget;
    public bool taskCompleted;
    public bool taskFailed;

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
        c.assigneeIdx    = assigneeIdx;
        c.taskType       = taskType;
        c.winTarget      = winTarget;
        c.taskCompleted  = taskCompleted;
        c.taskFailed     = taskFailed;
        return c;
    }

    // ---------------------------------------------------------------
    // 합법 액션 (현재 플레이어의 손패 인덱스)
    //   Follow-suit 강제: 리드 슈트 카드 있으면 그것만, 없으면 자유.
    // ---------------------------------------------------------------
    public List<int> LegalActions()
    {
        var legal = new List<int>();
        if (currentPlayer < 0 || currentPlayer >= hands.Length) return legal;
        var hand = hands[currentPlayer];
        if (hand.Count == 0) return legal;

        // 트릭 첫 카드는 무엇이든 합법
        if (cardsOnTable.Count == 0)
        {
            for (int i = 0; i < hand.Count; i++) legal.Add(i);
            return legal;
        }

        bool hasLeadSuit = false;
        for (int i = 0; i < hand.Count; i++)
            if (hand[i].suit == leadSuit) { hasLeadSuit = true; break; }

        if (hasLeadSuit)
        {
            for (int i = 0; i < hand.Count; i++)
                if (hand[i].suit == leadSuit) legal.Add(i);
        }
        else
        {
            for (int i = 0; i < hand.Count; i++) legal.Add(i);
        }
        return legal;
    }

    // ---------------------------------------------------------------
    // 액션 적용 (in-place)
    //   1. 카드를 손패에서 테이블로 이동
    //   2. 4명 다 냈으면 트릭 해결 (승자 결정, 카운트, task 평가)
    //   3. 그 외엔 다음 플레이어로 turn 넘김
    // ---------------------------------------------------------------
    public void ApplyAction(int cardIdx)
    {
        var hand = hands[currentPlayer];
        var card = hand[cardIdx];
        hand.RemoveAt(cardIdx);

        cardsOnTable.Add(card);
        playersOnTable.Add(currentPlayer);

        // 첫 카드면 리드 슈트 결정
        if (cardsOnTable.Count == 1) leadSuit = card.suit;

        if (cardsOnTable.Count >= hands.Length)
        {
            ResolveTrick();
        }
        else
        {
            currentPlayer = (currentPlayer + 1) % hands.Length;
        }
    }

    // ---------------------------------------------------------------
    // 트릭 해결: 승자 결정, 카운트, task 평가, 다음 트릭 셋업
    // ---------------------------------------------------------------
    private void ResolveTrick()
    {
        int winnerIdx = 0;
        Card winningCard = cardsOnTable[0];
        for (int i = 1; i < cardsOnTable.Count; i++)
        {
            if (Beats(cardsOnTable[i], winningCard))
            {
                winningCard = cardsOnTable[i];
                winnerIdx = i;
            }
        }
        int winner = playersOnTable[winnerIdx];
        trickWinCounts[winner]++;

        bool isFirst = (trickNumber == 0);
        if (isFirst) firstTrickWinner = winner;

        // 핸드 끝 (모든 손패 비었나)?
        bool allEmpty = true;
        for (int i = 0; i < hands.Length; i++)
            if (hands[i].Count > 0) { allEmpty = false; break; }
        if (allEmpty) lastTrickWinner = winner;

        EvaluateTaskAfterTrick(winner, isFirst, allEmpty);

        // 다음 트릭 셋업
        cardsOnTable.Clear();
        playersOnTable.Clear();
        leadSuit = Card.Suit.Rocket;
        currentPlayer = winner;
        trickNumber++;
    }

    // ---------------------------------------------------------------
    // 트릭 후 task 평가 (즉시 completed/failed 판정)
    // ---------------------------------------------------------------
    private void EvaluateTaskAfterTrick(int winner, bool isFirstTrick, bool isLastTrick)
    {
        switch (taskType)
        {
            case MissionManager.Phase1Task.WinFirst:
                if (isFirstTrick)
                {
                    if (winner == assigneeIdx) taskCompleted = true;
                    else taskFailed = true;
                }
                break;

            case MissionManager.Phase1Task.WinNone:
                if (winner == assigneeIdx) taskFailed = true;
                break;

            case MissionManager.Phase1Task.WinLast:
                if (isLastTrick)
                {
                    if (winner == assigneeIdx) taskCompleted = true;
                    else taskFailed = true;
                }
                break;

            case MissionManager.Phase1Task.WinAtLeast:
                if (isLastTrick)
                {
                    if (trickWinCounts[assigneeIdx] >= winTarget) taskCompleted = true;
                    else taskFailed = true;
                }
                break;
        }
    }

    // ---------------------------------------------------------------
    // Terminal: task 결판 났거나 핸드 끝
    // ---------------------------------------------------------------
    public bool IsTerminal()
    {
        if (taskCompleted || taskFailed) return true;
        for (int i = 0; i < hands.Length; i++)
            if (hands[i].Count > 0) return false;
        return true;
    }

    // ---------------------------------------------------------------
    // Reward (terminal 시): task 성공 1.0 / 실패 0.0
    //   협력 게임: 모든 플레이어 동일 보상.
    // ---------------------------------------------------------------
    public float Reward()
    {
        if (taskCompleted) return 1.0f;
        if (taskFailed)    return 0.0f;
        return 0.0f;  // 핸드 끝났는데 평가 안 됐으면 실패로 간주 (정상 흐름이면 도달 X)
    }

    // ---------------------------------------------------------------
    // Beats: a가 b를 이기나 (TrickManager의 Beats와 동일 규칙)
    // ---------------------------------------------------------------
    public bool Beats(Card a, Card b)
    {
        bool aSub = a.suit == Card.Suit.Rocket;
        bool bSub = b.suit == Card.Suit.Rocket;
        if (aSub && !bSub) return true;
        if (aSub && bSub)  return a.value > b.value;
        if (!aSub && bSub) return false;
        bool aLead = a.suit == leadSuit;
        bool bLead = b.suit == leadSuit;
        if (aLead && !bLead) return true;
        if (!aLead)          return false;
        return a.value > b.value;
    }

    // ---------------------------------------------------------------
    // WinStrength (Safest/Highest에서 사용)
    // ---------------------------------------------------------------
    public int WinStrength(Card c)
    {
        if (c.suit == Card.Suit.Rocket) return 200 + c.value;
        if (c.suit == leadSuit)            return 100 + c.value;
        return c.value;
    }
}
