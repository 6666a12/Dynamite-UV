# Dynamite Universe 运行时架构

> 本文描述当前社区客户端的职责边界与重构不变量。它不是玩法规格；判定与布局的权威规则仍见
> `gameplay-spec.md`、`chart-format-v2.md` 和 `GameplayMain.cs` 顶部的测量注释。

## 1. 分层与依赖方向

```text
client Godot scenes / UI / gameplay ──► presentation ──► shared
client services / input / audio ─────────────────────► shared
editor shell ──► presentation + chart-editor-core ────► shared
chart-tool / core tests ─────────────────────────────► shared
```

- `shared/` 不依赖 Godot，拥有谱面模型、legacy/v2 语义、精确时间线、判定计划、判定规则、
  分数 identity、Gameplay Digest 和确定性 v2 JSON。
- `client/` 是 Godot 适配层：读取本地包、创建节点和音频播放器、归一化输入、驱动 shared
  判定、保存用户设置与成绩、展示各场景。
- `presentation/` 是共用 Godot 视觉程序集：字体、基础 UI、音符外观和舞台。它不引用 client/editor，
  不读取其全局设置/会话；由调用方传入视觉参数。资源路径保持，两端各自提供资源。
- `tools/chart-editor-core/` 是纯 C# 的可编辑文档、命令、文档会话、画布投影及草稿模型。
- `tools/` 与 `tools/chart-editor*` 使用 shared 作为格式和规则来源；客户端不得引用 editor 项目。

## 2. 场景与会话流

```text
main.tscn ── PLAY ──> song_select.tscn ── START ──> gameplay.tscn
     ↑                     ↑                         │
     └──── settings.tscn ──┴──── result / pause ──────┘
```

- 场景文件是轻量入口；`Main`、`SongSelect`、`SettingsScreen` 和 `GameplayMain` 在运行时创建
  控件。
- `GameSession` 是唯一跨场景会话 façade：初始化 settings/scores/catalog，保存已提交的谱面
  选择，并维护与选择匹配的预加载 `LoadedChart`。
- `SongSelect` 的本地索引只表示浏览状态；只有 START 才提交跨场景选择。预加载只能缓存当前
  浏览的 pack/chart，不能隐式改变已提交选择。

## 3. 谱面加载与格式边界

`ChartCatalog` 扫描目录并维持发布策略：Editor/Internal 先扫描 `res://testdata/packs` 再扫描
`user://charts`，Public 只扫描 `user://charts`；同 id 的用户包覆盖开发包。

`ChartLoadService` 是加载模式边界：

- 显式 v2：必须走 shared 严格解码、跨文件校验与 Digest 路径，失败即拒绝；
- normal legacy：先尝试 legacy → v2 语义转换；转换不适用时才保留 legacy-direct 兼容路径；
- explicit legacy-direct：仅供兼容/一致性验证，不能替代显式 v2 的 fail-closed 行为。

`ChartPack` / `ChartDiff` 是 UI 的稳定目录元数据模型；`LoadedChart` 是供 gameplay 使用的
运行时图、时间线、scroll、同步描边、成绩身份与派生统计的只读载体。

## 4. GameplayMain 的协调顺序（不可重排）

每帧的执行顺序是行为合同：

1. 读取/推进 `SongClock`；
2. 计算当前 BarTime；
3. 生成音符视图；
4. 更新音符、连接体与延后视觉；
5. 将 Godot 输入归一化为帧触点快照；
6. 更新 Hold/Mixer sustain 状态；
7. sweep 尚未结算的判定单元；
8. 收尾已结束的 Mixer；
9. 刷新 HUD 并写 Internal verification trace；
10. 判断结束并展示结算。

第 10 步的结束判断是 `GameplayEndCondition` 的双锁存：`songTime` 到达谱末 +2 秒与
`SongPlayback.HasNaturallyEnded` 分别单调锁存，两者都成立才 `ShowResults`；
音频自然结束后 `songTime` 会冻结/归零，因此该对象用音频结束后的真实帧 `delta`
（单帧上限 0.5 秒）补足谱末条件，回退/固定帧时钟没有自然结束信号时只走谱末路径。
BLEED 耗尽在本帧第 8 步之后、第 9 步之前单独短路：先 `Pause()` 再以 `stageFailure` 进结算。

