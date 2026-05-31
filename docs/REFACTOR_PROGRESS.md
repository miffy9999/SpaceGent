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

---

## 현재 구현 상태 요약 (환경 모델링 & 학습 흐름)

### ✅ 환경에 제대로 모델링 + 학습되는 것
1. **트릭테이킹** — 플레이 페이즈 `Branch[0]`(카드). follow-suit 마스킹, 로켓 우선 승자 판정,
   대상 카드가 든 트릭의 승자=소유자면 task 완료/아니면 즉시 실패 (`MissionManager.OnTrickResolved`). **학습됨.**
2. **task 드래프트 선택** — 선택 페이즈 `Branch[0]`(풀 슬롯)+`Branch[1]`(가져가기/패스). 함장부터, `T<R` 패스 규칙.
   AI는 `RequestDecision`→정책으로 결정. **학습됨.**
3. **보상** — MA-POCA 그룹 리워드: task 완수 +1 / 실패 -1(즉시 미션 종료). 선택·플레이 두 정책의 크레딧을 POCA 중앙 critic이 분배.

### ⚠️ 액션은 있으나 학습 신호가 없는 것 — **통신 토큰**
- 플레이 페이즈 `Branch[1]`=0/1로 **사용 여부를 정책이 출력** → 형식상 "학습 대상"은 맞음.
- 그러나 공개된 카드(`CommunicationToken.revealedCard`)는 **관측 벡터(257)에 전혀 포함되지 않음**.
  → 동료 에이전트가 통신 내용을 **볼 수 없음** → 협력 정보 채널로서 **효과 없음(사실상 dead action)**. 현재는 UI 표시(`GameUIManager`)에만 쓰임.
- **결론**: 통신 토큰은 "켜고 끄는 버튼"만 학습될 뿐, 스페이스 크루의 핵심인 *제한된 정보 공유*가 환경에 빠져 있음.
  의미 있게 하려면 관측에 "각 플레이어 통신 토큰 상태(사용/미사용) + 공개 카드(suit/value/위치)"를 추가해야 함
  (관측 크기 증가 → 씬 BehaviorParameters 편집 또는 빈 슬롯 재활용 필요).

### ❌ 학습 환경에 없는 것
- **조난신호**: AI 전용/배치 모드에선 `TrickManager.StartDistressSignalPhase`가 **즉시 스킵** → 학습·사용 안 함.
  인간 UI(`D`키)로만 동작. `Branch[2]`(size 4)는 전부 마스킹된 예비 슬롯.
- **순서 토큰(①②Ω→)**: 평가 로직(`IsOrderTokenValid`)·관측 피처는 있으나, 현재 학습 풀 생성(`GenerateTaskPool`→`CreateUnassignedTask`)은
  `orderToken=None`만 만들어 → 학습 데이터에 등장하지 않음.

### 🔁 학습이 도는 방식 (에피소드 1회)
1. 카드 40장 분배(`DeckManager`) → 함장(로켓4 소지자) 결정
2. **[선택 페이즈]** 함장부터 시계방향 `RequestDecision` → 각자 take/pass(정책). 풀이 빌 때까지(패스 규칙 `T<R`). 모든 task 배정 보장
3. **[조난신호 페이즈]** AI면 스킵
4. **[플레이 페이즈]** 트릭마다 선부터 `RequestDecision` → 카드 제출(+통신 토큰 옵션). 트릭 승자 판정 → task 완료/실패(그룹 ±1).
   task 실패 시 즉시 미션 종료
5. 핸드/미션 종결 → `teamGroup.EndGroupEpisode()` → 재시작
- 관측 **257** / 액션 **[10,2,4]** 고정, 페이즈별 마스킹으로 선택↔플레이 분리
- 커리큘럼: `num_tasks` 1→2→3→4 (그룹 평균 보상 threshold로 진급)

### 한 줄 평가
**핵심 게임 루프(딜·드래프트·트릭·task 판정·그룹 보상)는 잘 모델링되어 4-에이전트 협력 학습이 돈다.**
다만 **통신 토큰은 관측 미반영으로 협력 채널 역할을 못 하고, 조난신호는 학습에서 빠져 있다.** —
스페이스 크루의 "정보 공유" 메커닉을 학습에 넣으려면 이 둘(특히 통신 관측 추가)이 다음 우선순위.

---

## Phase 1c — 최종 관측 레이아웃 확정 (전이형 커리큘럼 준비)

> 결정: 조난신호는 학습에서 **제외 확정**(인간 UI 전용). 통신은 관측 추가 확정.
> 커리큘럼 조건들을 `--resume`로 **이어받아 학습(transfer)** 하려면 관측/액션 공간이 단계 간 동일해야 하므로,
> **관측을 처음부터 "최종형"으로 확장**하고 조건은 env 파라미터로 켜고 끈다.

