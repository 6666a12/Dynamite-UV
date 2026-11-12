# DynaMaker UV 当前说明

> 2026-11-12：Avalonia + Godot 为正式 editor 主线，用户已确认当前Windows精简包配置作为测试基线（包身份统一见verification）。本文描述当前进程边界、已接线的 UI/文件/预览/制谱能力及未完成范围；旧 Penpot 页面与旧 Godot 外壳只作历史参考。剩余功能与专项边界见 §6，具体确认范围见 [验证记录](verification.md#用户反馈与验收边界)。

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

## 1.1 Windows 测试基线与运行时

- 用户入口是包根目录的原生 `DynaMakerUV.exe`，不依赖任何 .NET 才能进入运行时探测。它检查 x64 .NET 10 兼容启动能力：系统安装可用时运行普通 `shell/`；当前测试基线不可用时启动 `shell-bundled/` 的裁剪自包含Shell。旧完整运行时方案的 `runtime/` 仅用于未启用精简profile的构建。
- 探测是一次独立的 `--runtime-check` 子进程，不把应用崩溃误判成运行时缺失；真正的 Shell 退出码不会自动在第二套运行时重试。`--bundled-runtime` 可用于诊断/复验，`--launcher-check` 只检查选择链路。
- 启动器设置 `DOTNET_MULTILEVEL_LOOKUP=0` 并固定到选中的运行时，避免系统其他架构或全局目录偷偷改变结果。运行来源写入 `%LOCALAPPDATA%/DynaMakerUV/logs/launcher.log`。
- Shell 发布目录包含 `shell/`，Godot 导出舞台包含 `stage/DynaMakerStage.exe`、`.pck` 及数据目录；包根还带 `distribution.json`、`files.sha256.json`、许可证/第三方通知和 `README.txt`。必须整体解压，不能只复制入口 EXE。
- 4.7.1 Godot Windows .NET 导出模板自身按自包含方式携带舞台运行时。隔离探针证明仅移除其运行时并设置 `DOTNET_ROOT` 无法启动；因此当前不宣称舞台也遵循系统优先，首候选包是“Shell 系统优先/包内兜底 + Godot 舞台自包含”。
- 构建脚本在独立空项目挂载最终舞台PCK，检查两种音符shader及共享字体能实际加载；这些由C#字符串动态引用的资源必须显式列入导出preset，源码中存在不等于已经打包。该检查不替代GPU画面验收。
- 候选包不含自动更新、不含签名，`distributionStatus` 明确为本地候选；NuGet 依赖中仍有若干包只有 SPDX/license 元数据而没有随包 notice 文件，完成依赖归属复核并补齐后才能公开分发。
- 2026-11-12 用户确认当前带卸载入口的包作为现有测试基线，暂时固定 `--profile windows-x64-test-v1`（[配置](../tools/editor-release/test-profile.json)、[构建说明](../tools/editor-release/README.md)）；包名/hash统一见[当前测试基线](verification.md#当前-windows-编辑器测试包基线)。参数漂移必须显式复核；不是免除许可证/干净机器等发布检查。便携包附Uninstall.cmd，确认后仅删除清单内hash未变的程序文件，保留谱包/额外文件/修改文件和用户偏好，不创建系统安装项，遵守PowerShell执行策略。
- 实验精简参数：`tools/editor-release/build.py --trimmed-fallback` 保留普通 `shell/` 供系统.NET使用，另发布 `shell-bundled/` 自包含裁剪应用作离线兜底；它不是可供任意应用使用的通用.NET安装。`--trimmed-stage` 保留GodotSharp/项目程序集和原生托管入口，仅裁剪可达运行时依赖。两项均非默认，裁剪警告和实际覆盖以verification为准；不得用一次启动成功替代保存、布局、音频和原生舞台验证。

## 2. 已接线的文档与 UI 能力

- `Ctrl+N` 打开新建包表单，填写包信息、音频/可选封面、初始 BPM、偏移和首个差分；草稿在首次保存时导入外部资源。FILE → ADD DIFFICULTY 添加空差分，工具栏 ComboBox 切换当前差分。
- FILE → PACKAGE / DIFFICULTY SETTINGS 编辑包与当前差分元数据、Chart ID/文件、音频偏移、包/差分试听区间及音频/封面替换；差分也可复制、删除和上移/下移。整组设置由 `EditorPackageSettings` 组成一个可撤销命令，待保存资源替换立即用于预览音频。
- FILE 或 `Ctrl+O` 打开 strict v2 **目录包**；SAVE / `Ctrl+S` 保存，`Ctrl+Shift+S` 另存为。当前文档有未保存修改时，新建或打开另一个包会被阻止并提示先保存，不会静默丢弃。
- 标题、曲师、难度、Note 数量和 dirty 标记读取真实 `EditorDocument`；时间轴刻度来自当前 Chart 的 Note/节点时间，不是假波形。
- TIMING Dock 列出当前差分的 BPM/Scroll 事件，支持精确 BarTime 的新增、修改、删除与撤销；Scroll 可编辑出向曲线，尾事件自动省略 `curveToNext`，首个事件与 bar 0 基准约束继续由核心命令校验。
- Godot Note 点击和 Shift 多选同步 Inspector；路径节点选择保留父 Note 与节点两级身份。多选可批量应用差速、按一个吸附步长或 0.1 位置步进移动，并作为一次组合命令删除。按下已选 Note 不再塌缩选区，SELECT 拖动会按相同精确 BarTime/中心增量移动整个选区，并作为一个组合命令撤销。单选可通过 Inspector 精确编辑 BarTime、中心、宽度、轨道和类型；几何与类型/轨道分别作为一次可撤销命令提交。
- SELECT 空白处左键拖动框选**当前放置面/轨道的谱面位置 × 时间范围**（先用侧栏或 ↑ 切轨）。按下固定起始位置与 BarTime，按住时终点随鼠标和播放时钟更新；鼠标不动也可随音乐跨屏选中。候选来自完整差分的该轨父 Note，按头部原始时间/中心判断，不依赖可见 NoteView；已经命中回收或离屏仍保留在范围内，Hold/Mixer 整颗选中，节点不单独加入。显示随谱面投影的选框与数量，可见对象高亮；松开一次性同步 Shell。Shift 追加，空框/空白单击清空，右键/Esc 取消保留原选择，切谱/切工具取消；向后 seek 会收缩范围而不是累加扫过对象。框选时间反查走全局 Scroll/玩家流速（不按每颗 Note 的差速重定义范围）；Note 差速只影响视觉，不影响谱面时间归属。保留当前 Shift 追加语义，不照搬参考版宽度交叉模式。
- SELECT 支持拖动普通 Note、整体移动 Hold/Mixer、拖拽边缘调宽和拖动幽灵；PAN 支持纵向调整预览时间与横向视图，播放中 Pan 会临时挂起音频并在松开后从新位置继续。所有手势松开时才提交一次文档命令。
- 放置工具提供七类 Note、三轨和宽度记忆：hover 即显示复用 `NoteView` 的真实 ghost、目标 BarTime/位置/宽度、当前轨与跨轨导引线及同位置/同宽对齐标记；左键按下后拖动调宽，普通 Note 松开提交。Hold/Mixer 第一段调宽，松开后移动尾点，第二次点击以一个文档命令提交；右键取消草稿。Z 暂时关闭时间吸附，X 使用 0.01 位置精调。
- 路径工具双击身体按 shared 路径采样插入节点，拖动头或节点执行精确时间/中心命令；SELECT 单独拖动真实路径节点也使用节点命令，Shift 保留父 Note 多选语义。曲线细分点只参与连接体渲染，不显示为编辑手柄、不参与命中。节点按下立即同步选择，拖动有目标轮廓，Z 关闭节点时间吸附；Inspector 应用出向曲线和 Hold 非尾节点的判定状态，Delete 删除当前节点或全部选中 Note。
- `Ctrl+Z / Ctrl+Y` 调用文档 Undo/Redo。同差分普通编辑通过带 revision 的 `update` 快照更新舞台，保留播放状态、播放头和选择；切包、切差分或音频来源变化才完整 `load`。音频缓存独立于 Chart 身份：完整切谱仍停止播放，但相同音频路径复用已解码流，不重复读取解码。差分下拉框选中事件只切换文档/舞台，不在 Avalonia 提交选择期间清空自身条目；列表重建仅用于文档/差分列表刷新。
- `Ctrl+C` / `Ctrl+V` 或 EDIT 菜单复制/粘贴当前选中的完整父 Note（Hold/Mixer 包含全部路径）。首颗音符对齐当前播放头按时间网格吸附后的拍位置，其他对象保留精确相对拍时间、空间、轨道、差速、曲线和节点判定标记；生成新 ID/编辑身份，粘贴后选中新组，一次撤销移除整组。剪贴板是当前工作台会话的独立快照，可跨差分/包，不读取系统文本剪贴板；文本输入控件内 Ctrl+C/V 保留原生语义。不含剪切、镜像/指定轨粘贴、悬浮粘贴预览或批量变换。
- 多选时右侧 Inspector 切换为选区摘要、整体变换、统一属性和折叠对象列表，不新增 Dock。时间支持带正负号的精确小节偏移/移动选区起点，位置偏移平移完整 Hold/Mixer 路径；宽度可设值/增减，默认头部，可显式包含整个路径。换轨保留谱面坐标、不自动镜像；批量类型仅支持 Tap/Drag/ExTap。混合值显示占位，只提交实际改动字段；两区分别应用、一次撤销，非法值整组拒绝并保留输入。切换选区丢弃未应用草稿；对象列表只定位、不缩减选区。既有差速/步进控件保留在折叠区。此版用户反馈目前无异议；对象磁吸因精度反馈已隐藏并停用，暂缓完善。
- 中英文资源位于 `editor/shell/Localization/`，`IWorkbenchLocalizer` 与 `{local:Tr}` 提供运行时切换和英语缺键回退。

## 3. 预览与快捷键

- 2026-11-11 编辑态同步新版舞台：底线延长、去除旧中央Mixer装饰，保留无框标题/难度、顶部4px播放进度与两段蓝线，不显示判定统计或血条。顶部中央新增编辑器专用184×80播放/暂停按钮（中心960,72），没有谱面时禁用；播放状态来自现有Controller，游戏本体按钮不变。共用HUD组件位于 `presentation/game/hud/`。
- 游戏效果预览按用户决定撤下UI入口，默认关闭且不持久化；底层完整HUD/Auto模式和内部验证入口保留，供未来自动录制使用（自动录制尚未实现）。内部模式隐藏编辑选区/放置导引与编辑器大按钮、禁用舞台编辑手势；Shell文件/属性命令仍是原入口，不是锁定文档的只读模式。显示为Standard Auto满血预览，不模拟真实失误；seek重算统计，不写成绩、不改dirty、不新增Storyboard。
- Hold连接体只在谱面真实节点处显示横向端边，自动细分点只参与侧边与填充。阶跃曲线以同刻左极限+终点表达，避免256段近似；曲线公式/原始节点/判定/Digest不改变。性能数据和未覆盖设备见verification。

- Godot 使用 `V2RuntimeAdapter`、`V2BpmTimeline`、`V2ScrollMap`、`VisualScrollMath` 与 `GameplayStageRenderer`，保留 1920×1080、等比缩放、游戏 +40Y 偏移和逐 Note 差速。
- 播放使用与游戏共用的 `GameplayNoteRenderer`/特效池：普通命中转残影并消失、Mine Auto 避让无爆发、Hold 动态头/连续接触/尾判、Mixer 动态接收头/持续粒子/结束回收。中间 Hold/Mixer 计分点与社区游戏一样不额外生成命中爆发；不新增 Mixer 尾判。编辑器特效默认 Full，可在空白处右键菜单切换；Note 亮度为 130%，残影为 300ms，不读取或修改游戏设置；游戏 Full/Reduced/Off 继续保留。
- 普通 seek/PAN 和播放中编辑按当前秒数重建 Auto 状态，清掉旧瞬时特效，不补播历史命中；落在 Hold/Mixer 中途时恢复接触头。滚轮导航单独走 Auto 推进：向前越过音符触发命中/残影，向后静默恢复并允许再次命中；暂停滚轮不会启动音乐，瞬时视觉按真实帧时间衰减。普通播放暂停冻结当前 Auto 画面及 GPU 粒子，继续播放不重判；暂停后普通 seek/编辑回到静态编辑视图。重建不重启音频，音频生命周期仍由 `EditorPreviewController` 管理。长谱频繁重建性能尚未专项验证。
- 启动以 `--wid` 和 1×1 分辨率创建 Godot 原生窗口，关闭 boot splash；Godot 初始为带宿主 owner 的 `WS_POPUP`，`NativeStageHost` 先隐藏、清除 popup owner，再设 `WS_CHILD` 并挂接实际 Avalonia 父窗口，最后无激活显示。布局不请求前台，不设置永久置顶；不再调用 `Window.GrabFocus()` 对 child 执行窗口级置前，点击后的键盘焦点由 Godot 嵌入窗口消息处理及受前台条件限制的 Shell `SetFocus` 接管。任务栏恢复问题已获用户确认修复，证据与边界见 verification。
- 音频预览支持 WAV、MP3、Ogg Vorbis；FLAC 由 shared 托管解码为 16-bit PCM 后包装为 `AudioStreamWav`。
- 舞台空白处右键打开紧凑工具面板：左/中/右放置面、七种 Note、SELECT/PATH、音乐预览音量和 Off/Reduced/Full 命中特效；不含 PAN 或对象删除菜单。切面/类型走 Shell 现有工具入口并同步侧栏，切 Note 进入 PLACE；点击而非悬停改变选项，选择后菜单保留，外部点击/右键/Esc 关闭并消费关闭点击。有未完成手势时右键只取消，不弹菜单。菜单在 Godot 舞台内绘制并限制在视口内，中英文由 Shell 资源发送；音量/效果通过IPC反馈Shell并作为编辑器偏好持久化，不写谱面或游戏设置。
- `Space` 播放/暂停；`R` 回开头；`A / D` 后退/前进 1 秒，`Shift+A / D` 为 0.01 秒，不按时间网格吸附；`C / V` 调吸附密度，`Shift` 使分母增减一。舞台滚轮按当前全局 Scroll 与玩家落速换算约 100 设计像素的时间位移，手势进行中不滚动。`B` 切换滚轮方向（默认向下前进，翻转后向下后退，再按恢复），沿用参考 DynaMaker 键位；作为编辑器偏好持久化，不影响 A/D、PAN 或谱面数据。
- 位置模式默认「网格」：中心 0.1、宽度 0.05，按住 X 临时使用 0.01；「自由」模式使用连续几何。放置、SELECT、PATH 节点与调宽共用规则，多选只吸附抓取锚点，纯时间移动保留原位置。Inspector 精确输入不量化，模式作为编辑器偏好持久化；允许中心及宽度越过推荐区 `[0,5]`。原有对齐标记使用 1e-6 相等容差。
- 对象磁吸（实验暂停）：用户反馈吸向坐标2时出现不稳定偏差，当前已移除工具栏开关和提示，Shell强制发送关闭状态，不恢复旧偏好的开启值；网格/自由位置模式与时间吸附不变。原生算法及回归代码保留，但不作为可用功能或已验收能力；中心/边缘候选竞争与屏幕坐标反算问题未修复，后续是否恢复另行决定。
- `Q / E` 降低/提高玩家预览流速，每次 0.1、范围 0.1–2.0，语义与游戏 `FallSpeedLevel / 10` 一致，不写谱面 Scroll。
- `W / S` 提高/降低音乐倍率，每次 0.1、范围 0.2–2.0，同时驱动音频 PitchScale 与预览时钟，不写谱面。
- `1/2/3` 分别进入 Tap/Drag/Hold 放置，`4` 进入 SELECT，`5/6/7` 分别进入 ExTap/Mixer/Mine 放置；`↑` 按中→左→右循环切轨；PATH/PAN 保留工具按钮，不分配数字键。`Ctrl+Shift+F / D` 浮动/还原舞台；`Ctrl+Shift+A` 添加面板；`F1 / Esc` 打开/关闭快捷键指南。Godot 原生焦点下的按键会转发给同一 Shell 命令入口。

## 4. 保留核心的不可变语义

[ChartEditor.Core](<../tools/chart-editor-core/>) 不依赖 Godot 或 Avalonia。文档、命令、会话由 UI 与回归共同调用。Shell 的 `ShellChartEditor` 负责把舞台、Inspector 与 Timing 请求转换为原子文档命令，窗口只协调显示、预览和文件生命周期。

- 放置 ghost 的头/尾草稿和读数保留原始 `ExactBarTime`，仅坐标投影与旧 IPC 数值边界转 double；支持 1/3、1/5 等网格，不用浮点二进制分数回构吸附读数。
- 所有修改通过文档命令进入权威模型；精确 BarTime 不经显示秒值往返。
- 十进制空间网格、位置/宽度步进、组移动提交与边缘调宽使用 `EditorSpatialMath`，避免交互计算制造 `0.6000000000000001` 一类尾差；半格仍向正方向舍入。它只负责交互算术，不统一截断谱面数值：自由模式不吸附、精确输入与未改字段保持原值，decimal 无法无损表达的范围回退 double。Note 倍率、偏移和试听区间在控件值未改时保留原始 double，BPM/Scroll 沿用既有保护；writer/Digest 不模糊化，也不批量清洗旧文件。
- 画布预览 `Center` 沿用左缘语义，提交文档命令时才转换为 v2 的 `center + width`。
- native 舞台的轨道转换、侧轨 `SideDistanceScale` 逆变换及 BPM/Scroll/玩家流速/逐 Note 差速反查统一由 `EditorStagePlacement` 拥有；`StagePlacementDraft` 使用精确 BarTime 和中心坐标维护放置/尾点草稿。`GameplayVisualMapper.BoundsAt` 是最终 Note 轴向与命中矩形的唯一入口，侧轨不再二次交换 X/Y。
- 出界编辑不再夹紧中心或把宽度限制到 5；交互加宽仍有最小宽度，精确属性接受所有有限正宽。属性面板提供全谱 Note/节点定位列表，屏幕外节点也可精确编辑；PAN 的横向空间移动不再限制 ±960，按住 Shift 可平移二维空间视图。
- `EditorSpatialAnalysis` 只给建议，不修改游戏规则或阻止保存：三级提示分别为推荐区越界、判定线处可能不可见、检查到不可触达范围。分析包含正常/镜像、普通触宽扩边与 Mine/Mixer 身体的严格范围；路径节点及有预算的中间采样不构成全路径安全证明。设备边缘、HUD 遮挡、时间和操作速度不在分析范围。
- SELECT 多选只对被抓父 Note 的时间锚点吸附一次，再按全组最早时间限制公共时间增量；空间增量不再受推荐边界限制；ghost 按各轨道与差速重新投影。路径命令维持头部空间编辑、全路径时间平移的既有语义。边缘调宽共用固定逻辑对边的吸附计算，避免松手时预览与提交不一致。
- 2026-11-07 `result.json` 的历史逐项核实、修复和数值表示边界见 [审查核对](<archive/reports/2026-11-07-editor-debug-review.md>)。
- 成功保存后才确认已保存版本；打开/创建失败不替换当前文档；保存失败保留内存文档与 dirty。Shell使用会话SaveAsync：UI线程采集快照、后台仅写不可变请求、UI线程采用结果；保存期间的新编辑保留dirty并提示再次保存，不将最新历史位置误认作磁盘版本。保存/目录选择期间阻止关闭窗口与重复文件操作；文本框内Ctrl+S/Ctrl+Shift+S仍进入全局保存。已成功写入且没有被新替换覆盖的pending资源映射解除，后续保存与预览使用谱包内文件。
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
- Shell 用户数据保存在 `%LOCALAPPDATA%/DynaMakerUV/`，`preferences.json` 与 `layout.json` 独立版本化，不依赖源码/安装目录可写、不恢复旧 Godot 设置。偏好保存语言、时间网格、位置模式、滚轮方向、当前放置宽度、预览音量/特效、Shell 动效；不保存播放状态、音乐倍率、选择或剪贴板，不改谱面 dirty。偏好500ms合并写入，关闭再刷盘；异常/不支持版本回退默认，写回前保留原文件 recovery 副本。
- 布局关闭时保存 Dock 树/比例/活动页/隐藏与自动隐藏工具/浮窗位置尺寸，启动只在控件挂接前恢复；沿用原面板实例，不序列化 HWND/控件/进程。浮窗限制到现有显示器工作区，缺屏回主屏；VIEW 恢复舞台和工具并重置基础比例。主窗口关闭采用 OwnerWindowOnly，在 Dock 销毁浮窗前保存；单独关闭浮动舞台仍只归位不退出。真实 HWND、多屏/DPI 和导出包仍需实测。

## 6. 尚未完成

- 多选Inspector已接入精确整体变换、宽度/轨道与非路径类型批量设置；用户此前无异议，当前整体包已成为测试基线，不将其整体重列为待验收。基础复制粘贴、范围框选、Shift追加及既有批量操作已接入。
- 偏好持久化、Dock布局恢复和基础复制粘贴已有自动回归及相应用户反馈；设备/DPI/完整手动Dock等专项不因整体基线确认而自动通过，具体证据见verification。
- 资源替换跨保存的历史媒体Undo/Redo版本恢复未完善；独立原生Auto对象测试退出清理异常尚未修复。首次保存尾分隔符、保存中编辑dirty、文本框保存快捷键和当前资源外部依赖问题已修复，不再列为未修任务。
- 对象磁吸按用户决定隐藏并强制停用，暂不修算法；旧ObjectSnap偏好字段兼容保留但不启用功能，正常保存偏好时写为false。原有自动回归不证明用户报告的精度问题已解决。破坏性类型转换、时间缩放/反转和图案镜像仍不做。
- 真实波形分析不列为当前目标，使用专门音频工具；BPM 导入保留后续候选，尚未实施。
- 首个稳定版本前不做正式插件 API；当前保留内部官方扩展空间，不为假设的插件需求冻结接口。
- 合并桌面候选包已包含 Avalonia Shell、Godot 舞台和离线运行时，运行策略见§1.1；正式公开发行所需许可证复核、干净构建基线及无.NET机器验证尚未完成。偏好/布局继续使用独立用户数据位置，不依赖安装目录可写，升级不得覆盖个人配置。
- 其他 DPI、完整手动 Dock 拖放、长谱/大音频性能和听感专项。

## 7. 构建与验证

侧栏自动隐藏预览使用 Inline 布局，为工具面板留出空间，不覆盖原生舞台。VIEW 恢复舞台及工具/时序/属性面板；附加面板关闭后可以重新添加。舞台“停靠”按钮与 Ctrl+Shift+D 只调用 Dock 工厂迁回原舞台，已停靠时幂等；不得手工摘除 StageView 或清空/重设整个 Dock.Layout，否则会触发全工作区生命周期销毁。分层交互清单和未覆盖项见 [UI 回归清单](<editor-ui-regression.md>)。

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

Shell Headless 回归覆盖真实临时包打开/新建/加差分、放置与路径事件、SELECT 移动/调宽事件、Inspector 精确几何与类型/轨道、BPM/Scroll 控件增删改及撤销、包与差分组合设置、复制/删除/重排、Hold 判定、多选批量命令、双差分写包重读、快捷键、i18n 和工作区生命周期；具体执行时的用例数量只在验证记录维护。smoke 另覆盖真实 Godot 窗口、音频、真实 placement/Hold 尾点 ghost、左右轨放置、左轨拖动/调宽、原生 SELECT 拖动/边缘调宽、PAN、Note 点击、路径手势、播放中增量编辑、倍率、保存、Dock 和截图；每次实际执行与未覆盖范围见[验证状态与操作](<verification.md>)。

## 8. 许可与发布边界

DynaMaker UV 不进入 Public 或 Internal Android APK。不得加入原版或参考项目的曲绘、字体、音效、谱面、图标等素材。交互参考 DynaMaker Modified 固定 commit，但不编译其 JS/HTML/CSS/JSON。桌面 UI 包依赖及合并导出通知要求见[第三方通知](<../THIRD_PARTY_NOTICES.md>)。
