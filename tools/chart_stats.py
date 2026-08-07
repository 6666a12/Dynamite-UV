#!/usr/bin/env python3
"""Dynamix Universe 谱面统计脚本。

对 `_rev/extracted/Charts/*.asset.json` 全部谱面做结构化统计，
输出 Markdown 报告（chart_stats_report.md）。

用法:
    python3 chart_stats.py [--charts DIR] [--out FILE]
"""
import argparse
import json
import math
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

# ---- 可配置常量（相对本脚本位置解析）----
TOOLS_DIR = Path(__file__).resolve().parent
COMMUNITY_DIR = TOOLS_DIR.parent
DEFAULT_CHARTS_DIR = COMMUNITY_DIR.parent / "_rev" / "extracted" / "Charts"
DEFAULT_OUT = TOOLS_DIR / "chart_stats_report.md"

TRACK_KEYS = ["NotesLeft", "NotesCenter", "NotesRight"]

# 文件名: <hash>_Map_<歌号>.<难度号> - 歌名 [难度名].asset.json
# 歌号允许 TestDy 前缀（测试曲目，如 Map_TestDy1001.4）
FNAME_RE = re.compile(
    r"^[^_]*_Map_((?:TestDy)?\d+)\.(\d+) - (.*?) \[([^\]]+)\]\.asset\.json$"
)

# 报告里抽样的代表谱面（高物量优先自动选，不在则用最大谱面）
SAMPLE_PREFERRED = ("Tera", "Tech", "Giga")


