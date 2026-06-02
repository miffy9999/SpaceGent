#!/usr/bin/env python3
"""
WinSpecificCard 과제 천장 측정 (Unity 없이 순수 파이썬 재현)
==========================================================

목적: RL(MA-POCA)이 num_tasks=1에서 그룹보상 ~ -0.22(성공률 ~39%)로 정체.
     "과제가 구조적으로 어려운가" vs "RL이 못 배우는가"를 가르기 위해
     게임 규칙을 충실히 재현하고 비-RL 정책의 성공률(천장)을 측정한다.

규칙 (Unity TrickManager/MissionManager와 동일):
  - 카드 40장: 색 4종 × 1~9(=36) + 로켓 1~4(트럼프).
  - 4인, 각 10장. 함장 = 로켓4 보유자. 함장이 첫 트릭 선.
  - follow-suit: 리드 슈트 있으면 반드시 냄. 로켓이 첫 카드면 로켓이 리드.
  - 승자: 로켓 있으면 최고 로켓 / 없으면 리드 슈트 최고값. 승자가 다음 선.
  - 과제(WinSpecificCard): 색 카드 1장(타깃)을 owner가 "그 카드가 든 트릭"에서 이겨야 성공.

정책:
  - random            : 모두 합법 카드 무작위.
  - coop              : 협력 휴리스틱(은닉정보 — 각자 자기 손패 + 테이블 + 공개타깃/owner만 사용).
                        owner=무작위 → "RL이 도달해야 할 현실적 천장".
  - coop_best_owner   : 같은 휴리스틱을 owner 후보 4명 각각으로 돌려 하나라도 성공하면 성공
                        → "이 딜이 누군가에 의해 이길 수 있는가" = 구조적 천장.

사용: python ceiling_sim.py [딜수]   (기본 20000)
"""
import random
import sys
from collections import defaultdict

N_PLAYERS = 4
ROCKET = 4          # suit 인덱스 4 = 로켓
COLORS = [0, 1, 2, 3]


def make_deck():
    deck = [(s, v) for s in COLORS for v in range(1, 10)]      # 36 색 카드
    deck += [(ROCKET, v) for v in range(1, 5)]                 # 4 로켓
    return deck


def deal(rng):
    deck = make_deck()
    rng.shuffle(deck)
    hands = [deck[i * 10:(i + 1) * 10] for i in range(N_PLAYERS)]
    return hands


def commander_of(hands):
    for i, h in enumerate(hands):
        if (ROCKET, 4) in h:
            return i
    return 0


def beats(a, b, lead):
    """a가 b를 이기나 (lead = 리드 슈트). Unity Beats와 동일."""
    a_r, b_r = a[0] == ROCKET, b[0] == ROCKET
    if a_r and not b_r:
        return True
    if a_r and b_r:
        return a[1] > b[1]
    if not a_r and b_r:
        return False
    a_l, b_l = a[0] == lead, b[0] == lead
    if a_l and not b_l:
        return True
    if not a_l:
        return False
    return a[1] > b[1]


def legal_moves(hand, lead):
    """리드 슈트가 손에 있으면 그 슈트만, 없으면 전부."""
    if lead is None:
        return list(hand)
    same = [c for c in hand if c[0] == lead]
    return same if same else list(hand)


def trick_winner(cards, players, lead):
    """cards[i]를 players[i]가 냄. 승자 player와 승리 카드 반환."""
    best_i = 0
    for i in range(1, len(cards)):
        if beats(cards[i], cards[best_i], lead):
            best_i = i
    return players[best_i], cards[best_i]


# ───────────────────────────── 정책 ─────────────────────────────
def card_strength(c, lead):
    """낮을수록 안 이김 (off<lead<rocket)."""
    if c[0] == ROCKET:
        return 200 + c[1]
    if c[0] == lead:
        return 100 + c[1]
    return c[1]


def lowest(cards, lead):
    return min(cards, key=lambda c: card_strength(c, lead))


def highest(cards, lead):
    return max(cards, key=lambda c: card_strength(c, lead))


def lowest_winning(cards, table, lead):
    """현재 테이블 최강을 이기는 카드 중 가장 약한 것. 없으면 None."""
    if not table:
        return highest(cards, lead)            # 리드 상황: 가장 강한 카드로 확보
    best = table[0]
    for c in table[1:]:
        if beats(c, best, lead):
            best = c
    winners = [c for c in cards if beats(c, best, lead)]
    return min(winners, key=lambda c: card_strength(c, lead)) if winners else None


