# DynaMaker UV 当前说明

> 当前工作区核对日期：**2026-10-26，仅文档与源码静态核对**。本文是制谱器详细能力与使用边界的唯一入口；
> [编辑器 README](<../editor/README.md>)只保留启动速览，[交接](<handoff.md>)保留项目级摘要。
> 本页编辑器能力核对没有执行构建、C# 回归、启动 Godot 或实机验证，也不把既有产物/历史通过记录当作当前工作区验证。
> 文内 2026-10-08 等日期仍指当时的 UI 决策；验证记录与复验流程见[验证状态与操作](<verification.md>)，
> 旧实现说明与阶段原文见[历史归档索引](<archive/README.md>)，不作为当前能力承诺。

## 1. 项目与启动

DynaMaker UV 是独立的 Windows 桌面制谱器，使用 **Godot 4.7.1 Mono/.NET + C#，目标框架 net9.0**。
[编辑器项目](<../editor/DynaMakerUv.Editor.csproj>)与[游戏项目](<../client/DynamiteUniverse.csproj>)
拥有各自的 Godot 项目根、入口场景、程序集和导出 preset，运行时不互相启动。
编辑器项目直接引用 [shared](<../shared/DynamiteUniverse.Shared.csproj>)、
[presentation](<../presentation/DynamiteUniverse.Presentation.csproj>) 和
[ChartEditor.Core](<../tools/chart-editor-core/ChartEditor.Core.csproj>) 三个程序集；**没有 client 项目引用或 client 源码链接**。
游戏主菜单的“谱面工坊”仍为未来服务器谱面库预留，不是本地制谱器入口。

| 当前职责 | 源码入口 |
| --- | --- |
| Godot shell：输入/视觉预览时钟、页面流与目录对话框 | [StandaloneEditorMain](<../editor/scripts/StandaloneEditorMain.cs>)、[EditorFlow](<../editor/scripts/flow/EditorFlow.cs>)及同名分部文件；Start/Settings/HUD 等视图已独立为对象 |
| 文档会话：打开、新建、加差分、保存及首次保存的外部资源所有权 | [EditorDocumentSession](<../tools/chart-editor-core/Documents/EditorDocumentSession.cs>)；页面/对话框/错误展示仍由 editor 负责 |
| 权威文档、命令、精确时间、投影与编辑手势 | [EditorDocument](<../tools/chart-editor-core/Documents/EditorDocument.cs>)、[CanvasAuthoringController](<../tools/chart-editor-core/Canvas/CanvasAuthoringController.cs>)、[CanvasProjection](<../tools/chart-editor-core/Canvas/CanvasProjection.cs>)、[CanvasEditGesture](<../tools/chart-editor-core/Canvas/CanvasEditGesture.cs>)、[CanvasPlacementDraft](<../tools/chart-editor-core/Canvas/CanvasPlacementDraft.cs>)；均属于不依赖 Godot 的 ChartEditor.Core |
| 两端公共 Godot 视觉 | [presentation](<../presentation/>)：舞台、Note 视觉与公共 UI；不读取 client/editor 的全局设置或会话 |
| 格式、时间/路径求值、判定与安全写包语义 | [shared](<../shared/>)：纯 C# 核心，不依赖 Godot；底层支持不等于编辑器 UI 已接入 |

完整模块边界与仍保留的耦合见[模块地图](<module-map.md>)；不要恢复已移除的 client 源码链接。

以下 PowerShell 示例从 **community 根目录**执行。使用 Mono/.NET 版 Godot，标准版不支持 C#；
启动前必须先结束残留 Godot 进程。以下是操作示例，本次整理没有执行这些命令。

```powershell
# 首次启动前构建 C# 程序集
dotnet build editor/DynaMakerUv.Editor.csproj

# 启动前清理旧 Godot；无匹配进程时 taskkill 会提示未找到
 taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
 taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe

# 从独立项目启动制谱器
 & "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path editor
```

启动后在 **Start** 页点击 **OPEN PACKAGE** 大卡选择包含包元数据的本地目录（大卡本身即 BROWSE 动作），
选中的目录只回填到卡下的路径行，再按 **OPEN**（或回车）才真正解析并打开；也可以直接在路径行粘贴目录路径。
选择的是包目录，不是单个谱面文件；也可把目录拖到 Start 页，只回填路径，仍需 OPEN/回车确认。
Start 页不接收单个音频/封面文件，当前打开流程以 UI 为入口，不再推荐旧 `--editor-package` / `--editor-workbench` 参数。

## 2. 当前页面与包流程

[页面流程代码](<../editor/scripts/flow/EditorFlow.cs>)当前是 **Start / Pack / Create / Edit / Settings** 五页：

