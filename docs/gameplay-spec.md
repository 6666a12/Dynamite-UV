# Dynamix Universe 玩法规格书（社区版重写依据）

> 数据来源：`Dynamix+Universe_00.18.00`（com.c4cat.dynamix2，Unity 6000.0.58f2 + IL2CPP）逆向产物。
> 谱面统计基于全部 350 张谱面 JSON（`community/tools/chart_stats.py` 生成，
> 明细见 `community/tools/chart_stats_report.md`）；判定数值来自提取的
> JudgeSettings 资产实例；符号线索来自 `_rev/metadata_dump.txt`。
> 标注【推测】的条目未由数据直接证明，给出了依据；标注 UNKNOWN 的条目附获取路径。
> 本文只含事实性规格（数值/格式/行为），不含任何原游戏素材。

---

## 1. 谱面格式 schema

谱面是 Unity 序列化的 MonoBehaviour（Map asset），已导出为 JSON。
文件名：`<hash>_Map_<歌号>.<难度号> - <歌名> [<难度名>].asset.json`
（测试曲歌号形如 `TestDy1001`、`9999`）。

### 1.1 顶层字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `path` / `name` | string | 原始工程路径 / 资产名 |
| `AudioData` | PPtr 字符串 | 音频资产引用（`m_FileID`/`m_PathID`），含 TimeLine、BeatPerBar、PreviewTime 等 |
| `Baked_TotalMainNote` | int | 烘焙的"主音符"计数，公式见 §5.3 |
| `TimeEnd` | float | 谱面结束秒。347/350 为 0.0（未使用）；仅 2 张 Tutorial 与 1 张 Test 非零 |
| `JudgeSettings` | PPtr 字符串 | 判定参数资产外部引用，难度→资产映射见 §6.1 |
| `NoteSystem__DropSpeeds` | array | 变速事件 `[{BarTime, Value}]`，30/350 谱非空（8.6%）。Value 为下落速度倍率（常见 0.3~1.15，个别谱出现 12.0） |
| `TimeLine.BakedBarSections` | array | BPM 时间线，见 §3 |
| `NotesLeft` / `NotesCenter` / `NotesRight` | array | 三轨音符，见 §1.2 |
| `get_type` | 任意 | 导出器垃圾字段，解析时忽略（音符与段落内均有） |

### 1.2 音符字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `Id` | int | 谱内唯一（350 谱统计：无跨轨重复） |
| `SubNoteId` | int | 链式音符的下一节点 Id；`-1` = 无。只指向同轨音符（48238/48239），见 §5 |
| `Type` | int | 音符类型 1–9，见 §4 |
| `Baked_SyncNote` | int | 同刻联动标记，取值 {0,1,2,3}，语义见 §5.4 |
| `BarTime` | float | 小节时间（单位：小节 bar，1 小节 = 4 拍，已验证） |
| `Position` | float | 轨内横向位置（NS 单位）。Center ∈ [-5.5, 5.5]；Left ∈ [-1.8, 4.7]；Right ∈ [-1.8, 5.3] |
| `Width` | float | 音符宽度（NS 单位）。Center 常规 1.0（最大 16.0）；侧轨常规 2.0（最大 7.5/6.75）；最小 0.1 |
| `Baked_Second` | float | 烘焙后的命中秒（由 BarTime 经 BPM 时间线换算，公式已 100% 验证，见 §3.2） |

音符 JSON 样例（`Map_0005.6 - The Villager [Tech]`，NotesLeft 前两个）：

```json
[
  {"Id": 98, "SubNoteId": -1, "Type": 1, "Baked_SyncNote": 0,
   "BarTime": 4.125, "Position": 1.65, "Width": 1.5, "Baked_Second": 8.959},
  {"Id": 99, "SubNoteId": 100, "Type": 6, "Baked_SyncNote": 0,
   "BarTime": 4.375, "Position": 1.65, "Width": 1.5, "Baked_Second": 9.502}
]
```

### 1.3 总量统计

- 谱面 350 张 ≈ 111 首正式曲 × 各难度 + Test/TestDy 测试谱。
- 音符行总数 303,447：Left 40,620 / Center 223,654 / Right 39,173。
  中央轨是绝对主力（均值 ~639/谱，最多 2248），侧轨多数谱 < 200。
- `TimeLine.BakedBarSections` 为空的 3 张：`Map_0123.3 Against The Pseudo [Hard]`、
  `Map_0129.4 DeadSoul [Mega]`（均有音符与 Baked_Second，但时间线未导出/未烘焙）、
  `Map_TestDy1002.6`（空壳测试谱，TotalMainNote=0）。引擎应容忍空时间线、
  直接使用 `Baked_Second`。

---

## 2. 难度档位表

由 350 个文件名后缀统计确认（难度号 = 文件名 `.N` 后缀）：