- **관측 벡터 257 → 297 → 313** (`CrewAgent.ObservationSize`). 직렬화 `VectorObservationSize`도 동일하게:
  `Player Agent.prefab`, `Table Environment.prefab`(오버라이드 4), `SampleScene.unity`(4). 액션 `[10,2,4]` 불변.
  - **+24 통신**: viewer 기준 4명 × [사용, 공개suit/4, 공개value/9, 최고/유일/최저]. 실제 공개 카드 정보 반영 → **통신이 협력 채널로 작동**(이전 dead action 해소).
  - **+32 특수규칙(예약)**: mission-level. Phase A엔 0. `MissionManager.GetSpecialRuleObs()` 스텁(32칸, 카테고리+파라미터 레이아웃 문서화).
    미션 md 특수규칙 19종을 one-hot이 아니라 메커니즘+파라미터로 수용하기 위해 16→32로 증설(전이 깨짐 방지 위해 초반에 헤드룸 확보). Phase B/C에서 채움.
- **순서 토큰 전체 관측**: task 슬롯 `+2`를 `orderIndex(N1~5만)` → `(int)orderToken/10`(None~Arrow4 전부)로 변경. Ω·화살표도 관측 가능.
- **순서 토큰 커리큘럼 게이트**: env `enable_order_tokens`(기본 0). 1이면 `AssignSequentialOrderTokens()`가 풀 앞쪽부터 N1..N5 부여.
  enforce 로직(`IsOrderTokenValid`)은 이미 존재 → 켜기만 하면 학습에 적용. (num_tasks≥2에서 의미)

### 전이형 커리큘럼 로드맵
- **Phase A**: 모든 조건 off (`enable_order_tokens=0`, 특수규칙 0). `num_tasks` 1→증가로 드래프트+트릭테이킹 학습.
- **Phase B**: `--resume`로 이어받아 **가산적 제약**을 env로 점진 투입 — 숫자토큰 → Ω/화살표 → 통신차단(⚡) → 데드존 → "9 못이김" 등.
  (관측/액션 공간 불변이므로 동일 네트워크로 전이됨)
- **Phase C(후순위)**: 사령관의 결정/분배·0-task 미션 등 **드래프트/흐름 구조가 바뀌는** 규칙은 별도 모드로.

### 가산적 제약 vs 구조 변경 (전이 가능성)
- 가산적(전이 잘 됨): 숫자토큰·Ω·화살표, "9 트릭 불가", "로켓 승리 불가", 통신차단, 데드존, 상대 트릭차 제한.
- 구조 변경(전이 약함, 별도 설계): 사령관 결정(미션 20·27·37), 사령관 분배(24·32·36·43), 0-task 미션.

### 남은 작업
1. 학습 실행 검증(Stage1 num_tasks=1 → 점증). 통신 사용이 의미 있게 학습되는지(공개 후 동료 정책 변화) 관찰.
2. Phase B: 특수규칙 obs 채우기 + enforce 구현(통신차단·데드존부터). `enable_order_tokens`로 순서토큰 단계 추가.
3. Phase C: 사령관 결정/분배 드래프트 변형.

---

## 통합 (`integration` 브랜치) — 팀원 `space-crew-env` 베이스 + 내 기여 얹기

> 베이스 = 팀원 브랜치(50미션 DB·`GlobalMissionRule`/`MissionTaskRule`·playMode·텍스트태스크·통신규칙 enforce).
> 규칙 표현/enforce는 **팀원 구현을 정본으로 채택**하고, 내 차별 기여만 얹음.

**얹은 것:**
- **`GetSpecialRuleObs(viewer)` 채움** (팀원은 stub=0이었음) — `currentMission`(globalRule·taskRule·순서토큰) + `CommunicationManager`(데드존·통신차단·통신금지)를 32칸 관측으로 노출. **에이전트가 활성 규칙을 인지 → 학습 가능.** CrewAgent 호출 `GetSpecialRuleObs(this)`.
- **Simulation 합성 미션**(`BuildSyntheticMissionFromEnv`) — 학습 모드에서 env 플래그(`global_rule`·`order_token_mode`·`dead_zone`·`comm_disrupt_until`·`no_comm_player`·`card_pass_after_first`)로 특수규칙을 켜면 합성 `currentMission`에 주입 → 팀원 enforce·내 관측이 그대로 작동. 플래그 없으면 null(기존 num_tasks 베이스 동일).
- **config**: 위 env 플래그 문서화(커리큘럼/fixed 예시).

**팀원 정본으로 둔 것(중복 제거):** globalRule enforce(`CheckGlobalRule`), 통신 규칙(`CommunicationManager`), 카드교환·순서토큰 enforce, 50미션 DB, playMode/UI/씬.

**합의/확인 필요(팀원과):**
- 데드존 시 AI 통신이 `UseCommToken`(위치 공개) 경로라 `UseCommTokenDeadZone`로 라우팅 필요(현재 위치가 노출될 수 있음) — 팀원 도메인.
- `commander_decision/distribution`은 드래프트 흐름 미구현(관측만). 필요 시 별도 구현.
- m46=`LeftOfPinkNineWinsAllPink`(빨강=Pink) 해석 일치 확인.
