# 2026-11-09 文档整理记录

## 范围与原则

本次只整理文档，不改产品源码、格式、测试或发布策略，不提交或推送。用户确认游戏 HUD 本体实际效果满意；追加确认最近一批制谱器整体满意可收尾、音符材质与命中特效及模式切换基本通过。后续继续本体视觉微调和制谱器完善。

完整确认和未测范围集中在 [验证记录](../../verification.md#用户反馈与验收边界)。不推定 Android/具体设备/所有输入组合/低端性能通过；Mixer 暂停恢复、快速切曲/试听未获得新的确认。模式切换通过不等于成绩隔离已实现。

## 归档与导航变更

| 来源 | 去向 / 处理 |
| --- | --- |
| verification 中独立 BLEED v1–v8 试稿流水 | [设计过程](../art/2026-11-08-09-bleed-hud-design.md)，原有失败、未接入和图像工具限制按当时时点保留 |
| verification 中应用图标探索 | [候选记录](../art/2026-11-08-app-icon-candidates.md)，仍未选定/未接入/未排期 |
| verification 中 11月2–8日逐轮实施与验证 | [制谱器/格式历史](2026-11-02-08-editor-verification.md)；当前页留摘要与索引 |
| verification 中10月记录 | [游戏与结构历史](2026-10-verification.md)；旧测试包身份留在当前验证页并标明历史 |
| editor-debug-review-2026-11-07.md | [一次性审查](2026-11-07-editor-debug-review.md)，不再放当前说明表 |
| modularity-review-2026-10-26.md | [结构审查](2026-10-26-modularity-review.md)，历史源码位置失效的链接改为历史路径文字，不伪装当前可跳转代码 |
| editor-ui-regression-2026-11-07.md | 活跃覆盖清单改名 [editor-ui-regression.md](../../editor-ui-regression.md)；实现流水归入 [实施切片](2026-11-07-editor-ui-implementation.md) |
| editor-ui-design 旧 Editor 板、字体/缩放、交互目标 | [旧设计](../art/2026-10-editor-ui-design.md)；保留当前游戏板位、规范、MCP 安全规则 |
| ui-motion §9.1–§9.4 旧编辑器目标 | [旧动效目标](../art/2026-10-editor-motion-targets.md)；粒子 §9.5 保留兼容锚点并明确 Auto 预览已复用 |

未归档玩法/v2合同、原版证据与测量、发布安全、制谱器操作、HUD本体说明及有效粒子/动效规范。未移动或删除设计媒体、脚本、日志、APK、Blender源或其他工作区；既有不可变归档不回写。

## 当前状态修正

- 根 README 不再称血条 UI 待扩展；handoff 不再把已确认 HUD/材质/模式切换/制谱器整体列为未验收。
- `runtime-architecture.md` 与实际耗尽顺序一致：第8步后发布0血HUD，再暂停并进入失败结果，不写普通帧trace。
- UI 设计文档使用当前 TECH 难度色和 HUD 特例，不再用旧切角/中央装饰/线段范围覆盖 v8。
- 选曲滚动合同留在活跃设计文档，不依赖旧编辑器历史章节。
- 最新完整 verify 的品牌扫描失败继续显式保留，不通过改白名单或清理工作区掩盖。

## 检查边界

本次只做文档相对链接/锚点检查、历史条目保留检查及 `git diff --check`，实际结果记录在当前 verification。没有重跑构建、完整测试、Godot或导出；用户确认与前次自动执行结果分开记录。
