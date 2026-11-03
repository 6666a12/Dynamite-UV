# AGENTS.md — community/

接手本目录前必读 `docs/handoff.md`（当前实现、固定决策、待办和验证流程）；专题入口见 `docs/README.md`。
`docs/archive/` 只供历史追溯，不作为当前待办或恢复旧架构的依据。

要点速览：

- clean-room 社区音游：**不得把任何原版素材/谱面放进 Public 发布内容**；整个 `client/testdata/` 仅限受控开发/Internal 使用。
- 制谱器主线为 `editor/shell` 的 Avalonia 外壳 + 现有 Godot `editor` 原生舞台；文件、文档命令与快捷键接线状态见 `docs/dyna-maker-uv.md`。不要把核心已支持等同于所有 UI 手势已完成。
- 制谱器将从头设计响应式 UI；旧 Penpot Editor 板只作历史参考。MCP 安全约定与游戏设计稿见 `docs/editor-ui-design.md`，改设计稿前先读。
- 构建：`cd client && dotnet build`（须 0 错误）；shared 核心单测在 `tools/core-tests`，编辑器回归在 `tools/chart-editor-tests`。
- 模块职责和拆分入口见 `docs/module-map.md`。完整非图形检查：`python -B tools/verify.py`（依赖已还原可加 `--no-restore`）。结构改动须通过 `python -B tools/architecture/check.py`；不要用新增 partial 文件或放宽预算代替对象职责拆分。
- 模块化的目标是让新功能有明确落点、让不同模块能隔离维护。按状态拥有者、输入输出、生命周期和依赖方向划边界；完成时必须说清调用方保留什么、行为如何回归，而不是以缩短行数或增加文件数为目标。
- 物理目录同样按功能族组织；场景/程序入口可留根，纯模型跟随所属功能，`common` 只放跨族共享类型。移动源码时同步 `.uid`、场景/测试链接、结构策略和活跃文档，不改不可变归档。
- 游玩布局常量在 `client/scripts/game/GameplayMain.cs` 顶部（固定 1920×1080 + `canvas_items` 等比黑边，不使用自适应游玩布局）；改数值前先看测量依据。
- 启动 Godot 前先结束 `Godot_v4.7.1-stable_mono_win64_console.exe` 和 `Godot_v4.7.1-stable_mono_win64.exe`（避免残留窗口污染截图）。
- 普通修改后**不启动游戏代替用户目测**；确需截图/录屏时按 handoff §6.2 操作。
- 实际执行、历史构建/测试、测试包身份和用户实机验收统一见 `docs/verification.md`，分开记录；静态检查不能宣称当前构建或实机通过。归档、移动和文档职责按 `docs/README.md`，不要向 handoff 顶部继续叠阶段流水。
