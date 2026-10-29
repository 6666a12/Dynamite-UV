# 结果前奏、七类结果与正常结束条件

> 2026-10-28 已接入客户端源码（本文件与源码同轮更新）。本文区分**已实现**、**已实际执行的检查**、
> **待用户最终视觉/设备验收**，三者不能互相替代；主代理已完成 Godot 窗口验收，没有公开包导出或 Android 验收记录。
> 设计源仍在被 Git 忽略的 `design/result-outcomes/surge/`，发行资产已复制到 `client/assets/results/outcomes/`。

## 1. 正常完成：谱末 +2 秒与音频自然结束取晚（已实现）

正常完成必须**同时**满足两个条件，两者分别单调锁存，先到的一方只能等待另一方：

1. 已到达 `JudgePlan.Plan.EndTime + 2` 秒；
2. 音频流已自然播放结束。

实现位于 `client/scripts/game/gameplay/GameplayEndCondition.cs`，由 `GameplayMain._Process`
第 10 步（结束检测）每次传入 `songTime`、`SongPlayback.HasNaturallyEnded` 和本帧 `delta`：

- **谱面时间**：采样到的最小/最大值里取最大（单调），用于谱末锁存；
- **音频结束后的真实时间**：音频自然结束那一刻记下当时的谱面时间作为锚点，
  之后按每帧 `delta` 累加（单帧最多 0.5 秒，避免一次长停顿把等待快进掉）；
- 有效时间取两者较大值，谱末锁存只看有效时间。

这条设计是必须的：音频流播完后 `AudioStreamPlayer.GetPlaybackPosition()` 归零，
`SongClock` 的单向过滤把位置冻结在最后一次读数，`songTime` 不会再增长。
若写成瞬时条件 `songTime >= EndTime + 2 && audioEnded`，谱末条件可能永远不再成立。
因此**禁止**用 `audioEnded && t >= EndTime` 之类的瞬时判断替代。

回退路径：固定帧验证时钟和 headless 手动回退时钟不产生自然结束信号
（`SongPlayback.TracksNaturalEnd == false`），此时结束条件只等谱末锁存——
这是自动回归实际走的路径，不会因为等不到 `audioEnded` 而死锁。

长音乐尾段因此在谱末 +2 秒后继续播放，Storyboard 尾段不会被提前遮住。

已知取舍：若音频流完全无法加载（`AudioStreamPlayer.Stream == null`），首帧起就没有"正在播"，
`audioEnded` 会立即成立、`songTime` 停在 0，结束条件退化为**按真实时间等满谱末 +2 秒**
（期间音符不动，可暂停返回）。这是取晚方案的直接后果，不为坏音频开"取早"的特例；
该路径只按代码推演，未在坏包上实测。

## 2. BLEED 耗尽（已实现）

- 规则仍是 `shared/Judge/BleedHealth.cs`（100 血；Standard Miss −2、Perfect +0.2、Great/Good +0.05；
  Hardcore Miss −4、Perfect +0.1、Great/Good 不回血）。
- 耗尽当帧：本帧判定与 Mixer 收尾完成 → `_playback.Pause()` → 立即 `ShowResults(stageFailure: true)`，
  **不等待**谱末或音频结束，字标为 `STAGE FAILURE`。
- 仍然复用普通结算页；专用失败结算页、Bleed 血条与失败音效仍是后续独立设计，本轮没有实现。

## 3. 结果字标前奏（已实现）

统一流程：**游玩结束 → 背景压暗 → 中央黑色斜纹区 → 对应字标拍入并同步提亮 → 短停留 →
现有 `ResultShutter` / `ResultScreen` 揭晓**。前奏在屏风之前，不是结算页播完之后。

| 阶段（Full，相对前奏开始） | 画面 |
| --- | --- |
| 0–0.30s | 全屏压暗到 0.80 不透明度（游玩画面、HUD 仍可辨） |
| 0.04–0.28s | 中央斜纹区淡入并扫过 |
| 0.20–0.60s | 字标从 1.58 倍拍入到 1.0（easeOutBack `c1=1.35`），0.20–0.29s 淡入，能量 0.18→2.25 |
| 0.52s | 命中辉光峰值（`impact`），字标与斜纹区同步提亮 |
| 0.60s 起 | 能量按 `exp(-(t-0.60)×7)` 衰减回 1.0，进入短停留 |
| 1.55s | 交付给现有屏风：600ms 合拢 → 320ms 停顿 → 480ms 拉开 → 评级与信息浮现 |

