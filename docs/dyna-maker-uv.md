# DynaMaker UV 当前说明

> 状态日期：**2026-10-07，静态代码核对**。本文是制谱器当前实现范围的单一状态来源；
> 本次整理未运行构建、测试、Godot 或服务，不代表新的构建通过或实机验收。
> 旧实现说明与历史验证记录统一见[历史归档索引](<archive/README.md>)，不作为当前能力承诺。

## 1. 项目与启动

DynaMaker UV 是独立的 Windows 桌面制谱器，使用 **Godot 4.7.1 Mono/.NET + C#，目标框架 net9.0**。
[编辑器项目](<../editor/DynaMakerUv.Editor.csproj>)与[游戏项目](<../client/DynamiteUniverse.csproj>)
拥有各自的 Godot 项目根、入口场景、程序集和导出 preset；二者在构建时共享谱面核心，运行时不互相启动。
游戏主菜单的“谱面工坊”仍为未来服务器谱面库预留，不是本地制谱器入口。

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

启动后在 **Start** 页点击 **BROWSE** 选择包含包元数据的本地目录，或直接填写目录路径，
再点击 **OPEN PACKAGE**。选择的是包目录，不是单个谱面文件；当前打开流程以 UI 为入口。

## 2. 当前页面与包流程

[页面流程代码](<../editor/scripts/EditorFlow.cs>)当前只有 **Start / Pack / Create / Edit** 四页：

| 页面 | 当前行为 |
| --- | --- |
| Start | 目录路径、BROWSE、OPEN PACKAGE、NEW PACKAGE。 |
| Pack | 显示包信息与差分列表；选择差分进入 Edit，CREATE DIFFICULTY 进入 Create。 |
| Create | 填写包/差分元数据；新包要求一个可访问的外部音频文件，创建结果仍是内存草稿；添加差分也只更新内存文档。 |
| Edit | 固定 1920×1080 舞台、画布编辑、返回 Pack、曲名/难度牌、AUTO 视觉预览开关与只读状态文字。 |

**strict v2 本地目录包打开已经真实接入**：OPEN PACKAGE 调用
[EditorPackageRepository.Open](<../tools/chart-editor-core/PackageRepository.cs>)，进行目录与普通资源文件的安全检查、
包/谱面严格解码和跨文件语义校验；失败不进入 Pack 页。这不等于 legacy XML/DY 或 ZIP 导入，
当前打开流程也没有音频解码、时长探测或试听能力。格式合同见[Chart Format v2](<chart-format-v2.md>)。

**新建不写盘，现有包的编辑也不写盘。** 当前 Create 以 BPM=150、offset=0、grid=16、无封面的参数
构建草稿；初始谱面不自动添加 Note。进入 Edit 后，画布网格仍由自身设置管理，不能把创建参数当作完整的配置 UI。
保存、另存、创建落盘和导出入口均未接通，关闭后不能依赖当前 UI 恢复编辑修改。

当前没有可用的 **Settings 或 Storyboard 页面**，也没有可交互的数值 Inspector 或保存按钮。
界面中的 Inspector 名称对应只读选中对象摘要，不代表属性编辑面板已经实现。

## 3. 画布与编辑范围

[AuthoringCanvas](<../editor/scripts/AuthoringCanvas.cs>)采用固定 1920×1080 坐标，
主场景中的 `ChartCanvas/SharedGameplayStage` 负责 clean-room 舞台视觉；画布输入叠加在视觉层上，
不是游戏完整判定运行时。

### 已接入

- Center / Left / Right 三个音符编辑侧，以及 Events 事件侧。
- **Normal → Tap、Chain → Drag、Hold、Select** 四种画布工具；Hold 为先定头部/宽度、再定尾部的两阶段放置。
- 对象选择、Shift 多选、框选、移动、宽度调整、已有路径节点的时间/位置/宽度编辑，以及删除和取消手势。
- Events 侧选择或添加 BPM 事件；新事件当前使用默认 **150 BPM**，首个 BPM 受删除保护。
- 按实际 BPM 时间线换算的时间网格与吸附。
- 文档权威编辑链路：画布经
  [CanvasAuthoringController](<../tools/chart-editor-core/CanvasAuthoringController.cs>)提交命令给
  [EditorDocument](<../tools/chart-editor-core/EditorDocument.cs>)，再由文档重建画布投影。
  已接入 dirty 跟踪和撤销/重做，精确 BarTime 属于文档，而不是把显示秒数当作唯一权威数据。

