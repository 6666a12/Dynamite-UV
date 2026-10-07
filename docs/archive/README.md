# 历史文档归档

> 本目录保存阶段记录与原文快照，**不是当前实现说明或待办清单**。
> 当前工作从 [交接文档](<../handoff.md>)、[制谱器说明](<../dyna-maker-uv.md>) 和 [文档导航](<../README.md>) 开始。

## 带日期的报告与检查点

| 原文 | 归档 | 用途 |
| --- | --- | --- |
| 判定实现对照审计 | [2026-08-13 判定审计](<reports/2026-08-13-judgement-implementation-audit.md>) | 追溯当时的输入、Hold 与渲染判断；其中 EX 窗口、时间锁、宽限等表述已被后续规格覆盖 |
| 编辑器重建检查点 | [2026-08-24 Godot 重建](<checkpoints/2026-08-24-editor-rebuild.md>) | 旧 Avalonia 移除及第一版 canvas-first shell 的阶段状态 |
| Code Review Fix Handoff | [2026-09-09 审查与修复](<reports/2026-09-09-code-review-fixes.md>) | 缺陷原始描述、后续修复经过与当时验证结果；不是要求再次实施已修复项 |

原入口保留简短跳转，便于旧链接继续使用。归档正文的代码路径通常以 community 根目录为基准，旧命令不作为现有使用说明。

## 2026-10-07 文档清理快照

[本批次说明](<2026-10-07-cleanup/README.md>) 与 [校验清单](<2026-10-07-cleanup/manifest.json>) 保存了清理前的原始内容及 SHA-256。
其中包括旧交接的历史流水、早期评级 v4/v5 比较过程、旧编辑器能力宣告与历史构建结果。

快照按字节保存，**正文不回写**；快照内的相对链接仍按原文件位置解释，不保证在归档位置直接可点击。
需要找到对应现有文档时查看 manifest 的 `originalPath`。

## 当前仍有效、没有归档的内容

- [玩法规格](<../gameplay-spec.md>)、[v2 格式](<../chart-format-v2.md>)、[原版判定证据](<../original-judgement-analysis.md>)。
- [视频几何依据](<../video-geometry-analysis.md>)、[像素标定](<../pixel-calibration.md>)。
- [发布安全流程](<../releasing.md>) 和 [设计施工约定](<../editor-ui-design.md>)。
- [Storyboard 提案](<../storyboard-editor-proposal.md>) 保持未实现提案状态，不混入当前能力。

本次不搬动网页参考、Blender 工程、图片或脚本，以免破坏资源路径与历史设计重建。
