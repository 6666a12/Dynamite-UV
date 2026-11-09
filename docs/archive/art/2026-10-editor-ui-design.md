# 2026-10 旧 Godot Editor UI 设计与实施记录

> 2026-11-09 从现行设计文档分离。旧 Godot 外壳已删除；本记录不是当前待办、能力说明或恢复旧架构的依据。现行路线见 [制谱器说明](../../dyna-maker-uv.md)，游戏施工安全见 [UI 设计](../../editor-ui-design.md)。
> 裸源码路径/旧锚点按历史时点解释。保留原方向和当时“已接入/未接入”表述，不外推到现行 Avalonia 工作台。

## 旧 Editor 画板

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



## 旧 Godot 编辑器字体与缩放

- **正文字重与字号偏差（2026-10-08 拍板）**：编辑器正文从 SpaceGrotesk Regular 升到 **Medium**，
  设计稿字号统一经 `editor/scripts/common/EditorFonts.cs` 的 `Pt()` 先加一档（≤13 +2、14–18 +1、>18 不变）、
  再乘用户可调的 **UI SCALE** 档（90 / 100 / 110 / 125%，默认 100% = 只加这一档），
  `UiFonts.TechBold` 等强调字不动。原因是编辑器视口固定 1920×1080 但窗口开 1600×900
  （`canvas_items` 等比缩放打 0.833 折），物理像素上字偏小，而 Regular 又偏细，实测可读性差。
  这是有意偏离设计板标注的**全部编辑器页面**的观感调整，不是某块板的个案；今后要再调观感
  只改 `EditorFonts.cs` 一处（字体、字号档位与 UI SCALE 的换算顺序都集中在那里），
  不要把 Medium/Pt/AddThemeFontSizeOverride 散改回各页面。UI SCALE 的四档、立即生效机制、
  字距不随缩放的选择见 §4 第 15 条。


## 旧 Godot 编辑器 hover、拖放与后续设想

- **hover 统一过渡（2026-10-08 拍板）**：全部 hover 可点元素从瞬时换色改为约 0.10s 的颜色
  lerp（共享 helper `editor/scripts/common/EditorMotion.cs`：`HoverFade` 单值版给 hotspot 少而固定的
  页面，`HoverFadeMap` 键值版给动态/大量 hotspot 的页面；时长集中在 `HoverMotion.Seconds`），
  端点色与原瞬时 hover 一致，只有中间过程平滑；`MOTION=OFF`（设置真源）时推进直接跳终值。
  各页面在自己的 `_Process` 里推进、全部到终值即 `SetProcess(false)`，不空转；右键菜单 Open
  重建元素列表时 hover 值表整体清空（下标含义全变，残留值会画到错误元素上）。
  client 侧同口径原语是 `client/scripts/ui/common/UiHoverFade.cs`（0.10s 同源），覆盖范围见 §2.1。
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

以下保留已讨论的产品方向；现有 shell 的具体已接入/缺口以 [制谱器说明](<../../dyna-maker-uv.md>) 为准。
Settings 页、浮动 chip、右键菜单的步进器、已有路径节点的 curve/judge 与
CURVE/JUDGE/DELETE NODE 行已在 2026-10-07 接入（未实现的行按占位惯例 55% 压暗），
路径的逐段真实投影（`V2PathEvaluator`）、`PATH_FIELD` 菱形手柄与**双击插入路径节点**在 2026-10-08 接入
（节点菜单 ADD NODE 因此改为真实动作，文案从 `ADD NODE · DBL-CLICK PATH` 改成 `ADD NODE · AT CURSOR`，
它按游标插入而不是单纯提示手势）；手柄的「沿路径依次点亮」stagger 仍是动效目标，当前静态全部显示。
**放置手势视觉**（§4 第 9、10 条，设计板 `Editor — Placing`）在 2026-10-08 接入
`editor/scripts/authoring/AuthoringCanvasPlacement.cs`：导引十字线 + 线上三读数 + 虚线幽灵描边 + 阶段 1 的宽度
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
只读、TIMING 隐藏）见 [制谱器说明](<../../dyna-maker-uv.md>)（当前主线已切换到 Avalonia + Godot，以上仅为当时实现记录）。

