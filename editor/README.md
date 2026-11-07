# DynaMaker UV

Windows 制谱器采用两个协作进程：`shell/` 是 .NET 10 / Avalonia 工作台，当前 Godot 4.7.1 Mono/.NET 9 项目提供共用游戏舞台与音频。Shell 通过 Win32 HWND 嵌入 Godot，并用 JSON-lines IPC 传递 Chart、预览控制和输入事件。

不进入 Android APK；不引用 client 项目或链接 client 源码。UI 依赖使用公开 NuGet 包，不依赖 `.tmp` 研究源码。

## 当前可用

- strict v2 本地目录包新建/打开、保存、另存为；添加/切换/复制/删除/重排差分、dirty 状态和失败提示。
- 包与差分设置可编辑元数据、音频偏移、试听区间和音频/封面替换；整组设置只产生一个撤销步骤。
- Godot 内 Note 点击/Shift 多选，SELECT 拖动移动、边缘调宽、Hold/Mixer 整体移动和多选整组拖动；PAN 调整预览时间与横向视图。
- Inspector 支持单选 Note 的精确时间、位置、宽度、轨道和类型编辑；多选可批量调速、步进和删除。
- TIMING 面板支持 BPM/Scroll 事件增删改、精确 BarTime、Scroll 曲线与撤销。
- 七类 Note 的真实 hover ghost、目标 BarTime/位置/宽度、跨轨导引和同位置/同宽提示；点击放置、拖动调宽、宽度记忆，Z 自由时间、X 位置精调；Hold/Mixer 两段式尾点创建。
- Hold/Mixer 路径双击插点、拖动头/节点、出向曲线及 Hold 节点判定编辑。
- 播放/暂停、时间轴 seek、真实 Note 刻度；WAV/MP3/Ogg Vorbis/FLAC 音频预览。同差分编辑走带 revision 的增量更新，保留播放、播放头和选择，音频路径不变时不重读音频。
- `Q / E` 调整玩家预览流速（0.1–2.0，与游戏落速倍率一致），`W / S` 调整音乐倍率（0.2–2.0）。两者不写谱面。
- 中英文即时切换、Dock 浮动/还原、Suki 动效档位；`F1` 查看完整快捷键。

尚未接真实波形分析、设置与布局持久化、正式插件接口、多选精确几何或 ZIP/XML/DY。现有 Godot export preset 只导出原生舞台，不是包含 Avalonia shell 的完整桌面包。

## 构建与启动

从 **community 根目录**执行：

```powershell
dotnet build editor/DynaMakerUv.Editor.csproj
dotnet build editor/shell/DynaMakerUv.Editor.Shell.csproj

# 无匹配进程时 taskkill 会提示未找到
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe

dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj --no-build
```

Shell 默认使用外层 `godot/` 的 Godot 4.7.1 Mono；可用环境变量 `DYNAMAKER_GODOT` 指定引擎可执行文件。直接用 Godot 启动 `editor` 仅用于原生舞台调试，不会显示 Avalonia 工作台。

## 验证

```powershell
# 纯 UI/真实文件/命令回归，不启动 Godot
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --headless

# 独立空草稿窗口：空舞台、首次 Note 放置和首次保存
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke-empty builds/editor-shell-empty-smoke

# 完整窗口：包/音频加载、Note 选择、快捷键、放置/路径、保存、Dock 与中英文截图
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke builds/editor-shell-smoke

# 核心回归与统一非图形门禁
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
python -B tools/verify.py --no-restore
```

详细语义见 [DynaMaker UV 当前说明](<../docs/dyna-maker-uv.md>)。仅可使用获授权的 clean-room 内容；参考项目和原版素材不得进入发行内容。
