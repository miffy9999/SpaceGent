using UnityEngine;

/// <summary>
/// 플레이어 1인당 1개씩 보유하는 통신 토큰.
/// 사용하면 손패 중 카드 1장을 공개하고, 그 위치(최고/유일/최저)를 알린다.
/// </summary>
public class CommunicationToken
{
    public enum RevealPosition
    {
        Highest,  // 해당 수트에서 가장 높은 숫자 보유
        Only,     // 해당 수트를 이 카드 하나만 보유
        Lowest,   // 해당 수트에서 가장 낮은 숫자 보유
    }

    public CrewAgent owner;
    public bool isUsed = false;

    // 공개된 카드 정보 (사용 전 null)
    public Card revealedCard = null;
    public RevealPosition revealPosition;

    public CommunicationToken(CrewAgent owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// AI 자동 선택: 손패에서 통신 가능한 카드(최고/유일/최저) 중 가장 높은 값 카드를 공개한다.
    /// </summary>
    public bool TryReveal()
    {
        if (isUsed) return false;

        Card best = null;
        foreach (Card c in owner.hand)
        {
            if (c.suit == Card.Suit.Submarine) continue;
            if (!IsValidCommunicationCard(c)) continue;  // 중간값 카드 제외
            if (best == null || c.value > best.value) best = c;
        }

        if (best == null) return false;

        revealedCard   = best;
        revealPosition = DeterminePosition(best);
        isUsed         = true;

        Debug.Log($"[CommToken] {owner.name} → {best.suit} {best.value} 공개 ({revealPosition})");
        return true;
    }

    /// <summary>
    /// 인간 플레이어용: 특정 카드를 지정하여 공개한다.
    /// 로켓 카드 불가, 해당 무늬에서 최고/유일/최저 조건을 만족해야 한다.
    /// </summary>
    public bool TryReveal(Card card)
    {
        if (isUsed) return false;
        if (card == null) return false;
        if (card.suit == Card.Suit.Submarine) return false;
        if (!owner.hand.Contains(card)) return false;
        if (!IsValidCommunicationCard(card)) return false;

        revealedCard   = card;
        revealPosition = DeterminePosition(card);
        isUsed         = true;

        Debug.Log($"[CommToken] {owner.name} → {card.suit} {card.value} 공개 ({revealPosition})");
        return true;
    }

    /// <summary>통신 가능 조건: 해당 무늬에서 최고값이거나, 유일하거나, 최저값이어야 한다.</summary>
    public bool IsValidCommunicationCard(Card card)
    {
        if (card.suit == Card.Suit.Submarine) return false;
        bool hasHigher = false, hasLower = false;
        foreach (Card c in owner.hand)
        {
            if (c.suit != card.suit) continue;
            if (c.value > card.value) hasHigher = true;
            if (c.value < card.value) hasLower  = true;
        }
        // 더 높은 카드도 있고 더 낮은 카드도 있는 중간값 → 통신 불가
        return !(hasHigher && hasLower);
    }

    private RevealPosition DeterminePosition(Card card)
    {
        int sameCount = 0;
        bool hasHigher = false;

        foreach (Card c in owner.hand)
        {
            if (c.suit != card.suit) continue;
            sameCount++;
            if (c.value > card.value) hasHigher = true;
        }

        if (sameCount == 1) return RevealPosition.Only;
        if (!hasHigher)     return RevealPosition.Highest;
        return RevealPosition.Lowest;
    }

    public void Reset()
    {
        isUsed       = false;
        revealedCard = null;
    }
}
