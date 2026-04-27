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

    // 보상 상수
    private const float RewardTaskComplete = 1.0f;
    private const float RewardMissionWin   = 2.0f;
    private const float PenaltyTaskFail    = -1.0f;
    private const float PenaltyMissionFail = -2.0f;

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

        // 미션 선택 (커리큘럼)
        int maxDifficulty = Mathf.RoundToInt(
            Academy.Instance.EnvironmentParameters.GetWithDefault("difficulty", 9f));
        currentMission = database != null ? database.GetByMaxDifficulty(maxDifficulty) : null;

        // 태스크 풀 생성 (미배정)
        if (currentMission != null)
        {
            Debug.Log($"[Mission] 난이도 상한={maxDifficulty} → {currentMission.id} 선택");
            GenerateTaskPool(currentMission, captainIndex);
        }
        else
        {
            GenerateFallbackPool(captainIndex);
        }

        // 실제 딥 씨 크루 규칙: 함장부터 시계방향으로 모든 플레이어가 선택 (함장 포함)
        selectionOrder.Clear();
        for (int i = 0; i < players.Count; i++)
            selectionOrder.Add((captainIndex + i) % players.Count);
        selectionCursor = 0;

        // 3개 이상의 태스크가 있는 경우 일부에 순서 토큰 부여
        AssignOrderTokens();

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

        // 실제 딥 씨 크루: 함장도 태스크를 가질 수 있으므로 모든 플레이어 손패 참고
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
            if (current == GameManager.Instance.players[0])
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
    // 순서 토큰 할당 (실제 딥 씨 크루 규칙)
    // 태스크가 3개 이상일 때 일부 태스크에 번호를 부여한다.
    // 번호가 붙은 태스크는 반드시 낮은 번호 순서대로 달성해야 한다.
    // ---------------------------------------------------------------
    private void AssignOrderTokens()
    {
        if (taskPool.Count < 3) return;

        // 절반까지 순서 토큰 부여 (최대 5개, 실제 보드게임 토큰 수)
        int orderCount = Mathf.Min(taskPool.Count / 2, 5);
        if (orderCount < 2) return;

        // 랜덤으로 인덱스 섞기
        List<int> indices = new List<int>();
        for (int i = 0; i < taskPool.Count; i++) indices.Add(i);
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
        }

        for (int k = 0; k < orderCount; k++)
            taskPool[indices[k]].orderIndex = k + 1;

        Debug.Log($"[Mission] 순서 토큰 {orderCount}개 부여");
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
    public void OnTrickResolved(CrewAgent winner, List<Card> trickCards,
                                CrewAgent opener, Card.Suit openerSuit, Card.Suit trickLeadSuit)
    {
        if (missionEnded) return;

        trickWinCounts[winner]++;
        trickNumber++;

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
            if (task.isCompleted || task.isFailed) continue;

            switch (task.type)
            {
                // ── 기존 타입 ─────────────────────────────────────────
                case TaskCard.TaskType.WinSpecificCard:
                    if (trickCards.Contains(task.targetCard))
                    {
                        if (winner == task.assignedTo) CompleteTask(task);
                        else FailTask(task);
                    }
                    break;

                case TaskCard.TaskType.WinFirst:
                    if (isFirstTrick)
                    {
                        if (winner == task.assignedTo) CompleteTask(task);
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
                        CompleteTask(task);
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
                            CompleteTask(task);
                        else
                            FailTask(task);
                    }
                    break;

                // ── 슈트 관련 ─────────────────────────────────────────
                case TaskCard.TaskType.WinNoSuit:
                    // 해당 슈트가 리드된 트릭을 이기면 안 됨
                    if (winner == task.assignedTo && trickLeadSuit == task.targetSuit)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinNoOpenSuit:
                    // 해당 슈트로 트릭을 시작(첫 카드)하면 안 됨
                    if (opener == task.assignedTo && openerSuit == task.targetSuit)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinMoreSuitThan:
                    break; // OnHandEnded에서 처리

                case TaskCard.TaskType.WinExactSuitCount:
                    // 해당 슈트 카드 초과 시 즉시 실패
                    if (suitCardCounts[task.assignedTo][(int)task.targetSuit] > task.requiredCount)
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinEachColor:
                    break; // OnHandEnded에서 처리

                // ── 트릭 수 관련 ──────────────────────────────────────
                case TaskCard.TaskType.WinAtLeast:
                    break; // OnHandEnded에서 처리

                case TaskCard.TaskType.WinNoneFirstN:
                    // 처음 N 트릭 안에 이기면 즉시 실패
                    if (winner == task.assignedTo && trickNumber <= task.requiredCount)
                        FailTask(task);
                    break;

                // ── 카드 값 관련 ──────────────────────────────────────
                case TaskCard.TaskType.WinOddTrick:
                    // 이긴 트릭의 모든 카드가 홀수여야 완료
                    if (winner == task.assignedTo && AllValuesMatch(trickCards, v => v % 2 == 1))
                        CompleteTask(task);
                    break;

                case TaskCard.TaskType.WinEvenTrick:
                    // 이긴 트릭의 모든 카드가 짝수여야 완료
                    if (winner == task.assignedTo && AllValuesMatch(trickCards, v => v % 2 == 0))
                        CompleteTask(task);
                    break;

                // ── 상대 비교 ─────────────────────────────────────────
                case TaskCard.TaskType.WinRelativeFewer:
                case TaskCard.TaskType.WinRelativeMore:
                    break; // OnHandEnded에서 처리
            }
        }

        isFirstTrick = false;

        if (!missionEnded && IsMissionFailed())
        {
            missionEnded = true;
            GiveTeamReward(success: false);
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

        foreach (TaskCard task in tasks)
        {
            if (task.isCompleted || task.isFailed) continue;

            switch (task.type)
            {
                // ── 기존 타입 ────────────────────────────────────────
                case TaskCard.TaskType.WinSpecificCard:
                    FailTask(task);
                    break;

                case TaskCard.TaskType.WinTrickCount:
                    if (trickWinCounts[task.assignedTo] == task.requiredCount) CompleteTask(task);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinNone:
                    CompleteTask(task);
                    break;

                case TaskCard.TaskType.WinFirst:
                    FailTask(task);
                    break;

                case TaskCard.TaskType.WinLast:
                    if (lastTrickWinner == task.assignedTo) CompleteTask(task);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinConsecutive:
                    FailTask(task);
                    break;

                case TaskCard.TaskType.WinNoConsecutive:
                    CompleteTask(task);
                    break;

                case TaskCard.TaskType.WinOnlyFirst:
                    if (firstTrickWinner == task.assignedTo) CompleteTask(task);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinOnlyLast:
                    if (lastTrickWinner == task.assignedTo) CompleteTask(task);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinFirstAndLast:
                    FailTask(task);
                    break;

                // ── 슈트 관련 ────────────────────────────────────────
                case TaskCard.TaskType.WinNoSuit:
                    CompleteTask(task); // 실패 없이 여기까지 왔으면 성공
                    break;

                case TaskCard.TaskType.WinNoOpenSuit:
                    CompleteTask(task);
                    break;

                case TaskCard.TaskType.WinMoreSuitThan:
                {
                    int a = suitCardCounts[task.assignedTo][(int)task.targetSuit];
                    int b = suitCardCounts[task.assignedTo][(int)task.suitB];
                    if (a > b) CompleteTask(task);
                    else FailTask(task);
                    break;
                }

                case TaskCard.TaskType.WinExactSuitCount:
                    if (suitCardCounts[task.assignedTo][(int)task.targetSuit] == task.requiredCount)
                        CompleteTask(task);
                    else
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinEachColor:
                {
                    bool ok = suitCardCounts[task.assignedTo][(int)Card.Suit.Yellow] > 0
                           && suitCardCounts[task.assignedTo][(int)Card.Suit.Blue]   > 0
                           && suitCardCounts[task.assignedTo][(int)Card.Suit.Green]  > 0
                           && suitCardCounts[task.assignedTo][(int)Card.Suit.Pink]   > 0;
                    if (ok) CompleteTask(task);
                    else    FailTask(task);
                    break;
                }

                // ── 트릭 수 관련 ─────────────────────────────────────
                case TaskCard.TaskType.WinAtLeast:
                    if (trickWinCounts[task.assignedTo] >= task.requiredCount) CompleteTask(task);
                    else FailTask(task);
                    break;

                case TaskCard.TaskType.WinNoneFirstN:
                    CompleteTask(task); // 실패 없이 여기까지 왔으면 성공
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
                    if (fewer) CompleteTask(task);
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
                    if (more) CompleteTask(task);
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
    }

    // ---------------------------------------------------------------
    // 태스크 완료 / 실패
    // ---------------------------------------------------------------
    private void CompleteTask(TaskCard task)
    {
        // 실제 딥 씨 크루 규칙: 순서 토큰이 있는 태스크는 낮은 번호가 먼저 완료되어야 한다.
        // 낮은 번호의 태스크가 아직 미완료면 순서 위반 → 즉시 미션 실패
        if (task.orderIndex > 0)
        {
            foreach (var t in tasks)
            {
                if (t == task) continue;
                if (t.orderIndex > 0 && t.orderIndex < task.orderIndex && !t.isCompleted)
                {
                    Debug.Log($"[Mission] 순서 위반! [{task.orderIndex}] 태스크가 [{t.orderIndex}] 보다 먼저 완료됨 → 미션 실패");
                    FailTask(task);
                    return;
                }
            }
        }

        task.isCompleted = true;
        task.assignedTo.AddReward(RewardTaskComplete);
        Debug.Log($"[Mission] 태스크 완료 {task.assignedTo.name} → {task}");
    }

    private void FailTask(TaskCard task)
    {
        task.isFailed = true;
        task.assignedTo.AddReward(PenaltyTaskFail);
        Debug.Log($"[Mission] 태스크 실패 {task.assignedTo.name} → {task}");
    }

    private void GiveTeamReward(bool success)
    {
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

    // ---------------------------------------------------------------
    // 관찰 벡터 (42개)
    //   슬롯당 10개 × 4슬롯 (40) + 완료·실패 비율 (2)
    //   slot[b+0] 태스크 타입  (type / 20)
    //   slot[b+1] requiredCount (/ 10)
    //   slot[b+2] requiredConsecutive (/ 5)
    //   slot[b+3] targetSuit (/ 4)
    //   slot[b+4] suitB (/ 4)
    //   slot[b+5] targetCard.suit (/ 4, WinSpecificCard)
    //   slot[b+6] targetCard.value (/ 9, WinSpecificCard)
    //   slot[b+7] orderIndex (/ 5)
    //   slot[b+8] isCompleted
    //   slot[b+9] isFailed
    // ---------------------------------------------------------------
    public float[] GetTaskObservation(CrewAgent agent)
    {
        float[] obs = new float[42];
        List<TaskCard> agentTasks = tasks.FindAll(t => t.assignedTo == agent);

        int completed = 0, failed = 0;
        for (int slot = 0; slot < 4 && slot < agentTasks.Count; slot++)
        {
            TaskCard task = agentTasks[slot];
            int b = slot * 10;
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
            if (task.isCompleted) completed++;
            if (task.isFailed)    failed++;
        }

        float norm = Mathf.Max(agentTasks.Count, 1);
        obs[40] = completed / norm;
        obs[41] = failed    / norm;
        return obs;
    }

    // ---------------------------------------------------------------
    // 유틸
    // ---------------------------------------------------------------
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
            type = TaskCard.TaskType.WinSpecificCard;

        // 랜덤 슈트 선택 헬퍼 (잠수함 제외)
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
            case TaskCard.TaskType.WinAtLeast:        return TaskCard.AtLeast(Random.Range(1, 5));
            case TaskCard.TaskType.WinNoneFirstN:     return TaskCard.NoneFirstN(Random.Range(1, 4));
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
        List<Card> available = hand.FindAll(c => c.suit != Card.Suit.Submarine && !used.Contains(c));
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
