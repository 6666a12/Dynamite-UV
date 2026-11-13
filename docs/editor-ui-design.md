# UI 设计稿与施工约定

> 2026-11-12 更新：Claude Code 已连接 Penpot 并核对当前文件；旧 `Editor UI` 页面已由用户删除。游戏画板已重建分入六个功能页面，当前页面索引及迁移边界见 §1.1。保留游戏设计位置、采用方案和 MCP 安全施工规则；本文不是功能验收清单。
> 当前制谱器 Avalonia + Godot 主线与核心接线见 [DynaMaker UV](<dyna-maker-uv.md>)；格式权威见 [v2 合同](<chart-format-v2.md>)，游戏动效权威见 [ui-motion.md](<ui-motion.md>)。
> 旧 Editor 板和实现说明见 [历史设计](archive/art/2026-10-editor-ui-design.md)，不得据历史状态重开已完成工作。
> 旧版比较过程、已解决项与阶段状态已存 [原文快照](<archive/2026-10-07-cleanup/editor-ui-design.before.md>)。
>
> **2026-11-02 主线更新：正式制谱器采用 Avalonia Shell + Godot 原生舞台。已归档的 Editor 板清单及交互目标只保留为历史设计参考，不再代表当前代码、页面或快捷键；游戏 UI 章节不受此变更影响。**

## 1. 设计稿在哪

- 当前 Claude Code 连接（2026-11-12）：`penpot` 使用 HTTP transport，保存在用户 `.claude.json` 的项目 local 配置，不在仓库。已通过插件读取文件及页面，不只是服务端握手；浏览器需打开目标文件、运行 MCP Plugin 并保持标签页活跃。不得据旧客户端配置推定新会话已加载工具。
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

## 1.1 当前页面与功能拆分

2026-11-12 已完成重建迁移：文件 ID `24d9d841-759d-81bc-8008-b55a77e8150d`，现有下列六个分类页，共34个画板；原 `Game UI` 的34板在核对后删除，空原页也已删除。旧 `Editor UI` 页先前已由用户删除，不恢复。所有新页面从(0,0)开始按三列排列，步长2120×1280，保持板内相对坐标。旧脚本、旧对象ID和下文历史坐标不能直接用于新页面。

用户要求将拥挤的 `Game UI` 按功能拆成多个 Penpot **页面（Page）**，后续明确允许脚本重建后删除原板；采用先重建、校验再删源的顺序，不将整板栅格化。分组如下，**重建、校验与原页清理已完成**：

| 目标页面 | 保留的画板 | 数量 |
| --- | --- | --- |
| `Game UI — Home` | Home、Home v2 Compact、Home Power-On 01–03（旧方案保留追溯） | 5 |
| `Game UI — Song Select` | Song Select、Song Select v2、v2 Expanded | 3 |
| `Game UI — Gameplay` | Gameplay、Pause、Signal Lock、Track Handoff、Handoff Lock、Handoff Discharge | 6 |
| `Game UI — Settings` | Gameplay/Audio/Display/Effects 四设置板、Calibration 两板、Note Colors v2 两板 | 8 |
| `Game UI — Results` | Result、Result v2 六个阶段 | 7 |
| `Game UI — Grade Studies` | Grade Directions/Lighting/Surface/Set 五个探索板 | 5 |

音符配色两板的4条交互已重建并指向新板，`Flow 1`起点为新的配色编辑板；两个网格的轨道和单元格已核对一致。各页没有顶层散落非画板对象。历史方案仍保留在对应分类页，不因迁移成为当前产品规格。

第一轮已保存版本 `Before Game UI page organization · 2026-11-12`，跨页appendChild被活动页保护拒绝；当轮撤除空试验页，原稿未变。用户随后授权重建迁移，另保存 `Before verified rebuild migration · 2026-11-12`。跨页clone同样不可用，改用原生图形/文字逐层重建、图片沿用媒体填充；仅原稿已有SVG特效组通过SVG重新导入（内部ID/包装层会改变，需检查视觉与引用），不是整板截图。

当前页面索引（画板按下表次序，从左到右、从上到下排列；第i项从0开始，坐标为`(i%3*2120, floor(i/3)*1280)`）：

