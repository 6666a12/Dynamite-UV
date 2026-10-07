# Dynamix Universe 原版判定、渲染与生命周期逆向报告

> 分析对象：`Dynamix Universe 00.18.00`（Unity IL2CPP）
> 报告日期：2026-08-13
> 项目名称说明：本文中的 `DUX-Community` 是 Dynamite Universe 的前身项目名；为保留研究证据链，历史表述不机械改写。
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
7. 原版成功 Holding tick 会增加 Combo，失败 tick 会断 Combo。社区版不复刻独立的
   Holding tick 统计口径，而把 Hold 实际路径节点和 Mixer 八分点作为完整主判定；这些
   单元都影响主 Combo。这是明确的社区产品决策。
8. Health/Boost 的资产值不是直接加到固定槽位，而会按谱面主判定总数缩放。
9. 普通候选只在 `note.Time-currentTime <= missSecond` 时进入判定，等号包含；原版没有
   per-touch consumed 或触点与 Note 一一配对，同一触点可以命中多颗空间重叠的 Note。
10. 判定只写终态并发出 `OnJudged`，不会当场销毁 Entity。Hold Body 采用固定网格拓扑，
    通过推进首截面和退化已过线截面连续裁剪；实际实体由独立 Dispose 系统稍后递归回收。
11. Mixer Body/Tail 与动态 Slider 头是分离的视觉链。Body 不依赖连接状态；断触主要影响
    动态头，尾后再由统一 Note Dispose 流程回收。

## 1. 证据范围与可信度

### 1.1 证据来源

- JudgeSettings 的 4 个资产实例：窗口、BPM 边界、Score/Health/Boost 表。
- `lib_burst_generated.so` ARM64 静态叶函数：Tap、Burst、Chain、Mine、HoldStart、
  MixerStart 的时间、空间和触摸 phase 判定。
- `libil2cpp_dump.bin` ARM64 方法体：常量捕获、Hold/Mixer 状态机、OnNoteHit、
  Combo、CLEAR、Health/Boost 结算、OnJudged 提交和 Note Dispose 生命周期。
- `lib_burst_generated.so` 的 Hold/Mixer mesh 生成与更新函数：固定拓扑、判定线裁剪、
  Mixer Tail 和 HorizontalStretch renderer 状态。
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

### 2.2 判定结果值（`NoteData+0x21`，确认）

| 值 | 语义 | 计分档 |
| ---: | --- | --- |
| 0 | Pending，尚未结算 | 无 |
| 1 | AutoMiss，超时且没有有效输入 | Miss |
| 2 | Miss，有效输入但不在 Good 内 | Miss |
| 3 | Good | Good |
| 4 | Great | Great |
| 5 | Prefect | Prefect |

原版代码与资产均拼作 `Prefect`。结果 `2` 可以理解为“输入造成的 Miss”，但它不是
独立的 Bad 计分等级。结果 `1` 与 `2` 都会进入 GradeValue 的 Miss 列；区分它们的价值
在于输入反馈、状态机迁移与统计诊断。

### 2.3 Note 生命周期状态（`NoteData+0x20`，确认）

`ENoteJudgeState` 与上面的判定结果是两个独立字段：

| 值 | 名称 |
| ---: | --- |
| 0 | `NotJudged` |
| 1 | `JudgeInProgressing` |
| 2 | `DelayJudging` |
| 3 | `JudgeEnded` |

`_calculateHoldPos` 路径 `0xbb77928` 会同时写生命周期状态 `2` 和判定结果 `1`，即
“已经生成 AutoMiss/Miss 结果，但延迟完成判定生命周期”。Tap/Burst 正常完成写生命周期
状态 `3`，与 `JudgeEnded` 闭环。因此 `+0x20 = 2` 不能解释成 Mixer 断触或 AutoMiss grade。

### 2.4 普通时间档（确认）

对需要按下的普通音符，最终档位是：

