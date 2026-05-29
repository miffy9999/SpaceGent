using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 통신 토큰 + 조난신호를 통합 관리한다.
/// - 통신 토큰: 자기 카드 1장을 공개 (최고/유일/최저 위치 표시)
/// - 조난신호: 미션 시작 전 카드 1장을 인접 플레이어에게 전달
/// </summary>
public class CommunicationManager : MonoBehaviour
{
    public static CommunicationManager Instance { get; private set; }

    private List<CommunicationToken> commTokens = new List<CommunicationToken>();

    /// <summary>미션당 공유 1개. 조난신호 토큰 상태를 나타낸다.</summary>
    public DistressSignal distressSignal { get; private set; } = new DistressSignal();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ---------------------------------------------------------------
    // 초기화
    // ---------------------------------------------------------------
    public void InitTokens()
    {
        commTokens.Clear();
        distressSignal.Reset();

        foreach (var p in GameManager.Instance.players)
            commTokens.Add(new CommunicationToken(p));

        Debug.Log("[CommManager] 통신 토큰 + 조난신호 초기화 완료");
    }

    // ---------------------------------------------------------------
    // 통신 토큰: AI 자동 선택 (최고값 비-로켓 카드)
    // ---------------------------------------------------------------
    public bool UseCommToken(CrewAgent agent)
    {
        CommunicationToken t = GetCommToken(agent);
        if (t == null || t.isUsed) return false;

        if (!GameManager.Instance.trickManager.IsBetweenTricks)
        {
            Debug.Log($"[CommToken] {agent.name} — 트릭 진행 중 통신 불가 (트릭 사이에만 가능)");
            return false;
        }
        return t.TryReveal();
    }

    /// <summary>인간 플레이어용: 특정 카드를 지정해 통신한다.</summary>
    public bool UseCommTokenWithCard(CrewAgent agent, Card card)
    {
        CommunicationToken t = GetCommToken(agent);
        if (t == null || t.isUsed) return false;

        if (!GameManager.Instance.trickManager.IsBetweenTricks)
        {
            Debug.Log($"[CommToken] {agent.name} — 트릭 진행 중 통신 불가");
            return false;
        }
        return t.TryReveal(card);
    }

    public bool HasUsedCommToken(CrewAgent agent) => GetCommToken(agent)?.isUsed ?? false;

    // ---------------------------------------------------------------
    // 조난신호: 팀이 사용 결정 → 카드 전달
    // ---------------------------------------------------------------
    public bool ActivateDistressSignal(CrewAgent player, Card card, DistressSignal.Direction dir)
        => distressSignal.Activate(player, card, dir);

    public bool ExecuteDistressSignal()
        => distressSignal.Execute();

    public bool IsDistressSignalActive => distressSignal.isActive;

    // ---------------------------------------------------------------
    // UI용 조회
    // ---------------------------------------------------------------
    public CommunicationToken GetCommToken(CrewAgent agent)
        => commTokens.Find(t => t.owner == agent);

}