| 难度号 | 难度名 | 谱面数 | 备注 |
| --- | --- | --- | --- |
| 1 | Casual | 58 | |
| 2 | Normal | 82 | |
| 3 | Hard | 83 | 另有 1 张 `Map_0097.3` 后缀写作 `[Legacy]`（命名不一致，实测存在） |
| 4 | Mega | 66 + 1 张 TestDy | |
| 5 | Giga | 29 + 1 张 TestDy | |
| 6 | Tech | 19 + 1 张 TestDy | |
| 8 | Legacy | 4 | 旧谱复刻档 |
| 9 | Another | 1 | `Map_9014.9 LAST Re;SØRT pt4` |
| 12 | Tera | 2 | |
| 16 | Tutorial | 2 | |

- 难度号与难度名基本一一对应；`3` 出现一次 `[Legacy]` 写法（`Map_0097.3 Mechanismós ton Antikythíron`），按文件名直读即可。
- 索引先前提到的 Another/Legacy 档确认存在：Another=9（1 张）、Legacy=8（4 张）。
- 测试谱：`Map_9999.2/.3/.4 Test`（TimeEnd=5 的那张是 9999.2）、`Map_TestDy1001.4/.5`、`Map_TestDy1002.6`。

---

## 3. BPM 时间线与 BarTime→秒换算

### 3.1 BakedBarSections 结构

```json
{"BPM": 140.0, "BarTime": 1.0, "BarTimeRangeStarted": 1.0, "Seconds": 1.7142857}
```

- 每段：`BPM`（该段速度）、`BarTime`（段起始小节）、`BarTimeRangeStarted`（与 BarTime 一致，统计中未见差异）、`Seconds`（段起始秒）。
- 段数分布：1 段 ×335 谱（单 BPM 定速为主流），3 段 ×4，6 段 ×2，7 段 ×2，54 段 ×4（重度变速曲），0 段 ×3（见 §1.3）。
- BPM 取值 92 种，最常见 200/170/180/175/194。

### 3.2 换算公式（核心，已全量验证）

**1 小节 = 4 拍**，即 1 bar = `240 / BPM` 秒。对任意小节时间 `bar`：

```
i = 最后一个满足 sections[i].BarTime <= bar 的段
sec(bar) = sections[i].Seconds + (bar - sections[i].BarTime) * 240 / sections[i].BPM
```

验证结果：对全部 350 谱的 **302,953 个音符**，`sec(BarTime)` 与烘焙值 `Baked_Second`
最大误差 **0.00051 秒**（浮点精度量级），**100% 在 11ms 容差内**；
相邻段的 `Seconds` 递推关系（Δsec = Δbar × 240 / BPM）在 347 张多/单段谱上零失败。

> 引擎实现建议：加载时用 `Baked_Second` 作为权威命中时刻；需要从小节坐标
> （如编辑器、DropSpeeds 事件的 BarTime）换算时用上述公式。

---

## 4. 音符类型行为表（Type 1–9）

Type 全集为 {1,2,3,4,5,6,7,8,9}，**三条轨的 Type 集合相同**（侧轨无独有类型）。
名称映射的依据：metadata dump 中 9 个 `NoteTag_Type*` 标签（Tap/Burst/Mine/Chain/Line/
Hold/HoldSub/Mixer/MixerSub）+ 9 个判定函数（`_judgeTap`/`_judgeBurst`/`_judgeChain`/
`_judgeMine`/`_judgeHold_Start`/`_judgeHold_Holding`/`_judgeMixer_Start`/`_judgeMixer_Holding`/
`_judgeMixer_DelayedHit`，**无 `_judgeLine`**）+ 谱面统计结构。

| Type | 名称 | 数量 | 结构特征（统计） | 判定（dump 符号） |
| --- | --- | --- | --- | --- |
| 1 | Tap | 156,498 | 从不带 SubNote | `_judgeTap`；Score 类别 Tap |
| 2 | Burst | 55,591 | 从不带 SubNote（全库仅 2 例异常） | `_judgeBurst`；Score 类别 Burst |
| 3 | Hold（长条头）【视频实锤】 | 15,516 | **100% 带 SubNoteId**，指向 Type 4 | `_judgeHold_Start` / `_judgeHold_Holding`；Score 类别 HoldStart/HoldEnd/HoldHolding |
| 4 | HoldSub（长条体）【视频实锤】 | 26,059 | 绝大多数（25,001）被引用为长条体，链内 4→4 续接 | 随 `_judgeHold_Holding` 持续判定；holding 期间按 HoldHolding 档每拍给分（见 §6.4） |
| 5 | Mine【推测】 | 10,746 | 从不带 SubNote；三轨均有，Center 为主 | `_judgeMine`；Score/Health 类别 Mine（误触惩罚最重：Health Miss 最高 -1500）；命名为 Mine 的依据是 `PlayMineTriggerSound__Default` 与其独立判定函数，且其频率远低于 Tap/Burst 符合"地雷"定位 |
| 6 | Chain（滑链头）【视频实锤】 | 4,209 | **100% 带 SubNoteId**，指向 Type 7 | `_judgeChain`；Score 类别 Chain |
| 7 | Line（滑链体节点）【视频实锤】 | 22,309 | 22,236 被引用为链体，链内 7→7 续接 | 无 `_judgeLine`——【推测】链体节点自动判定（划过即中），每个节点按 Chain 档计分（50 满分，为 Tap 的一半） |
| 8 | Mixer（混音起点） | 3,111 | 不带 SubNoteId | `_judgeMixer_Start`；Score 类别 MixerStart |
| 9 | MixerSub（混音滑条） | 9,408 | Width 几乎恒为 5.5（9,387/9,408），覆盖整个侧轨宽 | `_judgeMixer_Holding` / `_judgeMixer_DelayedHit`；Score 类别 MixerHolding/MixerEnd；不计入 TotalMainNote（见 §5.3） |

