# Dynamite Universe

Dynamix 风格的 clean-room 社区音游重实现。公开发布包不使用任何原版素材，
也不包含原版谱面；内部测试包可在受控开发环境中临时携带官方测试数据，绝不可公开分发。

> 本仓库目前未提供项目级 `LICENSE`，项目原创代码和原创资产保留所有权利；公开可见不等于
> 获得复制、修改或再分发授权。第三方内容见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

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
- `tools/chart-editor/` — 独立 Avalonia / .NET 9 本地 v2 制谱器；不集成进 Godot 客户端。
- `tools/chart-editor-core/`、`tools/chart-editor-tests/` — 编辑器可变文档、保存与回归。
- `tools/` — 谱面打包/统计、逆向分析和素材生成辅助脚本；研究工具不进入客户端 APK。
- `release/` — APK 身份/内容政策和版本化资产来源清单。
- `schemas/chart-format-v2/` — v2 机器可读 Schema、clean-room golden pack 和 Digest 向量。
- `docs/` — 当前交接、玩法规格、正式 v2 格式、判定报告、几何标定和 UI 样式稿。

独立 Avalonia 本地制谱器已位于 `tools/chart-editor/`；网页交互稿 `docs/ui-mock/editor.html` 仅作为
视觉参考。ASP.NET Core 社区服务器仍处于规划阶段，仓库内尚无 `server/` 工程。

## 文档导航

| 文档 | 用途 |
| --- | --- |
| [`docs/handoff.md`](docs/handoff.md) | 当前实现、用户拍板项、待办与运行/验证流程 |
| [`docs/gameplay-spec.md`](docs/gameplay-spec.md) | 当前 legacy loader、运行时玩法、判定和布局规格 |
| [`docs/runtime-architecture.md`](docs/runtime-architecture.md) | 客户端、shared 与工具链的职责边界及重构不变量 |
| [`docs/chart-format-v2.md`](docs/chart-format-v2.md) | 正式 Dynamite Universe Chart Format v2；客户端、工具和编辑器已接入 |
| [`schemas/chart-format-v2/`](schemas/chart-format-v2/) | v2 Draft 2020-12 Schema、clean-room 示例与 Digest 测试向量 |
| [`docs/original-judgement-analysis.md`](docs/original-judgement-analysis.md) | 原版判定机制与仍未确认的边界 |
| [`docs/video-geometry-analysis.md`](docs/video-geometry-analysis.md) | 当前采用的视频几何结论 |
| [`docs/pixel-calibration.md`](docs/pixel-calibration.md) | 当前 1920×1080 布局标定表 |
| [`tools/chart_stats_report.md`](tools/chart_stats_report.md) | 开发样本的聚合结构统计，不作为 Type 语义来源 |
| [`docs/releasing.md`](docs/releasing.md) | 公开包/内部测试包的隔离、导出和 APK 强制检查 |
| [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) | 第三方字体和运行时组件许可说明 |
| [`docs/ui-mock/index.html`](docs/ui-mock/index.html) | UI 样式稿 |
| [`docs/ui-mock/motion-preview.html`](docs/ui-mock/motion-preview.html) | Signal Lock / 独立 Track Handoff / Hit Echo 可交互动效预览（静态 handoff 可显示合成封面，移动卡保持纯文本） |
| [`docs/ui-motion.md`](docs/ui-motion.md) | Full/Reduced/Off UI 动效 token、TRACK HANDOFF 状态顺序与行为边界 |

## 构建与核心测试

```powershell
cd client
dotnet build
```

构建要求 0 错误。shared 核心测试从仓库根目录运行：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj
```

默认核心测试只使用版本化的 clean-room 合成谱，不依赖 `client/testdata/`。需要对本机
官方开发语料做额外回归时，显式使用 `--dev-testdata`；该结果不替代公开发布门禁：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj -- --dev-testdata client/testdata/packs
```

APK 分为 **Public Release** 与 **Internal Testdata** 两种，必须使用各自 preset 和最终
APK 检查模式。完整命令及验收条件见 [`docs/releasing.md`](docs/releasing.md)。

按项目约定，普通修改后无需代替用户启动游戏目测；只有明确需要截图或录屏对比时，
才按 `docs/handoff.md` 的流程启动 Godot，并且启动前必须先结束旧 Godot 进程。
