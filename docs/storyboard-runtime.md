# Storyboard A1–A3：游玩舞台原型与验证

状态：开发原型。脚本语义见[首轮脚本规格](storyboard-script-spec.md)；独立工作台与 C#／C++ SDK 仍是[下一阶段](storyboard-editor-plan.md)。本轮没有改 v2／v2.1 谱面或 Gameplay Digest，也未开启商店、录制出包或公开发行的 Storyboard 合同。

## 如何试用

- **Editor/Internal → 设置 → 画面与动效 → Storyboard**，默认开启；Public 无这组设置。关闭、或界面动效设为减弱／关闭时，在读取索引前返回，保留原游戏背景。打击特效设置独立。**演出内存预算**有 128／256 MiB 两档（分别为移动／桌面默认），下局生效；每档最多 128 槽，见[流式驻留](<storyboard-residency-plan.md>)。
- **演出亮度**可在同页调到 0–100%，默认 100%（保持作者亮度）。它只调制 Storyboard 与替换底色的 RGB；原有背景（`keep`）、脚本的 Alpha、判定线、音符及 HUD 不变。0% 只是画成黑色，仍会加载和更新；降低设备负担应关闭 Storyboard。不会按 Note 或休息段自动切换亮度。
- 目前只发现 Editor/Internal 中用户本地的严格 v2 目录谱包，跳过商店安装包和 `res://` 资源。显式绑定放在包内 `storyboard/index.json`，结构为 `{"version":0,"charts":{"精确差分ID":"对应脚本.uvsb"}}`；脚本路径相对 `storyboard/`。脚本根素材路径相对脚本所在目录，首轮为 PNG。用[原创 Prism Relay 生成器](../tools/storyboard-demo/README.md)可重建样片，不需改现有谱。
- `backdrop mode="keep"` 是默认，演出叠在离子余辉上；`backdrop mode="replace" color="#10131A"` 的不透明底色在索引、脚本、素材头及编译通过后挂载，覆盖整个 1920×1080。预取来不及时保留静态底、不画部分演出；后台发现坏 PNG 则取消演出并恢复原背景。没有 RenderingDevice 的兼容渲染使用原背景。

## 实现边界

| 层 | 入口与职责 |
| --- | --- |
| 数据 | [StoryboardDocument](../shared/Storyboard/StoryboardDocument.cs) 读取并严格校验 KDL；[StoryboardCompiler](../shared/Storyboard/StoryboardCompiler.cs) 解析图集尺寸和时间窗；[StoryboardSampler](../shared/Storyboard/StoryboardSampler.cs) 按未校准的媒体秒求任意时刻的绘制状态，无需运行脚本代码。 |
| 资源和局生命周期 | [GameplayStoryboardLoader](<../client/scripts/game/gameplay/GameplayStoryboardLoader.cs>) 校验本地绑定与 PNG 头；[纹理调度器](<../client/scripts/game/gameplay/StoryboardTextureStore.cs>)负责窗口、后台解码、主线程提交和退役；[GameplayStoryboardSession](<../client/scripts/game/gameplay/GameplayStoryboardSession.cs>)管理首屏预热、跳转、降级和离场；[SongClock](<../client/scripts/audio/SongClock.cs>)一次读出媒体与判定时刻。 |
| 绘制 | [GameplayStageRenderer](../presentation/game/GameplayStageRenderer.cs) 背景、演出、玩法装饰／Note 分开；[StoryboardCanvas](../presentation/game/storyboard/StoryboardCanvas.cs) 保序合并相邻绘制状态，[StoryboardGeometry](../presentation/game/storyboard/StoryboardGeometry.cs) 将裁切框变换到舞台坐标后裁多边形，不以轴对齐边界凑近似。脚本不能越过判定线、Note、提示或 HUD。 |

