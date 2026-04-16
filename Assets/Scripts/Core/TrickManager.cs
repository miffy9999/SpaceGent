using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrickManager : MonoBehaviour
{
    [HideInInspector] public List<CrewAgent> players = new List<CrewAgent>();

    public GamePhase currentPhase { get; private set; } = GamePhase.Setup;

    public Card.Suit leadSuit;
    public List<Card>      cardsOnTable   = new List<Card>();
    public List<CrewAgent> playersOnTable = new List<CrewAgent>();

    private int captainIndex  = 0;
    private int trickLeadIndex = 0;   // 현재 트릭의 선 플레이어 인덱스

    // currentPlayerIndex: 지금 턴을 가진(또는 가져야 할) 플레이어 인덱스.
    // StartNewTrick / OnCardPlayed 에서만 갱신된다.
    private int currentPlayerIndex = 0;

    private Coroutine turnTimeoutCoroutine;
    private Coroutine watchdogCoroutine;

    /// <summary>
    /// 실제 딥 씨 크루 규칙: 통신 토큰은 트릭과 트릭 사이(아무도 카드를 내지 않은 상태)에만 사용 가능.
    /// 트릭이 시작되어 첫 카드가 나오면 false가 된다.
    /// </summary>
    public bool IsBetweenTricks { get; private set; } = true;

    // ---------------------------------------------------------------
    // GameManager.Start()에서 호출
    // ---------------------------------------------------------------
    public void StartGame()
    {
        currentPhase = GamePhase.Setup;

        // 모든 플레이어 턴 초기화 (에피소드 리셋 안전)
        ResetAllTurns();

        // 1. 카드 분배
        deckManager.DealCardsToAgents();

        // 2. 함장 결정
        captainIndex = FindCaptainIndex();
        Debug.Log($"[TrickManager] 함장: {players[captainIndex].name}");

        // 3. 통신 토큰 초기화
        GameManager.Instance.communicationManager.InitTokens();

        // 4. Watchdog 재시작 (Playing 페이즈에서만 동작)
        if (watchdogCoroutine != null) StopCoroutine(watchdogCoroutine);
        watchdogCoroutine = StartCoroutine(TurnWatchdog());

        // 5. 태스크 선택 단계
        currentPhase = GamePhase.TaskSelection;
        MissionManager.Instance.StartTaskSelectionPhase(captainIndex);
    }

    // ---------------------------------------------------------------
    // 태스크 선택 완료 후 MissionManager에서 호출
    // ---------------------------------------------------------------
    public void StartPlaying()
    {
        currentPhase = GamePhase.Playing;
        StartNewTrick(captainIndex);
    }

    // ---------------------------------------------------------------
    // 새 트릭 시작
    // ---------------------------------------------------------------
    public void StartNewTrick(int leadingPlayerIndex)
    {
        Debug.Log($"--- 새 트릭 | 선: {players[leadingPlayerIndex].name} ---");

        // 이전 트릭의 남은 isMyTurn 플래그를 모두 초기화한다.
        // 늦게 도착하는 RequestDecision 응답이 새 트릭에 섞이지 않도록 방지.
        ResetAllTurns();

        cardsOnTable.Clear();
        playersOnTable.Clear();
        leadSuit = Card.Suit.Submarine;

        trickLeadIndex     = leadingPlayerIndex;
        currentPlayerIndex = leadingPlayerIndex;

        IsBetweenTricks = true;   // 새 트릭 시작 전 = 통신 가능 구간
        GiveTurnToPlayer(currentPlayerIndex);
    }

    // ---------------------------------------------------------------
    // 카드 제출 알림 (CrewAgent.PlayCard → 이곳으로)
    // ---------------------------------------------------------------
    public void OnCardPlayed(CrewAgent player, Card playedCard)
    {
        // 이미 이번 트릭에서 카드를 낸 플레이어 → 무시
        if (playersOnTable.Contains(player))
        {
            Debug.LogWarning($"[TrickManager] {player.name} 중복 제출 무시");
            return;
        }

        // Playing 단계가 아니면 버린다 (TaskSelection 중 오발 방지)
        if (currentPhase != GamePhase.Playing)
        {
            Debug.LogWarning($"[TrickManager] {player.name} — Playing 아닌 단계({currentPhase})에서 카드 제출 무시");
            return;
        }

        IsBetweenTricks = false;  // 첫 카드가 나오는 순간 통신 불가
        cardsOnTable.Add(playedCard);
        playersOnTable.Add(player);

        // 첫 번째 카드(잠수함 제외)가 선 수트를 결정
        if (cardsOnTable.Count == 1 && playedCard.suit != Card.Suit.Submarine)
            leadSuit = playedCard.suit;

        if (cardsOnTable.Count >= players.Count)
        {
            DetermineTrickWinner();
        }
        else
        {
            // 다음 플레이어: 선 인덱스 + 제출 수로 결정
            // (currentPlayerIndex + 1 대신 이 식을 쓰면, Watchdog 오발 후에도 순서가 올바르다)
            currentPlayerIndex = (trickLeadIndex + playersOnTable.Count) % players.Count;
            GiveTurnToPlayer(currentPlayerIndex);
        }
    }

    // ---------------------------------------------------------------
    // 승자 판별
    // ---------------------------------------------------------------
    private void DetermineTrickWinner()
    {
        int  winnerIdx   = 0;
        Card winningCard = cardsOnTable[0];

        for (int i = 1; i < cardsOnTable.Count; i++)
        {
            Card c      = cardsOnTable[i];
            bool cIsSub = c.suit == Card.Suit.Submarine;
            bool wIsSub = winningCard.suit == Card.Suit.Submarine;

            if      (cIsSub && !wIsSub)                                 { winningCard = c; winnerIdx = i; }
            else if (cIsSub && wIsSub  && c.value > winningCard.value)  { winningCard = c; winnerIdx = i; }
            else if (c.suit == leadSuit && winningCard.suit == leadSuit
                     && c.value > winningCard.value)                    { winningCard = c; winnerIdx = i; }
        }

        CrewAgent winner      = playersOnTable[winnerIdx];
        int       nextLead    = players.IndexOf(winner);

        Debug.Log($"트릭 승자: {winner.name} ({winningCard.suit} {winningCard.value})");

        MissionManager.Instance.OnTrickResolved(winner, new List<Card>(cardsOnTable));
        ClearTableAndStartNextTrick(nextLead);
    }

    // ---------------------------------------------------------------
    // 테이블 정리 → 다음 트릭 or 게임 종료
    // ---------------------------------------------------------------
    private void ClearTableAndStartNextTrick(int nextLeadIndex)
    {
        foreach (Transform child in GameManager.Instance.centerBoard)
            Destroy(child.gameObject);

        if (players[0].hand.Count == 0)
        {
            // 모든 카드를 냈으면 핸드 종료
            MissionManager.Instance.OnHandEnded();
            EndGame();
        }
        else if (MissionManager.Instance.HasMissionEnded)
        {
            // 트릭 도중 미션 실패 확정 → 즉시 종료
            EndGame();
        }
        else
        {
            StartNewTrick(nextLeadIndex);
        }
    }

    // ---------------------------------------------------------------
    // 게임 종료
    // ---------------------------------------------------------------
    private void EndGame()
    {
        Debug.Log("[TrickManager] 에피소드 종료");

        currentPhase = GamePhase.Result;

        // 코루틴 정지
        if (turnTimeoutCoroutine != null) { StopCoroutine(turnTimeoutCoroutine); turnTimeoutCoroutine = null; }
        if (watchdogCoroutine    != null) { StopCoroutine(watchdogCoroutine);    watchdogCoroutine    = null; }

        // 모든 턴 초기화 후 에피소드 종료
        ResetAllTurns();
        foreach (CrewAgent agent in players)
            agent.EndEpisode();

        StartCoroutine(RestartAfterEpisodeEnd());
    }

    private IEnumerator RestartAfterEpisodeEnd()
    {
        yield return new WaitForSeconds(1.5f);
        StartGame();
    }

    // ---------------------------------------------------------------
    // 턴 부여 + 타임아웃 감시
    // ---------------------------------------------------------------
    private void GiveTurnToPlayer(int index)
    {
        // [핵심 수정] 다른 플레이어의 isMyTurn을 먼저 모두 끄고 대상만 켠다.
        // → 이전 RequestDecision 응답이 늦게 도착해도 isMyTurn=false 로 즉시 무효화된다.
        ResetAllTurns();
        players[index].isMyTurn = true;

        if (index != 0)
            players[index].RequestDecision();

        // 기존 타임아웃 취소 후 새로 시작
        if (turnTimeoutCoroutine != null)
            StopCoroutine(turnTimeoutCoroutine);
        turnTimeoutCoroutine = StartCoroutine(TurnTimeout(index));

        Debug.Log($"→ {players[index].name}의 차례");
    }

    // 일정 시간 안에 카드를 내지 않으면 RequestDecision 재요청 (AI 전용)
    private IEnumerator TurnTimeout(int index)
    {
        yield return new WaitForSeconds(5f);

        if (players[index].isMyTurn && index != 0)
        {
            Debug.LogWarning($"[TrickManager] {players[index].name} 5초 타임아웃 → RequestDecision 재요청");
            players[index].RequestDecision();
        }
    }

    // ---------------------------------------------------------------
    // Watchdog: 아무도 턴을 갖지 않을 때 복구
    // [핵심 수정] Playing 페이즈에서만 동작. 복구 대상도 트릭 상태에서 계산.
    // ---------------------------------------------------------------
    private IEnumerator TurnWatchdog()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);

            // Playing 단계가 아니면 완전히 건너뛴다
            if (currentPhase != GamePhase.Playing) continue;

            bool anyoneHasTurn = false;
            foreach (var p in players)
                if (p.isMyTurn) { anyoneHasTurn = true; break; }

            if (!anyoneHasTurn)
            {
                // 트릭 상태(선 인덱스 + 이미 낸 카드 수)로 올바른 다음 플레이어를 산출
                int expectedIndex = (trickLeadIndex + playersOnTable.Count) % players.Count;
                Debug.LogWarning($"[Watchdog] 턴 소유자 없음 → {players[expectedIndex].name} 강제 복구");
                GiveTurnToPlayer(expectedIndex);
            }
        }
    }

    // ---------------------------------------------------------------
    // Follow Suit 규칙 검사
    // ---------------------------------------------------------------
    public bool IsValidPlay(CrewAgent player, Card cardToPlay)
    {
        if (cardsOnTable.Count == 0)                  return true;
        if (leadSuit == Card.Suit.Submarine)           return true;
        if (cardToPlay.suit == Card.Suit.Submarine)    return true;
        if (cardToPlay.suit == leadSuit)               return true;

        // 손패에 선 수트가 있으면 반드시 내야 한다
        foreach (Card c in player.hand)
            if (c.suit == leadSuit) return false;

        return true;
    }

    // ---------------------------------------------------------------
    // 함장 (잠수함 4번 소지자)
    // ---------------------------------------------------------------
    private int FindCaptainIndex()
    {
        for (int i = 0; i < players.Count; i++)
            foreach (Card c in players[i].hand)
                if (c.suit == Card.Suit.Submarine && c.value == 4)
                    return i;

        Debug.LogWarning("[TrickManager] 잠수함 4번을 찾지 못했습니다. 0번이 함장이 됩니다.");
        return 0;
    }

    // ---------------------------------------------------------------
    // 모든 플레이어의 isMyTurn 플래그 일괄 초기화
    // ---------------------------------------------------------------
    private void ResetAllTurns()
    {
        foreach (var p in players)
            p.isMyTurn = false;
    }

    private DeckManager deckManager => GameManager.Instance.deckManager;
}
