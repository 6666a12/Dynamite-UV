# OCR 全量报告核实（2026-11-13）

对象：根目录 `result.json`，SHA-256 `a2002f033894c97568943b1270f8d12e12c47466ed43b833ee6aa27ec936c2de`。报告218条comments，编号为数组下标+1；针对当前工作区，不是仅HEAD。

**218条均已阅读并核对对应当前源码，完成逐项分流；不代表218条均做过运行复现，也不宣称代码库没有其他问题。** 初次核实时未改产品代码；之后用户已授权修复下述四组问题，进度见下一节。没有改result.json。下表“确认”保留修复前的审查结论，可指代码控制流/算法直接证明或已执行探针；原生窗口、真实音频、GPU像素与设备条件未覆盖时明确保留。

## 2026-11-13 授权修复范围

用户授权修复回复中1–4节所列问题；以下是修复记录，不将全量218条自动转为修复任务。

- **Dock #122–124**：恢复目标容器前先解除旧父关系，普通浮出后重挂舞台容器；首次分析面板也走恢复入口。真实Dock headless探针及正式回归覆盖。
- **文档/IPC #97/#99/#100/#106/#107/#121**：未保存关闭提示保存/放弃/取消；实际关闭后才释放服务；打开/新建后台准备、在拥有者上下文检查文档身份与修改版本后接纳；picker后再次检查；进程写入与生命周期互斥、退出写失败仍继续回收；异常协议使请求明确失败；拒绝无法回转decimal的宽度。
- **选曲/试听 #56/#59/#61–67**：首帧前收起可立即落位；取消触摸不点击；按真实控件矩形命中（包括展开卡和传输键）；装饰面板不拦截点击；切换立即停止旧试听，收起中不接受新试听；自然结束同步状态，结束后手动播放从试听起点重播；回改开关先结束旧Tween。
- **校准 #25/#26/#33/#34/#39**：有效读数撤销旧stray计时，重测立即更新数字，超窗重复点击为stray，循环时钟小幅回退钳制，视觉持续时间按实际行程计算。不改判定窗口/建议偏移公式。
- **工具 #150/#185/#186/#193/#199/#200/#209/#210**：结构规则使用真正递归glob；产物清单只含生成器明确拥有的29个文件，保留其他资产来源；打包前拒绝媒体/谱面名称冲突；Range检查206及精确区间，续传要求源URL摘要+强ETag+区间+内容hash，无强ETag不复用；APK核实manifest应用/启动入口标签引用并结构解析ECFG自定义feature，不再子串匹配。没有运行美术生成或下载真实资源。

实际构建/测试结果与未覆盖设备条件统一见 [verification.md](verification.md)。下方原始分类和证据不回写为“所有问题已修”；其余意见未自动处理。


## 分类汇总

|分类|数量|含义|
|---|---:|---|
|确认（源码/探针）|122|存在明确代码依据；不是每项都是用户可见高危|
|部分成立/需限定|51|局部问题、输入条件、调用可达性或严重性需限定|
|测试覆盖或测试清理缺口|18|不充分断言/失败清理，不是证实被测产品行为错误|
|旧路径/已停用功能|13|真实旧代码问题不等于现行产品故障|
|证据不足，保留|6|需原生/工具链专项或实际资源证据|
|维护建议|6|无直接行为损坏，低优先级|
|误报/建议不成立|2|报告忽略产品合同或实际后端能力|

## 优先处理的功能域（不是修复授权）

- **编辑器数据与生命周期**：#106未保存关闭，#107异步替换；#97/#99/#100 IPC异常/退出；#122–124 Dock恢复；#121非法宽度偏好。
- **游戏输入与选曲**：#56/#59/#61/#62/#63–67卡片、触控、试听；#55设置多指；#22/#23扫尾与Auto时间窗；#21仍区分shared结算证据和整帧输入复现，不擅改冻结玩法顺序。
- **校准**：#25/#26读数、#33重复窗口、#34时钟、#36提示、#39视觉流速。
- **发布/验证可靠性**：#150结构递归规则；#185–186资产来源；#209–212 APK校验边界。
- **性能/视觉**：#86/#110/#118多选扫描；#135/#137/#138/#144/#145；不因静态复杂度直接宣称低端性能失败，也不自动重启用户暂缓的优化专项。

## 实际执行与反证

- 原始纯C#类探针：#56展开后首帧前收起，两秒后仍presenting/collapsing且不再Animating；正常先展开完成再收起对照通过。#33窗口外重复点返回Duplicate；#34时间1→0.995；#42大滚动坐标不前进；#16首个结束帧过算。
- 数值探针：#10 JSON 1e400解析Infinity；#121 decimal上界double回转抛OverflowException；#137 Mine末帧半径-1。#24报告的int.Max反号例在本机.NET10不复现，但int.Min得到上界140而非下界80，确认存在溢出。#35报告3e9反号例在本机得到+300，不冒称已复现。
- #21直接调用原shared SettleReleasedHold：1.05的未结算body在release1.10时记Miss；连同帧序证据支持疑点，但未模拟完整Godot输入批次。#14只验证结束模型在原判定时间冻结时继续到终点，真实音频/末尾迟判还需专项。
- 离线Python探针：#199错误HTTP状态/Range但同长度被接受；#200不同URL复用旧分段；#151错误Comfy历史被当完成；#154工作流尺寸节点未接入；#193临时包实际输出OK但chart被cover覆盖；#204提取原纯多边形算法发现5个inset顶点在外；#150 Path.match漏直层及多层目录（当前9文件漏广域规则，但未发现这些文件已有禁用名称）。不连接网络服务、不读取真实用户包。
- **真实Avalonia/Dock对象探针**：#122直接FloatDockable后原stage-dock离主树，DockStage仍无stage；#123关闭tools/timing后首次analysis不可见；#124嵌套left恢复后两个父集合均含同一对象；#130坏layout校验通过。仅UseHeadless、不启动Godot、不开原生窗口、不接用户偏好。第一次探针因Factory.Find返回重复结果的Single报错，改按实际树/字段核实后退出0；未为探针修改产品。
- 引擎语义复核使用本机Godot源树：ResourceLoader失败请求仍须get回收user token（#52）；触屏合成鼠标作为独立事件（#92）。这不是新构建引擎验证。
- #166反证：冻结v2合同明确Hold尾必须显式judge:true，C#校验器一致；Python不是误拒绝。#91反证：默认音频探测器拒绝Opus/M4A/AAC，核心扩展名表不证明产品支持。

本机探针：`.tmp/ocr-verification-20261112/probe/`、`.tmp/ocr-python-probes.py`、`.tmp/ocr-dock-probe/src/`（忽略目录，不随克隆提供）。纯逻辑net9探针首次运行缺系统net9退出150，随后用已装net10 `--roll-forward Major`执行，不能外推成Godot内置net9验证。没有安装依赖、修改测试策略、运行卸载或Frida插桩、生成新美术资产。