| 页面 | 页面 ID | 画板顺序 |
| --- | --- | --- |
| Settings | `30863453-d807-80da-8008-ef13478ac249` | Settings Gameplay、Audio、Display；Calibration Live、Result、Settings Effects；Note Colors v2 Draft、Readability |
| Home | `30863453-d807-80da-8008-ef15552ae400` | Home、Home Power-On 01、02、03、Home v2 Compact |
| Song Select | `30863453-d807-80da-8008-ef15552f0307` | Song Select、Song Select v2、v2 Expanded |
| Gameplay | `30863453-d807-80da-8008-ef1555336825` | Signal Lock、Track Handoff、Handoff Lock；Handoff Discharge、Gameplay、Pause |
| Results | `30863453-d807-80da-8008-ef155537079a` | Result、Result v2 01–06 |
| Grade Studies | `30863453-d807-80da-8008-ef15553a9dad` | Grade Directions v4、Lighting v4、Lighting v5、Surface Current v5、Set v5 Current |

全34板核对文字内容/字体、尺寸、相对坐标与可映射对象的填充/描边/阴影等属性无差异；SVG特效组内部包装层及ID改变，未做逐对象等价证明。新旧画板/可映射形状ID保存在文件插件数据`gameUiMigration20261112`。删源前另保存`Game UI rebuilt and checked — before source removal · 2026-11-12`（保留新旧两套供恢复），完成后保存`Game UI organized into six pages · 2026-11-12`。旧ID链接须转向新对象。

标签页重启会清空storage，须按实际对象恢复，不能依据超时重复创建。网格恢复需分阶段建立空容器、轨道，再逐个放入单元格，避免异步排布冲突。历史中断过程见verification。

技术检查：generateMarkup对部分SVG组重复输出子元素且带绝对viewBox，直接导入导致失真；已删除该错误的Track Handoff副本后重建，未动原板。原生SVG export可正常归一化但服务端曾500超时；备选为取完整首个根g、显式平移到局部坐标。Track Handoff及Gameplay原/新PNG数值对比分别平均通道误差约0.06–0.13/255和0.08–0.13/255，仍非逐像素相同；设置样板有文字亚像素重排。图片读取工具未提供可目视的图像，**未宣称视觉验收或全部无损**。迁移核对已完成，保留上述视觉验收边界，不将属性核对等同于全板像素一致。

## 1.2 在线功能首版设计稿

2026-11-12 新增 `Game UI — Online` 页面（ID `3765ced0-d076-80df-8008-ef32ce17ee18`），不改动前述六个分类页。当前为待用户评审的主页面草稿，不代表功能实现：

| 画板 | ID | 坐标/尺寸 |
| --- | --- | --- |
| `Game — Online v2 · 01 Login` | `3765ced0-d076-80df-8008-ef32fb966012` | (0,0)，1920×1080 |
| `Game — Online v2 · 02 Chart Store` | `3765ced0-d076-80df-8008-ef32fbce12f0` | (2120,0)，1920×1080 |

当前v2按用户反馈改为无边框探索：深色底、青蓝选中态、留白和明暗层次，不再逐块套切角描边。英文Space Grotesk，中文Noto Sans SC。登录页去掉宣传大标题和外框，左侧保留服务器身份/地址/切换入口，右侧使用下划线输入框。商店改为单列纵向列表视口（上下裁切半行、选中项前移/放大），右侧扩大封面与详情，内容只保留封面、标题、谱师、差分、BPM及下载按钮。删除永久免费口号、宣传介绍、版本/大小/上架说明和分页。所有服务器/曲目/BPM为设计示例，示例说明移至页面插件数据`onlineV2DesignNotes`，不占产品界面；封面是中性占位，不生成曲绘或使用原版素材。v1图层保留在各画板隐藏组`Archive · v1 before borderless revision`供回退，现有选曲页未改动。

