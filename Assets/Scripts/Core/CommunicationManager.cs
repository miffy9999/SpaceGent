using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 통신 토큰 + 소나 토큰을 통합 관리한다.
/// - 통신 토큰: 자기 카드를 공개
/// - 소나 토큰: 타인 카드를 공개
/// </summary>
public class CommunicationManager : MonoBehaviour
{
    public static CommunicationManager Instance { get; private set; }

    private List<CommunicationToken> commTokens  = new List<CommunicationToken>();
    private List<SonarToken>         sonarTokens = new List<SonarToken>();

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
        sonarTokens.Clear();

        foreach (var p in GameManager.Instance.players)
        {
            commTokens.Add(new CommunicationToken(p));
            sonarTokens.Add(new SonarToken(p));
        }

        Debug.Log("[TokenManager] 통신·소나 토큰 초기화 완료");
    }

    // ---------------------------------------------------------------
    // 통신 토큰 사용
    // ---------------------------------------------------------------
    public bool UseCommToken(CrewAgent agent)
    {
        CommunicationToken t = GetCommToken(agent);
        if (t == null || t.isUsed) return false;

        // 실제 딥 씨 크루 규칙: 트릭 진행 중(카드가 1장 이상 올라간 상태)에는 통신 불가
        if (!GameManager.Instance.trickManager.IsBetweenTricks)
        {
            Debug.Log($"[CommToken] {agent.name} — 트릭 진행 중 통신 불가 (트릭 사이에만 가능)");
            return false;
        }

        return t.TryReveal();
    }

    public bool HasUsedCommToken(CrewAgent agent) => GetCommToken(agent)?.isUsed ?? false;

    // ---------------------------------------------------------------
    // 소나 토큰 사용
    // agent: 사용자, relativeTarget: 상대적 플레이어 인덱스 (1~3)
    // ---------------------------------------------------------------
    public bool UseSonarToken(CrewAgent agent, int relativeTarget)
    {
        SonarToken t = GetSonarToken(agent);
        if (t == null || t.isUsed) return false;

        var players    = GameManager.Instance.players;
        int selfIndex  = players.IndexOf(agent);
        int targetIndex = (selfIndex + relativeTarget) % players.Count;
        CrewAgent target = players[targetIndex];

        return t.TryReveal(target);
    }

    public bool HasUsedSonarToken(CrewAgent agent) => GetSonarToken(agent)?.isUsed ?? false;

    // ---------------------------------------------------------------
    // 관찰 벡터
    // ---------------------------------------------------------------

    // 통신 토큰 관찰 (44개)
    //   [0~3]  : 플레이어별 사용 여부
    //   [4~43] : 공개 카드 원-핫 OR 합산
    public float[] GetCommObservation()
    {
        var players = GameManager.Instance.players;
        float[] obs = new float[44];

        for (int i = 0; i < players.Count; i++)
        {
            CommunicationToken t = GetCommToken(players[i]);
            obs[i] = (t != null && t.isUsed) ? 1f : 0f;
        }

        foreach (CommunicationToken t in commTokens)
        {
            if (!t.isUsed || t.revealedCard == null) continue;
            int idx = 4 + players[0].GetCardIndex(t.revealedCard);
            if (idx < obs.Length) obs[idx] = 1f;
        }

        return obs;
    }

    // 소나 토큰 관찰 (44개)
    //   [0~3]  : 플레이어별 사용 여부
    //   [4~43] : 소나로 공개된 카드 원-핫 OR 합산
    public float[] GetSonarObservation()
    {
        var players = GameManager.Instance.players;
        float[] obs = new float[44];

        for (int i = 0; i < players.Count; i++)
        {
            SonarToken t = GetSonarToken(players[i]);
            obs[i] = (t != null && t.isUsed) ? 1f : 0f;
        }

        foreach (SonarToken t in sonarTokens)
        {
            if (!t.isUsed || t.revealedCard == null) continue;
            int idx = 4 + players[0].GetCardIndex(t.revealedCard);
            if (idx < obs.Length) obs[idx] = 1f;
        }

        return obs;
    }

    // ---------------------------------------------------------------
    // UI용 조회
    // ---------------------------------------------------------------
    public CommunicationToken GetCommToken(CrewAgent agent) =>
        commTokens.Find(t => t.owner == agent);

    public SonarToken GetSonarToken(CrewAgent agent) =>
        sonarTokens.Find(t => t.owner == agent);
}
