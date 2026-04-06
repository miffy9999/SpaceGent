// using UnityEngine;
// using UnityEditor;
// using UnityEngine.UI;
// using TMPro;
// using System.IO;

// /// <summary>
// /// SeaAI/Create Game UI 메뉴로 Canvas 전체 레이아웃을 자동 생성한다.
// /// </summary>
// public static class CreateGameUIEditor
// {
//     // ── 레이아웃 상수 ───────────────────────────────────────────────
//     const float REF_W      = 1920f;
//     const float REF_H      = 1080f;
//     const float TOP_H      = 52f;
//     const float RIGHT_W    = 220f;
//     const float BOTTOM_H   = 190f;

//     static readonly Color PanelDark   = new Color(0.06f, 0.08f, 0.13f, 0.88f);
//     static readonly Color PanelMid    = new Color(0.10f, 0.13f, 0.20f, 0.85f);
//     static readonly Color PanelLight  = new Color(1.00f, 1.00f, 1.00f, 0.93f);
//     static readonly Color ColComm     = new Color(0.30f, 0.70f, 1.00f, 1.00f);
//     static readonly Color ColSonar    = new Color(1.00f, 0.80f, 0.20f, 1.00f);
//     static readonly Color ColText     = new Color(0.92f, 0.95f, 1.00f, 1.00f);

//     // ────────────────────────────────────────────────────────────────
//     [MenuItem("SeaAI/Create Game UI")]
//     public static void Create()
//     {
//         // ── Canvas ──────────────────────────────────────────────────
//         Canvas canvas = Object.FindObjectOfType<Canvas>();
//         if (canvas == null)
//         {
//             var go = new GameObject("GameCanvas");
//             canvas = go.AddComponent<Canvas>();
//             canvas.renderMode = RenderMode.ScreenSpaceOverlay;
//             canvas.sortingOrder = 10;
//             go.AddComponent<GraphicRaycaster>();
//         }

//         var scaler = canvas.GetComponent<CanvasScaler>();
//         if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
//         scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
//         scaler.referenceResolution  = new Vector2(REF_W, REF_H);
//         scaler.matchWidthOrHeight   = 0.5f;

//         var uiManager = canvas.GetComponent<GameUIManager>();
//         if (uiManager == null) uiManager = canvas.gameObject.AddComponent<GameUIManager>();

//         Transform root = canvas.transform;

//         // ── 1. 상단 바 ──────────────────────────────────────────────
//         var topBar = MakePanel(root, "TopBar", PanelDark,
//             ancMin: new Vector2(0, 1), ancMax: new Vector2(1, 1),
//             pivot:  new Vector2(0.5f, 1));
//         SetRect(topBar, Vector2.zero, new Vector2(0, -TOP_H));

//         uiManager.leadSuitText  = MakeText(topBar.transform, "LeadSuitText",
//             "선 색상: -", 15, ColText, TextAlignmentOptions.MidlineLeft,
//             ancMin: new Vector2(0,    0), ancMax: new Vector2(0.22f, 1),
//             offsetMin: new Vector2(12, 0), offsetMax: Vector2.zero);

//         uiManager.turnText = MakeText(topBar.transform, "TurnText",
//             "▶ 플레이어 차례", 17, Color.white, TextAlignmentOptions.Center,
//             ancMin: new Vector2(0.22f, 0), ancMax: new Vector2(0.78f, 1),
//             offsetMin: Vector2.zero, offsetMax: Vector2.zero);

//         uiManager.trickCountText = MakeText(topBar.transform, "TrickCountText",
//             "Trick 1 / 10", 15, ColText, TextAlignmentOptions.MidlineRight,
//             ancMin: new Vector2(0.78f, 0), ancMax: new Vector2(1, 1),
//             offsetMin: Vector2.zero, offsetMax: new Vector2(-12, 0));

//         // ── 2. 우측 플레이어 패널 ───────────────────────────────────
//         var rightPanel = MakePanel(root, "RightPlayerPanel", PanelDark,
//             ancMin: new Vector2(1, 0), ancMax: new Vector2(1, 1),
//             pivot:  new Vector2(1, 0.5f));
//         SetRect(rightPanel, new Vector2(-RIGHT_W, TOP_H), new Vector2(0, 0));

