# RL 학습 기록 (RL history)

> 스페이스 크루(The Crew: Planet Nine) 4-에이전트 MA-POCA 학습 로그.

## 📋 작성 규칙

- **모든 회차(Run)** 는 아래 3가지로 적는다:
  1. **빌드 이름 / 설정** — run-id, 커밋, *직전 회차 대비 무엇을 바꿨는지*.
  2. **목적 (왜)** — *직전 결과에서 무엇을 보고* 이 시도를 하게 됐는지 **반드시** 적는다.
  3. **결과 분석** — 학습 종료 후 채운다(핵심 지표·추이·해석).
- **"다음에 시도해볼 방법들" 은 항상 이 문서 맨 아래에 유지**한다(살아있는 백로그).
- 흐름: ①맨 아래 후보 중 하나를 골라 학습 → ②새 Run으로 승격(목적·결과 작성) → ③맨 아래 목록 갱신.
- 즉 위→아래는 **시간순 히스토리**, 맨 아래는 **항상 갱신되는 백로그**.

---

## 공통 — 게임 환경 모델링

- **게임**: 4인 협동 트릭테이킹. 카드 40장(색상 4종 × 1~9 = 36 + 로켓 1~4).
- **task**: 단일 타입 `WinSpecificCard` — "지정된 색깔+숫자 카드가 든 트릭을 소유자가 이긴다".
  - 색상 카드 36장 중 추첨. 대상 카드가 나온 트릭의 승자가 소유자면 완수, 아니면 **즉시 미션 실패**.
- **두 학습 정책 (한 네트워크, 페이즈별 마스킹)**:
  1. **task 드래프트(선택)**: 사령관(로켓4)부터 시계방향. 매 차례 가져가기/패스.
     - 패스 규칙: 남은 task `T` < 라운드 잔여 인원 `R(=N-cursor%N)`일 때만 패스. `T>=R`이면 강제 선택 → 모든 task 배정 보장.
  2. **트릭테이킹**: follow-suit 강제, 로켓 우선, 최고 트럼프 승리. 트릭 승자가 다음 선.
- **보상 (MA-POCA 그룹)**: ~~task 완수 +1~~ / task 실패 -1(즉시 종료). **Run7~ 완수 보상 정규화 `+1/N`** (전부 완수 +1.0, 실패 시 `k/N−1`). 드래프트 shaping은 Run6~ 제거(없음).
  - **리턴 해석 공식**: `평균리턴 = (평균 task완수율) − (미션실패확률)`. **N=1**이면 `리턴 = 2·성공률 − 1` (예: −0.5=25%(랜덤)/−0.24=38%/0.0=50%/+0.087=54%천장).
- **학습에서 제외/미사용**:
  - **조난신호**: AI/배치 모드에서 스킵 (인간 UI 전용).
  - **순서 토큰**: enforce·관측 준비됨, `enable_order_tokens=0`이라 미부여(현재 학습엔 없음).
  - **특수 규칙**(데드존·통신차단·사령관 결정/분배 등): 관측 슬롯만 예약(0), 미구현.
- **에피소드 흐름**: 카드 분배 → 사령관 결정 → [선택 페이즈 드래프트] → (조난신호 스킵) → [플레이 페이즈 트릭] → 미션 종결 → `EndGroupEpisode` → 재시작.

## 공통 — 학습 설정 (`config/trainer_config.yaml`)

- **trainer**: `poca` (MA-POCA, 4명 group + 중앙집중 critic)
- **hyperparameters**: batch_size 256, buffer_size 4096, lr 3.0e-4(linear), beta 0.01, epsilon 0.2, lambd 0.95, num_epoch 3
- **network**: hidden_units 256, num_layers 3, normalize false
- **reward_signals**: extrinsic gamma 0.99, strength 1.0
- **max_steps** 10,000,000 / time_horizon 128 / summary_freq 10,000
- **커리큘럼** (`num_tasks`, 진급 기준 = 그룹 평균 보상):

  | Stage | num_tasks | threshold (Run1~3) | threshold (Run4~, 천장교정) |
  |---|---|---|---|
  | 1 | 1 | ≥ 0.6 | **≥ 0.05** |
  | 2 | 2 | ≥ 0.8 | **≥ 0.0** |
  | 3 | 3 | ≥ 1.0 | **≥ 0.05** |
  | 4 | 4 | — | — |

  > ⚠️ Run1~3의 0.6/0.8/1.0 은 측정된 천장(최대 0.23)을 모두 초과 → **전 스테이지 도달 불가**(아래 종합 분석).
  > Run4부터 천장 측정(`ceiling_test/ceiling_sim.py`) 기반으로 하향. measure=reward는 부분점수 때문에
  > 난이도를 못 가리는 한계가 있어, 근본 해법은 보상을 "미션 성공 이진(+1/-1)"으로 바꾸는 것.

- **액션 공간(고정)**: `[10, 2, 4]` — Branch0 카드/풀슬롯, Branch1 통신사용/(선택시)take·pass, Branch2 통신포지션(Run6~: 0=Highest/1=Only/2=Lowest of task수트, 3=마스킹).
- **씬**: 4명 모두 `BehaviorType=Default`.

