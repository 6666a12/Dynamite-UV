# DynaMaker UV

独立的 **Godot 4.7.1 Mono/.NET、C# / net9.0** Windows 桌面制谱器，不是游戏主菜单的“谱面工坊”，
也不进入 Android APK。直接依赖 [shared](<../shared/>)、[presentation](<../presentation/>) 与
[ChartEditor.Core](<../tools/chart-editor-core/>)，不引用 client 项目或链接 client 源码。

**详细能力、使用说明、UI 决策与已知边界只在 [DynaMaker UV 当前说明](<../docs/dyna-maker-uv.md>)维护。**
该说明已按 **2026-10-26 工作区静态源码核对**更新；本页能力核对没有构建、C# 回归、启动 Godot 或实机验证。
验证记录与复验流程见[验证状态与操作](<../docs/verification.md>)，阶段原文见[归档索引](<../docs/archive/README.md>)，不作为当前工作区通过声明。

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

## 打开与能力速览

- Start 页 **OPEN PACKAGE 大卡 → 选择 strict v2 包目录 → 路径行 OPEN/回车**；也可粘贴/拖入目录后确认。不要用旧工作台参数。
- **NEW PROJECT** 创建内存草稿；Pack 页选差分后由 **OPEN IN EDITOR** 进入 Edit。
- 右键 **SAVE** / `Ctrl+S` 已接保存（草稿先选目标目录）；**SAVE AS** / `Ctrl+Shift+S` 另存。新建/编辑不会自动写盘。
- 当前有 Tap/Drag/两阶段 Hold/Select、BPM 添加/选择、既有路径节点编辑与撤销/重做；完整类型/属性工具及 ZIP/XML/DY 导出未完成。
- Settings 的七项真接点为 MOTION、UI SCALE、DEFAULT GRID、SNAP、GRID OPACITY、BACKGROUND DIM、SCROLL INERTIA；EFFECTS / AUTO SAVE / AUDIO 仍占位。
- **Preview 只有计时与舞台视觉，没有真实音乐/击音、波形或音频 mixer；Storyboard 未实现。** 保存音频格式、失败边界、快捷键差异与完整 UI 细节均见[当前说明](<../docs/dyna-maker-uv.md>)，不要由菜单或底层支持推定可用。

## 许可与发布

仅可发布获授权的 clean-room 内容；不得加入原版或参考项目借用的素材、谱面和研究测试数据。
参考来源、固定 commit、MIT 许可与功能边界见[当前说明](<../docs/dyna-maker-uv.md#6-参考来源许可与发布边界>)；
第三方归属见[第三方通知](<../THIRD_PARTY_NOTICES.md>)，APK 门禁见[发布规范](<../docs/releasing.md>)。
