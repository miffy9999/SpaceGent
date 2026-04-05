using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 4명의 통신 토큰 상태를 보관하고,
/// CrewAgent 관찰 벡터용 데이터를 제공한다.
/// </summary>
public class CommunicationManager : MonoBehaviour
{
    public static CommunicationManager Instance { get; private set; }

    private List<CommunicationToken> tokens = new List<CommunicationToken>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ---------------------------------------------------------------
    // 새 판 시작 시 초기화 (GameManager → 여기)
    // ---------------------------------------------------------------
    public void InitTokens()
    {
        tokens.Clear();
        foreach (var p in GameManager.Instance.players)
            tokens.Add(new CommunicationToken(p));

        Debug.Log("[CommManager] 통신 토큰 초기화 완료");
    }

    // ---------------------------------------------------------------
    // 특정 플레이어가 토큰을 사용
    // ---------------------------------------------------------------
    public bool UseToken(CrewAgent agent)
    {
        CommunicationToken token = GetToken(agent);
        if (token == null || token.isUsed) return false;
        return token.TryReveal();
    }

    public bool HasUsedToken(CrewAgent agent)
    {
        return GetToken(agent)?.isUsed ?? false;
    }

    // ---------------------------------------------------------------
    // 관찰 벡터용 데이터 (총 44개)
    //   [0~3]  : 각 플레이어 토큰 사용 여부 (0/1)
    //   [4~43] : 공개된 카드 원-핫 합산 (40칸)
    // ---------------------------------------------------------------
    public float[] GetObservation()
    {
        var players = GameManager.Instance.players;
        float[] obs = new float[44];

        // 토큰 사용 여부
        for (int i = 0; i < players.Count; i++)
        {
            CommunicationToken t = GetToken(players[i]);
            obs[i] = (t != null && t.isUsed) ? 1f : 0f;
        }

        // 공개된 카드 원-핫 (여러 명이 공개했으면 OR 합산)
        foreach (CommunicationToken t in tokens)
        {
            if (!t.isUsed || t.revealedCard == null) continue;
            int idx = 4 + GameManager.Instance.players[0].GetCardIndex(t.revealedCard);
            if (idx < obs.Length) obs[idx] = 1f;
        }

        return obs;
    }

    private CommunicationToken GetToken(CrewAgent agent)
    {
        return tokens.Find(t => t.owner == agent);
    }
}
