using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  Determinizer — PIMC 핵심: 미플레이 카드를 다른 플레이어 손에 분배.
// ---------------------------------------------------------------------
//  알려진 제약:
//    - self의 손패 (자기 자신은 정확)
//    - 핸드 누적 played cards (전체에서 제거)
//    - 현재 트릭의 cardsOnTable (전체에서 제거)
//    - 각 플레이어의 knownVoids (특정 suit를 가질 수 없음)
//    - 각 플레이어의 손패 크기 (현재 시점 기준)
//
//  알고리즘: void 제약을 우선으로 만족하면서 unplayed 카드를 분배.
//    1. unplayed pool 구성 (40장 - 알려진 카드)
//    2. 각 unplayed 카드의 "가능한 owner 집합" 계산
//    3. constrained sampling (제약 위반 시 재시도, 최대 N회)
// =====================================================================
public static class Determinizer
{
    private const int MaxRetries = 100;

    // ---------------------------------------------------------------
    // Sample: self의 시점에서 가능한 손패 분포 1개 생성.
    //   knownVoids[i] = null이면 빈 집합으로 취급.
    //   handSizes[i] = 분배 시점에 player i가 가져야 할 카드 수.
    //   selfIdx = self 플레이어 (이미 손패 정확함).
    // ---------------------------------------------------------------
    public static List<Card>[] Sample(
        List<Card> selfHand,
        int selfIdx,
        HashSet<Card> playedCards,
        List<Card> tableCards,
        HashSet<Card.Suit>[] knownVoids,
        int[] handSizes,
        System.Random rng)
    {
        int playerCount = handSizes.Length;
        var hands = new List<Card>[playerCount];
        for (int i = 0; i < playerCount; i++) hands[i] = new List<Card>();

        // self는 정확함
        hands[selfIdx] = new List<Card>(selfHand);

        // unplayed pool 구성
        var unplayed = BuildUnplayedPool(selfHand, playedCards, tableCards);

        // 분배 시도
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            if (TryDistribute(unplayed, hands, selfIdx, handSizes, knownVoids, rng))
                return hands;

            // 실패: hands 리셋 (self만 유지) 후 재시도
            for (int i = 0; i < playerCount; i++)
                if (i != selfIdx) hands[i].Clear();
        }

        // MaxRetries 초과 시: void 제약 완화하고 1번 더 시도 (fail-soft)
        Debug.LogWarning("[Determinizer] void 제약 충족 분배 실패 — void 무시하고 분배");
        for (int i = 0; i < playerCount; i++)
            if (i != selfIdx) hands[i].Clear();
        DistributeIgnoringVoids(unplayed, hands, selfIdx, handSizes, rng);
        return hands;
    }

    // ---------------------------------------------------------------
    // unplayed = 40장 - self 손 - 이미 plyed - 현재 테이블
    // ---------------------------------------------------------------
    private static List<Card> BuildUnplayedPool(
        List<Card> selfHand, HashSet<Card> playedCards, List<Card> tableCards)
    {
        var taken = new HashSet<Card>();
        foreach (var c in selfHand) taken.Add(c);
        if (playedCards != null) foreach (var c in playedCards) taken.Add(c);
        if (tableCards  != null) foreach (var c in tableCards)  taken.Add(c);

        var unplayed = new List<Card>(40);
        for (int s = 0; s < 4; s++)
            for (int v = 1; v <= 9; v++)
            {
                var card = new Card((Card.Suit)s, v);
                if (!taken.Contains(card)) unplayed.Add(card);
            }
        for (int v = 1; v <= 4; v++)
        {
            var card = new Card(Card.Suit.Rocket, v);
            if (!taken.Contains(card)) unplayed.Add(card);
        }
        return unplayed;
    }

    // ---------------------------------------------------------------
    // unplayed를 self를 제외한 플레이어에게 분배 (void 제약 준수).
    //   카드별로 "가능한 owner" 중 하나에 무작위 할당.
    //   하나라도 할당 불가 (가능 owner 모두 이미 full) → 실패 반환.
    // ---------------------------------------------------------------
    private static bool TryDistribute(
        List<Card> unplayed, List<Card>[] hands,
        int selfIdx, int[] handSizes, HashSet<Card.Suit>[] knownVoids,
        System.Random rng)
    {
        // 순서 shuffle
        var pool = new List<Card>(unplayed);
        Shuffle(pool, rng);

        foreach (var card in pool)
        {
            var candidates = new List<int>();
            for (int p = 0; p < hands.Length; p++)
            {
                if (p == selfIdx) continue;
                if (hands[p].Count >= handSizes[p]) continue;     // 이미 full
                if (knownVoids != null && knownVoids[p] != null
                    && knownVoids[p].Contains(card.suit)) continue; // void
                candidates.Add(p);
            }
            if (candidates.Count == 0) return false;
            int chosen = candidates[rng.Next(candidates.Count)];
            hands[chosen].Add(card);
        }

        // 모든 플레이어 손패 크기 정확한지 검증
        for (int p = 0; p < hands.Length; p++)
            if (hands[p].Count != handSizes[p]) return false;
        return true;
    }

    // ---------------------------------------------------------------
    // 폴백: void 제약 무시하고 round-robin 분배.
    // ---------------------------------------------------------------
    private static void DistributeIgnoringVoids(
        List<Card> unplayed, List<Card>[] hands,
        int selfIdx, int[] handSizes, System.Random rng)
    {
        var pool = new List<Card>(unplayed);
        Shuffle(pool, rng);

        int idx = 0;
        foreach (var card in pool)
        {
            // 다음 자리 찾기
            int tries = 0;
            while (tries < hands.Length)
            {
                int p = idx % hands.Length;
                idx++;
                if (p != selfIdx && hands[p].Count < handSizes[p])
                {
                    hands[p].Add(card);
                    break;
                }
                tries++;
            }
        }
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
