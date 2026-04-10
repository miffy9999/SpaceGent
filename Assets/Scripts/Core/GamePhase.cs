/// <summary>
/// 게임 진행 단계 (BGA The Crew: Deep Sea 기준)
/// </summary>
public enum GamePhase
{
    Setup,          // 카드 배분 중
    TaskSelection,  // 인간 플레이어가 과제 선택 중
    Playing,        // 트릭 진행 중
    Result          // 미션 성공/실패 결과
}
