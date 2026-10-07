# Dynamite Universe 交接文档

> 最后更新：2026-09-09。本文只记录当前有效实现、固定决策、待办和验证流程。
> 2026-08-18 起正式项目名为 Dynamite Universe；冻结的 `dynamite-uv-*` / `gameplay-v1` wire 标签继续原样兼容。
> 当前 legacy 运行时与行为规格见 `gameplay-spec.md`；运行时职责边界见
> `runtime-architecture.md`；正式目标格式见 `chart-format-v2.md`；原版判定证据见
> `original-judgement-analysis.md`，布局数值来源见 `video-geometry-analysis.md` 和
> `pixel-calibration.md`，UI 样式稿见 `ui-mock/index.html`。

## 1. 项目约束

- 项目是 Dynamix 风格的 clean-room 社区版；Public APK 不得包含任何原版素材或原版谱面。
- 整个 `client/testdata/` 都是内部开发区，可在制谱器完成前供 Internal Testdata APK 使用，
  但始终被 Git 忽略，且绝不可进入 Public APK。双模式导出和强制检查见 `releasing.md`。
- 客户端使用固定 1920×1080 设计坐标，Godot stretch 为 `canvas_items` keep；比例不符时
  留黑边。不要引入自适应游玩布局。
- 游戏和制谱器是两个独立的 Godot 4.7.1 .NET 项目：游戏在 `client/`，DynaMaker UV
  在 `editor/`。二者共享版本化核心代码，但不互相启动，也不共享导出 preset。制谱器只编辑
  目标为 strict v2 包；当前重建 shell 仍只保留内存草稿，未接入导入/导出；
  `docs/ui-mock/editor.html` 仅保留为早期视觉/交互参考。
- 默认使用中文直接交流。普通修改完成后不启动游戏代替用户目测；明确需要截图/录屏时才
  执行本文 §6 流程。

## 2. 当前实现

### 2.1 应用闭环

- 主菜单：游玩和设置入口可用；“谱面工坊”禁用并预留给未来服务器谱面库，不启动本地制谱器。
- 选曲（`SongSelect.cs`，设计稿「Game — Song Select v2」）：左列 QUICK SETTINGS（MODE 两段
  互斥 STANDARD/HARDCORE + BLEED / MIRROR / AUTO 三个同规格开关 + 「全部设置」入口），右列
  曲库（`SONG LIBRARY · N CHARTS` 表头 + 搜索框 + 筛选占位 + 点击行就地展开的详情卡与预览
  试听），底部条循环切换可用难度、显示最佳成绩并进入游玩。几何与交互口径见
  `docs/editor-ui-design.md` §2.1。
- 游玩：三轨输入、鼠标/多点触摸、Auto（`settings.json` 的 `autoEnabled`，不写成绩、
  忽略全部游玩输入、结果严格全 Prefect）、
  谱面左右镜像（`mirrorEnabled`，见 §2.2）、Esc/按钮暂停、重开和返回选曲。
- 结算 v2：双屏风合拢 600ms / 停顿 320ms / 拉开 480ms；v5 表面电流 Ω/S/A/B/C
  动画先入场，再浮现分数、CLEAR、AP/FC、Max Combo、判定分布与纪录增量，完整约 2650ms。
  支持点击跳过、Reduced/Off、返回/下一首/再来一次；实机观感待用户验收。
- 设置：移动端风格三 tab 布局（GAMEPLAY/AUDIO/DISPLAY，设计稿在 Penpot「Game UI」页）；判定偏移、落速、打击特效开关、Music/Hit/UI 音量和 Full/Reduced/Off UI 动效持久化到 `user://settings.json`。
- 成绩：按 `packId:diff` 保存到 `user://scores.json`；Auto 演示不写成绩。

跨场景状态由 `GameSession` 保存。直接启动 `gameplay.tscn` 且未选择曲目时，仅编辑器和
Internal Testdata 构建使用开发谱并默认开启 Auto；Public 和未分类导出返回选曲页。

### 2.2 谱面与判定

- `ChartPack` 在编辑器/Internal Testdata 构建先扫描 `res://testdata/packs`，再扫描
  `user://charts`；同 id 的玩家包覆盖开发包。Public 和未分类导出只扫描 `user://charts`。
