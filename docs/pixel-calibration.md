# 玩法场景像素标定（运行时截图实测）

来源：AVD 模拟器 2340x1080 横屏，Star Cape TUTORIAL 自动演示截图
（comfyui_dl/t01-t20.png, play1-3.png）。原生分辨率局部测量。

> **⚠ 更正（视频分析后，2026-08）**：本文早期把粉色横条当成"判定条"是**错的**——
> 粉条是 Mixer UI（见 `video-geometry-analysis.md` §5）。真实判定线是**隐形水平线**，
> 1440×1080 下 y≈858（79.4% 屏高）。几何结论一律以
> `video-geometry-analysis.md`（Charter 视频 + 真人手元视频交叉验证）和
> `gameplay-spec.md` §8.6 为准；本文仅保留 2340×1080 参考系的原始测量值。

## 判定条（粉色 judge bar）→ 实为 Mixer UI
- 屏幕坐标：x 875–1465（长 **590px** = 屏宽 25.2%），y 650–680（厚 30px）
- 中心刻度：x 1155–1180，蓝/紫色小块，中心 x≈1168（屏幕中心 1170）
- 判定条中心 y≈665 = 屏高 61.6%
- 外观：粉/品红实心条 + 黑白格点描边，下方有黑色刻度带（小节线标记）
- 【更正】此为 Mixer 滑条 UI；真实判定线不可见，y≈79.4% 屏高（视频实测）

## Tap 音符（绿色，教程皮肤）
- 宽约 **270px**（≈判定条长的 46%，屏宽 11.5%）
- 本体厚约 30px + 辉光；中央有指向判定条的箭头/茎（高约 25px）
- 多深度样本宽度 265–280px → 透视极弱，接近正交
- 同帧节拍间距约 95–100px（垂直）

## Hold 音符（橙色长条）
- 束宽约 250px，从屏幕顶部 (y≈160) 延伸至判定条
- 音符从屏幕顶端附近生成，垂直落向判定条（路径 y≈0..665）

## Mixer（混音滑条）
- 开局静止时可见判定条下方地板上有一条**青色发光滑杆**：
  x 975–1245（宽 ≈270px，与 tap 音符同宽），y 785–825（厚 40px），
  中心 x≈1110（略偏屏幕中心左侧，因地板透视），两端箭头形
- 教程演示中 mixer 音符为左侧青色流光（play2），MixerFlyNSWidthCenter=0.25 / Sides=0.5（JudgeSettings 字段）

## HUD
- 连击数：判定条正下方 y≈853（如 "14"、"26"）
- 判定文字（PERFECT/MISS）：判定条下方 y≈780–820
- 底部：进度%（左下）、曲名+难度（中下）、分数（右下）
- 顶部：暂停按钮 (1170, 28)、血条/Boost（顶部中央紫条）

## 待离线数据补全
- 判定条/note 的世界单位尺寸（prefab/scene Transform）
- NS→屏幕比例（配合 DropSpeeds 数据）

## 谱面结构（Star Cape Tutorial, Map_0014.16）
- BPM 184，TimeEnd 80.87s，BarTime 单位 = 小节（4/4），Baked_Second 精确到 ms
- 音符按 Region 分三列表：NotesLeft(6) / NotesCenter(50) / NotesRight(0) + NotesSystem(1, Type=209 结束)
- 音符字段：Id, Type, BarTime, Position(float), Width(float), Baked_Second
- 本谱所有音符 Width=1.0 ↔ 屏上 ~270px；判定条 590px ≈ 2.19 个 Width 单位
- Type 观察：1=Tap，2=滑动/链（Position 0.1-1.5 连续变化），3=Hold 头，4=Hold 尾，
  6/7=Mixer 左右？（Position 含负值 -0.3/-0.1 和 1.5），209=系统结束
- Center Position 范围约 -0.3..1.5；Left 谱 Position 1.6-3.3, Width 1.8-2.0（不同坐标域）
- 左区音符（NotesLeft）在演示中是屏幕左侧的青色流光（play2）
