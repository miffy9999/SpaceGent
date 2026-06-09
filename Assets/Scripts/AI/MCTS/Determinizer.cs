using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  Determinizer — SO-ISMCTS 신념 결정화: 미플레이 카드를 다른 플레이어 손에 분배.
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

    // ---------------------------------------------------------------
    // 통신 위치 제약: 특정 플레이어가 특정 무늬에 가질 수 있는 값 범위.
    //   maxV : 이 값 초과의 같은 무늬 카드 보유 불가 (최고값 공개)
    //   minV : 이 값 미만의 같은 무늬 카드 보유 불가 (최저값 공개)
    //   forbidAll : 그 무늬 카드를 더 가질 수 없음 (유일 공개 — 공개 카드 1장뿐)
    // ---------------------------------------------------------------
    private class SuitLimit { public int maxV = int.MaxValue; public int minV = int.MinValue; public bool forbidAll; }

    // ---------------------------------------------------------------
    // Sample: self의 시점에서 가능한 손패 분포 1개 생성.
    //   knownVoids[i] = null이면 빈 집합으로 취급.
    //   handSizes[i] = 분배 시점에 player i가 가져야 할 카드 수.
    //   selfIdx = self 플레이어 (이미 손패 정확함).
    //   commReveals = 통신으로 공개된 카드/위치 (신념 제약).
    // ---------------------------------------------------------------
    public static List<Card>[] Sample(
        List<Card> selfHand,
        int selfIdx,
        HashSet<Card> playedCards,
        List<Card> tableCards,
        HashSet<Card.Suit>[] knownVoids,
        int[] handSizes,
        System.Random rng,
        List<CommReveal> commReveals = null)
    {
        int playerCount = handSizes.Length;
        var hands = new List<Card>[playerCount];
        for (int i = 0; i < playerCount; i++) hands[i] = new List<Card>();

        // self는 정확함
        hands[selfIdx] = new List<Card>(selfHand);

        // unplayed pool 구성
        var unplayed = BuildUnplayedPool(selfHand, playedCards, tableCards);

        // 통신 공개 → 강제 배치 카드 + 위치 제약 구성
        //   이미 플레이된(테이블/누적) 카드는 제외. self가 공개한 카드는 self가 정확하므로 무시.
        var forced = new Dictionary<int, List<Card>>();          // playerIdx → 강제 보유 카드
        var limits = new Dictionary<int, Dictionary<Card.Suit, SuitLimit>>();
        if (commReveals != null)
        {
            var taken = BuildTakenSet(selfHand, playedCards, tableCards);
            foreach (var r in commReveals)
            {
                if (r == null || r.card == null) continue;
                if (r.playerIdx == selfIdx) continue;            // self는 정확
                if (r.playerIdx < 0 || r.playerIdx >= playerCount) continue;
                if (taken.Contains(r.card)) continue;            // 이미 플레이됨 → 제약 무의미

                // 강제 보유 카드 (unplayed pool에 있을 때만)
                if (unplayed.Remove(r.card))
                {
                    if (!forced.TryGetValue(r.playerIdx, out var list))
                        forced[r.playerIdx] = list = new List<Card>();
                    list.Add(r.card);
                }

                // 위치 제약 (데드존이면 위치 정보 없음)
                if (!r.hasPosition) continue;
                if (!limits.TryGetValue(r.playerIdx, out var bySuit))
                    limits[r.playerIdx] = bySuit = new Dictionary<Card.Suit, SuitLimit>();
                if (!bySuit.TryGetValue(r.card.suit, out var lim))
                    bySuit[r.card.suit] = lim = new SuitLimit();
                switch (r.position)
                {
                    case CommunicationToken.RevealPosition.Highest: lim.maxV = r.card.value; break;
                    case CommunicationToken.RevealPosition.Lowest:  lim.minV = r.card.value; break;
                    case CommunicationToken.RevealPosition.Only:    lim.forbidAll = true;    break;
                }
            }
        }

        // 분배: MCV(가장 제약 심한 카드 우선) + 백트래킹 = 완전 탐색.
        //   참 손패가 항상 제약을 만족하므로 유효 해는 반드시 존재 → 거의 항상 성공.
        //   후보 순서를 셔플해 N개 결정화 표본의 다양성을 확보.
        ApplyForced(hands, forced);
        int budget = 50000;   // 안전 상한 (병적 케이스 방지)
        if (Distribute(unplayed, hands, selfIdx, handSizes, knownVoids, limits, rng, ref budget))
            return hands;

        // 여기 도달은 사실상 불가(유효 해 존재 보장). 안전망: 제약 완화 분배.
        WarnFallbackThrottled();
        for (int i = 0; i < playerCount; i++)
            if (i != selfIdx) hands[i].Clear();
        ApplyForced(hands, forced);
        DistributeIgnoringVoids(unplayed, hands, selfIdx, handSizes, rng);
        return hands;
    }

    // ---------------------------------------------------------------
    // MCV + 백트래킹 분배. remaining을 모두 배치하면 true.
    //   매 단계 후보가 가장 적은 카드를 골라 배치(forward-checking),
    //   실패 시 되돌리고 다른 후보 시도. 후보 순서는 rng로 셔플(표본 다양성).
    // ---------------------------------------------------------------
    private static bool Distribute(
        List<Card> remaining, List<Card>[] hands,
        int selfIdx, int[] handSizes, HashSet<Card.Suit>[] knownVoids,
        Dictionary<int, Dictionary<Card.Suit, SuitLimit>> limits,
        System.Random rng, ref int budget)
    {
        if (remaining.Count == 0) return true;
        if (--budget < 0) return false;

        // MCV: 후보가 가장 적은 카드 선택
        int bestIdx = -1, bestCount = int.MaxValue;
        List<int> bestCands = null;
        for (int i = 0; i < remaining.Count; i++)
        {
            var cands = Candidates(remaining[i], hands, selfIdx, handSizes, knownVoids, limits);
            if (cands.Count == 0) return false;          // 막다른 길 → 백트랙
            if (cands.Count < bestCount)
            {
                bestCount = cands.Count; bestIdx = i; bestCands = cands;
                if (bestCount == 1) break;
            }
        }

        Card card = remaining[bestIdx];
        remaining.RemoveAt(bestIdx);

        Shuffle(bestCands, rng);   // 표본 다양성
        foreach (int p in bestCands)
        {
            hands[p].Add(card);
            if (Distribute(remaining, hands, selfIdx, handSizes, knownVoids, limits, rng, ref budget))
                return true;
            hands[p].RemoveAt(hands[p].Count - 1);   // 되돌리기
        }

        remaining.Insert(bestIdx, card);   // 원복
        return false;
    }

    // card를 받을 수 있는 플레이어 목록 (full/void/통신 제약 준수).
    private static List<int> Candidates(
        Card card, List<Card>[] hands, int selfIdx, int[] handSizes,
        HashSet<Card.Suit>[] knownVoids,
        Dictionary<int, Dictionary<Card.Suit, SuitLimit>> limits)
    {
        var result = new List<int>(hands.Length);
        for (int p = 0; p < hands.Length; p++)
        {
            if (p == selfIdx) continue;
            if (hands[p].Count >= handSizes[p]) continue;
            if (knownVoids != null && knownVoids[p] != null
                && knownVoids[p].Contains(card.suit)) continue;
            if (ViolatesLimit(limits, p, card)) continue;
            result.Add(p);
        }
        return result;
    }

    // fallback 경고 throttle: 256회마다 1번만 출력 (시뮬 스팸 방지)
    private static int s_fallbackCount;
    private static void WarnFallbackThrottled()
    {
        s_fallbackCount++;
        if ((s_fallbackCount & 0xFF) == 1)
            Debug.LogWarning($"[Determinizer] 제약 분배 fallback 누적 {s_fallbackCount}회 " +
                             "(통신/void 제약 완화하고 분배). 빈번하면 제약 충돌 점검.");
    }

    // 강제 보유 카드를 해당 플레이어 손에 배치 (재시도마다 호출 전 hands는 self만 남김)
    private static void ApplyForced(List<Card>[] hands, Dictionary<int, List<Card>> forced)
    {
        if (forced == null) return;
        foreach (var kv in forced)
            foreach (var c in kv.Value)
                if (!hands[kv.Key].Contains(c)) hands[kv.Key].Add(c);
    }

    private static HashSet<Card> BuildTakenSet(
        List<Card> selfHand, HashSet<Card> playedCards, List<Card> tableCards)
    {
        var taken = new HashSet<Card>();
        foreach (var c in selfHand) taken.Add(c);
        if (playedCards != null) foreach (var c in playedCards) taken.Add(c);
        if (tableCards  != null) foreach (var c in tableCards)  taken.Add(c);
        return taken;
    }

    // ---------------------------------------------------------------
    // unplayed = 40장 - self 손 - 이미 plyed - 현재 테이블
    // ---------------------------------------------------------------
    private static List<Card> BuildUnplayedPool(
        List<Card> selfHand, HashSet<Card> playedCards, List<Card> tableCards)
    {
        var taken = BuildTakenSet(selfHand, playedCards, tableCards);

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

    // 통신 위치 제약 위반 여부 (card를 player p에게 줄 수 없는가)
    private static bool ViolatesLimit(
        Dictionary<int, Dictionary<Card.Suit, SuitLimit>> limits, int p, Card card)
    {
        if (limits == null) return false;
        if (!limits.TryGetValue(p, out var bySuit)) return false;
        if (!bySuit.TryGetValue(card.suit, out var lim)) return false;
        if (lim.forbidAll) return true;                 // 유일 → 그 무늬 추가 불가
        if (card.value > lim.maxV) return true;         // 최고값 초과 불가
        if (card.value < lim.minV) return true;         // 최저값 미만 불가
        return false;
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
