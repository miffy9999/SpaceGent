using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Linq;

/// <summary>
/// SeaAI/Create Game UI 메뉴로 Canvas 전체를 자동 생성한다.
/// 폰트: 배달의민족 주아체 (BMJUA_ttf SDF)
/// </summary>
public static class CreateGameUIEditor
{
    const float REF_W = 1920f;
    const float REF_H = 1080f;
    const float TOP_H = 52f;
    const float RIGHT_W = 230f;
    const float BOT_H = 190f;

    const string FONT_PATH = "Assets/TextMesh Pro/Fonts/BMJUA_ttf SDF.asset";

    static readonly Color BgDark = new Color(0.05f, 0.07f, 0.12f, 0.90f);
    static readonly Color BgMid = new Color(0.09f, 0.12f, 0.18f, 0.88f);
    static readonly Color BgWhite = new Color(1.00f, 1.00f, 1.00f, 0.92f);
    static readonly Color ColComm = new Color(0.30f, 0.70f, 1.00f, 1.00f);
    static readonly Color ColSonar = new Color(1.00f, 0.80f, 0.20f, 1.00f);
    static readonly Color ColTxt = new Color(0.92f, 0.95f, 1.00f, 1.00f);

    static TMP_FontAsset _font;

    [MenuItem("SeaAI/Create Game UI")]
    public static void Create()
    {
        // ── 카드 스프라이트 자동 할당 ────────────────────────────────
        PopulateCardSprites();

        // ── 폰트 로드 ───────────────────────────────────────────────
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
        if (_font == null)
            Debug.LogWarning($"[SeaAI] 폰트를 찾지 못했습니다: {FONT_PATH}. 기본 폰트로 대체됩니다.");
        else
            Debug.Log($"[SeaAI] 폰트 로드 완료: {_font.name}");

        // ── Canvas ─────────────────────────────────────────────────
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var go = new GameObject("GameCanvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            go.AddComponent<GraphicRaycaster>();
        }

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REF_W, REF_H);
        scaler.matchWidthOrHeight = 0.5f;

        var ui = canvas.GetComponent<GameUIManager>();
        if (ui == null) ui = canvas.gameObject.AddComponent<GameUIManager>();

        Transform R = canvas.transform;

        // ── 1. 상단 바 ─────────────────────────────────────────────
        var topBar = Panel(R, "TopBar", BgDark,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            Vector2.zero, new Vector2(0, -TOP_H));

        ui.leadSuitText = Txt(topBar.transform, "LeadSuitText", "선 색상: -", 15, ColTxt,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(0, 0), new Vector2(0.22f, 1), new Vector2(12, 0), Vector2.zero);

        ui.turnText = Txt(topBar.transform, "TurnText", "[ 플레이어 차례 ]", 18, Color.white,
            TextAlignmentOptions.Center,
            new Vector2(0.22f, 0), new Vector2(0.78f, 1), Vector2.zero, Vector2.zero);

        ui.trickCountText = Txt(topBar.transform, "TrickCountText", "Trick 1 / 10", 15, ColTxt,
            TextAlignmentOptions.MidlineRight,
            new Vector2(0.78f, 0), new Vector2(1, 1), Vector2.zero, new Vector2(-12, 0));

