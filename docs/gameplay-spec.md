# DUX-Community 玩法规格

> 本文描述社区版当前采用的谱面、判定、计分和布局规则。原版方法体证据见
> `original-judgement-analysis.md`，几何测量见 `video-geometry-analysis.md`。
> 文中不包含任何原版素材或可发布原版谱面。

## 1. 谱面数据

社区谱面 JSON 沿用逆向得到的三轨结构，由 `DynamixChartLoader` 解析。

### 1.1 顶层字段

| 字段 | 类型 | 当前用途 |
| --- | --- | --- |
| `name` | string | 解析标题与原始难度号 |
| `Baked_TotalMainNote` | int | 主判定总数，用于 Health/Boost 缩放和一致性检查 |
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

`meta.json` 提供 `id/title/artist/charter/audio/cover/charts[]`。客户端先扫描
`res://testdata/packs`，再扫描 `user://charts`，后者可用相同 id 覆盖开发包。

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
- 每帧先把歌曲秒换算为 `currentBar`，再计算：

  ```text
  visualDistance = (note.BarTime - currentBar) * speed(currentBar) * playerScale
  ```

- 使用当前流速乘完整剩余 BarTime，因此升速时 note 可以暂时远离判定线，再随剩余时间
  缩短而折返；已生成 note 回退出屏时不销毁。
- 玩家落速倍率为 `FallSpeedLevel/10`，与谱面速度相乘。
- Lv10 的二维标尺为 1641.6px/bar，在 150 BPM、DropSpeed=1 时等价于 1026px/s。
- Center/Side 的可见行程保持 790px/691px；位置直接映射到现有二维轨道，不做透视投影。
- 空 BPM 时间线无法做秒/BarTime 互转，视觉坐标回退为 `note.Second-currentSecond`。

## 3. Type 1–9 权威语义

| Type | 名称 | 判定 | 当前视觉 |
| --- | --- | --- | --- |
| 1 | Tap | 普通输入 | 蓝色切角渐变、上缘高光和受控辉光；SyncNote 非零时金色描边 |
| 2 | Drag | 接触判定，按住经过即可 | 绿色切角渐变和方向纹 |
| 3 | HoldHead | Hold 起点输入 | 琥珀切角渐变头、半透明连接体和亮侧边 |
| 4 | HoldNode | Hold 路径节点/尾 | 中间控制节点隐藏；尾节点保留琥珀材质 |
| 5 | ExTap | 普通输入，所有窗口 1.5× | 冰蓝切角渐变和虚线芯 |
| 6 | MixerHead | Mixer 起点输入 | 粉色纹理头；接上后在线上显示动态头 |
| 7 | MixerNode | Mixer 路径节点/尾 | 控制节点隐藏，只显示节点间细连线 |
| 8 | Mine | 危险窗内触碰为 Miss，未触碰为 Prefect | 暗红危险斜纹 |
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
| `tutorial` | Tutorial |
| 其他键（含 hard/mega/giga） | Hard |

### 4.2 窗口

普通 Prefect/Great/Good/Miss 窗固定按 `StandardBPM=150` 换算：

| 窗口 | Casual/Tutorial | Normal | Hard |
| --- | --- | --- | --- |
| Prefect | ±100ms | ±62.5ms | ±62.5ms |
| Great | ±150ms | ±150ms | ±112.5ms |
| Good | ±200ms | ±200ms | ±162.5ms |
| Miss 候选范围 | ±250ms | ±250ms | ±250ms |

EX-Tap 对上述窗口统一乘 1.5。

Hold/Mixer Holding tick 的间隔为：

```text
0.125 * 240 / clamp(currentBpm, 120, 200)
```

因此 BPM≤120 时为 250ms，BPM=150 时为 200ms，BPM≥200 时为 150ms。

## 5. 输入与候选选择

### 5.1 屏幕区域

在 1920×1080 设计坐标中：

```text
y > 771       -> Center
y <= 771 且 x < 400  -> Left
y <= 771 且 x > 1520 -> Right
其余上部区域          -> Center
```

屏幕坐标转换为谱面 Position：

```text
Center: (x - 280) / 273.2
Side:   (840 - y) / 115
```

### 5.2 触点状态

客户端按触点 id 保存 Track、Position 和 phase：

- `Began=1`
- `Moved=2`
- `Stationary=3`

一帧内先形成完整触点快照，再从普通输入、Drag 和 Mine 候选中寻找最早可判定时刻。
`InputTimeGroupGate` 把该批输入锁到一个 float32 目标时间；完全同刻多押继续放行，
不同目标时刻必须等待下一批输入。

