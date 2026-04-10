using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    [Header("토큰 상태 (플레이어 4명 순서대로)")]
    public Image[] commTokenIcons = new Image[4]; // 통신 토큰 아이콘
    public Image[] sonarTokenIcons = new Image[4]; // 소나 토큰 아이콘

    [Header("토큰 색상")]
    public Color tokenActiveColor = Color.white;
    public Color tokenUsedColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);

    // ── 점수 / 결과 ───────────────────────────────────────────────
    [Header("결과 패널")]
    public GameObject resultPanel;    // 성공/실패 패널
    public TMP_Text resultText;     // "미션 성공!" / "미션 실패"

    // ── 손패 표시 (인간 플레이어) ─────────────────────────────────
    [Header("손패 표시 (인간 플레이어)")]
    public Transform handCardParent;  // HorizontalLayoutGroup 부모
    public GameObject handCardPrefab;  // HandCardUI 프리팹

    // ── 플레이어 턴 하이라이트 ────────────────────────────────────
    [Header("턴 하이라이트 (플레이어 4명 순서대로)")]
    public Image[] playerTurnHighlights = new Image[4];

    // ── 태스크 선택 패널 (BGA 방식) ──────────────────────────────
    [Header("태스크 선택 패널")]
    public GameObject taskSelectionPanel;   // 선택 오버레이 패널 루트
    public TMP_Text taskSelectionTitle;   // "누구의 선택 차례" 텍스트
    public Transform taskPoolContainer;    // 태스크 풀 버튼이 들어갈 부모
    public GameObject taskPoolItemPrefab;   // 태스크 선택 버튼 프리팹

    // ─────────────────────────────────────────────────────────────

    private List<GameObject> taskItems = new List<GameObject>();
    private List<GameObject> handCardObjs = new List<GameObject>();
    private List<GameObject> poolItemObjs = new List<GameObject>();
    private string lastHandSig = "";

    void Start()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
        if (taskSelectionPanel != null) taskSelectionPanel.SetActive(false);
    }

    void Update()
    {
        if (GameManager.Instance == null) return;

        UpdateTrickInfo();
        UpdateTokenStatus();
        UpdateMissionInfo();
        UpdateTaskList();
        UpdateHandDisplay();
        UpdatePlayerHighlights();
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
    // 토큰 상태
    // ─────────────────────────────────────────────────────────────
    private void UpdateTokenStatus()
    {
        var cm = GameManager.Instance.communicationManager;
        var players = GameManager.Instance.players;
        if (cm == null) return;

        for (int i = 0; i < players.Count; i++)
        {
            if (i < commTokenIcons.Length && commTokenIcons[i] != null)
                commTokenIcons[i].color = cm.HasUsedCommToken(players[i])
                    ? tokenUsedColor : tokenActiveColor;

            if (i < sonarTokenIcons.Length && sonarTokenIcons[i] != null)
                sonarTokenIcons[i].color = cm.HasUsedSonarToken(players[i])
                    ? tokenUsedColor : tokenActiveColor;
        }
    }

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
    // 태스크 목록 (인간 플레이어 기준)
    // ─────────────────────────────────────────────────────────────
    private void UpdateTaskList()
    {
        var mm = MissionManager.Instance;
        if (mm == null || taskListParent == null) return;

        var human = GameManager.Instance.players.Count > 0
            ? GameManager.Instance.players[0] : null;
        if (human == null) return;

        // 태스크 목록이 바뀌면 재생성
        List<TaskCard> myTasks = mm.tasks.FindAll(t => t.assignedTo == human);
        if (myTasks.Count != taskItems.Count)
            RebuildTaskList(myTasks);
        else
            RefreshTaskColors(myTasks);
    }

    private void RebuildTaskList(List<TaskCard> myTasks)
    {
        foreach (var obj in taskItems) Destroy(obj);
        taskItems.Clear();

        if (taskItemPrefab == null) return;

        foreach (TaskCard task in myTasks)
        {
            GameObject item = Instantiate(taskItemPrefab, taskListParent);
            TMP_Text label = item.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = task.ToString();
                label.color = TaskColor(task);
            }
            taskItems.Add(item);
        }
    }

    private void RefreshTaskColors(List<TaskCard> myTasks)
    {
        for (int i = 0; i < taskItems.Count && i < myTasks.Count; i++)
        {
            TMP_Text label = taskItems[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = TaskColor(myTasks[i]);
        }
    }

    private Color TaskColor(TaskCard task)
    {
        if (task.isCompleted) return Color.green;
        if (task.isFailed) return Color.red;
        return Color.white;
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

        for (int i = 0; i < mm.taskPool.Count; i++)
        {
            int capturedIndex = i;
            TaskCard task = mm.taskPool[i];

            GameObject item = Instantiate(taskPoolItemPrefab, taskPoolContainer);
            poolItemObjs.Add(item);

            // 텍스트 설정
            TMP_Text label = item.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = task.ToString();

            // 버튼: 인간 차례일 때만 활성화
            if (item.TryGetComponent<Button>(out var btn))
            {
                btn.interactable = humanTurn;
                if (humanTurn)
                    btn.onClick.AddListener(() => MissionManager.Instance.HumanPickTask(capturedIndex));
            }

            // 배경색 (태스크 타입별 구분)
            if (item.TryGetComponent<Image>(out var bg))
                bg.color = TaskTypeColor(task.type);
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
