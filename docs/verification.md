# 验证状态与操作

> 维护原则：**执行日期、对象、条件、结果、未覆盖范围**一起写。静态核对、自动检查、导出验包、用户验收是四种证据，不能合并为“全通过”。
> 当前功能与待办以 [交接](<handoff.md>) 为准；这里统一维护最近验证记录、测试包身份与操作命令，避免各 README 重复漂移。

## 原生制谱手势回归

先构建 editor，再用 Godot 4.7.1 Mono 执行：

```powershell
<Godot-Mono> --headless --path editor -- --verify-gestures
```

该入口直接驱动 `EditorStageAuthoring` 的 press/motion/release，创建真实 `NoteView`/连接体并检查精确读数；覆盖三轨、3/5/4/12 网格和 SELECT/PATH 的 Hold/Mixer 中间节点身份。它不是可见窗口截图测试，也不验证 Draw 回调的 GPU 像素或原生 HWND 输入投递。

Auto 预览对象回归：`<Godot-Mono> --headless --path editor -- --verify-auto`。覆盖实际共用渲染器/特效节点及舞台播放切换，不是截图或音频回归；同样遵守启动前结束残留 Godot 的约定。

## 最近验证记录

| 日期 / 来源 | 已有结果 | 不能证明什么 |
| --- | --- | --- |
| 2026-11-07 Dock 标题、即时翻译与 Auto 选择框 | 覆盖 Dock `DockSurfaceHeaderActiveBrush`/普通 header 资源，活动标题深底亮字；翻译 ComboBox 的已选标签直接绑定当前 item Content，移除 Motion/Inspector 的取消重选刷新，语言事件仅刷新 Inspector 文案，保留未应用选择。Auto 选择框读取渲染器当前本体位置/尺寸，排除残影/回收对象，Hold/Mixer 随动态头移动及消失；不清空文档选择。完整 `verify.py --no-restore` 通过：285 源文件、三端 0 warning/0 error、核心/编辑器、182 项 Shell headless 和 37 发布测试。Shell 验证中英文反复切换、选中标签、不产生选择事件、活动/非活动标题画刷；Godot `--verify-auto` 三轨通过，增加命中后框消失、Hold 变宽/移动/回收、Mixer 结束和暂停 seek 恢复框断言。日志 `builds/ui-three-fixes-verify-final.log`、`builds/ui-selection-native-final.log` | 未启动可见窗口，不宣称像素/最终观感验收。首轮 XAML 编译因对象 Content 路径改用 ReflectionBinding；默认输出曾被用户编辑器 PID 81768 锁定，先用独立目录，用户关闭后正常构建。原有取消重选刷新使新增无事件断言失败，移除并改旧测试为实际标签断言后通过。结构首轮因 renderer 新增查询超过 800 行失败，查询仅保留已有字典读取，选择可见性职责留在编辑器；未放宽预算或新增 partial。 |
| 2026-11-07 制谱器 Auto 播放预览 | 将现有游戏 `GameplayNoteRenderer`、`GameplayEffects`、命中残影/粒子和 Hold/Mixer 持续视觉迁移为 presentation 共用组件；编辑器新增 `EditorAutoTimeline`/`EditorAutoPreview`，以 shared `JudgePlan` 驱动 Auto 命中，普通 Note 命中后残影/特效/消失，Mine 不产生命中特效，Hold 动态头与连续效果/尾判，Mixer 接收头与持续效果/结束回收；seek、PAN、播放中编辑按秒重建且不补历史爆发，暂停冻结 CPU/GPU 动画，恢复不重判。游戏 `GameSettings` 的 Full/Reduced/Off 仍保留，编辑器默认 Full，不读取或修改游戏设置。`--verify-auto` 原生 Godot 无窗口回归覆盖三轨、七类 Note、逐 Note 差速、Hold/Mixer、seek、暂停、重建、镜像及 Full/Reduced/Off；`--verify-gestures` 既有 31 组继续通过；`python -B tools/verify.py --no-restore` 通过（285 源文件、三端 0 warning/0 error、核心/编辑器/154 项 Shell headless、37 发布测试）。日志：`builds/auto-native-all-types.log`、`builds/auto-verify-final2.log` | 未启动可见窗口，不宣称 GPU 像素、音频听感、真实窗口手势或用户视觉验收；编辑器频繁重建和长谱性能未专项验证。首次新增测试编译误用类型名，首次原生测试缺少 fixture 的显式 Hold 尾判标记，修正后通过；首轮统一 verify 最后因 EOF 空行失败，修正后完整重跑通过（失败记录保留 `auto-native.log`、`auto-verify.log`）。最后扩展七类 Note 测试后 editor 再构建 0 warning/0 error 并重跑原生测试通过；Godot import 无错误，既有手势日志 `builds/auto-gestures.log`。 |
| 2026-11-07 工作区侧栏与 UI 分层回归 | 自动隐藏改为 Inline，避免覆盖原生 Godot 舞台；VIEW 恢复工具/时序/属性及舞台，附加面板关闭后可重开；NativeStageHost 跟随祖先布局同步位置并缓存 HWND 几何。统一 verify 通过（280 源文件，日志 `builds/workspace-verify.log`）；最后补充宿主 detach 缓存清理与恢复状态提示后，Shell 再构建 0 warning/0 error、154 项 headless 通过，完整真实窗口 smoke 通过（`builds/workspace-headless-final.log`、`builds/workspace-desktop-retry.log`）。真实鼠标覆盖三面板各两轮收起/展开/固定及无布局重叠，Help、File/Edit、VIEW 恢复；原有音频/路径/PAN/三轨/两次浮动还原亦通过 | 早期新增用例错误按 DataContext 定位 Pin 按钮，改用 CommandParameter；指针投递增加进入事件及150ms等待。完整重跑曾遇 DXGI 空帧，保留 `builds/workspace-desktop-final.log`，之后重跑通过。截图工具读取仍无可视内容，不宣称目视验收。系统文件选择器、所有焦点组合、多屏/DPI和全控件穷举仍未覆盖，见 [UI 清单](<editor-ui-regression-2026-11-07.md>)。Auto 预览任务因子代理隔离限制未实现，本轮没有改变播放判定表现 |
| 2026-11-07 出界编辑与特殊 Note 快捷键 | 开放中心越界、宽度大于 5、组移动/节点/调宽及精确属性；增加对象定位列表、Shift+PAN 空间平移、推荐区/不可见/不可触达提示，正常与镜像均分析，路径中间仅有预算采样。5/6/7 对应 ExTap/Mixer/Mine。完整非图形 verify 通过（278 源文件、三端 0 warning/0 error、115 项 Shell、核心/编辑器与 37 发布测试）；Godot 无窗口手势 31 组通过，含三轨出界放置。日志 `builds/overscan-verify-final.log`、`builds/overscan-native.log` | 完整真实窗口 smoke 最终通过（`builds/overscan-desktop-stable.log`），含音频连续性、PAN、侧轨和两次浮动关闭还原。此前首轮旧测试用两次 D 定位 bar2，已修测试；第二轮 Dock 截图遇 DXGI 空帧；第三轮 held-PAN 未观察到挂起，测试补鼠标进入事件及150ms等待后完整重跑通过。截图已生成但图片工具未返回可视内容，不宣称目视验收。未运行社区游戏实际出界触控测试；分析不证明设备可玩性，镜像、路径采样、HUD/设备限制见界面说明 |
| 2026-11-07 空间双模式与核心键位 | 位置网格默认 0.1 / X 0.01，自由连续；宽度默认 0.05 / X 0.01 / 自由连续；共享单锚空间增量用于 ghost、节点和组移动，精确属性不量化。1/2/3 为 Tap/Drag/Hold，4 SELECT，↑ 中左右切轨，A/D ±1s、Shift ±0.01s，舞台滚轮导航。统一 verify 通过（277 源文件、三端 0 warning/0 error、核心/编辑器回归、37 发布测试）；随后补充公共空间增量测试，最终 Shell 108 项通过。最终 Godot 原生无窗口回归 28 组通过，含三轨网格/X/自由、滚轮方向，日志无错误；X 测试初次因合成键盘事件未 flush 失败，修正测试投递后通过。日志 `builds/spatial-verify.log`、`builds/spatial-shell-final.log`、`builds/spatial-native.log` | 本轮未运行可见窗口，不宣称新下拉框布局/滚轮手感最终验收；模式不持久化，对象磁吸未实现。出界限制及社区游戏判定均未改变；后续应核对现有社区游戏实际触点可达范围，而不是研究原版游戏来替代 |
| 2026-11-07 浮动舞台关闭还原 | 浮动宿主 Closing 先取消关闭，再异步复用 DockStage 将原舞台移回，避免宿主提前销毁；退出应用时不触发还原。占位项改按 VisibleDockables 实际成员判断，修复第一次移除后 Owner 残留导致第二次浮动丢失还原位置。独立输出目录 Shell 构建及 99 项 headless 通过，包括两次浮动/关闭/重复还原；结构门禁与 diff 检查通过。用户关闭编辑器后，默认目录重新构建 0 warning/0 error，完整真实窗口 smoke 通过，连续两次调用实际浮动 Window.Close()（与 X 按钮相同 Closing 链路）均恢复同一 Godot HWND/PID、同一文档，且 IPC 时钟持续；还原后的 F1/Escape 快捷键亦通过。日志 `builds/floating-close-headless.log`、`builds/floating-close-native-smoke.log` | 首次普通输出构建被正在运行的编辑器 PID 81620 锁定（MSB3027/MSB3021），未终止用户进程；用户关闭后才执行真实测试。第一次 headless 新回归第二轮关闭失败，补占位成员检查后通过。本轮真实测试并非鼠标点击标题栏 X，而是实际 Window.Close()；截图已生成但读取工具未返回可视内容，不宣称已目视验收或覆盖所有手动 Dock 布局 |
| 2026-11-07 非二分放置与路径节点手势修复 | 放置读数和草稿保留 ExactBarTime，消除 1/3、1/5 经 double 回构后格式化抛错；SELECT 单独拖真实节点保留 nodeId，PATH 按下立即同步节点选择，节点拖动支持 Z 自由时间及目标轮廓；曲线细分点不再作为可编辑手柄。Godot `--verify-gestures` 24 组通过（3/5/4/12 网格×三轨；SELECT/PATH×Hold/Mixer×三轨），检查真实 ghost 对象、头尾精确读数/草稿、提交事件、节点身份、插值点排除及连接体保留，最终日志无错误。统一 verify 通过：277 源文件、三端 0 warning/0 error、93 项 Shell headless、核心/编辑器回归、37 项发布测试；Godot import 成功。日志 `builds/gesture-regressions.log`、`builds/gesture-verify.log` | 新增测试曾因测试代码编译类型/参数错误、Hold fixture 未显式标尾判及节点在可视命中范围外失败，均修正后重跑通过。本轮未启动可见窗口，不能把对象/读数检查称为 Draw 回调像素验收；用户所述“已提交 Note 不显示”尚未独立目视复现，只确认并修复非二分读数的确定性异常；前一轮窗口通过不能代替本轮验证 |
| 2026-11-07 `result.json` debug 修复 | [21 项核对](<editor-debug-review-2026-11-07.md>)：18 项功能问题、3 项测试缺口；第 18 项数值示例不准确但缺陷成立。最终 `verify.py --no-restore` 通过：276 个源码文件、client/editor/shell 均 0 warning / 0 error、核心及编辑器回归、88 项 Shell headless、37 项发布测试及 diff 检查；含陡坡/各 Scroll 曲线反查、单锚整组吸附、固定逻辑对边调宽、Timing/Inspector 精度、草稿首次资源替换及真实 WAV→FLAC 写包重开。空草稿真实窗口 smoke 通过。完整窗口最终重跑通过：真实音频连续性、PAN 按住期间 Space/seek 与释放恢复、左右轨放置、左轨拖动/调宽，以及保存、路径、快捷键、Dock 和 i18n。日志：`builds/debug-verify-final.log`、`builds/debug-empty-smoke.log`、`builds/debug-full-smoke-retry.log`；截图在 `builds/editor-debug-20261107-empty/`、`builds/editor-debug-20261107-retry/` | 首次完整 smoke 的 PAN 测试误将停止流的 `StreamPaused` 当挂起判据，已改为独立 PAN 状态加实际播放/位置/计数断言；不得把该失败记为产品音频泄漏。第二次完整 smoke 在 Dock 阶段遇到 DXGI 空帧退出（保留 `builds/debug-full-smoke-final.log`），未改产品代码，第三次完整重跑通过。截图文件已生成，但本轮图片读取工具未返回可视内容，未完成截图目视核验；不代替用户视觉、手感、听感验收。跨轨差速多选 ghost 的完整原生组合、左右两边所有 resize 手势、其他 DPI、长谱性能仍未穷尽 |
| 2026-11-07 制谱器放置与三轨交互切片 | 新增真实 NoteView placement ghost、Hold/Mixer 两段式尾点、宽度记忆、Z 自由时间、X 位置精调、目标 Bar/位置/宽度读数、跨轨导引和同位置/同宽标记；多选按下不再塌缩，SELECT 支持整组拖动与单次组合撤销。侧轨命中/选择/调宽统一使用 `GameplayVisualMapper.BoundsAt`，不再二次交换 X/Y；时间反查收归 `EditorStagePlacement`，覆盖侧轨尺度、BPM/Scroll、玩家和逐 Note 流速；新增精确中心坐标 `StagePlacementDraft`。统一 `verify.py --no-restore` 通过：275 个源码文件、三端 0 warning / 0 error、核心与编辑器回归、57 项 Shell headless、37 项发布测试和 diff 检查。空草稿与完整 smoke 通过，真实窗口覆盖两段式 Hold、左右放置、左轨拖动/调宽、PAN 和原有保存/播放路径；截图含 `05-placement-ghost.png`、`05-hold-tail-ghost.png`、`06-side-ghost.png`、`06-side-authoring.png` | 主代理已查看上述截图，但不代替用户最终视觉、吸附手感与听感验收；其他 DPI、长谱频繁 ghost 重建成本、完整手动 Dock、真实波形和合并桌面包仍未覆盖 |
| 2026-11-07 制谱器 P2 直接编辑与对轨 | 接入 SELECT 普通 Note 拖动、边缘调宽、Hold/Mixer 整体移动、拖动幽灵与 PAN；Inspector 支持单选精确 BarTime/中心/宽度/轨道/类型；新增 BPM/Scroll Dock 增删改、曲线和尾事件处理。新增 `ShellChartEditor` 统一把 UI/舞台请求转成原子文档命令，撤销/重做保留仍存在的 Note 选择。统一 `verify.py --no-restore` 通过：272 个源码文件、三端 0 warning / 0 error、核心与编辑器回归、54 项 Shell headless、37 项发布测试和 diff 检查。Godot 4.7.1 Mono 正式导入并生成新 UID；`--smoke-empty` 与 `--smoke` 通过，真实鼠标覆盖 SELECT 移动/调宽/撤销、1.4× 逐 Note 差速拖动、Hold 放置/插点/拖动及 PAN 预览时钟；另回归非网格精确时间在调宽后不变 | 自动窗口与状态检查不代替用户最终视觉、手势和听感验收；其他 DPI、长谱/大音频性能、真实波形、偏好持久化与合并桌面包仍未覆盖 |
| 2026-11-07 制谱器 P1 管理与预览更新 | 接入包/差分组合设置、差分复制/删除/重排、资源替换与预览音频解析、多选批量调速/步进/删除及 Hold 节点判定；普通编辑改用带 revision 的 `update`，完整加载与音频变化仍走 `load`，相同路径音频复用。统一 `verify.py --no-restore` 全部通过：270 个源码文件、三端 0 warning / 0 error、核心与编辑器回归、38 项 Shell headless（含设置对话框加载）、37 项发布测试和 diff 检查。Godot 4.7.1 Mono 正式导入成功并生成新脚本 UID；独立 `--smoke-empty` 与 `--smoke` 均通过，后者验证播放中编辑保持播放、播放头和选择。自动窗口使用 Topmost 防止前台抢占，WAV fixture 延长为 20 秒以覆盖播放中更新 | 自动回归和窗口状态检查不代替用户最终 UI、批量操作手感或听感验收；其他 DPI、完整手动 Dock、长谱/大音频性能、普通 Note 拖动手势和合并桌面包仍未覆盖 |
| 2026-11-03 Godot 启动灰窗治理 | 通过逐帧截图和顶层窗口 PID/父窗口记录确认灰窗来自 Godot 初始化，而非 Avalonia 白帧。最终启动直接使用非 console Godot，以 `--wid` 创建 Avalonia 子窗口、初始分辨率 1×1，并关闭 boot splash；嵌入后设置最终位置再显示，鼠标按下同步 Godot/Win32 焦点。最终 312 帧启动采集未出现大面积 Godot 灰帧，空草稿首次放置及完整 smoke 的选择、Q/E/W/S、播放、Ctrl+S、Hold、路径、Dock 均通过；统一 `verify.py --no-restore` 全部通过：268 个源码文件、三端 0 warning / 0 error、核心与编辑器回归、25 项 Shell headless、37 项发布测试和 diff 检查。证据在 `tmp_shots/startup-godot-final/` | 自动逐帧和窗口验证不代替用户最终视觉/手感验收；其他 DPI、完整手动 Dock、长谱性能、听感和合并桌面包未覆盖 |
| 2026-11-03 审查第 1–3 部分修复 | 舞台全局鼠标松开先转换为 Control 局部坐标；单条 IPC 命令异常以 `error` 返回且舞台继续运行。FLAC Probe 在分配前限制 64 MiB，以轻量帧遍历逐个子帧定位帧尾并校验 CRC-8/CRC-16，不生成 PCM；RIFF/WAV chunk 遍历限制在容器声明边界。测试 FLAC fixture 改为真实 constant 子帧和 CRC-16，新增载荷损坏、超限文件、RIFF 附加数据及 fixed/LPC/立体声完整帧回归；UTF-8 测试编码器修正续字节数量与首字节掩码，覆盖 128/2048/65536 编号边界。统一 `verify.py --no-restore` 全部通过：268 个源码文件、三端 0 warning / 0 error、核心与编辑器回归、25 项 Shell headless、37 项发布测试和 diff 检查。两个独立真实窗口 smoke 通过，含非法 IPC 后舞台存活、空草稿首次放置、Hold 与路径手势 | 自动窗口验证不等于用户手感/视觉验收；其他 DPI、长谱和大音频性能、音乐听感、合并桌面包仍未覆盖。后续差速性能、内存、尾随标签合同和低优先级加固不在本轮实施范围 |
| 2026-11-03 空草稿放置与鼠标松开修复 | 修复新建空草稿被预览编码/解码/RuntimeAdapter 的计分 Note 校验阻断：仅编辑预览显式 `requireMainJudgement:false`，默认 strict 行为不变，并有默认拒绝回归。Godot 根控制器补收全局左键松开，避免嵌入窗口的放置/路径提交丢失；空草稿与完整 smoke 分窗口隔离。统一 `verify.py --no-restore` 再次全部通过：266 个源码文件、三端 0 warning / 0 error、核心与编辑器回归、25 项 Shell headless、37 项发布测试和 diff 检查。真实 `--smoke-empty` 通过空舞台加载、首次原生点击放置和首次保存；真实 `--smoke` 通过 Note 选择、Q/E/W/S、播放、Ctrl+S、Hold 放置、路径双击/拖动、Dock 与中英文 | 自动窗口验证不是用户最终手感/视觉验收；普通 Note 拖动/调宽、Pan、其他 DPI、长谱性能、音乐听感和合并桌面包仍未覆盖。截图在 `builds/editor-shell-empty-smoke/empty-first-note.png` 与 `builds/editor-shell-smoke/05-authoring.png` |
| 2026-11-03 制谱器新建/差分与舞台手势 | 接入新建包/添加差分表单、差分切换、七类 Note 点击放置/拖动加宽、路径双击插点/拖动头与节点、节点曲线和删除；全部修改复用文档命令。Godot 4.7.1 Mono `--headless --path editor --import` 成功并生成新脚本 UID。统一 `verify.py --no-restore` 通过：结构门禁 266 个源码文件，client/editor/shell 均 0 warning / 0 error，核心与编辑器回归、25 项 Shell headless（含双差分 strict 写包重读）、37 项发布测试和 diff 检查 | 本轮 Windows smoke 在原有 Note 点击步骤失败；截图显示系统处于锁屏界面，原生输入未进入编辑器，因此新建表单的原生鼠标和新增放置/路径实机手势均未验收，不能沿用 11-02 的窗口通过记录替代。普通 Note 拖动/调宽、Pan、真实波形、完整手动 Dock、其他 DPI、长谱性能、音乐听感和合并桌面包仍未覆盖 |
| 2026-11-02 制谱器 Avalonia + Godot 主线 | 新增 `editor/shell` Avalonia/Dock/Ursa/Suki 外壳与现有 Godot 原生舞台 IPC；接入 strict v2 打开、保存/另存为、真实 Note 选择/差速/删除/撤销重做、中英文和 Q/E、W/S。统一 `verify.py --no-restore` 通过：结构门禁 264 个源码文件，client/editor/shell 均 0 warning / 0 error，核心与编辑器回归、16 项 Shell headless 文件/命令/UI 检查、37 项发布测试和 diff 检查。Windows 1.25× DPI smoke 通过真实包/WAV 加载、Note 点击、倍率/播放快捷键、Ctrl+S 重读验证、Dock 浮动还原和中文指南；截图在 `builds/editor-shell-smoke/` | 未覆盖新建/加差分、放置/移动/路径手势、真实波形、完整手动 Dock、其他 DPI、长谱性能与音乐听感；没有合并桌面安装包。自动播放到时钟/状态，不作为听感验收；用户最终 UI 验收仍待进行 |
| 2026-11-02 Note 差速、缓动与音频格式扩展 | v2 新增可选父 Note `scrollSpeedMultiplier`（缺省 1 且不改旧 Digest），Scroll/路径新增六种 Quad/Cubic/Sine 缓动并覆盖跨帧接触求根；编辑命令、strict 编解码、运行时视觉与差速生成窗口已接入。默认写包 Probe 支持 WAV/MP3/FLAC/Ogg Vorbis，客户端以托管解码支持原生 FLAC。`python -B tools/verify.py --no-restore` 通过：结构门禁 247 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查；清理测试警告后编辑器回归再次通过。Schema/golden pack 校验通过，golden Digest 保持 `5f3cafcfa02097b555c860e0da9ca8e8a55ce428df8d69fcb263e080a7249a1f` | 未启动 Godot 做视觉/听感验收，未覆盖 Android、真实设备性能或制谱 UI；FLAC 客户端限常见 8/16/24-bit、1–2 声道并有输入/PCM 预算，24-bit 转 16-bit。Opus/M4A/AAC 仍不支持 |
| 2026-10-30 制谱器旧 UI 移除 | 旧页面、画布、菜单、输入和设置宿主已删除，入口仅保留空白 `StandaloneEditorMain`；文档/命令/会话/投影等纯核心保留，新增 `EditorGameplayDigest` 及与 shared 结果一致的回归。`python -B tools/verify.py --no-restore` 通过：结构门禁 225 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查。Godot 4.7.1 Mono `--headless --path editor --quit` 实际加载空白场景成功，无脚本或资源错误 | 当前没有制谱 UI 可供功能或视觉验收；headless 只证明场景可加载，不证明窗口观感、新 UI 交互、音频或设备行为。旧核心能力仍由自动回归覆盖，后续需重新接线 |
| 2026-10-30 顶部判定栏居中 | `TopHudOffsetY` 从 16px 调整为 8px，顶部判定栏由 `y=28..68` 上移到 `y=20..60`，中心 `y=40` 位于屏幕顶部与 `y=80` 蓝线正中；暂停按钮及其触控区域同步上移 8px，舞台与下方 HUD 不变。`python -B tools/verify.py --no-restore` 通过：结构门禁、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查 | 本次为确定坐标的小幅位移，未重新启动 Godot 或捕获新截图；未覆盖 Android 和真实暂停触控，最终观感仍由用户确认 |
| 2026-10-30 游玩页面纯位移 | 客户端舞台、音符、特效与下方 HUD 下移 40px，顶部统计和暂停按钮下移 16px；Center 输入阈值同步为 `y>811`，Side 纵向反查与暂停触控区域同步偏移。共享基准、编辑器、校准页、元素尺寸、落速和侧轨距离比不变；背景增加顶部覆盖以避免偏移后露出透明条带。最终 `python -B tools/verify.py --no-restore` 通过：结构门禁 254 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查 | Godot 4.7.1 Mono / Forward Mobile 以 Hold Recovery Test 录制 1920×1080@60 并检查 `tmp_shots/gameplay-layout40-preview.png`：判定线 y=901、底部信息板余量 52px、Hold/特效随舞台移动，顶部无透明条带且底部无裁切。未做 Android、真实触控/暂停点击或用户最终视觉验收；自动回归不直接覆盖新的客户端屏幕偏移 |
| 2026-10-29 命中粒子回收 | `GameplayHitParticles._Process` 推进 `_age`，寿命加尾段结束后停止处理；普通实例仍由 `GameplayEffects.RecycleExpired` 重置归池，独立预热实例到期 `QueueFree`。不改粒子数量、材质、寿命参数与持续粒子。`python -B tools/verify.py --no-restore` 通过：结构检查、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 diff 检查 | 未启动 Godot；现有纯逻辑回归不覆盖 GPU 粒子节点的实际归池与预热释放，运行时及视觉仍待验证 |
| 2026-10-29 审查高优先级修复 | GameSession 缓存读取与 GameplayMain 入口按当前模式 Preset 决定复用；ScoreFilePersistence 接管文件恢复，读取失败不清空已有内存成绩，首次保存前复制原始字节到 `scores.json.recovery-<guid>`，复制失败则不发布新存档；同目录写包回滚逐项尝试，聚合发布与回滚错误，保留未恢复备份。新增成绩文件正常往返/损坏恢复/重复保存/备份失败回归，以及目录阻塞文件恢复的写包故障注入，证明仍恢复其他文件并保留原错误和备份。`python -B tools/verify.py --no-restore` 通过：结构门禁、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试、diff 检查 | 未启动 Godot、导出或做设备验收；模式缓存链路为源码检查及客户端编译，尚未覆盖真实 UI 的 Standard→Hardcore→Standard 操作；没有改变成绩模式隔离，也未修复本次审查的中低优先级项目 |
| 2026-10-29 首页 Home v2 + Mk VI | 首页按 Penpot「Game — Home v2 · 01 Compact」（MCP 读取几何/颜色/字距；此前已在板内新增“段位模式 / 联机大厅”并修正主按钮文字层级，本轮接入未再改 Penpot）重做：`Main.cs` 只装配与入场，布局在 `ui/home/`（`HomePalette`、`HomeBrandBlock`、`HomeActions`、`HomeActionTile`、`HomeUtilityRow`、`HomeReticle`）。`detonation-orrery-mk6.glb`（3756 三角面、14 材质、单个 4 秒 clip）替代 Mk II：FOV 32 / 18° 仰角 / 22° 方位角 / margin 0.90，1000×920 子视口；Environment glow 关闭，近/远双层 canvas 弥散光默认 `u_glow=1.6` 且开启；Full 原速循环、Reduced 0.45 倍速与 0.06 脉动、Off 停帧且弥散能量固定 1.0；删除 Mk II 的 8 秒归一化、轨道脉冲灯与材质名调优。新增“段位模式 / 联机大厅”禁用底板，保留谱面工坊禁用与游玩/设置真实导航，顶部 CREDITS / LOGIN 置灰无导航；三处注释小字未添加，右下版本串仍走 `Main.BuildDescription()`。发布合同同步：`export_presets.cfg` 与 `apk-policy.json` 用 mk6 取代 mk2 并把 `shaders/home_diffuse.gdshader` 列为必需，`asset-provenance.json` 记录真实哈希（模型 `877ceeff…e8774`，生成器 `design/home-orrery-v6/build_orrery_mk6.py` `a42b8791…f279e`），发布测试改为断言 mk6 必需且 mk2 不再必需。Godot 4.7.1 Mono `--headless --import` 正式生成 `.glb.import`、着色器与 6 个新 `.cs` 的 `.uid`。最终 `python -B tools/verify.py --no-restore` 通过：结构门禁 252 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 `git diff --check` | 主代理用 Movie Maker 在 1920×1080@60 离屏窗口录制并抽帧：布局与 Penpot 主按钮底板 bbox 完全一致（96,616–745,723），入场错峰可见，Full/Reduced/Off 第 60 帧与第 120 帧差异分别为 52.71 / 24.42 / 0.00（Off 完全静帧）；弥散开/关 A/B 在模型区差均值 0.66（最大约 0.17 的单通道 8-bit 亮度）；用真实点击核对了游玩→选曲、设置两处导航；后续启动日志与进程核对确认本机 F5 使用 Vulkan 1.4.329 + Forward Mobile，Compatibility 仅用于独立预览。未覆盖：Android/APK 导出与设备、低端帧率、hover/按下手感、CREDITS/LOGIN 与三个禁用入口的触控端到端、Penpot 稿本身；Forward Mobile 下 canvas 弥散比独立预览（gl_compatibility）更收敛，最终视觉与是否加压待用户判断 |
| 2026-10-28 TRACK COMPLETE 普通完成字标 | 新增第七类 `TrackComplete`：不满足 AP、FC、ONE GOOD、ONE MISS、DOUBLE ONE 且非 BLEED 失败的有效正常成绩统一显示 TRACK COMPLETE；沿用 80% 字标、弥散光和 Full 1.55s / Reduced 0.26s / Off 无前奏。无效空成绩仍走无字标保护路径，普通完成不新增 AP/FC 徽章。clean-room 字形生成器补充 K 字形与新字标，运行时 PNG、导出白名单和来源清单由打包脚本更新。`python -B tools/verify.py --no-restore` 通过（两端构建、核心/编辑器回归、37 项发布测试及 diff 检查） | 主代理用 Godot 4.7.1 Mono 正式导入新 `.ctex`，再以受控验证入口指定 Hold Recovery Test、AUTO=false 完成真实窗口录像 `tmp_shots/result-track-complete-client.mp4`；日志 `P=0 / M=6 / outcome=TrackComplete`，无资源或着色器错误。抽帧确认字标、斜纹、屏风和结算页。未重新覆盖其他六类、Android、公开导出或用户最终视觉验收 |
| 2026-10-28 SURGE 结果字标前奏与结束双锁存 | 正常结束改为谱末 +2 秒与音频自然结束**两者取晚**：新增 `GameplayEndCondition`（两个单调锁存 + 音频结束后按真实帧 delta 补时，单帧上限 0.5s；固定帧/回退时钟只走谱末路径），`SongPlayback.TracksNaturalEnd` 区分可观测性，BLEED 耗尽改为当帧 `Pause()` + `ShowResults(stageFailure:true)`。屏风前新增六类结果字标前奏：`ResultFlow`（交付顺序与只一次闸门）、`ResultOutcomePrelude`（自带 `_Process`，1280×320 @ (320,380)、斜纹区 translate(192,108) scale(.80)、`wordmark_diffuse.gdshader`）、纯时钟 `ResultPreludeTimeline`（Full 1.55s / 普通 0.68s / Reduced 0.26s / Off 0 帧）、纯分类器 `ResultOutcome`、防穿透 `ResultInputGuard`（120ms）；`ResultScreen` 徽章改用同一分类器并在每次 `Present` 重置 covered/unlock。六张 1600×400 PNG（`client/assets/results/outcomes/`）与着色器由新增 `tools/grade-assets/build_runtime_outcomes.py` 复制/写 `.import`/加白名单/写 provenance，`build_runtime_results.py` 显式跳过该子树（其 29 条 `generatorSha256` 已同步）。最终统一 `verify.py --no-restore` 通过：结构门禁 246 个 C# 文件、两端 0 warning / 0 error、核心回归（新增 `[22]` 双锁存、`[23]` 六类分类与徽章、`[24]` 前奏三档/跳过/防穿透/只一次，原 ResultReveal 用例不变）、编辑器回归、37 项发布测试及 `git diff --check` | 主代理已用 Godot 4.7.1 Mono 实际导入六张字标 `.ctex` 并编译着色器；Hold Recovery Test 全程 AUTO 正常 AP（Full/Reduced/Off 日志均为 `P=6 / M=0 / outcome=AllPerfect`），Tablear BLEED 耗尽触发 `StageFailure`，另有普通多 Miss `outcome=None` 路径。逐段查看 `tmp_shots/result-{ap,failure,reduced,off}-client.mp4` 与对应日志，确认 80% 字标/斜纹、弥散光、屏风衔接及 Off 无前奏帧；临时设置已通过 UI 恢复为 Full / BLEED 开 / AUTO 关。仍未覆盖 Android 触控、公开 APK 导出验包、坏音频包、音效、专用失败页与血条；最终视觉验收待用户 |
| 2026-10-27 结果前奏交接文档 | 新增 `docs/result-outcomes.md` 并接入文档导航、handoff 与结算规格；区分当前正常结束取早、未来取晚方案及静态候选。本次 4 份相关文档本地链接检查 0 缺失，最新 expressive 对照图与生成器存在，`git diff --check` 通过 | 仅文档整理，没有实施正常结束取晚、前奏、音效或客户端资产接入；未重跑 C#/发布测试、未启动 Godot，候选视觉尚未最终定稿。全量链接扫描另发现模块化旧审查中的 31 个历史源码链接失效，本次未扩大范围处理 |
| 2026-10-27 BLEED 数值与耗尽结算 | 新增 `shared/Judge/BleedHealth.cs`（Standard/Hardcore 数值、100 封顶、0 钳制、耗尽幂等），`GameplayMain` 在 `ApplyUnit` 同步血量、`_Process` 判定与 Mixer 收尾后检查耗尽，先 `_playback.Pause()` 再复用 `ShowResults`，自然完成仍保留尾奏；核心回归新增 `[21] BLEED health rules and depletion`；Hold 中途断触/头 Miss 的 shared 批量结算另在客户端两处补齐血量。最终统一 `verify.py --no-restore` 通过：结构门禁 237 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 `git diff --check` | 未启动 Godot、导出或设备验收；耗尽暂停调用与正常尾奏路径经代码审核及构建回归确认，实际听感未测；未设计 Bleed 血条 UI，耗尽专用结算仍是复用普通结算的占位；Mine 不参与扣血、Auto 不写成绩等既有行为只按调用点静态确认 |
| 2026-10-27 全量功能族目录整理 | client UI/game、editor、chart-editor-core 与两套测试完成按功能族分组；场景/程序入口保留，UID、core-test 链接、策略和活跃文档同步。最终统一 `verify.py --no-restore` 通过：235 个 C# 文件、两端 0 warning / 0 error、核心与编辑器回归、37 项发布测试及 `git diff --check` | 未启动 Godot、导出或设备验收；构建与静态路径检查不证明场景实际加载、视觉或触控体验 |
| 2026-10-27 手势/视口/设置拆分与目录整理 | 目录移动后统一 `verify.py --no-restore` 通过：结构门禁覆盖 235 个 C# 文件，两端 0 warning / 0 error，核心与编辑器回归、37 项发布测试及 `git diff --check` 通过；场景/自动加载、UID、core-test 链接和策略路径同步更新，新增手势预览、滚动与设置 codec 回归 | 未启动 Godot、导出或设备验收；场景实际加载、触控手感、动态行高命中、AudioServer 和视觉效果未覆盖；固定步距命中疑点未宣称复现或修复 |
| 2026-10-27 命中与 Track Handoff 拆分 | 最终代码运行统一 `verify.py --no-restore` 通过：结构门禁覆盖 228 个 C# 文件，两端 0 warning / 0 error，核心与编辑器回归、37 项发布测试及 `git diff --check` 通过；新增命中优先级/边缘与 Full/Reduced/Off 相位、放电完成和重置回归 | 未启动 Godot、导出或设备验收；纯命中适配和相位测试不证明真实输入派发、节点层级、GPU 合成、coverage 淡出或放电尾巴的实际视觉效果 |
| 2026-10-27 选择与创建请求拆分 | 最终代码运行统一 `verify.py --no-restore` 通过：结构门禁覆盖 223 个 C# 文件，两端 0 warning / 0 error，核心与编辑器回归、37 项发布测试及 `git diff --check` 通过；新增选择状态与创建请求核心回归，保留 Shift 主选择语义和 AddDifficulty 不查询外部资源的行为 | 未启动 Godot、导出或设备验收；纯核心测试不证明真实 Godot 输入派发、命中几何、焦点或视觉效果 |
| 2026-10-26 模块化复审 | [进程/对象边界分析](<modularity-review-2026-10-26.md>)；重新运行结构报告通过（219 C# 源码文件），补统计 NoteColorsScreen 3 partial 共 895 行；219 个 C# 文件、八个项目、三个检查/策略文件及本轮审查保护指纹在建立后未变；未发现暂存项；新增分析及入口文档链接/源码行号核验与 `git diff --check` 通过 | 仅静态复审及文档检查；未运行 C# 构建/回归、Godot/headless 探针、导出/验包或设备验收；建议未实施，静态风险未复现 |
| 2026-10-26 文档整理 | 源码/文档/项目引用静态核对；结构检查通过（219 源码文件）；37 项发布测试通过；35 份 Markdown / 378 个本地链接检查通过，31 份快照及 7 项归档哈希一致；417 个已跟踪非文档路径与 Git index 均未变；`git diff --check` 通过 | 未重跑 C# build/core/editor-tests，未启动 Godot、导出 APK/桌面包或做设备验收；发布测试不是实际 APK 验包 |
| 2026-10-25 client 续拆历史记录 | 统一 `verify.py --no-restore` 通过：两端 0 warning / 0 error、核心/编辑回归、37 项发布测试；9 组 1980 帧状态比较一致（详情卡类型名归一化） | 探针固定试听完成顺序和 elapsed；不证明真实 I/O 时延、GPU 像素、触控或听感 |
| 2026-10-25 第三轮历史记录 | 两端构建/回归/37 项发布测试；8 组 11520 条游玩记录与 3300 帧 UI 捕获状态一致；Windows ExportRelease 程序集 publish 含 Presentation/编辑核心 DLL | 不是完整 Godot 桌面包或 Android APK 导出；不是可见窗口/真机验收 |
| 2026-10-25 第二轮历史记录 | 统一验证；8 组 3840 模拟帧/7680 判定与节点记录一致；草稿新增差分后首次保存回归 | 不证明 GPU 粒子、音频、性能或所有 UI 交互 |
| 2026-10-20 配色 v2 历史记录 | 两端构建、核心/编辑回归和 37 项发布测试通过 | 无对应新 APK/截图；触控、软键盘、最终视觉待用户验收 |

