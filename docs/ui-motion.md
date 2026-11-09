# UI 动效规格

> 游戏动效基线与 token 日期：2026-10-08；2026-10-26 仅静态校正 §9 的编辑器能力注记，未调整时序或重做视觉验收。原生客户端已接入游戏 UI 动效；[网页参考](<ui-mock/motion-preview.html>)不是现有功能完成清单。
> 本文保留有效 token、三种运动策略、场景约束及两端共用命中粒子；旧编辑器扩展目标单独归档。
> 2026-10-08 增补：`--t-hover` 独立 token（§3.1）、结算组内数据动效（§2.3 第 6 条）、选曲 v2 局部动效（§2.4）。
> 2026-10-28 增补：屏风前的结果字标前奏（§2.3.0）与 `--t-outcome` token；只做过纯逻辑回归，未做视觉验收。
> 2026-11-09 已把旧 Editor 目标单独归档；HUD v8 分档见 §2.6。
> 不改变固定 UI/游玩布局；历史原文见 [归档快照](<archive/2026-10-07-cleanup/ui-motion.before.md>)。

## 1. 范围与基线

- 预览舞台固定为 **1920×1080**，按窗口等比缩放并保留黑边；不得把预览实现改成自适应游戏布局。
- 页面是独立、纯前端预览，不连接 Godot，不读取客户端资源、开发谱、玩家谱或成绩文件。
- 除现有 `ui-mock/fonts/Orbitron.woff2` 外不加载任何文件；禁止网络字体、图片、音频、视频、CDN 和运行时请求。
- 全部曲名、封面、轨道和数字均为 clean-room 合成占位内容，不代表可发布曲目或最终美术。
- 动效不得改变最终布局、按钮命中区、选中项、导航目标、谱面时序、判定时刻或暂停语义。动画只表达已经确定的状态变化。
- 同一演示从同一初始场景开始；再次点击会先取消旧定时器和临时 class，再从头播放，不能依赖上一次播放的中间状态。

## 2. 视觉语言

### 2.1 Signal Lock

Signal Lock 是跨页面进入、导航确认和局部加载的基础语言：

1. 输入先被确认并冻结，旧内容不再响应。
2. 青色扫描线沿主阅读方向通过。
3. 切角分片短暂覆盖，而不是整屏长时间闪白。
4. 新页面从扫描后的“已锁定”区域出现，结束后移除遮罩并恢复输入。

约束：

- 主色固定为 cyan `#35e0ff`；白色只用于扫描线核心，不做整屏白闪。
- 全屏 Signal Lock 只用于页面级状态变化；详情加载只能在详情卡内部扫描。
- 扫描期间不使用随机噪声、随机 glitch、无限循环或逐帧抖动。
- 页面离场和入场可以重叠，但最终只保留一个可交互场景。
- `Motion Off` 下不播放扫描插值；常规目标状态立即提交。Select → Gameplay 仍需先提交一个稳定不透明的 Handoff 安全帧，避免同步场景构建暴露半成品。

### 2.2 Track Relay

Track Relay 专用于“已选曲目 → 游玩上下文”的连续传递，也用于 Replay：

1. 选中行和 START（或结算 Replay）先做一次短促确认。
2. 曲目、难度等必要上下文压缩为一张青色信号卡。
3. 信号卡沿单一路径移向独立的 **TRACK HANDOFF** 场景；该飞行卡保持纯文本，只显示曲名、难度和等级，禁止携带封面像素。
4. TRACK HANDOFF 是 Select 与 Gameplay 之间真实、独立且不透明的状态，不是覆盖在两页之上的装饰层。它可静态显示 clean-room 合成封面，以及占位 path/state、标题、难度、Lv、status 和 core。
5. START 确认后先进入 TRACK HANDOFF；等待/校验态结束并显示 READY 后，才分层揭示地平线、轨道参照线、判定线与 HUD。

约束：

- Relay 只表达同一曲目/难度上下文的延续，不伪装实际资源加载进度。
- 移动信号卡只保留文字识别上下文，不携带曲绘像素，不把整张选曲 UI 缩放进游玩场景；静态、不透明的 TRACK HANDOFF 场景可以显示 clean-room 合成封面。
- TRACK HANDOFF 的上下文展示在 Full 下至少约 `650ms`；等待态仅在真实 Gameplay ready 尚未到达时维持约 `1200ms` 的低强度循环扫描，不能把扫描周期当成强制 READY 延迟。
- 同一次转场只有一条主运动路径；禁止多个对象无关飞散。
- 游玩输入只能在 Relay 完成并锁定舞台后开放。
- Replay 使用相同语言，但源头是结算确认；不得先返回选曲再进入游玩。

