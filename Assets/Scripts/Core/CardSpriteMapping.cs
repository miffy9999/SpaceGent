using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카드 데이터(Suit + Value) → Sprite를 매핑하는 ScriptableObject.
/// Project 창에서 우클릭 → Create → SeaAI → Card Sprite Mapping 으로 생성.
/// 생성 후 인스펙터에서 슬라이싱된 스프라이트를 드래그해서 채운다.
/// </summary>
[CreateAssetMenu(fileName = "CardSpriteMapping", menuName = "SeaAI/Card Sprite Mapping")]
public class CardSpriteMapping : ScriptableObject
{
    [System.Serializable]
    public class CardSpriteEntry
    {
        public Card.Suit suit;
        [Range(1, 9)] public int value;
        public Sprite sprite;
    }

    public List<CardSpriteEntry> entries = new List<CardSpriteEntry>();

    // 카드 뒷면 공용 스프라이트
    public Sprite cardBack;

    // ---------------------------------------------------------------
    // 빠른 조회를 위한 Dictionary (런타임 빌드)
    // ---------------------------------------------------------------
    private Dictionary<(Card.Suit, int), Sprite> _lookup;

    public Sprite Get(Card card)
    {
        if (_lookup == null) BuildLookup();
        _lookup.TryGetValue((card.suit, card.value), out Sprite s);
        return s;
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<(Card.Suit, int), Sprite>();
        foreach (var e in entries)
        {
            var key = (e.suit, e.value);
            if (!_lookup.ContainsKey(key))
                _lookup[key] = e.sprite;
        }
    }

    // 에디터에서 항목을 수정하면 캐시 무효화
    private void OnValidate() => _lookup = null;

    /// <summary>
    /// 인스펙터 세팅 전 자동으로 40장 슬롯을 채워주는 헬퍼.
    /// Editor 메뉴: Tools > Populate Card Sprite Mapping
    /// </summary>
    public void PopulateSlots()
    {
        entries.Clear();
        for (int s = 0; s < 4; s++)
        {
            for (int v = 1; v <= 9; v++)
            {
                entries.Add(new CardSpriteEntry
                {
                    suit  = (Card.Suit)s,
                    value = v,
                    sprite = null   // 인스펙터에서 직접 드래그
                });
            }
        }
        // 잠수함 1~4
        for (int v = 1; v <= 4; v++)
        {
            entries.Add(new CardSpriteEntry
            {
                suit  = Card.Suit.Submarine,
                value = v,
                sprite = null
            });
        }
    }
}