点击登录按钮可原型跳转到商店（不是实际鉴权）；当前Flow 1起点为登录页。列表是静态滚动状态示意，不宣称Penpot已具备真实滚动/惯性；动效方向为选中项小幅位移放大、详情淡入切换，待评审后再细化。注册表单、服务器切换弹层、个人记录、下载中/失败、登录错误等详细状态尚未设计，按钮除该原型跳转外不具备实现能力。设计范围见 [网络服务第一阶段](network-services-phase1.md)。v2可见文本无画板边界溢出，商店PNG已导出；图片读取工具仍未提供可目视图像，不宣称视觉验收，等待用户在Penpot评审。

2026-11-12 后续细化：六行差分改为沿用选曲配色的等级方块，详情HARD/MEGA紧凑排列；登录页服务器信息上移，下方新增公告预览、日期与展开入口（虚构示例，公告展开尚未接交互）。恢复连接后确认先前账号浮层实际已保存，未重复创建。`Online · Account dropdown`（ID `b05bcbc7-af24-804f-8008-ef468b909f32`）位于(4240,0)、344×350，使用无描边底色/阴影、12px圆角、56px菜单行，展示本人记录、切换服务器、退出登录；记录行展示悬停样式。商店用户名已接open-overlay，位置(1500,92)，点击外部关闭、不遮暗整页；退出仅原型跳转登录，记录/切服未实现。新增展开状态板`Game — Online v2 · 03 Account menu expanded`（ID `00bc4a80-87db-80e4-8008-ef4a758dd3e4`），位于(0,1280)、1920×1080，供直接评审。四个顶层画板可见文本外边界无溢出，overlay目的地/位置/外部关闭配置已读回；展开PNG导出成功，但Read未显示图像，未目视或实际点击验收。下拉风格可用于其他入口，尚未做共享组件资产或完整状态集。

## 1.3 选曲页v3低边框探索（仅Penpot）

2026-11-12 用户要求以当前游戏实现而非旧Penpot稿为基线，只制作设计，不改游戏。只读核对`SongSelect.Layout.cs`、`SongSelect.QuickSettings.cs`、`SongDetailCard.cs`与`SongRow.cs`后，在`Game UI — Song Select`页新增两板，保留旧三板：

| 画板 | ID | 坐标/尺寸 |
| --- | --- | --- |
| `Game — Song Select v3 · 01 Collapsed` | `bac61716-ba66-8029-8008-ef4e0622a5e9` | (0,1280)，1920×1080 |
| `Game — Song Select v3 · 02 Expanded` | `bac61716-ba66-8029-8008-ef4e066e0c51` | (2120,1280)，1920×1080 |

保留左快捷设置、右侧列表内原位展开、底部难度/BEST/CLEAR/评级/START结构；不套用商店右侧独立详情。列表x590、宽1280、高736，行96/间隔22、展开392，封面288方形等取当前源码。移除大型CutPanel外框和整圈选中描边，使用低对比底色、青色短定位线、轻分隔及彩色难度块；开关仍有清晰轨道/滑块，START保留实色主操作。曲目/成绩为虚构示例，不引用原版素材。

两板示意STANDARD、BLEED/MIRROR/AUTO关闭；Hardcore强制BLEED语义不变（尚未单独画状态）。筛选仍灰色占位；试听仍只在展开后启动，暂停入口及只读进度保留，不新增拖动seek。原型只接首曲展开/收起导航，未实现真实滚动、音频、设置或START，动效继续遵守现有Full/Reduced/Off方向。几何和行为来源写入画板插件数据`designContract`。文本画板外边界检查无溢出，列表裁切为源码736高；展开PNG导出成功，但Read未呈现可目视图像，不宣称视觉验收。等待用户评审后再决定是否落地游戏。

## 1.4 切角与明暗层级修订（仅设计，待评审）

2026-11-12 用户指出无边框稿顶部偏高、切角丢失且整体偏暗。已对Online当前四板与Song Select v3两板试调：背景`#0D1524`、普通表面`#18283D`、选中/展开`#24445A`；主文字`#F0F5FF`、次文字`#AFBED2`、辅助文字`#899DB7`，禁用筛选保留暗色。选曲品牌字号34、局部y43，返回字号22、y48。主按钮、列表底面、模式选中底面、差分块恢复切角实色轮廓，不恢复全区域描边；开关/试听保持圆形操作语义。Online公告增加较亮切角底面，下拉菜单外轮廓也恢复小切角。被替换矩形以隐藏的pre-cut backup保留，原型交互复制到新路径；旧Online v1隐藏组不变。六板可见文字外边界及交互数量已检查，选曲展开PNG导出成功；未做全板目视或实机验收，没有修改游戏源码。

