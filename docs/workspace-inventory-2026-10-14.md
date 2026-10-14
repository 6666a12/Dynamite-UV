# 工作区盘点基线 — 2026-10-14

## 1. 范围与基线

本轮只做 Git/源码 diff/项目引用/文件目录/资产哈希核对，以及文档状态同步；没有重构、清理、暂存、提交或重新出包。下面计数取**新增本文之前**的工作区快照，本文自身会使新增文档数增加 1。

- 仓库：`D:/Workspace/Dynamix/community`（不是外层 Workspace 根）。
- 分支：`main`。
- HEAD：`333be1feb5d72146e16f352ff26d283157644e8c`，标题 `10.12 commit: fix many bugs`。
- Git 已跟踪文件：460；暂存区为空；无删除/重命名状态。
- 未提交：47 个 tracked modified + 36 个 untracked = **83 个文件**（`git status --porcelain=v1 -uall`）。

| 目录 | 修改 | 新增 | 合计 |
| --- | ---: | ---: | ---: |
| client | 18 | 20 | 38 |
| shared | 11 | 7 | 18 |
| tools | 5 | 5 | 10 |
| docs | 12 | 4 | 16 |
| release | 1 | 0 | 1 |
| editor | 0 | 0 | 0 |

editor 无未提交源码，不代表完全不受影响：它引用 shared，并源码链接 client 的部分视觉类。

## 2. 全部改动清单与归属

下列 M=修改，U=新增；目录均相对仓库根。分类描述是依据当前 diff 的工作内容，不推定历史作者。多功能交叉文件单独列出，不能按整文件机械拆提交。

### A. 加载、缓存与选曲异步（7 M + 1 U）

- M `client/scripts/game/ChartPack.cs`：meta 字节单次读取复用。
- M `client/scripts/game/GameSession.cs`：谱面预加载 LRU×4。
- M `client/scripts/game/Res.cs`：音频缓存与虚拟 FileAccess 锁。
- M `client/scripts/game/V2Integration.cs`：选中差分按需深校验、res:// 分支锁。
- M `shared/Chart/V2/Integration.cs`：结构校验与选中条目校验分开。
- M `shared/Chart/V2/Validation.cs`：新增 ValidatePackStructure/ValidateEntry，完整 Validate 仍保留。
- M `shared/Chart/V2/V2JsonDecoder.cs`：读取 type 时减少中间数组。
- U `shared/IO/PathResourceCache.cs`：缓存基础设施。

还依赖交叉文件 SongSelect 的异步加载与试听接线；当前“结构＋当前差分深校验”不等于进场深度解码包内全部差分，文档描述应保持精确。

### B. 视图与特效对象池（4 M + 1 U）

- M `client/scripts/game/GameplayHitBloom.cs`
- M `client/scripts/game/GameplayHitParticles.cs`（同文件含 SustainParticles）
- M `client/scripts/game/GameplaySustainEffect.cs`
- M `client/scripts/game/NoteView.cs`
- U `shared/Judge/VisualPoolPlanning.cs`

内容：重置/复用接口、池容量规划、生命周期与材质/几何缓冲复用。GameplayMain 含池接线、Hold 连接体归位与重复 AddChild 修复。

### C. 输入、命名与短 Hold（5 M + 2 U）

- M `shared/Judge/V2InputProtection.cs`：精确 double 时间批次锁，早晚侧统一。
- M `shared/Judge/InputJudgeRules.cs`：Perfect 命名同步。
- M `shared/Judge/JudgeSettings.cs`：Perfect 字段命名同步。
- M `shared/Judge/OriginalJudgeMath.cs`：Perfect 命名同步。
- M `client/scripts/game/V2IntegrationVerification.cs`：trace 的 perfect 字段同步。
- U `shared/Judge/HoldContactFrame.cs`：带时间戳的多触点重放。
- U `tools/core-tests/ShortHoldTests.cs`：晚头、同帧输入、尾、换指、分母回归。

头尾与累计分母实现还在 JudgeEngine/GameplayMain 交叉文件。Perfect 重命名贯穿多个模块，建议先形成可构建的全调用链提交，不能只改枚举或设置字段。

### D. Mixer 放置接收器（5 U）