```text
abs(noteDeltaSecond) <= prefectSecond  -> Prefect
                     <= greatSecond    -> Great
                     <= goodSecond     -> Good
有效错误输入                              -> Miss (result 2)
晚于 missSecond 且仍无输入                 -> AutoMiss (result 1)
尚未到可结算时刻                           -> Pending (result 0)
```

Tap/Burst 叶函数在“输入过早且超出 Good”的路径中没有自行检查 `missSecond` 上界。
完整候选门位于 Manual 主循环 `0xbb73380..0xbb733a0`：

```text
note.Time - currentTime <= missSecond
```

比较使用 `b.gt` 排除严格大于，因此等于 `missSecond` 的边界仍会进入叶函数。全难度
`missSecond` 都是 250 ms，所以极早输入不会打掉任意远的未来音符，早侧候选外边界为
`+250 ms` 且包含等号。

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

换算到秒见 §3.2（×240/StandardBPM 150 = ×1.6）。

难度键到实例的映射（原版规则）：`casual`→Casual、`normal`→Normal、
`hard` 及以上（含 `mega`/`giga`/`tech`/自定义）→Hard、`tutorial`→Tutorial（数值等同
Casual）。社区实现见 `JudgeSettings.PresetForDifficulty`，全部判定窗（含按键扫描窗、
Drag 接触窗、Hold 断触宽限）一律从当前实例读取，不再硬编码。

EX-Tap（Type 5）已完全对齐原版（2026-09 起窗口倍率回 `1.0`，即与 Tap 同窗）。

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

Hold 与 Mixer 都不是缓存一次间隔后始终累加固定秒数：

- Hold 每帧用当前 BPM 重算 interval，从起点计算当前应达到的 ordinal，并补发缺少的 tick。
- Mixer 累积 frame delta，再除以当前 interval 补发本帧跨过的 tick。
- Hold 断触宽限也在每帧按当前 BPM 重算。

动态重算行为已经确认；仍未知的只是 BPM 切段同一帧中时间线系统和 JudgeSystem 的执行
先后，见 §10。

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

Manual 主循环对每颗 Note 都从触点数组索引 0 重新扫描，命中后只写 Note 自身状态，
不修改触点数组，也没有 per-touch consumed、owner 或 Note-touch 配对字段。因此：

```text
同一有效触点
  -> 可以被第一颗空间重叠 Note 接受
  -> 随后仍可被第二颗、第三颗空间重叠 Note 再次接受
```

Early 仍受上述目标时间锁约束：异时未来 Note 被锁挡住，完全同刻且空间重叠的 Note 可以
共享同一触点。多根触点也不是先做全局最优匹配，而是每颗 Note 独立扫描当前触点快照。

社区实现补充：

- **EX-Tap（Type 5）参与该时间锁**：候选照常被早侧锁检查，命中照常武装锁。RE 已确证
  `_judgeBurst` 与 Tap 在时间锁上逐指令一致（三个 Burst 克隆体字节一致），此前"EX 不
  参与锁"的推断作废。
- 锁的武装值是**本批已结算早侧目标里的最早时刻**（本仓库实现为毫秒整数比较），因此与
  事件到达顺序无关；晚侧命中不武装锁（原版晚侧分支没有 timestamp 检查，H1 修复）。

### 4.4 EX-Tap（Burst）二值判定（确认）

原版 Burst 叶函数没有 grade 3/4 的输出路径：

```text
接触到 Note 且 |Δ| <= Good 窗   -> grade 5（Prefect）
接触到 Note 且 |Δ| >  Good 窗   -> grade 2（输入 Miss），早侧晚侧一致
没有任何触点，直到超时           -> grade 1（AutoMiss）
```

即"窗内恒 Prefect、出窗即 Miss"的二值判定，没有 GR/GD 中间档，也没有晚侧 Pending 缓冲。

社区实现（已与原版一致）：