2026-11-12 未激活按钮试色：先在Song Select v3两板试用`#192D3F`（背景`#0D1524`与激活底面`#24445A`的近似中间色），为Hardcore未选项、全部设置、返回增加无描边切角底面，关闭态开关轨道使用同色。激活STANDARD保持原底色/青色定位线，真正禁用的筛选仍灰暗；未扩展到Online，待用户看效果。此处“未激活”指可操作但未选中，不等于disabled。

2026-11-12 选曲v3收尾：用户反馈其余方向基本无异议，仅调整返回/START内容为按钮内水平及垂直居中；两板普通歌曲行共18个差分块由44×44改为44×34，数字21→18，保持中心位置、颜色与切角。未调整底部大难度切换按钮或Online差分。本次仅设计，不作为游戏实机验收或实现授权。

## 1.5 设置 / 调色 / 校准 v3（仅Penpot）

2026-11-12 以游戏SettingsScreen.Layout、NoteColorsScreen.Layout、CalibrationScreen.Layout为语义依据，在`Game UI — Settings`保留旧8板，新增下列8板。采用§1.4的亮度层次、中间色未激活按钮、切角实色底面、弱化大框与按钮文字居中，不修改游戏实现。

| 画板 | ID | 坐标 |
| --- | --- | --- |
| Settings v3 Gameplay | `33297f09-0040-80b1-8008-ef5884cd1c60` | (0,3840) |
| Settings v3 Audio | `33297f09-0040-80b1-8008-ef58881fdd13` | (2120,3840) |
| Settings v3 Display | `33297f09-0040-80b1-8008-ef58b644237c` | (4240,3840) |
| Settings v3 Effects | `33297f09-0040-80b1-8008-ef58b7b7c3d6` | (0,5120) |
| Calibration v3 Live | `33297f09-0040-80b1-8008-ef58dfd66234` | (2120,5120) |
| Calibration v3 Result | `33297f09-0040-80b1-8008-ef58ead7d7a2` | (4240,5120) |
| Note Colors v3 Hold / Draft | `33297f09-0040-80b1-8008-ef59072448a5` | (0,6400) |
| Note Colors v3 Readability / Hold | `33297f09-0040-80b1-8008-ef592324b413` | (2120,6400) |

全部1920×1080。修正旧稿Gameplay将JUDGE TEXT误写HIT EFFECTS、前三页缺Effects标签/校准入口；Display仍只展示已有UI MOTION，不虚构设置项。四tab、校准入口/返回/重试/应用及配色观察来回导航指向新v3，均只是原型，不实际保存值；已选tab不自导航。校准示例建议偏移由+3修正为按5ms取整的+5ms，不修改实际计算规则。

调色只改周边界面，保留六类图标、音符陈列、SV/Hue、三种测试背景和近似色比较区的几何/颜色/描边。核对13处受保护区域内容一致（同名Type glyph按所属类型匹配）。禁用重做不按可操作未激活按钮提亮。

按用户中途反馈补查对齐：修正校准重建底面相对文字的4px偏移，返回/目标加减/APPLY/RETRY文字匹配按钮框居中；Effects页脚配色入口与RESET共享y946中心线；观察页类型标签恢复色块右侧固定区域，避免整框居中挤占色块。七组重点按钮居中属性读回通过，8板文字无外边界溢出。Effects/调色草稿PNG已导出但非最终对齐后的完整目视验收；未运行产品，等待用户评审。预设/RGB等详细弹层未在本轮新增，不把导航稿当作全部手势已完成。

