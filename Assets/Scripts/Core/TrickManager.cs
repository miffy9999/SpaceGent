using System.Collections.Generic;
using UnityEngine;

public class TrickManager : MonoBehaviour
{
    public DeckManager deckManager;
    public List<CrewAgent> players = new List<CrewAgent>(); // 참여하는 플레이어들

    // 트릭(한 턴) 정보
    public Card.Suit leadSuit;
    public List<Card> cardsOnTable = new List<Card>();
    public List<CrewAgent> playersOnTable = new List<CrewAgent>();

    private int currentPlayerIndex = 0; // 지금 누구 차례인지 (players 리스트의 인덱스)
    private int startPlayerIndex = 0; // 이번 트릭을 처음 시작한 사람

    void Awake()
    {
        if (deckManager == null)
        {
            deckManager = FindAnyObjectByType<DeckManager>();
        }
    }
    void Start()
    {
        StartGame();
    }

    // 게임 시작 시 초기화
    public void StartGame()
    {
        players = deckManager.players; // DeckManager에서 플레이어 목록 가져오기
        deckManager.DealCardsToAgents(); // 카드 분배

        // 게임 시작: 0번 플레이어부터 트릭 시작!
        StartNewTrick(0);
    }

    // 새로운 트릭(한 바퀴) 시작
    public void StartNewTrick(int leadingPlayerIndex)
    {
        Debug.Log($"--- 새로운 트릭 시작! 선 플레이어: {players[leadingPlayerIndex].name} ---");
        cardsOnTable.Clear();
        playersOnTable.Clear();

        startPlayerIndex = leadingPlayerIndex;
        currentPlayerIndex = leadingPlayerIndex;

        // 선 플레이어에게 턴 넘겨주기
        GiveTurnToPlayer(currentPlayerIndex);
    }

    // 특정 플레이어의 'isMyTurn'을 켜주는 함수
    private void GiveTurnToPlayer(int index)
    {
        players[index].isMyTurn = true;
        Debug.Log($"👉 {players[index].name}의 차례입니다.");
    }

    // 💡 방금 에이전트가 카드를 내면 여기로 연락이 옵니다.
    public void OnCardPlayed(CrewAgent player, Card playedCard)
    {
        cardsOnTable.Add(playedCard);
        playersOnTable.Add(player);

        // 첫 번째로 낸 카드라면 선 색상(Lead Suit) 설정
        if (cardsOnTable.Count == 1 && playedCard.suit != Card.Suit.Submarine)
        {
            leadSuit = playedCard.suit;
        }

        // 모두가 카드를 냈는지 확인 (3명 기준)
        if (cardsOnTable.Count >= players.Count)
        {
            // 3명이 다 냈으면 승자 판별!
            DetermineTrickWinner();
        }
        else
        {
            // 아직 다 안 냈으면 다음 사람에게 바통 터치
            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
            GiveTurnToPlayer(currentPlayerIndex);
        }
    }

    // 승자 판별
    public void DetermineTrickWinner()
    {
        CrewAgent winner = playersOnTable[0];
        Card winningCard = cardsOnTable[0];

        for (int i = 1; i < cardsOnTable.Count; i++)
        {
            Card currentCard = cardsOnTable[i];

            if (currentCard.suit == Card.Suit.Submarine && winningCard.suit != Card.Suit.Submarine)
            {
                winningCard = currentCard;
                winner = playersOnTable[i];
            }
            else if (currentCard.suit == Card.Suit.Submarine && winningCard.suit == Card.Suit.Submarine)
            {
                if (currentCard.value > winningCard.value) { winningCard = currentCard; winner = playersOnTable[i]; }
            }
            else if (currentCard.suit == leadSuit && winningCard.suit == leadSuit)
            {
                if (currentCard.value > winningCard.value) { winningCard = currentCard; winner = playersOnTable[i]; }
            }
        }

        Debug.Log($"🏆 트릭 승자: {winner.name} (이긴 카드: {winningCard.suit} {winningCard.value})");

        // 💡 새로 추가: 승자에게 칭찬 스티커(보상 1점) 부여!
        winner.AddReward(1.0f);

        // 이긴 사람이 다음 트릭의 선 플레이어가 됨
        int winnerIndex = players.IndexOf(winner);

        // 잠깐 쉬었다가(여기선 바로) 다음 트릭 시작
        ClearTableAndStartNextTrick();
    }

    private void ClearTableAndStartNextTrick()
    {
        // 바닥 그래픽 지우기
        foreach (Transform child in deckManager.players[0].centerBoard)
        {
            Destroy(child.gameObject);
        }

        // 💡 새로 추가: 손패를 다 썼는지 확인 (게임 종료 조건)
        if (players[0].hand.Count == 0)
        {
            EndGame(); // 게임 오버 처리
        }
        else
        {
            // 아직 손패가 남았다면 다음 트릭 시작 (방금 이긴 사람부터)
            // 편의상 0번으로 고정했던 것을 승자 인덱스로 바꿔주면 완벽합니다.
            StartNewTrick(0);
        }
    }

    // 💡 새로 추가: 에이전트가 낸 카드가 합법적인 규칙(Follow Suit)인지 검사합니다.
    public bool IsValidPlay(CrewAgent player, Card cardToPlay)
    {
        // 1. 내가 첫 번째(선)로 내는 거면 무조건 합법!
        if (cardsOnTable.Count == 0) return true;

        // 2. 선 색상이 아직 안정해졌거나, 내가 잠수함(조커)을 냈으면 합법!
        if (leadSuit == Card.Suit.Submarine || cardToPlay.suit == Card.Suit.Submarine) return true;

        // 3. 내가 낸 카드가 선 색상과 같으면 합법!
        if (cardToPlay.suit == leadSuit) return true;

        // 4. 선 색상과 다른 걸 냈다면? -> 내 손패를 뒤져서 선 색상이 있는지 확인!
        foreach (Card c in player.hand)
        {
            if (c.suit == leadSuit)
            {
                // 손패에 선 색상이 있는데 안 냈으므로 반칙!
                return false;
            }
        }

        // 손패에 선 색상이 없어서 어쩔 수 없이 다른 걸 낸 거라면 합법!
        return true;
    }

    private void EndGame()
    {
        Debug.Log("🏁 모든 카드를 소진했습니다. 게임 종료! 새 게임을 준비합니다.");

        // 1. 모든 에이전트에게 "이번 판 끝났어! 학습해!" 라고 알림
        foreach (CrewAgent agent in players)
        {
            agent.EndEpisode();
        }

        // 2. 다시 덱을 섞고 새 게임 시작 (무한 루프)
        StartGame();
    }
}