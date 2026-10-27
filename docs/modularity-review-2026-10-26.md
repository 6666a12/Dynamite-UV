# 2026-10-26 模块化进程复审

> 性质：对当前工作区的结构分析与建议，不是新一轮重构完成报告、缺陷复现记录或实施排期。
> 已实际执行的检查统一见 [验证记录](<verification.md>)；当前职责导航仍以 [模块地图](<module-map.md>) 为准。
> 本轮未改产品源码、项目引用、门禁或测试，未运行 C# 构建/回归、Godot、导出或设备验收。

## 1. 结论

**前几轮模块化有效，但应用层的对象职责拆分尚未完成。** 核心程序集与共享视觉层已经建立方向明确的依赖；渲染池、文档会话、输入缓冲、后台请求、校准采样等也不是把方法搬到新文件，而是有独立状态与生命周期的对象。

剩余问题分三类，不能统一按行数处理：

1. **真正需要继续解耦的复合职责**：画布的选择/命中/拖动预览，Create 的表单预校验与请求构造，转场的两套呈现及独立放电尾巴，选曲列表的触摸/滚动/动态布局。
2. **文件大，但对象本来已经分开**：配色控件集合、判定类型集合、时间与路径求值集合。拆成一类型一文件可以改善导航，但不等于新增模块边界。
3. **较大但仍有合理协调职责的宿主**：GameplayMain、校准页、设置页和编辑器 shell。没有新的状态拥有者与回归合同前，不应继续机械拆小。

**推荐方向：先补可复跑的保护，再按状态拥有者逐个提取；不重新拆一次已经独立的 loader/HUD/session，也不改玩法或格式来配合重构。**

## 2. 范围与统计口径

审查对象为当前未提交工作区，而非仅 Git HEAD。历史来源为 [第二轮](<archive/reports/2026-10-25-modularization-phase2.md>)、[第三轮](<archive/reports/2026-10-25-modularization-phase3.md>) 与 [client 续拆](<archive/reports/2026-10-25-client-followup.md>)，只用来追溯进程；现状以本轮源码读取为准。

实际运行：`python -B tools/architecture/check.py --report`。门禁覆盖八个项目的声明依赖及七个源码根中的 **219 个 C# 文件**，排除 bin/obj/.godot；不是整个工作区所有 C# 的统计。

行数含空行、注释、内部类；partial 合计按完整所属文件相加，不是 Roslyn 语义类 LOC，也不是复杂度或质量分。

### 2.1 当前主要类族

| 类族 | 文件数 | 当前合计行数 | 判断 |
| --- | ---: | ---: | --- |
| AuthoringCanvas | 6 | 2402 | 首要职责拆分目标；画布投影、选择、命中、手势和视觉仍共享状态 |
| CreatePageOverlay | 3 | 1330 | 表单/显示/输入相联，但包含已独立的嵌套 NumericField、LevelField，不能全当一个状态机 |
| GameplayMain | 7 | 1317 | 相较历史 2859→1823 已实质缩减；仍需保留固定帧序协调 |
| StandaloneEditorMain / EditorFlow | 9 | 1297 | session 和部分页面已独立；路由/对话框/菜单/预览仍由 shell 协调 |
| SongSelect | 6 | 1296 | 浏览、卡片、请求、试听已独立；列表视口交互仍集中 |
| CalibrationScreen | 3 | 1213 | 采样、音源、控件已独立，暂不因总量继续强拆 |
| NoteColorsScreen | 3 | 895 | 原剩余大类表遗漏的关注点；Draft 已独立，模态和观察展示可按需再拆 |
| SettingsScreen | 2 | 789 | 543 行是布局文件，页面总量不等于单一业务逻辑复杂度 |
| GameplayNoteRenderer | 3 | 762 | 已有内聚的视图/池生命周期；不应因达到数百行再拆一次 |

现有八个预算类族中不含 NoteColorsScreen；以上补充统计不改变现有门禁或预算。

### 2.2 物理大文件需要另看