说明：

- Type 2 与 5 的名字归属（Burst/Mine）是【推测】：二者都是独立单击类音符，
  依据是 Burst 有宽键特征（Width 2.0 占 8,542 例）与独立 `_judgeBurst`，
  而 Type 5 数量少、惩罚表最重，符合 Mine。【推测】Burst 行为可能与普通 Tap
  相同但使用宽判定区/不同打击音；准确行为 UNKNOWN（需方法体反汇编）。
- Hold 链长 2–513 节；Chain 链长 2–128 节（统计见报告 §6.2）。
- **Type 3/4 与 6/7 的名称曾经标反**（旧版写作 3=Chain、6=Hold，依据仅为谱面统计
  推测）。Tablear GIGA 谱面确认视频实锤：63.7–64.8s Center 斜长条（琥珀色面板）
  对应谱面 Type 3/4；88–89s 侧轨彩虹缎带滑链对应 Type 6/7。已按视频证据对调，
  判定函数归属随之对调（3/4→`_judgeHold_*`，6/7→`_judgeChain`，详见
  `video-geometry-analysis.md`）。
- 打击音效共 7 种（HitSound 资产），谱面不指定音效，由皮肤/设置决定。

---

## 5. SubNote / SyncNote 联动语义

### 5.1 SubNoteId 链（统计事实）

- 48,239 个音符带 SubNoteId；指向关系只有两种链型：
  - **长条（Hold）**：`Type 3 → Type 4 → Type 4 → …`（3→4 共 15,516 对，4→4 共 10,485 对）
  - **滑链（Chain）**：`Type 6 → Type 7 → Type 7 → …`（6→7 共 4,209 对，7→7 共 18,027 对）
- **全部指向同轨音符**（48,238/48,239）；Id 谱内唯一、无跨轨引用。
- 唯一例外：`Map_0004.4 Chaotic_Reflexion [Mega]` NotesCenter Id=125 的
  SubNoteId=126 悬空（Id 126 不存在）——引擎应对悬空引用容错。
- 节点各自携带独立的 `BarTime`/`Position`/`Baked_Second`：链就是一串带时间戳的
  折线顶点，长条/滑条的形状由顶点序列决定。

### 5.2 链的方向

链头 = 不被任何 SubNoteId 引用、且自身带 SubNoteId 的音符；
沿 SubNoteId 走到 -1（或悬空）为链尾。链内 Id 通常递增但不保证连续。

### 5.3 Baked_TotalMainNote 公式（统计发现，344/350 精确吻合）

```
TotalMainNote = 链头数 + (Type3 数) + (Type6 数) - (Type9 数)
其中 链头数 = Id 不被任何 SubNoteId 引用的音符总数
```

解读【推测】：计数对应"判定单元"——Chain/Hold 头各计 2 次（开始 + 结束各一次
判定，与 Score 表 HoldStart/HoldEnd、MixerStart/MixerEnd 对应），MixerSub(Type 9)
不计。例外 6 张：3 张 TestDy（未烘焙，baked=0）、2 张 Map_9999 Test、
1 张 Chaotic_Reflexion [Mega]（差 1，与 §5.1 的悬空 SubNote 对应）。

### 5.4 Baked_SyncNote（取值 {0,1,2,3}，语义【推测】）

- 分布：0 ×275,963；1 ×18,055；2 ×4,324；3 ×5,105。
- 统计规律：非零值只出现在"同一 Baked_Second 有多个不同轨音符"的和弦处，
  用于绘制跨区域同步连线（dump 中有 `SkinPackNote.SyncLeft` 等符号）。
- 值与伙伴轨相关：侧轨音符与邻轨同刻时恒为 1；Center 与 Left 同刻时常为 2、
  与 Right 同刻时常为 3。【推测】编码 = 同步伙伴的区域 id（1=侧轨/邻区，2=Left，
  3=Right），仅作视觉连线用途，不影响判定。精确编码 UNKNOWN。

