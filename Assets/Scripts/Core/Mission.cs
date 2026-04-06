using UnityEngine;

/// <summary>
/// 미션 하나의 데이터. 스프라이트 이름 규칙: Mission_XYZ
/// X, Y, Z = 비-함장 플레이어 3명의 태스크 개수.
/// 예) Mission_123 → 1명은 1개, 1명은 2개, 1명은 3개 (총 6개 태스크)
/// </summary>
[System.Serializable]
public class Mission
{
    public string id;       // "111", "234" 등
    public Sprite sprite;

    // 비-함장 플레이어 3명에게 배정할 태스크 개수 (크기 3 고정)
    public int[] taskCounts = new int[3];

    public int TotalTaskCount
    {
        get
        {
            int sum = 0;
            foreach (int c in taskCounts) sum += c;
            return sum;
        }
    }

    // 총 태스크 수가 곧 난이도 (3 = 가장 쉬움, 12 = 가장 어려움)
    public int Difficulty => TotalTaskCount;
}
