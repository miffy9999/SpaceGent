using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators; // 행동(Action)을 위해 추가
using Unity.MLAgents.Sensors;   // 관찰(Observation)을 위해 추가

public class CrewAgent : Agent
{
    [Header("Agent Data")]
    public List<Card> hand = new List<Card>();
    public bool isMyTurn = false; // 💡 새로 추가: 내 턴인지 확인하는 변수

    [Header("Visual & Prefabs")]
    public GameObject cardPrefab;
    public Transform handTransform;
    public Transform centerBoard; // 💡 새로 추가: 카드를 낼 중앙 테이블 위치
    public TrickManager trickManager; // 💡 새로 추가: 심판 레퍼런스

    private List<GameObject> cardVisualObjects = new List<GameObject>();
    private int pendingAction = -1; // 💡 새로 추가: 키보드 입력을 임시로 저장할 곳

    void Start()
    {
        if (trickManager == null)
        {
            trickManager = FindAnyObjectByType<TrickManager>();
        }
    }

    // --- 기존 카드 분배 / 초기화 함수 ---
    public void ReceiveCard(Card newCard)
    {
        hand.Add(newCard);
        GameObject cardObj = Instantiate(cardPrefab, handTransform);

        CardDisplay display = cardObj.GetComponent<CardDisplay>();
        if (display != null) display.Setup(newCard);

        cardVisualObjects.Add(cardObj);
        RearrangeHand(); // 정렬 함수 분리
    }

    public void ClearHand()
    {
        hand.Clear();
        foreach (var obj in cardVisualObjects) Destroy(obj);
        cardVisualObjects.Clear();
    }

    // --- 💡 새로 추가되는 행동(Action) 관련 파트 ---

