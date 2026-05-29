using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 태스크 카드 → 스프라이트 매핑 ScriptableObject.
///
/// ── 인스펙터 설정 방법 ──────────────────────────────────────────────
/// 1. Project 창 우클릭 → Create → SpaceCrew → Task Sprite Mapping
/// 2. Task Entries 리스트에 항목 추가:
///      Type = WinFirst  → Sprite = Task_WinFirst2
///      Type = WinNone   → Sprite = Task_WinNoneFirst4
///      Type = WinTrickCount, Count = 1 → Sprite = Task_Sheet_XX
///      Type = WinTrickCount, Count = 2 → Sprite = Task_Sheet_YY
///      ...
///    (WinSpecificCard는 CardSpriteMapping에서 자동으로 카드 스프라이트를 가져옴)
/// 3. GameManager.taskSpriteMapping 슬롯에 이 에셋 연결
/// ────────────────────────────────────────────────────────────────────
/// </summary>
[CreateAssetMenu(fileName = "TaskSpriteMapping", menuName = "SpaceCrew/Task Sprite Mapping")]
public class TaskSpriteMapping : ScriptableObject
{
    [System.Serializable]
    public struct TaskSpriteEntry
    {
        public TaskCard.TaskType type;

        [Tooltip("WinTrickCount / WinAtLeast / WinNoneFirstN / WinConsecutive / WinExactSuitCount 용.\n" +
                 "해당 수치와 일치하면 사용. 0이면 모든 값에 대한 폴백.")]
        public int count;

        [Tooltip("WinNoSuit / WinNoOpenSuit / WinMoreSuitThan / WinExactSuitCount 용 주 슈트.\n" +
                 "슈트 무관 타입은 Yellow(0)으로 두면 됨.")]
        public Card.Suit suit;

        [Tooltip("WinMoreSuitThan 용 비교 슈트 (suit > suitB 여야 성공).\n" +
                 "나머지 타입에서는 무시됨.")]
        public Card.Suit suitB;

        public Sprite sprite;
    }

    [Header("태스크별 스프라이트 (리스트에 자유롭게 추가)")]
    public List<TaskSpriteEntry> entries = new List<TaskSpriteEntry>();

    [Header("WinSpecificCard 폴백 (카드 스프라이트 없을 때)")]
    public Sprite winSpecificFrame;

    [Header("토큰 스프라이트")]
    public Sprite commTokenActive;   // Token_Comm_Active.png
    public Sprite commTokenUsed;     // Token_Comm_Used.png
    [FormerlySerializedAs("sonarToken")]
    public Sprite distressSignalToken;  // Token_DistressSignal.png (조난신호)
    public Sprite commanderToken;    // Token_Commander.png

    [Header("순서 토큰 스프라이트 (태스크 완수 순서 조건)")]
    public Sprite tokenNumber1;   // Token_Number_1.png
    public Sprite tokenNumber2;   // Token_Number_2.png
    public Sprite tokenNumber3;   // Token_Number_3.png
    public Sprite tokenNumber4;   // Token_Number_4.png
    public Sprite tokenNumber5;   // Token_Number_5.png
    public Sprite tokenOmega;     // Token_Omega.png  (Ω, 마지막)
    public Sprite tokenArrow1;    // Token_Arrow_1.png  (→ before)
    public Sprite tokenArrow2;    // Token_Arrow_2.png  (→→ after)
    public Sprite tokenArrow3;    // Token_Arrow_3.png  (→→→)
    public Sprite tokenArrow4;    // Token_Arrow_4.png  (→→→→)

    // ── 순서 토큰 스프라이트 반환 ────────────────────────────────────
    public Sprite GetOrderTokenSprite(OrderToken token) => token switch
    {
        OrderToken.N1     => tokenNumber1,
        OrderToken.N2     => tokenNumber2,
        OrderToken.N3     => tokenNumber3,
        OrderToken.N4     => tokenNumber4,
        OrderToken.N5     => tokenNumber5,
        OrderToken.Omega  => tokenOmega,
        OrderToken.Arrow1 => tokenArrow1,
        OrderToken.Arrow2 => tokenArrow2,
        OrderToken.Arrow3 => tokenArrow3,
        OrderToken.Arrow4 => tokenArrow4,
        _                 => null
    };

    // ── 태스크에 맞는 스프라이트 반환 ────────────────────────────────
    public Sprite GetTaskSprite(TaskCard task)
    {
        if (task == null) return null;

        if (task.type == TaskCard.TaskType.WinSpecificCard)
        {
            // 카드 스프라이트 우선, 없으면 폴백 프레임
            var cardMapping = GameManager.Instance?.cardSpriteMapping;
            Sprite cardSprite = cardMapping?.Get(task.targetCard);
            return cardSprite != null ? cardSprite : winSpecificFrame;
        }

        // entries 리스트에서 일치하는 항목 탐색
        Sprite fallback = null;
        foreach (var e in entries)
        {
            if (e.type != task.type) continue;

            switch (task.type)
            {
                // count 기반 타입: 정확히 일치 우선, count=0 폴백
                case TaskCard.TaskType.WinTrickCount:
                case TaskCard.TaskType.WinAtLeast:
                case TaskCard.TaskType.WinNoneFirstN:
                    if (e.count == task.requiredCount) return e.sprite;
                    if (e.count == 0) fallback = e.sprite;
                    break;

                case TaskCard.TaskType.WinConsecutive:
                    if (e.count == task.requiredConsecutive) return e.sprite;
                    if (e.count == 0) fallback = e.sprite;
                    break;

                // suit 기반 타입: suit 일치 우선, suit=Yellow(0) 폴백
                case TaskCard.TaskType.WinNoSuit:
                case TaskCard.TaskType.WinNoOpenSuit:
                    if (e.suit == task.targetSuit) return e.sprite;
                    if (e.suit == Card.Suit.Yellow) fallback = e.sprite;
                    break;

                case TaskCard.TaskType.WinMoreSuitThan:
                    if (e.suit == task.targetSuit && e.suitB == task.suitB) return e.sprite;
                    if (e.count == 0) fallback = e.sprite;
                    break;

                case TaskCard.TaskType.WinExactSuitCount:
                    if (e.suit == task.targetSuit && e.count == task.requiredCount) return e.sprite;
                    if (e.suit == task.targetSuit && e.count == 0) fallback = e.sprite;
                    break;

                default:
                    // 나머지 타입은 첫 번째 매치 반환
                    return e.sprite;
            }
        }

        return fallback;
    }
}
