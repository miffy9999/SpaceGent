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

    [Header("Visual & Prefabs")]
    public GameObject cardPrefab;
    public Transform handTransform;

    private List<GameObject> cardVisualObjects = new List<GameObject>();
    private int pendingCardAction   = -1;
    private int pendingCommAction   =  0; // 0=통신 안 함, 1=통신 토큰 사용

    private TrickManager trickManager           => GameManager.Instance.trickManager;
    private CommunicationManager commManager    => GameManager.Instance.communicationManager;

    // ---------------------------------------------------------------
    // 카드 분배 / 초기화
    // ---------------------------------------------------------------
    public void ReceiveCard(Card newCard)
    {
        hand.Add(newCard);
        GameObject cardObj = Instantiate(cardPrefab, handTransform);

        CardDisplay display = cardObj.GetComponent<CardDisplay>();
        if (display != null) display.Setup(newCard);

        cardVisualObjects.Add(cardObj);
        RearrangeHand();
    }

    public void ClearHand()
    {
        hand.Clear();
        foreach (var obj in cardVisualObjects) Destroy(obj);
        cardVisualObjects.Clear();
    }

    // ---------------------------------------------------------------
    // 키보드 입력 (인간 플레이어 / Heuristic 모드)
    //   숫자 1~0 : 카드 선택
    //   Space    : 통신 토큰 사용
    // ---------------------------------------------------------------
    void Update()
    {
        if (!isMyTurn) return;

        if      (Input.GetKeyDown(KeyCode.Alpha1)) pendingCardAction = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) pendingCardAction = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) pendingCardAction = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha4)) pendingCardAction = 3;
        else if (Input.GetKeyDown(KeyCode.Alpha5)) pendingCardAction = 4;
        else if (Input.GetKeyDown(KeyCode.Alpha6)) pendingCardAction = 5;
        else if (Input.GetKeyDown(KeyCode.Alpha7)) pendingCardAction = 6;
        else if (Input.GetKeyDown(KeyCode.Alpha8)) pendingCardAction = 7;
        else if (Input.GetKeyDown(KeyCode.Alpha9)) pendingCardAction = 8;
        else if (Input.GetKeyDown(KeyCode.Alpha0)) pendingCardAction = 9;

        if (Input.GetKeyDown(KeyCode.Space))
            pendingCommAction = 1;

        if (pendingCardAction != -1)
            RequestDecision();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = pendingCardAction >= 0 ? pendingCardAction : 0;
        if (discrete.Length > 1)
            discrete[1] = pendingCommAction;
        pendingCardAction = -1;
        pendingCommAction = 0;
    }

    // ---------------------------------------------------------------
    // AI 행동 처리
    // Branch[0] : 낼 카드 인덱스 (0~9)
    // Branch[1] : 통신 토큰 사용 여부 (0=안 함, 1=사용)
    // ---------------------------------------------------------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!isMyTurn) return;
        if (hand.Count == 0) return;

        int cardIndex = actions.DiscreteActions[0];
        int useComm   = actions.DiscreteActions.Length > 1 ? actions.DiscreteActions[1] : 0;

        // 통신 토큰 사용 (카드를 내기 전에 처리)
        if (useComm == 1)
        {
            bool success = commManager.UseToken(this);
            if (!success)
                AddReward(-0.1f); // 이미 사용했거나 공개할 카드 없음
        }

        // 범위 초과 행동 → 벌점 후 강제 0번 카드
        if (cardIndex < 0 || cardIndex >= hand.Count)
        {
            AddReward(-1.0f);
            cardIndex = 0;
        }

        Card cardToPlay = hand[cardIndex];

        if (!trickManager.IsValidPlay(this, cardToPlay))
        {
            AddReward(-1.0f);
            Debug.Log($"[{gameObject.name}] 규칙 위반 (벌점 -1.0)");
        }

        PlayCard(cardIndex);
        isMyTurn = false;
    }

    // ---------------------------------------------------------------
    // 카드를 내는 실제 로직
    // ---------------------------------------------------------------
    private void PlayCard(int index)
    {
        Card playedCard = hand[index];
        hand.RemoveAt(index);

        GameObject cardObj = cardVisualObjects[index];
        cardObj.transform.SetParent(GameManager.Instance.centerBoard);
        cardObj.transform.localPosition = new Vector3(
            Random.Range(-1.5f, 1.5f),
            Random.Range(-1.5f, 1.5f),
            0f
        );

        cardVisualObjects.RemoveAt(index);
        RearrangeHand();

        Debug.Log($"[{gameObject.name}] {playedCard.suit} {playedCard.value} 제출");
        trickManager.OnCardPlayed(this, playedCard);
    }

    // ---------------------------------------------------------------
    // 관찰 (Observation) — 총 171개
    //   [0~39]    내 손패 원-핫           (40)
    //   [40~79]   바닥 카드 원-핫          (40)
    //   [80~84]   선 색상 원-핫            (5)
    //   [85~126]  내 태스크 상태           (42)
    //   [127~130] 플레이어별 남은 손패 수  (4)
    //   [131~174] 통신 토큰 상태           (44)
    // ---------------------------------------------------------------
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 내 손패 (40)
        float[] handObs = new float[40];
        foreach (Card c in hand)
            handObs[GetCardIndex(c)] = 1f;
        foreach (float f in handObs) sensor.AddObservation(f);

        // 2. 바닥 카드 (40)
        float[] tableObs = new float[40];
        foreach (Card c in trickManager.cardsOnTable)
            tableObs[GetCardIndex(c)] = 1f;
        foreach (float f in tableObs) sensor.AddObservation(f);

        // 3. 선 색상 (5)
        float[] leadObs = new float[5];
        if (trickManager.cardsOnTable.Count > 0)
            leadObs[(int)trickManager.leadSuit] = 1f;
        foreach (float f in leadObs) sensor.AddObservation(f);

        // 4. 내 태스크 정보 (42)
        float[] taskObs = MissionManager.Instance.GetTaskObservation(this);
        foreach (float f in taskObs) sensor.AddObservation(f);

        // 5. 플레이어별 남은 손패 수 (4, 정규화)
        foreach (var p in GameManager.Instance.players)
            sensor.AddObservation(p.hand.Count / 10f);

        // 6. 통신 토큰 상태 (44) — 사용 여부 4 + 공개 카드 원-핫 40
        float[] commObs = commManager.GetObservation();
        foreach (float f in commObs) sensor.AddObservation(f);
    }

    // ---------------------------------------------------------------
    // 카드 → 0~39 인덱스 변환
    // ---------------------------------------------------------------
    public int GetCardIndex(Card card)
    {
        if (card.value <= 0) return 0;

        if (card.suit == Card.Suit.Submarine)
            return Mathf.Clamp(36 + (card.value - 1), 36, 39);

        return Mathf.Clamp((int)card.suit * 9 + (card.value - 1), 0, 35);
    }

    // ---------------------------------------------------------------
    // 손패 시각 정렬
    // ---------------------------------------------------------------
    private void RearrangeHand()
    {
        for (int i = 0; i < cardVisualObjects.Count; i++)
        {
            cardVisualObjects[i].transform.localPosition = new Vector3(i * 1.5f, 0, 0);
        }
    }
}
