# 玩法场景布局标定

> 本文记录社区客户端当前采用的 1920×1080 设计坐标。数值与
> `client/scripts/game/GameplayMain.cs` 顶部常量一致；测量依据见
> `video-geometry-analysis.md`。

## 画布

- viewport：1920×1080。
- stretch mode：`canvas_items`。
- 非 16:9 窗口保持比例并留黑边。
- 所有游玩、HUD 和结算坐标均直接使用 1920×1080 设计坐标。

## 三轨判定几何

共享基准仍为 Center `y=861`、Side `y=840`。客户端游玩舞台与下方 HUD 整体下移 40px，
顶部统计与暂停按钮独立下移 8px；判定栏位于 `y=20..60`，顶部模式色细线为 `y=72`。
编辑器和校准页继续使用共享基准，不跟随这次客户端布局偏移。

| 项目 | 客户端当前值 |
| --- | --- |
| Center 判定线 | `y=901` |
| Center 轨中心 | `x=960` |
| Center 左缘映射 | `x=280+273.2·P` |
| Center 视觉宽 | `W·273.2·0.95` |
| Left/Right 判定线 | `x=184/1736` |
| Side 命中 y | `880−115·(P+W/2)` |
| Side note 长度 | `W·102·0.95px` |
| Side BarLine 长度 | `W·190·0.95px` |
| Center 可见行程 | 790px |
| Side 可见行程 | 691px |
| Side 距离尺度 | `691/790 ≈ 0.8747`（当前保留的行程比；2026-10-17 用户实机反馈基本通过） |

下列 P/Position 指 legacy 条左缘，v2 文档使用 center+width，不可混用。Center 渲染中心为：

```text
x = 280 + 273.2 * (P + W/2)
```

Side 渲染中心为：

```text
Left:  x = 184 + remainingDistance * SideDistScale
Right: x = 1736 - remainingDistance * SideDistScale
SideDistScale = 691/790 ≈ 0.8747
y = 880 - 115 * (P + W/2)
```

## 输入区域

| 屏幕范围 | Track |
| --- | --- |
| `y>811` | 加入 Center |
| `x<400`（不限 y） | 加入 Left |
| `x>1520`（不限 y） | 加入 Right |
| 三条件均不满足 | 回落 Center |

这些条件独立叠加，不是互斥分区：下方角落可同时属于 Center 与侧轨。权威实现为 [InputJudgeRules.ProjectedTrackMask](<../shared/Judge/InputJudgeRules.cs>)，客户端在调用时传入下移后的 Center 阈值，并同步偏移 Side 的纵向反查。

转换后的触点 Position 与 note 的 `[P,P+W]` 做重叠判定。普通 note 使用
`CommunityTouchWidth=0.40` 扩边（每侧 `0.20`），Mine 不扩边。

## 固定舞台元素

- 原 Mixer 粉色装饰条 `x=675..1244`、`y=672` 在游戏端 v8 HUD 中移除，编辑器保留；不是判定线。血条采用 [HUD v8](<gameplay-hud-v8.md>) 的顶部固定坐标。
- Center 判定反馈基准：`y=736`，命中后轻微上浮并淡出。
- Combo 数字：`y=912`；`COMBO` 小字：`y=984`。
- 左下曲名/难度：基准 `y=960`。
- 右下分数：基准 `y=919`。
- 游戏侧判定线从 `y=72` 接顶部模式色线并延伸到 Center 判定线；底线延伸至 `x=0..1920`。仅改变线条绘制范围，不改判定/输入映射；编辑器仍从共享基准70开始。
- 前景地板和透视参照线随舞台下移，超出画面的部分正常裁剪。

## 音符视觉

| Type | 颜色/形态 |
| --- | --- |
| Tap | 蓝色横条/侧轨竖条 |
| Drag | 绿色条 |
| Hold | 琥珀半透明面板，亮色描边 |
| EX-Tap | 浅蓝色条 |
| Mixer | 粉色连接体 |
| Mine | 暗红色条 |
| BarLine | 暗灰细线 |

Hold/Mixer 连接体作为刚性形状整体运动，过判定线部分连续裁剪。侧轨 Hold 只调整远近
alpha，不缩窄、不钳制远端。本文只规定几何；材质、残影/接触特效和设置倍率统一见 [玩法 §9.1](<gameplay-spec.md#91-note-材质与判定反馈>) 与 [动效规格](<ui-motion.md>)，不沿用早期固定辉光时长。

## HUD 与结果层

- 顶部 pill 行显示 P/GR/GD/M、实时 ACC、M.COMBO；结算 CLEAR 是不同的统计分母，见 [计分规格](<gameplay-spec.md#82-clear-与评级>)。
- 暂停按钮位于 pill 行下方中央。
- 左下显示曲名与难度，右下显示百万分数。
- Full 结算先合拢不透明屏风，完全遮住后才隐藏舞台/音符/HUD，再揭示结果；Reduced/Off 不透明接管。封面是顶端小图，不是整屏背景，见 [结算 v2](<result-v2-design.md>)。