## 尚未解决的证据边界

U条目：#50, #85, #112, #178, #196, #203。这些已核对代码但尚不能确认报告的实际后果；不是遗漏、不是误报，也不计入确认缺陷。其他P项的条件见逐条说明。

## 全量逐项清单

路径/行号取报告锚点，当前UI接入可能使行号偏移；以当前符号和说明为准。重复项保留原编号，便于回查，但不能把同根因重复计为独立修复任务。

### 1–43：基础工具、玩法与校准

|编号|结论|源码锚点|核实依据与限制|
|---|---|---|---|
|1|部分成立/需限定|`tools/memscan/memscan.c:29`|malloc无NULL检查属实；研究扫描器内存不足路径，未实际施加OOM。进程不会保证静默成功，报告后果并非唯一。|
|2|确认（源码/探针）|`tools/memscan/memscan.c:60`|保留PATLEN而非PATLEN-1会重复扫描边界上完整签名；仅独立研究扫描器。|
|3|确认（源码/探针）|`tools/memscan/memscan.c:45`|pread<=0直接break而scanned仍加整个区间、最终return0；短读/中断覆盖率报告不可靠。|
|4|部分成立/需限定|`tools/memscan/memscan.c:44`|32位off_t配置下成立；本机/目标是否采用该构建未知，64位构建不受影响。|
|5|维护建议|`tools/memscan/memscan.c:66`|缺显式free/fclose/close属实，但单次进程退出由OS回收，不是常驻泄漏。|
|6|确认（源码/探针）|`tools/memscan/memscan.c:52`|上下文读取未钳mapping起点，前32字节附近可能读失败或无符号回绕；研究工具。|
|7|确认（源码/探针）|`tools/editor-release/launcher.cpp:125`|探测成功后CreateProcess失败直接返回错误码，无对话框/日志；故障诊断缺口，不应重试应用业务失败。|
|8|确认（源码/探针）|`tools/editor-release/launcher.cpp:129`|catch丢失what()且无追加日志；Incomplete package等只显示通用提示。|
|9|维护建议|`tools/editor-release/launcher.cpp:67`|CommandLineToArgvW至LocalFree间分配异常会漏清理；进程即退出，低优先级RAII建议。|
|10|确认（源码/探针）|`client/scripts/game/charts/LegacyChartMetadata.cs:63`|1e400经TryGetDouble为Infinity；ChartPreview无校验，播放器Math.Max不拦截。已纯逻辑复现解析链关键值。|
|11|确认（源码/探针）|`client/scripts/game/charts/ChartPack.cs:122`|legacy CoverPath走ResolvePath而非ResolveLegacyRegularFile，仅检查祖先目录，缺叶链接检查；res虚拟资源另论。|
|12|部分成立/需限定|`client/scripts/audio/SongClock.cs:77`|无Attach时fallback初始化缺口属实；现行两个创建路径均先Attach，不是正常运行故障。|
|13|部分成立/需限定|`client/scripts/audio/SongClock.cs:100`|Resume自身不幂等；现行暂停层有IsPaused/_motionInFlight控制，未找到正常重复恢复调用。|
|14|部分成立/需限定|`client/scripts/game/GameplayMain.cs:253`|原始判定时间与结束补时分离属实；模型已复现。strict v2限制末判定不超过音频+50ms，legacy及末尾迟判仍需实际音频验证。|
|15|部分成立/需限定|`client/scripts/game/charts/V2ChartMetadataAdapter.cs:0`|TrimEnd确实破坏/、C:\和res://根路径；正常扫描器传子目录，属于公开Load根目录输入边界。|
|16|确认（源码/探针）|`client/scripts/game/gameplay/GameplayEndCondition.cs:63`|首次audioEnded帧立即加delta；10秒采样+0.5秒delta立即满足10.5阈值，原类探针复现。|
|17|部分成立/需限定|`client/scripts/game/charts/V2Integration.cs:33`|catch覆盖所有InvalidDataException属实；但项目既定允许不可转换legacy回退，不能仅依局部“空timeline”注释把所有回退判错。损坏结构的拒绝范围需明确。|
|18|确认（源码/探针）|`client/scripts/game/common/Res.cs:71`|WAV/OGG/MP3直接引擎加载，无输入预算；只有FLAC有64MiB输入/PCM限额。未施加OOM。|
|19|部分成立/需限定|`client/scripts/game/common/Res.cs:89`|锁外加载可在失效后回填；当前EvictAudio/ClearAudioCache无正式调用点，属于潜在接口并发缺口。|
|20|确认（源码/探针）|`client/scripts/game/gameplay/GameplayMain.Input.cs:66`|F12无开发特性守卫，仅Seek、不清输入/判定/渲染状态；目标已过时可倒退。调试入口缺隔离，不新增常规seek需求。|
|21|部分成立/需限定|`client/scripts/game/gameplay/GameplayMain.Sustain.cs:142`|原shared批量结算确实将release前尚未判的body记Miss，已探针；帧序UpdateSustain在Sweep前匹配报告。仍需带真实输入队列的整帧验证；既有冻结规则/暂停专项不能擅改。|
|22|确认（源码/探针）|`client/scripts/game/gameplay/GameplayMain.Judgement.cs:152`|未来未判Input触发Sweep提前break，后面的Contact/Mine早窗无法扫描；源码控制流确认，未跑原生输入。|
|23|确认（源码/探针）|`client/scripts/game/gameplay/GameplayMain.Judgement.cs:226`|Contact晚窗检查在Auto前，跨窗帧可记Miss；Input Auto走精准计划时间，二者不一致。|
|24|确认（源码/探针）|`client/scripts/game/settings/GameSettings.cs:314`|先取整乘5会溢出；报告int.Max例在当前.NET10得到140并未复现，但int.Min得到140而非80已复现。应修边界，不照抄例子。|
|25|确认（源码/探针）|`client/scripts/ui/CalibrationScreen.cs:254`|ShowHitReadout不清旧stray倒计时；_Process超时把新_lastHitKind清None并刷新。|
|26|确认（源码/探针）|`client/scripts/ui/CalibrationScreen.cs:281`|RestartSampling清模型后只ApplyPhase；CommitPhaseVisuals不更新数值，_Process亦不常规RefreshReadouts，旧统计留到下次操作。|
|27|部分成立/需限定|`client/scripts/game/session/GameSession.cs:38`|Rescan按ID保留旧对象、IndexOf引用查找可为-1；现行Rescan仅初始化调用，重复扫描接口潜在缺口。|
|28|部分成立/需限定|`client/scripts/game/session/GameSession.cs:41`|同ID重扫不作内容版本失效属实；当前无热更新/重复扫描入口，不能声称普通切歌已读旧包。|
|29|部分成立/需限定|`client/scripts/game/verification/V2IntegrationVerification.cs:25`|StartGatePath未Globalize而Ready/Trace有转换；官方录制脚本传绝对路径不受影响。user://参数边界。|
|30|确认（源码/探针）|`client/scripts/game/verification/V2IntegrationVerification.cs:160`|trace按runtime数值ID排序后才投影LegacyComparableId；转换按源字符串序分配ID，同刻2/10会与legacy顺序不同。|
|31|确认（源码/探针）|`client/scripts/game/verification/V2IntegrationVerification.cs:202`|先Flush异常将跳过Dispose；故障时资源释放缺口，仅录制trace。|
|32|测试覆盖或测试清理缺口|`client/scripts/game/verification/GameplayHudVerification.cs:60`|Full推进0.05再Reduced推进0.1越过0.14寿命，impact自然归零也通过；需要独立区分模式的断言。|
|33|确认（源码/探针）|`client/scripts/ui/calibration/CalibrationSampling.cs:116`|原CalibrationSampling探针Accepted(4.02)后Hit(4.5)返回Duplicate；窗口外点显示有效但不新增样本。|
|34|确认（源码/探针）|`client/scripts/ui/calibration/CalibrationBeatSource.cs:49`|原LoopPositionUnwrapper探针1.000->0.995倒退；正式校准有音频分支直接用它，绕过SongClock过滤。|
|35|部分成立/需限定|`client/scripts/ui/calibration/CalibrationStats.cs:103`|接口对极大double的cast具有运行时边界；报告3e9在本机.NET10返回+300，未复现反号。正常来源current±300、样本±150，不可当正常校准高危。|
|36|确认（源码/探针）|`client/scripts/ui/calibration/CalibrationScreen.Layout.cs:207`|提示任意位置但HandleTap限定中央TapZone；是提示问题，不能擅自扩大点击区域。|
|37|确认（源码/探针）|`client/scripts/ui/calibration/CalibrationWidgets.cs:279`|StepperButton/HoverLabel禁用setter只重画不启用process；已停止的hoverFade可残留，代码确认。|
|38|部分成立/需限定|`client/scripts/ui/common/CutButton.cs:100`|绘制不检测模式切换属实；Settings页切Motion可影响存活按钮。v3去掉文字hover平移但方向定位线仍需更新；不是所有按钮状态均受影响。|
|39|确认（源码/探针）|`client/scripts/ui/calibration/CalibrationWidgets.cs:58`|渲染行程703而持续时间按790算，速度约0.890倍；纯几何不一致，判定时间公式另保持。|
|40|旧路径/已停用功能|`client/scripts/ui/common/UiGlyphs.cs:77`|Auto圆角采样跳过切点属实；v3隐藏该快捷图标，现行页面不显示此处。低优先级旧绘制路径。|
|41|旧路径/已停用功能|`client/scripts/ui/common/UiGlyphs.cs:68`|Mirror中心线替换alpha而非乘alpha属实；v3隐藏该图标且未找到透明色实参调用，不是现行淡出故障。|
|42|确认（源码/探针）|`client/scripts/ui/common/ScrollInertia.cs:21`|原ScrollInertia探针1048576到1048577在240Hz不前进；需约百万px超长列表，正常短库不受影响。|
|43|确认（源码/探针）|`client/scripts/ui/home/HomeActionTile.cs:84`|HomeActionTile已SetProcess(false)后按下只设置_pressAmount，未重启；绘制存在卡住风险，常规导航很快离页会掩盖。|

