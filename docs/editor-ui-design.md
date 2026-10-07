# UI 设计稿与施工约定

> 状态整理：2026-10-07。本文记录设计稿位置、当前采用方案、设计目标与施工规则，**不是功能完成清单**。
> 当前编辑器能力见 [DynaMaker UV](<dyna-maker-uv.md>)；格式权威见 [v2 合同](<chart-format-v2.md>)，游戏动效权威见 [ui-motion.md](<ui-motion.md>)。
> 下列 Editor UI 板与 §4 是设计目标，包含尚未接入的 Settings、曲线/judge、节点插入和完整工具；不可据画板认定已实现。
> 旧版比较过程、已解决项与阶段状态已存 [原文快照](<archive/2026-10-07-cleanup/editor-ui-design.before.md>)。

## 1. 设计稿在哪

- Penpot 网页端，文件 **「Dynamaker UV UI design」**（账号 Natsume K）。
- 接入方式：Penpot MCP（streamable HTTP），URL 带 userToken，配置在
  `~/.kimi-code/mcp.json` 的 `mcpServers.penpot.url`。可配置于 Codex 的
  `~/.codex/config.toml` → `mcp_servers.penpot`，使用 120s 启动超时、180s 工具超时与
  `User-Agent: Codex-Penpot/1.0`；配置备份只放用户配置目录。
  `.tmp/penpot_mcp.py` 现在优先读取 Codex 配置，缺少时回退 Kimi。**token 是密钥，不进仓库。**
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
| Pack Select | 0,1280 | 1920×1080 | 谱面包选择 + 差分列表 |
| Create Project | 2120,1280 | 1920×1080 | 新建工程（含自定义难度创建态） |
| Widget — Level States | 6360,1280 | 1100×660 | 等级输入五状态组件 |
| Editor — Settings | 8480,0 | 1920×1080 | 设置页（GENERAL/EDITOR/AUDIO 三组） |
| Editor — Placing | 0,2560 | 1920×1080 | 放置手势状态：幽灵 Note、导引十字线+线上数值、宽度拖拽、Hold 拖尾预览、对齐提示方块 |

以上十板都在 **Page「Editor UI」**。

**Page「Game UI」** 保留基础十四板及后续资产/结算/首页概念板。尺寸以 1920×1080 为主，x 间距 2120。
基础板中的旧 Song Select 和 Result 仅作历史对照；当前分别采用 Song Select v2 与 Result v2。
下面表格用于定位，不意味着每一块参考板都是当前 UI。

| 板 | 坐标 | 尺寸 | 内容 |
| --- | --- | --- | --- |
| Game — Settings (Gameplay) | 0,0 | 1920×1080 | GAMEPLAY tab 四行：JUDGE OFFSET / DROP SPEED 大步进器、HIT EFFECTS 大开关、MODE 二段器 |
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
`04 Grade First 1750ms`、`05 Settled`、`06 All Prefect`。600ms 合拢、320ms 停顿、480ms 拉开，
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
- 预览整段加载音频，仅缓存当前曲，切歌/离页释放；大 WAV 首次加载可能卡顿。
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

## 3. 视觉规范

- 色板以 `client/scripts/ui/UiFonts.cs` 为准：bg `#0A0E1A`、panel `#141B33`、
  line `#2A3560`、cyan `#35E0FF`、pink `#FF4D8F`、text `#DFE6FF`、dim `#7C88B0`；
  难度色 casual `#4ADE80` / normal `#38BDF8` / hard `#FBBF24` / mega `#C084FC` /
  giga `#F87171` / tech `#E879F9`；Note 配色看 `client/scripts/game/NoteVisualSpec.cs`。
- 字体 Space Grotesk；切角语言：只切**左上 + 右下**。中文界面文案按源码原文写（游玩 / 设置 /
  谱面工坊 / 继续 / 重开 / 返回选曲 / 点击切换难度 …）——Penpot 端对 Space Grotesk 缺字会自动
  回退到 CJK 字面，不会出现豆腐块，所以不必为了排版把中文改成英文。
- **少文字原则**：不写实现注脚类文字（运行时、规则集名等）；重装饰轻信息的内容不做
  （歌曲详情不增加装饰性波形；这不意味着制谱器的音频波形需求已完成或被取消）。
- 编辑器几何复用游玩客户端：中央判定线、侧轨位置等与 `GameplayMain.cs` 顶部常量一致。
- 游戏页板的数值一律取客户端源码/常量：判定线 y=861、判定线跨 x=60..1800、侧轨 x=184/1736、
  Mixer 装饰条 y=620/642 跨 x=675..1244、中心轨 x=280+273.2·(P+W/2)、侧轨 x=184+剩余·0.75 /
  1736−剩余·0.75、侧轨 y=840−115·(P+W/2)、Note 视觉缩放 0.95（`GameplayStageGeometry`）。

