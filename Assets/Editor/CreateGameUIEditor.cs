using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;

/// <summary>
/// SeaAI/Create Game UI
///
/// 모든 최상위 Canvas 자식은 순수 앵커 비율(offsetMin=offsetMax=zero)로 배치한다.
/// 이렇게 해야 Edit 모드 Scene 뷰와 Play 모드 Game 뷰가 동일하게 보인다.
///
/// 레이아웃 (1920×1080 기준):
///   TopBar  : 전체 너비, 상단 44px
///   TL 슬롯  : PlayerSlot_1 (players[1])  ← 왼쪽 위
///   TR 슬롯  : PlayerSlot_3 (players[3])  ← 오른쪽 위
///   BL 슬롯  : PlayerSlot_2 (players[2])  ← 왼쪽 아래
///   BR 슬롯  : PlayerSlot_0 (players[0], 나) ← 오른쪽 아래
///   Center   : 미션 패널
///   HandArea : 전체 너비, 하단 195px
/// </summary>
public static class CreateGameUIEditor
{
    // ── 레이아웃 비율 상수 (1920×1080 기준 → 0~1 범위) ─────────────
    const float REF_W = 1920f;
    const float REF_H = 1080f;

    // 화면 분할 비율
    const float TOP_F  = 44f  / REF_H;   // 상단바 높이 비율
    const float BOT_F  = 195f / REF_H;   // 손패 영역 높이 비율
    const float SLOT_W = 265f / REF_W;   // 플레이어 슬롯 너비 비율
    const float SLOT_H = 225f / REF_H;   // 플레이어 슬롯 높이 비율
    const float MRG_X  = 10f  / REF_W;   // 수평 여백 비율
    const float MRG_Y  = 10f  / REF_H;   // 수직 여백 비율

    // 슬롯 Y 좌표 (상단/하단)
    static float SlotTopY    => 1f - TOP_F - MRG_Y;
    static float SlotTopBotY => 1f - TOP_F - MRG_Y - SLOT_H;
    static float SlotBotY    => BOT_F + MRG_Y;
    static float SlotBotTopY => BOT_F + MRG_Y + SLOT_H;

    // 슬롯 X 좌표 (왼쪽/오른쪽)
    static float SlotLMin => MRG_X;
    static float SlotLMax => MRG_X + SLOT_W;
    static float SlotRMin => 1f - MRG_X - SLOT_W;
    static float SlotRMax => 1f - MRG_X;

    const string FONT_PATH = "Assets/TextMesh Pro/Fonts/BMJUA_ttf SDF.asset";

    static readonly Color BgDark   = new Color(0.04f, 0.07f, 0.13f, 0.92f);
    static readonly Color BgMid    = new Color(0.08f, 0.12f, 0.20f, 0.90f);
    static readonly Color BgSlot   = new Color(0.06f, 0.10f, 0.17f, 0.95f);
    static readonly Color BgHuman  = new Color(0.04f, 0.10f, 0.06f, 0.95f);
    static readonly Color ColComm  = new Color(0.30f, 0.75f, 1.00f, 1.00f);
    static readonly Color ColSonar = new Color(1.00f, 0.80f, 0.20f, 1.00f);
    static readonly Color ColTxt   = new Color(0.92f, 0.95f, 1.00f, 1.00f);
    static readonly Color ColHuman = new Color(0.35f, 1.00f, 0.55f, 1.00f);

    static TMP_FontAsset _font;

    [MenuItem("SeaAI/Create Game UI")]
    public static void Create()
    {
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
        if (_font == null)
            Debug.LogWarning($"[SeaAI] 폰트 없음: {FONT_PATH}");

        // ── Canvas ─────────────────────────────────────────────────────
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var go = new GameObject("GameCanvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            go.AddComponent<GraphicRaycaster>();
        }
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REF_W, REF_H);
        scaler.matchWidthOrHeight  = 0.5f;

        // 기존 자식 전부 제거 → 클린 빌드
        for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(canvas.transform.GetChild(i).gameObject);

        var ui = canvas.GetComponent<GameUIManager>();
        if (ui == null) ui = canvas.gameObject.AddComponent<GameUIManager>();

