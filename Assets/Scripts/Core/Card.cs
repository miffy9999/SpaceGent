[System.Serializable]
public class Card
{
    public enum Suit { Yellow, Blue, Green, Pink, Rocket } // 색상 4종 + 로켓(트럼프)
    public Suit suit;
    public int value; // 1~9 (로켓은 1~4)

    public Card(Suit s, int v)
    {
        suit = s;
        value = v;
    }

    // Contains(), Equals() 등이 값 기반으로 동작하도록 오버라이드
    public override bool Equals(object obj)
    {
        if (obj is Card other)
            return suit == other.suit && value == other.value;
        return false;
    }

    public override int GetHashCode() => System.HashCode.Combine(suit, value);

    public override string ToString() => $"{suit} {value}";
}
