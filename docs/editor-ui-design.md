# UI 设计稿与施工约定

> 2026-10-26 校正文档入口/归档引用；各板与决策的具体日期仍保留在正文。本文记录设计稿位置、采用方案、目标与施工规则，**不是功能完成或实机验收清单**；本次未读取/修改 Penpot 设计稿。
> 当前编辑器能力见 [DynaMaker UV](<dyna-maker-uv.md>)；格式权威见 [v2 合同](<chart-format-v2.md>)，游戏动效权威见 [ui-motion.md](<ui-motion.md>)。
> 下列 Editor UI 板与 §4 是设计目标，包含已接入的 Settings、曲线/judge、路径投影与
> 双击路径节点插入，以及**尚未接入**的完整工具与试玩反馈；不可据画板认定已实现。
> 旧版比较过程、已解决项与阶段状态已存 [原文快照](<archive/2026-10-07-cleanup/editor-ui-design.before.md>)。

## 1. 设计稿在哪

- Penpot 网页端，文件 **「Dynamaker UV UI design」**（账号 Natsume K）。
- 接入方式：Penpot MCP（streamable HTTP），URL 带 userToken。**历史本机连接记录（2026-10-17，本次未复验）**：当时 DSH Desktop 的 `web` profile 在
  `%APPDATA%/dsh-desktop/harness/profiles/web/cordis.patch.yml` 插入
  `@deepseek-ai/dsh-mcp-client`，`serverName: penpot`、`transport: streamable-http`、
  `toolCallTimeoutMs: 180000`，请求头为 `User-Agent: DSH-Penpot/1.0`。
  当时复用本机 Kimi 连接，备份保存在同一用户配置目录；当时验证过工具发现、原生 API
  文档读取及一次只读文件/页面识别，**没有修改设计稿**。标签页休眠会阻止画布读取/写入，
  使用时须打开该文件、启动 Penpot MCP Plugin 并保持标签页前台（见 §5）。
- 本机既有凭据来源为 `~/.kimi-code/mcp.json` 的 `mcpServers.penpot.url`。也可配置于
  Codex 的 `~/.codex/config.toml` → `mcp_servers.penpot`，使用 120s 启动超时、180s
  工具超时与 `User-Agent: Codex-Penpot/1.0`；不要据历史配置记录推定当前仍有该条目。
  `.tmp/penpot_mcp.py` 优先读取已启用的 Codex 配置，缺少时回退 Kimi。
  **token 是密钥，不进仓库，不打印完整 URL。**
- 驱动脚本（工作区 `.tmp/`，不入库，丢了可按本文 §5 重写）：
  - `.tmp/penpot_mcp.py` — JSON-RPC 客户端；必须带 `User-Agent` 头，否则 403。
  - `.tmp/penpot_build2.py` — `RESET`（按名字 reconcile 板子）+ SVG 助手。
  - `.tmp/penpot_build4.py` — Editor UI 页全部板子的构建代码；`python penpot_build4.py [步骤名…]`
    可全量或部分重建，末尾自动两阶段对齐（§5）并导出 PNG 到 `.tmp/pp4_*.png`。
  - `.tmp/penpot_build5.py` — Game UI 页第一轮（设置两 tab + 四块过场），带页面闸门
    `ENSURE_PAGE` + `RESET2`。
  - `.tmp/penpot_build6.py` — Game UI 页第二轮（首页/选曲/游玩/暂停/结算/Display + 两块设置板
    修正），沿用 build5 的闸门与助手，导出 PNG 到 `.tmp/pp6_*.png`；
    `python penpot_build6.py [板 key…]` 可只重建部分板（不列出的板原样保留）。
  - `.tmp/penpot_build7.py` — Game UI 页第三轮（选曲 v2 两板），复用 build6 的 RESET/助手/板构造器
    并只在 defs 表里追加两块新板（`assert` 锚点防止 build6 改动后静默失配），
    导出 PNG 到 `.tmp/pp7_select_v2*.png`；`python penpot_build7.py [板 key…]`
    默认只重建这两块新板，也可用 build6 的 key 重建任意既有板。

## 2. 板清单（画布坐标 / 尺寸）

| 板 | 坐标 | 尺寸 | 内容 |
| --- | --- | --- | --- |
| Editor — Main | 0,0 | 1920×1080 | 主编辑态：裸舞台（复用实机 stage）+ 四角文字 HUD |
| Editor — Context Menu | 2120,0 | 1920×1080 | 右键菜单（普通 Note 选择态） |
| Editor — Path Edit | 4240,1280 | 1920×1080 | Hold/Mixer 路径编辑态 + 双击插入手势 |
| Editor — Node Menu | 6360,0 | 1920×1080 | 右键菜单（节点选中态） |
| Start | 4240,0 | 1920×1080 | 起始页 |
| Pack Select | 0,1280 | 1920×1080 | 谱面包选择 + 差分列表 + 底部 STORYBOARD 禁用占位入口（OPEN IN EDITOR 左侧） |
| Create Project | 2120,1280 | 1920×1080 | 新建工程（含自定义难度创建态） |
| Widget — Level States | 6360,1280 | 1100×660 | 等级输入五状态组件 |
| Editor — Settings | 8480,0 | 1920×1080 | 设置页（GENERAL/EDITOR/AUDIO 三组） |
| Editor — Placing | 0,2560 | 1920×1080 | 放置手势状态：幽灵 Note、导引十字线+线上数值、宽度拖拽、Hold 拖尾预览、对齐提示方块 |
| Editor — Workbench Research · 01 | 0,3840 | 1920×1080 | 历史研究：密度/历史导航、三轨舞台、同纵轴属性轨与选择驱动 Inspector；非施工定稿 |
| Editor — Single Lane Workbench · 03 | 2120,3840 | 1920×1080 | 新版研究：一次一个当前轨道编辑画布 + Left/Center/Right 切换 + 当前轨道时间轴 + Inspector；非施工定稿 |
| Editor — Full Chart Preview · 03 | 4240,3840 | 1920×1080 | 与单轨编辑页同播放头的整谱只读演示；无选择、画布编辑或 Inspector；非施工定稿 |

以上板都在 **Page「Editor UI」**。`Editor — Workbench Research · 01` 是 2026-10-25 参考
RPE Next 后的历史工作区探索，不替代当前 Main/Placing，也不是 Storyboard 设计定稿。它保留三轨舞台，
用 PATH/BPM/SCROLL 验证同一纵向 BarTime 播放头和属性轨，补充密度导航、0/1/N 选择驱动 Inspector、
选区循环预览与单次命令提交；这些属性轨不作为后续既定布局。

同日的 `Single Lane Workbench · 03` / `Full Chart Preview · 03` 是重新核对 RPE 实现后的成对参考。
编辑态只有**一个当前轨道画布**，通过 Left/Center/Right 或前后切换更换当前轨道，底部时间轴也只显示
当前轨道；不能把 RPE 的判定线缩略切换器理解成多个编辑舞台同屏。RPE 对应实现是 session 的单一
`lineIndex` / `selectLine()`，预览再由 `allLines` 决定合成全部判定线。BPM、流速及判定线 RGBA 收在右侧
Inspector，不设常驻事件轨。整谱合成是独立只读演示状态，只用于查看三轨整体效果，不能编辑。
两板仍是视觉与交互研究，不代表这些页面或状态切换已经实现；本轮不展开 Storyboard 工作区。

**Page「Game UI」** 保留基础十四板及后续资产/结算/首页概念板。尺寸以 1920×1080 为主，x 间距 2120。
基础板中的旧 Song Select 和 Result 仅作历史对照；当前分别采用 Song Select v2 与 Result v2。
下面表格用于定位，不意味着每一块参考板都是当前 UI。

