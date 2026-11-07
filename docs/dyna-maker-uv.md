# DynaMaker UV 当前说明

> 2026-11-07：Avalonia + Godot 已成为正式 editor 主线。本文描述当前进程边界、已接线的 UI/文件/预览/制谱能力及未完成范围；旧 Penpot 页面与旧 Godot 外壳只作历史参考。

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
- FILE → PACKAGE / DIFFICULTY SETTINGS 编辑包与当前差分元数据、Chart ID/文件、音频偏移、包/差分试听区间及音频/封面替换；差分也可复制、删除和上移/下移。整组设置由 `EditorPackageSettings` 组成一个可撤销命令，待保存资源替换立即用于预览音频。
- FILE 或 `Ctrl+O` 打开 strict v2 **目录包**；SAVE / `Ctrl+S` 保存，`Ctrl+Shift+S` 另存为。当前文档有未保存修改时，新建或打开另一个包会被阻止并提示先保存，不会静默丢弃。
- 标题、曲师、难度、Note 数量和 dirty 标记读取真实 `EditorDocument`；时间轴刻度来自当前 Chart 的 Note/节点时间，不是假波形。
- TIMING Dock 列出当前差分的 BPM/Scroll 事件，支持精确 BarTime 的新增、修改、删除与撤销；Scroll 可编辑出向曲线，尾事件自动省略 `curveToNext`，首个事件与 bar 0 基准约束继续由核心命令校验。
- Godot Note 点击和 Shift 多选同步 Inspector；路径节点选择保留父 Note 与节点两级身份。多选可批量应用差速、按一个吸附步长或 0.1 位置步进移动，并作为一次组合命令删除。按下已选 Note 不再塌缩选区，SELECT 拖动会按相同精确 BarTime/中心增量移动整个选区，并作为一个组合命令撤销。单选可通过 Inspector 精确编辑 BarTime、中心、宽度、轨道和类型；几何与类型/轨道分别作为一次可撤销命令提交。
- SELECT 支持拖动普通 Note、整体移动 Hold/Mixer、拖拽边缘调宽和拖动幽灵；PAN 支持纵向调整预览时间与横向视图，播放中 Pan 会临时挂起音频并在松开后从新位置继续。所有手势松开时才提交一次文档命令。
- 放置工具提供七类 Note、三轨和宽度记忆：hover 即显示复用 `NoteView` 的真实 ghost、目标 BarTime/位置/宽度、当前轨与跨轨导引线及同位置/同宽对齐标记；左键按下后拖动调宽，普通 Note 松开提交。Hold/Mixer 第一段调宽，松开后移动尾点，第二次点击以一个文档命令提交；右键取消草稿。Z 暂时关闭时间吸附，X 使用 0.01 位置精调。
- 路径工具双击身体按 shared 路径采样插入节点，拖动头或节点执行精确时间/中心命令；SELECT 单独拖动真实路径节点也使用节点命令，Shift 保留父 Note 多选语义。曲线细分点只参与连接体渲染，不显示为编辑手柄、不参与命中。节点按下立即同步选择，拖动有目标轮廓，Z 关闭节点时间吸附；Inspector 应用出向曲线和 Hold 非尾节点的判定状态，Delete 删除当前节点或全部选中 Note。
- `Ctrl+Z / Ctrl+Y` 调用文档 Undo/Redo。同差分普通编辑通过带 revision 的 `update` 快照更新舞台，保留播放状态、播放头和选择；切包、切差分或音频来源变化才完整 `load`，相同音频路径不重读解码。
- 多选的几何/类型/轨道精确编辑仍保持禁用，避免把不同 Note 意外折叠到同一坐标；路径节点几何继续由 PATH 手势和节点命令编辑。
- 中英文资源位于 `editor/shell/Localization/`，`IWorkbenchLocalizer` 与 `{local:Tr}` 提供运行时切换和英语缺键回退。

## 3. 预览与快捷键

