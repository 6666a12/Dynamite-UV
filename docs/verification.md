# 验证状态与操作

> 维护原则：**执行日期、对象、条件、结果、未覆盖范围**一起写。静态核对、自动检查、导出验包、用户验收是四种证据，不能合并为“全通过”。
> 当前功能与待办以 [交接](<handoff.md>) 为准；这里统一维护最近验证记录、测试包身份与操作命令，避免各 README 重复漂移。

## 原生对象回归入口

先构建对应项目，再按下方运行约定结束残留 Godot。以下为入口，不代表本次重新执行。

游戏 HUD（不加载歌曲）：

```powershell
<Godot-Mono> --headless --path client res://scenes/verification/gameplay_hud.tscn --quit-after 120
```

制谱器手势：

```powershell
<Godot-Mono> --headless --path editor -- --verify-gestures
```

该入口直接驱动 `EditorStageAuthoring` 的 press/motion/release，创建真实 `NoteView`/连接体并检查精确读数；覆盖三轨、3/5/4/12 网格和 SELECT/PATH 的 Hold/Mixer 中间节点身份。它不是可见窗口截图测试，也不验证 Draw 回调的 GPU 像素或原生 HWND 输入投递。

Auto 预览对象回归：`<Godot-Mono> --headless --path editor -- --verify-auto`。覆盖实际共用渲染器/特效节点及舞台播放切换，不是截图或音频回归；同样遵守启动前结束残留 Godot 的约定。

右键面板对象回归：`<Godot-Mono> --headless --path editor -- --verify-context`。检查菜单请求、选中状态、视口边缘和关闭/取消手势；不证明实际 HWND 输入、GPU 像素或音量听感。

## 最近验证记录

2026-11-09 多选Inspector试用版：右侧按选区切换整体变换/统一属性，新增 `EditNoteSelectionCommand` 预校验整组副本再原子替换，保持路径和编辑身份；Shell只负责草稿/输入/选择同步。`builds/editor-batch-shell.log` Shell构建成功，`editor-batch-core.log` 编辑器核心通过，`editor-batch-headless.log` Shell回归通过（真实控件输入、正负分数、错误保留草稿、混合宽度不被换轨覆盖、切选区清草稿、整组撤销）；结构门禁311源文件与diff检查通过。首轮测试误用了internal RequireNote导致编译失败，改用公开集合查找；首轮控件测试未排空Avalonia文本事件队列导致断言失败，补正常dispatcher推进后通过。**未启动可见窗口、未做截图/窄面板GPU视觉验收，未运行完整verify/发布测试；对象磁吸未实现。** 用户已另行确认第一轮偏好记忆有效，不外推为本次Inspector验收。

2026-11-09 制谱器第一轮：偏好/布局持久化与基础复制粘贴已接入。最终 `builds/editor-round1-stage-final.log`、`editor-round1-shell-final.log` 两端构建均0警告/0错误；`editor-round1-core-final.log` 为 `EDITOR TESTS PASS`，覆盖路径完整复制、精确混合网格相对拍时间、新ID/编辑身份、单步撤销重做、重复粘贴和跨文档快照隔离；`editor-round1-headless-final.log` Shell回归通过，新增偏好往返、损坏字节备份、浮动舞台退出重启、自动隐藏工具恢复、Ctrl+C/V与粘贴撤销。结构门禁307源文件通过，`git diff --check`通过。过程中修正NumericUpDown歧义编译错误；浮动布局重启用例先失败，定位Avalonia子窗口关闭优先触发归位，改OwnerWindowOnly并在销毁前捕获后通过。测试配置使用独立临时目录，不写真实用户偏好。**未启动可见编辑器/Godot、未做真实HWND/多屏/DPI/听感验证、未打包桌面发行版、未获本轮用户验收；完整verify/发布测试未重跑，不宣称历史品牌扫描问题已解决。** 持久化和剪贴板语义见 [制谱器说明](dyna-maker-uv.md)。

