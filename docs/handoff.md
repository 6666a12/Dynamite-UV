# Dynamite Universe 当前交接

> 更新：2026-11-12。本文面向**当前工作区**，不是仅描述 Git HEAD；以当前源码、项目引用与验证记录为准。
> 本轮实际检查与历史构建、出包、用户验收分别记录在 [验证状态与操作](<verification.md>)，不能互相替代。
> 接手顺序：本文 → [模块地图](<module-map.md>) → 与任务有关的 [专题文档](<README.md>)。归档不作为当前待办。

## 1. 项目约束

- clean-room 社区音游；Public 不能含原版素材/谱面，整个 `client/testdata/` 只限受控开发/Internal。Public 发布必须按 [发布流程](<releasing.md>) 从 clean worktree 导出并通过最终验包；Internal 可以从受控开发目录导出，但仍须验证内部身份与数据且不得公开分发。
- Godot **4.7.1 Mono** / C# / `net9.0`；[游戏](<../client/>) 与 [Windows 制谱器](<../editor/>) 是独立项目、场景、程序集和导出 preset，互不启动，编辑器不进入 Android 包。
- 游玩固定 **1920×1080 + canvas_items 等比黑边**，不做自适应布局。当前侧轨距离尺度保留 `691/790`；改几何前读 [玩法规格](<gameplay-spec.md>)、[像素标定](<pixel-calibration.md>) 与 [视频依据](<video-geometry-analysis.md>)。
- [shared](<../shared/>) 拥有格式、精确时间、判定、计分、成绩身份和包写入语义，不依赖 Godot；[presentation](<../presentation/>) 是共用 Godot 视觉层，不读取两端全局会话/设置。editor 引用 ChartEditor.Core，不再链接 client 源码。
- 修改前先看 **community 自己的 Git 状态**。不得 reset、checkout、清理或覆盖无关改动，不为提交便利回退现有功能。旧报告的数量、路径和“下一步”不代表当前基线。
- 默认中文交流；普通修改后不代替用户启动游戏目测。确需运行/采集时按 [§6.2](<#62-启动截图和录屏>)，先结束残留 Godot。
- 原创代码与资产目前没有项目级 LICENSE，保留所有权利；第三方归属见 [通知](<../THIRD_PARTY_NOTICES.md>)。签名材料与带 token 的工具 URL 不入库。

## 2. 当前实现

### 2.1 游戏

| 区域 | 已接入 | 能力边界 |
| --- | --- | --- |
| 主流程 | 首页 → 选曲 → 游玩 → 结算；暂停/继续、重开、下一首 | 首页采用 Penpot「Game — Home v2 · 01 Compact」布局与 Mk VI 模型；只有“游玩”“设置”接真实导航，“谱面工坊”“段位模式”“联机大厅”是禁用占位（预留服务器曲库/段位/联机），顶部 CREDITS / LOGIN 置灰不可点 |
| 选曲 | 曲名/曲师/谱师实时搜索、展开卡、难度切换、最佳成绩、试听、QUICK SETTINGS | 筛选/排序占位；进场收起、不试听，展开才自动试听，切歌/收起/离页/START 停止，进度只读且不循环 |
| 玩法 | 三轨鼠标/多点触摸、Hold/Mixer、Mirror/Auto、Standard/Hardcore、BLEED | Auto 全 Perfect 且不写成绩；BLEED 已有真实扣血（100 血，Miss −2/−4、Perfect +0.2/+0.1、Great/Good +0.05/不回血），耗尽当帧暂停并立即进入 STAGE FAILURE 字标前奏，仍复用普通结算页；v8 HUD 已接真实血量/统计/暂停，模式固定蓝/深红、Full/Reduced/Off 分档，用户已确认本体实际效果满意，见 [游戏 HUD](<gameplay-hud-v8.md>) |
| 设置/校准 | 四 tab 设置与持久化、偏移/落速/特效/音量；120BPM 合成节拍试打、统计与 APPLY | 校准原流程有 Android 用户验收；完整 Hit/UI 声音资产链路未完成 |
| 结算 v2 | 不透明双屏风、Ω/Ψ/X/S/A/B/C/D 八评级、AP/FC、判定分布、纪录增量和分档动效 | 正常完成已改为谱末 +2 秒与音频自然结束**两者取晚**（分别单调锁存）；屏风前新增七类字标前奏（普通完成为 TRACK COMPLETE），三档动效与跳过已接入，见 [结果前奏](<result-outcomes.md>)。用户已确认首页与结果字标完成；结果音效仍待处理 |
| 音符配色 v2 | 六类真实样张、调色与可读性观察双状态；20 步撤销/重做、十槽库、草稿/显式应用 | 不改玩法、不污染游玩全局预览；对比/色差只提醒、不钳色、不代表可读性认证；用户已确认音符配色 v2 完成，见 [专题](<note-colors-v2.md>) |

当前性能实现包括轨道窗口扫描、路径斜率缓存、Note/连接体/特效池、音频路径缓存、谱面预加载 LRU×4、详情/试听异步及成绩延后保存。**已经做过这些优化不等于低端帧率达标**；不按旧审查重新实施同一修复。

### 2.2 谱面、判定与存档的重要边界

完整规则以 [玩法规格](<gameplay-spec.md>)、[v2 合同](<chart-format-v2.md>) 与 shared 为准；以下是接手时不能误改的边界：

- 显式 v2 严格解码/跨文件校验/Digest，失败拒绝；普通 legacy 优先转 v2，不适用才保留 legacy-direct。开发/Internal 扫 `res://testdata/packs` 再扫 `user://charts`，同 id 用户包覆盖；Public/未分类导出只扫用户包。
- **res:// 必须走 Godot 虚拟文件系统，不可引入 System.IO**：导出后原资源可能在 PCK/导入 remap 中。此分支 Digest 按空音频字节计算；user:// /绝对路径的真实文件分支含音频字节，两者不可混同。见 [加载适配](<../client/scripts/game/charts/V2Integration.cs>)。
- legacy `Position` 是左缘；v2 是 `center + width`。画布预览中的 Center 仍沿用左缘语义，提交文档命令时才转换；精确 BarTime 不经显示秒值往返。
- v2 父 Note 可用 `scrollSpeedMultiplier` 在全局 Scroll 与玩家落速之上逐 Note 调整视觉倍率，缺省为 1；Hold/Mixer 全路径共用父倍率，命中秒与判定不变，非默认值进入 Digest。Scroll/路径曲线已扩至 Quad、Cubic、Sine 常用族，`smooth` 仍为路径专用 PCHIP。
- 客户端直接播放 WAV/MP3/Ogg Vorbis；原生 FLAC 由 shared 托管解码为 16-bit PCM 后包装为 WAV 流。编辑器默认写包 Probe 支持 WAV/MP3/FLAC/Ogg Vorbis 的结构与时长校验；FLAC 输入限 64 MiB，逐帧解析子帧并验证 CRC-8/CRC-16，WAV chunk 不得越过 RIFF 声明边界；Opus/M4A/AAC 明确拒绝。
- 普通判定按固定 150BPM 换算；Standard 依难度选预设，Hardcore 使用 Hard 窗 ×0.5；Hold 首次断触冻结宽限 deadline，Mixer 则为实际音乐时间的 1/8 chart bar 宽限。当前 Mixer 无额外尾判。
- 输入批次锁是精确 double 同刻锁，任何普通输入结算均可武装，Mine 不检查也不武装；触点可复用、跨轨投影可重叠。未知 id 的 Move 忽略，失焦清触点。短 Hold 保留头的晚判窗口，以带时间戳的联系快照处理同帧短点/换指。
- 每帧音频时钟、视图、输入、持续状态、sweep、Mixer 收尾、HUD、结算的顺序不可重排；音频位置单次播放单调过滤，暂停/恢复下限保留。详见 [运行时架构](<runtime-architecture.md>)。
- 分数归一化 1,000,000；HUD ACC 分母为已判理论满分，结算 CLEAR 分母为全谱理论满分。未舍入评级：Ω=100，Ψ≥99，X≥97，S≥95，A≥90，B≥80，C≥60，否则 D；Ψ 的资源/存档键为 Y。
- 成绩写 `user://scores.json`。v2/成功转换路径用 `(packId, chartId, rulesetId, gameplayDigest)`，legacy-direct 保留 `packId:diff`；**无独立 Standard/Hardcore 模式维度，不能称已完整隔离**。部分 legacy 转换可能改变摘要，不等于模式隔离设计完成。
- Auto 不写成绩；JudgeEngine 的原版研究 Health/Boost 虽有内部计算，无对应 HUD/GameOver/Buff/EX Boost；它们不同于已接 HUD 的独立 BleedHealth。原版研究数学不驱动社区 HUD/存档。
- 校准建议偏移 = 当前值 − 样本中位数，5ms 取整、±300 钳制；校准时钟不含 userOffset。手写循环 WAV 保留 loop_end 后 1 个静音保护帧，避免 Android 混音边界越界。

### 2.3 制谱器

- 2026-11-02 正式路线改为 `editor/shell` 的 Avalonia 12 外壳（Dock.Avalonia + Ursa + Suki Motion），通过 Win32 HWND 嵌入现有 Godot 4.7.1 Mono 项目；不引用 client，也不依赖 `.tmp` 中的 UI 源码。启动时以 `--wid` 建立原生宿主关系，初始尺寸 1×1 且关闭 boot splash；Shell 清除 Godot 初始 popup owner 后转为子窗口，嵌入完成后再显示和调整尺寸，避免独立窗口与启动灰帧。顶层激活与舞台键盘焦点分离，不对 child 调用 `Window.GrabFocus()`；任务栏恢复已获用户确认修复。
- 已接真实 strict v2 目录包新建/打开、保存/另存为、添加/切换/复制/删除/重排差分、dirty、包与差分设置、资源替换、Note 多选整组拖动/批量调速/步进/删除、单选精确几何/轨道/类型、BPM/Scroll 事件面板、Hold 节点判定和撤销/重做；文件与写包语义继续由 `EditorDocumentSession`/shared 拥有。
- Godot 舞台使用 shared v2 RuntimeAdapter、BPM/Scroll 与游戏几何；支持 WAV/MP3/Ogg/FLAC 预览、播放/seek、Q/E 玩家流速（0.1–2.0）与 W/S 音乐倍率（0.2–2.0）。后两者只影响预览，不修改谱面。同差分编辑走带 revision 的 `update`，保留播放/播放头/选择并复用相同路径音频；切包、切差分或换音频完整 `load`。空草稿仅预览豁免计分 Note 要求，strict 写包不变；根控制器补收全局左键松开并转换到舞台局部坐标以完成放置/路径手势，单条 IPC 命令异常通过 `error` 事件隔离。
- 已有中英文即时切换与统一快捷键入口。放置工具支持七类 Note、真实 hover ghost、精确吸附读数、跨轨导引、对齐标记、宽度记忆及 Hold/Mixer 两段式创建；SELECT 支持当前轨道谱面位置/时间框选（按住可随播放跨屏，Shift 追加，右键/Esc 取消）、拖动、边缘调宽与整体移动，PATH 支持双击插点、拖动头/节点及出向曲线，PAN 支持预览时间与横向视图。偏好与Dock布局持久化、基础复制粘贴已接入，具体窗口证据与多屏/DPI边界见verification；真实波形不列当前目标，正式插件接口首稳版前不做，ZIP/XML/DY 与 Storyboard 尚未接线；合并桌面候选包已实现，正式发布检查尚未完成。
- 播放预览已接共用游戏音符/残影/命中及 Hold/Mixer 持续视觉；seek/播放中编辑静默重建当前状态，普通暂停冻结、继续不重判；滚轮向前导航触发跨线命中/残影，向后静默恢复，暂停滚轮不启动音频。编辑器默认 Full，可通过空白处右键面板调预览音量与特效，不改游戏档位；本轮用户已确认这批制谱器功能整体满意、可收尾；具体自动证据与设备/性能覆盖边界见 verification。
- 编辑态最小HUD保留标题/顶部进度/蓝线，顶部中央新增大播放/暂停按钮；游戏效果预览UI入口按用户决定隐藏，内部能力保留供未来自动录制（未实现）。只读展示组件共用presentation，编辑器Auto统计独立、不写成绩，尚待本轮用户视觉验收。曲线采样点不再画闭合横线；阶跃使用左极限端点减少运行时对象，低端性能未验收；原生Auto回归退出清理失败见verification。
- Windows编辑器已确定当前测试包基线，用户确认暂固定 `windows-x64-test-v1`：双托管裁剪、兼容系统.NET优先/包内兜底、已验证精简模板及便携卸载。当前包身份/hash和剩余覆盖见[测试基线](verification.md#当前-windows-编辑器测试包基线)，不自动继续瘦身，也不等同公开发行批准。
- 保存现行实现：writer规范化目录尾分隔符；SaveAsync在UI线程取快照、后台写入、回UI确认版本，保存期间新编辑保留dirty；文本框保存快捷键有效，已发布资源解除pending外部依赖。资源历史恢复限制见§4。
- 核心入口与保存错误语义见 [制谱器说明](<dyna-maker-uv.md>)。

## 3. 验证与验收现状

详见 [验证状态与操作](<verification.md>)，当前摘要与历史证据索引在那里维护；品牌扫描现已排除本地嵌套 worktree，完整 verify 的最新结果以该页执行记录为准，不沿用旧结果。

- 构建与自动回归、原生对象回归、真实窗口操作、截图目视和用户实机验收分别记录；任何一层通过都不自动证明其他层通过。
- 当前制谱器、HUD、首页、结果前奏与配色的证据及未覆盖范围，以该页对应条目为准，不用旧测试包证明后续功能已出包或已验收。
- 用户已反馈基本通过的多指、短 Hold、对象池及侧轨比例，不应整体重列为未验收；也不能外推为所有设备和边界已通过。剩余专项见下节。

## 4. 仍需处理

### 功能缺口（不是本轮实施授权或排期）

- 资源替换跨保存后的历史Undo/Redo媒体版本恢复仍未完善；原生Auto回归退出清理失败仍待处理。保存已完成的修复见§2.3，不将旧候选缺陷重新列为当前未修。

- 制谱器偏好/Dock布局持久化与基础复制粘贴已接入，覆盖范围见 [制谱器说明](dyna-maker-uv.md) 和当前测试基线，不再笼统写成整批未验收。用户已确认偏好记忆生效；多选精确编辑Inspector用户目前无异议；对象磁吸因用户反馈坐标偏差，已隐藏UI并强制停用（忽略旧开启偏好），保留实验代码但暂缓修复，不列待手感验收；真实波形不再作为当前目标，BPM导入仅保留候选；首个稳定版本前不做插件API，当前保留官方内部扩展。合并桌面候选包已实现，许可证/干净基线/无.NET环境发布验证待补；ZIP/XML/DY仍未实现，持久化设计须兼容最终发行包；Storyboard仍不排期。
- BLEED 专用失败结算页（当前复用普通结算页）；Standard/Hardcore 成绩隔离。v8 血条与顶部 HUD 本体实际效果已获用户确认；后续只做本体微调，不扩大为全部设备/性能验收。
- 结果字标音效与音乐淡出：前奏视觉与分类已接入，音效只保留接入点（`Pause()` 仍是立即停流，不能当淡出用）。字标的视觉验收见 [结果前奏](<result-outcomes.md>)。
- 选曲筛选/排序和搜索清除增强、完整 Music/Hit/UI 声音资产链路。
- 首页在线入口仍未实现：谱面工坊（服务器曲库）、段位模式、联机大厅只有底板与禁用态；CREDITS / LOGIN 置灰且没有页面。已讨论第一阶段账号、免费曲库和本人记录上传，范围与页面设计见 [网络服务设计](network-services-phase1.md)；本轮仅记录设计，未授权实现，CREDITS 不在范围内。
- 主仓库 CI、可复现提交基线、本地设计源备份、生成物保留/清理策略。
- 服务器未接客户端；外层 C++ 骨架不代表正式服务架构。用户不要求沿用 C++，希望支持第三方自部署；第一阶段段位/联机不做，反作弊与管理员脚本扩展留后续。技术栈建议与待决策项统一见网络服务设计，不视为已冻结协议。

### 待决策、暂缓与静态疑点

- Hold 无输入移动路径断触采样、同帧跨 deadline 的批量结算顺序：**静态疑点，临时探针没有成功运行，用户暂缓**。不能写成已复现 Bug，也不能用短 Hold 手感基本通过消除此疑点。
- Mixer 原版有命中率尾判，社区当前没有；是否增加是玩法决策，不是默认补做事项。
- 模式切换与预加载：2026-10-29 已在缓存读取和游玩入口按当前模式的 Preset 检查复用，旧预设会重新加载；非图形验证通过，2026-11-09 用户确认实际模式切换基本通过。与成绩身份缺模式维度分开处理，见 [验证记录](<verification.md>)。
- Internal 当前验证过的流程使用 `--export-debug`，历史 Release 配置探针曾改善低端卡顿。政策 `debuggable=true` 是 APK manifest 约束，不单独证明 .NET 编译/JIT 配置；替代配置需基准和政策核查，见 [发布说明](<releasing.md>)。
- UI 每帧分配/首页核心渲染预算优化（原“批次 D”）用户暂缓，不按旧建议自动实施。
- 首页分区启灯只是设计探索。Storyboard **首版不做、未排期**，仅保留禁用入口；未来 sidecar 不改 v2/Digest，但 writer 落地时必须避免清除 sidecar。原提案在 [归档](<archive/proposals/storyboard-editor-proposal.md>)。

### 后续方向与未覆盖专项

- 当前继续方向：游戏本体视觉微调、制谱器完善；具体新功能仍按后续任务决定，不将未实现清单自动转成排期。2026-11-09 后续已实现顶部4px进度（AP金/FC淡蓝/断连白、移动端点）与三轨判定线预告启灯，尚待本轮最终复验/用户目测，具体规则见 [HUD 调整](gameplay-hud-v8.md)，执行边界见 verification。

- 用户已确认音符配色 v2、首页、结果字标、游戏 HUD v8、音符材质与命中特效，以及最近一批制谱器功能整体通过；不再整体重列为未验收。具体设备覆盖不据此扩大，历史证据保留。
- Mixer 暂停恢复/原异常段落、快速切曲/收起/离页的真实 I/O 和试听体验；多指/取消/失焦端到端输入护栏。
- **低端性能未验收，用户暂缓**：华为平板等低端帧率、密集曲线及 Smooth 路径重建性能，不自动启动专项或宣称达标。

## 5. 工作区与源码入口

- 产品仓库是 [community](<../>)；外层也有独立 Git，不能用外层旧提交判断产品技术栈。
- 当前分支、HEAD 与未提交文件以接手时 `git status --short --branch` 和 `git log -1 --oneline` 为准；不要把历史盘点中的提交、文件数量或旧路径当作当前基线。保留已有未提交功能与文档改动。
- 目录职责、依赖图、场景入口、剩余大类与新功能落点见 [模块地图](<module-map.md>)；完整导航见 [文档索引](<README.md>)。
- 外层 `_rev/`、XAPK、引擎、录像、截图、工具环境，以及 `design/`、`.tmp/`、`builds/` 不作为产品发行内容；本轮不做磁盘清理。

## 6. 构建、运行与验证

### 6.1 非图形验证

从 community 根目录：

```powershell
python -B tools/verify.py
# 已还原依赖时可用：python -B tools/verify.py --no-restore
```

它串起结构门禁、两端构建、两套 C# 回归、发布测试及 diff 检查。分项命令、环境警告处理与历史证据见 [验证说明](<verification.md>)。构建必须 0 error；不能只跑结构检查便宣称全部通过。

### 6.2 启动、截图和录屏

只在用户要求运行或确需视觉验证时操作；先结束残留两个 Mono Godot 进程，再择一启动游戏/编辑器，不用旧 editor 工作台启动参数。
具体命令、受控截图/录屏、开发定位和 headless 限制见 [运行操作](<verification.md#运行截图和录屏>)。不要代替用户宣布视觉或真机验收。

## 7. 资产与安全

- 曲绘由社区谱师随包提供，不使用 AI 生成；公开客户端不内置 AI 曲绘，缺图用 clean-room 占位。AI/曲绘研究生成工具只输出仓库外本地目录，不得向 `client/assets/` 写图。这不限制已批准的原创 Blender 评级管线。
- Public `com.dynamiteuniverse.game` / Internal `com.dynamiteuniverse.game.internaltest`；签名凭据仅放忽略的 Godot credentials 或环境变量，历史暴露的 debug keystore 绝不能用于产品签名。
- [发布流程](<releasing.md>)、[资产来源](<../release/asset-provenance.json>)、[第三方通知](<../THIRD_PARTY_NOTICES.md>) 是发布必读；原版研究/历史报告不授权分发素材或回退现有规则。
