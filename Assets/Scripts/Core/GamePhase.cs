/// <summary>
/// 게임 진행 단계 (The Crew: The Quest for Planet Nine 기준)
/// </summary>
public enum GamePhase
{
    Setup,           // 카드 배분 중
    TaskSelection,   // 태스크(임무) 선택 중
    DistressSignal,  // 조난신호 결정 단계 (첫 트릭 전, 선택 사항)
    Playing,         // 트릭 진행 중
    Result           // 미션 성공/실패 결과
}
