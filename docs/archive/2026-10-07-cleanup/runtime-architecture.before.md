# Dynamite Universe 运行时架构

> 本文描述当前社区客户端的职责边界与重构不变量。它不是玩法规格；判定与布局的权威规则仍见
> `gameplay-spec.md`、`chart-format-v2.md` 和 `GameplayMain.cs` 顶部的测量注释。

## 1. 分层与依赖方向

```text
Godot scenes / client UI / gameplay adapters
                 ↓
     client game services and presentation
                 ↓
shared chart / v2 / judge / score semantics
                 ↑
       chart-tool / chart editor / core tests
```

- `shared/` 不依赖 Godot，拥有谱面模型、legacy/v2 语义、精确时间线、判定计划、判定规则、
  分数 identity、Gameplay Digest 和确定性 v2 JSON。
- `client/` 是 Godot 适配层：读取本地包、创建节点和音频播放器、归一化输入、驱动 shared
  判定、保存用户设置与成绩、展示各场景。
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

`JudgePlan`、`JudgeEngine`、`InputJudgeRules`、`V2InputProtection`、
`SustainJudgementRules`、`VisualScrollMath` 和 `DropSpeedMap` 是 shared 权威规则，客户端
只编排它们，不复制规则。

需要区分两种时间：

- **song-clock time**：音频播放、判定和普通 legacy 显示使用；
- **chart/audio time**：v2 timeline/scroll 查询使用，按现有用户 timing offset 换算。

不要借由重构调整这两者的算式。

## 5. 展示、成绩与发布不变量

- `GameplayStageView`、HUD、Pause 和 Result 是展示层；成绩计算/写入仍由 gameplay 在显示
  Result 前执行。Result v2 用两扇不透明屏风合拢后才隐藏舞台，屏风背后先铺不透明
  结算背景，再拉开、播放评级帧、浮现各组信息。封面只在顶部小图中显示，缺图使用
  clean-room placeholder。Reduced/Off 同样先接管不透明背景，不能让游玩层透出。
  `ResultScreen` 只展示数据，`ResultRevealTimeline` 管理时序/加载等待/跳过，
  `GameplayMain.ShowResults` 继续拥有成绩快照与保存职责。
- AUTO 演示绝不写成绩。v2 使用 `(packId, chartId, rulesetId, gameplayDigest)`；legacy 仍使用
  历史 `packId:diff` 兼容键。两者不能合并或删除。
- 玩法坐标固定为 1920×1080。不能引入自适应游玩布局，且改布局数值前必须先更新相应测量依据。
- Public APK 绝不扫描、加载或打包 `client/testdata/`，也不得包含任何非 clean-room 素材或谱面。

## 6. 回归层级

- **构建层**：`cd client && dotnet build` 必须 0 error。
- **shared 行为层**：`tools/core-tests` 覆盖 timing、input、sustain、score、legacy/v2 语义。
- **格式层**：chart-tool self-test、editor tests 和 schema validator 验证 v2 转换、编码、Digest
  与包安全性。
- **等价层**：涉及 legacy/v2 runtime 行为时，以 Internal deterministic trace / frame hash
  比较作为附加护栏；不用手工启动游戏替代自动回归。
