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

在 Start 页通过 **BROWSE → 选择 strict v2 包目录 → OPEN PACKAGE** 打开本地包，
或用 NEW PACKAGE 创建内存草稿。页面为 Start / Pack / Create / Edit；
**编辑修改当前无法通过 UI 保存或导出，Preview 仅为计时视觉预览，没有音频播放**。
不要将现有菜单占位或共享写包底层能力当作已完成的功能。

## 许可与发布

仅可发布获授权的 clean-room 内容；不得加入原版或参考项目借用的素材、谱面和研究测试数据。
参考来源、固定 commit、MIT 许可与功能边界见[当前说明](<../docs/dyna-maker-uv.md#6-参考来源许可与发布边界>)；
第三方归属见[第三方通知](<../THIRD_PARTY_NOTICES.md>)，APK 门禁见[发布规范](<../docs/releasing.md>)。
