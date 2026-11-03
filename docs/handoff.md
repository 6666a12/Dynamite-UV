# Dynamite Universe 当前交接

> 更新：2026-10-26。本文面向**当前工作区**，不是仅描述 Git HEAD；本次以源码、项目引用与现有记录核对状态，未改产品代码。
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
| 玩法 | 三轨鼠标/多点触摸、Hold/Mixer、Mirror/Auto、Standard/Hardcore、BLEED | Auto 全 Perfect 且不写成绩；BLEED 已有真实扣血（100 血，Miss −2/−4、Perfect +0.2/+0.1、Great/Good +0.05/不回血），耗尽当帧暂停并立即进入 STAGE FAILURE 字标前奏，仍复用普通结算页，无血条 UI |
| 设置/校准 | 四 tab 设置与持久化、偏移/落速/特效/音量；120BPM 合成节拍试打、统计与 APPLY | 校准原流程有 Android 用户验收；完整 Hit/UI 声音资产链路未完成 |
| 结算 v2 | 不透明双屏风、Ω/Ψ/X/S/A/B/C/D 八评级、AP/FC、判定分布、纪录增量和分档动效 | 正常完成已改为谱末 +2 秒与音频自然结束**两者取晚**（分别单调锁存）；屏风前新增七类字标前奏（普通完成为 TRACK COMPLETE），三档动效与跳过已接入，见 [结果前奏](<result-outcomes.md>)。主代理窗口检查已完成，用户最终视觉验收与音效仍待处理 |
| 音符配色 v2 | 六类真实样张、调色与可读性观察双状态；20 步撤销/重做、十槽库、草稿/显式应用 | 不改玩法、不污染游玩全局预览；对比/色差只提醒、不钳色、不代表可读性认证；视觉/触控仍待验收，见 [专题](<note-colors-v2.md>) |

当前性能实现包括轨道窗口扫描、路径斜率缓存、Note/连接体/特效池、音频路径缓存、谱面预加载 LRU×4、详情/试听异步及成绩延后保存。**已经做过这些优化不等于低端帧率达标**；不按旧审查重新实施同一修复。

### 2.2 谱面、判定与存档的重要边界

完整规则以 [玩法规格](<gameplay-spec.md>)、[v2 合同](<chart-format-v2.md>) 与 shared 为准；以下是接手时不能误改的边界：

- 显式 v2 严格解码/跨文件校验/Digest，失败拒绝；普通 legacy 优先转 v2，不适用才保留 legacy-direct。开发/Internal 扫 `res://testdata/packs` 再扫 `user://charts`，同 id 用户包覆盖；Public/未分类导出只扫用户包。
- **res:// 必须走 Godot 虚拟文件系统，不可引入 System.IO**：导出后原资源可能在 PCK/导入 remap 中。此分支 Digest 按空音频字节计算；user:// /绝对路径的真实文件分支含音频字节，两者不可混同。见 [加载适配](<../client/scripts/game/charts/V2Integration.cs>)。
- legacy `Position` 是左缘；v2 是 `center + width`。画布预览中的 Center 仍沿用左缘语义，提交文档命令时才转换；精确 BarTime 不经显示秒值往返。
- v2 父 Note 可用 `scrollSpeedMultiplier` 在全局 Scroll 与玩家落速之上逐 Note 调整视觉倍率，缺省为 1；Hold/Mixer 全路径共用父倍率，命中秒与判定不变，非默认值进入 Digest。Scroll/路径曲线已扩至 Quad、Cubic、Sine 常用族，`smooth` 仍为路径专用 PCHIP。
- 客户端直接播放 WAV/MP3/Ogg Vorbis；原生 FLAC 由 shared 托管解码为 16-bit PCM 后包装为 WAV 流。编辑器默认写包 Probe 支持 WAV/MP3/FLAC/Ogg Vorbis 的结构与时长校验；Opus/M4A/AAC 明确拒绝。
- 普通判定按固定 150BPM 换算；Standard 依难度选预设，Hardcore 使用 Hard 窗 ×0.5；Hold 首次断触冻结宽限 deadline，Mixer 则为实际音乐时间的 1/8 chart bar 宽限。当前 Mixer 无额外尾判。
- 输入批次锁是精确 double 同刻锁，任何普通输入结算均可武装，Mine 不检查也不武装；触点可复用、跨轨投影可重叠。未知 id 的 Move 忽略，失焦清触点。短 Hold 保留头的晚判窗口，以带时间戳的联系快照处理同帧短点/换指。
- 每帧音频时钟、视图、输入、持续状态、sweep、Mixer 收尾、HUD、结算的顺序不可重排；音频位置单次播放单调过滤，暂停/恢复下限保留。详见 [运行时架构](<runtime-architecture.md>)。
- 分数归一化 1,000,000；HUD ACC 分母为已判理论满分，结算 CLEAR 分母为全谱理论满分。未舍入评级：Ω=100，Ψ≥99，X≥97，S≥95，A≥90，B≥80，C≥60，否则 D；Ψ 的资源/存档键为 Y。
- 成绩写 `user://scores.json`。v2/成功转换路径用 `(packId, chartId, rulesetId, gameplayDigest)`，legacy-direct 保留 `packId:diff`；**无独立 Standard/Hardcore 模式维度，不能称已完整隔离**。部分 legacy 转换可能改变摘要，不等于模式隔离设计完成。
- Auto 不写成绩；Health/Boost 虽有内部计算，无对应 HUD/GameOver/Buff/EX Boost。原版研究数学不驱动社区 HUD/存档。
- 校准建议偏移 = 当前值 − 样本中位数，5ms 取整、±300 钳制；校准时钟不含 userOffset。手写循环 WAV 保留 loop_end 后 1 个静音保护帧，避免 Android 混音边界越界。

