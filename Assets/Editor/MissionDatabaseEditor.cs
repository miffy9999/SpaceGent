using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MissionDatabase))]
public class MissionDatabaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(8);

        if (GUILayout.Button("50개 미션 코드 정의로 재생성", GUILayout.Height(34)))
        {
            var db = (MissionDatabase)target;
            // 기존 스프라이트 캐시 보존
            var oldSprites = new Dictionary<int, Sprite>();
            foreach (var m in db.missions)
                if (m.sprite != null) oldSprites[m.number] = m.sprite;

            db.missions = MissionDatabase.BuildAllMissions();

            // 스프라이트 재연결
            foreach (var m in db.missions)
                if (oldSprites.TryGetValue(m.number, out var sp))
                    m.sprite = sp;

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MissionDatabase] {db.missions.Count}개 미션 재생성 완료 (기존 스프라이트 유지)");
        }

        GUILayout.Space(4);

        if (GUILayout.Button("미션 요약 로그 출력", GUILayout.Height(26)))
        {
            var db = (MissionDatabase)target;
            foreach (var m in db.missions)
            {
                string tokens = m.orderTokensForTasks?.Length > 0
                    ? string.Join(",", System.Array.ConvertAll(m.orderTokensForTasks, t => t.ToString()))
                    : "—";
                Debug.Log($"M{m.number:00} tasks:{m.totalTaskCount} special:{m.isSpecialMission} " +
                          $"tokens:[{tokens}] rule:{m.taskRule} global:{m.globalRule} " +
                          $"dz:{m.hasDeadZone} disrupt:{m.commDisruptionTrick}");
            }
        }
    }
}
