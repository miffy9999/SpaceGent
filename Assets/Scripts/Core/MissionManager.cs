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
        isFirstTrick = true;
        missionEnded = false;
        GameManager.Instance.uiManager?.HideResult();

        var players = GameManager.Instance.players;
        foreach (var p in players)
            trickWinCounts[p] = 0;

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

        // 선택 순서: 함장+1부터 시계방향 (함장 제외)
        selectionOrder.Clear();
        for (int i = 1; i < players.Count; i++)
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

        // 미션이 지정한 총 태스크 수만큼 풀 생성
        int total = mission.TotalTaskCount;
        for (int i = 0; i < total; i++)
        {
            // 태스크 카드를 생성할 때 손패를 참고하기 위해 비-함장 플레이어 순환
            int pIdx = (captainIndex + 1 + (i % (players.Count - 1))) % players.Count;
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

        // 비-함장 3명에게 1개씩, 총 3개
        for (int i = 1; i < players.Count; i++)
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
        while (taskPool.Count > 0)
        {
            var current = GetCurrentPickingPlayer();
            if (current == null) break;

            // 인간 플레이어 차례 → UI 갱신 후 대기
            if (current == GameManager.Instance.players[0])
            {
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
    public void OnTrickResolved(CrewAgent winner, List<Card> trickCards)
    {
        if (missionEnded) return;

        trickWinCounts[winner]++;

        foreach (TaskCard task in tasks)
        {
            if (task.isCompleted || task.isFailed) continue;

            switch (task.type)
            {
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
                    if (winner == task.assignedTo)
                        FailTask(task);
                    break;
            }
        }

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
            if (task.isCompleted || task.isFailed) continue;

            switch (task.type)
            {
                case TaskCard.TaskType.WinTrickCount:
                    if (trickWinCounts[task.assignedTo] >= task.requiredCount)
                        CompleteTask(task);
                    else
                        FailTask(task);
                    break;

                case TaskCard.TaskType.WinNone:
                    CompleteTask(task);
                    break;

                case TaskCard.TaskType.WinSpecificCard:
                    FailTask(task);
                    break;
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
    // ---------------------------------------------------------------
    public float[] GetTaskObservation(CrewAgent agent)
    {
        float[] obs = new float[42];
        int completed = 0, failed = 0, total = 0;

        foreach (TaskCard task in tasks)
        {
            if (task.assignedTo != agent) continue;
            total++;

            if (task.type == TaskCard.TaskType.WinSpecificCard && task.targetCard != null)
                obs[agent.GetCardIndex(task.targetCard)] = 1f;

            if (task.isCompleted) completed++;
            if (task.isFailed)    failed++;
        }

        float norm = Mathf.Max(total, 1);
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
        TaskCard.TaskType type = r < 0.6f ? TaskCard.TaskType.WinSpecificCard
                               : r < 0.8f ? TaskCard.TaskType.WinTrickCount
                               : r < 0.9f ? TaskCard.TaskType.WinFirst
                                          : TaskCard.TaskType.WinNone;

        // 중복 방지
        if (type == TaskCard.TaskType.WinFirst && taskPool.Exists(t => t.type == TaskCard.TaskType.WinFirst))
            type = TaskCard.TaskType.WinSpecificCard;

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
                return TaskCard.TrickCount(Random.Range(1, 4));
            case TaskCard.TaskType.WinFirst:
                return TaskCard.First();
            case TaskCard.TaskType.WinNone:
                return TaskCard.None();
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