---

## 6. 判定窗口与判定等级

### 6.1 JudgeSettings 资产（已找到全部 4 个实例）

位置：`Assets/_Dy2System/MapPlayer/JudgeSettings/JudgeSettings_<N> - <名>.asset`
（提取于 `_rev/extracted/AllAssets/ad6321cff225_JudgeSettings_*.json`）。
谱面经 `JudgeSettings` PPtr 引用，映射（350 谱 PPtr 统计 + PathID 实检）：

| 谱面难度 | 引用资产 | 谱面数 |
| --- | --- | --- |
| Casual (1) | JudgeSettings_1 Casual | 58 |
| Normal (2) | JudgeSettings_2 Normal | 82 |
| Tutorial (16) | JudgeSettings_16 Tutorial | 2 |
| Hard/Mega/Giga/Tech/Tera/Legacy/Another (3,4,5,6,8,9,12) | JudgeSettings_3 Hard | 268 |
| `Map_9999.4 Test` 一张 | PathID=0（无引用） | 1 |

**即全游戏只有 4 套判定参数；Hard 及以上所有难度共用 Hard 档。**

### 6.2 判定窗口（资产原值，单位 BarTime）

| 字段 | Casual | Normal | Hard | Tutorial |
| --- | --- | --- | --- | --- |
| `PrefectBarTime`（原文拼写即 Prefect） | 0.0625 | 0.0390625 | 0.0390625 | 0.0625 |
| `GreatBarTime` | 0.09375 | 0.09375 | 0.0703125 | 0.09375 |
| `GoodBarTime` | 0.125 | 0.125 | 0.1015625 | 0.125 |
| `MissBarTime` | 0.15625 | 0.15625 | 0.15625 | 0.15625 |
| `HoldHoldingJudgeBarTime` | 0.125 | 0.125 | 0.125 | 0.125 |
| `MixerHoldingJudgeBarTime` | 0.125 | 0.125 | 0.125 | 0.125 |
| `MinBPM` / `MaxBPM` / `StandardBPM` | 120 / 200 / 150 | 同左 | 同左 | 同左 |
| `MixerFlyNSWidthCenter` | 0.25 | 0.25 | 0.25 | 0.25 |
| `MixerFlyNSWidthSides` | 0.5 | 0.5 | 0.5 | 0.5 |

### 6.3 BarTime 窗口 → 秒（换算规则【推测】）

窗口以小节存储，运行时由 `NoteJudgeConstant`（字段 `prefectSecond`/`greatSecond`/
`missSecond`/`holdHoldingJudgeSecond`/`mixerHoldingJudgeSecond`）缓存为秒，
相关符号：`CaptureJudgeConstant`、`BarTimeToSeconds`。
按 §3.2 同一公式换算（1 bar = 240/BPM 秒），取 `StandardBPM=150` 时的参考值：

| 窗口 | Casual | Normal | Hard（含 Mega+） |
| --- | --- | --- | --- |
| Prefect | ±100 ms | ±62.5 ms | ±62.5 ms |
| Great | ±150 ms | ±150 ms | ±112.5 ms |
| Good | ±200 ms | ±200 ms | ±162.5 ms |
| Miss | ±250 ms | ±250 ms | ±250 ms |
| Hold/Mixer Holding 判定点间隔 | 200 ms | 200 ms | 200 ms |

【推测】实际换算用有效 BPM = clamp(当前 BPM, MinBPM=120, MaxBPM=200)，
即窗口随曲速缩放但被钳制；150 BPM 时与 StandardBPM 一致。
是否按当前 BPM 还是 StandardBPM 固定换算 UNKNOWN（需方法体验证）。
窗口为 ± 双侧（早/晚对称）【推测，依据：detailed grade 区分 Early/Late】。

### 6.4 判定等级

- `ENoteJudgedGrade`：基本档为 **Prefect / Great / Good / Miss**（Score 表四列即此四档；
  原文拼写 "Prefect"）。
- `GetJudgedDetailedGrade` + dump 中 `EarlyPrefect`/`EarlyGreat` 符号：详细档区分
  早/晚（EarlyPrefect/LatePrefect/EarlyGreat/LateGreat …），用于偏移提示。
- `ENoteHitState`（_Dynamix2.ServerSharedLegacy，PVP 用）：可见值 `NOT_ARRIVING`、
  `PREFECT`（dump 截断，其余值 UNKNOWN）。

---

## 7. 计分、血量与 Boost（JudgeSettings 原值，全 4 档通用部分）

