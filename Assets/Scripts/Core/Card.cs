[System.Serializable]
public class Card
{
    public enum Suit { Yellow, Blue, White, Pink, Submarine } // 색상 4종 + 잠수함(조커)
    public Suit suit;
    public int value; // 1~9 (잠수함은 1~4)

    public Card(Suit s, int v)
    {
        suit = s;
        value = v;
    }
}