### 2.3 Result v2 屏风与评级

2026-10-03 原生客户端按 Penpot 确认稿更新；旧 HTML demo 的 Result 段仅留作历史预览，
当前预览见 `design/result-v2/timing-preview.mp4`，时间线与几何见 `result-v2-design.md`。
2026-10-28 起屏风之前增加结果字标前奏（§2.3.0），屏风本体时序未改。

#### 2.3.0 屏风前的字标前奏（2026-10-28 接入）

1. Full：0–0.30s 背景压暗到 0.80；0.04–0.28s 中央斜纹区淡入；0.20–0.60s 字标 1.58→1.0
   拍入（easeOutBack `c1=1.35`）并淡入；0.52s 命中辉光让字标与斜纹区同步提亮；0.60s 起能量
   衰减回 1.0；1.55s 交给屏风。普通完成使用 TRACK COMPLETE，同样走完整字标前奏；
   只有无有效成绩数据的保护路径保留压暗+斜纹区，0.68s 后衔接。
2. Reduced：0.26s 静态短淡入，无方向位移、无砸入缩放、无强闪（能量/辉光恒定）。
3. Off：0 帧前奏，直接显示稳定结算页，不留一帧中间态。
4. 跳过：前奏期间任意左键/触摸或 Enter/Space/Escape，事件被消费并直接落稳定终值，
   随后交付屏风；交付后 120ms 内按压不落到结算按钮。
5. 一次结算只进入一次前奏、只交付一次结算页；字标分类与结算页 AP/FC 徽章同源，
   详见 [结果前奏](<result-outcomes.md>)。

1. 双屏风合拢 600ms，完全遮挡时才隐藏舞台并换成不透明结算背景。
2. 停顿 320ms，再用 480ms 拉开；评级图集尚未 ready 时延长闭合等待。
3. 1400ms 开始播放本次评级的 v5 表面电流序列，24fps、30 帧；约 1692ms 第 8 帧命中。
4. 1780ms 起曲目信息/CLEAR、分数与纪录、成就/Max Combo、判定分布、按钮依次浮现，向上 18px。
5. 2650ms 进入低频表面电流循环；Reduced 为 260ms 静态短淡入，Off 立即显示完整页。
6. 组内数据动效（2026-10-08 接入）：SCORE 0→终值 550ms CubicOut；判定四档计数各 250ms、
   错峰 60ms；判定分布条整带从左缘 350ms 擦入；NEW RECORD 章 1.25→1.0 盖印回弹
   （180ms easeOutBack）。全部是揭示时钟 `ResultRevealTimeline.Elapsed` 的纯函数，
   跳过即落终值，不需要收尾分支；Reduced/Off 直接终值，盖印缩放仅 Full。

（以上 1–6 的钟从屏风开始时算起，即前奏交付之后；它不是从最后一个判定瞬间算起。）

约束：

- Echo 只强调等级、分数或新纪录等关键结果，不应用到每个普通按钮和每一行文本。
- 每个关键结果最多一组主命中和两层轮廓回声；不无限呼吸，不循环闪烁。
- 回声不改变最终字号和布局，不遮挡数字可读性。
- 入场可点击/触摸或 Enter/Space/Escape 跳过；消费当前事件并留 120ms 防穿透间隔。

### 2.4 校准向导

校准页（Game — Calibration 两板，行为口径见 editor-ui-design.md §2.2）的动效服务于
状态机 Ready → Sampling → Complete：

1. 进场沿用分组交错 fade + shift（头部/相位块/试打台/统计板/条带）；路由转场是标准
   Signal Lock（`OPEN CHANNEL / CALIBRATION`），返回用 Back 类转场。
2. 状态切换时相位块与右板内容交叉淡换（0.18s），Complete 的轨道冻结压暗 0.2s，
   条带 MEDIAN 线 0.18s 淡入。
3. note 下落与拍点脉冲是**测量本体**，三档 MOTION 下都不降级；Reduced 只保留透明度类
   过渡，Off 全部瞬时。

约束：

- 击打反馈只显示毫秒读数（早粉晚青），不使用 Hit Echo 与判定字——校准页不评价
  「准不准」，只呈现「差多少」。
- Complete 是冻结帧，击打不再产生读数；采样中目标样本步进器锁定为禁用态，不另做动画。
- 数据计数是**时长驱动**的纯函数，终值必须精确等于真实成绩，不按数值密度变速。

