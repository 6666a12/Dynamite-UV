# 判定实现对照审计

> 审计日期：2026-08-13。当前实现以 `gameplay-spec.md` 为社区版权威规格，以
> `original-judgement-analysis.md` 为原版 clean-room 逆向证据。用户已经拍板的社区规则
> 优先于原版行为，不作为缺陷。本文件不包含原版素材或谱面。

## 1. 结论

当前判定主干已经具备可玩的完整闭环：固定判定窗、逐触点输入、空间重叠、Drag/Mine
phase、Hold/Mixer 状态、主判定统计、百万分、CLEAR、Health/Boost 都有明确实现。

本轮修复后，多指输入不再依赖 GUI 是否把触摸传到 `_UnhandledInput`，也不会把同一根
手指重复解释成触摸和模拟鼠标。每个触点 id 都进入同一帧的完整快照，再做多押仲裁。
Hold 尾判会记录首次失去接触的实际 release 时刻；宽限只是决定断触是否最终成立，不再
把“宽限耗尽时刻”误当作松手时刻。

与原版仍然不同的主要部分不是遗漏，而是社区规则：Hold 实际路径节点、Mixer 八分点、
主 Combo、百万分和 CLEAR 均采用社区口径。静态逆向新增确认了一项需要修正的非产品
差异：原版不消费触点，同一触点可命中多颗重叠 Note；社区版现已按 Note 独立扫描并允许同一 Press 复用。其他高风险区集中在真机触摸事件、触摸宽度常量和焦点/取消事件。

## 2. 已与原版证据一致的部分

| 领域 | 原版证据 | 当前实现 | 审计结论 |
| --- | --- | --- | --- |
| 普通窗口 | 固定按 StandardBPM=150 换算 | `JudgeSettings` 固定 150 BPM | 一致 |
| 难度预设 | Casual/Normal/Hard/Tutorial 四套窗口 | 难度键映射到四套预设 | 一致 |
| 输入结构 | 逐有效触点保存位置和 phase | 逐 id 保存 Track/Position/Began/Moved/Stationary | 一致 |
| 普通空间范围 | Note 范围两侧扩 `NSTouchWidth/2`；静态函数 `NSTouchWidth(input)=input/240*trackScale` | `[P,P+W]` 两侧扩社区触摸半宽 | 缩放公式已确认，`trackScale`/设备输入真值待测 |
| Mine 范围 | 精确 `[P,P+W]`，不扩触摸宽度 | `expandByTouchWidth=false` | 一致 |
| Early 时间锁 | 同批只允许同一 float32 目标时刻 | Early/Exact 锁最早 float32 时间组，同刻多押放行 | 一致 |
| Late 分支 | 不受同一 Early timestamp 检查限制 | Late 逐触点扫描，不受时间锁限制 | 一致 |
| 早侧候选外边界 | `note.Time-currentTime <= missSecond`，`+250 ms` 等号包含 | 使用同一 Miss 外窗筛候选 | 一致 |
| Drag phase | 早侧外半段只收 Began，内半段及晚侧收全部 phase | `AcceptsContactPhase` 同规则 | 一致 |
| Mine phase | Prefect 早窗外 3/4 只收 Began，最后 1/4 收全部 phase | `AcceptsMinePhase` 同规则 | 一致 |
| Hold 几何 | 对路径左右缘分别做时间插值 | `SustainPath.BoundsAt` 插值左右边缘 | 一致 |
| Hold 断触 | 当前 BPM、clamp 120–200 的动态宽限 | 每帧按当前 BPM 计算宽限 | 主体一致 |
| Hold 尾判 | 提前断开按 release 与尾时刻差评级 | 保存 `ContactLostAt`，尾走普通窗口 | 一致 |
| Hold 持有过尾 | 正常持有到尾为 Prefect | release 晚于尾时钳到尾时刻 | 一致 |
| Mixer 连接 | 无永久断触，可由任意有效 phase 重接 | 每帧扫描并动态显隐滑块头 | 一致 |
| Miss 分源 | AutoMiss 与输入 Miss 分开 | `JudgeResolution` 和独立计数保留分源 | 一致 |
| Health/Boost | 按原版总主判定数缩放并钳制 | 同一缩放数学，分母改用社区主判定数，Health 10000 fallback、Boost 3000 | 公式一致，计数口径为社区规则 |

