# 模块地图与拆分边界

2026-11-07 按当前项目引用和结构报告复核；制谱器采用 Avalonia Shell + Godot 舞台，保留纯编辑核心。
本文只维护当前代码导航与对象边界；玩法合同见 [gameplay-spec](<gameplay-spec.md>) 与 [chart-format-v2](<chart-format-v2.md>)，执行结果统一见 [验证记录](<verification.md>)。

目前已建立纯规则核心、纯编辑核心、共享 Godot 视觉、游戏和编辑器适配五个边界。
渲染/特效、文档会话、浏览、试听、校准音源、HUD、画布选择/命中/手势预览、创建请求、Track Handoff、选曲视口与设置编解码已独立成对象。
制谱器现行 UI 为 Avalonia Shell + Godot 原生舞台；旧 Penpot Editor 页面和旧 Godot 画布宿主不再作为现有模块或拆分对象。
第三轮范围与行为证据见 [第三轮模块化](<archive/reports/2026-10-25-modularization-phase3.md>)；此前的草稿保存修正见
[第二轮模块化](<archive/reports/2026-10-25-modularization-phase2.md>)。历史报告不代表旧 UI 仍存在。

## 工作区与工程关系

| 路径 | 当前用途 |
| --- | --- |
| `community/` | 实际产品仓库：Godot 游戏、桌面制谱器、纯 C# 核心与回归工具 |
| 外层 `server/` | C++/Boost 起步骨架，main 只创建 io_context，尚未接入产品 |
| 外层 `godot/` | 本地 Godot Mono 引擎，不是产品源码 |
| 外层 `_rev/`、XAPK、`gameplay_videos/`、`tmp_shots/` | 研究材料、录像与截图，不进入公开产品 |
| `design/`、`local-generated/`、`.tmp/`、`builds/` | 本地设计源、生成物与验证材料；不是本轮源码拆分的清理目标 |

```text
client ──► presentation ──────────────► shared
client ───────────────────────────────► shared
editor Godot ──► presentation ────────► shared
editor Godot ──► chart-editor-core ───► shared
editor shell ──► chart-editor-core ───► shared
core-tests / chart-tool ──────────────► shared
chart-editor-tests ──► chart-editor-core + shared
```

editor 现在通过 `ProjectReference` 使用 `ChartEditor.Core.dll`，与编辑器回归测试使用同一核心程序集。
旧的 `Compile ../tools/chart-editor-core/*.cs` 已移除，命令可以按子目录组织而不会被 shell 漏编译。
editor 的 14 个客户端源码链接已全部移除。共享字体、几何、音符外观、舞台与基础控件进入
`presentation/DynamiteUniverse.Presentation.csproj`；纯设置文件写入进入 shared。
presentation 不引用 client/editor，不读取两端全局会话或设置；namespace 与 `res://` 资源路径保持。
两端继续各自提供字体、shader 等资源，独立程序集并不自动合并资源目录。

## 当前对象边界