### 2.3 制谱器

- 2026-11-02 正式路线改为 `editor/shell` 的 Avalonia 12 外壳（Dock.Avalonia + Ursa + Suki Motion），通过 Win32 HWND 嵌入现有 Godot 4.7.1 Mono 项目；不引用 client，也不依赖 `.tmp` 中的 UI 源码。
- 已接真实 strict v2 目录包新建/打开、保存/另存为、添加与切换差分、dirty、Note 选择、逐 Note 差速命令、删除、撤销/重做；文件与写包语义继续由 `EditorDocumentSession`/shared 拥有。
- Godot 舞台使用 shared v2 RuntimeAdapter、BPM/Scroll 与游戏几何；支持 WAV/MP3/Ogg/FLAC 预览、播放/seek、Q/E 玩家流速（0.1–2.0）与 W/S 音乐倍率（0.2–2.0）。后两者只影响预览，不修改谱面。空草稿仅预览豁免计分 Note 要求，strict 写包不变；根控制器补收全局左键松开以完成放置/路径手势。
- 已有中英文即时切换与统一快捷键入口。放置工具支持七类 Note、三轨、点击放置与拖动加宽；路径工具支持双击插点、拖动头/节点及出向曲线。普通 Note 拖动/调宽、平移手势、真实波形分析、布局持久化、正式插件接口、ZIP/XML/DY 与 Storyboard 尚未接线；合并桌面导出包尚未实现。
- 核心入口与保存错误语义见 [制谱器说明](<dyna-maker-uv.md>)。

## 3. 验证与验收现状

详见 [验证状态与操作](<verification.md>)，只在该处维护最近执行记录和测试包身份。

- 2026-10-29：首页换用 Penpot「Game — Home v2 · 01 Compact」布局（1920×1080 固定坐标）与 Mk VI 核心：`detonation-orrery-mk6.glb`（3756 三角面、14 材质、单个 4 秒 clip）替代 Mk II，近/远双层 canvas 弥散光默认 `u_glow=1.6` 且开启，Environment glow 关闭；新增四个动作底板，段位模式/联机大厅与既有谱面工坊同为禁用占位，顶部 CREDITS/LOGIN 置灰无导航。`verify.py --no-restore` 与真实客户端 Full/Reduced/Off 窗口截图见验证记录。
- 2026-10-28：正常结束改为双锁存取晚，七类结果字标前奏（普通完成 TRACK COMPLETE、Full/Reduced/Off、跳过与防穿透）接入客户端；新增 core-tests `[22]`–`[24]` 并在统一验证中通过。主代理已完成 Godot 导入及 Full/Reduced/Off AP、BLEED Failure、普通完成窗口验收；公开导出、Android 与用户最终视觉验收仍待处理，详见验证记录。
- 2026-10-27：结果前奏交接文档及 4 份相关文档链接检查完成，候选文件存在；未实施取晚/前奏或重跑 C# 验证，详见验证记录。
- 2026-10-25：历史统一验证通过；两端构建/核心与编辑回归/37 项发布测试，以及分阶段 headless 状态等价与 Windows 程序集发布已有记录。没有对应新 APK、完整 Godot 桌面包或可见窗口验收。
- 2026-10-17 用户反馈：多指、短 Hold 手感、对象池修复、侧轨 `691/790` 基本通过；保留现值，不外推到所有设备/边界。Mixer 初步反馈正常，专项仍未穷尽。
- 2026-10-20 配色 v2 与之后的拆分不能用 2026-10-14 测试 APK 证明已出包或已验收。

