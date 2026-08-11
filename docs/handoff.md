# DUX-Community 交接文档

> 最后更新：2026-08-11。本文只记录当前有效实现、固定决策、待办和验证流程。
> 玩法数据与行为规格见 `gameplay-spec.md`，原版判定证据见
> `original-judgement-analysis.md`，布局数值来源见 `video-geometry-analysis.md` 和
> `pixel-calibration.md`，UI 样式稿见 `ui-mock/index.html`。

## 1. 项目约束

- 项目是 Dynamix 风格的 clean-room 社区版；发布内容不得包含任何原版素材或原版谱面。
- `client/testdata/` 下的 `tablear`、`the_villager` 两个谱面包只用于开发验证。
- 客户端使用固定 1920×1080 设计坐标，Godot stretch 为 `canvas_items` keep；比例不符时
  留黑边。不要引入自适应游玩布局。
- 谱面编辑器不集成进游戏：计划先做网页版，再考虑本地 app。
- 默认使用中文直接交流。普通修改完成后不启动游戏代替用户目测；明确需要截图/录屏时才
  执行本文 §6 流程。

## 2. 当前实现

### 2.1 应用闭环

- 主菜单：游玩和设置入口可用，谱面工坊入口禁用。
- 选曲：扫描谱面包、显示曲目、循环切换可用难度、显示最佳成绩并进入游玩。
- 游玩：三轨输入、鼠标/多点触摸、F1 Auto、Esc/按钮暂停、重开和返回选曲。
- 结算：Ω/S/A/B/C、百万分数、CLEAR、Max Combo、P/GR/GD/M、NEW RECORD，支持
  返回、下一首和再来一次。
- 设置：判定偏移、落速和 Music/Hit/UI 音量持久化到 `user://settings.json`。
- 成绩：按 `packId:diff` 保存到 `user://scores.json`；Auto 演示不写成绩。

跨场景状态由 `GameSession` 保存。直接启动 `gameplay.tscn` 且未选择曲目时，使用开发谱
并默认开启 Auto。

### 2.2 谱面与判定

- `ChartPack` 先扫描 `res://testdata/packs`，再扫描 `user://charts`；同 id 的玩家包覆盖
  开发包。
- 时间线存在时，loader 用 `BarTimeToSeconds` 重算命中秒；空时间线使用
  `Baked_Second`。
- Position 是条的左缘，命中范围为 `[P,P+W]`，渲染中心使用 `P+W/2`。
- Type 权威语义：1 Tap、2 Drag、3/4 Hold、5 EX-Tap、6/7 Mixer、8 Mine、9 BarLine。
- T2 是接触判定；T5 使用 1.5× 判定窗口；T9 只渲染，不计分和 Combo。
- SyncNote 非零只给 T1 Tap 画金色多押描边。
- 普通窗口固定按 StandardBPM=150 换算；Holding tick 按当前 BPM，并钳制到 120–200。
- 输入按触点 id 保存轨道、Position 和 phase；同帧锁定最早 float32 目标时刻，同时允许
  完全同刻多押。
- 普通 note 使用实际 `[P,P+W]` 与 `CommunityTouchWidth=0.30` 选择最近重叠候选；
  Mine 使用精确范围，不扩边。
- Hold 已实现条体左右缘插值、动态断触宽限、跨帧补 tick 和提前尾判；头部越线后 Late
  窗仍可正常接起，Body 线外部分持续裁剪。
- Mixer Body 始终渲染；当前范围内有同轨有效触点时在线上显示动态头，断开即隐藏，
  Began/Moved/Stationary 均可随时重接，没有超时或永久 Miss，尾判按 tick 命中率结算。
- Note 的下穿、回收和判定反馈按类型分流，当前权威规则见 §3；逻辑 Late 窗不依赖
  Note 本体是否仍然可见。

### 2.3 分数和统计

