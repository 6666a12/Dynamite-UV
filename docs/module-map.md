# 模块地图与拆分边界

本文按当前工程引用和场景入口导航。玩法合同见 [gameplay-spec](<gameplay-spec.md>) 与 [chart-format-v2](<chart-format-v2.md>)；历史构建、回归与设备验收分别见 [验证记录](<verification.md>)。

纯规则核心 `shared` 供游戏、编辑器和服务端使用；编辑器另有纯文档/命令核心 `chart-editor-core`。游戏和编辑器舞台共享 Godot 展示层 `presentation`，不共用应用会话；网络后端是单独的进程。
渲染/特效、文档会话、浏览、试听、校准音源、HUD、画布选择/命中/手势预览、创建请求、Track Handoff、选曲视口与设置编解码已独立成对象。
制谱器现行 UI 为 Avalonia Shell + Godot 原生舞台；旧 Penpot Editor 页面和旧 Godot 画布宿主不再作为现有模块或拆分对象。
第三轮范围与行为证据见 [第三轮模块化](<archive/reports/2026-10-25-modularization-phase3.md>)；此前的草稿保存修正见
[第二轮模块化](<archive/reports/2026-10-25-modularization-phase2.md>)。历史报告不代表旧 UI 仍存在。

## 工作区与工程关系

| 路径 | 当前用途 |
| --- | --- |
| `community/` | 产品仓库：[游戏](../client/)、[Windows 制谱器](../editor/)、共享核心和回归工具 |
| [仓内 server](../server/README.md) | 游戏连接的 ASP.NET Core / PostgreSQL 服务；账号、曲库和本人成绩的模块边界见下文 |
| [site](website.md) | 独立静态首页与制谱教程；Pages 工作流只发布站点目录，不调用游戏后端 |
| 外层 `../server/` | 历史 C++/Boost 起步骨架，不是游戏连接的后端 |
| 外层 `godot/` | 本地 Godot Mono 引擎，不是产品源码 |
| 外层 `_rev/`、XAPK、`gameplay_videos/`、`tmp_shots/` | 研究材料、录像与截图，不进入公开产品 |
| `design/`、`local-generated/`、`.tmp/`、`builds/` | 本地设计源、生成物与验证材料；不是本轮源码拆分的清理目标 |

主要编译依赖（箭头表示直接 `ProjectReference`，不表示网络调用或子进程）：

```text
client               → presentation + shared
editor（Godot 舞台） → presentation + chart-editor-core + shared
editor/shell          → chart-editor-core + shared
presentation、chart-editor-core、server → shared
core-tests、chart-tool → shared
chart-editor-tests    → chart-editor-core + shared
server-tests          → server
```

运行时，[游戏网络适配](../client/scripts/network/) 通过 HTTP 调用后端，不引用 server 程序集；[Windows Shell](../editor/README.md) 通过 HWND 嵌入 Godot 子窗口，经 JSON-lines IPC 驱动舞台，不引用游戏。测试另有源码链接：`core-tests` 编入客户端及展示层的纯 C# 类型；`server-tests` 编入客户端网络层，不需要启动 Godot。

editor 通过 `ProjectReference` 使用 `ChartEditor.Core.dll`，与编辑器回归测试使用同一核心程序集。
旧的 `Compile ../tools/chart-editor-core/*.cs` 已移除，命令可以按子目录组织而不会被 shell 漏编译。
editor 的 14 个客户端源码链接已全部移除。共享字体、几何、音符外观、舞台与基础控件进入
`presentation/DynamiteUniverse.Presentation.csproj`；纯设置文件写入进入 shared。
presentation 不引用 client/editor，不读取两端全局会话或设置；namespace 与 `res://` 资源路径保持。
两端继续各自提供字体、shader 等资源，独立程序集并不自动合并资源目录。

## 当前对象边界

