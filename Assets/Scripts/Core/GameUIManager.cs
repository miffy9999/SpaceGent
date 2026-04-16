using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

/// <summary>
/// 게임 HUD를 관리한다. 매 프레임 게임 상태를 읽어 UI를 갱신한다.
///
/// ── 씬 설정 방법 ──────────────────────────────────────────────────
/// 1. Hierarchy에서 Canvas (Screen Space - Overlay) 생성
/// 2. 아래 헤더의 각 필드에 UI 오브젝트를 드래그해 연결
/// 3. GameManager 인스펙터의 uiManager 슬롯에 이 컴포넌트 할당
/// ─────────────────────────────────────────────────────────────────
/// </summary>
public class GameUIManager : MonoBehaviour
{
    // ── 트릭 정보 ─────────────────────────────────────────────────
    [Header("트릭 정보")]
    public TMP_Text trickCountText;   // "Trick 3 / 10"
    public TMP_Text leadSuitText;     // "선 색상: Blue"
    public TMP_Text turnText;         // "▶ Player1의 차례"

    // ── 미션 정보 ─────────────────────────────────────────────────
    [Header("미션 정보")]
    public Image missionImage;      // 미션 스프라이트
    public TMP_Text missionIdText;    // "Mission 1-2-3"

    // ── 플레이어 태스크 패널 (인간 플레이어 기준) ───────────────────
    [Header("태스크 (인간 플레이어)")]
    public Transform taskListParent;  // 태스크 항목이 들어갈 부모
    public GameObject taskItemPrefab; // TaskItem 프리팹 (TMP_Text 1개 포함)

    // ── 토큰 상태 ─────────────────────────────────────────────────
    [Header("토큰 아이콘 (플레이어 4명 순서대로)")]
    public Image[] commTokenIcons  = new Image[4];
    public Image[] sonarTokenIcons = new Image[4];

    [Header("토큰 색상")]
    public Color tokenActiveColor = Color.white;
    public Color tokenUsedColor   = new Color(0.3f, 0.3f, 0.3f, 0.5f);

    // ── 통신 토큰 공개 표시 ────────────────────────────────────────
    [Header("통신 토큰 공개 표시 (플레이어 4명)")]
    public Image[]    commRevealCardImages    = new Image[4];    // 공개된 카드 이미지
    public TMP_Text[] commRevealPositionTexts = new TMP_Text[4]; // ▲ 최고값 / ▼ 최저값 / ● 유일
    public GameObject[] commRevealPanels      = new GameObject[4]; // 공개 패널 루트 (숨김/표시)

    // ── 소나 토큰 공개 표시 ────────────────────────────────────────
    [Header("소나 토큰 공개 표시 (플레이어 4명)")]
    public Image[]    sonarRevealCardImages = new Image[4];
    public TMP_Text[] sonarRevealTexts      = new TMP_Text[4];
    public GameObject[] sonarRevealPanels   = new GameObject[4];

    // ── 인간 플레이어 토큰 버튼 ───────────────────────────────────
    [Header("인간 플레이어 토큰 버튼")]
    public Button   useCommTokenButton;            // 통신 토큰 사용
    public Button[] useSonarButtons = new Button[3]; // 소나: AI 1·2·3번 대상

    // ── 점수 / 결과 ───────────────────────────────────────────────
    [Header("결과 패널")]
    public GameObject resultPanel;    // 성공/실패 패널
    public TMP_Text resultText;     // "미션 성공!" / "미션 실패"

    // ── 손패 표시 (인간 플레이어) ─────────────────────────────────
    [Header("손패 표시 (인간 플레이어)")]
    public Transform handCardParent;  // HorizontalLayoutGroup 부모
    public GameObject handCardPrefab;  // HandCardUI 프리팹

    // ── AI 플레이어 카드 수 ────────────────────────────────────────
    [Header("AI 카드 수 텍스트 (players[1~3] 순서)")]
    public TMP_Text[] aiCardCountTexts = new TMP_Text[3];