- 窗口倍率 `JudgePlan.ExTapWindowScale = 1.0`（与 Tap 同窗；2026-09 起从社区 1.5× 回退）。
- 二值判定：`JudgeUnit.IsExTap` 显式标记 T5 单元，`JudgePress(..., binaryExTap)` 把 Good
  窗内一律评为 Prefect，出窗（早/晚）立即按输入 Miss 结算，超时仍为 AutoMiss。
- 与"EX 参与时间锁"共同构成 T5 的完整原版化路径：锁与 Tap 一致，窗口一致，分级为二值。

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

### 5.6 Hold Body 网格与判定线裁剪（确认）

Hold Body 不是每一段各画一块再删除已经过线的段，而是创建固定拓扑并每帧重写顶点。
`FillNotePosData` 的正确范围是 `0xbb2f1a8..0xbb2f360`；`0xbb2f364` 实际已经是
`MeshVerticesCount`，旧地址标注不能再沿用。

对 `N` 个路径点，网格数量为：

```text
VertexCount:
  N < 2  -> 18
  N = 2  -> 36
  N >= 3 -> 24 + 6*N

IndexCount:
  N < 2  -> 60
  N = 2  -> 150
  N >= 3 -> 90 + 30*N
```

首尾各使用 18 个顶点/60 个索引；每个内部路径点增加 6 个顶点，每对相邻截面增加
30 个索引。精确截面形状还受 `SkinNote_HoldMesh__ComponentData` 的 half 参数控制，这些
字段的业务名尚未恢复。

裁剪流程为：

1. `FillMeshDataArray 0x51c344..0x51c574` 计算
   `yOffset = max(-Note_VisualPosition.y, 0)`。
2. `PreprocessNotePos 0xbb2e8ac..0xbb2ea98` 找到判定线穿过的路径段，并将首路径点沿该
   线段插值推进到裁剪位置。
3. `_MeshFillIntermediateFrame 0x502834..0x502ce8` 对已经过线的旧截面复制下一有效截面的
   6 个顶点，使固定索引指向的三角形退化为零面积。
4. index buffer 保持不变；Body 因而连续缩短，不会按路径节点跳变。

HoldMesh 只读取视觉位置和 Note 状态。提前松手若已经由 Judge 完成尾判并写入终态 `3`，
会进入与自然尾判相同的 mesh disposal 路径，没有独立的 early-release 几何动画。

### 5.7 Mixer Body、Tail 与动态头（确认/强推断）

Mixer 至少包含两条独立视觉链：

```text
Mixer Body/Tail 网格  -> `0xbb44508` 生成并维护
动态 Slider 头        -> `SliderSkin.OnUpdateVisual 0xbb43e34`
```

Body 网格更新不读取 Slider 位置或 Holding 连接状态，因此断触不会重建、销毁或整体隐藏
Body。这与动态头是结构性分离，不只是材质开关。

Tail 的固定拓扑已经确认：

```text
GetTailVertexCount(n):   0 / 6 / 12 / (7*n - 2)
GetTailVertexIndex(n):   0 / 6 / (7*n - 1)
GetTailTriangleCount(n): 0 / 12 / 36 / (24*n - 12)
GetTailTriangleIndex(n): 0 / 12 / (24*n - 12)
```

这里的 TriangleCount 实际是 index 数。尾端固定追加 6 个顶点和 24 个 index，即 8 个
三角形；`0xbb4756c` 会把 Body 与 Tail 全部顶点纳入 Bounds。六个顶点的精确业务名称仍
是 UNKNOWN。

动态 Slider 头与位置更新的关联已确认；`NoteData+0x20 = 2` 已闭环为通用
`ENoteJudgeState.DelayJudging`，不能作为 Mixer 断触证据。静态证据仍强支持断触时只退休/
禁用动态头关联视觉，重接后恢复位置更新；究竟是删组件、禁用 Entity 还是改变 query
匹配条件仍为 UNKNOWN。