2026-10-25 个别额外 editor restore/build 曾因 NuGet 漏洞索引不可达出现 2 条 NU1900（0 error）；不能把这改写为代码失败，也不能无条件忽略全部警告。
2026-10-13/19/20 的 SongSelect CS8604 是旧构建记录；10-25 统一验证记录为 0 warning，不应继续列为当前已复现警告。本次没有重新编译确认。

历史原文与边界：

- [第二轮报告](<archive/reports/2026-10-25-modularization-phase2.md>)、[第三轮报告](<archive/reports/2026-10-25-modularization-phase3.md>)、[client 续拆记录](<archive/reports/2026-10-25-client-followup.md>)。
- [配色 v2 的当时验证与设备清单](<note-colors-v2.md>)。
- [整理前交接快照](<archive/2026-10-26-cleanup/README.md>) 保留了更早的逐项构建/验包流水。历史记录不等于当前工作区重验。

## 用户反馈与验收边界

- 2026-10-07：既有结算 v2 原流程通过用户实机验收。
- 2026-10-14：用户认可已接入客户端的 X/Ψ/D；Mixer 初步反馈“目前看来没什么问题”，不扩大为全部边界通过。
- 2026-10-17：多指输入、短 Hold 手感、对象池修复、侧轨 `691/790` 基本通过。侧轨保留现值；不再把这些整体列为未验收，也不推定设备/指数量/所有边界均已覆盖。
- 校准页已有 Android 采样/统计/APPLY 全流程验收记录；10-25 拆分并未重新进行设备验收。
- 最新配色、材质、低端性能、密集曲线、快速切曲和暂停恢复专项仍按 [交接 §4](<handoff.md#4-仍需处理>) 跟踪。

## 最近有记录的 Internal 测试包

**这是历史产物身份，不是“与当前源码同步”的承诺。** 2026-10-26 未重新哈希、验包或检查安装；使用前须重新核对现有文件。

| 字段 | 2026-10-14 导出/10-17 反馈时的记录 |
| --- | --- |
| 本地路径 | `client/builds/internal/dynamite-universe-internal-testdata.apk`（忽略，不随克隆取得） |
| 版本 | `0.1.2-internal-testdata` / code 4 / ARM64 |
| 身份与用途 | Internal / Debug 流程 / `com.dynamiteuniverse.game.internaltest`；**DO NOT DISTRIBUTE** |
| 大小 | 185406710 bytes |
| SHA-256 | `235dc36bc64214e1ff67978168346f900ae2453e6e5f26ed661b77867d904e82` |
| 覆盖 | 短 Hold、八评级、Mixer 放置/宽限、音频回退、此前异步卡/池修复及侧轨比例；当时 APK gate PASS |
| 不覆盖 | 10-20 配色及 10-25 拆分；从当时未提交工作区导出，不能只凭 HEAD 独立重建 |

Release 性能探针不等于合法的正式 Internal 测试包。Public 包只有按 [发布流程](<releasing.md>) 从 clean worktree 导出并通过最终 checker 才能分发。

## 非图形复验命令

从 **community 根目录**执行；以下是复验流程，不是本次已经执行的声明。

```powershell
git status --short --branch
python -B tools/verify.py
# 已还原依赖：python -B tools/verify.py --no-restore
```

统一脚本串起以下步骤，任何非零退出即停止：

```powershell
python -B tools/architecture/check.py --report
dotnet build client/DynamiteUniverse.csproj
dotnet build editor/DynaMakerUv.Editor.csproj
dotnet build editor/shell/DynaMakerUv.Editor.Shell.csproj
dotnet run --project tools/core-tests/CoreTests.csproj
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --headless
python -B -m unittest discover -s tools/release/tests -p "test_*.py"
git diff --check
```

- 两套 C# 回归是断言式控制台程序，应使用 `dotnet run`，不能仅跑 `dotnet test` 后认定执行过。
- 构建要求 0 error；遇环境 warning/离线 restore 必须写明条件和实际结果，失败不能跳过后继续宣称通过。
- 核心默认只用版本化 clean-room fixture。官方开发语料需显式传参，且不能代替公开门禁：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj -- --dev-testdata client/testdata/packs
```

- 结构门禁只证明依赖/源码链接/行数预算，不是语义、编译、行为或性能证明。
- `.tmp/modularize*/` 的快照/探针被忽略，可能不随克隆取得，不能当成已接入 CI 的稳定回归入口。涉及行为变化时应补版本化用例，不能只引用旧 headless 哈希。
- 严禁为通过检查而放宽源码预算、发布白名单、品牌历史允许计数或把内部语料变为默认依赖。

## 运行、截图和录屏

只在用户要求运行或确需视觉验证时使用 **Mono** 版 Godot。先结束残留窗口，无进程时 taskkill 提示未找到属预期：

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe

# 游戏；编辑器从 Avalonia Shell 启动并嵌入 Godot
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path client
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj

# 制谱器真实窗口回归（分窗口隔离，会启动/结束本次 Godot 并保存截图）
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke-empty builds/editor-shell-empty-smoke
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke builds/editor-shell-smoke
```

制谱器直接使用 Godot `--path editor` 仅用于原生舞台调试，不包含文件菜单和 Avalonia 工作台。
在同一受控操作中启动、采集、结束本次进程；输出放被忽略的 `tmp_shots/`，不残留窗口污染下次截图。

以下仅以窗口位于 (0,0)、1600×900 为例，实际需调整 gdigrab 偏移和尺寸：

```powershell
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -i desktop -frames:v 1 tmp_shots/shot.png
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1600x900 -framerate 30 -i desktop -t 45 -c:v libx264 -pix_fmt yuv420p -preset veryfast tmp_shots/demo.mp4
```

`-frames:v` 放在 `-i` 后；yuv420p 尺寸须为偶数。开发 gameplay 可用 `DYNAMITE_UNIVERSE_START_SEC` 定位，Esc 暂停，F12 跳至谱尾前 3 秒。
headless SongClock 使用系统计时器/固定验证时钟，不代替窗口音频结束、真实 I/O、听感、GPU 像素或真机触控验证。

## 文档维护检查

文档移动后检查所有活跃文档与新归档的相对链接/锚点；旧不可变快照的链接以原位置解释，不能为“修链接”重写历史正文。
本次整理的迁移清单、原文 SHA-256 与归档范围见 [2026-10-26 归档说明](<archive/2026-10-26-cleanup/README.md>)。
