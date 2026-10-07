# Dynamite Universe

Dynamix 风格的 clean-room 社区音游重实现。公开发布包不使用原版素材或谱面；内部官方测试数据只能在受控开发环境使用，不得公开分发。

> 本仓库没有项目级 LICENSE，原创代码与资产保留所有权利；公开可见不等于获得复制、修改或再分发授权。第三方内容见 [THIRD_PARTY_NOTICES.md](<THIRD_PARTY_NOTICES.md>)。

## 当前状态

> 2026-10-07 按源码静态核对；不是本次构建/测试通过或实机验收声明。

- **游戏**：主菜单 → 选曲 → 游玩 → 结算闭环可用；设置、本地成绩、三轨输入、Hold/Mixer 等主体判定已接入。
- **UI**：选曲 v2、搜索/试听、Standard/Hardcore、Mirror/Auto、三 tab 设置、首页 3D 能量核心已实现。结算 v2 已接入，实机观感待用户验收；首页新分区启灯仍是设计探索。
- **制谱器**：独立 Godot 桌面项目；能打开本地 strict v2 目录包，创建内存草稿，画布编辑与 undo/redo 已接入。**UI 保存/导出、真实音频试听/波形和 Storyboard 尚未完成**。
- **谱面/成绩**：支持 legacy 与 strict v2；分数归一化为 1,000,000。v2 使用四元成绩身份，legacy-direct 保留历史兼容键；Auto 不写成绩。
- **明确缺口**：BLEED 目前只有开关/锁定/持久化，没有真实玩法机制；筛选排序占位，Hit/UI 声音链路不完整，服务器未接客户端。

接手先读 [当前交接](<docs/handoff.md>)；专题与权威边界见 [文档导航](<docs/README.md>)。旧阶段完成记录和历史验证已移入 [历史归档](<docs/archive/README.md>)。

## 技术与目录

Godot 4.7.1 **Mono** / C#，目标 `net9.0`；固定 1920×1080 设计坐标，`canvas_items` 等比保留黑边，不使用自适应游玩布局。

| 目录 | 职责 |
| --- | --- |
| [client](<client/>) | 游戏 Godot 项目，轻量场景入口与 C# UI/游玩适配 |
| [editor](<editor/>) | 独立 DynaMaker UV Godot Windows 制谱器 |
| [shared](<shared/>) | 不依赖 Godot 的格式、加载、精确时间、判定、分数与安全包写入 |
| [core-tests](<tools/core-tests/>) | shared 行为回归 |
| [chart-editor-core](<tools/chart-editor-core/>) / [chart-editor-tests](<tools/chart-editor-tests/>) | 可编辑文档、命令、快照与格式/写包回归 |
| [tools](<tools/>) | 开发辅助、研究与美术生成脚本，不进入 APK |
| [release](<release/>) | APK 身份、内容政策和资产来源清单 |
| [schemas/chart-format-v2](<schemas/chart-format-v2/>) | v2 Schema、clean-room golden pack 与 Digest 向量 |
| [design](<design/>) | Git 忽略的本地美术源文件与预览；不随克隆取得，也不整体打入游戏 |
| [docs](<docs/README.md>) | 当前说明、有效规格、设计提案和历史归档 |

游戏和制谱器各有自己的项目、场景、程序集和导出 preset。游戏“谱面工坊”仍禁用，预留给服务器谱面库，不是本地编辑器入口。本仓库尚无服务器工程；工作区外层只有 C++ 起步骨架，不代表已确定正式服务器架构。

## 构建、启动和验证

从 community 根目录执行（以下是复验命令，不是本轮运行结果）：

```powershell
dotnet build client/DynamiteUniverse.csproj
dotnet build editor/DynaMakerUv.Editor.csproj
dotnet run --project tools/core-tests/CoreTests.csproj
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
python -B -m unittest discover -s tools/release/tests -p "test_*.py"
```

默认核心测试只使用版本化的 clean-room fixture。官方开发语料需显式启用，不能代替公开发布门禁：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj -- --dev-testdata client/testdata/packs
```

仅在需要运行/视觉对比时启动；标准 Godot 版本不支持 C#，必须用 Mono，并先结束残留进程：

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe

# 游戏与制谱器择一启动
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path client
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path editor
```

制谱器从 UI 的 BROWSE/OPEN PACKAGE 选择目录，不使用旧工作台启动参数。普通修改后不代替用户启动游戏目测，截图/录屏流程见 [交接文档](<docs/handoff.md>)。

## 主要文档

| 文档 | 用途 |
| --- | --- |
| [handoff.md](<docs/handoff.md>) | 当前实现、固定决策、真实待办与验证流程 |
| [dyna-maker-uv.md](<docs/dyna-maker-uv.md>) | 制谱器已接入/未接入能力与操作 |
| [gameplay-spec.md](<docs/gameplay-spec.md>) / [runtime-architecture.md](<docs/runtime-architecture.md>) | 社区玩法、几何与运行时不变量 |
| [chart-format-v2.md](<docs/chart-format-v2.md>) | 正式冻结的 v2 合同；不能把 shell 的能力缺口当作格式缺口 |
| [editor-ui-design.md](<docs/editor-ui-design.md>) / [ui-motion.md](<docs/ui-motion.md>) | 当前设计板、目标交互、施工约定与动效 |
| [result-v2-design.md](<docs/result-v2-design.md>) / [blender-pipeline.md](<docs/blender-pipeline.md>) | 已接入结算 v2 与当前评级资产管线 |
| [releasing.md](<docs/releasing.md>) | Public/Internal 分离、clean worktree 导出、签名与 APK 强制检查 |
| [全部文档](<docs/README.md>) / [历史归档](<docs/archive/README.md>) | 导航、证据/提案边界与历史记录 |

## 发布边界

玩家包从 `user://charts/` 扫描，设置和成绩写入 Godot `user://`。被忽略的 [client/testdata](<client/testdata/>) 只能供开发/Internal 使用；Public APK 必须从 clean worktree 导出并通过最终验包。
研究输出、原版参考内容、设计源工程、工具环境及签名材料均不能随游戏发布。完整流程见 [发布规范](<docs/releasing.md>)。
