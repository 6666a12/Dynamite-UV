# Dynamix Universe 原版判定机制逆向报告

> 分析对象：`Dynamix Universe 00.18.00`（Unity IL2CPP）
> 报告日期：2026-08-10
> 用途：为 DUX-Community 的 clean-room 重写提供行为规格，不包含原版素材、谱面、代码或二进制。

## 0. 结论摘要

本报告记录已经闭环到方法体级别的判定结论。

1. 普通判定窗是固定毫秒值。资产虽然以 BarTime 保存，但运行时统一用
   `StandardBPM=150` 换算；不会随歌曲当前 BPM 改变。
2. Hold/Mixer 的 Holding tick 间隔才使用当前 BPM，并把 BPM 钳制到
   `[120, 200]`。`0.125 bar` 因而对应 250–150 ms，而不是始终 200 ms。
3. 输入模型不是“三轨是否按住”，而是逐触点的有效标志、NS 横坐标与 phase。
   普通按下只接受 phase `1`；Chain 和 Mine 在临近命中时会接受持续接触 phase
   `1..3`。
4. 原版内部保留两种 Miss 来源：超时自动 Miss 与有效错误输入 Miss。两者计分都
   落入 Miss 档，不存在额外得分档 Bad。
5. Hold 每帧插值条体当前位置，允许一个 Holding tick 长度的断触宽限；Mixer 是
   独立的虚拟滑块状态机，没有超时失效，断开后可随时重接，并按 Holding 命中率结算尾判。
6. 原版 raw score 带 1.0x–1.5x Combo 倍率，300 Combo 封顶；CLEAR 则与 raw score
   分离，只按主判定的 `100/70/50/0` 权重计算。
7. 原版成功 Holding tick 会增加 Combo，失败 tick 会断 Combo。社区版目前让 tick
   不影响主 Combo，是合理但明确不同于原版的产品决策。
8. Health/Boost 的资产值不是直接加到固定槽位，而会按谱面主判定总数缩放。

## 1. 证据范围与可信度

### 1.1 证据来源

- JudgeSettings 的 4 个资产实例：窗口、BPM 边界、Score/Health/Boost 表。
- `lib_burst_generated.so` ARM64 静态叶函数：Tap、Burst、Chain、Mine、HoldStart、
  MixerStart 的时间、空间和触摸 phase 判定。
- `libil2cpp_dump.bin` ARM64 方法体：常量捕获、Hold/Mixer 状态机、OnNoteHit、
  Combo、CLEAR、Health/Boost 结算。
- `metadata_dump.txt`：方法名、参数名与字段名，仅用于给已反汇编的方法恢复语义。
- 社区版现有 `JudgeEngine.cs` 与 `GameplayMain.cs`：只用于差异分析，不作为原版证据。

本报告只记录数值、状态机和伪代码。地址用于复核分析，不能视为可移植 API；换版本后
RVA 很可能变化。

### 1.2 标记约定

| 标记 | 含义 |
| --- | --- |
| **确认** | 资产值与方法体/调用点可以闭环，或控制流直接可见 |
| **强推断** | 控制流已知，但字段名、业务名依赖 metadata 或相邻调用恢复 |
| **UNKNOWN** | 当前证据仍不足，不能写成兼容规格 |
| **社区决策** | DUX-Community 已拍板行为，不声称来自原版 |

## 2. 时间轴与判定状态

### 2.1 Delta 的方向（确认）

原版叶函数接收的时间差是：

```text
noteDeltaSecond = noteTime - currentTime
```

- 正数：玩家输入偏早，音符尚未到点。
- 负数：玩家输入偏晚，音符已经过点。

社区版使用 `inputTime - noteTime`，符号方向相反；Early/Late 映射和绝对窗口比较保持一致。

### 2.2 内部结果值（确认）

| 值 | 语义 | 计分档 |
| ---: | --- | --- |
| 0 | Pending，尚未结算 | 无 |
| 1 | AutoMiss，超时且没有有效输入 | Miss |
| 2 | Miss，有效输入但不在 Good 内 | Miss |
| 3 | Good | Good |
| 4 | Great | Great |
| 5 | Prefect | Prefect |

