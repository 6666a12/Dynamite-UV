# 验证状态与操作

> 维护原则：**执行日期、对象、条件、结果、未覆盖范围**一起写。静态核对、自动检查、导出验包、用户验收是四种证据，不能合并为“全通过”。
> 当前功能与待办以 [交接](<handoff.md>) 为准；这里统一维护最近验证记录、测试包身份与操作命令，避免各 README 重复漂移。

## 当前 Windows 编辑器测试包基线

**2026-11-12 用户确认：当前配置“问题不大”，可作为现有测试包基线。** 后续测试与问题定位以本节身份为准，不继续使用早期同名candidate实验包。此确认不是所有设备、保存边界或公开发行检查全部通过的声明。

| 字段 | 当前基线 |
| --- | --- |
| ZIP | `builds/editor-test-20261112-uninstall.zip`（本地忽略产物，不随克隆取得） |
| 解压目录 | `builds/editor-test-20261112-uninstall/` |
| 大小 / SHA-256 | 约67.15MiB / `165f8f7728031fa0487452c9550805947439066b46a3bab041a8779dc43bcc62` |
| 平台 / 版本 | Windows x64 / `0.1.0-preview`；版本号未区分所有历史候选，须同时核对包名和hash |
| 导出配置 | `windows-x64-test-v1`；定义在 [test-profile.json](../tools/editor-release/test-profile.json)，操作见 [打包说明](../tools/editor-release/README.md) |
| 运行时 | Shell优先系统兼容.NET 10，包内裁剪自包含应用兜底；Godot舞台使用裁剪自包含.NET 9 |
| 原生模板 | 保留3D API的精简Godot 4.7.1；模板hash及运行时版本由profile检查 |
| 启动 / 卸载 | `DynaMakerUV.exe` / 关闭编辑器后运行 `Uninstall.cmd` 并确认；保留额外谱包、修改文件和用户偏好 |
| 资源来源 | PCK显式复用transport候选；托管代码重新发布，来源/hash记录在包内distribution.json，不称本轮全新导出 |
| 自动证据 | 最终包双运行时Shell回归、卸载只读预检、发布47项及ZIP校验；细节见下方2026-11-12记录 |

### 基线中的修复与保留边界

- 已包含目录尾分隔符首次保存修复、保存快照版本/dirty修复、文本框保存快捷键、成功发布资源解除外部依赖、保存期间关闭保护，以及便携卸载入口。
- 保存修复版真实窗口首次保存和三差分持久化验证见下方；加卸载后的最终包未重复可见窗口专项。用户确认单独记录，不把前一包证据冒称为后一包重新执行。
- 资源替换跨保存后的历史媒体Undo/Redo版本恢复仍未完善；独立原生Auto回归退出132、用户暂缓的headless结算问题未修。
- 精简模板缺ANGLE回退、WinRT TTS、AccessKit，原生图标/版本资源未重新应用；跨GPU、DPI、低端性能、无.NET/不兼容.NET干净机器尚未全面覆盖。
- 未签名，许可证/notice复核与干净可复现发布基线未完成；包内`local-candidate-not-cleared-for-publication`不因本次文档确认自动改变。没有安装器、自动更新或自动录制。
- 导出参数暂时固定，不自动继续裁剪。运行时安全更新或模板变更须更新profile并复验；旧版本不是永久冻结。早期包仅作历史证据，不回写其失败结论。

## 原生对象回归入口

先构建对应项目，再按下方运行约定结束残留 Godot。以下为入口，不代表本次重新执行。

游戏 HUD（不加载歌曲）：

```powershell
<Godot-Mono> --headless --path client res://scenes/verification/gameplay_hud.tscn --quit-after 120
```

制谱器手势：

```powershell
<Godot-Mono> --headless --path editor -- --verify-gestures
```

该入口直接驱动 `EditorStageAuthoring` 的 press/motion/release，创建真实 `NoteView`/连接体并检查精确读数；覆盖三轨、3/5/4/12 网格和 SELECT/PATH 的 Hold/Mixer 中间节点身份。它不是可见窗口截图测试，也不验证 Draw 回调的 GPU 像素或原生 HWND 输入投递。

Auto 预览对象回归：`<Godot-Mono> --headless --path editor -- --verify-auto`。覆盖实际共用渲染器/特效节点及舞台播放切换，不是截图或音频回归；同样遵守启动前结束残留 Godot 的约定。

右键面板对象回归：`<Godot-Mono> --headless --path editor -- --verify-context`。检查菜单请求、选中状态、视口边缘和关闭/取消手势；不证明实际 HWND 输入、GPU 像素或音量听感。

2026-11-11 首个 Windows x64 编辑器分发候选包：新增 `tools/editor-release/build.py`、原生 `tools/editor-release/launcher.cpp` 与 Shell `StageLaunch`。候选包位于忽略的 `builds/editor-candidate-20261111/`，约303MiB，包含 Avalonia Shell、Godot自包含舞台、包内.NET 10.0.10、运行来源日志、文件SHA-256、README和许可证/第三方通知目录；`distributionStatus` 明确为 `local-candidate-not-cleared-for-publication`。系统运行时优先与 `--bundled-runtime` 强制包内路径均已在仓库外中文/空格目录通过 `--launcher-check`；复制到该目录后直接运行 Shell headless 回归，退出0。包内运行时完整真实窗口回归 `tmp_shots/editor-distribution-20261111/desktop-results.txt` 通过：Godot嵌入、三差分/音频推进、Shell Save As、Inspector、Ctrl+Z/Y/S、重开。完整仓库 `builds/editor-distribution-verify.log` 返回0：结构319源文件，三端构建（Shell保留3条既有Watermark弃用警告）、核心/编辑器/Shell回归、38项发布测试和diff检查通过。Godot导出舞台运行时优先切换探针失败并已确认是Godot 4.7.1官方Mono导出模板要求其自包含runtime；没有修改引擎或伪称舞台支持系统优先。候选包未经代码签名，且29个NuGet包当前只有license标识而未发现包内notice文件，需依赖归属/notice复核后才能对外分发；未实现自动更新，未完成无.NET/不兼容.NET的真实机器测试，不称正式发行包。

## 最近验证记录