三套数值表：`Score`（得分）、`Health`（血量）、`Boost`（能量），
按 10 个判定类别 × 4 档（Prefect/Great/Good/Miss）给值。
**4 套 JudgeSettings 的 Score 与 Boost 表完全相同**；Health 表仅 Miss 惩罚不同
（Casual/Normal：普通 Miss -500、Holding 类 -100、Mine -1000；Hard：Mine -1500；
Tutorial：全部放宽为 -100/-20/-200）。

### 7.1 Score（得分）

| 类别 | Prefect | Great | Good | Miss |
| --- | --- | --- | --- | --- |
| Tap / Burst / Mine / HoldStart / HoldEnd / MixerStart / MixerEnd | 100 | 70 | 50 | 0 |
| Chain | 50 | 35 | 25 | 0 |
| HoldHolding / MixerHolding | 10 | 0 | 0 | 0 |

（Holding 类只有 Prefect 给分：每个 holding 判定点 +10。）

### 7.2 Health（血量，Casual/Normal 档）

| 类别 | Prefect | Great | Good | Miss |
| --- | --- | --- | --- | --- |
| Tap/Burst/Chain/HoldStart/HoldEnd/MixerStart | +20 | +15 | +10 | -500 |
| Mine | +20 | +15 | +10 | -1000（Hard -1500，Tutorial -200） |
| HoldHolding / MixerHolding | +5 / +10 | 0 | 0 | -100 |
| MixerEnd | +20 | +10 | 0 | -500 |

（相关符号：`get_PlayerHealth`/`set_PlayerHealth`、`get_PlayerMaxHealth`、`GameOver`、
`IsVictory`——血量制，归零 GameOver。）

### 7.3 Boost（能量）

| 类别 | Prefect | Great | Good | Miss |
| --- | --- | --- | --- | --- |
| Tap/Burst/Mine/HoldStart/HoldEnd/MixerStart/MixerEnd | 100 | 50 | 25 | 0 |
| Chain | 50 | 25 | 12 | 0 |
| HoldHolding / MixerHolding | 15 | 15 | 15 | 0 |

（符号：`get/set_PlayerBoostEnergy`、`ExBoostValueByLevel`、`GetExBoostLevel`、
`IsExboostable`——Boost 攒满可触发 EX Boost，分级加成，细节 UNKNOWN。）

### 7.4 连击与倍率

- 符号：`GetComboMultifier`、`get_Combo`、`<OnNoteHit>g____GetBuffScoreMultiple`、
  `GetScoreMultiple`、`<OnNoteHit>g____GetHealMultiple`、`_SubmitScore`、
  `GetClearPercent100`、`CalculateStatistics`、`SetScore`/`SetJudgeGrade`/`SetPercent`。
- **已知**：存在连击倍率函数与分数倍率（含 Buff 加成）；结算按百分制
  （`GetClearPercent100`）。
- **UNKNOWN**：`GetComboMultifier` 的具体分段/数值、总分配比（理论满分如何由
  TotalMainNote 归一）、评级（SSS 等）阈值。获取路径：Frida 内存 dump +
  Il2CppDumper 出 RVA，再反汇编方法体（见 §9）。

---

## 8. 三轨玩法结构与校准

### 8.1 区域与触控（dump 符号）

- 三个判定区域：`NoteRegion_Left` / `NoteRegion_Center` / `NoteRegion_Right`
  （对应 `NoteTag_RegionLeft/Center/Right` 与 `NSTouchDataLeft/Center/Right`）。
  触控按区域分发到三套 `NSTouchData`，与谱面 `NotesLeft/Center/Right` 对应。
- 命中检测符号：`_prepareTouches`、`_checkAnyValidTouch`、`CheckOverlap`、
  `CheckNearestOverlap`（触摸点与音符区重叠判定，取最近重叠音符）、
  `nsTouchWidth`/`noteNSWidth`（触摸与音符的 NS 宽度）、`GetNoteLocalX`
  （Position → 本地 X 坐标换算）。
- Position/Width 采用统一的 "NS"（NoteSpace）坐标：Center 常规宽 1.0、
  侧轨常规宽 2.0，MixerSub 宽 5.5 ≈ 侧轨全宽。Region 原点与 NS→屏幕换算
  已从场景数据解出（见 §8.5）；Position 基准点【推测：轨左缘，1 单位≈2.75 NS，
  像素标定支持但未经方法体验证，见 §8.5 末】。

### 8.2 中央混音轨（Mixer）机制

- Mixer 是 Dynamix Universe 特色：Type 8（Mixer 起点）+ Type 9（MixerSub 滑条段）。
- `MixerFlyNSWidthCenter=0.25` / `MixerFlyNSWidthSides=0.5`：混音"飞键"在中央/
  侧轨的 NS 宽度。