## 4. 编辑器交互目标（不是当前能力清单）

以下保留已讨论的产品方向；现有 shell 的具体已接入/缺口以 [制谱器说明](<dyna-maker-uv.md>) 为准。
Settings、浮动 chip、curve/judge、双击插入路径节点和完整试玩反馈并未因此成为可用功能。

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
     落在路径身体上时 center/width 起手值**采样路径求值结果**（形状零跳变）。
   - 手势导引（对照原版 playView）：过 note 时间一条 pink 2px 全宽时间线（两端各
     200px 渐隐）、过 note 位置一条白色 3px 纵贯位置线；读数值（位置、宽度）贴在位置线
     两侧、分数式时间贴在时间线上方左侧，各带 1px 偏移的深色底衬充当原版的黑描边；
     已放置 note 的锚点用 26×26 色块标注与笔尖的同异（蓝=同位置、红=同宽度、品红=两者
     皆同）。
3. **两级选中**：点身体选中整个 Note；选中后路径显示菱形节点手柄，再点手柄选中
   单个 node。node 菜单有 TIME/CENTER/WIDTH 步进器、CURVE TO NEXT 六段选择器
   （linear/hold/easeInQuad/easeOutQuad/easeInOutCubic/smooth）、Hold 专属 JUDGE
   拨杆（Mixer 无此行，尾 node 锁定 ON）。
4. **难度列表排序**：casual→normal→hard→mega→giga→**tech 垫底**，自定义难度
   （虚线 chip）排在下面按添加顺序；列表超高滚动（渐隐 + 细滚动条）；底部常驻
   「+ NEW CHART」虚线按钮。
5. **等级输入**：十进制块编码——十位数 = 大块（26×34 切角），个位数 = 小块
   （12×18，半高，**等距不分组**），0 位不出块；数字永远是权威值。范围 1..99，
   步进器钳制、99 时 › 禁用；非法值（如 -1）红框拒绝；`unrated:true` 时数字禁用
   无块。
6. **自定义难度**：CUSTOM chip 选中时出现 DIFF KEY 输入框；约束 ≤24 字符、
   包内唯一（规范 §3.2）。
7. **设置页目标**：未来编辑器提供独立 Settings 页（Start 页进入；编辑器内经右键菜单进入），
   左导航 GENERAL/EDITOR/AUDIO 三组。v1 内容：Motion 三档（与客户端同语义）、
   特效质量、自动保存、默认网格/吸附、网格与背景压暗、试听音量/节拍器/打击音。
   视觉沿用客户端设置页语言。
8. **动效归属**：编辑器动效 token 分配见 `ui-motion.md` §9（吸附 ping 已拍板不做）。
   Penpot 稿只画状态帧：`Editor — Placing` 记录放置手势的幽灵态、宽度拖拽指示与
   Hold 拖尾虚线预览；hover 只有亮度/不透明度变化，不单独出板。
9. **放置导引十字线**（行为参考 dynamaker 原版 `playView.js:1473-1599`，clean-room
   只取行为）：写模式下过幽灵 Note 画全宽 pink 时间线（两端渐隐）+ 3px 白色纵向
   位置线；线上贴三个数值——位置线右侧宽度（白）、左侧位置（类型色）、时间线上方
   分数式时间（如 `94+7/32`；时间自由态显示原始小数）。放置态不再用独立宽度 chip。
10. **对齐提示方块**（参考原版 `playView.js:942-1024`）：已放 Note 的锚点画 26×26
    方块，颜色编码与笔尖幽灵的关系——蓝 `#4060FF`=同位置、红 `#FF4040`=同宽度、
    品红 `#FF40FF`=两者都同、白=搬运中。这是"不靠吸附靠撞色"的对齐语言。
11. **网格三档分级**（参考原版 `playView.js:450-659`）：细分线 1px 淡、拍线 1.5px
    加 4px 软光晕、小节线 2px 加 8px 软光晕；不做距离渐隐。小节编号密度随缩放
    自适应（实现规则：2 的因子越多越优先显示，缩到最小时只剩小节号）。
12. **命中反馈复用目标**：游玩命中 = 程序化核心爆发（GameplayHitBloom）+ GPU
    粒子质感层（火花/余烬、加法混合，参数权威见 `ui-motion.md` §9.5）。编辑器
    目标复用同一双层反馈：放置提交确认为缩小版（Strength≈0.4），试玩命中与游玩一致；
    Hold/Mixer 试玩接触中同样有持续粒子层。编辑器不另造特效语言。

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