### 5.3 空间重叠

普通 note 和 sustain 使用 `[P,P+W]`，并在两侧各扩展一半
`CommunityTouchWidth=0.30`。同轨多个候选重叠时选择中心距离最近者。Mine 使用精确范围，
不应用触摸宽度扩边。

### 5.4 Drag 与 Mine phase

- Drag：Prefect 窗早侧外半段只接受 Began；早侧内半段和晚侧接受 Began/Moved/Stationary。
- Mine：只在到点前 Prefect 窗内危险；外 3/4 只接受 Began，最后 1/4 接受三种 phase。
- Mine 到点仍未触发时安全结算为 Prefect。

## 6. Hold 与 Mixer

### 6.1 路径

`SustainPath.BoundsAt(time)` 分别对相邻节点的左缘 `Position` 和右缘
`Position+Width` 做线性插值。Holding tick 在头尾开区间内每 0.125 bar 生成；空 BPM
时间线才使用固定秒间隔回退。

### 6.2 Hold

- 头判命中后进入 Holding。
- Hold 的判定与 Body 裁剪相互独立：头时刻过后但仍在有效 Late 窗口内时，玩家仍可按
  实际时间差正常判定并接起；Body 过判定线的部分始终裁掉，未判定头部按 §9.1 下穿。
- 成功判定无论 Early、Exact 或 Late，Hold 头、瞬时爆发和持续接触效果都锚定判定线。
  Early 接起时，第一段 Body 的近端提前连接到判定线，直到谱面头实际到线后再恢复常规
  连续裁剪。
- 每帧按当前插值范围寻找同轨有效触点。
- 断触宽限使用当前 BPM 对应的动态 Holding 间隔。
- 一帧跨过多个 tick 时补齐中间 tick。
- 提前到达尾判条件时安全结算，避免帧间漏尾。

### 6.3 Mixer

- Mixer Body 始终按路径正常渲染；中间节点和尾节点不渲染实体。
- 从头时刻到尾时刻，每帧在当前插值范围内寻找同轨有效触点；Began、Moved、Stationary
  都能建立或恢复连接。
- 有有效触点时，虚拟滑块跟随该触点并钳制在条体边界；判定线上显示一个动态 Mixer 头。
- 没有有效触点时立即视为断开并隐藏动态头。Mixer 不存在超时或永久 Miss 状态，之后
  任意时刻重新进入条体范围都能恢复连接；漏掉 MixerStart 也不阻止 Body 接入。
- Mixer 不使用普通 Note 的 Miss 下穿：头判或尾判得到 Miss 时不生成下穿或打击爆发；
  静态头在线上静默回收，Body、动态头和随时重接逻辑不受影响。
- 每个 MixerHolding tick 只依据该时刻是否连接结算，不追溯此前断开时长。
- 尾判按 Holding 命中率：100% Prefect、≥70% Great、≥50% Good，否则 Miss。

## 7. 判定单元与统计口径

`JudgePlan` 将谱面展开为按时间排序的最小判定单元：

- Tap、Drag、EX-Tap、Mine 各一个主判定。
- Hold/Mixer 包含头、Holding tick 和尾。
- BarLine 不生成判定单元。
- Holding tick 计 Score/Health/Boost，但 `AffectsCombo=false`、
  `AffectsJudgeCounts=false`。

`HeadlineUnitCount` 是所有影响主统计的单元数，应与 `Baked_TotalMainNote` 一致。
`TheoreticalMax` 是所有单元 Prefect 时的原始得分总和，包含 Holding tick。

`JudgeResolution` 保留 Pending、AutoMiss、InputMiss、Good、Great、Prefect；UI 将其映射为
Prefect/Great/Good/Miss 四档，并另外保留 Early/Exact/Late 时序信息。

## 8. 计分、CLEAR、Health 与 Boost

### 8.1 原始得分

| 类别 | Prefect | Great | Good | Miss |
| --- | --- | --- | --- | --- |
| Tap/Drag/Mine/HoldStart/HoldEnd/MixerStart/MixerEnd | 100 | 70 | 50 | 0 |
| Chain（保留类别） | 50 | 35 | 25 | 0 |
| HoldHolding/MixerHolding | 10 | 0 | 0 | 0 |

显示和存档分数统一为：

```text
round(RawScore / TheoreticalMax * 1,000,000)
```

结果钳制在 0..1,000,000，全 Prefect 必须严格等于 1,000,000。

### 8.2 CLEAR 与评级