    // ── 플레이어 턴 하이라이트 ────────────────────────────────────
    [Header("턴 하이라이트 (플레이어 4명 순서대로)")]
    public Image[] playerTurnHighlights = new Image[4];

    // ── 플레이어별 과제 표시 ──────────────────────────────────────
    [Header("플레이어별 과제 부모 (4명)")]
    public Transform[] playerTaskParents = new Transform[4];

    // ── 태스크 선택 패널 (BGA 방식) ──────────────────────────────
    [Header("태스크 선택 패널")]
    public GameObject taskSelectionPanel;   // 선택 오버레이 패널 루트
    public TMP_Text taskSelectionTitle;   // "누구의 선택 차례" 텍스트
    public Transform taskPoolContainer;    // 태스크 풀 버튼이 들어갈 부모
    public GameObject taskPoolItemPrefab;   // 태스크 선택 버튼 프리팹

    // ─────────────────────────────────────────────────────────────

    private List<GameObject>[] allPlayerTaskItems;  // [4] 플레이어별 과제 아이템
    private List<GameObject> handCardObjs = new List<GameObject>();
    private List<GameObject> poolItemObjs = new List<GameObject>();
    private string lastHandSig = "";

    void Awake()
    {
        // 배열이 null로 직렬화된 경우 초기화
        if (commRevealPanels        == null || commRevealPanels.Length        < 4) commRevealPanels        = new GameObject[4];
        if (sonarRevealPanels       == null || sonarRevealPanels.Length       < 4) sonarRevealPanels       = new GameObject[4];
        if (commRevealCardImages    == null || commRevealCardImages.Length    < 4) commRevealCardImages    = new Image[4];
        if (commRevealPositionTexts == null || commRevealPositionTexts.Length < 4) commRevealPositionTexts = new TMP_Text[4];
        if (sonarRevealCardImages   == null || sonarRevealCardImages.Length   < 4) sonarRevealCardImages   = new Image[4];
        if (sonarRevealTexts        == null || sonarRevealTexts.Length        < 4) sonarRevealTexts        = new TMP_Text[4];
        if (useSonarButtons         == null || useSonarButtons.Length         < 3) useSonarButtons         = new Button[3];
        if (playerTaskParents       == null || playerTaskParents.Length       < 4) playerTaskParents       = new Transform[4];

        // 플레이어별 과제 아이템 추적 리스트 초기화
        allPlayerTaskItems = new List<GameObject>[4];
        for (int i = 0; i < 4; i++) allPlayerTaskItems[i] = new List<GameObject>();
    }

    void Start()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
        if (taskSelectionPanel != null) taskSelectionPanel.SetActive(false);

        // 인스펙터 미연결 시 이름으로 자동 탐색
        AutoFindRevealPanels();

        // 통신 토큰 버튼 이벤트 연결
        if (useCommTokenButton != null)
            useCommTokenButton.onClick.AddListener(OnUseCommTokenClicked);

        for (int i = 0; i < useSonarButtons.Length; i++)
        {
            int captured = i + 1; // 상대적 인덱스 1~3
            if (useSonarButtons[i] != null)
                useSonarButtons[i].onClick.AddListener(() => OnUseSonarTokenClicked(captured));
        }

        // 공개 패널 초기 숨김 (Unity null 안전 체크)
        for (int i = 0; i < 4; i++)
        {
            if (i < commRevealPanels.Length  && commRevealPanels[i]  != null) commRevealPanels[i].SetActive(false);
            if (i < sonarRevealPanels.Length && sonarRevealPanels[i] != null) sonarRevealPanels[i].SetActive(false);
        }