### 44–74：首页、选曲、过场

|编号|结论|源码锚点|核实依据与限制|
|---|---|---|---|
|44|旧路径/已停用功能|`client/scripts/ui/home/HomeEnergyCore.cs:29`|HomeEnergyCore旧类无实例化/场景引用；局部模式缓存问题不能当当前首页问题。|
|45|旧路径/已停用功能|`client/scripts/ui/home/HomeEnergyCore.cs:37`|同44；旧类周期8秒不是当前MkVI动画来源。|
|46|旧路径/已停用功能|`client/scripts/ui/note_colors/NoteColorsWidgets.cs:501`|PresetSlot旧控件无调用；现行预设由TechButton构建，不受旧touch release路径影响。|
|47|旧路径/已停用功能|`client/scripts/ui/note_colors/NoteColorsWidgets.cs:512`|同46；旧长按捕获问题不等于现行预设手势问题。|
|48|旧路径/已停用功能|`client/scripts/ui/note_colors/NoteColorsWidgets.cs:621`|SegmentedChips无实例化；空Configure潜在非法索引存在于旧控件，当前使用其他分段器。|
|49|确认（源码/探针）|`client/scripts/ui/home/HomeEnergyReactor.cs:276`|Play传0.45 custom_speed，下一帧SpeedScale再设0.45；Godot乘法语义导致0.2025，Full也保留custom_speed。源码确认。|
|50|证据不足，保留|`client/scripts/ui/home/HomeEnergyReactor.cs:269`|未显式赋AssignedAnimation确实有缺口；初始导入GLB播放器是否已有assigned/default姿态须查实际资源/原生运行，不能仅凭Play分支断言视觉必错。|
|51|部分成立/需限定|`client/scripts/ui/note_colors/NotePaletteSpecimen.cs:41`|Configure离树缓存后Ready未RestoreNotes；当前页面AddChild后再Refresh配置，故现行调色页不触发。接口边界。|
|52|确认（源码/探针）|`client/scripts/ui/results/ResultGradeArt.cs:32`|本地同版Godot resource_loader.cpp:932-995确认load_threaded_get负责user_rc回收；失败未get会保留token，源码确认。|
|53|部分成立/需限定|`client/scripts/ui/results/ResultScreen.cs:341`|延迟Tween不被ResetForPresent取消属实；当前ResultFlow一次结算闸门避免同屏重复Present，潜在重用接口问题。|
|54|部分成立/需限定|`client/scripts/ui/song_select/SongBrowseState.cs:49`|Restore找不到chart保留旧DifficultyIndex；实际每次进入选曲新建browse且初始0，后续又Clamp，普通入口不触发报告连续Restore例。|
|55|确认（源码/探针）|`client/scripts/ui/settings/SettingsWidgets.cs:197`|SettingsSlider无touch index，ScreenDrag直接ApplyAt；多指可覆盖同一滑块。未真机复现。|
|56|确认（源码/探针）|`client/scripts/ui/song_select/SongCardMotion.cs:159`|原SongCardMotion探针复现首帧前Expand/Collapse卡住；完整展开再收起对照通过。|
|57|部分成立/需限定|`client/scripts/ui/song_select/SongCardMotion.cs:172`|Applied分支不清旧fade属实；当前模式只能离开选曲去设置改变，无法在同一卡存活时正常切模式。接口健壮性缺口。|
|58|部分成立/需限定|`client/scripts/ui/song_select/SongCardMotion.cs:152`|即时Collapse不清resize状态属实；报告依赖同一卡内切模式，现行路由通常重建页面。接口健壮性缺口。|
|59|确认（源码/探针）|`client/scripts/ui/song_select/SongListViewport.cs:61`|释放分支不排除Canceled，取消未移动触点仍选择歌曲。|
|60|部分成立/需限定|`client/scripts/ui/song_select/SongRow.cs:76`|SongRow未按profile变化强制重画属实；当前模式变更离页，正常新建行不触发。与38不同可达性。|
|61|确认（源码/探针）|`client/scripts/ui/song_select/SongDetailCard.cs:68`|详情装饰CutPanel默认Stop且无Ignore，阻止父卡接收鼠标；v3仅改paint未修此输入问题。Godot控件传播规则确认。|
|62|确认（源码/探针）|`client/scripts/ui/song_select/SongSelect.Library.cs:63`|392px详情仍按118步距触摸索引；坐标430实际第2首却算第4首，探针确认。|
|63|确认（源码/探针）|`client/scripts/ui/song_select/SongSelect.Loading.cs:91`|OnRowPressed/NextDifficulty->BeginRefreshAll未StopPreview；仅新StartPreview或Collapse才Stop；新曲加载中/失败时旧试听可持续。|
|64|确认（源码/探针）|`client/scripts/ui/song_select/SongPreviewPlayer.cs:84`|试听无Finished处理，短音频默认30秒预览播完后Playing仍true；代码确认，未原生音频复现。|
|65|确认（源码/探针）|`client/scripts/ui/song_select/SongPreviewPlayer.cs:70`|StopAtEnd调用Stop却保留_stream；TogglePause仅改StreamPaused不Play，结束后按钮可显示播放却无声音。|
|66|确认（源码/探针）|`client/scripts/ui/song_select/SongSelect.Library.cs:128`|筛选折叠中IsPresenting仍true，替换曲完成可StartPreview；落点只Filter未Stop。异步时序控制流确认。|
|67|确认（源码/探针）|`client/scripts/ui/song_select/SongSelectWidgets.cs:169`|QuickSwitch near-target提前return不Kill旧Tween；连续状态回改可被旧动画覆盖，不需要修改业务值也能复现视觉错位。|
|68|确认（源码/探针）|`client/scripts/ui/transitions/TrackRelayPresentation.cs:39`|CoverTextureCache发布path后允许texture=null；同路径下次直接返回null，失败无重试。|
|69|部分成立/需限定|`client/scripts/ui/transitions/TransitionDirector.cs:38`|nullable枚举null!=Idle为true属实；正常autoload存在，缺director的直接测试/提前初始化接口才受影响。|
|70|部分成立/需限定|`client/scripts/ui/transitions/TransitionDirector.cs:399`|文档称eased、实现回调raw确实不一致；当前消费者与转场Kind需区分，不能盲目加二次easing。|
|71|确认（源码/探针）|`client/scripts/ui/transitions/TransitionOverlay.cs:135`|DrawBackdrop斜线无rect裁剪，正/反wipe可超覆盖边缘；属于视觉缺口，不等于用户不满意已通过样式。|
|72|确认（源码/探针）|`client/scripts/ui/transitions/TransitionOverlay.cs:94`|非方向淡出背景用coverage、marks恒1；信号标记会突然消失，源码确认。|
|73|确认（源码/探针）|`editor/scripts/stage/EditorContextMenu.cs:163`|菜单FilterInput吞除Tab外键盘，方向键到不了HSlider；Enter/Space非Button焦点也吞，正式菜单键盘路径。|
|74|确认（源码/探针）|`editor/scripts/stage/EditorAutoPreview.cs:56`|播放Advance先UpdateViews再Resolve，拖动ScrubForward反序；大步长可先回收再命中缺残影，源码确认，需原生视觉专项。|

