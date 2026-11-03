# DynaMaker UV 当前说明

> 2026-11-03：Avalonia + Godot 已成为正式 editor 主线。本文描述当前进程边界、已接线的 UI/文件/预览/制谱能力及未完成范围；旧 Penpot 页面与旧 Godot 外壳只作历史参考。

## 1. 进程与项目

- [editor/shell](<../editor/shell/DynaMakerUv.Editor.Shell.csproj>)：.NET 10、Avalonia 12.1.4、Dock.Avalonia、Ursa、SukiUI Motion，负责窗口、Dock、菜单、文件对话框、i18n 和快捷键。
- [editor](<../editor/DynaMakerUv.Editor.csproj>)：现有 Godot 4.7.1 Mono/.NET 9 项目，负责共用游戏舞台、Note 命中选择、预览时钟与音频。`StandaloneEditorMain` 只装配 `EditorPreviewController`。
- Shell 使用 Win32 HWND 嵌入 Godot 窗口，通过 JSON-lines stdin/stdout IPC 发送严格 Chart JSON、播放/seek/倍率命令，并接收选择、时钟和舞台按键。
- Godot 项目引用 [shared](<../shared/>)、[presentation](<../presentation/>) 和 [ChartEditor.Core](<../tools/chart-editor-core/>)；Shell 引用 shared 与 ChartEditor.Core。两端都不引用 client，Shell 也不依赖 `.tmp` 中的 UI 源码。

```text
editor/shell ──► ChartEditor.Core ──► shared
     │  IPC / HWND
editor Godot ──► presentation ──────► shared
```

## 2. 已接线的文档与 UI 能力

- `Ctrl+N` 打开新建包表单，填写包信息、音频/可选封面、初始 BPM、偏移和首个差分；草稿在首次保存时导入外部资源。FILE → ADD DIFFICULTY 添加空差分，工具栏 ComboBox 切换当前差分。
- FILE 或 `Ctrl+O` 打开 strict v2 **目录包**；SAVE / `Ctrl+S` 保存，`Ctrl+Shift+S` 另存为。当前文档有未保存修改时，新建或打开另一个包会被阻止并提示先保存，不会静默丢弃。
- 标题、曲师、难度、Note 数量和 dirty 标记读取真实 `EditorDocument`；时间轴刻度来自当前 Chart 的 Note/节点时间，不是假波形。
- Godot Note 点击和 Shift 多选同步 Inspector；路径节点选择保留父 Note 与节点两级身份。
- 放置工具提供七类 Note、三轨和默认宽度：点击按当前 BPM/Scroll/玩家流速反查并吸附时间，拖动增加宽度；Hold/Mixer 默认一小节尾点。
- 路径工具双击身体按 shared 路径采样插入节点，拖动头或节点执行精确时间/中心命令；Inspector 应用出向曲线，Delete 删除当前节点或父 Note。
- Apply Speed 执行 `SetNoteScrollSpeedMultiplierCommand`；`Ctrl+Z / Ctrl+Y` 调用文档 Undo/Redo。修改后重新发送 Chart 快照并保留播放头。
- 普通 Note 拖动、既有 Note 调宽和 Pan 手势尚未接线；位置和类型字段仍只读/禁用。
- 中英文资源位于 `editor/shell/Localization/`，`IWorkbenchLocalizer` 与 `{local:Tr}` 提供运行时切换和英语缺键回退。

## 3. 预览与快捷键

- Godot 使用 `V2RuntimeAdapter`、`V2BpmTimeline`、`V2ScrollMap`、`VisualScrollMath` 与 `GameplayStageRenderer`，保留 1920×1080、等比缩放、游戏 +40Y 偏移和逐 Note 差速。
- 启动直接以 `--wid` 创建 Avalonia 子窗口，初始分辨率 1×1，关闭 boot splash；嵌入宿主并设置最终位置后才显示。原生选择事件与 Godot 鼠标按下同步键盘焦点，避免启动灰窗和 `--wid` 快捷键丢失。
- 音频预览支持 WAV、MP3、Ogg Vorbis；FLAC 由 shared 托管解码为 16-bit PCM 后包装为 `AudioStreamWav`。
- `Space` 播放/暂停；`R` 回开头；`A / D` 移动一小节，`Shift+A / D` 移动一个吸附步长；`C / V` 调吸附密度，`Shift` 精细调整。
- `Q / E` 降低/提高玩家预览流速，每次 0.1、范围 0.1–2.0，语义与游戏 `FallSpeedLevel / 10` 一致，不写谱面 Scroll。
- `W / S` 提高/降低音乐倍率，每次 0.1、范围 0.2–2.0，同时驱动音频 PitchScale 与预览时钟，不写谱面。
- `1–4` 切工具；`Ctrl+Shift+F / D` 浮动/还原舞台；`Ctrl+Shift+A` 添加面板；`F1 / Esc` 打开/关闭快捷键指南。Godot 原生焦点下的按键会转发给同一 Shell 命令入口。