        // taskItemPrefab 미연결 시 Resources에서 탐색
        if (taskItemPrefab == null)
            taskItemPrefab = Resources.Load<GameObject>("TaskItem");
    }

    /// <summary>
    /// 인스펙터에서 연결되지 않은 패널/버튼을 Hierarchy에서 이름으로 자동 탐색.
    /// 새 레이아웃: PlayerSlot_0(나), PlayerSlot_1, PlayerSlot_2, PlayerSlot_3
    /// </summary>
    private void AutoFindRevealPanels()
    {
        for (int i = 0; i < 4; i++)
        {
            Transform slot = transform.Find($"PlayerSlot_{i}");
            if (slot == null) continue;

            // 과제 목록 부모
            if (i < playerTaskParents.Length && playerTaskParents[i] == null)
            {
                var tz = slot.Find("TaskListZone");
                if (tz != null) playerTaskParents[i] = tz;
            }

            // 통신 공개 패널
            if (i < commRevealPanels.Length && commRevealPanels[i] == null)
            {
                var zone = slot.Find("CommRevealZone");
                if (zone != null)
                {
                    commRevealPanels[i] = zone.gameObject;
                    var img = zone.Find("RevealCardImage");
                    var txt = zone.Find("PositionText");
                    if (img != null && i < commRevealCardImages.Length)
                        commRevealCardImages[i] = img.GetComponent<Image>();
                    if (txt != null && i < commRevealPositionTexts.Length)
                        commRevealPositionTexts[i] = txt.GetComponent<TMP_Text>();
                }
            }

            // 소나 공개 패널
            if (i < sonarRevealPanels.Length && sonarRevealPanels[i] == null)
            {
                var sZone = slot.Find("SonarRevealZone");
                if (sZone != null)
                {
                    sonarRevealPanels[i] = sZone.gameObject;
                    var img = sZone.Find("SonarCardImage");
                    var txt = sZone.Find("SonarTargetText");
                    if (img != null && i < sonarRevealCardImages.Length)
                        sonarRevealCardImages[i] = img.GetComponent<Image>();
                    if (txt != null && i < sonarRevealTexts.Length)
                        sonarRevealTexts[i] = txt.GetComponent<TMP_Text>();
                }
            }
        }

        // 인간 플레이어 토큰 버튼 (PlayerSlot_0/HumanTokenButtons)
        if (useCommTokenButton == null)
        {
            var slot0 = transform.Find("PlayerSlot_0");
            if (slot0 != null)
            {
                var btnArea = slot0.Find("HumanTokenButtons");
                if (btnArea != null)
                {
                    var commBtn = btnArea.Find("UseCommTokenBtn");
                    if (commBtn != null) useCommTokenButton = commBtn.GetComponent<Button>();

                    var sonarRow = btnArea.Find("SonarBtnRow");
                    if (sonarRow != null)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            var sb = sonarRow.Find($"SonarBtn{j + 1}");
                            if (sb != null && j < useSonarButtons.Length)
                                useSonarButtons[j] = sb.GetComponent<Button>();
                        }
                    }
                }
            }
        }
    }

    void Update()
    {
        if (GameManager.Instance == null) return;

        UpdateTrickInfo();
        UpdateTokenStatus();
        UpdateCommRevealDisplay();
        UpdateMissionInfo();
        UpdateTaskList();
        UpdateHandDisplay();
        UpdatePlayerHighlights();
        UpdateAICardCounts();
    }

    // ─────────────────────────────────────────────────────────────
    // 트릭 정보
    // ─────────────────────────────────────────────────────────────
    private void UpdateTrickInfo()
    {
        var tm = GameManager.Instance.trickManager;
        if (tm == null) return;

        // 트릭 번호 (손패 감소량으로 계산: 10장에서 시작)
        int maxCards = 10;
        int remaining = GameManager.Instance.players.Count > 0
            ? GameManager.Instance.players[0].hand.Count : maxCards;
        int trickNum = maxCards - remaining + 1;

        if (trickCountText != null)
            trickCountText.text = $"Trick {trickNum} / {maxCards}";

        if (leadSuitText != null)
            leadSuitText.text = tm.cardsOnTable.Count > 0
                ? $"선 색상: {tm.leadSuit}"
                : "선 색상: -";

        // 현재 턴 표시
        if (turnText != null)
        {
            string turnName = "-";
            foreach (var p in GameManager.Instance.players)
                if (p.isMyTurn) { turnName = p.name; break; }
            turnText.text = $"[ {turnName} ]";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 토큰 아이콘 색상 (사용 전=원색, 사용 후=어둡게)
    // ─────────────────────────────────────────────────────────────
    private static readonly Color CommActiveColor  = new Color(0.30f, 0.75f, 1.00f, 1.00f);
    private static readonly Color CommUsedColor    = new Color(0.15f, 0.35f, 0.50f, 0.45f);
    private static readonly Color SonarActiveColor = new Color(1.00f, 0.80f, 0.20f, 1.00f);
    private static readonly Color SonarUsedColor   = new Color(0.45f, 0.36f, 0.10f, 0.45f);

    private void UpdateTokenStatus()
    {
        var cm      = GameManager.Instance.communicationManager;
        var players = GameManager.Instance.players;
        if (cm == null) return;

        var tsm = GameManager.Instance.taskSpriteMapping;

        for (int i = 0; i < players.Count; i++)
        {
            bool commUsed  = cm.HasUsedCommToken(players[i]);
            bool sonarUsed = cm.HasUsedSonarToken(players[i]);

            if (i < commTokenIcons.Length && commTokenIcons[i] != null)
            {
                // 스프라이트가 있으면 교체, 없으면 색상만 변경
                if (tsm != null && (tsm.commTokenActive != null || tsm.commTokenUsed != null))
                {
                    commTokenIcons[i].sprite = commUsed ? tsm.commTokenUsed : tsm.commTokenActive;
                    commTokenIcons[i].color  = Color.white;
                }
                else
                {
                    commTokenIcons[i].color = commUsed ? CommUsedColor : CommActiveColor;
                }
            }

            if (i < sonarTokenIcons.Length && sonarTokenIcons[i] != null)
            {
                if (tsm?.sonarToken != null)
                {
                    sonarTokenIcons[i].sprite = tsm.sonarToken;
                    sonarTokenIcons[i].color  = sonarUsed ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
                }
                else
                {
                    sonarTokenIcons[i].color = sonarUsed ? SonarUsedColor : SonarActiveColor;
                }
            }
        }

        // 통신 토큰 버튼: 인간 플레이어가 아직 사용 안 했을 때만 활성화
        if (useCommTokenButton != null && players.Count > 0)
            useCommTokenButton.interactable = !cm.HasUsedCommToken(players[0]);
    }

    // ─────────────────────────────────────────────────────────────
    // 통신·소나 토큰 공개 카드 표시
    // ─────────────────────────────────────────────────────────────
    private void UpdateCommRevealDisplay()
    {
        var cm      = GameManager.Instance?.communicationManager;
        var players = GameManager.Instance?.players;
        if (cm == null || players == null) return;

        var mapping = GameManager.Instance.cardSpriteMapping;

        for (int i = 0; i < players.Count; i++)
        {
            // ── 통신 토큰 ──
            var ct     = cm.GetCommToken(players[i]);
            bool ctUsed = ct != null && ct.isUsed && ct.revealedCard != null;

            if (i < commRevealPanels.Length && commRevealPanels[i] != null)
                commRevealPanels[i].SetActive(ctUsed);

            if (ctUsed)
            {
                if (i < commRevealCardImages.Length && commRevealCardImages[i] != null)
                {
                    Sprite sp = mapping?.Get(ct.revealedCard);
                    commRevealCardImages[i].sprite = sp;
                    commRevealCardImages[i].color  = sp != null ? Color.white : SuitToColor(ct.revealedCard.suit);
                }

                if (i < commRevealPositionTexts.Length && commRevealPositionTexts[i] != null)
                {
                    commRevealPositionTexts[i].text = ct.revealPosition switch
                    {
                        CommunicationToken.RevealPosition.Highest => "▲ 최고값",
                        CommunicationToken.RevealPosition.Lowest  => "▼ 최저값",
                        CommunicationToken.RevealPosition.Only    => "● 유일",
                        _                                         => ""
                    };
                }
            }

            // ── 소나 토큰 ──
            var st     = cm.GetSonarToken(players[i]);
            bool stUsed = st != null && st.isUsed && st.revealedCard != null;

            if (i < sonarRevealPanels.Length && sonarRevealPanels[i] != null)
                sonarRevealPanels[i].SetActive(stUsed);

            if (stUsed)
            {
                if (i < sonarRevealCardImages.Length && sonarRevealCardImages[i] != null)
                {
                    Sprite sp = mapping?.Get(st.revealedCard);
                    sonarRevealCardImages[i].sprite = sp;
                    sonarRevealCardImages[i].color  = sp != null ? Color.white : SuitToColor(st.revealedCard.suit);
                }

                if (i < sonarRevealTexts.Length && sonarRevealTexts[i] != null)
                    sonarRevealTexts[i].text = $"↗ {st.target?.name ?? "?"}";
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 인간 플레이어 토큰 버튼 핸들러
    // ─────────────────────────────────────────────────────────────
    public void OnUseCommTokenClicked()
    {
        var players = GameManager.Instance?.players;
        if (players == null || players.Count == 0) return;
        bool ok = GameManager.Instance.communicationManager.UseCommToken(players[0]);
        if (!ok) Debug.Log("[UI] 통신 토큰 사용 불가 (이미 사용했거나 유효한 카드 없음)");
    }

    public void OnUseSonarTokenClicked(int relativeTarget) // 1=AI1, 2=AI2, 3=AI3
    {
        var players = GameManager.Instance?.players;
        if (players == null || players.Count == 0) return;
        bool ok = GameManager.Instance.communicationManager.UseSonarToken(players[0], relativeTarget);
        if (!ok) Debug.Log($"[UI] 소나 토큰 사용 불가 (target={relativeTarget})");
    }

    private static Color SuitToColor(Card.Suit suit) => suit switch
    {
        Card.Suit.Yellow    => new Color(1.00f, 0.80f, 0.10f),
        Card.Suit.Blue      => new Color(0.10f, 0.50f, 0.90f),
        Card.Suit.Green     => new Color(0.10f, 0.68f, 0.30f),
        Card.Suit.Pink      => new Color(0.88f, 0.18f, 0.52f),
        Card.Suit.Submarine => new Color(0.28f, 0.28f, 0.33f),
        _                   => Color.gray
    };

    // ─────────────────────────────────────────────────────────────
    // 미션 정보
    // ─────────────────────────────────────────────────────────────
    private void UpdateMissionInfo()
    {
        var mm = MissionManager.Instance;
        if (mm == null || mm.currentMission == null) return;

        if (missionImage != null && mm.currentMission.sprite != null)
            missionImage.sprite = mm.currentMission.sprite;

        if (missionIdText != null)
            missionIdText.text = $"Mission {mm.currentMission.id}";
    }

    // ─────────────────────────────────────────────────────────────
    // 과제 목록 (플레이어 4명 각자의 슬롯에 표시)
    // ─────────────────────────────────────────────────────────────
    private void UpdateTaskList()
    {
        var mm = MissionManager.Instance;
        if (mm == null) return;

        var players = GameManager.Instance.players;

        for (int i = 0; i < players.Count && i < 4; i++)
        {
            // playerTaskParents[i] 우선, 없으면 구형 taskListParent (인간만)
            Transform parent = (i < playerTaskParents.Length) ? playerTaskParents[i] : null;
            if (parent == null && i == 0) parent = taskListParent;
            if (parent == null) continue;

            List<TaskCard> pTasks = mm.tasks.FindAll(t => t.assignedTo == players[i]);
            var itemList = allPlayerTaskItems[i];

            if (pTasks.Count != itemList.Count)
            {
                foreach (var obj in itemList) Destroy(obj);
                itemList.Clear();

                if (taskItemPrefab != null)
                {
                    var tsm = GameManager.Instance.taskSpriteMapping;
                    foreach (TaskCard task in pTasks)
                    {
                        GameObject item = Instantiate(taskItemPrefab, parent);
                        SetTaskItemDisplay(item, task, tsm);
                        itemList.Add(item);
                    }
                }
            }
            else
            {
                var tsm = GameManager.Instance.taskSpriteMapping;
                for (int j = 0; j < itemList.Count && j < pTasks.Count; j++)
                {
                    SetTaskItemDisplay(itemList[j], pTasks[j], tsm);
                }
            }
        }
    }

    private Color TaskColor(TaskCard task)
    {
        if (task.isCompleted) return Color.green;
        if (task.isFailed)    return Color.red;
        return Color.white;
    }

    private void SetTaskItemDisplay(GameObject item, TaskCard task, TaskSpriteMapping tsm)
    {
        // TaskImage 없으면 런타임에 동적 생성
        var taskImgT = item.transform.Find("TaskImage");
        if (taskImgT == null)
        {
            var imgGO = new GameObject("TaskImage", typeof(RectTransform), typeof(Image));
            imgGO.transform.SetParent(item.transform, false);
            var imgRT = imgGO.GetComponent<RectTransform>();
            imgRT.anchorMin = new Vector2(0f, 0f);
            imgRT.anchorMax = new Vector2(0f, 1f);
            imgRT.offsetMin = new Vector2(3f, 3f);
            imgRT.offsetMax = new Vector2(29f, -3f);
            var imgComp = imgGO.GetComponent<Image>();
            imgComp.color = Color.white;
            imgComp.preserveAspect = true;
            imgComp.raycastTarget = false;
            imgGO.SetActive(false);
            taskImgT = imgGO.transform;
        }

        var taskImg = taskImgT.GetComponent<Image>();
        bool hasSprite = false;
        Sprite s = tsm?.GetTaskSprite(task);
        if (s != null)
        {
            taskImg.sprite = s;
            taskImg.color  = Color.white;
            taskImgT.gameObject.SetActive(true);
            hasSprite = true;
        }
        else
        {
            taskImgT.gameObject.SetActive(false);
        }

        // 라벨 텍스트 + 색상, 스프라이트 있으면 왼쪽 여백 추가
        TMP_Text label = item.transform.Find("Label")?.GetComponent<TMP_Text>()
                      ?? item.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text  = task.ToString();
            label.color = TaskColor(task);
            var lRT = label.GetComponent<RectTransform>();
            if (lRT != null)
                lRT.offsetMin = hasSprite ? new Vector2(32f, 0f) : new Vector2(5f, 0f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 외부에서 결과 패널 표시
    // ─────────────────────────────────────────────────────────────
    public void ShowResult(bool success)
    {
        if (resultPanel == null) return;
        resultPanel.SetActive(true);
        if (resultText != null)
            resultText.text = success ? "미션 성공!" : "미션 실패";
    }

    public void HideResult()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // 손패 2D 표시 (인간 플레이어)
    // ─────────────────────────────────────────────────────────────
    private void UpdateHandDisplay()
    {
        if (handCardParent == null || handCardPrefab == null) return;
        var players = GameManager.Instance.players;
        if (players.Count == 0) return;

        var human = players[0];

        // 손패가 바뀐 경우에만 재빌드
        string sig = string.Join(",", human.hand.ConvertAll(c => c.ToString()));
        if (sig == lastHandSig) return;
        lastHandSig = sig;

        foreach (var obj in handCardObjs) Destroy(obj);
        handCardObjs.Clear();

        for (int i = 0; i < human.hand.Count; i++)
        {
            var cardGO = Instantiate(handCardPrefab, handCardParent);
            var hui = cardGO.GetComponent<HandCardUI>();
            if (hui != null) hui.Setup(human.hand[i], i, human);
            handCardObjs.Add(cardGO);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // AI 손패 수 갱신
    // ─────────────────────────────────────────────────────────────
    private void UpdateAICardCounts()
    {
        var players = GameManager.Instance.players;
        for (int i = 0; i < aiCardCountTexts.Length; i++)
        {
            if (aiCardCountTexts[i] == null) continue;
            int idx = i + 1;
            if (idx < players.Count)
                aiCardCountTexts[i].text = $"{players[idx].hand.Count}장";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 현재 턴 플레이어 하이라이트
    // ─────────────────────────────────────────────────────────────
    private void UpdatePlayerHighlights()
    {
        var players = GameManager.Instance.players;
        for (int i = 0; i < players.Count && i < playerTurnHighlights.Length; i++)
        {
            if (playerTurnHighlights[i] == null) continue;
            playerTurnHighlights[i].color = players[i].isMyTurn
                ? new Color(1f, 0.9f, 0.2f, 0.35f)
                : Color.clear;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 태스크 선택 패널 (BGA 방식)
    // ─────────────────────────────────────────────────────────────
    public void ShowTaskSelection()
    {
        if (taskSelectionPanel != null) taskSelectionPanel.SetActive(true);
        RefreshTaskSelection();
    }

    public void HideTaskSelection()
    {
        if (taskSelectionPanel != null) taskSelectionPanel.SetActive(false);
    }

    public void RefreshTaskSelection()
    {
        if (taskSelectionPanel == null || taskPoolContainer == null) return;

        var mm = MissionManager.Instance;
        if (mm == null) return;

        // 제목 갱신
        if (taskSelectionTitle != null)
        {
            var picker = mm.GetCurrentPickingPlayer();
            string name = picker != null ? picker.name : "-";
            bool isHuman = picker == GameManager.Instance.players[0];
            taskSelectionTitle.text = isHuman
                ? $"[{name}] 태스크를 선택하세요"
                : $"[{name}] 선택 중...";
        }

        // 기존 풀 항목 제거
        foreach (var obj in poolItemObjs) Destroy(obj);
        poolItemObjs.Clear();

        if (taskPoolItemPrefab == null) return;

        var picker2 = mm.GetCurrentPickingPlayer();
        bool humanTurn = picker2 == GameManager.Instance.players[0];

        var tsm = GameManager.Instance.taskSpriteMapping;

        for (int i = 0; i < mm.taskPool.Count; i++)
        {
            int capturedIndex = i;
            TaskCard task = mm.taskPool[i];

            GameObject item = Instantiate(taskPoolItemPrefab, taskPoolContainer);
            poolItemObjs.Add(item);

            // 태스크 스프라이트 설정
            var taskImg = item.transform.Find("TaskImage")?.GetComponent<Image>();
            if (taskImg != null)
            {
                Sprite s = tsm?.GetTaskSprite(task);
                if (s != null)
                {
                    taskImg.sprite = s;
                    taskImg.color  = Color.white;
                    taskImg.gameObject.SetActive(true);
                }
                else
                {
                    taskImg.gameObject.SetActive(false);
                }
            }

            // 텍스트 설정 (WinSpecificCard는 카드 스프라이트로 표시되면 짧게)
            TMP_Text label = item.transform.Find("Label")?.GetComponent<TMP_Text>()
                          ?? item.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = task.ToString();

            // 버튼: 인간 차례일 때만 활성화
            if (item.TryGetComponent<Button>(out var btn))
            {
                btn.interactable = humanTurn;
                if (humanTurn)
                    btn.onClick.AddListener(() => MissionManager.Instance.HumanPickTask(capturedIndex));
            }

            // 배경색 (스프라이트 없을 때 타입별 구분)
            if (item.TryGetComponent<Image>(out var bg))
                bg.color = tsm != null ? new Color(0.1f, 0.12f, 0.18f, 0.92f) : TaskTypeColor(task.type);
        }
    }

    private Color TaskTypeColor(TaskCard.TaskType type)
    {
        return type switch
        {
            TaskCard.TaskType.WinSpecificCard => new Color(0.2f, 0.5f, 1f, 0.85f),
            TaskCard.TaskType.WinTrickCount => new Color(0.2f, 0.8f, 0.4f, 0.85f),
            TaskCard.TaskType.WinFirst => new Color(1f, 0.8f, 0.1f, 0.85f),
            TaskCard.TaskType.WinNone => new Color(0.7f, 0.2f, 0.2f, 0.85f),
            _ => Color.gray
        };
    }
}