- 判定相关符号：`_judgeMixer_Start`、`_judgeMixer_Holding`
  （内含 `__calculateSliderPos`、`__updateTouchSliderPosition`、
  `__checkDelayedHit` 子函数）、`_judgeMixer_DelayedHit`、
  `_CalculateMixerCorrectPos`。
  【推测】Mixer 按住后触点变成滑块，需跟随滑条移动（位置连续判定，
  每 0.125 bar = 1 个 holding 判定点）；DelayedHit 处理滞后命中补偿。
- 渲染：`SkinNote_MixerStretchMesh3Part`（三段拉伸网格）、
  `SkinNote_StretchMesh3Part/5Part`、`SkinNote_HoldMesh`、
  `SkinNote_HorizontalStretch`、`SkinNote_MeshMultiPartStretch`。

### 8.3 下落与速度

- `NoteSystem__DropSpeeds`：`[{BarTime, Value}]` 速度事件序列（30/350 谱），
  Value 为倍率，BarTime 用小节坐标（需用 §3.2 公式换算成秒）。
- `SetDropSpeedPlayerSetting`、`dropSpeedSetting`：玩家侧全局速度设置，与谱面
  事件叠加【推测】。
- 符号：`Note_MovementSetting`、`WithExitSpeed`、`GetNoteVisualPosition`、
  `GetNoteMovementSetting`、`NoteMovement`——音符运动由设置 + 谱面事件驱动。

### 8.4 校准（touch offset）

- 双判定系统：`NoteJudgeSystem__Auto`（自动演示/AutoPlay）与
  `NoteJudgeSystem__Manual`（玩家输入）。
- `NoteJudgeSystem__Manual` 携带 **`touchOffset`** 字段，与 `NSTouchDataCenter`
  一起传入各 `_judge*` 函数；另有 `touchLocOffset`（位置偏移，传入
  `_transformTouches`/`CheckNearestOverlap`）。
  【推测】`touchOffset` = 全局触控延迟校准（秒，用户设置里调），
  `touchLocOffset` = 触控位置偏移校准；判定前统一加到触摸时间/位置上。
- 相关方法：`GetTimeOffsetBySecond`、`GetTimeDelayToPlay`、
  `BarTimePlayBack`、`MinPitch`（偏移超阈值时降调？UNKNOWN）。

### 8.5 玩法场景几何（已从场景数据解出，含运行时交叉验证）

数据来源：`MapPlayer.unity` = assetpack `_rev/assetpack/assets/bin/Data/level2`
（SerializedFile v22，已离线解析层级）；其引用的 NoteManagerSubscene（DOTS，
GUID `b6a50fc5-…-353543b99bc1` = `_rev/assetpack/assets/EntityScenes/6b5af05cd544f6d41bde5353349bb91c.0.entities`）含 Region 记录。
运行时验证：AVD 截图像素标定（`community/docs/pixel-calibration.md`，
2340×1080 屏）+ memscan 内存扫描确认资产加载未被篡改。

**Region / NoteSpace（NS）坐标：**

| 项 | 值（NS 单位） | 说明 |
| --- | --- | --- |
| 全区总宽 | 22 | 实体记录 (22, 11) 数对 |
| 中央轨宽 | 11 | Position 域 ≈ [0, 4]（350 谱统计 [-0.4, 4.3]，负值为贴边溢出） |
| Region 原点 | Center 左缘 **-5.5**；Left **-11**；Right **+11** | 侧轨 = 中央轨绕 Z 轴 ±90° 旋转后平移（quat (0,0,±0.7071,0.7071)） |
| 判定条宽度 | 9.9 | 三条 Region 记录相同 |
| 音符生成距离 | 50 | spawn 距离（NS） |
| 触控 NS 宽度 | ±0.1 | Center +0.1 / Left -0.1 |

**NS → 屏幕换算（level2 Transform 链）：**

- `MapPlayer/Canvas/PlayArea/Wrap/__NoteManagerTrs`：pos (0,-54,135)，scale 67.5；
  `Wrap` scale 0.8333 (=1/1.2) → **1 NS = 67.5 画布 px（净 56.25 px）**，
  中央轨 11 NS ≈ 742.5 px（净 618.75 px）。
- 3D 相机：pos (0, 1.94, -6.5)，FOV 57.81°，near 0.3 / far 1000；场景接近正交
  （实测不同深度音符宽度差 <6%）。
- 判定齿轮 UI（画布 px）：中央 `Gear2` 1140×141 anch (0,-474)；
  左右 `Gear1/3` 各 400×502 anch (±740,-261)。
- 背景 3D 房间 `SceneModel_2001 - BattleRoom` scale=100。

**像素标定实测（2340×1080，与上面换算互证）：**

- 判定条：x 875–1465（590px），y 650–680；理论 9.9 NS×56.25=557px /
  11 NS×56.25=619px，实测居中且含辉光描边，量级吻合。
