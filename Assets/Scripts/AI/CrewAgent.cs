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

    // centerBoard는 GameManager.Instance.centerBoard를 사용
    // (인스펙터 할당 불필요)

    private List<GameObject> cardVisualObjects = new List<GameObject>();
    private int pendingAction = -1;

    private TrickManager trickManager => GameManager.Instance.trickManager;

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
    // 4인 기준: 1인당 최대 10장 → 키 1~9, 0으로 충분히 커버
    // ---------------------------------------------------------------
    void Update()
    {
        if (!isMyTurn) return;

        if      (Input.GetKeyDown(KeyCode.Alpha1)) pendingAction = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) pendingAction = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) pendingAction = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha4)) pendingAction = 3;
        else if (Input.GetKeyDown(KeyCode.Alpha5)) pendingAction = 4;
        else if (Input.GetKeyDown(KeyCode.Alpha6)) pendingAction = 5;
        else if (Input.GetKeyDown(KeyCode.Alpha7)) pendingAction = 6;
        else if (Input.GetKeyDown(KeyCode.Alpha8)) pendingAction = 7;
        else if (Input.GetKeyDown(KeyCode.Alpha9)) pendingAction = 8;
        else if (Input.GetKeyDown(KeyCode.Alpha0)) pendingAction = 9;

        if (pendingAction != -1)
        {
            RequestDecision();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = pendingAction >= 0 ? pendingAction : 0;
        pendingAction = -1;
    }

    // ---------------------------------------------------------------
    // AI 행동 처리
    // ---------------------------------------------------------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!isMyTurn) return;
        if (hand.Count == 0) return;

        int cardIndex = actions.DiscreteActions[0];

        // 범위 초과 → 벌점 후 강제로 0번 카드
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
        else
        {
            AddReward(0.1f);
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

        // 카드 오브젝트를 공용 centerBoard로 이동
        GameObject cardObj = cardVisualObjects[index];
        cardObj.transform.SetParent(GameManager.Instance.centerBoard);
        cardObj.transform.localPosition = new Vector3(
            Random.Range(-1.5f, 1.5f),
            Random.Range(-1.5f, 1.5f),
            0f
        );

        cardVisualObjects.RemoveAt(index);
        RearrangeHand();

        Debug.Log($"[{gameObject.name}] {playedCard.suit} {playedCard.value} 카드 제출");

        trickManager.OnCardPlayed(this, playedCard);
    }

    // ---------------------------------------------------------------
    // 관찰 (Observation) — 총 127개
    // ---------------------------------------------------------------
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 내 손패 (40칸)
        float[] handObs = new float[40];
        foreach (Card c in hand)
            handObs[GetCardIndex(c)] = 1f;
        foreach (float f in handObs) sensor.AddObservation(f);

        // 2. 바닥 카드 (40칸)
        float[] tableObs = new float[40];
        foreach (Card c in trickManager.cardsOnTable)
            tableObs[GetCardIndex(c)] = 1f;
        foreach (float f in tableObs) sensor.AddObservation(f);

        // 3. 선 색상 (5칸)
        float[] leadObs = new float[5];
        if (trickManager.cardsOnTable.Count > 0)
            leadObs[(int)trickManager.leadSuit] = 1f;
        foreach (float f in leadObs) sensor.AddObservation(f);

        // 4. 내 태스크 정보 (42칸) — 목표 카드 원-핫 40 + 완료 1 + 실패 1
        float[] taskObs = MissionManager.Instance.GetTaskObservation(this);
        foreach (float f in taskObs) sensor.AddObservation(f);

        // 5. 각 플레이어 남은 손패 장수 (4칸, 정규화)
        var players = GameManager.Instance.players;
        foreach (var p in players)
            sensor.AddObservation(p.hand.Count / 10f);
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