原版代码与资产均拼作 `Prefect`。状态 `2` 可以理解为“输入造成的 Miss”，但它不是
独立的 Bad 计分等级。状态 `1` 与 `2` 都会进入 GradeValue 的 Miss 列；区分它们的价值
在于输入反馈、状态机迁移与统计诊断。

### 2.3 普通时间档（确认）

对需要按下的普通音符，最终档位是：

```text
abs(noteDeltaSecond) <= prefectSecond  -> Prefect
                     <= greatSecond    -> Great
                     <= goodSecond     -> Good
有效错误输入                              -> Miss (state 2)
晚于 missSecond 且仍无输入                 -> AutoMiss (state 1)
尚未到可结算时刻                           -> Pending (state 0)
```

Tap/Burst 叶函数在“输入过早且超出 Good”的路径中没有自行检查 `missSecond` 上界。
因此它不是完整的候选选择器，调用层仍应负责限制过早候选。不能据此实现“任意早的输入
都会打掉未来音符”。这是本报告刻意保留的调用层 UNKNOWN，见 §10。

## 3. 判定窗换算

### 3.1 资产原值（确认）

| 难度资产 | Prefect | Great | Good | Miss |
| --- | ---: | ---: | ---: | ---: |
| Casual / Tutorial | 0.0625 | 0.09375 | 0.125 | 0.15625 |
| Normal | 0.0390625 | 0.09375 | 0.125 | 0.15625 |
| Hard 及以上 | 0.0390625 | 0.0703125 | 0.1015625 | 0.15625 |

共同参数：

```text
MinBPM = 120
MaxBPM = 200
StandardBPM = 150
HoldHoldingJudgeBarTime = 0.125
MixerHoldingJudgeBarTime = 0.125
```

### 3.2 普通窗固定使用 StandardBPM（确认）

换算公式：

```text
seconds = barTime * 240 / BPM
```

Prefect/Great/Good/Miss 的方法体读取 `StandardBPM`，所以得到固定双侧窗口：

| 难度 | Prefect | Great | Good | Miss/候选外窗 |
| --- | ---: | ---: | ---: | ---: |
| Casual / Tutorial | ±100 ms | ±150 ms | ±200 ms | ±250 ms |
| Normal | ±62.5 ms | ±150 ms | ±200 ms | ±250 ms |
| Hard 及以上 | ±62.5 ms | ±112.5 ms | ±162.5 ms | ±250 ms |

Hard 及以上包括 Mega、Giga、Tech、Tera、Legacy、Another，均引用 Hard 资产。

### 3.3 Holding tick 使用当前 BPM（确认）

Hold/Mixer Holding 的换算先执行：

```text
effectiveBpm = clamp(currentBpm, 120, 200)
tickSecond = 0.125 * 240 / effectiveBpm
           = 30 / effectiveBpm
```

典型值：

| 当前 BPM | 有效 BPM | tick 间隔 |
| ---: | ---: | ---: |
| 90 | 120 | 250 ms |
| 120 | 120 | 250 ms |
| 150 | 150 | 200 ms |
| 200 | 200 | 150 ms |
| 240 | 200 | 150 ms |

结论是普通窗口固定，只有 Holding 间隔随当前 BPM 变化。

## 4. 触摸与空间判定

### 4.1 触点结构（确认）

静态判定函数逐个读取当前帧触点：

```text
valid : byte
x     : float   // NS 坐标，不是屏幕像素
phase : int
```

普通 Tap/Burst/HoldStart/MixerStart 只接受 `valid != 0 && phase == 1`，也就是新按下。
Chain/Mine 会在更接近命中点的窄窗接受 phase `1..3`，从而允许手指持续滑过。

### 4.2 普通命中范围（确认）

`Position` 是左边缘，`Width` 是宽度。普通按下与 Hold/Mixer 接触使用：

```text
noteLeft  = Position
noteRight = Position + Width
halfTouch = NSTouchWidth / 2

hit = touchX >= noteLeft  - halfTouch
   && touchX <= noteRight + halfTouch
```

等价地说，触点具有 `NSTouchWidth` 的接触宽度，音符范围向两侧各扩半个触摸宽度。

Mine 是例外：危险范围严格为 `[Position, Position + Width]`，不使用上述扩边。

### 4.3 候选、多点触控与目标时间锁（确认）

