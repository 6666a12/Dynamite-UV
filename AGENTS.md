# AGENTS.md — community/

接手本目录前必读 `docs/handoff.md`（当前实现、固定决策、待办和验证流程）；专题入口见 `docs/README.md`。
`docs/archive/` 只供历史追溯，不作为当前待办或恢复旧架构的依据。

要点速览：

- clean-room 社区音游：**不得把任何原版素材/谱面放进 Public 发布内容**；整个 `client/testdata/` 仅限受控开发/Internal 使用。
- 当前制谱器能力见 `docs/dyna-maker-uv.md`；不要把底层写包能力或旧阶段记录当作现有 UI 已完成保存。
- 编辑器 UI 设计稿与施工约定见 `docs/editor-ui-design.md`（Penpot、当前/目标边界、MCP 安全约定）；改设计稿前先读。
- 构建：`cd client && dotnet build`（须 0 错误）；shared 核心单测在 `tools/core-tests`，编辑器回归在 `tools/chart-editor-tests`。
- 游玩布局常量在 `client/scripts/game/GameplayMain.cs` 顶部（固定 1920×1080 + `canvas_items` 等比黑边，不使用自适应游玩布局）；改数值前先看测量依据。
- 启动 Godot 前先结束 `Godot_v4.7.1-stable_mono_win64_console.exe` 和 `Godot_v4.7.1-stable_mono_win64.exe`（避免残留窗口污染截图）。
- 普通修改后**不启动游戏代替用户目测**；确需截图/录屏时按 handoff §6.2 操作。
- 状态核对、历史构建/测试结果和用户实机验收分开写；仅文档/源码静态检查不能宣称当前构建或测试已通过。
