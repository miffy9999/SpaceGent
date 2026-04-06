using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// 미션 선택 → 태스크 배분 → 매 트릭 판정 → 보상/패널티 처리.
/// 카드 분배 이후(TrickManager.StartGame)에 InitMission()이 호출되어야 한다.
///
/// 커리큘럼 학습: Academy 환경 파라미터 "difficulty"(1~9)로 난이도 제어.
/// trainer_config.yaml의 environment_parameters에서 단계적으로 증가시킨다.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("미션 데이터베이스 (인스펙터에서 할당)")]
    public MissionDatabase database;

    // 현재 진행 중인 미션
    public Mission currentMission { get; private set; }

    // 이번 판의 태스크 목록
    public List<TaskCard> tasks = new List<TaskCard>();

    // 플레이어별 이긴 트릭 수
    private Dictionary<CrewAgent, int> trickWinCounts = new Dictionary<CrewAgent, int>();

    private bool isFirstTrick  = true;
    private bool missionEnded  = false; // 중복 종료 방지

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
    // 미션 초기화 — 반드시 카드 분배(DealCardsToAgents) 이후에 호출
    // ---------------------------------------------------------------
    public void InitMission(int captainIndex)
    {
        tasks.Clear();
        trickWinCounts.Clear();
        isFirstTrick = true;
        missionEnded = false;
        GameManager.Instance.uiManager?.HideResult();

        var players = GameManager.Instance.players;
        foreach (var p in players)
            trickWinCounts[p] = 0;

        // 미션 선택 (커리큘럼: Academy 환경 파라미터 "difficulty" 1~9)
        int maxDifficulty = Mathf.RoundToInt(
            Academy.Instance.EnvironmentParameters.GetWithDefault("difficulty", 9f));
        currentMission = database != null ? database.GetByMaxDifficulty(maxDifficulty) : null;
        if (currentMission == null)
        {
            FallbackMission(captainIndex);
            return;
        }

        Debug.Log($"[Mission] 난이도 상한={maxDifficulty} → {currentMission.id} 선택");

        AssignTasksFromMission(currentMission, captainIndex);
    }

    // ---------------------------------------------------------------
    // 미션 정의대로 태스크 배분
    // 함장(captainIndex)은 제외, 나머지 3명에게 taskCounts만큼 배정
    // ---------------------------------------------------------------
    private void AssignTasksFromMission(Mission mission, int captainIndex)
    {
        var players = GameManager.Instance.players;

        // 비-함장 플레이어 목록
        List<CrewAgent> nonCaptains = new List<CrewAgent>();
        for (int i = 0; i < players.Count; i++)
            if (i != captainIndex) nonCaptains.Add(players[i]);

        // taskCounts 배열을 셔플해서 누가 몇 개를 받을지 랜덤화
        int[] counts = (int[])mission.taskCounts.Clone();
        ShuffleArray(counts);

        for (int i = 0; i < nonCaptains.Count && i < counts.Length; i++)
            GenerateTasksForPlayer(nonCaptains[i], counts[i]);

        LogTaskSummary();
    }

    // ---------------------------------------------------------------
    // 플레이어 1명에게 count개 태스크 생성
    // ---------------------------------------------------------------
    private void GenerateTasksForPlayer(CrewAgent player, int count)
    {
        // 이미 배정된 태스크들의 타겟 카드 집합 (중복 방지)
        HashSet<Card> usedCards = new HashSet<Card>();
        foreach (var t in tasks)
            if (t.type == TaskCard.TaskType.WinSpecificCard && t.targetCard != null)
                usedCards.Add(t.targetCard);

        for (int i = 0; i < count; i++)
        {
            TaskCard task = CreateTask(player, usedCards);
            if (task == null) break;
            task.assignedTo = player;
            tasks.Add(task);

            if (task.type == TaskCard.TaskType.WinSpecificCard)
                usedCards.Add(task.targetCard);
        }
    }

    // ---------------------------------------------------------------
    // 태스크 하나 생성
    // WinSpecificCard는 해당 플레이어 손패 카드를 우선 선택
    // ---------------------------------------------------------------
    private TaskCard CreateTask(CrewAgent player, HashSet<Card> usedCards)
    {
        // 태스크 타입 가중치: WinSpecificCard 60%, WinTrickCount 20%, WinFirst 10%, WinNone 10%
        float r = Random.value;
        TaskCard.TaskType type = r < 0.6f ? TaskCard.TaskType.WinSpecificCard
                               : r < 0.8f ? TaskCard.TaskType.WinTrickCount
                               : r < 0.9f ? TaskCard.TaskType.WinFirst
                                          : TaskCard.TaskType.WinNone;

        // WinFirst / WinNone은 같은 타입을 중복 배정하지 않음
        if (type == TaskCard.TaskType.WinFirst && tasks.Exists(t => t.type == TaskCard.TaskType.WinFirst))
            type = TaskCard.TaskType.WinSpecificCard;
        if (type == TaskCard.TaskType.WinNone && tasks.Exists(t => t.assignedTo == player && t.type == TaskCard.TaskType.WinNone))
            type = TaskCard.TaskType.WinSpecificCard;

        switch (type)
        {
            case TaskCard.TaskType.WinSpecificCard:
            {
                // 1순위: 해당 플레이어 손패 중 미사용 카드
                Card target = PickCardFromHand(player.hand, usedCards);

                // 2순위: 전체 카드 풀에서 랜덤
                if (target == null) target = PickCardFromPool(usedCards);
                if (target == null) return null;

                return TaskCard.SpecificCard(target);
            }

            case TaskCard.TaskType.WinTrickCount:
            {
                // 1~3회 (손패 10장 기준)
                int count = Random.Range(1, 4);
                return TaskCard.TrickCount(count);
            }

            case TaskCard.TaskType.WinFirst:
                return TaskCard.First();

            case TaskCard.TaskType.WinNone:
                return TaskCard.None();

            default:
                return null;
        }
    }

    // 손패에서 미사용 카드 선택 (잠수함 제외)
    private Card PickCardFromHand(List<Card> hand, HashSet<Card> used)
    {
        List<Card> available = hand.FindAll(c => c.suit != Card.Suit.Submarine && !used.Contains(c));
        if (available.Count == 0) return null;
        return available[Random.Range(0, available.Count)];
    }

    // 전체 색상 카드 풀에서 미사용 카드 선택
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

    // MissionDatabase 없을 때 폴백: 각자 1개씩 WinSpecificCard
    private void FallbackMission(int captainIndex)
    {
        var players = GameManager.Instance.players;
        HashSet<Card> used = new HashSet<Card>();

        for (int i = 0; i < players.Count; i++)
        {
            if (i == captainIndex) continue;
            Card target = PickCardFromHand(players[i].hand, used)
                       ?? PickCardFromPool(used);
            if (target == null) continue;

            TaskCard task = TaskCard.SpecificCard(target);
            task.assignedTo = players[i];
            tasks.Add(task);
            used.Add(target);
        }

        LogTaskSummary();
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
                    // Card.Equals 오버라이드로 값 비교 동작
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

        // 태스크 하나라도 실패하면 즉시 패널티 (게임은 계속)
        if (!missionEnded && IsMissionFailed())
        {
            missionEnded = true;
            GiveTeamReward(success: false);
        }
    }

    // ---------------------------------------------------------------
    // 핸드 종료 시 최종 판정 (TrickManager에서 호출)
    // ---------------------------------------------------------------
    public void OnHandEnded()
    {
        // WinTrickCount, WinNone 최종 판정
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
                    // 한 번도 트릭을 안 이겼으면 완료
                    CompleteTask(task);
                    break;

                case TaskCard.TaskType.WinSpecificCard:
                    // 핸드가 끝났는데 아직 미완료면 실패
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
    // 관찰 벡터 (42개) — 다수 태스크 대응
    //   [0~39] : 내 모든 WinSpecificCard 목표 카드 원-핫 OR 합산
    //   [40]   : 완료된 태스크 수 (정규화)
    //   [41]   : 실패한 태스크 수 (정규화)
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
    private void LogTaskSummary()
    {
        foreach (var t in tasks)
            Debug.Log($"[Mission] {t.assignedTo.name} → {t}");
    }

    private void ShuffleArray(int[] arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            int r = Random.Range(i, arr.Length);
            (arr[i], arr[r]) = (arr[r], arr[i]);
        }
    }
}
