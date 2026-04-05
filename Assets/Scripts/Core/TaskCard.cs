using UnityEngine;

/// <summary>
/// 플레이어에게 배정되는 태스크 하나를 나타내는 데이터 클래스.
/// </summary>
[System.Serializable]
public class TaskCard
{
    public enum TaskType
    {
        WinSpecificCard,  // 특정 카드가 포함된 트릭을 이겨야 함
        WinTrickCount,    // N번의 트릭을 이겨야 함
        WinFirst,         // 첫 번째 트릭을 이겨야 함
        WinNone,          // 트릭을 하나도 이기면 안 됨
    }

    public TaskType type;

    // WinSpecificCard 전용
    public Card targetCard;

    // WinTrickCount 전용
    public int requiredCount;

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

    public static TaskCard First()
        => new TaskCard { type = TaskType.WinFirst };

    public static TaskCard None()
        => new TaskCard { type = TaskType.WinNone };

    public override string ToString()
    {
        return type switch
        {
            TaskType.WinSpecificCard => $"{targetCard.suit} {targetCard.value} 트릭 획득",
            TaskType.WinTrickCount   => $"트릭 {requiredCount}회 획득",
            TaskType.WinFirst        => "첫 트릭 획득",
            TaskType.WinNone         => "트릭 0회 획득",
            _ => "알 수 없는 태스크"
        };
    }
}
