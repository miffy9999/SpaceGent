using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrickManager : MonoBehaviour
{
    [HideInInspector] public List<CrewAgent> players = new List<CrewAgent>();

    public GamePhase currentPhase { get; private set; } = GamePhase.Setup;

    public Card.Suit leadSuit;
    public List<Card>      cardsOnTable   = new List<Card>();
    public List<CrewAgent> playersOnTable = new List<CrewAgent>();

    private int captainIndex  = 0;
    private int trickLeadIndex = 0;   // 현재 트릭의 선 플레이어 인덱스

    // currentPlayerIndex: 지금 턴을 가진(또는 가져야 할) 플레이어 인덱스.
    // StartNewTrick / OnCardPlayed 에서만 갱신된다.
    private int currentPlayerIndex = 0;

    private Coroutine turnTimeoutCoroutine;
    private Coroutine watchdogCoroutine;
    private Coroutine restartCoroutine;

    // [v4 Opponent Modeling] 핸드 내 플레이어별 void(보유 안 한 suit) 추적.
    //   매 트릭 종료 시: lead suit를 follow 안 한 플레이어는 그 suit에 void 확정.
    //   Smart Helper Lead가 "담당자 void suit 회피" 결정에 사용.
    //   핸드 시작 시 Clear.
    private Dictionary<CrewAgent, HashSet<Card.Suit>> knownVoids
        = new Dictionary<CrewAgent, HashSet<Card.Suit>>();

    /// <summary>
    /// 실제 딥 씨 크루 규칙: 통신 토큰은 트릭과 트릭 사이(아무도 카드를 내지 않은 상태)에만 사용 가능.
    /// 트릭이 시작되어 첫 카드가 나오면 false가 된다.
    /// </summary>
    public bool IsBetweenTricks { get; private set; } = true;

    // ---------------------------------------------------------------
    // GameManager.Start()에서 호출
    // ---------------------------------------------------------------
    public void StartGame()
    {
        currentPhase = GamePhase.Setup;

        // 모든 플레이어 턴 초기화 (에피소드 리셋 안전)
        ResetAllTurns();

        // [v4] Void 정보 초기화 (이전 핸드의 정보 무효)
        knownVoids.Clear();

        // 1. 카드 분배
        deckManager.DealCardsToAgents();

        // 2. 함장 결정
        captainIndex = FindCaptainIndex();
        Debug.Log($"[TrickManager] 함장: {players[captainIndex].name}");

        // 3. 통신 토큰 초기화
        GameManager.Instance.communicationManager.InitTokens();

        // 4. Watchdog 재시작 (Playing 페이즈에서만 동작)
        if (watchdogCoroutine != null) StopCoroutine(watchdogCoroutine);
        watchdogCoroutine = StartCoroutine(TurnWatchdog());

        // 5. 태스크 선택 단계
        currentPhase = GamePhase.TaskSelection;
        MissionManager.Instance.StartTaskSelectionPhase(captainIndex);
    }

    // ---------------------------------------------------------------
    // 태스크 선택 완료 후 MissionManager에서 호출
    // 조난신호 단계를 먼저 거친 뒤 트릭 플레이 시작
    // ---------------------------------------------------------------
    public void StartPlaying()
    {
        StartDistressSignalPhase();
    }

    // ---------------------------------------------------------------
    // 조난신호 단계 (첫 트릭 전 선택 사항)
    //   인간 플레이어가 있으면 UI/키보드 입력을 기다린다.
    //   AI 전용(배치 모드 포함)이면 즉시 게임 시작.
    // ---------------------------------------------------------------
    public void StartDistressSignalPhase()
    {
        currentPhase = GamePhase.DistressSignal;

        bool hasHuman = false;
        foreach (var p in players)
            if (p.isHumanPlayer) { hasHuman = true; break; }

        if (!hasHuman || Application.isBatchMode)
        {
            // AI 전용: 조난신호 스킵하고 바로 게임 시작
            StartTrickPlay();
            return;
        }

        Debug.Log("[조난신호] 단계 시작 — D: 카드 선택 후 활성화 / Space: 건너뛰기");
        GameManager.Instance.uiManager?.ShowDistressSignalPhase();
    }

    /// <summary>인간 플레이어가 조난신호 활성화 또는 스킵 결정 후 호출</summary>
    public void ConfirmDistressSignal()
    {
        var cm = GameManager.Instance.communicationManager;
        if (cm != null && cm.IsDistressSignalActive)
            cm.ExecuteDistressSignal();

        GameManager.Instance.uiManager?.HideDistressSignalPhase();
        StartTrickPlay();
    }

    private void StartTrickPlay()
    {
        currentPhase = GamePhase.Playing;
        StartNewTrick(captainIndex);
    }

    // ---------------------------------------------------------------
    // 새 트릭 시작
    // ---------------------------------------------------------------
    public void StartNewTrick(int leadingPlayerIndex)
    {
        Debug.Log($"--- 새 트릭 | 선: {players[leadingPlayerIndex].name} ---");

        // 이전 트릭의 남은 isMyTurn 플래그를 모두 초기화한다.
        // 늦게 도착하는 RequestDecision 응답이 새 트릭에 섞이지 않도록 방지.
        ResetAllTurns();

        cardsOnTable.Clear();
        playersOnTable.Clear();
        leadSuit = Card.Suit.Submarine;

        trickLeadIndex     = leadingPlayerIndex;
        currentPlayerIndex = leadingPlayerIndex;

        IsBetweenTricks = true;   // 새 트릭 시작 전 = 통신 가능 구간
        GiveTurnToPlayer(currentPlayerIndex);
    }

    // ---------------------------------------------------------------
    // 카드 제출 알림 (CrewAgent.PlayCard → 이곳으로)
    // ---------------------------------------------------------------
    public void OnCardPlayed(CrewAgent player, Card playedCard)
    {
        // 이미 이번 트릭에서 카드를 낸 플레이어 → 무시
        if (playersOnTable.Contains(player))
        {
            Debug.LogWarning($"[TrickManager] {player.name} 중복 제출 무시");
            return;
        }

        // Playing 단계가 아니면 버린다 (TaskSelection 중 오발 방지)
        if (currentPhase != GamePhase.Playing)
        {
            Debug.LogWarning($"[TrickManager] {player.name} — Playing 아닌 단계({currentPhase})에서 카드 제출 무시");
            return;
        }

        IsBetweenTricks = false;  // 첫 카드가 나오는 순간 통신 불가
        cardsOnTable.Add(playedCard);
        playersOnTable.Add(player);

        // 첫 번째 카드가 선 수트를 결정 (잠수함이 첫 카드면 잠수함이 리드 슈트)
        if (cardsOnTable.Count == 1)
            leadSuit = playedCard.suit;

        if (cardsOnTable.Count >= players.Count)
        {
            DetermineTrickWinner();
        }
        else
        {
            // 다음 플레이어: 선 인덱스 + 제출 수로 결정
            // (currentPlayerIndex + 1 대신 이 식을 쓰면, Watchdog 오발 후에도 순서가 올바르다)
            currentPlayerIndex = (trickLeadIndex + playersOnTable.Count) % players.Count;
            GiveTurnToPlayer(currentPlayerIndex);
        }
    }

    // ---------------------------------------------------------------
    // 승자 판별
    // ---------------------------------------------------------------
    private void DetermineTrickWinner()
    {
        int  winnerIdx   = 0;
        Card winningCard = cardsOnTable[0];

        for (int i = 1; i < cardsOnTable.Count; i++)
        {
            Card c      = cardsOnTable[i];
            bool cIsSub = c.suit == Card.Suit.Submarine;
            bool wIsSub = winningCard.suit == Card.Suit.Submarine;

            if      (cIsSub && !wIsSub)                                 { winningCard = c; winnerIdx = i; }
            else if (cIsSub && wIsSub  && c.value > winningCard.value)  { winningCard = c; winnerIdx = i; }
            else if (c.suit == leadSuit && winningCard.suit == leadSuit
                     && c.value > winningCard.value)                    { winningCard = c; winnerIdx = i; }
        }

        // [v4 Opponent Modeling] Void 정보 업데이트
        //   lead suit를 follow 안 한 플레이어 = 그 suit에 void 확정.
        //   (lead suit가 sub면 그것도 동일 처리)
        for (int i = 0; i < playersOnTable.Count; i++)
        {
            if (cardsOnTable[i].suit != leadSuit)
            {
                CrewAgent p = playersOnTable[i];
                if (!knownVoids.ContainsKey(p))
                    knownVoids[p] = new HashSet<Card.Suit>();
                knownVoids[p].Add(leadSuit);
            }
        }

        CrewAgent winner      = playersOnTable[winnerIdx];
        int       nextLead    = players.IndexOf(winner);

        Debug.Log($"트릭 승자: {winner.name} ({winningCard.suit} {winningCard.value})");

        // opener: 트릭을 처음 연 플레이어 (playersOnTable[0])
        // openerSuit: 첫 카드의 실제 슈트 (leadSuit는 잠수함 제외하므로 별도 전달)
        MissionManager.Instance.OnTrickResolved(
            winner,
            new List<Card>(cardsOnTable),
            playersOnTable[0],
            cardsOnTable[0].suit,
            leadSuit);
        ClearTableAndStartNextTrick(nextLead);
    }

    // ---------------------------------------------------------------
    // 테이블 정리 → 다음 트릭 or 게임 종료
    // ---------------------------------------------------------------
    private void ClearTableAndStartNextTrick(int nextLeadIndex)
    {
        foreach (Transform child in GameManager.Instance.centerBoard)
            Destroy(child.gameObject);

        if (players[0].hand.Count == 0)
        {
            // 모든 카드를 냈으면 핸드 종료
            MissionManager.Instance.OnHandEnded();
            EndGame();
        }
        else if (MissionManager.Instance.HasMissionEnded)
        {
            // 트릭 도중 미션 실패 확정 → 즉시 종료
            //   [버그 수정] OnHandEnded를 호출해야 EvaluationStats에 fail이 기록됨.
            //   OnHandEnded는 isFailed/isCompleted된 task를 스킵하고, missionEnded면
            //   GiveTeamReward도 스킵하므로 중복 처리 위험 없음.
            MissionManager.Instance.OnHandEnded();
            EndGame();
        }
        else
        {
            StartNewTrick(nextLeadIndex);
        }
    }

    // ---------------------------------------------------------------
    // 게임 종료
    // ---------------------------------------------------------------
    private void EndGame()
    {
        Debug.Log("[TrickManager] 에피소드 종료");

        currentPhase = GamePhase.Result;

        // 코루틴 정지
        if (turnTimeoutCoroutine != null) { StopCoroutine(turnTimeoutCoroutine); turnTimeoutCoroutine = null; }
        if (watchdogCoroutine    != null) { StopCoroutine(watchdogCoroutine);    watchdogCoroutine    = null; }

        // 모든 턴 초기화 후 에피소드 종료
        // PPO(helperHeuristicOnly)면 전원 EndEpisode, POCA면 EndGroupEpisode
        ResetAllTurns();
        GameManager.Instance.EndGroupOrLearnerEpisode();

        restartCoroutine = StartCoroutine(RestartAfterEpisodeEnd());
    }

    private IEnumerator RestartAfterEpisodeEnd()
    {
        // 학습 속도를 위해 배치 모드에서는 즉시 재시작.
        // 인간 플레이어 없으면(all-rule-based 시뮬레이션 모드 포함) 즉시 재시작.
        bool hasHuman = false;
        foreach (var p in players)
            if (p.isHumanPlayer) { hasHuman = true; break; }
        float wait = (Application.isBatchMode || !hasHuman) ? 0.01f : 5f;
        yield return new WaitForSeconds(wait);
        restartCoroutine = null;
        StartGame();
    }

    // 결과 화면의 재시작 버튼에서 호출
    public void ManualRestart()
    {
        if (restartCoroutine != null) { StopCoroutine(restartCoroutine); restartCoroutine = null; }
        StartGame();
    }

    // ---------------------------------------------------------------
    // 턴 부여 + 타임아웃 감시
    // ---------------------------------------------------------------
    private void GiveTurnToPlayer(int index)
    {
        // [핵심 수정] 다른 플레이어의 isMyTurn을 먼저 모두 끄고 대상만 켠다.
        // → 이전 RequestDecision 응답이 늦게 도착해도 isMyTurn=false 로 즉시 무효화된다.
        ResetAllTurns();
        players[index].isMyTurn = true;

        if (!players[index].isHumanPlayer)
            players[index].RequestDecision();

        // 기존 타임아웃 취소 후 새로 시작
        if (turnTimeoutCoroutine != null)
            StopCoroutine(turnTimeoutCoroutine);
        turnTimeoutCoroutine = StartCoroutine(TurnTimeout(index));

        Debug.Log($"→ {players[index].name}의 차례");
    }

    // 일정 시간 안에 카드를 내지 않으면 RequestDecision 재요청 (AI 전용)
    private IEnumerator TurnTimeout(int index)
    {
        yield return new WaitForSeconds(5f);

        if (players[index].isMyTurn && !players[index].isHumanPlayer)
        {
            Debug.LogWarning($"[TrickManager] {players[index].name} 5초 타임아웃 → RequestDecision 재요청");
            players[index].RequestDecision();
        }
    }

    // ---------------------------------------------------------------
    // Watchdog: 아무도 턴을 갖지 않을 때 복구
    // [핵심 수정] Playing 페이즈에서만 동작. 복구 대상도 트릭 상태에서 계산.
    // ---------------------------------------------------------------
    private IEnumerator TurnWatchdog()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);

            // Playing 단계가 아니면 완전히 건너뛴다
            if (currentPhase != GamePhase.Playing) continue;

            bool anyoneHasTurn = false;
            foreach (var p in players)
                if (p.isMyTurn) { anyoneHasTurn = true; break; }

            if (!anyoneHasTurn)
            {
                // 트릭 상태(선 인덱스 + 이미 낸 카드 수)로 올바른 다음 플레이어를 산출
                int expectedIndex = (trickLeadIndex + playersOnTable.Count) % players.Count;
                Debug.LogWarning($"[Watchdog] 턴 소유자 없음 → {players[expectedIndex].name} 강제 복구");
                GiveTurnToPlayer(expectedIndex);
            }
        }
    }

    // ---------------------------------------------------------------
    // Follow Suit 규칙 검사
    // ---------------------------------------------------------------
    public bool IsValidPlay(CrewAgent player, Card cardToPlay)
    {
        // 트릭 첫 카드는 무엇이든 가능
        if (cardsOnTable.Count == 0)             return true;

        // 리드 슈트와 같은 카드는 항상 합법 (잠수함 리드면 잠수함끼리 비교)
        if (cardToPlay.suit == leadSuit)         return true;

        // 손패에 리드 슈트 카드가 있으면 반드시 그 슈트를 내야 한다 (잠수함도 예외 없음)
        foreach (Card c in player.hand)
            if (c.suit == leadSuit) return false;

        return true;
    }

    // ---------------------------------------------------------------
    // 플레이 평가 (Phase1 협력 계측/보상용) — 카드 제출 "직전"에 호출
    //   couldWin : toPlay가 현재 테이블 기준 이길 수 있는 카드인가
    //   hadSafe  : 합법 카드 중 '안 이기는' 선택지가 있었는가
    //   리드(테이블 비어있음)는 비교 대상이 없어 (false,false)
    // ---------------------------------------------------------------
    public (bool couldWin, bool hadSafe) EvaluatePlay(CrewAgent player, Card toPlay)
    {
        if (cardsOnTable.Count == 0) return (false, false);

        Card best = cardsOnTable[0];
        for (int i = 1; i < cardsOnTable.Count; i++)
            if (Beats(cardsOnTable[i], best)) best = cardsOnTable[i];

        bool couldWin = Beats(toPlay, best);

        bool hadSafe = false;
        foreach (Card c in player.hand)
            if (IsValidPlay(player, c) && !Beats(c, best)) { hadSafe = true; break; }

        return (couldWin, hadSafe);
    }

    // [진단용/Rule-based] 합법 카드 중 '가장 안 이길' 카드의 손패 인덱스.
    //   1차: 현재 테이블 최강을 안 이기는 합법 카드 중 WinStrength 최솟값.
    //        (follow-suit 강제로 본의 아니게 이기는 케이스 방지 — Throw의 진짜 의미)
    //   2차 fallback: 모든 합법 카드가 이기는 상황 → 그중 WinStrength 최솟값
    //                 (어쩔 수 없이 이김, "이왕이면 약한 카드로")
    //   리드 상황(테이블 빔): 비교 대상 없으므로 그냥 최솟값.
    public int SafestLegalCardIndex(CrewAgent player)
    {
        // 1차 시도: 현재 winning을 안 이기는 카드 중 가장 약한 것
        if (cardsOnTable.Count > 0)
        {
            Card best = cardsOnTable[0];
            for (int i = 1; i < cardsOnTable.Count; i++)
                if (Beats(cardsOnTable[i], best)) best = cardsOnTable[i];

            int loseIdx = -1, loseScore = int.MaxValue;
            for (int i = 0; i < player.hand.Count; i++)
            {
                if (!IsValidPlay(player, player.hand[i])) continue;
                if (Beats(player.hand[i], best)) continue;     // 이기는 카드 스킵
                int score = WinStrength(player.hand[i]);
                if (score < loseScore) { loseScore = score; loseIdx = i; }
            }
            if (loseIdx >= 0) return loseIdx;
            // 모든 합법 카드가 이김 → 2차 fallback으로
        }

        // 2차 (또는 리드): 합법 카드 중 WinStrength 최솟값
        int bestIdx = -1, bestScore = int.MaxValue;
        for (int i = 0; i < player.hand.Count; i++)
        {
            if (!IsValidPlay(player, player.hand[i])) continue;
            int score = WinStrength(player.hand[i]);
            if (score < bestScore) { bestScore = score; bestIdx = i; }
        }
        return bestIdx;
    }

    // [Rule-based helper - Mode B] 적극 이기기:
    //   테이블에 카드가 있으면 → 현재 최강을 이기는 카드 중 WinStrength 가장 낮은 것
    //   리드(테이블 빈 상태)면 → 합법 카드 중 가장 강한 카드(잠수함 우선)로 트릭을 가져감
    //   이길 카드가 전혀 없으면 -1 (호출자가 fallback)
    public int ClaimingLegalCardIndex(CrewAgent player)
    {
        // 리드 상황: 비교 대상이 없으므로 가장 확실히 이길 카드 = 가장 강한 카드
        if (cardsOnTable.Count == 0)
            return HighestLegalCardIndex(player);

        // 현재 테이블의 최강 카드 결정 (DetermineTrickWinner/EvaluatePlay와 동일 규칙)
        Card best = cardsOnTable[0];
        for (int i = 1; i < cardsOnTable.Count; i++)
            if (Beats(cardsOnTable[i], best)) best = cardsOnTable[i];

        int bestIdx = -1, bestScore = int.MaxValue;
        for (int i = 0; i < player.hand.Count; i++)
        {
            Card c = player.hand[i];
            if (!IsValidPlay(player, c)) continue;
            if (!Beats(c, best)) continue;       // 이길 수 없는 카드는 스킵
            int score = WinStrength(c);
            if (score < bestScore) { bestScore = score; bestIdx = i; }
        }
        return bestIdx;
    }

    // [Rule-based helper 보조] 합법 카드 중 가장 강한 카드 (리드 시 사용)
    public int HighestLegalCardIndex(CrewAgent player)
    {
        int bestIdx = -1, bestScore = int.MinValue;
        for (int i = 0; i < player.hand.Count; i++)
        {
            if (!IsValidPlay(player, player.hand[i])) continue;
            int score = WinStrength(player.hand[i]);
            if (score > bestScore) { bestScore = score; bestIdx = i; }
        }
        return bestIdx;
    }

    // ───────────────────────────────────────────────────────────────
    // [v4] Opponent Modeling — Void 추적 + Smart Lead
    // ───────────────────────────────────────────────────────────────

    // 해당 플레이어가 그 suit에 void(보유 안 한)인 게 확정됐는가
    public bool IsKnownVoid(CrewAgent player, Card.Suit suit)
    {
        return knownVoids.TryGetValue(player, out var set) && set.Contains(suit);
    }

    // [v4 — 현행] Smart Throw Lead — 담당자가 void인 suit를 피함.
    //   목표: 담당자가 자연스럽게 follow 가능한 suit로 lead → 담당자 winning 확률 ↑
    //   1차: 합법 + 비-잠수함 + 담당자 not void + WinStrength 최솟값
    //   2차 fallback: 기존 Safest (1차 후보 없을 때)
    //
    //   [v5 폐기 2026-05-28]: 다중 플레이어 void 점수 추가했으나 회귀 유발 (-6.9%p WinLast).
    //   가설: 다른 도우미들 void인 suit는 "전체적으로 소진된 suit"라 담당자도 약함.
    //   → v4 단순함이 robust. CHANGELOG.md v5 참고.
    public int SafestLeadForAssignee(CrewAgent player, CrewAgent assignee)
    {
        if (assignee == null) return SafestLegalCardIndex(player);

        int idx = -1, score = int.MaxValue;
        for (int i = 0; i < player.hand.Count; i++)
        {
            Card c = player.hand[i];
            if (!IsValidPlay(player, c)) continue;
            if (c.suit == Card.Suit.Submarine) continue;      // sub lead 회피 (담당자 sub 없으면 trump 못 침)
            if (IsKnownVoid(assignee, c.suit)) continue;       // 담당자 void인 suit 회피
            int sc = WinStrength(c);
            if (sc < score) { score = sc; idx = i; }
        }
        if (idx >= 0) return idx;

        // 폴백: 회피 조건이 모두 막힌 경우 기존 Safest
        return SafestLegalCardIndex(player);
    }

    // ───────────────────────────────────────────────────────────────
    // [v3] 카드 메모리 기반 "Guaranteed Winner" 판정 + Smart Claim
    // ───────────────────────────────────────────────────────────────

    // 카드 c가 "보장된 winning"인가:
    //   1. 현재 테이블 최강을 이길 수 있고
    //   2. 아직 안 나온 모든 카드 중 c를 이길 수 있는 게 없음
    //   → 이후 누가 무엇을 내든 트릭을 가져옴
    public bool IsGuaranteedWinner(Card c, CrewAgent self, HashSet<Card> playedInHand)
    {
        if (playedInHand == null) return false;

        // 1. 현재 테이블 최강을 이기는지 확인 (lead 상황이면 통과)
        if (cardsOnTable.Count > 0)
        {
            Card best = cardsOnTable[0];
            for (int i = 1; i < cardsOnTable.Count; i++)
                if (Beats(cardsOnTable[i], best)) best = cardsOnTable[i];
            if (!Beats(c, best)) return false;
        }

        // 2. 남은 미플레이 카드(다른 플레이어 손) 중 c를 이길 수 있는지 검사
        //    "미플레이" = 전체 40장 - 핸드 누적 played - 현재 테이블 - 내 손패
        for (int s = 0; s < 4; s++)
            for (int v = 1; v <= 9; v++)
            {
                Card other = new Card((Card.Suit)s, v);
                if (playedInHand.Contains(other)) continue;
                if (cardsOnTable.Contains(other)) continue;
                if (self.hand.Contains(other))    continue;
                if (Beats(other, c))              return false;
            }
        for (int v = 1; v <= 4; v++)
        {
            Card other = new Card(Card.Suit.Submarine, v);
            if (playedInHand.Contains(other)) continue;
            if (cardsOnTable.Contains(other)) continue;
            if (self.hand.Contains(other))    continue;
            if (Beats(other, c))              return false;
        }
        return true;
    }

    // [v3] Smart Claim — 보장된 winning을 우선, 없으면 기존 Claim 폴백.
    //   Block 행동의 업그레이드 버전. playedInHand=null이면 기존 동작과 동일.
    public int SmartClaimCardIndex(CrewAgent player, HashSet<Card> playedInHand)
    {
        if (playedInHand != null)
        {
            // 1차: 합법 + 현재 테이블을 이기는 + guaranteed 중 WinStrength 최솟값
            int gIdx = -1, gScore = int.MaxValue;
            for (int i = 0; i < player.hand.Count; i++)
            {
                Card c = player.hand[i];
                if (!IsValidPlay(player, c)) continue;
                if (!IsGuaranteedWinner(c, player, playedInHand)) continue;
                int score = WinStrength(c);
                if (score < gScore) { gScore = score; gIdx = i; }
            }
            if (gIdx >= 0) return gIdx;
        }
        // 2차: 기존 Claim (lowest beating) 폴백
        return ClaimingLegalCardIndex(player);
    }

    // ───────────────────────────────────────────────────────────────
    // 상황 조회 (Rule-based helper용)
    // ───────────────────────────────────────────────────────────────

    // 이번 트릭이 핸드의 마지막 트릭인가 (진행 중에도 호출 가능)
    //   모든 플레이어의 (남은 손패 + 이번 트릭 제출 여부) ≤ 1 이면 마지막
    public bool IsLastTrickInProgress()
    {
        foreach (var p in players)
        {
            int total = p.hand.Count + (playersOnTable.Contains(p) ? 1 : 0);
            if (total > 1) return false;
        }
        return true;
    }

    // 해당 플레이어가 이번 트릭에 카드를 냈는가
    public bool HasPlayerPlayedThisTrick(CrewAgent p) => playersOnTable.Contains(p);

    // 해당 플레이어의 이번 트릭 카드가 현재 최강(이기는 중)인가
    //   아직 안 낸 플레이어가 있어도 "지금 시점" 기준 판정.
    public bool IsPlayerCurrentlyWinning(CrewAgent p)
    {
        int idx = playersOnTable.IndexOf(p);
        if (idx < 0) return false;
        Card mine = cardsOnTable[idx];
        for (int i = 0; i < cardsOnTable.Count; i++)
        {
            if (i == idx) continue;
            if (Beats(cardsOnTable[i], mine)) return false;
        }
        return true;
    }

    // 카드의 승리 가능성 점수(낮을수록 안 이김): 다른색 비트럼프 < 리드색 < 잠수함
    //   리드 중(테이블 빔, leadSuit=Submarine)이면 비-잠수함이 낮은 값으로 → 낮은 카드 리드
    private int WinStrength(Card c)
    {
        if (c.suit == Card.Suit.Submarine) return 200 + c.value;
        if (c.suit == leadSuit)            return 100 + c.value;
        return c.value;
    }

    // a가 b를 이기나 (현재 leadSuit 기준, DetermineTrickWinner와 동일 규칙)
    private bool Beats(Card a, Card b)
    {
        bool aSub = a.suit == Card.Suit.Submarine;
        bool bSub = b.suit == Card.Suit.Submarine;
        if (aSub && !bSub) return true;
        if (aSub && bSub)  return a.value > b.value;
        if (!aSub && bSub) return false;
        bool aLead = a.suit == leadSuit;
        bool bLead = b.suit == leadSuit;
        if (aLead && !bLead) return true;
        if (!aLead)          return false;   // a가 리드 슈트가 아니면 못 이김
        return a.value > b.value;            // 둘 다 리드 슈트
    }

    // ---------------------------------------------------------------
    // 함장 (잠수함 4번 소지자)
    // ---------------------------------------------------------------
    private int FindCaptainIndex()
    {
        for (int i = 0; i < players.Count; i++)
            foreach (Card c in players[i].hand)
                if (c.suit == Card.Suit.Submarine && c.value == 4)
                    return i;

        Debug.LogWarning("[TrickManager] 잠수함 4번을 찾지 못했습니다. 0번이 함장이 됩니다.");
        return 0;
    }

    // ---------------------------------------------------------------
    // 모든 플레이어의 isMyTurn 플래그 일괄 초기화
    // ---------------------------------------------------------------
    private void ResetAllTurns()
    {
        foreach (var p in players)
            p.isMyTurn = false;
    }

    private DeckManager deckManager => GameManager.Instance.deckManager;
}
