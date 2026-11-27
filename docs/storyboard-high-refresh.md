# 高刷新率游玩、Storyboard 视频与预算试验

本轮以 [Twin Bloom 1440p 对照](<storyboard-twin-bloom-profile.md>) 为基线。这里的 FPS 是实时游戏循环吞吐，不是显示器实际送达的帧。目标帧时：低端 60 Hz **16.67 ms**；120 Hz **8.33 ms**；144 Hz **6.94 ms**。按设备实际可用刷新率设目标；[M5 iPad Pro](https://support.apple.com/en-us/125406) 的 ProMotion 上限是 120 Hz。以 P95、P99、错过刷新次数、尾延迟、实机热稳与触控共同验收，平均 FPS 过线不算。尚无 iPad/Android 高刷新率验收。

## 命中与批绘

Windows Ryzen 9 7940HX / RTX 4060 Laptop，Godot 4.7.1 Mono Debug，Forward Mobile Vulkan，真实 2560×1440，不限帧、关闭 VSync，1.4× Note 落速。Windows 同条件三轮交错，表格为逐轮统计的中位数。

| 对照 | 平均 FPS | P95 帧间隔 | P99 帧间隔 | 解释 |
| --- | ---: | ---: | ---: | --- |
| 高叠层段，旧版每刷新提交命中爆发 | 92.70 | 14.80 ms | 15.88 ms | 现有 12 帧命中效果，每次显示刷新重新生成圆环、射线等 |
| 高叠层段，复用相同 12 帧的绘制命令 | 157.52 | 10.66 ms | 11.89 ms | 未减特效、未改时钟或判定；均值过 144，帧尾不过 |
| 496 对象，普通纹理 | 146.88 | 10.05 ms | 11.13 ms | Canvas draw calls 中位数 586 |
| 496 对象，试验性 1K 图集 | 149.60 | 9.94 ms | 11.27 ms | Canvas draw calls 中位数 280，帧时未稳定改善 |

两组 A/B 分开运行，互不混用作基线。命中只在可见的离散动画帧改变时请求重绘，保留原来的节点处理顺序与年龄推进，复用时重设年龄。Forward Mobile 隔离验证用同一源码并排比较旧／新，在模拟 144 次更新/秒下覆盖全部六种命中类别、两种旋转、共 228 帧对，各通道差值不超过 2/255（不同位置光栅化容差）；不把整局粒子与持续效果当确定像素。安全渲染模式的阶段埋点显示，高叠层段从游玩脚本结束到 `frame_pre_draw` 的 P95 **约 10.11→6.59 ms**，主要差值发生在 GPU viewport 计时之前；这个区间仍包括其它节点处理、延后回调和命令生成，不是单项热点耗时。`frame_post_draw` 不代表扫描输出。Godot 的 separate 渲染线程一次探索在纹理 update 和 finalize 打出原生错误，未作为优化采用。

小图集只把不大于 256×256 的合格小图原尺寸放入最多一张 1024²（4 MiB）页；保持纹理采样、混合、裁切回退和原顺序，不重排。Twin Bloom 打包 50 张；额外挂载时间约 35 ms（试验版会回读小纹理）。固定时钟真实片段的 6 个 2560×1440 样点（隔离音符特效）每帧至多 115 个像素有差值，单通道最大差 2/255；还不能覆盖所有素材语义。这是 draw calls 有收益、整帧收益不足的例子，默认关闭。下一步优先测持续 Hold 的即时几何、透明命中特效以及热稳平台输出；只有逐 Sprite 提交成为更大瓶颈时，再尝试保序的保留实例批绘。低档 Storyboard 子画布降采样只在透明填充确认是首要瓶颈、Normal/Add 画面对照通过后考虑。

## 内部静音视频试验

仅 Editor/Internal 验证进程，且包须在工作区 `.tmp/` 内。`DUV_STORYBOARD_VIDEO_FILE` 选择该包 `storyboard/` 中的单个本地 `.ogv` 文件名；不扩展稳定版 UVSB 语法，不支持任意路径、网络、H.264 或对外发布。播放器在黑底之上、Storyboard Sprite 和 Note/HUD 之下，共用裁切、亮度，按未经判定校准的歌曲媒体秒同步，原歌曲仍是唯一音源。禁止有音轨或多流素材；上限 1280×720、30 FPS、120 秒、32 MiB。关闭 Storyboard 或降低 UI 动效不加载附件；失败回落为原舞台／替换底色与普通 Sprite。

必须经完整外部解码再做游戏内画面对照。曾出现一种实测花屏：原 H.264 正常，当前 MSYS FFmpeg/libtheora 的 `-g 30` 帧间试片在独立解码也报 `unpack_block_qpis`；那些试片与跑分全部作废。验证过的替换为 **全帧内 `-g 1`**，两档均完整解码 1,951 帧，65.098 s、29.97 FPS，分别 **18.76 MiB（360p）／24.06 MiB（720p）**。内部只接受全帧内文件，仍不等同不可信视频解码沙箱；旧花屏文件已在真实场景复验为预检拒绝，谱面和静态黑底正常。实机 Windows 720p，在 15 秒测试跳转后从引擎回读视频纹理，与同一时点独立解码的 RGB 平均绝对误差 **0.285/255**（P95 1/255），实际舞台未见原试片花屏；验证了冻结 24 帧和继续。没有覆盖所有跳转、所有真机或解码恶意输入。

| 0.5–8.5 秒片头 | 1440p 帧间隔 P95 | P99 | viewport GPU P95 |
| --- | ---: | ---: | ---: |
| 同黑底 | 2.16 ms | 2.87 ms | 0.21 ms |
| 360p 全帧内 | 3.11 ms | 3.81 ms | 0.26 ms |
| 720p 全帧内 | 6.81 ms | 8.15 ms | 0.30 ms |

每组 3 次，无其它活动 Sprite，不能叠加推算密集场面。720p 的尾延迟主要不在 viewport GPU 时间；[Godot 4.7.1 Theora 路径](https://raw.githubusercontent.com/godotengine/godot/4.7.1-stable/modules/theora/video_stream_theora.cpp) 在主线程解码转 RGBA，再更新纹理。实时 8.5 秒样本从启动到结束提交约 252–253 次新纹理（不是呈现帧），曾观察 0.079–0.103 秒的最大播放器时钟偏差。单次从 15 秒开始的同步 seek 约 **437 ms**，目前技术路线不能进入正式高刷档。正式版先建立后台有界解码队列、带 PTS 的取帧、seek 代际失效和静态降级；H.264 如需硬解，按平台验证解码与 GPU 互操作，不能把硬解等同于零拷贝。

## 预算

此前 eager 版为默认 64 MiB／本地显式 96 MiB；`DUV_STORYBOARD_PREVIEW_BUDGET` 不再是运行时入口。原尺寸高叠层片段是 16 张图、20,546,073 像素（约 78.38 MiB 的整集 RGBA8），历史 eager 验证为默认档拒绝、96 MiB 档加载且 Digest 不变。当前 [流式驻留](<storyboard-residency-plan.md>) 已接 128／256 MiB 两档，每档 128 槽，按使用窗口保留，不要求能放下约 783 MiB 的原始全曲。运行时用 `DUV_STORYBOARD_MEMORY_MIB=128|256` 验证；本地转换工具的 `--preview-budget` 仅作旧版兼容审计。当前帧率、内存、视频的验收以[新记录](<verification.md#2026-11-28--storyboard-审查修复与流式驻留>)为准。

## 复跑要点

- [Windows 脚本](<../tools/storyboard-demo/profile_windows.py>)：按前一轮 1440p 设置，命中对照加 `--case on --case redraw-hits --render-thread safe --phases`；小图集加 `--case on --case atlas`。视频把经过严格解码的文件放到同黑底验证包的 `storyboard/` 目录，在 `[0.5,8.5)` 取样，`--seek 0 --case flat --case video --case video-low --video-file twin-video.ogv --video-low-file twin-video-low.ogv`。`--phases` 只支持 `safe`，不会误把 separate 的延后回调算成耗时。
- 转换视频时用源分辨率或显式缩放至 640×360，彩色矩阵按需要转换；本案输入是 BT.709、Godot Theora 使用 BT.601 转换，720p 用 `-an -vf scale=in_color_matrix=bt709:out_color_matrix=bt601,format=yuv420p -c:v libtheora -b:v 3000000 -g 1`，360p 用对应尺寸、`-q:v 5`。每个导出先完整执行 `ffmpeg -v error -xerror -err_detect explode -i <fixture.ogv> -an -f null -`。不要用仅读取元数据的通过替代完整解码；最终仍需看实际舞台画面。所有原素材与生成物留在本地忽略目录。