2026-11-12 头部纠偏：用户截图指出Settings各页标题视觉位置不一致，之前仅检查按钮文字框居中不足以发现问题。现四张Settings v3统一按Effects参考：标题字号36、字距0、文本框(176,58,700,56)，返回切角按钮(64,50,72,72)，字形36居中；标题和按钮中心y86。Calibration两板标题(176,62,520,56)、返回(64,54,72,72)，中心y90，副标题y116。返回按钮由96缩至72，内容区不变。实读标题textBounds视觉中心与按钮中心相差约0.4px，六页一致；未据此宣称像素截图目视验收。

2026-11-12 头部最终修订（覆盖上一段Calibration定位/字重）：按用户要求，8张v3页主标题统一Space Grotesk 600、36px、字距0，同时设置整段TextRange以避免仅顶层属性一致。四张Settings保留(176,58)标题位置及72px返回按钮；Calibration改为与NOTE COLORS/READABILITY相同：标题(152,30)、高44，副标题(154,80)、16px，返回按钮(48,28)、76×76，箭头复用调色页形状。逐页读回顶层字重、文字范围字重及variant均600；以此为当前头部方案。

## 1.6 v3 游戏外观接入（当前工作区）

2026-11-12 用户确认上述设计，并授权接入游戏，约束为**只改外观，不改行为**。选曲、四 tab 设置、Calibration 两状态和音符调色/可读性观察已接入 v3；§1.3–1.5 中“仅 Penpot / 未授权实现”是此前设计阶段记录，不再描述当前工作区。Online 不在本次实现范围。

- 客户端 `ui/common/MenuStyle.cs` 集中维护新菜单色值与真实 Space Grotesk 600 标题字体。`CutButton` / `TechButton` 的低边框为显式启用项，旧调用方默认外观不变；共享 `presentation/UiFonts` 未全局换色，首页、结果、游戏舞台及编辑器不随菜单改色。
- 选曲保留原列表尺寸、行高/展开高度和视口滚动，移除外围大框，使用切角层次底面、小等级块、底部 BEST/CLEAR 分列和难度色条。两项读数仍取同一成绩记录；无数据/加载中的状态一并保留。关闭态圆形开关绘制在原输入矩形内；返回及全部设置的矩形按新布局移动。
- Settings 标题(176,58)、36/600，返回(64,50)、72方形；Calibration 与调色共用(152,30)标题、(48,28)76方形返回及矢量箭头。Settings 大内容框和 Calibration 统计底板保留节点/入场登记但绘制透明，避免改变原有进场时序。
- 原控件输入方法、事件接线、设置数值/保存、试听生命周期、校准采样/公式、配色草稿/比较/撤销/应用保持原逻辑；真实音符样张、HSV操作区及三种测试背景不替换为设计示例。
- 新字体来自 Google Fonts 的600字重交付，原样加入客户端，归属及 SHA-256 记录在 THIRD_PARTY_NOTICES / asset-provenance。中文继续沿用现有系统字体回退，不宣称跨设备与 Penpot Noto Sans SC 像素一致。

构建和回归证据见 verification；尚未进行这轮游戏窗口目视/触屏验收，设计稿批准不等同于游戏实机批准。

## 2. 板清单（画布坐标 / 尺寸）

旧 Editor UI 画板与工作台探索已移至 [历史设计](archive/art/2026-10-editor-ui-design.md)。它们不代表 Avalonia 工作台；当前能力见 [制谱器说明](dyna-maker-uv.md)。

**原Page「Game UI」的历史清单**：以下坐标是迁移前参考，不再是当前定位依据；实际页面与坐标按§1.1查找。尺寸以1920×1080为主。
基础板中的旧 Song Select 和 Result 仅作历史对照；当前分别采用 Song Select v2 与 Result v2。
下面表格保留历史内容说明，不代表每块参考板都是当前UI；不可按旧坐标操作新页。

