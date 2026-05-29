using UnityEngine;
using UnityEditor;

/// <summary>
/// CardSpriteMapping 인스펙터에 "슬롯 자동 생성" 버튼 추가.
/// Tools > Populate Card Sprite Mapping 메뉴로도 실행 가능.
/// </summary>
[CustomEditor(typeof(CardSpriteMapping))]
public class CardSpriteMappingEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(8);

        if (GUILayout.Button("슬롯 자동 생성 (40장 + 로켓 4장)", GUILayout.Height(30)))
        {
            CardSpriteMapping mapping = (CardSpriteMapping)target;
            mapping.PopulateSlots();
            EditorUtility.SetDirty(mapping);
            AssetDatabase.SaveAssets();
            Debug.Log("[CardSpriteMapping] 44개 슬롯 생성 완료. 스프라이트를 드래그해서 채우세요.");
        }
    }

    [MenuItem("Tools/Populate Card Sprite Mapping")]
    public static void PopulateFromMenu()
    {
        string[] guids = AssetDatabase.FindAssets("t:CardSpriteMapping");
        if (guids.Length == 0)
        {
            Debug.LogWarning("CardSpriteMapping 에셋을 찾을 수 없습니다. " +
                             "Project 창에서 Create > SpaceCrew > Card Sprite Mapping 으로 먼저 생성하세요.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        CardSpriteMapping mapping = AssetDatabase.LoadAssetAtPath<CardSpriteMapping>(path);
        mapping.PopulateSlots();
        EditorUtility.SetDirty(mapping);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CardSpriteMapping] {path} 슬롯 생성 완료.");
    }
}
