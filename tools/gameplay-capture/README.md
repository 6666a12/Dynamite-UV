# 谱面视频：正式场景原型

下文是走 AVI 的 Windows 开发样片流程，不是 DynaMaker UV 的成品导出路径。它保留作可复现的历史对照，使用原创合成音频、封面和 strict v2 自造谱。

DynaMaker UV 现在由独立游戏录制进程边渲染边编码。参考[原生 MovieWriter、协议和队列测试](<native/README.md>)及[候选包构建](<../editor-release/README.md#optional-chart-video-export>)。

从仓库根目录执行。需要已导入公开资源的 Godot 4.7.1 Mono 开发工程、.NET 9 SDK、Python 3.11+、FFmpeg 和 FFprobe。先按[运行约定](<../../docs/verification.md#运行截图和录屏>)结束残留 Godot，再运行：

```powershell
 dotnet build client/DynamiteUniverse.csproj --configuration Debug
 python -B tools/gameplay-capture/record.py --preview-seconds 3.5 --result-hold-seconds 4 --codec av1 --preset medium
 # 不指定 --codec 时仍默认 H.264；例如 HEVC 高速档：
 python -B tools/gameplay-capture/record.py --codec hevc --preset high
 python -B tools/gameplay-capture/verify_audio.py '<本次 RUN 输出的绝对路径>'
 python -B -m unittest discover -s tools/gameplay-capture -p test_record.py
```

- 准备器复用[正式菜单 QA](<../client-menu-qa/README.md>)的资产白名单与隔离环境，使用全新的 `user://`，不复制 `client/testdata`、用户账号、存档或既有谱包。仅临时工程加载录制驱动，进程不用公网或本地服务。
- 正式选曲场景播放谱包配置的试听段；驱动向正式 UI 发送展开和 START 点击，经过 Track Handoff，游戏从音乐起点按真实 Auto 规则跑到结算。录制专用时钟只在开发引擎实际处于 Movie Maker 模式并收到显式参数时生效，不带入设备校准、不改写谱面偏移。
- `--preview-seconds` / `--result-hold-seconds` 默认 2，接受 0–60 秒并取最近的 60 fps 帧。试听不超过谱包区间（自造谱为 5 秒），不写入谱包；0 从正式 Track Handoff 起片，临时预热不进成片。结算留屏从评级揭晓后算，0 仍完整播放字标与评级动画。
- `--codec` 和 `--preset` 相互独立。`--preset slow/medium/high` 是速度档，`high` 最快，默认 `medium`，不改变所选 CRF。`--crf` 可覆盖各格式默认值；同一编码器内数值越小通常越保真，不同编码器的 CRF 不等价。启动录制前检查 FFmpeg 是否包含所选软件编码器，缺少时直接报错，不回退到其他格式或硬编。

  | `--codec` | FFmpeg / MP4 标识 | `slow` / `medium` / `high` 映射 | 默认 CRF / 可选范围 |
  | --- | --- | --- | --- |
  | `h264`（默认，别名 `x264`） | `libx264` / `avc1` | `slow` / `fast` / `veryfast` | 16 / 0–51 |
  | `hevc`（别名 `x265`、`h265`） | `libx265` / `hvc1` | `slow` / `medium` / `veryfast` | 20 / 0–51 |
  | `av1` | `libsvtav1` / `av01` | `4` / `7` / `9` | 24 / 1–63 |

- 三种格式都输出 MP4，固定 1920×1080@60、8-bit 4:2:0 和 192 kbps AAC 立体声。编码设置不改变游戏画面配置、播放速度或中间 AVI 的采集质量。AV1 适合接收该格式的现代平台，上传与播放兼容性仍取决于具体平台和设备。
- Godot 用固定帧离线采集，关闭垂直同步，不为成片帧率主动等待；实际吞吐取决于渲染、取帧、AVI 及二次编码。不加速成片中的音乐或动画。音频驱动显式选 `Dummy`，不把声音送往系统设备；Movie Maker 仍把内部混音写进 AVI。画面仍经图形窗口渲染，不等于 `--headless` 或后台隐藏窗口已经支持。
- 产物在 `.tmp/gameplay-capture/runs/`。`synthetic-pulse.mp4` 是样片，`metadata.json` 记录实际帧节点、剪去的预热帧、验片参数及分阶段墙钟耗时。`verify_audio.py` 解码成片，匹配试听和游玩源波形，检查切换时没有残留试听及曲首曲尾采样的相对漂移。
- AVI 只作短片中间文件；长谱磁盘/容器限制、取消、对任意未保存差分取快照、Windows 独立录制运行时及编码器分发仍待后续验证。