### 5.8 判定、视觉与实体生命周期（确认/强推断）

原版把三个阶段分开：

```text
Manual / Auto 判定
  -> 写 NoteData 终态
  -> OnNoteHit
  -> 写 OnJudged ECS event
  -> 后续判定帧跳过该 Note
  -> renderer/mesh 系统独立消费 OnJudged
  -> 达到离场条件后由 NoteDisposeSystem 回收
  -> 递归处理子 Note
  -> ECB DestroyEntity
  -> NoteData.entity = Entity.Null
```

Manual 的统一提交区间是 `0xbb734ec..0xbb735b8`，其中 `OnNoteHit` 在 `0xbb7352c`，
`OnJudged` 在 `0xbb73594`；下一帧 `0xbb73340` 发现状态非零便跳过。Auto 主体
`0xbb71960` 在普通分支 `0xbb72400..0xbb724f0` 做同样提交。判定函数内没有直接
`DestroyEntity`。

生成 metadata 已把 `OnJudged` 接入 `SkinNote_HorizontalStretch__System` 的 mesh 管线，
因此 renderer 有独立 consumer 可以确认；但它究竟在同帧隐藏、改 renderer slot、停留
一帧还是先做动画，静态证据不足。`HorizontalStretch.OnUpdate 0x52b4f8` 尾部的
`INT_MAX / state 3 / -1.0f` 写入很像退休 mesh record，只能标为强推断。

最终 Dispose 扫描主体是 `0xbb6ffbc`；`0xbb70964` 是单 Note 递归处理 helper，不是系统
入口。它在 `0xbb70c14..0xbb70c34` 递归子 Note，并在 `0xbb70c50..0xbb70c78` 向 ECB
提交 `DestroyEntity`。调用方随后把 NoteData 保存的 Entity 清空。

`DisposeSubNote` event wrapper `0xbb70e80` 的载荷布局可确认是 `int32 @ +0` 与
`byte @ +4`；另一个 `NoteDisposeParam` wrapper 在 `0xbb70f10`。两字段属于 mesh/子 Note
回收载荷，不是 judgement grade；精确字段语义仍为 UNKNOWN。

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
显著抬高 Combo。社区版改用 Hold 实际路径节点与 Mixer 八分点作为完整主判定，同样会
影响 Combo，但计数来源和原版 Holding tick 不同。

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

新增静态证据确认 runtime `NoteData+0x19` 保留序列化 Type 原值，`NoteManager__System`
显式分发 1..10。Type 1..9 进入 judged-note bookkeeping，Type 10 单独分流；结合唯一的
非判定 tag `NoteTag_TypeLine`，可将“当前逆向版本 Type 10 = Line/BarLine”标为 STRONG。
但 1..9 的 tag 注入分支尚未逐一闭环到各 `_judge*` kernel，因此不能据此给 1..9 的具体
名称标 CONFIRMED，也不改写社区版已经拍板的 Type 1..9 语义。

本报告中的内部类别名只用于描述算法，不用于重新解释谱面 Type 编号。

## 10. 仍未确认的边界

主体玩法已经可以实现，但以下边界仍不应写成原版定论：

1. **phase 2/3 的业务枚举名**：数值与接受范围已确认，但 Move/Stationary 的准确名称
   尚未从当前版本枚举恢复，不应影响实现。
2. **NSTouchWidth 的资产真值与设备缩放**：`NSTouchWidth 0xbb5a794` 的公式已确认是
   `input / 240 * trackScale`，其中 `trackScale` 来自对象 `+0x14`；资产原值、三轨是否不同
   以及设备最终缩放仍未知。
3. **BPM 变化点的同帧系统顺序**：Hold/Mixer 都按当前 interval 动态补发已经确认，但
   时间线 BPM 更新与 JudgeSystem 在同一帧的先后没有明确 `UpdateBefore/UpdateAfter` 证据。
