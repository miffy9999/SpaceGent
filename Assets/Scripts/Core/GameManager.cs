using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

/// <summary>
/// 게임 전체의 단일 진실 원천(Single Source of Truth).
/// - players 리스트 소유
/// - 공용 centerBoard 소유
/// - DeckManager / TrickManager 초기화 순서 보장
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Players (0번 = 인간, 1~3번 = AI)")]
    public List<CrewAgent> players = new List<CrewAgent>();

    [Header("공용 테이블 (카드가 모이는 중앙 오브젝트)")]
    public Transform centerBoard;

    [Header("매니저 참조")]
    public DeckManager deckManager;
    public TrickManager trickManager;
    public MissionManager missionManager;
    public CommunicationManager communicationManager;
    public GameUIManager uiManager;

    [Header("스프라이트 매핑")]
    public CardSpriteMapping cardSpriteMapping;
    public TaskSpriteMapping taskSpriteMapping;

    // 협력 그룹 (MA-POCA) — 4명을 한 팀으로 묶어 group reward + 중앙집중 critic 사용
    // helperHeuristicOnly 모드에서는 null (PPO 단일 에이전트 사용)
    public SimpleMultiAgentGroup teamGroup { get; private set; }

    // [A2] helpers를 HeuristicOnly로 격리하는 모드 — PPO 단일 에이전트 학습
    public bool helperHeuristicOnly { get; private set; }

    void Awake()
    {
        // 싱글턴 보장
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        ValidateSetup();

        deckManager.players  = players;
        trickManager.players = players;

        // 배치 모드(서버 학습)에서는 전원 AI.
        // 일반 실행 시에는 player[0]이 인간 — 단, player[0]도 HeuristicOnly로 설정된 경우
        // all-rule-based 시뮬레이션 모드로 간주하고 AI로 동작 (키보드 대기 안 함).
        bool player0Heuristic = players.Count > 0 &&
            players[0].GetComponent<BehaviorParameters>()?.BehaviorType == BehaviorType.HeuristicOnly;
        if (!Application.isBatchMode && players.Count > 0 && !player0Heuristic)
            players[0].isHumanPlayer = true;

        // [A2] helpers가 씬에서 HeuristicOnly로 설정된 경우 PPO 단일 에이전트 모드
        //   BehaviorType 런타임 변경은 Academy 초기화 이후라 Python에 전달 안 됨 → 씬에서 사전 설정 필요
        helperHeuristicOnly = players.Count > 1 &&
            players[1].GetComponent<BehaviorParameters>()?.BehaviorType == BehaviorType.HeuristicOnly;

        if (helperHeuristicOnly)
        {
            // PPO 단일 에이전트: teamGroup 사용 안 함
            teamGroup = null;
        }
        else
        {
            teamGroup = new SimpleMultiAgentGroup();
            foreach (var p in players) teamGroup.RegisterAgent(p);
        }

        communicationManager.InitTokens();
        trickManager.StartGame();
    }

    // ---------------------------------------------------------------
    // 보상 / 에피소드 종료 라우팅
    //   helperHeuristicOnly(PPO) : player[0]에 직접 보상, 전원 EndEpisode
    //   일반(POCA)               : teamGroup group reward / EndGroupEpisode
    // ---------------------------------------------------------------
    public void AddGroupOrLearnerReward(float reward)
    {
        if (helperHeuristicOnly)
            players[0].AddReward(reward);
        else
            teamGroup.AddGroupReward(reward);
    }

    public void EndGroupOrLearnerEpisode()
    {
        if (helperHeuristicOnly)
        {
            foreach (var p in players) p.EndEpisode();
        }
        else
        {
            teamGroup.EndGroupEpisode();
        }
    }

    private void ValidateSetup()
    {
        if (players.Count != 4)
            Debug.LogWarning($"[GameManager] players가 {players.Count}명입니다. 4명으로 설정하세요.");
        if (centerBoard == null)
            Debug.LogError("[GameManager] centerBoard가 비어 있습니다. 인스펙터에서 할당하세요.");
        if (deckManager == null)
            Debug.LogError("[GameManager] deckManager가 비어 있습니다.");
        if (trickManager == null)
            Debug.LogError("[GameManager] trickManager가 비어 있습니다.");
        if (missionManager == null)
            Debug.LogError("[GameManager] missionManager가 비어 있습니다.");
        if (communicationManager == null)
            Debug.LogError("[GameManager] communicationManager가 비어 있습니다.");
    }
}