| 组件 | 拥有的职责 | 调用方仍拥有的职责 |
| --- | --- | --- |
| `shared/Score/ModeScoreStore` / `ModeScoreFile` | 模式外层本机BEST、未分类历史与迁移前备份 | 调用方明确模式和保存时机；原ScoreIdentity/比较算法不变 |
| `GameplayOnlineRun` / `network/PlayUpload` / `ResultUploadControl` | 开局来源与模式快照 / 结果身份固定、一次自动发送和手动重试 / 云状态与结果内重登UI | GameplayMain保留判定/本地成绩及冻结帧顺序；服务端保存client_reported，不重算反作弊 |
| `network/CatalogTransport` / `CatalogInstaller` / `ui/store/` | 有界同源曲库传输 / 进程级首次安装与来源receipt事务 / 列表和详情展示 | `OnlineSession`提供已确认身份，`ChartCatalog`只纳入完成安装，`GameSession`/选曲以LibraryId隔离选择和缓存；shared归档安全检查不做重复媒体审核 |
| `ui/online/OnlineScreen` / `OnlineServerPicker` / `OnlineWidgets` | 账号主页面状态映射与动作接线 / 服务器地址草稿和确认浮层 / Online v2视觉控件 | `network/AccountConnection`保留请求代际和单会话权威，`AccountTransport`拥有HTTP，`game/session/OnlineSession`拥有进程会话与非秘密条目持久化；UI不拥有token或修改成绩 |
| `shared/Judge/PointerInputBuffer.cs` | 指针存活、帧首/帧末快照、原始按下时间、同帧短点、Hold 联系快照与清理 | Godot 事件读取、屏幕/镜像投影、暂停区域、判定/计分顺序 |
| `shared/Judge/BleedHealth.cs` | BLEED 血量数值表（Standard/Hardcore）、100 上限与 0 钳制、耗尽后幂等 | 判定调用点、模式选择、耗尽后的结算/导航与 UI |
| `shared/IO/LatestRequestLoader.cs` | 单并发后台加载、最新请求、取消排队、丢弃过期完成、异常传递、释放 | 曲库选择、缓存身份、播放器、封面/按钮更新 |
| `shared/Score/ScoreFilePersistence.cs` | 成绩文件加载、失败后首次保存前保留原始字节、原子写入 | ScoreStore 拥有内存成绩库、Godot 路径、延后保存与警告；不改变成绩身份和比较规则 |
| `shared/Audio/` + `shared/Chart/V2/AudioProbe*` | 托管 FLAC 解码与 WAV/MP3/FLAC/Ogg Vorbis 容器校验、时长探测和预算 | 客户端 Godot 音频流与缓存、编辑器写包事务、设备播放验收 |
| `tools/chart-editor-core/Commands/` | 现有命令按 Pack/Chart/Note/Path/Timing 与验证规则分组 | EditorDocument 的 history/dirty、shared 的格式和写包合同 |
| `tools/core-tests/` / `tools/chart-editor-tests/` | 分成独立的场景测试类，公共 fixture/断言收在各自 TestSupport | Program 只解析参数、按原顺序调用测试并返回退出码 |
| `presentation/game/GameplayNoteRenderer` / `GameplayEffects` | 两端共用的音符/连接段/残影/持续接收头与特效池生命周期；外观由 `IGameplayVisualSettings` 注入，镜像由 `GameplayRenderGeometry` 实例持有 | 游戏保留判定状态、成绩、设置和既有帧顺序；编辑器保留音频/文档/输入，按只读快照驱动视觉 |
| `EditorMarqueeSelection` | 当前轨道的谱面位置/精确时间起终点、追加基线、完整父 Note 候选集合与提交/取消；不依赖渲染对象、不写文档 | 舞台使用 `EditorStagePlacement` 反查指针时间，播放/seek 更新终点并绘制投影/数量；控制器/IPC 同步 Shell 多选，文档命令仍由 Shell/Core 拥有 |
| `EditorContextMenu` | 舞台内右键面板控件、选中反馈、边缘布局和关闭输入消费；不写文档 | 舞台判断空白/取消手势，控制器持有预览音量/效果，Shell 持有工具/放置面/类型和翻译，IPC 仅发送意图与状态 |
| `EditorAutoTimeline` / `EditorAutoPreview` | JudgePlan 上的可 seek 事件游标 / 编辑器 Auto 视觉生命周期、当前持续状态恢复、CPU/GPU 暂停 | `EditorPreviewController` 仍负责音频与 IPC；`EditorStagePreview` 保留静态编辑视图、选择、手势和舞台布局 |
| `EditorPreviewHud` | 独立Auto计分快照、seek重建、编辑态最小HUD与游戏效果预览模式；共用presentation/game/hud展示对象 | Shell传曲名/难度与模式意图；Controller持有音频和时钟，不写成绩、不新增Storyboard语义 |
| `SustainOutline` | 连接体侧边和真实控制节点端边，按运行时IsShapeSample隐藏细分横线 | 两端renderer持有几何、裁剪和池生命周期；shared保留曲线/判定权威 |
| `EditorDocumentSession` | 当前文档、草稿资源、打开/创建/新增差分/保存用例 | shell 的对话框、消息和页流 |
| `SongBrowseState` / `CanvasEditGesture` | 本地浏览状态 / 已有对象编辑手势的提交与取消 | UI 动画与预览投影、START 会话提交 |
| `presentation/game/hud/GameplayHud` / `GameplayHudHeader` | 下方 HUD 与顶部装配，读取真实快照；`GameplayHudPlate` 拥有形状/文本/输入和 `HudLightState` 包络，`GameplayBleedBar` + `BleedHudState` 拥有血量显示插值 | 场景保留判定/血量/分数权威、既有暂停/失败回调和帧顺序；展示不计算损伤或演示统计，见 [HUD v8](<gameplay-hud-v8.md>) |
| `GameplayProgressState` / `GameplayProgressBar` / `JudgmentLineTimeline` | 顶部进度比例与成就退化、移动端点绘制 / 三轨只读活跃区间与启灯采样 | 场景提供歌曲有效时间/结束口径/判定计数；公共舞台只接收亮度，镜像映射不改变判定；见 [HUD 调整](gameplay-hud-v8.md) |
| `WorkbenchUserStore` / `WorkbenchPreferenceSession` / `WorkbenchLayoutSession` | 独立用户目录的版本化快照、原子写入/损坏备份、偏好合并写入与启动前布局恢复；布局DTO不包含控件/HWND | Shell持有活控件/进程生命周期；预览音量和效果经IPC同步，测试使用隔离目录 |
| `EditorNoteClipboard` | 完整父Note及路径的会话快照、精确相对拍时间、新身份和组合插入命令 | Shell 拥有播放头/网格锚点、快捷键焦点保护与选区同步；Inspector 的批量变换另走选区编辑命令，不由剪贴板执行 |
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
| `HomeActionTile` / `HomeActions` | 单个动作底板（切角、中文/英文两行、可选箭头、禁用态与 hover/按下反馈）/ SELECT DESTINATION 区接线 | 游玩、设置可直接进入；谱面商店按登录状态启用；段位模式和联机大厅仍禁用 |
| `HomeBrandBlock` / `HomeUtilityRow` / `HomePalette` | 品牌块、顶部 LOGIN/ACCOUNT → 账号页与版本串、Home v2 颜色与字距 token | 入场登记、`Main.BuildDescription()` 构建身份串 |
| `StandaloneEditorMain` / `EditorPreviewController` / `EditorStageAuthoring` | 装配 Godot 舞台 / 预览时钟、音频与 IPC / SELECT、PLACE、PATH、PAN 的手势状态 | Avalonia Shell 的文件、文档命令、布局、i18n 与快捷键；舞台不另写时间反查公式 |
| `EditorStagePlacement` / `StagePlacementDraft` / `EditorPlacementOverlay` | 三轨转换、侧轨尺度与精确 BarTime 反查 / 中心坐标放置、宽度与尾点草稿 / 真实 NoteView ghost、导引读数和对齐标记 | 文档命令与保存仍由 ShellChartEditor/Core 拥有；旧 CanvasPlacementDraft 的左缘语义不混用 |
| `EditorSpatialMath` | 十进制空间交互算术：规范网格值、相对位移加减与范围回退；不统一舍入文档或文件 | 舞台负责坐标投影，Shell/Core 命令负责提交；自由模式、精确输入与未修改字段保留原值，shared 保持严格格式/Digest |
| `CanvasProjection` / `CanvasPlacementDraft` | 文档到可变预览数据的映射 / 新建音符宽度与 Hold 两阶段草稿 | 新 UI 的输入、坐标/吸附、选择恢复；提交仍走文档命令 |
| `CanvasSelectionState` | Note.Id 多选、主选择/实际单选、BPM 精确时间、二级 Node.Id 与框选生命周期 | 新 UI 的输入与几何、选择事件、重绘和文档命令 |
| `CanvasHitTesting` | 逆序遮挡、节点/头/尾/身体优先级与边缘 typed hit | 新 UI 的矩形、路径求值、坐标换算及 Hit 适配 |
| `CanvasGesturePreview` | 移动、宽度、节点时间/宽度与路径精确时间平移的纯预览计算 | 新 UI 的输入、屏幕几何、Projection 回写和一次命令提交 |
| `TrackHandoffPresentation` / `TrackHandoffState` | relay UI、front/fx、相位推进与独立放电尾巴 | Signal Lock、路由、ready timeout、输入锁和转场完成时机 |
| `CatalogueDrum` / `SongListViewport` / `SongListScrollState` | 菜单浅弧的绘制与逆投影、列表指针捕获与原生点击重放 / 滚动条适配 / 滚轮与触摸板惯性 | 各页面保留布局、列表数据、选择、展开/收起、搜索、详情试听及导航 |
| `ContactRim` / `ChannelStripKey` / `SongRow` | 外框包络及触点归属 / 快捷键的真实输入捕获与提交判定 / 曲行的选择视觉（隐藏时同步到终值） | 外框不修改设置或接管输入；快捷键直接转交已捕获的按下、松手及取消；选曲页仍拥有实际选择和试听 |
| `GameSettingsCodec` | 设置快照、JSON、旧字段迁移和配色恢复 | 唯一运行态、user://、原子写入、警告和 AudioServer |
| `EditorCreateRequest` / `EditorDifficultyCreation` / `EditorMediaSlots` | 新包请求 / 新差分的独立对轨快照与音频预检 / 媒体引用与写入路径分配 | `EditorDocumentSession` 只在文档与参考差分仍有效时提交；writer 保留严格发布校验 |
| `ProjectWorkspaceSession` / `WorkbenchLayoutSession` | 当前窗口从起始页进入制谱工作区，首次启动/显式重试舞台；进入项目时挂接和恢复 Dock | 文件选择、会话采纳/撤销仍属 Shell 与 EditorDocumentSession；IPC/进程由 GodotStageService 负责。没有挂接 Dock 就不写布局 |
| `WorkbenchModalHost` / `DifficultyCreationFlow` | 主窗内表单的生命周期、焦点、浮动工作台和原生输入边界 / 将差分表单接到会话 | 表单负责输入与可取消的预检；主窗口只装配宿主并在提交后刷新。其他表单可复用宿主，无须依赖差分模型 |

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
下表中游戏脚本以 [client/scripts](../client/scripts/) 为根；[UiRoutes](../client/scripts/ui/common/UiLayout.cs) 列出场景路径，脚本绑定见 [client/scenes](../client/scenes/)。结算由游玩场景创建，不是单独的场景路由。
游戏 UI 按 `home/`、`song_select/`、`results/`、`settings/`、`calibration/`、`note_colors/`、`online/`、`store/`、`transitions/`、`common/` 分组；`game/` 下分 `gameplay/`、`charts/`、`session/`、`settings/`、`common/`、`verification/`，纯 C# 网络适配放在同级 `network/`。
编辑器的 `shell/` 是 Avalonia 工作台，`scripts/stage/` 是 Godot 舞台适配；`chart-editor-core` 按 `Canvas/`、`Documents/`、`Timing/`、`Commands/` 组织。