        // 배열 초기화
        ui.commTokenIcons          = new Image[4];
        ui.sonarTokenIcons         = new Image[4];
        ui.playerTurnHighlights    = new Image[4];
        ui.commRevealPanels        = new GameObject[4];
        ui.commRevealCardImages    = new Image[4];
        ui.commRevealPositionTexts = new TMP_Text[4];
        ui.sonarRevealPanels       = new GameObject[4];
        ui.sonarRevealCardImages   = new Image[4];
        ui.sonarRevealTexts        = new TMP_Text[4];
        ui.aiCardCountTexts        = new TMP_Text[3];
        ui.useSonarButtons         = new Button[3];
        ui.playerTaskParents       = new Transform[4];

        Transform R = canvas.transform;

        // ── 1. TopBar ──────────────────────────────────────────────────
        // 순수 앵커 비율. offsetMin=offsetMax=zero
        var topBar = AnchorPanel(R, "TopBar", BgDark,
            new Vector2(0f, 1f - TOP_F), new Vector2(1f, 1f));

        ui.leadSuitText = Txt(topBar.transform, "LeadSuitText", "선 색상: -", 14, ColTxt,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 0f), new Vector2(0.25f, 1f),
            new Vector2(14f, 0f), Vector2.zero);

        ui.turnText = Txt(topBar.transform, "TurnText", "[ - ]", 18, Color.white,
            TextAlignmentOptions.Center,
            new Vector2(0.25f, 0f), new Vector2(0.75f, 1f),
            Vector2.zero, Vector2.zero);

        ui.trickCountText = Txt(topBar.transform, "TrickCountText", "Trick 1 / 10", 14, ColTxt,
            TextAlignmentOptions.MidlineRight,
            new Vector2(0.75f, 0f), new Vector2(1f, 1f),
            Vector2.zero, new Vector2(-14f, 0f));

        // ── 2. 플레이어 슬롯 4개 (2×2, 순수 앵커 비율) ─────────────
        // players[1] → 왼쪽 위
        MakePlayerSlot(R, "PlayerSlot_1", "플레이어 1",
            new Vector2(SlotLMin, SlotTopBotY), new Vector2(SlotLMax, SlotTopY),
            ui, playerIndex: 1, uiAiIndex: 0);

        // players[3] → 오른쪽 위
        MakePlayerSlot(R, "PlayerSlot_3", "플레이어 3",
            new Vector2(SlotRMin, SlotTopBotY), new Vector2(SlotRMax, SlotTopY),
            ui, playerIndex: 3, uiAiIndex: 2);

        // players[2] → 왼쪽 아래
        MakePlayerSlot(R, "PlayerSlot_2", "플레이어 2",
            new Vector2(SlotLMin, SlotBotY), new Vector2(SlotLMax, SlotBotTopY),
            ui, playerIndex: 2, uiAiIndex: 1);

        // players[0] → 오른쪽 아래 (나)
        MakePlayerSlot(R, "PlayerSlot_0", "나 (플레이어)",
            new Vector2(SlotRMin, SlotBotY), new Vector2(SlotRMax, SlotBotTopY),
            ui, playerIndex: 0, uiAiIndex: -1);

        // ── 3. 중앙 미션 패널 ──────────────────────────────────────────
        var mPanel = AnchorPanel(R, "MissionPanel", BgMid,
            new Vector2(0.34f, 0.41f), new Vector2(0.66f, 0.80f));

        Txt(mPanel.transform, "MissionTitle", "미션", 14, ColTxt,
            TextAlignmentOptions.Center,
            new Vector2(0f, 0.82f), new Vector2(1f, 1f),
            Vector2.zero, Vector2.zero);

        var mImgGO = new GameObject("MissionImage", typeof(RectTransform), typeof(Image));
        mImgGO.transform.SetParent(mPanel.transform, false);
        var mRT = mImgGO.GetComponent<RectTransform>();
        mRT.anchorMin = new Vector2(0.05f, 0.18f);
        mRT.anchorMax = new Vector2(0.95f, 0.82f);
        mRT.offsetMin = mRT.offsetMax = Vector2.zero;
        ui.missionImage = mImgGO.GetComponent<Image>();
        ui.missionImage.color = new Color(0.95f, 0.78f, 0.18f, 0.85f);

        ui.missionIdText = Txt(mPanel.transform, "MissionIdText", "미션 ---", 12, ColTxt,
            TextAlignmentOptions.Center,
            new Vector2(0f, 0f), new Vector2(1f, 0.18f),
            Vector2.zero, Vector2.zero);

        // ── 4. 손패 영역 (하단) ────────────────────────────────────────
        var handBg = AnchorPanel(R, "HandArea", new Color(0.04f, 0.07f, 0.12f, 0.80f),
            new Vector2(0f, 0f), new Vector2(1f, BOT_F));

        var handArea = new GameObject("HandCardParent", typeof(RectTransform));
        handArea.transform.SetParent(handBg.transform, false);
        var handRT = handArea.GetComponent<RectTransform>();
        handRT.anchorMin = Vector2.zero;
        handRT.anchorMax = Vector2.one;
        handRT.offsetMin = new Vector2(8f, 8f);
        handRT.offsetMax = new Vector2(-8f, -8f);
        var handHG = handArea.AddComponent<HorizontalLayoutGroup>();
        handHG.spacing              = 8f;
        handHG.childAlignment       = TextAnchor.MiddleCenter;
        handHG.childForceExpandWidth  = false;
        handHG.childForceExpandHeight = true;
        handHG.padding = new RectOffset(8, 8, 6, 6);
        ui.handCardParent = handRT;
        ui.handCardPrefab = MakeHandCardPrefab();

        // ── 5. 결과 패널 ───────────────────────────────────────────────
        var resultBg = AnchorPanel(R, "ResultPanel", new Color(0f, 0f, 0f, 0.78f),
            Vector2.zero, Vector2.one);
        var resultCard = AnchorPanel(resultBg.transform, "ResultCard",
            new Color(0.08f, 0.11f, 0.18f, 0.98f),
            new Vector2(0.28f, 0.40f), new Vector2(0.72f, 0.60f));
        ui.resultText = Txt(resultCard.transform, "ResultText", "미션 성공!", 44,
            new Color(0.2f, 0.9f, 0.35f, 1f), TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ui.resultPanel = resultBg;
        resultBg.SetActive(false);

        // ── 6. 태스크 선택 오버레이 ───────────────────────────────────
        var taskSelPanel = AnchorPanel(R, "TaskSelectionPanel",
            new Color(0f, 0f, 0f, 0.85f), Vector2.zero, Vector2.one);

        ui.taskSelectionTitle = Txt(taskSelPanel.transform, "TaskSelTitle",
            "태스크를 선택하세요", 26, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.88f),
            Vector2.zero, Vector2.zero);

        var poolGO = new GameObject("TaskPoolContainer", typeof(RectTransform));
        poolGO.transform.SetParent(taskSelPanel.transform, false);
        var poolRT = poolGO.GetComponent<RectTransform>();
        poolRT.anchorMin = new Vector2(0.08f, 0.12f);
        poolRT.anchorMax = new Vector2(0.92f, 0.70f);
        poolRT.offsetMin = poolRT.offsetMax = Vector2.zero;
        var grid = poolGO.AddComponent<GridLayoutGroup>();
        grid.cellSize       = new Vector2(200f, 80f);
        grid.spacing        = new Vector2(14f, 14f);
        grid.childAlignment = TextAnchor.UpperCenter;
        ui.taskPoolContainer  = poolRT;
        ui.taskPoolItemPrefab = MakeTaskPoolItemPrefab();
        ui.taskItemPrefab     = MakeTaskItemPrefab();
        taskSelPanel.SetActive(false);
        ui.taskSelectionPanel = taskSelPanel;

        // ── GameManager 자동 연결 ────────────────────────────────────
        var gm = Object.FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            var so   = new SerializedObject(gm);
            var prop = so.FindProperty("uiManager");
            if (prop != null) { prop.objectReferenceValue = ui; so.ApplyModifiedProperties(); }
            EditorUtility.SetDirty(gm);
            Debug.Log("[SeaAI] GameManager.uiManager 자동 연결 완료");
        }

        Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Create SeaAI Game UI");
        EditorUtility.SetDirty(canvas.gameObject);
        Selection.activeGameObject = canvas.gameObject;
        Debug.Log("[SeaAI] Game UI 생성 완료!");
    }

    // ── 플레이어 슬롯 (4명 공용, 최상위도 순수 앵커) ────────────────────
    static void MakePlayerSlot(Transform parent, string slotName, string playerLabel,
        Vector2 ancMin, Vector2 ancMax,
        GameUIManager ui, int playerIndex, int uiAiIndex)
    {
        bool isHuman = (playerIndex == 0);

        // 슬롯 루트: 순수 앵커, offset=zero
        var slot = AnchorPanel(parent, slotName,
            isHuman ? BgHuman : BgSlot, ancMin, ancMax);

        // ── 턴 하이라이트 (전체, 최하위 레이어) ──────────────────────
        var hl = new GameObject("TurnHighlight", typeof(RectTransform), typeof(Image));
        hl.transform.SetParent(slot.transform, false);
        FillParent(hl.GetComponent<RectTransform>());
        var hlImg = hl.GetComponent<Image>();
        hlImg.color = Color.clear;
        hlImg.raycastTarget = false;
        ui.playerTurnHighlights[playerIndex] = hlImg;

        // ── 이름 (상단 16%) ───────────────────────────────────────────
        Txt(slot.transform, "NameText", playerLabel, 13,
            isHuman ? ColHuman : ColTxt,
            TextAlignmentOptions.Center,
            new Vector2(0f, 0.84f), new Vector2(1f, 1f),
            new Vector2(4f, 2f), new Vector2(-4f, -2f));

        // ── 토큰 행 (67~84%) ─────────────────────────────────────────
        var tokenRowGO = new GameObject("TokenRow", typeof(RectTransform));
        tokenRowGO.transform.SetParent(slot.transform, false);
        var trRT = tokenRowGO.GetComponent<RectTransform>();
        trRT.anchorMin = new Vector2(0f, 0.67f);
        trRT.anchorMax = new Vector2(1f, 0.84f);
        trRT.offsetMin = new Vector2(4f, 0f);
        trRT.offsetMax = new Vector2(-4f, 0f);
        var trHG = tokenRowGO.AddComponent<HorizontalLayoutGroup>();
        trHG.spacing              = 4f;
        trHG.childAlignment       = TextAnchor.MiddleLeft;
        trHG.childForceExpandWidth  = false;
        trHG.childForceExpandHeight = false;
        trHG.padding = new RectOffset(4, 4, 2, 2);

        // 통신 토큰 아이콘 + 레이블
        MakeTokenIcon(tokenRowGO.transform, "CommTokenIcon", ColComm, out var commImg);
        ui.commTokenIcons[playerIndex] = commImg;
        MakeLabel(tokenRowGO.transform, "CommLabel", "통신", 11,
            new Color(ColComm.r, ColComm.g, ColComm.b, 0.85f), 28f);

        Spacer(tokenRowGO.transform, 6f);

        // 소나 토큰 아이콘 + 레이블
        MakeTokenIcon(tokenRowGO.transform, "SonarTokenIcon", ColSonar, out var sonarImg);
        ui.sonarTokenIcons[playerIndex] = sonarImg;
        MakeLabel(tokenRowGO.transform, "SonarLabel", "소나", 11,
            new Color(ColSonar.r, ColSonar.g, ColSonar.b, 0.85f), 28f);

        // AI: 카드 수 텍스트
        if (!isHuman && uiAiIndex >= 0)
        {
            Spacer(tokenRowGO.transform, 6f);
            var countGO = new GameObject("CardCountText",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            countGO.transform.SetParent(tokenRowGO.transform, false);
            var cTmp = countGO.GetComponent<TextMeshProUGUI>();
            cTmp.text = "10장"; cTmp.fontSize = 12; cTmp.color = ColTxt;
            cTmp.alignment = TextAlignmentOptions.MidlineLeft;
            if (_font != null) cTmp.font = _font;
            countGO.AddComponent<LayoutElement>().preferredWidth = 40f;
            ui.aiCardCountTexts[uiAiIndex] = cTmp;
        }

        // ── 과제 목록 영역 (17~67%, 인간은 하단 버튼 여백) ───────────
        float taskBottom = isHuman ? 0.17f : 0.02f;
        var taskZoneGO = new GameObject("TaskListZone", typeof(RectTransform));
        taskZoneGO.transform.SetParent(slot.transform, false);
        var tzRT = taskZoneGO.GetComponent<RectTransform>();
        tzRT.anchorMin = new Vector2(0.02f, taskBottom);
        tzRT.anchorMax = new Vector2(0.98f, 0.67f);
        tzRT.offsetMin = tzRT.offsetMax = Vector2.zero;
        var vl = taskZoneGO.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 2f;
        vl.childForceExpandWidth  = true;
        vl.childForceExpandHeight = false;
        vl.padding = new RectOffset(2, 2, 2, 2);
        ui.playerTaskParents[playerIndex] = tzRT;

        // ── 통신 공개 표시 오버레이 (숨김) ───────────────────────────
        var commZone = new GameObject("CommRevealZone", typeof(RectTransform), typeof(Image));
        commZone.transform.SetParent(slot.transform, false);
        var czRT = commZone.GetComponent<RectTransform>();
        czRT.anchorMin = new Vector2(0.02f, 0.16f);
        czRT.anchorMax = new Vector2(0.70f, 0.67f);
        czRT.offsetMin = czRT.offsetMax = Vector2.zero;
        commZone.GetComponent<Image>().color = new Color(0.08f, 0.38f, 0.78f, 0.93f);

        var revCard = new GameObject("RevealCardImage", typeof(RectTransform), typeof(Image));
        revCard.transform.SetParent(commZone.transform, false);
        var rcRT = revCard.GetComponent<RectTransform>();
        rcRT.anchorMin = new Vector2(0.08f, 0.22f);
        rcRT.anchorMax = new Vector2(0.92f, 0.88f);
        rcRT.offsetMin = rcRT.offsetMax = Vector2.zero;

        var posTxt = Txt(commZone.transform, "PositionText", "▲ 최고", 10, Color.yellow,
            TextAlignmentOptions.Center,
            new Vector2(0f, 0f), new Vector2(1f, 0.26f),
            Vector2.zero, Vector2.zero);

        commZone.SetActive(false);
        ui.commRevealPanels[playerIndex]        = commZone;
        ui.commRevealCardImages[playerIndex]    = revCard.GetComponent<Image>();
        ui.commRevealPositionTexts[playerIndex] = posTxt;

        // ── 인간 토큰 버튼 (0~17%, players[0] 전용) ──────────────────
        if (isHuman)
        {
            var btnArea = new GameObject("HumanTokenButtons", typeof(RectTransform));
            btnArea.transform.SetParent(slot.transform, false);
            var baRT = btnArea.GetComponent<RectTransform>();
            baRT.anchorMin = new Vector2(0f, 0f);
            baRT.anchorMax = new Vector2(1f, 0.17f);
            baRT.offsetMin = new Vector2(4f, 3f);
            baRT.offsetMax = new Vector2(-4f, -3f);
            var baHG = btnArea.AddComponent<HorizontalLayoutGroup>();
            baHG.spacing              = 4f;
            baHG.childAlignment       = TextAnchor.MiddleCenter;
            baHG.childForceExpandWidth  = true;
            baHG.childForceExpandHeight = true;
            baHG.padding = new RectOffset(2, 2, 1, 1);

            // 통신 토큰 버튼
            var commBtnGO = new GameObject("UseCommTokenBtn",
                typeof(RectTransform), typeof(Image), typeof(Button));
            commBtnGO.transform.SetParent(btnArea.transform, false);
            commBtnGO.GetComponent<Image>().color = ColComm;
            Txt(commBtnGO.transform, "L", "통신 공개", 11, Color.black,
                TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ui.useCommTokenButton = commBtnGO.GetComponent<Button>();

            // 소나 버튼 3개 묶음
            var sonarRowGO = new GameObject("SonarBtnRow", typeof(RectTransform));
            sonarRowGO.transform.SetParent(btnArea.transform, false);
            var srHG = sonarRowGO.AddComponent<HorizontalLayoutGroup>();
            srHG.spacing              = 3f;
            srHG.childForceExpandWidth  = true;
            srHG.childForceExpandHeight = true;

            for (int j = 0; j < 3; j++)
            {
                var sbGO = new GameObject($"SonarBtn{j + 1}",
                    typeof(RectTransform), typeof(Image), typeof(Button));
                sbGO.transform.SetParent(sonarRowGO.transform, false);
                sbGO.GetComponent<Image>().color = new Color(0.80f, 0.60f, 0.10f, 1f);
                Txt(sbGO.transform, "L", $"소나{j + 1}", 10, Color.black,
                    TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                if (j < ui.useSonarButtons.Length)
                    ui.useSonarButtons[j] = sbGO.GetComponent<Button>();
            }
        }
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────────

    /// <summary>순수 앵커 비율 패널. offsetMin=offsetMax=zero. pivot=(0.5,0.5).</summary>
    static GameObject AnchorPanel(Transform parent, string name, Color color,
        Vector2 ancMin, Vector2 ancMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = ancMin;
        rt.anchorMax = ancMax;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return go;
    }

    static void FillParent(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void MakeTokenIcon(Transform parent, string name, Color color, out Image img)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        img = go.GetComponent<Image>();
        img.color = color;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = 22f;
        le.preferredHeight = 22f;
    }

    static void MakeLabel(Transform parent, string name, string text,
        float fontSize, Color color, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = fontSize; tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        if (_font != null) tmp.font = _font;
        go.AddComponent<LayoutElement>().preferredWidth = width;
    }

    static void Spacer(Transform parent, float width)
    {
        var go = new GameObject("_sep", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredWidth = width;
    }

    static TMP_Text Txt(Transform parent, string name, string text,
        float size, Color color, TextAlignmentOptions align,
        Vector2 ancMin, Vector2 ancMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
        if (_font != null) tmp.font = _font;
        return tmp;
    }

    // ── 프리팹 생성 ─────────────────────────────────────────────────────

    static GameObject MakeHandCardPrefab()
    {
        const string dir  = "Assets/Prefabs";
        const string path = dir + "/HandCard.prefab";
        if (!Directory.Exists(dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        var card = new GameObject("HandCard", typeof(RectTransform));
        card.GetComponent<RectTransform>().sizeDelta = new Vector2(82f, 124f);
        var bg  = card.AddComponent<Image>(); bg.color = Color.white;
        var btn = card.AddComponent<Button>();
        var bc  = btn.colors;
        bc.highlightedColor = new Color(1f, 1f, 0.7f, 1f);
        bc.pressedColor     = new Color(0.7f, 0.7f, 0.7f, 1f);
        btn.colors = bc;
        var hui = card.AddComponent<HandCardUI>();

        // 카드 앞면 스프라이트 이미지 (CardSpriteMapping 연결 시 사용)
        var faceGO = new GameObject("CardFaceImage", typeof(RectTransform), typeof(Image));
        faceGO.transform.SetParent(card.transform, false);
        var faceRT = faceGO.GetComponent<RectTransform>();
        faceRT.anchorMin = Vector2.zero;
        faceRT.anchorMax = Vector2.one;
        faceRT.offsetMin = faceRT.offsetMax = Vector2.zero;
        var faceImg = faceGO.GetComponent<Image>();
        faceImg.color = Color.white;
        faceImg.raycastTarget = false;
        faceImg.preserveAspect = true;
        faceGO.SetActive(false); // 스프라이트가 없으면 숨김

        // 폴백용 텍스트 (스프라이트 없을 때만 표시)
        var valGO = new GameObject("ValueText", typeof(RectTransform), typeof(TextMeshProUGUI));
        valGO.transform.SetParent(card.transform, false);
        var valRT  = valGO.GetComponent<RectTransform>();
        valRT.anchorMin = new Vector2(0f, 0.28f); valRT.anchorMax = Vector2.one;
        valRT.offsetMin = valRT.offsetMax = Vector2.zero;
        var valTmp = valGO.GetComponent<TextMeshProUGUI>();
        valTmp.text = "1"; valTmp.fontSize = 32; valTmp.fontStyle = FontStyles.Bold;
        valTmp.color = Color.white; valTmp.alignment = TextAlignmentOptions.Center;
        if (_font != null) valTmp.font = _font;

        var suitGO = new GameObject("SuitText", typeof(RectTransform), typeof(TextMeshProUGUI));
        suitGO.transform.SetParent(card.transform, false);
        var suitRT  = suitGO.GetComponent<RectTransform>();
        suitRT.anchorMin = Vector2.zero; suitRT.anchorMax = new Vector2(1f, 0.32f);
        suitRT.offsetMin = suitRT.offsetMax = Vector2.zero;
        var suitTmp = suitGO.GetComponent<TextMeshProUGUI>();
        suitTmp.text = "Y"; suitTmp.fontSize = 13;
        suitTmp.color = new Color(1f, 1f, 1f, 0.8f);
        suitTmp.alignment = TextAlignmentOptions.Center;
        if (_font != null) suitTmp.font = _font;

        var border = new GameObject("SelectedBorder", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(card.transform, false);
        var borderRT  = border.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-3f, -3f); borderRT.offsetMax = new Vector2(3f, 3f);
        var borderImg = border.GetComponent<Image>();
        borderImg.color = new Color(1f, 0.95f, 0.1f, 0.9f);
        borderImg.raycastTarget = false;
        border.SetActive(false);

        hui.background     = bg;
        hui.cardFaceImage  = faceImg;
        hui.selectedBorder = borderImg;
        hui.valueText      = valTmp;
        hui.suitText       = suitTmp;

        var prefab = PrefabUtility.SaveAsPrefabAsset(card, path);
        Object.DestroyImmediate(card);
        return prefab;
    }

    static GameObject MakeTaskPoolItemPrefab()
    {
        const string dir  = "Assets/Prefabs";
        const string path = dir + "/TaskPoolItem.prefab";
        if (!Directory.Exists(dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        var item = new GameObject("TaskPoolItem", typeof(RectTransform));
        item.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 100f);
        item.AddComponent<Image>().color = new Color(0.1f, 0.12f, 0.18f, 0.92f);
        var btn = item.AddComponent<Button>();
        var bc  = btn.colors;
        bc.highlightedColor = new Color(1f, 1f, 0.7f, 1f);
        bc.pressedColor     = new Color(0.6f, 0.6f, 0.6f, 1f);
        bc.disabledColor    = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        btn.colors = bc;

        // 왼쪽: 태스크 스프라이트 이미지
        var imgGO = new GameObject("TaskImage", typeof(RectTransform), typeof(Image));
        imgGO.transform.SetParent(item.transform, false);
        var imgRT = imgGO.GetComponent<RectTransform>();
        imgRT.anchorMin = new Vector2(0f, 0f);
        imgRT.anchorMax = new Vector2(0.45f, 1f);
        imgRT.offsetMin = new Vector2(6f, 6f);
        imgRT.offsetMax = new Vector2(-3f, -6f);
        var imgComp = imgGO.GetComponent<Image>();
        imgComp.color = Color.white;
        imgComp.preserveAspect = true;
        imgComp.raycastTarget = false;
        imgGO.SetActive(false); // 스프라이트 없으면 숨김

        // 오른쪽: 텍스트
        var lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(item.transform, false);
        var lRT  = lbl.GetComponent<RectTransform>();
        lRT.anchorMin = new Vector2(0.45f, 0f);
        lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(4f, 6f);
        lRT.offsetMax = new Vector2(-6f, -6f);
        var tmp  = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = "태스크 설명"; tmp.fontSize = 13; tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        if (_font != null) tmp.font = _font;

        var prefab = PrefabUtility.SaveAsPrefabAsset(item, path);
        Object.DestroyImmediate(item);
        return prefab;
    }

    static GameObject MakeTaskItemPrefab()
    {
        const string dir  = "Assets/Prefabs";
        const string path = dir + "/TaskItem.prefab";
        if (!Directory.Exists(dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        var item = new GameObject("TaskItem", typeof(RectTransform));
        item.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 28f);
        item.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        // 왼쪽: 태스크 스프라이트 이미지 (32px 고정폭)
        var imgGO = new GameObject("TaskImage", typeof(RectTransform), typeof(Image));
        imgGO.transform.SetParent(item.transform, false);
        var imgRT = imgGO.GetComponent<RectTransform>();
        imgRT.anchorMin = new Vector2(0f, 0f);
        imgRT.anchorMax = new Vector2(0f, 1f);
        imgRT.offsetMin = new Vector2(3f, 3f);
        imgRT.offsetMax = new Vector2(29f, -3f); // 26px 너비
        var imgComp = imgGO.GetComponent<Image>();
        imgComp.color = Color.white;
        imgComp.preserveAspect = true;
        imgComp.raycastTarget = false;
        imgGO.SetActive(false); // 스프라이트 없으면 숨김

        // 오른쪽: 텍스트 (스프라이트 있으면 왼쪽 32px 비움, 없으면 전체 폭)
        var lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(item.transform, false);
        var lRT  = lbl.GetComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(5f, 0f); lRT.offsetMax = new Vector2(-5f, 0f);
        var tmp  = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = "과제"; tmp.fontSize = 11; tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        if (_font != null) tmp.font = _font;

        var prefab = PrefabUtility.SaveAsPrefabAsset(item, path);
        Object.DestroyImmediate(item);
        return prefab;
    }
}
