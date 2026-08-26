# SeaAI — The Crew: Deep Sea (Unity 6.3)

> **팀원 최초 설정 필수**: 이 프로젝트는 Git LFS를 사용합니다.
> ```bash
> git lfs install   # 1회만 실행
> git clone https://github.com/miffy9999/Divergent.git
> ```
> Git LFS 없이 clone하면 폰트/에셋 파일이 깨집니다.

4인 협동 트릭 테이킹 카드 게임. [The Crew: 심해 탐험](https://boardgamegeek.com/boardgame/324856/the-crew-mission-deep-sea) 룰을 기반으로 Unity ML-Agents AI를 구현하는 프로젝트.

---

## 팀 역할 분담

| 담당 | 영역 |
|------|------|
| **Haetae** | Unity — 게임 로직, 씬, UI, 에디터 스크립트 |
| **팀원** | Python — ML-Agents 학습 설정, trainer config, 결과 분석 |

- Unity 작업: `Assets/` 폴더 전반
- Python 작업: `config/` (trainer yaml), `results/` (학습 결과), TensorBoard 분석

---

## 주요 기능

- **딥 씨 크루 룰 충실 구현**
  - Follow-suit, 잠수함(트럼프) 우선, 트릭 승자 판별
  - 함장(잠수함 4번 소지자)부터 시계방향으로 태스크 선택
  - 순서 토큰: 번호가 있는 태스크는 낮은 번호 순서대로 달성
  - 통신 토큰: Playing 단계, 트릭 사이에만 사용 가능
  - 소나 토큰: Playing 단계에서만 사용 가능
- **21종 태스크 타입**: 특정 카드 획득부터 홀수 트릭, 상대 비교까지 다양한 조건
- **BGA 방식 태스크 선택**: 함장부터 시계방향, AI 자동 · 인간 UI/키보드 선택
- **ML-Agents PPO**: 관찰 벡터 219개, 이산 행동 3 브랜치
  - 인간 플레이어는 ML-Agents 파이프라인을 완전히 우회 (`HumanDirectPlay`)
- **커리큘럼 학습**: 난이도 3→5→7→9 단계적 증가
- **입력 시스템**: New Input System (`Keyboard.current`) + old Input (Both 모드)
- **2D 손패 UI**: 카드 클릭 및 키보드 1~0 선택 지원
- **프리팹 없이도 동작**: `taskPoolItemPrefab` 미연결 시 동적 버튼 자동 생성

---

## 프로젝트 구조

```
SeaAI/
├── Assets/
│   ├── Editor/
│   │   └── CreateGameUIEditor.cs      # SeaAI/Create Game UI 메뉴 — Canvas 자동 생성
│   ├── Prefabs/
│   │   ├── HandCard.prefab            # 인간 플레이어 손패 카드 UI
│   │   ├── TaskItem.prefab            # 태스크 목록 항목
│   │   ├── TaskPoolItem.prefab        # 태스크 선택 버튼 (미연결 시 동적 생성)
│   │   ├── Card_prefab.prefab         # 중앙 테이블 카드 오브젝트
│   │   └── Player Agent.prefab        # CrewAgent + BehaviorParameters + DecisionRequester
│   └── Scripts/
│       ├── AI/
│       │   └── CrewAgent.cs           # ML-Agents 에이전트 + 인간 입력 처리
│       ├── Core/
│       │   ├── Card.cs                # 카드 데이터 (수트 4종 + 잠수함, 값 기반 동등성)
│       │   ├── CardDisplay.cs         # 3D 카드 비주얼
│       │   ├── CardSpriteMapping.cs   # 카드 → Sprite 매핑 ScriptableObject
│       │   ├── CommunicationManager.cs# 통신/소나 토큰 통합 관리
│       │   ├── CommunicationToken.cs  # 통신 토큰 (자기 카드 공개)
│       │   ├── DeckManager.cs         # 덱 생성(40장) 및 4인 분배
│       │   ├── GameManager.cs         # 싱글턴 — 플레이어/매니저 참조 허브
│       │   ├── GamePhase.cs           # Setup / TaskSelection / Playing / Result
│       │   ├── GameUIManager.cs       # HUD + 태스크 선택 패널 + 토큰 버튼
│       │   ├── HandCardUI.cs          # 손패 카드 버튼 (클릭 → SelectCard)
│       │   ├── Mission.cs             # 미션 데이터 (id, taskCounts, 난이도)
│       │   ├── MissionDatabase.cs     # Mission ScriptableObject 컬렉션
│       │   ├── MissionManager.cs      # 태스크 선택 + 트릭 판정 + 보상
│       │   ├── SonarToken.cs          # 소나 토큰 (상대 카드 공개)
│       │   ├── TaskCard.cs            # 21종 태스크 데이터 + 순서 토큰
│       │   ├── TaskSpriteMapping.cs   # 태스크 → Sprite 매핑 ScriptableObject
│       │   └── TrickManager.cs        # 게임 흐름 제어 + 트릭 로직 + Watchdog
│       └── InputSystem_Actions.cs     # New Input System 자동 생성 파일 (수정 금지)
├── config/                            # [Python 담당] ML-Agents trainer yaml
└── results/                           # [Python 담당] 학습 결과 / TensorBoard 로그
```

---

## 게임 규칙 (딥 씨 크루 기준)

### 기본 트릭 테이킹
- 잠수함(트럼프) 카드는 어떤 색 카드도 이긴다
- 선(lead) 색상을 가지고 있으면 반드시 그 색을 내야 한다 (follow-suit)
- 잠수함 카드끼리는 숫자가 높은 쪽이 이긴다
- 트릭 승자가 다음 트릭의 선이 된다
- **잠수함 4번 소지자 = 함장**, 첫 트릭의 선

### 태스크 선택 단계
1. 미션에 따라 태스크 카드 풀이 생성된다
2. **함장부터** 시계방향으로 모든 플레이어가 1장씩 번갈아 선택한다
3. AI는 자동 선택, 인간은 UI 버튼 클릭 또는 키 `1~9`로 선택
4. 일부 태스크에는 **순서 토큰(1·2·3…)** 이 붙어 있어, 반드시 낮은 번호 순서대로 달성해야 한다
5. 모든 태스크 선택 완료 후 트릭 게임 시작

### 21종 태스크 타입

| 타입 | 조건 |
|------|------|
| WinSpecificCard | 특정 카드가 포함된 트릭 획득 |
| WinTrickCount | 트릭 정확히 N회 획득 |
| WinFirst | 첫 트릭 획득 |
| WinNone | 트릭 0회 (한 번도 이기면 안 됨) |
| WinLast | 마지막 트릭 획득 |
| WinConsecutive | N트릭 연속 획득 |
| WinNoConsecutive | 2트릭 연속 획득 금지 |
| WinOnlyFirst | 첫 트릭만 획득 (이후 이기면 안 됨) |
| WinOnlyLast | 마지막 트릭만 획득 (이전에 이기면 안 됨) |
| WinFirstAndLast | 첫 트릭 + 마지막 트릭 둘 다 획득 |
| WinNoSuit | 특정 슈트가 리드된 트릭 획득 금지 |
| WinNoOpenSuit | 특정 슈트로 트릭 시작 금지 |
| WinMoreSuitThan | 슈트 A 획득 수 > 슈트 B 획득 수 |
| WinExactSuitCount | 특정 슈트 카드 정확히 N장 획득 |
| WinEachColor | 4가지 슈트 각 1장 이상 획득 |
| WinAtLeast | 트릭 적어도 N회 획득 |
| WinNoneFirstN | 처음 N트릭 획득 금지 |
| WinOddTrick | 모든 카드가 홀수인 트릭 획득 |
| WinEvenTrick | 모든 카드가 짝수인 트릭 획득 |
| WinRelativeFewer | 다른 모든 플레이어보다 트릭 적게 |
| WinRelativeMore | 다른 모든 플레이어보다 트릭 많게 |

### 통신 · 소나 토큰
- **통신 토큰**: 게임당 1회, Playing 단계의 트릭 사이에만 사용 가능. 자기 손패 카드 1장을 공개하고 최고값/최저값/유일 중 하나를 표시
- **소나 토큰**: 게임당 1회, Playing 단계에서만 사용 가능. 상대 손패의 카드 1장을 공개

---

## 게임 흐름

```
StartGame()
  ├─ 카드 분배 (DeckManager, 40장 → 4명 × 10장)
  ├─ 함장 결정 (잠수함 4번 소지자)
  ├─ 통신/소나 토큰 초기화
  ├─ [TaskSelection] MissionManager.StartTaskSelectionPhase()
  │     ├─ 태스크 풀 생성 (미션 DB 또는 fallback)
  │     ├─ 순서 토큰 부여 (태스크 3개 이상 시)
  │     └─ 함장부터 시계방향: AI 자동 선택 → 인간 UI/키보드 선택 대기
  ├─ [Playing] TrickManager.StartPlaying()
  │     ├─ 트릭마다: 선 플레이어부터 시계방향 입력
  │     │     ├─ 인간: 카드 클릭 또는 키보드 1~0
  │     │     └─ AI: RequestDecision() → OnActionReceived()
  │     ├─ 트릭 승자 판별 → MissionManager.OnTrickResolved()
  │     │     └─ 태스크 달성/실패 판정, 순서 토큰 위반 시 즉시 미션 실패
  │     └─ 손패 소진 → MissionManager.OnHandEnded() → 최종 판정
  └─ [Result] ShowResult() → 1.5초 후 에피소드 재시작
```

---

## ML-Agents 설정

### 관찰 벡터 (총 219개)

| 인덱스 | 크기 | 내용 |
|--------|------|------|
| 0~39 | 40 | 내 손패 원-핫 (카드 40장 슬롯) |
| 40~79 | 40 | 바닥 카드 원-핫 |
| 80~84 | 5 | 선 색상(Lead Suit) 원-핫 |
| 85~126 | 42 | 내 태스크 상태 (슬롯 4개 × 10 + 완료/실패 비율 2) |
| 127~130 | 4 | 플레이어별 남은 손패 수 (/ 10 정규화) |
| 131~174 | 44 | 통신 토큰 상태 (사용 여부 4 + 공개 카드 원-핫 40) |
| 175~218 | 44 | 소나 토큰 상태 (사용 여부 4 + 공개 카드 원-핫 40) |

**태스크 관찰 슬롯 구조** (슬롯당 10개, 최대 4개 태스크):

| 오프셋 | 내용 |
|--------|------|
| +0 | 태스크 타입 (type / 20) |
| +1 | requiredCount (/ 10) |
| +2 | requiredConsecutive (/ 5) |
| +3 | targetSuit (/ 4) |
| +4 | suitB (/ 4) |
| +5 | targetCard.suit (/ 4) |
| +6 | targetCard.value (/ 9) |
| +7 | orderIndex (/ 5) |
| +8 | isCompleted |
| +9 | isFailed |

### 행동 공간

| 브랜치 | 크기 | 내용 |
|--------|------|------|
| Branch[0] | 10 | 낼 카드 인덱스 (0~9) |
| Branch[1] | 2 | 통신 토큰 (0=안 함, 1=사용) |
| Branch[2] | 4 | 소나 토큰 (0=안 함, 1~3=상대 방향) |

### 보상 구조

| 이벤트 | 보상 | 대상 |
|--------|------|------|
| 태스크 달성 | +1.0 | 해당 플레이어 |
| 태스크 실패 | −1.0 | 해당 플레이어 |
| 미션 성공 | +2.0 | 팀 전원 |
| 미션 실패 | −2.0 | 팀 전원 |
| 규칙 위반 카드 | −1.0 | 해당 AI (자동 대체 후) |
| 토큰 사용 실패 | −0.1 | 해당 AI |

### 커리큘럼 학습

| 단계 | difficulty | 총 태스크 수 | 진급 조건 |
|------|-----------|------------|---------|
| Stage 1 | 3 | 3개 | 보상 ≥ 1.5 |
| Stage 2 | 5 | 5개 | 보상 ≥ 1.0 |
| Stage 3 | 7 | 7개 | 보상 ≥ 0.5 |
| Stage 4 | 9 | 9개 | — |

### 학습 실행 [Python 담당]

```bash
# 학습 시작 (Unity에서 Play 버튼 먼저)
mlagents-learn config/trainer_config.yaml --run-id=seaai_v1

# 이어서 학습
mlagents-learn config/trainer_config.yaml --run-id=seaai_v1 --resume

# 결과 확인
tensorboard --logdir results/
```

> ML-Agents에 연결하지 않으면 "Couldn't connect to trainer" 메시지가 뜨며 inference 모드로 동작합니다. 학습이 아닌 단순 플레이 테스트 시 정상입니다.

---

## Unity 씬 설정 [Unity 담당]

### Inspector 필수 연결 항목

**GameManager**
- `players` — CrewAgent 4개 (index 0 = 인간)
- `centerBoard` — 중앙 테이블 Transform
- `deckManager`, `trickManager`, `missionManager`, `communicationManager`, `uiManager`

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
| `Space` | Playing (트릭 사이) | 통신 토큰 사용 예약 |
| `Z / X / C` | Playing (트릭 사이) | 소나 토큰 (왼쪽/맞은편/오른쪽 상대) |

> **Game 뷰 포커스**: 키보드 입력은 Unity 에디터에서 Game 뷰 화면 **내부**를 클릭하여 포커스를 맞춘 후 동작합니다.

### 입력 시스템 설정

`Project Settings > Player > Active Input Handling = Both`

New Input System (`UnityEngine.InputSystem.Keyboard.current`) 기반으로 동작합니다.