- 时间线存在时，loader 用 `BarTimeToSeconds` 重算命中秒；空时间线使用
  `Baked_Second`。
- Position 是条的左缘，命中范围为 `[P,P+W]`，渲染中心使用 `P+W/2`。
- Type 权威语义：1 Tap、2 Drag、3/4 Hold、5 EX-Tap、6/7 Mixer、8 Mine、9 BarLine。
- T2 是接触判定；T5 与原版一致：同窗（窗口倍率 1.0）+ 二值判定（窗内一律 Prefect、出窗
  即输入 Miss，无 GR/GD 档），并与其他类型一样参与同帧时间锁；T9 只渲染，不计分和 Combo。
- 游玩模式 `GameSettings.GameplayMode`：**Standard**（缺省）按难度取预设；**Hardcore**
  对任何难度强制 Hardcore 实例（Hard 窗口 ×0.5 = 31.25/56.25/81.25/125ms，Holding 宽限
  沿用 Hard 值）。选曲页 MODE 只在这两档间二选一。
- `GameSettings.BleedEnabled`（选曲页 BLEED 开关）是游玩修饰而非档位：Standard 下自由
  开关，Hardcore 下强制开启并锁定（压暗 + 方块停右侧 60% + 挂锁，副文案换成
  `HARDCORE 下强制开启`）；锁定期**不改写**持久化值，切回 Standard 恢复用户此前的选择。
  Bleed 机制本身仍是预留，没有任何判定运行逻辑，见 `docs/gameplay-spec.md`「游玩模式」。
- `GameSettings.MirrorEnabled`（选曲页 MIRROR 开关）开启后游玩画面左右镜像：侧轨
  Left↔Right 互换（含输入触点归属、判定线侧轨、note 视觉与粒子朝向），中轨水平坐标关于
  面板中轴反射、宽度不变。中轴取 **2.5**（中轨 note 中心的实际范围是 `[0,5]`，见
  `GameplayStageGeometry.MirrorAxis`；屏幕 `x=963`）——不是 2.0，那是 `[0,4]` 左缘带的
  中点 `x=826.4`，会把整个中轨镜像左移 136.6px。只改屏幕映射，不改任何判定数据；映射本体是
  `GameplayStageGeometry.MirroredDisplay` / `MirroredTrack`（自反，因此屏幕→谱面的输入
  反查复用同一函数），客户端 `GameplayMirror` 只负责读设置。**判定侧的逆映射只允许走
  `GameplayMain.ChartTouchOf` / `ChartPointOf`**——它们内部先经 `DisplayPositionOf`
  （屏幕→显示空间，**不**镜像）再经 `GameplayMirror.Display`（显示→谱面）施加唯一一次镜像；
  在任何别处再镜像一次都会因自反而抵消，判定就会退回显示坐标。core-tests
  `TestGameplayMirrorMapping` 覆盖其几何不变量与输入逆映射。
- 判定窗口按难度取原版 JudgeSettings 实例：casual→Casual、normal→Normal、
  hard 及以上（含 mega/giga/tech/自定义）→Hard、tutorial→Tutorial（= Casual 数值）。
  按键扫描窗、Drag 接触窗、Hold 断触宽限和 EX 倍率都从该实例算出，客户端不再硬编码窗口。
- SyncNote 非零只给 T1 Tap 画金色多押描边。
- 普通窗口固定按 StandardBPM=150 换算；Hold 断触宽限按当前 BPM 计算并钳制到
  120–200，该间隔不生成判定点。
- 触摸由全局 `_Input` 采集并按触点 id 保存轨道、Position 和 phase，已关闭触摸模拟鼠标；
  同帧 Early/Exact 锁定最早目标时刻（与事件到达顺序无关）并允许完全同刻多押，
  Late 不检查也不武装该锁；EX-Tap（T5）与 Tap 一致参与该锁（RE 已确证 Burst 三个克隆
  体逐指令一致）。
- 普通 note 使用实际 `[P,P+W]` 与 `CommunityTouchWidth=0.40`（每侧扩 `0.20`，按 Mixer 试玩反馈从 `0.30` 提高）；每颗 Note 独立扫描触点，同刻空间重叠 Note 可共享同一触点，触点不会被消费；
  Mine 使用精确范围，不扩边。
