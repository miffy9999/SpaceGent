# SeaAI — The Crew: Deep Sea (Unity 6.3)

4인 협동 트릭 테이킹 카드 게임. [The Crew: 심해 탐험](https://boardgamegeek.com/boardgame/324856/the-crew-mission-deep-sea) 룰을 기반으로 Unity ML-Agents AI와 BGA(Board Game Arena) 스타일 UI를 구현한 프로젝트.

---

## 주요 기능

- **완전한 트릭 테이킹 룰**: Follow suit, 잠수함(트럼프), 선 결정, 트릭 승자 판별
- **BGA 방식 태스크 선택 단계**: 함장 기준 시계 방향으로 플레이어가 태스크 카드를 직접 선택
- **ML-Agents AI**: PPO 기반 AI 에이전트 (관찰 벡터 219개, 이산 행동 3 브랜치)
- **커리큘럼 학습**: 난이도 3→5→7→9 단계적 증가
- **2D 손패 UI**: 인간 플레이어 카드 클릭 선택 지원
- **배달의민족 주아체** 한글 폰트 적용
- **MCP for Unity** 연동 (CoplayDev/unity-mcp)

---

## 프로젝트 구조

```
Assets/
├── Editor/
│   └── CreateGameUIEditor.cs     # SeaAI/Create Game UI 메뉴 — Canvas 전체 자동 생성
├── Prefabs/
│   ├── HandCard.prefab           # 인간 플레이어 손패 카드 UI
│   ├── TaskItem.prefab           # 태스크 목록 항목
│   └── TaskPoolItem.prefab       # 태스크 선택 버튼 (BGA 풀)
├── Scripts/
│   ├── AI/
│   │   └── CrewAgent.cs          # ML-Agents 에이전트 (관찰/행동/휴리스틱)
│   └── Core/
│       ├── Card.cs               # 카드 데이터 (수트 4종 + 잠수함)
│       ├── CardDisplay.cs        # 3D 카드 비주얼
│       ├── CardSpriteMapping.cs  # 카드 스프라이트 매핑
│       ├── CommunicationManager.cs # 통신/소나 토큰 관리
│       ├── CommunicationToken.cs
│       ├── DeckManager.cs        # 덱 생성 및 카드 분배
│       ├── GameManager.cs        # 싱글턴 — 플레이어/매니저 참조 허브
│       ├── GamePhase.cs          # 게임 단계 열거형 (Setup/TaskSelection/Playing/Result)
│       ├── GameUIManager.cs      # HUD + 태스크 선택 오버레이 UI
│       ├── HandCardUI.cs         # 손패 카드 버튼 컴포넌트
│       ├── Mission.cs            # 미션 데이터 (난이도/태스크 분배)
│       ├── MissionDatabase.cs    # 미션 ScriptableObject 컬렉션
│       ├── MissionManager.cs     # 태스크 선택 단계 + 트릭 판정 + 보상
│       ├── SonarToken.cs
│       ├── TaskCard.cs           # 태스크 데이터 (WinSpecific/TrickCount/First/None)
│       └── TrickManager.cs       # 게임 흐름 제어 + 트릭 로직
└── TextMesh Pro/
    └── Fonts/
        └── BMJUA_ttf SDF.asset   # 배달의민족 주아체
```

---

## 게임 흐름

```
StartGame()
  └─ 카드 분배 (DeckManager)
  └─ 함장 결정 (잠수함 4번 소지자)
  └─ [TaskSelection] MissionManager.StartTaskSelectionPhase()
        └─ 태스크 풀 생성 (미배정)
        └─ AI 자동 선택 → 인간 UI 선택 → 반복
        └─ 풀 소진 시 TrickManager.StartPlaying() 호출
  └─ [Playing] 트릭 진행
        └─ 트릭 승자 판별 → MissionManager.OnTrickResolved()
        └─ 손패 소진 시 MissionManager.OnHandEnded()
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
| 85~126 | 42 | 내 태스크 상태 (목표 카드 + 완료/실패 수) |
| 127~130 | 4 | 플레이어별 남은 손패 수 |
| 131~174 | 44 | 통신 토큰 상태 |
| 175~218 | 44 | 소나 토큰 상태 |

### 행동 공간

| 브랜치 | 크기 | 내용 |
|--------|------|------|
| Branch[0] | 10 | 낼 카드 인덱스 |
| Branch[1] | 2 | 통신 토큰 (0=안 함, 1=사용) |
| Branch[2] | 4 | 소나 토큰 (0=안 함, 1~3=상대 인덱스) |

### 보상 구조

| 이벤트 | 보상 | 대상 |
|--------|------|------|
| 태스크 달성 | +1.0 | 해당 플레이어 |
| 태스크 실패 | -1.0 | 해당 플레이어 |
| 미션 성공 | +2.0 | 팀 전원 |
| 미션 실패 | -2.0 | 팀 전원 |
| 규칙 위반 | -1.0 | 해당 플레이어 |
| 잘못된 토큰 사용 | -0.1 | 해당 플레이어 |

### 커리큘럼 학습

| 단계 | difficulty | 진급 조건 (보상) |
|------|-----------|-----------------|
| Stage 1 | 3 | ≥ 1.5 |
| Stage 2 | 5 | ≥ 1.0 |
| Stage 3 | 7 | ≥ 0.5 |
| Stage 4 | 9 | — |

### 학습 실행

```bash
mlagents-learn config/trainer_config.yaml --run-id=seaai_v1
# Unity에서 Play 버튼 → 학습 시작

mlagents-learn config/trainer_config.yaml --run-id=seaai_v1 --resume

tensorboard --logdir results/
```

---

## UI 자동 생성

Unity Editor에서 `SeaAI > Create Game UI` 메뉴 실행.

생성 후 인스펙터에서 확인:
- `GameManager.uiManager` — 자동 연결됨
- `GameManager.players` — CrewAgent 4개 수동 할당
- `GameManager.centerBoard` — 중앙 테이블 Transform 수동 할당

---

## 인간 플레이어 조작

| 입력 | 동작 |
|------|------|
| 카드 클릭 (UI) | 카드 선택 |
| 숫자키 1~0 | 카드 선택 (키보드) |
| Space | 통신 토큰 사용 |
| Z / X / C | 소나 토큰 (왼쪽/맞은편/오른쪽) |

---

## MCP for Unity 설정

`Window > MCP for Unity`에서 서버 시작 후 `.claude.json`에 API Key 설정:

```json
"mcpServers": {
  "UnityMCP": {
    "type": "http",
    "url": "http://127.0.0.1:8080/mcp",
    "headers": { "X-API-Key": "<EditorPrefs의 MCPForUnity.ApiKey 값>" }
  }
}
```

패키지 설치: `Window > Package Manager > + > Add package from git URL`
```
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main
```
