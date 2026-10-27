# 第三轮模块化：公共视觉、页面组件与画布模型

> 归档说明（2026-10-26）：下文是原日期的阶段记录/未实施提案，不作为当前能力、待办、验收或排期。当前见 [交接](<../../handoff.md>) 与 [模块地图](<../../module-map.md>)。正文中的旧路径、计数和未完成项按当时背景解释；本次仅添加标识并修正 Markdown 相对链接。整理前字节原文见 [快照清单](<../2026-10-26-cleanup/manifest.json>)。

2026-10-25。在前两轮及现有未提交功能改动上继续拆分。新的 editor 产品想法暂不实施或排期，
本轮没有增加产品流程或调整玩法。当前职责和后续入口见 [模块地图](<../../module-map.md>)。

## 已完成的边界

| 模块 | 拥有的内容 | 仍由调用方负责 |
| --- | --- | --- |
| `presentation/` | 字体/基础 UI 几何、CutPanel、封面占位、音符外观/遮罩/视图、舞台/背景/命中 bloom | 资源文件、应用设置、判定、会话与导航 |
| `shared/IO/SettingsFilePersistence` | 原阶段式 UTF-8 设置文件发布与失败清理 | user:// 路径解析、保存触发时机；兼容保留原 namespace |
| `GameplayHud` | HUD 控件、文字缓存、判定闪字 | 分数与 ACC 计算、每帧顺序、暂停、结果与保存 |
| `SongPreviewPlayer` | 试听 loader、stream、播放/暂停/停止、时长读数 | generation/pack/chart/展开状态校验、卡片和选择 |
| `CalibrationBeatSource` | WAV 合成、播放器、零偏移 SongClock、循环位置展开 | 采样/统计/建议偏移/应用与页面动画 |
| 三组 `*Widgets` | 原内部控件的绘制、输入、信号、局部动画 | 全局动效/落速经 provider 读取，页面保存与导航 |
| editor 页面组件 | Start、HUD、Settings、ContextMenu，以及共用页面 UI 构造 | 页流、文件对话框、文档会话、预览、菜单命令路由 |
| `CanvasProjection` | 文档到画布预览的映射，稳定的 List 容器 | 选择恢复、Godot 坐标、输入、显示 |
| `CanvasPlacementDraft` | 起拖阈值、宽度草稿、Hold 头/尾阶段、一次命令提交 | 吸附与精确时间换算、controller、选择与 redraw |

editor 的 14 个 client Compile Link 全部移除，两端通过 ProjectReference 使用公共视觉程序集。
presentation 只依赖 shared 和 Godot；不能反向依赖 client/editor 或读取其全局设置。
原 namespace、Godot 资源路径与已有入口场景保持；两端资源目录仍独立。

编辑 HUD 当前只读文档/画布，不改变文档；Settings 组件持有注入的设置与画布接点，沿用
离散改动立即保存、滑条松手保存的节奏。它们没有引用 shell，但仍存在明确的画布适配依赖。

## 行为不变量与报告

本轮没有有意的产品行为变化，自动对比未发现恶性或非预期的功能变化。
此前“未保存草稿新增差分丢失外部资源”的修复属于 [第二轮](<2026-10-25-modularization-phase2.md>)，本轮未再改保存合同。

- 判定窗口、帧执行顺序、Hold/Mixer 时间重放、固定几何、镜像、成绩身份与 Auto 不保存保持。
- HUD 的分数/ACC 仍由宿主计算；暂停输入守卫、节点顺序和原 delta 保持。
- 画布投影 `Center` **仍是左缘**，文档使用中心；精确 BarTime 和 BPM 转秒公式保持。
  曲线/judge 元数据、BPM 排序与首项锁定、16–600s 时长边界保持。预览修改不直接写文档。
- 新建拖拽保留 3px 起拖阈值与原 `BaseWidth + Abs(delta) * delta` 宽度算法；Hold 两阶段
  合成一次 undo，cancel 不产生历史。未借本轮重构修改这个看起来特殊的宽度公式。
- 试听 Stop/Play/StreamPaused 和到尾停止顺序保持；到尾判断保留原正向比较，非有限值也未改变分支语义。
- 校准仍为 44100Hz、16bit、单声道、2s 循环。88200 个循环采样帧之外的 **1 个静音保护帧**保留，
  `LoopEnd=88200`，避免此前 Android 循环边界越界。校准时钟 user offset 仍为 0。

## 验证证据

