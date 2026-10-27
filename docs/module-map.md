# 模块地图与拆分边界

2026-10-27 按当前项目引用和结构报告复核；已提取画布选择/命中/手势预览、创建请求、Track Handoff、选曲视口和设置编解码边界，并按功能族整理源码目录。
本文只维护当前代码导航与对象边界；玩法合同见 [gameplay-spec](<gameplay-spec.md>) 与 [chart-format-v2](<chart-format-v2.md>)，执行结果统一见 [验证记录](<verification.md>)。

目前已建立纯规则核心、纯编辑核心、共享 Godot 视觉、游戏和编辑器适配五个边界。
渲染/特效、文档会话、浏览、试听、校准音源、HUD、画布选择/命中/手势预览、创建请求、Track Handoff、选曲视口、设置编解码和部分编辑器页面已独立成对象。
应用层仍有大类，尤其是画布交互；当前属于核心层分离较好、应用层持续拆分的状态。
第三轮范围与行为证据见 [第三轮模块化](<archive/reports/2026-10-25-modularization-phase3.md>)；此前的草稿保存修正见
[第二轮模块化](<archive/reports/2026-10-25-modularization-phase2.md>)。新的 editor 产品想法暂不实施或排期。

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
editor ──► presentation ──────────────► shared
editor ──► chart-editor-core ─────────► shared
editor ───────────────────────────────► shared
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
| `shared/IO/LatestRequestLoader.cs` | 单并发后台加载、最新请求、取消排队、丢弃过期完成、异常传递、释放 | 曲库选择、缓存身份、播放器、封面/按钮更新 |
| `tools/chart-editor-core/Commands/` | 现有命令按 Pack/Chart/Note/Path/Timing 与验证规则分组 | EditorDocument 的 history/dirty、shared 的格式和写包合同 |
| 两套测试目录 | 分成独立的场景测试类，公共 fixture/断言收在各自 TestSupport | Program 只解析参数、按原顺序调用测试并返回退出码 |
| `GameplayNoteRenderer` / `GameplayEffects` | 音符/连接段/残影/持续接收头与特效池生命周期 | 场景拥有判定状态与成绩，按原顺序传入值快照和结果 |
| `EditorDocumentSession` | 当前文档、草稿资源、打开/创建/新增差分/保存用例 | shell 的对话框、消息和页流 |
| `SongBrowseState` / `CanvasEditGesture` | 本地浏览状态 / 已有对象编辑手势的提交与取消 | UI 动画与预览投影、START 会话提交 |
| `GameplayHud` | HUD 节点、文字缓存、判定闪字，读取值快照 | 分数/ACC 计算、时间、暂停和成绩保存 |
| `SongPreviewPlayer` / `CalibrationBeatSource` | 试听加载和播放生命周期 / 合成节拍、零偏移时钟、循环位置展开 | 当前选择有效性 / 校准采样状态、统计和应用设置 |
| `CalibrationSampling` | 采样相位、目标锁定、热身、note 归属、重复/stray 与完成；纯 C# | 页面读数、展示切换、建议偏移应用、保存和导航 |
| `SongCardMotion` / `SongDetailCard` | 展开/收拢/文字/封面状态机 / 卡片控件子树和展示 | 列表位置、行可见性、底部操作、加载与页面导航 |
| `SongDetailRequest` | 详情请求代际、独立 loader、作废、完成消费和释放 | 当前 pack/chart、展开状态、缓存、试听身份和页面生命周期 |
| `GameplayPauseOverlay` | 暂停状态、覆盖层、按钮和三档动效 | 输入清理、播放暂停/恢复、导航、结算与成绩保存 |
| `SongSelectWidgets` / `SettingsWidgets` / `CalibrationWidgets` | 控件输入、信号、绘制和局部动效 | 页面接线、设置保存；当前动效/落速用 provider 注入 |
| `EditorStartPage` / `EditorHudView` / `EditorSettingsPage` / `EditorContextMenu` | Start 控件和事件、HUD 展示、设置接线与原保存节奏、菜单展示 | 页流、文档会话、对话框、预览播放与菜单命令路由 |
| `CanvasProjection` / `CanvasPlacementDraft` | 文档到可变预览数据的映射 / 新建音符宽度与 Hold 两阶段草稿 | Godot 输入、坐标/吸附、选择恢复；提交仍走文档命令 |
| `CanvasSelectionState` | Note.Id 多选、主选择/实际单选、BPM 精确时间、二级 Node.Id 与框选生命周期 | Godot 输入与几何、选择事件、重绘和文档命令 |
| `CanvasHitTesting` | 逆序遮挡、节点/头/尾/身体优先级与边缘 typed hit | Godot 矩形、路径求值、坐标换算及 Hit 适配 |
| `CanvasGesturePreview` | 移动、宽度、节点时间/宽度与路径精确时间平移的纯预览计算 | Godot 输入、屏幕几何、Projection 回写和一次命令提交 |
| `TrackHandoffPresentation` / `TrackHandoffState` | relay UI、front/fx、相位推进与独立放电尾巴 | Signal Lock、路由、ready timeout、输入锁和转场完成时机 |
| `SongListViewport` / `SongListScrollState` | 列表触摸/滚轮输入、滚动读写、惯性与选择意图 | Browse、展开/收起、搜索、详情试听和页面导航 |
| `GameSettingsCodec` | 设置快照、JSON、旧字段迁移和配色恢复 | 唯一运行态、user://、原子写入、警告和 AudioServer |
| `EditorCreateRequest` / `EditorCreatePreflight` | New/Add 的纯请求组装、前置校验和 Add 的固定默认时间线 | 控件与文本解析、文件可用性查询、session、错误显示和页流 |