| 组件 | 拥有的职责 | 调用方仍拥有的职责 |
| --- | --- | --- |
| `shared/Judge/PointerInputBuffer.cs` | 指针存活、帧首/帧末快照、原始按下时间、同帧短点、Hold 联系快照与清理 | Godot 事件读取、屏幕/镜像投影、暂停区域、判定/计分顺序 |
| `shared/Judge/BleedHealth.cs` | BLEED 血量数值表（Standard/Hardcore）、100 上限与 0 钳制、耗尽后幂等 | 判定调用点、模式选择、耗尽后的结算/导航与 UI |
| `shared/IO/LatestRequestLoader.cs` | 单并发后台加载、最新请求、取消排队、丢弃过期完成、异常传递、释放 | 曲库选择、缓存身份、播放器、封面/按钮更新 |
| `shared/Score/ScoreFilePersistence.cs` | 成绩文件加载、失败后首次保存前保留原始字节、原子写入 | ScoreStore 拥有内存成绩库、Godot 路径、延后保存与警告；不改变成绩身份和比较规则 |
| `shared/Audio/` + `shared/Chart/V2/AudioProbe*` | 托管 FLAC 解码与 WAV/MP3/FLAC/Ogg Vorbis 容器校验、时长探测和预算 | 客户端 Godot 音频流与缓存、编辑器写包事务、设备播放验收 |
| `tools/chart-editor-core/Commands/` | 现有命令按 Pack/Chart/Note/Path/Timing 与验证规则分组 | EditorDocument 的 history/dirty、shared 的格式和写包合同 |
| 两套测试目录 | 分成独立的场景测试类，公共 fixture/断言收在各自 TestSupport | Program 只解析参数、按原顺序调用测试并返回退出码 |
| `presentation/game/GameplayNoteRenderer` / `GameplayEffects` | 两端共用的音符/连接段/残影/持续接收头与特效池生命周期；外观由 `IGameplayVisualSettings` 注入，镜像由 `GameplayRenderGeometry` 实例持有 | 游戏保留判定状态、成绩、设置和既有帧顺序；编辑器保留音频/文档/输入，按只读快照驱动视觉 |
| `EditorMarqueeSelection` | 当前轨道的谱面位置/精确时间起终点、追加基线、完整父 Note 候选集合与提交/取消；不依赖渲染对象、不写文档 | 舞台使用 `EditorStagePlacement` 反查指针时间，播放/seek 更新终点并绘制投影/数量；控制器/IPC 同步 Shell 多选，文档命令仍由 Shell/Core 拥有 |
| `EditorContextMenu` | 舞台内右键面板控件、选中反馈、边缘布局和关闭输入消费；不写文档 | 舞台判断空白/取消手势，控制器持有预览音量/效果，Shell 持有工具/放置面/类型和翻译，IPC 仅发送意图与状态 |
| `EditorAutoTimeline` / `EditorAutoPreview` | JudgePlan 上的可 seek 事件游标 / 编辑器 Auto 视觉生命周期、当前持续状态恢复、CPU/GPU 暂停 | `EditorPreviewController` 仍负责音频与 IPC；`EditorStagePreview` 保留静态编辑视图、选择、手势和舞台布局 |
| `EditorDocumentSession` | 当前文档、草稿资源、打开/创建/新增差分/保存用例 | shell 的对话框、消息和页流 |
| `SongBrowseState` / `CanvasEditGesture` | 本地浏览状态 / 已有对象编辑手势的提交与取消 | UI 动画与预览投影、START 会话提交 |
| `GameplayHud` / `GameplayHudHeader` | 下方 HUD 与顶部装配，读取真实快照；`GameplayHudPlate` 拥有形状/文本/输入和 `HudLightState` 包络，`GameplayBleedBar` + `BleedHudState` 拥有血量显示插值 | 场景保留判定/血量/分数权威、既有暂停/失败回调和帧顺序；展示不计算损伤或演示统计，见 [HUD v8](<gameplay-hud-v8.md>) |
| `GameplayProgressState` / `GameplayProgressBar` / `JudgmentLineTimeline` | 顶部进度比例与成就退化、移动端点绘制 / 三轨只读活跃区间与启灯采样 | 场景提供歌曲有效时间/结束口径/判定计数；公共舞台只接收亮度，镜像映射不改变判定；见 [HUD 调整](gameplay-hud-v8.md) |
| `WorkbenchUserStore` / `WorkbenchPreferenceSession` / `WorkbenchLayoutSession` | 独立用户目录的版本化快照、原子写入/损坏备份、偏好合并写入与启动前布局恢复；布局DTO不包含控件/HWND | Shell持有活控件/进程生命周期；预览音量和效果经IPC同步，测试使用隔离目录 |
| `EditorNoteClipboard` | 完整父Note及路径的会话快照、精确相对拍时间、新身份和组合插入命令 | Shell拥有当前播放头/网格锚点、快捷键焦点保护、选区同步；不增加第二轮批量变换 |
| `SongPreviewPlayer` / `CalibrationBeatSource` | 试听加载和播放生命周期 / 合成节拍、零偏移时钟、循环位置展开 | 当前选择有效性 / 校准采样状态、统计和应用设置 |
| `CalibrationSampling` | 采样相位、目标锁定、热身、note 归属、重复/stray 与完成；纯 C# | 页面读数、展示切换、建议偏移应用、保存和导航 |
| `SongCardMotion` / `SongDetailCard` | 展开/收拢/文字/封面状态机 / 卡片控件子树和展示 | 列表位置、行可见性、底部操作、加载与页面导航 |
| `SongDetailRequest` | 详情请求代际、独立 loader、作废、完成消费和释放 | 当前 pack/chart、展开状态、缓存、试听身份和页面生命周期 |
| `GameplayPauseOverlay` | 暂停状态、覆盖层、按钮和三档动效 | 输入清理、播放暂停/恢复、导航、结算与成绩保存 |
| `GameplayEndCondition` | 谱末 +2 秒与音频自然结束的双锁存、音频结束后的真实帧补时；纯 C# | 结束条件成立后的 `ShowResults`、判定/AudioStreamPlayer 状态、固定帧推进 |
| `ResultFlow` | 一次结算的“字标前奏 → 结算页”交付顺序与“只一次”闸门 | 成绩快照与保存、结算回调（隐藏舞台、返回/下一首/重开）、结算数据 |
| `ResultOutcomePrelude` / `ResultPreludeTimeline` | 前奏节点（压暗/斜纹区/字标/着色器）与其纯时钟（Full/Reduced/Off、跳过落终值） | 何时进入前奏、结算页本身、音效与导航 |
| `ResultOutcome` / `ResultInputGuard` | 七类结果分类（普通完成为 TRACK COMPLETE）与徽章映射 / 120ms 防穿透算术；纯 C# | 展示字标与徽章、结算页输入派发 |
| `SongSelectWidgets` / `SettingsWidgets` / `CalibrationWidgets` | 控件输入、信号、绘制和局部动效 | 页面接线、设置保存；当前动效/落速用 provider 注入 |
| `HomeEnergyReactor` / `HomeReticle` | Mk VI 透明子视口渲染（FOV 32 / 18° / 22° / margin 0.90）、单 4 秒 clip 的三档速度与 canvas 弥散光能量 / 模型后方参考环绘制 | 首页布局、入场登记、当前位置与尺寸；不接管导航 |
| `HomeActionTile` / `HomeActions` | 单个动作底板（切角、中文/英文两行、可选箭头、禁用态与 hover/按下反馈）/ SELECT DESTINATION 区接线 | 真实路由（游玩→选曲、设置）与三个占位禁用入口 |
| `HomeBrandBlock` / `HomeUtilityRow` / `HomePalette` | 品牌块、顶部工具入口与版本串、Home v2 颜色与字距 token | 入场登记、`Main.BuildDescription()` 构建身份串 |
| `StandaloneEditorMain` / `EditorPreviewController` / `EditorStageAuthoring` | 装配 Godot 舞台 / 预览时钟、音频与 IPC / SELECT、PLACE、PATH、PAN 的手势状态 | Avalonia Shell 的文件、文档命令、布局、i18n 与快捷键；舞台不另写时间反查公式 |
| `EditorStagePlacement` / `StagePlacementDraft` / `EditorPlacementOverlay` | 三轨转换、侧轨尺度与精确 BarTime 反查 / 中心坐标放置、宽度与尾点草稿 / 真实 NoteView ghost、导引读数和对齐标记 | 文档命令与保存仍由 ShellChartEditor/Core 拥有；旧 CanvasPlacementDraft 的左缘语义不混用 |
| `EditorSpatialMath` | 十进制空间交互算术：规范网格值、相对位移加减与范围回退；不统一舍入文档或文件 | 舞台负责坐标投影，Shell/Core 命令负责提交；自由模式、精确输入与未修改字段保留原值，shared 保持严格格式/Digest |
| `CanvasProjection` / `CanvasPlacementDraft` | 文档到可变预览数据的映射 / 新建音符宽度与 Hold 两阶段草稿 | 新 UI 的输入、坐标/吸附、选择恢复；提交仍走文档命令 |
| `CanvasSelectionState` | Note.Id 多选、主选择/实际单选、BPM 精确时间、二级 Node.Id 与框选生命周期 | 新 UI 的输入与几何、选择事件、重绘和文档命令 |
| `CanvasHitTesting` | 逆序遮挡、节点/头/尾/身体优先级与边缘 typed hit | 新 UI 的矩形、路径求值、坐标换算及 Hit 适配 |
| `CanvasGesturePreview` | 移动、宽度、节点时间/宽度与路径精确时间平移的纯预览计算 | 新 UI 的输入、屏幕几何、Projection 回写和一次命令提交 |
| `TrackHandoffPresentation` / `TrackHandoffState` | relay UI、front/fx、相位推进与独立放电尾巴 | Signal Lock、路由、ready timeout、输入锁和转场完成时机 |
| `SongListViewport` / `SongListScrollState` | 列表触摸/滚轮输入、滚动读写、惯性与选择意图 | Browse、展开/收起、搜索、详情试听和页面导航 |
| `GameSettingsCodec` | 设置快照、JSON、旧字段迁移和配色恢复 | 唯一运行态、user://、原子写入、警告和 AudioServer |
| `EditorCreateRequest` / `EditorCreatePreflight` | New/Add 的纯请求组装、前置校验和 Add 的固定默认时间线 | 新 UI 的控件与文本解析、文件可用性查询、session、错误显示和页流 |

