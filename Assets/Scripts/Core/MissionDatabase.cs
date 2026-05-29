using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전체 미션 목록을 보관하는 ScriptableObject.
/// Project 창 우클릭 → Create → SpaceCrew → Mission Database
/// </summary>
[CreateAssetMenu(fileName = "MissionDatabase", menuName = "SpaceCrew/Mission Database")]
public class MissionDatabase : ScriptableObject
{
    public List<Mission> missions = new List<Mission>();

    /// <summary>최대 난이도 이하의 미션 중 랜덤 선택</summary>
    public Mission GetByMaxDifficulty(int maxDifficulty)
    {
        var pool = missions.FindAll(m => m.Difficulty <= maxDifficulty);
        if (pool.Count == 0) pool = missions; // 없으면 전체에서 선택
        return pool[Random.Range(0, pool.Count)];
    }

    public Mission GetRandom()
    {
        if (missions.Count == 0) return null;
        return missions[Random.Range(0, missions.Count)];
    }

    /// <summary>
    /// 에디터 전용: 스프라이트 이름(Mission_XYZ)에서 taskCounts를 자동 파싱해 채운다.
    /// </summary>
    public void AutoPopulateFromSprites()
    {
#if UNITY_EDITOR
        missions.Clear();
        string[] spriteNames = {
            "111","112","122","123",
            "211","222","223","233","234",
            "322","333","334","344","345",
            "433","444"
        };

        foreach (string name in spriteNames)
        {
            Mission m = new Mission
            {
                id = name,
                taskCounts = new int[3]
                {
                    name[0] - '0',
                    name[1] - '0',
                    name[2] - '0'
                }
            };
            missions.Add(m);
        }
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[MissionDatabase] {missions.Count}개 미션 자동 생성 완료");
#endif
    }
}
