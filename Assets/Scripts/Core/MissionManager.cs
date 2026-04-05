using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미션 태스크를 생성·배분하고, 매 트릭 후 달성 여부를 판정한다.
/// TrickManager가 트릭 결과를 넘겨주면 여기서 보상/패널티를 처리한다.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    // 이번 미션에서 수행해야 할 태스크 목록
    public List<TaskCard> tasks = new List<TaskCard>();

    // 플레이어별 이긴 트릭 수 (WinTrickCount 태스크 판정용)
    private Dictionary<CrewAgent, int> trickWinCounts = new Dictionary<CrewAgent, int>();

    // 게임 전체가 첫 트릭인지 여부
    private bool isFirstTrick = true;

    // 보상 상수
    private const float RewardTaskComplete  =  1.0f;
    private const float RewardMissionWin    =  2.0f;
    private const float PenaltyTaskFail     = -1.0f;
    private const float PenaltyMissionFail  = -2.0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ---------------------------------------------------------------
    // 미션 초기화 (GameManager → TrickManager → 여기 순서로 호출)
    // ---------------------------------------------------------------
    public void InitMission()
    {
        tasks.Clear();
        trickWinCounts.Clear();
        isFirstTrick = true;

        foreach (var p in GameManager.Instance.players)
            trickWinCounts[p] = 0;

        GenerateRandomMission();
    }

    /// <summary>
    /// 미션을 무작위 생성해서 플레이어에게 배분한다.
    /// 난이도는 추후 커리큘럼 러닝으로 조절 예정.
    /// </summary>
    private void GenerateRandomMission()
    {
        var players = GameManager.Instance.players;

        // 태스크 풀: 색상 카드 중 랜덤 4장을 각 플레이어에게 하나씩 배정
        List<Card> pool = new List<Card>();
        for (int s = 0; s < 4; s++)
            for (int v = 1; v <= 9; v++)
                pool.Add(new Card((Card.Suit)s, v));

        Shuffle(pool);

        for (int i = 0; i < players.Count; i++)
        {
            TaskCard task = TaskCard.SpecificCard(pool[i]);
            task.assignedTo = players[i];
            tasks.Add(task);

            Debug.Log($"[MissionManager] {players[i].name} 태스크: {task}");
        }
    }

    // ---------------------------------------------------------------
    // TrickManager가 트릭 결과를 알려줄 때 호출
    // ---------------------------------------------------------------
    public void OnTrickResolved(CrewAgent winner, List<Card> trickCards)
    {
        // 트릭 횟수 카운트
        trickWinCounts[winner]++;

        // 각 태스크 판정
        foreach (TaskCard task in tasks)
        {
            if (task.isCompleted || task.isFailed) continue;

            switch (task.type)
            {
                case TaskCard.TaskType.WinSpecificCard:
                    if (trickCards.Contains(task.targetCard))
                    {
                        if (winner == task.assignedTo)
                            CompleteTask(task);
                        else
                            FailTask(task);     // 다른 사람이 가져감
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

                case TaskCard.TaskType.WinTrickCount:
                    // 핸드 종료 시 판정 (OnHandEnded에서 처리)
                    break;
            }
        }

        isFirstTrick = false;

        // 미션 전체 실패 조기 감지
        if (IsMissionFailed())
            EndMission(success: false);
    }

    // ---------------------------------------------------------------
    // 핸드(판) 종료 시 호출 — WinTrickCount 최종 판정
    // ---------------------------------------------------------------
    public void OnHandEnded()
    {
        foreach (TaskCard task in tasks)
        {
            if (task.isCompleted || task.isFailed) continue;

            if (task.type == TaskCard.TaskType.WinTrickCount)
            {
                if (trickWinCounts[task.assignedTo] >= task.requiredCount)
                    CompleteTask(task);
                else
                    FailTask(task);
            }
        }

        bool success = IsMissionComplete();
        EndMission(success);
    }

    // ---------------------------------------------------------------
    // 태스크 완료 / 실패 처리
    // ---------------------------------------------------------------
    private void CompleteTask(TaskCard task)
    {
        task.isCompleted = true;
        task.assignedTo.AddReward(RewardTaskComplete);
        Debug.Log($"[MissionManager] 태스크 완료! {task.assignedTo.name} → {task}");
    }

    private void FailTask(TaskCard task)
    {
        task.isFailed = true;
        task.assignedTo.AddReward(PenaltyTaskFail);
        Debug.Log($"[MissionManager] 태스크 실패! {task.assignedTo.name} → {task}");
    }

    // ---------------------------------------------------------------
    // 미션 종료 — 전원에게 팀 보상/패널티 부여
    // ---------------------------------------------------------------
    private void EndMission(bool success)
    {
        float teamReward = success ? RewardMissionWin : PenaltyMissionFail;
        string result = success ? "성공" : "실패";

        Debug.Log($"[MissionManager] 미션 {result}! 팀 보상: {teamReward}");

        foreach (var p in GameManager.Instance.players)
            p.AddReward(teamReward);
    }

    // ---------------------------------------------------------------
    // 상태 조회
    // ---------------------------------------------------------------
    public bool IsMissionComplete()
    {
        foreach (TaskCard t in tasks)
            if (!t.isCompleted) return false;
        return true;
    }

    public bool IsMissionFailed()
    {
        foreach (TaskCard t in tasks)
            if (t.isFailed) return true;
        return false;
    }

    /// <summary>
    /// CrewAgent의 관찰 벡터용: 내 태스크 상태를 배열로 반환.
    /// 인덱스 0~39 = 목표 카드 원-핫, 40 = 완료 여부, 41 = 실패 여부
    /// </summary>
    public float[] GetTaskObservation(CrewAgent agent)
    {
        float[] obs = new float[42];

        foreach (TaskCard task in tasks)
        {
            if (task.assignedTo != agent) continue;

            if (task.type == TaskCard.TaskType.WinSpecificCard && task.targetCard != null)
                obs[agent.GetCardIndex(task.targetCard)] = 1f;

            obs[40] = task.isCompleted ? 1f : 0f;
            obs[41] = task.isFailed    ? 1f : 0f;
            break; // 지금은 1인 1태스크
        }

        return obs;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int r = Random.Range(i, list.Count);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }
}
