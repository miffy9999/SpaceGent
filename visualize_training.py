#!/usr/bin/env python3
"""
ML-Agents 학습 시각화 (tensorboard 없이 tfevents 직접 파싱)
============================================================

tensorflow / tensorboard / protobuf 패키지가 전혀 없어도 동작한다.
TFEvent(tfevents) 파일을 바이트 단위로 직접 읽어(자체 varint 디코더)
학습 곡선을 PNG로, 원본 수치를 CSV로 저장한다.

폴더 구조 (이 스크립트는 프로젝트 루트에서 실행)
------------------------------------------------
    visualize_training.py            <- 이 파일
    Result/
        build_vector313/
            events.out.tfevents.....     (여러 개여도 됨 — 합쳐서 처리)
        spacegent_v1/
            events.out.tfevents.....
        vector297/
            ...

사용법
------
    # Result/ 아래 모든 run을 자동 처리 (기본)
    python visualize_training.py

    # 특정 폴더 지정 (여러 개 가능)
    python visualize_training.py Result/build_vector313 results/

각 run 폴더에 결과가 생성된다:
    <run>/_OVERVIEW_dashboard.png   <- 핵심 6지표 종합
    <run>/_DIAGNOSTIC_4key.png      <- 건강검진 4지표
    <run>/_COOP_dashboard.png       <- coop/* (협력 계측)
    <run>/_HFSM_dashboard.png       <- hfsm/* (도우미 행동 분포)
    <run>/_SUMMARY.txt              <- 시작10% -> 끝10% 평균 (복붙용)
    <run>/_extracted/all_metrics_{long,wide}.csv
    <run>/_extracted/plots/*.png    <- 지표별 개별 그래프

그리고 처리한 run이 2개 이상이면 루트(공통 부모)에 비교 그래프:
    _COMPARE_group_reward.png       <- run별 Group Cumulative Reward 오버레이

MA-POCA 주의
------------
4-에이전트 그룹 학습이라 개별 `Environment/Cumulative Reward`는 0이고,
실제 학습 신호는 `Environment/Group Cumulative Reward`다. 대시보드는 이걸 우선 표시한다.

필요 패키지: pandas, matplotlib  (pip install pandas matplotlib)
"""

import os
import sys
import glob
import gc
import struct

import pandas as pd
import matplotlib
matplotlib.use("Agg")          # 헤드리스(GPU 서버)에서도 PNG 저장 가능
import matplotlib.pyplot as plt


# ===========================================================================
# varint / wire-type 디코딩 (protobuf 패키지 불필요 — 직접 구현)
# ===========================================================================
def _read_varint(buf, pos):
    result = 0
    shift = 0
    while True:
        b = buf[pos]
        pos += 1
        result |= (b & 0x7F) << shift
        if not (b & 0x80):
            return result, pos
        shift += 7


def _read_tag(buf, pos):
    t, pos = _read_varint(buf, pos)
    return t >> 3, t & 0x7, pos     # (field_number, wire_type, pos)


def _skip(buf, pos, wt):
    if wt == 0:                      # varint
        _, pos = _read_varint(buf, pos)
    elif wt == 1:                    # 64-bit
        pos += 8
    elif wt == 2:                    # length-delimited
        ln, pos = _read_varint(buf, pos)
        pos += ln
    elif wt == 5:                    # 32-bit
        pos += 4
    else:
        raise ValueError(f"bad wire type {wt}")
    return pos


# ===========================================================================
# TFRecord 컨테이너:  [uint64 length][uint32 crc][payload][uint32 crc]
#   CRC 검증은 생략(데이터 추출만). payload = Event protobuf.
# ===========================================================================
def read_tfrecords(path):
    with open(path, "rb") as f:
        data = f.read()
    n = len(data)
    off = 0
    while off + 12 <= n:
        (length,) = struct.unpack_from("<Q", data, off)
        off += 12                                  # length(8) + length-crc(4)
        if off + length + 4 > n:
            break                                  # 잘린 마지막 레코드 → 종료
        yield data[off:off + length]
        off += length + 4                          # payload + payload-crc


