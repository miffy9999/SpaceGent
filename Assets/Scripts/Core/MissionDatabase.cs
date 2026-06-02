using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 미션 목록 (1~50)을 보관하는 ScriptableObject.
/// BuildAllMissions()로 미션 정의를 코드에서 생성한다.
/// </summary>
[CreateAssetMenu(fileName = "MissionDatabase", menuName = "SpaceCrew/Mission Database")]
public class MissionDatabase : ScriptableObject
{
    public List<Mission> missions = new List<Mission>();

    // ── 조회 ─────────────────────────────────────────────────────────
    public Mission GetByNumber(int n) => missions.Find(m => m.number == n);

    public Mission GetByMaxDifficulty(int maxDifficulty)
    {
        var pool = missions.FindAll(m => m.Difficulty <= maxDifficulty && m.Difficulty > 0);
        if (pool.Count == 0) pool = missions;
        return pool[Random.Range(0, pool.Count)];
    }

    public Mission GetRandom()
    {
        if (missions.Count == 0) return null;
        return missions[Random.Range(0, missions.Count)];
    }

    // ── 미션 정의 빌더 ───────────────────────────────────────────────
    // missions.md 를 기준으로 1~50 전부 정의.
    // 스프라이트는 인스펙터에서 별도 연결.
    public static List<Mission> BuildAllMissions()
    {
        var list = new List<Mission>();

        // ── 헬퍼 람다 ───
        Mission M(int num, int tasks, bool special = false,
                  OrderToken[] tokens = null,
                  MissionTaskRule rule = MissionTaskRule.None,
                  GlobalMissionRule global = GlobalMissionRule.None,
                  bool deadZone = false, int disruption = 0)
        {
            return new Mission
            {
                number               = num,
                id                   = special ? $"{num}E" : $"{num}",
                totalTaskCount       = tasks,
                isSpecialMission     = special,
                orderTokensForTasks  = tokens ?? new OrderToken[0],
                taskRule             = rule,
                globalRule           = global,
                hasDeadZone          = deadZone,
                commDisruptionTrick  = disruption,
            };
        }

        // 미션 1~50
        list.Add(M( 1, tasks:  1));
        list.Add(M( 2, tasks:  2));
        list.Add(M( 3, tasks:  2,
            tokens: new[]{ OrderToken.N1, OrderToken.N2 }));
        list.Add(M( 4, tasks:  3));

        // M5: 0E — 사령관이 한 명 선택, 그 사람이 트릭을 0번 이겨야 함 (0E이므로 task 0)
        list.Add(M( 5, tasks: 0, special: true,
            rule: MissionTaskRule.CommanderDecision));

        // M6: 3 태스크 + Arrow 토큰 + 데드존
        list.Add(M( 6, tasks: 3,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2 },
            deadZone: true));

        list.Add(M( 7, tasks: 3,
            tokens: new[]{ OrderToken.Omega }));
        list.Add(M( 8, tasks: 3,
            tokens: new[]{ OrderToken.N1, OrderToken.N2, OrderToken.N3 }));

        // M9: 0E — 색깔(비로켓) 1 값 카드 한 장이 어떤 트릭이든 이겨야 함
        list.Add(M( 9, tasks: 0, special: true,
            global: GlobalMissionRule.ColorOneWins));

        list.Add(M(10, tasks:  4));

        // M11: 4E + N1 토큰 + 크루 1명 통신 불가
        list.Add(M(11, tasks: 4, special: true,
            tokens: new[]{ OrderToken.N1 },
            rule: MissionTaskRule.OnePlayerNoComm));

        // M12: 4E + Ω + 첫 트릭 후 카드 교환
        list.Add(M(12, tasks: 4, special: true,
            tokens: new[]{ OrderToken.Omega },
            rule: MissionTaskRule.CardExchangeAfterFirst));

        // M13: 0E — 로켓 1~4가 각각 1트릭씩 이겨야 함
        list.Add(M(13, tasks: 0, special: true,
            global: GlobalMissionRule.AllRocketsMustWin));

