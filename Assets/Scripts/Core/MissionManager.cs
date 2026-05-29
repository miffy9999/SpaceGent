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

    // 연속 트릭 추적 (WinConsecutive / WinNoConsecutive)
    private Dictionary<CrewAgent, int> consecutiveWins = new Dictionary<CrewAgent, int>();

    // 슈트별 카드 획득 수 [slotIndex = (int)Card.Suit] (WinNoSuit, WinMoreSuitThan 등)
    private Dictionary<CrewAgent, int[]> suitCardCounts = new Dictionary<CrewAgent, int[]>();

    // 첫/마지막 트릭 승자
    private CrewAgent firstTrickWinner;
    private CrewAgent lastTrickWinner;

    // 현재 트릭 번호 (1부터 시작, WinNoneFirstN 용)
    private int trickNumber = 0;

    private bool isFirstTrick = true;
    private bool missionEnded = false;
    public bool HasMissionEnded => missionEnded;

    // 현 미션의 난이도 상한 (커리큘럼) — 태스크 타입 필터링에 사용
    private int currentMaxDifficulty = 9;

    // 보상 상수 (terminal)
    private const float RewardTaskComplete = 1.0f;
    private const float RewardMissionWin   = 2.0f;
    private const float PenaltyTaskFail    = -1.0f;
    private const float PenaltyMissionFail = -2.0f;

    // 보상 shaping (per-trick, terminal의 10% 이하로 작게 유지)
    //   목적: 핸드 종료까지 신호가 없는 long-horizon 태스크들에
    //         매 트릭 진척 신호를 주어 PPO의 credit assignment 부담을 줄임.
    private const float ShapeOnTrack  =  0.05f;
    private const float ShapeOffTrack = -0.05f;

    // ───────────────────────────────────────────────────────────────
    // [임시] 단계적 학습 모드 (검증 후 Normal로 복귀)
    //   Normal            : 실제 게임 (커리큘럼/미션보상/팀보너스 정식)
    //   Phase0_Individual : 전원 WinAtLeast(N) + 개별 보상만. RL 배선 검증용.
    //                       → 통과(엔트로피↓, value loss 수렴). 단 대칭 천장으로 reward 평탄.
    //   Phase1_CoopSingle : 무작위 1명에게만 WinAtLeast(N), 보상은 팀 결합(완료 전원+1/실패 전원-1).
    //                       → 나머지가 "져주는" 협력이 학습되면 reward가 baseline 위로 상승.
    // ───────────────────────────────────────────────────────────────
    public enum TrainingMode { Normal, Phase0_Individual, Phase1_CoopSingle }
    public static readonly TrainingMode Phase = TrainingMode.Phase1_CoopSingle;
    //   2026-05-28 측정: 담당자 평균 wins ≈ 2.94 → win_target=4는 천장 초과(38%).
    //   3으로 낮춰 천장 ~55% 확보. 자세한 근거는 Rule Base Model Upgrade/policy.md §11 참고.
    private const int          SanityTaskCount = 3;

    // [rule_based] 축소 게임 task 4종 — Rule-based helper가 분기 가능한 task만
    public enum Phase1Task { WinAtLeast, WinFirst, WinLast, WinNone }

    // Phase1 런타임(에피소드별) + 계측 통계 (보상은 MA-POCA group reward가 담당)
    private CrewAgent phase1Assignee;     // 이번 에피소드 담당자(나머지는 도우미)
    private int   phase1Target;           // 담당자 목표 트릭 수 (win_target)
    private Phase1Task phase1TaskType;    // 이번 에피소드 task 종류 (4종 중 1개)
    private bool  scriptedHelpers;        // [진단] 도우미 강제 져주기 — 협력 천장 측정용
    private int   epSteals, epHelperPlays, epVoluntaryContests;   // 에피소드 통계

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
        consecutiveWins.Clear();
        suitCardCounts.Clear();
        isFirstTrick = true;
        missionEnded = false;
        firstTrickWinner = null;
        lastTrickWinner  = null;
        trickNumber = 0;
        GameManager.Instance.uiManager?.HideResult();

        int suitCount = System.Enum.GetValues(typeof(Card.Suit)).Length;
        var players = GameManager.Instance.players;
        foreach (var p in players)
        {
            trickWinCounts[p]  = 0;
            consecutiveWins[p] = 0;
            suitCardCounts[p]  = new int[suitCount];
        }

        // [v3 카드 메모리] 새 핸드 시작 — 이전 핸드의 played cards 비움
        playedCardsInHand.Clear();

        // [Phase0/1] 선택 단계를 건너뛰고 태스크를 직접 배정 후 즉시 시작
        if (Phase != TrainingMode.Normal)
        {
            currentMaxDifficulty = 0;
            if (Phase == TrainingMode.Phase1_CoopSingle)
            {
                // 무작위 1명에게만 부여 → 나머지 3명은 도우미(태스크 없음)
                //   N·보상 magnitude·패널티 방식 모두 env 파라미터로 → 빌드 1개로 A/B 병렬
                var ep = Academy.Instance.EnvironmentParameters;
                phase1Target    = Mathf.RoundToInt(ep.GetWithDefault("win_target", SanityTaskCount));
                scriptedHelpers = ep.GetWithDefault("scripted_helpers", 0f) > 0.5f;
                bool fixedAssignee = ep.GetWithDefault("fixed_assignee", 0f) > 0.5f;
                epSteals = epHelperPlays = epVoluntaryContests = 0;
                // [A2] fixed_assignee=1이면 player[0] 고정(HeuristicOnly 도우미와 짝) — 단일 에이전트 격리
                phase1Assignee = fixedAssignee ? players[0] : players[Random.Range(0, players.Count)];

                // [rule_based] 3종 task 중 1개 랜덤 선택 (env 파라미터로 고정 가능)
                //   task_type: -1=랜덤(기본, WinNone 제외), 0=WinAtLeast, 1=WinFirst, 2=WinLast, 3=WinNone
                //   2026-05-28 측정: WinNone 천장 11.6% (fast-fail 구조 한계) → 학습 환경에서 제외.
                //   단일 실험용으로 task_type=3 명시 시에만 WinNone 활성. 근거: policy.md §11.3
                int taskTypeSel = Mathf.RoundToInt(ep.GetWithDefault("task_type", -1f));
                if (taskTypeSel < 0 || taskTypeSel > 3)
                    taskTypeSel = Random.Range(0, 3);   // 0~2 (WinAtLeast/First/Last)
                phase1TaskType = (Phase1Task)taskTypeSel;

                // HFSM action 카운트 초기화 (도우미 의도 분포 측정용)
                RuleBasedHelper.ResetEpisodeStats();

                TaskCard t = phase1TaskType switch
                {
                    Phase1Task.WinFirst => TaskCard.First(),
                    Phase1Task.WinLast  => TaskCard.Last(),
                    Phase1Task.WinNone  => TaskCard.None(),
                    _                   => TaskCard.AtLeast(phase1Target),   // WinAtLeast
                };
                t.assignedTo = phase1Assignee;
                tasks.Add(t);
            }
            else // Phase0_Individual: 전원에게
            {
                foreach (var p in players)
                {
                    var t = TaskCard.AtLeast(SanityTaskCount);
                    t.assignedTo = p;
                    tasks.Add(t);
                }
            }
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
            if (task.type == TaskCard.TaskType.WinSpecificCard && task.targetCard != null)
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
            if (task.type == TaskCard.TaskType.WinSpecificCard && task.targetCard != null)
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

        // 연속 트릭
        foreach (var p in GameManager.Instance.players)
            consecutiveWins[p] = (p == winner) ? consecutiveWins[p] + 1 : 0;

        // 슈트별 카드 획득 수 (winner가 이긴 트릭의 모든 카드)
        foreach (Card c in trickCards)
            suitCardCounts[winner][(int)c.suit]++;

        // 마지막 트릭 여부: 카드를 낸 후 호출되므로 hand.Count == 0이면 마지막
        bool isLastTrick = GameManager.Instance.players[0].hand.Count == 0;

        // ── 태스크 판정 ─────────────────────────────────────────────────
        foreach (TaskCard task in tasks)
        {
            if (missionEnded) break; // 순서 토큰 위반 등으로 미션 종료 시 즉시 탈출
            if (task.isCompleted || task.isFailed) continue;

            switch (task.type)
            {
                // ── 기존 타입 ─────────────────────────────────────────
                case TaskCard.TaskType.WinSpecificCard:
                    if (trickCards.Contains(task.targetCard))
                    {
                        if (winner == task.assignedTo) TryCompleteTask(task, snapCompleted, completingNow);
                        else FailTask(task);
                    }
                    break;

                case TaskCard.TaskType.WinFirst:
                    if (isFirstTrick)
                    {
                        if (winner == task.assignedTo) TryCompleteTask(task, snapCompleted, completingNow);
                        else FailTask(task);
                    }
                    break;

                case TaskCard.TaskType.WinNone:
                    if (winner == task.assignedTo) FailTask(task);
                    break;

                case TaskCard.TaskType.WinTrickCount:
                    if (trickWinCounts[task.assignedTo] > task.requiredCount)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinLast:
                    break; // OnHandEnded에서 처리

                case TaskCard.TaskType.WinConsecutive:
                    if (consecutiveWins[task.assignedTo] >= task.requiredConsecutive)
                        TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                case TaskCard.TaskType.WinNoConsecutive:
                    if (winner == task.assignedTo && consecutiveWins[task.assignedTo] >= 2)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinOnlyFirst:
                    if (isFirstTrick) { if (winner != task.assignedTo) FailTask(task); }
                    else              { if (winner == task.assignedTo) FailTask(task); }
                    break;

                case TaskCard.TaskType.WinOnlyLast:
                    if (!isLastTrick && winner == task.assignedTo) FailTask(task);
                    break;

                case TaskCard.TaskType.WinFirstAndLast:
                    if (isFirstTrick && winner != task.assignedTo) { FailTask(task); break; }
                    if (isLastTrick)
                    {
                        if (winner == task.assignedTo && firstTrickWinner == task.assignedTo)
                            TryCompleteTask(task, snapCompleted, completingNow);
                        else
                            FailTask(task);
                    }
                    break;

                // ── 슈트 관련 ─────────────────────────────────────────
                case TaskCard.TaskType.WinNoSuit:
                    if (winner == task.assignedTo && trickLeadSuit == task.targetSuit)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinNoOpenSuit:
                    if (opener == task.assignedTo && openerSuit == task.targetSuit)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinMoreSuitThan:
                    break; // OnHandEnded에서 처리

                case TaskCard.TaskType.WinExactSuitCount:
                    if (suitCardCounts[task.assignedTo][(int)task.targetSuit] > task.requiredCount)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinEachColor:
                    break; // OnHandEnded에서 처리

                // ── 트릭 수 관련 ──────────────────────────────────────
                case TaskCard.TaskType.WinAtLeast:
                    break; // OnHandEnded에서 처리

                case TaskCard.TaskType.WinNoneFirstN:
                    if (winner == task.assignedTo && trickNumber <= task.requiredCount)
                        FailTask(task);
                    break;

                // ── 카드 값 관련 ──────────────────────────────────────
                case TaskCard.TaskType.WinOddTrick:
                    if (winner == task.assignedTo && AllValuesMatch(trickCards, v => v % 2 == 1))
                        TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                case TaskCard.TaskType.WinEvenTrick:
                    if (winner == task.assignedTo && AllValuesMatch(trickCards, v => v % 2 == 0))
                        TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                // ── 상대 비교 ─────────────────────────────────────────
                case TaskCard.TaskType.WinRelativeFewer:
                case TaskCard.TaskType.WinRelativeMore:
                    break; // OnHandEnded에서 처리
            }
        }
        if (missionEnded) return;

        // ── 매-트릭 shaping 보상 ─────────────────────────────────────────
        //   목적: OnHandEnded에서만 판정되는 long-horizon 태스크들에 매 트릭
        //         진척 신호를 흘려 PPO의 credit assignment 부담을 줄임.
        //   주의: terminal 보상(±1, ±2)의 ~5% 크기로 작게 유지해 dominance 보존.
        ApplyTrickShaping(winner, trickCards);

        isFirstTrick = false;

        if (!missionEnded && IsMissionFailed())
        {
            missionEnded = true;
            GiveTeamReward(success: false);
        }
    }

    // ---------------------------------------------------------------
    // 트릭 단위 shaping — 진행 중 태스크에 대해 진척/역행 신호 부여
    // ---------------------------------------------------------------
    private void ApplyTrickShaping(CrewAgent winner, List<Card> trickCards)
    {
        foreach (TaskCard task in tasks)
        {
            if (task.isCompleted || task.isFailed) continue;
            bool iWonThis = (winner == task.assignedTo);

            switch (task.type)
            {
                // 카운트 기반: 핸드 종료 시에만 판정 → 진행 중 진척 신호 필수
                case TaskCard.TaskType.WinAtLeast:
                    if (Phase == TrainingMode.Phase1_CoopSingle)
                    {
                        // 진척(담당자가 필요 트릭 획득)은 그룹/학습자 보상
                        if (iWonThis && trickWinCounts[task.assignedTo] <= task.requiredCount)
                            GameManager.Instance.AddGroupOrLearnerReward(ShapeOnTrack);
                        else if (!iWonThis && trickWinCounts[task.assignedTo] < task.requiredCount)
                            epSteals++;   // 통계만 (수제 패널티 제거 — POCA가 처리)
                    }
                    // Phase0/Normal: 담당자 진척 시 기존 team shaping(±0.05)
                    else if (iWonThis && trickWinCounts[task.assignedTo] <= task.requiredCount)
                        AddTeamShaping(task.assignedTo, ShapeOnTrack);
                    break;

                case TaskCard.TaskType.WinTrickCount:
                    // 정확히 N트릭 → 미달이면 진척, 초과는 이미 즉시 실패 처리됨
                    int cur = trickWinCounts[task.assignedTo];
                    if (iWonThis && cur <= task.requiredCount)
                        AddTeamShaping(task.assignedTo, ShapeOnTrack);
                    break;

                case TaskCard.TaskType.WinNone:
                    // 한 번도 이기면 안 됨 → 이번 트릭 진 것도 진척 신호
                    //   (이기면 즉시 FailTask로 -1.0이 들어가니 여기선 +만 줌)
                    if (!iWonThis) AddTeamShaping(task.assignedTo, ShapeOnTrack);
                    break;

                case TaskCard.TaskType.WinLast:
                    // 마지막을 이겨야 함 → 중간 트릭 이기면 약한 - (마지막에 못 잡을 위험)
                    if (iWonThis) AddTeamShaping(task.assignedTo, ShapeOffTrack * 0.5f);
                    break;

                // ── Stage2~ 추가 타입들 ─────────────────────────────────
                case TaskCard.TaskType.WinRelativeFewer:
                    if (!iWonThis) AddTeamShaping(task.assignedTo, ShapeOnTrack * 0.5f);
                    else           AddTeamShaping(task.assignedTo, ShapeOffTrack * 0.5f);
                    break;

                case TaskCard.TaskType.WinRelativeMore:
                    if (iWonThis)  AddTeamShaping(task.assignedTo, ShapeOnTrack * 0.5f);
                    else           AddTeamShaping(task.assignedTo, ShapeOffTrack * 0.5f);
                    break;

                case TaskCard.TaskType.WinMoreSuitThan:
                    if (iWonThis)
                    {
                        int a = suitCardCounts[task.assignedTo][(int)task.targetSuit];
                        int b = suitCardCounts[task.assignedTo][(int)task.suitB];
                        if      (a >  b) AddTeamShaping(task.assignedTo, ShapeOnTrack * 0.5f);
                        else if (a <  b) AddTeamShaping(task.assignedTo, ShapeOffTrack * 0.5f);
                    }
                    break;

                case TaskCard.TaskType.WinEachColor:
                    // 처음 보는 색을 이긴 경우 진척 (suitCardCounts는 위에서 이미 업데이트됨)
                    if (iWonThis)
                    {
                        foreach (Card c in trickCards)
                        {
                            if (c.suit == Card.Suit.Rocket) continue;
                            if (suitCardCounts[task.assignedTo][(int)c.suit] == 1)
                            {
                                AddTeamShaping(task.assignedTo, ShapeOnTrack);
                                break;
                            }
                        }
                    }
                    break;
            }
        }
    }

    // 트릭의 모든 카드 값이 조건을 만족하는지 검사
    private bool AllValuesMatch(List<Card> cards, System.Func<int, bool> predicate)
    {
        foreach (Card c in cards)
            if (!predicate(c.value)) return false;
        return true;
    }

    // ---------------------------------------------------------------
    // 핸드 종료 시 최종 판정
    // ---------------------------------------------------------------
    public void OnHandEnded()
    {
        var players = GameManager.Instance.players;
        int snapCompleted = CountCompleted();
        var completingNow = new System.Collections.Generic.HashSet<TaskCard>();

        foreach (TaskCard task in tasks)
        {
            if (missionEnded) break;
            if (task.isCompleted || task.isFailed) continue;

            switch (task.type)
            {
                // ── 기존 타입 ────────────────────────────────────────
                case TaskCard.TaskType.WinSpecificCard:
                    FailTask(task);
                    break;

                case TaskCard.TaskType.WinTrickCount:
                    if (trickWinCounts[task.assignedTo] == task.requiredCount) TryCompleteTask(task, snapCompleted, completingNow);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinNone:
                    TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                case TaskCard.TaskType.WinFirst:
                    FailTask(task);
                    break;

                case TaskCard.TaskType.WinLast:
                    if (lastTrickWinner == task.assignedTo) TryCompleteTask(task, snapCompleted, completingNow);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinConsecutive:
                    FailTask(task);
                    break;

                case TaskCard.TaskType.WinNoConsecutive:
                    TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                case TaskCard.TaskType.WinOnlyFirst:
                    if (firstTrickWinner == task.assignedTo) TryCompleteTask(task, snapCompleted, completingNow);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinOnlyLast:
                    if (lastTrickWinner == task.assignedTo) TryCompleteTask(task, snapCompleted, completingNow);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinFirstAndLast:
                    FailTask(task);
                    break;

                // ── 슈트 관련 ────────────────────────────────────────
                case TaskCard.TaskType.WinNoSuit:
                    TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                case TaskCard.TaskType.WinNoOpenSuit:
                    TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                case TaskCard.TaskType.WinMoreSuitThan:
                {
                    int a = suitCardCounts[task.assignedTo][(int)task.targetSuit];
                    int b = suitCardCounts[task.assignedTo][(int)task.suitB];
                    if (a > b) TryCompleteTask(task, snapCompleted, completingNow);
                    else FailTask(task);
                    break;
                }

                case TaskCard.TaskType.WinExactSuitCount:
                    if (suitCardCounts[task.assignedTo][(int)task.targetSuit] == task.requiredCount)
                        TryCompleteTask(task, snapCompleted, completingNow);
                    else
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinEachColor:
                {
                    bool ok = suitCardCounts[task.assignedTo][(int)Card.Suit.Yellow] > 0
                           && suitCardCounts[task.assignedTo][(int)Card.Suit.Blue]   > 0
                           && suitCardCounts[task.assignedTo][(int)Card.Suit.Green]  > 0
                           && suitCardCounts[task.assignedTo][(int)Card.Suit.Pink]   > 0;
                    if (ok) TryCompleteTask(task, snapCompleted, completingNow);
                    else    FailTask(task);
                    break;
                }

                // ── 트릭 수 관련 ─────────────────────────────────────
                case TaskCard.TaskType.WinAtLeast:
                    if (trickWinCounts[task.assignedTo] >= task.requiredCount) TryCompleteTask(task, snapCompleted, completingNow);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinNoneFirstN:
                    TryCompleteTask(task, snapCompleted, completingNow);
                    break;

                // ── 카드 값 관련 ─────────────────────────────────────
                case TaskCard.TaskType.WinOddTrick:
                case TaskCard.TaskType.WinEvenTrick:
                    FailTask(task); // 완료는 OnTrickResolved에서만 가능
                    break;

                // ── 상대 비교 ────────────────────────────────────────
                case TaskCard.TaskType.WinRelativeFewer:
                {
                    int mine = trickWinCounts[task.assignedTo];
                    bool fewer = true;
                    foreach (var p in players)
                        if (p != task.assignedTo && trickWinCounts[p] <= mine)
                            { fewer = false; break; }
                    if (fewer) TryCompleteTask(task, snapCompleted, completingNow);
                    else       FailTask(task);
                    break;
                }

                case TaskCard.TaskType.WinRelativeMore:
                {
                    int mine = trickWinCounts[task.assignedTo];
                    bool more = true;
                    foreach (var p in players)
                        if (p != task.assignedTo && trickWinCounts[p] >= mine)
                            { more = false; break; }
                    if (more) TryCompleteTask(task, snapCompleted, completingNow);
                    else      FailTask(task);
                    break;
                }
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
            int wins = GetTrickWinCount(phase1Assignee);

            // task type별 실제 성공 여부 (CompleteTask로 결정된 isCompleted 기준)
            bool taskSuccess = tasks.Count > 0 && tasks[0].isCompleted;
            sr.Add("coop/assignee_success", taskSuccess ? 1f : 0f);

            // task type별 분리 stat — 어떤 task가 막혔는지 진단
            string tag = phase1TaskType.ToString();   // "WinAtLeast" / "WinFirst" / "WinLast" / "WinNone"
            sr.Add($"coop_task/{tag}_success", taskSuccess ? 1f : 0f);

            sr.Add("coop/assignee_wins", wins);
            sr.Add("coop/helper_steals", epSteals);
            if (epHelperPlays > 0)
                sr.Add("coop/voluntary_contest_rate", (float)epVoluntaryContests / epHelperPlays);

            // HFSM 도우미 action 분포 — 도우미가 의도대로 작동했는지 정량 검증
            //   예: WinLast인데 Burn 비율 < 50%면 도우미 로직 버그 의심
            if (RuleBasedHelper.CountTotal > 0)
            {
                float total = RuleBasedHelper.CountTotal;
                sr.Add($"hfsm/{tag}_throw_rate", RuleBasedHelper.CountThrow / total);
                sr.Add($"hfsm/{tag}_burn_rate",  RuleBasedHelper.CountBurn  / total);
                sr.Add($"hfsm/{tag}_block_rate", RuleBasedHelper.CountBlock / total);
                sr.Add($"hfsm/{tag}_save_rate",  RuleBasedHelper.CountSave  / total);
            }

            // [All-rule-based 시뮬레이션] 콘솔 누적 성공률 — 학습 없이 평가 모드
            EvaluationStats.RecordEpisode(phase1TaskType, taskSuccess, wins);
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

    // 진척 shaping 라우팅 (Phase별)
    //   Phase0_Individual : 담당자만
    //   Phase1_CoopSingle : 양수(진척)는 전원 동일 공유, 음수(역행)는 담당자만
    //   Normal            : 담당자 전액 + 팀원 1/4씩(양수만)
    private void AddTeamShaping(CrewAgent primary, float amount)
    {
        if (Phase == TrainingMode.Phase1_CoopSingle)
        {
            if (amount > 0f)
                foreach (var p in GameManager.Instance.players) p.AddReward(amount);
            else
                primary.AddReward(amount);
            return;
        }

        primary.AddReward(amount);
        if (Phase == TrainingMode.Normal && amount > 0f)
        {
            float share = amount * 0.25f;
            foreach (var p in GameManager.Instance.players)
                if (p != primary) p.AddReward(share);
        }
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

    public int Phase1AssigneeRemaining()
        => (Phase == TrainingMode.Phase1_CoopSingle && phase1Assignee != null)
           ? Mathf.Max(0, phase1Target - GetTrickWinCount(phase1Assignee)) : 0;

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
        return RuleBasedHelper.DecideAssignee(in ctx, phase1TaskType).cardIndex;
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
            taskType               = phase1TaskType,
            winTarget              = phase1Target,
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
        return RuleBasedHelper.Decide(in ctx, phase1TaskType).cardIndex;
    }

    // HelperContext 통합 빌더 — helper/assignee dispatch 양쪽에서 재사용.
    //   업그레이드된 정책(remainingTricks / isLastToPlay) 필드도 일관되게 채움.
    private HelperContext BuildHelperContext(CrewAgent self, TrickManager tm)
    {
        var players = GameManager.Instance.players;
        int playerCount = players.Count;
        int totalTricks = playerCount > 0 ? 40 / playerCount : 10;  // 4인=10, 3인=13 등

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
            myCurrentWins    = GetTrickWinCount(self),
            winTarget        = phase1Target,
            remainingTricks  = Mathf.Max(0, totalTricks - trickNumber),
            isLastToPlay     = tm.cardsOnTable.Count == playerCount - 1,
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
        if (GetTrickWinCount(phase1Assignee) >= phase1Target) return;        // 담당자 미달일 때만
        epHelperPlays++;
        if (couldWin && hadSafe) epVoluntaryContests++;   // 통계만 (보상은 POCA group reward)
    }

    // ---------------------------------------------------------------
    // 관찰 벡터 (162개) — 룰북상 모든 태스크는 공개정보
    //   16슬롯 × 10피처 (160) + viewer 본인 완료·실패 비율 (2)
    //   슬롯 배치: [0..3]=viewer, [4..7]=viewer+1, [8..11]=viewer+2, [12..15]=viewer+3
    //   (viewer 기준 시계방향)
    //   slot[b+0] 태스크 타입  (type / 20)
    //   slot[b+1] requiredCount (/ 10)
    //   slot[b+2] requiredConsecutive (/ 5)
    //   slot[b+3] targetSuit (/ 4)
    //   slot[b+4] suitB (/ 4)
    //   slot[b+5] targetCard.suit (/ 4, WinSpecificCard)
    //   slot[b+6] targetCard.value (/ 9, WinSpecificCard)
    //   slot[b+7] orderIndex (/ 5)  — 현 패치 이후 항상 0
    //   slot[b+8] isCompleted
    //   slot[b+9] isFailed
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
                obs[b + 0] = (int)task.type / 20f;
                obs[b + 1] = task.requiredCount / 10f;
                obs[b + 2] = task.requiredConsecutive / 5f;
                obs[b + 3] = (int)task.targetSuit / 4f;
                obs[b + 4] = (int)task.suitB / 4f;
                obs[b + 5] = task.targetCard != null ? (int)task.targetCard.suit / 4f : 0f;
                obs[b + 6] = task.targetCard != null ? task.targetCard.value / 9f    : 0f;
                obs[b + 7] = task.orderIndex / 5f;
                obs[b + 8] = task.isCompleted ? 1f : 0f;
                obs[b + 9] = task.isFailed    ? 1f : 0f;

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

    // 커리큘럼 단계별 태스크 타입 허용 — 룰북 권고: 단순 태스크부터 단계 확장.
    //  Stage1 (≤3): 가장 기본 6개 타입
    //  Stage2 (≤5): + 슈트·연속·트릭순서 일부
    //  Stage3 (≤7): + 슈트 비교·홀짝 제외 전부
    //  Stage4 (≥8): 전체
    private static bool IsTaskTypeAllowed(TaskCard.TaskType type, int maxDifficulty)
    {
        if (maxDifficulty <= 3)
        {
            // Stage 1: 자기주도적 + 매 트릭 피드백 있는 타입만
            //   WinNone        — 낮은 카드를 내면 혼자 달성 가능
            //   WinAtLeast     — 높은 카드를 내면 혼자 달성 가능
            //   WinNoneFirstN  — 초반 트릭만 회피하면 됨
            return type == TaskCard.TaskType.WinNone
                || type == TaskCard.TaskType.WinAtLeast
                || type == TaskCard.TaskType.WinNoneFirstN;
        }
        if (maxDifficulty <= 5)
        {
            // Stage 2: 타이밍 기반 + 기본 제약 추가
            return type == TaskCard.TaskType.WinNone
                || type == TaskCard.TaskType.WinAtLeast
                || type == TaskCard.TaskType.WinNoneFirstN
                || type == TaskCard.TaskType.WinFirst
                || type == TaskCard.TaskType.WinLast
                || type == TaskCard.TaskType.WinTrickCount
                || type == TaskCard.TaskType.WinNoConsecutive
                || type == TaskCard.TaskType.WinNoSuit
                || type == TaskCard.TaskType.WinNoOpenSuit
                || type == TaskCard.TaskType.WinRelativeFewer;
        }
        if (maxDifficulty <= 7)
        {
            // Stage 3: 팀 협력 필요 타입 추가, WinSpecificCard / 극단 타입 제외
            return type != TaskCard.TaskType.WinSpecificCard
                && type != TaskCard.TaskType.WinOddTrick
                && type != TaskCard.TaskType.WinEvenTrick
                && type != TaskCard.TaskType.WinFirstAndLast
                && type != TaskCard.TaskType.WinExactSuitCount;
        }
        return true; // Stage 4: 전체 허용
    }

    private TaskCard CreateUnassignedTask(CrewAgent refPlayer, HashSet<Card> used)
    {
        float r = Random.value;
        TaskCard.TaskType type;

        if      (r < 0.40f) type = TaskCard.TaskType.WinSpecificCard;
        else if (r < 0.52f) type = TaskCard.TaskType.WinTrickCount;
        else if (r < 0.58f) type = TaskCard.TaskType.WinFirst;
        else if (r < 0.63f) type = TaskCard.TaskType.WinNone;
        else if (r < 0.67f) type = TaskCard.TaskType.WinLast;
        else if (r < 0.70f) type = TaskCard.TaskType.WinConsecutive;
        else if (r < 0.72f) type = TaskCard.TaskType.WinNoConsecutive;
        else if (r < 0.74f) type = TaskCard.TaskType.WinOnlyFirst;
        else if (r < 0.76f) type = TaskCard.TaskType.WinOnlyLast;
        else if (r < 0.78f) type = TaskCard.TaskType.WinFirstAndLast;
        else if (r < 0.81f) type = TaskCard.TaskType.WinNoSuit;
        else if (r < 0.83f) type = TaskCard.TaskType.WinNoOpenSuit;
        else if (r < 0.85f) type = TaskCard.TaskType.WinMoreSuitThan;
        else if (r < 0.87f) type = TaskCard.TaskType.WinExactSuitCount;
        else if (r < 0.89f) type = TaskCard.TaskType.WinEachColor;
        else if (r < 0.91f) type = TaskCard.TaskType.WinAtLeast;
        else if (r < 0.93f) type = TaskCard.TaskType.WinNoneFirstN;
        else if (r < 0.94f) type = TaskCard.TaskType.WinOddTrick;
        else if (r < 0.95f) type = TaskCard.TaskType.WinEvenTrick;
        else if (r < 0.97f) type = TaskCard.TaskType.WinRelativeFewer;
        else                type = TaskCard.TaskType.WinRelativeMore;

        // 풀 전체에 하나만 있어야 하는 타입 중복 방지
        bool UniqueExists(TaskCard.TaskType t) => taskPool.Exists(x => x.type == t);
        bool IsSingletonType(TaskCard.TaskType t) =>
            t == TaskCard.TaskType.WinFirst        || t == TaskCard.TaskType.WinLast          ||
            t == TaskCard.TaskType.WinNone         || t == TaskCard.TaskType.WinOnlyFirst      ||
            t == TaskCard.TaskType.WinOnlyLast     || t == TaskCard.TaskType.WinFirstAndLast   ||
            t == TaskCard.TaskType.WinNoConsecutive|| t == TaskCard.TaskType.WinEachColor      ||
            t == TaskCard.TaskType.WinRelativeFewer|| t == TaskCard.TaskType.WinRelativeMore;

        if (IsSingletonType(type) && UniqueExists(type))
            type = TaskCard.TaskType.WinAtLeast;

        // 커리큘럼 stage 필터: 단계별로 허용 타입을 점진 확장 (룰북: 단순 태스크부터 학습)
        if (!IsTaskTypeAllowed(type, currentMaxDifficulty))
        {
            if      (currentMaxDifficulty <= 3) type = TaskCard.TaskType.WinAtLeast;
            else if (currentMaxDifficulty <= 5) type = TaskCard.TaskType.WinFirst;
            else                                type = TaskCard.TaskType.WinSpecificCard;
        }

        // 랜덤 슈트 선택 헬퍼 (로켓 제외)
        Card.Suit RandSuit() => (Card.Suit)Random.Range(0, 4);
        Card.Suit RandSuitExcept(Card.Suit exclude)
        {
            Card.Suit s;
            do { s = RandSuit(); } while (s == exclude);
            return s;
        }

        switch (type)
        {
            case TaskCard.TaskType.WinSpecificCard:
            {
                Card target = PickCardFromHand(refPlayer.hand, used)
                           ?? PickCardFromPool(used);
                if (target == null) return null;
                return TaskCard.SpecificCard(target);
            }
            case TaskCard.TaskType.WinTrickCount:
                return TaskCard.TrickCount(Random.Range(1, 5));
            case TaskCard.TaskType.WinFirst:          return TaskCard.First();
            case TaskCard.TaskType.WinNone:           return TaskCard.None();
            case TaskCard.TaskType.WinLast:           return TaskCard.Last();
            case TaskCard.TaskType.WinConsecutive:    return TaskCard.Consecutive(Random.Range(2, 4));
            case TaskCard.TaskType.WinNoConsecutive:  return TaskCard.NoConsecutive();
            case TaskCard.TaskType.WinOnlyFirst:      return TaskCard.OnlyFirst();
            case TaskCard.TaskType.WinOnlyLast:       return TaskCard.OnlyLast();
            case TaskCard.TaskType.WinFirstAndLast:   return TaskCard.FirstAndLast();
            case TaskCard.TaskType.WinNoSuit:         return TaskCard.NoSuit(RandSuit());
            case TaskCard.TaskType.WinNoOpenSuit:     return TaskCard.NoOpenSuit(RandSuit());
            case TaskCard.TaskType.WinMoreSuitThan:
            {
                Card.Suit a = RandSuit();
                return TaskCard.MoreSuitThan(a, RandSuitExcept(a));
            }
            case TaskCard.TaskType.WinExactSuitCount:
                return TaskCard.ExactSuitCount(RandSuit(), Random.Range(1, 4));
            case TaskCard.TaskType.WinEachColor:      return TaskCard.EachColor();
            case TaskCard.TaskType.WinAtLeast:
                return TaskCard.AtLeast(currentMaxDifficulty <= 3 ? Random.Range(1, 3) : Random.Range(1, 5));
            case TaskCard.TaskType.WinNoneFirstN:
                return TaskCard.NoneFirstN(currentMaxDifficulty <= 3 ? Random.Range(2, 4) : Random.Range(1, 4));
            case TaskCard.TaskType.WinOddTrick:       return TaskCard.OddTrick();
            case TaskCard.TaskType.WinEvenTrick:      return TaskCard.EvenTrick();
            case TaskCard.TaskType.WinRelativeFewer:  return TaskCard.RelativeFewer();
            case TaskCard.TaskType.WinRelativeMore:   return TaskCard.RelativeMore();
            default:
                return null;
        }
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