叶函数会扫描全部有效触点，而不是只保留每轨一个布尔值。每个 note runner 还保存上次
绑定的音符时间，避免同一 runner 在状态未切换时误处理另一颗音符。

`NoteJudgeSystem__Manual` 主循环（RVA `0xbb72d64`）还为一次输入更新创建共享的
`JudgeState`：约 `0xbb72f4c` 把目标时间初始化为 `-1`，约
`0xbb72f68..0xbb72f74` 清零本批判定标志，然后把同一状态传给 Tap、Burst、Chain、
HoldStart、MixerStart 等叶函数。

Tap 叶函数 `0x498e18` 证实了“异时同按锁”：在音符尚未到点的 `noteDelta >= 0`
分支，如果本批已经接受过输入且 `JudgeState.time != note.Time`，该音符直接保持
Pending；时间相同则继续扫描触点并允许命中。因此一次输入更新中的行为是：

```text
第一颗被接受的目标时间 -> 锁定
完全同刻的 Note          -> 继续允许（合法多押）
不同时间、尚未到点 Note  -> Pending，必须重新按下
```

这里比较的是原版 `float` 目标时间的精确相等，不是“判定窗重叠就算同刻”。普通
Tap/HoldStart 等同时要求触点 phase `1`，所以提前按住也不能覆盖后续异时 Note。
过点后的晚侧分支没有完全相同的 timestamp 检查，因此它不是绝对的“一次触点只消费
一颗音符”；核心作用是阻止高密度未来 Note 被同一批新按下一起提前吃掉。

## 5. 各类音符的判定状态机

本节使用反编译内部的 judge 名称。它们描述行为类别，不直接声明社区谱面 Type 编号；
序列化 Type 映射的冲突见 §9。

### 5.1 Tap / Burst / HoldStart / MixerStart（确认）

- 同时检查时间窗与 NS 空间重叠。
- 只接受 phase `1` 的新按下。
- Tap 与 Burst 的时间和空间控制流一致；差异在调用层类别、资源或后续结算。
- HoldStart/MixerStart 使用同类头判逻辑，成功后才进入各自持续状态机。

静态函数入口：

| 函数 | `lib_burst_generated.so` 偏移 |
| --- | ---: |
| `_judgeTap` | `0x498e18` |
| `_judgeBurst` | `0x491ae4` |
| `_judgeHold_Start` | `0x49440c` |
| `_judgeMixer_Start` | `0x4936b8` |

### 5.2 Chain / 连续接触类（确认）

Chain 节点不是“到点必须重新按一次”。到达前 Prefect 窗被分成两段：

- `Prefect/2 < noteDelta <= Prefect`：只接受 phase `1`，防止很早就按住后无条件吃点。
- `0 <= noteDelta <= Prefect/2`：接受 phase `1..3`，手指持续滑过可以命中。
- 过点后继续按 Prefect/Great/Good/Miss 时间档结算。

这说明 Chain/Drag 的正确抽象是“带 phase 的空间接触”，而不是在节点时刻读取轨道
`pressed` 布尔值。静态函数入口：`0x49856c`。

### 5.3 Mine（确认）

Mine 是到点前触发的危险区：

- 空间范围严格为 `[Position, Position + Width]`，不加 `NSTouchWidth/2`。
- 在到点前的短窗内接触会结算 Miss。
- `Prefect/4 < noteDelta <= Prefect` 时只接受 phase `1`。
- 最后 `Prefect/4` 内接受 phase `1..3`，所以按住滑入雷区也会触发。
- 到达音符时刻仍未触发，则立即安全结算 Prefect。

Mine 不是对称的 ±Miss 窗普通 Tap，也不应按“整条轨道在 ±250 ms 内被按下”处理。
静态函数入口：`0x4941d4`。

### 5.4 Hold（确认）

真实持续函数位于 `libil2cpp_dump.bin` RVA `0xbb74554`，位置插值辅助函数位于
`0xbb7fb20`。

每帧流程为：

1. 找到当前时间两侧相邻的 Hold 节点。
2. 分别对节点左右边缘按时间线性插值，得到此刻条体的 `Position` 与 `Width`。
3. 用 `NSTouchWidth/2` 扩展后的范围扫描当前触点。
4. 有重叠则把断触累计清零；没有重叠则累加 `frameDeltaTime`。
5. 断触累计超过 `HoldHoldingJudgeSecond` 才真正断开。短暂抬手、触点采样间隙或轻微
   滑出不会立即判死。
