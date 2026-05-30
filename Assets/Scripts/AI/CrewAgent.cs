using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CrewAgent : Agent
{
    [Header("Agent Data")]
    public List<Card> hand = new List<Card>();
    public bool isMyTurn = false;

    [Header("플레이어 유형 (GameManager가 자동 설정)")]
    public bool isHumanPlayer = false;

    [Header("카드 프리팹 (중앙 테이블 스폰용)")]
    public GameObject cardPrefab;

    private int pendingCommAction = 0;

    private TrickManager         trickManager => GameManager.Instance.trickManager;
    private CommunicationManager commManager  => GameManager.Instance.communicationManager;

    // ---------------------------------------------------------------
    // 카드 분배
    // ---------------------------------------------------------------
    public void ReceiveCard(Card newCard) => hand.Add(newCard);
    public void ClearHand()               => hand.Clear();

    // ---------------------------------------------------------------
    // UI 손패 클릭 (HandCardUI 전용)
    // ---------------------------------------------------------------
    public void SelectCard(int index)
    {
        if (!isMyTurn || !isHumanPlayer) return;
        HumanDirectPlay(index);
    }

    // ---------------------------------------------------------------
    // 키보드 입력 (인간 플레이어)
    //   숫자 1~0 : 카드 선택
    //   Space    : 통신 토큰 사용 예약
    // ---------------------------------------------------------------
    void Update()
    {
        if (!isMyTurn || !isHumanPlayer) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        if (kb.spaceKey.wasPressedThisFrame) pendingCommAction = 1;

        int idx = -1;
        if      (kb.digit1Key.wasPressedThisFrame) idx = 0;
        else if (kb.digit2Key.wasPressedThisFrame) idx = 1;
        else if (kb.digit3Key.wasPressedThisFrame) idx = 2;
        else if (kb.digit4Key.wasPressedThisFrame) idx = 3;
        else if (kb.digit5Key.wasPressedThisFrame) idx = 4;
        else if (kb.digit6Key.wasPressedThisFrame) idx = 5;
        else if (kb.digit7Key.wasPressedThisFrame) idx = 6;
        else if (kb.digit8Key.wasPressedThisFrame) idx = 7;
        else if (kb.digit9Key.wasPressedThisFrame) idx = 8;
        else if (kb.digit0Key.wasPressedThisFrame) idx = 9;

        if (idx >= 0) HumanDirectPlay(idx);
    }

    // ---------------------------------------------------------------
    // 인간 플레이어 직접 카드 제출
    //   ML-Agents RequestDecision/Heuristic 파이프라인을 완전히 우회.
    //   BehaviorType 설정이나 모델 유무와 무관하게 항상 동작한다.
    // ---------------------------------------------------------------
    private void HumanDirectPlay(int index)
    {
        if (index < 0 || index >= hand.Count)
        {
            Debug.Log($"[Human] 카드 인덱스 {index} 범위 초과 (손패 {hand.Count}장)");
            return;
        }

        Card cardToPlay = hand[index];
        if (!trickManager.IsValidPlay(this, cardToPlay))
        {
            Debug.Log($"[Human] {cardToPlay} — follow-suit 위반, 다른 카드를 선택하세요");
            return;
        }

        // Space로 예약된 통신 토큰 처리
        if (pendingCommAction == 1) { commManager.UseCommToken(this); pendingCommAction = 0; }

        isMyTurn = false;
        PlayCard(index);
    }

    // Heuristic — [rule_based + MCTS] HeuristicOnly 모드에서 호출.
    //
    //   역할 dispatch:
    //     - 본인이 담당자(assignee) + env.mcts_assignee=1 → MCTSSearch
    //     - 본인이 담당자(assignee)                       → AssigneeStrategy (rule-based)
    //     - 본인이 도우미(helper)                         → HelperStrategy   (rule-based)
    //
    //   env 파라미터:
    //     mcts_assignee   : 1이면 담당자가 MCTS 사용 (기본 0 = rule-based)
    //     mcts_budget     : 총 iteration 수 (기본 500)
    //     mcts_dets       : determinization 샘플 수 (기본 10)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        d.Clear();
        if (trickManager != null && hand.Count > 0 && d.Length > 0)
        {
            int idx = -1;
            var mm = MissionManager.Instance;
            if (mm != null)
            {
                bool isAssignee = mm.IsPhase1Assignee(this);
                if (isAssignee && IsMCTSAssigneeEnabled())
                {
                    idx = DecideWithMCTS(mm);
                }
                else
                {
                    idx = isAssignee
                        ? mm.HeuristicAssigneeCardIndex(this)
                        : mm.HeuristicHelperCardIndex(this);
                }
            }
            if (idx < 0) idx = trickManager.SafestLegalCardIndex(this);

            // follow-suit 최종 검증: 위반이면 SafestLegalCard로 교체
            if (idx >= 0 && idx < hand.Count && !trickManager.IsValidPlay(this, hand[idx]))
                idx = trickManager.SafestLegalCardIndex(this);

            if (idx >= 0) d[0] = idx;
        }
        // d[1], d[2] (통신/예비): Clear로 0 (사용 안 함)

        // 선택(드래프트) 페이즈: 풀 슬롯 0 가져가기 (베이스라인 — 학습 경로는 정책이 담당)
        if (trickManager != null && trickManager.currentPhase == GamePhase.TaskSelection
            && d.Length > 0)
            d[0] = 0;
    }

    // ── MCTS 진입점 (담당자만 호출) ────────────────────────────────
    //   활성화 우선순위: Inspector override > env parameter > 기본 false
    private bool IsMCTSAssigneeEnabled()
    {
        var mm = MissionManager.Instance;
        if (mm != null && mm.overrideMctsAssignee) return true;

        var academy = Unity.MLAgents.Academy.Instance;
        if (academy == null) return false;
        return academy.EnvironmentParameters.GetWithDefault("mcts_assignee", 0f) > 0.5f;
    }

    private int DecideWithMCTS(MissionManager mm)
    {
        int budget, dets;
        if (mm.overrideMctsAssignee)
        {
            budget = mm.overrideMctsBudget;
            dets   = mm.overrideMctsDeterminizations;
        }
        else
        {
            var academy = Unity.MLAgents.Academy.Instance;
            budget = Mathf.RoundToInt(academy.EnvironmentParameters.GetWithDefault("mcts_budget", 500f));
            dets   = Mathf.RoundToInt(academy.EnvironmentParameters.GetWithDefault("mcts_dets",   10f));
        }

        var ctx = mm.BuildMCTSContext(this);
        if (ctx == null || ctx.legalActionsInSelfHand == null
            || ctx.legalActionsInSelfHand.Count == 0)
            return -1;

        return MCTSSearch.ChooseCard(ctx, budget, dets);
    }

    // ---------------------------------------------------------------
    // 액션 마스킹
    //   Branch 0: 카드 선택        (size 10)
    //   Branch 1: 통신 토큰        (size 2 — 0=skip, 1=use)
    //   Branch 2: 예비(미사용)      (size 4 — 전부 마스킹, 조난신호는 트릭 전 단계에서 별도 처리)
    // ---------------------------------------------------------------
    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // === 선택(드래프트) 페이즈 마스킹 ===
        //   Branch 0 = 풀 슬롯(가져갈 task), Branch 1 = 0:가져가기 / 1:패스
        if (trickManager != null && trickManager.currentPhase == GamePhase.TaskSelection)
        {
            var msel = MissionManager.Instance;
            int pool = msel != null ? msel.PoolCount : 0;
            for (int i = 0; i < 10; i++)
                actionMask.SetActionEnabled(0, i, pool == 0 ? i == 0 : i < pool);
            actionMask.SetActionEnabled(1, 1, msel != null && msel.CanCurrentPickerPass());
            actionMask.SetActionEnabled(2, 1, false);
            actionMask.SetActionEnabled(2, 2, false);
            actionMask.SetActionEnabled(2, 3, false);
            return;
        }

        // 안전망: 손패 0장이면 마스킹 스킵.
        //   에피소드 종료 직후 재시작 대기 중 MLAgents SendInfo 사이클이 한 번 더 돌면서
        //   이 함수를 호출할 수 있는데, 모든 액션 마스킹은 "All actions masked" 예외 유발.
        if (hand.Count == 0) return;

        // === Branch 0: 카드 ===
        // 손패 범위 밖 인덱스 마스킹
        for (int i = hand.Count; i < 10; i++)
            actionMask.SetActionEnabled(0, i, false);

        // follow-suit 위반 카드 마스킹 (유효 카드가 1장 이상 있을 때만)
        if (trickManager != null)
        {
            bool hasValidCard = hand.Exists(c => trickManager.IsValidPlay(this, c));
            if (hasValidCard)
                for (int i = 0; i < hand.Count; i++)
                    if (!trickManager.IsValidPlay(this, hand[i]))
                        actionMask.SetActionEnabled(0, i, false);
        }

        // === Branch 1: 통신 토큰 ===
        //   사용 가능 조건 (모두 만족해야 함):
        //     1) CommunicationManager 존재
        //     2) 트릭 사이 (IsBetweenTricks) — 룰북상 트릭 진행 중엔 통신 불가
        //     3) 아직 사용하지 않음
        //     4) 손에 비-로켓 카드 1장 이상 (공개할 카드가 있어야 함)
        bool canComm = commManager != null
                       && trickManager != null
                       && trickManager.IsBetweenTricks
                       && !commManager.HasUsedCommToken(this)
                       && hand.Exists(c => c.suit != Card.Suit.Rocket);
        if (!canComm)
            actionMask.SetActionEnabled(1, 1, false);

        // === Branch 2: 미사용 (조난신호는 트릭 전 단계에서 별도 처리) ===
        actionMask.SetActionEnabled(2, 1, false);
        actionMask.SetActionEnabled(2, 2, false);
        actionMask.SetActionEnabled(2, 3, false);
    }

    // ---------------------------------------------------------------
    // AI 행동 처리
    //   Branch 0: 카드 인덱스
    //   Branch 1: 통신 토큰 사용 (0/1)
    //   Branch 2: 미사용 (0으로 고정)
    // ---------------------------------------------------------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (isHumanPlayer) return;
        var d = actions.DiscreteActions;

        // === 선택(드래프트) 페이즈 ===
        if (trickManager != null && trickManager.currentPhase == GamePhase.TaskSelection)
        {
            var msel = MissionManager.Instance;
            if (msel != null && msel.GetCurrentPickingPlayer() == this)
            {
                int poolSlot  = d.Length > 0 ? d[0] : 0;
                bool wantPass = d.Length > 1 && d[1] == 1;
                msel.AgentSelectTask(this, poolSlot, wantPass);
            }
            return;
        }

        // === 플레이(트릭) 페이즈 ===
        if (!isMyTurn) return;
        if (hand.Count == 0) { isMyTurn = false; return; }

        int cardIndex  = d[0];
        int commAction = d.Length > 1 ? d[1] : 0;

        if (commAction == 1 && commManager != null)
            commManager.UseCommToken(this);

        // 범위 보정
        if (cardIndex < 0 || cardIndex >= hand.Count)
            cardIndex = 0;

        // [rule_based] override를 먼저 적용 — rule-based 함수는 항상 valid 카드를 반환
        if (MissionManager.Instance != null)
        {
            int overrideIdx = MissionManager.Instance.RuleBasedHelperCardIndex(this);
            if (overrideIdx >= 0) cardIndex = overrideIdx;
        }

        // follow-suit 최종 검증 — 여기까지 왔는데도 위반이면 진짜 마스킹 누락
        if (!trickManager.IsValidPlay(this, hand[cardIndex]))
        {
            int validIdx = hand.FindIndex(c => trickManager.IsValidPlay(this, c));
            if (validIdx >= 0) cardIndex = validIdx;
            Debug.LogWarning($"[{gameObject.name}] follow-suit 위반 (최종 보정), 카드[{cardIndex}]로 대체");
        }

        // [Phase1] 협력 계측/보상: 제출 직전 평가 (hand 온전한 상태)
        if (MissionManager.Instance != null)
        {
            var (couldWin, hadSafe) = trickManager.EvaluatePlay(this, hand[cardIndex]);
            MissionManager.Instance.RecordHelperPlay(this, couldWin, hadSafe);
        }

        isMyTurn = false;
        PlayCard(cardIndex);
    }

    // ---------------------------------------------------------------
    // 카드 제출 — 중앙 테이블에 카드 오브젝트 생성
    // ---------------------------------------------------------------
    private void PlayCard(int index)
    {
        Card playedCard = hand[index];
        hand.RemoveAt(index);

        // 중앙 테이블에 카드 프리팹 스폰 (인간/AI 공통)
        if (cardPrefab != null)
        {
            var center = GameManager.Instance.centerBoard;
            GameObject cardObj = Instantiate(cardPrefab, center);
            if (cardObj.TryGetComponent<CardDisplay>(out var display))
                display.Setup(playedCard);
            cardObj.transform.localPosition = new Vector3(
                Random.Range(-1.5f, 1.5f),
                Random.Range(-1.5f, 1.5f), 0f);

            // 플레이어 이름 레이블 (카드 아래)
            var labelGO = new GameObject("PlayerLabel");
            labelGO.transform.SetParent(cardObj.transform, false);
            labelGO.transform.localPosition = new Vector3(0f, -0.65f, -0.05f);
            var tm = labelGO.AddComponent<TextMesh>();
            tm.text          = gameObject.name;
            tm.fontSize      = 24;
            tm.characterSize = 0.06f;
            tm.color         = Color.white;
            tm.anchor        = TextAnchor.UpperCenter;
            tm.alignment     = TextAlignment.Center;
            tm.fontStyle     = FontStyle.Bold;
        }

        Debug.Log($"[{gameObject.name}] {playedCard} 제출");
        trickManager.OnCardPlayed(this, playedCard);
    }

    // ---------------------------------------------------------------
    // 관찰 (Observation) — 총 257개 (벡터 크기 불변)
    //   40 (손패) + 40 (테이블 / 선택 페이즈엔 task 풀 슬롯) + 5 (리드)
    //   + 162 (팀 태스크 4명분) + 4 (손패 수) + 4 (현재 트릭 승리 수)
    //   + 2 (플래그: [0]선택페이즈인가 / [1]선택중=패스가능·플레이중=내task타깃보유)
    //
    //   선택(드래프트) 페이즈에는 비어있는 '테이블 40칸'을 풀 슬롯 인코딩으로 재사용:
    //     슬롯 j(0..9): [targetSuit/4, targetValue/9, 내가보유, 점유] (4칸 × 10)
    //   → 액션 Branch[0]=j 가 이 슬롯 j에 대응. 벡터/액션 공간 변경 없음(씬 편집 불필요).
    // ---------------------------------------------------------------
    public const int ObservationSize = 257;

    public override void CollectObservations(VectorSensor sensor)
    {
        if (GameManager.Instance == null)
        {
            sensor.AddObservation(new float[ObservationSize]);
            return;
        }

        var tm = trickManager;

        // 1. 내 손패 (40)
        float[] handObs = new float[40];
        foreach (Card c in hand) handObs[GetCardIndex(c)] = 1f;
        foreach (float f in handObs) sensor.AddObservation(f);

        bool selecting = tm != null && tm.currentPhase == GamePhase.TaskSelection;
        var mmObs = MissionManager.Instance;

        // 2. 바닥 카드 (40) — 선택 페이즈엔 동일 40칸에 task 풀 슬롯 인코딩
        float[] tableObs = new float[40];
        if (selecting && mmObs != null)
        {
            var pool = mmObs.taskPool;
            for (int j = 0; j < 10 && j < pool.Count; j++)
            {
                Card c = pool[j].targetCard;
                int b = j * 4;
                if (c != null)
                {
                    tableObs[b + 0] = (int)c.suit / 4f;
                    tableObs[b + 1] = c.value / 9f;
                    tableObs[b + 2] = hand.Contains(c) ? 1f : 0f;
                }
                tableObs[b + 3] = 1f;   // 슬롯 점유
            }
        }
        else if (tm != null)
        {
            foreach (Card c in tm.cardsOnTable) tableObs[GetCardIndex(c)] = 1f;
        }
        foreach (float f in tableObs) sensor.AddObservation(f);

        // 3. 선 색상 (5)
        float[] leadObs = new float[5];
        if (tm != null && tm.cardsOnTable.Count > 0)
            leadObs[(int)tm.leadSuit] = 1f;
        foreach (float f in leadObs) sensor.AddObservation(f);

        // 4. 팀 태스크 (162) — viewer 기준 시계방향 4명분
        float[] taskObs = MissionManager.Instance != null
            ? MissionManager.Instance.GetTaskObservationFor(this)
            : new float[162];
        foreach (float f in taskObs) sensor.AddObservation(f);

        // 5. 손패 수 (4) — viewer 기준 시계방향
        var players = GameManager.Instance.players;
        int selfIdx = players.IndexOf(this);
        if (selfIdx < 0) selfIdx = 0;
        for (int i = 0; i < players.Count; i++)
        {
            int idx = (selfIdx + i) % players.Count;
            sensor.AddObservation(players[idx].hand.Count / 10f);
        }

        // 6. 현재 트릭 승리 수 (4) — viewer 기준 시계방향
        //   count 기반 태스크(WinAtLeast 등)의 진척을 에이전트가 인지하도록.
        var mm = MissionManager.Instance;
        for (int i = 0; i < players.Count; i++)
        {
            int idx = (selfIdx + i) % players.Count;
            int wins = mm != null ? mm.GetTrickWinCount(players[idx]) : 0;
            sensor.AddObservation(wins / 10f);
        }

        // 7. 페이즈/역할 플래그 (2)
        //   [0] 선택 페이즈인가 (블록2 해석: 1=풀 슬롯 / 0=바닥 카드)
        //   [1] 선택 중이면 '패스 가능', 플레이 중이면 '내 task 타깃 보유'
        sensor.AddObservation(selecting ? 1f : 0f);
        if (selecting)
            sensor.AddObservation(mm != null && mm.CanCurrentPickerPass() ? 1f : 0f);
        else
            sensor.AddObservation(mm != null && mm.HoldsOwnTaskTarget(this) ? 1f : 0f);
    }

    // ---------------------------------------------------------------
    // 카드 → 0~39 인덱스
    // ---------------------------------------------------------------
    public int GetCardIndex(Card card)
    {
        if (card.value <= 0) return 0;
        if (card.suit == Card.Suit.Rocket)
            return Mathf.Clamp(36 + (card.value - 1), 36, 39);
        return Mathf.Clamp((int)card.suit * 9 + (card.value - 1), 0, 35);
    }
}