### 75–109：编辑器舞台、进程与窗口

|编号|结论|源码锚点|核实依据与限制|
|---|---|---|---|
|75|测试覆盖或测试清理缺口|`editor/scripts/stage/EditorGestureVerification.cs:118`|fractional commit只断言head Bar，tail/geometry验证的是先前preview，不保证提交请求字段正确。|
|76|测试覆盖或测试清理缺口|`editor/scripts/stage/EditorGestureVerification.cs:161`|拖动只断言identity/route，未核实位移字段；它证明路由而非位移正确。|
|77|测试覆盖或测试清理缺口|`editor/scripts/stage/EditorMagnetVerification.cs:100`|Alt-down后断言异常无finally release；仅实验磁吸原生测试污染风险。|
|78|测试覆盖或测试清理缺口|`editor/scripts/stage/EditorHudVerification.cs:16`|HUD plates.All对空集合通过；其他Bleed/line断言不能证明plates存在。|
|79|测试覆盖或测试清理缺口|`editor/scripts/stage/EditorHudVerification.cs:50`|mouse/touch合计2不能分别证明各1，测试断言覆盖缺口。|
|80|确认（源码/探针）|`editor/scripts/stage/EditorPlacementOverlay.cs:155`|长条ghost由headVisible gate整组隐藏；头过线100px即隐藏仍可见尾/连线，放置预览路径。|
|81|确认（源码/探针）|`editor/scripts/stage/EditorStagePreview.cs:564`|SelectionBounds跟随Auto、左击HitNote仍按静态位置；暂停中Hold头视觉和选择命中不一致，源码确认。|
|82|确认（源码/探针）|`editor/scripts/stage/EditorStageIpc.cs:180`|Drain后读取Ended，reader可在间隙入队并EOF；最后命令可被跳过退出。并发控制流确认，未压力测试。|
|83|部分成立/需限定|`editor/scripts/stage/EditorStageIpc.cs:29`|Drain无帧预算、queue无界属实；当前可信本机Shell生产者，实际堵塞需负载测量，不是远程攻击面。|
|84|旧路径/已停用功能|`editor/scripts/stage/EditorObjectMagnet.cs:66`|磁吸width inverse未处理12px clamp有数学缺口；正式Shell ObjectSnapEnabled强制false，不在当前功能修复范围。|
|85|证据不足，保留|`editor/scripts/stage/EditorPreviewController.cs:107`|播放器独立delta时钟且不对齐音频已确认；长帧截断/漂移大小及末尾重播需要真实音频stall专项，未把高危后果当已复现。|
|86|确认（源码/探针）|`editor/scripts/stage/EditorPreviewController.cs:52`|每个选中ID都AllNotes.Single，确认O(选中数×谱规模)；未测低端耗时，不宣称性能达标或崩溃。|
|87|确认（源码/探针）|`editor/scripts/stage/EditorPreviewController.cs:428`|async void Capture在await后的异常逃离Drain catch，无生命周期检查；故障路径确认。|
|88|部分成立/需限定|`editor/scripts/stage/EditorStageAuthoring.cs:177`|无V2Metadata时选择可空引用；正式编辑器输入由V2RuntimeAdapter产生，未发现正常无metadata note进入该列表。|
|89|旧路径/已停用功能|`editor/scripts/stage/EditorStageAuthoring.cs:404`|放置/点击路径退出未reset磁吸latch；正式磁吸已停用，局部实验缺陷不应恢复功能。|
|90|确认（源码/探针）|`editor/shell/CreateChartDialog.axaml.cs:119`|BrowseAudio/Cover async void await未catch，PickFile失败可未处理；代码确认，未模拟系统picker故障。|
|91|误报/建议不成立|`editor/shell/CreateChartDialog.axaml.cs:13`|将核心扩展名允许等同默认解码支持错误；默认probe明确拒绝Opus/M4A/AAC，不能将这些加入选择器来修。|
|92|确认（源码/探针）|`editor/scripts/stage/EditorTransportButton.cs:39`|editor未关闭mouse-from-touch，控件同时接收两种press；同版Godot input.cpp明确合成额外事件。未触屏设备实测。|
|93|确认（源码/探针）|`editor/shell/DesktopVerification.cs:607`|redirect stderr却先WaitForExit，无timeout；缓冲填满可死锁，测试采集工具问题。|
|94|确认（源码/探针）|`editor/shell/DesktopVerification.cs:419`|finally写报告失败跳过Close/Shutdown，async dispatcher回调未保护清理；测试故障路径。|
|95|测试覆盖或测试清理缺口|`editor/shell/DesktopVerification.cs:584`|只校验after.AudioSecond，NaN差值比较均false，断言可误通过；不是已发现实际音频NaN。|
|96|确认（源码/探针）|`editor/scripts/stage/EditorStagePreview.cs:507`|零stage scale未防护，活跃拖拽viewport motion/release仍转换除0；源码确认，需窗口resize专项。|
|97|确认（源码/探针）|`editor/shell/GodotStageService.cs:57`|Running与pipe write有TOCTOU，write无catch，Dispose/Start不共锁；自然退出与UI命令并发时异常可外逸。|
|98|部分成立/需限定|`editor/shell/GodotStageService.cs:39`|Start复用已退出process时未释放旧实例/过滤旧事件属实；正式UI是否重启同service实例未证实，接口生命周期缺口。|
|99|确认（源码/探针）|`editor/shell/GodotStageService.cs:101`|quit写失败跳过wait/kill；Kill其他异常又可跳过Dispose。进程退出清理不可靠，源码确认。|
|100|确认（源码/探针）|`editor/shell/GodotStageService.cs:119`|TryRemove后再parse可使request丢失仅timeout；GetInt32/64 FormatException不在catch，协议异常边界。|
|101|确认（源码/探针）|`editor/shell/InspectorView.axaml.cs:48`|Inspector订阅静态localizer无解订阅；文档/控件/回调被保留。普通整进程退出回收，反复窗口/测试可累积。|
|102|确认（源码/探针）|`editor/shell/PackageSettingsDialog.axaml.cs:163`|PackageSettingsDialog同90，三个async picker入口无异常隔离。|
|103|测试覆盖或测试清理缺口|`editor/shell/SelectionInspectorVerification.cs:52`|所谓track edit验证只比width，track没有断言；不是证实track编辑已坏。|
|104|测试覆盖或测试清理缺口|`editor/shell/SelectionInspectorVerification.cs:40`|失败/undo仅验证首音符，不证明批量原子性；现有其他核心用例不替代这个UI断言。|
|105|测试覆盖或测试清理缺口|`editor/shell/SaveInteractionVerification.cs:74`|失败finally仅resume.Set即释放events/关窗，未await save，可能后台仍用event且_ioBusy拒绝Close；正常通过路径已等完成。|
|106|确认（源码/探针）|`editor/shell/MainWindow.axaml.cs:146`|关闭只防_ioBusy不查dirty，未保存确认缺失；与保存中禁止退出不同。|
|107|确认（源码/探针）|`editor/shell/MainWindow.axaml.cs:466`|picker前检查dirty、返回后不复查；后台替换期间Undo/舞台place未统一_ioBusy防护，旧文档编辑可能丢。未窗口并发复现。|
|108|部分成立/需限定|`editor/shell/MainWindow.axaml.cs:138`|清理放在可取消Closing确实不安全；依赖之后有订阅者取消，当前Dock时序还需验证，不声称每次取消均坏。|
|109|确认（源码/探针）|`editor/shell/MainWindow.axaml.cs:700`|非标准吸附分母先FindLastIndex再-1跳过最近低档；10向下得到4而非8，源码算术确认。|