2026-11-13 OCR授权四组修复：范围见[修复记录](ocr-review-verification.md#2026-11-13-授权修复范围)。`python -B tools/verify.py --no-restore` 最终退出0，输出VERIFICATION PASS（`.tmp/ocr-fixes-verify.log`）：结构328源文件、客户端/Godot编辑器/Shell构建、core-tests、chart-editor-tests、Shell headless及55项发布/工具测试通过。客户端与Godot编辑器0警告/0错误；Shell保留3条既有Watermark弃用警告。最后补充坏audioSnapshot关联请求明确失败的断言后，另跑Shell headless退出0（`.tmp/ocr-fixes-shell-final.log`）。新增回归覆盖原Dock三反例、宽度数值上界、取消/保存后关闭、加载期间编辑不丢失、协议坏数值、首帧前收起、超窗重复击打、循环时钟抖动、递归结构门禁、打包冲突、Range拒绝/来源隔离、资产产物清单、manifest错误标签及feature子串伪装。

本轮中间失败如实保留：首次Shell编译因ChangeVersion只在核心内部可见失败，调整为只读公开版本后通过；首次全套回归发现旧测试明确期待校准时钟回退，按已授权的单调修复更新精确预期后通过；下一轮唯一失败为生成器hash变更，未伪造旧资产的新来源，保存与旧hash一致的不可变脚本快照至`release/provenance-sources/`并让现有资产继续指向它，复验通过。未修改冻结玩法/判定顺序、v2/Digest、配色/版式；没有重新生成资产、下载外部资源、导出APK/编辑器包、启动Godot或执行真实卸载。真实触屏取消/多点、音频自然结束听感、原生HWND拖拽/退出竞态及最终APK验包仍未实机执行，headless与离线合成包不替代这些验收。现有测试ZIP身份保持不变，修复只在当前源码。`result.json`保持核实时SHA-256不变。

2026-11-13 OCR报告全量核实：对根目录result.json的218条comments逐项读报告并核对当前源码，分类为122条源码/探针确认、51条部分成立或需限定、18条测试覆盖/清理缺口、13条旧路径/已停用功能、6条维护建议、2条明确误报、6条证据不足保留。完整编号、来源hash及限制见[核实清单](ocr-review-verification.md)。执行原类纯C#探针（系统无net9，使用net10 roll-forward）、离线Python临时数据/mock响应探针、真实Avalonia/Dock headless对象探针；确认Dock普通浮出回收/首次分析页/多父集合问题。首次Dock探针因Find重复返回的Single断言中止，改按真实树后通过运行，未改产品。#24 int.Max与#35 3e9原例在net10不按报告结果复现；#24另以int.Min复现错误饱和。未启动Godot、无真实音频/触屏/原生窗口或Blender目视，不将静态复杂度算作性能实测。只新增核实文档/导航及忽略目录探针，未修产品、未改result.json、未运行插桩/卸载。git diff --check通过。

2026-11-12 游戏菜单v3外观接入：用户确认Penpot最终稿并要求只改外观。已实时读取Settings/Calibration/Note Colors八板标题及选曲展开稿，接入选曲、设置四tab、校准和调色/观察页；Online未实现。客户端最终 `dotnet build client/DynamiteUniverse.csproj --no-restore` 0警告/0错误；结构检查326源文件通过，`git diff --check`通过。`.tmp/check-ui-v3-behavior.py` 比对53个输入/状态方法、改动文件事件订阅与11个受保护逻辑/样张文件，均保持不变（源级检查，不是原生GUI手势测试）。临时检查器最初因同名方法/表达式体匹配报错，修正检查器后通过，未为通过检查改动业务逻辑。新Space Grotesk SemiBold字体读回OS/2字重600，哈希已加入asset-provenance；本机缺fontTools，改用标准库读取字体表，未安装依赖。完整非图形 `python -B tools/verify.py --no-restore` 已退出0并输出VERIFICATION PASS（日志 `.tmp/ui-v3-verification.log`）：三端构建0错误，客户端/原生编辑器0警告，Shell保留3条Watermark弃用警告；核心、编辑器、Shell headless回归及47项发布测试通过。后台运行期间的最终颜色/注释微调另经客户端构建、源级行为护栏及结构检查复查。未运行Godot、未验收字体导入后的实际绘制/窗口/触屏、未导出新包。既有result.json不属于本次修改。

2026-11-12 头部字重再修订：用户指出字重仍不一致，实读确认Settings前三页700而Effects/Calibration为400。此次通过Font.applyToText及整段applyToRange统一8页标题至600/36px，读回顶层/范围/variant均600；Calibration两页按NOTE COLORS/READABILITY复制标题、副标题和76px返回按钮坐标，箭头复用形状。四Settings维持先前定位，未改内容区；非目视验收。

2026-11-12 Settings/Calibration头部纠偏：按用户截图反馈，修正Settings前三页字距6和Effects字距0/文本框高度不同造成的视觉不一致。四页统一Effects标题落点，按钮96→72并与标题中心y86对齐；Calibration标题与按钮下移到中心y90，字号36，副标题y116。六页textBounds读回标题视觉中心距按钮中心约0.4px；内容区未动，未运行游戏，仍待用户目视。

2026-11-12 设置/调色/校准v3设计：只读核对游戏布局代码，保留旧8板、克隆新增8板并应用切角/中间色按钮/低边框层次；修正旧稿JUDGE TEXT、补Effects标签和校准入口，原型导航重连v3。13处调色受保护区域的相对几何/填充/描边一致；首次同名glyph比较误匹配，按所属类型复核六类均一致。校准示例按5ms取整改+5ms。用户提醒按钮错位后修正校准4px偏差、Effects页脚中心线和观察页色块旁标签；7组居中属性核对通过，8板文字无外边界溢出。Effects/调色PNG导出成功，未最终全板目视验收；未修改游戏代码或运行构建。新板ID/坐标及限制记录在editor-ui-design §1.5。

2026-11-12 选曲v3收尾微调：两板返回与START文字框匹配按钮尺寸并设双向居中；18个普通行差分块压至44×34、数字18。API读回两板居中属性及所有差分尺寸/字号一致；用户此前反馈其余方向基本无异议，不扩大为本次调整已目视或游戏已验收。只改Penpot及文档，未修改游戏代码。

2026-11-12 未激活按钮试色：仅Song Select v3两板新增Hardcore/全部设置/返回的中间色切角底面，并调整关闭态开关轨道为#192D3F；STANDARD及禁用筛选保留原状态。展开PNG导出成功，未目视验收或修改游戏代码；Online暂未同步，等待试色反馈。

2026-11-12 Penpot切角/亮度试调：Online四板及Song Select v3两板提亮表面与正常文字，保留禁用灰；恢复主操作、选中底面和差分彩块切角，选曲品牌/返回下移放大。替换前矩形隐藏保留并复制原型交互，六板文字外边界无溢出，交互数量读回保留，选曲展开PNG导出成功。不宣称完整目视或实机验收，未修改游戏源码。

2026-11-12 选曲v3低边框Penpot草稿：只读当前游戏布局/快捷设置/展开卡/行渲染源码，以左设置+列表原位展开+底部操作条为基线，新增Collapsed/Expanded两板，不改旧板或游戏代码。保留源码列表尺寸/展开高度/封面及现有交互语义，弱化大框和选中描边，保留难度彩块和主操作。文本外边界检查通过，展开PNG导出成功，Read未显示图像，不宣称目视验收；原型仅首曲展开/收起。未构建、未运行应用；设计记录已同步editor-ui-design.md。

2026-11-12 在线账号下拉续接完成：恢复后发现先前浮层已实际保存（9子对象），未重复创建；商店用户名绑定open-overlay，固定(1500,92)、点击外部关闭，退出仅导航到登录原型。新增(0,1280)菜单展开状态板，方便评审。四个顶层画板可见文本外边界无溢出，原型配置读回正确，展开PNG导出成功；Read仍未显示图像，未目视/实际点击验收。公告全文展开、记录和切服操作未实现，不宣称在线业务或共享组件库完成。

2026-11-12 在线UI细节补充（中断）：恢复插件连接后将商店六行差分替换为选曲配色小方块，详情两差分收紧，登录页添加服务器公告预览并上移服务器信息。上述调用成功。账号下拉浮层创建被84秒无心跳阻止，尚未确认生成，也未接下拉交互或做最终图像检查；恢复先读实际页面，不重复创建。文档已记录中间状态，未改产品代码。

2026-11-12 在线UI v2无边框修订：按用户反馈，原两板ID/位置不变，v1子图层归入隐藏回退组；商店从双列卡片改为单列滚动视口示意及大详情区，只留封面/标题/谱师/差分/BPM/下载，去除永久免费、版本大小、宣传说明及分页。登录页去掉外框/宣传大标题，使用留白与下划线输入。可见文本画板边界检查通过，商店PNG导出成功，但Read未显示可目视内容，未视觉验收。登录按钮原型跳转保留；未实现真实滚动、惯性或详情动画，未改现有选曲页及产品代码。

2026-11-12 在线UI首稿：Game UI — Online新增登录与谱面商店两张1920×1080画板，分别(0,0)/(2120,0)，37/93个直接子对象（后续属性调整不改数量）。恢复断连后按实际对象补全商店，不重复创建；文案/曲目/服务器均为设计示例，封面占位。两图导出成功，文本外边界检查无超出画板；Read未呈现可目视图像，不宣称视觉验收。登录按钮及其文字已接商店原型跳转；显式createFlow报frame类型错误，但最终实读已有Flow 1且起点为登录板，未重复创建。注册/切服弹层和异常状态未设计，游戏未接入；等待用户评审。

2026-11-12 Penpot六页迁移完成：恢复连接后补完配色第二板及其余Home/Song Select/Results，六页共34板，统一从(0,0)三列排布、步长2120×1280。全板检查可映射对象的文字/字体、尺寸/相对坐标和填充/描边/阴影等属性无差异；配色网格曾发生自动单元格冲突，通过空网格分阶段恢复并逐个放入后，两组轨道/单元格一致，4条跳转指向新板、Flow起点正确。源页无评论；新旧ID映射持久化于文件插件数据gameUiMigration20261112，保存删源前版本后按明确34个源ID删除，移除空原页，再保存完成版本。最终实读六页34板、无顶层散落对象；当前打开Home页。SVG内部包装层与ID改变、抽样PNG非逐像素相同、未全板目视验收等边界保留；没有启动游戏或修改产品代码。设计说明与handoff已同步，历史中断记录保留。

2026-11-12 Penpot图层分批续建与原点排版：用户要求分类页靠近(0,0)排列。恢复后确认storage已清空、17个副本存在、此前配色板未生成；Settings六板改为三列网格，步长2120×1280。重建迁移快照，配色第一板按图层队列完成图形创建，第二板最后成功批次后余22项；下一批被32秒无心跳阻止，尚未恢复配色网格、交互与Flow。原34板未删；其余页紧凑排版及全板校验未完成。实际中间状态已同步设计文档与handoff，不能以顶层板存在判定完整。

2026-11-12 Penpot重建迁移（进行中）：用户授权重建后删除原位置，执行顺序为先建后验再删；新增六分类页，17个顶层副本确定创建完成（Settings六板、Gameplay六板、Grade Studies五板）。配色第一板调用超时并报82秒无心跳，完成状态未知；原34板未执行删除。简单设置板属性核对一致但文字重排存在亚像素差异；Track Handoff首次SVG导入失真，删除错误副本、修正后PNG平均通道误差约0.06–0.13/255；Gameplay约0.08–0.13/255，不等于视觉/无损验收。SVG导出服务曾500超时，已知generateMarkup重复子元素问题及后续检查要求记录于editor-ui-design.md。未完成配色交互/Flow迁移、紧凑排版或全板验收；需用户恢复浏览器标签页心跳后核对中间对象再继续。未修改产品代码或构建。

2026-11-12 Penpot连接与页面整理尝试：Claude Code插件实读文件Dynamaker UV UI design，当前仅Game UI页，用户删除的旧Editor UI页不再存在。保存整理前历史版本；发现34个顶层画板、2991个形状、4条配色原型交互和1个Flow。跨页appendChild被插件活动页保护拒绝，未进行破坏性重建；撤除本次创建的空Settings页并回原页，复核形状ID无缺失、画板名称/位置/尺寸未变、交互数量与Flow起点保留。功能拆页尚未完成，拟定六页分组和限制记录于editor-ui-design.md，同步handoff；未做截图目视验收、未修改产品代码、未运行游戏。

2026-11-12 网络服务第一阶段设计记录：新增 network-services-phase1.md，分开记录用户确认的开放注册、免费管理员曲库、本人记录、结算上传与手动重试，以及服务器隔离/部署/页面的建议和待定细节；明确段位、联机、反作弊与脚本扩展不在第一阶段。同步文档导航和handoff入口。检查新专题及两份入口共88个本地链接目标均存在（不检查锚点），工作区diff检查通过，仅有既有LF/CRLF提示。本轮仅文档，未构建或运行产品、未实现网络功能、未接入Penpot或修改凭据/设计稿。

2026-11-12 测试基线文档整理：按用户确认建立本页当前Windows测试包身份表，入口README/editor README、handoff、制谱器专题和打包说明改为引用该基线；清理“无完整桌面包”“多选尚未接入”“仍用runtime目录兜底”等过时现状，将已修保存问题与剩余媒体历史恢复缺口分开。历史失败/候选记录原文保留，不修改归档。检查7份更新文档132个本地链接目标无缺失，新基线锚点标题存在，diff检查通过（既有LF/CRLF提示）。本轮仅文档，不重跑构建/产品回归、不启动应用、不重新打包，现有ZIP/hash不变。

2026-11-12 固定测试导出配置与便携卸载：新增windows-x64-test-v1配置，固定双裁剪、Shell 10.0.10/舞台9.0.18和验证过的模板SHA-256，构建遇版本/hash漂移失败。新增Uninstall.cmd/uninstall.ps1，确认后按manifest/hash删除程序文件，不递归删除根目录，保留额外谱包、修改文件及用户偏好；路径越界/reparse检查与占用预检查。遵守系统PowerShell执行策略，不设置Bypass。四项隔离卸载执行测试通过（中文/空格路径、保留额外谱面与修改文件、越界拒绝、占用拒绝），发布47项与结构/diff通过（editor-uninstall-release-tests.log）。新包builds/editor-test-20261112-uninstall/构建通过；双运行时Shell回归与最终包卸载只读预检通过（editor-uninstall-launcher.log、editor-uninstall-package-check.log：295项），未在用户真实目录执行卸载、未点击确认对话框或重跑本轮可见编辑窗口。ZIP 67.15MiB、CRC/hash通过，SHA-256 165f8f7728031fa0487452c9550805947439066b46a3bab041a8779dc43bcc62（editor-uninstall-zip.log）。许可证复核、干净构建/无.NET机器及模板覆盖限制仍保留；用户认可测试配置不等于这些发行检查完成。

2026-11-12 保存状态与交互修复：会话新增SaveAsync，由调用线程捕获pack/chart/resource快照，仅writer在后台执行，回调用上下文确认保存版本；EditorDocument用单调ChangeVersion避免保存期间编辑/撤销重做被误清dirty。成功发布只解除仍匹配本次快照的pending资源，保存后移走外部替换音频、同会话继续保存通过。Shell在目录选择前设置ioBusy、保存期间禁止关闭；文本框Ctrl+S/Ctrl+Shift+S进入全局保存，保留其它文本编辑快捷键。新增SaveInteractionVerification直接触发SAVE控件、真实文本焦点按键和writer发布屏障，确认磁盘/内存/dirty与二次保存一致；首轮夹具控件未布局导致查找失败，补选择与布局后通过。`editor-save-safety-verify.log`完整verify通过（发布43项），`editor-save-safety-core.log`覆盖资源归属与尾分隔符生命周期。资源替换跨保存的Undo/Redo历史媒体版本保留仍未实现，不将本次解除当前pending依赖外推成该能力。新最小候选构建完成：`builds/editor-candidate-20261112-save-safety/`（editor-save-safety-build.log）；仍显式复用transport PCK并重新发布托管代码，精简模板/许可证/干净机器等限制不变。最终包系统/包内两条运行时路径的Shell回归通过（editor-save-safety-launcher.log）；包内运行时真实窗口首次保存（StorageProvider带尾分隔符路径、磁盘strict重开）与三差分保存/撤销重做/重开专项通过（tmp_shots/editor-save-safety-first-20261112/desktop-results.txt、editor-save-safety-persistence-20261112/desktop-results.txt）。ZIP已生成并校验hash/CRC，67.15MiB、解压168.10MiB，SHA-256 `8f3b9f13453340f0052e9a0de98e2b4101303fd3283579cb2ea5e095427e9c3d`（editor-save-safety-zip.log）。不代表用户手动目录对话框、所有设备或视觉验收。

2026-11-12 单差分首次保存路径定位：用户明确最小包内已有Tap且选择全新空目录，排除空谱/非空目录解释。旧最小包`--smoke-empty`在注入不带尾分隔符路径时成功，但不能证明真实目录选择路径。隔离探针使用最小包程序集确认同一Tap草稿保存到空目录：无尾分隔符成功，有尾分隔符失败“参数错误”、meta/chart均不存在（editor-first-save-package-audit.log）；Avalonia StorageProvider返回的folder.Path.LocalPath确实带尾反斜杠（editor-save-ui-storage-audit.log）。writer把staging/placeholder后缀拼到原路径，结果变成目标子目录，发布移动父目录到其内部失败。修复RequireDirectoryPath/RequireDirectory统一TrimEndingDirectorySeparator，不放宽strict校验/非空目录保护；PackageLifecycleTests覆盖带尾分隔符首次保存、打开及另存为，editor-save-path-final-core.log通过。真实窗口smoke-empty改用StorageProvider解析的路径，并检查meta.json及strict重开的一颗Note，不仅检查dirty。其它已复现并发/快捷键/资源依赖缺陷未修；用户实际目录未读取或修改。完整verify通过（editor-save-path-fix-verify.log），最终生命周期补测通过（editor-save-path-final-core.log）。修复候选 `builds/editor-candidate-20261112-save-path-fix/` 构建通过（editor-save-path-fix-build.log），包内运行时真实窗口新建单差分/原生放置一个Tap/StorageProvider带尾反斜杠目录首次保存/磁盘strict重开通过（tmp_shots/editor-save-path-fixed-20261112/desktop-results.txt）；未自动操作系统目录选择对话框本身。对应ZIP已生成且hash/CRC验证通过（editor-save-path-fix-zip.log），仍只修复目录路径问题，不是保存全链路安全声明。

2026-11-12 保存UI专项诊断（未修复）：使用独立临时谱包和本地探针，不访问用户实际谱包。`builds/editor-save-ui-audit.log` 直接触发顶部真实SAVE控件Click：正常保存磁盘速度1.3、dirty=false，证明按钮确实接到磁盘writer；文本框实际获焦后发送Ctrl+S，磁盘仍1.3、dirty=true，快捷键被文本输入过滤。利用writer现有PublicationInterceptor在快照完成/发布前建立确定性屏障，再通过Inspector速度入口编辑，真实SAVE结束后内存1.9、磁盘1.7、dirty=false，确认后台保存与UI编辑并发会误清dirty。`builds/editor-save-audit.log` 核心会话同样复现内存1.8/磁盘1.2/dirty=false；另确认资源替换保存成功后移走外部源，在同一会话继续保存抛V2DiagnosticException且pending=1，MarkSaved未解除外部资源依赖。测试为Avalonia headless真实控件/路由及磁盘读回，不是原生HWND鼠标端到端或用户具体复现；初次探针项目相对引用路径错误，修正后执行以上结果。未改产品代码、未出新包，现有候选仍含这些缺陷；此前顺序保存回归通过不能覆盖保存中继续编辑、文本框焦点和资源替换后不重开会话的场景。

2026-11-12 精简模板兼容版：保留3D核心API以兼容官方GodotSharp，关闭Vulkan/D3D12及不使用的可选模块，原生EXE 104.32→46.98MiB（editor-slim-template-api-build.log）。最终 `builds/editor-candidate-20261112-slim-api/` 构建成功，PCK资源、普通启动/IPC及HUD门禁通过（editor-slim-api-build.log）；双运行时选择和Shell回归通过（editor-slim-api-launcher.log）。强制包内运行时真实窗口专项三差分/音频推进/保存/撤销重做/重开/内部预览通过（tmp_shots/editor-slim-api-20261112/desktop-results.txt）；系统运行时路径同一窗口专项随后通过（tmp_shots/editor-slim-api-system-20261112/desktop-results.txt）。截图已采集，但本轮工具未返回可供目视的图像内容，不宣称GPU像素验收。最终目录168.08MiB（原303.49MiB），294文件；实际ZIP `builds/editor-candidate-20261112-slim-api.zip` 为67.14MiB，文件hash与ZIP CRC检查通过，SHA-256 `a9779973cd929b6732b7de73ea1b2c6916f0fcdaded1ec9d05afcdc5b740f5f4`。测量见editor-slim-final-sizes.json、editor-slim-api-zip.log。构建环境缺少可选ANGLE/WinRT TTS/AccessKit依赖，原生模板只有OpenGL回退覆盖且未重新写入原生图标/版本资源，限制已入distribution.json；不作为默认公开发行配置。复用既有PCK、裁剪警告、无.NET真机与许可证等未覆盖范围同下条。

2026-11-11—12 编辑器精简实验（尚非公开发行）：`build.py` 新增可选 `--trimmed-fallback` / `--trimmed-stage` / `--stage-template` / `--stage-from`；默认完整运行时策略保留。系统优先路径仍使用普通Shell，包内兜底为独立自包含裁剪Shell，不伪装通用.NET安装；Godot托管裁剪保留完整GodotSharp/项目程序集与原生托管入口。首轮JSON反射开关缺失导致Shell启动失败；中间产物复用使后续开关未生效，隔离发布后Shell回归通过。Godot首次裁剪关闭native hosting而退出139，保留SDK对应开关与桥接程序集后HUD/手势/菜单对象回归通过。源码自编译首版引擎42.62MiB，但`disable_3d=yes`使官方GodotSharp的Viewport.set_world_3d绑定失效；HUD专用入口未覆盖，普通启动/IPC和真实窗口发现，**`builds/editor-candidate-20261112-slim/` 不可用**。已新增普通启动/IPC错误门禁，负例退出1（editor-slim-startup-negative.log），不把退出0但日志有ERROR计为通过。

仅运行时精简、保留官方引擎的 `builds/editor-candidate-20261112-slim-managed/` 已通过最终包双运行时启动/非图形回归（editor-slim-managed-launcher.log），包内裁剪Shell真实窗口三差分加载、音频推进、Save As、Inspector、Ctrl+Z/Y/S、重开与内部预览切换通过（editor-slim-managed-desktop.log，截图/保存产物tmp_shots/editor-slim-managed-20261112）。大小303.49→225.42MiB，实际逐文件DEFLATE payload124.55→87.39MiB，非已生成ZIP大小；测量见editor-slim-managed-size.json。完整仓库verify通过（editor-slim-full-verify.log），后续发布43项通过（editor-slim-release-tests-final3.log）；Shell既有3条Watermark警告与裁剪分析警告未消除。两次官方导出日志到打包完成后未退出，使用受跟踪任务停止，不能计成功；本轮候选明确复用transport候选PCK并记录来源/hash，托管代码重新发布。保留3D API的第二版原生模板及最终合并包验证另记。随后系统运行时路径同一真实窗口专项也通过（tmp_shots/editor-slim-managed-system-20261112/desktop-results.txt），候选293项文件hash全部匹配。没有无.NET机器、全部文件对话框/编码/DPI/低端性能或用户视觉验收；旧Auto退出132未修，许可证/签名/干净发行基线仍待处理。

仅运行时精简ZIP已实际生成并通过CRC检查：`builds/editor-candidate-20261112-slim-managed.zip`，87.46MiB，SHA-256 `b5dc8d7e889d74d557869e294c5c23667b2c9894a0369429c00245af9b78d205`（editor-slim-managed-zip.log）。这是本地实验压缩包，不代表公开发行许可或干净机器验收。

2026-11-11 隐藏游戏效果预览入口与编辑器大播放/暂停键：按用户决定移除StageView公开复选框，内部模式/IPC/验证入口保留、默认关闭且不持久化，自动录制尚未实现。编辑器新增独立 `EditorTransportButton`，184×80、中心(960,72)，绘制播放三角/暂停双竖线，不改变游戏本体120×64暂停键；Controller继续拥有播放/暂停/音频生命周期。`editor-transport-verify.log` 完整verify通过（结构324源文件、三端构建、核心/编辑器/Shell及发布39项）；随后补隐藏入口断言并重跑Shell headless通过（editor-transport-shell-final.log）。原生 `editor-transport-native.log` 验证按钮几何、无谱禁用、播放状态、鼠标/触摸意图和内部预览隐藏/恢复通过。未启动可见应用进行本轮目测，不等于真实HWND点击/用户视觉验收；旧Auto退出132问题未改。候选包 `builds/editor-candidate-20261111-transport/` 构建完成，最终PCK资源加载门禁通过（editor-transport-candidate.log）；本轮未对该包运行可见窗口回归，仍为未签名且待许可证复核的本地候选。

2026-11-11 编辑器HUD/游戏效果预览与路径描边：将9个HUD展示/纯显示状态文件及UID移入 `presentation/game/hud/`，标题改由值快照传入，游戏保留原判定/血量/暂停/成绩权威，测试链接和结构策略同步、不放宽预算。编辑态保留标题/4px进度/蓝线，完整Auto预览用工具栏开关，隐藏舞台编辑导引并阻止舞台编辑手势，不锁定Shell文档命令、不实现Storyboard。`SustainOutline`只在真实节点画横向端边；`IsShapeSample`为运行时元数据，不写文件或Digest。阶跃改为同刻左极限和真实终点，三轨Hold/Mixer精确边界、非计分采样、原始字节回归通过；100阶跃样本300对象（原算法该样本25700），本机预热后约1.93–2.02ms/486360分配字节，不是低端FPS证据。`editor-preview-full-verify.log` 完整verify通过（结构323、三端构建、核心/编辑器/Shell、发布39项）；最终模式同步后 `editor-preview-final-verify.log` 同样打印VERIFICATION PASS，发布39项全过。新增原生HUD/描边测试 `editor-preview-hud-native2.log` 通过；游戏HUD `editor-preview-client-hud-native.log` 通过。真实窗口 `editor-preview-desktop.log` 验证三差分、音频推进、保存/重开、预览切换/seek且文档不变，截图在 `tmp_shots/editor-preview-20261111/`；不替代用户视觉验收。**独立Auto原生测试两次三轨行为断言完成后在Godot C#对象清理时失败，退出132，报ShaderMaterial unsafe reference/instance binding错误（editor-preview-auto-native.log、editor-preview-auto-recheck.log），未修复、不计为通过。** 首轮HUD测试夹具缺JudgeWasExplicit导致strict拒绝，修正后通过；首次renderer aggregate 805超800，端边配置职责移入SustainOutline后通过，未改预算。本批新包 `builds/editor-candidate-20261111-preview/` 已生成，最终PCK三种shader/两份字体加载门禁通过（editor-preview-candidate-build.log）；使用包内runtime运行该候选包真实窗口回归通过（editor-preview-package-smoke.log），截图在 `tmp_shots/editor-preview-package-20261111/`。后补阶跃投影同刻左极限断言并重跑编辑器测试通过（editor-preview-step-final.log）。候选包仍未签名且许可证复核未完成，不是正式公开发行包；低端设备/用户视觉验收仍待补。

2026-11-11 候选包音符材质缺失修复：用户报告普通音符材质未加载，核对 `NoteView` 动态引用与导出preset确认遗漏 `res://shaders/note_material.gdshader`（旧note_surface已包含）。新增 `tools/editor-release/verify_resources.gd`，由build.py在空临时项目挂载最终PCK，实际加载两种shader与共享字体；旧包检测明确失败，日志 `builds/editor-material-old-pack-check.log`，不能将上轮窗口流程通过当作材质完整性证明。补齐显式导出项，并新增静态防漏回归；发布39项全部通过（`builds/editor-material-release-tests.log`），结构319源文件与diff检查通过。新包 `builds/editor-candidate-20261111-material-fix/` 构建成功（`builds/editor-material-fixed-build.log`），最终PCK两种shader与两份字体实际加载全部通过；本轮未重新运行可见窗口/GPU像素验收。未修改shader效果或判定逻辑，旧包未覆盖。

2026-11-11 用户授权临时产物清理：删除旧Shell构建副本（floating-close-check、ui-fixes-shell、workspace-check）、旧dyna-maker-uv构建、发行运行时失败探针目录、首轮persistence样本、部分11月7—8日窗口smoke目录，以及 `.tmp/ui-research/alignment`、Dock/SukiUI/Ursa研究副本和 `.tmp/modularize2` / `.tmp/modularize3` 旧探针快照。按清理前目录统计估算释放约6.0GiB；清理后builds约352.0MiB、.tmp约302.9MiB、tmp_shots约265.1MiB。当前候选包 `builds/editor-candidate-20261111/` 的518项哈希全部匹配；最终persistence样本、当前两组窗口证据、截图驱动capture_frame.py及dxcam、builds根日志均保留，tmp_shots本轮未删除。**上述已删除旧目录在历史文档中的路径不再可用，历史通过记录不等于产物仍存在；不改写归档原文。** 没有删除Git跟踪文件，结构检查319源文件通过；本轮未重跑构建/产品回归，未清理设计源、Internal数据、系统临时目录或其他worktree。

2026-11-11 品牌扫描worktree范围修复：`test_brand_identity.py` 将扫描提取为可测函数，遍历时剪枝仓库根下 `.claude/worktrees/`，不读取其中其他checkout；没有删除工作树、修改旧名称允许计数或发布资源白名单。新增隔离用例确认正式源码、未跟踪新源码、历史文档、其他 `.claude` 内容以及产品子目录中同名worktrees仍被检查。`builds/brand-worktree-scope.log` 4项通过；完整 `python -B tools/verify.py --no-restore` 在 `builds/brand-worktree-final-verify.log` 返回0并打印 `VERIFICATION PASS`：结构317源文件、三端增量构建各0警告/0错误、核心/编辑器/Shell回归、38项发布测试及diff检查全部通过。既有Watermark警告在此前重新编译Shell时出现，本次增量构建不代表警告代码已修复。之后仅同步入口/本页文档，另行diff复查。未启动Godot、未出包；headless结算问题按用户决定保留不改，历史失败记录不回写。

2026-11-11 后续失败定位（仅诊断，未改品牌门禁/产品shader/退出语义）：品牌扫描额外57次命中全部来自 `.claude/worktrees/` 下三个已登记旧工作树（各19次，共12个文件），主工作区无额外命中、历史允许项无缺失。`test_brand_identity.py` 的物理递归不读取gitignore，且只排除当前测试文件自身，因而扫描了旧工作树的历史文档及检查脚本字面量；未删除工作树或扩大允许计数。结算首轮日志已有 `=== RESULT ===`，不是结算未触发：`ShowResults` 设置 `_finished` 后，`GameplayMain._Process` 提前返回，位于其后的验证 `CompleteFrame` 不再累计，故4200游玩帧目标不可达。另用隔离 `.tmp/result-shader-diagnostic/` 探针确认，原 `wordmark_diffuse.gdshader` 将内建TEXTURE传入sampler2D辅助函数时，headless稳定报9条 `custom_samplers` 错误；同一原shader在本机Vulkan Mobile/OpenGL Compatibility编译均无ERROR，探针改显式纹理uniform后headless无ERROR。日志 `builds/result-shader-probe-{original,uniform,mobile,gl_compatibility}.log`；这只验证编译/材质设置，不代表GPU像素或所有设备验收。三个问题分别为扫描范围污染、验证生命周期退出缺口和headless后端shader兼容，不能互相归因；本轮未实施修复。

2026-11-11 首发前谱包保存/读取专项（本轮执行）：新增 `PackageLifecycleTests` / `PersistenceFixture`，默认编辑器回归运行三差分756父Note的完整磁盘往返；保留样本在 `builds/persistence-lifecycle-20261111-final/搬移后 Portable/`。`builds/editor-persistence-lifecycle-final.log` 通过：新建首次保存、独立dotnet子进程重开、完整字段/媒体/Digest对比、原外部资源移走后重开、编辑及撤销重做、Windows文件占用保存失败保护、另存为源包不变、差分复制/删除/重排、包音频替换与差分覆盖保持、搬移后重开、三轮重复保存字节稳定。真实Avalonia+Godot窗口专项 `builds/editor-persistence-desktop-v3.log` 通过三差分加载/音频位置推进、Inspector修改、Ctrl+Z/Y/S入口、另存为及重开；保存输出与截图在 `tmp_shots/persistence-20261111-v3/`。首轮窗口断言误把运行时对象数当父Note数、只接受loaded而非updated；第二轮固定500ms等待不足，改为等对应revision回执后通过。首版测试封面CRC错误，在进入最终样本前改为有效原创PNG；这些为测试夹具/同步修正，未修改产品保存规则。最终Shell构建 `editor-persistence-shell-build-v3.log` 为0错误、3条既有Watermark弃用警告；`editor-persistence-core-final.log` 编辑器核心与 `editor-persistence-headless-final.log` Shell回归通过；结构门禁317源文件通过。完整verify两次均为三端构建/核心/编辑器/Shell通过、发布37项中36通过，仍仅 `.claude/worktrees/` 旧副本品牌扫描失败（`editor-persistence-baseline.log` / `editor-persistence-final-verify.log`）；后续测试夹具最终修订另跑上述定向回归，未冒称完整verify全绿。没有读取/修改用户实际谱包，没有使用原版语料；未测试系统文件选择对话框、全部原生键鼠、断电/磁盘耗尽恢复、听感、DPI/性能或独立发行包环境，不替代用户验收。游戏首轮Mega加载成功并在有效trace中达到486/486 Perfect，但进入结算时headless shader报告 `custom_samplers` 错误，结算后固定帧计数不再推进，180秒超时退出（124）；末行trace因强制退出截断，不计为整轮通过。未修改结算/判定产品代码。最终以2280帧（固定60FPS、模拟38秒，在结算前退出）分别运行Shell保存的Mega/Hard/Normal，三次退出码均0且无ERROR；每个trace完整2280行、486/486 Perfect、pending=0、Miss=0、归一化1,000,000，见 `builds/editor-persistence-game-final-{mega,hard,normal}.log` / `persistence-game-final-{mega,hard,normal}.jsonl` 及 `editor-persistence-game-summary.json`。证明本次谱包经游戏实际ChartPack/音频解码/运行时适配/判定链可用，不证明结算headless已修复、真实触控或听感。最终确认Shell/Godot进程均已退出，diff检查通过（仅既有LF/CRLF提示）。

2026-11-11 应用图标 v2 设计与接入：游戏与制谱器各一枚，由 `design/app-icons-v2/build_icons.py` 程序化生成（硬几何 4× 超采样后降采样；光效在 1024 分辨率走独立加色通道）。游戏版为三颗下落音符 + 命中爆闪与分叉电光，编辑版为同材质弯成的路径 + 菱形节点 + 粉色控制柄 + 网格底板。已拷入受跟踪目录并接线：`client/project.godot` 与 `editor/project.godot` 的 `config/icon`、`client/export_presets.cfg` 两个 Android preset 的 `launcher_icons`（192/432×3）与 `splash_screen/icon`、`editor/export_presets.cfg` 的 `application/icon` 与 `export_files`、`editor/shell/MainWindow.axaml` 的窗口 `Icon`；八份新资产登记进 `release/asset-provenance.json`。`python -B tools/architecture/check.py` PASS（314 源文件）；`dotnet build` 三端 0 错误（Shell 3 条既有 `SelectionInspector` TextBox.Watermark 弃用警告）；Shell `--headless` 回归通过；`test_release_contract` 8 项全通过（含 provenance 哈希）。adaptive 前景实体安全半径 0.537/0.541，低于 66dp 上限 0.611；单色墨量 0.178/0.157。发布测试全套仍只有既有的品牌扫描失败（`.claude/worktrees/` 旧副本），本次未新增失败、未放宽白名单。**未导出 APK、未在 Android 真机验证 adaptive/themed icon 与启动图、未启动可见窗口目视窗口图标、未验证 iOS/macOS 裁切；`_iterations/` 总目录视为可清理的过程稿，未取得真实窗口用户验收。**

2026-11-10 对象磁吸暂停：用户报告吸向坐标2时出现1.9几/2.0几且不稳定，随后决定暂不完善磁吸。已移除左侧开关和提示，ToolsView强制返回关闭，忽略旧ObjectSnap=true偏好；原生实验实现保留，未修复算法，不再列为待手感验收。`builds/editor-magnet-hidden-build.log` Shell构建成功（0错误，3条既有SelectionInspector TextBox.Watermark弃用警告）；`editor-magnet-hidden-tests.log` Shell headless通过，新增旧开启偏好不生效、入口不存在、位置模式仍独立恢复三项断言。结构门禁314源文件、diff检查通过。未启动可见窗口或Godot，未重跑原生/核心/完整verify；本次只证明入口撤下及Shell关闭策略，不证明磁吸精度已修复。

2026-11-10 对象磁吸：已接持久化独立开关、Alt绕过、同轨近邻中心/同侧可视边缘对齐、6/10视口像素滞回和目标导引。`builds/editor-magnet-stage.log`、`editor-magnet-shell.log` 两端构建通过；`editor-magnet-core.log` 编辑器核心、`editor-magnet-shell-tests.log` Shell偏好/控件回归通过。`editor-magnet-native.log` 当前程序集原生手势回归通过，新增三轨×0.5/1/1.5缩放的吸住/脱开、非网格中心、SELECT移动/边缘调宽预览提交一致性、选区排除、Alt、自由模式、屏外和远邻排除，并保留原有路径/放置回归。结构门禁314源文件、diff检查通过。首轮新增测试PositionAt调用参数错误导致构建失败，紧接一次headless运行实际用了旧程序集，不计为新功能证据；修正后使用构建成功才启动的命令链复验通过。按项目约定先结束残留Godot进程；未启动可见窗口，未做GPU截图/原生鼠标端到端/多屏DPI或长谱性能专项，未导出发行包，完整verify未跑。用户对Inspector目前无异议；磁吸手感尚待用户验收，不能由自动回归代替。

2026-11-09 多选Inspector试用版：右侧按选区切换整体变换/统一属性，新增 `EditNoteSelectionCommand` 预校验整组副本再原子替换，保持路径和编辑身份；Shell只负责草稿/输入/选择同步。`builds/editor-batch-shell.log` Shell构建成功，`editor-batch-core.log` 编辑器核心通过，`editor-batch-headless.log` Shell回归通过（真实控件输入、正负分数、错误保留草稿、混合宽度不被换轨覆盖、切选区清草稿、整组撤销）；结构门禁311源文件与diff检查通过。首轮测试误用了internal RequireNote导致编译失败，改用公开集合查找；首轮控件测试未排空Avalonia文本事件队列导致断言失败，补正常dispatcher推进后通过。**未启动可见窗口、未做截图/窄面板GPU视觉验收，未运行完整verify/发布测试；对象磁吸未实现。** 用户已另行确认第一轮偏好记忆有效，不外推为本次Inspector验收。

2026-11-09 制谱器第一轮：偏好/布局持久化与基础复制粘贴已接入。最终 `builds/editor-round1-stage-final.log`、`editor-round1-shell-final.log` 两端构建均0警告/0错误；`editor-round1-core-final.log` 为 `EDITOR TESTS PASS`，覆盖路径完整复制、精确混合网格相对拍时间、新ID/编辑身份、单步撤销重做、重复粘贴和跨文档快照隔离；`editor-round1-headless-final.log` Shell回归通过，新增偏好往返、损坏字节备份、浮动舞台退出重启、自动隐藏工具恢复、Ctrl+C/V与粘贴撤销。结构门禁307源文件通过，`git diff --check`通过。过程中修正NumericUpDown歧义编译错误；浮动布局重启用例先失败，定位Avalonia子窗口关闭优先触发归位，改OwnerWindowOnly并在销毁前捕获后通过。测试配置使用独立临时目录，不写真实用户偏好。**未启动可见编辑器/Godot、未做真实HWND/多屏/DPI/听感验证、未打包桌面发行版、未获本轮用户验收；完整verify/发布测试未重跑，不宣称历史品牌扫描问题已解决。** 持久化和剪贴板语义见 [制谱器说明](dyna-maker-uv.md)。

2026-11-09 顶部进度与判定线启灯后续调整：首轮 `builds/progress-lines-build.log` 客户端构建0警告/0错误；`builds/progress-lines-core.log` 为 `ALL TESTS PASSED`，含新增进度状态、终点口径、音频结束补时与三轨时间轴/长条/三档/seek测试。随后结构检查失败：`GameplayMain` 合计1358行超过1350预算；已清理原有重复空行，未放宽预算。此后 Bash 权限检查服务连续超时，未能执行最终构建/核心复验、结构复查及无窗口Godot回归；不能将首轮成功冒称最终全通过。只读Git检查仍可执行，最终 `git diff --check` 通过（仅LF/CRLF提示）。新增原生用例已写入 `GameplayHudVerification`，但本轮尚未运行。配色脚本检查最终金/淡蓝/白的CVD区分、普通色差与底色对比均通过；分类色板的亮度/彩度限制不通过（此处是用户指定的浅色状态条，含白色，并非分类图表），不称色板验证全通过；AP/FC/—文字提供冗余标识。未启动可见游戏、未检查GPU像素、未导出APK、未取得本次效果用户验收。规则与对象边界见 [HUD 调整](gameplay-hud-v8.md)。

2026-11-09 提交前文档整理：按用户逐项确认同步 HUD/材质/模式切换和制谱器整体验收，将独立血条设计、旧编辑器目标、一次性审查与阶段验证迁入归档，当前页保留本体接入结果/失败与操作入口。检查37份活跃或本批新归档文档的340个本地Markdown链接及标题/显式锚点，0缺失；历史验证保留检查确认47条日期表格记录和14段设计/实施记录仍在当前页或归档中；`git diff --check`通过。首轮发现新归档动效目标仍链接旧§4锚点，已改指同批旧设计归档后重查通过。**本轮仅文档与静态检查，没有重跑C#构建、产品测试、Godot或导出；最新完整verify的品牌扫描失败仍保留，用户实际效果确认单独见下文。**

> 以下保留本体接入时实际执行结果；用户后续确认见下方独立章节。本次文档整理不重新冒称构建/设备通过。

2026-11-09 v8 HUD 接入游戏本体：用户确认整体方向并授权迁入。正式客户端新增顶部装配/无框控件/血条节点及纯显示状态，接真实计分快照、BleedHealth与原暂停流程，不带入演示事件或模拟ACC；BLEED关闭隐藏、两模式固定色、耗尽同帧先清血条、Full/Reduced/Off、标题难度色条和游戏专用线条延长已接。编辑器默认chrome保持。首轮客户端构建因旧HUD装配残留出现17个缺失符号错误，清理后最终`builds/gameplay-hud-v8-build-final.log`为0警告/0错误；核心回归`gameplay-hud-v8-core-final.log`通过（新增血量显示/事件光包络测试）；结构门禁296源文件通过。无窗口Godot原生对象回归`gameplay-hud-v8-native-final.log`打印`GAMEPLAY_HUD_NATIVE PASS`，覆盖节点装配、真实shader参数、模式/关闭/耗尽、鼠标触摸回调与入口护栏、几何/难度条及编辑器默认chrome。完整`python -B tools/verify.py --no-restore`在`builds/gameplay-hud-v8-verify.log`：三端构建、核心/编辑器/Shell回归通过；37个发布测试中36通过、1失败，品牌扫描进入`.claude/worktrees/`里的旧仓库副本导致额外旧名称，未放宽白名单或清理其他工作区；因而**完整verify未全绿**，末尾diff检查另行运行通过。**未启动可见游戏、未做GPU截图或Android/触控端到端/性能验收、未出APK；对象回归不替代实机视觉。** 实施边界和入口见[游戏HUD v8](<gameplay-hud-v8.md>)，后续微调以本体为准。

## 历史证据索引

| 记录 | 现状与阅读边界 |
| --- | --- |
| [2026-11-02—08 制谱器/格式验证](archive/reports/2026-11-02-08-editor-verification.md) | 保留各轮构建、失败/重试、对象与真实窗口证据。用户已确认这批功能整体满意，可收尾；不外推全部 DPI/设备/性能 |
| [2026-10 游戏与结构验证](archive/reports/2026-10-verification.md) | 旧构建/素材/拆分/布局/规则实施证据，不覆盖最新源码 |
| [BLEED/HUD 设计过程](archive/art/2026-11-08-09-bleed-hud-design.md) | 已被本体 v8 替代的独立方案与预览；不再作为未接入待办 |
| [应用图标候选](archive/art/2026-11-08-app-icon-candidates.md) | 2026-11-08 的旧候选（A/B/C 环与字母），未采纳；现行图标见 `design/app-icons-v2/`。原文不改动，归档不等于批准发布 |
| [更早原文快照](archive/2026-10-26-cleanup/README.md) | 原始记录保留，不回写历史验证结论 |

## 用户反馈与验收边界

- **2026-11-12 基线确认**：用户明确当前导出配置可作为现有测试包基线；身份见[当前基线](#当前-windows-编辑器测试包基线)。不是仅计划固定，也不外推完整发布验收。

- **2026-11-12 用户反馈**：save-safety最小包“不错”，要求增加卸载入口后交给他人测试，并暂时固定这套导出配置。记录为对该候选的积极试用反馈，不外推全部保存边界/设备/正式发行许可；固定配置为windows-x64-test-v1。

- **2026-11-11 用户决定**：处理本地worktree污染品牌扫描的问题；headless结算shader兼容与验证退出问题暂不处理。保留诊断证据，不将其称为已修复，也不作为当前制谱器首发的自动新增任务。

- **2026-11-09 用户确认**：游戏 HUD v8 本体实际效果满意；音符材质与命中特效（侧轨旋转、Mixer 徽章、默认亮度和残影）、Standard→Hardcore→Standard 模式切换基本通过。模式切换确认不代表成绩模式隔离已实现。
- **2026-11-09 用户确认**：制谱器最近这批功能整体满意、可以收尾，范围包括放置/拖动/调宽/路径编辑、播放预览与滚轮命中特效、右键面板、跨屏框选、Dock 面板与窗口恢复。无需再把这一批整体列为待验收；设备/DPI/长谱性能等未覆盖边界仍保留。
- **仍保留专项**：Mixer 暂停恢复/原异常段落、快速切曲/试听没有新增用户确认；低端性能继续暂缓。上述通过项未附具体设备/系统清单，不补写 Android、所有触控组合或性能达标。
- **后续方向（非自动实施授权）**：在游戏本体上继续视觉微调，以及继续完善制谱器；具体新功能待后续任务。结果音效、BLEED 专用失败页等保留缺口，不自动排期。

- 2026-11-08：用户确认任务栏恢复问题已修复。此前屏幕矩形框选在播放跨屏时丢失已判定对象，不能用其静态框选测试宣称跨屏能力通过；现行规则见制谱器专题。

- 2026-11-07：用户确认**音符配色 v2、首页与结果字标已完成**；不再作为整体待验收事项。低端性能仍未验收，**暂缓**；未提供逐项设备记录，不外推到所有平台，也不把结果音效或未实现功能视为完成。

- 2026-10-07：既有结算 v2 原流程通过用户实机验收。
- 2026-10-14：用户认可已接入客户端的 X/Ψ/D；Mixer 初步反馈“目前看来没什么问题”，不扩大为全部边界通过。
- 2026-10-17：多指输入、短 Hold 手感、对象池修复、侧轨 `691/790` 基本通过。侧轨保留现值；不再把这些整体列为未验收，也不推定设备/指数量/所有边界均已覆盖。
- 校准页已有 Android 采样/统计/APPLY 全流程验收记录；10-25 拆分并未重新进行设备验收。
- 未覆盖设备/性能、密集曲线、快速切曲和暂停恢复专项仍按 [交接 §4](<handoff.md#4-仍需处理>) 跟踪。

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

## 谱包保存/读取专项复验

`tools/chart-editor-tests/Packages/PackageLifecycleTests.cs` 已接入默认编辑器核心回归。它通过 `EditorDocumentSession` 和文档命令创建原创合成谱包：三差分、每差分252个父Note（共756）、三轨七类对象、Hold/Mixer多节点、精确分数拍、BPM/Scroll及曲线、逐Note差速、包/差分试听与独立音频。用独立子进程重开并比对完整规范编码、媒体哈希及Digest；覆盖另存为源包不变、外部资源失效后的可移植性、资源替换/差分删除、Windows文件占用失败、Undo/Redo和重复保存字节稳定性。不使用原版语料，不测试断电恢复或发行包安装环境。

如需保留样本供真实窗口/游戏验证，输出目录必须不存在：

```powershell
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj -- --persistence-output builds/persistence-<unique-run>
# 按下节约定先结束残留 Godot，再运行；截图目录同样使用新的运行名。
$env:DYNA_VERIFY_PERSISTENCE_PACK = (Resolve-Path "builds/persistence-<unique-run>/搬移后 Portable").Path
dotnet run --project editor/shell/DynaMakerUv.Editor.Shell.csproj -- --smoke tmp_shots/persistence-<unique-run>
Remove-Item Env:DYNA_VERIFY_PERSISTENCE_PACK
```

该专项 `--smoke` 使用真实Avalonia窗口、Godot子窗口和IPC，逐差分等待revision回执，检查音频播放位置推进、Inspector修改、快捷键入口、保存与重开，并采集截图。文件路径通过现有验证入口注入，**不覆盖系统文件选择对话框或全部原生键鼠操作**；声音推进不等于听感验收。截图仍依赖本机 `.tmp/ui-research/capture_frame.py`，不随克隆取得。默认完整非图形verify不会启动该窗口专项。

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
2026-11-09 整理范围见 [本次整理说明](archive/reports/2026-11-09-documentation-cleanup.md)；更早的字节快照与哈希仍见 [2026-10-26 归档说明](archive/2026-10-26-cleanup/README.md)。