首轮包括组／`clip`／`repeat`、媒体／拍时钟、基础属性轨、加色、图集与序列、矩形／椭圆、线性渐变、祖先矩形裁切以及直线／三次贝塞尔路径。逐场景淘汰不在 1920×1080 内的绘制项。渐变颜色轨和色标先按线性 RGB 求值；首轮交给现有 Godot SDR canvas 混合，不能据此宣称跨渲染器的逐像素线性光合成已冻结。椭圆及渐变分别使用多段几何和纹理查表，边缘不是理想解析曲线。游戏暂停冻结位置；自然结束/失败停止绘制，不延长结算。

## Windows 样片与性能

下方 2026-11-27 表格与像素对照是流式接入**前**的历史证据；流式回归见[验证记录](<verification.md#2026-11-28--storyboard-审查修复与流式驻留>)。样片为原创合成音乐与原创图形。同一首 20.4 秒音乐、同一谱面，分别用 `title-only`、`demo-keep` 和 `demo-replace` 看标题入场、背景差异、带节拍的加色脉冲、路径、图集换帧和倾斜裁切；用 100／500／1500 个同时活动的 18×18 同纹理旋转 Sprite 看扩展成本。截图与录屏走真实 Gameplay 场景，Auto 不写成绩；固定 60 FPS 的视频仅用于看画面，不作为真实帧率数据。

### 复跑

先按[运行约定](verification.md#运行截图和录屏)结束残留两个 Mono Godot 进程。确认设置中 Storyboard 与完整动效已开。测试程序只通过已有 Editor/Internal 验证入口选谱；Off 对照增加一次局部跳过，不写玩家偏好。

```powershell
python -B tools/storyboard-demo/generate.py --output .tmp/storyboard-demo/prism-relay-run

# Debug 是本机直接运行 Godot 项目时使用的 C# 构建。
dotnet build client/DynamiteUniverse.csproj --no-restore -c Debug
python -B tools/storyboard-demo/verify_windows.py bench --pack .tmp/storyboard-demo/prism-relay-run --output .tmp/storyboard-bench --rounds 3
python -B tools/storyboard-demo/verify_windows.py capture --pack .tmp/storyboard-demo/prism-relay-run --output .tmp/storyboard-video --chart demo-replace

# 纯数据回归与采样微基准；不启动 Godot。
dotnet run -c Release --project tools/core-tests/CoreTests.csproj -- --storyboard-only
dotnet run -c Release --project tools/core-tests/CoreTests.csproj -- --storyboard-benchmark
```

### 实测，2026-11-27

Ryzen 9 7940HX、RTX 4060 Laptop GPU；Windows、Godot 4.7.1 Mono Debug、Vulkan Forward Mobile、1280×720 窗口（舞台逻辑 1920×1080）、关闭 VSync 和 FPS 上限。每档三次独立进程，取各次统计量的中位数；不是同一帧内 GPU 单独耗时。

| 同屏 Sprite | 平均 FPS 的中位数 | 帧间隔 P95 的中位数 | Storyboard 更新 P95 的中位数 | 脚本字节 | KDL 解析 P50 / 编译 P50 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 0（关闭） | 481.31 | 3.20 ms | 无 | 不读 | 不执行 |
| 100 | 400.13 | 3.63 ms | 0.14 ms | 21,238 | 38.98 / 20.12 ms |
| 500 | 321.70 | 4.41 ms | 0.50 ms | 105,548 | 54.94 / 23.57 ms |
| 1500 | 196.09 | 6.63 ms | 1.70 ms | 316,229 | 98.72 / 26.82 ms |

完整 `demo-replace` 一次本地单独截图进程的冷加载：8,758 字节，读取＋UTF-8 0.44 ms、KDL 解析与校验 47.62 ms、素材预检 6.46 ms、编译 24.38 ms、PNG 解码及 Godot 纹理创建约 59.90 ms（含独立裁口的纹理）；数字包含各阶段首轮 JIT，不是 KDL 解释器的热态单独速度。在桌面机上，本批 1500 个小 Sprite 的吞吐较 Off 中位数约低 59%，不能外推到手机，也不能外推到大量大图透明叠加、很多材质切换或复杂裁切。Off 和 On 的底纹不同；比较三个 On 档位时底色、素材尺寸与图层模式相同。图集为避免串色在加载阶段裁成独立纹理，和根素材一同计入像素预算。

### 优化后的 A/B

纯采样优化以 5 次 Release 交错运行复测：**101.86 → 82.95 µs/sample（−18.6%）**，仍为 0 managed bytes、相同 8,174,880 draw items 和 checksum。真实 Godot A/B 使用同一 1280×720、Vulkan Mobile、关闭 VSync／FPS 上限、100% 演出亮度；`verify_windows.py bench --compare-batch --rounds 3` 将逐 Sprite 和批绘交错执行，且验证实际实例数与批次数。批绘仅在 Forward Mobile 启用：整个相邻状态段至少 32 个、同纹理、普通 Alpha、无裁切才生效；Add、形状、裁切、混合纹理段和短片段保留直接绘制。实例容量不超过最大绘制项数的两倍，超预算回退。

| 同屏 Sprite | FPS：直接 → 批绘 | 帧间隔 P95：直接 → 批绘 | Storyboard 更新 P95：直接 → 批绘 |
| ---: | ---: | ---: | ---: |
| 关闭（普通舞台参考） | 476.46 | 3.21 ms | — |
| 100 | 431.73 → 441.45 | 3.50 → 3.41 ms | 0.108 → 0.153 ms |
| 500 | 338.08 → 369.00 | 4.18 → 3.94 ms | 0.416 → 0.616 ms |
| 1500 | 208.20 → 242.51 | 6.29 → 5.60 ms | 1.383 → 2.035 ms |

每列为每次统计量的三次独立进程中位数，两种绘制都使用优化后的求值器。1500 档在三组直接/批绘中均提速，FPS 中位数增 **16.5%**，帧间隔 P95 降 **10.9%**。更新更贵，因为它现在也打包并提交缓冲；整帧仍更快。该场景的 Sprite 同纹理且都较小，不能外推到 Android、复杂裁切或大面积透明重叠。

这一批的 Off 不是逐项 Storyboard 渲染，而是普通游戏背景；因此它只适合作为整场设备吞吐参考，不把差值全部归因于 Storyboard。`STORYBOARD_BENCH` 的 FPS 是由相邻 `_Process` 的真实间隔计算的整局吞吐，无法分离 GPU 和脚本；`update_p95_ms` 只计更新，批绘时还计入打包和提交实例缓冲。后续若要定位 GPU 时间，需补 viewport Canvas draw calls／GPU measured time，以及背景一致的零 Sprite 对照。

实例批绘资源在离场时释放；单位四边形与每实例颜色保留变换、色调和透明度。Windows 同帧像素专项用两段各 48 个着色、旋转、翻转及半透明重叠的 Sprite，两段分别用双线性／最近点采样，中间穿插 Add 图形；在 100% 与 45% 亮度时，取覆盖两段的 1000×270 ROI，直接与实例路径逐像素一致。另验证 0% 的替换底色仍全黑不透、`keep` 的默认背景及判定线的取样点不受亮度影响。

普通玩法渲染下一步先测密集 Note／Hold，而不是从本样本的六个 Active Note 推断。优先候选是将每帧不变的 BPM／Scroll 投影输入从每个 Note/连段提取出来，以及避免已保持接触的 Hold 重复提交相同 shader 参数；本轮没有改这两条路径。

统计 `STORYBOARD_LOAD` 分别记录读取／UTF-8 解码、KDL 解析和语义校验、图片预检、时间／图集编译和 PNG／纹理创建提交的墙钟时间。GPU 实际上传可能在后续，不从这些数字推断显存上传时间。`STORYBOARD_BENCH` 从媒体 4.25 到 15.75 秒记录真实 `_Process` 间隔及 Storyboard 更新 P95；后者不包含随后由 Godot 调度的 `_Draw` 与 GPU 工作。帧间隔反映完整游戏的吞吐，不是孤立的 GPU 耗时。日志中的 `max_items` 是采样出的同时活动项，`max_runs` 是相邻混合/过滤组，不是 GPU draw call 数量。