| 板 | 坐标 | 尺寸 | 内容 |
| --- | --- | --- | --- |
| Game — Settings (Gameplay) | 0,0 | 1920×1080 | GAMEPLAY tab 四行：JUDGE OFFSET / DROP SPEED 大步进器、JUDGE TEXT 大开关、MODE 二段器（原 HIT EFFECTS 大开关已由 EFFECTS 板的三段器取代） |
| Game — Settings (Audio) | 2120,0 | 1920×1080 | AUDIO tab：三路音量粗滑条 + 底部 RESET DEFAULTS 行 |
| Game — Signal Lock | 4240,0 | 1920×1080 | 过场：扫描线定格帧（斜线底纹 + 三分屏切角分片 + 扫描线 + 左下信号标记） |
| Game — Track Handoff | 6360,0 | 1920×1080 | 过场「充电」：封面居中 + 四角斜对角电缆（25%），每缆 2 个亮脉冲；状态 `PLAYFIELD / LOCKING` |
| Game — Handoff Lock | 8480,0 | 1920×1080 | 过场「充满锁定」：电缆 100%、四节点点亮、封面描边 100% + 内侧 8% 辉光；无判定线 |
| Game — Handoff Discharge | 10600,0 | 1920×1080 | 过场「放电」：电缆 12% 余辉、四节点小爆闪、封面白热闪光 + 双圈冲击波、判定线 25% |
| Game — Home | 0,1280 | 1920×1080 | 首页（`Main.cs`）：左列 eyebrow / DYNAMITE UNIVERSE 两行 wordmark / pink 短轨 / 副题，切角 SELECT DESTINATION 面板内 1 大 2 小切角按钮（游玩实心青、谱面工坊禁用、设置描边）；右侧 3D 能量核心（`HomeEnergyReactor` 示意）；右下版本串 |
| Game — Song Select | 2120,1280 | 1920×1080 | 选曲（`SongSelect.cs`）：顶栏 logo + 返回；左详情板（封面占位 + NO COVER、曲名 / 曲师·谱师 / 时长·Note）；右曲库板（列表头 + 6 行 SongRow + 视口裁切的第 7 行残片）；底部难度实心键 + BEST/CLEAR 条 + 等级字 + START |
| Game — Gameplay | 4240,1280 | 1920×1080 | 游玩 HUD 定格帧：程序化背景纵深、∪ 形判定线（y=861 + 两侧竖轨）、Mixer 装饰粉条与光标、三轨 Note（Tap/Drag/ExTap/Hold+body/Mixer/BarLine）、顶部六 pill + 暂停钮、判定线进度段、左下曲目信息板、右下分数、线下 Combo、线上判定字 |
| Game — Pause | 6360,1280 | 1920×1080 | 暂停浮层：同一游玩帧 + 全屏压暗 72% + 700×480 切角面板（PAUSED + 继续 / 重开 / 返回选曲） |
| Game — Result | 8480,1280 | 1920×1080 | 结算：封面占位全屏压暗背景 + 1160×600 切角面板（RESULT 行、大评级字、NEW RECORD chip、百万分、CLEAR / MAX COMBO 行、四段判定分布条、P/GR/GD/M 四格、三个动作键） |
| Game — Settings (Display) | 10600,1280 | 1920×1080 | DISPLAY tab：单行 UI MOTION 三段器（FULL/REDUCED/OFF）+ RESET DEFAULTS |
| Game — Song Select v2 | 0,2560 | 1920×1080 | 选曲改版（收起态）：左列改 QUICK SETTINGS（MODE 两段互斥 STANDARD/HARDCORE + BLEED / MIRROR 两行等大开关 + 全部设置入口）、右列曲库列表 + 底部操作条 |
| Game — Song Select v2 Expanded | 2120,2560 | 1920×1080 | 选曲改版（展开态）：选中行就地长成 1280×392 详情卡（288 封面 / 曲名 / 曲师 / 本差分谱师 / 预览播放控件），下方行整体下推；底部操作条与收起态逐像素一致 |
| Game — Calibration · 01 Live | 0,7680 | 1920×1080 | 校准向导（采样态）：左列相位块（无面板）+ 中央单轨试打台 + 右列信号分析板（目标样本步进器 / 中位数 / 离散度 / 采样进度条 / 建议偏移）+ 底部偏差条带 |
| Game — Calibration · 02 Result | 2120,7680 | 1920×1080 | 校准向导（结束态）：轨道冻结压暗 + 右列确认面板（建议值 / 应用 / 再测 / 放弃）+ 条带定格 + MEDIAN 标记线 |
| Game — Settings (Effects) | 0,8960 | 1920×1080 | EFFECTS tab 四行（HIT EFFECTS 三段器 / NOTE BRIGHTNESS / FLASH INTENSITY / HIT RESIDUAL）+ 页脚右侧「音符配色 ▶」入口（与 RESET DEFAULTS 同一行） |
| Game — Note Colors · 01 Default | 2120,8960 | 1920×1080 | 独立配色页默认态：左 LIVE PREVIEW（样张区仍是旧 1:1 24px + 2× 48px 方块，实现已改为 320×24 条幅，待重画，见下文「预览尺寸」；Hold 链 / 判定线残影 / TEST HIT）+ 右 PALETTE（BURST 两段器 + 六类色行 + UNDO / RESET SLOT）+ 底部 10 槽预设栏（槽 1 DEFAULT，其余 + NEW PRESET） |
| Game — Note Colors · 02 Edit | 4240,8960 | 1920×1080 | 编辑展开态：点色行就地长成通栏编辑卡（顶部六类切换 / 640×340 SV 面 + 色相条 + HEX + RGB / 右侧对比度·ΔE·STATUS 三读数 / 1×·2× 实时样张（旧板是 24px·48px 方块，同上待重画）+ TEST HIT / UNDO·RESET COLOR·DONE），预设栏保持可见 |
| Game — Note Colors · 03 Warnings | 6360,8960 | 1920×1080 | 警告态：允许任意颜色但出提醒——低对比度行内 chip、EX 与 TAP 色差过近 chip、顶部「3 处可读性提醒」、预设槽警告点；BURST 切到 NOTE |
| Game — Note Colors · 04 Empty Slots | 8480,8960 | 1920×1080 | 空槽态：槽 1 之外 9 个虚线空槽（+ NEW PRESET）、槽 1 动作菜单（RENAME / RESET TO DEFAULT / CLEAR SLOT）、底部空槽提示 |
| Game — Note Colors · 05 Rename | 10600,8960 | 1920×1080 | 命名对话框（模态）：列表压暗 + 560×250 切角面板（标题 / 单行输入 + 光标 / `4 / 12` 计数 / 规则提示 / OK·CANCEL）；点空槽先命名再落槽，已有槽点 RENAME 复用同一框 |

实现已新增第 4 个 **EFFECTS** tab（HIT EFFECTS 三段器 / NOTE BRIGHTNESS / FLASH INTENSITY /
HIT RESIDUAL），并为它补了 Penpot 板 `Game — Settings (Effects)`；入口按「页脚动作」放在
RESET DEFAULTS 同一行右侧。

### Note Colors v2（2026-10-20 当前采用）

上表 Note Colors 01–05 是旧版设计记录，不再作为施工目标；2026-10-20 实际连接的 Game UI 页未发现这五板。
当前采用用户确认的 v2 R2 两板：

| 当前板 | 坐标 / 尺寸 | ID |
| --- | --- | --- |
| Game — Note Colors v2 · 01 Hold / Draft | 0,10240 / 1920×1080 | `6d28ecc0-5fb3-80fe-8008-d18b991e5244` |
| Game — Note Colors v2 · 02 Readability / Hold | 2120,10240 / 1920×1080 | `6d28ecc0-5fb3-80fe-8008-d190c69ad51e` |

已接入独立路由 `scenes/note_colors.tscn`；同一场景内两个全屏状态共用草稿，**不是两个重载场景**。
实现与验收说明见 [音符配色 v2](<note-colors-v2.md>)。本轮只构建/自动回归，没有启动游戏或导出 APK；不得将 Penpot 导出视为实机截图。

- 主态左侧 1120px 为六类错落陈列，**无游玩舞台、轨道或判定线**；右侧 672px 固定调色台，间距 32px。
  六类选择器、SV、48px 热区/24px 色带的 Hue、HEX/RGB、原色/新色、会话内最近颜色同屏。
- 顶部预设选择器打开模态库；十槽容量保留，只显示占用槽和一个新建入口。槽 1 可改色/重命名、不可清空。
  新建复制当前配色；12 位 ASCII 名称拒绝非法/重名，取消不创建；重置/清空需要确认。
- **应用才保存**：颜色、命中特效跟随、预设库、当前槽统一纳入 `NotePaletteDraft`。20 步 undo/redo，单次拖拽一条历史；
  原方案固定为进入页面时的快照。按住比较不进入历史，松开/失焦/切页恢复草稿；主态离开或切换预设时提供应用/放弃/取消。
- “另存为预设”只修改草稿，并不绕过应用。保存失败保留草稿与原内存状态；设置先写同目录临时文件再替换，避免直接截断旧存档。
- 观察态左侧为三个背景上的同一音符；右侧按 CIE76 色差选择最近类型并可手动更换。对比度只算基础色与背景，
  `#05070F` 是现有游戏深色基准，`#18212D` / `#35424F` 是测试色而非实机采样。不以阈值宣称材质/色觉可读性通过。

#### 预览材质与几何

`NotePaletteSpecimen` 使用真实 `NoteView` / `note_material.gdshader`，不把 Penpot SVG 烘进游戏，不写 `NoteVisualSpec.Active`。

- 主陈列条宽按类型为 260/290/240/320/260/220；非 Hold 高 24、Hold 高 26。Hold 尾宽 260、头尾间距 190。
- 观察卡三份完全同尺寸：头宽 272，Hold 尾宽 240、间距 172。比较区同宽 272；类型选择器小图标用整节点缩放 .58，
  不能单改高度使 Mine X、Mixer 徽章、EX 端标比例失真。
- Hold 使用 `Polygon2D`（颜色 alpha=.28）和 3px 闭合 `Line2D`（alpha=.9），半宽=条宽×.421。
  样张根层级 Z=1，条身/描边局部 Z=0、头尾局部 Z=1，不能用负绝对 Z 把条身画到不透明背景后。
- `NoteView.Reuse` 会归零位置/缩放，样张复用后必须恢复头尾位置；外层缩放由样张节点承担。
- 测试命中调用所选类型的真实 Bloom/Particles/残影路径，遵守 HIT EFFECTS、亮度、爆发尺度和残影设置；
  Hold/Mixer 展示接触亮度和一次爆发，不把本页视为完整持续判定模拟。
- 初始选中 Hold，但仍使用玩家已存颜色；设计示例 `#7BECC5` **不覆盖出厂色**。
- 预设/命名/离开确认/RGB 为同风格补齐的实装弹窗，当前两张 Penpot 板未单独画这些状态。

### 当前方案与历史板位