//         string[] pNames = { "나 (Human)", "Player 1", "Player 2", "Player 3" };
//         float slotH = (REF_H - TOP_H - BOTTOM_H) / 4f;

//         for (int i = 0; i < 4; i++)
//         {
//             float yMax = 1f - (float)i       / 4f;
//             float yMin = 1f - (float)(i + 1) / 4f;

//             var slot = MakePanel(rightPanel.transform, $"PlayerSlot{i}", PanelMid,
//                 ancMin: new Vector2(0, yMin), ancMax: new Vector2(1, yMax),
//                 pivot:  new Vector2(0.5f, 0.5f));
//             SetRect(slot, new Vector2(4, 4), new Vector2(-4, -4));

//             // 이름
//             MakeText(slot.transform, "NameText", pNames[i], 13, ColText,
//                 TextAlignmentOptions.Center,
//                 ancMin: new Vector2(0, 0.55f), ancMax: new Vector2(1, 1),
//                 offsetMin: new Vector2(6, 2), offsetMax: new Vector2(-6, -2));

//             // 토큰 행
//             var tokenRow = new GameObject("TokenRow", typeof(RectTransform)).transform;
//             tokenRow.SetParent(slot.transform, false);
//             var rowRT = (RectTransform)tokenRow;
//             rowRT.anchorMin = new Vector2(0, 0);
//             rowRT.anchorMax = new Vector2(1, 0.55f);
//             rowRT.offsetMin = new Vector2(6,  4);
//             rowRT.offsetMax = new Vector2(-6, -4);

//             var hg = tokenRow.gameObject.AddComponent<HorizontalLayoutGroup>();
//             hg.spacing             = 6;
//             hg.childAlignment      = TextAnchor.MiddleCenter;
//             hg.childForceExpandWidth  = false;
//             hg.childForceExpandHeight = false;
//             hg.padding = new RectOffset(4, 4, 2, 2);

//             // 통신 토큰 아이콘
//             var commIcon = MakeIconInLayout(tokenRow, "CommIcon", ColComm, 22);
//             uiManager.commTokenIcons[i] = commIcon.GetComponent<Image>();
//             MakeLabelInLayout(tokenRow, "CommLabel", "통신", 10);

//             // 구분선
//             MakeLabelInLayout(tokenRow, "Sep", "|", 12);

//             // 소나 토큰 아이콘
//             var sonarIcon = MakeIconInLayout(tokenRow, "SonarIcon", ColSonar, 22);
//             uiManager.sonarTokenIcons[i] = sonarIcon.GetComponent<Image>();
//             MakeLabelInLayout(tokenRow, "SonarLabel", "소나", 10);
//         }

//         // ── 3. 중앙 미션/과제 패널 ──────────────────────────────────
//         var missionPanel = MakePanel(root, "MissionPanel", PanelLight,
//             ancMin: new Vector2(0.28f, 0.28f), ancMax: new Vector2(0.72f, 0.82f),
//             pivot:  new Vector2(0.5f,  0.5f));
//         SetRect(missionPanel, Vector2.zero, Vector2.zero);

//         MakeText(missionPanel.transform, "TaskPanelTitle", "남아있는 과제",
//             16, Color.black, TextAlignmentOptions.Center,
//             ancMin: new Vector2(0, 0.82f), ancMax: new Vector2(1, 1),
//             offsetMin: new Vector2(0, 2), offsetMax: new Vector2(0, -2));

//         // 미션 이미지
//         var missionImgGO = new GameObject("MissionImage", typeof(RectTransform), typeof(Image));
//         missionImgGO.transform.SetParent(missionPanel.transform, false);
//         var miRT = missionImgGO.GetComponent<RectTransform>();
//         miRT.anchorMin = new Vector2(0.05f, 0.18f);
//         miRT.anchorMax = new Vector2(0.95f, 0.82f);
//         miRT.offsetMin = Vector2.zero;
//         miRT.offsetMax = Vector2.zero;
//         uiManager.missionImage = missionImgGO.GetComponent<Image>();
//         uiManager.missionImage.color = new Color(1f, 0.8f, 0.2f, 0.8f);