6. 每经过一个 Holding 间隔生成 tick；若一帧跨过多个间隔，会补发缺少的 tick。

结尾规则：

- 正常持有到尾：`HoldEnd = Prefect`，不要求尾点重新按下。
- 提前断开：按断开时刻距离尾部的时间差，使用 Prefect/Great/Good/Miss 阈值结算
  HoldEnd；离尾部越近，尾判越高。
- 真实断开后未发现可重接路径；这与 Mixer 可随时恢复连接的机制不同。

### 5.5 Mixer（确认）

Mixer 主函数位于 RVA `0xbb738fc`。它不是换皮 Hold，而是虚拟滑块状态机：

1. Mixer Body 沿节点路径正常存在；控制节点本身不作为可见 note 渲染。
2. 从 MixerStart 时刻起，当前插值条体范围内存在同轨有效触点时即为已连接；虚拟滑块
   跟随最近触点，并由条体边界修正位置。
3. 已连接时在判定线上的虚拟滑块位置渲染头；未连接时不渲染该动态头。
4. Mixer 没有持续越界超时，也不会进入不可恢复的 Miss 状态。触点离开时断开，之后
   Began、Moved、Stationary 任一有效触点重新进入当前条体范围都可立即恢复。
5. MixerStart 漏判只影响头判本身，不会阻止后续 Body 重接。
6. 每个 `MixerHoldingJudgeSecond` 只按 tick 当时的连接状态结算 Prefect 或 Miss。
7. MixerEnd 不要求尾部重新按键，而按整条 Holding tick 命中率自动评级。

尾判：

```text
holdingHitRate == 100% -> Prefect
holdingHitRate >= 70%  -> Great
holdingHitRate >= 50%  -> Good
otherwise              -> Miss
```

`70%` 的二进制常量为 `0.699999988`。相关辅助函数：

| 作用 | RVA |
| --- | ---: |
| 更新虚拟滑块 | `0xbb758c0` |
| 计算滑块边界 | `0xbb75aa0` |
| 重接检查 | `0xbb75b7c` |
| 扫描三轨新触摸 | `0xbb75ee8` |
| MixerStart fallback | `0xbb7fd5c` |
| 重接 fallback | `0xbb802e8` |

## 6. 计分与 Combo

### 6.1 基础分（确认）

| 类别 | Prefect | Great | Good | Miss |
| --- | ---: | ---: | ---: | ---: |
| Tap/Burst/Mine/HoldStart/HoldEnd/MixerStart/MixerEnd | 100 | 70 | 50 | 0 |
| Chain | 50 | 35 | 25 | 0 |
| HoldHolding/MixerHolding | 10 | 0 | 0 | 0 |

查表函数 `GradeValue` 位于 RVA `0xbb5ac44`。

### 6.2 Combo 倍率与单次得分（确认）

`OnNoteHit` 主体位于 RVA `0xbb5bbd8`。原版单次 raw score 为：

```text
comboFactor = 10 * (1 + 0.5 * clamp(comboBeforeHit / 300, 0, 1))
scoreDelta  = floor(baseJudgeScore * comboFactor * buffScoreFactor)
```

若把固定的 `10` 视为基础缩放，Combo 倍率就是从 `1.0x` 线性增长到 `1.5x`，在
300 Combo 封顶。当前命中使用“命中前”的 Combo。

`buffScoreFactor` 初始为 `1.0`，再累加活动 Buff 的得分加成；聚合函数位于
`0xbb5c564`。各 Buff 的具体来源、叠加上限仍不在本报告范围内。

### 6.3 Combo 成败（确认）

- Prefect/Great/Good 都是成功命中并增加 Combo。
- AutoMiss 与输入 Miss 都会把 Combo 清零。
- 原版成功 HoldHolding/MixerHolding tick 也增加 Combo。
- 原版失败 Holding tick 会断 Combo。

因此原版 Combo 不等于谱面 `Baked_TotalMainNote` 的主判定计数，长条内部 tick 也会
显著抬高 Combo。社区版把 tick 排除出 Combo 与主统计，是另一套更清晰的产品口径。

