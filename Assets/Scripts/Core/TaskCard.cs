using UnityEngine;

/// <summary>
/// 태스크 카드에 붙는 순서 토큰.
/// 숫자 토큰(N1~N5, Omega)은 몇 번째로 완수해야 하는지,
/// 화살표 토큰(Arrow1~4)은 상대적 순서를 지정한다.
/// </summary>
public enum OrderToken
{
    None   = 0,   // 제약 없음
    N1     = 1,   // 1번째로 완수
    N2     = 2,   // 2번째로 완수
    N3     = 3,   // 3번째로 완수
    N4     = 4,   // 4번째로 완수
    N5     = 5,   // 5번째로 완수
    Omega  = 6,   // 마지막으로 완수 (Ω)
    Arrow1 = 7,   // → : Arrow2보다 먼저
    Arrow2 = 8,   // →→ : Arrow1 이후, Arrow3 이전
    Arrow3 = 9,   // →→→ : Arrow2 이후, Arrow4 이전
    Arrow4 = 10,  // →→→→ : Arrow3 이후
}

/// <summary>
/// 플레이어에게 배정되는 태스크 하나를 나타내는 데이터 클래스.
///
/// 스페이스 크루(The Crew: The Quest for Planet Nine)의 태스크 카드는 단일 종류뿐이다:
///   "지정된 색깔 카드(targetCard)가 포함된 트릭을 자신이 이겨라."
/// 태스크 카드는 색상 4종 × 1~9 = 36장에 대응한다 (로켓은 태스크 카드가 아니다).
///
/// 미션별 특수 승리 조건(예: "9는 트릭을 못 이김", "로켓 오름차순")은
/// 태스크 카드가 아니라 미션 단위 특수 규칙이며, 별도로 처리한다(추후 관찰 벡터로 표현).
/// </summary>
[System.Serializable]
public class TaskCard
{
    // 완수해야 할 대상 카드 (색깔 카드, 로켓 제외)
    public Card targetCard;

    // 순서 토큰 (None=제약 없음)
    public OrderToken orderToken = OrderToken.None;

    // ML-Agents 관측용 (숫자 토큰 값, 없으면 0)
    public int orderIndex => orderToken is >= OrderToken.N1 and <= OrderToken.N5
                             ? (int)orderToken : 0;

    // 런타임 상태
    public CrewAgent assignedTo;
    public bool isCompleted;
    public bool isFailed;

    // ---------------------------------------------------------------
    // 생성 헬퍼
    // ---------------------------------------------------------------
    public static TaskCard SpecificCard(Card card)
        => new TaskCard { targetCard = card };

    public static TaskCard SpecificCard(Card card, OrderToken token)
        => new TaskCard { targetCard = card, orderToken = token };

    // ---------------------------------------------------------------
    // 표시 텍스트
    // ---------------------------------------------------------------
    public static string OrderTokenLabel(OrderToken t) => t switch
    {
        OrderToken.N1     => "[1] ",
        OrderToken.N2     => "[2] ",
        OrderToken.N3     => "[3] ",
        OrderToken.N4     => "[4] ",
        OrderToken.N5     => "[5] ",
        OrderToken.Omega  => "[Ω] ",
        OrderToken.Arrow1 => "[→] ",
        OrderToken.Arrow2 => "[→→] ",
        OrderToken.Arrow3 => "[→→→] ",
        OrderToken.Arrow4 => "[→→→→] ",
        _                 => ""
    };

    public override string ToString()
    {
        string o = OrderTokenLabel(orderToken);
        return targetCard != null
            ? $"{o}{targetCard.suit} {targetCard.value} 트릭 획득"
            : $"{o}(대상 카드 없음)";
    }
}