---

## Run 1 — `spacegent_v1`

- **시작**: 2026-05-30 (진행 중)
- **빌드 상태**: 통신 관측 **추가 이전** — **관측 벡터 257**.
  - 통신 토큰: 액션(Branch1)으로 사용 여부는 출력하나, **공개 카드가 관측에 없어** 동료가 못 봄 → 협력 채널로는 사실상 무효(dead action).
  - 그 외 모델링/보상/커리큘럼은 공통과 동일.
- **실행**: `mlagents-learn config/trainer_config.yaml --run-id=spacegent_v1`
- **목적 (왜)**: 최초 베이스라인. 드래프트+트릭테이킹 협력이 *애초에 학습되는지* 확인(통신 정보 공유 없음).
- **결과 분석** (10M 스텝 완료):
  - **Lesson 0(num_tasks=1)에서 끝까지 고정** — 진급 0회.
  - Group Cumulative Reward: -0.280 → **-0.228**(말기 10% 평균) ≈ **task 성공률 38.6%** (mean=2p−1).
  - 추이: ~2M에서 -0.16(≈42%)까지 개선 후 5M 이후 -0.25~-0.33로 **회귀·진동** → 지속 학습 실패.
  - Policy/Entropy 0.998 → 0.411(수렴), Value Loss ~0.43. 개인 Cumulative Reward 0(MA-POCA 정상).
  - coop: assignee_success 0.386 / success_by_assignee 0.490 / success_by_helper 0.321 / voluntary_contest 0.08.
  - **해석**: 협력 *플레이*는 학습됐으나 *드래프트*는 거의 랜덤. 천장 측정의 `coop+랜덤owner(37.8%)`와 일치.

## Run 2 — `vector297`

- **시작**: 2026-05-30 (진행 중)
- **빌드 상태**: 현재 코드 — **관측 벡터 297** (통신 관측 추가).
  - 통신 토큰: 공개 카드(suit/value/위치 최고·유일·최저)가 관측 257~280에 포함 → **동료가 통신 내용을 관측** → 협력 정보 채널로 작동.
  - 특수규칙 16칸은 예약(0). 액션 공간은 Run 1과 동일 `[10,2,4]`.
  - 관련 커밋: `7269a66`(관측 297 확장), `a3605a8`(라벨 정리).
- **실행**: `mlagents-learn config/trainer_config.yaml --run-id=vector297`
- **목적 (왜)**: Run1이 음수 보상에 정체 → *"동료가 통신 내용(공개 카드)을 관측 못 해 협력이 안 되는 것 아닌가"* 가설.
  통신 관측을 추가(257→297)해 Run1 대비 효과 검증.
- **결과 분석** (10M 스텝 완료):
  - **Lesson 0 고정**, Group Reward -0.291 → **-0.237** ≈ **성공률 38.2%**. Run1(257)과 사실상 동일.
  - coop: assignee_success 0.381 / success_by_assignee 0.500 / success_by_helper 0.313.
  - Entropy 0.988 → 0.412. 추이도 Run1과 동형(초반 개선 후 진동).
  - **해석 — 통신 관측은 효과 없음**: 257(−0.228) vs 297(−0.237) 차이 무의미.
    통신 정보 공유가 협력에 기여 못 함. 근거: ①드래프트가 병목인데 통신은 *플레이 중(트릭 사이)* 에만 가능해
    드래프트를 못 도움, ②통신 자체도 거의 안 씀(voluntary_contest 0.08).

## Run 3 — `build_vector313`

- **시작**: 2026-05-31
- **빌드 상태**: **관측 벡터 313** (특수규칙 예약 블록 16 → **32** 증설; 297 → 313).
  미션 19종 수용 헤드룸 + `--resume` 전이 깨짐 방지 위해 최종 레이아웃 선확보. 액션 공간·보상·커리큘럼 동일.
- **실행**: `mlagents-learn config/trainer_config.yaml --run-id=build_vector313`
- **목적 (왜)**: Run2도 정체. 향후 특수규칙 커리큘럼을 `--resume`로 전이하려면 관측 레이아웃이 고정돼야 함
  → 특수규칙 예약 32 포함 **최종 313 레이아웃을 확정**하고 그 위에서 베이스 성능 재확인.
- **결과 분석** (10M 스텝 완료):
  - **Lesson 0 고정**, Group Reward -0.280 → **-0.225** ≈ **성공률 38.7%**. 257/297과 동일.
  - 추이: 100k −0.29 → 1M −0.21 → **2M −0.16(피크, ≈42%)** → 5M −0.31 → 10M −0.33 (회귀·진동).
  - coop: assignee_success 0.388 / success_by_assignee 0.484 / success_by_helper 0.321. Entropy 0.989 → 0.435.
  - **해석**: 관측 확장(297→313)도 효과 없음. 베이스 성능은 obs 크기와 무관하게 ~38%.

## Run 4 — `build_vector313_modified_yaml`