`python -B tools/verify.py --no-restore` 已通过：客户端/编辑器构建 0 warning / 0 error，
核心与编辑器回归通过，37 项发布测试通过，结构门禁与 diff 检查通过。
新增 CanvasProjection/CanvasPlacementDraft 回归覆盖分数时间、变 BPM、左右缘转换、投影隔离、
容器复用、undo 后重建、起拖阈值、Hold 尾钳制、一次 undo 与取消不入历史。

隔离 headless Godot 对比使用第三轮开始前源码快照；不加载玩家设置或写成绩，不开可见窗口。

| 场景 | 覆盖 | 结果 |
| --- | --- | --- |
| 游玩 | golden/stress × auto/manual × mirror on/off；每组 480 帧 | 8 组 × 480 × 判定/节点/HUD 三类，共 **11520 条记录逐字节相同** |
| editor | Start/Settings/Create/Pack/Edit 往返、Tap/侧轨 Drag/两阶段 Hold、undo/redo/cancel、右键菜单、输入锁 | Full/Reduced/Off 各 220 帧，捕获状态相同；音符数实际 7→8→9→10，撤销9/重做10，取消仍10 |
| pause | 连按、入场忙碌守卫、退出动画、失焦清输入 | 三档各 220 帧，捕获状态相同 |
| song | 展开→立刻收起→重开、暂停/恢复、切难度、停止/重启、到尾停止 | 三档各 220 帧，控制状态和 UI 相同，另断言实际进入播放与暂停 |
| calibration | 热身、重复击打只更新读数、stray、8 样本完成、冻结、retry | 三档各 220 帧，音源哈希、时钟、统计与 UI 相同 |
| settings | 四 tab、独立控件鼠标/触摸、滑条边界、分段信号、运行中切换动效 provider | 三档各 220 帧，信号序列和捕获状态相同 |

UI 共 **3300 帧**。捕获控件层级、位置/尺寸、可见性、透明度、文字、字体/边框颜色和场景状态；
只归一化提取前后的控件类型名称。未做 GPU 像素、实际声音听感或触控设备验收。

试听测试中真实音频线程和后台解码完成时机并不随 fixed-fps 确定；最初观察到进度条和两帧加载态差异。
最终 probe 保留真实 worker 和应用方法，将异步结果的消费固定到帧边界，并传入相同 elapsed 核对进度。
因此该结果证明相同输入和完成顺序下的行为等价，**不声称真实 I/O 时延逐帧相同**。
校准使用 SongClock 既有固定时钟入口，日志中的“无音频设备”警告是预期 headless 回退。

早期 probe 在前后版本均偶发退出时 AudioStreamWAV/AudioStreamPlaybackWAV 的 ObjectDB 泄漏警告。
最终测试释放场景/缓存并在退出前等待音频混音线程清理后通过；未为此修改产品生命周期。
这不能作为真机长期资源使用已验收的证据。

校准 WAV 数据 176402 字节，SHA-256：
`846a57ac5e5d8309874ee05136d7a59ae2eac37bab25da193f3cb10a00e3704d`。
工作区证据位于 `.tmp/modularize2/phase3-*`、`.tmp/modularize3/phase3-*`；完整文件哈希与比较结果为
`.tmp/modularize3/phase3-equivalence.json`，SHA-256：
`3f3718ef70dc41f105db05c38095018fdf85ae7d8dad2417b46facd3d6a979b5`。
这些快照与探针是被忽略的本地迁移证据，不是已接入 CI 的运行时测试入口。

两端 Windows ExportRelease 的 `dotnet publish` 成功；输出和 deps.json 均包含
`DynamiteUniverse.Presentation.dll`，editor 还包含 `ChartEditor.Core.dll`。
APK 必需程序集策略及发布测试已同步。**没有导出 APK、完整 Godot 桌面包或进行真机验收。**

## 维护门禁与剩余耦合

结构门禁检查八个项目和 211 个源码文件：editor 禁止重新链接 client 源码，公共视觉禁止引用应用状态，
HUD/音源/控件/页面禁止回读所属宿主或全局会话。原大类预算仅收紧，新组件有独立预算。

AuthoringCanvas 仍有 2402 行，CreatePageOverlay 1330 行；绘制/命中/框选/路径交互、Create 表单、
卡片动画以及部分采样/暂停编排尚未完全隔离。下一步职责和保护条件见模块地图。
本轮完成的是上述对象与依赖边界，不把“所有文件都拆过”或“所有上帝类已消除”作为结论。