### 110–145：工作台、回归、共用视觉

|编号|结论|源码锚点|核实依据与限制|
|---|---|---|---|
|110|确认（源码/探针）|`editor/shell/InspectorView.axaml.cs:205`|每次Inspector更新全量Analyze+对象列表，选中集合线性Contains；复杂度确认，卡顿程度未基准。|
|111|确认（源码/探针）|`editor/shell/NativeStageHost.cs:134`|SetParent/MoveWindow返回值不检查，先写cache，失败后相同布局不重试；Win32失败路径。|
|112|证据不足，保留|`editor/shell/NativeStageHost.cs:118`|fallbackParent首值常驻属实；首次浮窗承载/随后销毁的实际Dock时序需复现，不能断言现有验收被推翻。|
|113|测试覆盖或测试清理缺口|`editor/shell/PackageDesktopVerification.cs:23`|Ready前SendDocument不提交revision，等待旧revision会超时；慢启动测试假失败风险。|
|114|确认（源码/探针）|`editor/shell/Program.cs:17`|smoke参数取下一个token不排除其他选项；错误参数产生意外目录，仅测试CLI。|
|115|部分成立/需限定|`editor/shell/Program.cs:22`|正常桌面分支丢StartWithClassicDesktopLifetime返回码；当前正常退出0，测试分支已有显式return，不是所有自动验证退出码丢失。|
|116|确认（源码/探针）|`editor/shell/TimelineView.cs:85`|Timeline缺OnPointerCaptureLost，_scrubbing残留；Avalonia控件捕获丢失路径。|
|117|确认（源码/探针）|`editor/shell/TimelineView.cs:91`|Timeline未绑定pointer/初始按钮，其他release可结束拖拽；同116功能域。|
|118|确认（源码/探针）|`editor/shell/SelectionInspectorView.axaml.cs:34`|SelectionInspector ids为IReadOnlyList，逐note Contains确认O(N×S)，正式多选路径。|
|119|部分成立/需限定|`editor/shell/TimingView.axaml.cs:280`|空时间直接Parse可能throw；正常非负播放头受上层限制，负pre-roll/异常值需确认可达性。防御性边界而非常规添加必崩。|
|120|确认（源码/探针）|`editor/shell/TimingView.axaml.cs:35`|TimingView静态localizer强引用无解订阅，与101同根因。|
|121|确认（源码/探针）|`editor/shell/ToolsView.axaml.cs:21`|偏好接受(double)decimal.MaxValue而SetPlacementWidth强转overflow；原数值探针复现。|
|122|确认（源码/探针）|`editor/shell/WorkbenchFactory.cs:125`|真实Avalonia/Dock headless对象探针：不经定制FloatStage而直接FloatDockable，原stage-dock脱离主树；DockStage后主树stage计数仍0。未运行Godot/真实HWND。|
|123|确认（源码/探针）|`editor/shell/WorkbenchFactory.cs:198`|真实Dock探针关闭tools/timing后left消失；首次AddAnalysisPanel后可见树analysis计数0。对象级确认，analysis本身为占位功能。|
|124|确认（源码/探针）|`editor/shell/WorkbenchFactory.cs:151`|真实Dock探针将left移动到nested proportional，再RestoreWorkspace，left同时存在于main和nested集合（True/True），对象级确认重复父引用。|
|125|部分成立/需限定|`editor/shell/ShellChartEditor.cs:48`|插入后重归一化非线性曲线确实改变其他采样值；但添加控制点是否承诺整体形状不变需产品定义。不是时值损坏/保存丢失。|
|126|确认（源码/探针）|`editor/shell/WorkbenchLayoutSession.cs:23`|屏幕工作区小于模型最小尺寸时先min压小再Validate拒绝布局；极小逻辑工作区边界。|
|127|测试覆盖或测试清理缺口|`editor/shell/Verification.cs:26`|headless泵无总deadline属实；外部runner也没有per-command timeout，测试挂起不能自报失败。|
|128|测试覆盖或测试清理缺口|`editor/shell/Verification.cs:330`|CreateChartDialog.Show后ConfirmForVerification仅BuildResult且未Close；测试成功路径也留下modeless窗口。|
|129|测试覆盖或测试清理缺口|`editor/shell/Verification.cs:56`|IconButton.All对空集合通过；其他Dock断言不保证Ursa目标存在。|
|130|确认（源码/探针）|`editor/shell/WorkbenchLayoutSnapshot.cs:47`|Validate遍历所有集合、Restore仅root恢复隐藏/固定集合且split忽略children；真实类型反射探针构造non-root Hidden stage，Validate通过。Restore会忽略该集合，文件输入边界。|
|131|测试覆盖或测试清理缺口|`editor/shell/WorkbenchPersistenceVerification.cs:31`|测试窗口/语言恢复仅success，异常失败后污染后续；未断言生产应用本身失败。|
|132|测试覆盖或测试清理缺口|`editor/shell/WorkbenchPersistenceVerification.cs:47`|!ReferenceEquals(null,reopened)为true；未附着stage会误报floating restored。|
|133|测试覆盖或测试清理缺口|`editor/shell/WorkspaceDesktopVerification.cs:98`|点击View前已恢复，postcondition仅旧stage仍在，无法证明恢复动作工作。|
|134|部分成立/需限定|`presentation/game/GameplayEffects.cs:139`|单独MotionMode切Off会遗留particles；当前editor将Off映射HitEffectLevel.Off先Release，游戏不在游玩页切模式。接口组合缺口，非正常已证实。|
|135|确认（源码/探针）|`presentation/game/GameplayEffects.cs:170`|持续粒子Span仅ResetFor时设置，Hold/Mixer变宽时未同步，正式动态路径视觉偏差。|
|136|测试覆盖或测试清理缺口|`editor/shell/WorkspaceVerification.cs:145`|按realized控件枚举且不要求非空、遇非ComboBoxItem跳过；测试可能漏掉本应验证控件。|
|137|确认（源码/探针）|`presentation/game/GameplayHitBloom.cs:161`|150%末帧radius156，Lerp(12,4,156/96)=-1，已算术探针；负半径调用缺口。|
|138|确认（源码/探针）|`presentation/game/GameplayHitBloom.cs:52`|Bloom ResetForPool仅隐藏不SetProcess(false)，池节点仍树内，空闲继续_age/QueueRedraw；代码确认，未性能基准。|
|139|旧路径/已停用功能|`presentation/game/GameplayStageRenderer.cs:71`|Ghost过滤仅Rebuild而非seek属实；SetGhostNoteIds无当前调用点，旧预览接口缺口。|
|140|旧路径/已停用功能|`presentation/game/GameplayStageRenderer.cs:129`|Ghost Hold clamp后SetPreview立即覆盖属实；同139未启用ghost接口。|
|141|部分成立/需限定|`presentation/game/GameplayStageRenderer.cs:115`|Rebuild清NoteLayer所有children与公开共享层契约不一致；游戏只build chrome后放动态视图不再Rebuild，编辑器此层由renderer独占。|
|142|确认（源码/探针）|`presentation/game/GameplaySustainEffect.cs:35`|SetContact断再连时_age=0，重新爬亮，与first-contact-only注释不符；视觉需求若接受可修注释而非强制改效果。|
|143|部分成立/需限定|`presentation/game/GameplaySustainEffect.cs:89`|连续pulse调alpha属实，与注释“只内部12帧”不完全一致；已认可视觉效果，属于规格/实现偏差，不宜作为功能错误盲修。|
|144|确认（源码/探针）|`presentation/game/GameplayNoteRenderer.cs:187`|成功早判发生在生成提前量之前时，无已消费ID阻止后生成；Hold可自建头后再Spawn重头。高倍scroll能达到条件，源码确认。|
|145|确认（源码/探针）|`presentation/game/GameplayNoteRenderer.cs:159`|任意非默认倍率激活全剩余列表每帧扫描+RemoveAt；复杂度缺口属实，替代阈值索引需兼容Scroll非单调，不能直接套建议。|