//         uiManager.missionIdText = MakeText(missionPanel.transform, "MissionIdText",
//             "Mission 111", 13, Color.gray, TextAlignmentOptions.Center,
//             ancMin: new Vector2(0, 0), ancMax: new Vector2(1, 0.18f),
//             offsetMin: Vector2.zero, offsetMax: Vector2.zero);

//         // ── 4. 내 태스크 목록 (좌상단) ─────────────────────────────
//         var taskListGO = new GameObject("TaskListParent", typeof(RectTransform));
//         taskListGO.transform.SetParent(root, false);
//         var tlRT = taskListGO.GetComponent<RectTransform>();
//         tlRT.anchorMin = new Vector2(0,     0.85f);
//         tlRT.anchorMax = new Vector2(0.22f, 0.98f);
//         tlRT.offsetMin = new Vector2(10, 0);
//         tlRT.offsetMax = new Vector2(-4, 0);
//         var vl = taskListGO.AddComponent<VerticalLayoutGroup>();
//         vl.spacing              = 4;
//         vl.childForceExpandWidth  = true;
//         vl.childForceExpandHeight = false;
//         vl.padding = new RectOffset(4, 4, 4, 4);
//         uiManager.taskListParent = tlRT;

//         // ── 5. TaskItem 프리팹 ──────────────────────────────────────
//         uiManager.taskItemPrefab = CreateTaskItemPrefab();

//         // ── 6. 결과 오버레이 ────────────────────────────────────────
//         var resultPanel = MakePanel(root, "ResultPanel", new Color(0, 0, 0, 0.75f),
//             ancMin: Vector2.zero, ancMax: Vector2.one,
//             pivot:  new Vector2(0.5f, 0.5f));
//         SetRect(resultPanel, Vector2.zero, Vector2.zero);

//         // 결과 카드
//         var resultCard = MakePanel(resultPanel.transform, "ResultCard", PanelLight,
//             ancMin: new Vector2(0.3f, 0.35f), ancMax: new Vector2(0.7f, 0.65f),
//             pivot:  new Vector2(0.5f, 0.5f));
//         SetRect(resultCard, Vector2.zero, Vector2.zero);

//         uiManager.resultText = MakeText(resultCard.transform, "ResultText",
//             "미션 성공!", 40, new Color(0.1f, 0.6f, 0.2f, 1f), TextAlignmentOptions.Center,
//             ancMin: Vector2.zero, ancMax: Vector2.one,
//             offsetMin: Vector2.zero, offsetMax: Vector2.zero);

//         uiManager.resultPanel = resultPanel;
//         resultPanel.SetActive(false);

//         // ── 7. 하단 손패 영역 안내 라벨 ────────────────────────────
//         var bottomLabel = MakePanel(root, "BottomHandHint", new Color(0.1f, 0.1f, 0.15f, 0.4f),
//             ancMin: new Vector2(0, 0), ancMax: new Vector2(1 - RIGHT_W / REF_W, 0),
//             pivot:  new Vector2(0.5f, 0));
//         SetRect(bottomLabel, Vector2.zero, new Vector2(0, BOTTOM_H));

//         MakeText(bottomLabel.transform, "HintText", "손패 카드가 여기에 표시됩니다",
//             13, new Color(1, 1, 1, 0.35f), TextAlignmentOptions.Center,
//             ancMin: Vector2.zero, ancMax: Vector2.one,
//             offsetMin: Vector2.zero, offsetMax: Vector2.zero);

//         // ── GameManager 연결 ─────────────────────────────────────────
//         var gm = Object.FindObjectOfType<GameManager>();
//         if (gm != null)
//         {
//             var so = new SerializedObject(gm);
//             var prop = so.FindProperty("uiManager");
//             if (prop != null) { prop.objectReferenceValue = uiManager; so.ApplyModifiedProperties(); }
//             EditorUtility.SetDirty(gm);
//         }

//         Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Create SeaAI Game UI");
//         EditorUtility.SetDirty(canvas.gameObject);
//         Selection.activeGameObject = canvas.gameObject;

//         Debug.Log("[SeaAI] UI 생성 완료. GameManager.uiManager 자동 연결됨.");
//     }