## 4. 保留核心的不可变语义

[ChartEditor.Core](<../tools/chart-editor-core/>) 不依赖 Godot 或 Avalonia。文档、命令、会话由 UI 与回归共同调用。

- 所有修改通过文档命令进入权威模型；精确 BarTime 不经显示秒值往返。
- 画布预览 `Center` 沿用左缘语义，提交文档命令时才转换为 v2 的 `center + width`。
- 成功保存后才 `MarkSaved`；打开/创建失败不替换当前文档；保存失败保留内存文档与 dirty。
- 未落盘草稿新增差分仍保留外部音频/封面来源；首次保存发布外部资源。
- 仅内存预览的编码、解码和 RuntimeAdapter 显式使用 `requireMainJudgement:false`，使空草稿可以放置首个 Note；默认 strict 打开、编码、Digest 与写包规则不变，空草稿仍不能直接保存。
- Godot 根控制器补收全局左键松开，并先把 viewport 坐标转换到舞台 Control 局部坐标，保证嵌入窗口中的放置和路径拖动能够提交；文档命令保持幂等，不重复写入。单条 IPC 命令的数据、枚举、IO 或解码异常以 `error` 事件返回，不再直接终止舞台。
- `EditorGameplayDigest.Compute(document, audioSha256Hex)` 继续从当前选中差分计算 shared Digest。

核心支持不等于 UI 已完成。七类 Note、路径节点、Scroll、BPM、差分复制/重排、资源替换等命令虽已存在，当前工作台只接入了上文明确列出的操作。

## 5. 文件与格式边界

- 打开的是本地 strict v2 目录包；ZIP/XML/DY 导入导出没有实现。
- shared 继续拥有严格校验、Digest、安全写包和默认音频 Probe。Shell 不复制这些规则。
- 默认 Probe 支持 WAV/MP3/FLAC/Ogg Vorbis 结构与时长；FLAC 输入限 64 MiB，逐帧验证子帧边界和 CRC-8/CRC-16，WAV chunk 不得越过 RIFF 声明边界；Opus/M4A/AAC 明确拒绝。
- 同目录保存可能在磁盘发布成功后因清理失败而抛错，不能把异常一概解释为完全回滚。
- 新语言当前不做持久化；新设置模型不恢复旧 Godot `user://editor-settings.json`。

## 6. 尚未完成

- 普通 Note 拖动、既有 Note 调宽和 Pan 手势；Inspector 的位置与类型字段仍只读。
- 真实波形分析、布局与偏好持久化、正式插件 API。
- 包含 Avalonia Shell、Godot 舞台和全部运行时的合并桌面导出包。现有 Godot export preset 只覆盖原生舞台。
- 其他 DPI、完整手动 Dock 拖放、长谱/大音频性能和听感专项。

## 7. 构建与验证

从 community 根目录：

```powershell
dotnet build editor/DynaMakerUv.Editor.csproj
dotnet build editor/shell/DynaMakerUv.Editor.Shell.csproj
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --headless
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke-empty builds/editor-shell-empty-smoke
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke builds/editor-shell-smoke
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
python -B tools/verify.py --no-restore
```

25 项 Headless 覆盖真实临时包打开/新建/加差分、放置与路径事件、命令、撤销/重做、双差分写包重读、快捷键与 i18n。smoke 另覆盖真实 Godot 窗口、音频、Note 点击、原生放置/路径鼠标手势、倍率、保存、Dock 和截图；每次实际执行与未覆盖范围见[验证状态与操作](<verification.md>)。

## 8. 许可与发布边界

DynaMaker UV 不进入 Public 或 Internal Android APK。不得加入原版或参考项目的曲绘、字体、音效、谱面、图标等素材。交互参考 DynaMaker Modified 固定 commit，但不编译其 JS/HTML/CSS/JSON。桌面 UI 包依赖及合并导出通知要求见[第三方通知](<../THIRD_PARTY_NOTICES.md>)。