- **시작**: 2026-06-01
- **빌드 상태**: Run 3과 **동일 빌드(관측 313, C# 변경 없음)**. 바뀐 것은 `trainer_config.yaml`의
  커리큘럼 **진급 임계값만** (0.6/0.8/1.0 → 0.05/0.0/0.05). 빌드 재생성 불필요(YAML은 트레이너가 런타임 로드).
- **실행**: `mlagents-learn config/trainer_config.yaml --run-id=build_vector313_modified_yaml`
- **목적 (왜)**: 천장 측정(`ceiling_sim.py`)으로 기존 임계값 0.6이 측정 천장(0.087)을 **초과 = 도달 불가**임을 발견.
  임계값 하향이 정체를 푸는지 검증 (가설: 임계값은 진급 시점만 정하고 *스테이지 내 학습엔 영향 0*).
- **결과 분석** (10M 스텝 완료):
  - **Lesson 0 고정**(말기까지 0.0), Group Reward -0.272 → **-0.236** ≈ **성공률 38.2%**. Run3과 통계적으로 동일.
  - 추이: 100k −0.34 → 1M −0.22 → 2M −0.24 → 5M −0.35 → 10M −0.21. 여전히 진동, 0.05 근처도 못 감.
  - coop: assignee_success 0.382 / success_by_assignee 0.459 / success_by_helper 0.321. Entropy 0.994 → 0.396.
  - **해석 — 가설 검증됨**: 임계값을 내려도 동역학·최종 성능 불변. 보상이 0.05에 도달 못 하니 진급도 그대로 0회.
    → **임계값은 정체의 원인이 아니다**(필요조건일 뿐). 정체 원인은 드래프트 미학습(크레딧/보상).

## Run 5 — `build_vector313_draftB`

- **시작**: 2026-06-01 (빌드/실행 대기) · 커밋 `0734ab1`
- **빌드 상태**: 관측 313(불변). **C# 변경 = 드래프트 시점 보상(B) 추가** → 재빌드 필요.
  - `MissionManager.RewardDraftOwnerQuality`: task 배정 순간, 뽑힌 owner가 4명 중 얼마나 좋은 owner인지를
    `OwnerScore`(= `ceiling_sim.py` 이식)로 평가해 그룹 보상 ±`DraftShapeScale(0.3)`. 관측·액션 불변.
  - config: 임계값 0.05/0.0/0.05 유지(+ shaping이 measure에 더해진다는 주석).
- **실행**: `mlagents-learn config/trainer_config.yaml --env=<새빌드> --run-id=build_vector313_draftB`
- **목적 (왜)**: Run1~4 종합 = 정체 병목이 **드래프트 미학습**으로 확정(RL 38% ≈ 천장 sim의 랜덤 owner 37.8%).
  드래프트는 학습 액션인데 신호가 에피소드 끝 희소 ±1뿐이라 못 배움 → **드래프트 시점에 즉시 크레딧**을 줘
  38%(랜덤 owner) → ~55%(부분관측 천장)로 끌어올릴 수 있는지 검증. (백로그에서 선택, [`ceiling.md`](ceiling.md) §7)
- **결과 분석** (10M 스텝 완료):
  - **Lesson 0 고정**(말기까지 0회 진급). Group Reward -0.333 → **-0.256**(end 10% 평균).
  - 시점별 추이: 100k −0.39 → 1M −0.31 → 3M −0.22 → **5M −0.19(피크①)** → 7M −0.32(회귀) → **9M −0.18(피크②)** → 10M −0.22. Run3 피크(2M −0.16)보다 낮고 말기 평균도 Run3(-0.225)보다 열등.
  - 말기 mean -0.256 ≈ **성공률 37.2%** — shaping 포함 기준으로 Run1~4(≈38%) 이하.
  - coop: assignee_success 0.308 → **0.348** / success_by_assignee 0.312 → 0.363 / success_by_helper 0.308 → 0.322. **전항목 Run3 말기(0.388/0.484/0.321)보다 낮음.**
  - voluntary_contest_rate: 0.064 → **0.063**(사실상 불변) — 에이전트가 shaping 신호에 반응해 드래프트 전략을 바꾸지 않음.
  - Entropy 1.005 → 0.446. Value Loss 0.339 → 0.415.
  - **해석 — 드래프트 shaping(B) 효과 없음, 오히려 소폭 열화**:
    - 피크(−0.18~−0.19)는 Run3 피크(−0.16)보다 낮고, 말기 기준 성공률도 낮아 shaping이 게임 성능을 개선하지 못함.
    - voluntary_contest_rate 불변 → 에이전트가 shaping 보상을 드래프트 전략 학습에 활용하지 못함(draft 행동 변화 없음).
    - 해석: ①OwnerScore가 에이전트의 부분관측 정보와 일치하지 않아 그레이디언트 방향이 엉킴, ②shaping(±0.3)이 에피소드 내 결과 잡음(-1~+1) 대비 너무 작아 유효 신호 희미, ③shaping 추가로 value 함수 학습 난이도 증가(Value Loss Run3 대비 유지, Baseline Loss 존재).
    - **결론: B도 정체 해결 실패.** 즉시 크레딧을 추가해도 에이전트가 드래프트에서 다른 행동을 탐색하지 않는 한 효과 없다 → 탐색 자체를 강제하는 (E)가 다음 우선 후보.

## Run 6 — `build_vector313_draftE`

- **시작**: 2026-06-02 · 빌드 대기
- **빌드 상태**: 관측 313·액션 [10,2,4] 불변. **C# 3곳 + YAML 변경.**
  1. **드래프트 shaping 제거** — `AssignTask`에서 `RewardDraftOwnerQuality` 호출 삭제. Run5의 OwnerScore 기반 ±0.3이 사령관 선점 균형을 오히려 강화했음을 확인, 완전 제거.
  2. **ε-greedy 드래프트 오버라이드** (`MissionManager.AgentSelectTask`) — env param `draft_explore_eps`로 패스 가능 위치(cursor<3)의 take/pass를 확률 ε으로 랜덤 강제. YAML 커리큘럼: progress 0→50% ε=1.0, 50→80% ε=0.5, 80→100% ε=0.0.
  3. **통신 포지션 학습** (`CommunicationToken`, `CommunicationManager`, `CrewAgent`) — Branch2를 task 타깃 수트 기준 포지션 선택(0=Highest/1=Only/2=Lowest)으로 재정의. 기존 "최고값 카드 자동 선택" 휴리스틱 대체. task 수트 카드가 없으면 Branch1=1도 마스킹.
- **실행**: `mlagents-learn config/trainer_config.yaml --env=<새빌드> --run-id=build_vector313_draftE`
- **목적 (왜)**: Run5 로그 실측(351K 에피소드)으로 **사령관(cursor=0)이 64% 선점하는 균형** 확인.
  - shaping(B)이 역효과였음: 로켓4 보유 → OwnerScore 높음 → 사령관 take 시 +보상 → 균형 강화.
  - value function이 "비사령관이 owner일 때 어떻게 되는지" 경험이 전무 → ε-greedy로 owner 분포를 50/25/12.5/12.5로 강제 다양화, value function이 "타깃 카드 보유자가 owner = 성공률 높음"을 학습하도록 유도.
  - 통신도 task 수트 외 정보 공개는 의미 없으므로 task 수트 중심으로 재설계, 포지션 선택까지 학습에 포함.
- **결과 분석** (총 20M — 원본 9.46M + resume 10.54M):

  **(1) 원본 9.46M: 유효한 학습 확인**
  - **Lesson 0 고정**. Group Reward -0.490 → **-0.374**(end 10%). Entropy 0.997 → 0.336(수렴).
  - 구간별: ε=1.0 구간 asuc ≈25%, **ε=0 시작(8M) 이후 28.9% → 31.4%**(기울기 유지). cursor 분포 개선 ✓.
  - **cursor 분포**: draftB 64/9/18/9% → **draftE 51/15/20/14%** (사령관 독점 해소 ✓)
  - coop: assignee_success 25.5% → **31.4%** / success_by_assignee 26.0% → **33.4%**

  **(2) Resume 20M: `trainer_config_draftE_resume.yaml` 설계 결함으로 붕괴**

  | 구간 | N (lesson) | asuc | group_reward | entropy |
  |---|---|---|---|---|
  | 8~10M | N=1 (ε=0) | **33.9%** | **-0.321** | 0.610 |
  | 10~12M | **N=2** ← 붕괴 | 20.1% | -0.616 | 0.511 |
  | 12~16M | N=2→3→4 | 18.3% | -0.615 | 0.354 |
  | 18~20M | N=4 | 15.3% | -0.664 | 0.171 |

  - **원인**: draftE_resume.yaml에 `num_tasks: curriculum(threshold 0.30/0.55/0.78)`이 포함됨. max_steps=20M에서 resume 시점(9.46M)의 progress = 0.473 > 0.30 → 10.01M에 N=2 전환, 11M에 N=3, 15.6M에 N=4. **Run7-A와 동일한 패턴으로 정책 붕괴.**
  - SIGTERM(kill -15) 재시작은 무관 — 체크포인트 정상 복원됨. B가 깨끗하게 이어진 게 증거.
  - **수정**: `trainer_config_draftE_resume.yaml`에서 `num_tasks: curriculum` → `num_tasks: 1`(고정)으로 교체 완료.
  - 20M 데이터 자체는 N 혼입으로 더 이상 N=1 순수 학습 결과를 보여주지 않음. **유효한 데이터는 원본 9.46M 및 10M 직전까지.**
  - **draftE의 N=1 착취 구간 최고점**: 10M 직전 **asuc 33.9%, group_reward -0.321** (2M 평균, ε=0 구간 2M 기준).

## Run 6.5 — `build_vector313_draftE2`

- **시작**: 2026-06-04 · 빌드 재사용(draftE 동일 빌드, C# 변경 없음) · `config/trainer_config_draftE2.yaml` 신규
- **빌드 상태**: 관측 313·액션 [10,2,4]·C# 코드 모두 draftE와 동일. YAML만 변경.
- **실행**: `mlagents-learn config/trainer_config_draftE2.yaml --env=<draftE빌드> --run-id=build_vector313_draftE2`
- **목적 (왜)**: draftE(9.46M) ε=0 착취 구간이 1.46M뿐 → asuc 33.9%에서 기울기 유지한 채 중단. 55% 천장 도달 가능성이 미확인. resume 시도했으나 `trainer_config_draftE_resume.yaml`에 `num_tasks: curriculum`이 잔존해 10.01M에 N=2 강제 전환 → 정책 붕괴. 체크포인트도 18.5M부터밖에 없어 롤백 불가.
  - **교훈**: resume YAML에 `measure: progress` 커리큘럼을 남기면, max_steps 변경 시 progress 재계산으로 의도치 않은 N 전환 발생. **resume config는 모든 env 파라미터를 고정값으로 교체하거나, curriculum 구조 유지 시 값만 0/고정으로 덮어쓸 것.**
  - 동일 빌드로 새 run-id 사용 → draftE 오염 데이터와 완전 분리. ε 탐색 구간은 원본과 동일(5M/8M 절대 step), 착취는 8M→20M = **12M** 확보.
- **결과 분석**: (진행 중 — 완료 후 작성)

| 구간 | ε | group_reward | asuc | success_by_assignee | entropy |
|---|---|---|---|---|---|
| 2M | 1.0 | — | — | — | — |
| 5M | 1.0→0.5 | — | — | — | — |
| 8M | 0.5→**0.0** | — | — | — | — |
| 14M | 0.0 | — | — | — | — |
| **20M** | **0.0** | — | — | — | — |

**기대 지표**: ε=0 착취 12M 기준, asuc ≥ 38%(baseline 초과) → 55% 천장 근접 여부 확인.

---

## Run 6.7 — `build_vector313_commE`

- **시작**: 2026-06-04 · C# 신규 빌드 필요 · `config/trainer_config_commE.yaml` 신규
- **빌드 상태**: 관측 313·액션 [10,2,4] 불변. **C# 2곳 변경 (MissionManager + CrewAgent).**
  1. **통신 메트릭 3종** (`MissionManager.cs`) — 에피소드 종료 시 TensorBoard 송출:
     - `coop/comm_used_rate`: 통신한 플레이어 수 / 총 플레이어 수 (0=전혀 안 씀, 1=전원 사용)
     - `coop/assignee_communicated`: task 담당자 중 누군가가 통신했는가 (0/1)
     - `coop/comm_reveal_in_winning_trick`: 공개 카드가 task 완수 트릭에 포함됐는가 (0/1)
  2. **ε-greedy 통신 강제** (`CrewAgent.cs`) — env param `comm_explore_eps`. `canComm=true` + 에이전트가 스킵 선택 시 확률 ε으로 강제 사용. Branch2(포지션)는 정책 유지.
- **실행**: `mlagents-learn config/trainer_config_commE.yaml --env=<commE빌드> --run-id=build_vector313_commE`
- **목적 (왜)**: run7_B 20M에서 `voluntary_contest_rate`(헬퍼 행동 지표)가 완전 flat, `success_by_helper`(23.9%)와 `success_by_assignee`(35.6%) 갭이 12M 착취에도 안 좁혀짐 → 통신이 팀 조율에 기여 못 하는 것으로 추정. 그러나 기존 코드에 통신 전용 메트릭이 없어서 "못 쓰는건지 안 쓰는건지" 분리 불가. 드래프트 ε-greedy와 동일한 논리: 에이전트가 자발적으로 안 쓰면 강제로 경험 생성 → value function이 "통신 → 팀 반응 → 성공" 학습.
  - **대조군**: draftE2 (draft ε만, comm ε 없음) — 동시 진행 중.
- **ε 스케줄**:
  | 구간 | draft ε | comm ε | 의미 |
  |---|---|---|---|
  | 0→5M | 1.0 | 1.0 | 둘 다 탐색 |
  | 5→8M | 0.5 | 1.0 | draft 수렴 중, comm 계속 강제 |
  | 8→14M | **0.0** | 0.5 | draft 착취, comm 반강제 |
  | 14→20M | 0.0 | **0.0** | 순수 정책 — comm 자발적 채택 여부 확인 (6M) |
- **결과 분석**: (진행 중 — 완료 후 작성)

  | 구간 | comm ε | comm_used | assignee_comm | reveal_in_trick | asuc | helper |
  |---|---|---|---|---|---|---|
  | 5M | 1.0 | — | — | — | — | — |
  | 8M | 1.0→0.5 | — | — | — | — | — |
  | 14M | 0.5→**0.0** | — | — | — | — | — |
  | **20M** | **0.0** | — | — | — | — | — |

  **핵심 관찰 포인트**:
  - `comm_used_rate` ≫ 0 → 메트릭 정상 작동 확인
  - `comm_reveal_in_winning_trick` 상승 → 공개 카드가 실제 완수에 기여하기 시작
  - `success_by_helper` 상승 → 팀원이 통신 정보를 활용해 지원
  - 14M 이후(ε=0 구간) `comm_used_rate` 유지 → 자발적 통신 학습 성공

---

## Run 7 — 멀티-task 정규화 + 두 전략 병렬 (A: 커리큘럼 / B: 혼합)

- **시작**: 2026-06-02 · 빌드 대기 · 커밋(코드) 작업분
- **빌드 상태**: 관측 313·액션 [10,2,4] **불변**(예비 슬롯 재사용). **C# 3곳 + YAML 2개.**
  1. **보상 정규화** (`MissionManager.CompleteTask`) — task 완수 보상을 `+1 → +1/N`으로 정규화(`currentTaskCount`).
     실패 -1은 유지(즉시 종료 신호). 전부 완수 시 +1.0(N무관) → 에피소드 리턴 범위 ≈ [-1,+1].
  2. **N 관측 노출** (`MissionManager.GetSpecialRuleObs`) — 특수규칙 obs `[20] = N/10`. 정책의 N별 전략 전환 보조.
  3. **혼합 N 샘플링** (`MissionManager.StartTaskSelectionPhase`) — env `mix_tasks=1`이면 N을 가중 추출
     (N=1:40%/2:35%/3:15%/4:10%). Option B 트리거.
  4. **YAML**: (A) `trainer_config.yaml` num_tasks 커리큘럼을 `measure: reward → progress`로 전환
     (Stage 경계 0.30/0.55/0.78, ε 스케줄과 정렬). (B) `trainer_config_B.yaml` 신규 = `mix_tasks:1` 고정.
- **실행**:
  - A) `mlagents-learn config/trainer_config.yaml   --env=<새빌드> --run-id=run_optionA_curriculum`
  - B) `mlagents-learn config/trainer_config_B.yaml --env=<새빌드> --run-id=run_optionB_mixed`
