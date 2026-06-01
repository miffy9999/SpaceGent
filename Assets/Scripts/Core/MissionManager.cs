using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// 미션 선택 → 태스크 배분 → 매 트릭 판정 → 보상/패널티 처리.
/// BGA 방식: StartTaskSelectionPhase()로 태스크 풀을 생성하고,
/// 함장 왼쪽부터 시계 방향으로 플레이어가 하나씩 태스크를 선택한다.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("미션 데이터베이스 (인스펙터에서 할당)")]
    public MissionDatabase database;

    [Header("[MCTS Phase 1] Eval 모드 — Python 없이 MCTS 활성화 (env param 대체)")]
    public bool overrideMctsAssignee = false;
    public int  overrideMctsBudget = 500;
    public int  overrideMctsDeterminizations = 10;

    [Header("[Eval] Phase1 다중 태스크 테스트 (0=env num_tasks 사용, >0=강제)")]
    [Tooltip("협력 테스트용: 2~6으로 설정하면 여러 플레이어가 태스크를 나눠 가져\n" +
             "도우미가 owner 타깃을 보유하는 협력 상황이 생긴다. evaluationLogging과 함께 사용.")]
    public int overrideNumTasks = 0;

    // 현재 진행 중인 미션
    public Mission currentMission { get; private set; }

    // 이번 판의 확정된 태스크 목록 (배정 완료)
    public List<TaskCard> tasks = new List<TaskCard>();

    // 태스크 선택 풀 (아직 배정되지 않은 태스크들)
    public List<TaskCard> taskPool = new List<TaskCard>();

    // 선택 순서 (플레이어 인덱스 목록, 함장+1부터 시계방향)
    private List<int> selectionOrder = new List<int>();
    private int selectionCursor = 0;

    // 플레이어별 이긴 트릭 수
    private Dictionary<CrewAgent, int> trickWinCounts = new Dictionary<CrewAgent, int>();

    // 첫/마지막 트릭 승자
    private CrewAgent firstTrickWinner;
    private CrewAgent lastTrickWinner;

    // 현재 트릭 번호 (1부터 시작)
    private int trickNumber = 0;
    public  int TrickNumber => trickNumber;

    private bool isFirstTrick = true;
    private bool missionEnded = false;
    public bool HasMissionEnded => missionEnded;

    // 현 미션의 난이도 상한 (커리큘럼) — 미션 태스크 개수 결정에 사용
    private int currentMaxDifficulty = 9;

    // 보상 상수 (terminal)
    private const float RewardTaskComplete = 1.0f;
    private const float RewardMissionWin   = 2.0f;
    private const float PenaltyTaskFail    = -1.0f;
    private const float PenaltyMissionFail = -2.0f;

    // ───────────────────────────────────────────────────────────────
    // 학습 모드
    //   Normal            : 실제 게임 (커리큘럼/미션보상 정식). 미션 DB에서 태스크 수 결정.
    //   Phase1_CoopSingle : 무작위 1명에게만 WinSpecificCard 태스크 1개. 보상은 팀 결합.
    //                       나머지 3명은 도우미(타깃 카드를 적시에 흘려주거나 양보).
    // ───────────────────────────────────────────────────────────────
    public enum TrainingMode { Normal, Phase1_CoopSingle }
    public static readonly TrainingMode Phase = TrainingMode.Phase1_CoopSingle;

    // Phase1 런타임(에피소드별) + 계측 통계 (보상은 MA-POCA group reward가 담당)
    private CrewAgent phase1Assignee;     // 이번 에피소드 담당자(나머지는 도우미)
    private bool  scriptedHelpers;        // 도우미를 rule-based로 override (협력 베이스라인)
    private bool  epTargetHeldByAssignee; // 이번 에피소드 타깃 카드를 담당자가 보유했는가(계측용)
    private int   epHelperPlays, epVoluntaryContests;   // 에피소드 통계

    // 드래프트 풀 상한 = 선택 액션(Branch[0]) 크기. task 개수는 이 값 이하로 제한.
    public const int MaxPoolSize = 10;

    // [v3 카드 메모리] 이번 핸드에서 이미 플레이된 카드 (트릭 종료 시 누적).
    //   "guaranteed winner" 판정에 사용 — 내 카드보다 강한 카드가 모두 소진됐는지 확인.
    //   핸드 시작 시 Clear, OnTrickResolved에서 trickCards 추가.
    private HashSet<Card> playedCardsInHand = new HashSet<Card>();
    public  HashSet<Card> PlayedCardsInHand => playedCardsInHand;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ---------------------------------------------------------------
    // [BGA 방식] 태스크 선택 단계 시작
    //   함장+1부터 시계방향으로 플레이어가 태스크 풀에서 하나씩 선택.
    //   AI는 자동 선택, 인간은 UI 클릭으로 선택.
    // ---------------------------------------------------------------
    public void StartTaskSelectionPhase(int captainIndex)
    {
        tasks.Clear();
        taskPool.Clear();
        trickWinCounts.Clear();
        isFirstTrick = true;
        missionEnded = false;
        firstTrickWinner = null;
        lastTrickWinner  = null;
        trickNumber = 0;
        GameManager.Instance.uiManager?.HideResult();

        var players = GameManager.Instance.players;
        foreach (var p in players)
            trickWinCounts[p] = 0;

        // [v3 카드 메모리] 새 핸드 시작 — 이전 핸드의 played cards 비움
        playedCardsInHand.Clear();

        var ep = Academy.Instance.EnvironmentParameters;
        scriptedHelpers = ep.GetWithDefault("scripted_helpers", 0f) > 0.5f;
        epHelperPlays = epVoluntaryContests = 0;
        RuleBasedHelper.ResetEpisodeStats();

        // 태스크 개수 결정 (우선순위)
        //   0) overrideNumTasks(>0) — 모드 무관 강제 (에디터 협력 평가용, 최우선)
        //   1) 인터랙티브/Normal      — 미션 DB의 실제 미션
        //   2) 그 외(Phase1 학습)     — 커리큘럼 num_tasks
        int taskCount;
        bool useMissionDb = database != null
                            && (Phase == TrainingMode.Normal || UseInteractiveAutoPick());
        // 강제 태스크 수: GameManager(사용자 편집처) 우선, 없으면 MissionManager 필드
        var gmForTasks = GameManager.Instance;
        int forcedTasks = (gmForTasks != null && gmForTasks.overrideNumTasks > 0)
                          ? gmForTasks.overrideNumTasks : overrideNumTasks;
        if (forcedTasks > 0)
        {
            taskCount = forcedTasks;
            currentMission = null;
            currentMaxDifficulty = taskCount;
        }
        else if (useMissionDb)
        {
            currentMaxDifficulty = Mathf.RoundToInt(ep.GetWithDefault("difficulty", 9f));
            currentMission = SelectNextMission();
            taskCount = currentMission != null ? currentMission.TotalTaskCount : players.Count;
        }
        else
        {
            // 학습(커리큘럼): task 개수를 1개부터 점차 늘린다 (env: num_tasks)
            taskCount = Mathf.RoundToInt(ep.GetWithDefault("num_tasks", 1f));
            currentMission = null;
            currentMaxDifficulty = taskCount;
        }
        taskCount = Mathf.Clamp(taskCount, 0, MaxPoolSize);

        // 설정 확인 로그 (구성이 바뀔 때만 1회) — overrideNumTasks/정책/budget 적용 여부 확인용
        LogConfigIfChanged(taskCount);

        // WinSpecificCard 태스크 풀 생성 (미배정) — 드래프트로 배정
        GenerateTaskPool(taskCount, captainIndex);

        // 미션 정의에서 순서 토큰 적용
        if (currentMission != null && currentMission.orderTokensForTasks?.Length > 0)
            ApplyMissionOrderTokens(currentMission.orderTokensForTasks);
        else if (ep.GetWithDefault("enable_order_tokens", 0f) > 0.5f)
            AssignSequentialOrderTokens();

        // 미션 통신 규칙 적용 (데드존 / 통신 차단)
        if (currentMission != null)
            ApplyMissionCommRules(currentMission);

        // 선택 순서: 함장(로켓4 소지자)부터 시계방향
        selectionOrder.Clear();
        for (int i = 0; i < players.Count; i++)
            selectionOrder.Add((captainIndex + i) % players.Count);
        selectionCursor = 0;

        Debug.Log($"[Mission] 태스크 풀 {taskPool.Count}개 생성, 드래프트 시작");
        GameManager.Instance.uiManager?.ShowTaskSelection();
        GiveSelectionTurn();
    }

    // ---------------------------------------------------------------
    // 태스크 풀 생성 (미배정 상태)
    // ---------------------------------------------------------------
    private void GenerateTaskPool(int count, int captainIndex)
    {
        var players = GameManager.Instance.players;
        HashSet<Card> used = new HashSet<Card>();

        for (int i = 0; i < count; i++)
        {
            int pIdx = (captainIndex + (i % players.Count)) % players.Count;
            TaskCard task = CreateUnassignedTask(players[pIdx], used);
            if (task == null) break;
            taskPool.Add(task);
            if (task.targetCard != null)
                used.Add(task.targetCard);
        }
    }

    // 설정(태스크수/정책/budget)이 직전과 다르면 1회만 콘솔 출력 — 스팸 없이 구성 확인.
    private string _lastConfigSig;
    private void LogConfigIfChanged(int taskCount)
    {
        var gm = GameManager.Instance;
        string policy = gm != null ? gm.AiPolicyLabel : "?";
        int budget = gm != null ? gm.mctsBudget : 0;
        int dets   = gm != null ? gm.mctsDeterminizations : 0;
        string sig = $"{policy}|tasks={taskCount}|b={budget}|d={dets}";
        if (sig == _lastConfigSig) return;
        _lastConfigSig = sig;
        Debug.Log($"[Config] 정책={policy} 태스크수={taskCount} " +
                  $"MCTS budget={budget} dets={dets} " +
                  $"| evalLog={(gm != null && gm.evaluationLogging)}");
    }

    // [커리큘럼] 풀의 앞쪽 태스크부터 순차로 N1..N5 순서 토큰 부여
    private void AssignSequentialOrderTokens()
    {
        for (int i = 0; i < taskPool.Count && i < 5; i++)
            taskPool[i].orderToken = (OrderToken)((int)OrderToken.N1 + i);
    }

    // 미션 정의에서 순서 토큰 배열을 풀의 앞쪽 태스크에 순서대로 적용
    private void ApplyMissionOrderTokens(OrderToken[] tokens)
    {
        for (int i = 0; i < tokens.Length && i < taskPool.Count; i++)
            taskPool[i].orderToken = tokens[i];
    }

    // 미션 통신 규칙 CommunicationManager에 전달
    private void ApplyMissionCommRules(Mission m)
    {
        var cm = GameManager.Instance.communicationManager;
        if (cm == null) return;
        cm.SetMissionCommRules(
            deadZone:          m.hasDeadZone,
            disruptionTrick:   m.commDisruptionTrick,
            onePlayerNoComm:   m.HasTaskRule(MissionTaskRule.OnePlayerNoComm));
    }

    // ---------------------------------------------------------------
    // 현재 선택 차례인 플레이어
    // ---------------------------------------------------------------
    public CrewAgent GetCurrentPickingPlayer()
    {
        if (taskPool.Count == 0) return null;
        if (selectionOrder.Count == 0) return null;
        int idx = selectionOrder[selectionCursor % selectionOrder.Count];
        return GameManager.Instance.players[idx];
    }

    // 미배정 풀 크기 (관측/마스킹용)
    public int PoolCount => taskPool.Count;

    // ── 인터랙티브 미션 진행 (1→50 순서, 성공 시 다음으로) ──────────
    private int interactiveMissionNumber = 1;
    public int InteractiveMissionNumber => interactiveMissionNumber;

    private Mission SelectNextMission()
    {
        if (database == null || database.missions.Count == 0) return null;

        // 학습(Normal·배치): 난이도 상한 내 랜덤
        if (Phase == TrainingMode.Normal && !UseInteractiveAutoPick())
            return database.GetByMaxDifficulty(currentMaxDifficulty);

        // 인터랙티브: 순서대로 1→50
        var m = database.GetByNumber(interactiveMissionNumber);
        return m ?? database.missions[0];
    }

    // 미션 결과에 따라 다음 미션으로 진행 (인터랙티브 전용)
    private void AdvanceMissionOnResult(bool success)
    {
        if (!UseInteractiveAutoPick()) return;
        if (success)
            interactiveMissionNumber = Mathf.Clamp(interactiveMissionNumber + 1, 1, 50);
        // 실패 시 같은 미션 재시도 (번호 유지)
    }

    // ---------------------------------------------------------------
    // 드래프트 턴 엔진 — 함장부터 시계방향, ML 주도.
    //   AI: 정책(RequestDecision→OnActionReceived→AgentSelectTask)
    //   인간: UI 버튼/키 (HumanPickTask / HumanPassTask)
    // ---------------------------------------------------------------
    private void GiveSelectionTurn()
    {
        if (taskPool.Count == 0) { CompleteTaskSelection(); return; }
        var picker = GetCurrentPickingPlayer();
        if (picker == null) { CompleteTaskSelection(); return; }

        // 패널은 항상 갱신 (인간 버튼 활성/비활성, "선택 중" 표시)
        GameManager.Instance.uiManager?.RefreshTaskSelection();

        if (picker.isHumanPlayer)
        {
            Debug.Log($"[Mission] 인간 차례 — 풀 {taskPool.Count}개 / 패스 가능={CanCurrentPickerPass()}");
            return;   // HumanPickTask / HumanPassTask 대기
        }

        // AI 차례:
        //   인터랙티브 플레이(인간 있음·비배치)에서는 ML 브레인 유무와 무관하게
        //   결정론적 자동 선택으로 진행 → 임무창이 멈추지 않는다.
        //   배치/학습 모드에서는 정책(RequestDecision)이 결정.
        if (UseInteractiveAutoPick())
            StartCoroutine(AutoPickAfterDelay(picker));
        else
            picker.RequestDecision();
    }

    // 인간이 함께 플레이하는 인터랙티브 세션인가 (배치 모드 학습이 아닌가)
    private bool UseInteractiveAutoPick()
    {
        if (Application.isBatchMode) return false;
        foreach (var p in GameManager.Instance.players)
            if (p.isHumanPlayer) return true;
        return false;
    }

    // AI 자동 선택: 잠깐 보여준 뒤 진행. 가능하면 자기가 타깃 카드를 든 태스크를 선호.
    private IEnumerator AutoPickAfterDelay(CrewAgent picker)
    {
        yield return new WaitForSeconds(0.6f);

        // 코루틴 대기 중 상태가 바뀌었으면 중단
        if (missionEnded) yield break;
        if (GetCurrentPickingPlayer() != picker) yield break;
        if (taskPool.Count == 0) { CompleteTaskSelection(); yield break; }

        int pick = ChooseTaskSlotForAI(picker);
        AssignTask(pick, picker);
        selectionCursor++;
        GiveSelectionTurn();
    }

    // AI 선택 휴리스틱: 자기 손패에 타깃 카드가 있는 태스크 우선, 없으면 0번
    private int ChooseTaskSlotForAI(CrewAgent picker)
    {
        for (int i = 0; i < taskPool.Count; i++)
            if (taskPool[i].targetCard != null && picker.hand.Contains(taskPool[i].targetCard))
                return i;
        return 0;
    }

    // 현재 선택자가 패스 가능한가: 남은 task < 이번 라운드 남은 플레이어 수
    //   R = N - (cursor % N), 패스 가능 ⇔ T < R  (T >= R 이면 강제 선택)
    public bool CanCurrentPickerPass()
    {
        int n = GameManager.Instance.players.Count;
        if (n == 0) return false;
        int remainingInRound = n - (selectionCursor % n);
        return taskPool.Count < remainingInRound;
    }

    // AI 정책의 선택 결정 (CrewAgent.OnActionReceived → 선택 페이즈에서 호출)
    public void AgentSelectTask(CrewAgent picker, int poolSlot, bool wantPass)
    {
        if (GetCurrentPickingPlayer() != picker) return;      // 차례 아님 → 무시
        if (taskPool.Count == 0) { CompleteTaskSelection(); return; }

        if (wantPass && CanCurrentPickerPass())
        {
            selectionCursor++;
            GiveSelectionTurn();
            return;
        }

        AssignTask(Mathf.Clamp(poolSlot, 0, taskPool.Count - 1), picker);
        selectionCursor++;
        GiveSelectionTurn();
    }

    // 인간: 풀 인덱스 선택
    public void HumanPickTask(int poolIndex)
    {
        var human = GameManager.Instance.players[0];
        if (GetCurrentPickingPlayer() != human) { Debug.LogWarning("[Mission] 인간의 선택 차례가 아닙니다."); return; }
        if (poolIndex < 0 || poolIndex >= taskPool.Count) return;
        AssignTask(poolIndex, human);
        selectionCursor++;
        GiveSelectionTurn();
    }

    // 인간: 패스 (가능할 때만)
    public void HumanPassTask()
    {
        var human = GameManager.Instance.players[0];
        if (GetCurrentPickingPlayer() != human) return;
        if (!CanCurrentPickerPass()) { Debug.Log("[Mission] 지금은 패스 불가 (강제 선택)"); return; }
        selectionCursor++;
        GiveSelectionTurn();
    }

    // ---------------------------------------------------------------
    // 내부: 태스크 배정 + 풀에서 제거
    // ---------------------------------------------------------------
    private void AssignTask(int poolIndex, CrewAgent player)
    {
        TaskCard task = taskPool[poolIndex];
        task.assignedTo = player;
        tasks.Add(task);
        taskPool.RemoveAt(poolIndex);
        Debug.Log($"[Mission] {player.name} → {task} 선택");
    }

    private void CompleteTaskSelection()
    {
        // 베이스라인/관측 호환: 단일 담당자 개념은 tasks[0] 소유자로 대표
        phase1Assignee = tasks.Count > 0 ? tasks[0].assignedTo : null;
        epTargetHeldByAssignee = phase1Assignee != null && tasks.Count > 0
                                 && phase1Assignee.hand.Contains(tasks[0].targetCard);
        LogTaskSummary();
        GameManager.Instance.uiManager?.HideTaskSelection();
        GameManager.Instance.trickManager.StartPlaying();
    }

    // ---------------------------------------------------------------
    // (레거시) 자동 배분 — 더 이상 사용하지 않지만 호환성 유지
    // ---------------------------------------------------------------
    public void InitMission(int captainIndex)
    {
        StartTaskSelectionPhase(captainIndex);
    }

    // ---------------------------------------------------------------
    // 트릭 결과 판정 (TrickManager에서 호출)
    // ---------------------------------------------------------------

    // 순서 토큰 포함 태스크 완수 헬퍼
    //   snapCompleted / completingNow : IsOrderTokenValid에 전달
    //   위반 시 즉시 미션 실패, 반환값 false
    private bool TryCompleteTask(TaskCard task, int snapCompleted,
                                  System.Collections.Generic.HashSet<TaskCard> completingNow)
    {
        if (!IsOrderTokenValid(task, snapCompleted, completingNow))
        {
            Debug.Log($"[Mission] 순서 토큰 위반 → 미션 실패 ({task})");
            EndMissionFailed();
            return false;
        }
        completingNow.Add(task);
        CompleteTask(task);
        return true;
    }

    private void EndMissionFailed()
    {
        missionEnded = true;
        GiveTeamReward(success: false);
    }

    public void OnTrickResolved(CrewAgent winner, List<Card> trickCards,
                                CrewAgent opener, Card.Suit openerSuit, Card.Suit trickLeadSuit)
    {
        if (missionEnded) return;

        trickWinCounts[winner]++;
        trickNumber++;

        // 순서 토큰 검증용 스냅샷 (이번 트릭 시작 전 완수 수)
        int snapCompleted = CountCompleted();
        var completingNow = new System.Collections.Generic.HashSet<TaskCard>();

        // [v3 카드 메모리] 이번 트릭에 나온 카드를 모두 누적
        foreach (Card c in trickCards) playedCardsInHand.Add(c);

        // ── 추적 변수 업데이트 ──────────────────────────────────────────
        if (isFirstTrick) firstTrickWinner = winner;
        lastTrickWinner = winner;

        // ── 전역 미션 규칙 판정 ─────────────────────────────────────────
        if (currentMission != null && !CheckGlobalRule(currentMission.globalRule, winner, trickCards))
            return; // EndMissionFailed 이미 호출됨

        if (missionEnded) return;

        // ── 태스크 판정 (WinSpecificCard) ───────────────────────────────
        foreach (TaskCard task in tasks)
        {
            if (missionEnded) break;
            if (task.isCompleted || task.isFailed) continue;

            if (trickCards.Contains(task.targetCard))
            {
                if (winner == task.assignedTo) TryCompleteTask(task, snapCompleted, completingNow);
                else                           FailTask(task);
            }
        }
        if (missionEnded) return;

        // M12: 첫 트릭 후 카드 교환
        if (isFirstTrick && currentMission != null
            && currentMission.HasTaskRule(MissionTaskRule.CardExchangeAfterFirst))
            ExecuteCardExchangeAfterFirstTrick();

        isFirstTrick = false;

        if (!missionEnded && IsMissionFailed())
        {
            missionEnded = true;
            GiveTeamReward(success: false);
        }
    }

    // ---------------------------------------------------------------
    // 핸드 종료 시 최종 판정
    // ---------------------------------------------------------------
    public void OnHandEnded()
    {
        foreach (TaskCard task in tasks)
        {
            if (missionEnded) break;
            if (task.isCompleted || task.isFailed) continue;

            // WinSpecificCard: targetCard가 든 트릭은 OnTrickResolved에서 이미 판정됨.
            //   36색 카드는 모두 플레이되므로 여기 도달(미해결)은 안전망 — 실패 처리.
            FailTask(task);
        }

        // ── 핸드 종료 시 전역 규칙 최종 판정 ────────────────────────────
        if (!missionEnded && currentMission != null)
        {
            bool globalOk = true;
            switch (currentMission.globalRule)
            {
                case GlobalMissionRule.CommanderFirstAndLast:
                    globalOk = CheckCommanderFirstAndLast()
                               && CheckGlobalRule(GlobalMissionRule.BalanceTricks, null, new List<Card>());
                    break;
                case GlobalMissionRule.OmegaOnLastTrick:
                    globalOk = CheckOmegaOnLastTrick();
                    break;
                case GlobalMissionRule.AllRocketsMustWin:
                    // 각 로켓이 적어도 1트릭을 이겼는지 확인
                    for (int v = 1; v <= 4 && globalOk; v++)
                    {
                        bool won = false;
                        foreach (Card c in playedCardsInHand)
                            if (c.suit == Card.Suit.Rocket && c.value == v) { won = true; break; }
                        if (!won) globalOk = false;
                    }
                    break;
                case GlobalMissionRule.RocketOneWinsTwice:
                    // 로켓-1 이 트릭에서 2번 이겼는지 (count는 TrickManager가 tracking해야 하지만 여기선 playedCards 활용)
                    // 간소화: 로켓-1은 한번만 낼 수 있으므로 "두 번"은 게임 디자이너 의도 재해석 필요.
                    // 현 구현: 로켓-1이 적어도 1번은 이겼으면 통과
                    { bool r1won = false;
                      foreach (Card c in playedCardsInHand)
                          if (c.suit == Card.Suit.Rocket && c.value == 1) { r1won = true; break; }
                      globalOk = r1won; }
                    break;
            }
            if (!globalOk)
            {
                Debug.Log($"[GlobalRule:{currentMission.globalRule}] 핸드 종료 시 위반 → 미션 실패");
                missionEnded = true;
                GiveTeamReward(success: false);
            }
        }

        if (!missionEnded)
        {
            missionEnded = true;
            GiveTeamReward(IsMissionComplete());
        }

        // [Phase1] 협력 계측 — 에피소드당 1회 TensorBoard 송출
        if (Phase == TrainingMode.Phase1_CoopSingle && phase1Assignee != null)
        {
            var sr = Academy.Instance.StatsRecorder;

            // CompleteTask로 결정된 isCompleted 기준 성공 여부
            bool taskSuccess = tasks.Count > 0 && tasks[0].isCompleted;
            sr.Add("coop/assignee_success", taskSuccess ? 1f : 0f);

            // 타깃 보유자별 분리 stat — 낮은 성공률이 구조적 천장인지 정책 결함인지 판별
            sr.Add($"coop/success_by_{(epTargetHeldByAssignee ? "assignee" : "helper")}",
                   taskSuccess ? 1f : 0f);
            if (epHelperPlays > 0)
                sr.Add("coop/voluntary_contest_rate", (float)epVoluntaryContests / epHelperPlays);

            // 도우미 action 분포 — 의도대로 작동했는지 정량 검증
            if (RuleBasedHelper.CountTotal > 0)
            {
                float total = RuleBasedHelper.CountTotal;
                sr.Add("hfsm/throw_rate",      RuleBasedHelper.CountThrow      / total);
                sr.Add("hfsm/win_rate",        RuleBasedHelper.CountWin        / total);
                sr.Add("hfsm/playtarget_rate", RuleBasedHelper.CountPlayTarget / total);
            }

            // [All-rule-based 시뮬레이션] 콘솔 누적 성공률 — 학습 없이 평가 모드
            int targetVal = (tasks.Count > 0 && tasks[0].targetCard != null) ? tasks[0].targetCard.value : 0;
            EvaluationStats.RecordEpisode(taskSuccess, epTargetHeldByAssignee, targetVal);
        }
    }

    // ---------------------------------------------------------------
    // 순서 토큰 검증
    //   snapCompleted : 이번 트릭 시작 전 완수된 태스크 수 (스냅샷)
    //   completingNow : 이번 트릭에서 이미 완수 확정된 태스크 집합
    // ---------------------------------------------------------------
    private bool IsOrderTokenValid(TaskCard task, int snapCompleted,
                                   System.Collections.Generic.HashSet<TaskCard> completingNow)
    {
        switch (task.orderToken)
        {
            case OrderToken.None: return true;

            // 숫자 토큰: 이전에 완수된 태스크 수 + 이번 트릭 먼저 완수된 태스크 수 == k-1
            case OrderToken.N1:
            case OrderToken.N2:
            case OrderToken.N3:
            case OrderToken.N4:
            case OrderToken.N5:
                int required = (int)task.orderToken - 1; // N1→0, N2→1, ...
                int alreadyDone = snapCompleted;
                // 같은 트릭에서 더 낮은 N 토큰이 먼저 완수된 경우도 카운트
                foreach (var t in completingNow)
                    if (t.orderToken >= OrderToken.N1 && t.orderToken < task.orderToken)
                        alreadyDone++;
                return alreadyDone == required;

            case OrderToken.Omega:
                // 나 자신과 Omega 토큰 태스크를 제외한 모든 태스크가 완수(또는 실패)돼야 함
                foreach (var t in tasks)
                {
                    if (t == task || t.orderToken == OrderToken.Omega) continue;
                    if (!t.isCompleted && !t.isFailed && !completingNow.Contains(t))
                        return false;
                }
                return true;

            // 화살표 토큰: 이전 단계 Arrow가 완수됐거나 같은 트릭에서 먼저 완수돼야 함
            case OrderToken.Arrow1:
                return true; // Arrow1은 항상 먼저 올 수 있음
            case OrderToken.Arrow2:
                return ArrowPreconditionMet(OrderToken.Arrow1, completingNow);
            case OrderToken.Arrow3:
                return ArrowPreconditionMet(OrderToken.Arrow2, completingNow);
            case OrderToken.Arrow4:
                return ArrowPreconditionMet(OrderToken.Arrow3, completingNow);

            default: return true;
        }
    }

    private bool ArrowPreconditionMet(OrderToken prevArrow,
        System.Collections.Generic.HashSet<TaskCard> completingNow)
    {
        // 이전 단계 Arrow 태스크가 이미 완수됐거나 이번 트릭에 먼저 완수되는 경우
        foreach (var t in tasks)
            if (t.orderToken == prevArrow && t.isCompleted) return true;
        foreach (var t in completingNow)
            if (t.orderToken == prevArrow) return true;
        return false;
    }

    private int CountCompleted()
    {
        int n = 0;
        foreach (var t in tasks) if (t.isCompleted) n++;
        return n;
    }

    // ---------------------------------------------------------------
    // 태스크 완료 / 실패
    // ---------------------------------------------------------------
    private void CompleteTask(TaskCard task)
    {
        task.isCompleted = true;
        if (Phase == TrainingMode.Phase1_CoopSingle)
        {
            // 그룹/학습자 보상 (PPO면 player[0]에 직접, POCA면 group)
            GameManager.Instance.AddGroupOrLearnerReward(RewardTaskComplete);
        }
        else
        {
            task.assignedTo.AddReward(RewardTaskComplete);
            if (Phase == TrainingMode.Normal)   // 실제 게임: 협력 보너스
            {
                float teamBonus = RewardTaskComplete * 0.3f;
                foreach (var p in GameManager.Instance.players)
                    if (p != task.assignedTo) p.AddReward(teamBonus);
            }
        }
        Debug.Log($"[Mission] 태스크 완료 {task.assignedTo.name} → {task}");
    }

    private void FailTask(TaskCard task)
    {
        task.isFailed = true;
        if (Phase == TrainingMode.Phase1_CoopSingle)
            GameManager.Instance.AddGroupOrLearnerReward(PenaltyTaskFail);
        else
            task.assignedTo.AddReward(PenaltyTaskFail);
        Debug.Log($"[Mission] 태스크 실패 {task.assignedTo.name} → {task}");
    }

    private void GiveTeamReward(bool success)
    {
        // 평가 집계 (미션당 1회 — missionEnded 가드로 보장). 정책별 누적 성공률 콘솔 로그.
        var gm = GameManager.Instance;
        if (gm != null && gm.evaluationLogging)
            EvalStats.Record(gm.AiPolicyLabel,
                             currentMission != null ? currentMission.totalTaskCount : tasks.Count,
                             success);

        // 인터랙티브 플레이: 결과 패널 표시 + 다음 미션 진행 (보상은 학습용이라 생략 가능)
        if (UseInteractiveAutoPick())
        {
            Debug.Log($"[Mission] 미션 {(success ? "성공" : "실패")} (미션 #{interactiveMissionNumber})");
            GameManager.Instance.uiManager?.ShowResult(success);
            AdvanceMissionOnResult(success);
            return;
        }

        // 학습: 미션 ±2.0은 Normal에서만 (Phase0/1은 태스크-레벨 보상 사용)
        if (Phase != TrainingMode.Normal) return;
        float reward = success ? RewardMissionWin : PenaltyMissionFail;
        foreach (var p in GameManager.Instance.players)
            p.AddReward(reward);
        Debug.Log($"[Mission] 미션 {(success ? "성공" : "실패")} 팀 보상 {reward}");
        GameManager.Instance.uiManager?.ShowResult(success);
    }

    // ---------------------------------------------------------------
    // 전역 미션 규칙 판정
    //   true = 계속, false = 미션 실패(EndMissionFailed 호출됨)
    // ---------------------------------------------------------------
    private bool CheckGlobalRule(GlobalMissionRule rule, CrewAgent winner, List<Card> trickCards)
    {
        switch (rule)
        {
            case GlobalMissionRule.None: return true;

            // M16, M17: 9값 카드가 트릭을 이기면 즉시 실패
            case GlobalMissionRule.NoNineWins:
            {
                Card winCard = null;
                foreach (Card c in trickCards) if (c.suit != Card.Suit.Rocket && c.value == 9) { winCard = c; break; }
                if (winCard != null && winner != null)
                {
                    int wIdx = GameManager.Instance.trickManager.cardsOnTable.IndexOf(winCard);
                    // 9 값 카드가 실제로 트릭을 이겼는지 확인 (승자가 낸 카드가 9)
                    // TrickManager 내부에서 이미 winner가 결정됐으므로 승자가 9 카드를 냈는지 확인
                    bool nineWon = false;
                    var table = GameManager.Instance.trickManager.cardsOnTable;
                    var players = GameManager.Instance.trickManager.playersOnTable;
                    for (int i = 0; i < table.Count; i++)
                        if (table[i].value == 9 && table[i].suit != Card.Suit.Rocket && players[i] == winner)
                            nineWon = true;
                    if (nineWon)
                    {
                        Debug.Log("[GlobalRule] 9값 카드가 트릭을 이김 → 미션 실패");
                        EndMissionFailed(); return false;
                    }
                }
                return true;
            }

            // M29, M34: 어떤 플레이어도 다른 플레이어보다 2트릭 이상 앞설 수 없음
            case GlobalMissionRule.BalanceTricks:
            case GlobalMissionRule.CommanderFirstAndLast:
            {
                int max = 0, min = int.MaxValue;
                foreach (var cnt in trickWinCounts.Values)
                {
                    if (cnt > max) max = cnt;
                    if (cnt < min) min = cnt;
                }
                if (max - min >= 2)
                {
                    Debug.Log("[GlobalRule] 2트릭 차이 초과 → 미션 실패");
                    EndMissionFailed(); return false;
                }
                return true;
            }

            // M44: 로켓은 1→2→3→4 순서로만 트릭을 이길 수 있음
            case GlobalMissionRule.RocketsInOrder:
            {
                // 이번 트릭에서 로켓이 이겼는가?
                Card rocketPlayed = null;
                var table   = GameManager.Instance.trickManager.cardsOnTable;
                var players2 = GameManager.Instance.trickManager.playersOnTable;
                for (int i = 0; i < table.Count; i++)
                    if (table[i].suit == Card.Suit.Rocket && players2[i] == winner)
                        rocketPlayed = table[i];
                if (rocketPlayed != null)
                {
                    // 이전에 이긴 로켓 중 가장 최근 값 확인
                    int expectedNext = 1;
                    foreach (Card c in playedCardsInHand)
                        if (c.suit == Card.Suit.Rocket) expectedNext = Mathf.Max(expectedNext, c.value + 1);
                    if (rocketPlayed.value != expectedNext - 1 && playedCardsInHand.Contains(rocketPlayed))
                    {
                        // 이 트릭이 처음 플레이됐을 때의 로켓 값 검증
                        // playedCardsInHand에 이미 추가됐으므로 previousRocketMax를 별도 추적
                    }
                    // 간소화: 현재까지 이긴 로켓의 최대값보다 크면서 연속이어야 함
                    int maxRocketWon = 0;
                    foreach (Card c in playedCardsInHand)
                        if (c.suit == Card.Suit.Rocket && c != rocketPlayed) maxRocketWon = Mathf.Max(maxRocketWon, c.value);
                    if (rocketPlayed.value != maxRocketWon + 1)
                    {
                        Debug.Log($"[GlobalRule] 로켓 순서 위반 (예상 {maxRocketWon+1}, 실제 {rocketPlayed.value}) → 미션 실패");
                        EndMissionFailed(); return false;
                    }
                }
                return true;
            }

            // M26: 로켓-1이 정확히 2트릭을 이겨야 함 (핸드 종료 시 판정 — 여기선 초과 감시)
            case GlobalMissionRule.RocketOneWinsTwice:
            {
                // 로켓-1이 이미 2트릭 이상 이겼는지는 OnHandEnded에서 판정
                return true;
            }

            default: return true;
        }
    }

    // M34: 사령관 첫+마지막 트릭 판정 (OnHandEnded에서 호출)
    private bool CheckCommanderFirstAndLast()
    {
        // TrickManager에서 함장 인덱스를 알 수 있음 (로켓4 소지자)
        var tm = GameManager.Instance.trickManager;
        // TrickManager.captainIndex는 private이므로 firstTrickWinner 기반 간접 확인:
        // 사령관이 첫 트릭을 선이므로 firstTrickWinner 또는 trickLead가 사령관.
        // 간소화: firstTrickWinner == lastTrickWinner (M34 정신에 부합)
        return firstTrickWinner != null && firstTrickWinner == lastTrickWinner;
    }

    // M48: Omega 태스크가 마지막 트릭에서 완수됐는지 확인
    private bool CheckOmegaOnLastTrick()
    {
        foreach (var t in tasks)
        {
            if (t.orderToken != OrderToken.Omega) continue;
            if (!t.isCompleted) return false;
            // 마지막 트릭에서 완수됐어야 함
            // lastTrickWinner == t.assignedTo 이어야 함 (태스크 카드가 마지막 트릭에서 이겨야)
            if (lastTrickWinner != t.assignedTo) return false;
        }
        return true;
    }

    // M12: 첫 트릭 이후 오른쪽 동료에게서 카드 1장 받기 (랜덤)
    private void ExecuteCardExchangeAfterFirstTrick()
    {
        var players = GameManager.Instance.players;
        var toReceive = new Card[players.Count];
        for (int i = 0; i < players.Count; i++)
        {
            var right = players[(i + 1) % players.Count];
            if (right.hand.Count > 0)
            {
                int pick = UnityEngine.Random.Range(0, right.hand.Count);
                toReceive[i] = right.hand[pick];
            }
        }
        for (int i = 0; i < players.Count; i++)
        {
            if (toReceive[i] == null) continue;
            var giver = players[(i + 1) % players.Count];
            giver.hand.Remove(toReceive[i]);
            players[i].hand.Add(toReceive[i]);
            Debug.Log($"[M12 카드 교환] {giver.name} → {players[i].name}: {toReceive[i]}");
        }
    }

    // ---------------------------------------------------------------
    // 상태 조회
    // ---------------------------------------------------------------
    public bool IsMissionComplete()
    {
        // 0E(특수) 미션: 태스크 카드가 없고 전역 규칙으로만 판정.
        //   OnHandEnded에서 전역 규칙 위반 시 이미 missionEnded 처리되므로,
        //   여기까지 도달했다면(미실패) 성공으로 간주.
        if (currentMission != null && currentMission.isSpecialMission)
            return !IsMissionFailed();

        foreach (var t in tasks) if (!t.isCompleted) return false;
        return tasks.Count > 0;
    }

    public bool IsMissionFailed()
    {
        foreach (var t in tasks) if (t.isFailed) return true;
        return false;
    }

    // 현재 핸드에서 해당 플레이어가 이긴 트릭 수 (관찰용)
    public int GetTrickWinCount(CrewAgent agent)
        => trickWinCounts.TryGetValue(agent, out var n) ? n : 0;

    // ── 역할 조회 (관찰/계측용) — 다중 task: 'task를 1개라도 소유했는가' ──
    public bool IsPhase1Assignee(CrewAgent a)
        => tasks.Exists(t => t.assignedTo == a);

    // 해당 플레이어가 자기 task의 타깃 카드를 손에 들고 있는가 (관찰용)
    public bool HoldsOwnTaskTarget(CrewAgent a)
        => a != null && tasks.Exists(t => t.assignedTo == a
                                          && t.targetCard != null && a.hand.Contains(t.targetCard));

    // Phase1 담당자의 타깃 카드 (관찰/계측용). 그 외엔 null.
    public Card CurrentTargetCard()
        => (Phase == TrainingMode.Phase1_CoopSingle && tasks.Count > 0) ? tasks[0].targetCard : null;

    // [진단] 이 플레이어가 지금 강제 져주기 대상인가 (도우미 + scripted 모드)
    public bool ScriptedHelperShouldThrow(CrewAgent a)
        => Phase == TrainingMode.Phase1_CoopSingle && scriptedHelpers
           && phase1Assignee != null && a != phase1Assignee;

    // [rule_based] Rule-based helper 카드 선택 — 정책 override 경로 (OnActionReceived).
    //   조건: Phase1 + scripted_helpers=1 (env) + 도우미(담당자 아님) + task 존재.
    //   조건 미충족 시 -1 → 호출자가 원래 정책 액션 유지.
    public int RuleBasedHelperCardIndex(CrewAgent helper)
    {
        if (!scriptedHelpers) return -1;
        return ResolveHelperCardIndex(helper);
    }

    // [rule_based] Heuristic 경로 전용 — PPO helperHeuristicOnly 모드에서 호출.
    //   scripted_helpers env 게이트 없이 항상 rule-based로 동작 (helpers는 BehaviorType 자체가 HeuristicOnly).
    public int HeuristicHelperCardIndex(CrewAgent helper)
        => ResolveHelperCardIndex(helper);

    // [rule_based] Heuristic 경로 — 담당자(assignee) 시점 rule-based 정책.
    //   All-rule-based 시뮬레이션 모드에서 4명 전원이 HeuristicOnly로 설정되었을 때
    //   담당자도 자기 task를 달성하려는 결정론적 정책으로 플레이.
    public int HeuristicAssigneeCardIndex(CrewAgent assignee)
    {
        if (Phase != TrainingMode.Phase1_CoopSingle) return -1;
        if (phase1Assignee == null || assignee != phase1Assignee) return -1;
        if (tasks.Count == 0) return -1;

        var tm = GameManager.Instance.trickManager;
        if (tm == null) return -1;

        var ctx = BuildHelperContext(assignee, tm);
        return RuleBasedHelper.DecideAssignee(in ctx).cardIndex;
    }

    // ─────────────────────────────────────────────────────────────
    // [MCTS Phase 1] MCTSContext 빌더 — 담당자가 MCTS로 카드 결정 시 사용.
    //   실제 게임 상태(TrickManager/MissionManager)를 MCTS 입력으로 변환.
    // ─────────────────────────────────────────────────────────────
    public MCTSContext BuildMCTSContext(CrewAgent self)
    {
        var tm = GameManager.Instance.trickManager;
        if (tm == null) return null;
        var players = GameManager.Instance.players;
        int n = players.Count;

        // 핸드 사이즈 (이번 트릭에 카드 낸 사람은 -1 이미 적용된 상태)
        var handSizes = new int[n];
        for (int i = 0; i < n; i++) handSizes[i] = players[i].hand.Count;

        // 합법 액션 (self 손패 인덱스 기준)
        var legal = new List<int>();
        for (int i = 0; i < self.hand.Count; i++)
            if (tm.IsValidPlay(self, self.hand[i])) legal.Add(i);

        // 알려진 voids 복제
        var voids = new HashSet<Card.Suit>[n];
        for (int i = 0; i < n; i++)
        {
            voids[i] = new HashSet<Card.Suit>();
            foreach (Card.Suit s in System.Enum.GetValues(typeof(Card.Suit)))
                if (tm.IsKnownVoid(players[i], s)) voids[i].Add(s);
        }

        // 통신 공개 정보 수집 (신념 제약) — 사용된 통신 토큰의 공개 카드/위치
        var commReveals = new List<CommReveal>();
        var cm = GameManager.Instance.communicationManager;
        if (cm != null)
        {
            for (int i = 0; i < n; i++)
            {
                var tok = cm.GetCommToken(players[i]);
                if (tok == null || !tok.isUsed || tok.revealedCard == null) continue;
                commReveals.Add(new CommReveal
                {
                    playerIdx   = i,
                    card        = tok.revealedCard,
                    position    = tok.revealPosition,
                    hasPosition = !tok.IsDeadZone,
                });
            }
        }

        // 다중 태스크 → MctsTask 변환 (미완료 태스크만 의미 있음)
        var mctsTasks = new List<MctsTask>();
        int doneCount = 0;
        foreach (var t in tasks)
        {
            if (t.targetCard == null) continue;
            int owner = players.IndexOf(t.assignedTo);
            if (owner < 0) continue;
            mctsTasks.Add(new MctsTask
            {
                ownerIdx  = owner,
                target    = t.targetCard,
                order     = t.orderToken,
                completed = t.isCompleted,
                failed    = t.isFailed,
            });
            if (t.isCompleted) doneCount++;
        }

        return new MCTSContext
        {
            selfIdx                = players.IndexOf(self),
            selfHand               = new List<Card>(self.hand),
            legalActionsInSelfHand = legal,
            playedCards            = new HashSet<Card>(playedCardsInHand),
            tableCards             = new List<Card>(tm.cardsOnTable),
            tablePlayers           = ListPlayersOnTable(tm, players),
            leadSuit               = tm.leadSuit,
            currentPlayer          = players.IndexOf(self),   // 본인 차례
            trickNumber            = trickNumber,             // 0-indexed
            totalTricks            = n > 0 ? 40 / n : 10,
            trickWinCounts         = BuildTrickWinCountsArray(players),
            firstTrickWinner       = firstTrickWinner != null ? players.IndexOf(firstTrickWinner) : -1,
            lastTrickWinner        = lastTrickWinner  != null ? players.IndexOf(lastTrickWinner)  : -1,
            tasks                  = mctsTasks,
            globalRule             = currentMission != null ? currentMission.globalRule : GlobalMissionRule.None,
            completedCount         = doneCount,
            rocketWinsMax          = 0,
            knownVoids             = voids,
            handSizes              = handSizes,
            commReveals            = commReveals,
        };
    }

    private List<int> ListPlayersOnTable(TrickManager tm, List<CrewAgent> players)
    {
        var result = new List<int>();
        foreach (var p in tm.playersOnTable) result.Add(players.IndexOf(p));
        return result;
    }

    private int[] BuildTrickWinCountsArray(List<CrewAgent> players)
    {
        var arr = new int[players.Count];
        for (int i = 0; i < players.Count; i++)
            arr[i] = GetTrickWinCount(players[i]);
        return arr;
    }

    // 공통 핵심 — HFSM(Hierarchical FSM)으로 도우미 카드 선택.
    //   Layer 1: task type (per-episode) → IHelperStrategy
    //   Layer 2: 트릭 상황 (per-card)    → HelperAction (Throw/Burn/Block/Save)
    //   Layer 3: action → 손패 인덱스 변환
    //
    // 상위/하위 state는 RuleBasedHelper.cs에 분리. 여기서는 context만 만들어 위임.
    private int ResolveHelperCardIndex(CrewAgent helper)
    {
        if (Phase != TrainingMode.Phase1_CoopSingle) return -1;
        if (phase1Assignee == null || helper == phase1Assignee) return -1;
        if (tasks.Count == 0) return -1;

        var tm = GameManager.Instance.trickManager;
        if (tm == null) return -1;

        var ctx = BuildHelperContext(helper, tm);
        return RuleBasedHelper.Decide(in ctx).cardIndex;
    }

    // HelperContext 통합 빌더 — helper/assignee dispatch 양쪽에서 재사용.
    //   업그레이드된 정책(remainingTricks / isLastToPlay) 필드도 일관되게 채움.
    private HelperContext BuildHelperContext(CrewAgent self, TrickManager tm)
    {
        var players = GameManager.Instance.players;
        int playerCount = players.Count;
        Card target = tasks.Count > 0 ? tasks[0].targetCard : null;

        return new HelperContext
        {
            helper           = self,
            assignee         = phase1Assignee,
            trickManager     = tm,
            isLeading        = tm.cardsOnTable.Count == 0,
            assigneePlayed   = tm.HasPlayerPlayedThisTrick(phase1Assignee),
            assigneeWinning  = tm.HasPlayerPlayedThisTrick(phase1Assignee)
                               && tm.IsPlayerCurrentlyWinning(phase1Assignee),
            isLastTrick      = tm.IsLastTrickInProgress(),
            isFirstTrick     = isFirstTrick,
            isLastToPlay     = tm.cardsOnTable.Count == playerCount - 1,
            targetCard       = target,
            iHoldTarget      = target != null && self.hand.Contains(target),
            targetOnTable    = target != null && tm.cardsOnTable.Contains(target),
            playedCardsInHand = playedCardsInHand,
        };
    }

    // 도우미의 매 플레이 평가 (TrickManager가 플레이 시점에 호출)
    //   couldWin : 이 카드가 현재 테이블 기준 이길 수 있는 카드인가
    //   hadSafe  : 합법 카드 중 '안 이기는' 선택지가 있었는가
    //   → 자발적 경쟁(couldWin && hadSafe): 질 수 있었는데 굳이 이기러 감
    public void RecordHelperPlay(CrewAgent player, bool couldWin, bool hadSafe)
    {
        if (Phase != TrainingMode.Phase1_CoopSingle) return;
        if (phase1Assignee == null || player == phase1Assignee) return;     // 도우미만
        if (tasks.Count == 0 || tasks[0].isCompleted || tasks[0].isFailed) return; // task 미해결일 때만
        epHelperPlays++;
        if (couldWin && hadSafe) epVoluntaryContests++;   // 통계만 (보상은 POCA group reward)
    }

    // ---------------------------------------------------------------
    // 관찰 벡터 (162개) — 룰북상 모든 태스크는 공개정보
    //   16슬롯 × 10피처 (160) + viewer 본인 완료·실패 비율 (2)
    //   슬롯 배치: [0..3]=viewer, [4..7]=viewer+1, [8..11]=viewer+2, [12..15]=viewer+3
    //   (viewer 기준 시계방향)
    //   slot[b+0] targetCard.suit  (/ 4)
    //   slot[b+1] targetCard.value (/ 9)
    //   slot[b+2] orderToken 전체 (None~Arrow4) (/ 10)
    //   slot[b+3] isCompleted
    //   slot[b+4] isFailed
    //   slot[b+5..9] 예비 (0)
    // ---------------------------------------------------------------
    public const int TaskObservationSize = 162;

    // 특수 규칙 관찰 (32, 예약) — mission-level. Phase A엔 전부 0, Phase B/C에서 채움.
    //   "미션마다 one-hot"이 아니라 메커니즘 카테고리 + 파라미터로 인코딩(미션 md 19종 수용).
    //   계획 레이아웃:
    //   통신:   [0]데드존 [1]통신차단 재개트릭(/10) [2]특정인 통신불가
    //   드래프트:[3]사령관 결정 [4]사령관 분배 [5]순서토큰 이동/교환 허용
    //   승리조건:[6]9 트릭불가 [7]로켓 승리불가 [8]2트릭차 금지 [9]로켓 오름차순
    //           [10]사령관 첫·마지막 [11]첫·마지막만(로켓없이) [12]특정값 승리필요 [13]그 값(/9)
    //           [14]로켓당 1트릭 [15]특정"1"카드 트릭승리 [16]특정 색 전담 [17]그 색(/4)
    //           [18]첫N트릭 전담 [19]N(/10) [20]마지막트릭 전담
    //   흐름:   [21]첫 트릭 후 카드 전달
    //   [22..31] 예비
    public const int SpecialRuleObsSize = 32;
    public float[] GetSpecialRuleObs() => new float[SpecialRuleObsSize];

    public float[] GetTaskObservationFor(CrewAgent viewer)
    {
        float[] obs = new float[TaskObservationSize];
        var players = GameManager.Instance.players;
        int viewerIdx = players.IndexOf(viewer);
        if (viewerIdx < 0) viewerIdx = 0;

        int viewerCompleted = 0, viewerFailed = 0, viewerTaskCount = 0;

        for (int p = 0; p < 4; p++)
        {
            int actualIdx = (viewerIdx + p) % players.Count;
            CrewAgent owner = players[actualIdx];
            List<TaskCard> ownerTasks = tasks.FindAll(t => t.assignedTo == owner);

            for (int slot = 0; slot < 4 && slot < ownerTasks.Count; slot++)
            {
                TaskCard task = ownerTasks[slot];
                int b = (p * 4 + slot) * 10;
                obs[b + 0] = task.targetCard != null ? (int)task.targetCard.suit / 4f : 0f;
                obs[b + 1] = task.targetCard != null ? task.targetCard.value / 9f    : 0f;
                obs[b + 2] = (int)task.orderToken / 10f;   // 순서토큰 전체(None~Arrow4)
                obs[b + 3] = task.isCompleted ? 1f : 0f;
                obs[b + 4] = task.isFailed    ? 1f : 0f;
                // obs[b + 5..9] : 예비 (0으로 유지)

                if (owner == viewer)
                {
                    viewerTaskCount++;
                    if (task.isCompleted) viewerCompleted++;
                    if (task.isFailed)    viewerFailed++;
                }
            }
        }

        float norm = Mathf.Max(viewerTaskCount, 1);
        obs[160] = viewerCompleted / norm;
        obs[161] = viewerFailed    / norm;
        return obs;
    }

    // ---------------------------------------------------------------
    // 유틸
    // ---------------------------------------------------------------

    // WinSpecificCard 태스크 생성 — refPlayer 손패의 색깔 카드(없으면 풀)에서 타깃 1장 선택.
    //   타깃 카드는 색깔 카드 36장 중 하나(로켓 제외)이며 반드시 누군가의 손에 있다.
    private TaskCard CreateUnassignedTask(CrewAgent refPlayer, HashSet<Card> used)
    {
        Card target = PickCardFromHand(refPlayer.hand, used) ?? PickCardFromPool(used);
        if (target == null) return null;
        return TaskCard.SpecificCard(target);
    }

    private Card PickCardFromHand(List<Card> hand, HashSet<Card> used)
    {
        List<Card> available = hand.FindAll(c => c.suit != Card.Suit.Rocket && !used.Contains(c));
        if (available.Count == 0) return null;
        return available[Random.Range(0, available.Count)];
    }

    private Card PickCardFromPool(HashSet<Card> used)
    {
        List<Card> pool = new List<Card>();
        for (int s = 0; s < 4; s++)
            for (int v = 1; v <= 9; v++)
            {
                Card c = new Card((Card.Suit)s, v);
                if (!used.Contains(c)) pool.Add(c);
            }
        if (pool.Count == 0) return null;
        return pool[Random.Range(0, pool.Count)];
    }

    private void LogTaskSummary()
    {
        foreach (var t in tasks)
            Debug.Log($"[Mission] {t.assignedTo.name} → {t}");
    }
}