| 源码 | 物理行数 | 不应混淆的事实 |
| --- | ---: | --- |
| [TransitionOverlay](<../client/scripts/ui/TransitionOverlay.cs#L72-L106>) | 948 | 真正集中两套呈现和多层生命周期，属于新增重点候选 |
| [NoteColorsWidgets](<../client/scripts/ui/NoteColorsWidgets.cs#L7-L930>) | 930 | 八个顶层类型及嵌套 Ring，不是单个 930 行控件 |
| [JudgeEngine](<../shared/Judge/JudgeEngine.cs#L240-L271>) | 776 | 同文件还有枚举、JudgeUnit、SustainJudgementRules、HoldContactState、JudgePlan；JudgeEngine 本体不是 776 行 |
| [V2PackageWriter](<../shared/Chart/V2/V2PackageWriter.cs#L66-L134>) | 733 | 流程编排、安全路径、发布回滚、验证及 WAV 探测相联；属可按职责改进但风险较高的第二梯队 |
| [V2JsonDecoder](<../shared/Chart/V2/V2JsonDecoder.cs#L27-L32>) | 698 | 严格格式解码器；大不等于职责失控，不为缩行引入通用插件式 parser |
| [Evaluation](<../shared/Chart/V2/Evaluation.cs#L5-L16>) | 680 | 已分 V2BpmTimeline、V2Curves、V2ScrollMap、V2PathEvaluator 等类型；文件整理收益大于再次抽象 |

结构门禁对每文件默认 1000 行，只有 [显式类族](<../tools/architecture/policy.json#L125-L165>) 做 partial 合计。NoteColorsScreen 与 TransitionOverlay 未列入类族预算，不表示有违规；但以后若只增加 partial，默认单文件检查不能防止其类总量继续增长。后续可在真实拆分时同步评审预算/依赖约束，不应本轮随意设阈值或放宽旧预算。

## 3. 对既往模块化的评价

### 3.1 已完成的是实质边界，不应推倒重来

- **程序集边界**：shared、ChartEditor.Core 不依赖 Godot；client/editor 经 ProjectReference 使用 presentation，共享视觉不再靠 editor 链接 client 源码。依据见 [项目策略](<../tools/architecture/policy.json#L167-L235>) 与 [模块地图](<module-map.md#L22-L37>)。
- **游玩渲染与规则分离**：renderer/effects 自有视图与池，HUD 收展示值，pause 经回调回到宿主；这些禁止反向获取场景/判定/会话的约束已在 [门禁](<../tools/architecture/policy.json#L16-L45>) 固定。
- **编辑器文档与预览分离**：session 拥有文档及草稿资源，projection 拥有可变预览，命令仍是文档写入口。手势和放置草稿也已有独立提交生命周期，见 [CanvasEditGesture](<../tools/chart-editor-core/CanvasEditGesture.cs#L8-L57>)、[CanvasPlacementDraft](<../tools/chart-editor-core/CanvasPlacementDraft.cs#L6-L62>)。
- **选曲与校准的纯状态已有回归入口**：Browse、CardMotion、DetailRequest、CalibrationSampling 和 PaletteDraft 已链接进 [核心测试工程](<../tools/core-tests/CoreTests.csproj#L13-L26>)，并由 [Program](<../tools/core-tests/Program.cs#L66-L80>) 实际调用。

### 3.2 还没完成的不是“文件不够短”

- partial 仍共享字段、私有方法和隐含调用顺序，不应把 Input/Judgement/Sustain 等文件名算成独立对象。
- 画布的独立手势对象目前管阈值和提交/取消，不管完整的逐帧预览算术；这一阶段边界是有价值但有限的。
- 场景与控件集成的迁移等价探针仍主要在忽略目录，不能仅凭旧哈希认为下一次重构已有持续保护。
- 历史报告列过的“下一步”必须按现状筛选，例如卡片动画、校准采样和暂停覆盖层后来已抽出，不能重复列为尚未拆分。

## 4. 优先候选与具体对象边界

下列名称是建议，不是已新增类型。优先级表示维护收益，不是已复现缺陷等级。

### 4.1 第一优先：AuthoringCanvas，拆状态与查询，不再追加 partial

**证据**：[主类 87–203](<../editor/scripts/AuthoringCanvas.cs#L87-L203>) 同时持 projection、document/controller、选择、框选、手势、草稿和舞台；[拖动更新 235–307](<../editor/scripts/AuthoringCanvas.Gestures.cs#L235-L307>) 直接修改预览 note/node 的精确时间、显示秒值及宽度位置。

建议分三小步，不一口气重写“总交互状态机”：

| 建议边界 | 输入 / 输出 | 拥有的状态与生命周期 | 画布继续负责 |
| --- | --- | --- | --- |
| CanvasSelectionState | 命中/Shift/框选候选/重建映射 → 选中快照 | 当前音符/节点/BPM 选择、框选 begin/update/end、clear/restore | Godot 输入、坐标/命中几何、菜单接线、重绘及文档命令 |
| CanvasHitTesting | 局部点、轨道、预览数据、共享几何适配 → typed hit | 无状态只读查询 | 输入门控、选择变更和交互启动 |
| 编辑预览计算（扩现有 CanvasEditGesture） | 起始目标快照、已换算指针、BPM/grid/snap → transient geometry / 一次提交请求 | 单次 begin→3px advance→update→finish/cancel | Godot 事件、视口换算、舞台显示、最终命令调用 |

必须锁住：

- [当前恢复语义](<../editor/scripts/AuthoringCanvas.cs#L110-L135>) 是 Note.Id / BPM TimeExact，不借本轮替换为 EntityKey；身份改进另开行为变更。
- [命中顺序](<../editor/scripts/AuthoringCanvas.cs#L412-L420>) 与逆序遮挡语义保持，路径节点/头/尾/身体边界不变。
- 命中、选择描边、预览必须复用 [既有几何](<../editor/scripts/AuthoringCanvas.cs#L452-L501>)；不复制一套坐标或曲线公式。
- Center 的预览左缘语义、精确 BarTime、头拖动整条路径、leading/trailing resize、节点严格时序、cancel/no-op/remembered width 保持。
- 状态对象不接收整个 AuthoringCanvas 或“万能 context”，不直接操作 Godot 节点；projection 不是新的文档写入口。

现有 [手势测试](<../tools/chart-editor-tests/CanvasEditGestureTests.cs#L19-L60>) 保护阈值、一次提交/undo、cancel、节点和头移动；**不能代替当前 UpdateGesture 全部分支的逐帧预览测试**。在现有编辑测试中未找到实际画布命中/框选/重建选择接线用例。先加选择恢复、遮挡/边缘、三轨和拖动预览分支测试，再配少量 Godot 交互快照。

### 4.2 较小且低风险的一轮：Create 表单请求组装与前置校验

**证据**：[Form](<../editor/scripts/CreatePageOverlay.cs#L29-L61>) 已是读取合同；但 [ConfirmCreate](<../editor/scripts/EditorFlow.Create.cs#L89-L130>) 与 [PreflightCreate](<../editor/scripts/EditorFlow.Create.cs#L133-L189>) 同时包含输入规则和不同创建分支的构造。

建议将现有创建模式、Form、当前包摘要组装为纯请求：

- 输入：NewPackage/AddDifficulty、表单、已打开包的只读摘要及必要的文件可用性结果。
- 输出：可读校验结果，以及 EditorProjectDraftRequest 或待加入的 EditableChart。
- overlay 继续拥有真实控件、文本解析、焦点、折叠动效；shell 继续拥有对话框、session 调用、导航和错误显示。
- 复用既有 factory/validator，不再复制第三套规则；预检提示与最终权威校验的关系写清楚，不声称两者必须完全同构。

保护 New 的 BPM/offset/audio/cover、自定义 key 的 NFC+大小写折叠、等级/unrated；AddDifficulty 仍沿用现有 150 BPM / 0 offset，不顺便增加时间线功能。已有 factory/session 回归不等于 overlay→preflight→confirm 接线保护，需补请求组装合同测试。

[NumericField/LevelField](<../editor/scripts/CreatePageOverlay.Fields.cs#L9-L29>) 已是嵌套独立 Control。把它们搬出可以改善阅读，但不能宣称这就完成表单语义解耦。

### 4.3 新增重点：TransitionOverlay 的 Track Handoff 呈现

**证据**：[72–106](<../client/scripts/ui/TransitionOverlay.cs#L72-L106>) 的三相状态、relay 控件；[337–452](<../client/scripts/ui/TransitionOverlay.cs#L337-L452>) 构建封面、front 与独立 FX；[582–679](<../client/scripts/ui/TransitionOverlay.cs#L582-L679>) 多层绘制。它确实是一类中并存 Signal Lock 和 Track Handoff，而非仅控件集合。

建议 TrackHandoffPresentation 拥有 relay UI、charge/lock/discharge、front/fx 和尾巴生命周期；外层 overlay 选择呈现并转交 coverage/profile/kind/relay、Ready/Failed/BeginReveal/Advance/Clear。**TransitionDirector 继续拥有路由、场景切换、暂停、ready timeout 和输入锁，不迁到视觉对象。**

收益是修改一套转场不会穿过另一套的状态。风险是 [coverage 归零后的放电尾巴](<../client/scripts/ui/TransitionOverlay.cs#L287-L295>) 和 [FX 不随页面 alpha 消失](<../client/scripts/ui/TransitionOverlay.cs#L443-L451>) 被错误合并。

先补 Full/Reduced/Off、锁定/失败、覆盖归零但 FX 未完、clear 重置及多次导航的行为用例；纯 timeline 只能证明时间推进，层级/alpha 仍需 Godot 节点快照。现有 ResultRevealTimeline 的测试不能证明这套转场已受保护。

### 4.4 高价值但先核行为：SongSelect 的列表视口

**证据**：[Library](<../client/scripts/ui/SongSelect.Library.cs#L12-L28>) 仍拥有触摸与惯性，页面 [123–200](<../client/scripts/ui/SongSelect.Library.cs#L123-L200>) 接管输入；[234–258](<../client/scripts/ui/SongSelect.Library.cs#L234-L258>) 同时包含固定步距命中和动态展开卡重排。

建议 SongListViewport 拥有 ScrollContainer、子项布局/矩形、触摸捕获、惯性推进及外部滚动回读；输出选择/滚动意图。宿主继续拥有 Browse、当前选择、卡片展开/收起的业务时序、加载/试听和 START 提交，避免再包出一个重复 SongSelect。

**静态风险，未运行复现**：SelectVisibleRowAt 用 `contentY/(RowHeight+RowGap)`，但展开详情卡会隐藏普通行并插入不同高度的控件；搜索也改变可见集合。应检查展开后的下方行、卡内 transport/collapse 和过滤场景是否仍正确接收触摸。不能据静态读取宣称触控已坏，也不能直接把“按真实矩形命中”的修正混入无行为变动提取。

顺序应为：先刻画现有行为并验证疑点 → 如复现，单独确定/修正行为 → 再提取视口，或先严格保持行为提取、另做修复。新增用例覆盖动态行高、过滤、滚动偏移、点击/拖动阈值、release 一次性、滚动条/EnsureControlVisible 打断惯性。Browse/CardMotion 的纯测试不覆盖这些 Godot 输入接线。

## 5. 第二梯队与不建议强拆的部分

| 对象 | 判断与触发条件 |
| --- | --- |
| NoteColorsScreen（895 行） | Draft 已独立；若继续扩配色页，优先抽模态宿主或只读可读性 presenter。页面仍唯一拥有 Draft、Apply/回滚和导航。[模态](<../client/scripts/ui/NoteColorsScreen.Dialogs.cs#L8-L39>) 涉及取消手势、ProcessMode 和焦点，不是纯绘制搬家 |
| NoteColorsWidgets（930 行） | 可以按控件拆文件提升导航；8 个顶层类型已有边界。[TechButton](<../client/scripts/ui/NoteColorsWidgets.cs#L108-L176>)、[HueBar](<../client/scripts/ui/NoteColorsWidgets.cs#L645-L653>)、[SvField](<../client/scripts/ui/NoteColorsWidgets.cs#L772-L802>) 共用 [指针拥有者](<../client/scripts/ui/NoteColorsWidgets.cs#L18-L102>)，先补匹配 device/touch id、cancel/失焦、拖动一次 undo 的交互测试，不急着做全项目通用输入框架 |
| GameSession（103 行） | 行数小也有多职责：全局初始化、曲库、已提交选择、LRU。可按需提 ChartPreloadCache，但必须先明确身份和失效策略，保留显式 commit。[当前键](<../client/scripts/game/GameSession.cs#L14-L18>) 仅 pack/chart；切模式旧 Preset 风险已在交接记录，尚未复现，不能借提取顺带改键 |
| GameSettings（426 行） | 新设置继续增长时，可分 JSON codec/迁移与引擎音频适配；运行态仍只有一个 owner。[Load/Save](<../client/scripts/game/GameSettings.cs#L127-L219>) 和 [AudioServer](<../client/scripts/game/GameSettings.cs#L221-L238>) 是不同关注点。先补旧字段迁移、缺值、配色归一化、Reset 不清库及 roundtrip；原子 writer 测试不等于格式迁移测试 |
| V2PackageWriter（733 行） | 有清晰的 staged verifier、资源路径安全、publication、WAV probe 候选；没有当前变更压力时不优先。保留 [发布后清理错误合同](<../shared/Chart/V2/V2PackageWriter.cs#L279-L306>) 和 [失败注入回归](<../tools/chart-editor-tests/PackageTransactionTests.cs#L65-L169>)，不为缩行构造通用文件系统框架 |
| JudgeEngine / Evaluation 集合文件 | 优先按现有顶层类型整理导航，不主动拆窗判断与分数状态、精确时间与 runtime 数学来凑小文件；不改变任何 API/namespace/公式 |
| GameplayMain（1317 行） | 渲染/HUD/暂停已隔离。setup、固定帧序、判定应用和结算保存协调暂留；不把 plan/engine/sustain 全塞进万能 context，不重排 [帧顺序](<../client/scripts/game/GameplayMain.cs#L222-L270>) |
| CalibrationScreen / SettingsScreen | 采样/音源/独立控件已拆，余下页面显示、动效、保存导航可接受；页面专用布局不必抽成“布局服务” |
| StandaloneEditorMain / EditorFlow | shell 仍是协调宿主。优先抽具体 Create 请求而不是再造巨大 FlowManager；不在没有真实音频功能授权时重做预览时钟 |
| GameplayNoteRenderer / GameplayEffects | 已有内聚池与视图职责；避免过度拆成大量互相回调的小对象，尤其不要引入热路径分配或改变池抢占顺序 |

## 6. 验证债务：下一轮的前置工作

这不是指前几轮没有验证，而是**阶段迁移证据与持续可复跑测试尚未完全等价**。

- [统一入口](<../tools/verify.py#L18-L25>) 包含结构、两端构建、两套 CLI 回归、发布契约测试及 diff，不启动 Godot。
- [核心 Program](<../tools/core-tests/Program.cs#L66-L80>) 与 [编辑 Program](<../tools/chart-editor-tests/Program.cs#L12-L46>) 有真实注册，不是只有未被调用的测试文件。
- 已有纯状态回归很有用，但不包含屏幕节点、完整输入派发/焦点、玩家真实音频与设备触摸；不应把“core 全通过”外推为场景行为不变。
- [第三轮记录](<archive/reports/2026-10-25-modularization-phase3.md#L44-L79>) 的 headless 快照/trace 位于忽略目录。它们是历史证据，不是新 clone 后现成可跑的测试工程；本轮没有重新运行。

建议在进入下一次有风险的对象提取前，固化一小组**版本化、clean-room、隔离设置/成绩的行为探针**，而不是将全部本机快照和数万条日志入库：

1. 画布：单选/Shift/框选、节点/头/身体命中、变 BPM 拖动、一次提交/撤销、取消重建。
2. 转场：三档动效、ready/timeout、coverage 与 discharge 的双生命周期、输入锁释放。
3. 列表：展开/收起/过滤/滚动/触摸，独立 loader 完成顺序与页面离开后的结果丢弃。
4. 选取确实需要的游玩 auto/manual × mirror、短 Hold/Mixer/暂停代表场景，保留判定/节点/HUD 的关键状态断言，不扩大为性能或像素验收。

探针应有明确运行命令、合成 fixture、失败退出码、最小期望值及无玩家副作用；真实异步 I/O 时延不要求逐帧相同，但完成顺序假设必须写明。是否接入 CI/哪条命令调用是后续实施决定，不应直接往纯核心测试工程引入 Godot。

## 7. 建议的分阶段顺序与完成标准

以下是建议顺序，不是本轮实施或承诺排期：

| 阶段 | 小范围交付 | 验证/停止条件 |
| --- | --- | --- |
| 0：保护与基线 | 记录当前工作区；为目标边界补纯测试及少量可复跑场景探针；先核静态疑点 | 测试先能在旧实现运行；不要求先建设完整 CI，不使用玩家状态或原版数据 |
| 1a：选择状态 | 只抽 CanvasSelectionState，保持当前 ID/时间恢复行为 | Shift、多选、框选、重建/删除/撤销后选择一致，菜单/HUD 不变 |
| 1b：命中与编辑预览 | 按次提取 CanvasHitTesting 和扩展现有手势的预览计算 | 三轨/边缘/遮挡/曲线路径、精确时间、no-op/cancel/一次 undo 不变，显示与命中几何同源 |
| 2：Create 小切口 | 表单请求组装及预检；若需较快独立验证，可在 1a 前单独做 | New/Add 请求及错误提示合同锁定，不新增流程、不改默认时间线 |
| 3：client 重点 | Track Handoff 独立呈现；SongListViewport 在行为疑点明确后提取，分两轮而非一起改 | 转场层级/尾巴/输入锁和列表真实输入分别有独立证据，不把疑点修复夹进结构提取 |
| 4：按变更压力继续 | 配色模态/观察、设置 codec、缓存与 writer | 有具体需求、独立 owner 和回归再做，不以“仍超过 N 行”自动触发 |

一次对象提取完成应能回答：

1. 状态只有谁拥有，何时创建、重置、取消、释放？
2. 输入输出是否明确，能否在不拿整个宿主对象的情况下使用？
3. 原调用方保留什么，是否出现第二份文档/选择/设置/缓存真相？
4. 哪些既有用例、哪些新增边界、哪些场景/设备证据支持行为保持？
5. 是否有门禁防止反向依赖/全局状态回读，并在实际缩减后收紧旧预算？

**不设置“所有文件低于 500 行”之类目标。** 冻结格式、Judge/计分/时钟、成绩身份、固定几何、存档错误语义、资源虚拟文件系统和热路径预算都不因重构而变；静态疑点与产品功能缺口分别立项，不能打包为“模块化顺手修”。

## 8. 本轮证据边界

本轮实际完成源码/历史/项目策略/回归入口的静态复审，并运行结构报告和只读规模统计；219 个门禁范围 C# 文件、八个项目、三个检查/策略文件及 Git index 的前后指纹核对一致。文档更新与检查结果见 [验证记录](<verification.md>)。

没有 C# 构建或回归运行，没有 Godot headless/可见窗口、GPU/音频/性能/触控验收，也没有导出或验包。本文出现的风险不构成已复现缺陷，所有建议尚未实施；已有用户暂缓事项保持暂缓。
确认现状或继续实施时，先按 [当前交接](<handoff.md>) 与 [验证流程](<verification.md#非图形复验命令>) 重新取基线，不用历史行数或旧测试包代替。