| 入口 | 职责文件 |
| --- | --- |
| `game/GameplayMain.cs` | 启动、选择、测量常量和每帧协调；判定分部、HUD 接线、暂停与结束条件 `GameplayEndCondition` 在 `game/gameplay/`。两端共用的 HUD 展示在 `presentation/game/hud/`，音符渲染和特效在 `presentation/game/` |
| `Main.cs` | 首页场景装配与入场动画；Home v2 布局、品牌块、动作区、顶部工具行、参考环与 Mk VI 核心都在 `ui/home/`（旧 `HomeEnergyCore.cs` 已不被引用） |
| `ui/results/ResultScreen.cs` | 结算页数据展示与输入闸门；屏风在 `ResultShutter.cs`，屏风时钟在 `ResultRevealTimeline.cs`，交付顺序在 `ResultFlow.cs`，字标前奏在 `ResultOutcomePrelude.cs` / `ResultPreludeTimeline.cs`，分类在 `ResultOutcome.cs` |
| `ui/song_select/SongSelect.cs` | 会话、选中与导航；`.Library` 列表/搜索，`.Loading` 详情与试听接线，`.QuickSettings` 快捷设置，`.Motion` 卡片推进/页面进场，`.Layout` 页面布局；滚动/触摸在 `SongListViewport.cs`，惯性在 `SongListScrollState.cs`，卡片/动画/详情代际各自独立 |
| `ui/CalibrationScreen.cs` | 页面协调；其余分部、采样、音源和控件位于 `ui/calibration/` |
| `ui/SettingsScreen.cs` | 页面状态、保存与动效；字段布局与控件位于 `ui/settings/` |
| `game/settings/GameSettings.cs` | 唯一运行态、Godot 读写与音频适配；JSON/迁移在同目录 `GameSettingsCodec.cs` |
| `ui/transitions/TransitionOverlay.cs` | Signal Lock 与 Track Handoff 转发；relay/front/fx 在 `ui/transitions/TrackHandoffPresentation.cs`，纯相位在 `ui/transitions/TrackHandoffState.cs` |
| [账号页](../client/scripts/ui/online/OnlineScreen.cs) / [商店](../client/scripts/ui/store/StoreScreen.cs) | 页面接线、分页/详情与安装状态展示；会话由 `game/session/OnlineSession` 持有，传输、安装与上传实现见下节 |
| [editor/scripts/StandaloneEditorMain.cs](../editor/scripts/StandaloneEditorMain.cs) / [stage](../editor/scripts/stage/) | 原生舞台装配；预览、IPC、音频与 SELECT/PLACE/PATH/PAN 手势分别由独立对象负责 |
| `editor/shell/ShellChartEditor.cs` | 把舞台、Inspector 和 Timing 的 BPM/Scroll 请求转换为原子文档命令，窗口不堆编辑规则 |
| `WorkbenchTimingKeys` / `TimingView` / `ChartOffsetNudge` | Shell 统一路由两端的按下、重复和释放；Timing 展示当前差分的毫秒值，文档拥有偏移手势和撤销边界。舞台复用录音时间与增量更新，不自行修改差分 |
| `WorkbenchTimeline` / `TimelineRhythmData` / `TimelineTrackLayout` / `TimelineView` | 当前差分录音时长与小节换算；按轨投影真实起音、长条及判定刻痕；布局索引、可见区几何；Avalonia 绘制及冻结拖动。控件只请求导航，预览时钟仍由 Godot 持有。 |
| `StageFrameDispatch` / `WorkbenchPreviewClock` | 有序投递原生输入与播放边界，同一段待显示帧只保留最后一帧；显示舞台确认的秒数/小节，并区分暂停和主动向后跳转。两者都不启动计时器或控制音频资源。 |
| `EditorBarGuideGrid` / `EditorBarGuides` | 在已有 BPM/Scroll 映射上采样固定分度，再在编辑态按轨裁剪、分层绘制和标注。各轨显示档位由 Shell 会话保存，既不生成 Note，也不改变吸附 |
| `tools/chart-editor-core/Documents/` | 文档、会话、创建/打开/保存、Digest 与校验；`EditorGameplayDigest.cs` 为纯核心服务，`EditorPackageSettings.cs` 把包/差分设置与资源替换组成一次可撤销事务 |
| `tools/chart-editor-core/Canvas/` | 包含现行舞台用的 `EditorStagePlacement`、`StagePlacementDraft` 等纯计算，也保留旧画布的投影、命中和手势模型。当前手势是否接通以 `editor/scripts/stage/EditorStageAuthoring.cs` 与 Shell 命令入口为准 |
| `EditorCommands.cs` | `IEditorCommand` 与组合事务；具体命令位于 `Commands/`，命名空间/API 保持 |