- Hold 已实现条体左右缘插值和动态断触宽限；头部与每个实际路径节点均为完整主判定。
  接头后头部的位置和宽度随当前路径更新；头部越线后 Late 窗仍可正常接起，Body 线外部分持续裁剪。头 Miss 会批量 Miss 全部
  剩余节点；提前释放确认断开时，未结算中间节点 Miss，尾按实际 release 时刻评级；
  宽限内由原触点或其他覆盖触点接回会清除断触，持有过尾始终为 Prefect。正式失败后
  整条 Hold 的头、Body、描边和尾继续下落，Body 继续按判定线裁剪，在 180ms 内同步渐淡后回收；从当前透明度
  开始淡出，暂停时冻结，并禁止后续节点重新生成。
- Mixer Body 始终渲染；当前范围内有同轨有效触点时在线上显示动态头，断开即隐藏，
  重接时按当前位置和宽度恢复。静态头仅作入场提示，到线或提前判定后移除。
  Began/Moved/Stationary 均可随时重接，没有超时或永久 Miss。从 Mixer 头开始每
  `1/8 chart bar` 产生完整主判定，非网格尾不补判，也没有额外命中率尾判。
- Note 的下穿、回收和判定反馈按类型分流，当前权威规则见 §3；逻辑 Late 窗不依赖
  Note 本体是否仍然可见。

### 2.3 分数和统计

- Hold 头与每个路径节点、Mixer 头与每个八分点均使用 `100/70/50/0` 权重，并进入
  Combo、P/GR/GD/M、Score、Health、Boost、CLEAR 和理论满分。
- 显示与存档分数为 `round(RawScore/TheoreticalMax×1,000,000)`，上限 1,000,000。
- HUD 实时 ACC 使用当前得分除以已判定单元理论满分并钳制在 0..100%；结算 CLEAR 使用全谱理论满分。
- 评级阈值：Ω≥98、S≥95、A≥90、B≥80，否则 C。
- Health/Boost 已按 `JudgePlan.HeadlineUnitCount` 缩放并钳制，但当前不显示 UI，也不
  触发 GameOver。
- `OriginalJudgeMath` 保留原版 Combo/raw score/CLEAR 数学，不接入社区版 HUD 与存档。
- 判定字中 Prefect 统一显示 `PREFECT`；Great/Good 仍显示 E/L。

## 3. 游玩布局与视觉

所有坐标均为 1920×1080 设计坐标，权威常量位于 `GameplayMain.cs` 顶部。

| 项目 | 当前值/规则 |
| --- | --- |
| Center 判定线 | `y=861` |
| Mixer 装饰条 | `y=632`，`x=675..1244`；不是判定线 |
| Center 左缘映射 | `x=280+273.2·P`；宽度 `W·273.2·0.95` |
| 侧轨判定线 | `x=184/1736` |
| 侧轨命中 y | 左右对称 `y=840−115·(P+W/2)` |
| 侧轨条长度 | 单位 102px，实际 `W·102·0.95`；BarLine 实际 `W·190·0.95` |
| 可见行程 | Center 790px；Side 691px |
| 侧轨距离尺度 | 0.75 |
| Hold | 琥珀渐变头尾 + 半透明填充 + 亮描边；中间节点隐藏，远端不钳制、不缩窄 |
| Mixer | Body 为节点间细连线；控制节点隐藏；仅在当前接上时在线上显示动态头 |
| DropSpeeds | 相邻事件间按 BarTime 线性插值；距离 = 剩余音频秒 × 当前流速 × 玩家落速 × 1026px/s；恒为 150 BPM 基准，不随谱面 BPM 改变 |
| 触摸区域 | 同一触点多轨投影：`y>771` 包含 Center，`x<400` 包含 Left，`x>1520` 包含 Right；均不满足时回退 Center |

HUD 当前布局：顶部为 P/GR/GD/M、ACC、M.COMBO 计数行；暂停按钮位于其下方中央；
判定字在线上方，Combo 在线下安全区；左下为曲名/难度，右下为分数。结算屏风完全闭合后隐藏
舞台、音符和 HUD，结果层先铺不透明背景再拉开；Reduced/Off 直接接管不透明背景。

