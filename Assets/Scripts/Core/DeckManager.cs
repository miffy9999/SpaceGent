using System.Collections.Generic;
using UnityEngine;

public class DeckManager : MonoBehaviour
{
    public List<Card> allCards = new List<Card>();

    // GameManager.Start()에서 할당됨 (DeckManager가 직접 소유하지 않음)
    [HideInInspector] public List<CrewAgent> players = new List<CrewAgent>();

    void Awake()
    {
        CreateDeck();
    }

    private void CreateDeck()
    {
        allCards.Clear();

        // 4색 카드 (1~9) × 4 = 36장
        for (int s = 0; s < 4; s++)
        {
            for (int v = 1; v <= 9; v++)
            {
                allCards.Add(new Card((Card.Suit)s, v));
            }
        }

        // 잠수함 카드 (1~4) = 4장  →  합계 40장 (4인 기준 1인당 10장)
        for (int v = 1; v <= 4; v++)
        {
            allCards.Add(new Card(Card.Suit.Submarine, v));
        }
    }

    public void Shuffle()
    {
        for (int i = 0; i < allCards.Count; i++)
        {
            int r = Random.Range(i, allCards.Count);
            (allCards[i], allCards[r]) = (allCards[r], allCards[i]);
        }
    }

    public void DealCardsToAgents()
    {
        Shuffle();

        foreach (var player in players)
            player.ClearHand();

        // 4명에게 순서대로 1장씩 → 각자 10장
        for (int i = 0; i < allCards.Count; i++)
        {
            players[i % players.Count].ReceiveCard(allCards[i]);
        }

        Debug.Log($"[DeckManager] {players.Count}명에게 {allCards.Count / players.Count}장씩 분배 완료");
    }
}