## 网络服务边界

- **服务端：** [Hosting](../server/Hosting/ServerApplication.cs) 装配 HTTP、配置、健康检查、限流和路由，`Protocol` 定义无凭据发现合同。`Accounts` 处理注册、登录、本人查询与退出；`Catalog` 负责管理员发布/下架及登录用户查询/下载；`Records` 负责本人成绩幂等上传与分页查询。`Persistence` 拥有 PostgreSQL 连接池和事务迁移；谱包校验复用 `shared` 的 strict v2、音频与 Digest 规则，不依赖 Godot。运行入口及发布限制见 [服务端说明](../server/README.md) 和 [API 合同](../server/API.md)。
- **游戏端：** [network](../client/scripts/network/) 提供账号、曲库传输、首次安装和成绩上传；[OnlineSession](../client/scripts/game/session/OnlineSession.cs) 接入应用会话。进局时 [GameplayOnlineRun](../client/scripts/game/gameplay/GameplayOnlineRun.cs) 固定来源/账号和模式，符合条件的结算才上传。判定和按模式保存的本机 BEST 不依赖服务器；服务端支持本人记录查询，不等于商店已有记录列表 UI。
- **验证：** [server-tests](../tools/server-tests/Program.cs) 默认测客户端网络行为及回环 HTTP；真实 PostgreSQL 集成是独立专项，不能以默认回归代替。执行记录见 [verification](verification.md)，并非生产部署验收。