4. **OnJudged 的逐皮肤视觉动作与回收帧**：renderer consumer 和最终 ECB 销毁已确认，
   但中间是隐藏、改 slot、短暂停留还是动画，以及判定当帧/下一帧回收仍未知。
5. **Mixer 动态头显隐实现**：生命周期状态 `2` 已确认为通用 `DelayJudging`，不再与断触
   绑定；动态头究竟通过删除组件、禁用 Entity 还是 query 过滤消失仍未区分。
6. **Buff/EX Boost 完整规则**：得分因子入口与 Boost 上限已确认，但 Buff 来源、持续
   时间、等级和叠加上限尚未闭环。
7. **模式化血量上限**：fallback `10000` 已见，具体模式/角色如何提供 MaxHealth 未闭环。
8. **当前版本 Type dispatch**：已确认 runtime 显式分发序列化值 1..10，且 Type 10 =
   Line/BarLine 为 STRONG；Type 1..9 到内部 judge 的最终映射仍需补齐 tag/helper 链或以
   运行时参数 trace 终验。

这些未知项都可以隔离在输入适配、常量提供器和规则策略层，不妨碍先正确实现 Hold、
Mixer、Mine 与基础判定窗。

## 11. 与社区版现状的差异

> 社区实现摘要于 2026-10-07 静态同步；原版证据/地址未因文档整理改变。
> 原版每帧重算 Holding interval 的研究结论不等于社区 Hold 宽限策略；社区采用首次断触冻结 deadline，见 [v2 合同](<chart-format-v2.md>)。

| 项目 | 原版 | 社区版当前实现 | 判断 |
| --- | --- | --- | --- |
| 普通判定窗 | StandardBPM=150 固定 | 固定 150 BPM | 已一致 |
| Holding 间隔 | 当前 BPM，clamp 120–200 | Hold 实际路径节点/Mixer 八分 bar 主判定；Hold 首次断触按当时 BPM 冻结 deadline | 社区计数与宽限策略；不称原版每帧重算已完全复刻 |
| 输入 | 多触点 x + phase | 逐 id 触点快照、轨内 Position、phase 1/2/3 | 已落地 |
| 输入批次目标时间 | 同批早侧只放行精确同刻，晚侧不走同一锁 | Early/Exact 按帧锁最早目标毫秒整数，Late 不检查/武装锁 | 语义已接入；数值表示不是原版 float32 精确同刻 |
| 重叠 Note 的触点复用 | 每颗 Note 独立从触点 0 扫描，同一触点可命中多颗重叠 Note | `OnPress` 按 Note 独立扫描，同一 Press 可复用触点 | 已嵌入；仍需端到端事件序列和真机验证 |
| 空间候选 | note 范围 + 触摸宽度 | 实际 `[P,P+W]` + 集中可调触摸宽；Mine 无扩边 | 公式已落地；设备 NSTouchWidth 真值仍未知 |
| Drag/Chain | phase 分段的连续接触 | Prefect/2 phase 分段 + 空间接触 | 已落地 |
| Mine | 精确范围、到点前短危险窗 | Prefect/4 phase 分段、到点安全 Prefect | 已落地 |
| Hold | 实时条体插值 + 动态断触宽限 + Holding tick + 按 release 时刻尾判 | 左右缘插值、首次断触冻结宽限、实际路径节点主判定、按 release 时刻尾判 | 主体参考原版；节点计数与冻结策略按社区 v2 合同 |
| Hold Body 裁剪 | 固定拓扑，推进首截面并退化旧截面 | 每个 `NoteLink` 独立裁四边形，整段过线即 `QueueFree` | 视觉目标接近，拓扑与生命周期不同 |
| Mixer | 虚拟滑块 + 无超时随时重接 + Holding tick 命中率尾判 | 当前触点连接、动态头显隐、每 1/8 bar 主判定、无额外尾判 | 连接机制一致；结算口径为社区决策 |
| Mixer 视觉结构 | Body/Tail 与动态 Slider 头分离 | Body 连接段与运行时头分离 | 结构方向一致；Tail 拓扑和精确回收帧不同 |
| 判定后生命周期 | OnJudged、视觉退休、Entity Dispose 分阶段 | Tap/Drag 等命中时立即 `QueueFree` 本体 | 明确不同；逻辑判定不受影响，视觉节奏可能受影响 |
| Miss 状态 | AutoMiss / 输入 Miss 分开 | `JudgeResolution` 分源，界面仍映射四档 | 已落地 |
| Combo | 含 Holding tick，300 连击倍率封顶 | 所有社区主判定（含 Hold 路径节点与 Mixer 八分点）进入 Combo，无倍率 | 社区决策 + 差异 |
| CLEAR | 主判定 100/70/50 权重 | raw score / 理论满分 | 产品口径不同 |
| 显示分数 | raw score 随谱面长度与 Combo 变化 | 归一化 1,000,000 | 社区产品口径 |
| Health/Boost | 按 TotalMainNote 缩放，Health fallback 10000、Boost 上限 3000 | 已按公式缩放并钳制 | 已落地；模式化血量上限仍未知 |

