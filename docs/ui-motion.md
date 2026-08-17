# UI 动效规格

> 原生客户端已按本文 token 接入首批 UI 动效；独立预览：[`ui-mock/motion-preview.html`](ui-mock/motion-preview.html)。本文记录确定性动效 token、三种运动策略与场景约束，不改变固定 UI/游玩布局。

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

### 2.3 Hit Echo

Hit Echo 是结算信息的重点强调语言：

1. Gameplay 结束后先进入不透明的 pre-signal lock，隐藏已结束舞台和任何半构建结果。
2. 结果面板再落位。
3. 等级字以一次主命中出现。
4. Full 下 cyan 与 pink 两层描边向外扩散并消失；Reduced/Off 不播放 Echo。
5. 分数、CLEAR、判定分布和操作按钮按信息优先级收束出现。

约束：

- Echo 只强调等级、分数或新纪录等关键结果，不应用到每个普通按钮和每一行文本。
- 每个关键结果最多一组主命中和两层轮廓回声；不无限呼吸，不循环闪烁。
- 回声不改变最终字号和布局，不遮挡数字可读性。
- 统计进入必须保持顺序稳定：分数 → CLEAR → 分布线 → P/GR/GD/M → 操作。

## 3. Motion token

### 3.1 时间

| Token | Full | Reduced | Off | 用途 |
| --- | ---: | ---: | ---: | --- |
| `--t-micro` | 90ms | 70ms | 0ms | 按下确认、光点和状态 ping |
| `--t-control` | 160ms | 100ms | 0ms | Hover、按钮亮度和输入反馈 |
| `--t-focus` | 260ms | 140ms | 0ms | 数值替换、选择确认、局部聚焦 |
| `--t-enter` | 420ms | 180ms | 0ms | 面板、列表、HUD 内容进入 |
| `--t-route` | 620ms | 220ms | 0ms | Signal Lock 页面切换 |
| `--t-relay` | 760ms | 220ms | 0ms | Track Relay 与 Gameplay 分层揭示 |
| `--t-result` | 960ms | 260ms | 0ms | Hit Echo 结算重点序列 |
| `--stagger` | 60ms | 0ms | 0ms | 相邻同级信息的固定错峰 |

TRACK HANDOFF 另有状态门槛：Full 的上下文可读状态最少约 `650ms`，若真实 Gameplay 尚未 ready，则以约 `1200ms` 的周期维持低强度等待扫描；ready 和最短驻留同时满足后立即揭示，不额外等待整轮扫描。Reduced 使用 `180ms` 最短静态 handoff 与 `180ms` Gameplay alpha reveal，不扫描、不循环，并保持在 `220ms` route/relay 预算内。Off 为 `0ms`，但仍须先绘制稳定不透明 handoff 状态再提交 Gameplay。预览的 JS profile 是权威来源，并在初始化及切换模式时写入同名 CSS vars，禁止 CSS 与 JS 各自维护不同数值。

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
- 没有自动循环背景动画；用户不操作时，界面应稳定停住。

### Reduced

- `prefers-reduced-motion: reduce` 且 URL 未显式指定 `motion` 时自动采用。
- 页面使用 `70/100/140/180/220/220/260ms` token，`stagger=0`。
- 不保留任何方向位移、跨屏运动、长距离轨道绘制、循环扫描/状态 ping 或 Echo。
- Signal Lock 收束为不透明状态接管和短淡入；Track Relay 不显示飞行卡或 beam。
- TRACK HANDOFF 仍是必要的独立不透明页面，使用 `180ms` 最短静态状态确认，并在 `220ms` route/relay 预算内显示 READY。
- Result 仍先做 pre-signal lock，但关闭 cyan/pink Echo，只保留结果信息本身。
- 不移除状态信息，不改变演示顺序，不以“减少动效”为由跳过必要页面。

### Off

- 所有 CSS animation 与 transition 关闭，时间和位移 token 都为零。
- 所有插值立即完成，但 Select → Gameplay 仍先绘制一个独立、稳定、不透明的 TRACK HANDOFF READY 状态，避免暴露同步构建中的半成品界面；该状态无运动、扫描、循环或 Echo，不要求人为停留。
- Result 直接显示完整稳定结果，不播放 Echo；不得短暂透出 Gameplay。

## 5. 演示行为

| Demo | 确定性起点 | 目标与顺序 |
| --- | --- | --- |
| `MAIN ENTER` | MAIN 初始状态 | Signal Lock → 标题 → 副标题 → 三个菜单项 → 状态卡 |
| `MAIN→SETTINGS` | MAIN 稳定状态 | 输入确认 → MAIN 冻结/离场 → Signal Lock → SETTINGS 行项目 → 详情卡单次局部扫描 |
| `SONG SELECT ENTER` | SONG SELECT 初始状态 | 详情卡锁定 → 列表自上而下进入 → 底部难度/BEST/START |
| `DETAIL LOADING` | SONG SELECT 稳定状态 | 只压暗详情卡 → 卡内单次扫描 → 元数据就绪；列表不移动 |
| `START→GAMEPLAY` | SONG SELECT 稳定状态 | START 确认 → 独立不透明 TRACK HANDOFF（静态合成封面、path/state/title/difficulty/Lv/status/core）→ WAITING → READY → 地平线/轨道/判定线 → HUD |
| `PAUSE/RESUME` | GAMEPLAY 稳定状态 | 冻结输入 → 舞台压暗 → 暂停面板 → RESUME → 恢复舞台 |
| `RESULT REVEAL` | GAMEPLAY 稳定状态 | 不透明 pre-signal lock → 结果面板 → Full 等级主命中/cyan+pink Echo → 分数与统计 → 按钮；Reduced/Off 无 Echo |
| `REPLAY` | RESULT 稳定状态 | Replay 确认 → Track Relay → 同一曲目/难度 GAMEPLAY |

## 6. 交互、可访问性与自动化接口

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

## 7. URL 参数

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
