# 2026-10 旧编辑器动效扩展目标

> 2026-11-09 归档。对应已删除的 Godot 外壳，不是当前工作台能力/缺口或实施授权。现行 [制谱器说明](../../dyna-maker-uv.md) 与 [游戏动效](../../ui-motion.md) 优先；Auto 音频/命中预览现已接入，以下当时状态不再有效。旧 §4 交互目标见 [旧设计](2026-10-editor-ui-design.md)。

## 9. 编辑器目标与游戏粒子规格

§9.1–§9.4 是把游戏语言/token 扩展到制谱器的**设计目标**，不是整体已实现宣告。2026-10-26 静态核对：Settings、SAVE/SAVE AS、部分淡入/hover/焦点反馈、滚动惯性、路径与放置导引已接入；真实音频/判定试玩仍不存在，Signal Lock/Track Relay、完整 stagger、放置爆发等不能据此视为完成。
不新增缓动曲线或运动语言；Motion 设置当前有真实但局部的接点，REDUCED 与 FULL 尚无完整差异，OFF 也有待核查的局部反馈边界。详细能力及静态疑点见 [DynaMaker UV](<../../dyna-maker-uv.md>)，本表不覆盖其当前状态。
§9.5 则记录已接入游戏的粒子参数，编辑器复用仍为目标。

### 9.1 手势与画布反馈

| 交互 | Full 表现 | Reduced | Off |
| --- | --- | --- | --- |
| 放置·阶段 0 跟随 | 幽灵（约 80% 不透明）1:1 跟随光标与吸附结果，无插值 | 同 Full | 同 Full |
| 放置·阶段 1 宽度拖拽 | 宽度实时渲染；跨 0.05 档时缘边亮闪 `--t-micro` | 同 Full | 不闪 |
| 放置·提交确认 | 幽灵落为实体 + 缩小打击爆发（复用 `GameplayHitBloom`，Strength≈0.4，≤200ms） | 只做不透明度跳变 | 立即实体 |
| Hold/Mixer 拖尾 | 拖动中虚线实时预览路径重采样，松开变实线；无插值 | 同 Full | 同 Full |
| Hover | Note 边缘亮度 +20%、路径手柄 60%→100% 不透明，`--t-control` + `--e-standard` | 同 Full | 立即 |
| 选中 chip | `--move-xs` 滑入 + 淡入，`--t-control` + `--e-enter` | 只淡入 | 立即 |
| 右键菜单 | 以光标为锚点（近屏幕边缘翻转），项间 `--stagger` 自上而下单向进入，总长 ≤300ms | 整体淡入，无 stagger | 立即 |
| 路径手柄出现 | 菱形手柄沿路径从头向尾依次点亮（`--stagger`），兼作方向提示 | 同时淡入 | 立即 |

吸附成功不做专门光效（无吸附 ping）。**拖动式** scrubbing、放置跟随与拖尾预览在三档下全部 1:1 直连输入，不得插值
（滚轮/触摸板是离散推力而不是拖动，按 §9.2 的滚动惯性处理，两者不冲突）。

### 9.2 时间轴与网格

| 交互 | Full 表现 | Reduced | Off |
| --- | --- | --- | --- |
| 标尺跳转 | 当前位置三角 `--t-focus` + `--e-standard` 滑动到位 | 同 Full | 立即 |
| 网格密度切换 | 旧密度淡出 / 新密度淡入交叉 `--t-focus` | 同 Full | 立即 |
| 滚轮/触摸板滚动（画布视图时间、Pack 差分行列表、选曲列表） | 惯性指数趋近，τ 由设置档/固定常量给（见 [UI 设计施工约定](<2026-10-editor-ui-design.md#4-编辑器交互目标不是当前能力清单>) §4 第 14 条） | 同 Full | 立即（`MOTION = OFF` 时 τ 强制为 0） |
| 播放/试玩 | 底部进度线与标尺三角与歌曲时钟 1:1 推进；判定线命中可复用 `GameplayHitBloom` | 同 Full | 同 Full |

### 9.3 页面与组件

| 交互 | Full 表现 | Reduced | Off |
| --- | --- | --- | --- |
| 页面转场（Start / Pack Select / Settings / Editor） | Signal Lock `--t-route`，同 §2.1 | §4 Reduced | §4 Off |
| 进入编辑（Open in Editor） | Track Relay 变体 `--t-relay`：纯文本上下文卡（曲名+难度+Lv）→ READY → 分层揭示 判定线 → 网格 → 已有 Note | §4 Reduced | §4 Off |
| 等级步进器 | 新块 scale 0→1，`--t-focus` + `--e-enter`；数字 `--move-xs` 上移替换；非法值红框单次脉冲（禁用抖动） | 同 Full | 立即 |
| 难度列表 | 进入 `--stagger` 自上而下；「+ NEW CHART」hover 虚线→实线 `--t-control` | 无 stagger | 立即 |
| 保存反馈 | HUD 状态灯 cyan 单闪 `--t-micro`；失败 pink | 状态文字直换 | 状态文字直换 |

### 9.4 编辑器约束

- 编辑器无循环氛围动画；用户不操作时界面稳定停住（同 §4 Full）。
- 动效不得改变谱面时序、吸附结果、命中区、撤销栈或保存语义。
- Reduced 去掉方向位移、stagger、爆发与滑动，保留不透明度状态切换；Off 全部立即提交，但同 §4 Off 一样先绘制稳定状态再开放输入。
- 粒子特效属于纯视觉层，不承载业务事实；采用 §9.5 降级规则：该规则的 Reduced/Off 指的是游玩设置
  `HIT EFFECTS` 档位（`HitEffectLevel`），UI MOTION（`UiMotionMode`）只负责界面与转场动效，不再影响游玩粒子。