| 板组 | 位置 | 当前地位 |
| --- | --- | --- |
| Grade Directions v4 / Grade Lighting v4 | x=4240/6360，y=2560 | 三方向与薄光历史比较，不是当前评级来源 |
| Grade Lighting v5 | x=8480，y=2560 | 获选炫光 S 基底参考 |
| Grade Surface Current v5 | x=10600，y=2560 | 当前表面电流方向参考 |
| Grade Set v5 Current | x=0，y=3840 | 当前 Ω/S/A/B/C 五级资产总览，已接入结算 v2 |
| Result v2 · 01…06 | x=0/2120/4240/6360/8480/10600，y=5120 | 当前屏风结算六板；已接入、待实机验收 |
| Home Power-On 01 Dark / 02 Zone Key / 03 Zone Fill | y=6400 | 分区布光探索，**客户端未接入** |

当前评级使用自绘双刃方向的 v5 + 表面电流；源与管线见 [Blender 管线](<blender-pipeline.md>)。
不恢复 v4 薄光或未采用的 v6 试验，不把旧板上的“未接入”当成现在的结算状态。

Result v2 六板后缀为 `01 Shutter Close 300ms`、`02 Shutter Hold 800ms`、`03 Shutter Open 1250ms`、
`04 Grade First 1750ms`、`05 Settled`、`06 All Perfect`。600ms 合拢、320ms 停顿、480ms 拉开，
评级先入场、信息后浮现，总约 2650ms。几何、资源和真实触发边界见 [结算 v2](<result-v2-design.md>)。
恢复源在 [design/result-v2/source](<../design/result-v2/source/>)；不再沿用旧电流回收转场。

首页仍保留已经实现的 3D `HomeEnergyReactor`。新概念改为电影式分区启亮：待机核心 → 主光/填充 →
粉色轮廓、底部反弹与灯舱；不是横向扫光，不代表客户端已经改。脚本 `.tmp/penpot_home_power.py`。

### 2.1 当前选曲 v2

**模式关系**：MODE 只有 **STANDARD / HARDCORE 两档，互斥二选一**；
**BLEED 是游玩修饰开关，不是平级档位**——STANDARD 下可自由开/关，**HARDCORE 下强制开启且不可关**
（锁定态：轨道保持 cyan 但整体压到 55%、方块停在右侧 60%、左侧空位放 20px 挂锁图标，副文案换成
`HARDCORE 下强制开启`）。后续的「段位模式」= STANDARD + BLEED，本轮不为其出控件。

左列 (40,112,480×828) 采用 **QUICK SETTINGS**：

| 元素 | 几何 |
| --- | --- |
| 标题行 | `QUICK SETTINGS` 14px dim + 分隔线 y=176 |
| MODE 组 | 行标签 26px cy=232 + 当前档位说明 15px cy=264（`Normal 判定窗口` / `HARD · 硬核判定（窗口 ×0.5）`，后者是客户端 `GameplayModeTag` 原文）；两段列表 408×72、切角 12、间距 8，y=300 / 380，选中 = `#18244A` 填充 + cyan 2px 描边 + 6×44 定位条 + 图标与名称转 cyan |
| 分隔线 | y=480（MODE 组下方留 28px） |
| BLEED / MIRROR / AUTO 三行 | **同一个小规格**：图标 20px + 标签 20px + 副文案 13px + 开关 108×50（方块 40、切角 14/10）；行心 cy=540 / 620 / 700，共用一条 **80px 等距节奏**；副文案依次是 `可自由开关的游玩修饰`（HARDCORE 下换 `HARDCORE 下强制开启`）/ `谱面左右镜像` / `自动演示 · 不写成绩`。板上 AUTO 画**关** |
| 分隔线 + 入口 | y=772（AUTO 行下留 47px，仍是以后加内容的位置）；「全部设置 / ALL SETTINGS」408×80 描边切角键 (76,824) + 滑杆图标 |

**AUTO 已接入**：选曲开关直接读写 `GameSettings.AutoEnabled`（默认 false），游戏本体无 F1 切换。
HUD/结算明确标“不计成绩”；F1 仍可能是独立编辑器的预览快捷键，不能把两项目混为一处。

**曲库表头 + 搜索 + 筛选占位（收起/展开两态完全一致）**：

| 元素 | 几何 |
| --- | --- |
| 标题 | `SONG LIBRARY · 20 CHARTS` 20px dim，左上角锚点 (590,132)（客户端 `SongSelect.cs` 常量） |
| 筛选占位 | (1470,120,56×52,cut 14)：**只有描边**（`#2A3560` 2px）不填色，整块（含图标）压到 **55%** 表示"预留、未启用"；内放 22px 漏斗图标居中；右缘 1526，与搜索框留 8px |
| 搜索框 | (1534,120,336×52,cut 14)：`#2A3560` 2px 描边 + `#0F1429` 85% 填充；**右缘 1870 = 列表行板右缘**，垂直中心 146 与表头文字对齐（表头带只有 112..180，所以上下各留 8px） |
| 图标 + 占位 | 放大镜 22px @ (1558,135)；占位文本 `搜索谱面…` 18px dim，左缘 1594，垂直居中 146 |

搜索的作用域与行为（基础过滤已接入，板上只画默认态）：过滤**右侧曲库列表**（曲名 / 曲师 / 谱师匹配），
输入即过滤、无结果时列表区出空态；搜索框与表头文字互不遮挡（标题宽约 264px，搜索框左缘 1534）。
聚焦态、输入中（光标/清除按钮）、无结果态都记为变体，见下方状态变体表。

筛选/排序按钮**当前只放占位**：位置、尺寸、图标已定（见上表），具体筛选项（难度 / 通关状态 / 排序
依据）与点击后的面板形态用户尚未确定，所以按 55% 压暗当"未启用"处理，交互待拍板。

**展开卡（封面导向）**：右列选中行就地展开成 1280×392 详情卡（≈3.3 行，行高 96 + 间距 22 的步距
118），下方行整体下推 414（卡高 + 间距）。卡片内容只剩封面与曲目信息：

| 元素 | 几何 |
| --- | --- |
| 卡体 | (590,180,1280×392,cut 14)，`#18244A` 填充 + cyan 2px 描边；左缘 6×148 cyan 定位条 |
| 封面 | **288×288 @ (622,232)**，上下各留 52px，占满卡高、左对齐，卡内最大元素（`CoverPlaceholder` 占位 + `NO COVER` / `COMMUNITY CHART`） |
| 曲名 | `Tablear` 46px @ (950,254) |
| 曲师 | `RHYX` 24px @ (950,330) |
| 本差分谱师 | `谱师 RHYX` 22px dim @ (950,368)——只显示当前所看差分那张谱的谱师，不是全部谱师列表 |
| 预览播放控件 | 传输键 64×64 切角 18 @ (950,436)（描边 + cyan 2px，图标 24px）；`PREVIEW` 14px dim ls 3 左对齐 (1034,450)；`0:12 / 0:30` 15px dim 右对齐到 x=1838；轨道 6px 从 1034 铺到 1838（白 12%），已播段 cyan 75% 宽 40%、末端 14×14 切角旋钮。静态帧定格在**播放中**：暂停图标 + 进度走了 40% |

卡内**不再有**难度徽章切换、评级字 / BEST 记分列与 START——这些职责全部回到底部操作条；
时长 / Note 行也从卡内去掉（留白更干净；要保留的话建议并入谱师行尾，见待决项）。

**底部操作条在收起/展开两态都存在且逐像素一致**（难度循环实心键 + BEST/CLEAR 条 + 评级字 + START），
列表板两态都是 (560,112,1320×828)、
滚动视口都是 916（板底 24px 内边距），所以展开卡下方固定是「2 整行 + 1 行残片」。
被视口裁到的那一行按实际裁剪绘制：板切到裁切线，文字/徽章只要落在裁切线以上就照常显示
（残片只有 28px，正好什么都放不下）。

#### 已接入行为

- 搜索即时按曲名/曲师/谱师大小写不敏感 OR 匹配；无结果显示 `没有匹配的谱面`。
  聚焦时 Esc 清空并交还页面焦点；选择被过滤时回退可见项，空列表禁用 START。
- 展开行自动试听，切歌先停旧音频并收旧卡；再点已展开卡则收起并停；START/离页停止。
  传输键播放/暂停，进度只读，不循环；区间读 `PreviewFor(diff)`，缺省 0s/30s，走 Music 总线。
- 列表滚轮与触摸板滚动走惯性（指数趋近，固定 τ = 0.112s = 编辑器 SCROLL INERTIA 默认档；
  `MOTION = OFF` 时归零），触摸拖动与滚动条拖动保持 1:1 直接操纵。模型见 §4 第 14 条。
- 预览整段加载音频，仅缓存当前曲，切歌/离页释放；大 WAV 首次加载可能卡顿。
- 展开/收起卡走 0.20s 高度过渡（96↔392，`ClipContents` 裁切、行跟随滑动；收拢落点才交还
  选中行，途中点别的行从半截高度干净反向）；`MOTION` 非 Full 时保持瞬时切换。
- 切歌/切难度时卡内曲名/曲师/谱师 0.08s 淡出→换文→0.12s 淡入（换文由代际守卫，快速连切
  不会写回旧文本）；封面在谱面就绪后从 0 淡入 0.15s；`MOTION = OFF` 全部瞬时。
- client 侧 hover 统一 0.10s 过渡：共享原语 `client/scripts/ui/UiHoverFade.cs`（与编辑器
  `EditorMotion.HoverFade` 同思路；SongRow/CutButton 原 0.16s `HoverDuration` 已归一到
  `UiHoverMotion.Seconds`），覆盖曲库行、MODE 行、全部设置行、CutButton、预览传输键；
  `MOTION = OFF` 瞬时。
