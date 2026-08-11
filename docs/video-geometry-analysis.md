# 游玩几何视频标定

> 本文只保留社区版当前采用的几何结论与证据。正式游玩几何以真人手元视频
> `gameplay_videos/tablear_realplay.mp4` 为主；谱面确认视频只用于可交叉验证的时间、
> 尺寸和固定 UI 测量。

## 1. 数据与坐标

- 实机视频：1920×1080、30fps，画面校正到 1440×1080 分析坐标。
- 谱面确认视频：1440×1080、30fps，与 Tablear GIGA 的 `Baked_Second` 对齐。
- 坐标原点位于左上，x 向右、y 向下。
- 几何拟合使用命中星爆、note 条中心/边缘和固定 Mixer 条作锚点。

正式游玩中 Center 的 x(P) 固定；社区版不实现谱面确认画面的轨道漂移演出。

## 2. Center 映射

真人手元视频中，W=1 note 的命中中心拟合为：

```text
x_hit = 312.5 + 204.9 * P       @1440×1080
```

样本数 n=249，r²=0.9968。由于 P 是左缘，W=1 的左缘需减去半个视觉宽：

```text
x_left ~= 210 + 205 * P         @1440×1080
x_left = 280 + 273.2 * P        @1920×1080
```

因此当前渲染公式为：

```text
x_center = 280 + 273.2 * (P + W/2)
visual_width = W * 273.2 * 0.95
```

其他 Center 结论：

| 参数 | 当前值 |
| --- | --- |
| 判定线 | `y=861` @1920×1080 |
| 可见行程 | 790px |
| 基础下落标尺 | 1641.6px/bar（150 BPM、DropSpeed=1 时等价于 1026px/s） |
| 顶部淡入 | `y<=128` 透明，`128..200` 线性淡入 |

## 3. Side 映射

侧轨判定线按 1440 视频测量值换算为 1920 设计坐标：

```text
LeftLineX = 184
RightLineX = 1736
```

当前命中 y 使用左右相同的谱面中心映射：

```text
cP = P + W/2
y = 840 - 115 * cP
```

侧轨位置单位和条的视觉长度单位不同：

| 参数 | 当前值 |
| --- | --- |
| 普通 side note 长度 | `W*102*0.95px` |
| BarLine 长度 | `W*190*0.95px` |
| 条厚 | 约 17px |
| 可见行程 | 691px |
| 距离尺度 | Center 的 0.75 |
| 生成提前距离 | `691/0.75 ~= 921px` |

侧轨 note 从屏幕中央附近向边缘运动。连接体远端允许出屏，命中端在判定线连续裁剪。

## 4. Hold 与 Mixer

- Hold 使用琥珀色半透明面板和亮描边。
- Center Hold 头/尾帽宽度为 `W*273.2*0.95`，连接面板宽度为 `W*273.2*0.8`。
- Side Hold 半宽跟随 side note 的 `W*102*0.95/2`。
- Side Hold 不使用深度锥形，只按剩余距离把填充 alpha 从 0.08 提升到 0.28、描边
  alpha 从 0.25 提升到 0.9。
- Mixer 使用 Type 6/7 路径；粉色固定横条位于 `y=632, x=675..1244`，仅为装饰 UI。
- Hold/Mixer 的路径节点只塑形，整条连接体按同一时间位移运动。

## 5. DropSpeeds

谱面确认视频约 2:10 的变速段显示，DropSpeed 在事件之间连续变化。当前采用：

```text
speed(bar) = 相邻 DropSpeed 事件值的线性插值
visualDistance = (noteBar - currentBar) * speed(currentBar) * playerScale
```

Bar 104.375999 的速度为 0.2，Bar 105 为 1.1。对 Bar 105 的 note，上式预测它在
Bar 104.618666 附近由远离判定线转为靠近判定线；换算到视频约 130.286s，与逐帧观察到的
130.267--130.300s 换向一致，误差不超过一帧。近线 note 会先换向，中远距离 note 仍可
继续回退，也与逐帧轨迹一致。

运行时直接把该距离映射为 Center 的 y 或 Side 的 x，不增加透视投影。玩家落速倍率只
缩放视觉距离，不改变命中秒。

## 6. 当前 1920×1080 参数汇总

| 参数 | 数值 |
| --- | --- |
| CenterLineY | 861 |
| MixerBarY / Left / Length | 632 / 675 / 569 |
| CenterX0 / PosUnitPx | 280 / 273.2 |
| LeftLineX / RightLineX | 184 / 1736 |
| SideY0 / SideUnit | 840 / 115 |
| SideNoteLenUnitPx | 102 |
| SidePosUnitPx（BarLine） | 190 |
| SideDistScale | 0.75 |
| TravelPx | 790 |
| NoteVisualScale | 0.95 |

这些值的运行时权威定义位于 `GameplayMain.cs` 顶部；本文用于说明测量来源，不另设一套
可独立修改的参数。