2026-11-09 顶部进度与判定线启灯后续调整：首轮 `builds/progress-lines-build.log` 客户端构建0警告/0错误；`builds/progress-lines-core.log` 为 `ALL TESTS PASSED`，含新增进度状态、终点口径、音频结束补时与三轨时间轴/长条/三档/seek测试。随后结构检查失败：`GameplayMain` 合计1358行超过1350预算；已清理原有重复空行，未放宽预算。此后 Bash 权限检查服务连续超时，未能执行最终构建/核心复验、结构复查及无窗口Godot回归；不能将首轮成功冒称最终全通过。只读Git检查仍可执行，最终 `git diff --check` 通过（仅LF/CRLF提示）。新增原生用例已写入 `GameplayHudVerification`，但本轮尚未运行。配色脚本检查最终金/淡蓝/白的CVD区分、普通色差与底色对比均通过；分类色板的亮度/彩度限制不通过（此处是用户指定的浅色状态条，含白色，并非分类图表），不称色板验证全通过；AP/FC/—文字提供冗余标识。未启动可见游戏、未检查GPU像素、未导出APK、未取得本次效果用户验收。规则与对象边界见 [HUD 调整](gameplay-hud-v8.md)。

2026-11-09 提交前文档整理：按用户逐项确认同步 HUD/材质/模式切换和制谱器整体验收，将独立血条设计、旧编辑器目标、一次性审查与阶段验证迁入归档，当前页保留本体接入结果/失败与操作入口。检查37份活跃或本批新归档文档的340个本地Markdown链接及标题/显式锚点，0缺失；历史验证保留检查确认47条日期表格记录和14段设计/实施记录仍在当前页或归档中；`git diff --check`通过。首轮发现新归档动效目标仍链接旧§4锚点，已改指同批旧设计归档后重查通过。**本轮仅文档与静态检查，没有重跑C#构建、产品测试、Godot或导出；最新完整verify的品牌扫描失败仍保留，用户实际效果确认单独见下文。**

> 以下保留本体接入时实际执行结果；用户后续确认见下方独立章节。本次文档整理不重新冒称构建/设备通过。

2026-11-09 v8 HUD 接入游戏本体：用户确认整体方向并授权迁入。正式客户端新增顶部装配/无框控件/血条节点及纯显示状态，接真实计分快照、BleedHealth与原暂停流程，不带入演示事件或模拟ACC；BLEED关闭隐藏、两模式固定色、耗尽同帧先清血条、Full/Reduced/Off、标题难度色条和游戏专用线条延长已接。编辑器默认chrome保持。首轮客户端构建因旧HUD装配残留出现17个缺失符号错误，清理后最终`builds/gameplay-hud-v8-build-final.log`为0警告/0错误；核心回归`gameplay-hud-v8-core-final.log`通过（新增血量显示/事件光包络测试）；结构门禁296源文件通过。无窗口Godot原生对象回归`gameplay-hud-v8-native-final.log`打印`GAMEPLAY_HUD_NATIVE PASS`，覆盖节点装配、真实shader参数、模式/关闭/耗尽、鼠标触摸回调与入口护栏、几何/难度条及编辑器默认chrome。完整`python -B tools/verify.py --no-restore`在`builds/gameplay-hud-v8-verify.log`：三端构建、核心/编辑器/Shell回归通过；37个发布测试中36通过、1失败，品牌扫描进入`.claude/worktrees/`里的旧仓库副本导致额外旧名称，未放宽白名单或清理其他工作区；因而**完整verify未全绿**，末尾diff检查另行运行通过。**未启动可见游戏、未做GPU截图或Android/触控端到端/性能验收、未出APK；对象回归不替代实机视觉。** 实施边界和入口见[游戏HUD v8](<gameplay-hud-v8.md>)，后续微调以本体为准。

## 历史证据索引