参数与几何直接取自独立预览 `design/result-outcomes/surge/godot/preview.gd`：

- 字标用 1600×400 源图按 **80%** 显示：`TextureRect` 1280×320 @ (320,380)，中心 (960,540)，
  `PivotOffset` 取自身中心；
- 斜纹区同样按预览原坐标 80%：`translate(192,108)`、`scale(0.80)`，多边形与 72px 斜纹步距同预览；
- 字标辉光用 `client/shaders/wordmark_diffuse.gdshader`（`u_energy` / `u_glow` / `u_halo_color`），
  近/远两级弥散系数与预览一致；各结果的光晕色也与预览一致。

三档动效：

| Motion | 前奏 |
| --- | --- |
| Full | 1.55s（所有有效结果，含普通完成的 TRACK COMPLETE）；仅无有效成绩数据的保护路径为 0.68s |
| Reduced | 0.26s 静态短淡入；无方向位移、无砸入缩放、无强闪（能量与辉光恒定） |
| Off | 0 帧前奏，直接显示稳定结算页；不留一帧前奏 |

其他行为：

- **普通完成**：不满足 AP、FC、ONE GOOD、ONE MISS、DOUBLE ONE 的有效正常成绩显示
  `TRACK COMPLETE`，沿用同样的拍入、提亮和停留，不再留下空白斜纹区。
- **无有效成绩数据**：只有压暗与斜纹区，随后直接进入屏风；不复用任何错误字标。
- **跳过**：前奏期间任意左键/触摸按下或 Enter/Space/Escape 视为跳过，当前事件被
  `SetInputAsHandled()` 消费，字标直接落稳定终值并交付屏风；交付后 120ms 内按压不会落到
  结算按钮上（`ResultInputGuard`，与屏风揭晓共用同一条规则）。
- **只一次**：`ResultRunSequence` 保证一次结算只进入一次前奏、只交付一次结算页，
  计时到点与跳过重复触发都会被忽略；`GameplayMain._finished` 仍是最外层闸门。
- **成绩只算一次**：`ShowResults` 计算、`TryUpdate`、`SaveDeferred` 与前次最好成绩读取保持原样，
  只在这一处发生；前奏与屏风消费同一份 `ResultScreenData`/`ScoreRecord`。AUTO 仍不写成绩，
  但仍按 AUTO 的判定计数显示 AP 字标。
- 音乐尾音继续播放（正常完成不暂停）；离开或重开仍由既有导航停止播放；BLEED 失败保持暂停。
- 本方案不含音效、专用失败页与血条。

节点与职责：`client/scripts/ui/results/ResultFlow.cs` 拥有“前奏 → 结算页”的交付顺序，
`ResultOutcomePrelude.cs` 拥有前奏节点与自己的 `_Process`（游玩场景结算后不再推进帧逻辑）、
`ResultPreludeTimeline.cs` 是纯时钟，`ResultOutcome.cs` 是纯分类器，
`ResultInputGuard.cs` 是纯防穿透算术。前奏 `ZIndex = 90`，位于结算页（100）之下、HUD（20）之上，
并且是后加入的兄弟节点，因此 `_Input` 先于结算页收到跳过事件。

## 4. 七类结果与结算页徽章同源（已实现）

`ResultOutcome.Classify(ScoreRecord, totalUnits, stageFailure)` 是唯一分类器，判定顺序即优先级：

| 类别 | 条件 | 字标资产 |
| --- | --- | --- |
| ALL PERFECT | `Perfect == totalUnits && Great == Good == Miss == 0` | `all-perfect.png` |
| DOUBLE ONE | `Good == 1 && Miss == 1` | `double-one.png` |
| ONE GOOD | `Good == 1 && Miss == 0` | `one-good.png` |
| ONE MISS | `Miss == 1`（已排除 double-one） | `one-miss.png` |
| FULL COMBO | `Miss == 0 && MaxCombo >= totalUnits` | `full-combo.png` |
| TRACK COMPLETE | 其他有效正常完成（例如 Miss ≥ 2、或未满连） | `track-complete.png` |
| STAGE FAILURE | BLEED 耗尽，不参与判定计数推导 | `stage-failure.png` |
| 无字标 | 成绩为空或谱面总判定数无效的保护路径 | — |