- 内部按类型表累计 RawScore；Holding tick 仍影响 Score/Health/Boost。
- Holding tick 不改变主 Combo，也不进入 P/GR/GD/M。
- 显示与存档分数为 `round(RawScore/TheoreticalMax×1,000,000)`，上限 1,000,000。
- HUD 实时 CLEAR 使用当前得分除以已判定单元理论满分；结算 CLEAR 使用全谱理论满分。
- 评级阈值：Ω≥98、S≥95、A≥90、B≥80，否则 C。
- Health/Boost 已按 TotalMainNote 缩放并钳制，但当前不显示 UI，也不触发 GameOver。
- `OriginalJudgeMath` 保留原版 Combo/raw score/CLEAR 数学，不接入社区版 HUD 与存档。

## 3. 游玩布局与视觉

所有坐标均为 1920×1080 设计坐标，权威常量位于 `GameplayMain.cs` 顶部。

| 项目 | 当前值/规则 |
| --- | --- |
| Center 判定线 | `y=861` |
| Mixer 装饰条 | `y=632`，`x=675..1244`；不是判定线 |
| Center 左缘映射 | `x=280+273.2·P`；宽度 `W·273.2·0.95` |
| 侧轨判定线 | `x=184/1736` |
| 侧轨命中 y | 左右对称 `y=840−115·(P+W/2)` |
| 侧轨条长度 | 单位 102px，实际 `W·102·0.95`；BarLine 实际 `W·190·0.95` |
| 可见行程 | Center 790px；Side 691px |
| 侧轨距离尺度 | 0.75 |
| Hold | 琥珀渐变头尾 + 半透明填充 + 亮描边；中间节点隐藏，远端不钳制、不缩窄 |
| Mixer | Body 为节点间细连线；控制节点隐藏；仅在当前接上时在线上显示动态头 |
| DropSpeeds | 相邻事件间线性插值；距离 = 剩余 BarTime × 当前流速 × 玩家落速 |
| 触摸区域 | `y>771` 为 Center；上部 `x<400` 为 Left、`x>1520` 为 Right |

HUD 当前布局：顶部为 P/GR/GD/M、CLEAR、M.COMBO 计数行；暂停按钮位于其下方中央；
判定字在线上方，Combo 在线下安全区；左下为曲名/难度，右下为分数。结算前隐藏舞台、
音符和 HUD，结果层先铺不透明背景，避免封面缺失时透层。

全部可见 Note 使用 `shaders/note_surface.gdshader` 的程序化切角渐变材质；类型纹理和
左右侧亮度参数已统一。命中爆发为 12 帧式程序化效果，整体亮度系数 1.30。Hold 与侧轨
Mixer 的持续效果在 0.28 秒内爬到满亮，之后只循环纹理，松开立即消失。

| 类型 | 当前视觉生命周期 |
| --- | --- |
| Tap / EX-Tap / Drag | 未判定时越线后 24px 满亮、再用 40px 淡出；命中立即移除本体，只留爆发 |
| Hold | 头部使用普通下穿，Body 在线上裁剪；命中后的头、Body 近端和效果固定在线上 |
| Mixer | 不下穿；Miss 静默回收静态头，Body 和随时重接不受影响 |
| Mine | 触发时显示红色危险爆发；安全到线直接回收 |
| BarLine | 到线立即回收，不下穿、不停留 |

背景和纵深参照线同样为程序化绘制。
音符位置使用固定二维轨道映射，不做透视投影；变速回溯时，已生成 note 即使暂时退出
画面也保留到命中或过期。

## 4. 仍需处理

- 最新命中爆发亮度和 Note 生命周期分流尚待用户下次运行时目测确认。
- 发布级按钮和音效素材尚未生产；当前 Note 材质、瞬时爆发和持续效果均为程序化运行时
  实现，预览 GIF 只作设计参考。
