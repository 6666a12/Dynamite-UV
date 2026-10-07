# Dynamite Universe 当前交接

> 状态核对：2026-10-07，依据当前源码和有效规格进行静态核对；本次文档整理未重新构建、测试或启动游戏。
> 本文只保留当前实现、固定决策、真实待办和操作流程。阶段更新与旧验证记录见 [历史归档](<archive/README.md>)。
> 专题入口见 [文档导航](<README.md>)。项目名为 Dynamite Universe；冻结的 `dynamite-uv-*` / `gameplay-v1` wire 标签继续兼容。

## 1. 项目约束

- clean-room 社区音游；Public APK 不得包含任何原版素材或谱面。整个 `client/testdata/` 是被忽略的内部开发区，只可用于受控 Internal Testdata 构建，见 [发布流程](<releasing.md>)。
- Godot 4.7.1 Mono / C#，目标 `net9.0`。游戏在 [client](<../client/>)，桌面制谱器在 [editor](<../editor/>)，各有项目、场景、程序集和导出 preset；互不启动，不将编辑器打入 Android 包。
- 游玩固定 1920×1080，`canvas_items` 等比保留黑边，不引入自适应游玩布局。改几何前阅读 [玩法规格](<gameplay-spec.md>)、[视频测量](<video-geometry-analysis.md>) 与 [像素标定](<pixel-calibration.md>)。
- [shared](<../shared/>) 不依赖 Godot，拥有谱面/精确时间/判定/计分/包写入语义；客户端和编辑器是适配层，不复制规则。每帧协调顺序见 [架构](<runtime-architecture.md>)。
- 默认中文交流；普通修改后不启动游戏代替用户目测。确需截图/录屏时才按 §6 操作，启动前先结束残留 Godot。
- 当前工作区已有大量未提交代码和资产，不能 reset、checkout 或删除无关改动。归档记录不授权恢复旧架构或重新实施已完成修复。

## 2. 当前实现

### 2.1 游戏闭环与 UI

| 区域 | 已接入 | 边界 |
| --- | --- | --- |
| 首页 | 游玩、设置、真实 Blender 模型的 3D 能量核心 | “谱面工坊”禁用，预留给服务器谱面库；分区启灯新概念未接入 |
| 选曲 v2 | QUICK SETTINGS、Standard/Hardcore、BLEED/MIRROR/AUTO、曲名/曲师/谱师实时搜索、就地展开卡、试听、难度切换和最佳成绩 | 筛选排序仍占位；进场默认收起、不自动试听 |
| 游玩 | 三轨鼠标/多点触摸、镜像、Auto、暂停/继续、重开、返回选曲 | Auto 忽略游玩输入、全 Prefect 且不写成绩；多指设备层仍待验证 |
| 结算 v2 | 不透明双屏风、v5 五评级图集、分组信息、AP/FC、判定分布、纪录增量、三动作按钮 | 已接入，实机观感待用户验收；运行时评级是图片/图集，不是实时 3D 模型 |
| 设置 | GAMEPLAY/AUDIO/DISPLAY；偏移、落速、特效、模式、Music/Hit/UI 音量与 Full/Reduced/Off | 设置已持久化；完整 Hit/UI 声音资产链路未完成 |

选曲展开即自动播放预览，切歌/收起/离页/START 停止；传输键可暂停，进度只读且不循环。无配置时使用 0s/30s，预览整段加载音频，大 WAV 首次展开可能卡顿。
几何和状态细节见 [UI 设计施工约定](<editor-ui-design.md>)。

`GameSession` 保存跨场景选择与预加载。无选择直接启动 gameplay 时，仅 Godot 编辑环境和 Internal Testdata 构建可回退开发谱并默认 Auto；Public 和未分类导出回到选曲。

结算 Full 为 600ms 合拢、320ms 闭合停顿、480ms 拉开，1400ms 起评级、约 2650ms 完整揭晓；慢加载延长不透明闭合等待。Reduced/Off 为 260/0ms。歌曲尾音不因屏风动作主动截断。时间线从 `ShowResults` 被调用开始，**不是承诺最后一个判定瞬间启动**；当前结束条件仍为谱末后约 2 秒或音频自然结束。详见 [结算 v2](<result-v2-design.md>)。

### 2.2 谱面、模式与判定