全部可见 Note 使用 `shaders/note_surface.gdshader` 的程序化切角渐变材质；类型纹理和
左右侧亮度参数已统一。命中反馈为双层结构：12 帧式程序化核心爆发（亮度系数 1.55）+ 加法混合的 GPU 粒子质感层（火花/余烬，参数权威见 `ui-motion.md` §9.5；Hold/Mixer 接触中另有持续粒子层）。Hold 与侧轨
Mixer 的持续效果在 0.28 秒内爬到 0.72 亮度系数，之后只循环纹理，松开立即消失。

| 类型 | 当前视觉生命周期 |
| --- | --- |
| Tap / EX-Tap / Drag | 未判定时越线后 24px 满亮、再用 40px 淡出；命中立即移除本体，只留爆发 |
| Hold | 未判定头部使用普通下穿，Body 在线上裁剪；接头后头部沿线跟随当前路径位置和宽度，暂时断触时可恢复地下穿，宽限内接回即回线；正式 Miss / 确认断开后继续下落和裁剪，同时在 180ms 内渐淡回收 |
| Mixer | 不下穿；静态头到线或提前判定后移除，只在当前接上时显示动态头，位置随滑块、宽度随路径更新；断开隐藏，重接恢复，Body 持续播放 |
| Mine | 触发时显示红色危险爆发；安全到线直接回收 |
| BarLine | 到线立即回收，不下穿、不停留 |

背景和纵深参照线同样为程序化绘制。
音符位置使用固定二维轨道映射，不做透视投影；变速回溯时，已生成 note 即使暂时退出
画面也保留到命中或过期。

## 4. 仍需处理

- 2026-10-03 结算页与转场 v2：按用户要求在 Penpot「Game UI」y=5120 的原六板
  `Game — Result v2 · 01…06` 原位覆盖为屏风合拢、闭合停顿、拉开、评级先落位、
  S/FC 定格、Ω/AP 变体。原电流回收转场已替换，不另存比较版。
  已按用户确认实现合拢 600ms、停顿 320ms、拉开 480ms，再评级、其它信息依次浮现，
  约 2650ms 完整揭晓。
  设计几何及时间线见 `docs/result-v2-design.md`，图片见 `design/result-v2/`。
  **已接入客户端，待用户实机验收**。Full token 已改为 2650ms，Reduced/Off 为 260/0ms。
  判定结束后保留歌曲尾音，成绩身份与 AUTO 不写成绩规则不变。

- 2026-10-02 结算评级视觉资产：用户选定自绘「双刃折返」及 v5 炫光方向，并要求追加
  表面电流。五级 Ω/S/A/B/C 的 512×512 透明静态图与各 30 帧、24fps 入场序列已生成在
  `design/grades/v5_current/delivery/`，包含 ZIP、联排图和同步动画预览。
  S 保留获选 v5 的原始帧，只叠加独立表面电流；其余四级为同方向扩展。
  2026-10-03 随已确认的 Penpot 结算 v2 接入客户端；运行时 PNG/图集位于
  `client/assets/results/grades/`，每次只加载本次评级动画。源脚本、Blender 工程和分层文件
  见 `docs/blender-pipeline.md`，打包脚本为 `tools/grade-assets/build_runtime_results.py`。

- 2026-09（紧急修复）**开镜像后视觉镜像了、判定没镜像**——输入链路上镜像被施加了**两次**，
  两次互相抵消。`TrackPositionOf` 当时已经对中轨套了 `GameplayMirror.DisplayCenter`，而
  `AddProjectedTouches` / `AddProjectedPresses` / `ReplaceMixerSamples` 又把它的结果交给
  `GameplayMirror.ProjectTouch` 再镜像一次；映射自反，等于没镜像，触点留在**显示**坐标，
  而 note bounds 是**谱面**坐标 —— 于是「点在镜像后的显示位置不判、点在镜像前的原位置才判」。
  修法（方案 A / 单一入口）：`TrackPositionOf` 改名 `DisplayPositionOf` 并去掉内部镜像，语义
  固定为「屏幕位置 → 显示空间坐标」；新增 `ChartTouchOf`（屏幕触点 → 判定用 `TouchSample`）与
  `ChartPointOf`（只要坐标）作为**唯一**逆映射入口，镜像只在那里经 `GameplayMirror.Display`
  施加一次，判定侧一律只认谱面坐标。顺带修掉 `RecordHoldRelease` 拿**谱面**轨道去比**屏幕**
  掩码的问题（镜像下谱面 Left 的 Hold 显示在右轨），改为按 `GameplayMirror.DisplayTrack`
  取显示轨道再比、坐标走 `ChartPointOf`。回归证据：注入触点的判定探针（镜像开、点镜像后
  显示位置 → Prefect；点原位置 → 不命中；同一探针在旧代码上结果完全相反）、
  auto=on 镜像 trace 仍严格全 Prefect、core-tests `TestGameplayMirrorMapping` 新增输入
  逆映射断言。