## 后续功能放哪里

- 判定、Hold/Mixer 规则和精确时间进入 shared，并先用现有行为用例说明边界；不要加到页面或绘制方法。v2.1 的数学表达式由 [Chart/Curves](../shared/Chart/Curves/) 编译成不可变折线；[Chart/V2](../shared/Chart/V2/) 负责谱面版本、上下文范围、运行时求值和 Digest。Shell 的 [Curves](../editor/shell/Curves/) 只管草稿、小图、错误提示和首次升级确认。回归分别检查已知折点、粗细帧接触、编辑撤销和原文件保护。
- 选曲加载策略进入 loader/加载服务；列表筛选、排序的纯状态可进一步提成独立模型。
- 编辑器数据修改通过命令进入文档；新命令放在对应 Commands 组。持久化规则继续以 shared 写包器为准。
- 两端共用的 Godot 视觉进入 presentation；通过参数传外观，不在此读取应用级会话或设置。
- 原生舞台新增交互沿用 `EditorStageAuthoring` → `EditorStagePlacement` / `StagePlacementDraft`：按中心坐标计算，手势完成时交 Shell 提交命令。旧画布 `CanvasProjection` / `CanvasPlacementDraft` 的 `Center` 表示左缘，不得混用；精确 BarTime 不经秒值往返。
- 新制谱器 UI 拥有响应式布局、输入和展示；场景入口只协调，不堆回业务规则或后台任务。
- 结算相关的新阶段（前奏、失败页、音效接线）落在 `client/scripts/ui/results/`：纯时序/分类放纯 C# 文件并接入 core-tests，
  Godot 节点用自带 `_Process` 的独立对象；`GameplayMain.ShowResults` 只算一次成绩并交出快照。
- 新回归按主题放独立测试类，并接入 Program。保留测试数量与调用顺序，不能用移动文件掩盖漏跑。

## 剩余对象边界

| 对象 | 当前边界与验证（非排期） |
| --- | --- |
| 制谱器工作台 | Shell/Core 已接文件、差分和编辑命令，舞台保留放置、SELECT、PATH、PAN 与播放预览。同差分 `update` 仍传完整 Chart 快照，不是逐 Note 差量协议。偏好与 Dock 布局已持久化，合并 Windows 包已有固定测试基线；真实波形不是当前目标。长谱性能未验收，跨保存撤销的历史媒体恢复仍有缺口。见[制谱器说明](dyna-maker-uv.md) |
| SongSelect | 视口、滚动、卡片动画/展示、详情代际、浏览和试听已独立；列表触摸已改按可见控件的实际矩形命中，涵盖展开卡占位、过滤和滚动；真实设备手势仍需专项验收 |
| GameplayMain | 暂停、结束双锁存、结果前奏与结算页已独立，成绩快照/保存仍由场景按既有顺序拥有；不为缩短行数引入保存接口或重排音频、输入和判定（分部类预算余量以结构报告为准，新阶段应继续落在 `ui/results/` 或纯对象） |

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