- AP 最高；专属字标优先普通 FC；普通完成由 TRACK COMPLETE 兜底；BLEED 耗尽一律 STAGE FAILURE。
- `ResultScreen` 的成就徽章复用同一分类器（`ResultOutcome.BadgeOf`）：AP 显示 ALL PERFECT，
  FULL COMBO 与 ONE GOOD 显示 FULL COMBO（ONE GOOD 没有 Miss 且满连，本就属于 FC 家族），
  TRACK COMPLETE / ONE MISS / DOUBLE ONE / STAGE FAILURE 不显示 AP/FC 徽章，避免与刚播放的字标矛盾。
  因此徽章可见性与改动前一致，只有 BLEED 失败被显式排除。

## 5. 资产与发行清单（已实现）

- 运行时资产：`client/assets/results/outcomes/{track-complete,all-perfect,full-combo,one-miss,one-good,double-one,stage-failure}.png`
  （保留 1600×400 源尺寸，由显示层做 80%）与 `client/shaders/wordmark_diffuse.gdshader`。
- 生成/更新脚本：`tools/grade-assets/build_runtime_outcomes.py`（复制、按现有格式写 `.import`
  （稳定 uid + 与 Godot 一致的 ctex 缓存名 md5）、追加 Public 导出白名单、刷新
  `release/asset-provenance.json` 中 `client/assets/results/outcomes/` 的七条记录）。
  它只管理 `outcomes/` 子树；`build_runtime_results.py` 现在显式跳过该子树，避免把七张字标
  贴成 Blender 评级来源。（`build_runtime_results.py` 自身字节变化后，其 29 条 `generatorSha256`
  已同步刷新。）
- 七个 PNG 的 `kind` 为 `project-generated-ui-asset`，`source` 指向 clean-room 字形生成器
  `design/result-outcomes/surge/build_surge.py`，不含原版像素。

## 6. 实际执行的检查（2026-10-28）

- `dotnet run --project tools/core-tests/CoreTests.csproj`：新增
  `[22]` 双锁存（音频先结束且时钟冻结/归零仍真实等到谱末 +2、谱末先到时等音频、
  固定帧回退走谱末路径、长帧上限、锁存单调）、`[23]` 七类分类与徽章同源、`[24]` 前奏三档与
  跳过/防穿透/只一次；原有 `Result reveal timing / loading / skip` 用例保持不变并通过。
- `dotnet build client/DynamiteUniverse.csproj`：0 warning / 0 error。
- `python -B tools/architecture/check.py --report`：结构门禁通过（含新增纯文件与 core-tests 源码链接）。
- `python -B tools/verify.py --no-restore`：完整非图形验证（见 [验证记录](<verification.md>)）。

- 主代理另以 Godot 4.7.1 Mono 完成真实导入与窗口验收：七张 `.ctex` 实际生成，着色器无编译错误；
  Full / Reduced / Off 的 Hold Recovery Test 全程 AUTO 均为 `P=6 / M=0 / outcome=AllPerfect`，
  BLEED 耗尽为 `StageFailure`。新增普通完成验收以受控验证入口指定同一短谱、AUTO=false，
  日志为 `P=0 / M=6 / outcome=TrackComplete`，无资源或着色器错误。已逐段查看
  `tmp_shots/result-{ap,failure,reduced,off,track-complete}-client.mp4` 与日志，确认 80% 比例、弥散光、
  屏风衔接以及 Off 无前奏帧；临时设置已通过 UI 恢复为 Full / BLEED 开 / AUTO 关。

**未覆盖**：Android 设备或触控、公开 APK 导出验包、坏音频包、音效、专用失败页与血条；
最终视觉验收仍待用户。

## 7. 设计源与历史候选

- 最新设计源：`design/result-outcomes/surge/`（七种字标的 SVG、1600×400 PNG、1920×1080 预览、
  Godot 预览工程与 `build_surge.py`）。`contact.png` 为全套对照。
- 早期草案 `ap-fc/`、`judgements/`、`failure/`、`family/`、`expressive/` 仅作对照，不是当前版本。
  参考图 `D:/Dynamite app/cankao/Dynamite ui pics/StdEffect_full.png` 只用于理解上一代结果类型思路。
- `design/` 是被 Git 忽略的本地设计目录；生成脚本里的 `rsvg-convert` 路径是本机配置。

## 8. 仍待处理

1. 失败/成功字标音效与音乐淡出：本轮只保留接入点，`SongClock.Pause()` 仍立即停流，
   不能用它做淡出；音效应与 Music 通道分离。
2. 专用 BLEED 失败结算页与血条 UI。
3. 用户最终视觉与设备验收（主代理窗口检查已完成）。
4. 若以后 Storyboard 在音频结束后仍有独立内容，再单独讨论第三个完成条件。