| 记录 | 现状与阅读边界 |
| --- | --- |
| [2026-11-02—08 制谱器/格式验证](archive/reports/2026-11-02-08-editor-verification.md) | 保留各轮构建、失败/重试、对象与真实窗口证据。用户已确认这批功能整体满意，可收尾；不外推全部 DPI/设备/性能 |
| [2026-10 游戏与结构验证](archive/reports/2026-10-verification.md) | 旧构建/素材/拆分/布局/规则实施证据，不覆盖最新源码 |
| [BLEED/HUD 设计过程](archive/art/2026-11-08-09-bleed-hud-design.md) | 已被本体 v8 替代的独立方案与预览；不再作为未接入待办 |
| [应用图标候选](archive/art/2026-11-08-app-icon-candidates.md) | 未选定、未接入，暂无后续排期；不能当产品图标发布 |
| [更早原文快照](archive/2026-10-26-cleanup/README.md) | 原始记录保留，不回写历史验证结论 |

## 用户反馈与验收边界

- **2026-11-09 用户确认**：游戏 HUD v8 本体实际效果满意；音符材质与命中特效（侧轨旋转、Mixer 徽章、默认亮度和残影）、Standard→Hardcore→Standard 模式切换基本通过。模式切换确认不代表成绩模式隔离已实现。
- **2026-11-09 用户确认**：制谱器最近这批功能整体满意、可以收尾，范围包括放置/拖动/调宽/路径编辑、播放预览与滚轮命中特效、右键面板、跨屏框选、Dock 面板与窗口恢复。无需再把这一批整体列为待验收；设备/DPI/长谱性能等未覆盖边界仍保留。
- **仍保留专项**：Mixer 暂停恢复/原异常段落、快速切曲/试听没有新增用户确认；低端性能继续暂缓。上述通过项未附具体设备/系统清单，不补写 Android、所有触控组合或性能达标。
- **后续方向（非自动实施授权）**：在游戏本体上继续视觉微调，以及继续完善制谱器；具体新功能待后续任务。结果音效、BLEED 专用失败页等保留缺口，不自动排期。

- 2026-11-08：用户确认任务栏恢复问题已修复。此前屏幕矩形框选在播放跨屏时丢失已判定对象，不能用其静态框选测试宣称跨屏能力通过；现行规则见制谱器专题。

- 2026-11-07：用户确认**音符配色 v2、首页与结果字标已完成**；不再作为整体待验收事项。低端性能仍未验收，**暂缓**；未提供逐项设备记录，不外推到所有平台，也不把结果音效或未实现功能视为完成。