未落盘草稿新增难度时必须保留外部音频/封面映射；会话成功保存后才 MarkSaved。磁盘发布后的清理错误不保证回滚，不能把异常等同于“完全没写盘”。

旧编辑 HUD、设置页和画布宿主已删除。核心投影集合可修改用于预览，不得直接当作文档写入口。

`PointerInputBuffer` 不消费触点、不做镜像、不改变判定规则。`GameplayMain._Process` 的十步帧顺序保持。
同帧 press→move→release 仍有原始 press 与 Hold 时间戳，未知 Move 仍忽略；暂停/失焦由调用方清理。
快照集合复用，游玩热路径按索引读取，避免改成接口枚举后新增逐帧枚举器分配。

谱面与试听使用独立的 loader 实例，互不抢同一个并发槽。已开始的同步解码不能强行中断，
取消会使其结果失效，并取消尚未开始的工作。worker 不再对 SongSelect 调用 CallDeferred；
页面在 `_Process` 消费结果，仍检查 generation、pack/chart 与展开状态；视觉更新可相差一个渲染帧。
这部分需要后续真机快速切曲/收起/离页验证，自动回归不代表触控或试听体验已验收。

## 文件职责导航

当前文件与整个分部类的规模以 `python -B tools/architecture/check.py --report` 输出为准，预算以 `tools/architecture/policy.json` 为准；不在导航页复制易过期的“当前行数”表。行数包含空行、注释与内部类，只用于限制重新增长，不是模块化评分。历史拆分规模与行为证据见前述归档报告。