- 目录扫描：开发/Internal 先读取 `res://testdata/packs` 再读 `user://charts`，同 id 玩家包覆盖开发包；Public/未分类导出只读玩家包。
- 显式 v2 必须严格解码、跨文件校验与计算 Digest，失败拒绝；正常 legacy 先尝试转换成 v2，不适用时保留 legacy-direct 兼容路径。正式合同见 [Chart Format v2](<chart-format-v2.md>)。
- legacy `Position` 为左缘，范围 `[P,P+W]`，中心 `P+W/2`；v2 使用自己的 center/width 合同，不能混用。Type 1 Tap、2 Drag、3/4 Hold、5 EX-Tap、6/7 Mixer、8 Mine、9 BarLine。
- EX-Tap 为窗口倍率 1.0 的二值判定：窗内 Prefect、出窗输入 Miss，无 GR/GD；参与同帧时间锁。BarLine 只渲染，不计分/Combo。
- Standard 按难度取 Casual/Normal/Hard/Tutorial；Hard 及以上含 custom 用 Hard。Hardcore 对所有难度强制 Hard 窗口 ×0.5（31.25/56.25/81.25/125ms），Holding 宽限沿用 Hard。
- BLEED 是修饰开关，不是第三个模式；Hardcore 下强制开启且锁定，锁定期不改用户持久化值。**机制未实现**，不能把标签或开关当作真实扣血玩法。
- 镜像只改变屏幕映射：左右轨互换，中轨绕中心坐标 2.5（屏幕 x=963）反射，宽度不变。判定逆映射只走 `GameplayMain.ChartTouchOf` / `ChartPointOf`，镜像只施加一次；Hold release 掩码按显示轨道比较。
- 普通窗口按固定 150 BPM 换算；Hold 首次断触时用当时 BPM（钳制 120–200）冻结宽限 deadline，后续 BPM 改变不移动 deadline。
- 触摸由全局 `_Input` 按 id/phase 收集，关闭触摸模拟鼠标。Early/Exact 同批锁最早目标毫秒组，同刻多押允许；Late 不检查也不武装锁。普通 Note 独立扫描且可复用触点；触摸宽 0.40、每侧扩 0.20，Mine 不扩边。
- Hold：头和实际 judge 节点是完整主判定；头 Miss 批量结算后续节点；宽限内可换指接回；确认断开后中间节点 Miss、尾按 release 时刻评级；持有过尾为 Prefect。
- Mixer：可随时重接，Body 常驻，仅接触时显示动态头；从头起每 1/8 chart bar 产生主判定，非网格尾不补判，无额外尾命中率判定。
- legacy 资源加载已拒绝逃逸路径、URI/盘符和根/父目录 reparse point；v2 包读写也有严格路径边界。旧审查问题与修复经过仅在 [审查归档](<archive/reports/2026-09-09-code-review-fixes.md>) 保留。

详细规则以 [玩法规格](<gameplay-spec.md>)、[v2 合同](<chart-format-v2.md>) 与 shared 为准；原版研究的未知边界见 [原版判定分析](<original-judgement-analysis.md>)，不据未闭环证据改变社区语义。

### 2.3 分数与存档

- 主判定使用 100/70/50/0 权重，进入 Combo、判定计数、理论满分及 Health/Boost 缩放；显示/存档分数归一化为 1,000,000。
- HUD ACC = 当前得分 / 已判定理论满分；结果 CLEAR = 全谱得分 / 全谱理论满分。Ω≥98、S≥95、A≥90、B≥80，否则 C。
- 成绩写 `user://scores.json`：v2/成功转换路径使用 `(packId, chartId, rulesetId, gameplayDigest)`；legacy-direct 保留历史 `packId:diff` 键。两套身份不混为单一旧键。
- Auto 绝不写成绩。当前成绩身份没有独立运行模式维度；不能保证 Standard/Hardcore 已完全隔离，分模式记录仍待设计。
- Health/Boost 有内部计算，但客户端无对应 HUD 或 GameOver/Buff/EX Boost 行为；`OriginalJudgeMath` 仅保留研究数学，不驱动社区 HUD/存档。
- 判定文字沿用 `PREFECT`；Great/Good 保留 E/L。

### 2.4 制谱器（当前 shell，不是旧阶段能力）