- **목적 (왜)**: N=1만으론 연구 결과 불가 → 멀티-task 일반화 필요. 두 병목을 먼저 제거:
  - (a) **보상 스케일 불일치** — 비정규화 시 N=4 리턴이 N=1의 4배 → advantage 스케일 N의존. `/N` 정규화로 통일.
  - (b) **measure=reward 비단조 + 도달불가** — `ceiling_sim.py` 재측정(정규화): N=1 +0.087 / N=2 **-0.355** / N=3 **-0.575** / N=4 **-0.674**.
    정규화로 "N↑→리턴↑" 병리는 해소(이제 단조 감소)됐으나, N≥2 천장이 음수 + ε이 리턴을 더 끌어내려 **양수 reward threshold는 도달 불가** 확정.
    → Option A는 `progress` 기반 진급으로 전환(커리큘럼 정지 방지). Option B는 커리큘럼 자체를 제거하고 혼합 N으로 일반 정책 직접 학습.
  - coop 휴리스틱은 N≥2 조율이 약해 위 천장을 **과소평가** → 팀원 MCTS 천장이 실제 상한. 확정되면 A의 N≥2 단계를 reward+음수threshold로 되돌릴 수 있음.
- **결과 분석** (`build_run7_A` / `build_run7_B` 각 10M 완료):

  **⚠️ 핵심 전제**: ε 커리큘럼(progress 0→50% ε=1.0, 50→80% ε=0.5, 80→100% ε=0.0)이 두 run 공통. N별 착취 구간이 실제로 얼마나 확보됐는지가 성패를 가른다.

  **(1) Run7-A (`build_run7_A`, 커리큘럼, 10M 완료):** num_tasks 커리큘럼 전환 실측:
  - N=1: 0→3M / N=2: 3→5.5M / N=3: 5.5→7.8M / N=4: 7.8→10M
  - ε 전환: ε=1.0 0→6M / ε=0.5 6→9M / ε=0.0 9→10M

  | 구간 | N | ε | group_reward | asuc | entropy |
  |---|---|---|---|---|---|
  | 0→3M | 1 | 1.0 | -0.50 | 25.2% | 1.69 |
  | 3→5.5M | 2 | 1.0→0.5 | -0.82 | 18.1% | 1.13 |
  | 5.5→7.8M | 3 | 0.5→0.0 | -0.96 | 14.9% | 1.49↑ |
  | 7.8→10M | 4 | 0.0 | -1.03 | **14.3%** | **1.53** |

  - **N=1 착취 구간 = 0**: ε=1.0이 5M까지 유지되는데 N=1은 3M에서 끝남 → N=1은 **전 구간이 강제 랜덤 드래프트**. "N=1 먼저 잘 배운다"는 전제가 데이터로 붕괴.
  - Entropy 1.53으로 **미수렴** — N이 계속 바뀌어 정책이 한 번도 안정되지 못함. N=3 진입 시 오히려 상승.
  - **설계 실패 확정**: ε과 N 커리큘럼 타이밍이 완전히 어긋남. A 재설계 없이 재실행은 무의미.

  **(2) Run7-B (`build_run7_B`, 혼합 N, 20M 완료):** mix_tasks=1로 N=1:40%/2:35%/3:15%/4:10% 전 구간 적용. ε 전환: ε=1.0(0~5M) → ε=0.5(5~8M) → **ε=0.0(8M~20M, 착취 12M 확보)**. `trainer_config_B_resume.yaml` 설계 정상(`num_tasks: 1` 고정 → N 혼입 없음).

  | 구간 | ε | group_reward | asuc | success_by_assignee | entropy |
  |---|---|---|---|---|---|
  | 2M | 1.0 | -0.740 | 19.5% | 21.9% | 1.611 |
  | 6M | 0.5 | -0.724 | 20.6% | 24.3% | 1.095 |
  | 8M | 0.5→0 | -0.685 | 22.8% | 29.2% | 0.876 |
  | 10M | 0.0 | -0.607 | 27.5% | 36.8% | 0.709 |
  | 12M | 0.0 | -0.601 | 27.9% | 34.7% | 0.872↑ |
  | 16M | 0.0 | -0.594 | 28.2% | 34.5% | 1.034↑ |
  | 18M | 0.0 | -0.581 | 29.0% | 36.7% | 0.923 |
  | **20M** | **0.0** | **-0.583** | **28.9%** | **35.9%** | **0.866** |

  - **8M 이후 상승, 10M 이후 plateau**: success_by_assignee 10M 36.8% → 이후 34~37% 범위에서 진동. asuc도 28~29%에서 안정.
  - **entropy 역행(10M→0.709, 16M→1.034, 20M→0.866)**: resume 직후 일시적 상승(policy 재탐색 가능성) 후 재하강. 완전 수렴엔 미도달.
  - **group_reward -0.58~-0.60 유지**: 수학적 기대치 범위(N 혼합 구조). 학습 불량 아님.
  - **success_by_assignee 피크 36~37%**: draftE의 33.4%(9.46M) 및 33.9%(10M 직전) 초과. 혼합 N 노출 효과.
  - 20M에서도 entropy 0.87 → 아직 완전 수렴 전, 하지만 추가 착취 대비 한계점 접근.

  **A vs B 최종 비교**:
  | 지표 | draftE 유효(10M 직전) | run7_A(10M) | run7_B(20M) |
  |---|---|---|---|
  | group_reward(말기) | -0.321 | -1.025 | **-0.583**† |
  | asuc(말기) | **33.9%** | 14.3%↓ | 28.9% |
  | success_by_assignee | **33.9%** | 18.6% | 35.9%‡ |
  | entropy(말기) | 0.610 | 1.534(미수렴) | 0.866 |
  | ε=0 착취 기간 | 2M | 1M(@N=4) | **12M** |

  †B의 -0.583은 N 혼합 기대치 (N=1 전용 draftE와 직접 비교 불가)
  ‡B의 success_by_assignee는 혼합 N 기준. 단일 task 완수율 측면에서 draftE 초과.

  **결론**:
  - **A 실패 확정**: ε↘ 전 N 증가로 착취 구간 없음. 설계 근본 재검토 필요.
  - **B 수렴 근접**: 12M 착취 후 plateau. success_by_assignee ~36% 유지, entropy 미수렴(0.87). 추가 착취 대비 한계점. draftE 대비 N=1 성능은 비교 불가하지만 개별 task 완수율은 초과.
  - **배운 점**: resume 시 `num_tasks: curriculum`은 반드시 `num_tasks: 1`(고정)으로 교체 필요 — progress 재계산으로 N이 강제 전환됨.
  - 보상 정규화·N관측·혼합샘플링 모두 설계대로 정상 작동 확인.
  - 분석 도구: `summarize_csv.py`(CSV 추세) · `parse_multitask.py <run>`(N별 성공률) · `analyze_logs.py`(cursor 분포)