- Godot 使用 `V2RuntimeAdapter`、`V2BpmTimeline`、`V2ScrollMap`、`VisualScrollMath` 与 `GameplayStageRenderer`，保留 1920×1080、等比缩放、游戏 +40Y 偏移和逐 Note 差速。
- 播放使用与游戏共用的 `GameplayNoteRenderer`/特效池：普通命中转残影并消失、Mine Auto 避让无爆发、Hold 动态头/连续接触/尾判、Mixer 动态接收头/持续粒子/结束回收。中间 Hold/Mixer 计分点与社区游戏一样不额外生成命中爆发；不新增 Mixer 尾判。编辑器当前使用默认 Full、130% Note 亮度和 300ms 残影，不读取或修改游戏设置；游戏 Full/Reduced/Off 继续保留。
- seek/PAN 和播放中编辑按当前秒数重建 Auto 状态，清掉旧瞬时特效，不补播历史命中；落在 Hold/Mixer 中途时恢复接触头。暂停冻结当前 Auto 画面及 GPU 粒子，继续播放不重判；暂停后 seek/编辑回到静态编辑视图。重建不重启音频，音频生命周期仍由 `EditorPreviewController` 管理。长谱频繁重建性能尚未专项验证。
- 启动直接以 `--wid` 创建 Avalonia 子窗口，初始分辨率 1×1，关闭 boot splash；嵌入宿主并设置最终位置后才显示。原生选择事件与 Godot 鼠标按下同步键盘焦点，避免启动灰窗和 `--wid` 快捷键丢失。
- 音频预览支持 WAV、MP3、Ogg Vorbis；FLAC 由 shared 托管解码为 16-bit PCM 后包装为 `AudioStreamWav`。
- `Space` 播放/暂停；`R` 回开头；`A / D` 后退/前进 1 秒，`Shift+A / D` 为 0.01 秒，不按时间网格吸附；`C / V` 调吸附密度，`Shift` 使分母增减一。舞台滚轮按当前全局 Scroll 与玩家落速换算约 100 设计像素的时间位移，手势进行中不滚动。
- 位置模式默认「网格」：中心 0.1、宽度 0.05，按住 X 临时使用 0.01；「自由」模式使用连续几何。放置、SELECT、PATH 节点与调宽共用规则，多选只吸附抓取锚点，纯时间移动保留原位置。Inspector 精确输入不量化，模式仅本次工作台会话有效；允许中心及宽度越过推荐区 `[0,5]`。对象磁吸尚未实现，对齐标记使用 1e-6 相等容差。
- `Q / E` 降低/提高玩家预览流速，每次 0.1、范围 0.1–2.0，语义与游戏 `FallSpeedLevel / 10` 一致，不写谱面 Scroll。
- `W / S` 提高/降低音乐倍率，每次 0.1、范围 0.2–2.0，同时驱动音频 PitchScale 与预览时钟，不写谱面。
- `1/2/3` 分别进入 Tap/Drag/Hold 放置，`4` 进入 SELECT，`5/6/7` 分别进入 ExTap/Mixer/Mine 放置；`↑` 按中→左→右循环切轨；PATH/PAN 保留工具按钮，不分配数字键。`Ctrl+Shift+F / D` 浮动/还原舞台；`Ctrl+Shift+A` 添加面板；`F1 / Esc` 打开/关闭快捷键指南。Godot 原生焦点下的按键会转发给同一 Shell 命令入口。

## 4. 保留核心的不可变语义

[ChartEditor.Core](<../tools/chart-editor-core/>) 不依赖 Godot 或 Avalonia。文档、命令、会话由 UI 与回归共同调用。Shell 的 `ShellChartEditor` 负责把舞台、Inspector 与 Timing 请求转换为原子文档命令，窗口只协调显示、预览和文件生命周期。