def coop_choice(pidx, hand, lead, table_cards, table_players,
                owner, target, owner_played_winning):
    """협력 휴리스틱. 은닉정보(자기 손패+테이블+공개 owner/target)만 사용."""
    moves = legal_moves(hand, lead)
    if len(moves) == 1:
        return moves[0]
    target_on_table = target in table_cards
    i_hold_target = target in hand
    is_owner = (pidx == owner)

    if is_owner:
        if target_on_table:                                  # 내 타깃이 테이블에 → 무조건 획득
            w = lowest_winning(moves, table_cards, lead)
            return w if w else lowest(moves, lead)
        if i_hold_target:
            # 마지막 카드거나 리드+강하면 타깃을 냄, 아니면 보존(낮은 비타깃)
            if len(hand) == 1:
                return target if target in moves else lowest(moves, lead)
            if not table_cards and target in moves and target[1] >= 7:
                return target                                # 강한 타깃 리드
            non_t = [c for c in moves if c != target]
            return lowest(non_t, lead) if non_t else moves[0]
        # 타깃을 도우미가 가짐 → 강한 카드 보존(낮은 카드 냄), 테이블에 타깃 오면 위에서 처리
        return lowest(moves, lead)

    # ── 도우미 ──
    if i_hold_target:
        if owner_played_winning and target in moves:         # owner가 이기는 중 → 타깃 릴리스
            return target
        non_t = [c for c in moves if c != target]            # 아니면 타깃 보존
        return lowest(non_t, lead) if non_t else target
    # 타깃 미보유: 타깃 트릭이면 절대 안 이김 / 평소 양보(낮은 카드)
    return lowest(moves, lead)


def owner_score(hand, target):
    """자기 손패만으로 'WinSpecificCard owner로서 얼마나 좋은가' 추정 (은닉정보)."""
    c, v = target
    rockets = [x for x in hand if x[0] == ROCKET]
    suit = [x for x in hand if x[0] == c]
    beaters = [x for x in suit if x[1] > v]          # 타깃을 슈트 내에서 이길 카드
    score = 2.0 * len(rockets) + (max((r[1] for r in rockets), default=0)) * 0.2
    if target in hand:
        score += 1.0 + v * 0.4                       # 타깃 보유: 값 높을수록 자력 승리 쉬움
    else:
        score += 1.2 * len(beaters)                  # 미보유: 타깃 트릭을 이길 카드 수
    return score


def draft_owner(hands, target, threshold):
    """함장부터 시계방향 take/pass. 자기 점수≥threshold면 take, 마지막은 강제(T=1<R)."""
    commander = commander_of(hands)
    order = [(commander + k) % N_PLAYERS for k in range(N_PLAYERS)]
    for idx, p in enumerate(order):
        forced = (idx == N_PLAYERS - 1)
        if forced or owner_score(hands[p], target) >= threshold:
            return p
    return order[-1]


def play_episode(hands, owner, target, policy, rng):
    """한 핸드를 끝까지 플레이. 성공(owner가 타깃 트릭 승리) 여부 반환."""
    hands = [list(h) for h in hands]
    lead_player = commander_of(hands)
    for _ in range(10):                                       # 10트릭
        lead = None
        table_cards, table_players = [], []
        # owner가 이번 트릭에 이미 냈고 현재 이기는 중인지 (도우미 릴리스 판단용)
        order = [(lead_player + k) % N_PLAYERS for k in range(N_PLAYERS)]
        for pidx in order:
            owner_played_winning = False
            if table_cards and owner in table_players:
                # 현재 테이블 최강이 owner 카드인가
                best_i = 0
                for i in range(1, len(table_cards)):
                    if beats(table_cards[i], table_cards[best_i], lead):
                        best_i = i
                owner_played_winning = (table_players[best_i] == owner)

            hand = hands[pidx]
            if policy == "random":
                c = rng.choice(legal_moves(hand, lead))
            else:                                             # coop
                c = coop_choice(pidx, hand, lead, table_cards, table_players,
                                owner, target, owner_played_winning)
            hand.remove(c)
            table_cards.append(c)
            table_players.append(pidx)
            if lead is None:
                lead = c[0]                                   # 첫 카드가 리드 슈트(로켓이면 로켓)

        winner, _ = trick_winner(table_cards, table_players, lead)
        if target in table_cards:                             # 타깃이 나온 트릭
            return winner == owner                            # 즉시 결판
        lead_player = winner
    return False                                              # (타깃은 반드시 나오므로 도달 X)


