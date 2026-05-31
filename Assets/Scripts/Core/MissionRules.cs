using System.Collections.Generic;

// =====================================================================
//  MissionRules — 게임 규칙의 "순수 함수" 공유 모듈.
// ---------------------------------------------------------------------
//  실게임(TrickManager/MissionManager)과 시뮬레이션(MCTSState)이
//  동일한 규칙을 쓰도록 트릭 승패·전역 미션 규칙 판정을 한곳에 모은다.
//
//  여기 함수들은 CrewAgent/MonoBehaviour에 의존하지 않는다(카드/값/인덱스만).
//  → MCTS 시뮬레이터에서도 그대로 호출 가능.
// =====================================================================
public static class MissionRules
{
    // ---------------------------------------------------------------
    // 트릭 승패: a가 b를 이기는가 (leadSuit 기준)
    //   로켓(트럼프) > 리드 슈트 > 그 외. 같은 그룹이면 값 비교.
    // ---------------------------------------------------------------
    public static bool Beats(Card a, Card b, Card.Suit leadSuit)
    {
        bool aRkt = a.suit == Card.Suit.Rocket;
        bool bRkt = b.suit == Card.Suit.Rocket;
        if (aRkt && !bRkt) return true;
        if (aRkt && bRkt)  return a.value > b.value;
        if (!aRkt && bRkt) return false;

        bool aLead = a.suit == leadSuit;
        bool bLead = b.suit == leadSuit;
        if (aLead && !bLead) return true;
        if (!aLead)          return false;   // 둘 다 리드 아님 → 못 이김
        return a.value > b.value;
    }

    // 카드의 트릭 장악력 점수 (정렬/휴리스틱용)
    public static int WinStrength(Card c, Card.Suit leadSuit)
    {
        if (c.suit == Card.Suit.Rocket) return 200 + c.value;
        if (c.suit == leadSuit)         return 100 + c.value;
        return c.value;
    }

    // ---------------------------------------------------------------
    // 트릭 승자 위치: cardsOnTable 중 이긴 카드의 인덱스(0-based).
    //   leadSuit은 첫 카드 기준. 빈 트릭이면 -1.
    // ---------------------------------------------------------------
    public static int WinnerPosition(IReadOnlyList<Card> cardsOnTable, Card.Suit leadSuit)
    {
        if (cardsOnTable == null || cardsOnTable.Count == 0) return -1;
        int best = 0;
        for (int i = 1; i < cardsOnTable.Count; i++)
            if (Beats(cardsOnTable[i], cardsOnTable[best], leadSuit)) best = i;
        return best;
    }

    // ---------------------------------------------------------------
    // 전역 미션 규칙: 이번 트릭 결과가 규칙을 위반하면 true(=즉시 미션 실패).
    //   trickCards/trickPlayers: 이번 트릭에 깔린 카드와 낸 플레이어 인덱스(동일 순서)
    //   winner: 이번 트릭 승자 인덱스
    //   trickWinCounts: 이번 트릭 반영 후 각 플레이어 누적 승수
    //   rocketWinsSoFar: 이번 트릭 이전까지 "이긴 트릭"에 포함된 로켓 값들의 최대치
    //                    (RocketsInOrder 판정용; 호출자가 누적 관리)
    //   반환: 위반 여부. (핸드 종료 시 판정하는 규칙은 false 반환 — 여기선 트릭 단위만)
    // ---------------------------------------------------------------
    public static bool TrickViolatesGlobalRule(
        GlobalMissionRule rule,
        IReadOnlyList<Card> trickCards,
        IReadOnlyList<int>  trickPlayers,
        int winner,
        int[] trickWinCounts,
        int rocketWinsMaxSoFar)
    {
        switch (rule)
        {
            case GlobalMissionRule.None:
                return false;

            // 9 값 카드가 트릭을 이기면 실패
            case GlobalMissionRule.NoNineWins:
            {
                for (int i = 0; i < trickCards.Count; i++)
                    if (trickPlayers[i] == winner
                        && trickCards[i].suit != Card.Suit.Rocket
                        && trickCards[i].value == 9)
                        return true;
                return false;
            }

            // 어떤 플레이어도 다른 플레이어보다 2트릭 이상 앞설 수 없음
            case GlobalMissionRule.BalanceTricks:
            case GlobalMissionRule.CommanderFirstAndLast:
            {
                int max = int.MinValue, min = int.MaxValue;
                foreach (int c in trickWinCounts) { if (c > max) max = c; if (c < min) min = c; }
                return (max - min) >= 2;
            }

            // 로켓은 1→2→3→4 오름차순으로만 트릭을 이길 수 있음
            case GlobalMissionRule.RocketsInOrder:
            {
                for (int i = 0; i < trickCards.Count; i++)
                {
                    if (trickPlayers[i] == winner && trickCards[i].suit == Card.Suit.Rocket)
                    {
                        // 승리 로켓 값은 직전 최대치 + 1 이어야 함
                        if (trickCards[i].value != rocketWinsMaxSoFar + 1) return true;
                    }
                }
                return false;
            }

            // 나머지(AllRocketsMustWin, RocketOneWinsTwice, Omega, OnePlayerFirstFourOnly,
            //        LeftOfPinkNineWinsAllPink)는 핸드 종료 시 판정 → 트릭 단위 위반 없음
            default:
                return false;
        }
    }

    // 이번 트릭에서 승자가 낸 로켓 값(없으면 0). RocketsInOrder 누적 갱신용.
    public static int WinningRocketValue(
        IReadOnlyList<Card> trickCards, IReadOnlyList<int> trickPlayers, int winner)
    {
        for (int i = 0; i < trickCards.Count; i++)
            if (trickPlayers[i] == winner && trickCards[i].suit == Card.Suit.Rocket)
                return trickCards[i].value;
        return 0;
    }
}