### 2.4 选曲 v2 局部动效

2026-10-08 接入；几何与交互口径见 `editor-ui-design.md` §2.1。

1. 就地展开卡：200ms 高度过渡（96↔392，`cubic-bezier(.33,1,.68,1)`），`ClipContents` 裁切、
   下方行跟随滑动；收拢落点才交还选中行，途中可从中截高度干净反向。属 directional motion，
   Reduced/Off 保持瞬时切换。
2. 卡内内容切换：曲名/曲师/谱师 80ms 淡出 → 换文 → 120ms 淡入（换文由代际守卫，
   快速连切不写回旧文本）；封面在谱面就绪后 150ms 从 0 淡入。Reduced 保留淡变，Off 瞬时。
3. 列表滚动：滚轮/触摸板走惯性指数趋近，client 固定 τ = 112ms（当前对象见 [选曲滚动合同](editor-ui-design.md#选曲滚动合同)）；触摸拖动与滚动条拖动 1:1 直连不插值；
   选中行/展开卡的 `EnsureControlVisible`、程序刷新与跳转旁路惯性。
4. Hover 全量覆盖 `--t-hover`（曲库行、模式行、全部设置行、按钮、预览传输键），
   端点色与原瞬时 hover 一致，只平滑中间过程。

### 2.5 音符配色 v2

2026-10-20 接入：页面仍走 TransitionDirector 的场景导航，内容进场按当前 UiMotionProfile 做整组淡入，Off 直接落终态。
主态/观察态切换保持即时、同一草稿，不加入游玩轨道或循环落 note。按住比较为直接状态切换，松手/失焦立即恢复。
“测试命中”是用户主动触发的材质/特效预览，按 HIT EFFECTS / 残影设置走真实效果，不把 UI Motion Off 误当关闭玩法特效。
模态期间禁用背景输入，关闭恢复；预设、RGB 与确认弹窗不使用长时间动画。实机验收见 [配色 v2](<note-colors-v2.md>)。

### 2.6 游戏 HUD v8

已接真实 BLEED 血量、判定统计和暂停事件；用户2026-11-09确认本体实际效果满意。Full 保留残条/局部切口/低血端标呼吸与封顶点亮；Reduced 降低点亮和柔光、去掉残条/冲击/呼吸；Off 直接显示状态，无插值/事件点亮/外溢光。暂停不为动效延迟，血量动画在暂停冻结；具体参数与职责见 [HUD v8](gameplay-hud-v8.md)。不以 UI Motion 改写命中规则或 HIT EFFECTS。

## 3. Motion token

### 3.1 时间

| Token | Full | Reduced | Off | 用途 |
| --- | ---: | ---: | ---: | --- |
| `--t-micro` | 90ms | 70ms | 0ms | 按下确认、光点和状态 ping |
| `--t-hover` | 100ms | 100ms | 0ms | Hover 颜色过渡（client `UiHoverFade`；旧 Editor 同源实现已归档，推进只在过渡期跑 `_Process`，「值变了才重绘」与「到终值才停表」是两个独立条件） |
| `--t-control` | 160ms | 100ms | 0ms | 按钮亮度、开关方块和输入反馈（hover 已拆出到 `--t-hover`） |
| `--t-focus` | 260ms | 140ms | 0ms | 数值替换、选择确认、局部聚焦 |
| `--t-enter` | 420ms | 180ms | 0ms | 面板、列表、HUD 内容进入 |
| `--t-route` | 620ms | 220ms | 0ms | Signal Lock 页面切换 |
| `--t-relay` | 760ms | 220ms | 0ms | Track Relay 与 Gameplay 分层揭示 |
| `--t-result` | 2650ms | 260ms | 0ms | 双屏风 → v5 评级 → 信息浮现（原生客户端；钟从屏风开始算） |
| `--t-outcome` | 1550ms | 260ms | 0ms | 屏风前的结果字标前奏（普通完成也用 TRACK COMPLETE；仅无效成绩保护路径 Full 为 680ms） |
| `--stagger` | 60ms | 0ms | 0ms | 相邻同级信息的固定错峰 |

TRACK HANDOFF 另有状态门槛：Full 的上下文可读状态最少约 `650ms`，若真实 Gameplay 尚未 ready，则以约 `1200ms` 的周期维持低强度等待扫描；ready 和最短驻留同时满足后立即揭示，不额外等待整轮扫描。Reduced 使用 `180ms` 最短静态 handoff 与其后的 `180ms` Gameplay alpha reveal，不扫描；两段顺序执行至少 360ms，覆盖/加载另计。`220ms` 是 route/relay token，不是整个 handoff 的总预算。Off 为 `0ms`，但仍须先绘制稳定不透明 handoff 状态再提交 Gameplay。游戏以 `UiMotionProfile`、`ResultRevealTimeline` 和当前转场代码为实现来源；网页 demo 只在自身 JS/CSS 内保持 token 同源，不能覆盖原生结算 v2。

hover 的统一实现档是 **100ms**（client `UiHoverMotion.Seconds`；旧 Editor 实现不代表现行工作台），比 `--t-control` 的 160ms 更轻，专用于指针悬停的颜色/亮度过渡；
160ms 继续留给按下确认与输入反馈。选曲页的实现值：展开卡高度动画 200ms（`--t-focus` 档语义）、
卡内文字淡切 80ms 出 / 120ms 入、封面就绪淡入 150ms（`--t-micro`/`--t-control` 档语义），
明细见 [UI 设计施工约定](<editor-ui-design.md>) §2.1。

### 3.2 距离

| Token | Full | Reduced | Off | 用途 |
| --- | ---: | ---: | ---: | --- |
| `--move-xs` | 8px | 0px | 0px | 标签、数值替换、微型状态 |
| `--move-sm` | 20px | 0px | 0px | 面板内容和常规页面进入 |
| `--move-md` | 28px | 0px | 0px | Track Relay 和结算分组方向提示 |

距离是最大参考值，不要求每个对象都移动。Reduced 只采用不带方向的淡入和亮度锁定，避免任何方向位移与大范围穿越；Off 不保留任何空间插值。

### 3.3 缓动

| Token | 精确值 | 语义 |
| --- | --- | --- |
| `--e-standard` | `cubic-bezier(.2,.8,.2,1)` | 常规控件反馈，快速响应、平稳收束 |
| `--e-enter` | `cubic-bezier(.16,1,.3,1)` | 页面和内容进入，末端柔和 |
| `--e-exit` | `cubic-bezier(.4,0,1,1)` | 离场，立即让出视觉焦点 |
| `--e-signal` | `cubic-bezier(.65,0,.15,1)` | 扫描、裁切与 Relay 传递 |
| `--e-echo` | `cubic-bezier(.22,1,.36,1)` | 命中后回声扩散 |

禁止为同一语义随意新增近似曲线。若运行时实现受框架限制，优先保留总时长、开始响应和结束收束的相对关系。

## 4. 运动策略

### Full

- 使用完整 Signal Lock 分片、全程扫描、Track Relay 文字卡路径；等待真实 ready 时以约 `1200ms` 周期扫描，并使用 Hit Echo 双层轮廓。
- TRACK HANDOFF 的静态上下文保持至少约 `650ms`，可以显示合成封面；移动信号卡保持纯文本。
- 常规控件和网页 demo 不做无意义循环，业务信息稳定；原生首页 Mk VI 核心（2026-10-29 起替代 Mk II）保持 4 秒模型循环与弥散呼吸，Full 结算评级保留约 7fps 末尾电流循环。它们是采用的氛围效果，不受旧网页“完全静止”表述覆盖。

### Reduced

- `prefers-reduced-motion: reduce` 且 URL 未显式指定 `motion` 时自动采用。
- 页面使用 `70/100/140/180/220/220/260ms` token，`stagger=0`。
- 路由/控件去掉方向位移、跨屏运动、长轨道绘制、循环扫描/状态 ping 或 Echo；原生首页 Mk VI 在 Reduced 下把模型降到 0.45 倍速、弥散脉动幅度从 0.16 收到 0.06，Off 才停动画并把弥散能量固定在 1.0。Reduced 结算评级为静态。
- Signal Lock 收束为不透明状态接管和短淡入；Track Relay 不显示飞行卡或 beam。
- TRACK HANDOFF 仍是独立不透明页；使用 `180ms` 最短静态状态确认，ready 后接 `180ms` alpha reveal，覆盖/实际加载另计，不承诺 220ms 内完成全流程。
- Result 先以 260ms 静态短淡入显示结果字标（压暗+斜纹区+字标，无位移/砸入/强闪），
  再以不透明背景接管，静态评级与信息在 260ms 内淡入，无屏风或 Echo。
- 不移除状态信息，不改变演示顺序，不以“减少动效”为由跳过必要页面。

### Off

- 所有 CSS animation 与 transition 关闭，时间和位移 token 都为零。
- 所有插值立即完成，但 Select → Gameplay 仍先绘制一个独立、稳定、不透明的 TRACK HANDOFF READY 状态，避免暴露同步构建中的半成品界面；该状态无运动、扫描、循环或 Echo，不要求人为停留。
- Result 没有前奏帧，直接显示完整稳定结果，不播放 Echo；不得短暂透出 Gameplay。

## 5. 演示行为

| Demo | 确定性起点 | 目标与顺序 |
| --- | --- | --- |
| `MAIN ENTER` | MAIN 初始状态 | Signal Lock → 标题 → 副标题 → 三个菜单项 → 状态卡 |
| `MAIN→SETTINGS` | MAIN 稳定状态 | 输入确认 → MAIN 冻结/离场 → Signal Lock → SETTINGS 行项目 → 详情卡单次局部扫描；**进场错峰只排当前 tab 的可见条目，总预算 0.3s**（隐藏 tab 不占序号；旧实现按全表 60ms/条累加，落在最后构建的 EFFECTS 时要 1.7s 才轮到页脚入口） |
| `SONG SELECT ENTER` | SONG SELECT 初始状态 | 详情卡锁定 → 列表自上而下进入 → 底部难度/BEST/START |
| `DETAIL LOADING` | SONG SELECT 稳定状态 | 只压暗详情卡 → 卡内单次扫描 → 元数据就绪；列表不移动 |
| `START→GAMEPLAY` | SONG SELECT 稳定状态 | START 确认 → 独立不透明 TRACK HANDOFF（静态合成封面、path/state/title/difficulty/Lv/status/core）→ WAITING → READY → 地平线/轨道/判定线 → HUD |
| `PAUSE/RESUME` | GAMEPLAY 稳定状态 | 冻结输入 → 舞台压暗 → 暂停面板 → RESUME → 恢复舞台 |
| `RESULT REVEAL` | GAMEPLAY 稳定状态 | 原生客户端：结果字标前奏（压暗/斜纹区/字标，1.55s）→ 双屏风合拢/停顿/拉开 → v5 评级 → 信息 → 按钮；Reduced 前奏 260ms 静态、Off 无前奏且直接完整页。旧 HTML 段未同步 |
| `REPLAY` | RESULT 稳定状态 | Replay 确认 → Track Relay → 同一曲目/难度 GAMEPLAY |

## 6. 网页参考的交互、可访问性与自动化接口

- 控制按钮必须能用键盘聚焦，使用可见 `:focus-visible` 描边。
- 动画遮罩使用 `pointer-events:none`；可见场景之外的场景使用 `aria-hidden="true"` 且不能接收输入。
- 进入新演示时取消旧定时器；异步回调通过 generation 检查，旧回调不能污染新状态。
- 预览提供稳定入口：
  - 舞台：`#motionStage` / `data-testid="motion-stage"`
  - 模式：`#motionFull`、`#motionReduced`、`#motionOff`
  - 演示：`#demoMainEnter`、`#demoMainSettings`、`#demoSelectEnter`、`#demoDetailLoading`、`#demoStartGameplay`、`#demoPauseResume`、`#demoResultReveal`、`#demoReplay`
  - 场景：`#sceneMain`、`#sceneSettings`、`#sceneSelect`、`#sceneHandoff`、`#sceneGameplay`、`#sceneResult`
  - TRACK HANDOFF testids：`scene-track-handoff`、`handoff-cover`、`handoff-status`、`handoff-core`
  - 关键层：`#signalLock`、`#trackRelay`、`#pauseOverlay`、`[data-testid="result-pre-signal-lock"]`
- 全局测试接口：
  - `MotionPreview.runDemo(name)`
  - `MotionPreview.setMotion("full" | "reduced" | "off")`
  - `MotionPreview.showScene(scene)`
  - `MotionPreview.getState()`

## 7. 网页参考的 URL 参数

预览支持直接定位，参数不区分大小写的场景别名由页面归一化：

```text
motion-preview.html?motion=full&scene=main
motion-preview.html?motion=reduced&scene=select
motion-preview.html?motion=off&scene=handoff
motion-preview.html?motion=off&scene=gameplay
motion-preview.html?scene=result
```

- `motion`：`full`、`reduced`、`off`。合法显式参数优先于系统偏好。
- `scene`：`main`、`settings`、`select`/`song-select`、`handoff`/`track-handoff`、`gameplay`/`play`、`result`。
- 未提供合法 `motion` 时使用 `prefers-reduced-motion`；未提供合法 `scene` 时使用 `main`。
- 在控制台切换模式或完成演示后，页面尽可能通过 `history.replaceState` 更新参数；`file://` 或受限预览器禁止改写地址时不影响交互。

## 8. 实现边界

- 本文是动效规格，不授权修改 `GameplayMain.cs` 顶部的固定游玩布局常量。
- 客户端接入时不得把页面的合成轨道、封面或曲名当成产品资产。
- 不以 CSS `backdrop-filter`、`clip-path` 等网页表现细节强制约束 Godot 实现；必须保留的是时长层级、状态顺序、运动策略、clean-room 边界与 Reduced/Off 行为。
- 动效完成后控件必须处于唯一、可预测的最终状态；不得用动画结束事件承载存档、选曲提交、谱面加载或输入判定等业务事实。
- 会被转场揭示的页面，入场元素必须在 `TransitionDirector.ReportSceneReady` **之前**就置为隐藏起点
  （alpha 0，Full 再左移 `ContentShift`），由揭示完成回调再播放入场；否则转场覆盖层退场时会先露出
  完整页面，随后又归零重播入场，表现为“先显示一下再渐入”。现有实现见各页 `PrepareEnterAnimation`。

## 9. 共享命中特效与编辑器边界

旧编辑器 §9.1–§9.4 的扩展目标已移至 [历史目标](archive/art/2026-10-editor-motion-targets.md)，不作为现行 Avalonia 工作台的接线清单。编辑器 Auto 播放/滚轮预览现已复用游戏音符与命中/持续视觉，实际能力见 [制谱器说明](dyna-maker-uv.md)。放置提交 Strength≈0.4 的爆发仍只是旧设计目标，不据 Auto 复用宣称已完成。

<a id="95-已接入游戏的打击粒子规格编辑器复用目标"></a>
### 9.5 两端共用的打击粒子规格

命中反馈 = 程序化核心爆发（`GameplayHitBloom`，12 帧式，不变）+ GPU 粒子质感层
（`GameplayHitParticles`，火花/余烬两个发射器，加法混合）。粒子纹理全部运行时合成
（clean-room）；材质按 (accent, 层, 宽度桶) 静态缓存；进游玩时 Prewarm 预编译 shader。

**命中爆发层**（一次性，ZIndex 2，加法混合）：

| 参数 | 火花 | 余烬（仅 Full） |
| --- | --- | --- |
| 数量 | 14 × Strength × 类型系数（Hold/Mixer 0.75、ExTap 1.10） | 6 × Strength |
| 寿命 | 0.42s | 0.62s |
| 初速 / 阻尼 / 重力 | 200–440 / 500 / 无 | 40–120 / 40 / (0,−55) 上飘 |
| 形状 | 16×64 软条，AlignY 对齐速度，scale 0.55–0.78→0.22 | 32×32 软点，scale 0.60–0.95→0.22 |
| 颜色 | 白 → 判定色 lerp 类型色 0.60 → 透明（加法混合下峰值 ~0.80） | 同左 |

**持续层**（Hold/Mixer 接触保持中，ZIndex 2，加法混合）：

| 参数 | 火花 | 余烬（仅 Full） |
| --- | --- | --- |
| 速率 | 40/s（存活 14） | 12/s（存活 6） |
| 寿命 | 0.35s | 0.50s |
| 发射 | Box 沿接触头宽度（24px 桶量化），140° 朝面板外 | 同左 |
| 拖尾 | Trail 0.20s / 8 段 | 无 |
| 初速 / 阻尼 / 重力 | 60–140 / 260 / 无 | 25–70 / 30 / (0,−40) |
| 并发折算 | 第 1 条 ×1.0、第 2 条 ×0.85、第 3 条起 ×0.72 | 同左 |

共同规则：Mine 不出粒子；断触/结束即停发、排空后自回收、宽限内接回重新发射；
`HIT EFFECTS = REDUCED` 时火花半量、无余烬（拖尾与加法混合保留），`OFF` 时两层都不生成，
爆发与接触效果同时关闭，只留判定残影与判定浮字；
侧轨发射方向随带符号 Rotation（左 +π/2、右 −π/2）转向面板外侧。

**编辑器边界**：Auto 预览已经复用完整命中与持续视觉；放置提交缩小爆发未接入。命中特效档位与 UI Motion 独立，不能混作同一个设置。