- HUD 实时 CLEAR = 当前 RawScore / 已判定单元的理论满分。
- 结算 CLEAR = 最终 RawScore / 全谱 TheoreticalMax。
- 评级：Ω≥98、S≥95、A≥90、B≥80，否则 C。
- 存档字段 `acc` 为兼容旧格式保留，实际内容是结算 CLEAR。

### 8.3 Health 与 Boost

Health 使用资产基础值后按 `floor(value*600/TotalMainNote)` 缩放，默认上限 10,000；
Boost 按 `floor(value*100/TotalMainNote)` 缩放并钳制到 0..3000。

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
| Side 距离尺度 | `0.75` |

连接体作为刚性形状整体下落，过判定线部分被连续裁剪。远端允许出屏，不钳制；侧轨
Hold 不做远端缩窄，只按接近判定线的距离提高填充和描边 alpha。

HUD、背景和命中辉光均为程序化 UI。结算前必须隐藏舞台、音符和 HUD，并先显示不透明
结果背景，避免封面缺失或加载失败时透出游玩层。

### 9.1 Note 材质与判定反馈

- 所有可见 Note 类型统一使用 `note_surface.gdshader` 的切角渐变材质，并用虚线、方向纹、
  危险斜纹或扫描线区分类型；左右侧轨旋转同一套参数，亮度一致。
- 成功判定按 Note 类型生成 12 帧式程序化爆发，并按 Note 宽度和轨道方向适配；统一亮度
  系数为 1.30。
- Hold 与侧轨 Mixer 接通时使用 12 帧循环接触效果。亮度仅在接通后的 0.28 秒内从零爬升
  到满亮，随后保持不变，只有内部纹理继续循环；松开后立即消失。中心轨 Mixer 不显示
  这层侧面接触效果。

视觉运动与逻辑判定窗口相互独立，当前生命周期如下：

| 类型 | 到线及越线 | 判定反馈 |
| --- | --- | --- |
| Tap / EX-Tap / Drag | 未判定时立即连续越线；0–24px 满亮，随后 40px 淡出，累计 64px 回收；完整 Late 窗仍有效 | 命中立即回收本体，只在线上生成爆发；最终 Miss 不回线、不重启动画 |
| Hold | 未判定头部沿用普通下穿；Body 线外部分持续裁剪，Late 窗仍可接起 | Early/Exact/Late 命中后，头、Body 近端和全部效果锚定判定线；提前结算的尾节点等实际到线后再处理 |
| Mixer | Body 正常渲染，不使用普通下穿 | 命中可保留线头；Miss 静默回收静态头，不生成爆发，不影响 Body 和随时重接 |
| Mine | 不使用普通下穿 | 危险窗内触发时生成红色爆发并回收；安全到线直接回收，无爆发和残留 |
| BarLine | 到线立即回收，不下穿、不停留 | 无判定 |

## 10. 设置、音频与持久化

| 设置 | 范围/行为 |
| --- | --- |
| 判定偏移 | −300..+300ms，5ms 步进，接入 `SongClock.UserOffsetMs` |
| 落速 | 1..20，默认 10，倍率 `level/10` |
| Music/Hit/UI 音量 | 0..100，写入对应 Audio Bus |

`SongClock` 的窗口模式时间为：

```text
player.GetPlaybackPosition()
+ AudioServer.GetTimeSinceLastMix()
- AudioServer.GetOutputLatency()
+ userOffset
```

headless 模式改用系统计时器。暂停会记录当前位置并停止流，恢复时从记录位置重新播放。

## 11. 仍未确认的边界

以下项目不阻塞当前主体玩法，但不得写成原版定论：

- 设备 `NSTouchWidth` 的真实值及缩放规则。
- 同帧重叠 note 与多个触点的精确消费顺序。
- BPM 切段瞬间 Holding tick 的原版重调度方式。
- 模式/角色提供的真实 MaxHealth。
- 当前版本序列化 Type 到内部 judge dispatch 的最终映射。
- Buff/EX Boost 来源、持续时间、等级和叠加规则。
- 空 BPM 时间线正式谱的原始 BPM；当前 loader 依靠 `Baked_Second` 可正常游玩。

## 12. 验证

```powershell
cd client
dotnet build
```

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj
```

核心测试覆盖谱面加载、BarTime 换算、Auto 全 Prefect、主判定计数、窗口边界、EX-Tap、
Mine、百万分、时间组锁、触摸范围与 phase、动态 Holding 调度、sustain 插值、Miss 分源、
Health/Boost 缩放和原版数学策略。
