using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MissionDatabase))]
public class MissionDatabaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(8);

        if (GUILayout.Button("스프라이트 이름으로 미션 자동 생성", GUILayout.Height(30)))
        {
            ((MissionDatabase)target).AutoPopulateFromSprites();
            AssetDatabase.SaveAssets();
        }
    }
}