| 板 | 坐标 | 尺寸 | 内容 |
| --- | --- | --- | --- |
| Game — Settings (Gameplay) | 0,0 | 1920×1080 | GAMEPLAY tab 四行：JUDGE OFFSET / DROP SPEED 大步进器、JUDGE TEXT 大开关、MODE 二段器（原 HIT EFFECTS 大开关已由 EFFECTS 板的三段器取代） |
| Game — Settings (Audio) | 2120,0 | 1920×1080 | AUDIO tab：三路音量粗滑条 + 底部 RESET DEFAULTS 行 |
| Game — Signal Lock | 4240,0 | 1920×1080 | 过场：扫描线定格帧（斜线底纹 + 三分屏切角分片 + 扫描线 + 左下信号标记） |
| Game — Track Handoff | 6360,0 | 1920×1080 | 过场「充电」：封面居中 + 四角斜对角电缆（25%），每缆 2 个亮脉冲；状态 `PLAYFIELD / LOCKING` |
| Game — Handoff Lock | 8480,0 | 1920×1080 | 过场「充满锁定」：电缆 100%、四节点点亮、封面描边 100% + 内侧 8% 辉光；无判定线 |
| Game — Handoff Discharge | 10600,0 | 1920×1080 | 过场「放电」：电缆 12% 余辉、四节点小爆闪、封面白热闪光 + 双圈冲击波、判定线 25% |
| Game — Home | 0,1280 | 1920×1080 | 旧首页板（已被 Home v2 取代）：左列 eyebrow / DYNAMITE UNIVERSE 两行 wordmark / pink 短轨 / 副题，切角 SELECT DESTINATION 面板内 1 大 2 小切角按钮；右侧 3D 能量核心示意；右下版本串 |
| Game — Home v2 · 01 Compact | 0,11520 | 1920×1080 | **当前首页**：96px 主边距、无外框操作区、弱化描边切角底板、近全高右侧主视觉；CREDITS / LOGIN 置灰，副按钮两行，段位模式与联机大厅为新增禁用占位。2026-10-29 已接入客户端（`Main.cs` + `ui/home/`），核心是 Mk VI 动态模型与 canvas 弥散光；具体省略项和实施取舍见下文与 [交接](<handoff.md>) |
| Game — Song Select | 2120,1280 | 1920×1080 | 选曲（`SongSelect.cs`）：顶栏 logo + 返回；左详情板（封面占位 + NO COVER、曲名 / 曲师·谱师 / 时长·Note）；右曲库板（列表头 + 6 行 SongRow + 视口裁切的第 7 行残片）；底部难度实心键 + BEST/CLEAR 条 + 等级字 + START |
| Game — Gameplay（旧 HUD 快照，现以 v8 为准） | 4240,1280 | 1920×1080 | 游玩 HUD 定格帧：程序化背景纵深、∪ 形判定线（共享基准 y=861，客户端当前 y=901 + 两侧竖轨）、Mixer 装饰粉条与光标、三轨 Note（Tap/Drag/ExTap/Hold+body/Mixer/BarLine）、顶部六 pill + 暂停钮、判定线进度段、左下曲目信息板、右下分数、线下 Combo、线上判定字 |
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
| Result v2 · 01…06 | x=0/2120/4240/6360/8480/10600，y=5120 | 当前屏风结算六板；已接入，用户确认与设备边界见 verification |
| Home Power-On 01 Dark / 02 Zone Key / 03 Zone Fill | y=6400 | 分区布光探索，**客户端未接入** |

当前评级使用自绘双刃方向的 v5 + 表面电流；源与管线见 [Blender 管线](<blender-pipeline.md>)。
不恢复 v4 薄光或未采用的 v6 试验，不把旧板上的“未接入”当成现在的结算状态。

Result v2 六板后缀为 `01 Shutter Close 300ms`、`02 Shutter Hold 800ms`、`03 Shutter Open 1250ms`、
`04 Grade First 1750ms`、`05 Settled`、`06 All Perfect`。600ms 合拢、320ms 停顿、480ms 拉开，
评级先入场、信息后浮现，总约 2650ms。几何、资源和真实触发边界见 [结算 v2](<result-v2-design.md>)。
恢复源在 [design/result-v2/source](<../design/result-v2/source/>)；不再沿用旧电流回收转场。

首页仍保留已经实现的 3D `HomeEnergyReactor`。新概念改为电影式分区启亮：待机核心 → 主光/填充 →
粉色轮廓、底部反弹与灯舱；不是横向扫光，不代表客户端已经改。脚本 `.tmp/penpot_home_power.py`。

### 当前首页 v2 实施边界

