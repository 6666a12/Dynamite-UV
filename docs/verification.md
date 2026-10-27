# 验证状态与操作

> 维护原则：**执行日期、对象、条件、结果、未覆盖范围**一起写。静态核对、自动检查、导出验包、用户验收是四种证据，不能合并为“全通过”。
> 当前功能与待办以 [交接](<handoff.md>) 为准；这里统一维护最近验证记录、测试包身份与操作命令，避免各 README 重复漂移。

## 最近验证记录

| 日期 / 来源 | 已有结果 | 不能证明什么 |
| --- | --- | --- |
| 2026-10-26 模块化复审 | [进程/对象边界分析](<modularity-review-2026-10-26.md>)；重新运行结构报告通过（219 C# 源码文件），补统计 NoteColorsScreen 3 partial 共 895 行；219 个 C# 文件、八个项目、三个检查/策略文件及本轮审查保护指纹在建立后未变；未发现暂存项；新增分析及入口文档链接/源码行号核验与 `git diff --check` 通过 | 仅静态复审及文档检查；未运行 C# 构建/回归、Godot/headless 探针、导出/验包或设备验收；建议未实施，静态风险未复现 |
| 2026-10-26 文档整理 | 源码/文档/项目引用静态核对；结构检查通过（219 源码文件）；37 项发布测试通过；35 份 Markdown / 378 个本地链接检查通过，31 份快照及 7 项归档哈希一致；417 个已跟踪非文档路径与 Git index 均未变；`git diff --check` 通过 | 未重跑 C# build/core/editor-tests，未启动 Godot、导出 APK/桌面包或做设备验收；发布测试不是实际 APK 验包 |
| 2026-10-25 client 续拆历史记录 | 统一 `verify.py --no-restore` 通过：两端 0 warning / 0 error、核心/编辑回归、37 项发布测试；9 组 1980 帧状态比较一致（详情卡类型名归一化） | 探针固定试听完成顺序和 elapsed；不证明真实 I/O 时延、GPU 像素、触控或听感 |
| 2026-10-25 第三轮历史记录 | 两端构建/回归/37 项发布测试；8 组 11520 条游玩记录与 3300 帧 UI 捕获状态一致；Windows ExportRelease 程序集 publish 含 Presentation/编辑核心 DLL | 不是完整 Godot 桌面包或 Android APK 导出；不是可见窗口/真机验收 |
| 2026-10-25 第二轮历史记录 | 统一验证；8 组 3840 模拟帧/7680 判定与节点记录一致；草稿新增差分后首次保存回归 | 不证明 GPU 粒子、音频、性能或所有 UI 交互 |
| 2026-10-20 配色 v2 历史记录 | 两端构建、核心/编辑回归和 37 项发布测试通过 | 无对应新 APK/截图；触控、软键盘、最终视觉待用户验收 |

2026-10-25 个别额外 editor restore/build 曾因 NuGet 漏洞索引不可达出现 2 条 NU1900（0 error）；不能把这改写为代码失败，也不能无条件忽略全部警告。
2026-10-13/19/20 的 SongSelect CS8604 是旧构建记录；10-25 统一验证记录为 0 warning，不应继续列为当前已复现警告。本次没有重新编译确认。

历史原文与边界：

- [第二轮报告](<archive/reports/2026-10-25-modularization-phase2.md>)、[第三轮报告](<archive/reports/2026-10-25-modularization-phase3.md>)、[client 续拆记录](<archive/reports/2026-10-25-client-followup.md>)。
- [配色 v2 的当时验证与设备清单](<note-colors-v2.md>)。
- [整理前交接快照](<archive/2026-10-26-cleanup/README.md>) 保留了更早的逐项构建/验包流水。历史记录不等于当前工作区重验。

## 用户反馈与验收边界

- 2026-10-07：既有结算 v2 原流程通过用户实机验收。
- 2026-10-14：用户认可已接入客户端的 X/Ψ/D；Mixer 初步反馈“目前看来没什么问题”，不扩大为全部边界通过。
- 2026-10-17：多指输入、短 Hold 手感、对象池修复、侧轨 `691/790` 基本通过。侧轨保留现值；不再把这些整体列为未验收，也不推定设备/指数量/所有边界均已覆盖。
- 校准页已有 Android 采样/统计/APPLY 全流程验收记录；10-25 拆分并未重新进行设备验收。
- 最新配色、材质、低端性能、密集曲线、快速切曲和暂停恢复专项仍按 [交接 §4](<handoff.md#4-仍需处理>) 跟踪。

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
dotnet run --project tools/core-tests/CoreTests.csproj
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
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

# 游戏与编辑器择一启动
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path client
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path editor
```

编辑器从 Start 页 UI 打开 strict v2 目录包，不用旧 `--editor-package` / `--editor-workbench` 参数。
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
本次整理的迁移清单、原文 SHA-256 与归档范围见 [2026-10-26 归档说明](<archive/2026-10-26-cleanup/README.md>)。
