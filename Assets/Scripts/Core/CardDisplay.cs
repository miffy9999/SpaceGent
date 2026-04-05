using UnityEngine;

/// <summary>
/// 카드 한 장의 시각을 담당한다.
/// - CardSpriteMapping이 연결되어 있으면 스프라이트를 사용
/// - 없으면 런타임 단색 스프라이트 + 숫자 텍스트로 폴백
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CardDisplay : MonoBehaviour
{
    [Header("렌더러")]
    public SpriteRenderer bgRenderer;      // 카드 배경
    public SpriteRenderer faceRenderer;    // 카드 앞면 스프라이트 (옵션)

    [Header("텍스트")]
    public TextMesh valueText;             // 카드 숫자

    [Header("스프라이트 매핑 (옵션)")]
    public CardSpriteMapping spriteMapping;

    [Header("카드 크기")]
    [Range(0.1f, 3f)] public float cardScale = 1f;

    // 인스펙터 미연결 대비: 컴포넌트 자동 탐색
    void Awake()
    {
        if (bgRenderer == null)
            bgRenderer = GetComponent<SpriteRenderer>();

        if (valueText == null)
            valueText = GetComponentInChildren<TextMesh>();

        // bgRenderer에 스프라이트가 없으면 단색 표시용 흰 픽셀 스프라이트 생성
        if (bgRenderer != null && bgRenderer.sprite == null)
            bgRenderer.sprite = MakeWhiteSprite();

        transform.localScale = Vector3.one * cardScale;
    }

    // ---------------------------------------------------------------
    // 초기 세팅
    // ---------------------------------------------------------------
    public void Setup(Card cardData, bool faceUp = true)
    {
        if (faceUp) ShowFace(cardData);
        else        ShowBack();
    }

    // ---------------------------------------------------------------
    // 앞면 표시
    // ---------------------------------------------------------------
    private void ShowFace(Card cardData)
    {
        if (valueText != null)
        {
            valueText.gameObject.SetActive(true);
            valueText.text  = cardData.value.ToString();
            valueText.color = Color.black;
        }

        // 스프라이트 매핑이 있으면 스프라이트 사용
        if (spriteMapping != null)
        {
            Sprite s = spriteMapping.Get(cardData);
            if (s != null)
            {
                // faceRenderer가 있으면 거기에, 없으면 bgRenderer에 직접 표시
                if (faceRenderer != null)
                {
                    faceRenderer.sprite  = s;
                    faceRenderer.enabled = true;
                    if (bgRenderer != null) bgRenderer.color = Color.white;
                }
                else if (bgRenderer != null)
                {
                    bgRenderer.sprite = s;
                    bgRenderer.color  = Color.white;
                    if (valueText != null) valueText.gameObject.SetActive(false);
                }
                return;
            }
        }

        ApplyFallbackColor(cardData);
    }

    // ---------------------------------------------------------------
    // 뒷면 표시
    // ---------------------------------------------------------------
    private void ShowBack()
    {
        if (valueText != null) valueText.gameObject.SetActive(false);
        if (faceRenderer != null) faceRenderer.enabled = false;

        if (spriteMapping != null && spriteMapping.cardBack != null)
        {
            if (bgRenderer != null) { bgRenderer.sprite = spriteMapping.cardBack; bgRenderer.color = Color.white; }
        }
        else
        {
            if (bgRenderer != null) bgRenderer.color = new Color(0.2f, 0.2f, 0.2f);
        }
    }

    // ---------------------------------------------------------------
    // 앞/뒤 전환
    // ---------------------------------------------------------------
    public void Flip(Card cardData)
    {
        bool next = !(valueText != null && valueText.gameObject.activeSelf);
        if (next) ShowFace(cardData);
        else      ShowBack();
    }

    // ---------------------------------------------------------------
    // 폴백: 수트별 색상 블록
    // ---------------------------------------------------------------
    private void ApplyFallbackColor(Card cardData)
    {
        if (bgRenderer == null) return;
        if (faceRenderer != null) faceRenderer.enabled = false;

        Color textColor = Color.black;
        Color bgColor;

        switch (cardData.suit)
        {
            case Card.Suit.Yellow:    bgColor = Color.yellow;                  break;
            case Card.Suit.Blue:      bgColor = Color.blue;   textColor = Color.white; break;
            case Card.Suit.Green:     bgColor = Color.green;                   break;
            case Card.Suit.Pink:      bgColor = new(1f, 0.4f, 0.7f);          break;
            case Card.Suit.Submarine: bgColor = Color.black;  textColor = Color.white; break;
            default:                  bgColor = Color.gray;                    break;
        }

        bgRenderer.color = bgColor;
        if (valueText != null) valueText.color = textColor;
    }

    // ---------------------------------------------------------------
    // 1×1 흰색 픽셀 스프라이트 생성 (bgRenderer 색상 tint용)
    // ---------------------------------------------------------------
    private static Sprite MakeWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
    }
}