| 页面 | 当前行为 |
| --- | --- |
| Start | 设计稿版起始页：两行 `DESIGN · CHART · SHIP` 水印 + 椭圆轨道装饰（静态帧）+ `DYNAMITE UNIVERSE` / `DYNAMAKER UV` 居中标题组；两张 400×150 大卡——OPEN PACKAGE（= BROWSE 选目录）、NEW PROJECT（进 Create）——下方一条 slim 路径行（LineEdit + OPEN），右下角一枚低调 `SETTINGS` 文字键（设计板没有这个入口，按 §4.7「Start 页进入」补的）。 |
| Pack | 设计稿版包页（Pack Select）：左列 `LOCAL PACKAGES` + 当前已打开包一行（选中态）+ `IMPORT PACKAGE` 虚线行（换开别的包）；右侧 220×220 封面（真实封面图 / NO COVER 占位）+ 包名 + 包内难度 chips + DIGEST 盾标；下方 1184×60 差分行列表（点行选中、hover 提亮行底，超高滚动 + 底部渐隐 + 4px 细滚动条）；`NEW CHART` 进 Create（AddDifficulty）、`OPEN IN EDITOR` 进 Edit、`‹ BACK` 回 Start、`STORYBOARD` 禁用占位。 |
| Create | 设计稿版新建页（Create Project）：AUDIO（真实音频校验态 + 55% 试听占位 + 路径行 + BROWSE）、METADATA（TITLE/ARTIST/PACK ID/CHARTER）、TIMING（BPM/offset 大数字可直接输入 + 32 格 tick）、FIRST CHART（CHART ID/CHARTER + 七枚难度 chips + CUSTOM 的 DIFF KEY 与约束提示 + `‹ ›` 步进与十进制块编码的等级 + UNRATED）、可选 COVER、MEMORY DRAFT 真实状态行；中缝为实时校验状态，CREATE PACKAGE 在表单不过时压暗禁用。新包要求一个可访问的外部音频文件，创建结果仍是内存草稿；添加差分只更新内存文档，且包元数据/音频/封面为只读、TIMING 隐藏。 |
| Edit | 固定 1920×1080 裸舞台 + 四角纯文字 HUD（右上 `GRID 1/n · SNAP x · AUTO`、左下曲名 + 难度色名 + `· LV`、右下游标时间 + `BAR n · bpm BPM`）；画布叠三档网格、小节号、右缘小节标尺与底部进度细线；单选 Note 时出浮动 chip，右键出设计稿版上下文菜单。返回 Pack 与保存状态压成左下角曲名行上方的一行（近右缘对齐），**没有**常驻的快捷键速览/工具态/选中摘要调试文字。 |
| Settings | 设计稿版设置页（Editor — Settings）：左导航 GENERAL / EDITOR / AUDIO，右侧切角面板按组列出设置行；GENERAL **四行**（MOTION 三段器、**UI SCALE 四段器**——板面没有这一行，2026-10-08 随 UI 缩放新增、EFFECTS 两段器、AUTO SAVE 开关 + 秒步进），EDITOR **五行**（DEFAULT GRID 三段器、SNAP 步进器、GRID OPACITY 滑条、BACKGROUND DIM 滑条、**SCROLL INERTIA 滑条**——板面没有这一行，2026-10-08 随滚动惯性新增），AUDIO 三行（PREVIEW VOLUME 滑条、METRONOME 开关、HITSOUND 开关）；底部 `RESET DEFAULTS` 与 `BACK`。两个入口进同一页（Start 页右下 SETTINGS 文字键、Edit 右键菜单的 `SETTINGS` 行），BACK 各回其处。**只画当前选中的那一个组**（左导航分页），这是 2026-10-08 拍板的页形态，不铺成长面板；同日进一步明确**三组各自是从同一组顶（y=240）起排的独立面板**，面板高度随组行数，不是一块长板按组累进后切窗口。 |

**strict v2 本地目录包打开已经真实接入**：路径行的 **OPEN** / 回车调用
[EditorPackageRepository.Open](<../tools/chart-editor-core/Documents/PackageRepository.cs>)，进行目录与普通资源文件的安全检查、
包/谱面严格解码和跨文件语义校验；失败不进入 Pack 页。这不等于 legacy XML/DY 或 ZIP 导入，
当前打开流程也没有音频解码、时长探测或试听能力。格式合同见[Chart Format v2](<chart-format-v2.md>)。
Pack 页的 `IMPORT PACKAGE` 行是同一套打开流程（选完目录直接解析），失败只回报并保留当前已打开的包。