未落盘草稿新增难度时必须保留外部音频/封面映射；会话成功保存后才 MarkSaved。磁盘发布后的清理错误不保证回滚，不能把异常等同于“完全没写盘”。

编辑 HUD 目前只读文档和画布，设置页持有明确注入的 settings/canvas；这两者不引用 shell，
但尚不是完全独立于画布的显示模型。投影集合可修改用于预览，不得直接当作文档写入口。

`PointerInputBuffer` 不消费触点、不做镜像、不改变判定规则。`GameplayMain._Process` 的十步帧顺序保持。
同帧 press→move→release 仍有原始 press 与 Hold 时间戳，未知 Move 仍忽略；暂停/失焦由调用方清理。
快照集合复用，游玩热路径按索引读取，避免改成接口枚举后新增逐帧枚举器分配。

谱面与试听使用独立的 loader 实例，互不抢同一个并发槽。已开始的同步解码不能强行中断，
取消会使其结果失效，并取消尚未开始的工作。worker 不再对 SongSelect 调用 CallDeferred；
页面在 `_Process` 消费结果，仍检查 generation、pack/chart 与展开状态；视觉更新可相差一个渲染帧。
这部分需要后续真机快速切曲/收起/离页验证，自动回归不代表触控或试听体验已验收。

## 文件职责导航

完整类行数（含所有分部文件、内部类、空行和注释；用于限制重新增长，不是模块化评分）：

| 类 | 第三轮前 | 当前 |
| --- | ---: | ---: |
| GameplayMain | 1823 | 1317 |
| SongSelect | 2137 | 1122 |
| CalibrationScreen | 1835 | 1213 |
| StandaloneEditorMain（含 EditorFlow） | 2566 | 1230 |
| AuthoringCanvas | 2495 | 2359 |

其他当前总量：SettingsScreen 789、CreatePageOverlay 1330、GameplayNoteRenderer 762；TransitionOverlay 239、TrackHandoffPresentation 583、TrackHandoffState 72。以上数值于 2026-10-27 结构报告复核。
后续代码规模以 `tools/architecture/check.py --report` 的实际输出为准。

对既往进程、剩余大文件与后续优先级的分析见 [2026-10-26 模块化复审](<modularity-review-2026-10-26.md>)：补充了 TransitionOverlay、NoteColorsScreen 等观察对象，并区分职责提取、文件整理与合理保留的协调宿主。该文是建议，不是已实施拆分或新排期。

Godot 场景绑定继续使用原入口文件及类名。以下分部文件用于局部整理，仍属于同一个类，
不能当作已完成对象级隔离；字段随所属职责移动，也不新增“万能上下文”对象。
物理目录按功能族组织：client UI 分为 `song_select/`、`transitions/`、`calibration/`、`settings/`、`note_colors/`、`results/`、`home/`、`common/`；
client game 分为 `gameplay/`、`charts/`、`session/`、`settings/`、`common/`、`verification/`；editor 分为 `authoring/`、`flow/`、`pages/`、`common/`。
chart-editor-core 分为 `Canvas/`、`Documents/`、`Timing/` 与既有 `Commands/`；两套测试也按功能分组。场景/程序入口可留根，移动不改变 namespace 或类名。