        // ── 2. 우측 플레이어 패널 ──────────────────────────────────
        var rightBg = Panel(R, "RightPlayerPanel", BgDark,
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f),
            new Vector2(-RIGHT_W, TOP_H), Vector2.zero);

        string[] names = { "나 (인간)", "플레이어 1", "플레이어 2", "플레이어 3" };
        for (int i = 0; i < 4; i++)
        {
            float yMax = 1f - (float)i / 4f;
            float yMin = 1f - (float)(i + 1) / 4f;

            var slot = Panel(rightBg.transform, $"PlayerSlot{i}", BgMid,
                new Vector2(0, yMin), new Vector2(1, yMax), new Vector2(0.5f, 0.5f),
                new Vector2(5, 5), new Vector2(-5, -5));

            Txt(slot.transform, "NameText", names[i], 13, ColTxt,
                TextAlignmentOptions.Center,
                new Vector2(0, 0.55f), new Vector2(1, 1),
                new Vector2(6, 2), new Vector2(-6, -2));

            // 토큰 행
            var row = new GameObject("TokenRow", typeof(RectTransform));
            row.transform.SetParent(slot.transform, false);
            var rowRT = row.GetComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0, 0);
            rowRT.anchorMax = new Vector2(1, 0.55f);
            rowRT.offsetMin = new Vector2(6, 4);
            rowRT.offsetMax = new Vector2(-6, -4);
            var hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 5;
            hg.childAlignment = TextAnchor.MiddleCenter;
            hg.childForceExpandWidth = false;
            hg.childForceExpandHeight = false;
            hg.padding = new RectOffset(4, 4, 2, 2);

            // 통신 토큰
            var commGO = Icon(row.transform, "CommIcon", ColComm, 22);
            ui.commTokenIcons[i] = commGO.GetComponent<Image>();
            Label(row.transform, "CommLbl", "통신", 10, 28);

            Divider(row.transform);

            // 소나 토큰
            var sonarGO = Icon(row.transform, "SonarIcon", ColSonar, 22);
            ui.sonarTokenIcons[i] = sonarGO.GetComponent<Image>();
            Label(row.transform, "SonarLbl", "소나", 10, 28);

            // 턴 하이라이트 (슬롯 전체를 덮는 투명 오버레이)
            var hl = new GameObject("TurnHighlight", typeof(RectTransform), typeof(Image));
            hl.transform.SetParent(slot.transform, false);
            var hlRT = hl.GetComponent<RectTransform>();
            hlRT.anchorMin = Vector2.zero; hlRT.anchorMax = Vector2.one;
            hlRT.offsetMin = Vector2.zero; hlRT.offsetMax = Vector2.zero;
            var hlImg = hl.GetComponent<Image>();
            hlImg.color = Color.clear;
            hlImg.raycastTarget = false;
            ui.playerTurnHighlights[i] = hlImg;
        }

        // ── 3. 중앙 과제 패널 ─────────────────────────────────────
        var mPanel = Panel(R, "MissionPanel", BgWhite,
            new Vector2(0.30f, 0.25f), new Vector2(0.72f, 0.82f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);

        Txt(mPanel.transform, "TaskTitle", "남아있는 과제", 17, Color.black,
            TextAlignmentOptions.Center,
            new Vector2(0, 0.82f), new Vector2(1, 1), new Vector2(0, 2), new Vector2(0, -2));

        var mImgGO = new GameObject("MissionImage", typeof(RectTransform), typeof(Image));
        mImgGO.transform.SetParent(mPanel.transform, false);
        var mRT = mImgGO.GetComponent<RectTransform>();
        mRT.anchorMin = new Vector2(0.05f, 0.18f);
        mRT.anchorMax = new Vector2(0.95f, 0.82f);
        mRT.offsetMin = Vector2.zero;
        mRT.offsetMax = Vector2.zero;
        ui.missionImage = mImgGO.GetComponent<Image>();
        ui.missionImage.color = new Color(0.95f, 0.78f, 0.18f, 0.85f);

        ui.missionIdText = Txt(mPanel.transform, "MissionIdText", "미션 111", 13, Color.gray,
            TextAlignmentOptions.Center,
            new Vector2(0, 0), new Vector2(1, 0.18f), Vector2.zero, Vector2.zero);

        // ── 4. 내 태스크 목록 (좌상단) ───────────────────────────
        var taskListGO = new GameObject("TaskListParent", typeof(RectTransform));
        taskListGO.transform.SetParent(R, false);
        var tlRT = taskListGO.GetComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0f, 0.85f);
        tlRT.anchorMax = new Vector2(0.20f, 0.98f);
        tlRT.offsetMin = new Vector2(12, 0);
        tlRT.offsetMax = new Vector2(-6, 0);
        var vl = taskListGO.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 4;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.padding = new RectOffset(0, 0, 4, 4);
        ui.taskListParent = tlRT;

        // ── 5. TaskItem 프리팹 ────────────────────────────────────
        ui.taskItemPrefab = MakeTaskItemPrefab();

        // ── 5-b. 손패 카드 영역 + HandCard 프리팹 ────────────────
        var handArea = new GameObject("HandCardParent", typeof(RectTransform));
        handArea.transform.SetParent(R, false);
        var handRT = handArea.GetComponent<RectTransform>();
        handRT.anchorMin = new Vector2(0, 0);
        handRT.anchorMax = new Vector2(1 - RIGHT_W / REF_W, 0);
        handRT.offsetMin = new Vector2(20, 12);
        handRT.offsetMax = new Vector2(-20, BOT_H - 12);
        var handHG = handArea.AddComponent<HorizontalLayoutGroup>();
        handHG.spacing = 8;
        handHG.childAlignment = TextAnchor.MiddleCenter;
        handHG.childForceExpandWidth = false;
        handHG.childForceExpandHeight = true;
        handHG.padding = new RectOffset(10, 10, 5, 5);
        ui.handCardParent = handRT;
        ui.handCardPrefab = MakeHandCardPrefab();

        // ── 6. 태스크 선택 패널 (BGA 방식 오버레이) ──────────────
        var taskSelPanel = Panel(R, "TaskSelectionPanel", new Color(0, 0, 0, 0.82f),
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);

        // 제목
        ui.taskSelectionTitle = Txt(taskSelPanel.transform, "TaskSelTitle",
            "태스크를 선택하세요", 26, Color.white, TextAlignmentOptions.Center,
            new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.88f),
            Vector2.zero, Vector2.zero);

        // 안내 부제목
        Txt(taskSelPanel.transform, "TaskSelSubtitle",
            "아래 카드 중 하나를 선택하여 태스크를 가져가세요", 15,
            new Color(0.8f, 0.85f, 0.9f, 0.85f), TextAlignmentOptions.Center,
            new Vector2(0.1f, 0.64f), new Vector2(0.9f, 0.74f),
            Vector2.zero, Vector2.zero);

        // 태스크 풀 컨테이너 (GridLayout)
        var poolContainerGO = new GameObject("TaskPoolContainer", typeof(RectTransform));
        poolContainerGO.transform.SetParent(taskSelPanel.transform, false);
        var poolRT = poolContainerGO.GetComponent<RectTransform>();
        poolRT.anchorMin = new Vector2(0.08f, 0.12f);
        poolRT.anchorMax = new Vector2(0.92f, 0.64f);
        poolRT.offsetMin = Vector2.zero;
        poolRT.offsetMax = Vector2.zero;
        var grid = poolContainerGO.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 80);
        grid.spacing = new Vector2(14, 14);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.Flexible;
        ui.taskPoolContainer = poolRT;

        ui.taskPoolItemPrefab = MakeTaskPoolItemPrefab();
        taskSelPanel.SetActive(false);
        ui.taskSelectionPanel = taskSelPanel;

        // ── 7. 결과 패널 ─────────────────────────────────────────
        var resultBg = Panel(R, "ResultPanel", new Color(0, 0, 0, 0.75f),
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);

        var resultCard = Panel(resultBg.transform, "ResultCard", new Color(0.1f, 0.12f, 0.18f, 0.98f),
            new Vector2(0.25f, 0.38f), new Vector2(0.75f, 0.62f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);

        ui.resultText = Txt(resultCard.transform, "ResultText", "미션 성공!", 44,
            new Color(0.2f, 0.9f, 0.35f, 1f), TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        ui.resultPanel = resultBg;
        resultBg.SetActive(false);

        // ── 7. 하단 손패 힌트 ────────────────────────────────────
        var botHint = Panel(R, "BottomHandHint", new Color(0.08f, 0.10f, 0.16f, 0.45f),
            new Vector2(0, 0), new Vector2(1 - RIGHT_W / REF_W, 0), new Vector2(0.5f, 0),
            Vector2.zero, new Vector2(0, BOT_H));

        Txt(botHint.transform, "HintLabel", "손패 카드 영역",
            13, new Color(1, 1, 1, 0.3f), TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── GameManager 연결 ─────────────────────────────────────
        var gm = Object.FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            var so = new SerializedObject(gm);
            var propUI = so.FindProperty("uiManager");
            if (propUI != null) { propUI.objectReferenceValue = ui; }
            var propMapping = so.FindProperty("cardSpriteMapping");
            if (propMapping != null)
            {
                var mapping = AssetDatabase.LoadAssetAtPath<CardSpriteMapping>("Assets/Prefabs/CardSpriteMapping.asset");
                propMapping.objectReferenceValue = mapping;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(gm);
            Debug.Log("[SeaAI] GameManager.uiManager / cardSpriteMapping 자동 연결 완료");
        }
        else
        {
            Debug.LogWarning("[SeaAI] GameManager를 씬에서 찾지 못했습니다. 수동으로 연결해주세요.");
        }

        Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Create SeaAI Game UI");
        EditorUtility.SetDirty(canvas.gameObject);
        Selection.activeGameObject = canvas.gameObject;
        Debug.Log("[SeaAI] Game UI 생성 완료!");
    }

    // ── HandCard 프리팹 ──────────────────────────────────────────────
    static GameObject MakeHandCardPrefab()
    {
        const string dir = "Assets/Prefabs";
        const string path = dir + "/HandCard.prefab";
        if (!Directory.Exists(dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        // 루트
        var card = new GameObject("HandCard", typeof(RectTransform));
        card.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 120);

        // 배경 (색상은 런타임에 HandCardUI.Setup에서 설정)
        var bg = card.AddComponent<Image>();
        bg.color = Color.white;

        // 버튼
        var btn = card.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1f, 1f, 0.7f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        btn.colors = colors;

        // HandCardUI 컴포넌트
        var hui = card.AddComponent<HandCardUI>();
        hui.background = bg;

        // 카드 앞면 이미지 (스프라이트 모드)
        var faceGO = new GameObject("CardFaceImage", typeof(RectTransform), typeof(Image));
        faceGO.transform.SetParent(card.transform, false);
        var faceRT = faceGO.GetComponent<RectTransform>();
        faceRT.anchorMin = Vector2.zero; faceRT.anchorMax = Vector2.one;
        faceRT.offsetMin = Vector2.zero; faceRT.offsetMax = Vector2.zero;
        var faceImg = faceGO.GetComponent<Image>();
        faceImg.color = Color.white;
        faceImg.preserveAspect = false;
        faceGO.SetActive(false); // 런타임에 스프라이트 있을 때만 활성화
        hui.cardFaceImage = faceImg;

        // 숫자 텍스트 (중앙 큰 숫자)
        var valGO = new GameObject("ValueText", typeof(RectTransform), typeof(TextMeshProUGUI));
        valGO.transform.SetParent(card.transform, false);
        var valRT = valGO.GetComponent<RectTransform>();
        valRT.anchorMin = new Vector2(0, 0.3f); valRT.anchorMax = Vector2.one;
        valRT.offsetMin = Vector2.zero; valRT.offsetMax = Vector2.zero;
        var valTmp = valGO.GetComponent<TextMeshProUGUI>();
        valTmp.text = "1";
        valTmp.fontSize = 32;
        valTmp.fontStyle = FontStyles.Bold;
        valTmp.color = Color.white;
        valTmp.alignment = TextAlignmentOptions.Center;
        if (_font != null) valTmp.font = _font;

        // 수트 텍스트 (하단 작은 글씨)
        var suitGO = new GameObject("SuitText", typeof(RectTransform), typeof(TextMeshProUGUI));
        suitGO.transform.SetParent(card.transform, false);
        var suitRT = suitGO.GetComponent<RectTransform>();
        suitRT.anchorMin = Vector2.zero; suitRT.anchorMax = new Vector2(1, 0.35f);
        suitRT.offsetMin = Vector2.zero; suitRT.offsetMax = Vector2.zero;
        var suitTmp = suitGO.GetComponent<TextMeshProUGUI>();
        suitTmp.text = "Y";
        suitTmp.fontSize = 13;
        suitTmp.color = new Color(1, 1, 1, 0.8f);
        suitTmp.alignment = TextAlignmentOptions.Center;
        if (_font != null) suitTmp.font = _font;

        // 선택 테두리 (기본 비활성화)
        var border = new GameObject("SelectedBorder", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(card.transform, false);
        var borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-3, -3); borderRT.offsetMax = new Vector2(3, 3);
        var borderImg = border.GetComponent<Image>();
        borderImg.color = new Color(1f, 0.95f, 0.1f, 0.9f);
        borderImg.raycastTarget = false;
        border.SetActive(false);

        // HandCardUI 필드 연결
        hui.selectedBorder = borderImg;
        hui.valueText = valTmp;
        hui.suitText = suitTmp;

        var prefab = PrefabUtility.SaveAsPrefabAsset(card, path);
        Object.DestroyImmediate(card);
        return prefab;
    }

    // ── TaskPoolItem 프리팹 (태스크 선택 버튼) ──────────────────────
    static GameObject MakeTaskPoolItemPrefab()
    {
        const string dir = "Assets/Prefabs";
        const string path = dir + "/TaskPoolItem.prefab";
        if (!Directory.Exists(dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        var item = new GameObject("TaskPoolItem", typeof(RectTransform));
        item.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 80);

        // 배경 (색상은 런타임에 타입별로 설정)
        var bg = item.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.5f, 1f, 0.85f);

        // 버튼
        var btn = item.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1f, 1f, 0.7f, 1f);
        colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        btn.colors = colors;

        // 라벨 (태스크 설명)
        var lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(item.transform, false);
        var lRT = lbl.GetComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(10, 6); lRT.offsetMax = new Vector2(-10, -6);
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = "태스크 설명";
        tmp.fontSize = 15;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        if (_font != null) tmp.font = _font;

        var prefab = PrefabUtility.SaveAsPrefabAsset(item, path);
        Object.DestroyImmediate(item);
        return prefab;
    }

    // ── TaskItem 프리팹 ──────────────────────────────────────────────
    static GameObject MakeTaskItemPrefab()
    {
        const string dir = "Assets/Prefabs";
        const string path = dir + "/TaskItem.prefab";
        if (!Directory.Exists(dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

        var item = new GameObject("TaskItem", typeof(RectTransform));
        item.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 28);
        item.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

        var lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lbl.transform.SetParent(item.transform, false);
        var lRT = lbl.GetComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = new Vector2(6, 0); lRT.offsetMax = new Vector2(-6, 0);
        var tmp = lbl.GetComponent<TextMeshProUGUI>();
        tmp.text = "태스크";
        tmp.fontSize = 13;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        if (_font != null) tmp.font = _font;

        var prefab = PrefabUtility.SaveAsPrefabAsset(item, path);
        Object.DestroyImmediate(item);
        return prefab;
    }

    // ── 헬퍼: 패널 ───────────────────────────────────────────────────
    static GameObject Panel(Transform parent, string name, Color color,
        Vector2 ancMin, Vector2 ancMax, Vector2 pivot,
        Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax; rt.pivot = pivot;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        go.GetComponent<Image>().color = color;
        return go;
    }

    // ── 헬퍼: 텍스트 (폰트 자동 적용) ──────────────────────────────
    static TMP_Text Txt(Transform parent, string name, string text,
        float size, Color color, TextAlignmentOptions align,
        Vector2 ancMin, Vector2 ancMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        if (_font != null) tmp.font = _font;  // 배달의민족 주아체 적용
        return tmp;
    }

    // ── 헬퍼: 레이아웃 아이콘 ────────────────────────────────────────
    static GameObject Icon(Transform parent, string name, Color color, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size; le.preferredHeight = size;
        return go;
    }

    // ── 헬퍼: 레이아웃 텍스트 라벨 ──────────────────────────────────
    static void Label(Transform parent, string name, string text, float fontSize, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = new Color(0.8f, 0.85f, 0.9f, 0.9f);
        tmp.alignment = TextAlignmentOptions.Midline;
        if (_font != null) tmp.font = _font;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width; le.preferredHeight = 22;
    }

    // ── 헬퍼: 구분선 ─────────────────────────────────────────────────
    static void Divider(Transform parent)
    {
        var go = new GameObject("Sep", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = "|";
        tmp.fontSize = 12;
        tmp.color = new Color(1, 1, 1, 0.25f);
        tmp.alignment = TextAlignmentOptions.Midline;
        if (_font != null) tmp.font = _font;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = 12; le.preferredHeight = 22;
    }

    // ── 카드 스프라이트 자동 할당 ────────────────────────────────────
    [MenuItem("SeaAI/Populate Card Sprites")]
    public static void PopulateCardSprites()
    {
        const string mappingPath = "Assets/Prefabs/CardSpriteMapping.asset";
        const string sheetPath   = "Assets/Sprites/Card_Pack.png";

        var mapping = AssetDatabase.LoadAssetAtPath<CardSpriteMapping>(mappingPath);
        if (mapping == null) { Debug.LogError($"[SeaAI] CardSpriteMapping.asset을 찾을 수 없습니다: {mappingPath}"); return; }

        // Card_Pack.png의 모든 서브-스프라이트를 이름으로 딕셔너리화
        var allSprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
            .OfType<Sprite>()
            .ToDictionary(s => s.name);

        if (allSprites.Count == 0)
        {
            Debug.LogError($"[SeaAI] {sheetPath} 에서 스프라이트를 찾지 못했습니다. Sprite Mode가 Multiple인지 확인하세요.");
            return;
        }

        mapping.entries.Clear();

        // 일반 수트 (Yellow, Blue, Green, Pink) 1~9
        var suitNames = new (Card.Suit suit, string name)[]
        {
            (Card.Suit.Yellow,    "Yellow"),
            (Card.Suit.Blue,      "Blue"),
            (Card.Suit.Green,     "Green"),
            (Card.Suit.Pink,      "Pink"),
        };

        foreach (var (suit, name) in suitNames)
        {
            for (int v = 1; v <= 9; v++)
            {
                string key = $"{name}_{v}";
                mapping.entries.Add(new CardSpriteMapping.CardSpriteEntry
                {
                    suit   = suit,
                    value  = v,
                    sprite = allSprites.TryGetValue(key, out var s) ? s : null
                });
            }
        }

        // 잠수함 수트 1~4
        for (int v = 1; v <= 4; v++)
        {
            string key = $"Submarine_{v}";
            mapping.entries.Add(new CardSpriteMapping.CardSpriteEntry
            {
                suit   = Card.Suit.Submarine,
                value  = v,
                sprite = allSprites.TryGetValue(key, out var s) ? s : null
            });
        }

        // 카드 뒷면
        if (allSprites.TryGetValue("Card_Back", out var backSprite))
            mapping.cardBack = backSprite;

        EditorUtility.SetDirty(mapping);
        AssetDatabase.SaveAssets();

        int assigned = mapping.entries.Count(e => e.sprite != null);
        Debug.Log($"[SeaAI] 카드 스프라이트 할당 완료: {assigned}/{mapping.entries.Count}장");
    }
}
