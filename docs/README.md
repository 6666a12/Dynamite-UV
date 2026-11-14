# 文档导航与维护约定

> 按“接手入口 → 当前实现/操作 → 任务规格 → 历史/提案”阅读，不按文件日期把所有文档当作待办。入口与能力说明不复制测试数量、Git HEAD 或源码行数；动态状态以工作区、结构报告和验证记录为准。

## 接手最短路径

1. [项目入口](<../README.md>) → [当前交接](<handoff.md>)：产品范围、硬约束、功能与验收边界、真实缺口。
2. [模块地图](<module-map.md>)：当前依赖、文件职责、新功能落点和剩余耦合。
3. [验证状态与操作](<verification.md>)：实际执行、历史通过、测试包、用户反馈与运行命令。
4. 按下表进入专题。只有追溯决策/证据时才读 [历史归档](<archive/README.md>)。

## 当前说明与操作

| 文档 | 用途 / 单一维护职责 |
| --- | --- |
| [handoff.md](<handoff.md>) | 当前实现摘要、约束、未完成/待决策/暂缓/静态风险，不累加阶段流水 |
| [module-map.md](<module-map.md>) | 项目关系、源码职责、对象边界与拆分入口；不是行为合同 |
| [runtime-architecture.md](<runtime-architecture.md>) | 运行时会话/加载、不可重排的帧顺序、适配边界 |
| [verification.md](<verification.md>) | 最近实际检查、历史验证、测试包身份、用户验收和复验命令 |
| [OCR 报告逐项核实](ocr-review-verification.md) | 2026-11-13 对 result.json 全部218条的源码核对、探针证据及后续用户授权的四组修复记录；不是全部条目的修复授权 |
| [dyna-maker-uv.md](<dyna-maker-uv.md>) | 制谱器 Avalonia + Godot 主线、文件/命令接线、核心依赖与持久化边界；[editor README](<../editor/README.md>) 提供启动入口 |
| [制谱器 UI 回归清单](<editor-ui-regression.md>) | UI 分层覆盖、真实窗口交互与未覆盖项；具体执行结果仍以 verification 为准 |
| [releasing.md](<releasing.md>) | Public/Internal 隔离、clean worktree、签名和最终 APK 门禁 |
| [Windows 编辑器打包](../tools/editor-release/README.md) | 当前固定测试profile、精简运行时/模板、便携卸载与构建操作；包身份统一见verification |

## 有效规格、安全与测量（按任务必读）

| 文档 | 权威边界 |
| --- | --- |
| [网络服务第一阶段设计](<network-services-phase1.md>) | 已接通账号、免费曲库与本人记录后端开发闭环；[服务端运行/API](../server/README.md)，客户端尚未接通，生产部署未验收 |
| [gameplay-hud-v8.md](<gameplay-hud-v8.md>) | 已接本体的 BLEED 血条、顶部统计/暂停、无框标题卡、视觉判定线与三档动效边界 |
| [gameplay-spec.md](<gameplay-spec.md>) | 社区玩法、Legacy 兼容、空间/判定/计分；原版研究不能覆盖社区决策 |
| [chart-format-v2.md](<chart-format-v2.md>) | 已冻结的 v2 合同，不因 UI 能力不足而降格；[Schema/golden pack](<../schemas/chart-format-v2/>) 是机器合同 |
| [mixer-receiver.md](<mixer-receiver.md>) | Mixer 放置/保持/宽限、时间重放和专项设备边界 |
| [pixel-calibration.md](<pixel-calibration.md>) / [video-geometry-analysis.md](<video-geometry-analysis.md>) | 当前坐标与测量依据；改布局前必读，不因日期旧而归档 |
| [original-judgement-analysis.md](<original-judgement-analysis.md>) | 原版证据、UNKNOWN 和有意差异，不是社区产品实现清单 |
| [editor-ui-design.md](<editor-ui-design.md>) | Penpot 板位、设计/已接入边界、UI 施工与 MCP 安全约定；不是功能验收表 |
| [ui-motion.md](<ui-motion.md>) | 游戏现行动效与两端共用命中特效；旧编辑器目标已单独归档 |
| [result-v2-design.md](<result-v2-design.md>) | 已接入结算的层级、时序和交互；旧设计示例数据不能覆盖现评级门槛 |
| [result-outcomes.md](<result-outcomes.md>) | 已接入的正常结束双锁存、BLEED 失败、七类结果字标前奏（含 TRACK COMPLETE、三档/跳过/防穿透）与资产清单 |
| [note-colors-v2.md](<note-colors-v2.md>) | 配色草稿/应用/比较、可读性限制与未完成的设备验收清单 |
| [blender-pipeline.md](<blender-pipeline.md>) | 批准的八评级资产重建/打包、安全与设计源备份 |
| [第三方通知](<../THIRD_PARTY_NOTICES.md>) / [APK policy](<../release/apk-policy.json>) / [资产来源](<../release/asset-provenance.json>) | 许可证、产品身份、发布内容和资产哈希；不能以文档整理放宽 |

## 只在追溯时阅读

- [2026-10-26 模块化复审](archive/reports/2026-10-26-modularity-review.md)：当日建议，多个对象已被后续拆分取代。
- [2026-11-07 制谱器审查](archive/reports/2026-11-07-editor-debug-review.md)：一次性核对/修复，不是新待办。
- [2026-11-09 文档整理](archive/reports/2026-11-09-documentation-cleanup.md)：本次归档范围与验证边界。

- [历史归档总索引](<archive/README.md>)：按时间/类型查阶段报告、旧盘点、已完成美术交付和未排期提案。
- [2026-10-26 整理说明与原文清单](<archive/2026-10-26-cleanup/README.md>)：本次移动清单、整理前字节快照与 SHA-256。
- [Storyboard 历史提案](<archive/proposals/storyboard-editor-proposal.md>)：首版不做、未来 sidecar 方向；未冻结/未排期，不是当前开发计划。
- [网页 UI 参考](<ui-mock/>)、本地 `design/` 是设计材料，不证明游戏/编辑器能力或设备效果；本次不搬动图片、网页、脚本或 Blender 工程。
- 旧审计/重建入口中的跳转页只为兼容已有链接保留，不再列为接手必读。

## 维护约定

1. **现状、目标、历史分开。** 明确“已接入/待验收”“底层支持但 UI 未接”“静态疑点未复现”“用户暂缓”“未排期”。
2. 当前交接只维护摘要与真实缺口；详细 editor 能力放制谱器说明，结构放模块地图，验证执行与产物身份放 verification，不在多个 README 重复流水。
3. 写执行日期、范围、配置和未覆盖项；更新某页不能把历史构建/用户反馈改写成当前工作区验证。旧警告不自动等于当前警告。
4. 完成的阶段记录、旧盘点、已交付美术过程及暂不实施的长提案移到带类型/日期的 archive；在现行文档留必要结论和链接，不保留互相矛盾的“当前状态”。
5. 冻结格式、测量依据、原版未闭环证据、clean-room、签名和施工安全规则不因日期旧而归档失效。归档不授权恢复旧架构、再次实施修复或改玩法。
6. 移动时修相对链接/锚点，必要时保留兼容跳转；不可变原文快照不重写。不要因检验失败而放宽发布测试白名单。
7. 忽略的设计源/本地证据不随克隆取得；缺失时明确前提，不能把本机工具配置视为下一台机器已配置。