- Tap 音符（W=1.0）视觉宽 ~270px ≈ 4.8 NS；Hold 束宽 ~250px。
  注意：视觉宽度明显大于 1 NS（56px）→ **Width 单位不是 1 NS**，
  结合 Position 域 [0,4] 覆盖 11 NS 轨宽【推测：1 Position/Width 单位 = 2.75 NS
  （=11/4），Position 从轨左缘起算；Width=1.0 → 2.75 NS=155px 判定宽，
  皮肤网格比判定盒宽约 1.7 倍】。确切换算仍待 `GetNoteLocalX` 方法体验证。
- Mixer 青色滑杆：x 975–1245（270px），y 785–825（地板透视，中心略偏左）。
- 音符从屏幕顶部垂直落向判定条（路径 y≈0..665）。

### 8.6 视频实测几何（Tablear GIGA autoplay 谱面确认视频）

来源：`community/docs/video-geometry-analysis.md`（1440×1080、30fps 视频逐帧
blob 追踪 + 谱面 Baked_Second 逐 note 对齐交叉验证，分析区间 DropSpeed=1.0）。
以下为**已验证硬数值**（1440×1080 坐标系，其他分辨率按比例缩放）：

| 参数 | 数值 | 备注 |
| --- | --- | --- |
| Center 下落速度 | ~1050 px/s（1040–1065） | ≈0.97 屏高/秒；与 2340×1080 实测 990px/s（0.92 屏高/秒）吻合，**速度按屏高缩放** |
| Center 生成 | y≈300±20 淡入（命中前 ~0.52s） | 不是从屏幕顶端下落；过判定线即爆，不下沉 |
| Center 判定线 | y≈858 | 星爆质心中位 858.1±7.1 |
| Center Tap 尺寸 | 宽 204px×W，高 22–24px | W=1.0/0.8 两点验证；顶部有小凸 tab |
| 侧轨运动 | 竖条从中央 x≈720∓64 淡入，~1020px/s 向边缘外移 | 右轨向右、左轨向左 |
| 侧轨命中点 | 左 x=165±5，右 x=1275±9 | 1440 宽下对称 |
| 侧轨条尺寸 | 长 ≈204px（W=2.0），厚 17–24px | 端部尖角指向运动方向 |
| 颜色 | 无语义（青/绿按歌曲段落切换，与 Type 无关） | 推翻"颜色区分类型"猜测 |
| Mixer 粉条 | 固定 UI：y≈634，x 506–933，中央光标 x≈710 | 不随轨道演出移动 |

**映射问题已解决（实机验证）**：谱面确认（Charter）视频里 x(P)/y(P) 随时间
平移 ±40–80px，经真人手元 AP 视频（`tablear_realplay.mp4`，同谱）分窗对比证实
为**预览模式特有演出，正式游玩不存在**——实机 x(P) 全局固定，53 秒跨度的同 P
重复测量 std≈2–6px（详见 `video-geometry-analysis.md` §8）。

⚠ 注意上表来源是 Charter 预览视频：其中**判定线 y、Tap 宽度、Mixer 粉条**
已与实机交叉验证一致；而下落方式（匀速、y≈300 淡入）与侧轨运动细节是
**预览模式的平面渲染路径**，实机为 3D 隧道透视（灭点生成、加速下落、
x 恒定），平面复现按"x 恒定、匀速"即可。

渲染器应使用的**实机映射**（1440×1080 坐标，P∈[0.2,4.2] 验证）：

- 线性（够用）：`x = 312.5 + 204.9·P`（r²=0.9968，残差 std 15.6px）
- 三次（更准）：`x = −2.54P³ + 18.60P² + 168.49P + 326.14`（残差 std 14.3px）
- 实机为 3D 隧道透视渲染（灭点生成、加速下落、**x 全程恒定**）；平面复现
  按"x 恒定、匀速"即可，观感差异来自相机而非谱面逻辑。
- 侧轨 y(P) 未在实机视频复测（手遮挡数据脏），鉴于 center 固定，大概率同样
  固定；Charter 视频里的"时变"视为预览渲染差异。

成因排查结论（`_rev` 全库静态分析，见开放问题 6b）：

- **非线性部分最可能是 3D 透视投影**：note 区由 3D 相机渲染
  （FOV 57.8°、z=135、绕 X 微倾 ~2.8°），投影映射是有理函数，三次曲线是
  良好近似——不是谱面数据驱动。
- **时变漂移已证实为谱面确认（Charter）演示模式特有**：游戏存在独立的
  `MapDriver_Charter`（演示 driver，与 `MapDriver_SinglePlay` 并列）；谱面资产、
  Animator 开场动画（仅 1s）、Unity Timeline 均已排除，真人手元视频对比
  实锤（同 P 跨 53s std≈2–6px vs Charter ±80px）。社区版无需复现漂移。
- 静态分析已到顶：PairIP 打乱类型/方法名 + Burst 编译，方法体逻辑不可读；
  进一步只能运行时内存扫描（memscan 路径可行）。

