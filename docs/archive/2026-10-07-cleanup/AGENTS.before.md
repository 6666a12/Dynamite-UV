# AGENTS.md — community/

接手本目录前必读 `docs/handoff.md`（当前实现、拍板项、待办和验证流程）。

要点速览：

- clean-room 社区音游：**不得把任何原版素材/谱面放进发布内容**；`client/testdata/` 下两份谱面包仅开发用。
- 编辑器 UI 设计稿与施工约定见 `docs/editor-ui-design.md`（Penpot 文件、交互决策、MCP 施工坑）；改设计稿前先读它。
- 构建：`cd client && dotnet build`（须 0 错误）；shared 核心单测在 `tools/core-tests`。
- 游玩布局常量在 `client/scripts/game/GameplayMain.cs` 顶部（固定 1920×1080 + `canvas_items` keep 黑边；不使用自适应游玩布局），改数值前先看注释里的测量依据。
- 启动 Godot 前先 `taskkill //F //IM Godot_v4.7.1-stable_mono_win64*.exe`（用户要求，避免残留窗口污染截图）。
- 改完代码**不用启动游戏跑测试**，用户自己看效果；需要截图对比时再按 handoff §4 操作。