## 3. 社区版明确采用的差异

以下差异来自已拍板的社区规则，除非产品决策再次改变，否则不建议为了“像原版”而回退。

| 领域 | 原版 | 社区版当前规则 | 影响 |
| --- | --- | --- | --- |
| 游玩布局 | 原版设备布局逻辑 | 固定 1920x1080，`canvas_items` keep 黑边 | 视觉和输入坐标稳定 |
| Type 映射 | runtime 显式分发序列化值 1..10；Type 10 = Line 为 STRONG，1..9 具体 kernel 未闭环 | 1 Tap、2 Drag、3/4 Hold、5 EX-Tap、6/7 Mixer、8 Mine、9 BarLine | 社区谱面权威语义，不随未闭环原版映射改动 |
| EX-Tap | 逆向证据未定义社区放宽值 | 全部窗口 1.5x | 更宽松的特色判定 |
| Hold 单元 | Start/End 主判定，另有动态 Holding tick | 头和每个实际路径节点均为 100/70/50/0 主判定 | 满 Combo 和谱面密度由节点决定 |
| Mixer 单元 | 动态 Holding tick，加命中率 MixerEnd | 从头开始每 1/8 chart bar 为主判定，非网格尾不补判，无额外尾判 | 断续表现直接体现在每个网格点 |
| Combo | Holding tick 也改变 Combo，得分有 1.0x 到 1.5x 倍率 | 所有社区主判定改变 Combo，无 Combo 得分倍率 | 分数更可预测 |
| 显示分数 | raw score 随谱长和 Combo 变化 | 理论满分归一化为 1,000,000 | 跨谱展示统一 |
| CLEAR | 仅主判定 P/GR/GD 的 100/70/50 权重并截断 | RawScore/TheoreticalMax | 与社区全部主判定和百万分同口径 |
| 主判定总数 | 使用原版 `TotalMainNote` 口径 | 由 `JudgePlan` 展开后重算 | 不依赖旧 `Baked_TotalMainNote` |
| 判定字 | 原版显示细节不是本次权威目标 | Prefect 不显示 E/L，Great/Good 保留 E/L | 减少无价值的 Prefect 噪声 |

## 4. 本轮输入修复审计

### 4.1 三指及以上同时点击

原问题有两个高概率来源：触摸事件经过 HUD 的未处理输入链，以及 Godot 把触摸再模拟成
鼠标。前者可能让不同位置的手指因 Control 命中情况不同而被吞掉，后者会让一根手指
生成两套 pointer 事件，破坏同帧候选仲裁。

当前链路为：

```text
ScreenTouch / ScreenDrag (_Input)
  -> 按 touch.Index 更新独立 pointer
  -> 本帧完整 TouchSample 快照
  -> Early/Exact 最早 float32 时间组锁
  -> 每根 Press 分别寻找空间候选
  -> Drag / Mine / Hold / Mixer 读取同一快照
```

`pointing/emulate_mouse_from_touch=false` 消除重复事件。鼠标仍在 `_UnhandledInput`，因此
桌面点击暂停按钮不会落入游玩输入。触摸暂停按钮时，该 touch id 从按下到释放都被忽略；
进入暂停会清空 pointer、pending event、frame snapshot 和时间锁。

代码层面已经消除了已知的多指丢失路径，但核心测试无法模拟 Android/iOS 驱动和 Godot
窗口事件。必须用真机做 3、4、5 指同刻与错开 5-20ms 的回归，才可关闭设备层风险。

### 4.2 候选消费

静态证据已经闭环，不再是 UNKNOWN：原版每颗 Note 都从触点数组索引 0 重新扫描，命中
只写 Note 自身状态，不写回触点，也没有 per-touch consumed 或 Note-touch owner。同一
触点因此可命中多颗同刻且空间重叠的 Note；多根触点也不先做全局一一匹配。

