using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrickManager : MonoBehaviour
{
    [HideInInspector] public List<CrewAgent> players = new List<CrewAgent>();

    public GamePhase currentPhase { get; private set; } = GamePhase.Setup;

    public Card.Suit leadSuit;
    public List<Card> cardsOnTable   = new List<Card>();
    public List<CrewAgent> playersOnTable = new List<CrewAgent>();

    private int currentPlayerIndex = 0;
    private int captainIndex       = 0;
    private Coroutine turnTimeoutCoroutine;
    private Coroutine watchdogCoroutine;

    // ---------------------------------------------------------------
    // GameManager.Start()에서 호출
    // ---------------------------------------------------------------
    public void StartGame()
    {
        currentPhase = GamePhase.Setup;

        // 1. 카드 분배
        deckManager.DealCardsToAgents();

        // 2. 함장 결정
        captainIndex = FindCaptainIndex();
        Debug.Log($"[TrickManager] 함장: {players[captainIndex].name}");

        // 3. 통신 토큰 초기화
        GameManager.Instance.communicationManager.InitTokens();

        // 4. 감시 코루틴
        if (watchdogCoroutine != null) StopCoroutine(watchdogCoroutine);
        watchdogCoroutine = StartCoroutine(TurnWatchdog());

        // 5. 태스크 선택 단계 진입 (BGA 방식: 플레이어가 직접 선택)
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
        cardsOnTable.Clear();
        playersOnTable.Clear();
        leadSuit = Card.Suit.Submarine;

        currentPlayerIndex = leadingPlayerIndex;
        GiveTurnToPlayer(currentPlayerIndex);
    }

    // ---------------------------------------------------------------
    // 카드 제출 알림
    // ---------------------------------------------------------------
    public void OnCardPlayed(CrewAgent player, Card playedCard)
    {
        if (playersOnTable.Contains(player))
        {
            Debug.LogWarning($"[TrickManager] {player.name} 중복 제출 무시");
            return;
        }

        cardsOnTable.Add(playedCard);
        playersOnTable.Add(player);

        if (cardsOnTable.Count == 1 && playedCard.suit != Card.Suit.Submarine)
            leadSuit = playedCard.suit;

        try
        {
            if (cardsOnTable.Count >= players.Count)
                DetermineTrickWinner();
            else
            {
                currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
                GiveTurnToPlayer(currentPlayerIndex);
            }
        }
        catch (System.Exception e)
        {
            // 내부 예외로 턴이 끊기는 것을 방지 — 강제로 다음 플레이어에게 턴 부여
            Debug.LogError($"[TrickManager] OnCardPlayed 예외 → 턴 강제 복구\n{e}");
            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
            GiveTurnToPlayer(currentPlayerIndex);
        }
    }

    // ---------------------------------------------------------------
    // 승자 판별
    // ---------------------------------------------------------------
    private void DetermineTrickWinner()
    {
        int winnerIdx   = 0;
        Card winningCard = cardsOnTable[0];

        for (int i = 1; i < cardsOnTable.Count; i++)
        {
            Card c = cardsOnTable[i];
            bool cIsSub = c.suit == Card.Suit.Submarine;
            bool wIsSub = winningCard.suit == Card.Suit.Submarine;

            if      (cIsSub && !wIsSub)                                 { winningCard = c; winnerIdx = i; }
            else if (cIsSub && wIsSub  && c.value > winningCard.value)  { winningCard = c; winnerIdx = i; }
            else if (c.suit == leadSuit && winningCard.suit == leadSuit
                     && c.value > winningCard.value)                     { winningCard = c; winnerIdx = i; }
        }

        CrewAgent winner    = playersOnTable[winnerIdx];
        int nextLeadIndex   = players.IndexOf(winner);

        Debug.Log($"트릭 승자: {winner.name} ({winningCard.suit} {winningCard.value})");

        MissionManager.Instance.OnTrickResolved(winner, new List<Card>(cardsOnTable));
        ClearTableAndStartNextTrick(nextLeadIndex);
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
            MissionManager.Instance.OnHandEnded();
            EndGame();
        }
        else if (MissionManager.Instance.HasMissionEnded)
        {
            // 트릭 도중 미션 실패 확정 → 남은 트릭 없이 즉시 에피소드 종료
            EndGame();
        }
        else
        {
            StartNewTrick(nextLeadIndex);
        }
    }

    // ---------------------------------------------------------------
    // 게임 종료
    // Fix: EndEpisode() 후 1프레임 대기 → StartGame() 충돌 방지
    // ---------------------------------------------------------------
    private void EndGame()
    {
        Debug.Log("[TrickManager] 에피소드 종료");

        if (turnTimeoutCoroutine != null) { StopCoroutine(turnTimeoutCoroutine); turnTimeoutCoroutine = null; }
        if (watchdogCoroutine    != null) { StopCoroutine(watchdogCoroutine);    watchdogCoroutine    = null; }

        foreach (CrewAgent agent in players)
            agent.EndEpisode();

        StartCoroutine(RestartAfterEpisodeEnd());
    }

    private IEnumerator RestartAfterEpisodeEnd()
    {
        yield return new WaitForSeconds(1.5f); // 결과 패널 확인 후 재시작
        StartGame();
    }

    // ---------------------------------------------------------------
    // 턴 부여 + 타임아웃 감시
    // ---------------------------------------------------------------
    private void GiveTurnToPlayer(int index)
    {
        players[index].isMyTurn = true;

        if (index != 0)
            players[index].RequestDecision();

        // 기존 타임아웃 취소 후 새로 시작
        if (turnTimeoutCoroutine != null)
            StopCoroutine(turnTimeoutCoroutine);
        turnTimeoutCoroutine = StartCoroutine(TurnTimeout(index));

        Debug.Log($"→ {players[index].name}의 차례");
    }

    // 일정 시간 안에 카드를 내지 않으면 RequestDecision 재요청
    private IEnumerator TurnTimeout(int index)
    {
        yield return new WaitForSeconds(5f);

        if (players[index].isMyTurn)
        {
            Debug.LogWarning($"[TrickManager] {players[index].name} 5초 타임아웃 → RequestDecision 재요청");
            players[index].RequestDecision();
        }
    }

    // 아무도 턴을 갖지 않으면 currentPlayerIndex로 강제 복구
    private IEnumerator TurnWatchdog()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);

            bool anyoneHasTurn = false;
            foreach (var p in players)
                if (p.isMyTurn) { anyoneHasTurn = true; break; }

            if (!anyoneHasTurn)
            {
                Debug.LogWarning($"[Watchdog] 턴 소유자 없음 → {players[currentPlayerIndex].name} 강제 복구");
                GiveTurnToPlayer(currentPlayerIndex);
            }
        }
    }

    // ---------------------------------------------------------------
    // Follow Suit 규칙 검사
    // ---------------------------------------------------------------
    public bool IsValidPlay(CrewAgent player, Card cardToPlay)
    {
        if (cardsOnTable.Count == 0)         return true;
        if (leadSuit == Card.Suit.Submarine)  return true;
        if (cardToPlay.suit == Card.Suit.Submarine) return true;
        if (cardToPlay.suit == leadSuit)      return true;

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

        Debug.LogWarning("[TrickManager] 잠수함 4번을 찾지 못했습니다. 0번이 선이 됩니다.");
        return 0;
    }

    private DeckManager deckManager => GameManager.Instance.deckManager;
}