---

## 종합 분석 (Run 1~5)

| run | obs | 변경점 | Lesson 진급 | Group Reward(말기) | assignee_success(말기) | Entropy(말기) |
|---|---|---|---|---|---|---|
| spacegent_v1 | 257 | 베이스라인 | 0회 | -0.228 | 38.6% | 0.41 |
| vector297 | 297 | 통신 관측 추가 | 0회 | -0.237 | 38.1% | 0.41 |
| build_vector313 | 313 | obs 313 확정 | 0회 | -0.225 | 38.8% | 0.44 |
| build_vector313_modified_yaml | 313 | threshold 0.05 | 0회 | -0.236 | 38.2% | 0.40 |
| **build_vector313_draftB** | 313 | **드래프트 shaping +0.3** | **0회** | **-0.256** | **34.8%** | **0.45** |

**결론**

1. **5 run 모두 Lesson 0 고정**. 관측(257→313), 임계값(0.6→0.05), 드래프트 즉시 보상(shaping B) 모두 **레버가 아니다.**
2. **천장 측정과 정합**(`ceiling_test/ceiling_sim.py`, 2만 딜):
   - random(랜덤 owner) 25% / **coop+랜덤owner 37.8% ◄ RL이 여기** / coop+자기손패드래프트 55% / coop+best-owner(완전정보) 92%.
   - 즉 RL은 **협력 플레이는 학습, 드래프트는 거의 랜덤.** Run5에서 shaping을 줘도 voluntary_contest_rate(드래프트 탐색) 불변 → 탐색 자체가 없어서 shaping 신호가 쓸모없음.