## 7. CLEAR 百分比（确认）

真实入口位于 RVA `0xbb5d978`，数学函数位于 `0xb8d59c8`。输入仅包含：

```text
PrefectMain, GreatMain, GoodMain, TotalMainNote
```

公式：

```text
ClearPercent100 = trunc(
    (PrefectMain + 0.7 * GreatMain + 0.5 * GoodMain)
    * 10000 / TotalMainNote
)
```

- 返回范围是 `0..10000`；显示时除以 `100` 得到百分比。
- 全 Prefect 有显式快速路径返回 `10000`。
- Miss 权重为 0。
- Holding tick 不进入 Main 计数，因此不影响 CLEAR；但仍影响 raw score 和原版 Combo。
- 常量 `0.7` 的实际 float 为 `0.699999988`。

这意味着原版 CLEAR 不是 `rawScore / theoreticalMax`，也不受 Combo 倍率和 Buff 影响。
两位玩家拥有相同 P/GR/GD/M 主判定分布时，即使中间断 Combo 的位置不同，CLEAR 仍相同。

## 8. Health 与 Boost（确认）

JudgeSettings 中的表值还会按谱面规模缩放：

```text
healthDelta = floor(assetHealth * 600 / TotalMainNote)
boostDelta  = floor(assetBoost  * 100 / TotalMainNote)
```

随后执行：

- Health 钳制到 `0..PlayerMaxHealth`；未取得模式上限时可见 fallback `10000`。
- Boost 钳制到 `0..3000`。
- 不同难度资产仍决定基础 Health 惩罚，例如 Hard Mine Miss 的资产值是 `-1500`；
  该值会先按 TotalMainNote 缩放，再应用到模式血量上限。

社区版已经采用相同的 TotalMainNote 缩放公式，Health 默认上限为 10000，Boost 上限为
3000；当前只计算内部状态，不显示 UI，也不触发 GameOver。

## 9. Type 映射边界

方法体确认了 Tap、Burst/Contact、Mine、Hold、Mixer 等内部行为，但当前版本的序列化
Type 到内部 judge dispatch 尚未完成运行时终验。社区版只采用 `gameplay-spec.md` §3 的
权威映射：1 Tap、2 Drag、3/4 Hold、5 EX-Tap、6/7 Mixer、8 Mine、9 BarLine。

本报告中的内部类别名只用于描述算法，不用于重新解释谱面 Type 编号。

## 10. 仍未确认的边界

主体玩法已经可以实现，但以下边界仍不应写成原版定论：

1. **普通音符过早候选外边界**：叶函数不完整检查 early `missSecond`，需要继续追
   上层候选 job，或用 Frida 在极早输入时 trace 传入的 note 列表。
2. **同帧触点消费规则**：同一触点能否命中多个重叠 note、多个 note 如何争抢多个触点。
3. **phase 2/3 的业务枚举名**：数值与接受范围已确认，但 Move/Stationary 的准确名称
   尚未从当前版本枚举恢复，不应影响实现。
4. **NSTouchWidth 的运行时值与设备缩放**：字段及公式已确认，具体值可能随轨道、设备
   或设置变化。
5. **BPM 变化点上的 Holding 调度**：tick 使用当前 BPM 已确认；恰好跨 BPM 段时是按
   旧间隔补齐再换新间隔，还是直接重算 next tick，仍需边界 trace。
6. **Buff/EX Boost 完整规则**：得分因子入口与 Boost 上限已确认，但 Buff 来源、持续
   时间、等级和叠加上限尚未闭环。
7. **模式化血量上限**：fallback `10000` 已见，具体模式/角色如何提供 MaxHealth 未闭环。
8. **当前版本 Type dispatch**：机制已知，序列化编号到内部 judge 的最终映射仍应以
   运行时参数 trace 做终验。

这些未知项都可以隔离在输入适配、常量提供器和规则策略层，不妨碍先正确实现 Hold、
Mixer、Mine 与基础判定窗。

## 11. 与社区版现状的差异

