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

    public enum PlayMode
    {
        HumanVsAI,    // player[0] = 인간 (마우스/키보드 선택)
        Simulation,   // 전원 AI — 인간 입력 없이 빠르게 자동 진행 (시뮬/평가/학습)
    }

    [Header("플레이 모드 (시뮬레이션 = 전원 AI 자동)")]
    [Tooltip("Simulation: player[0]도 AI로 동작해 인간 선택 없이 빠르게 진행.\nHumanVsAI: player[0]이 인간 플레이어.")]
    public PlayMode playMode = PlayMode.HumanVsAI;

    public enum HeuristicPolicy
    {
        RuleBased,    // 규칙 기반 휴리스틱 (HFSM)
        MCTS,         // SO-ISMCTS 정보집합 몬테카를로 트리 탐색
    }

    [Header("AI 정책 (BehaviorType=HeuristicOnly 에이전트에 적용)")]
    [Tooltip("RL 학습은 BehaviorType=Default + 트레이너가 구동(이 설정 무관).\n" +
             "HeuristicOnly 에이전트는 이 정책으로 카드를 결정한다.\n" +
             "MCTS: 모든 AI(도우미 포함)가 SO-ISMCTS 탐색으로 협력 플레이.")]
    public HeuristicPolicy aiPolicy = HeuristicPolicy.RuleBased;

    [Header("평가 (미션 성공률 누적 콘솔 로그)")]
    [Tooltip("켜면 미션이 끝날 때마다 EvalStats에 누적, LogEvery마다 콘솔 출력.")]
    public bool evaluationLogging = false;
    [Tooltip("몇 미션마다 누적 성공률을 콘솔에 출력할지")]
    public int evalLogEvery = 50;

    [Header("MCTS / 평가 파라미터 (여기서 설정)")]
    [Tooltip("Phase1 강제 태스크 수 (0=커리큘럼 num_tasks). 협력 테스트는 3~6.")]
    public int overrideNumTasks = 0;
    [Tooltip("MCTS 총 iteration budget (>0). 클수록 깊은 탐색·느림.")]
    public int mctsBudget = 2000;
    [Tooltip("MCTS determinization(손패 표본) 수. 불완전정보 분산 감소.")]
    public int mctsDeterminizations = 20;
    [Tooltip("협력 목표: 목표 카드를 owner의 손이 아니라 전체 덱에서 무작위로 뽑는다.\n" +
             "→ 목표를 동료가 들고 있어 '흘려주기' 협력이 필요(MCTS 강점 발휘). 기본 off는 RL 보존용.")]
    public bool cooperativeTargets = false;

    // 현재 AI 정책 라벨 (RL / RuleBased / MCTS) — EvalStats 태그용.
    //   AI 슬롯(players[1])이 HeuristicOnly면 aiPolicy, 아니면 RL(트레이너 구동).
    public string AiPolicyLabel
    {
        get
        {
            if (players.Count >= 2)
            {
                var bp = players[1].GetComponent<BehaviorParameters>();
                if (bp != null && bp.BehaviorType != BehaviorType.HeuristicOnly)
                    return "RL";
            }
            return aiPolicy.ToString();
        }
    }

    // 인간 플레이어가 실제로 활성인지 (Simulation·배치모드면 false)
    public bool HasInteractiveHuman =>
        playMode == PlayMode.HumanVsAI && !Application.isBatchMode;

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

        // 인간 플레이어 결정:
        //   - Simulation 모드 또는 배치(서버 학습) → 전원 AI (인간 입력 대기 없음)
        //   - HumanVsAI 모드 + 비배치 + player[0]이 HeuristicOnly가 아님 → player[0] 인간
        bool player0Heuristic = players.Count > 0 &&
            players[0].GetComponent<BehaviorParameters>()?.BehaviorType == BehaviorType.HeuristicOnly;
        if (HasInteractiveHuman && players.Count > 0 && !player0Heuristic)
            players[0].isHumanPlayer = true;
        else if (players.Count > 0)
            players[0].isHumanPlayer = false;   // 시뮬레이션: player[0]도 AI

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

        if (evalLogEvery > 0) EvalStats.LogEvery = evalLogEvery;

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
