using UnityEngine;

/// <summary>
/// 조난신호: 미션 시작 전(첫 트릭 전), 팀이 사용 결정하면
/// 카드 한 장을 인접 플레이어에게 전달한다.
/// 로켓(잠수함) 카드는 전달 불가.
/// 사용 시 이번 미션의 시도 횟수 기록에 +1 패널티.
/// </summary>
public class DistressSignal
{
    public enum Direction { Left, Right }

    public bool isActive   = false;  // 팀이 사용 결정한 상태 (토큰 앞면)
    public bool isExecuted = false;  // 카드 전달 완료 여부

    public CrewAgent passingPlayer = null;
    public Card      cardToPass    = null;
    public Direction direction     = Direction.Right;

    public void Reset()
    {
        isActive       = false;
        isExecuted     = false;
        passingPlayer  = null;
        cardToPass     = null;
    }

    public bool CanActivate(CrewAgent player, Card card)
    {
        if (isActive || isExecuted) return false;
        if (card == null)           return false;
        if (card.suit == Card.Suit.Submarine) return false; // 로켓 카드 전달 불가
        return player.hand.Contains(card);
    }

    public bool Activate(CrewAgent player, Card card, Direction dir)
    {
        if (!CanActivate(player, card)) return false;
        passingPlayer = player;
        cardToPass    = card;
        direction     = dir;
        isActive      = true;
        Debug.Log($"[조난신호] {player.name}이(가) {card}를 {dir}쪽에 전달 예약");
        return true;
    }

    /// <summary>카드를 인접 플레이어에게 실제로 전달한다.</summary>
    public bool Execute()
    {
        if (!isActive || isExecuted)             return false;
        if (passingPlayer == null || cardToPass == null) return false;

        var players   = GameManager.Instance.players;
        int senderIdx = players.IndexOf(passingPlayer);
        if (senderIdx < 0) return false;

        int receiverIdx = direction == Direction.Left
            ? (senderIdx - 1 + players.Count) % players.Count
            : (senderIdx + 1)                 % players.Count;

        CrewAgent receiver = players[receiverIdx];

        if (!passingPlayer.hand.Remove(cardToPass))
        {
            Debug.LogWarning("[조난신호] 카드 전달 실패: 카드가 손패에 없음");
            return false;
        }
        receiver.hand.Add(cardToPass);
        isExecuted = true;

        Debug.Log($"[조난신호] {passingPlayer.name} → {receiver.name}: {cardToPass} 전달 완료");
        return true;
    }
}
