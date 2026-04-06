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
    private int pendingCardAction  = -1;
    private int pendingCommAction  =  0; // 0=안 함, 1=통신 토큰
    private int pendingSonarTarget =  0; // 0=안 함, 1~3=상대 플레이어 상대 인덱스

    private TrickManager       trickManager => GameManager.Instance.trickManager;
    private CommunicationManager commManager => GameManager.Instance.communicationManager;

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
    //   숫자 1~0  : 카드 선택
    //   Space     : 통신 토큰 사용
    //   Z/X/C     : 소나 토큰 (왼쪽/맞은편/오른쪽 플레이어)
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

        if (Input.GetKeyDown(KeyCode.Space)) pendingCommAction  = 1;
        if (Input.GetKeyDown(KeyCode.Z))     pendingSonarTarget = 1;
        if (Input.GetKeyDown(KeyCode.X))     pendingSonarTarget = 2;
        if (Input.GetKeyDown(KeyCode.C))     pendingSonarTarget = 3;

        if (pendingCardAction != -1)
            RequestDecision();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        d[0] = pendingCardAction >= 0 ? pendingCardAction : 0;
        if (d.Length > 1) d[1] = pendingCommAction;
        if (d.Length > 2) d[2] = pendingSonarTarget;
        pendingCardAction  = -1;
        pendingCommAction  =  0;
        pendingSonarTarget =  0;
    }

    // ---------------------------------------------------------------
    // AI 행동 처리
    //   Branch[0] size 10 : 낼 카드 인덱스
    //   Branch[1] size  2 : 통신 토큰 (0=안 함, 1=사용)
    //   Branch[2] size  4 : 소나 토큰 (0=안 함, 1~3=상대 플레이어)
    // ---------------------------------------------------------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!isMyTurn) return;
        if (hand.Count == 0) { isMyTurn = false; return; }

        var d = actions.DiscreteActions;
        int cardIndex    = d[0];
        int useComm      = d.Length > 1 ? d[1] : 0;
        int sonarTarget  = d.Length > 2 ? d[2] : 0;

        // 통신 토큰
        if (useComm == 1 && !commManager.UseCommToken(this))
            AddReward(-0.1f);

        // 소나 토큰
        if (sonarTarget > 0 && !commManager.UseSonarToken(this, sonarTarget))
            AddReward(-0.1f);

        // 카드 범위 초과
        if (cardIndex < 0 || cardIndex >= hand.Count)
        {
            AddReward(-1.0f);
            cardIndex = 0;
        }

        Card cardToPlay = hand[cardIndex];
        if (!trickManager.IsValidPlay(this, cardToPlay))
        {
            AddReward(-1.0f);
            Debug.Log($"[{gameObject.name}] 규칙 위반");
        }

        PlayCard(cardIndex);
        isMyTurn = false;
    }

    // ---------------------------------------------------------------
    // 카드 제출
    // ---------------------------------------------------------------
    private void PlayCard(int index)
    {
        Card playedCard = hand[index];
        hand.RemoveAt(index);

        GameObject cardObj = cardVisualObjects[index];
        cardObj.transform.SetParent(GameManager.Instance.centerBoard);
        cardObj.transform.localPosition = new Vector3(
            Random.Range(-1.5f, 1.5f),
            Random.Range(-1.5f, 1.5f), 0f);

        cardVisualObjects.RemoveAt(index);
        RearrangeHand();

        Debug.Log($"[{gameObject.name}] {playedCard} 제출");
        trickManager.OnCardPlayed(this, playedCard);
    }

    // ---------------------------------------------------------------
    // 관찰 (Observation) — 총 219개
    //   [0~39]    내 손패 원-핫           (40)
    //   [40~79]   바닥 카드 원-핫          (40)
    //   [80~84]   선 색상 원-핫            (5)
    //   [85~126]  내 태스크 상태           (42)
    //   [127~130] 플레이어별 남은 손패 수  (4)
    //   [131~174] 통신 토큰 상태           (44)
    //   [175~218] 소나 토큰 상태           (44)
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

    private void RearrangeHand()
    {
        for (int i = 0; i < cardVisualObjects.Count; i++)
            cardVisualObjects[i].transform.localPosition = new Vector3(i * 1.5f, 0, 0);
    }
}
