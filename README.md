# Dynamite Universe

Dynamix 风格的 **clean-room 社区音游重实现**，包含 Godot 游戏和独立 Windows 制谱器。公开包不得包含原版素材/谱面；内部官方测试数据仅限受控开发/Internal。

> 无项目级 LICENSE，原创代码与资产保留所有权利。公开可见不等于获得复制/修改/再分发授权；第三方见 [通知](<THIRD_PARTY_NOTICES.md>)。

## 接手从这里开始

1. [当前交接](<docs/handoff.md>)：当前能力、硬约束、缺口、待决策和设备验收边界。
2. [模块地图](<docs/module-map.md>)：项目依赖、源码入口、职责与剩余耦合。
3. [验证状态与操作](<docs/verification.md>)：本轮实际检查、历史构建/测试/产物/用户反馈分开记录。
4. [专题索引](<docs/README.md>)：玩法/v2 合同、设计施工、发布安全；[归档](<docs/archive/README.md>) 只用于历史追溯。

## 当前状态（2026-11-09 整理）

- **游戏**：主菜单 → 选曲 → 游玩 → 结算闭环；三轨输入、Hold/Mixer、设置/校准、本地成绩、搜索/试听、Mirror/Auto、结算八评级与配色 v2 已接入；[HUD v8](docs/gameplay-hud-v8.md) 已接真实血量/统计/暂停并获用户观感确认。
- **制谱器**：独立 Avalonia + Godot Windows 工作台，可创建/打开 strict v2 目录包、直接编辑 Note 与路径、编辑 BPM/Scroll、undo/redo、SAVE/SAVE AS。真实波形、ZIP/XML/DY 导出和 Storyboard 未完成；[详细能力](<docs/dyna-maker-uv.md>) 是编辑器状态入口。
- **缺口与限制**：BLEED 已有真实扣血/回血与耗尽结算，专用耗尽结算待扩展，血条 UI 已接入；筛选排序占位，Hit/UI 音效不完整，服务器未接入，模式成绩未完整隔离。预加载已按当前模式的 Preset 检查复用，用户已确认模式切换基本通过；这与成绩模式隔离是两个问题。
- **工程**：已分离纯规则、编辑核心、公共 Godot 视觉与两端适配；仍有大类，不宣称所有职责已解耦。当前实现以工作区源码为准，接手先运行 `git status --short --branch`；历史提交或旧 APK 不代表全部当前功能。
- **验证**：构建、自动回归、真实窗口操作及未覆盖范围统一见 [验证记录](<docs/verification.md>)；最近一次完整验证仍有品牌扫描误扫本地 worktree 的失败，不能宣称全绿；本轮用户确认与未测设备/性能边界分别记录。

## 技术与目录

Godot **4.7.1 Mono** / C# / `net9.0`。游玩固定 1920×1080、`canvas_items` 等比黑边，不采用自适应游玩布局。

| 目录 | 职责 |
| --- | --- |
| [client](<client/>) | 游戏 Godot 场景、UI、输入/音频/游玩适配 |
| [editor](<editor/>) | 独立 Windows DynaMaker UV shell，与游戏互不启动，不进入 Android APK |
| [presentation](<presentation/>) | 公共 Godot 字体、基础 UI、音符外观和舞台，不读取应用会话 |
| [shared](<shared/>) | Godot-free 格式、精确时间、判定、计分和安全包读写 |
| [chart-editor-core](<tools/chart-editor-core/>) | 文档、命令、会话、画布投影和草稿模型 |
| [core-tests](<tools/core-tests/>) / [chart-editor-tests](<tools/chart-editor-tests/>) | 断言式核心与编辑器回归 |
| [tools](<tools/>) | 验证、结构门禁、发布、美术及研究工具，不进入 APK |
| [release](<release/>) / [schemas](<schemas/chart-format-v2/>) | 内容/签名/身份策略、资产来源和 v2 机器合同 |
| [design](<design/>) | 忽略的本地美术源与预览，不随克隆取得，需单独备份 |
| [docs](<docs/README.md>) | 当前说明、有效规格与历史归档 |

本仓库无服务端；外层 `server/` 只是 C++/Boost 起步骨架，不代表正式技术选型。游戏“谱面工坊”仍禁用，预留未来服务器谱面库，不是本地编辑器入口。

## 构建与验证

从 community 根目录执行，以下是复验命令，不是本轮全部通过的声明：

```powershell
python -B tools/verify.py
# 依赖已还原时可加 --no-restore
```

统一入口依次运行结构门禁、客户端/编辑器构建、两套 C# 回归、发布测试与 diff 检查；分项命令和 Godot 启动/截图流程统一见 [验证状态与操作](<docs/verification.md>)。
普通修改后不代替用户启动游戏目测；确需运行时使用 Mono 版并先结束残留 Godot。

## 发布边界

玩家谱面在 `user://charts/`，设置和成绩在 Godot `user://`。
整个 `client/testdata/` 仅用于开发/Internal；Public APK 必须从 clean worktree 导出并通过最终验包，不能直接从带内部数据和导入缓存的开发目录分发。
研究材料、原版 APK、参考内容、设计源工程、工具环境和签名材料都不进入发行内容。详见 [发布流程](<docs/releasing.md>)。