`JudgePlan`、`JudgeEngine`、`InputJudgeRules`、`V2InputProtection`、
`SustainJudgementRules`、`VisualScrollMath` 和 `DropSpeedMap` 是 shared 权威规则，客户端
只编排它们，不复制规则。

Mixer 的 `MixerRuntime` / `MixerContactGeometry` / `MixerTimeline` 位于 shared：输入按时间戳推进，
接收头独立于手指存活，路径边界用 evaluator 多项式求根。`GameplayMain.Mixer.cs` 负责输入投影、
早头授权和视图更新，缓存 tick 结果给第 7 步统一应用，不能提前按 Mixer 分组修改 Score/Combo。

需要区分两种时间：

- **song-clock time**：音频播放、判定和普通 legacy 显示使用；
- **chart/audio time**：v2 timeline/scroll 查询使用，按现有用户 timing offset 换算。

不要借由重构调整这两者的算式。

实时音频补偿读数在 SongClock 通过 `MonotonicSongPosition` 保证单次播放周期不回退；
输入时间戳与帧时钟共享下限，恢复保留暂停下限，显式 Play/Seek 才重置。回退期间等待音频追上，
不累加假时间，用户校准仍在原始音频位置过滤后叠加。Mixer 另对迟到输入作顺序容错。

## 5. 展示、成绩与发布不变量

- `GameplayStageView`、`GameplayHud`、Pause 和 Result 是展示层；HUD 接收显示值快照，成绩计算/写入仍由 gameplay 在显示
  Result 前执行。结算的展示顺序是 `ResultFlow`（前奏 → 结算页）：`ResultOutcomePrelude`
  以压暗/斜纹区/结果字标做 1.55s（Reduced 0.26s、Off 0）前奏，自带 `_Process`，因为
  `ShowResults` 之后游玩场景不再推进帧逻辑；随后现有 Result v2 用两扇不透明屏风合拢才隐藏舞台，
  屏风背后先铺不透明结算背景，再拉开、播放评级帧、浮现各组信息。封面只在顶部小图中显示，
  缺图使用 clean-room placeholder。Reduced/Off 同样先接管不透明背景，不能让游玩层透出。
  `ResultScreen` 只展示数据（含与字标同源的 AP/FC 徽章），`ResultRevealTimeline` 管理屏风时序/加载等待/跳过，
  `ResultPreludeTimeline` 管理前奏时序，`ResultOutcome` 是唯一分类器，
  `GameplayMain.ShowResults` 继续拥有成绩快照与保存职责，并且只执行一次。
- AUTO 演示绝不写成绩。v2 使用 `(packId, chartId, rulesetId, gameplayDigest)`；legacy 仍使用
  历史 `packId:diff` 兼容键。两者不能合并或删除。
- 玩法坐标固定为 1920×1080。不能引入自适应游玩布局，且改布局数值前必须先更新相应测量依据。
- Public APK 绝不扫描、加载或打包 `client/testdata/`，也不得包含任何非 clean-room 素材或谱面。

## 6. 当前编辑器适配边界

- 独立 Godot shell 的 Start/Pack/Create/Edit 负责页流；Start/HUD/Settings/ContextMenu 已有独立组件，
  shell 保留文档会话、对话框与导航。新的 editor 产品方向暂不实施或排期。
- `EditorDocumentSession` 管理打开/草稿/保存；`EditorPackageRepository.Open` 做本地 strict v2 目录包打开。
- `AuthoringCanvas` 绑定 `EditorDocument` / `CanvasAuthoringController`；`CanvasProjection` 建立可变预览，
  `CanvasEditGesture` / `CanvasPlacementDraft` 管理已有对象手势与新建草稿。修改通过可撤销命令进入文档。
  画布坐标中的 Center 仍为左缘；精确 BarTime、选择恢复和坐标换算不得借拆分改语义。