        // M14: 4 + Arrow1~3 + 데드존
        list.Add(M(14, tasks: 4,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2, OrderToken.Arrow3 },
            deadZone: true));

        list.Add(M(15, tasks: 4,
            tokens: new[]{ OrderToken.N1, OrderToken.N2, OrderToken.N3, OrderToken.N4 }));

        // M16: 4E + No 9 wins
        list.Add(M(16, tasks: 4, special: true,
            global: GlobalMissionRule.NoNineWins));

        // M17: 2E + No 9 wins
        list.Add(M(17, tasks: 2, special: true,
            global: GlobalMissionRule.NoNineWins));

        // M18: 5E + ⚡2 (2번 트릭 이후 통신 가능)
        list.Add(M(18, tasks: 5, special: true, disruption: 2));

        // M19: 5E + N1 + ⚡3
        list.Add(M(19, tasks: 5, special: true,
            tokens: new[]{ OrderToken.N1 }, disruption: 3));

        // M20: 2E + 사령관 결정
        list.Add(M(20, tasks: 2, special: true,
            rule: MissionTaskRule.CommanderDecision));

        // M21: 5E + N1,N2 + 데드존
        list.Add(M(21, tasks: 5, special: true,
            tokens: new[]{ OrderToken.N1, OrderToken.N2 }, deadZone: true));

        list.Add(M(22, tasks: 5,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2, OrderToken.Arrow3, OrderToken.Arrow4 }));

        // M23: 5E + N1~N5 + 토큰 위치 교환 허용
        list.Add(M(23, tasks: 5, special: true,
            tokens: new[]{ OrderToken.N1, OrderToken.N2, OrderToken.N3, OrderToken.N4, OrderToken.N5 },
            rule: MissionTaskRule.TokenTransferAllowed));

        // M24: 6 + 사령관 분배
        list.Add(M(24, tasks: 6,
            rule: MissionTaskRule.CommanderDistribution));

        // M25: 5 + Arrow1,2 + 데드존
        list.Add(M(25, tasks: 5,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2 }, deadZone: true));

        // M26: 0E — 색깔(비로켓) 1 값 카드들이 트릭을 정확히 2번 이겨야 함
        list.Add(M(26, tasks: 0, special: true,
            global: GlobalMissionRule.ColorOnesWinTwice));

        // M27: 3 + 사령관 결정
        list.Add(M(27, tasks: 3,
            rule: MissionTaskRule.CommanderDecision));

        // M28: 6 + N1, Ω + ⚡3
        list.Add(M(28, tasks: 6,
            tokens: new[]{ OrderToken.N1, OrderToken.Omega }, disruption: 3));

        // M29: 0E + 균형 + 데드존
        list.Add(M(29, tasks: 0, special: true,
            global: GlobalMissionRule.BalanceTricks, deadZone: true));

        // M30: 6 + Arrow1~3 + ⚡2
        list.Add(M(30, tasks: 6,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2, OrderToken.Arrow3 }, disruption: 2));

        list.Add(M(31, tasks: 6,
            tokens: new[]{ OrderToken.N1, OrderToken.N2, OrderToken.N3 }));

        // M32: 7E + 사령관 분배
        list.Add(M(32, tasks: 7, special: true,
            rule: MissionTaskRule.CommanderDistribution));

        // M33: 0E — 사령관 결정: 선택된 한 명이 로켓 없이 1트릭만 이기기
        list.Add(M(33, tasks: 0, special: true,
            rule: MissionTaskRule.CommanderDecision));

        // M34: 0E — 사령관이 첫+마지막 트릭 이김, 균형
        list.Add(M(34, tasks: 0, special: true,
            global: GlobalMissionRule.CommanderFirstAndLast));

        list.Add(M(35, tasks: 7,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2, OrderToken.Arrow3 }));

        // M36: 7 + N1,N2 + 사령관 분배
        list.Add(M(36, tasks: 7,
            tokens: new[]{ OrderToken.N1, OrderToken.N2 },
            rule: MissionTaskRule.CommanderDistribution));

        // M37: 4 + 사령관 결정
        list.Add(M(37, tasks: 4,
            rule: MissionTaskRule.CommanderDecision));

        // M38: 8 + ⚡3
        list.Add(M(38, tasks: 8, disruption: 3));

        // M39: 8 + Arrow1~3 + 데드존
        list.Add(M(39, tasks: 8,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2, OrderToken.Arrow3 }, deadZone: true));

        // M40: 8 + N1~N3 + 토큰 이동 허용
        list.Add(M(40, tasks: 8,
            tokens: new[]{ OrderToken.N1, OrderToken.N2, OrderToken.N3 },
            rule: MissionTaskRule.TokenTransferAllowed));

        // M41: 0E — 사령관 결정: 선택된 한 명이 첫+마지막 트릭만 이기기(로켓 없이)
        list.Add(M(41, tasks: 0, special: true,
            rule: MissionTaskRule.CommanderDecision));

        list.Add(M(42, tasks: 9));

        // M43: 9 + 사령관 분배
        list.Add(M(43, tasks: 9,
            rule: MissionTaskRule.CommanderDistribution));

        // M44: 0E — 로켓 오름차순으로 트릭 이기기
        list.Add(M(44, tasks: 0, special: true,
            global: GlobalMissionRule.RocketsInOrder));

        list.Add(M(45, tasks: 9,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2, OrderToken.Arrow3 }));

        // M46: 0E — Pink-9 왼쪽 플레이어가 모든 분홍 카드를 이겨야 함
        list.Add(M(46, tasks: 0, special: true,
            global: GlobalMissionRule.LeftOfPinkNineWinsAllPink));

        list.Add(M(47, tasks: 10));

        // M48: 3 + Ω (Ω는 마지막 트릭에서 달성돼야 함)
        list.Add(M(48, tasks: 3,
            tokens: new[]{ OrderToken.Omega },
            global: GlobalMissionRule.OmegaOnLastTrick));

        list.Add(M(49, tasks: 10,
            tokens: new[]{ OrderToken.Arrow1, OrderToken.Arrow2, OrderToken.Arrow3 }));

        // M50: 0E — 한 명이 첫 4트릭만, 다른 한 명이 마지막 트릭만
        list.Add(M(50, tasks: 0, special: true,
            global: GlobalMissionRule.OnePlayerFirstFourOnly));

        return list;
    }
}