社区版 `OnPress` 现按每颗 Note 独立扫描本帧触点，不再按中心距离选出单一候选：

```text
原版：Note -> 独立扫描全部触点 -> 触点可复用
社区：Press -> 每颗 Note 独立扫描 -> 一根 Press 可命中多颗同刻空间重叠 Note
```

这覆盖了同位置叠 Note、宽 Note 重叠、以及一根手指覆盖完全同刻多押的谱面。Early 异时
Note 仍受时间组锁约束，所以触点复用不会导致一按吃掉后续不同时间的 Note。Mine 的精确范围、phase 和社区 Type 规则保持不变。

## 5. 本轮 Hold 修复审计

Hold 现在区分三个时刻：release、断触宽限耗尽、tail。判定使用 release；宽限耗尽只负责
确认该 Hold 是否永久断开。

| 场景 | 当前结果 |
| --- | --- |
| 头 Miss | 所有未结算 Hold 节点立即 Miss |
| 短暂抬手后在宽限内接回 | 清除 `ContactLostAt`，继续 Holding |
| 一根手指抬起但另一根仍覆盖 | 下一次完整快照确认仍接触，不断 Hold |
| 提前释放并最终超过宽限 | 未结算中间节点 Miss；尾按 release 与 tail 的差评级 |
| 尾前释放但宽限尚未耗尽就到尾 | 尾仍按 release 时刻评级 |
| 持有到尾或尾后才收到 release | 尾为 Prefect |
| 同帧 Press 后 Release | 先完成 Press 判定，再记录 release |

release 只会标记同轨且释放位置覆盖当前 Hold 条体的状态，避免一根无关手指抬起打断同轨
其他 Hold。显式 release 可以保留事件时刻；手指没有 release、只是滑出条体时，失联起点
仍只能落在帧边界，这是实时采样不可避免的近似。

## 6. 渲染与生命周期对照

### 6.1 判定、视觉和实体回收

原版把三个阶段拆开：判定系统写 Note 终态并发 `OnJudged`；renderer/mesh 系统独立消费
事件；达到离场条件后，`NoteDisposeSystem` 才递归处理子 Note 并通过 ECB 销毁 Entity。
判定后的 Note 在后续帧因终态非零被跳过，但判定函数本身不直接销毁它。

社区版普通 Tap、EX-Tap、Drag 等在成功命中时由 `ResolveNoteView` 立即
`QueueFree` 本体，只保留命中爆发。逻辑 Note 与 `JudgeUnit` 仍在，因而不会破坏后续统计，
但视觉消失和节点释放被合并到了判定时刻。这是明确的结构差异，可能影响原版那种“判定、
mesh 退休、实体回收”之间的帧级节奏。

精确的原版 OnJudged 视觉动作仍是 UNKNOWN：已经确认 renderer 有 consumer，但还不能
证明它是同帧隐藏、改 renderer slot、停留一帧还是播放短动画。因此现在不建议为了对齐而
给普通 Note 强行增加固定延迟；应先把社区视觉生命周期从判定逻辑中解耦，保留可配置策略。

### 6.2 Hold Body 裁剪

| 项目 | 原版 | 社区版 |
| --- | --- | --- |
| 拓扑 | 整条 Hold 固定顶点/index buffer | 每个 `NoteLink` 独立四边形与描边 |
| 判定线裁剪 | 推进首截面，旧截面复制为重合顶点使三角形退化 | `ClipToLine` 对当前线段插值裁剪 |
| 已过线段 | 索引保留、零面积不可见 | 整段过线后 `QueueFree` |
| 尾判后 | 统一 mesh disposal，再进入 Note Dispose | 依赖逐段几何过线与现有 View TTL |

