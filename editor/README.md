# DynaMaker UV

独立的 **Godot 4.7.1 Mono/.NET、C# / net9.0** Windows 桌面制谱器，不是游戏主菜单的“谱面工坊”，
也不进入 Android APK。

**当前实现范围统一以 [DynaMaker UV 当前说明](<../docs/dyna-maker-uv.md>) 为准。**
该说明基于 **2026-10-07 静态代码核对**，不是新的构建/测试通过记录；
旧实现与历史验证见[历史归档索引](<../docs/archive/README.md>)。

## 构建与启动

从 **community 根目录**执行以下 PowerShell 示例。必须使用 Mono/.NET 版 Godot；启动前先结束旧 Godot。
本次文档整理没有执行这些命令。

```powershell
dotnet build editor/DynaMakerUv.Editor.csproj

# 无匹配进程时 taskkill 会提示未找到
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe

& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path editor
```

在 Start 页通过 **OPEN PACKAGE 大卡 → 选择 strict v2 包目录 → 路径行的 OPEN** 打开本地包，
或用 NEW PROJECT 创建内存草稿。页面为 Start / Pack / Create / Edit / Settings；
Settings 由 Start 页右下的 `SETTINGS` 文字键或 Edit 右键菜单的 `SETTINGS` 行进入（BACK 各回其处），
行与控件按设计稿 `settings_board()` 自绘，真接点是 MOTION、DEFAULT GRID、SNAP、GRID OPACITY、BACKGROUND DIM，
值写在 `user://editor-settings.json`；EFFECTS / AUTO SAVE / AUDIO 三组按占位惯例 55% 压暗且不响应。
Pack 页按设计稿重建（`editor/scripts/PackPageOverlay.cs`）：左列当前包 + `IMPORT PACKAGE` 换包，
右侧真实封面 / NO COVER 占位、难度 chips 与 DIGEST 盾标，差分行点选后由 `OPEN IN EDITOR` 进 Edit，
底部状态行只放真实数据（难度 + 等级 + 主判定数，没有时长探测）。
Create 页同样按设计稿落地（`editor/scripts/CreatePageOverlay.cs`）：AUDIO / METADATA / TIMING / FIRST CHART /
COVER / MEMORY DRAFT 六块面板 + 底部操作条；BPM、音频偏移与可选封面已接进 draft 请求（默认 150 / 0 / 无封面），
等级是 1..99 的十进制块编码控件（步进钳制、非法值红框拒绝、unrated 不出块）；中缝是实时前置校验，
不过时禁用 `CREATE PACKAGE` 并写出原因。
**保存已接通**：右键 SAVE 或 `Ctrl+S` 保存到当前包目录（未落盘草稿先选目标目录并首次创建），
右键 SAVE AS 或 `Ctrl+Shift+S` 另存到新目录；**ZIP/XML/DY 导出仍未接入，
Preview 仅为计时视觉预览，没有音频播放**。
不要将现有菜单占位或共享写包底层能力当作已完成的功能。

## 许可与发布

仅可发布获授权的 clean-room 内容；不得加入原版或参考项目借用的素材、谱面和研究测试数据。
参考来源、固定 commit、MIT 许可与功能边界见[当前说明](<../docs/dyna-maker-uv.md#6-参考来源许可与发布边界>)；
第三方归属见[第三方通知](<../THIRD_PARTY_NOTICES.md>)，APK 门禁见[发布规范](<../docs/releasing.md>)。