- 2026-09 选曲页 v2 第一轮实机验收修掉三个缺陷（都已复现、修复并回归）：
  1. **MIRROR / AUTO 的 13px 副文案从来没落过字**——`AddQuickSwitch` 只建了 Label，之后
     只有 BLEED 在 `RefreshQuickSettings` 里被赋值。改为把副文案当参数传进
     `AddQuickSwitch`（`BleedSubStandard/Hardcore`、`MirrorSub`、`AutoSub` 常量）。
  2. **展开卡切歌时旧行永远不恢复可见**——`ExpandCard` 只手写
     `_rows[_packIdx].Visible = false`，从没把上一次展开的行放回来，连续切歌会让列表一行行
     消失（实测 `rowVis` 从 `1101111` 累积到 `1000101`）。改为统一走 `ApplySearchFilter()`
     刷新全部行可见性，每步只藏当前行（`1011111 → 1110111 → 1111101`）。
  3. **中轨镜像绕错轴**——`GameplayStageGeometry.MirrorAxis` 原取 2.0，那是 `[0,4]` 左缘带的
     中点（屏幕 `x=826.4`）；中轨 note 中心的实际范围是 `[0,5]`，面板中轴在 **2.5**
     （`x=963`），错轴会把整个中轨镜像左移 136.6px。已改为 2.5，并在 core-tests
     `TestGameplayMirrorMapping` 里把该常量与对称用例（中心 ±2.0 / 宽 3.0 的两条互为镜像）
     钉死。
- 2026-08-22 DynaMaker UV 已固定为 `editor/` 下的独立 Godot 项目，替换受支持的
  Avalonia 编辑器入口。通过 `editor` 项目的 Windows export 或 `--path editor` 启动；游戏
  项目普通启动只进入游戏。
  工具只读写 strict v2，具备内存草稿、staging 保存、Pack/Chart 媒体与元数据、三轨七类
  Note、路径、BPM/Scroll、稳定内部选择 Key、撤销/重做以及 Storyboard 占位工作区。
  Public 和 Internal Android preset 均不包含 DynaMaker UV；旧 Avalonia UI、资源检查器及
  NAudio 编辑器入口已移除。验证命令为 `dotnet build client`、
  `dotnet build editor/DynaMakerUv.Editor.csproj`、`tools/core-tests`、
  `tools/chart-editor-tests` 和 `docs/releasing.md` 中的 release 检查。
  本地 DynaMaker Modified 参考固定为 `third_party/dynamaker-modified-reference` 的
  `99a5a6049f5bc3ee69e5c8cd3a72f4d8c1e99a8d`（`dynamaker-tool/dynamaker-modified`），
  只作交互移植参考，绝不进入导出。

- 2026-08-18 完成首页与游玩 HUD 的第一轮品牌美化：主菜单改为两行 Dynamite Universe wordmark、程序化能量核心和主次明确的 action rail；游玩左下曲目信息改为固定两行切角信息板，长歌名使用 ellipsis。客户端拉丁/数字 UI 字体改为 OFL-1.1 的 Space Grotesk Regular/Bold，中文继续使用系统 CJK fallback。路由、输入、固定舞台和 HUD 数据来源均未改变；效果仍需用户运行后目测。
- 2026-08-17 的旧 Avalonia authoring 记录已由 DynaMaker UV 原生 Godot 项目承接；相关
  交互能力、精确时间与资源事务现以 `editor/scripts/` 和
  `tools/chart-editor-core/` 为准。