- Penpot 板已在 SELECT DESTINATION 下增加第二行「段位模式 / RANK MODE / OFFLINE」与
  「联机大厅 / ONLINE LOBBY / OFFLINE」，并修正主按钮文字被不透明底板遮盖的层级。
  客户端只有游玩、设置接真实导航；谱面工坊、段位模式、联机大厅和 CREDITS / LOGIN 均为禁用展示。
- 不实现模型右下的 DETONATION ORRERY / CORE ONLINE、左上 COMMUNITY RHYTHM SYSTEM / DU-01、
  左下 DU / COMMUNITY EDITION 三组注释小字；右下版本串继续使用 `Main.BuildDescription()`。
- 参考环已接入。稿内模型背景的径向环境辉光与 Utility / separators 两个刻度线未复制；
  LOGIN 去掉 `↗`，避免禁用入口仍暗示外链。
- Mk VI 使用当前立体折梁模型与近/远双层 canvas 弥散光，默认 `u_glow=1.6`。
  本机设计源、Blender 重建、独立 Godot 预览和渲染器差异见
  [Mk VI 设计源与预览说明](<../design/home-orrery-v6/README.md>)；独立预览不证明客户端或 Android 效果。

### 2.1 当前选曲 v2

**模式关系**：MODE 只有 **STANDARD / HARDCORE 两档，互斥二选一**；
**BLEED 是游玩修饰开关，不是平级档位**——STANDARD 下可自由开/关，**HARDCORE 下强制开启且不可关**
（锁定态：轨道保持 cyan 但整体压到 55%、方块停在右侧 60%、左侧空位放 20px 挂锁图标，副文案换成
`HARDCORE 下强制开启`）。后续的「段位模式」= STANDARD + BLEED；首页只有其禁用入口，
不代表该模式已经实现，也不因此给当前选曲页增加控件。

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
  `MOTION = OFF` 时归零），触摸拖动与滚动条拖动保持 1:1 直接操纵。模型见 [选曲滚动合同](#选曲滚动合同)。
- 预览整段加载音频，仅缓存当前曲，切歌/离页释放；大 WAV 首次加载可能卡顿。
- 展开/收起卡走 0.20s 高度过渡（96↔392，`ClipContents` 裁切、行跟随滑动；收拢落点才交还
  选中行，途中点别的行从半截高度干净反向）；`MOTION` 非 Full 时保持瞬时切换。
- 切歌/切难度时卡内曲名/曲师/谱师 0.08s 淡出→换文→0.12s 淡入（换文由代际守卫，快速连切
  不会写回旧文本）；封面在谱面就绪后从 0 淡入 0.15s；`MOTION = OFF` 全部瞬时。
- client 侧 hover 统一 0.10s 过渡：共享原语 `client/scripts/ui/common/UiHoverFade.cs`（与编辑器
  `EditorMotion.HoverFade` 同思路；SongRow/CutButton 原 0.16s `HoverDuration` 已归一到
  `UiHoverMotion.Seconds`），覆盖曲库行、MODE 行、全部设置行、CutButton、预览传输键；
  `MOTION = OFF` 瞬时。
- 进场默认收起、不自动试听。设计板的首行展开只是演示态，不改变客户端默认行为。
- BLEED 锁定/持久化、MIRROR 映射、AUTO 不写成绩均已接入；BLEED 数值与耗尽结算见玩法规格 §4.3。
  镜像规则只走 shared `GameplayStageGeometry`，中轨轴 2.5，输入逆映射只施加一次，见当前交接。
- 收起与展开底部操作条一致；展开卡不放 START/难度切换，时长/Note 行当前不显示。

#### 真正待定/未做

1. 筛选/排序条件、弹层形态和与搜索叠加的空态；按钮保持 55% 压暗占位。
2. 搜索清除 ✕、显式自动聚焦、匹配排序等增强；不能把状态变体表当作已实现控件。
3. BLEED 血条已由 [HUD v8](<gameplay-hud-v8.md>) 接入；专用耗尽结算待扩展，段位模式方向仍需产品拍板。
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
| 游玩 / 编辑器 Auto 预览 | 命中反馈 | 两端已复用 `GameplayHitBloom` + `GameplayHitParticles`；放置提交缩小爆发不据此视为已实现。参数见 `ui-motion.md` §9.5 |

当前 handoff 三板采用居中布局（封面居中 + 斜对角双线电缆）：
封面居中 + 四角电缆充电释放；标题组 y=840/900。客户端实现以 `TransitionOverlay.cs` 为准。
之前的左封面 / Relay 汇聚信号场方案已废弃。三块板共用同一套构图，只切换充电相位（充电 → 锁定 → 放电）。

`Game — Signal Lock` 的**布局权威是 `client/scripts/ui/transitions/TransitionOverlay.cs`**（程序化绘制，非自由设计）；
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
  giga / tech `#F87171`；Note 配色看 `presentation/game/NoteVisualSpec.cs`。
- 字体 Space Grotesk；常规切角语言为**左上 + 右下**；HUD v8 左右统计块镜像、暂停四角对称是已采用的例外。中文界面文案按源码原文写（游玩 / 设置 /
  谱面工坊 / 继续 / 重开 / 返回选曲 / 点击切换难度 …）——Penpot 端对 Space Grotesk 缺字会自动
  回退到 CJK 字面，不会出现豆腐块，所以不必为了排版把中文改成英文。
- **字距**：板面 `ls` 在 Godot 侧只能用 `FontVariation.SpacingGlyph` 表达，**它只有整数像素**，
  所以板面的 `ls 1.5`（HUD 的 BAR·BPM 行、菜单 TIME/CENTER/WIDTH 行标签、路径手柄与放置阶段标签）
  一律按仓库既有约定编码为 **2**；`ls 1 / ls 2 / ls 3 …` 照整数直取。不要去改板面的 ls 值来迁就
  实现，也不要为半像素字距另造逐字绘制。
- **少文字原则**：不写实现注脚类文字（运行时、规则集名等）；重装饰轻信息的内容不做
  （歌曲详情不增加装饰性波形；这不意味着制谱器的音频波形需求已完成或被取消）。
- 编辑器几何复用游玩共享基准，不跟随客户端舞台的 40px 下移；顶部 HUD 的 8px 偏移也只属于客户端。
- 游戏页板的数值一律取客户端源码/常量：判定线 y=901、游戏底线跨 x=0..1920、侧轨 x=184/1736 且上接 y=72；旧中央 Mixer 装饰已在游戏端移除，HUD 详见 v8 专题。中心轨 x=280+273.2·(P+W/2)、侧轨 x=184+剩余·k /
  1736−剩余·k（k=SideDistScale，当前保留 691/790≈0.8747，2026-10-17 用户实机反馈基本通过）、侧轨 y=880−115·(P+W/2)、Note 视觉缩放 0.95（`GameplayStageGeometry` 加客户端舞台偏移）。
- 游戏 hover 采用 `UiHoverFade` / `UiHoverMotion.Seconds`，100ms，Off 瞬时；旧编辑器 helper 和窗口拖放记录见历史设计，不作为当前 Avalonia 接线说明。

## 4. 当前设计边界

- 制谱器正式主线为 Avalonia Shell + Godot 原生舞台，操作与未完成项以 [制谱器说明](dyna-maker-uv.md) 为准；旧整页 Settings/Pack/浮动 chip/字号缩放等不恢复。
- 旧 Editor 画板、手势目标与实现流水归入 [历史设计](archive/art/2026-10-editor-ui-design.md)。放置提交爆发等旧目标未因此获得新实施授权。
- 游戏 HUD 以 [本体 v8](gameplay-hud-v8.md) 为准，旧 Game — Gameplay 板只用于追溯；模式色、切角、布局和验收不能由旧板覆盖。

### 选曲滚动合同

滚轮/触摸板推动目标值，按 `current += (target-current) × (1-exp(-delta/tau))` 追随；client 固定 tau=0.112s，UI Motion Off 时为0。触摸/滚动条拖动保持1:1，程序跳转、内容刷新与外部定位立即落目标并取消旧惯性。当前对象为 `SongListViewport` / `SongListScrollState` / `ScrollInertia`；旧编辑器的 SCROLL INERTIA 设置不代表现行工作台能力。

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