- shared 的 `V2PackageWriter.Write/Create` 已由 shell SAVE/SAVE AS 调用；首次保存使用草稿的外部资源映射。ZIP/XML/DY 导出仍未接入。
- 视觉计时预览不等同 `SongClock` 音频预览；Settings 已有部分真实设置，Storyboard 和更完整属性/类型 UI 仍未实现。
- 能力范围与启动操作统一见 [DynaMaker UV](<dyna-maker-uv.md>)，历史完整 UI 记录不能跨阶段套用。

## 7. 回归层级

- **构建层**：`cd client && dotnet build` 必须 0 error。
- **shared 行为层**：`tools/core-tests` 覆盖 timing、input、sustain、score、legacy/v2 语义，
  以及结算相关纯逻辑（屏风揭示、字标前奏三档、结果分类、结束双锁存）。
- **格式层**：chart-tool self-test、editor tests 和 schema validator 验证 v2 转换、编码、Digest
  与包安全性。
- **等价层**：涉及 legacy/v2 runtime 行为时，以 Internal deterministic trace / frame hash
  比较作为附加护栏；不用手工启动游戏替代自动回归。

## 8. 当前拆分与结构检查（2026-10-25）

文件导航与后续对象级拆分见 [模块地图](<module-map.md>)。
`GameplayMain.Input` 注入投影函数给 shared `PointerInputBuffer`，帧快照和指针生命周期由后者维护。
`SongDetailRequest` 与 `SongPreviewPlayer` 各自使用 shared `LatestRequestLoader` 管理详情/试听任务；
主线程消费结果，仍由页面核对当前选择并更新控件。`SongCardMotion` 拥有卡片动画状态，
`SongDetailCard` 拥有卡片控件与展示；页面保留列表、加载身份、缓存和导航。`CalibrationSampling`
拥有采样相位、热身、入样和完成，`CalibrationBeatSource` 拥有合成节拍与零偏移时钟；页面保留展示和设置保存。
`GameplayPauseOverlay` 拥有暂停状态、覆盖层和动效，宿主保留输入清理、播放暂停/恢复与导航。
`ResultFlow` 拥有“字标前奏 → 结算页”的交付顺序与“只一次”闸门，`ResultOutcomePrelude` 拥有前奏节点与
自己的 `_Process`，`ResultOutcome` / `ResultPreludeTimeline` / `ResultInputGuard` / `GameplayEndCondition`
是纯 C#（core-tests 直接链接）；`GameplayMain` 只保留成绩快照、归档节奏与导航回调。
editor 使用 ChartEditor.Core 和 Presentation 程序集，已移除所有 client 源码链接。

Godot 页面分部文件仍共享宿主状态，不能以单个入口变短认定整个类已解耦。
`tools/architecture/check.py` 同时检查依赖方向、显式源码链接、文件长度和主要分部类总量；
`tools/verify.py` 串起该检查、两端构建、两套 C# 回归、发布测试和 diff 检查。

第二轮中 GameplayMain 把渲染与特效生命周期交给 `GameplayNoteRenderer` / `GameplayEffects`，
`SustainRuntime` 仅保存判定状态；渲染只读取 `SustainVisualSnapshot` 值和判定结果。
节点仍挂在原 `_noteRoot`，创建/回收顺序及每帧协调顺序保持。editor 的文档/草稿资源归
`EditorDocumentSession`，已有对象的手势提交/取消归 `CanvasEditGesture`；选曲浏览归 `SongBrowseState`。
详见 [第二轮行为核对](<archive/reports/2026-10-25-modularization-phase2.md>)，其中明确记录一次草稿保存行为修正。

第三轮的公共视觉、HUD、音源、独立控件、editor 页面与画布模型见 [第三轮行为核对](<archive/reports/2026-10-25-modularization-phase3.md>)。
控件通过 provider 获取当前动效/落速，不保存全局会话；公共视觉无应用级依赖。源码/分部类预算同时收紧。
无窗口前后轨迹与桌面程序集发布验证不替代 APK 导出和设备视觉、触控、声音验收。
