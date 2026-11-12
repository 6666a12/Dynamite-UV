# DynaMaker UV

Windows 制谱器采用两个协作进程：`shell/` 是 .NET 10 / Avalonia 工作台，当前 Godot 4.7.1 Mono/.NET 9 项目提供共用游戏舞台与音频。Shell 通过 Win32 HWND 嵌入 Godot，并用 JSON-lines IPC 传递 Chart、预览控制和输入事件。

不进入 Android APK；不引用 client 项目或链接 client 源码。UI 依赖使用公开 NuGet 包，不依赖 `.tmp` 研究源码。

> 2026-11-09 用户确认最近这批编辑交互、预览、右键面板、跨屏框选及 Dock/恢复整体满意，可收尾；后续继续完善制谱器。此确认不替代全部设备/DPI/长谱性能验证，范围见 [验证记录](../docs/verification.md#用户反馈与验收边界)。

## 当前可用

- strict v2 本地目录包新建/打开、保存、另存为；添加/切换/复制/删除/重排差分、dirty 状态和失败提示。
- 包与差分设置可编辑元数据、音频偏移、试听区间和音频/封面替换；整组设置只产生一个撤销步骤。
- Godot 内 Note 点击/Shift 多选，SELECT 空白处左键框选当前轨道的位置/时间范围（按住可随音乐跨屏，已判定音符仍可选；Shift 追加、右键/Esc 取消）；支持拖动移动、边缘调宽、Hold/Mixer 整体移动和多选整组拖动；PAN 调整预览时间与横向视图。
- Inspector 支持单选 Note 的精确时间、位置、宽度、轨道和类型编辑；多选可批量调速、步进和删除。
- TIMING 面板支持 BPM/Scroll 事件增删改、精确 BarTime、Scroll 曲线与撤销。
- 七类 Note 的真实 hover ghost、目标 BarTime/位置/宽度、跨轨导引和同位置/同宽提示；点击放置、拖动调宽、宽度记忆，Z 自由时间、X 位置精调；Hold/Mixer 两段式尾点创建。
- Hold/Mixer 路径双击插点、拖动头/节点、出向曲线及 Hold 节点判定编辑。
- 播放/暂停、时间轴 seek、真实 Note 刻度；WAV/MP3/Ogg Vorbis/FLAC 音频预览。同差分编辑走带 revision 的增量更新，保留播放、播放头和选择，音频路径不变时不重读音频。
- `Q / E` 调整玩家预览流速（0.1–2.0，与游戏落速倍率一致），`W / S` 调整音乐倍率（0.2–2.0）。两者不写谱面。
- 播放 Auto 预览复用游戏音符、残影、命中及 Hold/Mixer 持续视觉；seek/编辑静默重建，暂停冻结。空白舞台右键面板提供放置面、Note 类型、SELECT/PATH、预览音量与特效档位，不写游戏设置或谱面。
- 中英文即时切换、Dock 浮动/还原、Suki 动效档位；`F1` 查看完整快捷键。

已接设置与布局持久化、基础复制粘贴（Ctrl+C/V，完整路径，播放头网格锚点，一次撤销）；用户数据独立保存在 `%LOCALAPPDATA%/DynaMakerUV/`，不写安装目录。多选精确编辑已接入；对象磁吸已隐藏并停用，算法完善暂缓。ZIP/XML/DY 谱面导入导出尚未接入。真实波形不作为当前目标，BPM 导入保留候选；首个稳定版本前不做插件 API。

## 当前测试包

用户已确认 `windows-x64-test-v1` 为现有测试包基线。完整便携包由 [打包工具](../tools/editor-release/README.md) 合并Shell、Godot舞台和离线运行时生成；Godot export preset单独仍只负责舞台。当前ZIP身份/hash与验证记录见 [测试基线](../docs/verification.md#当前-windows-编辑器测试包基线)，不在多处复制易过期的包名。

完整解压后运行 `DynaMakerUV.exe`；卸载前关闭编辑器，再运行 `Uninstall.cmd` 并确认。卸载保留额外谱包、修改文件和用户偏好，不是安装器或系统应用注册。请把个人谱包放在独立目录。

基线已包含首次保存目录规范化、后台保存快照版本、文本框保存快捷键与资源保存归属修复。资源替换跨保存的历史媒体恢复、全部GPU/DPI/干净机器覆盖仍有边界；许可证/签名等公开发行检查尚未完成。

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
