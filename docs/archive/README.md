# 历史文档归档

> 本目录保存阶段报告、制作记录、未排期提案和原文快照，**不是当前能力、待办、验收或实施授权**。
> 当前工作从 [交接](<../handoff.md>) → [模块地图](<../module-map.md>) → [验证记录](<../verification.md>) 开始；专题见 [文档导航](<../README.md>)。

## 工程报告与检查点

| 记录 | 用途与限制 |
| --- | --- |
| [2026-08-13 判定审计](<reports/2026-08-13-judgement-implementation-audit.md>) | 旧 EX 窗口、时间锁、宽限判断已被后续社区规格覆盖，不能照表再次修复 |
| [2026-08-24 编辑器重建](<checkpoints/2026-08-24-editor-rebuild.md>) | 旧 Avalonia 移除与首版 Godot canvas-first shell 的阶段状态 |
| [2026-09-09 审查与修复](<reports/2026-09-09-code-review-fixes.md>) | 缺陷原始描述、后续修复与当时验证；Required Fixes 不能单独当当前待办 |
| [2026-10-14 工作区盘点](<checkpoints/2026-10-14-workspace-inventory.md>) | 当时的 Git、APK 哈希、源码归属与本地磁盘/验收快照；83 项/源码链接/下一步不是当前基线 |
| [2026-10-25 第二轮模块化](<reports/2026-10-25-modularization-phase2.md>) | 对象边界、草稿资源保留修正、8 组无窗口对比；editor 源码链接已被第三轮移除 |
| [2026-10-25 第三轮模块化](<reports/2026-10-25-modularization-phase3.md>) | 公共视觉、页面/画布模型和 headless/DLL 发布证据；卡片/采样/暂停的剩余边界又被后续续拆更新 |
| [2026-10-25 client 续拆](<reports/2026-10-25-client-followup.md>) | 从整理前 handoff/module-map 提取的采样、卡片、详情请求、暂停与 1980 帧记录；未重新执行 |
| [未注明日期的自动审查原始 JSON](<reports/undated-code-review.json>) | 原根目录 result.json，42 选中项/20 发现的原始报告，按字节搬迁；未逐条建立当前缺陷/修复映射，不能直接当修复清单 |

历史报告中的裸代码路径通常以 community 根目录解释，旧命令/预算/测试计数属于原日期；归档不是恢复旧架构的理由。
早期三个短跳转入口保留用于兼容旧链接；本次六篇长文在活跃文档中的 Markdown 引用已迁移到新位置，不回写不可变历史快照。

## 已完成美术制作记录

- [2026-10-13 X/Ψ 扩展](<art/2026-10-13-grade-xy-art.md>)、[2026-10-13 D 扩展](<art/2026-10-13-grade-d-art.md>)：造型、分层渲染、交付路径和当时的哈希/预览检查。
- 用户在 2026-10-14 已认可接入客户端的新三档；正文中的“待验收”按制作时点解释。当前八级重建/打包命令集中在 [Blender 管线](<../blender-pipeline.md>)，不要求重新制作资产。
- `design/` 被忽略，源工程/渲染序列必须单独备份；本次只搬文档，不移动或删除图片、Blender 工程和生成脚本。

## 未排期提案

[Storyboard 与编辑器联动提案](<proposals/storyboard-editor-proposal.md>)：首版不做，未来 sidecar 方向和禁用入口决策保留；数据合同未冻结、实施顺序未排期。
当前状态与 writer 清理未知 sidecar 的风险仍写在交接；不得把归档里的阶段建议当作现在的下一步。

## 整理前原文快照

| 批次 | 说明与清单 |
| --- | --- |
| 2026-10-07 | [说明](<2026-10-07-cleanup/README.md>) / [manifest](<2026-10-07-cleanup/manifest.json>)：旧交接、美术比较、编辑器旧能力宣告和历史构建流水 |
| 2026-10-26 | [说明](<2026-10-26-cleanup/README.md>) / [manifest](<2026-10-26-cleanup/manifest.json>) / [迁移清单](<2026-10-26-cleanup/relocations.json>)：本次整理前 31 份文档字节快照及 7 项迁移的哈希 |

原文快照**不回写**，其链接以清单中的 originalPath 解释；不保证在归档位置直接可点。新迁移的可读报告只添加历史标识并重定向 Markdown 链接，原字节可由快照复核。

## 没有归档的有效依据

[玩法](<../gameplay-spec.md>)、[冻结 v2](<../chart-format-v2.md>)、[原版证据与 UNKNOWN](<../original-judgement-analysis.md>)、[视频几何](<../video-geometry-analysis.md>)、[像素标定](<../pixel-calibration.md>)、[发布安全](<../releasing.md>)、[UI 施工](<../editor-ui-design.md>) 与现行视觉管线仍有效，不因日期旧而失效。
