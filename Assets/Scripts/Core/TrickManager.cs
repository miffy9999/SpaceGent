using System.Collections.Generic;
using UnityEngine;

public class TrickManager : MonoBehaviour
{
    // GameManager가 Start()에서 할당해 줌
    [HideInInspector] public List<CrewAgent> players = new List<CrewAgent>();

    // 트릭(한 턴) 정보
    public Card.Suit leadSuit;
    public List<Card> cardsOnTable = new List<Card>();
    public List<CrewAgent> playersOnTable = new List<CrewAgent>();

    private int currentPlayerIndex = 0;

    // ---------------------------------------------------------------
    // GameManager.Start()에서 호출됨 (자체 Start() 없음)
    // ---------------------------------------------------------------
    public void StartGame()
    {
        // 카드를 먼저 분배한 뒤 잠수함 4번 소지자를 함장으로 지정
        deckManager.DealCardsToAgents();

        int captainIndex = FindCaptainIndex();
        Debug.Log($"[TrickManager] 함장: {players[captainIndex].name} (잠수함 4번 소지)");

        StartNewTrick(captainIndex);
    }

    // ---------------------------------------------------------------
    // 새로운 트릭 시작
    // ---------------------------------------------------------------
    public void StartNewTrick(int leadingPlayerIndex)
    {
        Debug.Log($"--- 새 트릭 시작 | 선: {players[leadingPlayerIndex].name} ---");
        cardsOnTable.Clear();
        playersOnTable.Clear();
        leadSuit = Card.Suit.Submarine; // 미정 상태로 초기화

        currentPlayerIndex = leadingPlayerIndex;
        GiveTurnToPlayer(currentPlayerIndex);
    }

    // ---------------------------------------------------------------
    // 카드가 낼 때 TrickManager에 알림 (CrewAgent → 여기)
    // ---------------------------------------------------------------
    public void OnCardPlayed(CrewAgent player, Card playedCard)
    {
        cardsOnTable.Add(playedCard);
        playersOnTable.Add(player);

        // 첫 카드 → 선 색상 결정 (잠수함은 선 색상이 되지 않음)
        if (cardsOnTable.Count == 1 && playedCard.suit != Card.Suit.Submarine)
        {
            leadSuit = playedCard.suit;
        }

        // 4명 모두 냈으면 승자 판별
        if (cardsOnTable.Count >= players.Count)
        {
            DetermineTrickWinner();
        }
        else
        {
            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
            GiveTurnToPlayer(currentPlayerIndex);
        }
    }

    // ---------------------------------------------------------------
    // 승자 판별 (버그 수정: winnerIndex를 추적해서 다음 선으로 사용)
    // ---------------------------------------------------------------
    private void DetermineTrickWinner()
    {
        int winnerIdx = 0;
        Card winningCard = cardsOnTable[0];

        for (int i = 1; i < cardsOnTable.Count; i++)
        {
            Card c = cardsOnTable[i];

            bool currentIsSubmarine = c.suit == Card.Suit.Submarine;
            bool winnerIsSubmarine  = winningCard.suit == Card.Suit.Submarine;

            if (currentIsSubmarine && !winnerIsSubmarine)
            {
                winningCard = c; winnerIdx = i;
            }
            else if (currentIsSubmarine && winnerIsSubmarine)
            {
                if (c.value > winningCard.value) { winningCard = c; winnerIdx = i; }
            }
            else if (c.suit == leadSuit && winningCard.suit == leadSuit)
            {
                if (c.value > winningCard.value) { winningCard = c; winnerIdx = i; }
            }
        }

        CrewAgent winner = playersOnTable[winnerIdx];
        int nextLeadIndex = players.IndexOf(winner); // ← 버그 수정: 0 하드코딩 제거

        Debug.Log($"트릭 승자: {winner.name} ({winningCard.suit} {winningCard.value})");
        winner.AddReward(1.0f);

        ClearTableAndStartNextTrick(nextLeadIndex);
    }

    // ---------------------------------------------------------------
    // 테이블 정리 후 다음 트릭 (버그 수정: 공용 centerBoard 사용)
    // ---------------------------------------------------------------
    private void ClearTableAndStartNextTrick(int nextLeadIndex)
    {
        // 버그 수정: players[0].centerBoard 하드코딩 → GameManager 공용 centerBoard
        foreach (Transform child in GameManager.Instance.centerBoard)
        {
            Destroy(child.gameObject);
        }

        // 손패 소진 여부로 게임 종료 판단
        if (players[0].hand.Count == 0)
        {
            EndGame();
        }
        else
        {
            StartNewTrick(nextLeadIndex); // ← 버그 수정: 승자가 다음 선
        }
    }

    // ---------------------------------------------------------------
    // 규칙 검사 (Follow Suit)
    // ---------------------------------------------------------------
    public bool IsValidPlay(CrewAgent player, Card cardToPlay)
    {
        if (cardsOnTable.Count == 0) return true;
        if (leadSuit == Card.Suit.Submarine) return true;
        if (cardToPlay.suit == Card.Suit.Submarine) return true;
        if (cardToPlay.suit == leadSuit) return true;

        foreach (Card c in player.hand)
        {
            if (c.suit == leadSuit) return false; // 선 색상 있는데 안 냄 → 반칙
        }

        return true;
    }

    // ---------------------------------------------------------------
    // 게임 종료 → ML-Agents 에피소드 종료 후 새 게임
    // ---------------------------------------------------------------
    private void EndGame()
    {
        Debug.Log("[TrickManager] 모든 카드 소진 → 에피소드 종료");

        foreach (CrewAgent agent in players)
        {
            agent.EndEpisode();
        }

        StartGame();
    }

    // ---------------------------------------------------------------
    // 잠수함 4번 소지자 = 함장 (버그 수정: 항상 0번 고정 제거)
    // ---------------------------------------------------------------
    private int FindCaptainIndex()
    {
        for (int i = 0; i < players.Count; i++)
        {
            foreach (Card c in players[i].hand)
            {
                if (c.suit == Card.Suit.Submarine && c.value == 4)
                    return i;
            }
        }
        Debug.LogWarning("[TrickManager] 잠수함 4번을 찾지 못했습니다. 0번 플레이어가 선이 됩니다.");
        return 0;
    }

    private void GiveTurnToPlayer(int index)
    {
        players[index].isMyTurn = true;

        // 0번(인간)은 Update()에서 키 입력 후 RequestDecision() 호출
        // 1~3번(AI)은 바로 RequestDecision() 호출
        if (index != 0)
            players[index].RequestDecision();

        Debug.Log($"→ {players[index].name}의 차례");
    }

    // DeckManager는 GameManager를 통해 접근
    private DeckManager deckManager => GameManager.Instance.deckManager;
}
