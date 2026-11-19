# Dynamite Universe

Dynamix 风格的 **clean-room 社区音游重实现**。游戏可离线游玩；独立 Windows 制谱器 DynaMaker UV 用于制作谱面，联网账号、曲库与成绩服务由仓内的独立后端提供。公开包不得包含原版素材/谱面；内部测试数据仅限受控开发/Internal。

> 无项目级 LICENSE，原创代码与资产保留所有权利。公开可见不等于获得复制/修改/再分发授权；第三方见 [通知](<THIRD_PARTY_NOTICES.md>)。

## 项目网页与制谱入门

- 项目首页：[中文](site/index.html) / [English](site/en/index.html)。DynaMaker UV 制谱教程：[中文](site/guide.html) / [English](site/en/guide.html)。两种语言都可以跟着同一段练习节拍，从放第一颗音符做到保存和重新打开谱包。
- 本地预览：`python -m http.server 8765 --bind 127.0.0.1 --directory site`，然后打开 `http://127.0.0.1:8765/`。
- [网页维护与 GitHub Pages 部署](docs/website.md)。部署工作流只发布 `site/`，不将内部文档、开发数据或本地测试软件包公开；配置完成前不视为已上线。

## 接手从这里开始

1. [当前交接](<docs/handoff.md>)：当前能力、硬约束、缺口、待决策和设备验收边界。
2. [模块地图](<docs/module-map.md>)：项目依赖、源码入口、职责与剩余耦合。
3. [验证状态与操作](<docs/verification.md>)：本轮实际检查、历史构建/测试/产物/用户反馈分开记录。
4. [专题索引](<docs/README.md>)：玩法/v2 合同、设计施工、发布安全；[归档](<docs/archive/README.md>) 只用于历史追溯。

## 当前状态

| 模块 | 已可使用 | 边界 |
| --- | --- | --- |
| [游戏](docs/handoff.md#21-游戏) | 首页 → 选曲/试听 → 三轨游玩 → 结算；支持 Hold/Mixer、校准、Mirror/Auto、BLEED 血条。Standard 与 Hardcore 分开保存本机最佳成绩。 | 选曲筛选/排序仍是占位，Hit/UI 与结果音效不完整；BLEED 失败仍复用普通结算页。低端性能未验收，专项暂缓。 |
| [Windows 制谱器](docs/dyna-maker-uv.md) | Avalonia 工作台嵌入 Godot 舞台；可新建、打开、保存 strict v2 目录谱包，编辑 Note、路径及 BPM/Scroll，并播放预览。 | `windows-x64-test-v1` 是已确认的[测试包基线](docs/verification.md#当前-windows-编辑器测试包基线)，不是公开发行批准。ZIP/XML/DY 未接入；真实波形、Storyboard 不列当前目标。 |
| [网络服务](server/README.md) | 游戏可注册/登录、从谱面商店首次下载安装、上传符合条件的本局成绩；管理员可发布/下架谱包。离线游玩不要求账号。 | 属于开发阶段，生产部署未验收；暂无更新/删除已安装谱包、排行榜、段位或多人联机。 |
| [项目网站](docs/website.md) | 中英文首页和制谱教程共用工作台截图与练习音频。 | Pages 工作流只发布网页；本地检查不证明网站已经上线，也不代表游戏或制谱器已公开发行。 |

[交接 §4](docs/handoff.md#4-仍需处理) 区分真实缺口、暂缓项和未覆盖的设备场景，不是排期。构建、真实窗口操作、用户反馈与产物身份分别查[验证记录](docs/verification.md)。

## 技术与目录

游戏与编辑器原生舞台使用 Godot **4.7.1 Mono** / C# / .NET 9；Windows 工作台外壳和 ASP.NET Core 服务端使用 .NET 10。游玩固定 1920×1080、`canvas_items` 等比黑边，不采用自适应游玩布局。

| 目录 | 职责 |
| --- | --- |
| [client](<client/>) | 游戏 Godot 场景、UI、输入/音频/游玩适配 |
| [editor](<editor/>) | 独立 Windows DynaMaker UV shell，与游戏互不启动，不进入 Android APK |
| [presentation](<presentation/>) | 公共 Godot 字体、基础 UI、音符外观和舞台，不读取应用会话 |
| [shared](<shared/>) | Godot-free 格式、精确时间、判定、计分和安全包读写 |
| [chart-editor-core](<tools/chart-editor-core/>) | 文档、命令、会话、画布投影和草稿模型 |
| [server](server/) | 独立账号、曲库和成绩 API；[接口合同](server/API.md) |
| [site](site/) | 可独立部署的静态首页和制谱教程，无游戏运行时依赖 |
| [core-tests](tools/core-tests/) / [chart-editor-tests](tools/chart-editor-tests/) / [server-tests](tools/server-tests/) | 规则、编辑器和网络回归；真实数据库另跑专项 |
| [tools](<tools/>) | 验证、结构门禁、发布、美术及研究工具，不进入 APK |
| [release](<release/>) / [schemas](<schemas/chart-format-v2/>) | 内容/签名/身份策略、资产来源和 v2 机器合同 |
| [design](<design/>) | 忽略的本地美术源与预览，不随克隆取得，需单独备份 |
| [docs](<docs/README.md>) | 当前说明、有效规格与历史归档 |

## 构建与验证

从 community 根目录执行，以下是复验命令，不是本轮全部通过的声明：

```powershell
python -B tools/verify.py
# 依赖已还原时可加 --no-restore
```

[统一脚本](tools/verify.py)运行结构检查、游戏及制谱器构建、核心/编辑器/网络回归、Shell 无窗口回归、发布工具测试与差异检查。真实 PostgreSQL、Godot 窗口与导出验包不在默认流程内；分项操作见[验证说明](docs/verification.md#非图形复验命令)。
普通修改后不代替用户启动游戏目测；确需运行时使用 Mono 版并先结束残留 Godot。

## 发布边界

手工导入的谱面放在 `user://charts/`；商店谱面由安装器独立登记，不要求玩家手动搬运。本机设置和成绩在 Godot `user://`。
整个 `client/testdata/` 仅用于开发/Internal；Public APK 必须从 clean worktree 导出并通过最终验包，不能直接从带内部数据和导入缓存的开发目录分发。
研究材料、原版 APK、参考内容、设计源工程、工具环境和签名材料都不进入发行内容。详见 [发布流程](<docs/releasing.md>)。