两者都能产生连续吞噬视觉，但原版在变速、回溯和复杂长路径下拥有一个整体网格状态，社区
版是多个节点的独立生命周期。若后续出现段间裂缝、描边接缝或回溯重建问题，优先把 Hold
改为单一固定拓扑，而不是继续给 `NoteLink` 增补特殊分支。

### 6.3 Mixer Body、Tail 与动态头

原版确认将 Body/Tail 网格和动态 Slider 头分成两条视觉链；Body 更新不读取 Holding
连接状态，断触不会重建或销毁 Body。社区版 Body 常驻、只在连接时显示运行时头，结构
方向与原版一致。

差异在于原版拥有固定 Tail 拓扑和统一 Bounds/Dispose 链，社区版仍是普通连接段，没有
独立尾网格。断触时动态头究竟通过删组件、禁用 Entity 还是 query 过滤消失，以及尾判后
Body/头的精确回收帧，静态证据仍不足；社区实现不应声称帧级完全一致。

## 7. 仍需验证或改进

### P0：判定逻辑

1. [已完成] 将 `OnPress` 从“只取中心最近的一颗”改为按 Note 独立扫描本帧触点，允许同一触点
   命中同刻且空间重叠的多个 Note，同时保留 Early 时间组锁。
2. [进行中] 为单触点重叠两颗/三颗 Note、多个触点重叠 Note、同位置异时 Note 和 Mine 重叠建立
   纯 C# 回归测试；当前已覆盖同触点双 Note，端到端 `GameplayMain` 事件序列仍需补齐。

### P0：真机验证

1. 在目标 Android/iOS 设备验证 3-5 指同刻、跨三轨、同轨多押、按住加新按、同帧短点。
2. 记录 touch id、phase、事件时间与帧号的开发日志，确认系统没有复用仍活跃的 id。
3. 覆盖 60/90/120Hz 和高触控采样率设备；多指问题可能只在特定帧率组合出现。

### P1：输入逻辑可测试化

1. 把 pointer 更新、帧快照和候选仲裁从 `GameplayMain` 提取为纯 C# 类。
2. 加入三指事件序列测试：任意事件到达顺序都应形成相同的同帧 chord 结果。
3. 加入 Press+Move+Release 同帧、pointer id 复用、暂停清理、另一指续 Hold 的测试。
4. 处理窗口失焦和平台 touch cancel 通知，统一调用 pointer 清理入口。

### P1：待逆向闭环

1. 测出设备/轨道实际 `NSTouchWidth`；静态已确认 `input / 240 * trackScale`，当前 `CommunityTouchWidth=0.40` 是根据 Mixer 试玩反馈调整的社区值。
2. 恢复 BPM 时间线系统与 JudgeSystem 的排序；动态 interval 重算本身已经静态确认。
3. 闭环 `OnJudged` 的逐皮肤视觉动作、Mixer 动态头显隐机制与精确回收帧；状态 2 已确认为通用 `DelayJudging`，不再作为断触线索。
4. 继续闭环当前版本 Type 1..9 的 tag/helper 到内部 judge kernel；Type 10 = Line 已有 STRONG 证据，社区映射仍保持产品权威。

### P2：完整玩法反馈

1. 接入真实 Hit/UI 音量总线后的完整打击音链路；目前视觉反馈已存在，但声音链路不完整，
   这仍是“打击感软”的主要来源之一。
2. Health/Boost 目前只计算内部值，没有 HUD、GameOver、Buff 或 EX Boost 行为。
3. 评估 Great/Good E/L 的颜色、停留时间和音效差异；Prefect 已按拍板统一去掉 E/L。

## 8. 验证边界

纯逻辑测试覆盖窗口、Early 锁与 Late 放行、触摸范围和 phase、Hold/Mixer 展开、Hold 批量
失败、按 release 时刻尾判、尾后 release 钳制、计分、Miss 分源和 Health/Boost 数学。
客户端构建能验证 Godot API 与 C# 类型连接，但不能证明移动设备多点事件完整性。

本轮按仓库要求不启动 Godot。最终接受标准应是：构建 0 error、核心测试全通过，并由用户
在真机完成多指和 Hold 手感目测。