### 146–184：发行辅助、研究工具、格式校验

|编号|结论|源码锚点|核实依据与限制|
|---|---|---|---|
|146|部分成立/需限定|`tools/editor-release/uninstall.ps1:69`|手写反斜杠manifest键验证接受但lookup改成斜杠会miss；官方builder总as_posix，当前发行manifest不触发。|
|147|部分成立/需限定|`tools/editor-release/uninstall.ps1:73`|hash-close-delete存在并发竞态，非原子身份验证；正常关闭编辑器的受控卸载通过不证明并发安全，high需按本地攻击/并发条件降级。|
|148|确认（源码/探针）|`tools/editor-release/uninstall.ps1:75`|manifest确认前快照，确认后不验证自身就删除；长确认期间更新manifest可丢元数据。|
|149|部分成立/需限定|`tools/editor-release/uninstall.ps1:78`|空目录清理未重验祖先链接，需并发替换才越界；本地竞态属实，未实作链接攻击或运行真实卸载。|
|150|确认（源码/探针）|`tools/architecture/check.py:50`|Path.match **非递归，探针及policy确认；9个当前深层源文件漏广域规则，但这些文件未发现该规则禁用名称。门禁缺口≠已有架构违规。|
|151|确认（源码/探针）|`tools/comfy_gen.py:102`|wait_done见history就返回error状态，mock接口已复现；未接Comfy服务。|
|152|确认（源码/探针）|`tools/comfy_gen.py:143`|name_01等固定目的地copy2覆盖，复用job name/out目录丢旧输出；研究生成工具，不是内置曲绘功能。|
|153|确认（源码/探针）|`tools/comfy_gen.py:136`|out_dir且无comfy_output走仅打印分支并丢subfolder，承诺输出目录被静默忽略。|
|154|确认（源码/探针）|`tools/comfy_gen.py:82`|img2img实际latent链VAEEncode->LoadImage，无resize；width/height只在未接的EmptyLatent节点。已检查生成workflow。|
|155|确认（源码/探针）|`tools/chart_stats.py:298`|第二遍bar_to_sec除BPM无0校验/异常隔离，坏BPM会中断统计报告；研究工具。|
|156|部分成立/需限定|`tools/chart_stats.py:180`|同轨重复ID覆盖字典，派生统计不完整；但此前明确记录collision，不能说工具完全静默接受。坏输入统计边界。|
|157|确认（源码/探针）|`tools/chart_stats.py:201`|type pair用targets[0]而同轨判定另用集合，跨轨同ID时类型统计可错；研究数据不等同strict v2唯一ID。|
|158|部分成立/需限定|`tools/chart_stats.py:222`|链长度确实包含异常终止，但结果并未明确声明每条都是有效完整链；可增加terminal reason，不宜把“完整性校验失败”当既有合同。|
|159|确认（源码/探针）|`tools/chart_stats.py:386`|通用统计输出硬编码所有难度映射结论，未根据当前输入验证；历史观察可单列不能冒充每次实测。|
|160|部分成立/需限定|`tools/editor-release/build.py:48`|copy_runtime不检架构属实，仅非trimmed fallback使用；当前固定profile走win-x64 publish不复制runtime。不能据此判当前测试包坏。|
|161|部分成立/需限定|`tools/editor-release/build.py:150`|stage-from+trimmed-stage可混旧PCK和新DLL且无输入hash等价门禁；明确授权的候选复用不是自动错误，但通用构建安全缺口存在，未证明现包不兼容。|
|162|维护建议|`tools/editor-release/build.py:7`|未使用import清理建议，无运行故障；优先级低，不与190个bug计数混同。|
|163|部分成立/需限定|`tools/editor-release/build_template.py:41`|resolve()会越过POSIX venv symlink；此脚本构建Windows模板，本机venv不是该情况。跨平台工具健壮性。|
|164|确认（源码/探针）|`tools/analyze_asset_style.py:54`|RGBA直接convert RGB统计隐藏RGB，透明图调色分析可错；未操作原版资产。|
|165|部分成立/需限定|`tools/analyze_asset_style.py:153`|无匹配结果仍写count0/空palette，输出明确0不是真造有数据；是否应拒绝空分析属于CLI策略，不是普遍数据损坏。|
|166|误报/建议不成立|`schemas/chart-format-v2/validate.py:287`|冻结合同docs/chart-format-v2.md明确Hold尾必须显式judge:true，C# Validation.cs同样要求；Python这一检查正确，报告只看schema漏语义约束。|
|167|部分成立/需限定|`schemas/chart-format-v2/validate.py:336`|python -O删除assert属实；该脚本是仓库fixtures自检而非生产谱包validator，正常命令未-O。测试运行配置限制应明确。|
|168|确认（源码/探针）|`tools/editor-release/verify_stage.py:14`|TimeoutExpired前无法打印captured stdout/stderr，诊断丢失属实；超时仍失败且subprocess会终止子进程，不是错误通过。|
|169|确认（源码/探针）|`tools/fit_side_y.py:53`|ffmpeg -y复用帧目录不删旧尾帧，后续目录扫描会混入；研究测量脚本。|
|170|确认（源码/探针）|`tools/fit_side_y.py:14`|未知profile走realplay素材而后续profile==realplay分支走另一几何，可产生混合参数；应拒绝未知值。|
|171|确认（源码/探针）|`tools/fit_side_y.py:117`|最小二乘未验证rank/样本多样性，退化样本仍打印拟合值；只否定退化输入可靠性，不推翻已测真实样本结论。|
|172|部分成立/需限定|`tools/frida/probe_drop_speed.py:39`|installed在挂钩前置true，异常会阻止重试；仅第三方研究插桩脚本，静态检查，未运行/改造挂钩。|
|173|确认（源码/探针）|`tools/frida/probe_drop_speed.py:114`|float允许nan/inf和反向范围，生成JS/等待参数无验证；仅研究CLI，未执行插桩。|
|174|部分成立/需限定|`tools/frida/run_attach.py:15`|模糊进程名选择存在误目标可能；受控研究工具非产品模块，未连接设备/运行第三方程序。|
|175|部分成立/需限定|`tools/frida/run_attach.py:6`|路径依赖外层Dynamix作为CWD，community根运行会失败；这些历史研究脚本原本使用外层路径，不能称原工作流必坏，high明显过高。|
|176|确认（源码/探针）|`tools/fit_opening_x.py:61`|used写不读、新建track立即进入候选，同一帧可重复归入；轨迹数据算法缺口。|
|177|确认（源码/探针）|`tools/fit_opening_x.py:89`|最近时间argmin后才排已用，同刻多音符都找第一条被丢；测量算法缺口。|
|178|证据不足，保留|`tools/frida/scan_judge_settings.py:70`|固定sleep不等于完成，但scanSync的script.load可能已等脚本完成；回调投递时序依赖Frida运行版本，未实机确认30秒后仍未收到，不按实证缺陷处理。|
|179|确认（源码/探针）|`tools/frida/scan_judge_settings.py:41`|扫描/上下文read异常被catch空吞，结果缺覆盖率状态；研究报告不能把空结果当完扫。|
|180|确认（源码/探针）|`tools/frida/scan_judge_settings.py:65`|脚本error仅打印，Python正常exit；可产生失败扫描当成功的自动化状态。|
|181|维护建议|`tools/frida/scan_judge_settings.py:19`|首个JS赋值随后覆盖，无读点；确认死代码，非第二套实际扫描。|
|182|维护建议|`tools/frida/scan_judge_settings.py:72`|for循环只有未用base和恒None，真实dump在后面ctx_js；确认无效残留。|
|183|部分成立/需限定|`tools/frida/run_spawn.py:20`|spawn后设置失败无清理，第三方研究启动器生命周期缺口；未运行/改善授权绕过脚本。|
|184|部分成立/需限定|`tools/frida/run_spawn.py:24`|仅打印错误/无detach退出与ready确认，挂起状态可误报；研究脚本不纳入产品运行故障。|

