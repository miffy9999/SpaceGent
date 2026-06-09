# SpaceGent — The Crew: 아홉 번째 행성을 찾아서 (Unity 6.3)

> **팀원 최초 설정 필수**: 이 프로젝트는 Git LFS를 사용합니다.
> ```bash
> git lfs install   # 1회만 실행
> git clone https://github.com/miffy9999/SpaceGent.git
> ```
> Git LFS 없이 clone하면 폰트/에셋 파일이 깨집니다.

4인 협동 트릭 테이킹 카드 게임 **[The Crew: 아홉 번째 행성을 찾아서 (The Quest for Planet Nine)](https://boardgamegeek.com/boardgame/284083/the-crew-the-quest-for-planet-nine)** 룰을 기반으로 Unity ML-Agents AI를 구현하는 프로젝트.

> 원래 *딥 씨 크루(The Crew: 심해 탐험)* 기반이었으나, 강화학습에 더 적합한 **스페이스 크루**로 마이그레이션했습니다. 룰 상세는 저장소의 `the_crew_rules_ko.md` 참고.

---

## 팀 역할 분담

| 담당 | 영역 |
|------|------|
| **Haetae** | Unity — 게임 로직, 씬, UI, 에디터 스크립트 |
| **팀원** | Python — ML-Agents 학습 설정, trainer config, 결과 분석 |
| **MCTS 담당** | Unity C# — MCTS 협력 AI (SO-ISMCTS) 알고리즘 설계 및 구현 |

- Unity 작업: `Assets/` 폴더 전반
- Python 작업: `config/` (trainer yaml), `results/` (학습 결과), TensorBoard 분석

---

## 주요 기능

- **스페이스 크루 룰 충실 구현**
  - Follow-suit, 로켓(트럼프) 우선, 트릭 승자 판별
  - 사령관(로켓 4번 소지자)부터 시계방향으로 태스크 선택
  - 순서 토큰: 번호(1·2·3·Ω)·화살표 토큰 순서대로 달성, 위반 시 즉시 미션 실패
  - **무선통신 토큰**: 미션당 1회, 트릭 사이에만 사용. 자기 카드 1장을 공개하고 최고/유일/최저 위치 표시 (로켓 불가)
  - **조난신호**: 첫 트릭 전, 로켓을 제외한 카드 1장을 인접 플레이어에게 전달
- **MCTS 협력 AI**: SO-ISMCTS 기반 탐색 트리, 신념 결정화 및 능동 feeding(협력 롤아웃)을 통해 순수 탐색만으로 높은 미션 성공률 달성
- **미션 1~50 전체 정의**: `MissionDatabase.BuildAllMissions()`가 태스크 수·순서 토큰·특수 규칙을 코드로 정의
  - **전역 미션 규칙**: 9 트릭 금지, 로켓 오름차순, 2트릭 차 금지, 로켓당 1트릭, 사령관 첫·마지막 등 (`GlobalMissionRule`)
  - **통신 특수 규칙**: 데드존(위치 비공개), 통신 차단(⚡N 트릭 전 금지), 특정인 통신 불가 (`MissionTaskRule`)
- **단일 태스크 타입(WinSpecificCard)**: "지정 색깔+숫자 카드가 든 트릭을 이긴다" — 색상 4종 × 1~9 = 36종. 태스크는 **텍스트로만 표시**(스프라이트 불필요)
- **task 드래프트 정책 학습**: 사령관부터 시계방향으로 가져가기/패스(`T<R`일 때만 패스)를 학습
- **ML-Agents (MA-POCA)**: 4-에이전트 협력 — 트릭테이킹 + task 선택 두 정책을 함께 학습. 관찰 313개, 이산 행동 3 브랜치
  - 인간 플레이어는 ML-Agents 파이프라인을 완전히 우회 (`HumanDirectPlay`)
- **플레이 모드 토글** (`GameManager.playMode`)
  - **Simulation**: 전원 AI 자동 진행 — 인간 입력 없이 빠르게(학습·평가·시뮬). 미션은 `num_tasks` 커리큘럼
  - **HumanVsAI**: player[0]이 인간. 미션 DB의 1→50을 순서대로 진행(성공 시 다음 미션)
- **커리큘럼 학습**: task 개수 1개 → 점차 증가 (`num_tasks`)
- **입력 시스템**: New Input System (`Keyboard.current`) + old Input (Both 모드)
- **2D 손패 UI**: 카드 클릭 및 키보드 1~0 선택 지원
- **프리팹 없이도 동작**: `taskPoolItemPrefab` 미연결 시 동적 버튼 자동 생성

---

## 프로젝트 구조

```
SpaceGent/
├── Assets/
│   ├── Editor/
│   │   └── CreateGameUIEditor.cs      # SpaceCrew/Create Game UI 메뉴 — Canvas 자동 생성
│   ├── Prefabs/
│   │   ├── HandCard.prefab            # 인간 플레이어 손패 카드 UI
│   │   ├── TaskItem.prefab            # 태스크 목록 항목
│   │   ├── TaskPoolItem.prefab        # 태스크 선택 버튼 (미연결 시 동적 생성)
│   │   ├── Card_prefab.prefab         # 중앙 테이블 카드 오브젝트
│   │   └── Player Agent.prefab        # CrewAgent + BehaviorParameters + DecisionRequester
│   └── Scripts/
│       ├── AI/
│       │   ├── CrewAgent.cs           # ML-Agents 에이전트 + 인간 입력 + 정책 dispatch(RL/MCTS/Rule)
│       │   ├── RuleBasedHelper.cs     # 규칙 기반(HFSM) 협력 정책 + 평가 통계
│       │   ├── EvalStats.cs           # 미션 성공률/평균 완수율 누적 콘솔 로그
│       │   └── MCTS/                  # 협력 MCTS (SO-ISMCTS)
│       │       ├── MCTSSearch.cs      # SO-ISMCTS 본체(카드키 공유 트리) + MCTSContext
│       │       ├── MCTSState.cs       # 결정화 게임 상태(다중 태스크/전역규칙) + MctsTask
│       │       ├── MCTSRollout.cs     # 협력 롤아웃 정책(목표 claim/양보/능동 feeding)
│       │       └── Determinizer.cs    # 신념 결정화(void+통신 제약, MCV 백트래킹)
│       ├── Core/
│       │   ├── Card.cs                # 카드 데이터 (수트 4종 + 로켓, 값 기반 동등성)
│       │   ├── CardDisplay.cs         # 3D 카드 비주얼
│       │   ├── CardSpriteMapping.cs   # 카드 → Sprite 매핑 ScriptableObject
│       │   ├── CommunicationManager.cs# 무선통신 토큰 + 조난신호 통합 관리
│       │   ├── CommunicationToken.cs  # 무선통신 토큰 (자기 카드 공개)
│       │   ├── DeckManager.cs         # 덱 생성(40장) 및 4인 분배
│       │   ├── DistressSignal.cs      # 조난신호 (로켓 제외 카드 1장을 인접 플레이어에게 전달)
│       │   ├── GameManager.cs         # 싱글턴 — 플레이어/매니저 참조 허브
│       │   ├── GamePhase.cs           # Setup / TaskSelection / DistressSignal / Playing / Result
│       │   ├── GameUIManager.cs       # HUD + 태스크 선택 패널 + 토큰 버튼
│       │   ├── HandCardUI.cs          # 손패 카드 버튼 (클릭 → SelectCard)
│       │   ├── Mission.cs             # 미션 데이터 (번호, 태스크 수, 순서토큰, 전역/통신 특수규칙)
│       │   ├── MissionDatabase.cs     # 미션 1~50 코드 정의 + ScriptableObject 컬렉션
│       │   ├── MissionManager.cs      # 태스크 선택 + 트릭 판정 + 전역규칙 + 보상 + MCTS 컨텍스트 빌드
│       │   ├── MissionRules.cs        # 규칙 순수함수(Beats/승자/전역규칙) — 실게임·MCTS 공용
│       │   ├── TaskCard.cs            # WinSpecificCard 태스크(targetCard) + 순서 토큰(OrderToken)
│       │   ├── TaskSpriteMapping.cs   # 태스크 → Sprite 매핑 ScriptableObject
│       │   └── TrickManager.cs        # 게임 흐름 제어 + 트릭 로직 + Watchdog
│       └── InputSystem_Actions.cs     # New Input System 자동 생성 파일 (수정 금지)
├── config/                            # [Python 담당] ML-Agents trainer yaml
└── results/                           # [Python 담당] 학습 결과 / TensorBoard 로그
```

---

## 게임 규칙 (스페이스 크루 기준)

### 기본 트릭 테이킹
- 로켓(트럼프) 카드는 어떤 색 카드도 이긴다
- 선(lead) 색상을 가지고 있으면 반드시 그 색을 내야 한다 (follow-suit)
- 로켓 카드끼리는 숫자가 높은 쪽이 이긴다 (4로켓이 최강)
- 트릭 승자가 다음 트릭의 선이 된다
- **로켓 4번 소지자 = 사령관**, 첫 트릭의 선

### 태스크 (단일 타입 — WinSpecificCard)

스페이스 크루의 태스크 카드는 한 종류뿐이다: **"지정된 색깔+숫자 카드(targetCard)가 포함된 트릭을 자신이 이긴다."**
대상 카드는 색상 4종 × 1~9 = **36종** (로켓은 태스크 카드가 아님). 대상 카드가 나온 트릭의 승자가 소유자면 완수, 아니면 즉시 실패.

> 미션별 특수 승리 조건(예: "9는 트릭 못 이김", "로켓 오름차순")은 태스크 카드가 아니라 **미션 단위 특수 규칙**(`GlobalMissionRule`)으로 구현되어 트릭마다/핸드 종료 시 판정된다. 위반 시 즉시 미션 실패.

### 태스크 드래프트(선택) 단계
1. 미션이 정한 수만큼 WinSpecificCard 태스크 풀이 생성된다 (학습: `num_tasks` 커리큘럼 / 인간 플레이: 미션 DB 정의)
2. **사령관(로켓4 소지자)부터** 시계방향으로 돌며, 각자 **가져가기 또는 패스**
   - 패스 규칙: 남은 task `T` < 이번 라운드 잔여 인원 `R`(=`N - cursor%N`)일 때만 패스 가능. `T >= R`이면 강제 선택 → 모든 task는 반드시 배정됨
3. **Simulation/학습**: AI가 ML 정책으로 선택(`RequestDecision`). **HumanVsAI**: AI는 결정론적 자동 선택(브레인 없이도 진행), 인간은 UI 버튼/키
4. 일부 태스크에는 **순서 토큰(1·2·3·Ω·화살표)** — 미션 정의에 따라 부여, 지정 순서대로 달성
5. 드래프트 완료 후 (조난신호 단계를 거쳐) 트릭 게임 시작

### 무선통신 · 조난신호
- **무선통신 토큰**: 미션당 1회, 트릭 사이(아무도 카드를 내지 않은 상태)에만 사용 가능. 자기 손패 카드 1장(로켓 제외)을 공개하고 최고값/최저값/유일 중 하나를 표시
- **조난신호**: 미션 시작 전 첫 트릭 전에 1회. 로켓을 제외한 카드 1장을 인접 플레이어에게 전달 (AI 전용/배치 모드에서는 자동 스킵)

---

## 게임 흐름

```
StartGame()
  ├─ 카드 분배 (DeckManager, 40장 → 4명 × 10장)
  ├─ 사령관 결정 (로켓 4번 소지자)
  ├─ 무선통신 토큰 + 조난신호 초기화
  ├─ [TaskSelection] MissionManager.StartTaskSelectionPhase()
  │     ├─ 태스크 풀 생성 (미션 DB 또는 fallback)
  │     ├─ 순서 토큰 부여 (태스크 3개 이상 시)
  │     └─ 사령관부터 시계방향: AI 자동 선택 → 인간 UI/키보드 선택 대기
  ├─ [DistressSignal] 조난신호 결정 (첫 트릭 전, 선택 사항 — AI 전용이면 자동 스킵)
  ├─ [Playing] TrickManager.StartPlaying()
  │     ├─ 트릭마다: 선 플레이어부터 시계방향 입력
  │     │     ├─ 인간: 카드 클릭 또는 키보드 1~0
  │     │     └─ AI: RequestDecision() → OnActionReceived()
  │     ├─ 트릭 승자 판별 → MissionManager.OnTrickResolved()
  │     │     └─ 태스크 달성/실패 판정, 순서 토큰 위반 시 즉시 미션 실패
  │     └─ 손패 소진 → MissionManager.OnHandEnded() → 최종 판정
  └─ [Result] ShowResult() → 에피소드 재시작
```

---

## ML-Agents 설정

### 관찰 벡터 (총 313개 — 최종 레이아웃, 커리큘럼 조건은 env로 켜고 끔)

| 인덱스 | 크기 | 내용 |
|--------|------|------|
| 0~39 | 40 | 내 손패 원-핫 (카드 40장 슬롯) |
| 40~79 | 40 | 바닥 카드 원-핫 — **선택 페이즈엔 task 풀 슬롯 10×4 인코딩으로 재사용** |
| 80~84 | 5 | 선 색상(Lead Suit) 원-핫 |
| 85~246 | 162 | 팀 태스크 (4명분, viewer 기준 시계방향: 16슬롯 × 10 + 본인 완료/실패 비율 2) |
| 247~250 | 4 | 플레이어별 남은 손패 수 (/ 10 정규화, viewer 기준 시계방향) |
| 251~254 | 4 | 플레이어별 현재 트릭 승리 수 (/ 10 정규화) |
| 255~256 | 2 | [0] 선택 페이즈 여부 / [1] 선택 중=패스 가능·플레이 중=내 task 타깃 보유 |
| 257~280 | 24 | **통신**: viewer 기준 4명 × [사용, 공개suit/4, 공개value/9, 최고, 유일, 최저] |
| 281~312 | 32 | **특수규칙 예약** (Phase A엔 0, 커리큘럼에서 채움; 미션 19종 수용 헤드룸) |

**태스크 관찰 슬롯 구조** (슬롯당 10개, 4명 × 4슬롯 = 16슬롯. `[0..3]`=viewer, `[4..7]`=viewer+1, …):

| 오프셋 | 내용 |
|--------|------|
| +0 | targetCard.suit (/ 4) |
| +1 | targetCard.value (/ 9) |
| +2 | orderToken 전체 None~Arrow4 (/ 10) |
| +3 | isCompleted |
| +4 | isFailed |
| +5~9 | 예비 (0) |

**선택 페이즈 풀 슬롯 인코딩** (바닥 카드 40칸 재사용, 슬롯 j(0~9) × 4):

| 오프셋 | 내용 |
|--------|------|
| +0 | targetCard.suit (/ 4) |
| +1 | targetCard.value (/ 9) |
| +2 | 내가 이 타깃 카드를 보유 |
| +3 | 슬롯 점유 |

### 행동 공간

| 브랜치 | 크기 | 내용 |
|--------|------|------|
| Branch[0] | 10 | 플레이: 낼 카드 인덱스 / **선택 페이즈: 가져갈 풀 슬롯 (0~9)** |
| Branch[1] | 2 | 플레이: 무선통신(0/1) / **선택 페이즈: 0=가져가기, 1=패스(가능 시)** |
| Branch[2] | 4 | 예비(미사용 — 전부 마스킹) |

> 페이즈별로 마스킹이 분리됩니다(선택/플레이). 액션·관찰 공간은 동일하게 **재사용**해 씬 BehaviorParameters 변경이 필요 없습니다.
> follow-suit 위반 카드는 마스킹되며, 그래도 들어오면 합법 카드로 자동 대체됩니다.

### 보상 구조

| 이벤트 | 보상(MA-POCA 그룹) |
|--------|------|
| task 완수 | +1.0 |
| task 실패 | −1.0 (즉시 미션 종료) |

> 그룹 리워드. 에피소드 그룹 리턴 ≈ (완수 task 수) − (실패 시 1). `num_tasks`개 모두 완수 시 +`num_tasks`.
> 선택(드래프트)·플레이 두 정책의 크레딧 할당은 POCA의 중앙집중 critic이 담당합니다.

### 커리큘럼 학습

`num_tasks` 환경 파라미터로 task 개수를 1개부터 늘린다. 진급 조건은 그룹 평균 보상.

| 단계 | num_tasks | 진급 조건 |
|------|-----------|---------|
| Stage 1 | 1 | ≥ 0.6 |
| Stage 2 | 2 | ≥ 0.8 |
| Stage 3 | 3 | ≥ 1.0 |
| Stage 4 | 4 | — |

### 학습 실행 [Python 담당]

```bash
# 학습 시작 (Unity에서 Play 버튼 먼저)
mlagents-learn config/trainer_config.yaml --run-id=spacegent_v1

# 이어서 학습
mlagents-learn config/trainer_config.yaml --run-id=spacegent_v1 --resume

# 결과 확인
tensorboard --logdir results/
```

> ML-Agents에 연결하지 않으면 "Couldn't connect to trainer" 메시지가 뜨며 inference 모드로 동작합니다. 학습이 아닌 단순 플레이 테스트 시 정상입니다.

---

## MCTS 협력 AI

### 알고리즘 개요

**SO-ISMCTS** (Single-Observer Information Set MCTS, Cowling 2012) 기반으로 불완전 정보 협력 게임에 적합하게 설계.

| 구성 요소 | 내용 |
|-----------|------|
| **탐색 트리** | 카드 키(suit×100+value) 공유 트리 — 결정화별 샘플이 동일 노드를 누적 |
| **신념 결정화** | MCV+백트래킹 — void·통신 제약을 만족하는 손패 분포를 완전 탐색으로 생성 (fallback 0) |
| **협력 롤아웃** | 목표 claim/양보 + 능동 feeding (owner가 동료 목표 무늬를 보장승리 카드로 리드 → 동료 release) |
| **탐색 파라미터** | C=0.7 (스윕 정점: 0.4=32.6%, **0.7=39.8%**, 1.0=33.5%), budget=1000 (이후 평탄) |
| **재결정화** | K=5 per iteration (pool 재사용 시 strategy fusion 발생 → 매 반복 재샘플링) |

### 벤치마크 (협력 2-task, cooperativeTargets=ON, 800 에피소드)

| 정책 | 미션 성공률 | 평균 태스크 완수율 |
|------|-------------|------------------|
| **MCTS** (SO-ISMCTS) | **34.6 %** | **49.9 %** |
| RuleBased (HFSM) | 6.7 % | 19.0 % |
| **배율** | **5.2×** | **2.6×** |

> **cooperativeTargets=ON**: 타깃 카드를 전체 덱에서 무작위 배정 → 동료가 타깃 보유 → 실질적인 협력이 필요한 시나리오. MCTS 본령이며 헤드룸이 큼.

### GameManager 평가 설정

| 필드 | 권장값 | 설명 |
|------|--------|------|
| `aiPolicy` | `MCTS` | MCTS 정책 사용 |
| `cooperativeTargets` | `true` | 협력 필요 시나리오 활성화 |
| `overrideNumTasks` | `2` | 태스크 수 고정 (2-task 표준 벤치마크) |
| `mctsBudget` | `1000` | 시뮬레이션 예산 (이후 평탄) |
| `mctsDeterminizations` | `10` | 결정화 수 |
| `evaluationLogging` | `true` | EvalStats 콘솔 출력 |

### 참고 문서

- [docs/MCTS_Cooperative_AI.md](docs/MCTS_Cooperative_AI.md) — 알고리즘 동작 상세(결정화·공유트리·협력 롤아웃)
- [docs/MCTS_DESIGN_NOTES.md](docs/MCTS_DESIGN_NOTES.md) — 의사결정 기록·벤치마크 전체 수치·되돌린 실험·규칙 구현 범위

---

## Unity 씬 설정 [Unity 담당]

### Inspector 필수 연결 항목

**GameManager**
- `playMode` — **Simulation**(전원 AI 자동, 학습/시뮬) 또는 **HumanVsAI**(player[0] 인간)
- `players` — CrewAgent 4개 (index 0 = 인간, Simulation 시 전원 AI)
- `centerBoard` — 중앙 테이블 Transform
- `deckManager`, `trickManager`, `missionManager`, `communicationManager`, `uiManager`
- `database`(MissionManager) — 미션 1~50 DB. Inspector의 "50개 미션 코드 정의로 재생성" 버튼으로 생성

**GameUIManager**
- `taskSelectionPanel` — 태스크 선택 오버레이 패널 루트 **(필수)**
- `taskPoolContainer` — 태스크 버튼이 들어갈 부모 Transform **(필수)**
- `taskPoolItemPrefab` — 태스크 버튼 프리팹 (미연결 시 동적 생성으로 자동 대체)
- 나머지 패널/아이콘/버튼은 미연결 시 Hierarchy 이름으로 자동 탐색

### 인간 플레이어 조작

| 입력 | 단계 | 동작 |
|------|------|------|
| 카드 UI 클릭 | Playing | 카드 선택 |
| 숫자키 `1~0` | Playing | 카드 선택 (손패 인덱스) |
| 숫자키 `1~9` | TaskSelection | 태스크 선택 |
| `Space` | Playing (트릭 사이) | 무선통신 토큰 사용 예약 |
| `D` | DistressSignal | 조난신호 활성화 (로켓 제외 카드 1장 → 오른쪽 전달 예약) |
| `Space` / `Enter` | DistressSignal | 조난신호 확정 또는 건너뛰기 |

> **Game 뷰 포커스**: 키보드 입력은 Unity 에디터에서 Game 뷰 화면 **내부**를 클릭하여 포커스를 맞춘 후 동작합니다.

### 입력 시스템 설정

`Project Settings > Player > Active Input Handling = Both`

New Input System (`UnityEngine.InputSystem.Keyboard.current`) 기반으로 동작합니다.

---

## MCP for Unity 설정 [선택사항]

Unity Editor를 Claude Code로 직접 제어할 때 사용.

`Window > MCP for Unity`에서 서버 시작 후 `.claude.json`에 설정:

```json
"mcpServers": {
  "UnityMCP": {
    "type": "http",
    "url": "http://127.0.0.1:8080/mcp",
    "headers": { "X-API-Key": "<EditorPrefs의 MCPForUnity.ApiKey 값>" }
  }
}
```

패키지 설치:
```
Window > Package Manager > + > Add package from git URL
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main
```