3. **학습 시간 문제 아님**: 2~3M에서 -0.16~-0.19까지 갔다가 회귀, entropy 0.4로 수렴 → 나쁜 국소최적에 빠진 것.
4. **병목 재확정 = 드래프트 탐색 부재.** 크레딧(shaping)을 줘도 탐색을 안 하면 의미 없음. → 탐색 강제(E)가 다음 방향.
5. **부수 관찰**: Run5에서 shaping 추가 후 assignee_success(34.8%)가 Run1~4(38~39%) 이하 → shaping이 value 함수 노이즈를 높여 소폭 열화 가능성.

### 드래프트 cursor 분포 실측 (build_vector313_draftB, 351,021 에피소드)

> `Result/logs_analysis/analyze_logs.py`로 Player-0~7.log 파싱. 가설 검증 목적.

| cursor | 비율 | 성공률 | 의미 |
|---|---|---|---|
| 0 (사령관) | **64.3%** | 36.1% | 사령관이 자발적으로 먼저 취함 |
| 1 | 8.9% | 34.0% | |
| 2 | 17.9% | 30.0% | |
| 3 (강제) | **8.9%** | 34.2% | 강제 위치인데 가장 낮은 비율 |

- **원래 가설(cursor=3 지배) 틀림.** 실제는 **사령관(cursor=0)이 64% 선점**.
- 사령관 성공률(36.1%) < 랜덤 owner 천장(37.8%) → 사령관이 최적 owner가 아님에도 항상 취하는 균형.
- 분포가 초반/중반/후반 동일 → Run1~5 내내 드래프트 전략 변화 없음 확인.
- **함장별 이상 패턴**: captain=1/2일 때 cursor=1,3 완전 0%, captain=0/3일 때 cursor=2 완전 0%. 특정 (captain, cursor) 쌍에서 특정 플레이어가 절대 취하지 않는 구조적 패턴 → 원인 미상, 추가 조사 필요.

