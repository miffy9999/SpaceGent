using UnityEngine;

// ── 전역 미션 규칙 (0E 미션 승리 조건, 트릭마다 판정) ─────────────────────────
public enum GlobalMissionRule
{
    None = 0,
    AllRocketsMustWin        = 1,  // M13: 로켓 1~4가 각각 정확히 1트릭씩 이겨야 함
    NoNineWins               = 2,  // M16,17: 어떤 9 값 카드도 트릭을 이기면 안 됨
    RocketOneWinsTwice       = 3,  // M26: 로켓-1이 트릭을 정확히 2번 이겨야 함
    BalanceTricks            = 4,  // M29,34: 어떤 플레이어도 다른 플레이어보다 2트릭 이상 앞설 수 없음
    RocketsInOrder           = 5,  // M44: 로켓은 1→2→3→4 오름차순으로만 트릭을 이길 수 있음
    CommanderFirstAndLast    = 6,  // M34: 사령관이 첫 트릭과 마지막 트릭을 이겨야 함 (+BalanceTricks)
    OmegaOnLastTrick         = 7,  // M48: Ω 태스크는 마지막 트릭에서 달성돼야 함
    OnePlayerFirstFourOnly   = 8,  // M50: 한 명은 첫 4트릭만, 다른 한 명은 마지막 트릭만
    LeftOfPinkNineWinsAllPink = 9, // M46: Pink-9를 가진 플레이어 왼쪽이 핑크 카드를 모두 이겨야 함
}

// ── 미션 특수 규칙 플래그 (트릭 진행 중 적용) ────────────────────────────────
[System.Flags]
public enum MissionTaskRule
{
    None                  = 0,
    CommanderDecision     = 1 << 0, // 사령관이 한 명을 골라 모든 태스크를 맡김
    CommanderDistribution = 1 << 1, // 사령관이 태스크를 1장씩 공개해 배분
    DeadZone              = 1 << 2, // 통신 가능하나 토큰 위치 표시 없음
    OnePlayerNoComm       = 1 << 3, // 크루 1명(사령관 왼쪽)은 통신 토큰 사용 불가
    CardExchangeAfterFirst = 1 << 4, // 첫 트릭 후 오른쪽 동료에게서 랜덤 카드 1장 받기
    TokenTransferAllowed  = 1 << 5, // 순서 토큰 1개를 다른 태스크에 옮겨도 됨 (M40)
}

/// <summary>
/// 미션 한 개의 전체 데이터.
/// number 1~50 기준, 태스크 수·순서 토큰·특수 규칙을 모두 포함한다.
/// </summary>
[System.Serializable]
public class Mission
{
    public int    number;    // 미션 번호 1~50
    public string id;        // 레거시/표시용 ("111", "5E" 등)
    public Sprite sprite;

    // 태스크
    public int  totalTaskCount;   // 뽑을 태스크 카드 수 (0E 미션은 0)
    public bool isSpecialMission; // 0E 미션 여부 (특수 승리 조건)

    // 순서 토큰 — 뽑힌 태스크에 순서대로 부여 (남은 태스크는 None)
    public OrderToken[] orderTokensForTasks = new OrderToken[0];

    // 통신 규칙
    public bool hasDeadZone;             // 데드존: 통신 가능하나 토큰 위치 비공개
    public int  commDisruptionTrick;     // ⚡N: N번째 트릭 전까지 통신 전면 금지 (0=없음)

    // 태스크 배분 및 기타 특수 규칙
    public MissionTaskRule taskRule = MissionTaskRule.None;

    // 전역 승리 조건 (0E 미션 또는 추가 전역 제약)
    public GlobalMissionRule globalRule = GlobalMissionRule.None;

    // 레거시 필드 (이전 코드와의 호환용)
    [HideInInspector] public int[] taskCounts = new int[3];

    // ── 헬퍼 ───────────────────────────────────────────────────────────
    public bool HasTaskRule(MissionTaskRule flag) => (taskRule & flag) != 0;

    // 이전 코드에서 사용하던 TotalTaskCount / Difficulty
    public int TotalTaskCount => totalTaskCount;
    public int Difficulty     => totalTaskCount;

    // taskCounts 자동 생성 (4인 기준, 가능한 균등 분배)
    public int[] GetTaskCountsFor4Players()
    {
        int n = totalTaskCount;
        int[] counts = new int[3];
        for (int i = 0; i < 3; i++) counts[i] = n / 3;
        for (int i = 0; i < n % 3; i++) counts[i]++;
        return counts;
    }
}