- U `shared/Judge/MixerContactGeometry.cs`
- U `shared/Judge/MixerRuntime.cs`
- U `shared/Judge/MixerTimeline.cs`
- U `client/scripts/game/GameplayMain.Mixer.cs`
- U `client/scripts/game/GameplayMain.Mixer.cs.uid`

内容：短按放置、有效移动、松手保留、多指接力、独立接收、音乐时间宽限。依赖 Evaluation 求根、GameplayMain 状态/计分接线、MixerTests。

### E. 音频时间回退（1 M + 1 U）

- M `client/scripts/audio/SongClock.cs`
- U `shared/Judge/MonotonicSongPosition.cs`

还有 MixerRuntime 迟到输入容错、GameplayMain.Mixer 成功后提交 tick 游标、MixerTests 回归。应在 D 的基础上提交，或随 D 一并保留已修复状态。

### F. 八评级与美术交付（8 M + 20 U）

- M `shared/Score/ScoreStoreCore.cs`：八档门槛、旧成绩重算、显示/资源键。
- M `client/scripts/ui/ResultGradeArt.cs`
- M `client/scripts/ui/ResultScreen.cs`
- M `client/scripts/ui/UiFonts.cs`
- M `client/export_presets.cfg`
- M `release/asset-provenance.json`
- M `tools/grade-assets/build_runtime_results.py`
- M `tools/grade-assets/package_grade_set.py`
- U `tools/grade-assets/review_missing_grades.py`
- U `tools/core-tests/GradeTests.cs`
- U `client/assets/results/background_{D,X,Y}.svg` 及各自 `.import`：6 个文件。
- U `client/assets/results/grades/{D,X,Y}.png`、`{D,X,Y}_entry.png` 及各自 `.import`：12 个文件。

补充：`render_grade_set.py` 在下一组工具交叉项，SongSelect 含 Ψ 标签接线。PNG/SVG 与导入描述、导出白名单、来源清单需一起审查；`.import` 和 `.uid` 是应保留的源侧元数据，不是 `.godot` 缓存。

### G. 跨功能集成与测试/工具（9 M + 2 U）

| 状态 | 文件 | 混合内容 |
| --- | --- | --- |
| M | `client/scripts/game/GameplayMain.cs` | 输入桶、对象池、Hold、Mixer、分数、Perfect 命名 |
| M | `client/scripts/game/GameplayMain.Presentation.cs` | 清触点、Mixer 控制清理、HUD 分母、延后存档 |
| M | `client/scripts/game/ScoreStore.cs` | TryUpdate 与 SaveDeferred 分开 |
| M | `client/scripts/ui/SongSelect.cs` | 详情/试听异步、缓存、Ψ 显示 |
| M | `shared/Chart/V2/Evaluation.cs` | runtime double 时间预算、PCHIP 缓存、Mixer 几何求根 |
| M | `shared/Judge/JudgeEngine.cs` | Perfect 命名、Hold 尾等待/结算、已判分母 |
| M | `shared/Judge/SustainPath.cs` | v2 runtime 路径求值 |
| M | `tools/core-tests/Program.cs` | 旧用例适配、命名更新、新回归入口 |
| M | `tools/grade-assets/render_grade_set.py` | X/Y/D 原创网格与分档渲染入口 |
| U | `tools/core-tests/BehaviorEquivalenceTests.cs` | 时间/PCHIP/扫描/池等价检查；旧 Mixer 镜像已移除 |
| U | `tools/core-tests/MixerTests.cs` | 新机制、曲线、帧切分及音频回退回归 |

### H. 文档与历史口径（13 M + 4 U）

- M `docs/README.md`
- M `docs/blender-pipeline.md`
- M `docs/chart-format-v2.md`
- M `docs/editor-ui-design.md`
- M `docs/gameplay-spec.md`
- M `docs/handoff.md`
- M `docs/original-judgement-analysis.md`
- M `docs/pixel-calibration.md`
- M `docs/releasing.md`
- M `docs/result-v2-design.md`
- M `docs/runtime-architecture.md`
- M `docs/video-geometry-analysis.md`
- M `tools/release/tests/test_brand_identity.py`：历史归档旧项目名的允许计数。
- U `docs/grade-d-art.md`
- U `docs/grade-xy-art.md`
- U `docs/mixer-receiver.md`

文档应尽量随所属功能提交。侧轨 691/790 实验及测量文档属于独立验收项；不要把它混写成 Mixer 已验收的一部分。

