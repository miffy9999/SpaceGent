using UnityEngine;

/// <summary>
/// 플레이어에게 배정되는 태스크 하나를 나타내는 데이터 클래스.
/// </summary>
[System.Serializable]
public class TaskCard
{
    public enum TaskType
    {
        // ── 기본 타입 ──────────────────────────────────────────────────
        WinSpecificCard,    // 특정 카드가 포함된 트릭을 이겨야 함
        WinTrickCount,      // 정확히 N번의 트릭을 이겨야 함
        WinFirst,           // 첫 번째 트릭을 이겨야 함
        WinNone,            // 트릭을 하나도 이기면 안 됨
        WinLast,            // 마지막 트릭을 이겨야 함
        WinConsecutive,     // N트릭을 연속으로 이겨야 함
        WinNoConsecutive,   // 2트릭 연속으로 이기면 안 됨
        WinOnlyFirst,       // 첫 트릭만 이겨야 함 (나머지 이기면 안 됨)
        WinOnlyLast,        // 마지막 트릭만 이겨야 함 (나머지 이기면 안 됨)
        WinFirstAndLast,    // 첫 트릭 + 마지막 트릭 둘 다 이겨야 함

        // ── 슈트 관련 ──────────────────────────────────────────────────
        WinNoSuit,          // 특정 슈트가 리드된 트릭을 이기면 안 됨
        WinNoOpenSuit,      // 특정 슈트로 트릭을 시작(리드)하면 안 됨
        WinMoreSuitThan,    // targetSuit 카드 수 > suitB 카드 수 (핑크>초록 등)
        WinExactSuitCount,  // targetSuit 카드를 정확히 N장 이겨야 함
        WinEachColor,       // 4가지 슈트(잠수함 제외) 각각 최소 1장 이겨야 함

        // ── 트릭 수 관련 ───────────────────────────────────────────────
        WinAtLeast,         // 적어도 N회 트릭 획득
        WinNoneFirstN,      // 처음 N개 트릭에서 이기면 안 됨

        // ── 카드 값 관련 ───────────────────────────────────────────────
        WinOddTrick,        // 트릭의 모든 카드 값이 홀수인 트릭 이기기
        WinEvenTrick,       // 트릭의 모든 카드 값이 짝수인 트릭 이기기

        // ── 상대 비교 ──────────────────────────────────────────────────
        WinRelativeFewer,   // 다른 모든 플레이어보다 트릭을 적게 이겨야 함
        WinRelativeMore,    // 다른 모든 플레이어보다 트릭을 많이 이겨야 함
    }

    public TaskType type;

    // WinSpecificCard 전용
    public Card targetCard;

    // WinTrickCount / WinAtLeast / WinNoneFirstN / WinExactSuitCount 전용
    public int requiredCount;

    // WinConsecutive 전용
    public int requiredConsecutive = 2;

    // 슈트 관련 타입 전용 (WinNoSuit, WinNoOpenSuit, WinMoreSuitThan, WinExactSuitCount)
    public Card.Suit targetSuit = Card.Suit.Yellow;

    // WinMoreSuitThan 전용 (targetSuit > suitB 여야 성공)
    public Card.Suit suitB = Card.Suit.Blue;

    // 순서 토큰 (0=제약 없음, 1·2·3…=낮은 번호 순서대로 달성)
    public int orderIndex = 0;

    // 런타임 상태
    public CrewAgent assignedTo;
    public bool isCompleted;
    public bool isFailed;

    // ---------------------------------------------------------------
    // 생성 헬퍼
    // ---------------------------------------------------------------
    public static TaskCard SpecificCard(Card card)
        => new TaskCard { type = TaskType.WinSpecificCard, targetCard = card };

    public static TaskCard TrickCount(int count)
        => new TaskCard { type = TaskType.WinTrickCount, requiredCount = count };

    public static TaskCard First()   => new TaskCard { type = TaskType.WinFirst };
    public static TaskCard None()    => new TaskCard { type = TaskType.WinNone };
    public static TaskCard Last()    => new TaskCard { type = TaskType.WinLast };

    public static TaskCard Consecutive(int n)
        => new TaskCard { type = TaskType.WinConsecutive, requiredConsecutive = n };

