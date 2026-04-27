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

    private int pendingCommAction  = 0;
    private int pendingSonarTarget = 0;

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
    //   Space    : 통신 토큰 예약
    //   Z/X/C    : 소나 토큰 대상 예약
    // ---------------------------------------------------------------
    void Update()
    {
        if (!isMyTurn || !isHumanPlayer) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        if (kb.spaceKey.wasPressedThisFrame) pendingCommAction  = 1;
        if (kb.zKey.wasPressedThisFrame)     pendingSonarTarget = 1;
        if (kb.xKey.wasPressedThisFrame)     pendingSonarTarget = 2;
        if (kb.cKey.wasPressedThisFrame)     pendingSonarTarget = 3;

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

        // 토큰 처리 (Space/Z/X/C로 예약된 경우)
        if (pendingCommAction == 1)  { commManager.UseCommToken(this);          pendingCommAction  = 0; }
        if (pendingSonarTarget > 0)  { commManager.UseSonarToken(this, pendingSonarTarget); pendingSonarTarget = 0; }

        isMyTurn = false;
        PlayCard(index);
    }

    // Heuristic은 AI 전용 (인간은 HumanDirectPlay를 사용)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // 인간 플레이어는 이 경로를 사용하지 않음
        var d = actionsOut.DiscreteActions;
        d[0] = 0;
    }

    // ---------------------------------------------------------------
    // AI 행동 처리 (Branch[0]=카드, Branch[1]=통신, Branch[2]=소나)
    // ---------------------------------------------------------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!isMyTurn || isHumanPlayer) return;
        if (hand.Count == 0) { isMyTurn = false; return; }

        var d = actions.DiscreteActions;
        int cardIndex   = d[0];
        int useComm     = d.Length > 1 ? d[1] : 0;
        int sonarTarget = d.Length > 2 ? d[2] : 0;

        if (useComm == 1 && !commManager.UseCommToken(this))
            AddReward(-0.1f);

        if (sonarTarget > 0 && !commManager.UseSonarToken(this, sonarTarget))
            AddReward(-0.1f);

        if (cardIndex < 0 || cardIndex >= hand.Count)
        {
            AddReward(-1.0f);
            cardIndex = 0;
        }

        Card cardToPlay = hand[cardIndex];
        if (!trickManager.IsValidPlay(this, cardToPlay))
        {
            AddReward(-1.0f);
            int validIdx = hand.FindIndex(c => trickManager.IsValidPlay(this, c));
            if (validIdx >= 0) cardIndex = validIdx;
            Debug.Log($"[{gameObject.name}] 규칙 위반 → {(validIdx >= 0 ? $"카드[{validIdx}]로 대체" : "대체 불가")}");
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
    // 관찰 (Observation) — 총 219개
    // ---------------------------------------------------------------
    public override void CollectObservations(VectorSensor sensor)
    {
        if (GameManager.Instance == null)
        {
            sensor.AddObservation(new float[219]);
            return;
        }

        var tm = trickManager;

        // 1. 내 손패 (40)
        float[] handObs = new float[40];
        foreach (Card c in hand) handObs[GetCardIndex(c)] = 1f;
        foreach (float f in handObs) sensor.AddObservation(f);

        // 2. 바닥 카드 (40)
        float[] tableObs = new float[40];
        if (tm != null)
            foreach (Card c in tm.cardsOnTable) tableObs[GetCardIndex(c)] = 1f;
        foreach (float f in tableObs) sensor.AddObservation(f);

        // 3. 선 색상 (5)
        float[] leadObs = new float[5];
        if (tm != null && tm.cardsOnTable.Count > 0)
            leadObs[(int)tm.leadSuit] = 1f;
        foreach (float f in leadObs) sensor.AddObservation(f);

        // 4. 내 태스크 (42)
        float[] taskObs = MissionManager.Instance != null
            ? MissionManager.Instance.GetTaskObservation(this)
            : new float[42];
        foreach (float f in taskObs) sensor.AddObservation(f);

        // 5. 손패 수 (4)
        foreach (var p in GameManager.Instance.players)
            sensor.AddObservation(p.hand.Count / 10f);

        // 6. 통신 토큰 (44)
        float[] commObs = commManager != null
            ? commManager.GetCommObservation()
            : new float[44];
        foreach (float f in commObs) sensor.AddObservation(f);

        // 7. 소나 토큰 (44)
        float[] sonarObs = commManager != null
            ? commManager.GetSonarObservation()
            : new float[44];
        foreach (float f in sonarObs) sensor.AddObservation(f);
    }

    // ---------------------------------------------------------------
    // 카드 → 0~39 인덱스
    // ---------------------------------------------------------------
    public int GetCardIndex(Card card)
    {
        if (card.value <= 0) return 0;
        if (card.suit == Card.Suit.Submarine)
            return Mathf.Clamp(36 + (card.value - 1), 36, 39);
        return Mathf.Clamp((int)card.suit * 9 + (card.value - 1), 0, 35);
    }
}
