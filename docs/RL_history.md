# RL 학습 기록 (RL history)

> 스페이스 크루(The Crew: Planet Nine) 4-에이전트 MA-POCA 학습 로그.
> 결과 분석은 학습 종료 후 각 run 섹션 하단에 추가한다.

---

## 공통 — 게임 환경 모델링

- **게임**: 4인 협동 트릭테이킹. 카드 40장(색상 4종 × 1~9 = 36 + 로켓 1~4).
- **task**: 단일 타입 `WinSpecificCard` — "지정된 색깔+숫자 카드가 든 트릭을 소유자가 이긴다".
  - 색상 카드 36장 중 추첨. 대상 카드가 나온 트릭의 승자가 소유자면 완수, 아니면 **즉시 미션 실패**.
- **두 학습 정책 (한 네트워크, 페이즈별 마스킹)**:
  1. **task 드래프트(선택)**: 사령관(로켓4)부터 시계방향. 매 차례 가져가기/패스.
     - 패스 규칙: 남은 task `T` < 라운드 잔여 인원 `R(=N-cursor%N)`일 때만 패스. `T>=R`이면 강제 선택 → 모든 task 배정 보장.
  2. **트릭테이킹**: follow-suit 강제, 로켓 우선, 최고 트럼프 승리. 트릭 승자가 다음 선.
- **보상 (MA-POCA 그룹)**: task 완수 +1 / task 실패 -1(즉시 종료). 에피소드 그룹 리턴 ≈ (완수 수) − (실패 시 1).
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

- **액션 공간(고정)**: `[10, 2, 4]` — Branch0 카드/풀슬롯, Branch1 통신/(선택시)take·pass, Branch2 예비.
- **씬**: 4명 모두 `BehaviorType=Default`.

---

## Run 1 — `spacegent_v1`

- **시작**: 2026-05-30 (진행 중)
- **빌드 상태**: 통신 관측 **추가 이전** — **관측 벡터 257**.
  - 통신 토큰: 액션(Branch1)으로 사용 여부는 출력하나, **공개 카드가 관측에 없어** 동료가 못 봄 → 협력 채널로는 사실상 무효(dead action).
  - 그 외 모델링/보상/커리큘럼은 공통과 동일.
- **실행**: `mlagents-learn config/trainer_config.yaml --run-id=spacegent_v1`
- **목적**: 드래프트+트릭테이킹 기본 협력 학습 baseline (통신 정보 공유 없음).
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
- **목적**: 통신 관측 추가가 협력(드래프트·트릭)에 주는 효과 검증 — Run 1(257) 대비.
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
- **목적**: 특수규칙 헤드룸 포함한 최종 관측 레이아웃에서 베이스(트릭+드래프트) 재학습.
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
- **목적**: 임계값 하향이 정체를 푸는지 검증 (가설: 임계값은 진급 시점만 정하고 *스테이지 내 학습엔 영향 0*).
- **결과 분석** (10M 스텝 완료):
  - **Lesson 0 고정**(말기까지 0.0), Group Reward -0.272 → **-0.236** ≈ **성공률 38.2%**. Run3과 통계적으로 동일.
  - 추이: 100k −0.34 → 1M −0.22 → 2M −0.24 → 5M −0.35 → 10M −0.21. 여전히 진동, 0.05 근처도 못 감.
  - coop: assignee_success 0.382 / success_by_assignee 0.459 / success_by_helper 0.321. Entropy 0.994 → 0.396.
  - **해석 — 가설 검증됨**: 임계값을 내려도 동역학·최종 성능 불변. 보상이 0.05에 도달 못 하니 진급도 그대로 0회.
    → **임계값은 정체의 원인이 아니다**(필요조건일 뿐). 정체 원인은 드래프트 미학습(크레딧/보상).

---

## 종합 분석 (4 run 비교)

| run | obs | threshold | Lesson 진급 | Group Reward(말기) | ≈성공률 | Entropy(말기) |
|---|---|---|---|---|---|---|
| spacegent_v1 | 257 | 0.6 | 0회 | -0.228 | 38.6% | 0.41 |
| vector297 | 297 | 0.6 | 0회 | -0.237 | 38.2% | 0.41 |
| build_vector313 | 313 | 0.6 | 0회 | -0.225 | 38.7% | 0.44 |
| build_vector313_modified_yaml | 313 | **0.05** | 0회 | -0.236 | 38.2% | 0.40 |

**결론**

1. **4 run 모두 ~-0.23 / 성공률 ≈38% / Lesson 0**에 수렴. 관측 크기(257→297→313)도, 임계값(0.6→0.05)도 **레버가 아니다.**
2. **천장 측정과 정합**(`ceiling_test/ceiling_sim.py`, 2만 딜):
   - random(랜덤 owner) 25% / **coop+랜덤owner 37.8% ◄ RL이 여기** / coop+자기손패드래프트 55% / coop+best-owner(완전정보) 92%.
   - 즉 RL은 **협력 플레이는 학습, 드래프트는 거의 랜덤.** 38→55는 자기 손패만으로도 가능(학습 가능), 55→92는 정보(통신) 문제.
3. **학습 시간 문제 아님**: 2M에서 -0.16(42%)까지 갔다가 회귀, entropy 0.4로 수렴 → 나쁜 국소최적에 빠진 것이지 미학습 아님.
4. **병목 확정 = 드래프트 크레딧/보상.** obs·threshold·RL용량·학습시간 전부 기각됨.
5. **부수 관찰**: success_by_assignee(owner가 타깃 보유, 0.46~0.50) > success_by_helper(0.31~0.32) — 천장 sim(owner-holds가 더 어려움)과 부호 반대 → 지표 정의 또는 휴리스틱 차이. 2차 확인 대상.

**다음 단계 (예정)** — 상세는 [`ceiling.md`](ceiling.md) §7

- **★ 1순위 (B) 드래프트 보조 보상/크레딧** — **구현됨(빌드/run 대기)**: 배정 순간 `MissionManager.RewardDraftOwnerQuality`가
  "뽑힌 owner가 4명 중 얼마나 좋은 owner인가"(`OwnerScore` = ceiling_sim 이식)로 그룹 보상 ±`DraftShapeScale(0.3)`.
  드래프트 액션에 즉시 크레딧 → 38%(랜덤 owner) → ~55%(부분관측 천장) 학습 유도. *(C# 수정 → 재빌드 필요)*
  - 주의: N개 task면 배정마다 ±0.3 → 고-N에서 shaping이 종단을 압도할 수 있음. 현재 N=1 검증 우선, 추후 1/N 스케일/캡 검토.
  - 탐색(entropy) 점검은 후속.
- **보류 (A) 보상 이진화**: ~~미션 성공 +1/실패 -1~~ — **정체와 무관**해 보류. ①num_tasks=1에선 이미 이진이라
  Stage1 정체에 영향 0, ②N≥2에선 보상을 희소화시켜 오히려 학습에 불리(현 task별 ±1은 합리적 shaping).
  measure=reward 비단조 문제는 별도(수동 lesson 제어 등)로 다룸.
- **(C) 검증**: (B) 적용 후 Group Reward가 -0.24 → 0(↑55% 천장) 쪽으로 움직이는지, Lesson이 진급하는지 확인.