对既往进程与剩余大文件的分析见 [2026-10-26 模块化复审](<archive/reports/2026-10-26-modularity-review.md>)。其中旧 Editor 页面和画布建议仅是历史记录，不再作为当前对象边界。

Godot 场景绑定继续使用原入口文件及类名。以下分部文件用于局部整理，仍属于同一个类，
不能当作已完成对象级隔离；字段随所属职责移动，也不新增“万能上下文”对象。
物理目录按功能族组织：client UI 分为 `song_select/`、`transitions/`、`calibration/`、`settings/`、`note_colors/`、`results/`、`home/`、`common/`；
client game 分为 `gameplay/`、`charts/`、`session/`、`settings/`、`common/`、`verification/`；editor 包含 Godot 根入口、`scripts/stage/` 预览适配与 `shell/` Avalonia 工作台。
chart-editor-core 分为 `Canvas/`、`Documents/`、`Timing/` 与既有 `Commands/`；两套测试也按功能分组。场景/程序入口可留根，移动不改变 namespace 或类名。

| 入口 | 职责文件 |
| --- | --- |
| `game/GameplayMain.cs` | 启动、选择、测量常量和每帧协调；其余判定分部、HUD/暂停位于 `game/gameplay/`，共用音符渲染/特效位于 `presentation/game/`，结束双锁存在 `gameplay/GameplayEndCondition.cs` |
| `Main.cs` | 首页场景装配与入场动画；Home v2 布局、品牌块、动作区、顶部工具行、参考环与 Mk VI 核心都在 `ui/home/`（旧 `HomeEnergyCore.cs` 已不被引用） |
| `ui/results/ResultScreen.cs` | 结算页数据展示与输入闸门；屏风在 `ResultShutter.cs`，屏风时钟在 `ResultRevealTimeline.cs`，交付顺序在 `ResultFlow.cs`，字标前奏在 `ResultOutcomePrelude.cs` / `ResultPreludeTimeline.cs`，分类在 `ResultOutcome.cs` |
| `ui/song_select/SongSelect.cs` | 会话、选中与导航；`.Library` 列表/搜索，`.Loading` 详情与试听接线，`.QuickSettings` 快捷设置，`.Motion` 卡片推进/页面进场，`.Layout` 页面布局；滚动/触摸在 `SongListViewport.cs`，惯性在 `SongListScrollState.cs`，卡片/动画/详情代际各自独立 |
| `ui/CalibrationScreen.cs` | 页面协调；其余分部、采样、音源和控件位于 `ui/calibration/` |
| `ui/SettingsScreen.cs` | 页面状态、保存与动效；字段布局与控件位于 `ui/settings/` |
| `game/settings/GameSettings.cs` | 唯一运行态、Godot 读写与音频适配；JSON/迁移在同目录 `GameSettingsCodec.cs` |
| `ui/transitions/TransitionOverlay.cs` | Signal Lock 与 Track Handoff 转发；relay/front/fx 在 `ui/transitions/TrackHandoffPresentation.cs`，纯相位在 `ui/transitions/TrackHandoffState.cs` |
| `StandaloneEditorMain.cs` / `stage/` | 原生舞台装配；预览、IPC、音频与 SELECT/PLACE/PATH/PAN 手势分别由独立对象负责 |
| `editor/shell/ShellChartEditor.cs` | 把舞台、Inspector 和 Timing 请求转换为原子文档命令；`TimingView` 只构造 BPM/Scroll 命令，窗口不堆编辑规则 |
| `tools/chart-editor-core/Documents/` | 文档、会话、创建/打开/保存、Digest 与校验；`EditorGameplayDigest.cs` 为纯核心服务，`EditorPackageSettings.cs` 把包/差分设置与资源替换组成一次可撤销事务 |
| `tools/chart-editor-core/Canvas/` | 投影、放置草稿、选择、命中与手势预览纯计算；舞台手势在 `editor/scripts/stage/EditorStageAuthoring.cs`，未接手势仍复用这些纯模型 |
| `EditorCommands.cs` | `IEditorCommand` 与组合事务；具体命令位于 `Commands/`，命名空间/API 保持 |