- 进场默认收起、不自动试听。设计板的首行展开只是演示态，不改变客户端默认行为。
- BLEED 锁定/持久化、MIRROR 映射、AUTO 不写成绩均已接入；BLEED **玩法本身未实现**。
  镜像规则只走 shared `GameplayStageGeometry`，中轨轴 2.5，输入逆映射只施加一次，见当前交接。
- 收起与展开底部操作条一致；展开卡不放 START/难度切换，时长/Note 行当前不显示。

#### 真正待定/未做

1. 筛选/排序条件、弹层形态和与搜索叠加的空态；按钮保持 55% 压暗占位。
2. 搜索清除 ✕、显式自动聚焦、匹配排序等增强；不能把状态变体表当作已实现控件。
3. BLEED 数值/规则与段位模式方向，需要产品拍板，不先写判定逻辑。
4. 是否补时长/Note、BPM/标签或保留卡右侧呼吸留白；不擅自填充。
5. HARDCORE + BLEED 锁定等静态板变体可在需要时补出，不假定已有独立板。

未单独出板的**状态变体**（设计参考，可按需补板，不代表当前全部接入）：

| 画面 | 变体 | 与主板的差别 |
| --- | --- | --- |
| 选曲 | 读取中 | 详情曲名/曲师保留，META 行 `READING CHART · PREPARING DETAIL`（白 35%）、BEST 行 `BEST —   PREPARING`、START 禁用、青色 28px 扫描条在详情板内横扫 |
| 选曲 | 空曲库 | 曲名 `—`、难度键 `无可用谱面`、START 禁用、BEST 行清空；列表显示 `曲库为空：把社区谱面包放进 user://charts/` |
| 选曲 v2 | 搜索的聚焦 / 输入 / 无结果 | 聚焦 = 描边转 cyan 2px（可选加 24px 外发光）；输入中 = 占位文本换成输入值 + 右端出现清除 ✕；无结果 = 列表区出空态文案（`没有匹配的谱面`）；画法都是同一套切角框换描边色/换文字 |
| 选曲 v2 | 快捷设置其他档位 | 两板都用 STANDARD + BLEED 关 + MIRROR 关 + AUTO 关；MIRROR/AUTO 开 = 轨道 cyan 14% 填充 + cyan 2.5px 描边、方块滑到右侧；**HARDCORE 选中 = STANDARD 行的选中态整体搬到 HARDCORE 行**、MODE 说明换成 `HARD · 硬核判定（窗口 ×0.5）`，同时 **BLEED 行转锁定态**（开关保持开但压到 55%、方块停右侧 60%、左侧空位出 20px 挂锁、副文案换 `HARDCORE 下强制开启`） |
| 选曲 v2 | 读取中 / 空曲库 | 展开卡内曲名/曲师/谱师保留，信息整体压到白 35%；预览控件隐藏或禁用（传输键转 dim、进度条保持 0%）；底部条 BEST 行 `BEST —   PREPARING` 且 START 禁用；空库时列表显示 `曲库为空：把社区谱面包放进 user://charts/`、卡不出现 |
| 选曲 v2 | 预览未播 / 已暂停 | 同一个预览控件换一帧：传输键里换成播放三角（脚本已备 `glyph('play')`），进度保持当前值或 0%、时间读数同步；播放中帧（本板）是暂停图标 + 40% 进度 |
| 游玩 | Auto 演示 | 左下状态行追加 ` / AUTO · 不计成绩`；不写成绩 |
| 选曲 v2 | 筛选按钮启用态 | 占位块从 55% 压暗回到正常描边（或选中后转 cyan 填充 + cyan 图标），具体形态随筛选项拍板 |
| 过场（Signal Lock 板） | 路由文案 | 同一定格帧只换左下两行：`OPEN CHANNEL / SONG LIBRARY`、`OPEN CHANNEL / SETTINGS`、`SIGNAL RETURN / RESTORING PREVIOUS CHANNEL`、`SIGNAL LOCK / SWITCHING CHANNEL`（默认） |
| Track Handoff | 重开过场 | 角标题变 `TRACK HANDOFF · RE-SYNC`（底衬加宽到 380×50）、副文案 `RE-SYNC / <难度>`；构图不变 |
| Track Handoff | 无就绪信号 | 状态行 `PLAYFIELD / NO READY SIGNAL` 且转 pink；构图与 Game — Track Handoff 完全一致 |
| 游玩 / 编辑器目标 | 命中反馈 | 判定线上的双层爆发与粒子（`GameplayHitBloom` + `GameplayHitParticles`）是游戏动效；编辑器复用仍为目标，不进静态板，参数权威见 `ui-motion.md` §9.5 |

当前 handoff 三板采用居中布局（封面居中 + 斜对角双线电缆）：
封面居中 + 四角电缆充电释放；标题组 y=840/900。客户端实现以 `TransitionOverlay.cs` 为准。
之前的左封面 / Relay 汇聚信号场方案已废弃。三块板共用同一套构图，只切换充电相位（充电 → 锁定 → 放电）。

`Game — Signal Lock` 的**布局权威是 `client/scripts/ui/TransitionOverlay.cs`**（程序化绘制，非自由设计）；
它按该文件的 `Label.Position` 取**左上角**锚点，所以用 top-anchored 的 `ttxt/rtxt_t`。
handoff 三板（Track Handoff / Lock / Discharge）是**重做的新设计**，不再照搬源码坐标——只有
`TRACK HANDOFF` / `CONTEXT LOCKED` 两个角标题沿用左上角锚点，其余文案一律居中锚定（`ctxt`）。
动效语义见 `ui-motion.md` §2.1/§2.2。

施工脚本：第一轮 Game UI 板用 `.tmp/penpot_build5.py`，第二轮（首页/选曲/游玩/暂停/结算/Display，以及
Gameplay 板加 MODE 行、Audio 板去掉 DISPLAY 组）用 `.tmp/penpot_build6.py`——两者都带同样的
ENSURE_PAGE + 按名字 reconcile 的页面闸门（见 §5）。build6 只清理并重建它自己列出的板
（`settings_gameplay / settings_audio / display / home / select / play / pause / result`），
Signal Lock 与 handoff 三板原样保留。
校准向导两板（2026-10-09 定稿）的施工脚本存档在 `design/calibration-v1/build.js`
（可重复执行、同名先删后建，经 `.tmp/pmcp.py` 的 curl MCP 客户端驱动），预览导出在同目录
`preview-live.png` / `preview-result.png`。

### 2.2 校准向导（Calibration，已接入客户端）

**入口**：设置页 AUDIO 组 JUDGE OFFSET 行右侧「校准 ▶」，转场 `OPEN CHANNEL / CALIBRATION`；
返回/放弃不写任何设置。路由 `UiRoutes.Calibration`（`client/scenes/calibration.tscn`）。

**形态**：B（单轨试打）+ C（信号控制台）的合并——中央是游玩几何的单轨下落试打台（视觉核心），
击打反馈不弹判定档、只弹**毫秒读数**（`LAST HIT +8 ms`，早粉晚青），底部 DEVIATION MAP 条带
把每次击打落成 tick（±150ms 轴），右列是实时统计/确认面板。

**行为口径（已拍板）**：

- 状态机 Ready → Sampling → Complete。Ready 下节拍循环与 note 下落照常但不采样，首次击打进入
  Sampling；打满目标样本进 Complete（轨道冻结压暗 35%、脉冲环停、条带定格 + MEDIAN 线）。
- 目标样本数 8–32、每档 4、默认 16（持久化 `calibrationTargetSamples`）；**步进器采样途中锁定**，
  只有 Ready 可调（Complete 里要改就先「再测一次」）。
- 前 2 颗 note 为热身：读数灰显、不入样；热身归属按「偏差指向的那颗 note」（最近小节头）判定，
  提前 ≤150ms 的击打不会被误记给前一颗。
- |偏差| > 150ms 的击打是 stray tap：读数显示 `—`（1 秒后消隐），不入样。
- 建议偏移 = **当前值 − 中位数** → 5ms 步进（半档远离零）→ ±300 钳制。符号推导：
  `JudgeEngine` 偏差 = inputTime − noteTime（正 = 晚），inputTime 含 `+UserOffsetMs`，
  所以打得晚要把偏移往回拉（减）。全项目只有 `CalibrationStats.SuggestedOffsetMs` 一处持有该约定。
- 计时与游玩同源：`GetPlaybackPosition + TimeSinceLastMix − OutputLatency`（**不叠加** userOffset，
  那是被测量本身），headless 回退系统计时器；节拍音是程序合成的 2s 无缝循环
  （120BPM、4 click/小节、小节头 2000Hz 加重，88200 采样点整，Music 总线）。
- 几何：判定线 y、note 尺寸、下落时长公式取自游玩常量（`GameplayStageGeometry` /
  `GameplayVisualMapper`，Lv10 ≈ 0.77s 压线）；轨道横向居中于舞台带（中轴 840）是页面布局，
  刻意不取游玩中轨轴 963——横向位置不影响测量，且 963 会让 LAST HIT 读数（x=1030）压上轨道。
- 输入：触摸按下 / 鼠标左键，区域 = 舞台带矩形（460,140,760×781），键盘不参与击打（移动端优先，
  Esc 仅返回）；事件在 `_UnhandledInput` 收取，GUI 控件（返回键/步进器/按钮）优先消费。