def run(n_deals, policy, owner_mode, rng, threshold=None):
    """owner_mode: 'random' | 'best' | 'selfdraft'. 성공률 + 분해 통계 반환."""
    succ = 0
    by_value = defaultdict(lambda: [0, 0])      # value -> [success, total]
    by_holder = defaultdict(lambda: [0, 0])     # 'owner'/'helper' -> [s, t]
    for _ in range(n_deals):
        hands = deal(rng)
        # 타깃 = 색 카드 1장 무작위
        target = (rng.choice(COLORS), rng.randint(1, 9))
        holder = next(i for i in range(N_PLAYERS) if target in hands[i])

        if owner_mode == "best":
            ok = any(play_episode(hands, o, target, policy, rng) for o in range(N_PLAYERS))
            owner = None
        elif owner_mode == "selfdraft":
            owner = draft_owner(hands, target, threshold)
            ok = play_episode(hands, owner, target, policy, rng)
        else:
            owner = rng.randrange(N_PLAYERS)
            ok = play_episode(hands, owner, target, policy, rng)

        succ += ok
        by_value[target[1]][0] += ok
        by_value[target[1]][1] += 1
        if owner is not None:
            key = "owner" if holder == owner else "helper"
            by_holder[key][0] += ok
            by_holder[key][1] += 1
    return succ / n_deals, by_value, by_holder


# ───────────────── 다중 task: 드래프트 + 그룹 리턴(커리큘럼 measure) ─────────────────
def draft_assign(hands, targets, threshold):
    """함장부터 시계방향 take/pass. pass 허용 iff T<R. 반환: {target: owner}."""
    commander = commander_of(hands)
    pool = list(targets)
    assign = {}
    k = 0
    while pool and k < 4 * N_PLAYERS:
        p = (commander + k) % N_PLAYERS
        R = N_PLAYERS - (k % N_PLAYERS)              # 이번 사이클 남은 플레이어(자신 포함)
        T = len(pool)
        best_t = max(pool, key=lambda t: owner_score(hands[p], t))
        forced = not (T < R)                         # pass 허용 iff T<R, 아니면 강제 take
        if forced or owner_score(hands[p], best_t) >= threshold:
            assign[best_t] = p
            pool.remove(best_t)
        k += 1
    for t in pool:                                   # 안전망
        assign[t] = commander
    return assign


def coop_choice_multi(pidx, hand, lead, table_cards, table_players, owner_of, unresolved):
    """다중 task 협력 휴리스틱(근사). 은닉정보만 사용."""
    moves = legal_moves(hand, lead)
    if len(moves) == 1:
        return moves[0]
    # 현재 트릭 승자(진행 중)
    cur_winner = None
    if table_cards:
        bi = 0
        for i in range(1, len(table_cards)):
            if beats(table_cards[i], table_cards[bi], lead):
                bi = i
        cur_winner = table_players[bi]
    present = [t for t in table_cards if t in unresolved]
    # 내가 테이블 위 어떤 미해결 타깃의 owner면 → 그 트릭 획득
    if any(owner_of[t] == pidx for t in present):
        w = lowest_winning(moves, table_cards, lead)
        return w if w else lowest(moves, lead)
    my_t = [t for t in unresolved if t in hand]
    if my_t:
        # 그 타깃 owner가 지금 이기는 중이면 릴리스
        if cur_winner is not None:
            rel = [t for t in my_t if owner_of[t] == cur_winner and t in moves]
            if rel:
                return rel[0]
        # 내가 owner인 내 타깃을 자력으로(마지막/강한 리드)
        mine = [t for t in my_t if owner_of[t] == pidx and t in moves]
        if mine:
            t = max(mine, key=lambda x: x[1])
            if len(hand) == 1 or (not table_cards and t[1] >= 7):
                return t
        non_t = [c for c in moves if c not in unresolved]   # 아니면 타깃 보존
        return lowest(non_t, lead) if non_t else lowest(moves, lead)
    return lowest(moves, lead)                               # 타깃 미보유 → 양보


def play_episode_multi(hands, assign):
    """그룹 리턴 반환: task 완수 +1/N, 첫 실패 시 -1 즉시 종료. 전부 성공 여부도 반환.
    Unity MissionManager와 동일하게 완수 보상을 N으로 정규화(전부 완수 → +1.0, N무관).
    실패 패널티(-1)는 정규화하지 않음(즉시 종료 신호)."""
    hands = [list(h) for h in hands]
    owner_of = dict(assign)
    unresolved = set(assign.keys())
    n_tasks = max(1, len(owner_of))   # 정규화 분모
    lead_player = commander_of(hands)
    ret = 0.0
    for _ in range(10):
        lead = None
        table_cards, table_players = [], []
        order = [(lead_player + k) % N_PLAYERS for k in range(N_PLAYERS)]
        for pidx in order:
            c = coop_choice_multi(pidx, hands[pidx], lead, table_cards,
                                  table_players, owner_of, unresolved)
            hands[pidx].remove(c)
            table_cards.append(c)
            table_players.append(pidx)
            if lead is None:
                lead = c[0]
        winner, _ = trick_winner(table_cards, table_players, lead)
        present = [t for t in table_cards if t in unresolved]
        fail = False
        for t in present:
            if owner_of[t] == winner:
                ret += 1.0 / n_tasks
                unresolved.discard(t)
            else:
                fail = True
        if fail:
            ret -= 1
            return ret, False
        if not unresolved:
            return ret, True
        lead_player = winner
    return ret, (len(unresolved) == 0)