---

## 9. 开放问题清单

| # | 问题 | 现状 | 下一步获取方案 |
| --- | --- | --- | --- |
| 1 | Type 2/5 = Burst/Mine 归属、Burst 确切行为 | 【推测】见 §4；**视频新证据**：Charter 视频中 Type 5 以普通下落横条呈现、命中时刻有星爆+PERFECT（慢速段 t≈126.8 实锤，±0.9s 无其他 tap）——不支持"隐形地雷"渲染，但 autoplay 是否触碰未知 | Frida dump 内存 metadata → Il2CppDumper 出 `dump.cs`+RVA → 反汇编 `_judgeBurst`/`_judgeMine`/`GetNoteJudgeTypeByNoteType` |
| 2 | Type 4（Line/链体节点）是否逐节点计分、链头尾各计几分 | 【推测】每节点 Chain 档 | 同上，反汇编 `_judgeChain`；或真机录屏数分 |
| 3 | 判定窗口 BarTime→秒 是否按当前 BPM（钳 120–200）缩放 | 【推测】，150 BPM 参考值已给出 | 反汇编 `CaptureJudgeConstant`/`BarTimeToSeconds` |
| 4 | `GetComboMultifier` 连击倍率分段、理论满分归一方式、评级阈值 | UNKNOWN | 反汇编 `GetComboMultifier`/`CalculateStatistics`/`GetClearPercent100` |
| 5 | `Baked_SyncNote` 精确编码 | 【推测】伙伴区域 id，仅视觉用途 | 反汇编烘焙侧 `_Dynamix2.MapConstructor`；不影响判定可暂缓 |
| 6 | Position 基准点（左缘/中心）与 NS→屏幕坐标比例 | **大部分已解**（§8.5：Region 原点、NS=56.25 净 px、Position 域 [0,4]；1 单位≈2.75 NS 为【推测】） | 反汇编 `GetNoteLocalX` 终验；或录屏标定 chord 间距 |
| 6b | ~~Position→像素映射时变的成因~~ | **已解决**：漂移是 Charter 预览模式特有演出，实机 x(P) 固定（`x=312.5+204.9·P` @1440 宽，r²=0.9968；`video-geometry-analysis.md` §8）；非线性≈3D 透视 | 社区版无需复现；若想复刻预览演出需运行时内存扫描 |
| 7 | Mine 的"判定"方向（不打=Prefect？）与计分时机 | 【推测】Miss 列=误触 | 反汇编 `_judgeMine` |
| 8 | DropSpeeds 的 Value 是乘算还是覆盖、与玩家速度设置叠加方式 | UNKNOWN（数据样例见报告 §9） | 反汇编 NoteMovement 相关 |
| 9 | EX Boost 触发/加成规则 | UNKNOWN（仅符号） | 反汇编 `ExBoostValueByLevel`/`IsExboostable` |
| 10 | 0 段 BakedBarSections 谱（2 张正式谱）的原始 BPM | UNKNOWN（音符自带 Baked_Second，可绕过） | 查对应 Raw 谱面源数据（`Assets/_Dy2Content/Map/Raw/`） |

通用获取路径（PairIP 静态分析已到极限后的标准方案）：
真机/模拟器 `frida -U -f com.c4cat.dynamix2` → dump 内存中已解密的
global-metadata + libil2cpp.so → Il2CppDumper 出 `dump.cs`/`script.json`
（方法名→RVA）→ Ghidra 按 RVA 标注 103 MB il2cpp 代码段，反汇编目标方法体。

---

## 附：可复现性

- 统计脚本：`community/tools/chart_stats.py`（`python3 chart_stats.py`，
  可用 `--charts/--out` 参数化路径），输出 `community/tools/chart_stats_report.md`。
- JudgeSettings 原始 JSON：`_rev/extracted/AllAssets/ad6321cff225_JudgeSettings_{1,2,3,16} - *.asset.json`。
- 像素标定记录：`community/docs/pixel-calibration.md`（截图在 `comfyui_dl/t01-t20.png` 等）。
- 内存扫描器：`community/tools/memscan/memscan.c`（NDK arm64 **动态链接**编译——
  静态链接版在模拟器 ARM 翻译层下启动即崩；root 运行 `memscan <pid>` 扫
  `/proc/pid/mem`），实测 JudgeSettings 运行时值与离线资产完全一致。
- AVD 运行注意事项：后端 AWS 东京轮询 IP `52.196.240.74` 部分网络不可达会致
  黑屏死等（SYN-SENT），用 `iptables -A OUTPUT -d 52.196.240.74 -p tcp --dport 443
  -j REJECT --reject-with tcp-reset` 强制快速失败即可；frida 注入在 Android 16
  ARM 翻译环境下不可用（Java 桥无、script load 超时），勿再投入。