## 12. 社区实现与验证边界

当前规则分层为：

```text
GameplayMain 输入适配与逐触点状态
  -> V2InputProtection / InputJudgeRules
  -> JudgePlan / SustainPath
  -> JudgeEngine
  -> HUD / ScoreStore
```

`OriginalJudgeMath` 单独保存原版 Combo/raw score/CLEAR 数学，不接入社区版百万分数和主
Combo。现有 clean-room 回归代码包含窗口、BPM、phase/空间、sustain、毫秒目标锁、
Hold release 冻结 deadline、计分/身份和结算时间线；官方开发语料需显式启用，不是默认测试依赖。
本次文档清理没有重跑测试；历史阶段结果见 [归档](<archive/README.md>)。

设备输入/焦点取消、音频自然结束/暂停恢复与最新视觉仍需端到端或实机验证；完整真实待办见 [当前交接](<handoff.md>)。

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
| Hold `FillMeshDataArray` | `0x51c344` |
| Hold `_MeshFillIntermediateFrame` | `0x502834` |
| Hold mesh `CreateMesh` | `0x50268c` |
| HorizontalStretch `OnUpdate` | `0x52b4f8` |

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
| Manual 普通候选时间门 | `0xbb73380` |
| Auto 主判定循环 | `0xbb71960` |
| CLEAR 入口 | `0xbb5d978` |
| CLEAR 数学函数 | `0xb8d59c8` |
| Mixer 主状态机 | `0xbb738fc` |
| Hold 主状态机 | `0xbb74554` |
| Hold 位置插值 | `0xbb7fb20` |
| `NSTouchWidth` 换算 | `0xbb5a794` |
| Hold `PreprocessNotePos` | `0xbb2e8ac` |
| Hold `FillNotePosData` | `0xbb2f1a8` |
| Mixer Slider visual update | `0xbb43e34` |
| Mixer Body/Tail 网格 | `0xbb44508` |
| Mixer Tail vertex count/index | `0xbb44b2c` / `0xbb44bf0` |
| Mixer Tail triangle count/index | `0xbb44ca0` / `0xbb44d64` |
| Manual `OnJudged` 提交 | `0xbb73594` |
| Auto `OnJudged` 提交 | `0xbb724d8` |
| Note Dispose 系统主体 | `0xbb6ffbc` |
| 单 Note / 子 Note 递归 Dispose | `0xbb70964` |
| `DisposeSubNote` event wrapper | `0xbb70e80` |
| `NoteDisposeParam` event wrapper | `0xbb70f10` |

以上地址和 JudgeSettings 资产数值共同构成本报告“确认”结论的复核入口。