## 后续功能放哪里

- 判定、Hold/Mixer 规则和精确时间进入 shared，并先用现有行为用例说明边界；不要加到页面或绘制方法。
- 选曲加载策略进入 loader/加载服务；列表筛选、排序的纯状态可进一步提成独立模型。
- 编辑器数据修改通过命令进入文档；新命令放在对应 Commands 组。持久化规则继续以 shared 写包器为准。
- 两端共用的 Godot 视觉进入 presentation；通过参数传外观，不在此读取应用级会话或设置。
- 画布预览映射放 CanvasProjection，新增对象草稿放 CanvasPlacementDraft；它们的 `Center` 沿用左缘语义，提交命令时才转回文档中心，精确 BarTime 不经秒值往返。
- 新制谱器 UI 拥有响应式布局、输入和展示；场景入口只协调，不堆回业务规则或后台任务。
- 结算相关的新阶段（前奏、失败页、音效接线）落在 `client/scripts/ui/results/`：纯时序/分类放纯 C# 文件并接入 core-tests，
  Godot 节点用自带 `_Process` 的独立对象；`GameplayMain.ShowResults` 只算一次成绩并交出快照。
- 新回归按主题放独立测试类，并接入 Program。保留测试数量与调用顺序，不能用移动文件掩盖漏跑。

## 剩余对象边界

| 优先级 | 对象 | 下一步边界与验证 |
| --- | --- | --- |
| 1 | 制谱器新 UI | Avalonia Shell 已接文件、包/差分管理、多选批量命令、单选精确属性、BPM/Scroll、放置、SELECT 移动/调宽、PATH 与 PAN；预览按 load/update 分离并复用音频。下一步接真实波形、持久化、合并导出，并以长谱基准决定是否需要真正的差量舞台更新，保持 ChartEditor.Core 权威 |
| 2 | SongSelect | 视口、滚动、卡片动画/展示、详情代际、浏览和试听已独立；动态行高/过滤场景的固定步距触摸命中仍是未复现疑点，需真实输入刻画 |
| 3 | GameplayMain | 暂停、结束双锁存、结果前奏与结算页已独立，成绩快照/保存仍由场景按既有顺序拥有；不为缩短行数引入保存接口或重排音频、输入和判定（分部类预算余量以结构报告为准，新阶段应继续落在 `ui/results/` 或纯对象） |

上述拆分不授权顺带更改 JudgeEngine、v2 读写、判定窗口、时钟公式、成绩身份或客户端布局数值。
继续拆分以状态拥有者、输入输出、生命周期和回归证据为完成标准，不以一个文件缩短或新增 partial 为完成标准。

## 验证与防止回退

```powershell
# 依赖已还原时；首次运行去掉 --no-restore
python -B tools/verify.py --no-restore
# 仅查看项目依赖和规模
python -B tools/architecture/check.py --report
```

结构门禁同时限制项目引用、源码链接、单文件与整个分部类总量；策略详见
`tools/architecture/README.md`。总行数含空行、注释与内部类，属于粗粒度工程预算，不能代替职责评审。

最近执行、历史构建与设备边界统一见 [验证状态](<verification.md>)；不在本页重复累加结果。
输入缓冲回归覆盖同帧短 Hold、多指、跨轨、未知 Move 与清理；loader 回归覆盖迟到结果、单并发、失败、取消与 Dispose。
阶段行为证据在 [第二轮](<archive/reports/2026-10-25-modularization-phase2.md>)、[第三轮](<archive/reports/2026-10-25-modularization-phase3.md>) 与 [client 续拆](<archive/reports/2026-10-25-client-followup.md>)；它们不证明当前设备体验已验收。