- 放置 ghost 的头/尾草稿和读数保留原始 `ExactBarTime`，仅坐标投影与旧 IPC 数值边界转 double；支持 1/3、1/5 等网格，不用浮点二进制分数回构吸附读数。
- 所有修改通过文档命令进入权威模型；精确 BarTime 不经显示秒值往返。
- 画布预览 `Center` 沿用左缘语义，提交文档命令时才转换为 v2 的 `center + width`。
- native 舞台的轨道转换、侧轨 `SideDistanceScale` 逆变换及 BPM/Scroll/玩家流速/逐 Note 差速反查统一由 `EditorStagePlacement` 拥有；`StagePlacementDraft` 使用精确 BarTime 和中心坐标维护放置/尾点草稿。`GameplayVisualMapper.BoundsAt` 是最终 Note 轴向与命中矩形的唯一入口，侧轨不再二次交换 X/Y。
- 出界编辑不再夹紧中心或把宽度限制到 5；交互加宽仍有最小宽度，精确属性接受所有有限正宽。属性面板提供全谱 Note/节点定位列表，屏幕外节点也可精确编辑；PAN 的横向空间移动不再限制 ±960，按住 Shift 可平移二维空间视图。
- `EditorSpatialAnalysis` 只给建议，不修改游戏规则或阻止保存：三级提示分别为推荐区越界、判定线处可能不可见、检查到不可触达范围。分析包含正常/镜像、普通触宽扩边与 Mine/Mixer 身体的严格范围；路径节点及有预算的中间采样不构成全路径安全证明。设备边缘、HUD 遮挡、时间和操作速度不在分析范围。
- SELECT 多选只对被抓父 Note 的时间锚点吸附一次，再按全组最早时间限制公共时间增量；空间增量不再受推荐边界限制；ghost 按各轨道与差速重新投影。路径命令维持头部空间编辑、全路径时间平移的既有语义。边缘调宽共用固定逻辑对边的吸附计算，避免松手时预览与提交不一致。
- 本轮 `result.json` 的逐项核实、修复和数值表示边界见 [审查核对](<editor-debug-review-2026-11-07.md>)。
- 成功保存后才 `MarkSaved`；打开/创建失败不替换当前文档；保存失败保留内存文档与 dirty。
- 未落盘草稿新增差分仍保留外部音频/封面来源；首次保存合并草稿原资源与 pending 替换，后者优先。跨格式音频替换同步目的扩展名；取消差分音频覆盖优先于已浏览来源；未编辑试听字段时保留包预览缺省。
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

- 多选几何/类型/轨道编辑和更丰富的选区、复制粘贴操作尚未接线。
- 真实波形分析、布局与偏好持久化、正式插件 API。
- 包含 Avalonia Shell、Godot 舞台和全部运行时的合并桌面导出包。现有 Godot export preset 只覆盖原生舞台。
- 其他 DPI、完整手动 Dock 拖放、长谱/大音频性能和听感专项。

## 7. 构建与验证

侧栏自动隐藏预览使用 Inline 布局，为工具面板留出空间，不覆盖原生舞台。VIEW 恢复舞台及工具/时序/属性面板；附加面板关闭后可以重新添加。分层交互清单和未覆盖项见 [UI 回归清单](<editor-ui-regression-2026-11-07.md>)。

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

154 项 Headless（含新增工作区生命周期与鼠标/键盘输入回归）覆盖真实临时包打开/新建/加差分、放置与路径事件、SELECT 移动/调宽事件、Inspector 精确几何与类型/轨道、BPM/Scroll 控件增删改及撤销、包与差分组合设置、复制/删除/重排、Hold 判定、多选批量命令、双差分写包重读、快捷键与 i18n。smoke 另覆盖真实 Godot 窗口、音频、真实 placement/Hold 尾点 ghost、左右轨放置、左轨拖动/调宽、原生 SELECT 拖动/边缘调宽、PAN、Note 点击、路径手势、播放中增量编辑、倍率、保存、Dock 和截图；每次实际执行与未覆盖范围见[验证状态与操作](<verification.md>)。

## 8. 许可与发布边界

DynaMaker UV 不进入 Public 或 Internal Android APK。不得加入原版或参考项目的曲绘、字体、音效、谱面、图标等素材。交互参考 DynaMaker Modified 固定 commit，但不编译其 JS/HTML/CSS/JSON。桌面 UI 包依赖及合并导出通知要求见[第三方通知](<../THIRD_PARTY_NOTICES.md>)。