## 3. 产物与验证/验收基线

### 最新测试 APK（本轮重新计算大小和哈希）

- 路径：`client/builds/internal/dynamite-universe-internal-testdata.apk`
- 版本：`0.1.2-internal-testdata`，code 4；ARM64；Debug/Internal。
- 大小：185406710 bytes（约 176.82 MiB）。
- SHA-256：`235dc36bc64214e1ff67978168346f900ae2453e6e5f26ed661b77867d904e82`
- 既有导出记录：2026-10-14，APK gate PASS；本轮哈希一致，没有重新运行导出或验包。
- 从未提交工作区导出，**不能用 HEAD 独立重建这个 APK**。本轮盘点不构成逐文件构建输入锁定清单。

| 项目 | 自动检查记录（前轮） | 用户反馈／当前状态 |
| --- | --- | --- |
| X/Ψ/D 八评级 | core、发布检查、构建、逐帧校验通过 | 用户明确认可已接入客户端的三档 |
| Mixer 接收器 | core、editor-tests、client/editor build、35 项发布测试通过 | 用户反馈“目前看来没什么问题”；初步正向，不扩写成所有设备/边界验收 |
| 时间回退修复 | 新增 clock/迟到输入回归及全部 core 通过，client 构建成功 | 已包含最新 APK；没有单独收到暂停恢复专项结论 |
| 短 Hold | ShortHoldTests 与核心/编辑器回归通过 | 用户表示暂不继续 Hold 调查；不是新增探针验证通过 |
| 前轮加载/对象池修复 | 历史构建与等价回归通过 | 低端帧率、快速切歌、池修复专项反馈仍不完整 |
| 侧轨行程比实验 | 代码/文档已有记录 | 保留待确认 |

最近 client 构建遗留 `SongSelect.cs:1223` CS8604；editor 构建 0 warning/error。本轮未重跑这些测试。
本轮核对 `release/asset-provenance.json` 中 results 资产 29 项，全部与当前文件 SHA-256 相符。

## 4. 本地资产与磁盘目录

以下为目录中文件逻辑大小（MiB），不等同文件系统可释放容量；不遍历符号链接，不包含整个外层 Workspace。

| 路径 | 文件数 | MiB | 性质与后续处理 |
| --- | ---: | ---: | --- |
| `design/` | 3084 | 438.31 | 美术源工程、分层、交付；被忽略，独立备份后才考虑去重 |
| `client/testdata/` | 41 | 257.64 | 本地测试谱/音频；保留 Internal 边界 |
| `client/.godot/` | 887 | 294.80 | 导入/构建缓存，但也含本地 export credentials，不能整个目录当垃圾清空 |
| `editor/.godot/` | 273 | 92.28 | 本地引擎缓存；清理前确认本地配置 |
| `client/builds/` | 4 | 345.88 | 最新测试 APK、旧 release-probe 及 idsig，保留最新并区分实验包 |
| `builds/` | 193 | 185.07 | 历史构建输出；按批次确认保留需求 |
| `.tmp/` | 219 | 118.93 | 日志、截图、探针和研究中间件，逐用途确认 |
| `tmp_shots/` | 215 | 113.86 | 历史视觉验证材料 |
| `local-generated/` | 211 | 4482.57 | 全部在 v2-equivalence，下有录制/并排/差分视频；最大整理收益约 4.38 GiB，先保留必要证据 |
| `.analysis-literal-probe/` | 7 | 0.04 | 本地探针 |
| `analysis_literal_probe/` | 7 | 0.04 | 本地探针，需确认与上项是否重复 |
| `WorkspaceDynamixcommunity.tmpapk-check/` | 1 | 151.68 | 非标准路径旧 APK；文件被通用 `*.apk` 忽略，目录无专门规则 |

`client/builds/internal/` 另有 `dynamite-universe-internal-release-probe.apk`（174422896 bytes），用途是前轮性能实验，不能当成最新测试包。

### 美术源工程核对

以下目录存在 `.blend` 与交付 ZIP；仅确认存在，未建立仓库外备份，也未验证备份恢复：

- `design/grades/v5_current/`：A/B/C/omega 源工程、`motion_S/S_surface_current.blend`、旧五级 ZIP。
- `design/grades/v5_current_xy/`：`set/X/X.blend`、`set/Y/Y.blend`、X/Y ZIP。
- `design/grades/v5_current_d/`：`set/D/D.blend`、D ZIP。