### 尚未完整接入

- v2 模型能承载七类 Note，不代表画布已提供七类完整新建工具；Mixer、EX-Tap、Mine、BarLine 没有独立的完整放置工作流。
- 已有路径节点可编辑，不代表完整路径预览：当前 Hold 舞台投影只连接头与最终尾，不能据此验收多节点曲线效果。
- 曲线参数、节点增删/judge 标记、Scroll 事件及 BPM 数值均没有完整的数值编辑 UI。
- 背景媒体、真实粒子/击音配置、波形/mixer 和完整属性编辑仍待接线；菜单中的提示或局部开关不等于这些功能可用。

## 4. Preview 与快捷键

当前 **Preview / AUTO 是计时与舞台视觉预览，不播放音频**。
[预览循环和快捷键实现](<../editor/scripts/StandaloneEditorMain.cs>)用帧 delta、hispeed 与 rate 推进游标，
没有接入音频播放器、音乐同步、波形或击音。画布时长按 Note 范围估算，不来自音频时长。
`AUDIO RATE` 和 `OFFSET` 的现有状态文字也不代表音频处理或包参数保存已实现。

下表仅列当前源码中的编辑快捷键；在画布编辑场景使用，未作本次实机验收：

| 操作 | 当前含义 |
| --- | --- |
| 右键 | 空白画布为基本菜单；框选形成非空选区时为删除菜单。进行中的放置/拖动/框选会先被取消。可从基本菜单切换三轨/Events。 |
| `1 / 2 / 3 / 4` | 音符侧切换 Normal / Chain / Hold / Select；Events 侧的 `1` 对应 BPM 事件模式。 |
| `↑` | 循环切换三个音符编辑侧；`← / → / ↓` 循环对应轨道的可见性，不是切换编辑侧。 |
| `Shift+← / Shift+→` | 撤销 / 重做。 |
| `Delete / Backspace`、`Escape` | 删除选中对象；取消当前手势或关闭菜单（Escape 不清空已有选择）。 |
| `Space`、`R`、`Enter` | 切换视觉预览播放/暂停；从零重播；归零并切换 Select。 |
| `A / D` | 游标后退/前进 1 秒；按住 Shift 为 0.01 秒。 |
| `C / V`、按住 `Z / X` | 调整网格分母；临时关闭时间吸附 / 将空间吸附改为更细步长。 |
| `Q / E`、`S / W` | 调整预览 hispeed / rate；目前仅影响计时视觉，不改变真实音频播放。 |

## 5. 持久化边界与后续接线

**共享底层已有写包能力，当前 Godot shell 没有调用写包接口。**

- [V2PackageWriter](<../shared/Chart/V2/V2PackageWriter.cs>)已有 `Write` / `Create`、相邻 staging、
  严格重新解码/校验、资源发布与失败回滚、音频 probe 和 Gameplay Digest 计算。
  默认音频 probe 为 PCM/IEEE-float RIFF/WAVE；其他解码器需通过 host probe 显式接入。
- [EditorProjectDraft](<../tools/chart-editor-core/EditorProjectDraft.cs>)已有外部资源映射和创建请求生成能力，
  但当前 Create 流程只保存 `draft.Document`，未把完整 draft 的外部音频来源/包内目标映射接续到保存流程。
- 接通保存前，需要保留并传递外部音频资源映射、选择目标目录、生成完整快照，调用共享 writer，
  并在成功后更新文档保存状态。不能仅把 UI 菜单改名就宣称导出完成。
- 音频播放/同步、波形与时长校验、属性数值编辑、完整路径预览及 Settings/Storyboard 属于后续工作，
  不从底层类的存在推导为现有界面能力。

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
