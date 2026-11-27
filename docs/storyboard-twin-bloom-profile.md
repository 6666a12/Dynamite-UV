# Twin Bloom：真实谱面 1440p 性能复验

本文记录流式接入前的 Editor/Internal 本机历史基线，不是新流式性能成绩；现行结果见[验证记录](<verification.md#2026-11-28--storyboard-审查修复与流式驻留>)。公开依据：[目标谱面及作者署名](https://osu.ppy.sh/beatmapsets/2623957#mania/5893787)、[官方赛事 Grand Finals 谱包链接](https://osu.ppy.sh/wiki/en/Tournaments/MWC/2026_4K)、[现行单张谱面文本](https://osu.ppy.sh/osu/5893787)。原素材、音频、生成包和演示仅留 Git 忽略的 `.tmp/`，不进发布物、商店或公开录像。

## 转换范围

- 现行 **[4K] Ramification**：3,423 Tap、3,055 Hold，四列放进正面轨道，头尾和同押保留原毫秒时间。Hold 用 UV 的持续音与尾判；不声称复刻 osu!mania 的 SV、输入和评分。Storyboard 使用赛事包收录的版本，现行 `.osu` 与赛事快照略有不同。两版身份和校验在[四键转换说明](<../tools/storyboard-demo/mania_local_chart.md>)。
- `.osb` 有 2,649 个对象（2,632 Sprite、17 Animation），全在 Background，另有约 65.10 s 的 1280×720 H.264 片头。没有要放到 Note 上方的对象。492 张实际引用图像按 RGBA8 一次展开约 783 MiB；当时 eager 上限为 128 声明和 16,777,216 纹理像素，无法直接整曲加载。现在有[流式图片驻留](<storyboard-residency-plan.md>)，仍未验收整曲；全量等效还需正式视频、分段/低档资源和少数命令语义（如同对象随时间变换混合模式）。视频不在这里的测试段内。
- `[82.000,87.500)`：496 个活动对象、65 张纹理保留原分辨率；保留 Sprite 的顺序和纹理，不预合成。`[192.000,204.000)`：峰值 20 个活动对象、16 张纹理，包含大面积加色、故障纹理与透明线条；仅 6 张 1920×1080 线动画帧明确缩为 1600×900，并补偿尺寸，不删对象。部分缓动近似、动画帧边界和相邻指令的细节见[片段转换说明](<../tools/storyboard-demo/twin_bloom_local_osb.md>)，没有逐像素比对原 osu! 客户端。

## 测试口径

Windows Ryzen 9 7940HX / RTX 4060 Laptop（Godot 4.7.1 Mono，Debug，Vulkan Forward Mobile），真实游玩场景 Auto，不写成绩。四列落速 **1.4×**，歌曲仍按 1× 播放；同一难度、音轨、场景设置及亮度 100%，通过媒体时间 seek 并提前预热，把历史判定与特效刷新的启动瞬态留在采样窗外。关闭/黑底/开演出三者的 Gameplay Digest 一致。显式关闭 VSync，逐进程检查应用窗口和 framebuffer 实际都是 2560×1440；不是只传命令行参数。帧间隔是两次脚本更新之间的 wall time，不是屏幕上的实际呈现间隔。

- **Off**：正常游戏背景，是玩家能用的关闭对照。**同黑底**：仅官方片段同款不透明黑底，没有 Sprite，才是隔离舞台绘制的对照。**Storyboard**：黑底加转换后的 Sprite；Game/HUD 始终照常运行。
- Godot viewport GPU/渲染 CPU、Canvas 总 draw calls、主线程脚本按 8 帧取一次；不是 GPU 驱动整条管线、已呈现帧、每 Sprite draw call 或可直接相加的时间。回读 framebuffer 只在采样结束后执行。NVIDIA 1 Hz 设备利用率包含系统其它工作，并会随 GPU 时钟变化；仅作辅助，既不是只测 Godot，也不是帧时。

## 测量结果

无引擎限帧，各模式三轮，交替正/倒序；下表取各进程统计的中位数。高叠层段有较明显的跨轮波动，不能把小差值当成稳定提升。

| 实际 2560×1440 | 模式 | FPS | 帧间隔 P95 | viewport GPU P50／P95 | Canvas calls P50 | 游玩脚本 P95 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 82–87.5 s，496 项 | Off | 194.92 | 7.93 ms | 0.89／1.82 ms | 245 | 0.52 ms |
| | 同黑底 | 196.51 | 7.92 ms | 0.66／1.78 ms | 245 | 0.48 ms |
| | Storyboard | 159.09 | 9.21 ms | 1.21／2.27 ms | 587 | 0.85 ms |
| 192–204 s，峰值 20 项 | Off | 105.91 | 13.44 ms | 1.38／1.74 ms | 445 | 0.70 ms |
| | 同黑底 | 98.35 | 14.21 ms | 0.74／1.67 ms | 447 | 0.76 ms |
| | Storyboard | 88.05 | 15.60 ms | 1.71／3.26 ms | 457 | 0.87 ms |

- **高数量段**：65 张已载入纹理，测量窗内原排序、混合状态及纹理分布没有命中现有整段同纹理实例化（`max_batches=0`）。同黑底到开启：Canvas 中位 draw calls **245→587**、viewport 渲染 CPU P95 **约 0.72→1.01 ms**、Storyboard 更新 P95 **0.004→0.367 ms**。可验证的后续候选是保持原顺序、按混合状态的 atlas 多纹理批绘，并用当前片段复测画面、提交成本和资源开销。
- **高叠层段**：Canvas draw calls 只增约 10，但相对同黑底 GPU P95 **1.67→3.26 ms**，每帧 Storyboard 更新 P95 只有 **0.062 ms**，与原素材的大面积半透明／加色覆盖一致。这里优先看透明填充；若手机上确认受限，可测试单独低分辨率 Storyboard（Note/HUD 保持原清晰度），但普通 Alpha 和加色的透明合成不能假设与全分辨率直接绘制等价，要先做画面对照。
- **不要把设备占用当优化目标**：高数量段实际 `nvidia-smi` 同黑底／开启利用率中位数约 **38%／38%**，但显卡核心频率中位数 **645／1013 MHz**、帧率 **197／159 FPS**。高叠层段相应为 **36%／35%**、**765／840 MHz**、**98／88 FPS**。计数器不是用户任务管理器所选的同一引擎指标。全帧明显长于已测的脚本、viewport 提交或 GPU 任一段；结果不支持“GPU 已吃满”，也不能给剩余时间全贴上“CPU”或“VSync”标签。引擎其余更新、同步和 Windows 呈现等待未单独做 ETW 分解。
- **限帧对照**：高叠层段加 `--cap 60` 单轮，Off／同黑底／开启都是 **60.00 FPS、约 16.72 ms P95**，开启的 GPU P95 为 **4.40 ms**，设备利用率中位数 **26%**；去掉上限后同开启动作 **88.05 FPS、35%**。这是可以提高占用的实测途径，但不是需要默认启用的优化，也不是 Android 60 Hz 上的结论。
- GPU 设备日志仅取落在媒体采样区间、且前面足够覆盖 1 秒的采样点。没有平板实测、温控/续航或公开发行验收。
- 非图形统测通过，两套转换器合计 11 项合成数据测试通过；21 个最终性能进程均通过参数、尺寸、演出状态、Digest 和采样数检查。两段单独的固定 60 FPS 演示由 Godot Movie Maker 输出为 1920×1080，补上对应原音轨，仅证明画面和顺序，不作为性能数据；Movie Maker 强制结束录制时各报 2 个 ObjectDB 实例残留，实时基准没有这条诊断。

## 复跑

转换出上述两个忽略目录后，可用同一采样器复验：

```powershell
python -B tools/storyboard-demo/profile_windows.py `
  --pack .tmp/storyboard-demo/twin-bloom-official-192-204 `
  --flat .tmp/storyboard-demo/twin-bloom-flat `
  --chart ramification --seek 181 --from 192 --until 204 `
  --resolution 2560x1440 --fullscreen --screen 0 --fall-speed-level 14 `
  --rounds 3 --nvidia-monitor --output .tmp/storyboard-evidence/twin-alpha-rerun
```

同黑底对照须复制四键基础包而不是改写任一测量包：在 `storyboard/index.json` 写入 `{"version":0,"charts":{"ramification":"control.uvsb"}}`，并在 `storyboard/control.uvsb` 放两行 `storyboard 0`、`backdrop mode="replace" color="#000000"`。省略 `--flat` 则只跑 Off 与开启。高数量片段改为包 `twin-bloom-official-82-87p5`、`--seek 73 --from 82 --until 87.5`。输出目录须新建；先关闭其他 Godot 进程。`--cap 60` 是引擎 FPS 上限，仍关闭 VSync；不能把它当 60 Hz 平板的触控和呈现验证。
