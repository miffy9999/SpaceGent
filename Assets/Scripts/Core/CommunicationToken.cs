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
    /// 손패에서 가장 높은 비-잠수함 카드를 골라 공개한다.
    /// 실제 보드게임에서는 플레이어가 직접 고르지만,
    /// 현재 구현에서는 AI가 자동으로 최고값 카드를 선택한다.
    /// </summary>
    public bool TryReveal()
    {
        if (isUsed) return false;

        Card best = null;
        foreach (Card c in owner.hand)
        {
            if (c.suit == Card.Suit.Submarine) continue;
            if (best == null || c.value > best.value) best = c;
        }

        if (best == null) return false;

        revealedCard = best;
        revealPosition = DeterminePosition(best);
        isUsed = true;

        Debug.Log($"[CommToken] {owner.name} → {best.suit} {best.value} 공개 ({revealPosition})");
        return true;
    }

    private RevealPosition DeterminePosition(Card card)
    {
        int sameCount = 0;
        bool hasHigher = false, hasLower = false;

        foreach (Card c in owner.hand)
        {
            if (c.suit != card.suit) continue;
            sameCount++;
            if (c.value > card.value) hasHigher = true;
            if (c.value < card.value) hasLower  = true;
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