## 다음에 시도해볼 방법들  (항상 맨 아래 유지 · 살아있는 백로그)

> 위 회차 결과를 보고 하나 골라 학습 → 새 Run으로 승격(목적·결과 작성) → 이 목록 갱신. 상세 근거: [`ceiling.md`](ceiling.md) §7
>
> **현재 상황**: draftE2(N=1 draft ε, 진행 중) + commE(draft ε + comm ε, 진행 중) 병렬 실행.
> draftE2 = 대조군(통신 없음), commE = 실험군(통신 강제 포함). 둘 다 20M 목표.

- ~~**(J) draftE N=1 순수 착취 재시도**~~ → `build_vector313_draftE2`로 fresh start 실행 중(20M, draft ε만).

- **(K) 통신 ε-greedy + draft ε 동시** ← **실행 중(`build_vector313_commE`)**. *왜*: run7_B 20M에서 통신 전용 메트릭 없어 미확인. 신규 메트릭 3종(comm_used_rate, assignee_communicated, comm_reveal_in_winning_trick) 추가. draft ε(draftE2와 동일) + comm ε(8M까지 1.0, 14M까지 0.5, 이후 0.0). 대조군 draftE2와 비교해 통신 강제의 효과 측정.

- ~~**(I) Run7-B resume + ε=0 고정**~~ → 실행 완료(20M). success_by_assignee ~36%, plateau. 추가 착취 대비 한계점 도달.

