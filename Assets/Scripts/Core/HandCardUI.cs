using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 하단 UI 손패 카드 한 장.
/// HandCardParent 아래에 자동 생성되며, 클릭 시 해당 카드를 선택한다.
/// 스프라이트 매핑이 있으면 카드 이미지를, 없으면 색상+텍스트 폴백을 사용한다.
/// </summary>
[RequireComponent(typeof(Button))]
public class HandCardUI : MonoBehaviour
{
    [HideInInspector] public Image      background;
    [HideInInspector] public Image      cardFaceImage;
    [HideInInspector] public Image      selectedBorder;
    [HideInInspector] public TMP_Text   valueText;
    [HideInInspector] public TMP_Text   suitText;

    private int       cardIndex;
    private CrewAgent owner;

    // ── 초기화 ──────────────────────────────────────────────────────
    public void Setup(Card card, int index, CrewAgent agent)
    {
        cardIndex = index;
        owner     = agent;

        // 스프라이트 매핑 조회
        var mapping = GameManager.Instance != null ? GameManager.Instance.cardSpriteMapping : null;
        Sprite sprite = mapping != null ? mapping.Get(card) : null;

        if (sprite != null && cardFaceImage != null)
        {
            // 스프라이트 모드: 카드 이미지 표시, 텍스트 숨김
            cardFaceImage.sprite = sprite;
            cardFaceImage.color  = Color.white;
            cardFaceImage.gameObject.SetActive(true);
            if (background != null) background.color = Color.white; // 카드 테두리용 흰 배경
            if (valueText  != null) valueText.gameObject.SetActive(false);
            if (suitText   != null) suitText.gameObject.SetActive(false);
        }
        else
        {
            // 폴백 모드: 색상 배경 + 텍스트
            if (cardFaceImage != null) cardFaceImage.gameObject.SetActive(false);
            if (background    != null) background.color = SuitColor(card.suit);
            if (valueText     != null) { valueText.text = card.value.ToString(); valueText.gameObject.SetActive(true); }
            if (suitText      != null) { suitText.text  = SuitLabel(card.suit);  suitText.gameObject.SetActive(true); }
        }

        if (selectedBorder != null) selectedBorder.gameObject.SetActive(false);

        var btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnClick);
    }

    // ── 선택 표시 ────────────────────────────────────────────────────
    public void SetSelected(bool selected)
    {
        if (selectedBorder != null)
            selectedBorder.gameObject.SetActive(selected);
    }

    // ── 유효/무효 표시 (follow-suit 위반 카드 반투명 처리) ────────────
    public void SetPlayable(bool playable)
    {
        float a = playable ? 1f : 0.35f;
        if (background    != null) { var c = background.color;    background.color    = new Color(c.r, c.g, c.b, a); }
        if (cardFaceImage != null) { var c = cardFaceImage.color; cardFaceImage.color = new Color(c.r, c.g, c.b, a); }
        if (valueText     != null) { var c = valueText.color;     valueText.color     = new Color(c.r, c.g, c.b, a); }
        if (suitText      != null) { var c = suitText.color;      suitText.color      = new Color(c.r, c.g, c.b, a); }
        var btn = GetComponent<Button>();
        if (btn != null) btn.interactable = playable;
    }

    // ── 클릭 처리 ────────────────────────────────────────────────────
    void OnClick()
    {
        if (owner == null || !owner.isMyTurn) return;
        owner.SelectCard(cardIndex);
    }

    // ── 폴백 색상 / 라벨 ─────────────────────────────────────────────
    static Color SuitColor(Card.Suit suit)
    {
        switch (suit)
        {
            case Card.Suit.Yellow:    return new Color(1.00f, 0.80f, 0.10f);
            case Card.Suit.Blue:      return new Color(0.10f, 0.50f, 0.90f);
            case Card.Suit.Green:     return new Color(0.10f, 0.68f, 0.30f);
            case Card.Suit.Pink:      return new Color(0.88f, 0.18f, 0.52f);
            case Card.Suit.Rocket: return new Color(0.28f, 0.28f, 0.33f);
            default:                  return Color.white;
        }
    }

    static string SuitLabel(Card.Suit suit)
    {
        switch (suit)
        {
            case Card.Suit.Yellow:    return "Y";
            case Card.Suit.Blue:      return "B";
            case Card.Suit.Green:     return "G";
            case Card.Suit.Pink:      return "P";
            case Card.Suit.Rocket: return "SUB";
            default:                  return "?";
        }
    }
}