- 动效：分组进场（头部/相位块/试打台/统计板/条带，ExpoOut 交错）；相位块与右板内容
  淡出→提交→淡入（0.18s）；冻结压暗 0.2s；MEDIAN 线 0.18s 淡入；hover 统一 0.10s
  （`UiHoverFade`）；MOTION Off 全瞬时、Reduced 只留透明度——note 下落与拍点脉冲是测量本体，
  任何档位都不降级。

## 3. 视觉规范

- 色板以 `presentation/ui/UiFonts.cs` 为准：bg `#0A0E1A`、panel `#141B33`、
  line `#2A3560`、cyan `#35E0FF`、pink `#FF4D8F`、text `#DFE6FF`、dim `#7C88B0`；
  难度色 casual `#4ADE80` / normal `#38BDF8` / hard `#FBBF24` / mega `#C084FC` /
  giga `#F87171` / tech `#E879F9`；Note 配色看 `presentation/game/NoteVisualSpec.cs`。
- 字体 Space Grotesk；切角语言：只切**左上 + 右下**。中文界面文案按源码原文写（游玩 / 设置 /
  谱面工坊 / 继续 / 重开 / 返回选曲 / 点击切换难度 …）——Penpot 端对 Space Grotesk 缺字会自动
  回退到 CJK 字面，不会出现豆腐块，所以不必为了排版把中文改成英文。
- **字距**：板面 `ls` 在 Godot 侧只能用 `FontVariation.SpacingGlyph` 表达，**它只有整数像素**，
  所以板面的 `ls 1.5`（HUD 的 BAR·BPM 行、菜单 TIME/CENTER/WIDTH 行标签、路径手柄与放置阶段标签）
  一律按仓库既有约定编码为 **2**；`ls 1 / ls 2 / ls 3 …` 照整数直取。不要去改板面的 ls 值来迁就
  实现，也不要为半像素字距另造逐字绘制。
- **正文字重与字号偏差（2026-10-08 拍板）**：编辑器正文从 SpaceGrotesk Regular 升到 **Medium**，
  设计稿字号统一经 `editor/scripts/EditorFonts.cs` 的 `Pt()` 先加一档（≤13 +2、14–18 +1、>18 不变）、
  再乘用户可调的 **UI SCALE** 档（90 / 100 / 110 / 125%，默认 100% = 只加这一档），
  `UiFonts.TechBold` 等强调字不动。原因是编辑器视口固定 1920×1080 但窗口开 1600×900
  （`canvas_items` 等比缩放打 0.833 折），物理像素上字偏小，而 Regular 又偏细，实测可读性差。
  这是有意偏离设计板标注的**全部编辑器页面**的观感调整，不是某块板的个案；今后要再调观感
  只改 `EditorFonts.cs` 一处（字体、字号档位与 UI SCALE 的换算顺序都集中在那里），
  不要把 Medium/Pt/AddThemeFontSizeOverride 散改回各页面。UI SCALE 的四档、立即生效机制、
  字距不随缩放的选择见 §4 第 15 条。
- **少文字原则**：不写实现注脚类文字（运行时、规则集名等）；重装饰轻信息的内容不做
  （歌曲详情不增加装饰性波形；这不意味着制谱器的音频波形需求已完成或被取消）。
- 编辑器几何复用游玩客户端：中央判定线、侧轨位置等与 `GameplayMain.cs` 顶部常量一致。
- 游戏页板的数值一律取客户端源码/常量：判定线 y=861、判定线跨 x=60..1800、侧轨 x=184/1736、
  Mixer 装饰条 y=620/642 跨 x=675..1244、中心轨 x=280+273.2·(P+W/2)、侧轨 x=184+剩余·k /
  1736−剩余·k（k=SideDistScale，当前保留 691/790≈0.8747，2026-10-17 用户实机反馈基本通过）、侧轨 y=840−115·(P+W/2)、Note 视觉缩放 0.95（`GameplayStageGeometry`）。
- **hover 统一过渡（2026-10-08 拍板）**：全部 hover 可点元素从瞬时换色改为约 0.10s 的颜色
  lerp（共享 helper `editor/scripts/EditorMotion.cs`：`HoverFade` 单值版给 hotspot 少而固定的
  页面，`HoverFadeMap` 键值版给动态/大量 hotspot 的页面；时长集中在 `HoverMotion.Seconds`），
  端点色与原瞬时 hover 一致，只有中间过程平滑；`MOTION=OFF`（设置真源）时推进直接跳终值。
  各页面在自己的 `_Process` 里推进、全部到终值即 `SetProcess(false)`，不空转；右键菜单 Open
  重建元素列表时 hover 值表整体清空（下标含义全变，残留值会画到错误元素上）。
  client 侧同口径原语是 `client/scripts/ui/UiHoverFade.cs`（0.10s 同源），覆盖范围见 §2.1。
  token 已落 `ui-motion.md` §3.1 `--t-hover`（100/100/0ms）。
- **窗口拖放接线（2026-10-08 拍板）**：`GetWindow().FilesDropped` 在 shell（`StandaloneEditorMain`）
  接一次，按当前页面路由——Start 页只收目录（回填路径行、不直接打开，与 BROWSE 卡同语义），
  Create 页按落点命中（`CreatePageOverlay.TryAcceptDrop`：音频区 = 音频路径行 ∪ BROWSE，
  封面区 = 虚线拖放区，走折叠重排的动态矩形）；校验口径：音频扩展名过
  `EditorProjectDraftFactory.IsSupportedAudioPath`，封面只收 .png/.jpg/.jpeg，多文件取第一个
  合法的，非法走中缝 `Report`；AddDifficulty 模式音频/封面只读，拒收并提示。
  Pack / Edit / Settings 拖入不响应。
- **之后考虑（用户已拍板「将来做现在不做」）**：
  1. 键盘流约定——Tab 顺序审计、`Esc` = 取消/返回、`Ctrl+Enter` = 确认等全套键盘导航。
  2. 保存回报 toast——替代现在 Pack 页状态行/Edit 页保存状态行的静态文本回报。

## 4. 编辑器交互目标（不是当前能力清单）