- **(H) ε 스케줄 ↔ 커리큘럼 정합 재설계** ← A 완전 재설계 필요 시. Run7-A에서 ε과 N 커리큘럼 타이밍 불일치로 붕괴. 해법: ε을 N=1 단계 안에서 1.0→0 완료 후 N 진급.

- ~~**(E) 드래프트 ε-greedy 탐색**~~ → Run6/draftE 실행됨. ε=0 도달 후 asuc 0.25→0.33 상승(유효 신호). (I)로 이어짐.
- ~~**(G) 멀티-task 정규화 + 2전략 병렬**~~ → Run7 실행됨. 정규화/N관측/혼합샘플링 정상 작동 확인. A 실패/B 유망 확정.
- ~~**(F) 멀티-task shaping 스케일링(1/N)**~~ → 종단 보상 정규화(완수 +1/N)로 대체·해결(Run7).
- **(보류) (A) 보상 이진화** — `/N` 정규화로 measure 비단조 해소. MCTS 천장 확정 후 reward+음수threshold로 복귀 검토.
- **(장기) 드래프트 단계 정보공유/통신** — 부분관측 천장 55% → 완전정보 92% 갭 해소용. 현재 통신은 플레이 중에만 가능.
- ~~**(B) 드래프트 shaping**~~ → Run5에서 기각.
- ~~**(D) shaping anneal**~~ → B 실패로 불필요.