### 185–218：资产、下载、发布门禁、录制

|编号|结论|源码锚点|核实依据与限制|
|---|---|---|---|
|185|确认（源码/探针）|`tools/grade-assets/build_runtime_results.py:72`|递归扫DEST所有PNG/SVG加入导出及provenance，不仅本次生成资产，stale文件可被背书。|
|186|确认（源码/探针）|`tools/grade-assets/build_runtime_results.py:80`|先保留outcomes来源条目再递归追加包含outcomes，重复路径/错误generator可出现；同185根因。|
|187|部分成立/需限定|`tools/grade-assets/compose_blade_fx.py:117`|固定msyh.ttc无跨平台支持属实，但资产管线原本Windows本地制作，不能以Linux不能跑判当前发行失效；缺少前置环境检查。|
|188|确认（源码/探针）|`tools/grade-assets/build_runtime_outcomes.py:98`|Pillow Image.open仅size/mode不解码，坏payload可通过复制/hash；生成输入检查不足。|
|189|确认（源码/探针）|`tools/grade-assets/build_runtime_outcomes.py:101`|逐张覆盖后才检查后续文件和stray，失败留下局部新资产+旧manifest；只是生成中间工作区，发布gate仍可拦hash不符。|
|190|确认（源码/探针）|`tools/grade-assets/prepare_preview.py:22`|preview icon先save再alpha assert，失败留不一致生成物；研究预览管线，非运行态修改。|
|191|确认（源码/探针）|`tools/grade-assets/package_grade_set.py:179`|ZIP全扫复用static/animation目录，manifest只当前grades；可包含未声明旧grade，生成器打包边界。|
|192|确认（源码/探针）|`tools/grade-assets/review_missing_grades.py:98`|缺snapshot跳过旧资产比对，但末尾声称audited；验证声明不实。|
|193|确认（源码/探针）|`tools/pack_chart.py:67`|cover/audio名可与chart/meta互相覆盖，无扩展名拒绝；临时目录实际运行已得到OK且chart内容被cover替换。|
|194|确认（源码/探针）|`tools/pack_chart.py:81`|live pack直接copy/metadata.write_text，无原子发布；中途失败可破坏已有legacy工具包，非Avalonia v2 writer。|
|195|确认（源码/探针）|`tools/pack_chart.py:50`|id作为任意Path拼接，绝对路径或..可逃出out；本地显式CLI输入，不等同远程漏洞。|
|196|证据不足，保留|`tools/grade-assets/render_surface_current.py:68`|branch端点固定、主路内点noise移动有数学依据；未跑Blender几何/成品目视，缝隙大小及成品可见性待验证，不推翻已验收资产。|
|197|维护建议|`tools/grade-assets/render_surface_current.py:110`|idle phase_frame未用、root局部未用属实；无运行影响。|
|198|确认（源码/探针）|`tools/grade-assets/render_grade_set.py:205`|frames字符串int转换未catch，坏参数traceback而非argparse错误；仍失败退出，不是坏frame被生成。|
|199|确认（源码/探针）|`tools/pardl.py:42`|mock返回200/错误Content-Range且长度匹配被接受，离线复现；未网络下载。|
|200|确认（源码/探针）|`tools/pardl.py:35`|同dest相同长度换URL直接复用旧分段，离线复现；无ETag/源manifest。|
|201|确认（源码/探针）|`tools/pardl.py:105`|最终out直接wb合并，失败先破坏旧文件；研究下载工具不是产品更新器。|
|202|确认（源码/探针）|`tools/pardl.py:64`|chunk0除0，负chunk无段后仍wb输出；workers非正延迟失败。CLI参数边界。|
|203|证据不足，保留|`tools/grade-assets/render_concepts.py:195`|单polygon裁剪凹轮廓可能多组件并接桥，静态依据成立；具体Blender n-gon/bevel错误未验证，不称当前评级坏，且是旧方向稿。|
|204|确认（源码/探针）|`tools/grade-assets/render_concepts.py:202`|提取原纯几何函数，不执行Blender；当前upper plate缩放内嵌5顶点在原多边形外，数学反例确认。旧方向稿非现行blade。|
|205|确认（源码/探针）|`tools/synth_hitsounds.py:73`|resonator从i2开始丢x0/x1，首样本单位脉冲输出0；滤波器数学缺口，现行noise输入影响程度另论。|
|206|部分成立/需限定|`tools/synth_hitsounds.py:95`|native int16大端WAV错误属实；当前Windows x64/ARM小端，不影响现交付样本。|
|207|旧路径/已停用功能|`tools/grade-assets/render_light_studies.py:125`|v6未采用方案；curve只parent root、blade另有group transform确有相对移动风险，非现行v5资产，不据此修改成品。|
|208|旧路径/已停用功能|`tools/grade-assets/render_light_studies.py:58`|v6未采用方案；Z不乘envelope，分支端与主路结点wave参数不同。非当前资产故障。|
|209|确认（源码/探针）|`tools/release/check_apk.py:725`|APK标签查固定资源值，不校验manifest引用/launcher override；完整identity门禁缺口，不代表当前APK已假标。|
|210|确认（源码/探针）|`tools/release/check_apk.py:428`|required feature仅子串匹配，缺project.binary时跳过；不是准确解析custom feature，门禁漏检。|
|211|确认（源码/探针）|`tools/release/check_apk.py:452`|deps JSON未验证object/string层级，AttributeError不在catch；坏包可令checker traceback而非结构化错误，仍非零退出。|
|212|确认（源码/探针）|`tools/release/check_apk.py:349`|ZIP成员整块read/hash且testzip无总预算，metadata预算在解压后；门禁资源消耗缺口，未制造zip bomb。|
|213|部分成立/需限定|`tools/track_notes.py:41`|脚本实为逐帧blob枚举，不完成跨帧跟踪/跨线检出；与注释目标不全符，但输出本就逐帧坐标，并未伪装已算命中。|
|214|确认（源码/探针）|`tools/verify_chart_equivalence.py:185`|desktop录制前release gate，ffmpeg初始化期间漏起始帧；图像A/B不能严格对齐。|
|215|确认（源码/探针）|`tools/verify_chart_equivalence.py:218`|忽略Godot returncode且不验实际frame count，partial AVI可被转换后报告请求帧数；验证可信度问题。|
|216|确认（源码/探针）|`tools/verify_chart_equivalence.py:264`|framemd5默认流选择并比完整文本，音频/时间元数据差异可令相同像素失败；须分清字节/像素/时间比较。|
|217|确认（源码/探针）|`tools/verify_chart_equivalence.py:310`|空trace返回0不写目标但count相等后换输入；可missing或复用stale trimmed文件，未验视频帧数亦属实。|
|218|部分成立/需限定|`tools/verify_chart_equivalence.py:49`|按image杀全部Godot确实有误杀风险；当前AGENTS明确受控采集前清残留，属操作政策而非擅自可改的bug。|

