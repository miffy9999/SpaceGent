using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 태스크 카드 → 스프라이트 매핑 ScriptableObject.
///
/// 스페이스 크루 태스크는 WinSpecificCard 단일 종류이므로, 태스크 스프라이트는
/// 대상 카드(targetCard)의 카드 스프라이트를 그대로 사용한다(CardSpriteMapping).
/// 카드 스프라이트가 없으면 winSpecificFrame 폴백.
///
/// ── 인스펙터 설정 ───────────────────────────────────────────────────
/// 1. Project 창 우클릭 → Create → SpaceCrew → Task Sprite Mapping
/// 2. GameManager.taskSpriteMapping 슬롯에 이 에셋 연결
/// ────────────────────────────────────────────────────────────────────
/// </summary>
[CreateAssetMenu(fileName = "TaskSpriteMapping", menuName = "SpaceCrew/Task Sprite Mapping")]
public class TaskSpriteMapping : ScriptableObject
{
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

    // ── 태스크에 맞는 스프라이트 반환 (대상 카드 스프라이트) ──────────
    public Sprite GetTaskSprite(TaskCard task)
    {
        if (task == null) return null;
        var cardMapping = GameManager.Instance?.cardSpriteMapping;
        Sprite cardSprite = cardMapping?.Get(task.targetCard);
        return cardSprite != null ? cardSprite : winSpecificFrame;
    }
}
