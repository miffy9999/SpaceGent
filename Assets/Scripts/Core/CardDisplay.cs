using UnityEngine;
using TMPro;
public class CardDisplay : MonoBehaviour
{
    public SpriteRenderer bgRenderer; // 카드의 배경색을 바꿀 렌더러
    public TextMesh valueText; // 2D 텍스트 (카드의 숫자)

    // 카드의 데이터(색상, 숫자)를 받아와서 그래픽으로 표현하는 함수
    public void Setup(Card cardData)
    {
        // 1. 숫자에 맞게 텍스트 변경
        valueText.text = cardData.value.ToString();

        // 2. 수트(색상)에 맞게 배경색 변경
        switch (cardData.suit)
        {
            case Card.Suit.Yellow: bgRenderer.color = Color.yellow; break;
            case Card.Suit.Blue: bgRenderer.color = Color.blue; break;
            case Card.Suit.White: bgRenderer.color = Color.white; break;
            case Card.Suit.Pink: bgRenderer.color = new Color(1f, 0.4f, 0.7f); break; // 핑크색
            case Card.Suit.Submarine:
                bgRenderer.color = Color.black;
                valueText.color = Color.white; // 검은 배경엔 흰 글씨
                break;
        }
    }
}