# ===========================================================================
# Event protobuf 파싱
#   Event:         wall_time(1,double)  step(2,int64)  summary(5,message)
#   Summary:       value(1,repeated message)
#   Summary.Value: tag(1,string)  simple_value(2,float32)
# ===========================================================================
def _parse_value(buf):
    pos, end, tag, val = 0, len(buf), None, None
    while pos < end:
        fn, wt, pos = _read_tag(buf, pos)
        if fn == 1 and wt == 2:                    # tag(string)
            ln, pos = _read_varint(buf, pos)
            tag = buf[pos:pos + ln].decode("utf-8", "replace")
            pos += ln
        elif fn == 2 and wt == 5:                  # simple_value(float32)
            val = struct.unpack_from("<f", buf, pos)[0]
            pos += 4
        else:
            pos = _skip(buf, pos, wt)
    return tag, val


def _parse_summary(buf):
    pos, end, out = 0, len(buf), []
    while pos < end:
        fn, wt, pos = _read_tag(buf, pos)
        if fn == 1 and wt == 2:                    # value(message)
            ln, pos = _read_varint(buf, pos)
            t, v = _parse_value(buf[pos:pos + ln])
            if t is not None and v is not None:
                out.append((t, v))
            pos += ln
        else:
            pos = _skip(buf, pos, wt)
    return out


def parse_event(buf):
    pos, end = 0, len(buf)
    step = wall = None
    scalars = []
    while pos < end:
        fn, wt, pos = _read_tag(buf, pos)
        if fn == 1 and wt == 1:                    # wall_time(double)
            wall = struct.unpack_from("<d", buf, pos)[0]
            pos += 8
        elif fn == 2 and wt == 0:                  # step(int64)
            step, pos = _read_varint(buf, pos)
        elif fn == 5 and wt == 2:                  # summary(message)
            ln, pos = _read_varint(buf, pos)
            scalars.extend(_parse_summary(buf[pos:pos + ln]))
            pos += ln
        else:
            pos = _skip(buf, pos, wt)
    if not scalars or step is None:
        return None
    return step, wall, scalars


# ===========================================================================
# run 폴더 탐색
# ===========================================================================
TFEVENT_PATTERNS = ("events.out.tfevents.", "events_out_tfevents_")


def _is_tfevent_file(name):
    return name.endswith(".tfevents") or ".tfevents." in name \
        or any(name.startswith(p) for p in TFEVENT_PATTERNS)


def find_event_files(run_dir):
    pats = ["events.out.tfevents.*", "events_out_tfevents_*", "*.tfevents.*"]
    files = []
    for p in pats:
        files += glob.glob(os.path.join(run_dir, "**", p), recursive=True)
        files += glob.glob(os.path.join(run_dir, p))
    files = sorted(set(files))

    def sort_key(fp):                              # 파일명 안의 unix ts로 시간 정렬
        name = os.path.basename(fp)
        nums = [int(s) for s in name.replace("-", "_").split(".")[0].split("_")
                if s.isdigit() and len(s) >= 9]
        return (nums[0] if nums else 0, name)

    return sorted(files, key=sort_key)


def iter_run_dirs(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames
                       if not d.startswith(".") and d != "_extracted"]
        if any(_is_tfevent_file(fn) for fn in filenames):
            yield dirpath


# MA-POCA: Group Cumulative Reward를 최우선으로
KEY_METRICS = [
    "Environment/Group Cumulative Reward",
    "Environment/Cumulative Reward",
    "Environment/Episode Length",
    "Policy/Entropy",
    "Losses/Policy Loss",
    "Losses/Value Loss",
]


def safe(s):
    return "".join(c if c.isalnum() or c in "-_." else "_" for c in s)


