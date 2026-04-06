using System.Collections.Generic;
using UnityEngine;

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

        // 두 매니저에 단일 players 리스트 전달
        deckManager.players = players;
        trickManager.players = players;

        // 게임 시작 (미션 초기화는 카드 분배 이후 TrickManager에서 수행)
        communicationManager.InitTokens();
        trickManager.StartGame();
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