### 两个需要归类的具体文件

1. 根目录 `result.json`：已跟踪，51814 bytes，旧自动审查报告（记录 42 个选中项、20 条发现）；是历史报告，不是配置。建议核对引用后迁到带日期的审查归档并记录哪些发现已处理，本轮保留原位。
2. `.tmp/hold-review/`：上轮新建的临时 Hold 探针。已编译，但启动因本机缺 net9 runtime 且临时项目未配置 RollForward 而失败；正式测试工程有 LatestMajor。探针没有产出运行结果，不能把它引用为缺陷复现证据。

Git 跟踪文件中未发现 `.godot/bin/obj/__pycache__/node_modules` 内的缓存或 APK/DLL/PYC/LOG，testdata 跟踪文件数为 0。根目录历史报告是组织卫生问题，不是构建污染。

## 5. 项目引用

- client → shared（ProjectReference）。
- chart-editor-core → shared；chart-editor-tests → chart-editor-core + shared。
- editor → shared；通过 Compile Link 引入整个 editor-core 源码和 client 的 11 个 UI/渲染文件。
- core-tests → shared，并源码链接 UiMotionMode、ResultRevealTimeline、CalibrationStats。
- chart-tool → shared。

核心依赖方向清楚；editor 的源码链接与测试的程序集引用是后续模块化候选，当前盘点不迁移。没有看到 client 引用 editor 项目。

## 6. 已知问题与状态冲突

| 编号 | 项目 | 状态／下一动作 |
| --- | --- | --- |
| I-01 | 47 M/36 U 未组成提交基线 | 按下一节分批审查；未授权本轮提交 |
| I-02 | 宽泛“Mixer 待验收”描述落后于用户反馈 | 本轮同步为初步反馈正常，专项未穷尽 |
| I-03 | Hold 宽限“不补 tick”的旧交接描述与当前节点 Perfect 分支不一致 | 本轮改为描述当前实现，并保留帧末批量结算问题待核查 |
| I-04 | Hold 路径无输入断触只在帧/事件采样，帧内跨 deadline 时中间节点顺序疑点 | 静态疑点，探针未运行，用户暂缓；不是本轮修复任务 |
| I-05 | 多个文档带历史“未出包/待验收”语句 | 当时验证记录可保留，但必须让最新状态有明确入口 |
| I-06 | SongSelect CS8604 | 已知构建警告，后续加载/试听拆分时处理 |
| I-07 | Internal Debug 性能、Mixer 密集曲线分配 | 需要设备基准；不是盲目改 Release 政策的依据 |
| I-08 | 无自定义项目图标导致导出提示 | 最近导出仍完成且验包 PASS；独立产品资源事项 |

## 7. 建议提交顺序（尚未执行）

1. **命名与输入基础**：完整 Perfect 重命名链、精确批次锁、输入有效性及对应测试。
2. **加载与性能**：A 组 + SongSelect 的异步片段、Evaluation 的预算/缓存片段；对象池 B 组可以另成批，但与 GameplayMain 接线一起。
3. **Hold 与计分分母**：HoldContactFrame、ShortHoldTests、JudgeEngine/GameplayMain/HUD 的对应片段。
4. **Mixer 与时钟修复**：D/E 组、Evaluation 求根、MixerTests、适配及文档；确保提交包含最新时钟修复。
5. **八评级**：完整 F 组、render 脚本、SongSelect 显示片段、交付文档。必须包含全部 18 个资产/导入文件。
6. **独立文档与卫生记录**：归档白名单、测量实验记录、本文与索引；各功能规格优先随所属代码批次提交。

这是依赖顺序建议，不是已验证每批可独立构建的拆分结果。GameplayMain、Evaluation、SongSelect、JudgeEngine、Program.cs 必须按 diff hunk 分拆并逐批检查；不可简单以目录作为提交边界。当前 APK 保留为整体行为参考。

## 8. 下一步建议

先确认是否按上述批次形成版本基线，再开展 GameplayMain 输入/视图职责拆分。磁盘清理单独选定保留策略：优先历史录像与旧构建包，设计源工程和本地凭据明确保护。盘点本身没有删除任何文件。