def load_chart(path: Path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def fmt_float(v, nd=6):
    if isinstance(v, float):
        r = round(v, nd)
        return repr(r)
    return repr(v)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--charts", default=str(DEFAULT_CHARTS_DIR))
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    args = ap.parse_args()
    charts_dir = Path(args.charts)
    out_path = Path(args.out)

    files = sorted(charts_dir.glob("*.asset.json"))
    if not files:
        sys.exit(f"no chart files in {charts_dir}")

    # ---- 聚合容器 ----
    diff_code_map = Counter()          # (code, name) 组合
    diff_code_to_names = defaultdict(Counter)
    note_counts_by_track = {k: Counter() for k in TRACK_KEYS}
    total_main_note_mismatch = []      # (file, baked, main_count, sub_count, total)
    type_by_track = {k: Counter() for k in TRACK_KEYS}
    pos_by_track = {k: Counter() for k in TRACK_KEYS}
    width_by_track = {k: Counter() for k in TRACK_KEYS}
    pos_minmax = {k: [math.inf, -math.inf] for k in TRACK_KEYS}
    width_minmax = {k: [math.inf, -math.inf] for k in TRACK_KEYS}
    subnote_charts = 0                 # 含 SubNote 的谱面数
    subnote_notes = 0                  # 有 SubNoteId 的音符数
    subnote_pairs_same_track = 0
    subnote_pairs_cross_track = 0
    subnote_pairs_unresolved = 0
    subnote_relation = Counter()       # SubNoteId 与 Id 的关系类型
    sub_pair_types = Counter()         # (源Type, 目标Type)
    chain_len_by_head = Counter()      # (链头Type, 链长)
    mainnote_formula_fail = []         # MainNote 公式验证失败
    judgesettings_map = Counter()      # (难度号, JudgeSettings m_PathID)
    sync_values = Counter()
    timeend_zero = 0
    timeend_values = []
    dropspeed_nonempty = []
    bpm_sections_count = Counter()     # 每谱 BPM 段数
    bpm_values = Counter()
    bpm_verify_failures = []           # (file, detail)
    bpm_verify_checked = 0
    charts_total_notes = []            # (file, total notes, difflabel)
    id_collision_charts = []           # Id 在谱内不唯一的情况

    for fp in files:
        m = FNAME_RE.match(fp.name)
        song_no, diff_code, title, diff_name = (
            (m.group(1), int(m.group(2)), m.group(3), m.group(4)) if m
            else ("?", -1, fp.name, "?")
        )
        diff_code_map[(diff_code, diff_name)] += 1
        diff_code_to_names[diff_code][diff_name] += 1
        diff_label = f"{diff_code}={diff_name}"

        data = load_chart(fp)

        # TimeEnd
        te = data.get("TimeEnd", 0.0)
        if te == 0.0:
            timeend_zero += 1
        else:
            timeend_values.append((fp.name, te))

        # JudgeSettings 外部引用
        js = data.get("JudgeSettings", "")
        pid_m = re.search(r"m_PathID=(-?\d+)", js)
        judgesettings_map[(diff_code, pid_m.group(1) if pid_m else "?")] += 1

        # DropSpeeds
        ds = data.get("NoteSystem__DropSpeeds") or []
        if ds:
            dropspeed_nonempty.append((fp.name, ds))

        # BPM 时间线
        sections = (data.get("TimeLine") or {}).get("BakedBarSections") or []
        bpm_sections_count[len(sections)] += 1
        for s in sections:
            bpm_values[round(float(s.get("BPM", 0)), 4)] += 1

        # 验算 Seconds 递推: section[i].Seconds + (BarTime - started)*60/BPM*4?
        # BarTime 单位为"小节"(bar)。先试每小节拍数未知, 用递推差验证:
        # Seconds[i+1] - Seconds[i] =? (BarTime[i+1] - BarTime[i]) * k / BPM[i]
        if len(sections) >= 2:
            bpm_verify_checked += 1
            for i in range(len(sections) - 1):
                s0, s1 = sections[i], sections[i + 1]
                dt_sec = float(s1["Seconds"]) - float(s0["Seconds"])
                dt_bar = float(s1["BarTime"]) - float(s0["BarTime"])
                bpm = float(s0["BPM"])
                if bpm <= 0 or dt_bar <= 0:
                    bpm_verify_failures.append(
                        (fp.name, f"seg{i}: bpm={bpm} dbar={dt_bar}"))
                    break
                # k = 秒/小节 系数 = dt_sec*bpm/dt_bar, 应为 60*BeatsPerBar 的常数
                k = dt_sec * bpm / dt_bar
                if abs(k - 240.0) > 0.5:  # 60*4
                    bpm_verify_failures.append(
                        (fp.name, f"seg{i}: k={k:.4f} (期望≈240)"))
                    break
        elif len(sections) == 1:
            bpm_verify_checked += 1

        # 音符统计
        all_notes = {}  # (track, Id) -> note ; Id 唯一性检查
        id_seen = defaultdict(set)
        chart_total = 0
        chart_main = 0
        chart_sub = 0
        has_sub = False
        for track in TRACK_KEYS:
            notes = data.get(track) or []
            note_counts_by_track[track][len(notes)] += 1
            chart_total += len(notes)
            for n in notes:
                chart_main += 1
                t = n.get("Type")
                type_by_track[track][t] += 1
                p = n.get("Position")
                w = n.get("Width")
                pos_by_track[track][round(float(p), 4)] += 1
                width_by_track[track][round(float(w), 4)] += 1
                mm = pos_minmax[track]
                mm[0] = min(mm[0], float(p)); mm[1] = max(mm[1], float(p))
                wm = width_minmax[track]
                wm[0] = min(wm[0], float(w)); wm[1] = max(wm[1], float(w))
                sync_values[n.get("Baked_SyncNote")] += 1
                nid = n.get("Id")
                if nid in id_seen:
                    id_collision_charts.append(
                        (fp.name, nid, sorted(id_seen[nid]), track))
                id_seen[nid].add(track)
                all_notes[(track, nid)] = n
                if n.get("SubNoteId", -1) != -1:
                    has_sub = True
                    subnote_notes += 1
                    chart_sub += 1

        # SubNote 指向分析
        by_id = {}
        for (track, nid), n in all_notes.items():
            by_id.setdefault(nid, []).append((track, n))
        for (track, nid), n in all_notes.items():
            sid = n.get("SubNoteId", -1)
            if sid == -1:
                continue
            targets = by_id.get(sid, [])
            if not targets:
                subnote_pairs_unresolved += 1
                subnote_relation["指向不存在的Id"] += 1
                sub_pair_types[(n.get("Type"), None)] += 1
            else:
                t_tracks = {t for t, _ in targets}
                sub_pair_types[(n.get("Type"), targets[0][1].get("Type"))] += 1
                if track in t_tracks:
                    subnote_pairs_same_track += 1
                    subnote_relation["同轨"] += 1
                else:
                    subnote_pairs_cross_track += 1
                    subnote_relation[f"跨轨->{sorted(t_tracks)}"] += 1

        # 链长统计（从链头顺 SubNoteId 走到尾）
        for track in TRACK_KEYS:
            notes = data.get(track) or []
            if not notes:
                continue
            local_by_id = {n["Id"]: n for n in notes}
            local_sub_targets = {n["SubNoteId"] for n in notes
                                 if n.get("SubNoteId", -1) != -1}
            for n in notes:
                if n["Id"] in local_sub_targets or n.get("SubNoteId", -1) == -1:
                    continue
                length, cur = 1, n
                seen = {n["Id"]}
                while cur.get("SubNoteId", -1) in local_by_id \
                        and cur["SubNoteId"] not in seen:
                    seen.add(cur["SubNoteId"])
                    cur = local_by_id[cur["SubNoteId"]]
                    length += 1
                chain_len_by_head[(n.get("Type"), length)] += 1

        if has_sub:
            subnote_charts += 1

        baked = data.get("Baked_TotalMainNote", 0)
        # MainNote 公式（统计发现，344/350 吻合）:
        #   MainNote = 链头数 + Type3数 + Type6数 - Type9数
        # 其中链头 = Id 不被任何 SubNoteId 引用的音符。
        # 语义解读：Chain(Type3)/Hold(Type6) 头计 2 次（头+尾判定），
        # MixerSub(Type9) 不计入主音符。
        sub_targets_all = {n.get("SubNoteId") for (tk, nid), n in all_notes.items()
                           if n.get("SubNoteId", -1) != -1}
        heads = sum(1 for (tk, nid), n in all_notes.items()
                    if nid not in sub_targets_all)
        t3 = sum(1 for n in all_notes.values() if n.get("Type") == 3)
        t6 = sum(1 for n in all_notes.values() if n.get("Type") == 6)
        t9 = sum(1 for n in all_notes.values() if n.get("Type") == 9)
        if baked != heads + t3 + t6 - t9:
            mainnote_formula_fail.append(
                (fp.name, baked, heads + t3 + t6 - t9))
        if baked != chart_main:
            total_main_note_mismatch.append(
                (fp.name, baked, chart_main, chart_sub, chart_total))

        charts_total_notes.append((fp.name, chart_total, diff_label))

    # ---- 代表谱面抽样 ----
    charts_total_notes.sort(key=lambda x: -x[1])
    samples = []
    for pref in SAMPLE_PREFERRED:
        for fname, total, lbl in charts_total_notes:
            if pref in lbl and fname not in [s[0] for s in samples]:
                samples.append((fname, total, lbl))
                break
    if len(samples) < 3:
        for fname, total, lbl in charts_total_notes:
            if fname not in [s[0] for s in samples]:
                samples.append((fname, total, lbl))
            if len(samples) >= 3:
                break

    sample_notes_dump = []
    for fname, total, lbl in samples:
        data = load_chart(charts_dir / fname)
        per_track = []
        for track in TRACK_KEYS:
            notes = (data.get(track) or [])[:3]
            cleaned = []
            for n in notes:
                cleaned.append({k: v for k, v in n.items() if k != "get_type"})
            per_track.append((track, cleaned))
        tl = (data.get("TimeLine") or {}).get("BakedBarSections") or []
        tl_clean = [{k: v for k, v in s.items() if k != "get_type"}
                    for s in tl[:3]]
        sample_notes_dump.append((fname, total, lbl, per_track, tl_clean))

    # ---- BarTime→秒 换算数值验证（在单一谱面上全量验证）----
    # 公式假设: Baked_Second = sec_of_bar(BarTime),
    # sec_of_bar(bt) = sec[i] + (bt - bar[i]) * 240 / BPM[i]
    # (240 = 60 * 4拍/小节)
    conv_checks = {"checked": 0, "ok": 0, "max_err": 0.0, "bad_files": []}

    def bar_to_sec(sections, bt):
        # 找到最后一个 BarTime <= bt 的段
        seg = sections[0]
        for s in sections:
            if float(s["BarTime"]) <= bt + 1e-9:
                seg = s
            else:
                break
        return float(seg["Seconds"]) + (bt - float(seg["BarTime"])) * 240.0 / float(seg["BPM"])

    for fp in files:
        data = load_chart(fp)
        sections = (data.get("TimeLine") or {}).get("BakedBarSections") or []
        if not sections:
            continue
        worst = 0.0
        for track in TRACK_KEYS:
            for n in data.get(track) or []:
                bt = float(n["BarTime"])
                expect = float(n["Baked_Second"])
                got = bar_to_sec(sections, bt)
                err = abs(expect - got)
                conv_checks["checked"] += 1
                worst = max(worst, err)
                conv_checks["max_err"] = max(conv_checks["max_err"], err)
                if err < 0.011:  # 允许 10ms 浮点误差
                    conv_checks["ok"] += 1
        if worst >= 0.011:
            conv_checks["bad_files"].append((fp.name, worst))

    # ---- 写报告 ----
    L = []
    L.append("# Dynamix Universe 谱面统计报告\n")
    L.append(f"- 谱面目录: `{charts_dir}`")
    L.append(f"- 谱面总数: {len(files)}\n")

    L.append("## 1. 难度号 → 难度名映射（文件名统计）\n")
    L.append("| 难度号 | 难度名 | 谱面数 |")
    L.append("| --- | --- | --- |")
    for (code, name), cnt in sorted(diff_code_map.items()):
        L.append(f"| {code} | {name} | {cnt} |")
    ambiguous = {c: n for c, n in diff_code_to_names.items() if len(n) > 1}
    if ambiguous:
        L.append(f"\n难度号有多个名字: {ambiguous}")
    else:
        L.append("\n每个难度号只对应唯一难度名。")
    known = {1, 2, 3, 4, 5, 6, 12, 16}
    extra = set(c for c, _ in diff_code_map) - known
    L.append(f"\n已知集合(1,2,3,4,5,6,12,16)之外的难度号: {sorted(extra) if extra else '无'}")
    L.append("（索引提到的 Another/Legacy 档在文件名中未出现。）\n" if not extra else "\n")

    L.append("## 2. 音符数分布（按轨）\n")
    for track in TRACK_KEYS:
        c = note_counts_by_track[track]
        total_notes = sum(k * v for k, v in c.items())
        vals = sorted(k for k in c if k > 0)
        L.append(f"### {track}")
        L.append(f"- 总音符数: {total_notes}")
        if vals:
            L.append(f"- 每张谱 {min(vals)} ~ {max(vals)} 个音符")
        # 分布摘要: 分桶
        buckets = Counter()
        for cnt, nfiles in c.items():
            b = (cnt // 200) * 200
            buckets[b] += nfiles
        L.append("- 分布（200 一档, 档内谱面数）:")
        for b in sorted(buckets):
            L.append(f"  - [{b}, {b+200}): {buckets[b]}")
        L.append("")

    L.append("## 3. Baked_TotalMainNote 与 MainNote 公式\n")
    L.append(f"- `Baked_TotalMainNote == 三轨音符行总数` 的谱面: "
             f"{len(files) - len(total_main_note_mismatch)} / {len(files)}"
             "（即大多数谱面 TotalMainNote ≠ 音符行数）")
    L.append("- 统计发现公式: `MainNote = 链头数 + Type3数 + Type6数 - Type9数`")
    L.append("  - 链头 = Id 不被任何 SubNoteId 引用的音符")
    L.append("  - 语义解读（推测）: Chain(Type3)/Hold(Type6) 头计 2 次（头+尾各一次判定），"
             "MixerSub(Type9) 不计入主音符")
    L.append(f"- 公式吻合: {len(files) - len(mainnote_formula_fail)} / {len(files)}")
    if mainnote_formula_fail:
        L.append("- 不吻合文件:")
        for fname, baked, calc in mainnote_formula_fail:
            L.append(f"  - `{fname}`: baked={baked}, 公式={calc}")
    L.append("")

    L.append("## 3.1 难度 → JudgeSettings 资产引用（谱面 PPtr m_PathID 统计）\n")
    L.append("| 难度号 | JudgeSettings m_PathID | 谱面数 |")
    L.append("| --- | --- | --- |")
    for (code, pid), cnt in sorted(judgesettings_map.items(),
                                   key=lambda kv: (kv[0][0], kv[0][1])):
        L.append(f"| {code} | {pid} | {cnt} |")
    L.append("\n已知 PathID 对应（AllAssets 实检）: "
             "-4017723247194766367=JudgeSettings_1 Casual, "
             "-1254297515179712648=JudgeSettings_2 Normal, "
             "-7567386502407530553=JudgeSettings_3 Hard, "
             "5172049694144183429=JudgeSettings_16 Tutorial。")
    L.append("即: Casual→Casual, Normal→Normal, Tutorial→Tutorial, "
             "其余全部难度(Hard/Mega/Giga/Tech/Tera/Legacy/Another)→Hard 档判定。\n")

    L.append("## 4. Type 取值（按轨频次）\n")
    all_types = sorted({t for k in TRACK_KEYS for t in type_by_track[k]
                        if t is not None})
    L.append("| Type | Left | Center | Right |")
    L.append("| --- | --- | --- | --- |")
    for t in all_types:
        L.append(f"| {t} | {type_by_track['NotesLeft'].get(t, 0)} | "
                 f"{type_by_track['NotesCenter'].get(t, 0)} | "
                 f"{type_by_track['NotesRight'].get(t, 0)} |")
    for track in TRACK_KEYS:
        L.append(f"\n{track} Type 集合: {sorted(type_by_track[track])}")
    L.append("")

    L.append("## 5. Position / Width（按轨）\n")
    for track in TRACK_KEYS:
        pmm, wmm = pos_minmax[track], width_minmax[track]
        L.append(f"### {track}")
        L.append(f"- Position 范围: [{pmm[0]}, {pmm[1]}]")
        L.append(f"- Width 范围: [{wmm[0]}, {wmm[1]}]")
        L.append("- Position 最常见值 (Top10): "
                 + ", ".join(f"{v}×{c}" for v, c in
                             pos_by_track[track].most_common(10)))
        L.append("- Width 最常见值 (Top10): "
                 + ", ".join(f"{v}×{c}" for v, c in
                             width_by_track[track].most_common(10)))
        L.append("")

    L.append("## 6. SubNoteId 分布\n")
    L.append(f"- 含 SubNote 的谱面: {subnote_charts} / {len(files)}")
    L.append(f"- 带 SubNoteId(≠-1) 的音符: {subnote_notes}")
    L.append(f"- 指向同轨音符: {subnote_pairs_same_track}")
    L.append(f"- 指向跨轨音符: {subnote_pairs_cross_track}")
    L.append(f"- 指向不存在 Id: {subnote_pairs_unresolved}")
    L.append(f"- 关系细分: {dict(subnote_relation)}")
    L.append(f"- Id 跨轨重复（同一 Id 出现在多轨）的文件数: "
             f"{len(set(f for f, _, _, _ in id_collision_charts))}")
    L.append("\n### 6.1 SubNote 链的 Type 配对（源Type → 目标Type）\n")
    L.append("| 源Type | 目标Type | 次数 |")
    L.append("| --- | --- | --- |")
    for (st, dt), cnt in sorted(sub_pair_types.items(),
                                key=lambda kv: (kv[0][0] or 0, kv[0][1] or 0)):
        L.append(f"| {st} | {dt} | {cnt} |")
    L.append("\n结构: 3→4→4→…（滑链）与 6→7→7→…（长条）两条链型。")
    L.append("\n### 6.2 链长分布（按链头 Type，Top）\n")
    for head_t in sorted({h for h, _ in chain_len_by_head}):
        items = sorted(((l, c) for (h, l), c in chain_len_by_head.items()
                        if h == head_t))
        total_chains = sum(c for _, c in items)
        top = sorted(items, key=lambda x: -x[1])[:8]
        maxlen = max(l for l, _ in items)
        L.append(f"- 链头 Type {head_t}: 共 {total_chains} 条链, "
                 f"最长 {maxlen}, 常见链长: "
                 + ", ".join(f"{l}×{c}" for l, c in top))
    L.append("")

    L.append("## 7. Baked_SyncNote 取值集合\n")
    L.append(f"{dict(sorted(sync_values.items(), key=lambda kv: (kv[0] is None, kv[0])))}")
    L.append("")

    L.append("## 8. TimeEnd\n")
    L.append(f"- TimeEnd == 0 的谱面: {timeend_zero} / {len(files)} "
             f"({timeend_zero/len(files)*100:.1f}%)")
    if timeend_values:
        L.append(f"- 非零样例: {timeend_values[:10]}")
    L.append("")

    L.append("## 9. NoteSystem__DropSpeeds\n")
    L.append(f"- 非空谱面: {len(dropspeed_nonempty)} / {len(files)} "
             f"({len(dropspeed_nonempty)/len(files)*100:.1f}%)")
    for fname, ds in dropspeed_nonempty[:5]:
        L.append(f"- `{fname}`: `{json.dumps(ds, ensure_ascii=False)[:400]}`")
    L.append("")

    L.append("## 10. BPM 时间线\n")
    L.append("- 每谱 BakedBarSections 段数分布: "
             + ", ".join(f"{k}段×{v}谱" for k, v in sorted(bpm_sections_count.items())))
    L.append(f"- BPM 取值种数: {len(bpm_values)}")
    L.append("- BPM 最常见值 (Top15): "
             + ", ".join(f"{v}×{c}" for v, c in bpm_values.most_common(15)))
    L.append(f"\n### Seconds 递推验证（相邻段: Δsec = Δbar × 240 / BPM，即每小节 4 拍）")
    L.append(f"- 验证的多段谱面: {bpm_verify_checked}")
    L.append(f"- 失败数: {len(bpm_verify_failures)}")
    for f_, d in bpm_verify_failures[:10]:
        L.append(f"  - {f_}: {d}")
    L.append("")

    L.append("## 11. BarTime → Baked_Second 全量换算验证\n")
    L.append("公式: `sec = sec[i] + (bar - bar[i]) * 240 / BPM[i]`"
             "（i = 最后一个 BarTime ≤ bar 的段；240 = 60秒 × 4拍/小节）\n")
    L.append(f"- 验证音符总数: {conv_checks['checked']}")
    L.append(f"- 误差 < 11ms 的音符: {conv_checks['ok']} "
             f"({conv_checks['ok']/max(1,conv_checks['checked'])*100:.3f}%)")
    L.append(f"- 最大误差: {conv_checks['max_err']:.6f} 秒")
    L.append(f"- 有误差超限的文件数: {len(conv_checks['bad_files'])}")
    for f_, w in conv_checks["bad_files"][:10]:
        L.append(f"  - {f_}: 最大误差 {w:.6f}s")
    L.append("")

    L.append("## 12. 代表谱面抽样\n")
    for fname, total, lbl, per_track, tl in sample_notes_dump:
        L.append(f"### `{fname}`（{lbl}，总音符 {total}）\n")
        L.append("BakedBarSections 前 3 段:")
        L.append("```json")
        L.append(json.dumps(tl, ensure_ascii=False, indent=1))
        L.append("```")
        for track, notes in per_track:
            L.append(f"{track} 前 {len(notes)} 个音符:")
            L.append("```json")
            L.append(json.dumps(notes, ensure_ascii=False, indent=1))
            L.append("```")
        L.append("")

    out_path.write_text("\n".join(L), encoding="utf-8")
    print(f"wrote {out_path} ({len(L)} lines)")


if __name__ == "__main__":
    main()