以下保留已讨论的产品方向；现有 shell 的具体已接入/缺口以 [制谱器说明](<dyna-maker-uv.md>) 为准。
Settings 页、浮动 chip、右键菜单的步进器、已有路径节点的 curve/judge 与
CURVE/JUDGE/DELETE NODE 行已在 2026-10-07 接入（未实现的行按占位惯例 55% 压暗），
路径的逐段真实投影（`V2PathEvaluator`）、`PATH_FIELD` 菱形手柄与**双击插入路径节点**在 2026-10-08 接入
（节点菜单 ADD NODE 因此改为真实动作，文案从 `ADD NODE · DBL-CLICK PATH` 改成 `ADD NODE · AT CURSOR`，
它按游标插入而不是单纯提示手势）；手柄的「沿路径依次点亮」stagger 仍是动效目标，当前静态全部显示。
**放置手势视觉**（§4 第 9、10 条，设计板 `Editor — Placing`）在 2026-10-08 接入
`editor/scripts/AuthoringCanvasPlacement.cs`：导引十字线 + 线上三读数 + 虚线幽灵描边 + 阶段 1 的宽度
双向箭头与光标十字 + 阶段标签；Hold 拖尾阶段头保持已放置实体外观、body 改由虚线拖尾预览 + 尾端虚线
菱形表示；同轨已放对象锚点的 26×26 对齐提示方块（蓝/红/品红，搬运中转白）也一并接入。
`ui-motion` §9.1 的「跨 0.05 档缘边亮闪」与放置提交确认的打击爆发仍是动效目标，本轮不做。
`PATH_FIELD` 的三处新增在同一轮补齐：编辑态顶中 (960,36) 的 `PATH EDIT` 标签、双击插入幽灵过其中心
位置轴的时间导引线（2px 白 35%、贯穿该轨可放区间）、以及选中 Mixer 时按 shared `V2MixerTicks`
的 1/8 小节网格画的 18×2 `#FF9CC4` 75% 判定 tick（位置取路径求值器在各 tick 时刻的 center）。
前景层的绘制都必须落在该层自己的 `_draw` 上（Godot 只允许把 `Draw*` 调进正在绘制的 item），
所以 `PlacementChrome` 把自身作为绘制目标传给 `AuthoringCanvasPlacement` 的各绘制函数。
完整试玩反馈仍未接入。Start、Pack Select 与 Create Project 三板已按稿落地，Settings 页的取舍与默认值偏差见 §4.7，
Pack 板的实现边界与偏差（状态行省略时长、
`DIGEST OK` 改为用当前编辑状态实算而非静态成功标记、滚动条长度按内容比例而非稿上的固定 280px）和 Create 板的偏差
（等级块按本文 §4.5 等距不分组而非 Create 板 `lvl_blocks` 的 5+2；没有时长/采样率探测，AUDIO 状态行与试听钮按真实
校验态和 55% 占位；METADATA 的 CHARTER 与 FIRST CHART 的 CHARTER 新建时互为镜像、两边都可编辑，
加差分时只读展示包内谱师——v2 只有 chart 级 charter，没有包级字段可写；AddDifficulty 下包元数据
只读、TIMING 隐藏）见 [制谱器说明](<dyna-maker-uv.md#2-当前页面与包流程>)。

1. **三层编辑器架构**：主界面是裸舞台（判定线/音符渲染与实机一致，底部判定线全宽；
   HUD 只有纯文字——左下歌曲标签（仿游戏内：歌名 + 难度色 + LV）、右下时间/Bar/BPM、
   右上 GRID/SNAP）；画布上的编辑信息只有三样：网格三档（小节线 160px／拍线 40px／
   细分线 10px 共用同一条点阵——小节 94 在 y=218，小节线 2px 白 22% 核心、拍线 1.5px
   白 10%、细分线 1px 白 4.5%，只在小节／拍线上各加一层白→透明的贴身软光晕，不做整屏
   距离渐隐；实现时按缩放自适应密度）、右缘小节标尺（小节刻度 + 书签菱形 + 当前位置
   三角，即整曲缩略时间轴）、底部 2px 进度细线。选中即出浮动 chip（高频微调）；
   右键菜单承载全部功能。
2. **放置手势行为参考**（固定参考快照中的 `mouse.js`，来源见 [制谱器说明](<dyna-maker-uv.md>)），所有
   Note 和路径 node 统一走这套三阶段单手势：
   - 阶段 0 按下：time + center 跟随光标；位置吸附 0.1，按住 X 精调 0.01；
     时间吸附网格，按住 Z 自由；
   - 阶段 1 拖动：宽度 = `preCoverWidth + |Δ|·Δ`（二次曲线响应，Δ 为光标到中心的
     轨内距离），步进 0.05、下限 0.1；
   - 松开提交并**记忆宽度**（`preCoverWidth`），下一个对象以相同宽度起手。
   - Hold/Mixer 追加阶段：拖时间轴定尾（钳制不早于头）。
   - 双击路径插入 node：双击点定 time+center，第二击不松手直接进宽度拖拽；
     落在路径身体上时 center/width 起手值**采样路径求值结果**（只保证插入采样点不跳变；非线性段切开后邻域可能形变，不承诺整段形状或通用亚像素误差，详见 [当前能力边界](<dyna-maker-uv.md#3-画布与编辑范围>)）。
   - 手势导引（对照原版 playView）：过 note 时间一条 pink 2px 全宽时间线（两端各
     200px 渐隐）、过 note 位置一条白色 3px 纵贯位置线；读数值（位置、宽度）贴在位置线
     两侧、分数式时间贴在时间线上方左侧，各带 1px 偏移的深色底衬充当原版的黑描边；
     已放置 note 的锚点用 26×26 色块标注与笔尖的同异（蓝=同位置、红=同宽度、品红=两者
     皆同）。
3. **两级选中**：点身体选中整个 Note；选中后路径显示菱形节点手柄，再点手柄选中
   单个 node。node 菜单有 TIME/CENTER/WIDTH 步进器、CURVE TO NEXT 六段选择器
   （linear/hold/easeInQuad/easeOutQuad/easeInOutCubic/smooth）、Hold 专属 JUDGE
   拨杆（Mixer 无此行，尾 node 锁定 ON）。
   **菜单背景决策（2026-10-08 拍板）**：右键菜单**不做全屏压暗层**，保持现状——板面 MENU/NMENU
   里那层 `#04060C` 55% 的全屏压暗不实现，菜单只用自己那块 480 宽切角面板（+ 120ms 淡入）表达层级；
   全屏压暗只留给切页（Settings 这类整页模态）与游戏侧的暂停/结算浮层。
4. **难度列表排序**：casual→normal→hard→mega→giga→**tech 垫底**，自定义难度
   （虚线 chip）排在下面按添加顺序；列表超高滚动（渐隐 + 细滚动条）；底部常驻
   「+ NEW CHART」虚线按钮。
5. **等级输入**：十进制块编码——十位数 = 大块（26×34 切角），个位数 = 小块
   （12×18，半高，**圆角 2**（`lvl_blocks` 的 `rect r:2`，不是切角），**等距不分组**），0 位不出块；
   数字永远是权威值。范围 1..99，
   步进器钳制、99 时 › 禁用；非法值（如 -1）红框拒绝；`unrated:true` 时数字禁用
   无块。**数字水平居中于两个步进器之间**（Widget — Level States 组件板的规则：组件板数字
   ctxt 280 正是步进器间隙 250..310 的中点；Create 板样例把数字放在 1062，比真实中点 1067
   偏左 5px，以组件板为准）。注意实现层有一个引擎陷阱（2026-10-09 实拍确诊）：LineEdit 的
   最小宽度 = `minimum_character_width`（默认 4）× 当前字号 `W` 宽，而 `Control.SetSize` 会把
   尺寸钳到不小于最小宽度——40px 的数字框在构造时先被默认主题（16px）钳到 ~68.5px，
   应用字号覆盖后还会被再钳一次，居中文字随之偏出步进器之间。修复 = 该控件
   `minimum_character_width` 清 0，且尺寸在进树（theme 缓存刷新、清零生效）后才回正——
   树外重设 Size 仍按旧缓存钳制（TIMING 的 BPM/offset 大数字同样处理）。
6. **自定义难度**：CUSTOM chip 选中时出现 DIFF KEY 输入框；约束 ≤24 字符、
   包内唯一（规范 §3.2）。
   **折叠重排决策（2026-10-08 拍板）**：难度非 CUSTOM 时 Create 页右列做**动画重排**而不是
   留空洞——DIFF KEY 行收起，LEVEL 行与 FIRST CHART 面板之下的右列（COVER/DRAFT）整体上移
   64px，FIRST CHART 面板高度 340→276；底部操作条不动，仍锚定页底。**展开态（CUSTOM 选中）
   坐标与设计稿 1:1 不变**，折叠量由单值驱动（`CreatePageOverlay` 的 `_collapseT`，展开 t=0
   即设计稿常量），过渡 0.18s CubicOut；DIFF KEY 输入框随过渡淡出，折叠终点完全不可交互。
   过渡期间命中矩形与 LineEdit 位置跟动画值走（可交互元素在动画中也点得到）；进入页面
   （Apply/PrefillChart）直接落终值不播动画，只有页内点 chips 切难度才过渡；MOTION=OFF
   时一切直接跳终值。
7. **设置页目标**：未来编辑器提供独立 Settings 页（Start 页进入；编辑器内经右键菜单进入），
   左导航 GENERAL/EDITOR/AUDIO 三组。v1 内容：Motion 三档（与客户端同语义）、
   特效质量、自动保存、默认网格/吸附、网格与背景压暗、试听音量/节拍器/打击音。
   视觉沿用客户端设置页语言。
   **已落地（2026-10-08）**：`editor/scripts/SettingsPageOverlay.cs` 按 §3 语言整页自绘，
   三组行与控件几何取 `.tmp/penpot_build4.py` 的 `settings_board()`。
   真接点七项——MOTION（关闭编辑器自己的淡入）、UI SCALE（正文字号倍率四档，见 §4 第 15 条）、
   DEFAULT GRID（新草稿与当前文档的网格分母）、
   SNAP（画布空间吸附步长）、GRID OPACITY（三档网格不透明度倍率）、BACKGROUND DIM
   （`GameplayBackdrop` 的 `Modulate` RGB 压暗）、SCROLL INERTIA（滚轮惯性强度，见 §4 第 14 条），
   值落在 `editor/scripts/EditorSettings.cs`
   （`user://editor-settings.json`），RESET DEFAULTS 回默认并落盘。
   EFFECTS、AUTO SAVE、AUDIO 三组没有实现可接，按设计稿示例值画 55% 占位、不响应交互，
   值也不进设置模型。相对板面的取值偏差：默认网格取 1/16（= 编辑器既有默认，板面示例是 1/32）、
   GRID OPACITY 默认 100%、BACKGROUND DIM 默认 0%（板面示例 35% / 60% 是滑条位置示例；
   若把 35% 当默认倍率，编辑器网格会比主板面淡约三倍）、
   **SCROLL INERTIA 是板面上没有的新行**（设计板 EDITOR 组只有四行），插在 BACKGROUND DIM
   之后（EDITOR 组因此变五行），行几何沿用同一套行距/标签/滑条常量，默认 40%。
   **UI SCALE 是板面上没有的第二条新行**，插在 GENERAL 组的 MOTION 之后（默认 100%，
   即引入这一项之前的观感）。
   **页形态决策（2026-10-08 拍板，同日修正）**：Settings 页保持「左导航分页、只画当前组」，
   **不**改成设计板的单面板全长版（把所有组一次铺满）。理由是后续设置内容还会继续增加，
   全长面板会先撑不住；分页也让「当前组」这件事永远是明确的。设计板的组内行几何与取值仍照旧是权威。
   同日用户进一步明确分页的形态：**三组各自是一块从头开始的独立面板**——任何组的第一行
   都从同一组顶（y=240）起排，面板高度随该组行数变化；不是一块长板按组累进堆叠后切窗口
   （那样切到后面的组时面板上半截是大段空白，像把一块板拆成三块）。行距因此回到稿上的 52。
8. **动效归属**：编辑器动效 token 分配见 `ui-motion.md` §9（吸附 ping 已拍板不做）。
   Penpot 稿只画状态帧：`Editor — Placing` 记录放置手势的幽灵态、宽度拖拽指示与
   Hold 拖尾虚线预览；hover 只有亮度/不透明度变化，不单独出板。
9. **放置导引十字线**（行为参考 dynamaker 原版 `playView.js:1473-1599`，clean-room
   只取行为）：写模式下过幽灵 Note 画全宽 pink 时间线（两端渐隐）+ 3px 白色纵向
   位置线；线上贴三个数值——位置线右侧宽度（白）、左侧位置（类型色）、时间线上方
   分数式时间（如 `94+7/32`；时间自由态显示原始小数）。放置态不再用独立宽度 chip。
   **落地（2026-10-08）**：`AuthoringCanvasPlacement.cs`。三处偏差——(a) 幽灵的**填充**
   不透明度仍取 shared 渲染器的 ghost alpha（0.6，`client/` 不改），板面的 75% 只体现在
   画布叠加的虚线描边上；(b) 时间读数沿用编辑器唯一一份 BarTime 文本（最简分式，可约分的
   板面示例 `91+8/32` 写成 `91+1/4`、`94+7/32` 原样），与 chip 和菜单 TIME 步进器一致，
   不另造网格分母式文本；(c) 板面只给了中轨几何，左右侧轨按 `PATH_FIELD` 的做法交换
   时间/位置两轴（时间线转竖、位置线转横并取判定线到该轨可放置端），三个读数沿判定线外侧
   的空档竖排。阶段 0（按下未拖动）板面没有示例，标签按同一命名写 `PHASE 0 · PLACE`；
   宽度箭头按任务描述保留了中段刻度（板面 `svg_dim_arrow` 本身没有）。
10. **对齐提示方块**（参考原版 `playView.js:942-1024`）：已放 Note 的锚点画 26×26
    方块，颜色编码与笔尖幽灵的关系——蓝 `#4060FF`=同位置、红 `#FF4040`=同宽度、
    品红 `#FF40FF`=两者都同、白=搬运中。这是"不靠吸附靠撞色"的对齐语言。
    **落地（2026-10-08）**：`EditorPlacementGuide.Classify` 判定 + `AuthoringCanvasPlacement.cs`
    绘制。比较的是吸附后的对象中心与宽度（与放置读数、chip 读数同一个值），容差取吸附步长的
    一半；只比较同轨对象；搬运已有对象（选择态的移动/宽度/节点手势）时全部锚点转白，
    被搬运对象自己不画。
11. **网格三档分级**（参考原版 `playView.js:450-659`）：细分线 1px 淡、拍线 1.5px
    加 4px 软光晕、小节线 2px 加 8px 软光晕；不做距离渐隐。小节编号密度随缩放
    自适应（实现规则：2 的因子越多越优先显示，缩到最小时只剩小节号）。
12. **命中反馈复用目标**：游玩命中 = 程序化核心爆发（GameplayHitBloom）+ GPU
    粒子质感层（火花/余烬、加法混合，参数权威见 `ui-motion.md` §9.5）。编辑器
    目标复用同一双层反馈：放置提交确认为缩小版（Strength≈0.4），试玩命中与游玩一致；
    Hold/Mixer 试玩接触中同样有持续粒子层。编辑器不另造特效语言。
13. **Storyboard Studio 入口占位**（2026-10-07 拍板）：Storyboard Studio 与 Note
    Editor 形态完全不同（对象树 / 属性轨 / 舞台预览的另一套工具），因此入口**不放
    编辑态 HUD**，而是放在 Pack Select 底部操作条、`OPEN IN EDITOR` 左侧——带着选定
    包与差分的上下文进入平行工作室。占位为禁用态：220×52 切角框只有 `#2A3560` 2px
    描边不填色、连同文字整体压到 55%（与选曲筛选占位同一语言），不代表任何已实现的
    Storyboard 能力。首版不做 Storyboard，正式形态为未来单独冻结的谱面 sidecar，
    决策与边界见 [Storyboard 提案](<archive/proposals/storyboard-editor-proposal.md>)。
14. **滚动惯性**（2026-10-08 拍板）：滚轮与触摸板滚动不再瞬时改位置，而是推一个"目标值"，
    显示值按帧率无关的指数趋近追上：
    `current += (target − current) × (1 − e^(−Δ/τ))`，因此同样一次滑动在 30fps 与 144fps 下
    的行程与耗时一致（不写成 per-frame lerp 常量，那会让高帧率滚动变快）。
    - **接入面**：编辑器画布的滚轮（`StandaloneEditorMain._Input`，改的是视图时间 `_time`/`PreviewTime`）
      与 Pack 页差分行列表（`PackPageOverlay` → `PackChartList._scroll`）；client 侧只有选曲列表
      （`SongSelect` 的 `ScrollContainer`）。
    - **必须直接落目标、不滑的路径**：键盘/按钮跳转（`M`/`R`/`Enter`/`A`/`D`、右键的 REPLAY 与
      TEST PLAY FROM MARK）、程序性刷新（打开文档归零、切页、内容整体换一份）、播放中的时间推进。
      实现上收在 `JumpTime` / `SetRows` 这类"落终值"入口里，与 `NudgeTime` / `ScrollBy` 这类
      "推目标"入口分开。
    - **播放旁路**：播放时视图时间是时钟，每帧把目标值同步成显示值，惯性完全不参与——否则 HUD
      时间读数、进度线与 note 位置会滞后于推进速度。
    - **client 侧的三个特殊点**：滚轮要在 `_Input` 里截住（它先于 GUI 派发），否则 `ScrollContainer`
      先把位置瞬移掉；触摸拖动与滚动条拖动保持 1:1 直接操纵、不吃惯性；`EnsureControlVisible`
      这类外部写入会被检测到并中止惯性（否则惯性会把它拽回去）。
    - **τ 映射与档位**：`EditorSettings.ScrollInertiaPercent` 0–100 线性映射到 0–0.28s，
      滑条最左（0%）= 0 = 瞬时，行为与加惯性前逐值一致。默认 **40% = 0.112s**（中档）。
      上限取 0.28s 是因为指数趋近 3τ 后仍剩约 5% 行程，再大就只剩"漂"的观感。
    - **与 MOTION 的关系**：滚动惯性有自己的滑条，**不与 MOTION 耦合**（0% 就是关）；
      但 `MOTION = OFF` 时强制把惯性也当作 0——OFF 的语义是"无动效"，保留滚动拖尾会自相矛盾。
      client 侧没有惯性强弱设置项，用固定常量 `ScrollInertia.TauSeconds = 0.112s`
      （= 编辑器默认档），同样在 `MOTION = OFF` 时归零，并在注释里写明它与编辑器默认档一致。
15. **UI 缩放（UI SCALE，2026-10-08 拍板）**：编辑器正文字号从固定偏差升级为**用户可调的四档**
    90 / 100 / 110 / 125%，默认 100%（即引入这一项之前的观感），设置真源是
    `EditorSettings.UiScalePercent`，持久化字段名 `uiScalePercent`；非法值按最近档回落
    （与网格分母、吸附步长同一套 `Nearest` 钳制），非法档位不会在设置页留出没有选中项的分段器。
    它是 GENERAL 组的第二行（MOTION 之后），控件与 MOTION 同款分段器，走"离散档位改动立即落盘"
    的事件通道（`SettingsPageOverlay.UiScaleSelected` → `EditorFlow.ApplyUiScale`）。

    - **换算顺序**：`EditorFonts.Pt(designSize)` = **先按设计稿档位修正**（≤13 +2、14–18 +1、更大不变），
      **再乘 `UiScalePercent/100`**，四舍五入（AwayFromZero，避免恰好 .5 时字号在奇偶档间跳）并保底 1px。
      顺序不可反——档位修正是相对设计稿的固定偏差，缩放是用户倍率；先修正再缩放才能保证 100% 档与
      引入这项之前**逐值一致**。`Pt` 保持纯函数（每次读 `EditorSettings.Current`，不缓存）。
    - **为什么是 90–125 这个窄区间**：编辑器几何全是设计稿的固定坐标，字号只改字的像素高度、
      **不缩放布局**（没有等比缩放画布）。125% 时最大的标题字已经贴住它那行固定的框，再往上就是顶框
      而不是更好读；90% 以下则把最小档（修正后 8–10px）压到看不清，失去这项设置的意义。
    - **两个读取面与立即生效机制**（重点，见 `editor/scripts/EditorFonts.cs`）：
      ① **Control 侧**（Label / LineEdit / Button 的 `font_size` theme override）设一次就固定，
      所以统一走 `EditorFonts.ApplySize(control, designSize)`——设 override 并登记 `(控件, 设计稿字号)`；
      换档时 `ReapplySizes()` 重放全部登记项。登记的是**设计稿字号**而不是换算结果，否则注册表会把
      换档前的旧字号冻结下来。控件用弱引用持有，重放时顺手剔除已释放实例，不产生泄漏。
      ② **画布自绘侧**（`DrawString` / `LeftText` / `RightText` / `CenteredText` 的字号实参）本来就是
      每次绘制现算；原先冻结在 18 个 `static readonly int` 里的那批已改成**现值属性**
      （`private static int BarNumberSize => EditorFonts.Pt(11)`），保证换档后下一次 `QueueRedraw`
      就用新值。直接把 `Pt()` 写进调用实参的地方（画布放置手势、路径插入读数、菜单绘制）不用动。
      ③ **触发链**：分段器事件 → 写 `EditorSettings` + 落盘 → `ReapplySizes()` 重放注册表 →
      `AuthoringCanvas.RedrawForUiScale()`（画布与压在 Note 之上的手势前景层两层一起刷）+
      Pack / Create / Settings 三个 overlay + 右键菜单 `QueueRedraw`。持 Label / LineEdit 的页面靠注册表，
      自绘页面靠重绘，**都不需要重启或重建页面**。RESET DEFAULTS 一并回到 100% 并走同一条链。
    - **字距不随缩放变**：各页的 `FontVariation.SpacingGlyph`（ls1 / ls2 / ls3…）保持设计稿原值。
      它是字形的呼吸，不是可用性参数——跟着字号放大会在小字上把词拆散，反倒降低可读性；
      而且 `FontVariation` 只吃整数像素，乘 0.9 / 1.1 后要么被四舍五入吃掉、要么在 1 与 2 之间来回抖。
      所以只有字号随档位走。这一条写在 `EditorFonts.cs` 的注释里。
    - **行几何（2026-10-08 修正）**：Settings 三组改为各自从头开始的独立面板后（见 §4.7 页形态
      决策），组顶恒为 240，行距保持稿上的 52，不再有加行压底问题；早先为累进堆叠版面临时
      收到 48 的行距偏差随之撤销。保留的两条小偏差：**UI SCALE 是板面上没有的第二条新增设置行**
      （同 SCROLL INERTIA 的处理，取默认 100% 时与板面观感一致）；
      **四档分段器的段宽按段数反算**（三档 = 稿上 92，四档 = 68）而不是照搬 92 宽，
      否则这一行会向标签方向多探出 96px、看着像控件列被顶开。

## 5. Penpot MCP 施工坑（都踩过）

- **导出分层后恢复 opacity 要处理缺省值**（2026-10-03）：部分 Shape 读出的
  `opacity` 是 `undefined`，回写该值会报 `Value not valid. Code: :opacity`。
  暂时隐藏图层做透明导出时，保存 `typeof s.opacity==='number' ? s.opacity : 1`，
  并在 `finally` 中恢复；不能把临时隐藏状态留在用户画板里。

- **Shape 代理对象按 id 比较，不按引用比较**（2026-10-02）：两次读取
  `page.root.children` 得到的同一形状可能是不同 JS 代理对象。按名 reconcile 时，
  用 `s !== old` 排除目标板会把目标自身误判成占位冲突；应使用 `s.id !== old?.id`。
- **新建 Path 的默认描边**（2026-10-02）：纯填色 cut helper 也要显式设置
  `strokes=[]`，否则 `createPath()` 的默认黑色轮廓可能保留在实心徽章与光点上。

- **大图直接塞进 `execute_code` 会遇到 HTTP 413**（2026-10-02）：约 100KB PNG
  转成 base64 后可能超过入口请求体限制。将 base64 按 24,000 字符分块存入
  `storage`，最后拼接为 `Uint8Array`，用 `penpot.uploadMediaData` 上传，再用
  `Rectangle.fills = [{fillImage: imageData, fillOpacity: 1}]` 贴图。先上传完所有媒体，
  再 reconcile 目标板；每次调用仍要执行文件/页面闸门。桥接脚本不要把 base64、密钥或
  上传内容写进日志。实例：`.tmp/penpot_grade_concepts.py`。
- **标签页休眠断心跳**：浏览器标签页不前台时一切写入报 "tab suspended"。构建脚本
  有 60 次 × 8s 重试；跑脚本时让 Penpot 标签页保持前台。
- **概念板不能按“看起来空白的行”选址**（2026-10-04）：Game UI 的第四行
  `y=3840` 已有 `Game — Grade Set v5 Current`，后续行还可能有结算过场等板；
  画布视图缩放或横向滚动时容易误判为空位。新增一组连续板前，先删除本脚本自己
  的旧概念板，再读取 `page.root.children` 的实际包围盒，取所有顶层对象最大
  `y + height`，从其后的网格行开始搜索整组板位，并对整组宽度做重叠检查。脚本
  `.tmp/penpot_home_power.py` 的 `create_boards_code()` 已按此规则实现；执行前仍
  必须通过 `Game UI` 页面闸门并保持 Penpot 标签页前台。
- **`createText` 同步尺寸恒为 1×1**：文本居中/右对齐必须两阶段——先估算摆放并登记
  锚点，等下一次 `execute_code` 调用（布局已完成）再读真实 `width/height` 吸附。
  即 build4 的 `ctxt/vtxt/rtxt` + `FIXUP` 步骤。
- **`createText('')` 恒返回 null**：空串文本直接跳过；偶发 null 用重试（5 次）。
- **渐变**：`fillColorGradient` 坐标语义与文档不符，一律用 `createShapeFromSvg`
  内嵌 `<linearGradient>/<radialGradient>`。
- **SVG 负坐标会撑大导入包围盒**：所有 SVG 内容坐标保持非负并按 viewBox 裁剪。
- `width/height` 只读，改尺寸用 `resize()`；虚线描边创建后重设 `strokes`
  （`strokeStyle:'dashed'`）。
- 导出：`export_shape`（shapeId=板 id，format png）→ content 里取 base64；
  偶发服务端 timeout，重跑即可。
- **同名重复板**：历史事故遗留过两块同名同坐标叠放的板（旧内容盖住新内容，导出的是
  新的、看到的是旧的）。`RESET` 已按名字去重（保留第一块、删除其余）；发现画面和
  导出对不上时先查同名板。
- **多页面**：`penpot.createPage()` 建页（不自动切过去），`page.name` 改名，
  `penpot.openPage(page)` 切页——但**切页是异步的**，同一 `execute_code` 调用内
  `penpot.currentPage` 不变，要等下一次调用才生效（跨 HTTP session 也保持）。
  `penpot.currentPage` 是**编辑器当前活动页**：用户点回别的页、或标签页重载，都会
  回退，所以每次构建前都得重新断言。**改非活动页会被插件拒绝**（`Cannot modify a
  page that is not currently active`），而 `storage.B` 是跨页存活的——若它留着上一页的
  板引用、而活动页刚好又是那一页，就会误改。故：每页各写一个按名字 reconcile 的
  RESET，开头硬闸门 `if (penpot.currentPage.name !== PAGE) throw`，并显式
  `storage.B = {}`（不要用 `|| {}`）。（`penpot_build5.py` 是这套的样板。）
- **失败的构建会留下孤儿图形**（第二轮踩过）：`penpot.createRectangle()` 先建对象、再
  `resize()`、最后才 `r.x = p.x + x`；若板引用是 `undefined`，异常抛在赋值处，于是**一个
  默认灰 `#B1B2B5`、默认尺寸的矩形留在画布原点**。它不属于任何板，所以板级导出 PNG 里
  看不到它，但画布上会盖住 (0,0) 那块板——排查「画面和导出对不上」时除了查同名板，还要查
  `page.root.children` 里有没有非板图形。收尾时统一清一遍。
- **板 key 与 RESET 里的 key 必须逐字一致**（第二轮踩过）：`storage.B.<key>` 写错时不会报
  「板不存在」，而是在第一次读 `B.x` 时抛 `Cannot read properties of undefined
  (reading 'x')`，报错位置离真正的原因很远。新增板时先核对 RESET 的 defs 表。
- **helper 片段必须拼在 `HELPERS2` 之前**（第二轮踩过）：build4 的 `HELPERS` 没有 return，
  build5 的 `HELPERS2` 以 `return 'helpers2 ok'` 结尾；后续追加的 helper 段若接在它后面，
  整段会被 return 短路，只有在**运行时**调用才暴露 `storage.xxx is not a function`
  （构建时报的是 "helpers2 ok"，看着一切正常）。build6 的拼法是
  `HELPERS + HELPERS3 + HELPERS2`。
- **Python f-string 里嵌的字符串不是 f-string**（第二轮踩过）：像
  `f"""...{txt('ctxt', 0, 0, 'CLEAR {PCT}')}..."""` 这种写法，替换字段内部的字符串字面量
  里 `{PCT}` **不会**展开，会原样落到设计稿上（Result 板的 CLEAR 行踩过，导出自查才发现）。
  动态值用拼接：`'CLEAR ' + PCT + ' · ...'`。

### 5.1 文本锚点约定（第二轮定稿）

Godot 侧的文字定位方式不同，Penpot 端用对应锚点，两阶段 `FIXUP3` 再按真实尺寸吸附：

| 源码写法 | 锚点 | 说明 |
| --- | --- | --- |
| `Label.Position = (x, y)`（左上角定位、默认顶部对齐） | `ttxt`（左+顶）/`tctxt`（中心 x + 顶）/`rtxt_t`（右+顶） | 源码给的是框左上角，所以锚顶边而不是框中心 |
| `Label` 带 `VerticalAlignment.Center` | `vtxt / rtxt / ctxt`（框中心） | 设置页的行标签就是这一档 |
| `HorizontalAlignment.Center` 且顶部对齐 | `tctxt` | build6 新增的 `tc` 模式：x 用中心、y 用顶边。游玩 Combo/判定字、暂停标题用它 |
| `CutButton` 自绘 `DrawString` | `ctxt`（居中）/`vtxt`（AlignLeft 时 x=+28） | 基线 = `h·0.40 + size·0.34`（有副文本）/ `h·0.50 + size·0.35`（无）；再减 `0.36·size` 当字形中心 |
| `SongRow` 自绘 `DrawString` | `vtxt` | 标题基线 `h·0.44`、副行 `h·0.78`，同样减 `0.36·size` |

已有游戏页字号/内容取当前源码；旧板的示例字号不能覆盖新结算图集或改版布局；
版本串从当前 `client/export_presets.cfg` 读取，不在施工约定中冻结历史版本号。

## 6. 实现注意

- 设计稿坐标是确定性的，导入 Godot 直接按稿取值；文本对齐由两阶段吸附保证，
  不需要再手调。
- 放置手势、编辑器状态等不进正式 chart、不进 Gameplay Digest（规范 §12.4）；
  非法输入编辑期钳制 + 导出前 validator 兜底，两层 fail closed。