- 2026-08-15 已完成第二轮 UI 动效与对应网页预览：Full token 统一为 `90/160/260/420/620/760/960ms`、stagger `60ms`，等待扫描约 `1200ms`；Reduced 为 `70/100/140/180/220/220/260ms`、stagger `0`，关闭方向移动/循环/Echo；Off 即时但先绘制稳定不透明 handoff。`TransitionDirector` 新增 opaque barrier、Track context hold 与 Gameplay reveal progress；Select/Replay 有 focus confirm，独立 `TRACK HANDOFF` 静态显示已解码封面或 clean-room placeholder，Gameplay 舞台/Note/HUD 仅按 alpha 分层揭示，遮罩移除后才启动 playback。原生同步补入三段 Signal Lock、Settings 行 stagger/数值替换、详情卡单次扫描、Result pre-signal scan 与 cyan+pink Echo。legacy optional cover 路径限制在包内，raw 玩家图片增加格式、字节与解码尺寸上限。预览同步同一 token，并保持移动 Relay 卡只含文字。
- 2026-08-15 已完成第一轮 UI 动效：`docs/ui-mock/motion-preview.html` 提供可交互的 Signal Lock、Track Relay 和 Hit Echo 分镜，`docs/ui-motion.md` 固定时长与 Reduced/Off 策略；客户端新增持久 `TransitionDirector`、Full/Reduced/Off 设置、主菜单/选曲入场、选曲详情准备态、Gameplay 播放前 ready gate、暂停和结算内容动画。转场期间统一锁输入，Gameplay 只在遮罩完全揭开后启动 `SongPlayback`，verification 仍绕过普通动画。
- 最新命中爆发亮度和 Note 生命周期分流尚待用户下次运行时目测确认。
- 发布级按钮和音效素材尚未生产；当前 Note 材质与爆发核心层为程序化运行时
  实现，预览 GIF 只作设计参考。
- 曲绘由社区谱师随谱面包提供，不使用 AI 生成；缺失曲绘时使用 clean-room 占位符。
  早期 8 张 AI 原型曲绘已从客户端资源树移除，生成工具不得输出到 `client/assets/`。
- `docs/ui-mock/editor.html` 是已完成的独立网页视觉/交互参考，后续不再向网页端加功能。
- 2026-08-16 制谱器第三版新增独立 Welcome 与 New Project 页面，不再在空谱面场上显示加载弹框。新建页收集 Title/Artist/Pack ID、首张 Chart 的 ID/Difficulty/Level-or-Unrated/Charter、外部 RIFF/WAVE、初始 BPM/Offset/Grid 和可选 Cover；创建后只生成内存草稿，绝不自动插入 Note。内存草稿始终显示 unsaved/`MEMORY DRAFT`，严格 v2 的“至少一个 main judgement”规则保持不变，放置有效 Note 后 `CREATE PACKAGE` 才会把外部资源复制到相邻 staging、确定性写 JSON、strict reopen、验证 WAV 时长和 Gameplay Digest，全部通过后发布目标目录，失败不留半成品。Editor shell 同步升级为原生字号上下 chrome、单一 scrubber、约 360px（紧凑窗口 320px）右侧工作区和细 page rail；Canvas 只保留谱面/轨道/手势反馈，并新增当前轨道低透明场域、切角渐变 Note 与 Hold terminal cap。该流程仅 clean-room 参考 DynaMaker 的“创建页与编辑场面分离”，未复制其代码、素材、文案、坐标或 trade dress。
- 2026-08-16 的旧编辑器视觉记录仅作为 clean-room 交互参考；当前实现以 DynaMaker UV 的 Godot 固定舞台与 Inspector 为准。
- 2026-08-15 已完成一轮保持行为的结构整合：`docs/runtime-architecture.md` 是客户端、shared
  与工具链的职责地图；UI 使用公共固定坐标/导航/切角/标签原语，`GameSession` 使用明确的已提交
  选择和浏览预加载 API，`ChartPack` 已按 catalog、legacy 元数据、v2 adapter、load service 分层，
  `GameplayMain` 的展示层拆至 `GameplayMain.Presentation.cs`，player/clock 由 `SongPlayback` 收束。
  每帧 gameplay 顺序、固定布局数值、legacy/v2 分流、成绩兼容和 Public/Internal 边界均保持不变。
