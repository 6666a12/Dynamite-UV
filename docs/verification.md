# 验证状态与操作

> 维护原则：**执行日期、对象、条件、结果、未覆盖范围**一起写。静态核对、自动检查、导出验包、用户验收是四种证据，不能合并为“全通过”。
> 当前功能与待办以 [交接](<handoff.md>) 为准；这里统一维护最近验证记录、测试包身份与操作命令，避免各 README 重复漂移。

## 最近验证记录

| 日期 / 来源 | 已有结果 | 不能证明什么 |
| --- | --- | --- |
| 2026-11-03 空草稿放置与鼠标松开修复 | 修复新建空草稿被预览编码/解码/RuntimeAdapter 的计分 Note 校验阻断：仅编辑预览显式 `requireMainJudgement:false`，默认 strict 行为不变，并有默认拒绝回归。Godot 根控制器补收全局左键松开，避免嵌入窗口的放置/路径提交丢失；空草稿与完整 smoke 分窗口隔离。统一 `verify.py --no-restore` 再次全部通过：266 个源码文件、三端 0 warning / 0 error、核心与编辑器回归、25 项 Shell headless、37 项发布测试和 diff 检查。真实 `--smoke-empty` 通过空舞台加载、首次原生点击放置和首次保存；真实 `--smoke` 通过 Note 选择、Q/E/W/S、播放、Ctrl+S、Hold 放置、路径双击/拖动、Dock 与中英文 | 自动窗口验证不是用户最终手感/视觉验收；普通 Note 拖动/调宽、Pan、其他 DPI、长谱性能、音乐听感和合并桌面包仍未覆盖。截图在 `builds/editor-shell-empty-smoke/empty-first-note.png` 与 `builds/editor-shell-smoke/05-authoring.png` |
| 2026-11-03 制谱器新建/差分与舞台手势 | 接入新建包/添加差分表单、差分切换、七类 Note 点击放置/拖动加宽、路径双击插点/拖动头与节点、节点曲线和删除；全部修改复用文档命令。Godot 4.7.1 Mono `--headless --path editor --import` 成功并生成新脚本 UID。统一 `verify.py --no-restore` 通过：结构门禁 266 个源码文件，client/editor/shell 均 0 warning / 0 error，核心与编辑器回归、25 项 Shell headless（含双差分 strict 写包重读）、37 项发布测试和 diff 检查 | 本轮 Windows smoke 在原有 Note 点击步骤失败；截图显示系统处于锁屏界面，原生输入未进入编辑器，因此新建表单的原生鼠标和新增放置/路径实机手势均未验收，不能沿用 11-02 的窗口通过记录替代。普通 Note 拖动/调宽、Pan、真实波形、完整手动 Dock、其他 DPI、长谱性能、音乐听感和合并桌面包仍未覆盖 |
| 2026-11-02 制谱器 Avalonia + Godot 主线 | 新增 `editor/shell` Avalonia/Dock/Ursa/Suki 外壳与现有 Godot 原生舞台 IPC；接入 strict v2 打开、保存/另存为、真实 Note 选择/差速/删除/撤销重做、中英文和 Q/E、W/S。统一 `verify.py --no-restore` 通过：结构门禁 264 个源码文件，client/editor/shell 均 0 warning / 0 error，核心与编辑器回归、16 项 Shell headless 文件/命令/UI 检查、37 项发布测试和 diff 检查。Windows 1.25× DPI smoke 通过真实包/WAV 加载、Note 点击、倍率/播放快捷键、Ctrl+S 重读验证、Dock 浮动还原和中文指南；截图在 `builds/editor-shell-smoke/` | 未覆盖新建/加差分、放置/移动/路径手势、真实波形、完整手动 Dock、其他 DPI、长谱性能与音乐听感；没有合并桌面安装包。自动播放到时钟/状态，不作为听感验收；用户最终 UI 验收仍待进行 |
| 2026-11-02 Note 差速、缓动与音频格式扩展 | v2 新增可选父 Note `scrollSpeedMultiplier`（缺省 1 且不改旧 Digest），Scroll/路径新增六种 Quad/Cubic/Sine 缓动并覆盖跨帧接触求根；编辑命令、strict 编解码、运行时视觉与差速生成窗口已接入。默认写包 Probe 支持 WAV/MP3/FLAC/Ogg Vorbis，客户端以托管解码支持原生 FLAC。`python -B tools/verify.py --no-restore` 通过：结构门禁 247 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查；清理测试警告后编辑器回归再次通过。Schema/golden pack 校验通过，golden Digest 保持 `5f3cafcfa02097b555c860e0da9ca8e8a55ce428df8d69fcb263e080a7249a1f` | 未启动 Godot 做视觉/听感验收，未覆盖 Android、真实设备性能或制谱 UI；FLAC 客户端限常见 8/16/24-bit、1–2 声道并有输入/PCM 预算，24-bit 转 16-bit。Opus/M4A/AAC 仍不支持 |
| 2026-10-30 制谱器旧 UI 移除 | 旧页面、画布、菜单、输入和设置宿主已删除，入口仅保留空白 `StandaloneEditorMain`；文档/命令/会话/投影等纯核心保留，新增 `EditorGameplayDigest` 及与 shared 结果一致的回归。`python -B tools/verify.py --no-restore` 通过：结构门禁 225 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查。Godot 4.7.1 Mono `--headless --path editor --quit` 实际加载空白场景成功，无脚本或资源错误 | 当前没有制谱 UI 可供功能或视觉验收；headless 只证明场景可加载，不证明窗口观感、新 UI 交互、音频或设备行为。旧核心能力仍由自动回归覆盖，后续需重新接线 |
| 2026-10-30 顶部判定栏居中 | `TopHudOffsetY` 从 16px 调整为 8px，顶部判定栏由 `y=28..68` 上移到 `y=20..60`，中心 `y=40` 位于屏幕顶部与 `y=80` 蓝线正中；暂停按钮及其触控区域同步上移 8px，舞台与下方 HUD 不变。`python -B tools/verify.py --no-restore` 通过：结构门禁、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查 | 本次为确定坐标的小幅位移，未重新启动 Godot 或捕获新截图；未覆盖 Android 和真实暂停触控，最终观感仍由用户确认 |
| 2026-10-30 游玩页面纯位移 | 客户端舞台、音符、特效与下方 HUD 下移 40px，顶部统计和暂停按钮下移 16px；Center 输入阈值同步为 `y>811`，Side 纵向反查与暂停触控区域同步偏移。共享基准、编辑器、校准页、元素尺寸、落速和侧轨距离比不变；背景增加顶部覆盖以避免偏移后露出透明条带。最终 `python -B tools/verify.py --no-restore` 通过：结构门禁 254 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查 | Godot 4.7.1 Mono / Forward Mobile 以 Hold Recovery Test 录制 1920×1080@60 并检查 `tmp_shots/gameplay-layout40-preview.png`：判定线 y=901、底部信息板余量 52px、Hold/特效随舞台移动，顶部无透明条带且底部无裁切。未做 Android、真实触控/暂停点击或用户最终视觉验收；自动回归不直接覆盖新的客户端屏幕偏移 |
| 2026-10-29 命中粒子回收 | `GameplayHitParticles._Process` 推进 `_age`，寿命加尾段结束后停止处理；普通实例仍由 `GameplayEffects.RecycleExpired` 重置归池，独立预热实例到期 `QueueFree`。不改粒子数量、材质、寿命参数与持续粒子。`python -B tools/verify.py --no-restore` 通过：结构检查、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查 | 未启动 Godot；现有纯逻辑回归不覆盖 GPU 粒子节点的实际归池与预热释放，运行时及视觉仍待验证 |
| 2026-10-29 审查高优先级修复 | GameSession 缓存读取与 GameplayMain 入口按当前模式 Preset 决定复用；ScoreFilePersistence 接管文件恢复，读取失败不清空已有内存成绩，首次保存前复制原始字节到 `scores.json.recovery-<guid>`，复制失败则不发布新存档；同目录写包回滚逐项尝试，聚合发布与回滚错误，保留未恢复备份。新增成绩文件正常往返/损坏恢复/重复保存/备份失败回归，以及目录阻塞文件恢复的写包故障注入，证明仍恢复其他文件并保留原错误和备份。`python -B tools/verify.py --no-restore` 通过：结构门禁、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试、diff 检查 | 未启动 Godot、导出或做设备验收；模式缓存链路为源码检查及客户端编译，尚未覆盖真实 UI 的 Standard→Hardcore→Standard 操作；没有改变成绩模式隔离，也未修复本次审查的中低优先级项目 |
| 2026-10-29 首页 Home v2 + Mk VI | 首页按 Penpot「Game — Home v2 · 01 Compact」（MCP 读取几何/颜色/字距；此前已在板内新增“段位模式 / 联机大厅”并修正主按钮文字层级，本轮接入未再改 Penpot）重做：`Main.cs` 只装配与入场，布局在 `ui/home/`（`HomePalette`、`HomeBrandBlock`、`HomeActions`、`HomeActionTile`、`HomeUtilityRow`、`HomeReticle`）。`detonation-orrery-mk6.glb`（3756 三角面、14 材质、单个 4 秒 clip）替代 Mk II：FOV 32 / 18° 仰角 / 22° 方位角 / margin 0.90，1000×920 子视口；Environment glow 关闭，近/远双层 canvas 弥散光默认 `u_glow=1.6` 且开启；Full 原速循环、Reduced 0.45 倍速与 0.06 脉动、Off 停帧且弥散能量固定 1.0；删除 Mk II 的 8 秒归一化、轨道脉冲灯与材质名调优。新增“段位模式 / 联机大厅”禁用底板，保留谱面工坊禁用与游玩/设置真实导航，顶部 CREDITS / LOGIN 置灰无导航；三处注释小字未添加，右下版本串仍走 `Main.BuildDescription()`。发布合同同步：`export_presets.cfg` 与 `apk-policy.json` 用 mk6 取代 mk2 并把 `shaders/home_diffuse.gdshader` 列为必需，`asset-provenance.json` 记录真实哈希（模型 `877ceeff…e8774`，生成器 `design/home-orrery-v6/build_orrery_mk6.py` `a42b8791…f279e`），发布测试改为断言 mk6 必需且 mk2 不再必需。Godot 4.7.1 Mono `--headless --import` 正式生成 `.glb.import`、着色器与 6 个新 `.cs` 的 `.uid`。最终 `python -B tools/verify.py --no-restore` 通过：结构门禁 252 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 `git diff --check` | 主代理用 Movie Maker 在 1920×1080@60 离屏窗口录制并抽帧：布局与 Penpot 主按钮底板 bbox 完全一致（96,616–745,723），入场错峰可见，Full/Reduced/Off 第 60 帧与第 120 帧差异分别为 52.71 / 24.42 / 0.00（Off 完全静帧）；弥散开/关 A/B 在模型区差均值 0.66（最大约 0.17 的单通道 8-bit 亮度）；用真实点击核对了游玩→选曲、设置两处导航；后续启动日志与进程核对确认本机 F5 使用 Vulkan 1.4.329 + Forward Mobile，Compatibility 仅用于独立预览。未覆盖：Android/APK 导出与设备、低端帧率、hover/按下手感、CREDITS/LOGIN 与三个禁用入口的触控端到端、Penpot 稿本身；Forward Mobile 下 canvas 弥散比独立预览（gl_compatibility）更收敛，最终视觉与是否加压待用户判断 |
| 2026-10-28 TRACK COMPLETE 普通完成字标 | 新增第七类 `TrackComplete`：不满足 AP、FC、ONE GOOD、ONE MISS、DOUBLE ONE 且非 BLEED 失败的有效正常成绩统一显示 TRACK COMPLETE；沿用 80% 字标、弥散光和 Full 1.55s / Reduced 0.26s / Off 无前奏。无效空成绩仍走无字标保护路径，普通完成不新增 AP/FC 徽章。clean-room 字形生成器补充 K 字形与新字标，运行时 PNG、导出白名单和来源清单由打包脚本更新。`python -B tools/verify.py --no-restore` 通过（两端构建、核心/编辑器回归、37 项发布测试及 diff 检查） | 主代理用 Godot 4.7.1 Mono 正式导入新 `.ctex`，再以受控验证入口指定 Hold Recovery Test、AUTO=false 完成真实窗口录像 `tmp_shots/result-track-complete-client.mp4`；日志 `P=0 / M=6 / outcome=TrackComplete`，无资源或着色器错误。抽帧确认字标、斜纹、屏风和结算页。未重新覆盖其他六类、Android、公开导出或用户最终视觉验收 |
| 2026-10-28 SURGE 结果字标前奏与结束双锁存 | 正常结束改为谱末 +2 秒与音频自然结束**两者取晚**：新增 `GameplayEndCondition`（两个单调锁存 + 音频结束后按真实帧 delta 补时，单帧上限 0.5s；固定帧/回退时钟只走谱末路径），`SongPlayback.TracksNaturalEnd` 区分可观测性，BLEED 耗尽改为当帧 `Pause()` + `ShowResults(stageFailure:true)`。屏风前新增六类结果字标前奏：`ResultFlow`（交付顺序与只一次闸门）、`ResultOutcomePrelude`（自带 `_Process`，1280×320 @ (320,380)、斜纹区 translate(192,108) scale(.80)、`wordmark_diffuse.gdshader`）、纯时钟 `ResultPreludeTimeline`（Full 1.55s / 普通 0.68s / Reduced 0.26s / Off 0 帧）、纯分类器 `ResultOutcome`、防穿透 `ResultInputGuard`（120ms）；`ResultScreen` 徽章改用同一分类器并在每次 `Present` 重置 covered/unlock。六张 1600×400 PNG（`client/assets/results/outcomes/`）与着色器由新增 `tools/grade-assets/build_runtime_outcomes.py` 复制/写 `.import`/加白名单/写 provenance，`build_runtime_results.py` 显式跳过该子树（其 29 条 `generatorSha256` 已同步）。最终统一 `verify.py --no-restore` 通过：结构门禁 246 个 C# 文件、两端 0 warning / 0 error、核心回归（新增 `[22]` 双锁存、`[23]` 六类分类与徽章、`[24]` 前奏三档/跳过/防穿透/只一次，原 ResultReveal 用例不变）、编辑器回归、37 项发布测试及 `git diff --check` | 主代理已用 Godot 4.7.1 Mono 实际导入六张字标 `.ctex` 并编译着色器；Hold Recovery Test 全程 AUTO 正常 AP（Full/Reduced/Off 日志均为 `P=6 / M=0 / outcome=AllPerfect`），Tablear BLEED 耗尽触发 `StageFailure`，另有普通多 Miss `outcome=None` 路径。逐段查看 `tmp_shots/result-{ap,failure,reduced,off}-client.mp4` 与对应日志，确认 80% 字标/斜纹、弥散光、屏风衔接及 Off 无前奏帧；临时设置已通过 UI 恢复为 Full / BLEED 开 / AUTO 关。仍未覆盖 Android 触控、公开 APK 导出验包、坏音频包、音效、专用失败页与血条；最终视觉验收待用户 |
| 2026-10-27 结果前奏交接文档 | 新增 `docs/result-outcomes.md` 并接入文档导航、handoff 与结算规格；区分当前正常结束取早、未来取晚方案及静态候选。本次 4 份相关文档本地链接检查 0 缺失，最新 expressive 对照图与生成器存在，`git diff --check` 通过 | 仅文档整理，没有实施正常结束取晚、前奏、音效或客户端资产接入；未重跑 C#/发布测试、未启动 Godot，候选视觉尚未最终定稿。全量链接扫描另发现模块化旧审查中的 31 个历史源码链接失效，本次未扩大范围处理 |
| 2026-10-27 BLEED 数值与耗尽结算 | 新增 `shared/Judge/BleedHealth.cs`（Standard/Hardcore 数值、100 封顶、0 钳制、耗尽幂等），`GameplayMain` 在 `ApplyUnit` 同步血量、`_Process` 判定与 Mixer 收尾后检查耗尽，先 `_playback.Pause()` 再复用 `ShowResults`，自然完成仍保留尾奏；核心回归新增 `[21] BLEED health rules and depletion`；Hold 中途断触/头 Miss 的 shared 批量结算另在客户端两处补齐血量。最终统一 `verify.py --no-restore` 通过：结构门禁 237 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 `git diff --check` | 未启动 Godot、导出或设备验收；耗尽暂停调用与正常尾奏路径经代码审核及构建回归确认，实际听感未测；未设计 Bleed 血条 UI，耗尽专用结算仍是复用普通结算的占位；Mine 不参与扣血、Auto 不写成绩等既有行为只按调用点静态确认 |
| 2026-10-27 全量功能族目录整理 | client UI/game、editor、chart-editor-core 与两套测试完成按功能族分组；场景/程序入口保留，UID、core-test 链接、策略和活跃文档同步。最终统一 `verify.py --no-restore` 通过：235 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 `git diff --check` | 未启动 Godot、导出或设备验收；构建与静态路径检查不证明场景实际加载、视觉或触控体验 |
| 2026-10-27 手势/视口/设置拆分与目录整理 | 目录移动后统一 `verify.py --no-restore` 通过：结构门禁覆盖 235 个 C# 文件，两端 0 warning / 0 error，核心与编辑器回归、37 项发布测试及 `git diff --check` 通过；场景/自动加载、UID、core-test 链接和策略路径同步更新，新增手势预览、滚动与设置 codec 回归 | 未启动 Godot、导出或设备验收；场景实际加载、触控手感、动态行高命中、AudioServer 和视觉效果未覆盖；固定步距命中疑点未宣称复现或修复 |
| 2026-10-27 命中与 Track Handoff 拆分 | 最终代码运行统一 `verify.py --no-restore` 通过：结构门禁覆盖 228 个 C# 文件，两端 0 warning / 0 error，核心与编辑器回归、37 项发布测试及 `git diff --check` 通过；新增命中优先级/边缘与 Full/Reduced/Off 相位、放电完成和重置回归 | 未启动 Godot、导出或设备验收；纯命中适配和相位测试不证明真实输入派发、节点层级、GPU 合成、coverage 淡出或放电尾巴的实际视觉效果 |
| 2026-10-27 选择与创建请求拆分 | 最终代码运行统一 `verify.py --no-restore` 通过：结构门禁覆盖 223 个 C# 文件，两端 0 warning / 0 error，核心与编辑器回归、37 项发布测试及 `git diff --check` 通过；新增选择状态与创建请求核心回归，保留 Shift 主选择语义和 AddDifficulty 不查询外部资源的行为 | 未启动 Godot、导出或设备验收；纯核心测试不证明真实 Godot 输入派发、命中几何、焦点或视觉效果 |
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
本次整理的迁移清单、原文 SHA-256 与归档范围见 [2026-10-26 归档说明](<archive/2026-10-26-cleanup/README.md>)。
