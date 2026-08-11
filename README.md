# DUX-Community

Dynamix 风格的 clean-room 社区音游重实现，不使用任何原版素材，也不发布原版谱面。

当前客户端已经打通“主菜单 → 选曲 → 游玩 → 结算”闭环，并实现设置、本地成绩、
玩家谱面包扫描、三轨输入和 Hold/Mixer 等主体判定。接手项目先阅读
[`docs/handoff.md`](docs/handoff.md)。

## 当前实现

- Godot 4.7.1 .NET / C#，目标框架 `net9.0`。
- 固定 1920×1080 设计坐标，`canvas_items` keep 黑边；不使用自适应游玩布局。
- 支持 Type 1–9、三轨、DropSpeeds、Drag、EX-Tap、Hold、Mixer、Mine 和小节线。
- 显示与存档分数统一归一化为 1,000,000；成绩写入 `user://scores.json`。
- 设置写入 `user://settings.json`，包含判定偏移、落速和 Music/Hit/UI 音量。
- `user://charts/` 可导入玩家谱面包；`client/testdata/` 内容仅供开发，不得发布。

## 目录

- `client/` — Godot 客户端；场景是轻量入口，界面和游玩舞台主要由 C# 构建。
- `shared/` — 不依赖 Godot 的谱面模型、加载器和判定核心。
- `tools/core-tests/` — shared 核心的断言式测试程序。
- `tools/` — 谱面打包/统计、逆向分析和素材生成辅助脚本。
- `docs/` — 当前交接、玩法规格、判定报告、几何标定和 UI 样式稿。

谱面编辑器和 ASP.NET Core 社区服务器仍处于规划阶段，仓库内尚无 `server/` 工程。

## 文档导航

| 文档 | 用途 |
| --- | --- |
| [`docs/handoff.md`](docs/handoff.md) | 当前实现、用户拍板项、待办与运行/验证流程 |
| [`docs/gameplay-spec.md`](docs/gameplay-spec.md) | 谱面格式、Type 语义、判定和社区玩法规格 |
| [`docs/original-judgement-analysis.md`](docs/original-judgement-analysis.md) | 原版判定机制与仍未确认的边界 |
| [`docs/video-geometry-analysis.md`](docs/video-geometry-analysis.md) | 当前采用的视频几何结论 |
| [`docs/pixel-calibration.md`](docs/pixel-calibration.md) | 当前 1920×1080 布局标定表 |
| [`tools/chart_stats_report.md`](tools/chart_stats_report.md) | 开发样本的聚合结构统计，不作为 Type 语义来源 |
| [`docs/ui-mock/index.html`](docs/ui-mock/index.html) | UI 样式稿 |

## 构建与核心测试

```powershell
cd client
dotnet build
```

构建要求 0 错误。shared 核心测试从仓库根目录运行：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj
```

按项目约定，普通修改后无需代替用户启动游戏目测；只有明确需要截图或录屏对比时，
才按 `docs/handoff.md` 的流程启动 Godot，并且启动前必须先结束旧 Godot 进程。
