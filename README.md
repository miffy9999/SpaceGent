# Divergent — 딥 씨 크루 보드게임 AI

4인 협동 트릭테이킹 카드게임 **The Crew** 기반.  
1명의 인간 플레이어 + 3명의 ML-Agents AI가 팀을 이뤄 미션을 완수한다.

---

## 프로젝트 구조

```
Assets/
├── Scripts/
│   ├── Core/
│   │   ├── Card.cs            # 카드 데이터 (4색 1~9, 잠수함 1~4)
│   │   ├── CardDisplay.cs     # 카드 시각화
│   │   ├── DeckManager.cs     # 덱 생성 / 셔플 / 분배
│   │   ├── GameManager.cs     # 싱글턴, 초기화 순서 총괄
│   │   ├── MissionManager.cs  # 태스크 배분 / 달성 판정 / 보상
│   │   ├── TaskCard.cs        # 태스크 데이터 구조
│   │   └── TrickManager.cs    # 턴 진행 / 트릭 승자 판별
│   └── AI/
│       └── CrewAgent.cs       # ML-Agents Agent (인간·AI 공용)
├── Prefabs/
│   ├── Card_prefab.prefab
│   ├── Player Agent.prefab
│   └── Table Environment.prefab
└── Sprites/                   # 카드·토큰·미션 이미지
```

---

## 게임 규칙 요약

- 덱: 색상 4종(Yellow·Blue·White·Pink) × 9장 + 잠수함(조커) 4장 = **총 40장**
- 4인 플레이, 1인당 10장 분배
- **잠수함 4번** 소지자가 첫 선(함장)
- Follow Suit 규칙: 선 색상이 있으면 반드시 그 색을 내야 함
- 잠수함은 모든 색을 이김 (높은 숫자 우선)
- 미션: 각 플레이어에게 태스크가 배정되며, 팀 전원이 달성해야 승리

---

## ML-Agents 인터페이스 (Python 작업자용)

### Behavior Name
```
CrewAgent
```

### 관찰 벡터 (Observation Space) — 총 127개

| 인덱스 | 크기 | 내용 |
|--------|------|------|
| 0 ~ 39 | 40 | 내 손패 원-핫 (카드 인덱스 기준) |
| 40 ~ 79 | 40 | 현재 바닥에 깔린 카드 원-핫 |
| 80 ~ 84 | 5 | 선 색상(Lead Suit) 원-핫 `[Yellow, Blue, White, Pink, Submarine]` |
| 85 ~ 124 | 40 | 내 태스크 목표 카드 원-핫 |
| 125 | 1 | 내 태스크 완료 여부 (0 or 1) |
| 126 | 1 | 내 태스크 실패 여부 (0 or 1) |
| 127 ~ 130 | 4 | 플레이어별 남은 손패 장수 (÷10 정규화) |
| 131 ~ 134 | 4 | 플레이어별 통신 토큰 사용 여부 (0 or 1) |
| 135 ~ 174 | 40 | 공개된 통신 카드 원-핫 (전체 플레이어 합산) |
| 175 ~ 178 | 4 | 플레이어별 소나 토큰 사용 여부 (0 or 1) |
| 179 ~ 218 | 40 | 소나로 공개된 카드 원-핫 (전체 플레이어 합산) |

#### 카드 인덱스 계산 방식
```
색상 카드: suit * 9 + (value - 1)   →  0~35
잠수함:    36 + (value - 1)          →  36~39
```
예: Blue(1) 5 → 1*9 + 4 = **13**

---

### 행동 공간 (Action Space)

| 항목 | 값 |
|------|----|
| 타입 | Discrete |
| Branch 수 | 3 |
| Branch 0 크기 | 10 (낼 카드 인덱스 0~9) |
| Branch 1 크기 | 2 (0=통신 안 함, 1=통신 토큰 사용) |
| Branch 2 크기 | 4 (0=소나 안 함, 1~3=상대 플레이어 상대 인덱스) |

> 4인 기준 1인당 최대 10장. 범위를 벗어난 행동은 페널티 후 0번 카드로 강제 처리.

---

### 보상 구조 (Reward)

| 이벤트 | 보상 | 대상 |
|--------|------|------|
| 자기 태스크 달성 | **+1.0** | 해당 플레이어 |
| 자기 태스크 실패 | **-1.0** | 해당 플레이어 |
| 미션 전체 성공 | **+2.0** | 팀 전원 |
| 미션 전체 실패 | **-2.0** | 팀 전원 |
| 규칙 위반 (Follow Suit 불이행) | **-1.0** | 해당 플레이어 |
| 범위 초과 행동 | **-1.0** | 해당 플레이어 |

| 통신 토큰 잘못 사용 (이미 사용/공개할 카드 없음) | **-0.1** | 해당 플레이어 |

| 소나 토큰 잘못 사용 | **-0.1** | 해당 플레이어 |

에피소드당 예상 보상 범위: **-3.2 ~ +3.0**

---

### 에피소드 종료 조건

- 전원 손패 소진 (10트릭 완료)
- `EndEpisode()` 호출 후 자동으로 새 게임 시작

---

### 플레이어 구성

| 인덱스 | 종류 | Behavior Type |
|--------|------|---------------|
| 0 | 인간 플레이어 | `Heuristic Only` |
| 1 | AI | `Default` (학습) |
| 2 | AI | `Default` (학습) |
| 3 | AI | `Default` (학습) |

> 학습 전 테스트 시에는 1~3도 `Heuristic Only`로 설정 후 키보드로 조작.

---

### 권장 trainer_config.yaml 출발점

```yaml
behaviors:
  CrewAgent:
    trainer_type: ppo
    hyperparameters:
      batch_size: 128
      buffer_size: 2048
      learning_rate: 3.0e-4
      beta: 0.01          # 엔트로피 계수 (협동게임은 탐색이 중요)
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
    network_settings:
      normalize: false
      hidden_units: 256
      num_layers: 2
    reward_signals:
      extrinsic:
        gamma: 0.99
        strength: 1.0
    max_steps: 5000000
    time_horizon: 64
    summary_freq: 10000
```

---

## 실행 방법

### Unity (게임)
1. `SampleScene` 열기
2. `GameManager` 오브젝트 인스펙터에서 players·centerBoard·매니저 할당
3. 각 AI 에이전트 **Behavior Parameters → Space Size: 127** 확인
4. Play

### Python (학습)
```bash
pip install mlagents
mlagents-learn config/trainer_config.yaml --run-id=crew_run_01
# Unity에서 Play 버튼 누르면 학습 시작
```

### TensorBoard 모니터링
```bash
tensorboard --logdir results/
```
