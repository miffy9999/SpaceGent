# 스페이스 크루 리팩터링 진행 기록

> 브랜치 `trainSetting`. task 체계를 딥 씨 크루 21종 → 스페이스 크루 단일 `WinSpecificCard`로 전환하고,
> 4-에이전트 MA-POCA로 **트릭테이킹 + task 드래프트(선택)** 두 정책을 학습시키는 작업.

## 완료 (커밋됨)

### Phase 1a — WinSpecificCard 단일 task (커밋 `b68f1d4`)
- `TaskCard`: TaskType 21종/필드/팩토리 제거 → `targetCard` + `orderToken`만.
- `MissionManager`: 죽은 분기·shaping·`IsTaskTypeAllowed`·`Phase1Task` 제거. WinSpecificCard 단일 생성/평가
  (타깃 카드가 든 트릭의 승자=소유자면 완료, 아니면 즉시 실패).
- `RuleBasedHelper`: 타이밍 전략 → WinSpecificCard 협력 전략(타깃 release/회피/획득). **베이스라인 전용**.
- `MCTS(State/Search/Rollout)`: `taskType` 제거, `targetCard` 배선.
- `CrewAgent`: 관측 마지막 피처 재정의. 벡터 **257 유지**.
- `TaskSpriteMapping`/`GameUIManager`: 타깃 카드 스프라이트 단일 경로.

### Phase 1b — 학습 드래프트(task 선택) 서브시스템 (이번 커밋, 코드만 — Unity 컴파일 확인 필요)
- **드래프트 = ML 학습**: 함장(로켓4)부터 시계방향, 매 차례 **가져가기 or 패스**.
  - 패스 규칙: `R = N - (cursor % N)`, **`T < R`이면 패스 가능 / `T >= R`이면 강제 선택**. 모든 task는 반드시 배정됨.
  - `MissionManager.GiveSelectionTurn / AgentSelectTask / CanCurrentPickerPass / HumanPickTask / HumanPassTask`.
- **액션 공간 불변 [10,2,4]** 재사용 → 씬/프리팹 편집 불필요:
  - 선택 페이즈: `Branch[0]`=풀 슬롯(0~9), `Branch[1]`=0:가져가기/1:패스(가능할 때만). 페이즈별 마스킹.
- **관찰 벡터 257 불변**: 선택 페이즈엔 비어있는 '테이블 40칸'에 풀 슬롯 인코딩
  (슬롯 j: `[targetSuit/4, value/9, 내가보유, 점유]` × 10). 플래그 2칸: `[선택페이즈?]`, `[패스가능 / 내task타깃보유]`.
- **보상**: 그룹 리워드(POCA) — task 완수 +1 / 실패 -1 (Phase1_CoopSingle 라우팅 유지).
- task 개수: env `num_tasks`(기본 1), `MaxPoolSize=10` 클램프. 커리큘럼으로 1→증가 예정.
- 단일 담당자 개념(`phase1Assignee`)은 `tasks[0]` 소유자로 대표(베이스라인/관측 호환).

## 완료 (이어서)

- **Unity 컴파일 확인**: Phase 1a·1b 모두 통과 (사용자 확인).
- **씬 설정**: 4명 `BehaviorType=Default` (MA-POCA) — 사용자 설정 완료.
- **config/trainer_config.yaml**: `trainer_type: poca`로 재작성, 커리큘럼을 `num_tasks` 1→2→3→4로 교체.
- **env/stat 정리**: `num_tasks` 도입, 구 파라미터(`win_target`/`task_type`/`fixed_assignee`) 코드에서 제거됨.
  stat 태그는 `coop/success_by_{assignee|helper}`, `hfsm/{throw|win|playtarget}_rate`로 정리됨.
- **README**: task 단일 타입·드래프트 학습·MA-POCA·관찰/액션/커리큘럼 섹션 갱신.

## 다음 할 일 (TODO)

1. **학습 실행 검증**: `mlagents-learn config/trainer_config.yaml --run-id=spacegent_v1` (Unity Play 후).
   Stage1(num_tasks=1) 그룹 평균 보상이 오르는지, 드래프트에서 take/pass 분포가 의미있게 갈리는지 확인.
2. **(선택) 선택 페이즈 watchdog**: 트레이너 없는 Play 모드에서 선택 단계가 정지하지 않도록(현재 미구현).
3. **Phase 2**: 미션 특수 규칙(데드존·통신차단·"9 못 이김"·로켓 순서 등)을 관찰 벡터로 인코딩.

## 알려진 주의점 / 리스크

- **선택 페이즈는 정책 결정을 기다림**: 트레이너 연결(학습) 또는 `HeuristicOnly`(시뮬)에서는 정상.
  단, 트레이너 없는 순수 Play 모드 + `Default`(모델 없음)면 선택 단계에서 **대기/정지**할 수 있음
  (트릭 페이즈처럼 selection watchdog는 아직 없음). 필요 시 watchdog 추가 고려.
- 다중 task에서 `phase1Assignee`/MCTS/RuleBasedHelper는 단일-담당자 근사로만 동작(학습 경로 아님, 베이스라인).
- 액션/관찰 크기를 의도적으로 고정(257, [10,2,4])했으므로 씬 BehaviorParameters는 그대로 두면 됨.