| 入口 | 职责文件 |
| --- | --- |
| `game/GameplayMain.cs` | 启动、选择、测量常量和每帧协调；其余分部、渲染/HUD/暂停/特效位于 `game/gameplay/` |
| `ui/song_select/SongSelect.cs` | 会话、选中与导航；`.Library` 列表/搜索，`.Loading` 详情与试听接线，`.QuickSettings` 快捷设置，`.Motion` 卡片推进/页面进场，`.Layout` 页面布局；滚动/触摸在 `SongListViewport.cs`，惯性在 `SongListScrollState.cs`，卡片/动画/详情代际各自独立 |
| `ui/CalibrationScreen.cs` | 页面协调；其余分部、采样、音源和控件位于 `ui/calibration/` |
| `ui/SettingsScreen.cs` | 页面状态、保存与动效；字段布局与控件位于 `ui/settings/` |
| `game/settings/GameSettings.cs` | 唯一运行态、Godot 读写与音频适配；JSON/迁移在同目录 `GameSettingsCodec.cs` |
| `ui/transitions/TransitionOverlay.cs` | Signal Lock 与 Track Handoff 转发；relay/front/fx 在 `ui/transitions/TrackHandoffPresentation.cs`，纯相位在 `ui/transitions/TrackHandoffState.cs` |
| `StandaloneEditorMain.cs` | 编辑器启动、输入、预览和菜单命令路由；`EditorContextMenu` 负责菜单展示 |
| `flow/EditorFlow.cs` | 页流与对话框协调；`.Start/.Settings/.Hud` 接入独立组件，`.Pack/.Create` 负责页接线，创建请求/预检位于 chart-editor-core；`.Digest` 哈希状态，`.Persistence` 会话保存结果 |
| `authoring/AuthoringCanvas.cs` | 文档投影接线、画布状态和坐标换算；`.Gestures` 手势，`.Drawing` 舞台/网格；Path/Placement/Selection 按专题维护，投影、放置草稿、选择、命中与手势预览计算位于 chart-editor-core |
| `pages/CreatePageOverlay.cs` | 表单与交互；`.Drawing` 自绘，`.Fields` 数值输入与等级控件 |
| `EditorCommands.cs` | `IEditorCommand` 与组合事务；具体命令位于 `Commands/`，命名空间/API 保持 |

## 后续功能放哪里

- 判定、Hold/Mixer 规则和精确时间进入 shared，并先用现有行为用例说明边界；不要加到页面或绘制方法。
- 选曲加载策略进入 loader/加载服务；列表筛选、排序的纯状态可进一步提成独立模型。
- 编辑器数据修改通过命令进入文档；新命令放在对应 Commands 组。持久化规则继续以 shared 写包器为准。
- 两端共用的 Godot 视觉进入 presentation；通过参数传外观，不在此读取 GameSession/EditorSettings。
- 画布预览映射放 CanvasProjection，新增对象草稿放 CanvasPlacementDraft；它们的 `Center` 沿用旧画布的左缘语义，提交命令时才转回文档中心，精确 BarTime 不经秒值往返。
- 布局值留在 Layout/视觉规格；场景入口只协调，不逐步堆回控件构建或后台任务。
- 新回归按主题放独立测试类，并接入 Program。保留测试数量与调用顺序，不能用移动文件掩盖漏跑。

## 剩余对象边界

| 优先级 | 对象 | 下一步边界与验证 |
| --- | --- | --- |
| 1 | AuthoringCanvas | 绘制与路径交互仍共享较多状态；选择、命中、投影/手势/放置草稿与预览计算已有独立边界，后续按变更压力处理绘制/路径 |
| 2 | CreatePageOverlay | 文本解析、输入焦点和绘制仍在同一类；请求组装/预检已独立，不引入新的 editor 产品流程 |
| 3 | SongSelect | 视口、滚动、卡片动画/展示、详情代际、浏览和试听已独立；动态行高/过滤场景的固定步距触摸命中仍是未复现疑点，需真实输入刻画 |
| 4 | GameplayMain | 暂停已独立，结算与成绩保存仍由场景按既有顺序拥有；不为缩短行数引入保存接口或重排音频、输入和判定 |

上述拆分不授权顺带更改 JudgeEngine、v2 读写、判定窗口、时钟公式、成绩身份或布局数值。
AuthoringCanvas 总体仍超过 2000 行；不宣称所有上帝类已消除。继续拆分以状态拥有者、输入输出、
生命周期和回归证据为完成标准，不以一个文件缩短或新增 partial 为完成标准。

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