def _ma(series, denom=80):
    """이동평균 (점이 50개 초과일 때만)."""
    if len(series) <= 50:
        return None
    w = max(5, len(series) // denom)
    return series.rolling(w, min_periods=1).mean(), w


# ===========================================================================
# run 1개 처리
# ===========================================================================
def process_run(run_dir):
    run_id = os.path.basename(os.path.normpath(run_dir))
    files = find_event_files(run_dir)
    if not files:
        print(f"[!] '{run_dir}' tfevents 없음 — 건너뜀")
        return None

    print(f"\n=== [{run_id}] tfevents {len(files)}개 ===")
    rows = []
    for fp in files:
        cnt, steps = 0, []
        try:
            for payload in read_tfrecords(fp):
                ev = parse_event(payload)
                if ev is None:
                    continue
                step, wall, scalars = ev
                for tag, val in scalars:
                    rows.append({"tag": tag, "step": step, "value": val, "wall_time": wall})
                    cnt += 1
                steps.append(step)
        except Exception as e:
            print(f"  [!] {os.path.basename(fp)} 읽기 오류: {e}")
        if steps:
            print(f"  {os.path.basename(fp)}: step {min(steps):,}~{max(steps):,} ({cnt:,} pts)")

    df = pd.DataFrame(rows)
    if df.empty:
        print("  [!] 추출 데이터 없음 — 건너뜀")
        return None

    # 이어서 학습(--resume) 시 (tag, step) 중복 → 마지막 기록 채택
    df = df.sort_values(["tag", "step", "wall_time"]).drop_duplicates(["tag", "step"], keep="last")

    out_dir = os.path.join(run_dir, "_extracted")
    plots_dir = os.path.join(out_dir, "plots")
    os.makedirs(plots_dir, exist_ok=True)

    df[["tag", "step", "value", "wall_time"]].to_csv(
        os.path.join(out_dir, "all_metrics_long.csv"), index=False)
    df.pivot_table(index="step", columns="tag", values="value", aggfunc="last") \
      .sort_index().to_csv(os.path.join(out_dir, "all_metrics_wide.csv"))

    tags = sorted(df["tag"].unique())
    print(f"  합계: 지표 {len(tags)}종, step {df['step'].min():,}~{df['step'].max():,}, {len(df):,} pts")

    def series(tag_name):
        if tag_name not in set(tags):
            return None
        s = df[df["tag"] == tag_name].sort_values("step")
        return s if not s.empty else None

    def plot_on(ax, tag_name, title):
        s = series(tag_name)
        if s is None:
            ax.text(0.5, 0.5, f"{tag_name}\n(N/A)", ha="center", va="center",
                    transform=ax.transAxes, fontsize=10, color="gray")
            ax.set_title(title, fontsize=11); ax.set_xticks([]); ax.set_yticks([])
            return
        ax.plot(s["step"], s["value"], lw=0.7, color="#1f77b4", alpha=0.5)
        ma = _ma(s["value"])
        if ma is not None:
            mavg, w = ma
            ax.plot(s["step"], mavg, lw=2, color="#d62728", label=f"MA(w={w})")
            ax.legend(fontsize=8, loc="best")
        ax.set_title(title, fontsize=11); ax.set_xlabel("Step"); ax.grid(True, alpha=0.3)

    # 지표별 개별 그래프
    for tag in tags:
        g = df[df["tag"] == tag].sort_values("step")
        plt.figure(figsize=(11, 5))
        plt.plot(g["step"], g["value"], lw=0.8, color="#1f77b4", alpha=0.55)
        ma = _ma(g["value"], denom=100)
        if ma is not None:
            mavg, w = ma
            plt.plot(g["step"], mavg, lw=2, color="#d62728", alpha=0.85, label=f"moving avg (w={w})")
            plt.legend(fontsize=9)
        plt.title(tag, fontsize=12); plt.xlabel("Step"); plt.ylabel("Value")
        plt.grid(True, alpha=0.3); plt.tight_layout()
        plt.savefig(os.path.join(plots_dir, safe(tag) + ".png"), dpi=120)
        plt.close()

    # 건강검진 4지표
    fig, ax = plt.subplots(2, 2, figsize=(14, 9))
    plot_on(ax[0][0], "Environment/Group Cumulative Reward", "1) Group Reward - is it learning? (MA-POCA)")
    plot_on(ax[0][1], "Policy/Entropy", "2) Entropy - exploration vs convergence")
    plot_on(ax[1][0], "Losses/Value Loss", "3) Value Loss - training stability")
    # 4) Value Estimate vs Extrinsic Reward
    ax4 = ax[1][1]
    ve, er = series("Policy/Extrinsic Value Estimate"), series("Policy/Extrinsic Reward")
    if ve is None and er is None:
        ax4.text(0.5, 0.5, "Extrinsic Value/Reward\n(N/A)", ha="center", va="center",
                 transform=ax4.transAxes, fontsize=10, color="gray")
        ax4.set_xticks([]); ax4.set_yticks([])
    else:
        lines = []
        if er is not None:
            l1, = ax4.plot(er["step"], er["value"], lw=1.2, color="#2ca02c", label="Extrinsic Reward")
            lines.append(l1)
        if ve is not None:
            axr = ax4.twinx()
            l2, = axr.plot(ve["step"], ve["value"], lw=1.2, color="#ff7f0e", label="Value Estimate")
            axr.set_ylabel("Value Estimate", color="#ff7f0e", fontsize=9)
            axr.tick_params(axis="y", labelcolor="#ff7f0e")
            lines.append(l2)
        ax4.set_ylabel("Extrinsic Reward", color="#2ca02c", fontsize=9)
        ax4.tick_params(axis="y", labelcolor="#2ca02c")
        ax4.legend(lines, [ln.get_label() for ln in lines], fontsize=8, loc="best")
    ax4.set_title("4) Value Estimate vs Extrinsic Reward", fontsize=11)
    ax4.set_xlabel("Step"); ax4.grid(True, alpha=0.3)
    plt.suptitle(f"ML-Agents  {run_id}  |  health check  |  {df['step'].max():,} steps", fontsize=13, y=1.00)
    plt.tight_layout()
    plt.savefig(os.path.join(run_dir, "_DIAGNOSTIC_4key.png"), dpi=130, bbox_inches="tight")
    plt.close()

    # 종합 대시보드 6지표
    present = [k for k in KEY_METRICS if k in set(tags)]
    for t in tags:
        if len(present) >= 6:
            break
        if t not in present:
            present.append(t)
    present = present[:6]
    fig, axes = plt.subplots(2, 3, figsize=(18, 9))
    for i in range(6):
        ax = axes[i // 3][i % 3]
        if i < len(present):
            plot_on(ax, present[i], present[i])
        else:
            ax.axis("off")
    plt.suptitle(f"ML-Agents  {run_id}  |  {df['step'].max():,} steps ({len(files)} tfevents)", fontsize=14, y=1.00)
    plt.tight_layout()
    plt.savefig(os.path.join(run_dir, "_OVERVIEW_dashboard.png"), dpi=130, bbox_inches="tight")
    plt.close()

    # 그룹별 패널 (coop/*, hfsm/*)
    def group_panel(prefix, fname, title):
        gtags = [t for t in tags if t.startswith(prefix)]
        if not gtags:
            return
        n = min(len(gtags), 4)
        _, axs = plt.subplots(1, n, figsize=(5 * n, 4))
        if n == 1:
            axs = [axs]
        for i, t in enumerate(gtags[:n]):
            g = df[df["tag"] == t].sort_values("step")
            axs[i].plot(g["step"], g["value"], lw=0.7, color="#1f77b4", alpha=0.5)
            ma = _ma(g["value"])
            if ma is not None:
                axs[i].plot(g["step"], ma[0], lw=2, color="#d62728")
            axs[i].set_title(t, fontsize=11); axs[i].set_xlabel("Step"); axs[i].grid(True, alpha=0.3)
        plt.suptitle(f"ML-Agents  {run_id}  |  {title}", fontsize=13, y=1.02)
        plt.tight_layout()
        plt.savefig(os.path.join(run_dir, fname), dpi=130, bbox_inches="tight")
        plt.close()

    group_panel("coop/", "_COOP_dashboard.png", "coop metrics")
    group_panel("hfsm/", "_HFSM_dashboard.png", "hfsm metrics")

    # 수치 요약 (시작 10% 평균 -> 끝 10% 평균)
    def headtail(tag_name):
        s = series(tag_name)
        if s is None:
            return None
        v = s["value"].to_numpy()
        k = max(1, len(v) // 10)
        return float(v[:k].mean()), float(v[-k:].mean())

    summary_tags = ["Environment/Group Cumulative Reward", "Environment/Cumulative Reward",
                    "Policy/Entropy", "Losses/Value Loss"] \
        + [t for t in tags if t.startswith("coop/") or t.startswith("hfsm/")]
    lines = [f"[{run_id}] SUMMARY (start 10% -> end 10% mean) | {df['step'].max():,} steps"]
    for t in summary_tags:
        ht = headtail(t)
        if ht:
            lines.append(f"  {t:<40s}: {ht[0]:+.3f} -> {ht[1]:+.3f}")
    with open(os.path.join(run_dir, "_SUMMARY.txt"), "w", encoding="utf-8") as fsum:
        fsum.write("\n".join(lines) + "\n")
    print("  [OK] " + " / ".join(["_OVERVIEW", "_DIAGNOSTIC_4key", "_SUMMARY.txt", "_extracted/"]))

    # run별 Group Reward(없으면 개별 Reward) 시리즈를 비교용으로 반환
    grp = series("Environment/Group Cumulative Reward")
    if grp is None:
        grp = series("Environment/Cumulative Reward")
    result = (run_id, grp[["step", "value"]].copy()) if grp is not None else None

    del df, rows, tags
    plt.close("all"); gc.collect()
    return result


# ===========================================================================
# 엔트리포인트
# ===========================================================================
def main():
    args = sys.argv[1:]
    if args:
        roots = args
    elif os.path.isdir("Result"):
        roots = ["Result"]
    elif os.path.isdir("results"):
        roots = ["results"]
    else:
        roots = ["."]

    scanned = skipped = 0
    compare = []          # (run_id, df[step,value]) — 비교 그래프용

    for root in roots:
        if not os.path.isdir(root):
            print(f"[!] 폴더 아님: {root} (건너뜀)")
            continue
        for run_dir in iter_run_dirs(root):
            scanned += 1
            if os.path.isdir(os.path.join(run_dir, "_extracted")):
                print(f"[skip] _extracted 이미 있음: {run_dir} (재생성하려면 폴더 삭제)")
                skipped += 1
                # 이미 처리된 run도 비교 그래프엔 포함 (wide csv에서 읽기)
                _maybe_load_for_compare(run_dir, compare)
                continue
            try:
                r = process_run(run_dir)
                if r:
                    compare.append(r)
            finally:
                plt.close("all"); gc.collect()

    if scanned == 0:
        print("[!] tfevents 폴더를 찾지 못했습니다.")
        sys.exit(1)

    # run 2개 이상이면 Group Reward 비교 오버레이
    if len(compare) >= 2:
        plt.figure(figsize=(12, 6))
        for run_id, g in compare:
            g = g.sort_values("step")
            ma = _ma(g["value"]) if len(g) > 50 else None
            y = ma[0] if ma is not None else g["value"]
            plt.plot(g["step"], y, lw=1.8, alpha=0.9, label=run_id)
        plt.title("Group Cumulative Reward - run comparison (MA)", fontsize=13)
        plt.xlabel("Step"); plt.ylabel("Group Reward"); plt.grid(True, alpha=0.3)
        plt.legend(fontsize=10)
        plt.tight_layout()
        out = os.path.join(roots[0], "_COMPARE_group_reward.png")
        plt.savefig(out, dpi=130, bbox_inches="tight")
        plt.close()
        print(f"\n[OK] run 비교 그래프 -> {out}")

    print(f"\n완료: 검사 {scanned}개 / 스킵 {skipped}개")


def _maybe_load_for_compare(run_dir, compare):
    """이미 _extracted가 있는 run은 wide CSV에서 Group Reward를 읽어 비교에 포함."""
    csv = os.path.join(run_dir, "_extracted", "all_metrics_wide.csv")
    if not os.path.isfile(csv):
        return
    try:
        w = pd.read_csv(csv)
    except Exception:
        return
    col = "Environment/Group Cumulative Reward"
    if col not in w.columns:
        col = "Environment/Cumulative Reward"
    if "step" in w.columns and col in w.columns:
        g = w[["step", col]].dropna().rename(columns={col: "value"})
        if not g.empty:
            compare.append((os.path.basename(os.path.normpath(run_dir)), g))


if __name__ == "__main__":
    main()
