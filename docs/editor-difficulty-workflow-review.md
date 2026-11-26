# 制谱器审查：从一张谱开始做第二张

审查基线：2026-11-19，`be6bda9`，Windows。下文的复现结果和源码行号以这次审查时的版本为准，不代表全部问题仍然存在。

**后续实施范围：**主窗口内「添加差分」已接入三种录音来源、完整 BPM 表和偏移的独立复制、可选 Scroll 与试听预检。§2 的 A1–A4 已处理；普通复制也改为给 CUSTOM 副本分配不冲突的名字，新建表单的第六项标签已统一为 TECH。§3 的长歌底栏范围和三分格点对轨也已修复。封面、录音和录音继承设置已移出谱面撤销历史；§3 的旧录音替换重做路径保留为历史记录。其余问题不要据此标为已修复。当前操作见[制谱器说明](dyna-maker-uv.md)，执行条件与未覆盖项见[验证记录](verification.md#最近验证记录)。

## 1. 新建空差分究竟带走了什么

用一段 16 秒 WAV 建包：原差分在小节 `0` 是 120 BPM，在 `4+1/3` 变为 180 BPM，音频偏移 `+0.25 s`。保存后从实际「添加差分」表单取值，走工作台使用的建谱命令：两张谱解析到了同一个 WAV，但新谱的 BPM 表只有 `0 → 150`，偏移为 `0`，没有音符。表单隐藏了音频和对轨字段。即使向建谱请求传另一套 BPM、偏移或音频，添加分支也不读取它们。[创建窗口](../editor/shell/CreateChartDialog.axaml.cs#L27-L50) · [请求构造](../tools/chart-editor-core/EditorCreateRequest.cs#L43-L55) · [现有测试明确要求固定 150](../tools/chart-editor-tests/Documents/EditorCreateRequestTests.cs#L24-L33)

| 操作 | 音频和试听从哪来 | 对轨和音符从哪来 |
|---|---|---|
| 添加空差分 | 新条目不写 `audio` 或 `preview`，回退到**包默认**。即使当前差分使用另一版录音，新谱也不会跟随它。 | BPM 重置为 150，偏移为 0，Scroll 和音符留空。 |
| 复制当前差分 | 复制条目里的覆盖引用；可以指向同一个包内文件。 | 复制整张谱的 BPM 表、偏移、Scroll、音符和路径。适合在旧谱上改编；不是「只要对轨，不要音符」。 |
| 文件 → 谱面包 / 差分设置 | 可以为当前差分单独覆盖音频和试听；正常保存会把新媒体写进包。两种覆盖是独立开关。 | 可改当前差分偏移；BPM 表在 TIMING 面板改。 |

你记得的格式能力确实存在：[包 Schema 的条件](../schemas/chart-format-v2/pack.schema.json#L55-L75) 允许没有包级音频，只要每张差分各自有音频；[差分条目](../schemas/chart-format-v2/pack.schema.json#L153-L169)有独立 `audio`、`preview`。游戏选曲试听读的是[当前差分解析后的音频与试听区间](../client/scripts/ui/song_select/SongSelect.Loading.cs#L30-L44)。不需要改 Schema，也不必把差分各自的 BPM 表上移成包级全局表。

**比多点几次更严重的边界：**准备一个无默认音频、每张谱都指定录音的合法包，编辑器能打开它；点「添加差分」则报 `chart has no entry-level or pack-level resolved audio`，保留原谱。不能靠“先创建，再去设置选音频”绕过，因为新差分根本没创建成功。[添加时的验证和回滚](../tools/chart-editor-core/Commands/ChartCommands.cs#L14-L26) · [音频解析约束](../shared/Chart/V2/Validation.cs#L35-L45)

### 建议的创建规则

- 「+ 差分」仍然创建**空谱**，显示参考差分和实际录音。音频可选：沿用当前差分的录音、跟随包默认、另选文件。前两项解析相同时不必给用户列两个相同文件；没有包默认时禁用对应选项。
- 同一录音默认带入参考差分的**整张 BPM 表和音频偏移**。复制精确小节值与 BPM 数值，各谱后续独立修改。不要只抄首个 BPM，也不要通过显示秒数反算分数。新谱不带音符或节点；Scroll 是视觉编排，默认不带，需要时单独勾选。
- 换录音时让人明确选「沿用对轨」还是「重新对轨」。同一曲子的不同混音可能只差前导静音，也可能换了段落或速度；不能单看扩展名、文件名或曲名决定。显示实际生效的偏移、试听起点和时长，超过新录音长度时指出是哪张谱，不静默截断。
- 创建和选媒体作为一次可撤销操作。窗口只采集选择；建谱核心负责产生独立时间表，文档会话管理待导入媒体与撤销，writer 继续做最终严格校验。不要用 UI 临时执行「复制全部 → 删除所有音符 → 再换音频」拼出结果。

## 2. 优先修：改到了用户没选的东西

| 编号 | 可复现的操作与后果 | 修复应保证什么 |
|---|---|---|
| A1 副本换音频串到原谱 | 原差分使用独立 WAV，保存，复制该差分；只在**副本**的设置中换另一段 WAV，保存重开。两张谱仍指向 `audio/hard.wav`，原谱的音频字节也换成了新录音。探针里原文件 SHA-256 前缀由 `782672F2C818` 变为 `FA8984F342CB`。这不是两张谱有意跟随包默认。[复制](../tools/chart-editor-core/Commands/ChartCommands.cs#L44-L58) · [替换目的地](../tools/chart-editor-core/Documents/EditorPackageSettings.cs#L43-L57) | 共享只读引用可以保留；“只替换本差分”发现目的文件还有别的引用时，为本差分分配未占用的媒体路径。验收应比较原谱和副本各自的文件内容，不能只检查路径存在。 |
| A2 已取消的录音仍被带走 | 在已保存的包里为一张谱应用独立 WAV；还没保存，又取消覆盖并应用。保存后条目的 `audio` 确已清空，但没被引用的 WAV 仍进了包；若保存前先移走这段已弃用的外部 WAV，保存反而失败。整目录分享会把弃用的文件一并交出去，商店预检也有明确的未引用文件拒绝规则。[清除只改引用](../tools/chart-editor-core/Commands/PackCommands.cs#L147-L174) · [保存快照与首次资源合并](../tools/chart-editor-core/Documents/EditorDocumentSession.cs#L77-L109) · [商店检查](../server/Catalog/PackageInspector.cs#L30-L47) | 以本次保存快照实际引用的文件来决定复制哪些待导入资源，同时保留其他差分仍在使用的文件。覆盖首次保存、原目录保存和另存；不要扫描并删除用户目录里的任意额外文件。 |
| A3 取消多选后，属性可能改到刚移除的音符 | 选 A、B，再 Shift 点掉 B。舞台构造的选择集合只剩 A，但仍用刚点击的 B 作为主对象。把该事件送入真实 Shell，Inspector 显示 B；把宽改为 3 后，A 仍为 1、B 变为 3。[舞台构造事件](../editor/scripts/stage/EditorPreviewController.cs#L394-L417) · [Shell 接收](../editor/shell/MainWindow.axaml.cs#L422-L442) | 主对象必须属于最终选区。属性面板、舞台高亮、复制和删除使用同一选择；单对象、取消最后一项、路径节点与父音符都要有回归。此处复验了消息和命令，未跑原生鼠标投递。 |
| A4 选中对轨事件，Delete 仍可能删上一个音符 | 选音符 A，再点 TIMING 的 BPM 行；文档选择已是 BPM，Shell 仍保留 A。经全局 Delete 分发后，A 消失，BPM 行还在。对轨面板自己的「删除」按钮是另一条命令。[对轨选择](../editor/shell/TimingView.axaml.cs#L175-L215) · [全局删除](../editor/shell/MainWindow.axaml.cs#L973-L981) | 根据当前工作区和选择类型分发，或在事件列表明确不接管 Delete；无论如何不能删旧音符。修复前用 TIMING 区自己的按钮。需补真实窗口键盘焦点验收。 |

## 3. 其他会打断制谱的情况

以下除特别注明外，均在本轮临时谱包、真实核心或无窗口 Shell 中观察到；不等于在已分发测试包上做过鼠标、键盘和听感验收。

| 场景 | 观察结果和开发建议 |
|---|---|
| 从自定义难度继续改编 | `CUSTOM / EXTRA` 复制时只改 ID 和谱面文件名，沿用 `difficultyKey=EXTRA`，随即因同包重名被拒；原谱保留。让复制窗口先收新难度名或建议不冲突的名字，并保留唯一性校验。[复制命令](../tools/chart-editor-core/Commands/ChartCommands.cs#L44-L64) · [custom 唯一规则](../shared/Chart/V2/Validation.cs#L53-L63) |
| 空差分拖住整个包的保存 | 已经落盘的有效谱包，添加空差分后保存，writer 指出了空谱的文件并拒绝；此前磁盘元数据没变，内存仍是未保存。**严格发行规则按设计生效。**若要让谱师先建几个难度、写完一张就能下班，做独立的工作草稿保存/恢复，或至少列出阻塞保存的差分并能跳过去；不要悄悄漏掉空谱，也不要往正式包塞占位音符。[严格判定要求](../shared/Chart/V2/Validation.cs#L119-L129) · [保存全部差分](../tools/chart-editor-core/Documents/EditorDocumentSession.cs#L85-L109) |
| 长歌的新谱跳不到后半段 | 130 秒、120 BPM 的录音长 65 小节，谱上只有开头两颗音符。底部时间轴上限却是 32，因为只看最后音符/节点加四小节。在 Shell 已收到小节 40（80 秒）的播放位置后，按 D 本应前进至 81 秒，结果退回小节 32（64 秒）。不是说播放本身到 32 就停止。上限至少应考虑解析后音频长度、偏移、BPM 表和已有事件。[范围计算](../editor/shell/MainWindow.axaml.cs#L568-L579) · [定位钳制](../editor/shell/MainWindow.axaml.cs#L714-L731) |
| 三连音上的对轨事件 | 网格设为 `1/3`，播放头定位 `1/3`，TIMING 时间留空再新增。写入的是 `333333/1000000`；手填 `1/3` 居然还能加第二行。同一个想要的拍点在模型里成了两个时间。手填精确分数的路径正常。另一个不一致：`1/4` 网格的正中点 `1/8`，底部定位吸到 `0`，文档吸附却得到 `1/4`（[Shell 舍入](../editor/shell/MainWindow.axaml.cs#L722-L729)、[文档舍入](../tools/chart-editor-core/Documents/EditorDocument.cs#L327-L337)）。保留播放头的网格坐标，统一半格舍入；只有连续时间定位时才明确选择怎样量化。[空输入取六位小数](../editor/shell/TimingView.axaml.cs#L275-L285) |
| 长条插点与 Z | PATH 双击插点的舞台分支考虑了 Z，发出的消息却只有音符与小节数。Shell 再按网格吸附一次；在 `1/4` 格上输入自由时刻 `1+3/8`，实际写成 `1+1/2`。这与其他保留 `snap` 标志的手势不同。补传吸附意图及精确时间，验收头尾附近不被挤到同一时刻。[发出](../editor/scripts/stage/EditorStageAuthoring.cs#L236-L245) · [接收](../editor/shell/ShellChartEditor.cs#L36-L49) |
| 一组选区的两组表单 | 对同一组选区同时填宽度 `3.25` 和时间偏移 `1/4`，只应用时间变换。选区没变，另一个面板的未提交宽度却回到 `1`。已保存的宽度没被改，丢的是刚输入的草稿。刷新同一选区时只清已应用的那一组。[面板重建](../editor/shell/InspectorView.axaml.cs#L142-L153) · [两组同时重置](../editor/shell/SelectionInspectorView.axaml.cs#L29-L45) |
| 换了短音频，保存时才知道试听截取太长 | 包试听从第 1 秒起播 4 秒；给某差分换成 2 秒录音，让它继承包试听。「应用设置」成功，核心结构校验没有错误；writer 读音频后才拒绝保存。新建时要能看见**生效的**音频和试听来源，并按该录音时长预检。另：没有试听元数据时，设置框显示 15 秒但不写入，游戏默认用 30 秒再按片长截短；两个数字都不等于作者显式设置。[设置框](../editor/shell/PackageSettingsDialog.axaml.cs#L43-L64) · [客户端回退](../client/scripts/ui/song_select/SongPreviewPlayer.cs#L14-L14) · [写包媒体校验](../shared/Chart/V2/V2PackageWriter.cs#L157-L196) |
| 替换封面后格式与后缀不符 | 已有有效 PNG 封面，替换为有效 JPEG。保存、重开都成功，但仍引用 `cover.png`，内容是 JPEG。把保存文件交给商店的实际媒体验证器，返回 `Unsupported cover codec`。让后缀随新编码变化并检查目的名冲突。编辑器可选 BMP 而商店只收 PNG/JPEG/WebP，这另属本地和商店政策差异，应标明，不必一概禁止本地使用。[替换路径](../tools/chart-editor-core/Documents/EditorPackageSettings.cs#L43-L57) · [商店媒体规则](../server/Catalog/CatalogMediaValidator.cs#L25-L34) |
| 只改谱面文件名的大小写 | Windows 本机 `charts/hard.json` 改为 `charts/Hard.json`：设置接受，保存失败，原包仍能打开。writer 在暂存目录写入新文件，再按区分大小写的字符串清理旧名，实际可能指向同一文件。修复后要在大小写敏感和不敏感文件系统各验一次。[写入顺序](../shared/Chart/V2/V2PackageWriter.cs#L86-L96) · [旧文件清理](../shared/Chart/V2/V2PackageWriter.cs#L377-L388) |
| 没有保存过也会丢重做步骤 | 选外部录音作覆盖 → 撤销 → 移走这个文件 → 重做。报告文件不存在是对的，但 `CanRedo` 从 true 变成 false，恢复源文件也没有这一步可重试。重做成功后才弹栈，失败应保留历史和目标选择，给出可恢复提示。这和已知的“跨保存找不回旧媒体”是两件事。[历史栈](../tools/chart-editor-core/Documents/EditorDocument.cs#L371-L410) · [资源前置条件](../tools/chart-editor-core/Commands/PackCommands.cs#L93-L101) |
| 没改署名，点击「应用」却改了署名 | 单个合法显示名 `Rhythm, Inc.`，打开设置不改任何字段直接应用，得到 `Rhythm`、`Inc.` 两个署名。窗口把列表拼成字符串，再无条件按逗号/分号拆开。未改时保留原数组；编辑时用可增删的署名项。[填值与读取](../editor/shell/PackageSettingsDialog.axaml.cs#L50-L75) |

## 4. 表单和格式还没对齐的小项

- 新建窗口第六个标准难度显示 `TERA`，设置窗口显示 `TECH`，实际枚举为 `Tech`。是标签错误，不是两个难度。建谱和设置应共用一份难度选项。[创建](../editor/shell/CreateChartDialog.axaml#L60-L68) · [设置](../editor/shell/PackageSettingsDialog.axaml#L71-L76)
- 自定义难度名的合同按 Unicode 码点计数。13 个补充平面字符是 13 个码点，却占 26 个 UTF-16 码元；探针中表单预检以“超过 24”拒绝，同一值由严格 writer 保存并重新打开成功。窗口长度限制与预检都要与 shared 对齐。[预检](../tools/chart-editor-core/EditorCreateRequest.cs#L64-L70) · [格式校验](../shared/Chart/V2/Validation.cs#L284-L293)
- **静态核对：**创建窗口的文件选择器列四种音频，路径输入框可编辑，草稿工厂/预检却认可 `.opus/.m4a/.aac`，正式 writer 明确拒绝。应在选文件时说明能否预览、能否写包，勿仅改报错文案。[草稿扩展名](../tools/chart-editor-core/Documents/EditorProjectDraft.cs#L66-L88) · [实际音频探测](../shared/Chart/V2/AudioProbe.cs#L32-L55)
- **操作缺口：**已有封面可以替换，却没有“移除封面”；包级试听一旦显式填写，没有“恢复游戏默认”。`PackPreview=null` 在现有请求里代表“不改”。需要明确区分“不动 / 设置 / 清除”，不能把打开表单误当成写入默认值。[包设置请求](../tools/chart-editor-core/Documents/EditorPackageSettings.cs#L5-L24) · [控件](../editor/shell/PackageSettingsDialog.axaml#L32-L55)
- **契约待统一：**属性清单说修改后保存会增加包 `revision`，实际保存保留原值。格式把它定义为发布修订号，不应为了对齐旧文案而每按一次 Ctrl+S 就加一。先定“发布新修订”的入口与职责，不把它当成本轮已复现的上传失败。[属性说明](../tools/chart-editor-core/EditorPropertyCatalog.cs#L29-L39) · [快照](../tools/chart-editor-core/Documents/EditorDocument.cs#L454-L460) · [格式语义](chart-format-v2.md#L163-L177)

## 5. 下一轮怎么验收

1. **先守住别人的数据。**同包准备 A、B 两张谱，记录两份音频哈希；仅替换 B 后分别预览、保存重开，A 的字节和引用不变。覆盖应用后取消、两个差分仍共用、删除最后一个引用、外部文件已移走。取消多选和点 TIMING 行后，分别试属性应用、复制和 Delete，实际目标与高亮一致。
2. **再做“从哪张谱起步”。**分别用包默认音频、当前差分独立音频、无包默认的合法包、另选新录音建谱。原谱有分数小节变速点和非零偏移。检验复制整张 BPM 表后改新谱不影响旧谱，音符为空、Scroll 按选项；创建一次撤销/重做，补一颗 TAP 后保存并重新打开。`CUSTOM` 把冲突处理放在点击确认之前。更新现在锁定固定 150 的测试，不要取消严格写包规则。
3. **最后接好时间与提示。**用超过 32 小节的稀疏谱测定位；`1/3` 格下，留空使用播放头与手填精确分数应落在同一拍；再用 `1/4` 格和 Z 试长条插点。换短音频、封面编码不符、设置后署名不变，都应在相应工作流有回归。修完再改两种语言的教程。

本轮执行条件与未覆盖项见[验证记录](verification.md#最近验证记录)。没有把真实波形、Storyboard、对象磁吸或公开插件接口列为新发现；这些是已有边界，不是这次审查发现的回归。