    // 1. 사람이 키보드로 직접 조종할 때 (Heuristic 모드)
    // 💡 새로 추가: 매 프레임마다 키보드 입력을 감지해서 저장해 둠 (씹힘 방지!)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = pendingAction;
        pendingAction = -1;
    }

    void Update()
    {
        if (!isMyTurn) return;

        // 1~9번째 카드 (키보드 1~9)
        if (Input.GetKeyDown(KeyCode.Alpha1)) pendingAction = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) pendingAction = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) pendingAction = 2;
        else if (Input.GetKeyDown(KeyCode.Alpha4)) pendingAction = 3;
        else if (Input.GetKeyDown(KeyCode.Alpha5)) pendingAction = 4;
        else if (Input.GetKeyDown(KeyCode.Alpha6)) pendingAction = 5;
        else if (Input.GetKeyDown(KeyCode.Alpha7)) pendingAction = 6;
        else if (Input.GetKeyDown(KeyCode.Alpha8)) pendingAction = 7;
        else if (Input.GetKeyDown(KeyCode.Alpha9)) pendingAction = 8;
        // 10번째 카드 (키보드 0)
        else if (Input.GetKeyDown(KeyCode.Alpha0)) pendingAction = 9;
        // 11~15번째 카드 (키보드 Q, W, E, R, T) - 3인플 대비용 넉넉한 공간
        else if (Input.GetKeyDown(KeyCode.Q)) pendingAction = 10;
        else if (Input.GetKeyDown(KeyCode.W)) pendingAction = 11;
        else if (Input.GetKeyDown(KeyCode.E)) pendingAction = 12;
        else if (Input.GetKeyDown(KeyCode.R)) pendingAction = 13;
        else if (Input.GetKeyDown(KeyCode.T)) pendingAction = 14;

        if (pendingAction != -1)
        {
            RequestDecision();
        }
    }

    // 💡 보상과 벌점이 추가된 AI의 행동 로직
    public override void OnActionReceived(ActionBuffers actions)
    {

        // 💡 0. 절대 방어막: 내 손패가 아예 없다면(0장) 아무 행동도 하지 않고 함수를 종료합니다.
        if (hand.Count == 0) return;

        int cardIndex = actions.DiscreteActions[0];

        // 1. 에러/벌점 처리: 없는 카드를 고른 경우
        if (cardIndex >= hand.Count || cardIndex < 0)
        {
            AddReward(-1.0f); // 꿀밤! 
            cardIndex = 0; // 강제로 0번 카드를 내도록 유도
        }

        // 2. 규칙 위반 처리
        Card cardToPlay = hand[cardIndex];

        bool isValid = trickManager.IsValidPlay(this, cardToPlay);

        if (!isValid)
        {
            AddReward(-1.0f); // 선 색상을 안 따랐으므로 꿀밤!
            Debug.Log($"🚨 {gameObject.name} 규칙 위반! (벌점 -1.0)");
        }
        else
        {
            AddReward(0.1f); // 규칙에 맞게 잘 냈으므로 소소한 칭찬 (당근)
        }

        // 3. 실제로 카드를 내고 턴 넘기기
        PlayCard(cardIndex);
        isMyTurn = false;
    }

    // 3. 카드를 내는 실제 물리적/데이터적 로직
    private void PlayCard(int index)
    {
        // 1) 데이터 리스트에서 빼기
        Card playedCard = hand[index];
        hand.RemoveAt(index);

        // 2) 그래픽(오브젝트)을 중앙 테이블(Center Board)로 옮기기
        GameObject cardObj = cardVisualObjects[index];
        cardObj.transform.SetParent(centerBoard);

        // 카드가 중앙에 예쁘게 모이도록 약간 무작위 위치로 흩뿌리기
        float randomX = Random.Range(-1.5f, 1.5f);
        float randomY = Random.Range(-1.5f, 1.5f);
        cardObj.transform.localPosition = new Vector3(randomX, randomY, 0);

        // 3) 손패 시각적 리스트에서 제거하고 다시 정렬
        cardVisualObjects.RemoveAt(index);
        RearrangeHand();

        Debug.Log($"[{gameObject.name}]가 {playedCard.suit} {playedCard.value} 카드를 냈습니다!");
        isMyTurn = false; // 내 턴 종료

        // 💡 새로 추가: 심판(TrickManager)에게 내가 카드를 냈다고 알림
        if (trickManager != null)
        {
            // 아직 에이전트에게 고유 번호(Index)가 없으니, 임시로 0, 1, 2 중 하나를 넘기는 구조로 갑니다.
            // 일단 TrickManager의 함수를 호출하도록 둡니다.
            trickManager.OnCardPlayed(this, playedCard);
        }
    }

    // 카드의 종류를 0~39번 인덱스로 변환해 주는 헬퍼 함수
    // 💡 방어 코드가 추가된 변환기
    public int GetCardIndex(Card card)
    {
        // 1. 안전장치: 카드의 숫자가 0 이하인 '더미 카드'가 들어오면 무조건 0번 방으로 처리 (에러 방지)
        if (card.value <= 0) return 0;

        // 2. 잠수함 카드 처리
        if (card.suit == Card.Suit.Submarine)
        {
            // 잠수함 1~4번은 36~39번 방을 씀. 혹시 4를 넘어가도 39번 방으로 고정(Clamp)
            return Mathf.Clamp(36 + (card.value - 1), 36, 39);
        }

        // 3. 일반 색상 카드 처리
        int index = ((int)card.suit * 9) + (card.value - 1);

        // 계산된 방 번호가 0~35를 벗어나지 않도록 고정
        return Mathf.Clamp(index, 0, 35);
    }

    // 손패 빈자리 메꾸며 다시 정렬하는 함수
    private void RearrangeHand()
    {
        for (int i = 0; i < cardVisualObjects.Count; i++)
        {
            float xOffset = i * 1.5f;
            cardVisualObjects[i].transform.localPosition = new Vector3(xOffset, 0, 0);
        }
    }

    // 관찰(Observation) - 에러 방지용 (임시)
    // 💡 완벽하게 번역된 AI의 눈 (총 85개의 숫자 정보)
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 내 손패 정보 (40칸)
        float[] handObs = new float[40];
        foreach (Card c in hand)
        {
            handObs[GetCardIndex(c)] = 1f; // 들고 있는 카드의 방에만 불을 켬
        }
        foreach (float f in handObs) sensor.AddObservation(f); // (누적 40개)

        // 2. 바닥에 깔린 카드 정보 (40칸)
        float[] tableObs = new float[40];
        if (trickManager != null)
        {
            foreach (Card c in trickManager.cardsOnTable)
            {
                tableObs[GetCardIndex(c)] = 1f; // 바닥에 있는 카드의 방에 불을 켬
            }
        }
        foreach (float f in tableObs) sensor.AddObservation(f); // (누적 80개)

        // 3. 현재 선 색상(Lead Suit) 정보 (5칸)
        float[] leadSuitObs = new float[5];
        if (trickManager != null && trickManager.cardsOnTable.Count > 0)
        {
            leadSuitObs[(int)trickManager.leadSuit] = 1f; // 선 색상 방에만 불을 켬
        }
        foreach (float f in leadSuitObs) sensor.AddObservation(f); // (누적 85개)
    }


}