## 4. 仍需处理

### 功能缺口（不是本轮实施授权或排期）

- 制谱器普通 Note 拖动/调宽、Pan 手势、真实波形分析、布局持久化、正式插件接口、合并桌面导出、ZIP/XML/DY 和 Storyboard 仍未实现；新建/差分、放置和路径手势已接入。
- BLEED 专用失败结算页（当前复用普通结算页）与 Bleed 血条 UI；Standard/Hardcore 成绩隔离。
- 结果字标音效与音乐淡出：前奏视觉与分类已接入，音效只保留接入点（`Pause()` 仍是立即停流，不能当淡出用）。字标的视觉验收见 [结果前奏](<result-outcomes.md>)。
- 选曲筛选/排序和搜索清除增强、完整 Music/Hit/UI 声音资产链路。
- 首页三个禁用入口没有后端：谱面工坊（服务器曲库）、段位模式、联机大厅只有底板与禁用态；CREDITS / LOGIN 置灰且没有页面。首页没有新增这些功能的授权。
- 主仓库 CI、可复现提交基线、本地设计源备份、生成物保留/清理策略。
- 服务器未接客户端；外层 C++ 骨架不代表正式服务架构。

### 待决策、暂缓与静态疑点

- Hold 无输入移动路径断触采样、同帧跨 deadline 的批量结算顺序：**静态疑点，临时探针没有成功运行，用户暂缓**。不能写成已复现 Bug，也不能用短 Hold 手感基本通过消除此疑点。
- Mixer 原版有命中率尾判，社区当前没有；是否增加是玩法决策，不是默认补做事项。
- 模式切换与预加载：2026-10-29 已在缓存读取和游玩入口按当前模式的 Preset 检查复用，旧预设会重新加载；非图形验证通过，真实 UI 切换仍待回归。与成绩身份缺模式维度分开处理，见 [验证记录](<verification.md>)。
- Internal 当前验证过的流程使用 `--export-debug`，历史 Release 配置探针曾改善低端卡顿。政策 `debuggable=true` 是 APK manifest 约束，不单独证明 .NET 编译/JIT 配置；替代配置需基准和政策核查，见 [发布说明](<releasing.md>)。
- UI 每帧分配/首页核心渲染预算优化（原“批次 D”）用户暂缓，不按旧建议自动实施。
- 首页分区启灯只是设计探索。Storyboard **首版不做、未排期**，仅保留禁用入口；未来 sidecar 不改 v2/Digest，但 writer 落地时必须避免清除 sidecar。原提案在 [归档](<archive/proposals/storyboard-editor-proposal.md>)。

### 待设备/视觉专项

- 配色 v2 的触控/软键盘/保存、Full/Reduced/Off、最终可读性；Penpot 样张仍有旧方块尺寸，不能冒充客户端真实条幅。
- 结果字标前奏的用户最终视觉与设备验收：主代理已检查 80% 尺寸/居中、斜纹区、0.52s 提亮、屏风衔接、三档动效及普通完成 TRACK COMPLETE；跳过与 120ms 防穿透仍只有纯逻辑回归。
- 首页 v2 的用户视觉验收与 Android 表现：主代理窗口检查已覆盖 1920×1080 布局、1.6 弥散、动作区禁用态与 Full/Reduced/Off，但在 Forward Mobile 下 canvas 弥散比独立预览（gl_compatibility）更收敛，只在亮棱/内嵌线附近形成细环；是否加压需要用户判断，不能拿桌面窗口截图当 Android 或最终视觉验收。
- 新材质、侧轨旋转、Mixer 徽章、默认 130% 亮度与 0.30s 残影。
- Mixer 暂停恢复/原异常段落/密集曲线性能、快速切曲/收起/离页的真实 I/O 和试听体验。
- 华为平板等低端帧率；密集 Smooth 路径画布重建成本；多指/取消/失焦端到端输入护栏。

## 5. 工作区与源码入口

- 产品仓库是 [community](<../>)；外层也有独立 Git，不能用外层旧提交判断产品技术栈。
- 2026-10-26 核对时产品 HEAD 为 `fc65e21`（2026-10-14）；当前工作区另有未提交功能与拆分改动，含旧位置删除/新位置未跟踪。状态数量随操作变化，**接手时重新运行 git status**，不据旧盘点推断。
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