| 项目 | 原版 | 社区版当前实现 | 判断 |
| --- | --- | --- | --- |
| 普通判定窗 | StandardBPM=150 固定 | 固定 150 BPM | 已一致 |
| Holding 间隔 | 当前 BPM，clamp 120–200 | 0.125 bar 动态生成，运行时宽限同样按当前 BPM | 已落地；BPM 边界调度仍按 §10 保留近似 |
| 输入 | 多触点 x + phase | 逐 id 触点快照、轨内 Position、phase 1/2/3 | 已落地 |
| 输入批次目标时间 | 同批早侧只放行精确同刻 | 按帧锁最早可判定 float32 时间组 | 核心机制已落地；晚侧仍采用统一锁的保守实现 |
| 空间候选 | note 范围 + 触摸宽度 | 实际 `[P,P+W]` + 集中可调触摸宽；Mine 无扩边 | 公式已落地；设备 NSTouchWidth 真值仍未知 |
| Drag/Chain | phase 分段的连续接触 | Prefect/2 phase 分段 + 空间接触 | 已落地 |
| Mine | 精确范围、到点前短危险窗 | Prefect/4 phase 分段、到点安全 Prefect | 已落地 |
| Hold | 实时条体插值 + 断触宽限 | 左右缘插值、动态宽限、补 tick、提前尾判 | 已落地 |
| Mixer | 虚拟滑块 + 无超时随时重接 + 命中率尾判 | 当前触点连接、动态头显隐、随时重接、比例尾判 | 已落地 |
| Miss 状态 | AutoMiss / 输入 Miss 分开 | `JudgeResolution` 分源，界面仍映射四档 | 已落地 |
| Combo | 含 Holding tick，300 连击倍率封顶 | tick 不影响主 Combo，无倍率 | 社区决策 + 差异 |
| CLEAR | 主判定 100/70/50 权重 | raw score / 理论满分 | 产品口径不同 |
| 显示分数 | raw score 随谱面长度与 Combo 变化 | 归一化 1,000,000 | 社区产品口径 |
| Health/Boost | 按 TotalMainNote 缩放，Health fallback 10000、Boost 上限 3000 | 已按公式缩放并钳制 | 已落地；模式化血量上限仍未知 |

## 12. 社区版落地与验证

当前规则分层为：

```text
GameplayMain 输入适配与逐触点状态
  -> InputTimeGroupGate / InputJudgeRules
  -> JudgePlan / SustainPath
  -> JudgeEngine
  -> HUD / ScoreStore
```

`OriginalJudgeMath` 单独保存原版 Combo/raw score/CLEAR 数学，不接入社区版百万分数和主
Combo。核心测试已覆盖窗口、动态 BPM、phase/空间边界、sustain 插值、时间组锁、Miss
分源、Health/Boost 缩放、原版数学策略和两份开发谱 Auto 全 Prefect。

仍需窗口模式确认的项目只有真实多点事件顺序、音频自然结束/暂停恢复，以及最新 HUD、
命中辉光和结算层的视觉效果。

## 13. 关键地址索引

### `lib_burst_generated.so`

| 方法 | 偏移 |
| --- | ---: |
| Tap | `0x498e18` |
| Burst | `0x491ae4` |
| Chain | `0x49856c` |
| Mine | `0x4941d4` |
| HoldStart | `0x49440c` |
| MixerStart | `0x4936b8` |

### `libil2cpp_dump.bin`

| 方法/作用 | RVA |
| --- | ---: |
| Hold tick 秒换算 | `0xbb5ab50` |
| Mixer tick 秒换算 | `0xbb5ab84` |
| Prefect 秒换算 | `0xbb5abb8` |
| Great 秒换算 | `0xbb5abd8` |
| Good 秒换算 | `0xbb5abfc` |
| Miss 秒换算 | `0xbb5ac20` |
| GradeValue | `0xbb5ac44` |
| OnNoteHit | `0xbb5bbd8` |
| Manual 主判定循环 / 共享 JudgeState | `0xbb72d64` |
| CLEAR 入口 | `0xbb5d978` |
| CLEAR 数学函数 | `0xb8d59c8` |
| Mixer 主状态机 | `0xbb738fc` |
| Hold 主状态机 | `0xbb74554` |
| Hold 位置插值 | `0xbb7fb20` |

以上地址和 JudgeSettings 资产数值共同构成本报告“确认”结论的复核入口。