- `tools/chart-editor-core/` 提供与 UI 分离的可编辑文档、稳定 ID、精确 BarTime、命令式
  undo/redo、v2 快照和实时语义校验。当前可打开 clean-room v2 目录包，新增 Tap/Drag/Hold/BPM，
  编辑项目与所选对象数值，并保留暂不支持可视化编辑的 v2 类型。
- shared 已提供确定性 `V2JsonEncoder` 和事务式 `V2PackageWriter`。编辑器 Save/Save As 会在 staging
  中重新 decode、跨文件校验、探测 WAV 时长并计算 Gameplay Digest；非 WAV 保存暂时 fail closed，
  音频试听、波形和 Mixer/Mine/EX-Tap/BarLine/Scroll 的完整交互编辑留待后续。
- Dynamite Universe Chart Format v2 已在 `chart-format-v2.md` 正式冻结，机器 Schema、clean-room
  golden pack 和 Gameplay Digest known-answer vector 位于 `../schemas/chart-format-v2/`。客户端已接入
  v2 loader/validator、Digest 成绩身份和运行时适配；legacy 格式继续作为兼容输入。
- 原版已经确认不消费触点，同一触点可命中多颗重叠 Note；社区版 `OnPress` 已按 Note 独立扫描并允许触点复用。仍需补充端到端输入序列回归和真机多指验证。仍未知：设备 `NSTouchWidth` 真值、BPM 切段同帧
  系统顺序、OnJudged 精确视觉动作和回收帧、模式 MaxHealth、Type dispatch、Buff/EX Boost。
- 真机三指以上事件序列仍需用户实测；OS 焦点丢失/触摸取消还没有独立的触点清理入口。
- 成绩身份仍是 `packId:diff`，因此 Hardcore 与 Standard 的成绩目前会混存同一个键；
  若要区分需要扩展成绩身份（本轮只记录，不处理）。
- 设置页已有 Hit/UI 音量总线，但完整打击音和 UI 音效资产链路尚未完成。

## 5. 目录地图

- `client/` — Godot 4.7.1 .NET 客户端，主场景 `scenes/main.tscn`
  - `scripts/game/GameplayMain.cs` — 输入、判定运行时、渲染、HUD、暂停和结算
  - `scripts/game/NoteView.cs`、`GameplayHitBloom.cs`、`GameplaySustainEffect.cs` —
    Note 材质、瞬时爆发和持续接触效果
  - `scripts/game/ChartPack.cs`、`GameSession.cs` — 谱面包与跨场景状态
  - `scripts/game/GameSettings.cs`、`ScoreStore.cs` — 设置与成绩持久化
  - `scripts/audio/SongClock.cs` — 音频时钟、延迟补偿和用户偏移
  - `scripts/ui/` — 主菜单以外的程序化 UI 与通用控件
- `shared/` — 纯 C# 谱面模型、v2 decoder/encoder/validator/package writer、判定计划和规则
- `editor/` — DynaMaker UV 独立 Godot 4.7.1 .NET 制谱器项目，主场景
  `scenes/editor_main.tscn`
  - `scripts/` — 原生 Godot 制谱器 shell 与固定画布工作区
- `tools/chart-editor-core/` — UI 无关的可编辑 v2 文档、命令、快照与校验
- `tools/chart-editor-tests/` — 编辑器 core、确定性编码、Digest 和包写入断言式测试
- `tools/core-tests/` — shared 核心断言式测试
- `tools/` — 谱面、逆向、标定和素材辅助脚本
- `release/` — Public/Internal APK 身份和内容政策、资产来源清单
- `schemas/chart-format-v2/` — 正式 v2 JSON Schema、clean-room golden pack 和 Digest 向量
- `third_party/`、`THIRD_PARTY_NOTICES.md` — 第三方许可正文与归属说明
- `docs/` — 当前/目标规格、判定报告、几何标定、发布流程和 UI 样式稿

## 6. 构建与验证

### 6.1 构建和核心测试

```powershell
cd client
dotnet build
```

构建必须 0 错误。编辑器 core 回归从仓库根目录运行：

```powershell
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
```

独立编辑器项目也必须可构建：

```powershell
dotnet build editor/DynaMakerUv.Editor.csproj
```

shared 核心测试同样从仓库根目录运行；默认只读取版本化的 clean-room
合成谱，不依赖 `client/testdata/`：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj
```

本地官方开发语料是额外的内部回归，需要显式传入目录：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj -- --dev-testdata client/testdata/packs
```

