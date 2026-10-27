# Dynamite Universe 当前客户端玩法与 Legacy 格式

> 本文描述 Godot 客户端的 legacy 兼容路径、判定、计分和布局规则，不是 v2
> 序列化合同。正式 v2 合同见 `chart-format-v2.md`；客户端已通过 strict loader、shared runtime
> adapter 和 legacy→v2 默认转换接入 v2，同时保留 legacy-direct 兼容与 Internal 等价性验证。
> 原版方法体证据见
> `original-judgement-analysis.md`，几何测量见 `video-geometry-analysis.md`。
> 文中不包含任何原版素材或可发布原版谱面。
> 2026-10-13 静态同步当前实现；本轮没有重跑构建/测试。历史原文见 [整理前快照](<archive/2026-10-07-cleanup/gameplay-spec.before.md>)。EX-Tap 二值判定与窗口倍率 1.0 已与冻结 v2 §12.1 对齐。
> 2026-10-17 用户实机反馈：多指输入、短 Hold 手感、对象池修复及侧轨 `691/790` 距离比例均基本通过，侧轨保留该比例；仅同步文档，未重新构建或测试。验收范围与剩余专项见 [当前交接 §4](<handoff.md#4-仍需处理>)。

## 1. 当前 Legacy 谱面数据

客户端当前读取的社区谱面 JSON 沿用逆向得到的三轨结构，由 `DynamixChartLoader` 解析。

### 1.1 顶层字段

| 字段 | 类型 | 当前用途 |
| --- | --- | --- |
| `name` | string | 解析标题与原始难度号 |
| `Baked_TotalMainNote` | int | 旧格式兼容字段；当前主判定总数由 `JudgePlan` 重算 |
| `TimeLine.BakedBarSections` | array | BPM 时间线 |
| `NoteSystem__DropSpeeds` | array | `{BarTime, Value}` 视觉速度事件 |
| `NotesLeft` / `NotesCenter` / `NotesRight` | array | 三轨音符 |
| `get_type` | 任意 | 导出器附加字段，loader 忽略 |

### 1.2 音符字段

| 字段 | 类型 | 当前语义 |
| --- | --- | --- |
| `Id` | int | 谱内唯一 id |
| `SubNoteId` | int | 同轨下一路径节点；`-1` 表示结束 |
| `Type` | 1..9 | 见 §3 |
| `Baked_SyncNote` | int | 多押视觉标记；运行时只判断是否非零 |
| `BarTime` | float | 小节时间，1 bar = 4 拍 |
| `Position` | float | 条的左缘 P |
| `Width` | float | 条宽 W；实际范围为 `[P,P+W]` |
| `Baked_Second` | float | 烘焙命中秒；空 BPM 时间线时使用 |

Position 不是中心点。渲染中心必须使用 `P+W/2`，空间判定必须使用完整范围
`[P,P+W]`。

### 1.3 谱面包

谱面包目录包含：

```text
meta.json
chart_<diff>.json
<audio file>
<optional cover file>
```

`meta.json` 提供 `id/title/artist/charter/audio/cover/charts[]`。这是当前 legacy 包入口；编辑器和 Internal
Testdata 构建先扫描 `res://testdata/packs`，再扫描 `user://charts`，后者可用相同 id 覆盖
开发包；Public 和未分类导出只扫描 `user://charts`。

### 1.4 与正式 v2 的边界

正式 Dynamite Universe Chart Format v2 见 `chart-format-v2.md`。它使用 `dynamite-uv-pack` /
`dynamite-uv-chart`、精确有理 BarTime、`center+width`、内嵌 Hold/Mixer nodes、Hold `judge`、
严格正向 Scroll 和 Gameplay Digest。v2 为无损迁移既有谱面允许三轨 Mixer 和超出推荐 `[0,5]`
安全区的有限 center/正 width；消费者不得擅自 clamp。同步 Tap 金框由精确时间与异轨按下型
父 Note 派生，不保存 `Baked_SyncNote`。本节的 float BarTime、左缘 Position、`SubNoteId` 与烘焙字段只描述 legacy-direct 输入。
当前客户端已支持 strict v2，并默认尝试 legacy→v2 转换；适配器在内部保留兼容 Note 图，同时携带精确时间、路径 evaluator 和判定元数据。

D4-C 首次失联冻结 grace deadline 与 D5-A 四档 Mixer 头/后续 Perfect-Miss tick 已接入。
v2 Hold 的 `judge:false` 节点只塑形，legacy 路径节点保留兼容主判定口径。成绩身份见 §10，不再统一为旧键。

## 2. 时间、BPM 与变速

### 2.1 BarTime 与秒互转

每个 BPM 段包含 `BPM`、`BarTime` 和 `Seconds`。对任意 bar：

```text
i = 最后一个满足 sections[i].BarTime <= bar 的段
sec = sections[i].Seconds + (bar - sections[i].BarTime) * 240 / sections[i].BPM
bar = sections[i].BarTime + (sec - sections[i].Seconds) * sections[i].BPM / 240
```

时间线存在时，loader 使用该公式统一重算所有 note 的 `Second`；时间线为空时使用
`Baked_Second`。默认 BPM 查询回退值为 150。

### 2.2 DropSpeeds

`NoteSystem__DropSpeeds` 只影响视觉位置，不改变命中秒。

- 同一 BarTime 的后一个事件覆盖前一个。
- 首事件之前和末事件之后使用端点值。
- 相邻事件之间按 BarTime 线性插值，不使用阶跃。
- 每帧先把歌曲秒换算为 `currentBar`，以便在 BarTime 轴上采样当前谱面流速；移动距离则按音频秒计算：

  ```text
  visualDistancePx =
      (note.Second - currentSecond)
      * speed(currentBar)
      * playerScale
      * 1026px/s
  ```

- 使用当前流速乘完整剩余音频秒，不对历史流速积分；升速时 note 仍可暂时远离判定线，
  再随剩余时间缩短而折返，已生成 note 回退出屏时不销毁。
- 玩家落速倍率为 `FallSpeedLevel/10`，与谱面速度相乘。
- Lv10、DropSpeed=1 的二维标尺恒为 150 BPM 基准的 `1026px/s`，不随谱面 BPM 改变。
  BPM 只负责 BarTime/秒换算和命中时刻。
- Center/Side 的可见行程保持 790px/691px；Side 的屏幕移动距离额外乘 SideDistScale
  （当前保留行程比 `691/790 ≈ 0.8747`，2026-10-17 用户实机反馈基本通过）；
  位置直接映射到现有二维轨道，不做透视投影。
- 空 BPM 时间线直接使用 `Baked_Second` 作为 note 命中秒，仍使用同一套固定秒速模型。

## 3. Type 1–9 权威语义

| Type | 名称 | 判定 | 当前视觉 |
| --- | --- | --- | --- |
| 1 | Tap | 普通输入 | 蓝色切角渐变、上缘高光和受控辉光；SyncNote 非零时金色描边 |
| 2 | Drag | 接触判定，按住经过即可 | 绿色切角渐变和方向纹 |
| 3 | HoldHead | Hold 起点输入 | 琥珀切角渐变头、半透明连接体和亮侧边 |
| 4 | HoldNode | Hold 路径节点/尾 | 中间控制节点隐藏；尾节点保留琥珀材质 |
| 5 | ExTap | 当前窗口倍率 1.0，Good 窗内 Perfect，否则输入 Miss | 冰蓝切角渐变和虚线芯 |
| 6 | MixerHead | Mixer 起点输入 | 粉色纹理头；接上后在线上显示动态头 |
| 7 | MixerNode | Mixer 路径节点/尾 | 控制节点隐藏，只显示节点间细连线 |
| 8 | Mine | 危险窗内触碰为 Miss，未触碰为 Perfect | 暗红危险斜纹 |
| 9 | BarLine | 无判定单元 | 低亮中性扫描线 |

T3/T4 和 T6/T7 通过 `SubNoteId` 组成同轨路径。构建路径时按 id 跟随到 `-1`；
悬空引用、环和无有效尾节点均安全终止。

`Baked_SyncNote != 0` 表示同一 BarTime 在其他轨存在按下型 note。当前只在 T1 Tap
上绘制金色多押描边；其他类型保留数据但不画框。SyncNote 不影响判定。

## 4. 难度与判定窗口

### 4.1 预设映射

| 难度键 | 判定预设 |
| --- | --- |
| `casual` | Casual |
| `normal` | Normal |
| `tutorial` | Tutorial（仅当前内置/legacy；v2 社区包禁止） |
| 其他键（含 hard/mega/giga） | Hard |

### 4.2 窗口

普通 Perfect/Great/Good/Miss 窗固定按 `StandardBPM=150` 换算：

| 窗口 | Casual/Tutorial | Normal | Hard |
| --- | --- | --- | --- |
| Perfect | ±100ms | ±62.5ms | ±62.5ms |
| Great | ±150ms | ±150ms | ±112.5ms |
| Good | ±200ms | ±200ms | ±162.5ms |
| Miss 候选范围 | ±250ms | ±250ms | ±250ms |

当前 EX-Tap 窗口倍率 `1.0`（与 Tap 同窗），判定为二值 —— `|Δ| ≤ Good` 一律
Perfect，落在输入窗内但超出 Good 窗（早侧或晚侧）立即按输入 Miss 结算，无触点直到超时才
是 AutoMiss；该类型与其他类型一样参与同帧时间锁。

### 4.3 游玩模式

| 模式 | 判定预设 | 说明 |
| --- | --- | --- |
| Standard（缺省） | 按难度映射（§4.1） | 与既有行为一致 |
| Hardcore | 强制 Hardcore 实例 | Hard 窗口 ×0.5：Perfect ±31.25ms / Great ±56.25ms / Good ±81.25ms / Miss ±125ms（`barTime` = Hard × 0.5，×240/150 换算）。对**任何难度**生效；Holding 断触宽限沿用 Hard 值 0.125 bar |

模式持久化在 `user://settings.json` 的 `gameplayMode` 字段，缺省 `standard`。
选曲页 QUICK SETTINGS 的 MODE 只在这两档之间二选一（互斥），不再有平级的 Bleed 档。

`GameSettings.BleedEnabled`（`bleedEnabled`，默认 false，选曲页的 `BLEED` 开关）是**游玩修饰
而非档位**：Standard 下自由开关，Hardcore 下强制开启并锁定（开关压暗、方块停右侧 60%、挂锁、
副文案 `HARDCORE 下强制开启`）；锁定期**不改写**持久化值，切回 Standard 恢复用户此前的选择。
机制本身仍是预留——该开关只持久化，没有任何判定运行逻辑。

`GameSettings.MirrorEnabled`（`mirrorEnabled`，默认 false，选曲页的 `MIRROR` 开关）开启后游玩
画面左右镜像。它只改**屏幕映射**，判定窗口、Position、Note 数据一律不变：侧轨 Left↔Right 互换
（含输入触点归属、判定线侧轨、note 视觉与粒子朝向），中轨水平坐标关于**面板中轴 2.5** 反射、
宽度不变。中轨 note 中心的实际范围是 `[0,5]`（全部发行谱面实测），故中轴 `x=963` 而不取
`[0,4]` 左缘带的中点 2.0（`x=826.4`，会让中轨镜像整体左移 136.6px）。映射本体是 shared 的
`GameplayStageGeometry.MirroredDisplay` / `MirroredTrack`，自反，
因此屏幕 → 谱面的输入触点反查复用同一函数（`client/scripts/game/GameplayMirror` 只负责读设置）。
判定侧的逆映射有且只有一个入口：`GameplayMain.ChartTouchOf` / `ChartPointOf`——先
`DisplayPositionOf`（屏幕 → 显示空间，不镜像）再 `GameplayMirror.Display`（显示 → 谱面）镜像
一次；在别处再镜像一次会因自反抵消，触点会退回显示坐标而判不上镜像后的 note。

所有窗口都从当前难度的 JudgeSettings 实例读取，客户端不再硬编码扫描窗或接触窗。
判定预设统一由客户端在加载时按「难度 + 游玩模式」解析（`V2Integration.PresetFor`），
v2 包/转换产物里的 `entry.JudgePreset` 只反映语义难度、不含运行时模式；legacy→v2 转换时
Hardcore 按 Hard、Tutorial 按 Casual 归档。成绩身份没有独立运行模式字段；legacy 转换可能间接改变语义预设与 Digest，这不构成完整模式隔离。另需核查预加载后切模式是否复用旧 Preset（2026-10-26 静态疑点，尚未运行复现），见 [当前交接](<handoff.md#4-仍需处理>)。

Hold 的断触宽限为：

```text
0.125 * 240 / clamp(currentBpm, 120, 200)
```

因此 BPM≤120 时为 250ms，BPM=150 时为 200ms，BPM≥200 时为 150ms。首次失去全部覆盖时计算一次并冻结 deadline；此后 BPM 改变不移动 deadline，接回清除此状态。
该数值只用于确认 Hold 断触，不生成 Hold 判定点。Mixer 判定网格见 §6.3。

## 5. 输入与候选选择

### 5.1 屏幕区域

在 1920×1080 设计坐标中：

```text
y > 771  -> 包含 Center
x < 400  -> 包含 Left
x > 1520 -> 包含 Right
均不命中 -> 回退 Center
```

同一触点可以投影到多个轨道，不是互斥分区。屏幕坐标转换为显示空间 Position（镜像时再经唯一逆映射）：

```text
Center: (x - 280) / 273.2
Side:   (840 - y) / 115
```

### 5.2 触点状态

客户端从全局 `_Input` 接收 `ScreenTouch`/`ScreenDrag`，避免触摸被 HUD `Control` 截断；
键盘和鼠标仍由 `_UnhandledInput` 接收，防止点击 HUD 被当作游玩输入。项目关闭
`pointing/emulate_mouse_from_touch`，同一根手指不会同时生成触摸和模拟鼠标两份输入。
触摸取消（若平台有投递）与窗口失焦会清空触点状态和待处理输入；Move 只更新本局已 Press 的 id，未知 id 直接忽略。暂停按钮所占触摸 id 不进入游玩输入，暂停时清空所有触点和待处理事件。

每根有效触点按 id 独立保存 Track、Position 和 phase：

- `Began=1`
- `Moved=2`
- `Stationary=3`

一帧内先形成包含全部有效触点 id 的完整快照。首个成功结算的普通输入（Perfect/Great/Good/输入 Miss，早晚侧均包括）将每帧批次锁定为该 note 的精确 double 秒值；同时间的多押继续放行，异时候选在本批次保持 Pending，等待后续帧或超时。Mine 是独立观测者，不检查也不武装批次锁。EX-Tap 与 Tap 一样参与锁。判定触点不被消费，可复用于多个空间重叠的普通 Note。

### 5.3 空间重叠

普通 note、持续音符的开始头判及 Hold 接触使用 `[P,P+W]`，并在两侧各扩展一半
`CommunityTouchWidth=0.40`（每侧扩边 `0.20`）。每颗 Note 独立扫描本帧触点；同轨同刻且空间重叠的普通 Note 可以共享同一触点，触点不会被消费。Mine 使用精确范围，
不应用触摸宽度扩边。
Mixer 放置和持续接收单独使用 §6.3 的接收头范围，不复用普通触摸扩边。

### 5.4 Drag 与 Mine phase

- Drag：Perfect 窗早侧外半段只接受 Began；早侧内半段和晚侧接受 Began/Moved/Stationary。
- Mine：只在到点前 Perfect 窗内危险；外 3/4 只接受 Began，最后 1/4 接受三种 phase。
- Mine 到点仍未触发时安全结算为 Perfect。

## 6. Hold 与 Mixer

### 6.1 路径

legacy-direct 的 `SustainPath.BoundsAt(time)` 对相邻节点左右缘做线性插值；v2 使用自身路径 evaluator 和精确元数据，保留 curve 与 `judge:false` 塑形节点。
Hold 判定来自实际判定节点，Mixer 从头部相位按 `1/8 chart bar` 派生。空 BPM legacy 时间线使用路径 BarTime/烘焙秒做局部插值回退。

### 6.2 Hold

- 头判命中后进入 Holding。
- 短 Hold 的尾和中间节点均等待头判完成，不在头仍处于 Late 候选窗口时提前 Miss。
  若合法接头时尾已经过去，头按实际偏差评级，尾直接 Perfect；头 Miss/超时仍一次性批量 Miss，
  不扩大既有判定窗，也不能复活已经失败的 Hold。
- Hold 的判定与 Body 裁剪相互独立：头时刻过后但仍在有效 Late 窗口内时，玩家仍可按
  实际时间差正常判定并接起；Body 过判定线的部分始终裁掉，未判定头部按 §9.1 下穿。
- 成功判定无论 Early、Exact 或 Late，Hold 头、瞬时爆发和持续接触效果都锚定判定线。
  已接起的头部沿线位置和宽度每帧随当前路径插值更新，不再使用起点的固定几何。
  Early 接起时，第一段 Body 的近端提前连接到判定线，直到谱面头实际到线后再恢复常规
  连续裁剪。
- Hold 判定由 shared `HoldContactFrame` 按输入时间戳重放完整触点快照，在各事件及尾/帧边界查询路径范围；
  同刻事件合成一个快照，只检查成功接头之后、尾时刻之前（含尾）的事件，支持同帧 Press/Release 与换指。
  渲染仍使用帧末触点；路径因自身运动产生的无输入断触在帧边界采样。
- 首次失去全部覆盖时按当时 BPM 冻结宽限 deadline，之后不逐帧重算；恢复覆盖清除本次失联状态。
- legacy 头部和每个实际路径节点为主判定；v2 只对头和 `judge:true` 节点判定，其他节点仍塑形，尾必须判定。
- 头部 Miss 时，全部未结算节点在同一帧批量 Miss；之后不能重新接回，也不会在未来
  到线时重复结算。整条 Hold 的头、Body、描边和尾在退场期间继续下落，Body 继续按判定线裁剪，从各自当前透明度
  开始在 180ms 内同步渐淡后回收；暂停时渐淡也冻结，尚未入场的后续节点不再生成。
- 抬手只在完整快照已无其它覆盖时记录实际 release 时刻；若另一根同轨触点仍覆盖当前条体，
  则不建立断触。冻结 deadline 前重新覆盖可清除断触，deadline 已在尾之前耗尽则不可接回，
  即使耗尽和再次接触发生在同一个渲染帧内也如此。
- 确认提前断开时，尚未结算的中间节点判 Miss，Hold 尾按
  `Judge(tailTime, releaseTime)` 的普通窗口评级，而不是按宽限耗尽时刻评级。
  确认断开后同样将整条渐淡后回收；宽限内的暂时断触仍保留恢复机会。
- 持有到尾为 Perfect；尾时刻之后才到达的 release 事件钳到尾时刻，因此不会把已经完整
  持有的 Hold 误判为 Late/Miss。

### 6.3 Mixer

- Mixer Body 始终按路径正常渲染；中间节点和尾节点不渲染实体。
- 静态头只作入场提示；到线或提前成功头判后回收。动态头一旦放置，保留到路径结束，松手和漏接都不隐藏。
- 短按可以直接重新放置；控制指可以滑动。候选头位置只有能接住当前身体中心线才被接受，否则保留最后有效位置，不钳到身体边缘。
- 接收半宽为当前路径 width/2；数值比较容差为 `1e-10` 谱面单位。显示皮肤的 0.95 内缩与光晕不参与判定，不增加普通触摸扩边。
- 有效新按下接管；当前控制指的有效移动更新位置。头已经接住时，其余静止指不抢占；头未放置或接不住时，从可接住的静止/移动触点选距旧头最近者，等距按 id 升序。触点不消费，同次输入可以同时判普通 Note 与放置一个或多个 Mixer。
- 松手只解除控制，头在原位继续接收。身体真正离开头范围时开始 `1/8 chart bar` 宽限，沿实际谱面时间推进、跨 BPM 不冻结秒数或钳 BPM；重新接住清除本次宽限。
- 持续节点在接住或宽限内判 Perfect；达到宽限结束且仍未接住判 Miss，不追溯撤销旧节点。漏接不是永久失败，后续可以重接。
- 成功的提前 MixerStart 可以初始化头；未获准的开始前短按不能放置。漏掉 MixerStart 不阻止开始后的接入，未放置时不自动生成可得分的头。
- 输入、路径边界和节点按时间戳重放；同刻输入先于节点。线性/二次/三次/PCHIP 按多项式边界求根，Hold 曲线跳变显式分段，避免漏接计时取决于渲染帧。
- Mixer 不使用普通 Note 的 Miss 下穿：头判 Miss 时静默回收静态头，不生成下穿或打击
  爆发；Body、动态头和随时重接逻辑不受影响。
- 判定点为 `head + k * 1/8 chart bar`，从 `k=0` 的 Mixer 头开始，只保留不晚于路径尾的
  点。非网格尾不补判；持续 1 bar 时共有 9 次判定。
- 每个八分点都是独立主判定，依据该时刻接收/宽限状态结算，也不再
  汇总命中率生成额外尾判（原版另有 100%/70%/50% 尾判，见原版判定分析 §5.5；社区是否
  对齐待拍板）。

## 7. 判定单元与统计口径

`JudgePlan` 将谱面展开为按时间排序的最小判定单元：

- Tap、Drag、EX-Tap、Mine 各一个主判定。
- Hold 包含头和实际判定节点（legacy 全部路径节点、v2 resolved `judge:true` 节点），尾必判。
- Mixer 从头开始每 `1/8 chart bar` 产生一个主判定，非网格尾不补判。
- BarLine 不生成判定单元。

Hold 节点和 Mixer 八分点都进入 Combo、P/Great/Good/Miss、Score、Health、Boost 和
CLEAR。`HeadlineUnitCount` 与 `TheoreticalMax` 均由展开后的判定单元重算，不采用旧格式
`Baked_TotalMainNote` 的口径。

`JudgeResolution` 保留 Pending、AutoMiss、InputMiss、Good、Great、Perfect；UI 将其映射为
Perfect/Great/Good/Miss 四档。Perfect 统一显示 `PERFECT`，不显示 E/L；Great 和 Good
保留 E/L，供玩家校准时序。

## 8. 计分、CLEAR、Health 与 Boost

### 8.1 原始得分

| 类别 | Perfect | Great | Good | Miss |
| --- | --- | --- | --- | --- |
| Tap/Drag/Mine、Hold 头与节点、Mixer 八分点 | 100 | 70 | 50 | 0 |
| Chain（保留类别） | 50 | 35 | 25 | 0 |

显示和存档分数统一为：

```text
round(RawScore / TheoreticalMax * 1,000,000)
```

结果钳制在 0..1,000,000，全 Perfect 必须严格等于 1,000,000。

### 8.2 CLEAR 与评级

- HUD 实时 ACC = 当前 RawScore / 已判定单元的理论满分，显示值钳制在 0..100%。
- 结算 CLEAR = 最终 RawScore / 全谱 TheoreticalMax。
- 评级（从高到低、未舍入 CLEAR）：Ω=100、Ψ≥99、X≥97、S≥95、A≥90、B≥80、C≥60，否则 D。
  Ψ 使用内部键 Y；显示舍入成 100.00% 不代表 Ω。旧成绩按持久化 Acc 重算等级，不改变原成绩数据。
- 存档字段 `acc` 为兼容旧格式保留，实际内容是结算 CLEAR。

### 8.3 Health 与 Boost

Health 使用资产基础值后按 `floor(value*600/HeadlineUnitCount)` 缩放，默认上限 10,000；
Boost 按 `floor(value*100/HeadlineUnitCount)` 缩放并钳制到 0..3000。

当前只计算和钳制这两个值，不显示 UI，也不触发 GameOver 或 EX Boost。
`OriginalJudgeMath` 中的原版 Combo 倍率、raw score 和 CLEAR 公式仅作策略参考，不接入
社区版显示与存档。

## 9. 1920×1080 布局

Godot viewport 固定 1920×1080，stretch mode 为 `canvas_items`，比例不符时保留黑边。
游玩布局不做自适应重排。

| 项目 | 当前值 |
| --- | --- |
| Center 判定线 | `y=861` |
| Mixer 装饰条 | `y=632`、`x=675..1244` |
| Center 左缘 | `x=280+273.2·P` |
| Center 视觉宽 | `W·273.2·0.95` |
| Center 轨中心 | `x=960` |
| 左/右侧线 | `x=184/1736` |
| 侧轨命中 y | `840−115·(P+W/2)` |
| 侧轨条视觉长度 | `W·102·0.95px` |
| 侧轨 BarLine 长度 | `W·190·0.95px` |
| Center/Side 可见行程 | `790px / 691px` |
| Side 距离尺度 | `691/790 ≈ 0.8747`（当前保留；2026-10-17 用户实机反馈基本通过） |

连接体作为刚性形状整体下落，过判定线部分被连续裁剪。远端允许出屏，不钳制；侧轨
Hold 不做远端缩窄，只按接近判定线的距离提高填充和描边 alpha。

HUD、背景和命中辉光均为程序化 UI。Full 结算先合拢不透明屏风，完全遮住后才隐藏舞台/音符/HUD，并在屏风背后铺结果背景；Reduced/Off 直接不透明接管。不能在屏风未闭合时先隐藏游玩层造成跳变，详见 [结算 v2](<result-v2-design.md>)。

### 9.1 Note 材质与判定反馈

- 可见 Note 使用 `client/shaders/note_material.gdshader`：与 `design/note-light-v1` 的 WebGL
  实现逐像素对照通过的单色程序化材质（九宫格遮罩的体面明暗 + 发光核心 + 外光晕，`u_kind`
  区分 EX 端标、Mixer 徽章、Mine 叉标）；左右侧轨由 mesh 子节点旋转 90° 复用同一套参数与
  同一份遮罩（遮罩 PNG 字节内嵌在 `NoteMask.cs`，运行期解码，不走导入管线）。BarLine 继续
  使用旧的 `note_surface.gdshader` 切角渐变材质。
- 尺寸：Center 的 Tap/EX/Drag/Mine/Mixer 为 24px、Hold 26px、BarLine 6px，侧轨厚 17px。
  Mixer 本体由 40px 改为 24px，使 38px 徽章按 `u_size.y/24` 只外扩 7px。
- 成功判定按 Note 类型生成 12 帧式程序化爆发，并按 Note 宽度和轨道方向适配；统一亮度
  系数为 1.55，并叠加 GPU 火花/余烬层，参数见 [动效规格 §9.5](<ui-motion.md#95-已接入游戏的打击粒子规格编辑器复用目标>)。
- Hold 与侧轨 Mixer 接通时使用 12 帧循环接触效果。亮度仅在接通后的 0.28 秒内从零爬升
  到亮度系数 0.72，随后保持不变，只有内部纹理继续循环；松开后立即消失。中心轨 Mixer 不显示
  这层侧面接触效果。

玩家可在设置页 EFFECTS 调整以下表现参数（只影响视觉，不参与判定与计分，全部持久化到
`user://settings.json`）：

| 设置 | 行为 |
| --- | --- |
| `HIT EFFECTS` | FULL：爆发 + 火花 + 余烬 + Hold/Mixer 接触；REDUCED：爆发 + 半量火花（无余烬）+ 接触；OFF：只保留命中残影与判定浮字。粒子密度只由该档位决定，`motionMode` 只负责界面动效 |
| `NOTE BRIGHTNESS` | 80–140%（5 步进），默认 130；100% 即本节三个 energy 设计原值 0.72 / 1.28 / 2.57，百分比同比例缩放下落、接触与残影峰值 |
| `FLASH INTENSITY` | 60–150%（5 步进），默认 100；按比例缩放爆发的半径与展开范围（`GameplayHitBloom.Intensity`） |
| `HIT RESIDUAL` | OFF / 0.20s / 0.30s / 0.45s，默认 0.30s；控制判定线上命中光斑的停留时长，0 即关闭残影（`hitResidualMs`） |
| `JUDGE TEXT` | 开/关（GAMEPLAY 页），关闭后不再显示 PERFECT / GREAT / GOOD / MISS 浮字 |

视觉运动与逻辑判定窗口相互独立，当前生命周期如下：

| 类型 | 到线及越线 | 判定反馈 |
| --- | --- | --- |
| Tap / EX-Tap / Drag | 未判定时立即连续越线；0–24px 满亮，随后 40px 淡出，累计 64px 回收；完整 Late 窗仍有效 | 命中时本体当帧消失，判定线上生成残影（默认 0.30s，设置可调；峰值亮度 = 2.57 × 音符亮度，约 0.115s 指数衰减叠加线性淡出）与爆发；early/late 的残影也固定在判定线，不跟随命中瞬间的本体位置；最终 Miss 不回线、不重启动画 |
| Hold | 未判定头部沿用普通下穿；Body 线外部分持续裁剪，Late 窗仍可接起 | Early/Exact/Late 命中后头部沿线跟随当前路径的位置和宽度；暂时断触时可恢复地下穿并淡出，宽限内接回则立即回线；正式 Miss / 确认断开后继续下落和裁剪，同时在 180ms 内渐淡回收，后续节点不再入场 |
| Mixer | Body 正常渲染，不使用普通下穿 | 已放置动态头保留到尾，松手/漏接不隐藏，宽度随路径更新；接收特效只在实际接住时播放，宽限保分不冒充接触；Miss 不生成爆发 |
| Mine | 不使用普通下穿 | 危险窗内触发时生成红色爆发并回收；安全到线直接回收，无爆发和残留 |
| BarLine | 到线立即回收，不下穿、不停留 | 无判定 |

## 10. 设置、音频与持久化

| 设置 | 范围/行为 |
| --- | --- |
| 判定偏移 | −300..+300ms，5ms 步进，接入 `SongClock.UserOffsetMs`；除设置页步进器外，校准向导页是第二个写入方（建议值 = 当前 − 击打偏差中位数 → 5ms 取整 → ±300 钳制） |
| 落速 | 1..20，默认 10，倍率 `level/10`；校准试打台沿用同一倍率，保证测量条件与实战一致 |
| 打击特效 | `hitEffectLevel`（full/reduced/off，默认 full）；旧存档缺该字段时由 `gameplayEffectsEnabled` 迁移（false → off），新存档仍写该布尔以兼容旧客户端 |
| 音符亮度 / 爆发尺度 | 80..140 / 60..150，5 步进，默认 130 / 100；100 即设计原值，见 §9.1 |
| 命中残影 | 0 / 200 / 300 / 450 ms，默认 300；0 = 关闭 |
| 判定浮字 | 开/关，默认开；关闭后 `FlashJudge` 不再出字 |
| Music/Hit/UI 音量 | 0..100，写入对应 Audio Bus |
| 校准目标样本 | 8..32、4 拍一档、默认 16（`calibrationTargetSamples`）；采样途中步进器锁定 |

`SongClock` 的窗口模式时间为：

```text
player.GetPlaybackPosition()
+ AudioServer.GetTimeSinceLastMix()
- AudioServer.GetOutputLatency()
+ userOffset
```

headless 模式改用系统计时器。暂停会记录当前位置并停止流，恢复时从记录位置重新播放。
判定偏差的方向约定：`JudgeEngine` 的 delta = inputTime − noteTime（正 = 晚），inputTime 含
`+userOffset`——所以玩家打得晚要靠**减小**偏移补偿，校准向导的建议值因此是「当前 − 中位数」。
校准页读取时钟时用同一公式但不叠加 userOffset（那是被测量本身）。

成绩写 `user://scores.json`：strict v2/成功转换路径使用 `(packId, chartId, rulesetId, gameplayDigest)`；legacy-direct 保留 `packId:diff`。Auto 不写成绩；当前没有独立 Standard/Hardcore 运行模式维度，不能称分模式隔离已完成。
Music/Hit/UI 音量总线已接，但完整 Hit/UI 资产链路仍未完成。

## 11. 仍未确认的边界

以下项目不阻塞当前主体玩法，但不得写成原版定论：

- 设备 `NSTouchWidth` 的真实值及缩放规则。
- 同帧多个触点各自扫描候选 Note；同一触点可独立命中多颗同刻空间重叠 Note，不存在全局消费顺序。
- 真机三指以上同时输入时，Godot/OS 触点 id 的稳定性与事件完整性。
- 窗口失焦已接 `NotificationWMWindowFocusOut → ClearPointerState()`（2026-10）；触摸取消事件的平台投递差异与端到端 pointer 序列回归仍待验证。
- 原版 BPM 切段同帧调度仍有研究未知；社区宽限固定为首次断触冻结，不是当前未实现项。
- 模式/角色提供的真实 MaxHealth。
- 当前版本序列化 Type 到内部 judge dispatch 的最终映射。
- Buff/EX Boost 来源、持续时间、等级和叠加规则。
- 空 BPM 时间线正式谱的原始 BPM；当前 loader 依靠 `Baked_Second` 可正常游玩。

## 12. 复验入口（非本次通过声明）

从 **community 根目录**执行：

```powershell
python -B tools/verify.py
# 依赖已还原可加 --no-restore
```

分项命令与最近实际执行/历史验证统一见 [验证状态与操作](<verification.md>)；两套核心/编辑器回归使用 `dotnet run`。
现有核心回归包括谱面加载/精确时间、几何/镜像、Auto、窗口/EX-Tap/Mine、精确 double 同刻批次锁（含早/晚侧结算）、触点缓冲、Hold/Mixer 时间重放、归一化计分/成绩身份和界面纯状态。
测试存在或历史通过不等于本次重新执行，也不能证明全设备触控、音频或性能通过。