- 曲绘由社区谱师随谱面包提供，不使用 AI 生成；缺失曲绘时使用 clean-room 占位符。
- 网页谱面编辑器、本地编辑器 app、ASP.NET Core 社区服务器尚未开工。
- 原版边界仍未确认：设备 `NSTouchWidth` 真值、重叠触点消费、BPM 切段瞬间 Holding
  调度、模式 MaxHealth、当前版本 Type dispatch、Buff/EX Boost。
- 设置页已有 Hit/UI 音量总线，但完整打击音和 UI 音效资产链路尚未完成。

## 5. 目录地图

- `client/` — Godot 4.7.1 .NET 客户端，主场景 `scenes/main.tscn`
  - `scripts/game/GameplayMain.cs` — 输入、判定运行时、渲染、HUD、暂停和结算
  - `scripts/game/NoteView.cs`、`GameplayHitBloom.cs`、`GameplaySustainEffect.cs` —
    Note 材质、瞬时爆发和持续接触效果
  - `scripts/game/ChartPack.cs`、`GameSession.cs` — 谱面包与跨场景状态
  - `scripts/game/GameSettings.cs`、`ScoreStore.cs` — 设置与成绩持久化
  - `scripts/audio/SongClock.cs` — 音频时钟、延迟补偿和用户偏移
  - `scripts/ui/` — 主菜单以外的程序化 UI 与通用控件
- `shared/` — 纯 C# 谱面模型、loader、判定计划和规则
- `tools/core-tests/` — shared 核心断言式测试
- `tools/` — 谱面、逆向、标定和素材辅助脚本
- `docs/` — 当前规格、判定报告、几何标定和 UI 样式稿

## 6. 构建与验证

### 6.1 构建和核心测试

```powershell
cd client
dotnet build
```

构建必须 0 错误。shared 核心测试从仓库根目录运行：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj
```

2026-08-11 最近一次记录：客户端 0 warning / 0 error，core-tests 全部通过（含 DropSpeed
插值、回溯换向和 BarTime 双向换算）；
The Villager Hard 为 1262 个计分单元/684 个主判定，Tablear Giga 为 1903/1734，
主判定数均与各自 `Baked_TotalMainNote` 一致。

### 6.2 启动、截图和录屏

启动 Godot 前必须先结束残留进程：

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe
```

Godot 4.7.1 mono 位于 `../godot/Godot_v4.7.1-stable_mono_win64/`。标准版没有 C# 支持，
不要使用。截图/录屏时在 `client/` 目录用 Git Bash，于同一次调用内完成启动、采集和结束进程：

```bash
mkdir -p ../tmp_shots
DUX_START_SEC=0 ../../godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
  --path . --position 0,0 --resolution 1600x900 --borderless --always-on-top \
  res://scenes/gameplay.tscn > ../tmp_shots/godot.log 2>&1 &
godot_pid=$!
sleep 3

ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 \
  -i desktop -frames:v 1 /d/Workspace/Dynamix/community/tmp_shots/shot.png

kill $godot_pid
```

录屏时把截图命令替换为：

```bash
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -framerate 30 \
  -i desktop -t 45 -c:v libx264 -pix_fmt yuv420p -preset veryfast \
  /d/Workspace/Dynamix/community/tmp_shots/demo.mp4
```

`-frames:v` 必须位于 `-i` 之后；yuv420p 输出尺寸必须为偶数。直接调试游玩场景可修改
`DUX_START_SEC` 与参考视频做同刻对比。游玩调试键：F1 切换 Auto，Esc 暂停，F12 跳到
谱尾前 3 秒。

## 7. 环境注意事项

- .NET SDK 已安装；项目目标为 net9.0，可由本机更高版本 SDK 构建。
- ComfyUI 便携版位于 `../comfyui/ComfyUI_windows_portable/`，默认端口 8188。
- AVD 名为 `Dynamite_test`。adb root 不跨重启；Frida server 需要 root 和
  `setenforce 0`。已安装包签名与 `_rev/apks` 不同，不可覆盖安装。
- headless 下 `SongClock` 使用系统计时器，音频自然结束分支与窗口模式不同；结算结束逻辑
  需要在窗口模式确认。