//     // ── TaskItem 프리팹 생성 ──────────────────────────────────────────
//     static GameObject CreateTaskItemPrefab()
//     {
//         const string dir  = "Assets/Prefabs";
//         const string path = dir + "/TaskItem.prefab";
//         if (!Directory.Exists(dir)) AssetDatabase.CreateFolder("Assets", "Prefabs");

//         var item = new GameObject("TaskItem", typeof(RectTransform));
//         var rt = item.GetComponent<RectTransform>();
//         rt.sizeDelta = new Vector2(180, 28);

//         var bg = item.AddComponent<Image>();
//         bg.color = new Color(0, 0, 0, 0.5f);

//         var textGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
//         textGO.transform.SetParent(item.transform, false);
//         var tRT = textGO.GetComponent<RectTransform>();
//         tRT.anchorMin = Vector2.zero;
//         tRT.anchorMax = Vector2.one;
//         tRT.offsetMin = new Vector2(6, 0);
//         tRT.offsetMax = new Vector2(-6, 0);

//         var tmp = textGO.GetComponent<TextMeshProUGUI>();
//         tmp.text      = "태스크";
//         tmp.fontSize  = 13;
//         tmp.color     = Color.white;
//         tmp.alignment = TextAlignmentOptions.MidlineLeft;

//         var prefab = PrefabUtility.SaveAsPrefabAsset(item, path);
//         Object.DestroyImmediate(item);
//         return prefab;
//     }

//     // ── 헬퍼: 패널 ───────────────────────────────────────────────────
//     static GameObject MakePanel(Transform parent, string name, Color color,
//         Vector2 ancMin, Vector2 ancMax, Vector2 pivot)
//     {
//         var go = new GameObject(name, typeof(RectTransform), typeof(Image));
//         go.transform.SetParent(parent, false);
//         var rt = go.GetComponent<RectTransform>();
//         rt.anchorMin = ancMin;
//         rt.anchorMax = ancMax;
//         rt.pivot     = pivot;
//         go.GetComponent<Image>().color = color;
//         return go;
//     }

//     // offsetMin/Max로 여백 적용
//     static void SetRect(GameObject go, Vector2 offsetMin, Vector2 offsetMax)
//     {
//         var rt = go.GetComponent<RectTransform>();
//         rt.offsetMin = offsetMin;
//         rt.offsetMax = offsetMax;
//     }

//     // ── 헬퍼: 텍스트 ─────────────────────────────────────────────────
//     static TMP_Text MakeText(Transform parent, string name, string text,
//         float fontSize, Color color, TextAlignmentOptions align,
//         Vector2 ancMin, Vector2 ancMax, Vector2 offsetMin, Vector2 offsetMax)
//     {
//         var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
//         go.transform.SetParent(parent, false);
//         var rt = go.GetComponent<RectTransform>();
//         rt.anchorMin  = ancMin;
//         rt.anchorMax  = ancMax;
//         rt.offsetMin  = offsetMin;
//         rt.offsetMax  = offsetMax;
//         var tmp = go.GetComponent<TextMeshProUGUI>();
//         tmp.text      = text;
//         tmp.fontSize  = fontSize;
//         tmp.color     = color;
//         tmp.alignment = align;
//         return tmp;
//     }

//     // ── 헬퍼: HorizontalLayout 안의 아이콘 ──────────────────────────
//     static GameObject MakeIconInLayout(Transform parent, string name, Color color, float size)
//     {
//         var go = new GameObject(name, typeof(RectTransform), typeof(Image));
//         go.transform.SetParent(parent, false);
//         go.GetComponent<Image>().color = color;
//         var le = go.AddComponent<LayoutElement>();
//         le.preferredWidth  = size;
//         le.preferredHeight = size;
//         return go;
//     }

//     static void MakeLabelInLayout(Transform parent, string name, string text, float fontSize)
//     {
//         var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
//         go.transform.SetParent(parent, false);
//         var tmp = go.GetComponent<TextMeshProUGUI>();
//         tmp.text      = text;
//         tmp.fontSize  = fontSize;
//         tmp.color     = new Color(0.8f, 0.85f, 0.9f, 0.9f);
//         tmp.alignment = TextAlignmentOptions.MidlineCenter;
//         var le = go.AddComponent<LayoutElement>();
//         le.preferredWidth  = fontSize * 2.2f;
//         le.preferredHeight = 22;
//     }
// }
