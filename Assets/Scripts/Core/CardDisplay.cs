using UnityEngine;
using TMPro;

/// <summary>
/// 카드 한 장의 시각을 담당한다.
/// - CardSpriteMapping이 연결되어 있으면 스프라이트를 사용
/// - 없으면 색상 블록 + 숫자 텍스트로 폴백
/// </summary>
public class CardDisplay : MonoBehaviour
{
    [Header("렌더러")]
    public SpriteRenderer bgRenderer;      // 카드 배경 (색상 or 스프라이트)
    public SpriteRenderer faceRenderer;    // 카드 앞면 스프라이트 (옵션)

    [Header("텍스트")]
    public TextMeshPro valueText;          // 카드 숫자

    [Header("스프라이트 매핑 (옵션)")]
    public CardSpriteMapping spriteMapping;

    // 앞/뒤 표시 상태
    private bool _isFaceUp = true;

    // ---------------------------------------------------------------
    // 초기 세팅
    // ---------------------------------------------------------------
    public void Setup(Card cardData, bool faceUp = true)
    {
        _isFaceUp = faceUp;

        if (faceUp)
            ShowFace(cardData);
        else
            ShowBack();
    }

    // ---------------------------------------------------------------
    // 앞면 표시
    // ---------------------------------------------------------------
    private void ShowFace(Card cardData)
    {
        // 숫자 텍스트
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
                if (faceRenderer != null)
                {
                    faceRenderer.sprite  = s;
                    faceRenderer.enabled = true;
                }
                // 스프라이트를 쓰면 색상 블록은 흰색으로 초기화
                if (bgRenderer != null)
                    bgRenderer.color = Color.white;
                return;
            }
        }

        // 폴백: 색상 블록 + 숫자
        ApplyFallbackColor(cardData);
    }

    // ---------------------------------------------------------------
    // 뒷면 표시
    // ---------------------------------------------------------------
    private void ShowBack()
    {
        if (valueText != null)
            valueText.gameObject.SetActive(false);

        if (faceRenderer != null)
            faceRenderer.enabled = false;

        if (spriteMapping != null && spriteMapping.cardBack != null)
        {
            if (bgRenderer != null)
            {
                bgRenderer.sprite = spriteMapping.cardBack;
                bgRenderer.color  = Color.white;
            }
        }
        else
        {
            // 폴백: 어두운 회색 블록
            if (bgRenderer != null)
                bgRenderer.color = new Color(0.2f, 0.2f, 0.2f);
        }
    }

    // ---------------------------------------------------------------
    // 앞/뒤 전환 (트릭 결과 공개 연출 등에 활용)
    // ---------------------------------------------------------------
    public void Flip(Card cardData)
    {
        _isFaceUp = !_isFaceUp;
        if (_isFaceUp) ShowFace(cardData);
        else ShowBack();
    }

    // ---------------------------------------------------------------
    // 폴백: 수트별 색상 블록
    // ---------------------------------------------------------------
    private void ApplyFallbackColor(Card cardData)
    {
        if (bgRenderer == null) return;

        if (faceRenderer != null) faceRenderer.enabled = false;

        switch (cardData.suit)
        {
            case Card.Suit.Yellow:
                bgRenderer.color = Color.yellow;
                break;
            case Card.Suit.Blue:
                bgRenderer.color = Color.blue;
                if (valueText != null) valueText.color = Color.white;
                break;
            case Card.Suit.White:
                bgRenderer.color = Color.white;
                break;
            case Card.Suit.Pink:
                bgRenderer.color = new Color(1f, 0.4f, 0.7f);
                break;
            case Card.Suit.Submarine:
                bgRenderer.color = Color.black;
                if (valueText != null) valueText.color = Color.white;
                break;
        }
    }
}