1. **三层编辑器架构**：主界面是裸舞台（判定线/音符渲染与实机一致，底部判定线全宽；
   HUD 只有纯文字——左下歌曲标签（仿游戏内：歌名 + 难度色 + LV）、右下时间/Bar/BPM、
   右上 GRID/SNAP）；画布上的编辑信息只有三样：网格三档（小节线 160px／拍线 40px／
   细分线 10px 共用同一条点阵——小节 94 在 y=218，小节线 2px 白 22% 核心、拍线 1.5px
   白 10%、细分线 1px 白 4.5%，只在小节／拍线上各加一层白→透明的贴身软光晕，不做整屏
   距离渐隐；实现时按缩放自适应密度）、右缘小节标尺（小节刻度 + 书签菱形 + 当前位置
   三角，即整曲缩略时间轴）、底部 2px 进度细线。选中即出浮动 chip（高频微调）；
   右键菜单承载全部功能。
2. **放置手势行为参考**（固定参考快照中的 `mouse.js`，来源见 [制谱器说明](<../../dyna-maker-uv.md>)），所有
   Note 和路径 node 统一走这套三阶段单手势：
   - 阶段 0 按下：time + center 跟随光标；位置吸附 0.1，按住 X 精调 0.01；
     时间吸附网格，按住 Z 自由；
   - 阶段 1 拖动：宽度 = `preCoverWidth + |Δ|·Δ`（二次曲线响应，Δ 为光标到中心的
     轨内距离），步进 0.05、下限 0.1；
   - 松开提交并**记忆宽度**（`preCoverWidth`），下一个对象以相同宽度起手。
   - Hold/Mixer 追加阶段：拖时间轴定尾（钳制不早于头）。
   - 双击路径插入 node：双击点定 time+center，第二击不松手直接进宽度拖拽；
     落在路径身体上时 center/width 起手值**采样路径求值结果**（只保证插入采样点不跳变；非线性段切开后邻域可能形变，不承诺整段形状或通用亚像素误差，详见 [当前能力边界](<../../dyna-maker-uv.md>)）。
   - 手势导引（对照原版 playView）：过 note 时间一条 pink 2px 全宽时间线（两端各
     200px 渐隐）、过 note 位置一条白色 3px 纵贯位置线；读数值（位置、宽度）贴在位置线
     两侧、分数式时间贴在时间线上方左侧，各带 1px 偏移的深色底衬充当原版的黑描边；
     已放置 note 的锚点用 26×26 色块标注与笔尖的同异（蓝=同位置、红=同宽度、品红=两者
     皆同）。
3. **两级选中**：点身体选中整个 Note；选中后路径显示菱形节点手柄，再点手柄选中
   单个 node。node 菜单有 TIME/CENTER/WIDTH 步进器、CURVE TO NEXT 六段选择器
   （linear/hold/三种 Quad/三种 Cubic/三种 Sine/smooth，共 12 条；Scroll 不含 smooth）、Hold 专属 JUDGE
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
   **已落地（2026-10-08）**：`editor/scripts/pages/SettingsPageOverlay.cs` 按 §3 语言整页自绘，
   三组行与控件几何取 `.tmp/penpot_build4.py` 的 `settings_board()`。
   真接点七项——MOTION（关闭编辑器自己的淡入）、UI SCALE（正文字号倍率四档，见 §4 第 15 条）、
   DEFAULT GRID（新草稿与当前文档的网格分母）、
   SNAP（画布空间吸附步长）、GRID OPACITY（三档网格不透明度倍率）、BACKGROUND DIM
   （`GameplayBackdrop` 的 `Modulate` RGB 压暗）、SCROLL INERTIA（滚轮惯性强度，见 §4 第 14 条），
   值落在 `editor/scripts/common/EditorSettings.cs`
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
    决策与边界见 [Storyboard 提案](<../proposals/storyboard-editor-proposal.md>)。
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
    - **两个读取面与立即生效机制**（重点，见 `editor/scripts/common/EditorFonts.cs`）：
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

