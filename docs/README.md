# 文档导航与维护约定

> 状态核对：2026-10-07。当前状态来自源码静态核对，不表示本次重新构建、运行测试或完成实机验收。

## 推荐阅读顺序

1. [项目 README](<../README.md>)：项目边界、目录与启动入口。
2. [当前交接](<handoff.md>)：有效实现、固定决策、待办及验证流程。
3. 按任务阅读下面的专题文档；需要追溯旧决策时才进入 [历史归档](<archive/README.md>)。

## 当前状态与操作

| 文档 | 职责 |
| --- | --- |
| [handoff.md](<handoff.md>) | 当前交接与待办；不再作为流水式更新日志 |
| [dyna-maker-uv.md](<dyna-maker-uv.md>) | 现有 Godot 制谱器的能力、缺口、启动方式与资产边界 |
| [runtime-architecture.md](<runtime-architecture.md>) | 分层、会话、加载、gameplay 帧顺序及发布不变量 |
| [releasing.md](<releasing.md>) | Public/Internal APK 隔离、导出与最终产物强制检查 |
| [blender-pipeline.md](<blender-pipeline.md>) | 当前采用的评级资产与重建/运行时打包管线 |

## 有效规格与证据

| 文档 | 性质与权威边界 |
| --- | --- |
| [gameplay-spec.md](<gameplay-spec.md>) | 当前判定/舞台与 legacy 兼容说明；已知 EX-Tap 合同偏差单独标注 |
| [chart-format-v2.md](<chart-format-v2.md>) | 冻结的正式 v2 合同，不因文档清理而改变格式 |
| [v2 Schema 与 golden pack](<../schemas/chart-format-v2/>) | 机器可读合同和 clean-room 向量 |
| [original-judgement-analysis.md](<original-judgement-analysis.md>) | 原版机制研究证据；不是社区产品待办或实现宣告 |
| [video-geometry-analysis.md](<video-geometry-analysis.md>)、[pixel-calibration.md](<pixel-calibration.md>) | 当前布局的测量依据；不能作为过期日志删除 |
| [ui-motion.md](<ui-motion.md>) | 当前游戏动效规格、网页参考边界与编辑器目标动效 |
| [editor-ui-design.md](<editor-ui-design.md>) | Penpot 板位、当前/参考稿标记、设计决策与施工安全约定 |
| [result-v2-design.md](<result-v2-design.md>) | 已接入结算 v2 的几何、时间线和待验收边界 |

## 提案与历史参考

- [Storyboard 提案](<storyboard-editor-proposal.md>)：未实现、未冻结的未来方向，不是现有编辑器工作区。
- [网页 UI 参考目录](<ui-mock/>)：历史视觉/交互稿；不能据其判断当前客户端或编辑器能力。
- [历史归档](<archive/README.md>)：旧阶段审计、编辑器重建检查点和整理前原文快照。
- [开发样本统计](<../tools/chart_stats_report.md>)：研究记录，不是 Type 语义或公开发行曲库。

## 维护规则

- **现状、目标、历史分开写。** 用“已接入/待验收”“底层已有但 UI 未接入”“设计目标/未实现”表达边界。
- 当前交接只列有效约束、现有能力和真实剩余工作；完成后的修复经过移入带日期的归档。
- 构建/测试结果必须附日期、范围与条件；不能把历史通过记录改写成最新验证。
- 归档正文允许保留当时的错误判断或过期命令，但必须有历史标识；不能再列作当前待办。
- 正式格式、测量依据、clean-room/签名安全约束和仍有效的施工规则不因“旧日期”而归档失效。
- 网页/设计资产保留原路径以免破坏脚本；过时版本在文档索引中明确标为历史参考。
