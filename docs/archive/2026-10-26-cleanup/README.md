# 2026-10-26 文档整理记录

本次依据当前工作区源码/项目引用、现行文档与已有验证记录整理，**不是产品功能改动、重新出包或实机验收**。
产品 HEAD 核对为 `fc65e21`（2026-10-14），本地还有未提交的功能与拆分改动，不能只凭 HEAD 重建当前全部状态。
当前入口：[交接](<../../handoff.md>) → [模块地图](<../../module-map.md>) → [验证状态](<../../verification.md>) → [专题索引](<../../README.md>)。

## 组织调整

- 工作区根增加产品定位导航，避免把外层研究仓库/旧 C++ 提交误当现行音游架构。
- 产品 README、AGENTS、文档索引、交接改为分层入口；交接不再叠阶段流水，不重复所有 UI 几何/历史警告。
- 新 verification 统一保存本轮实际检查、历史自动验证、测试 APK 身份、用户反馈与复验/运行命令；保留其证据边界。
- 模块地图保留当前依赖/职责/剩余耦合，验证细节转至历史报告；编辑器说明按源码更新日期、七设置、保存、快捷键/占位和实际依赖。

## 搬迁清单

| 原位置（community 相对） | 新位置 | 理由 |
| --- | --- | --- |
| docs/modularization-phase2.md | [第二轮报告](<../reports/2026-10-25-modularization-phase2.md>) | 已完成拆分与当时证据；有被第三轮覆盖的状态 |
| docs/modularization-phase3.md | [第三轮报告](<../reports/2026-10-25-modularization-phase3.md>) | 阶段 headless/publish 记录，剩余边界已继续更新 |
| docs/workspace-inventory-2026-10-14.md | [旧盘点](<../checkpoints/2026-10-14-workspace-inventory.md>) | 旧 Git、磁盘、产物和提交建议不是当前基线 |
| docs/grade-xy-art.md | [X/Ψ 制作](<../art/2026-10-13-grade-xy-art.md>) | 已获认可的制作/渲染过程；重建参数保留在当前管线 |
| docs/grade-d-art.md | [D 制作](<../art/2026-10-13-grade-d-art.md>) | 同上；不移动实际美术资产 |
| docs/storyboard-editor-proposal.md | [Storyboard 提案](<../proposals/storyboard-editor-proposal.md>) | 首版不做、未来长提案；固定方向/风险保留在交接 |
| result.json | [未注明日期的审查原始报告](<../reports/undated-code-review.json>) | 非运行配置；原始 42 项/20 发现报告不直接等同于当前缺陷 |

新增 [client 续拆历史摘要](<../reports/2026-10-25-client-followup.md>) 从整理前 handoff/module-map 提取；没有补造测试结果。
早期三个历史短跳转保留。迁移文件的 SHA-256 与原位置见 [relocations.json](<relocations.json>)。

## 修正的关键漂移

- SAVE/SAVE AS 已接，和未接的 ZIP/XML/DY 导出分开；编辑器只通过项目引用使用 shared/presentation/编辑核心，不再链接 client 源码。
- 保存清理错误可能发生在磁盘发布后，不能把所有异常写成磁盘完全回滚。
- 模式身份无独立模式维度；不能把部分 legacy 摘要变化当完整隔离。另记录模式切换/旧预设缓存的**静态风险，未运行复现**。
- Internal `debuggable=true` 是 manifest 发布约束，不直接等同于 C# JIT/优化配置；保留当前 export-debug 流程及历史性能探针边界。
- 像素标定改回可重叠的中/侧轨投影；原版分析只校正社区池化/精确锁摘要，不改原版研究证据/地址。
- 八评级来源/验收、97.60% 应为 X 的示例与完整 X/Y/D 重建命令修正；不重算历史报告或改玩法门槛。
- 本机 Penpot 连接记为历史记录，本次未读取凭据、访问设计稿或重建资产。

## 原文字节与回溯

[manifest.json](<manifest.json>) 记录整理前 31 份文档的 `originalPath`、`snapshotPath`、字节数和 SHA-256。
`originals/` 下为原字节 `.snapshot`，**不是活跃 Markdown**，不回写；相对链接按原位置解释，不保证可点击。
本次迁移正文只加历史标识并重定向链接，原始 JSON 按字节移动；归档内容也可能保留当时已过时的说法，应先读现行入口。

验证执行记录统一见 [verification](<../../verification.md>)。本次不提交 Git、不暂存，不改游戏/编辑器/shared/发布策略或测试代码，不清理引擎、工具、内部语料、设计源与凭据。