- 2026-10-07：既有结算 v2 原流程通过用户实机验收。
- 2026-10-14：用户认可已接入客户端的 X/Ψ/D；Mixer 初步反馈“目前看来没什么问题”，不扩大为全部边界通过。
- 2026-10-17：多指输入、短 Hold 手感、对象池修复、侧轨 `691/790` 基本通过。侧轨保留现值；不再把这些整体列为未验收，也不推定设备/指数量/所有边界均已覆盖。
- 校准页已有 Android 采样/统计/APPLY 全流程验收记录；10-25 拆分并未重新进行设备验收。
- 未覆盖设备/性能、密集曲线、快速切曲和暂停恢复专项仍按 [交接 §4](<handoff.md#4-仍需处理>) 跟踪。

## 最近有记录的 Internal 测试包

**这是历史产物身份，不是“与当前源码同步”的承诺。** 2026-10-26 未重新哈希、验包或检查安装；使用前须重新核对现有文件。

| 字段 | 2026-10-14 导出/10-17 反馈时的记录 |
| --- | --- |
| 本地路径 | `client/builds/internal/dynamite-universe-internal-testdata.apk`（忽略，不随克隆取得） |
| 版本 | `0.1.2-internal-testdata` / code 4 / ARM64 |
| 身份与用途 | Internal / Debug 流程 / `com.dynamiteuniverse.game.internaltest`；**DO NOT DISTRIBUTE** |
| 大小 | 185406710 bytes |
| SHA-256 | `235dc36bc64214e1ff67978168346f900ae2453e6e5f26ed661b77867d904e82` |
| 覆盖 | 短 Hold、八评级、Mixer 放置/宽限、音频回退、此前异步卡/池修复及侧轨比例；当时 APK gate PASS |
| 不覆盖 | 10-20 配色及 10-25 拆分；从当时未提交工作区导出，不能只凭 HEAD 独立重建 |

Release 性能探针不等于合法的正式 Internal 测试包。Public 包只有按 [发布流程](<releasing.md>) 从 clean worktree 导出并通过最终 checker 才能分发。

## 非图形复验命令

从 **community 根目录**执行；以下是复验流程，不是本次已经执行的声明。

```powershell
git status --short --branch
python -B tools/verify.py
# 已还原依赖：python -B tools/verify.py --no-restore
```

统一脚本串起以下步骤，任何非零退出即停止：

```powershell
python -B tools/architecture/check.py --report
dotnet build client/DynamiteUniverse.csproj
dotnet build editor/DynaMakerUv.Editor.csproj
dotnet build editor/shell/DynaMakerUv.Editor.Shell.csproj
dotnet run --project tools/core-tests/CoreTests.csproj
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --headless
python -B -m unittest discover -s tools/release/tests -p "test_*.py"
git diff --check
```

- 两套 C# 回归是断言式控制台程序，应使用 `dotnet run`，不能仅跑 `dotnet test` 后认定执行过。
- 构建要求 0 error；遇环境 warning/离线 restore 必须写明条件和实际结果，失败不能跳过后继续宣称通过。
- 核心默认只用版本化 clean-room fixture。官方开发语料需显式传参，且不能代替公开门禁：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj -- --dev-testdata client/testdata/packs
```

- 结构门禁只证明依赖/源码链接/行数预算，不是语义、编译、行为或性能证明。
- `.tmp/modularize*/` 的快照/探针被忽略，可能不随克隆取得，不能当成已接入 CI 的稳定回归入口。涉及行为变化时应补版本化用例，不能只引用旧 headless 哈希。
- 严禁为通过检查而放宽源码预算、发布白名单、品牌历史允许计数或把内部语料变为默认依赖。

## 运行、截图和录屏

只在用户要求运行或确需视觉验证时使用 **Mono** 版 Godot。先结束残留窗口，无进程时 taskkill 提示未找到属预期：

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe

# 游戏；编辑器从 Avalonia Shell 启动并嵌入 Godot
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path client
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj

# 制谱器真实窗口回归（分窗口隔离，会启动/结束本次 Godot 并保存截图）
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke-empty builds/editor-shell-empty-smoke
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke builds/editor-shell-smoke
```

制谱器直接使用 Godot `--path editor` 仅用于原生舞台调试，不包含文件菜单和 Avalonia 工作台。
在同一受控操作中启动、采集、结束本次进程；输出放被忽略的 `tmp_shots/`，不残留窗口污染下次截图。

以下仅以窗口位于 (0,0)、1600×900 为例，实际需调整 gdigrab 偏移和尺寸：

```powershell
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -i desktop -frames:v 1 tmp_shots/shot.png
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -framerate 30 -i desktop -t 45 -c:v libx264 -pix_fmt yuv420p -preset veryfast tmp_shots/demo.mp4
```

`-frames:v` 放在 `-i` 后；yuv420p 尺寸须为偶数。开发 gameplay 可用 `DYNAMITE_UNIVERSE_START_SEC` 定位，Esc 暂停，F12 跳至谱尾前 3 秒。
headless SongClock 使用系统计时器/固定验证时钟，不代替窗口音频结束、真实 I/O、听感、GPU 像素或真机触控验证。

## 文档维护检查

文档移动后检查所有活跃文档与新归档的相对链接/锚点；旧不可变快照的链接以原位置解释，不能为“修链接”重写历史正文。
2026-11-09 整理范围见 [本次整理说明](archive/reports/2026-11-09-documentation-cleanup.md)；更早的字节快照与哈希仍见 [2026-10-26 归档说明](archive/2026-10-26-cleanup/README.md)。
