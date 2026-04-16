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
  - Follow suit, 잠수함(트럼프) 우선, 선 결정, 트릭 승자 판별
  - 함장(잠수함 4번 소지자)부터 시계방향으로 태스크 선택 (함장 포함)
  - 통신 토큰: 트릭 사이에만 사용 가능 (트릭 진행 중 불가)
  - 순서 토큰: 번호가 있는 태스크는 낮은 번호 순서대로 달성해야 함
- **BGA 방식 태스크 선택 단계**: 함장부터 시계방향으로 태스크 카드 직접 선택
- **ML-Agents AI**: PPO 기반 AI 에이전트 (관찰 벡터 219개, 이산 행동 3 브랜치)
- **커리큘럼 학습**: 난이도 3→5→7→9 단계적 증가
- **2D 손패 UI**: 인간 플레이어 카드 클릭 선택 지원
- **배달의민족 주아체** 한글 폰트 적용

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
│   │   └── TaskPoolItem.prefab        # 태스크 선택 버튼
│   └── Scripts/
│       ├── AI/
│       │   └── CrewAgent.cs           # ML-Agents 에이전트 (관찰/행동/휴리스틱)
│       └── Core/
│           ├── Card.cs                # 카드 데이터 (수트 4종 + 잠수함)
│           ├── CardDisplay.cs         # 3D 카드 비주얼
│           ├── CommunicationManager.cs# 통신/소나 토큰 관리
│           ├── CommunicationToken.cs  # 통신 토큰 (자기 카드 공개)
│           ├── DeckManager.cs         # 덱 생성 및 카드 분배
│           ├── GameManager.cs         # 싱글턴 — 플레이어/매니저 참조 허브
│           ├── GamePhase.cs           # 게임 단계 (Setup/TaskSelection/Playing/Result)
│           ├── GameUIManager.cs       # HUD + 태스크 선택 UI
│           ├── HandCardUI.cs          # 손패 카드 버튼 컴포넌트
│           ├── Mission.cs             # 미션 데이터 (난이도/태스크 분배)
│           ├── MissionDatabase.cs     # 미션 ScriptableObject 컬렉션
│           ├── MissionManager.cs      # 태스크 선택 + 트릭 판정 + 보상
│           ├── SonarToken.cs          # 소나 토큰 (상대 카드 공개)
│           ├── TaskCard.cs            # 태스크 데이터 + 순서 토큰(orderIndex)
│           └── TrickManager.cs        # 게임 흐름 + 트릭 로직
├── config/                            # [Python 담당] ML-Agents trainer yaml
└── results/                           # [Python 담당] 학습 결과 / TensorBoard 로그
```

---

## 게임 규칙 (딥 씨 크루 기준)

### 기본 트릭 테이킹
- 잠수함(트럼프) 카드는 어떤 색 카드도 이긴다
- 선(lead) 색상을 가지고 있으면 반드시 그 색을 내야 한다
- 잠수함 카드끼리는 숫자가 높은 쪽이 이긴다
- 트릭 승자가 다음 트릭의 선이 된다
- **잠수함 4번 소지자 = 함장**, 첫 트릭의 선

### 태스크 선택 단계
1. 미션에 따라 태스크 카드 풀이 생성된다
2. **함장부터** 시계방향으로 모든 플레이어가 1장씩 번갈아 선택한다
3. 일부 태스크에는 **순서 토큰(1·2·3…)** 이 붙어 있어, 반드시 낮은 번호 순서대로 달성해야 한다
4. 모든 태스크 선택 완료 후 트릭 게임 시작

### 통신 토큰
- 각 플레이어는 게임당 1회 통신 가능
- **트릭과 트릭 사이**에만 사용 가능 (첫 카드가 올라간 후에는 불가)
- 자기 손패의 카드 1장을 공개하고, 그 색에서 **최고값 / 최저값 / 유일한 장** 중 하나를 표시

---

## 게임 흐름

```
StartGame()
  ├─ 카드 분배 (DeckManager, 40장 → 4명 × 10장)
  ├─ 함장 결정 (잠수함 4번 소지자)
  ├─ [TaskSelection] MissionManager.StartTaskSelectionPhase()
  │     ├─ 태스크 풀 생성 + 순서 토큰 부여
  │     └─ 함장부터 시계방향: AI 자동 선택 / 인간 UI 선택
  ├─ [Playing] 트릭 진행
  │     ├─ 트릭 승자 판별 → MissionManager.OnTrickResolved()
  │     │     └─ 순서 토큰 위반 시 즉시 미션 실패
  │     └─ 손패 소진 → MissionManager.OnHandEnded()
  └─ [Result] ShowResult() → 1.5초 후 재시작
```

---

## ML-Agents 설정

### 관찰 벡터 (총 219개)

| 인덱스 | 크기 | 내용 |
|--------|------|------|
| 0~39 | 40 | 내 손패 원-핫 |
| 40~79 | 40 | 바닥 카드 원-핫 |
| 80~84 | 5 | 선 색상(Lead Suit) 원-핫 |
| 85~126 | 42 | 내 태스크 상태 (목표 카드 + 완료/실패 비율) |
| 127~130 | 4 | 플레이어별 남은 손패 수 |
| 131~174 | 44 | 통신 토큰 상태 |
| 175~218 | 44 | 소나 토큰 상태 |

### 행동 공간

| 브랜치 | 크기 | 내용 |
|--------|------|------|
| Branch[0] | 10 | 낼 카드 인덱스 |
| Branch[1] | 2 | 통신 토큰 (0=안 함, 1=사용) |
| Branch[2] | 4 | 소나 토큰 (0=안 함, 1~3=상대 방향) |

### 보상 구조

| 이벤트 | 보상 | 대상 |
|--------|------|------|
| 태스크 달성 | +1.0 | 해당 플레이어 |
| 태스크 실패 | -1.0 | 해당 플레이어 |
| 미션 성공 | +2.0 | 팀 전원 |
| 미션 실패 | -2.0 | 팀 전원 |

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

---

## Unity 씬 설정 [Unity 담당]

### UI 자동 생성

Unity Editor에서 `SeaAI > Create Game UI` 메뉴 실행.

생성 후 인스펙터에서 수동 할당:
- `GameManager.players` — CrewAgent 4개
- `GameManager.centerBoard` — 중앙 테이블 Transform
- `GameManager.uiManager` — 자동 연결됨 (확인만)

### 인간 플레이어 조작

| 입력 | 동작 |
|------|------|
| 카드 클릭 (UI) | 카드 선택 |
| 숫자키 1~0 | 카드 선택 (키보드) |
| Space | 통신 토큰 사용 (트릭 사이에만 유효) |
| Z / X / C | 소나 토큰 (왼쪽/맞은편/오른쪽 상대) |

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