def run_multi(n_deals, n_tasks, threshold, rng):
    """N개 task. 평균 그룹 리턴(커리큘럼 measure)과 미션 성공률 반환."""
    total_ret, full_succ = 0, 0
    for _ in range(n_deals):
        hands = deal(rng)
        targets = set()
        while len(targets) < n_tasks:
            targets.add((rng.choice(COLORS), rng.randint(1, 9)))
        assign = draft_assign(hands, list(targets), threshold)
        ret, ok = play_episode_multi(hands, assign)
        total_ret += ret
        full_succ += ok
    return total_ret / n_deals, full_succ / n_deals


def pct(x):
    return f"{100*x:5.1f}%"


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 20000
    rng = random.Random(42)
    print(f"=== WinSpecificCard 천장 측정 (deals={n:,}) ===\n")

    configs = [
        ("random   (랜덤 정책, 랜덤 owner)",   "random", "random"),
        ("coop     (협력 휴리스틱, 랜덤 owner)", "coop",   "random"),
        ("coop+best (협력, best-of-4 owner=구조적 천장)", "coop", "best"),
    ]
    results = {}
    for label, pol, om in configs:
        rate, byv, byh = run(n, pol, om, rng)
        results[label] = (rate, byv, byh)
        print(f"[{label}]  전체 성공률 = {pct(rate)}")
        if byh:
            for k in ("owner", "helper"):
                if byh[k][1]:
                    s, t = byh[k]
                    print(f"     타깃 {k:6s} 보유 시: {pct(s/t)}  ({s:,}/{t:,})")
        line = "     값별: " + "  ".join(
            f"{v}:{pct(byv[v][0]/byv[v][1]) if byv[v][1] else '  -  '}" for v in range(1, 10))
        print(line + "\n")

    # ── 자기 손패만 쓰는 드래프트(=RL이 실제로 접근 가능한 정보)의 현실적 천장 ──
    print("[coop + self-hand draft] (각자 자기 패만 보고 take/pass — 임계값 스윕)")
    best = (0.0, None)
    for th in (2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0):
        rate, _, _ = run(n, "coop", "selfdraft", rng, threshold=th)
        marker = ""
        if rate > best[0]:
            best = (rate, th)
            marker = "  ← best"
        print(f"     threshold={th:>4}: {pct(rate)}{marker}")
    print(f"   → 자기손패 드래프트 최고: {pct(best[0])} (threshold={best[1]})\n")

    # ── 다중 task(N=1~4) 천장: 정규화 평균 그룹 리턴(=커리큘럼 measure)과 미션 성공률 ──
    #   보상 정규화(완수 +1/N, 실패 -1)로 리턴이 N↑→단조 감소. N≥2 천장이 음수가 되어
    #   양수 reward threshold는 도달 불가 → Option A 커리큘럼은 measure: progress로 전환했음.
    th = best[1] or 6.0
    print(f"[다중 task 천장] coop + self-hand draft (threshold={th}) — 정규화(완수 +1/N)")
    print(f"   {'N':>2} {'평균리턴(measure)':>16} {'미션성공률':>10}   (옛 reward threshold)")
    cur_th = {1: 0.05, 2: 0.0, 3: 0.05, 4: None}   # 정규화 전 yaml 잠정치 — 참고용
    for nt in (1, 2, 3, 4):
        mret, msucc = run_multi(n, nt, th, rng)
        cur = cur_th[nt]
        flag = ""
        if cur is not None:
            flag = "  ✗ reward로는 도달불가" if cur > mret else "  ○ 도달가능"
        print(f"   {nt:>2} {mret:>16.3f} {pct(msucc):>10}   (옛yaml={cur}){flag}")
    print("   ※ N≥2 천장이 음수 → 양수 reward threshold 무용. 현재 Option A는 progress 기반 진급.\n")

    print("─" * 60)
    print("해석 가이드:")
    print("  - coop(랜덤owner) ≈ RL(39%)  → 과제/협력 휴리스틱이 그 수준 = RL 탓 아님(과제·보상 재설계).")
    print("  - coop(랜덤owner) ≫ RL(39%)  → RL이 협력을 못 배움(희소보상/크레딧/탐색).")
    print("  - coop+best ≫ coop(랜덤owner) → 드래프트(누가 owner냐)가 결정적 → 드래프트 학습이 핵심.")
    print("  - 값별 성공률이 높은 값(7~9)↑ 낮은 값(1~3)↓ → 저가 타깃이 구조적으로 어려움(0-task/특수처리 검토).")


if __name__ == "__main__":
    main()
