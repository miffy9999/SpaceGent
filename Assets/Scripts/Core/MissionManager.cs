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

        // [Phase1] 선택 단계를 건너뛰고 WinSpecificCard 태스크 1개를 담당자에게 배정 후 즉시 시작
        if (Phase != TrainingMode.Normal)
        {
            currentMaxDifficulty = 0;
            var ep = Academy.Instance.EnvironmentParameters;
            scriptedHelpers    = ep.GetWithDefault("scripted_helpers", 0f) > 0.5f;
            bool fixedAssignee = ep.GetWithDefault("fixed_assignee", 0f) > 0.5f;
            epHelperPlays = epVoluntaryContests = 0;
            // fixed_assignee=1이면 player[0] 고정(HeuristicOnly 도우미와 짝) — 단일 에이전트 격리
            phase1Assignee = fixedAssignee ? players[0] : players[Random.Range(0, players.Count)];

            // WinSpecificCard: 색깔 카드 36장(로켓 제외) 중 1장을 타깃으로 추첨.
            //   36장은 전부 분배되므로 타깃 카드는 반드시 누군가의 손에 있다.
            Card target = new Card((Card.Suit)Random.Range(0, 4), Random.Range(1, 10));
            var t = TaskCard.SpecificCard(target);
            t.assignedTo = phase1Assignee;
            tasks.Add(t);
            epTargetHeldByAssignee = phase1Assignee.hand.Contains(target);

            RuleBasedHelper.ResetEpisodeStats();
            LogTaskSummary();
            GameManager.Instance.uiManager?.HideTaskSelection();
            GameManager.Instance.trickManager.StartPlaying();
            return;
        }

        // 미션 선택 (커리큘럼)
        currentMaxDifficulty = Mathf.RoundToInt(
            Academy.Instance.EnvironmentParameters.GetWithDefault("difficulty", 9f));
        currentMission = database != null ? database.GetByMaxDifficulty(currentMaxDifficulty) : null;

        // 태스크 풀 생성 (미배정)
        if (currentMission != null)
        {
            Debug.Log($"[Mission] 난이도 상한={currentMaxDifficulty} → {currentMission.id} 선택");
            GenerateTaskPool(currentMission, captainIndex);
        }
        else
        {
            GenerateFallbackPool(captainIndex);
        }

        // 실제 스페이스 크루 규칙: 함장부터 시계방향으로 모든 플레이어가 선택 (함장 포함)
        selectionOrder.Clear();
        for (int i = 0; i < players.Count; i++)
            selectionOrder.Add((captainIndex + i) % players.Count);
        selectionCursor = 0;

        Debug.Log($"[Mission] 태스크 풀 {taskPool.Count}개 생성, 선택 시작");

        // UI 표시 후 AI 자동 선택 진행
        GameManager.Instance.uiManager?.ShowTaskSelection();
        AdvanceUntilHumanOrDone();
    }

    // ---------------------------------------------------------------
    // 태스크 풀 생성 (미배정 상태)
    // ---------------------------------------------------------------
    private void GenerateTaskPool(Mission mission, int captainIndex)
    {
        var players = GameManager.Instance.players;
        HashSet<Card> used = new HashSet<Card>();

        // 실제 스페이스 크루: 함장도 태스크를 가질 수 있으므로 모든 플레이어 손패 참고
        int total = mission.TotalTaskCount;
        for (int i = 0; i < total; i++)
        {
            int pIdx = (captainIndex + (i % players.Count)) % players.Count;
            TaskCard task = CreateUnassignedTask(players[pIdx], used);
            if (task == null) break;
            taskPool.Add(task);
            if (task.targetCard != null)
                used.Add(task.targetCard);
        }
    }

    private void GenerateFallbackPool(int captainIndex)
    {
        var players = GameManager.Instance.players;
        HashSet<Card> used = new HashSet<Card>();

        // 모든 플레이어가 최소 1개씩 선택할 수 있도록 players.Count개 생성
        for (int i = 0; i < players.Count; i++)
        {
            int pIdx = (captainIndex + i) % players.Count;
            TaskCard task = CreateUnassignedTask(players[pIdx], used);
            if (task == null) continue;
            taskPool.Add(task);
            if (task.targetCard != null)
                used.Add(task.targetCard);
        }
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

    // ---------------------------------------------------------------
    // 인간 플레이어가 태스크 풀 인덱스를 선택
    // ---------------------------------------------------------------
    public void HumanPickTask(int poolIndex)
    {
        var human = GameManager.Instance.players[0];
        if (GetCurrentPickingPlayer() != human)
        {
            Debug.LogWarning("[Mission] 인간의 선택 차례가 아닙니다.");
            return;
        }
        if (poolIndex < 0 || poolIndex >= taskPool.Count) return;

        AssignTask(poolIndex, human);
        AdvanceSelection();
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

    // ---------------------------------------------------------------
    // 다음 선택자로 이동; AI면 자동 선택, 모두 완료면 게임 시작
    // ---------------------------------------------------------------
    private void AdvanceSelection()
    {
        selectionCursor++;
        AdvanceUntilHumanOrDone();
    }

    private void AdvanceUntilHumanOrDone()
    {
        int safety = 0;
        while (taskPool.Count > 0)
        {
            if (++safety > 500)
            {
                Debug.LogError($"[Mission] AdvanceUntilHumanOrDone 무한루프 감지! " +
                               $"pool={taskPool.Count} cursor={selectionCursor} order={selectionOrder.Count} → 강제 종료");
                break;
            }

            var current = GetCurrentPickingPlayer();
            if (current == null)
            {
                Debug.LogError($"[Mission] GetCurrentPickingPlayer() null → cursor={selectionCursor}");
                break;
            }

            // 인간 플레이어 차례 → UI 갱신 후 대기
            if (current.isHumanPlayer)
            {
                Debug.Log($"[Mission] 인간 차례 — 태스크 풀 {taskPool.Count}개, 키 1~{taskPool.Count} 또는 버튼으로 선택");
                GameManager.Instance.uiManager?.RefreshTaskSelection();
                return;
            }

            // AI 자동 선택
            int randIdx = Random.Range(0, taskPool.Count);
            AssignTask(randIdx, current);
            selectionCursor++;
        }

        // 풀이 비었으면 선택 완료
        CompleteTaskSelection();
    }

    private void CompleteTaskSelection()
    {
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

        // ── 태스크 판정 (WinSpecificCard) ───────────────────────────────
        foreach (TaskCard task in tasks)
        {
            if (missionEnded) break; // 순서 토큰 위반 등으로 미션 종료 시 즉시 탈출
            if (task.isCompleted || task.isFailed) continue;

            // WinSpecificCard: targetCard가 이번 트릭에 포함되면 승자로 완료/실패 즉시 판정
            if (trickCards.Contains(task.targetCard))
            {
                if (winner == task.assignedTo) TryCompleteTask(task, snapCompleted, completingNow);
                else                           FailTask(task);
            }
        }
        if (missionEnded) return;

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
            EvaluationStats.RecordEpisode(taskSuccess, epTargetHeldByAssignee);
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
        if (Phase != TrainingMode.Normal) return;   // 미션 ±2.0은 Normal에서만 (Phase0/1은 태스크-레벨 보상 사용)
        float reward = success ? RewardMissionWin : PenaltyMissionFail;
        foreach (var p in GameManager.Instance.players)
            p.AddReward(reward);
        Debug.Log($"[Mission] 미션 {(success ? "성공" : "실패")} 팀 보상 {reward}");
        GameManager.Instance.uiManager?.ShowResult(success);
    }

    // ---------------------------------------------------------------
    // 상태 조회
    // ---------------------------------------------------------------
    public bool IsMissionComplete()
    {
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

    // ── Phase1 역할 조회 (관찰/계측용) ─────────────────────────────
    public bool IsPhase1Assignee(CrewAgent a)
        => Phase == TrainingMode.Phase1_CoopSingle && a == phase1Assignee;

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
        if (Phase != TrainingMode.Phase1_CoopSingle) return null;
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
            assigneeIdx            = phase1Assignee != null ? players.IndexOf(phase1Assignee) : -1,
            targetCard             = tasks.Count > 0 ? tasks[0].targetCard : null,
            taskCompleted          = tasks.Count > 0 && tasks[0].isCompleted,
            taskFailed             = tasks.Count > 0 && tasks[0].isFailed,
            knownVoids             = voids,
            handSizes              = handSizes,
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
    //   slot[b+2] orderIndex (/ 5)
    //   slot[b+3] isCompleted
    //   slot[b+4] isFailed
    //   slot[b+5..9] 예비 (0)
    // ---------------------------------------------------------------
    public const int TaskObservationSize = 162;

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
                obs[b + 2] = task.orderIndex / 5f;
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
