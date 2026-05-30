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

    // ── 미션 통신 규칙 ─────────────────────────────────────────────
    /// <summary>데드존: 통신 가능하나 토큰 위치(최고/유일/최저) 정보를 숨긴다.</summary>
    public bool IsDeadZone { get; private set; } = false;

    /// <summary>통신 차단 트릭 번호. 이 트릭 번호부터 통신 재개 (0=없음).</summary>
    public int CommDisruptionTrick { get; private set; } = 0;

    /// <summary>통신 불가 플레이어 (M11: 크루 1명 통신 불가).</summary>
    public CrewAgent NoCommPlayer { get; private set; } = null;

    public void SetMissionCommRules(bool deadZone, int disruptionTrick, bool onePlayerNoComm)
    {
        IsDeadZone          = deadZone;
        CommDisruptionTrick = disruptionTrick;
        NoCommPlayer        = null;

        if (onePlayerNoComm)
        {
            // 사령관 왼쪽(인덱스 +1) 플레이어에게 통신 금지
            var players = GameManager.Instance.players;
            if (players.Count > 1)
                NoCommPlayer = players[1]; // 간단히 players[1]로 지정
        }
        Debug.Log($"[CommManager] 규칙 — DeadZone:{deadZone}, Disruption:{disruptionTrick}, NoComm:{NoCommPlayer?.name ?? "없음"}");
    }

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
        IsDeadZone          = false;
        CommDisruptionTrick = 0;
        NoCommPlayer        = null;

        foreach (var p in GameManager.Instance.players)
            commTokens.Add(new CommunicationToken(p));

        Debug.Log("[CommManager] 통신 토큰 + 조난신호 초기화 완료");
    }

    // ---------------------------------------------------------------
    // 통신 가능 여부 검사 (공통)
    // ---------------------------------------------------------------
    private bool CanUseCommToken(CrewAgent agent)
    {
        if (!GameManager.Instance.trickManager.IsBetweenTricks)
        {
            Debug.Log($"[CommToken] {agent.name} — 트릭 진행 중 통신 불가");
            return false;
        }
        // M11: 해당 플레이어 통신 금지
        if (NoCommPlayer == agent)
        {
            Debug.Log($"[CommToken] {agent.name} — 이번 미션에서 통신 불가 (미션 규칙)");
            return false;
        }
        // ⚡N: 통신 차단 구간
        if (CommDisruptionTrick > 0)
        {
            int trickNum = MissionManager.Instance?.TrickNumber ?? 0;
            if (trickNum < CommDisruptionTrick)
            {
                Debug.Log($"[CommToken] {agent.name} — 통신 차단 중 (트릭 {CommDisruptionTrick} 이후 가능)");
                return false;
            }
        }
        return true;
    }

    // ---------------------------------------------------------------
    // 통신 토큰: AI 자동 선택 (최고값 비-로켓 카드)
    // ---------------------------------------------------------------
    public bool UseCommToken(CrewAgent agent)
    {
        CommunicationToken t = GetCommToken(agent);
        if (t == null || t.isUsed) return false;
        if (!CanUseCommToken(agent)) return false;
        return t.TryReveal();
    }

    /// <summary>인간 플레이어용: 특정 카드를 지정해 통신한다.</summary>
    public bool UseCommTokenWithCard(CrewAgent agent, Card card)
    {
        CommunicationToken t = GetCommToken(agent);
        if (t == null || t.isUsed) return false;
        if (!CanUseCommToken(agent)) return false;
        return t.TryReveal(card);
    }

    /// <summary>데드존 모드에서 위치 정보 없이 통신한다 (AI용).</summary>
    public bool UseCommTokenDeadZone(CrewAgent agent)
    {
        if (!IsDeadZone) return UseCommToken(agent);
        CommunicationToken t = GetCommToken(agent);
        if (t == null || t.isUsed) return false;
        if (!CanUseCommToken(agent)) return false;
        return t.TryRevealDeadZone();
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