- 独立 Godot 桌面项目，页流为 Start → Pack → Create → Edit；从 UI 选本地目录并 OPEN PACKAGE，能真实打开 strict v2 包。
- 新建包/差分先进入内存草稿；现有包的编辑也只留内存。画布已绑定 `EditorDocument` / `CanvasAuthoringController`，变更走命令、dirty 与 undo/redo。
- 当前工具为三轨 Tap/Drag/两阶段 Hold/Select、Events/BPM、选择/拖动/宽度/已有路径节点编辑及网格/吸附。底层保留更多 v2 类型，不表示全部类型、Scroll、曲线或完整路径编辑 UI 已接通。
- **保存/导出未接入 UI**；shared 已有事务式 `V2PackageWriter.Write/Create`，不能因此称编辑器已完成发布闭环。外部音频映射、strict reopen/probe/Digest 等还需 shell 接线。
- 播放只是视觉游标计时，未接真实音乐/击音、波形或音频 mixer；无可用的 Settings/Storyboard 工作区。
- 不再推荐旧 `--editor-package` / `--editor-workbench` 参数。当前启动和详细能力以 [制谱器说明](<dyna-maker-uv.md>) 为准。

## 3. 舞台与视觉不变量

| 项目 | 当前值 |
| --- | --- |
| Center 判定线 | y=861 |
| Mixer 装饰条 | y=632，x=675..1244；不是判定线 |
| Center 中心映射 | x=280+273.2×(P+W/2)；视觉宽 W×273.2×0.95 |
| Side 判定线与中心 y | x=184/1736；y=840−115×(P+W/2) |
| Side Note 长度 | W×102×0.95；BarLine 使用 W×190×0.95 |
| 行程与距离尺度 | Center 790px，Side 691px，Side scale 0.75 |
| 滚动 | 剩余音频秒×当前流速×玩家落速×1026px/s，固定二维映射，不做透视 |
| 触点投影 | y>771 含 Center，x<400 含 Left，x>1520 含 Right；可同时属于多个轨道 |

布局测量注释保留在 `GameplayMain` 顶部；预览几何在 shared 的 `GameplayStageGeometry`。
Note 为程序化切角材质；命中核心爆发加 GPU 粒子，Hold/Mixer 有持续接触层。
普通 Tap/EX/Drag 未判定时越线后 24px 满亮、40px 淡出；命中即移除本体。Hold 失败后继续下落/裁剪并在 180ms 内渐淡；Mixer 不下穿；Mine 安全到线和 BarLine 到线即回收。
完整参数见 [动效规格](<ui-motion.md>)，有效布局见 [像素标定](<pixel-calibration.md>)。

## 4. 仍需处理

### 已接入、待用户验收

- 结算 v2 的屏风遮挡、五评级入场/低频电流、文字布局和 Reduced/Off 实机效果。
- 最新 Note 生命周期、命中爆发及持续粒子的真实设备观感。

### 未完成的功能/决策

- 制谱器保存/首次创建/Save As 闭环、外部资源映射和失败反馈；更完整类型/路径/属性 UI，真实音频试听与波形。
- BLEED 规则、Health/GameOver 等是否接入，以及 Standard/Hardcore 成绩隔离。
- **EX-Tap 规范偏差**：代码已为倍率 1.0 / 二值判定，冻结 v2 §12.1 仍写 1.5。需单独确认规则集版本与兼容策略；本轮不改冻结合同或业务行为，不把差异静默清掉。
- 选曲筛选/排序、搜索清除等增强；预览大音频首次加载性能。
- 完整打击音、UI 音效及 Music/Hit/UI 总线资产链路。
- 首页分区启灯只是设计探索，需单独确认；Storyboard 仍为 [未实现提案](<storyboard-editor-proposal.md>)。
- 社区服务器尚未接客户端；本仓库无服务器工程，工作区外层仅有 C++ 起步骨架，不代表已选定正式服务器架构。

### 验证与工程护栏

- 真机 3 指以上、跨轨多押/换指/同帧短点；端到端 pointer 序列回归，窗口失焦/触摸取消统一清理。
- 客户端/编辑器共享视觉副本的一致性保护、几何同源及密集 Smooth 路径性能评估。
- 主仓库 CI 与生成产物保留/清理策略；性能问题应先基准测量，不能把历史审查建议全部视为未实施缺陷。
- 公开 APK 最终验包未在本次文档整理中执行；未通过 public checker 的产物不得分发。

## 5. 目录地图

