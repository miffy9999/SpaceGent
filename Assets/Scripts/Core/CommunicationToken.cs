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
            if (c.suit == Card.Suit.Rocket) continue;
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
        if (card.suit == Card.Suit.Rocket) return false;
        if (!owner.hand.Contains(card)) return false;
        if (!IsValidCommunicationCard(card)) return false;

        revealedCard   = card;
        revealPosition = DeterminePosition(card);
        isUsed         = true;

        Debug.Log($"[CommToken] {owner.name} → {card.suit} {card.value} 공개 ({revealPosition})");
        return true;
    }

    /// <summary>
    /// 포지션 지정 공개: task 타깃 수트(suit) + 요청 포지션에 해당하는 카드 중 가장 높은 값을 공개한다.
    /// 해당 조건 카드가 없으면 false. 마스킹으로 사전 차단하는 것이 전제.
    /// </summary>
    public bool TryRevealWithPosition(RevealPosition pos, Card.Suit suit)
    {
        if (isUsed) return false;

        Card best = null;
        foreach (Card c in owner.hand)
        {
            if (c.suit == Card.Suit.Rocket) continue;
            if (c.suit != suit) continue;
            if (!IsValidCommunicationCard(c)) continue;
            if (DeterminePosition(c) != pos) continue;
            if (best == null || c.value > best.value) best = c;
        }

        if (best == null) return false;

        revealedCard   = best;
        revealPosition = pos;
        isUsed         = true;
        Debug.Log($"[CommToken] {owner.name} → {best.suit} {best.value} 공개 ({revealPosition}, 정책 선택)");
        return true;
    }

    /// <summary>지정 수트+포지션에 해당하는 통신 가능 카드가 손패에 있는지 확인한다 (마스킹용).</summary>
    public bool HasCardOfPosition(RevealPosition pos, Card.Suit suit)
    {
        foreach (Card c in owner.hand)
        {
            if (c.suit == Card.Suit.Rocket) continue;
            if (c.suit != suit) continue;
            if (!IsValidCommunicationCard(c)) continue;
            if (DeterminePosition(c) == pos) return true;
        }
        return false;
    }

    /// <summary>통신 가능 조건: 해당 무늬에서 최고값이거나, 유일하거나, 최저값이어야 한다.</summary>
    public bool IsValidCommunicationCard(Card card)
    {
        if (card.suit == Card.Suit.Rocket) return false;
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

    /// <summary>
    /// 데드존(Dead Zone) 통신: 카드를 공개하되 위치 토큰을 올리지 않는다.
    /// 다른 플레이어는 어떤 위치인지 직관으로만 추측해야 한다.
    /// </summary>
    public bool TryRevealDeadZone()
    {
        if (isUsed) return false;

        Card best = null;
        foreach (Card c in owner.hand)
        {
            if (c.suit == Card.Suit.Rocket) continue;
            if (best == null || c.value > best.value) best = c;
        }
        if (best == null) return false;

        revealedCard   = best;
        revealPosition = DeterminePosition(best);  // 내부 계산은 하지만 UI에 표시 안 함
        isUsed         = true;
        IsDeadZone     = true;

        Debug.Log($"[CommToken:DeadZone] {owner.name} → {best.suit} {best.value} 공개 (위치 비공개)");
        return true;
    }

    /// <summary>이 통신이 데드존 모드였는지 여부.</summary>
    public bool IsDeadZone { get; private set; } = false;

    public void Reset()
    {
        isUsed       = false;
        revealedCard = null;
        IsDeadZone   = false;
    }
}

