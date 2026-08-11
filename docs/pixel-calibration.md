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

| 项目 | 当前值 |
| --- | --- |
| Center 判定线 | `y=861` |
| Center 轨中心 | `x=960` |
| Center 左缘映射 | `x=280+273.2·P` |
| Center 视觉宽 | `W·273.2·0.95` |
| Left/Right 判定线 | `x=184/1736` |
| Side 命中 y | `840−115·(P+W/2)` |
| Side note 长度 | `W·102·0.95px` |
| Side BarLine 长度 | `W·190·0.95px` |
| Center 可见行程 | 790px |
| Side 可见行程 | 691px |
| Side 距离尺度 | 0.75 |

Position 是条的左缘。Center 渲染中心为：

```text
x = 280 + 273.2 * (P + W/2)
```

Side 渲染中心为：

```text
Left:  x = 184 + remainingDistance * 0.75
Right: x = 1736 - remainingDistance * 0.75
y = 840 - 115 * (P + W/2)
```

## 输入区域

| 屏幕范围 | Track |
| --- | --- |
| `y>771` | Center |
| `y<=771 && x<400` | Left |
| `y<=771 && x>1520` | Right |
| 其余上部区域 | Center |

转换后的触点 Position 与 note 的 `[P,P+W]` 做重叠判定。普通 note 使用
`CommunityTouchWidth=0.30` 扩边，Mine 不扩边。

## 固定舞台元素

- Mixer 粉色装饰条：`x=675..1244`、`y=632`，不是判定线。
- Center 判定反馈基准：`y=696`，命中后轻微上浮并淡出。
- Combo 数字：`y=872`；`COMBO` 小字：`y=944`。
- 左下曲名/难度：基准 `y=920`。
- 右下分数：基准 `y=879`。
- 侧判定线从顶栏下方 `y=70` 延伸到 Center 判定线。
- 前景地板和透视参照线继续绘制到 `y=1080`。

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
alpha，不缩窄、不钳制远端。命中辉光持续约 0.34 秒，可向判定线下方延伸约 90px。

## HUD 与结果层

- 顶部 pill 行依次显示 P/GR/GD/M、CLEAR、M.COMBO。
- 暂停按钮位于 pill 行下方中央。
- 左下显示曲名与难度，右下显示百万分数。
- 结算时隐藏舞台、音符和 HUD；结果层先显示不透明 fallback，再叠加封面背景。
