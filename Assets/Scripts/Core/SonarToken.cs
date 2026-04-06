using UnityEngine;

/// <summary>
/// 소나 토큰: 다른 플레이어의 손패 카드 1장을 전원에게 공개한다.
/// 통신 토큰(자기 카드 공개)과 달리 타인의 카드를 엿본다.
/// </summary>
public class SonarToken
{
    public CrewAgent owner;
    public bool isUsed      = false;
    public CrewAgent target = null;
    public Card revealedCard = null;

    public SonarToken(CrewAgent owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// target 플레이어의 손패 중 가장 높은 값 카드를 공개한다.
    /// </summary>
    public bool TryReveal(CrewAgent target)
    {
        if (isUsed)                          return false;
        if (target == owner)                 return false;
        if (target == null)                  return false;
        if (target.hand.Count == 0)          return false;

        // 가장 높은 값 카드 선택 (잠수함 제외)
        Card best = null;
        foreach (Card c in target.hand)
        {
            if (c.suit == Card.Suit.Submarine) continue;
            if (best == null || c.value > best.value) best = c;
        }

        // 잠수함만 있으면 그냥 첫 번째 카드
        if (best == null) best = target.hand[0];

        this.target      = target;
        revealedCard     = best;
        isUsed           = true;

        Debug.Log($"[Sonar] {owner.name} → {target.name}의 {best} 공개");
        return true;
    }

    public void Reset()
    {
        isUsed       = false;
        target       = null;
        revealedCard = null;
    }
}
