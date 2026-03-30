using System.Collections.Generic;
using UnityEngine;

public class DeckManager : MonoBehaviour
{
    public List<Card> allCards = new List<Card>();
    // 씬에 있는 에이전트
    public List<CrewAgent> players = new List<CrewAgent>();

    void Awake()
    {
        CreateDeck();
    }

    void CreateDeck()
    {
        allCards.Clear();

        // 4가지 색상 카드 생성 (1~9)
        for (int s = 0; s < 4; s++)
        {
            for (int v = 1; v <= 9; v++)
            {
                allCards.Add(new Card((Card.Suit)s, v));
            }
        }
        // 잠수함 카드 생성 (1~4)
        for (int v = 1; v <= 4; v++)
        {
            allCards.Add(new Card(Card.Suit.Submarine, v));
        }
    }

    public void Shuffle()
    {
        for (int i = 0; i < allCards.Count; i++)
        {
            Card temp = allCards[i];
            int randomIndex = Random.Range(i, allCards.Count);
            allCards[i] = allCards[randomIndex];
            allCards[randomIndex] = temp;
        }
    }
    public void DealCardsToAgents()
    {
        Shuffle();

        // 먼저 모든 플레이어의 기존 손패를 비웁니다.
        foreach (var player in players)
        {
            player.ClearHand();
        }

        // 차례대로 카드 분배
        int playerIndex = 0;
        foreach (Card card in allCards)
        {
            players[playerIndex].ReceiveCard(card); // 에이전트의 함수 호출!
            playerIndex = (playerIndex + 1) % players.Count;
        }

        Debug.Log($"총 {players.Count}명의 에이전트에게 카드 분배 완료!");
    }
}