**Pack 页（设计稿 Pack Select）已按稿落地**：[PackPageOverlay](<../editor/scripts/pages/PackPageOverlay.cs>) 整页自绘
（切角、虚线描边、密度条、细滚动条都是控件树表达不了的自绘形状），元素坐标与设计稿 1:1。
左列只有当前这一个已打开的包（单包会话，不伪造多包列表）；封面取包内 `cover` 引用，经
`V2PackageWriter.ResolveRegularFile` 的严格包内解析后解码，无引用或解码失败时回落到 presentation 的公共
[CoverPlaceholder](<../presentation/ui/CoverPlaceholder.cs>) 的 NO COVER 占位。
难度 chips 只画包内真实存在的难度（custom 用虚线 chip），排序按[UI 设计施工约定](<editor-ui-design.md#4-编辑器交互目标不是当前能力清单>) §4.4：casual→normal→hard→mega→giga→tech 垫底、
custom 按添加顺序；一行放不下就按行高 44 换行（右缘不超过 x=1888，设计板只给了单行 6 枚，包内可以有更多自定义难度）。
差分行点选只改变选中态、hover 只提亮行底（+8% 亮度，全局 hover 语言），`OPEN IN EDITOR` 才进入 Edit；等级取真实 level/unrated，
note 数取 shared 的主判定数（`V2SemanticValidator.CountMainJudgements`，与客户端计数同源），
密度条由该差分真实的 note 起始时间分布分 72 桶生成。

**Pack 页底部状态行不含时长**：当前没有音频时长探测，设计稿的 `02:34` 段省略，这一行只写
`难度 等级 · N NOTES`（unrated 写 `UNRATED`），保存/打开回报复用同一条。
`DIGEST OK` 不是读包里的存档 digest（v2 不把 digest 写进包元数据，writer 只计算并返回 digest），
而是用当前编辑状态的谱面快照 + 音频 SHA-256 真算 gameplay-v1 digest，与保存时 writer 同一算法。
[EditorFlow.Digest](<../editor/scripts/flow/EditorFlow.Digest.cs>)在后台读取音频字节算哈希，按路径 + 最后写入时间 + 长度缓存；
换包/保存会失效，回调会检查期望路径与元数据。首次哈希未就绪、音频缺失或谱面当前校验不过
（例如还没有主判定）时显示压暗的 `DIGEST —`，就绪后刷新盾标；这不是音频解码、完整保存校验或实机验收。
底部 `STORYBOARD` 仍是已拍板的禁用占位（只有描边、整体 55% 压暗、不响应点击）。

**Create 页（设计稿 Create Project）已按稿落地**：[CreatePageOverlay](<../editor/scripts/pages/CreatePageOverlay.cs>)
整页自绘（面板切角、字段框、难度 chips、封面虚线区与底部操作条都是控件树表达不了的自绘形状），
文本输入仍是真实的 LineEdit 子节点，坐标与设计稿 1:1。AUDIO 面板右上写**真实**音频校验态
（文件存在 / 扩展名受 draft factory 支持 / 落盘探测只吃 WAV），编辑器没有时长与采样率探测，
所以设计稿的 `✓ 02:34 · 44.1KHZ` 与波形进度线不画，试听钮按占位惯例只有描边并压到 55%。
TIMING 的 BPM 与 offset 是可直接输入的 34px 大数字（默认 150 / 0），配设计稿的 32 格 tick 条，
提交时校验：非有限数与 BPM ≤ 0 一律拒绝（短暂红框 + 回退到上一个合法值），合法值才进
`EditorProjectDraftFactory` 的 `initialBpm` / `audioOffsetSec`。COVER 是可选外部封面（PNG/JPG 文件对话框选入、
✕ 清空），经 draft 的 `ExternalCoverSource` 复制进包。表单的实时前置校验写在 shell 的 `PreflightCreate`，
不过时中缝写可读原因并转红、CREATE PACKAGE 压到 55% 禁用；真正的创建仍由 `ConfirmCreate` 与 core 决定
（chart ID 形状、CUSTOM key 必填/≤24/包内唯一，以及「有等级 / unrated 二选一」等约束与 v2 validator 同一口径，
只是提前报出来）。新包时 METADATA 与 FIRST CHART 的 CHARTER 是同一个差分级值，两框可编辑且互为镜像，
不是另有包级 charter 字段。音频路径区/BROWSE 区与封面区支持拖入文件（多文件取首个受支持扩展名）；
AddDifficulty 仍拒收资源替换，Pack / Edit / Settings 页不受理拖入。拖入成功不等于完成解码或落盘。
AddDifficulty 模式下包元数据、音频与封面属于已打开的包，改为**只读展示**（值取真实包数据，颜色转 dim、
不吃焦点），TIMING 面板隐藏：新差分的初始 BPM 与 offset 本轮仍是 `ConfirmCreate` 里的既有默认 150 / 0，
不新增参数入口。
非 CUSTOM 难度会收起 DIFF KEY 行：FIRST CHART 面板从 340px 缩到 276px，LEVEL、COVER、MEMORY DRAFT
一起上移 64px；页内切 chips 播 0.18s CubicOut，进页/预填直接落终值，MOTION=OFF 也直接落终值。
Start 路径框与 Create 输入框另有约 0.12s 焦点描边反馈；页面可点热点的 hover 颜色过渡走
[EditorMotion](<../editor/scripts/common/EditorMotion.cs>)（约 0.10s），不是把已有放置手势改成插值跟随。

**等级输入（设计稿 §4 第 5 条 + Widget — Level States）**：`‹ ›` 步进钳制在 1..99（99 时 `›` 与描边一起
压暗禁用）；数字是 LineEdit、**永远是权威值**——输入 0 / -1 / 100 或非数字时被拒（短暂红框后回到上一个
合法值）；下方是十进制块编码：十位 26×34 切角大块、个位 12×18 半高小块（设计稿 `lvl_blocks` 的圆角 2，
等距 18 不分组），0 位不出块；
勾选 `UNRATED` 后数字禁用显示 `--`、不出块、`level` 写 null。

**Settings 页已按稿落地**（[SettingsPageOverlay](<../editor/scripts/pages/SettingsPageOverlay.cs>)整页自绘，行与控件几何取设计稿
`settings_board()`）：左导航三组切换、行分隔线与切角面板都是自绘形状。
[EditorSettingsPage](<../editor/scripts/pages/EditorSettingsPage.cs>)把页面事件接到
[EditorSettings](<../editor/scripts/common/EditorSettings.cs>)（内存单例 + `user://editor-settings.json`，启动加载）。
**真接点共七项**，其中 SCROLL INERTIA 与 UI SCALE 是板面没有的新增行；离散档位立即落盘，滑条拖动实时改画面、松手才写盘：

| 项 | 状态 | 接点 |
| --- | --- | --- |
| GENERAL / MOTION（FULL / REDUCED / OFF） | 真实（部分） | OFF 跳过页面/菜单淡入、已接入的约 0.10s hover 颜色过渡与 Create 的 DIFF KEY 折叠过渡，并令滚动惯性为 0；Start 路径框、Create 数值/等级焦点反馈也有 OFF 分支。REDUCED 与 FULL 当前没有独立档位差异，不代表完整动效系统已实现。普通 Create 文本框的失焦 glow 仍有约 0.12s 回落，见下方静态疑点。 |
| GENERAL / UI SCALE（90% / 100% / 110% / 125%） | 真实（板面没有这一行） | 编辑器正文字号倍率：`EditorFonts.Pt()` 在固定档位修正后再乘该档，默认 100% = 只有固定修正。换档立即生效（控件走字号注册表重放、自绘页面重绘），不重启。机制见 [UI 设计稿与施工约定](<editor-ui-design.md#4-编辑器交互目标不是当前能力清单>) §4 第 15 条。 |
| EDITOR / DEFAULT GRID（1/8 · 1/16 · 1/32） | 真实 | 新草稿的 grid 参数（`ConfirmCreate`）与当前文档 `GridDivisor`（`AuthoringCanvas.ApplyDefaultGridDivisor`，打开文档、改设置、RESET 时都走这里）。默认 1/16 = 编辑器既有默认，`EditorDocument.DefaultGridDivisor` 仍是 core 的常量兜底。 |
| EDITOR / SNAP（0.05 · 0.10 · 0.25） | 真实 | `AuthoringCanvas.SnapSpatial` 的空间吸附步长（放置锚点、拖动、宽度边），HUD 的 `SNAP` 读数同源；按住 `X` 的 0.01 细步不受影响。右键菜单的 CENTER/WIDTH 步进仍是 `tools/chart-editor-core` 的常量，是另一套。 |
| EDITOR / GRID OPACITY | 真实 | 三档网格（小节/拍/细分）的线芯与软光一起乘 `GridOpacityScale`（`DrawTieredLine`），100% = 设计稿浓度。 |
| EDITOR / BACKGROUND DIM | 真实 | presentation 的 [GameplayBackdrop](<../presentation/game/GameplayBackdrop.cs>)没有压暗参数，编辑器按住它的 `Modulate` 做 RGB 压暗（alpha 不变，背景仍不透明），范围 0–80%。 |
| EDITOR / SCROLL INERTIA | 真实（板面没有这一行） | 画布滚轮与 Pack 页列表滚动的惯性强度：0–100 线性映射到 τ 0–0.28s，0% 即瞬时（与加惯性前一致），默认 40% = 0.112s。MOTION=OFF 时强制为 0。模型与旁路规则见 [UI 设计稿与施工约定](<editor-ui-design.md#4-编辑器交互目标不是当前能力清单>) §4 第 14 条。 |

EFFECTS、AUTO SAVE 与 AUDIO 三行组没有对应实现（无粒子、无自动保存、无音频链路），
按设计稿示例值画 55% 占位且不响应交互，值也不写进设置模型。`RESET DEFAULTS` 是真实动作：
回默认值、落盘，并把当前文档的网格与画面接点一起拉回默认。默认值相对板面的偏差见
[UI 设计稿与施工约定](<editor-ui-design.md#4-编辑器交互目标不是当前能力清单>) §4.7。

**新建与编辑都只留在内存，只有显式保存才写盘。** Create 的 grid 取 Settings / EDITOR / DEFAULT GRID
（默认 1/16，与编辑器既有默认一致），初始谱面不自动添加 Note；进入 Edit 后画布网格跟随同一份设置，
但设置页不是完整的属性 UI，PACK 元数据等其它创建参数仍不回读。
SAVE（`Ctrl+S` / 右键 SAVE）与 SAVE AS 已接通：未落盘草稿走 `V2PackageWriter.Create`，已打开包走
`V2PackageWriter.Write`（同目录保存或另存到新目录），成功后 `MarkSaved` 并刷新 dirty 状态；失败保留内存文档并显示错误。
ZIP/XML/DY 导出仍未接通。

当前没有可用的 **Storyboard 页面**，也没有可交互的数值 Inspector；
保存只有右键菜单与快捷键入口，界面上没有独立保存按钮。
Edit 主态不再有常驻的选中摘要文字行（2026-10-08 移除，连同快捷键速览与工具态那两行调试文字）；
选中的唯一读数位是浮动 chip 与右键菜单，它们都不代表属性编辑面板已经实现。
保存/错误回报的位：Edit 页是左下角的保存状态行（`SAVED · <目录>` / `UNSAVED CHANGES` / 失败原因），
其余页面复用各页自己的消息行（Start / Pack 的中缝状态行、Create 的中缝）。
在 Edit 页，右键菜单的 TIME/CENTER/WIDTH 步进器是当前选中 Note/节点的数值步进入口，
不是完整的属性面板（Create 页的初始 BPM/offset/等级输入是另一处表单）。

## 3. 画布与编辑范围

[AuthoringCanvas](<../editor/scripts/authoring/AuthoringCanvas.cs>)采用固定 1920×1080 坐标，
主场景中的 `ChartCanvas/SharedGameplayStage` 负责 clean-room 舞台视觉；画布输入叠加在视觉层上，
不是游戏完整判定运行时。

### 已接入

- Center / Left / Right 三个音符编辑侧，以及 Events 事件侧。
- **Normal → Tap、Chain → Drag、Hold、Select** 四种画布工具；Hold 为先定头部/宽度、再定尾部的两阶段放置。
- 对象选择、Shift 多选、框选、移动、宽度调整、已有路径节点的时间/位置/宽度编辑，以及删除和取消手势。
- **真实路径 body**：Hold/Mixer 在舞台上的投影经 [EditorPathProjection](<../tools/chart-editor-core/Canvas/EditorPathProjection.cs>)
  调 shared `V2PathEvaluator` 逐段求值，按曲线类型自适应细分；弦高误差的目标容差为 0.5px，
  编辑器每段上限 64 段，达到上限不代表任意路径都满足此容差（runtime 上限为 256）。
  与 `V2RuntimeAdapter` 共用路径数学/像素尺度，视觉由 presentation 的公共舞台渲染器提供，
  但不据此宣称所有曲线逐像素一致或已做实机目测。此前只连头与最终尾导致的多节点 Hold 被拉直、Mixer 无 body 已修正。
- **路径编辑层**（设计稿 `PATH_FIELD`）：Select 工具下单选一条 Hold/Mixer 时，编辑态在顶中 (960,36) 显示
  `PATH EDIT`（13px bold cyan，字距 3），路径各控制点画菱形手柄
  （头 24px 实心 base 色 + 「类型浅色顶边」描边（Hold = `#FFF2D1`）；中间/尾 18px panel 底 + 「类型浅色边」
  描边（Hold = `#FFCC59`）；两档都从 `EditorOverlayDraw.Surface(类型)` 取，不写死某个类型的色值；
  选中节点 cyan 实心菱形 + 32×32 切角 8 的 cyan 70% 环），指针悬停时由 60% 提到 100%，
  并有过节点中心的 1.5px `#FFE0A0` 55% 虚线 spine（dasharray 7 6）。选中 Mixer 时另沿 body 按 shared
  `V2MixerTicks` 的 1/8 小节判定网格画 18×2 `#FF9CC4` 75% 短横 tick（位置取路径求值器在各 tick 时刻的 center，
  这就是它的判定网格可视化）。[动效规格](<ui-motion.md>) §9.1 的「沿路径从头向尾依次点亮」stagger 仍是动效目标，
  当前为静态全部显示。
- **双击路径插入 node**（设计稿 §4 第 2 条）：在路径身体上双击，以按当前网格/吸附规则得到的时刻插入控制节点，center/width
  采样 shared 路径求值结果（仅保证插入时刻几何逐位相同、不在该点跳变），出向曲线继承区间左端的曲线种类；
  插入后新节点进入第二级选中（cyan 环 + 节点态右键菜单），第二击不松手直接进宽度拖拽
  （以采样宽度为基准的相对拖拽，1:1 跟手、0.05 步进），拖拽中显示 `PATH_FIELD` 的插入幽灵：
  过幽灵中心位置轴的时间导引线（2px 白 35%、贯穿该轨可放区间）+ 16px 深底 cyan 菱形 + 170px 双向宽度箭头
  + `时刻 · x 中心` / `w 宽度` 两行读数（读数画在压在 Note 之上的前景层，幽灵本体留在画布层）。双击空白处、
  头部或手柄都不插入；菜单 `ADD NODE` 行是同一入口的菜单形式（在游标时刻插入，游标不在路径内部时禁用）。
- **放置手势视觉**（设计板 `Editor — Placing`，设计稿 §4 第 9、10 条，
  [AuthoringCanvasPlacement](<../editor/scripts/authoring/AuthoringCanvasPlacement.cs>)）：
  - 导引十字线：过幽灵时间的 pink 2px 全宽时间线（两端各 200px 渐隐）与过幽灵位置、从屏幕/可放置端
    到判定线的 3px 白 60% 位置线；画在画布自己的 `_Draw`，也就是 presentation 舞台 Note **之下**（设计板
    要求导引压在 Note 下，幽灵与已放对象保持可读）。
  - 线上三读数（20px bold，各带 1px 偏移的深色底衬）：位置线一侧是位置（类型色）、另一侧是宽度（白），
    时间线旁是分数式时间（text 色）。三个数都是**吸附后的真实落点**（位置与宽度取草稿提交用的那两个值，
    贴轨边被钳制时位置取实际中心；时间取吸附后的 BarTime），吸附态写分数式 BarTime、按住 `Z` 或 SNAP OFF
    的自由态写原始小数小节。
  - 幽灵 Note 补切角虚线描边（dasharray 9 6、2px、75% 不透明，切角与 NoteView 着色器同源）；
    实色渐变填充仍由 presentation 渲染器的 ghost 层给出。
  - 阶段 1 追加宽度拖拽指示：从幽灵中心到光标的 cyan 双向箭头（沿宽度轴、两端箭头向外、中段一根刻度）
    与光标十字（28px 交叉线 55% + 16×16 切角 4 方块：30% 填充 + 实描边）。
  - 阶段标签 13px dim（字距 1.5）贴在对应幽灵下方：`PHASE 0 · PLACE` / `PHASE 1 · WIDTH`。
  - Hold 拖尾阶段：头保持**已放置实体外观**（不再标成幽灵、body 不画），拖尾改为沿时间轴的虚线路径
    预览（1.5px、dash 7 6、60%）+ 尾端 20px 虚线菱形幽灵（panel 底 + 类型色 2px 描边、85%），
    标签 `HOLD · TAIL DRAG`。
  - 对齐提示方块（§4 第 10 条）：放置中给同轨已放对象的锚点按「同位置 `#4060FF` / 同宽度 `#FF4040` /
    两者皆同 `#FF40FF`」画 26×26、90% 方块，都不同就不画；搬运已有对象时全部锚点转白。判定用吸附后的
    值，容差取吸附步长的一半（`EditorPlacementGuide.Classify`）。
  - 前景元素（读数、幽灵描边、箭头、拖尾预览、对齐方块，以及双击插入幽灵的两行读数）画在排在音符层之上的
    `PlacementChrome` 兄弟节点里，因此压在 Note 之上；绘制全部落在该节点自己的 `_draw` 上
    （Godot 4.7 只允许把 `Draw*` 调进正在 `_draw` 的 item，跨节点调用会被 `ERR_DRAW_GUARD` 拦掉），
    所以 `DrawPlacementForeground`/各绘制函数都收一个 `CanvasItem target`；全部 1:1 跟随输入、无插值，
    手势结束即消失，不参与吸附/提交/undo/dirty。
  - 与板面的偏差见 [UI 设计施工约定](<editor-ui-design.md>) §4 第 9、10 条的落地说明。
- **两级选中**：点身体选中整个 Note（长 Hold/Mixer 的任意一点都算身体）；选中后路径显示菱形手柄，
  再点手柄选中该单个 node。单选时出现浮动 chip
  （设计稿 MAIN_SELECTION 的 252×58 切角面板）：精确 BarTime 分数式 + 对象中心读数，删除图标接真实删除命令；
  复制图标没有 duplicate 命令，按 55% 压暗占位。选中描边是比 Note 矩形**每边外扩 6px** 的 cyan 2px 框
  （设计稿 sel-outline 142×36 对 note 130×24），chip 就锚在这个外扩框上（左缘 = 框右缘 + 16、底边贴框顶），
  不再画中心圆点。多选、空选与放置工具态不显示 chip。
- **右键菜单按设计稿重建**（普通 / 非空选区共用一块 480 宽切角面板，节点选中态更高一档）：
  - 轨道段 `← ↓ → #` 切三轨与 Events（真实）；工具格只有画布现有四个工具可选，
    EX-TAP / MIXER / MINE / BARLINE 为 55% 禁用占位。
  - TIME / CENTER / WIDTH 步进器接真实可撤销命令（TIME 步进 = 当前网格，整条路径随头平移；
    CENTER 0.10 / WIDTH 0.05，按住 `X` 细步 0.01）；节点步进不会跨过相邻节点，越界时直接拒绝。
  - 节点态：CURVE TO NEXT 六段选择器接 `SetPathCurveCommand`（尾节点没有出向曲线段，整行压暗）；
    Hold 专属 JUDGE 拨杆接 `SetHoldJudgeCommand`（尾节点锁定 ON，Mixer 没有这一行）；
    DELETE NODE 接真实删除命令（只剩一个节点时禁用）；ADD NODE 接真实插入命令
    （在游标时刻插入控制节点，与双击路径身体同一入口；设计稿文案是 `ADD NODE · DBL-CLICK PATH`，
    这里改成 `ADD NODE · AT CURSOR`，因为这一行自己会插入而不是单纯提示手势）。
  - 动作行按设计稿 NMENU/MENU：`M`/`MARK HERE · <游标>` 与 `R`/`TEST PLAY FROM MARK` 两行接现有游标标记
    与「从标记重播」；`REPLAY` 只在普通面板出现（节点态 NMENU 设计上只有上面两行，所以节点菜单没有这一行）。
    SAVE · CTRL+S、SAVE AS 与
    **SETTINGS** 继续走真实动作（SETTINGS 进设置页，BACK 回 Edit）；EXPORT 与 HITSOUND/MUSIC 滑条无实现，
    55% 占位且不响应点击。
  - 旧的框选非空选区删除菜单语义并进同面板的 DELETE 行；面板锚在光标、近屏幕边缘翻转，整体淡入 120ms。
    面板**不做全屏压暗层**（2026-10-08 拍板，见 [UI 设计施工约定](<editor-ui-design.md>) §4 第 3 条）。
- Events 侧选择或添加 BPM 事件；事件在画布上是 2px `#FF6BB4` 55% 的横线（板面 x=280..1496，即 1216 宽）
  + 线右端 6px 处的 15px bold 纯数值读数（如 `180.0`，选中只提不透明度）。新事件当前使用默认 **150 BPM**，
  首个 BPM 受删除保护。
- 按实际 BPM 时间线换算的时间网格与吸附：网格分小节线 / 拍线 / 细分线三档（密度不足时逐档退让），
  并画小节号、右缘整曲小节标尺（小节刻度 + 当前位置三角，三角尖朝舞台）与底部 2px 进度细线。
  标尺映射的是画布时长估算，不是音频长度；书签菱形还没有数据来源，未绘制。
- 设置页的七项真接点（MOTION / **UI SCALE** / DEFAULT GRID / SNAP / GRID OPACITY / BACKGROUND DIM / **SCROLL INERTIA**）见 §2 的 Settings 行，
  值持久化在 `user://editor-settings.json`；`RESET DEFAULTS` 会同时把当前文档网格与画面接点拉回默认。
- 文档权威编辑链路：画布经
  [CanvasAuthoringController](<../tools/chart-editor-core/Canvas/CanvasAuthoringController.cs>)提交命令给
  [EditorDocument](<../tools/chart-editor-core/Documents/EditorDocument.cs>)，再由文档重建画布投影。
  已接入 dirty 跟踪和撤销/重做，精确 BarTime 属于文档，而不是把显示秒数当作唯一权威数据。

### 尚未完整接入

- v2 模型能承载七类 Note，不代表画布已提供七类完整新建工具；Mixer、EX-Tap、Mine、BarLine 没有独立的完整放置工作流，
  菜单里这四格是 55% 禁用占位。
- 已有路径节点可编辑，路径预览也按曲线逐段求值；但双击插入只覆盖「在既有路径上加点」这一种形态，
  没有为路径新增独立工具，插入点的出向曲线只继承区间左端。插入点精确采样不代表整段曲线形状不变：
  非线性段切开后邻域可能形变，没有通用的亚像素保证；线性段切开保持原直线。
  [路径插入回归](<../tools/chart-editor-tests/Documents/EditorPathTests.cs>)中平滑 fixture 的形变断言是 ≤4px，
  不是所有曲线的误差合同，本轮也没有重新运行该测试。
- 节点菜单能改已有节点的曲线与 judge，也能在游标处插入 node，但没有 Note 级 duplicate 或 Scroll 事件 UI；
  已有 BPM 事件也没有数值编辑 UI（Create 页的初始 BPM 输入不等于事件属性编辑）。CURVE 段只作用于选中节点的出向段。
- 背景媒体、真实粒子/击音配置、波形/mixer 和完整属性编辑仍待接线；菜单里的 `EXPORT` 与
  `HITSOUND`/`MUSIC` 滑条是 55% 占位，不代表这些功能可用。设置页的 EFFECTS / AUTO SAVE / AUDIO 三组同理：
  它们是 55% 占位，既不改画面也不写盘。

## 4. Preview 与快捷键

当前 **Preview / AUTO 是计时与舞台视觉预览，不播放音频**。
[预览循环和快捷键实现](<../editor/scripts/StandaloneEditorMain.cs>)用帧 delta、hispeed 与 rate 推进游标，
没有接入音频播放器、音乐同步、波形或击音。画布时长按 Note 范围估算，不来自音频时长。
hispeed / rate 只影响预览计时，没有音频处理或包参数保存。
原先常驻的快捷键速览、工具态与选中摘要三行调试文字在 2026-10-08 移除（`_status`/`_inspector`），
因此 `Q/E S/W` 这类只改内部量的键现在没有文字回显；`O/P` 那对只改状态文字的假 OFFSET 键一并删掉。

下表仅列当前源码中的编辑快捷键；只在 **Edit 主态且无 LineEdit 持有焦点**时派发，菜单打开时仅处理 Escape 关闭菜单，
其它页面不会把快捷键落到隐藏画布。本轮未作实机验收。
**菜单动作行的 `M`/`R` 是设计稿 glyph，不是当前物理快捷键映射**：菜单 MARK HERE 写入标记，物理 `M` 却是从标记播；
菜单 TEST PLAY FROM MARK 从标记播，物理 `R` 则从零播。这里如实记录差异，不把菜单 glyph 擅改成新的快捷键承诺。

| 操作 | 当前含义 |
| --- | --- |
| 右键 | 打开设计稿版右键菜单：选中路径节点时是 Node Menu（曲线 / judge / 删节点 / ADD NODE，动作行只有 `M` MARK HERE 与 `R` TEST PLAY FROM MARK），否则是同一块普通面板（非空选区多出一行 DELETE，即旧的框选删除语义，动作行多一行 `REPLAY`）。若正在放置/拖动/框选，此次右键只取消手势、不打开菜单。可从菜单切换三轨/Events，并步进选中对象的 TIME/CENTER/WIDTH，也可以从 `SETTINGS` 行进设置页。菜单不叠全屏压暗层。 |
| `1 / 2 / 3 / 4` | 音符侧切换 Normal / Chain / Hold / Select；Events 侧的 `1` 对应 BPM 事件模式。 |
| `↑` | 循环切换三个音符编辑侧；`← / → / ↓` 循环对应轨道的可见性，不是切换编辑侧。 |
| `Shift+← / Shift+→` | 撤销 / 重做。 |
| `Delete / Backspace`、`Escape` | 删除选中对象；取消当前手势或关闭菜单（Escape 不清空已有选择）。 |
| `Space`、`R`、`Enter` | 分别为：切换视觉预览播放/暂停（同时设置 HUD 的 `AUTO`）；从零重播；归零并切换 Select（不主动改变播放态）。`Shift+R` 还将网格重置为设置默认值、hispeed/rate 重置为 1，再从零播。 |
| `M` | 跳到右键菜单里 MARK HERE 记录的游标位置并开始播放（无标记取 0；菜单的 TEST PLAY FROM MARK 行是同一个动作）。 |
| `F1` | 切换 AUTO，**同时把播放态设为新的 AUTO 值**（走 `SetAuto`，不改变工具态）；HUD 右上的 GRID/SNAP 行追加/移除 `AUTO`。 |
| `A / D` | 游标后退/前进 1 秒；按住 Shift 为 0.01 秒。 |
| `C / V`、按住 `Z / X` | 调整网格分母；临时关闭时间吸附（HUD 的 `SNAP` 转 `FREE`）/ 将空间吸附与菜单步进改为更细步长（HUD 显示细步长）。 |
| `Q / E`、`S / W` | 调整预览 hispeed / rate；目前仅影响计时视觉，不改变真实音频播放。 |
| `Ctrl+S` | 保存当前包；未落盘的草稿等价于首次保存，先弹出目标目录选择。 |
| `Ctrl+Shift+S` | 与右键 SAVE AS · CTRL+SHIFT+S 相同，另存到新目录。 |

`M` / `R` 以及菜单的从标记播放/重播仅把 `_playing` 设为 true，未同步 `_auto`；
因此这些入口后 HUD 的 AUTO 不一定反映真实播放态。这是源码中的现状/待确认口径，不在文档整理中修行为。

## 5. 持久化边界与后续接线

**共享底层写包能力已被 shell 调用。**

- [V2PackageWriter](<../shared/Chart/V2/V2PackageWriter.cs>)已有 `Write` / `Create`、相邻 staging、
  严格重新解码/校验、资源发布与失败回滚、音频 probe 和 Gameplay Digest 计算。
  默认音频 probe 为 PCM/IEEE-float RIFF/WAVE；其他解码器需通过 host probe 显式接入。
  **当前 shell 没有提供 host probe**：虽然 Create 接受 .wav/.mp3/.flac/.ogg/.opus/.m4a/.aac 路径作为草稿，
  当前 SAVE/SAVE AS 的音频验证仍只有该 WAV probe，不能把“允许选择扩展名”当作“可成功保存所有格式”。
  这是写盘阶段的音频校验，不是 UI 试听或时长显示。
- [EditorFlow.Persistence](<../editor/scripts/flow/EditorFlow.Persistence.cs>)负责保存对话框和回报，实际调用
  [EditorDocumentSession.Save](<../tools/chart-editor-core/Documents/EditorDocumentSession.cs>)；
  [EditorProjectDraft](<../tools/chart-editor-core/Documents/EditorProjectDraft.cs>)提供外部资源映射和创建请求生成能力。
  会话保留完整 draft，首次落盘用 `BuildPackageCreateRequest` 生成请求，外部音频/封面会一起发布；
  **未保存草稿新增差分不会清掉 draft**，因此仍保留首次保存所需的外部资源来源。
- 保存目标目录来自 `FileDialog(OpenDir)`，空目录通过 `AllowEmptyDestination` 放行；
  `Write` 的 `ExternalResources` 取 `EditorDocument.PendingResourceReplacements`。
- 空谱面等严格校验失败会显示 writer/validator 报错，不替换内存文档、不执行 `MarkSaved`，保留原 dirty/草稿状态。
  校验在发布前完成，替换发布失败有备份/回滚逻辑；但不能承诺所有异常都已回滚：
  **同目录保存可能已发布成功、随后临时文件清理失败而抛错**，此时磁盘保留新版包、会话却尚未标为已保存。
  遇到这类错误应保留内存现场并核对磁盘/备份，不把“保存失败”文字等同于“磁盘一定未变”。
- 编辑器自身的设置写在 `user://editor-settings.json`（[EditorSettings](<../editor/scripts/common/EditorSettings.cs>)），
  与谱面包无关，也不参与 v2 校验或 digest；设置页里没有实现可接的行不写进这个文件。
- 音频播放/同步、波形与 UI 时长显示、完整属性/类型/路径工具及 Storyboard 属于未完成范围；
  已有路径投影、节点编辑与 TIME/CENTER/WIDTH 步进不在此重复列作“全未接入”。
  不从底层类或菜单占位推导为现有界面能力。

### 本轮静态核对仍保留的边界

- MOTION=OFF 的普通 Create 文本框失焦 glow 仍按帧渐退（[CreatePageOverlay](<../editor/scripts/pages/CreatePageOverlay.cs>)的 `_Process`），
  与 Start 路径框、Create 数值/等级框的瞬时 OFF 分支不同；这是静态观察，未做窗口复现或修复。
- 菜单 glyph/物理 `M`、`R` 的差异与播放态/AUTO 不完全同源见 §4；没有擅改快捷键或据此宣称已经验收。
- 64 段路径投影上限、非线性插入形变、异步 digest 首次状态与保存清理异常的实际窗口体验仍需专项验证；
  本轮只核对调用链，不把代码/回归用例存在当作本次测试通过。

## 6. 参考来源、许可与发布边界

DynaMaker UV 是 clean-room 原生实现。交互参考来自
[DynaMaker Modified](<https://github.com/dynamaker-tool/dynamaker-modified>)，固定 commit 为
`99a5a6049f5bc3ee69e5c8cd3a72f4d8c1e99a8d`；参考快照仅用于本地开发研究，已被 Git 忽略，干净克隆不包含它。编辑器开发完成后可移除，不是构建、运行或发布依赖。
上游版权为 Copyright (c) 2021 jmakxd，许可为 MIT；来源、原文许可链接与边界见
[第三方通知](<../THIRD_PARTY_NOTICES.md#dynamaker-modified-reference>)。
当前 Godot 项目不编译、嵌入或运行上游 JavaScript/HTML/CSS/JSON；本轮核对未读取或修改上游源码。

- 不得把原版或上游借用的曲绘、字体、音效、谱面、图标等素材加入编辑器/游戏发行内容；只可发布获授权的 clean-room 内容。
- 编辑器是独立 Windows 工具，使用自己的 [DynaMaker UV Windows preset](<../editor/export_presets.cfg>)，
  不进入 Public 或 Internal Android APK。
- 开发测试数据与研究输出不是公开发行内容；Public APK 必须走[发布规范](<releasing.md>)中的资源白名单、
  clean worktree 与最终产物检查。Internal 测试包也不得公开分发。
- 第三方 MIT/OFL 等许可不等于本项目原创代码和原创资产获得了同样许可；项目当前未提供项目级 LICENSE。