    public static TaskCard NoConsecutive()  => new TaskCard { type = TaskType.WinNoConsecutive };
    public static TaskCard OnlyFirst()      => new TaskCard { type = TaskType.WinOnlyFirst };
    public static TaskCard OnlyLast()       => new TaskCard { type = TaskType.WinOnlyLast };
    public static TaskCard FirstAndLast()   => new TaskCard { type = TaskType.WinFirstAndLast };

    public static TaskCard NoSuit(Card.Suit suit)
        => new TaskCard { type = TaskType.WinNoSuit, targetSuit = suit };

    public static TaskCard NoOpenSuit(Card.Suit suit)
        => new TaskCard { type = TaskType.WinNoOpenSuit, targetSuit = suit };

    public static TaskCard MoreSuitThan(Card.Suit a, Card.Suit b)
        => new TaskCard { type = TaskType.WinMoreSuitThan, targetSuit = a, suitB = b };

    public static TaskCard ExactSuitCount(Card.Suit suit, int count)
        => new TaskCard { type = TaskType.WinExactSuitCount, targetSuit = suit, requiredCount = count };

    public static TaskCard EachColor()      => new TaskCard { type = TaskType.WinEachColor };

    public static TaskCard AtLeast(int n)
        => new TaskCard { type = TaskType.WinAtLeast, requiredCount = n };

    public static TaskCard NoneFirstN(int n)
        => new TaskCard { type = TaskType.WinNoneFirstN, requiredCount = n };

    public static TaskCard OddTrick()   => new TaskCard { type = TaskType.WinOddTrick };
    public static TaskCard EvenTrick()  => new TaskCard { type = TaskType.WinEvenTrick };
    public static TaskCard RelativeFewer() => new TaskCard { type = TaskType.WinRelativeFewer };
    public static TaskCard RelativeMore()  => new TaskCard { type = TaskType.WinRelativeMore };

    // ---------------------------------------------------------------
    // 표시 텍스트
    // ---------------------------------------------------------------
    public override string ToString()
    {
        string o = orderIndex > 0 ? $"[{orderIndex}] " : "";
        return type switch
        {
            TaskType.WinSpecificCard   => $"{o}{targetCard.suit} {targetCard.value} 트릭 획득",
            TaskType.WinTrickCount     => $"{o}트릭 정확히 {requiredCount}회 획득",
            TaskType.WinFirst          => $"{o}첫 트릭 획득",
            TaskType.WinNone           => $"{o}트릭 0회 획득",
            TaskType.WinLast           => $"{o}마지막 트릭 획득",
            TaskType.WinConsecutive    => $"{o}{requiredConsecutive}트릭 연속 획득",
            TaskType.WinNoConsecutive  => $"{o}2트릭 연속 획득 금지",
            TaskType.WinOnlyFirst      => $"{o}첫 트릭만 획득",
            TaskType.WinOnlyLast       => $"{o}마지막 트릭만 획득",
            TaskType.WinFirstAndLast   => $"{o}첫+마지막 트릭 획득",
            TaskType.WinNoSuit         => $"{o}{targetSuit} 리드 트릭 획득 금지",
            TaskType.WinNoOpenSuit     => $"{o}{targetSuit}로 트릭 시작 금지",
            TaskType.WinMoreSuitThan   => $"{o}{targetSuit} 카드 수 > {suitB} 카드 수",
            TaskType.WinExactSuitCount => $"{o}{targetSuit} 카드 정확히 {requiredCount}장 획득",
            TaskType.WinEachColor      => $"{o}4가지 색 각 1장 이상 획득",
            TaskType.WinAtLeast        => $"{o}트릭 적어도 {requiredCount}회 획득",
            TaskType.WinNoneFirstN     => $"{o}첫 {requiredCount}트릭 획득 금지",
            TaskType.WinOddTrick       => $"{o}홀수 카드만 있는 트릭 획득",
            TaskType.WinEvenTrick      => $"{o}짝수 카드만 있는 트릭 획득",
            TaskType.WinRelativeFewer  => $"{o}모든 상대보다 트릭 적게",
            TaskType.WinRelativeMore   => $"{o}모든 상대보다 트릭 많게",
            _                          => "알 수 없는 태스크"
        };
    }
}