| 目录 | 职责 |
| --- | --- |
| [client/scripts/game](<../client/scripts/game/>) | 输入/运行时、加载、会话、持久化与舞台适配 |
| [client/scripts/ui](<../client/scripts/ui/>) | 页面、转场、结算和 UI 原语 |
| [shared](<../shared/>) | 纯 C# 格式、时间、判定、分数与安全包读写 |
| [editor/scripts](<../editor/scripts/>) | Godot 制谱器 shell 与画布 |
| [chart-editor-core](<../tools/chart-editor-core/>) | 可编辑文档、命令、快照、精确编辑与安全包打开 |
| [core-tests](<../tools/core-tests/>) / [chart-editor-tests](<../tools/chart-editor-tests/>) | 断言式回归 |
| [release](<../release/>) / [schemas](<../schemas/chart-format-v2/>) | 发布政策/来源清单与 v2 机器合同 |
| [design](<../design/>) / [grade-assets](<../tools/grade-assets/>) | 美术源文件、预览与重建脚本；不整体打入游戏 |
| [archive](<archive/README.md>) | 历史报告、检查点及整理前快照 |

## 6. 构建、运行与验证

### 6.1 非图形验证

从 community 根目录运行；这是复验流程，不是本次通过声明：

```powershell
dotnet build client/DynamiteUniverse.csproj
dotnet build editor/DynaMakerUv.Editor.csproj
dotnet run --project tools/core-tests/CoreTests.csproj
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
python -B -m unittest discover -s tools/release/tests -p "test_*.py"
git diff --check
```

构建要求 0 error。离线 NuGet 漏洞索引不可达可能出现 NU1900，记录具体环境，不将其等同于代码失败或无条件忽略全部 warning。
默认核心回归只读 clean-room fixture；内部官方开发语料必须显式传入：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj -- --dev-testdata client/testdata/packs
```

历史通过记录与录像等价结果已归档，不能用作当前提交或工作区的最新验证。APK 两模式的 clean worktree、签名与最终验包见 [发布流程](<releasing.md>)。

### 6.2 启动、截图和录屏

只在用户要求运行或确需视觉验证时操作。使用 Mono 版本，先结束残留窗口：

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe

# 从 community 根目录启动各自项目
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path client
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path editor
```

游戏与编辑器择一启动。编辑器包从 UI 打开目录，不用旧工作台参数。
截图/录屏保存到被忽略的 `tmp_shots/`；按实际窗口调整 gdigrab 偏移与尺寸。在同一受控操作中启动、采集并结束本次进程，避免残留窗口污染截图。

```powershell
# 窗口位于 (0,0)，捕获尺寸 1600x900 时的示例
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -i desktop -frames:v 1 tmp_shots/shot.png
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -framerate 30 -i desktop -t 45 -c:v libx264 -pix_fmt yuv420p -preset veryfast tmp_shots/demo.mp4
```

`-frames:v` 位于 `-i` 后，yuv420p 尺寸须为偶数。开发直接启动 gameplay 可用 `DYNAMITE_UNIVERSE_START_SEC` 定位；Esc 暂停、F12 跳到谱尾前 3 秒。headless `SongClock` 使用系统计时器，不能替代窗口音频结束/实机观感验证。

## 7. 资产与安全

- 曲绘由社区谱师随包提供，不使用 AI 生成；缺图用 clean-room 占位，公开客户端不内置 AI 生成曲绘。AI/曲绘研究生成工具只输出仓库外本地目录，不得输出 `client/assets/`。这不限制已批准的原创 Blender 评级渲染与运行时打包流程，见 [Blender 管线](<blender-pipeline.md>)。
- Public application id 为 `com.dynamiteuniverse.game`，Internal 为 `com.dynamiteuniverse.game.internaltest`。签名凭据只放被忽略的 Godot credentials 或环境变量；曾暴露的 debug keystore 绝不能用于产品签名。
- 当前无项目级 LICENSE，原创代码和资产保留所有权利。Space Grotesk、历史 Orbitron、Godot/.NET 及参考代码许可见 [第三方通知](<../THIRD_PARTY_NOTICES.md>)。
- 外层逆向资料、原版 APK、工具环境和录像不属于发行内容；本地设备/研究环境参数不再放进当前交接，旧环境记载见 [原文快照](<archive/2026-10-07-cleanup/handoff.before.md>)。
