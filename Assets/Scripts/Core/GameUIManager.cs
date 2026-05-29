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
    public Image[] commTokenIcons      = new Image[4];
    public Image   distressSignalIcon;   // 조난신호 토큰 아이콘 (팀 공유 1개)

    [Header("토큰 색상")]
    public Color tokenActiveColor = Color.white;
    public Color tokenUsedColor   = new Color(0.3f, 0.3f, 0.3f, 0.5f);

    // ── 통신 토큰 공개 표시 ────────────────────────────────────────
    [Header("통신 토큰 공개 표시 (플레이어 4명)")]
    public Image[]    commRevealCardImages    = new Image[4];    // 공개된 카드 이미지
    public TMP_Text[] commRevealPositionTexts = new TMP_Text[4]; // ▲ 최고값 / ▼ 최저값 / ● 유일
    public GameObject[] commRevealPanels      = new GameObject[4]; // 공개 패널 루트 (숨김/표시)

    // ── 조난신호 UI ────────────────────────────────────────────────
    [Header("조난신호 패널 (첫 트릭 전 단계)")]
    public GameObject distressSignalPanel;     // 조난신호 결정 단계 패널
    public TMP_Text   distressSignalStatusText; // 조난신호 상태 텍스트

    // ── 인간 플레이어 토큰 버튼 ───────────────────────────────────
    [Header("인간 플레이어 토큰 버튼")]
    public Button useCommTokenButton;  // 통신 토큰 사용
    public Button useDistressSignalButton; // 조난신호 활성화 버튼
    public Button skipDistressSignalButton; // 조난신호 스킵 버튼

    // ── 점수 / 결과 ───────────────────────────────────────────────
    [Header("결과 패널")]
    public GameObject resultPanel;    // 성공/실패 패널
    public TMP_Text resultText;       // "미션 성공!" / "미션 실패"
    public Button restartButton;      // 수동 재시작 버튼 (미연결 시 자동 탐색)

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
    private string lastHandSig      = "";
    private string lastValiditySig  = "";

    void Awake()
    {
        // 배열이 null로 직렬화된 경우 초기화
        if (commRevealPanels        == null || commRevealPanels.Length        < 4) commRevealPanels        = new GameObject[4];
        if (commRevealCardImages    == null || commRevealCardImages.Length    < 4) commRevealCardImages    = new Image[4];
        if (commRevealPositionTexts == null || commRevealPositionTexts.Length < 4) commRevealPositionTexts = new TMP_Text[4];
        if (playerTaskParents       == null || playerTaskParents.Length       < 4) playerTaskParents       = new Transform[4];

        // 플레이어별 과제 아이템 추적 리스트 초기화
        allPlayerTaskItems = new List<GameObject>[4];
        for (int i = 0; i < 4; i++) allPlayerTaskItems[i] = new List<GameObject>();

        // Start() 실행 순서와 무관하게 패널이 숨김 상태에서 시작하도록 Awake에서 초기화
        if (resultPanel        != null) resultPanel.SetActive(false);
        if (taskSelectionPanel != null) taskSelectionPanel.SetActive(false);
    }

    void Start()
    {
        // 인스펙터 미연결 시 이름으로 자동 탐색
        AutoFindRevealPanels();

        // 통신 토큰 버튼 이벤트 연결
        if (useCommTokenButton != null)
            useCommTokenButton.onClick.AddListener(OnUseCommTokenClicked);

        // 조난신호 버튼 이벤트 연결
        if (useDistressSignalButton  != null) useDistressSignalButton.onClick.AddListener(OnUseDistressSignalClicked);
        if (skipDistressSignalButton != null) skipDistressSignalButton.onClick.AddListener(OnSkipDistressSignalClicked);

        // 공개 패널 초기 숨김 (Unity null 안전 체크)
        for (int i = 0; i < 4; i++)
        {
            if (i < commRevealPanels.Length && commRevealPanels[i] != null) commRevealPanels[i].SetActive(false);
        }
        if (distressSignalPanel != null) distressSignalPanel.SetActive(false);

        // taskItemPrefab 미연결 시 Resources에서 탐색
        if (taskItemPrefab == null)
            taskItemPrefab = Resources.Load<GameObject>("TaskItem");

        // 재시작 버튼: 미연결 시 resultPanel 하위에서 이름으로 탐색
        if (restartButton == null && resultPanel != null)
        {
            var rb = resultPanel.transform.Find("RestartButton");
            if (rb != null) restartButton = rb.GetComponent<Button>();
        }
        if (restartButton != null)
            restartButton.onClick.AddListener(OnRestartClicked);
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
                }
            }
        }

        // 조난신호 패널 자동 탐색
        if (distressSignalPanel == null)
        {
            var ds = transform.Find("DistressSignalPanel");
            if (ds != null)
            {
                distressSignalPanel = ds.gameObject;
                var txt = ds.Find("StatusText");
                if (txt != null) distressSignalStatusText = txt.GetComponent<TMP_Text>();
                var useBtn  = ds.Find("UseBtn");
                var skipBtn = ds.Find("SkipBtn");
                if (useBtn  != null) useDistressSignalButton  = useBtn.GetComponent<Button>();
                if (skipBtn != null) skipDistressSignalButton = skipBtn.GetComponent<Button>();
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
        UpdateHandCardValidity();
        UpdatePlayerHighlights();
        UpdateAICardCounts();
        UpdateTaskSelectionInput();
        UpdateDistressSignalInput();
    }

    // ─────────────────────────────────────────────────────────────
    // 태스크 선택 키보드 단축키 (1~9)
    // ─────────────────────────────────────────────────────────────
    private static readonly UnityEngine.InputSystem.Key[] s_TaskKeys =
    {
        UnityEngine.InputSystem.Key.Digit1, UnityEngine.InputSystem.Key.Digit2,
        UnityEngine.InputSystem.Key.Digit3, UnityEngine.InputSystem.Key.Digit4,
        UnityEngine.InputSystem.Key.Digit5, UnityEngine.InputSystem.Key.Digit6,
        UnityEngine.InputSystem.Key.Digit7, UnityEngine.InputSystem.Key.Digit8,
        UnityEngine.InputSystem.Key.Digit9
    };

    // ─────────────────────────────────────────────────────────────
    // 조난신호 단계 키보드 입력 (인간 플레이어)
    //   Space / Enter : 스킵 (조난신호 사용 안 함)
    //   D             : 조난신호 활성화 (첫 번째 비-로켓 카드를 오른쪽으로 전달)
    // ─────────────────────────────────────────────────────────────
    private void UpdateDistressSignalInput()
    {
        var tm = GameManager.Instance.trickManager;
        if (tm == null || tm.currentPhase != GamePhase.DistressSignal) return;

        var players = GameManager.Instance.players;
        if (players.Count == 0 || !players[0].isHumanPlayer) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
        {
            tm.ConfirmDistressSignal();
            return;
        }

        if (kb.dKey.wasPressedThisFrame)
        {
            var cm = GameManager.Instance.communicationManager;
            var human = players[0];
            Card cardToPass = human.hand.Find(c => c.suit != Card.Suit.Submarine);
            if (cardToPass != null && cm != null)
            {
                cm.ActivateDistressSignal(human, cardToPass, DistressSignal.Direction.Right);
                Debug.Log($"[조난신호 UI] {cardToPass}을 오른쪽으로 전달 예약 (Space/Enter로 확정)");
                if (distressSignalStatusText != null)
                    distressSignalStatusText.text = $"조난신호: {cardToPass} → 오른쪽\nSpace: 확정 / 취소 불가";
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 태스크 선택 키보드 단축키 (1~9)
    // ─────────────────────────────────────────────────────────────
    private void UpdateTaskSelectionInput()
    {
        if (taskSelectionPanel == null || !taskSelectionPanel.activeSelf) return;

        var mm = MissionManager.Instance;
        if (mm == null || GameManager.Instance == null) return;
        if (mm.GetCurrentPickingPlayer() != GameManager.Instance.players[0]) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        for (int i = 0; i < s_TaskKeys.Length && i < mm.taskPool.Count; i++)
        {
            if (kb[s_TaskKeys[i]].wasPressedThisFrame)
            {
                mm.HumanPickTask(i);
                return;
            }
        }
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
    private static readonly Color CommActiveColor     = new Color(0.30f, 0.75f, 1.00f, 1.00f);
    private static readonly Color CommUsedColor       = new Color(0.15f, 0.35f, 0.50f, 0.45f);
    private static readonly Color DistressActiveColor = new Color(1.00f, 0.40f, 0.20f, 1.00f);
    private static readonly Color DistressUsedColor   = new Color(0.45f, 0.18f, 0.10f, 0.45f);

    private void UpdateTokenStatus()
    {
        var cm      = GameManager.Instance.communicationManager;
        var players = GameManager.Instance.players;
        if (cm == null) return;

        var tsm = GameManager.Instance.taskSpriteMapping;

        for (int i = 0; i < players.Count; i++)
        {
            bool commUsed = cm.HasUsedCommToken(players[i]);

            if (i < commTokenIcons.Length && commTokenIcons[i] != null)
            {
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
        }

        // 조난신호 토큰 아이콘 (팀 공유)
        if (distressSignalIcon != null)
        {
            bool dsActive = cm.IsDistressSignalActive;
            if (tsm?.distressSignalToken != null)
            {
                distressSignalIcon.sprite = tsm.distressSignalToken;
                distressSignalIcon.color  = dsActive ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            }
            else
            {
                distressSignalIcon.color = dsActive ? DistressActiveColor : DistressUsedColor;
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

        }

        // 조난신호 상태 텍스트 갱신
        if (distressSignalStatusText != null)
        {
            var ds = GameManager.Instance?.communicationManager?.distressSignal;
            if (ds != null && ds.isActive)
            {
                if (ds.isExecuted)
                    distressSignalStatusText.text = $"조난신호: {ds.passingPlayer?.name} → {(ds.direction == DistressSignal.Direction.Left ? "왼쪽" : "오른쪽")} ({ds.cardToPass}) 전달 완료";
                else
                    distressSignalStatusText.text = $"조난신호 활성 — {ds.passingPlayer?.name}: {ds.cardToPass} → {(ds.direction == DistressSignal.Direction.Left ? "왼쪽" : "오른쪽")}";
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
        var phase = GameManager.Instance.trickManager.currentPhase;
        if (phase != GamePhase.Playing && phase != GamePhase.DistressSignal)
        {
            Debug.Log("[UI] 통신 토큰 — 트릭 시작 전 또는 트릭 사이에만 사용 가능");
            return;
        }
        bool ok = GameManager.Instance.communicationManager.UseCommToken(players[0]);
        if (!ok) Debug.Log("[UI] 통신 토큰 사용 불가 (이미 사용했거나 유효한 카드 없음)");
    }

    public void OnUseDistressSignalClicked()
    {
        var tm = GameManager.Instance?.trickManager;
        if (tm == null || tm.currentPhase != GamePhase.DistressSignal) return;

        var players = GameManager.Instance.players;
        var cm      = GameManager.Instance.communicationManager;
        if (players.Count == 0 || cm == null) return;

        var human    = players[0];
        Card cardToPass = human.hand.Find(c => c.suit != Card.Suit.Submarine);
        if (cardToPass != null)
        {
            cm.ActivateDistressSignal(human, cardToPass, DistressSignal.Direction.Right);
            tm.ConfirmDistressSignal();
        }
    }

    public void OnSkipDistressSignalClicked()
    {
        var tm = GameManager.Instance?.trickManager;
        if (tm == null || tm.currentPhase != GamePhase.DistressSignal) return;
        tm.ConfirmDistressSignal();
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

        // 순서 토큰 아이콘 (우측 끝에 표시)
        var orderTokenImgT = item.transform.Find("OrderTokenImage");
        if (orderTokenImgT == null && task.orderToken != OrderToken.None)
        {
            var oGO = new GameObject("OrderTokenImage", typeof(RectTransform), typeof(Image));
            oGO.transform.SetParent(item.transform, false);
            var oRT = oGO.GetComponent<RectTransform>();
            oRT.anchorMin = new Vector2(1f, 0f);
            oRT.anchorMax = new Vector2(1f, 1f);
            oRT.offsetMin = new Vector2(-28f, 2f);
            oRT.offsetMax = new Vector2(-2f, -2f);
            var oImg = oGO.GetComponent<Image>();
            oImg.preserveAspect = true;
            oImg.raycastTarget  = false;
            orderTokenImgT = oGO.transform;
        }
        if (orderTokenImgT != null)
        {
            Sprite tokenSprite = tsm?.GetOrderTokenSprite(task.orderToken);
            orderTokenImgT.GetComponent<Image>().sprite = tokenSprite;
            orderTokenImgT.gameObject.SetActive(tokenSprite != null);
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

        if (resultText == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(success ? "[성공] 미션 클리어!" : "[실패] 미션 실패");

        var mm = MissionManager.Instance;
        if (mm != null && mm.tasks.Count > 0)
        {
            sb.AppendLine();
            foreach (var task in mm.tasks)
            {
                string icon  = task.isCompleted ? "O" : task.isFailed ? "X" : "-";
                string owner = task.assignedTo?.name ?? "?";
                sb.AppendLine($"  {icon} [{owner}] {task}");
            }
        }

        resultText.text = sb.ToString();
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
        lastHandSig     = sig;
        lastValiditySig = ""; // 손패 변경 시 유효성 강제 갱신

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
    // 손패 카드 유효/무효 표시 (follow-suit 위반 카드 반투명)
    // ─────────────────────────────────────────────────────────────
    private void UpdateHandCardValidity()
    {
        var tm      = GameManager.Instance.trickManager;
        var players = GameManager.Instance.players;
        if (players.Count == 0 || handCardObjs.Count == 0) return;

        var human = players[0];

        // 인간 차례 + Playing 단계 + 선 카드가 이미 나온 상태일 때만 dimming 적용
        bool applyDim = tm != null
                     && tm.currentPhase == GamePhase.Playing
                     && human.isMyTurn
                     && tm.cardsOnTable.Count > 0;

        string vsig = $"{applyDim}:{tm?.leadSuit}:{tm?.cardsOnTable.Count}";
        if (vsig == lastValiditySig) return;
        lastValiditySig = vsig;

        for (int i = 0; i < handCardObjs.Count && i < human.hand.Count; i++)
        {
            var hui = handCardObjs[i].GetComponent<HandCardUI>();
            if (hui == null) continue;
            bool valid = !applyDim || tm.IsValidPlay(human, human.hand[i]);
            hui.SetPlayable(valid);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 결과 패널 수동 재시작
    // ─────────────────────────────────────────────────────────────
    public void OnRestartClicked()
    {
        HideResult();
        GameManager.Instance.trickManager.ManualRestart();
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

    /// <summary>조난신호 단계 UI 표시 (인간 플레이어 전용)</summary>
    public void ShowDistressSignalPhase()
    {
        if (distressSignalPanel != null)
        {
            distressSignalPanel.SetActive(true);
            if (distressSignalStatusText != null)
                distressSignalStatusText.text = "조난신호를 사용하시겠습니까?\nD키: 첫 카드를 오른쪽으로 전달\nSpace / Enter: 건너뛰기";
        }
        else if (taskSelectionTitle != null)
        {
            // 별도 패널 없을 때 태스크 선택 제목 텍스트 재활용
            taskSelectionTitle.text = "[조난신호 단계]\nD: 카드 전달  /  Space·Enter: 건너뛰기";
        }
    }

    public void HideDistressSignalPhase()
    {
        if (distressSignalPanel != null) distressSignalPanel.SetActive(false);
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
            string pickerName = picker != null ? picker.name : "-";
            bool isHuman = picker == GameManager.Instance.players[0];
            taskSelectionTitle.text = isHuman
                ? $"[{pickerName}] 태스크를 선택하세요"
                : $"[{pickerName}] 선택 중...";
        }

        // 기존 풀 항목 제거
        foreach (var obj in poolItemObjs) Destroy(obj);
        poolItemObjs.Clear();

        var picker2 = mm.GetCurrentPickingPlayer();
        bool humanTurn = picker2 == GameManager.Instance.players[0];

        var tsm = GameManager.Instance.taskSpriteMapping;

        for (int i = 0; i < mm.taskPool.Count; i++)
        {
            int capturedIndex = i;
            TaskCard task = mm.taskPool[i];

            // 프리팹이 없으면 동적으로 버튼 생성
            GameObject item = taskPoolItemPrefab != null
                ? Instantiate(taskPoolItemPrefab, taskPoolContainer)
                : CreateFallbackPoolItem(taskPoolContainer);
            poolItemObjs.Add(item);

            // 태스크 스프라이트 설정
            var taskImg = item.transform.Find("TaskImage")?.GetComponent<Image>();
            if (taskImg != null)
            {
                Sprite s = tsm?.GetTaskSprite(task);
                if (s != null) { taskImg.sprite = s; taskImg.color = Color.white; taskImg.gameObject.SetActive(true); }
                else taskImg.gameObject.SetActive(false);
            }

            // 텍스트 설정
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

            // 배경색
            if (item.TryGetComponent<Image>(out var bg))
                bg.color = tsm != null ? new Color(0.1f, 0.12f, 0.18f, 0.92f) : TaskTypeColor(task.type);

            // 순서 토큰 배지 (orderIndex > 0인 태스크)
            if (task.orderIndex > 0)
            {
                var badge = new GameObject("OrderBadge", typeof(RectTransform));
                badge.transform.SetParent(item.transform, false);
                var bRT = badge.GetComponent<RectTransform>();
                bRT.anchorMin = bRT.anchorMax = new Vector2(0f, 1f);
                bRT.pivot     = new Vector2(0f, 1f);
                bRT.anchoredPosition = new Vector2(2f, -2f);
                bRT.sizeDelta        = new Vector2(20f, 20f);
                var bBG = badge.AddComponent<Image>();
                bBG.color = new Color(1f, 0.75f, 0f, 0.95f);
                bBG.raycastTarget = false;

                var bNumGO = new GameObject("Num", typeof(RectTransform));
                bNumGO.transform.SetParent(badge.transform, false);
                var bNRT = bNumGO.GetComponent<RectTransform>();
                bNRT.anchorMin = Vector2.zero;
                bNRT.anchorMax = Vector2.one;
                bNRT.offsetMin = bNRT.offsetMax = Vector2.zero;
                var bTmp = bNumGO.AddComponent<TextMeshProUGUI>();
                bTmp.text      = task.orderIndex.ToString();
                bTmp.fontSize  = 12f;
                bTmp.fontStyle = TMPro.FontStyles.Bold;
                bTmp.alignment = TMPro.TextAlignmentOptions.Center;
                bTmp.color     = Color.black;
                bTmp.raycastTarget = false;
            }
        }
    }

    // taskPoolItemPrefab 미연결 시 동적으로 버튼 아이템 생성
    private GameObject CreateFallbackPoolItem(Transform parent)
    {
        var go = new GameObject("PoolItem", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(280f, 55f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.14f, 0.20f, 0.95f);
        bg.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        var cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = new Color(0.12f, 0.14f, 0.20f, 0.95f);
        cb.highlightedColor = new Color(0.25f, 0.35f, 0.55f, 1f);
        cb.pressedColor     = new Color(0.08f, 0.10f, 0.16f, 1f);
        cb.selectedColor    = new Color(0.20f, 0.28f, 0.45f, 1f);
        btn.colors = cb;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, false);
        var lRT = labelGO.GetComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero;
        lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(6f, 3f);
        lRT.offsetMax = new Vector2(-6f, -3f);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.fontSize  = 13f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
        tmp.raycastTarget = false;

        return go;
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