Public 与 Internal APK 的 preset、clean worktree 导出和最终产物强制检查统一见
`docs/releasing.md`。未经 `check_apk.py --mode public` 验证的 APK 不得公开分发。

2026-08-14 最近一次记录：客户端 Release 构建 0 warning / 0 error；默认 clean-room
core-tests 与显式 `--dev-testdata` 内部语料回归均通过；release hygiene 29 项单测通过。v2 的
Draft 2020-12 Schema、golden pack、严格解码/语义校验、legacy 转换、Gameplay Digest、
四元成绩身份及迁移测试均已接入。此次启动 Godot 做了仅本地的一致性录像，但没有导出 APK；
线性 clean-room fixture 的 legacy-direct 与 converted-v2 在 240 帧中 trace 与 raw frame hash
均完全一致（SSIM 1.0、PSNR 全帧 infinite）。Internal 语料 8 张谱中 7 张可确定转换并通过 v2
校验；Tablear Giga 因旧 BPM `Seconds` 连续性存在约 0.7µs/3.5µs 漂移而按转换规则 fail
closed。离线沙箱若无法读取 NuGet 漏洞索引，可能出现 `NU1900` warning。
开发谱按社区新口径重算后：Rain Tech 1105、Tablear Giga 1776、The Villager Hard
2100 个主判定；旧 `Baked_TotalMainNote` 不再作为一致性依据。

### 6.2 启动、截图和录屏

启动 Godot 前必须先结束残留进程：

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe
```

Godot 4.7.1 mono 位于 `../godot/Godot_v4.7.1-stable_mono_win64/`。标准版没有 C# 支持，
不要使用。游戏启动目标为 `--path client`；制谱器启动目标为 `--path editor`，可附
`--editor-package=<meta.json> --editor-workbench` 直接进入工作台。截图/录屏时在
`client/` 目录用 Git Bash，于同一次调用内完成启动、采集和结束进程：

```bash
mkdir -p ../tmp_shots
DYNAMITE_UNIVERSE_START_SEC=0 ../../godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
  --path . --position 0,0 --resolution 1600x900 --borderless --always-on-top \
  res://scenes/gameplay.tscn > ../tmp_shots/godot.log 2>&1 &
godot_pid=$!
sleep 3

ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 \
  -i desktop -frames:v 1 /d/Workspace/Dynamix/community/tmp_shots/shot.png

kill $godot_pid
```

录屏时把截图命令替换为：

```bash
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -framerate 30 \
  -i desktop -t 45 -c:v libx264 -pix_fmt yuv420p -preset veryfast \
  /d/Workspace/Dynamix/community/tmp_shots/demo.mp4
```

`-frames:v` 必须位于 `-i` 之后；yuv420p 输出尺寸必须为偶数。直接调试游玩场景可修改
`DYNAMITE_UNIVERSE_START_SEC` 与参考视频做同刻对比。游玩调试键：Esc 暂停，F12 跳到
谱尾前 3 秒。

## 7. 环境注意事项

- Public APK 使用 `com.dynamiteuniverse.game`；Internal Testdata APK 使用独立的
  `com.dynamiteuniverse.game.internaltest`。`export_presets.cfg` 可提交，签名凭据只能进入被忽略的
  `client/.godot/export_credentials.cfg` 或环境变量。
- 历史中的 `tools/apktool/dynamix_debug.keystore` 已公开暴露并从当前树移除，绝不能用于产品
  签名；Apktool JAR 改为按 `tools/apktool/README.md` 外部安装。
- 项目当前没有项目级 LICENSE，原创代码和资产保留所有权利；Orbitron 许可见
  `THIRD_PARTY_NOTICES.md`。
- .NET SDK 已安装；项目目标为 net9.0，可由本机更高版本 SDK 构建。
- ComfyUI 便携版位于 `../comfyui/ComfyUI_windows_portable/`，默认端口 8188。
- AVD 名为 `Dynamite_test`。adb root 不跨重启；Frida server 需要 root 和
  `setenforce 0`。已安装包签名与 `_rev/apks` 不同，不可覆盖安装。
- headless 下 `SongClock` 使用系统计时器，音频自然结束分支与窗口模式不同；结算结束逻辑
  需要在窗口模式确认。
