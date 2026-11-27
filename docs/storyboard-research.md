# osu! Storyboard 调研：格式、播放器和作者工具

调研日期：2026-11-27。本文以 osu! Wiki、osu!lazer 公开源码、工具作者的文档和发布件为主。这里的“有此功能”指规范或静态实现有证据，未安装或运行这些工具，也没有做兼容性和性能实测。Dynamite UV 的取舍见[独立编辑器方案](storyboard-editor-plan.md)。

## 1. osu! Storyboard 在文件里是什么

Storyboard 是随音乐播放的舞台演出。osu! 将它描述为对象及对象的动作：声明一张图、指定它的层和原点，再说它在什么时段移动、变色或消失。文件叫“脚本”，但 `.osb` 本身不是供播放器执行的通用编程语言。Python、C# 等代码通常是作者在编辑阶段运行的生成工具，游戏读取它们输出的数据。

| 组成 | osu! 的做法 | 对 UV 的意义 |
| --- | --- | --- |
| 文件归属 | `[Events]` 可在 `.osu` 里，作用于单差分；独立 `.osb` 可跨曲包差分共用。声明顺序主要决定覆盖关系，不决定哪一刻出现。 | 应支持一份演出给多个差分，也要能选某一个差分。UV 不能往现行 strict v2 文件塞新字段。 |
| 图片与帧动画 | `Sprite` 一张图；`Animation` 通过文件名编号取帧，指定帧总数、每帧毫秒间隔、`LoopForever` 或 `LoopOnce`。后者停在末帧，直到对象生命周期结束。Wiki 以 PNG/JPEG 为常见对象素材。 | 借鉴轻量序列帧，也支持图集；让作者在界面看到缺帧、有效区间、末帧保持和开销。 |
| 坐标和原点 | 原生 Design 基准 640×480，向右、向下为正。中心是 `(320,240)`。原点有中央、边、角等锚点；宽屏通常保持 480 的逻辑高度、扩大中央可见范围，中心仍沿 640×480 的构图中心。 | 借鉴离屏入场和可变旋转原点。UV 直接使用固定 1920×1080；迁移时显式选择等比适配，不能靠拉伸让旧作品填满。 |
| 层 | 通常有 Background、Fail、Pass、Foreground、Overlay；常见文档的四层数字表不包含 Overlay，但总说明明确有 Overlay。它在打击物件之上，仍低于 skin/HUD；Pass、Fail 是互斥游戏状态，不是结算的成功或失败按钮。 | UV 先开放只在判定线、音符、HUD 背后的层。真实分支以后另定，不硬套 osu! 的血条状态。 |
| 属性动作 | `F` 透明度；`M/MX/MY` 位置；`S/V` 等比与 XY 独立缩放；`R` 旋转（文件用弧度）；`C` RGB（0–255）；`P` 设置区间内水平/垂直翻转、加色混合。Wiki 缓动表列出 0–34，含线性、Sine、Expo、Elastic、Bounce 等。 | 每条属性单独可控很有用。界面展示曲线形状、名称和单位；无须让作者记数字。UV 的显隐、叠层与值边界单独定义。 |
| 对象寿命 | 常规命令按毫秒指定起止，结束后通常保持值，直至对象整体退出；`P` 的区间状态不是普通持有值。静图也常用一段 constant Fade 维持寿命，没有独立的图层裁切条。 | 直接给对象和片段一个可拖动的存在区间；删某条 Scale 时不应意外让整张图提前消失。 |
| 循环和触发 | `L` 使某个对象的一组动作重复，内部时间相对每次循环。`T` 在有效时间窗口内等事件，子动作相对事件发生时刻；官方 Wiki 介绍 Passing、Failing、HitSound 匹配和可选组号。最新 lazer 源码还能见到 `HitObjectHit`。 | 借有限循环，但允许可视化重复多对象片段。区分“按谱面计划时间铺事件”和“读真实游玩结果触发”；首轮只做前者。 |
| 样本音效与视频 | `Sample` 是按时间和音量播放的独立声明，并非能随意写进 `L/T` 的普通命令。背景/视频是 Events 家族的另外条目。部分项目会用 `UseSkinSprites` 引用玩家 skin 的图。 | 首轮做视觉，保持现有歌曲/命中声。避免作品依赖玩家 skin；以后若加短音效或视频，先定义混音总线、授权、播放时钟和录制。 |
| 变量 | `[Variables]` 在 `.osb` 中作文本替换，不是可随游玩改变的变量。Wiki 还提醒编辑器回写可能把相同字面值替换为变量。 | 做带类型、单位和范围的模板参数，可精确重命名，不扫描全文替换。 |

来源：[概览](https://osu.ppy.sh/wiki/en/Storyboard/Scripting)、[一般规则：坐标、层、时长](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/General_Rules)、[对象](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Objects)、[动作和缓动](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Commands)、[循环与触发](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Compound_Commands)、[音频样本](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Audio)、[变量](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Variables)、[开关](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/osu!_File_Toggles)。

用自备的 `lamp.png` 举例：

```text
[Events]
Sprite,Background,Centre,"lamp.png",320,240
 F,0,1000,1500,0,1
 F,0,2000,2500,1,0
```

它在 1 秒开始淡入，1.5 秒达到全亮，中间保持，到 2.5 秒淡出并结束。若要每拍重复，文件里可以用 `L`；UV 的作者会在时间轴上把这一段框住，选“重复”。如果希望第二次亮度不同，UV 可选择第二个实例覆写参数或分离修改。区别在编辑操作，不在作者能不能读懂文件语法。

### 游玩中为何看起来像乘了 Alpha（lazer）

官方 [UserDimContainer](https://github.com/ppy/osu/blob/master/osu.Game/Graphics/Containers/UserDimContainer.cs#L93-L106) 将 `1 - DimLevel` 作为灰色调制色，压低背景／Storyboard 的 RGB；另用 Alpha 从 0 到 1 渐变来显示或隐藏容器。默认 [DimLevel 为 0.70，LightenDuringBreaks 开启](https://github.com/ppy/osu/blob/master/osu.Game/Configuration/OsuConfigManager.cs#L128-L151)，所以平时灰度系数目标是 0.30，休息时 dim 减 0.30、灰度系数目标是 0.60。切换以 800 ms `OutQuint` 过渡。这是调制色的数值，不是屏幕实测亮度比例。休息由 [BreakTracker](https://github.com/ppy/osu/blob/master/osu.Game/Screens/Play/BreakTracker.cs#L30-L67) 判定，有效休息段、游玩开始前和计分结束后可进入该状态；没有逐帧数屏幕上的 Note。普通 Storyboard 在玩法下方；[真正的 Overlay](https://github.com/ppy/osu/blob/master/osu.Game/Screens/Play/Player.cs#L440-L489) 在玩法上方、HUD 下方，[全暗时也保留](https://github.com/ppy/osu/blob/master/osu.Game/Screens/Play/DimmableStoryboard.cs#L29-L42)。这些结论来自 lazer 公开实现，未验证闭源 stable 的内部混合算法。UV 借鉴了玩家控制的 RGB 调光：见[演出亮度](storyboard-runtime.md#如何试用)。本轮不随 Note 或休息段自动切换，亮度独立于脚本的 Alpha、开关及游戏判定。

### 原生 Design 选项卡能做到多少

[官方 Design 说明](https://osu.ppy.sh/wiki/en/Client/Beatmap_editor/Design)以旧版原生工具为例：作者可以放图、切层、改基础变换；需要循环、触发、采样音效或单轴移动时，就得写 Storyboard。UV 可以保留这条分工，但把代码和画布放进同一个工作台。Design 的能力不能外推到 lazer 编辑器。

## 2. 完整功能速查：从声明到动作

下面按实际文件来读。想核对一种命令，先找对象声明，再看其缩进下的动作。示例用空格展示层级；旧格式也接受下划线。这里覆盖常规 storyboard 的声明、全部基础命令、复合命令及相关谱面开关，不把 osu! 的整个 `.osu` 制谱格式算进 Storyboard。

### 文件、坐标、图层和可用素材

- **文件如何叠加。** 单差分可在 `.osu` 的 `[Events]` 写本差分演出；共享 `.osb` 也有自己的 `[Events]`。同一层内，Wiki 描述共享 `.osb` 的声明相当于接在当前 `.osu` 声明之后，后声明的对象盖在先声明的对象上。这个顺序不会让共享 Background 跨到差分 Foreground 前面；它也不是把同名对象合并成一个。`//` 整行注释可读，但不保证经过旧编辑器保存还能保留。
- **设计尺寸。** 640×480 指演出坐标，不等于可打击区域大小；Wiki 的旧编辑图用约 510×385 的内部游玩示意。中心 `(320,240)`，Y 向下。宽屏版按 480 高等比扩展，16:9 的逻辑宽约 853⅓，若以中心对齐，约 `x=-106.67…746.67`；画面不是把 640 拉宽。图片可先放到画外再飞进来。
- **原点。** 格式有 `0 TopLeft`、`1 Centre`、`2 CentreLeft`、`3 TopRight`、`4 BottomCentre`、`5 TopCentre`、`6 Custom`（旧规则等同 TopLeft，不是真正自定义）、`7 CentreRight`、`8 BottomLeft`、`9 BottomRight`。移动坐标落在该原点，旋转与缩放也围绕它。
- **图层。** 从后到前：`0 Background`、`1 Fail`、`2 Pass`、`3 Foreground`、`4 Overlay`。较旧的 Wiki 对象表只列到 `3`，`4` 可对照[当前 lazer 枚举](https://github.com/ppy/osu/blob/master/osu.Game/Beatmaps/Legacy/LegacyStoryLayer.cs)。该枚举还包含 `5 Video`，lazer 模型把视频放到背景层后；不要把它当作旧 Wiki 的普通可选创作层。`Overlay` 高过打击物件、仍低于 HUD 等游戏自身元素。原曲背景一般在演出下方；lazer 会在 Background 层引用了同一文件时判定演出取代原背景。

| 类型 | 典型写法 | 规则 |
| --- | --- | --- |
| 静图 | `Sprite,Background,Centre,"sb/lamp.png",320,240` | 读相对曲包路径；PNG 可透明，JPEG 适合不透明图。声明提供默认位置，不直接写出现时间。 |
| 帧动画 | `Animation,Foreground,Centre,"sb/ring.png",320,240,12,40,LoopOnce` | 文件为 `ring0.png` 到 `ring11.png`；每帧 40 ms，总序列 480 ms。`LoopOnce` 最后一帧保持到对象退出，省略循环类型默认为 `LoopForever`。序列帧循环与变换命令 `L` 是两回事。不要拿一张动画 PNG 当编号图片。 |
| 音频样本 | `Sample,24000,3,"sb/click.wav",70` | 在指定媒体时刻播放；文档列 WAV/MP3/OGG、音量 1–100（缺省 100），多个样本会混合。文档列层 `0–3`，可指定只在 Pass 或 Fail 时播放。它不需要打中音符，也不是 `HitSound…` 触发器；不能放到普通 `L`/`T` 子命令里。 |
| 原曲背景 | `0,0,"bg.jpg",0,0` | `.osu [Events]` 的背景条目，不等同于 `Sprite`；偏移相对屏幕中心，零偏移可省。 |
| 视频 | `Video,1500,"scene.mp4",0,0` | `.osu [Events]` 另有这种背景媒体条目（`Video` 可写 `1`），不是 `Animation`，也不能当作一张 Sprite 套任意命令链。实际支持的编解码还取决于客户端。 |
| 休息区间 | `2,20000,24000` 或 `Break,20000,24000` | 谱面事件，不是图像或脚本触发器；旧 Pass/Fail 判定在休息段有不同规则。 |

### 全部基础动作与默认值

同一个对象可以同时动不同属性。例如用 `MX` 左右移动时，可另用 `F` 淡出。动作共用 `命令,缓动,开始毫秒,结束毫秒,参数…` 的字段结构；`L`、`T` 是例外。下表列出完整的基础动作族，不把比较老的 Cheat Sheet 前表当作完整列表。

| 命令 | 参数顺序（省略公用前缀） | 效果和默认值 |
| --- | --- | --- |
| `F` Fade | `a0,a1` | 透明度 0–1，默认 1；可在一段时间内维持同一透明度。 |
| `M` Move | `x0,y0,x1,y1` | 同时改绝对 X/Y；没有对应位置动作就用声明位置。 |
| `MX`、`MY` | `x0,x1` 或 `y0,y1` | 一条轴各自取帧；另一轴可以独立打点。lazer 解码 `M` 时本身就送入两条轴轨。 |
| `S` Scale | `s0,s1` | 等比缩放，`1` 是原尺寸；围绕对象原点。 |
| `V` Vector Scale | `sx0,sy0,sx1,sy1` | 两轴缩放，默认 `1,1`。例如 `2,0.5` 表示两倍宽、一半高；不把 `S`/`V` 重叠写法当作所有客户端都相同。 |
| `R` Rotate | `r0,r1` | 文件用弧度，默认 0，正值顺时针，允许超过一圈。比如 `0 → 2π` 是转一圈，不是两端角度相等就不转。 |
| `C` Colour | `r0,g0,b0,r1,g1,b1` | 0–255 的 RGB 调色；白色保留原色，黑色压黑，不改变透明度。这里不是给场景叠一层彩色滤镜。 |
| `P` Parameters | `H`、`V` 或 `A` | 水平翻转、垂直翻转、加色混合。不同于普通数值插值，有结束的普通区间只在区间内生效；lazer 对起止相等另有端值处理，导入需单列测试。 |

**缓动编号表：** `0` Linear；`1/2` 旧式 Out/In；`3–5` Quad、`6–8` Cubic、`9–11` Quart、`12–14` Quint、`15–17` Sine、`18–20` Expo、`21–23` Circ（这些组三项都是 In/Out/InOut）；`24–28` Elastic In、Out、Half Out、Quarter Out、InOut；`29–31` Back In/Out/InOut；`32–34` Bounce In/Out/InOut。UV 应给它们显示名称和曲线，不要求作者记编号；即使 UV 使用相同数学曲线，也不要默认颜色、角度和越界属性与旧引擎完全相同。

**时间边界与三个简写：** 常规命令开始后做插值，结束后属性通常保持端值；整个 Sprite 的存续由动作与循环等共同决定。旧 Cheat Sheet 还记有“某属性在首个动作之前可先取该动作的起值”，这与 UV 建议的显式基础值不同，不能一概按声明默认值迁移。对象寿命受多个命令和 Alpha 优化影响，`F` 到 2000 不代表另一个持续到 3000 的 `M` 也会在 2000 停止。简写为：

1. `F,0,1000,2000,0.5`：起值和终值相同，整段 0.5。
2. `F,0,1000,,0.5`：空结束时间字段表示与起点同刻；逗号不省略。
3. `F,0,1000,1500,0,1,0.5,0`：旧 Wiki 的连续等长写法，表示 1000→1500（0→1）、1500→2000（1→0.5）、2000→2500（0.5→0）。**规范和当前解码源码有差别**：本次所读 lazer 的 `F`/`M` 等分支只从固定字段取一对起止值，没看到为第 3 种额外值循环建立动作。因此 UV 导入若支持它，应明确展开并用目标客户端夹具复核，不能直接宣称所有播放器等价。

### 循环、触发和状态层

```text
Sprite,Background,Centre,"sb/lamp.png",320,240
 L,8000,4
  F,0,0,250,0,1
  F,0,250,500,1,0
Sprite,Foreground,Centre,"sb/ring.png",320,240
 T,HitSoundClap,10000,20000,1
  F,0,0,100,1,0
```

- **`L,start,count`**：从 8000 ms 起，把灯的两段动作合计演 4 次，每次 500 ms；内层时间以本轮起点为零。它归当前对象，不是跨物体一起循环的容器。最好让段长由清楚的首尾点界定；重叠和不从零开始的旧作品另测。帧动画的 `LoopForever` 不由这里控制。
- **`T,type,start,end[,group]`**：只有在窗口内出现相应事件，才从事件时刻开始走内层动作。`Passing`/`Failing` 是状态**转变**；`HitSound` 可筛主样本库与附加库（`All/Normal/Soft/Drum`）、`Whistle/Finish/Clap` 及自定义库编号，例如 `HitSoundAllSoft` 或 `HitSoundDrumClap0`。当前 lazer 另能订阅计分命中的 `HitObjectHit`；样本触发则订阅**实际播放的样本**，两者不能互换。旧 Wiki 对抢占、普通动作冲突和组号有描述，当前控制器在窗口内投递延时变换；不要把旧行为和新行为无条件合并。首版 UV 可做计划锚点；实际事件要另定义可复现的事件记录、同刻顺序、冷却和抢占。
- **Pass/Fail 层跟事件不是一回事。** 层是“现在该显示哪一套画面”；`Passing/Failing` 是切换时启动一次动作。按旧 Wiki 的普通玩法描述：首个可玩段之前总是 Pass；游玩中参考先前连击结尾，休息段参考上一段完结时血量有没有过半，末尾还可能看各休息段中 Pass 占比。旧 taiko/catch 另有规则。这些是历史说明，不当作当前稳定版逐模式实测。当前 lazer 的可绘制路径以血量 `>=0.5` 切换，源码明确说明不等效。UV 只有一部分模式有类似血量，不能把这个阈值写入原生合同。

### 时间、开关、编辑器和规范没覆盖的事

- **媒体时钟**：文档以主音轨开头为 0，毫秒绝对时间不随 BPM/拍号变化；负时间可用来做序曲，尾部也可超音轨。历史文档介绍提前跳过、尾部与结果页等待，部分要看两套 Pass/Fail 层的最长动作；lazer 有自己的事件结束求值。UV 不接受附件改变结算，第一版只在已解析音轨范围内播。
- **变量和配置**：`.osb [Variables]` 的 `$name=value` 是文本替换，不是运行变量，也不是安全的宏语言；旧工具回写可能把同字面值替换成变量。`.osu [General]` 相关项有 `UseSkinSprites`、`WidescreenStoryboard`、`EpilepsyWarning`、`LetterboxInBreaks`；`StoryFireInFront`/`AlwaysShowPlayfield` 在 Wiki 标为废弃，`OverlayPosition` 管打击圈贴图与数字，并非 Storyboard 图层顺序。`EpilepsyWarning` 是警告标记，不代表作者检测过内容。旧文件可有编号事件和缩进简写，UV 新格式应有显式字段和清晰报错。
- **原生 Design 有哪些工具**：素材库和对象列表、层切换与手动排序、前后锚点、Live 画面、播放速度、时间读数和 SB Load。基本变换之外，[Design 文档](https://osu.ppy.sh/wiki/en/Client/Beatmap_editor/Design)还描述向量缩放、翻转选项、Tweening 开关、缓入/出、原点和差分保存。页面同时把完整 `P`、循环、触发、音效、独立 X/Y 和片尾列入脚本或限制范围；有简单翻转按钮不等于完整的区间 `P` 都能在界面编辑。
- **它不是通用 2D 场景格式**：这套原生命令没有可编辑父子层级、运行时文字排版、任意路径节点、任意 shader、程序粒子或音频频带轨。osu! 作品里看得到这些效果，常见办法是生成很多精灵/变换，或把文字、遮罩、复杂画面预先处理成素材；不能把生成器能做的工作误列为游戏原生指令。

本节补充来源：[`.osu` 文件与 Events](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29)、[命令速查](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Cheat_Sheet)、[简写](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Shorthand)、[历史时间与图层规则](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/General_Rules)、[lazer 解码器](https://github.com/ppy/osu/blob/master/osu.Game/Beatmaps/Formats/LegacyStoryboardDecoder.cs)、[lazer 触发器](https://github.com/ppy/osu/blob/master/osu.Game/Storyboards/Drawables/StoryboardTriggerController.cs)。以上实例为说明用途，没有运行 stable/lazer 做兼容实测。

## 3. lazer 底下怎么播，不同于哪些旧行为

这是对公开源码的静态阅读，不把旧 stable 的每个细节和当前 lazer 可执行版本视为一回事。

1. [`LegacyStoryboardDecoder`](https://github.com/ppy/osu/blob/master/osu.Game/Beatmaps/Formats/LegacyStoryboardDecoder.cs) 读取 General、Events 与变量，先做文本替换，数开头空格/下划线确定深度，再按事件类型建立对象或动作组。图片和帧动画带资源路径、锚点与初始位置；`M` 写 X/Y 两条动作，`R` 在解码时将弧度转角度，`C` 把 255 范围转换为颜色分量。普通命令接受省略结束值；可见代码还为很旧的动画按文件版本单独调整帧间隔。输入文件、规范和运行对象分开，便于兼容怪例。
2. [`Storyboard` 模型](https://github.com/ppy/osu/blob/master/osu.Game/Storyboards/Storyboard.cs) 建立 Video、Background、Fail、Pass、Foreground、Overlay 等层，资源交给谱包存储映射。`ReplacesBackground` 会检查 Background 层是否引用原曲背景。`EarliestEventTime`/`LatestEventTime` 用于演出时段估计，源代码提醒不要每帧遍历所有元素。
3. [`StoryboardSprite`](https://github.com/ppy/osu/blob/master/osu.Game/Storyboards/StoryboardSprite.cs) 区分普通动作、循环组、触发组，并计算显示寿命；可利用第一个有效 Alpha 区间跳过一段不可见时间。[可绘制精灵](https://github.com/ppy/osu/blob/master/osu.Game/Storyboards/Drawables/DrawableStoryboardSprite.cs) 从本地贴图存储加载图片；启用 `UseSkinSprites` 时优先查玩家 skin，再回退到本地贴图。随后给精灵挂载动作。为了兼容旧作品，代码还保留 `Alpha > 1` 时产生闪烁的历史行为。UV 应拒绝越界透明度或按明确区间约束，绝不借这条兼容规则当特效。
4. [`DrawableStoryboard`](https://github.com/ppy/osu/blob/master/osu.Game/Storyboards/Drawables/DrawableStoryboard.cs) 接可选的游戏时钟、贴图存储及逐层容器；缩放按父容器高度对齐逻辑高度，宽屏与普通内容宽度不同。[图层容器](https://github.com/ppy/osu/blob/master/osu.Game/Storyboards/Drawables/DrawableStoryboardLayer.cs) 使用生命周期管理类，不必每帧对全部对象做同样的处理。lazer 选择保留已完成变换以支持时钟相关行为；UV 可用当前时刻从有序属性轨直接求值，不需要复刻具体的 drawable transform 或旧异常。
5. [`StoryboardTriggerController`](https://github.com/ppy/osu/blob/master/osu.Game/Storyboards/Drawables/StoryboardTriggerController.cs) 订阅 Passing、判定结果和实际播出的样本，在触发窗口内匹配事件并开始动作。`DrawableStoryboard` 当前源码用血量 `>=0.5` 得到 passing，代码注释明确指出与 stable 行为不同；[Pass/Fail 层 PR](https://github.com/ppy/osu/pull/28480)、[状态及命中触发 PR](https://github.com/ppy/osu/pull/38335)、[样本触发 PR](https://github.com/ppy/osu/pull/38387)也记录了历史差异，例如样本库属性与哪些动作触发的区别。这是 UV 暂缓实战事件分支的理由：有事件历史之后，跳转、回放、冷却和录制不能只计算一根普通曲线。

**对 UV 最有价值的是职责拆分**：读入先校验；数据模型决定对象、时间和层；渲染只处理当前时刻需要的对象；实战事件另从游戏接线。没必要为了“像 osu!”引入旧语法的缩进兼容、依赖玩家 skin、隐式时长或超范围闪烁。

## 4. osbpy：为什么少量代码能生成大量内容

核对对象是 **Jiri Olszar / Wafu 的 `osbpy` 3.0.1**，2018-12-24 在 PyPI 上传，元数据标注 MIT。2017 年社区指南中的单文件 `osbpy.py` 链到同一 Wafu 仓库，是早期分发形态。该仓库的公开入口本次无法取得，不用同名代码补猜 API。我们另外只读检查了 [PyPI 元数据列出的 wheel](https://pypi.org/pypi/osbpy/3.0.1/json)，归档 SHA-256 与 PyPI 数据匹配，未安装或执行。

| 从发布代码看到的入口 | 它解决的问题 | 换成 UV 界面 |
| --- | --- | --- |
| `Osbject(path, layer, origin, posx, posy, frame_count=None, frame_rate=None, loop=None)` | 用相同素材、不同位置或层反复创建对象。命名易被其他工具的 `OsbSprite` 混淆。 | 多选、网格/圆环/路径重复器、局部原点和叠层选择。 |
| `fade/move/movex/movey/scale/vecscale/rotate/colour/para` | 以同一套属性方法批量生成效果。 | 属性轨加批量编辑；模板实例可以替换输入。 |
| `loop/trigger` | 为动作提供重复或条件上下文。PyPI 项目作者同时注明不支持独立的 storyboard hitsounds；这和支持输出触发声明并不矛盾。 | 有限重复片段先做；以 Note 计划锚点批量排布不依赖玩家命中。 |
| `spectrum` | 发布代码从 WAV 取第一个声道并生成频谱柱对象。没看到它支持任意音频格式。 | UV 可以把指定频带写成可见的曲线。作者选区间、平滑或去掉低值，再把曲线接到高度或颜色，不必限定成柱状图。 |
| `end/end_minimize` | 写目标文件，`end()` 可直接覆盖；`end_minimize()` 使用 `[Variables]` 缩减文本。 | 工程和发布文件分离；导出前显示合并、去重和出包预算，不覆盖原谱目录。 |

生成工具擅长的是“写出一条规则，用不同输入执行 100 次”。UV 若只有手工关键帧，同样的事得点 100 次；若只给几个效果预设，又无法满足定制。更好的借鉴是把重复的规则拆成作者能配置的参数、可定位的输出和可分离的实例。来源：[PyPI 项目](https://pypi.org/project/osbpy/)、[3.0.1 可下载清单](https://pypi.org/project/osbpy/3.0.1/#files)、[历史安装指南](https://osb.moe/learn/docs/storyboarding/osbpy/installation/#adding-osbpy-to-your-projects)。

## 5. storybrew 和可视工具各自长处

### storybrew：脚本提供能力，面板让作品可调

[storybrew](https://github.com/Damnae/storybrew) 是 Damnae 的独立工具，项目代码许可证为 [MIT](https://github.com/Damnae/storybrew/blob/master/LICENSE)，可核对到 [1.98 发布记录](https://github.com/Damnae/storybrew/releases/tag/1.98)。作者资料描述 C# 脚本和素材修改后的即时预览，并提供[不编程入门](https://github.com/Damnae/storybrew/wiki/Getting-Started-%28Without-Programming%29)：使用者可选择已有脚本、调整效果实例的公开参数、排序和导出；新增此前未实现的生成行为仍走代码。[界面教程](https://osb.moe/learn/docs/storyboarding/storybrew/interface/)还区分了作者工程与目标谱集目录、工具内图层顺序与导出层；可以切换预览差分，再选共享或差分导出。这里的“无代码”主要指套用与调参数。

源码有明确的分工：[生成器 API](https://github.com/Damnae/storybrew/blob/master/common/Scripting/StoryboardObjectGenerator.cs) 提供曲目与素材等上下文；[ScriptedEffect](https://github.com/Damnae/storybrew/blob/master/editor/Storyboarding/ScriptedEffect.cs) 把代码和效果实例关联；[EffectConfigUi](https://github.com/Damnae/storybrew/blob/master/editor/UserInterface/Components/EffectConfigUi.cs) 管可配置参数；[TimelineSlider](https://github.com/Damnae/storybrew/blob/master/editor/UserInterface/TimelineSlider.cs) 有节拍格、书签、A/B 区间、效果/层时段高亮及可选打击物件显示。这是很强的调参预览，并非已经证实的全对象拖拽关键帧编辑。项目代码中还可以看到 [Particles](https://github.com/Damnae/storybrew/blob/master/scripts/Particles.cs)、[Spectrum](https://github.com/Damnae/storybrew/blob/master/scripts/Spectrum.cs)、[RadialSpectrum](https://github.com/Damnae/storybrew/blob/master/scripts/RadialSpectrum.cs)、[Lyrics](https://github.com/Damnae/storybrew/blob/master/scripts/Lyrics.cs)、[Karaoke](https://github.com/Damnae/storybrew/blob/master/scripts/Karaoke.cs) 和 [ImportOsb](https://github.com/Damnae/storybrew/blob/master/scripts/ImportOsb.cs) 等例子。

- **建议照顾的工作习惯**：选择区间后循环看，修改一个参数立即重算该效果；快速独显或隐藏，找出哪一组在遮挡；保存发生在作者工程，导出才写游戏数据。
- **适合 UV 超过这一步的地方**：作者自己把几层变成模板，勾选参数即可复用；还能打开生成结果、分离单项、在普通关键帧里改完。正式播放器不装 C# 脚本编译器。
- **可从脚本示例反推的需求**：粒子要控制数量、范围和种子；字幕要有行/词的时间；频谱适合离线生成曲线；用于装饰打点的效果应能读取计划音符的位置和时间。脚本存在是需求证据，不是把其实现或素材复制进 UV 的理由。

### 其他项目：拿来对比什么

| 工具 | 可核实的能力和局限 | 对 UV 的参考 |
| --- | --- | --- |
| [KuanEditor](https://github.com/Sorihiko/KuanEditor/blob/main/README.md) | 作者展示近似剪辑软件的时间线，将歌词、图片、粒子/效果用拖放安排，可即时预览和导出 osu! Storyboard；[v0.9.0](https://github.com/Sorihiko/KuanEditor/releases/tag/v0.9.0) 有 Windows x64 便携版/安装包，[代码许可](https://github.com/Sorihiko/KuanEditor/blob/main/LICENSE)为 MIT。[片段源码](https://github.com/Sorihiko/KuanEditor/blob/main/src/config/clips.js)能见到图片关键帧、频谱、转场等入口；[导入源码](https://github.com/Sorihiko/KuanEditor/blob/main/src/engine/osb-import.js)把 `.osb` 按块预览和移动，并未恢复原工程。本次未运行。 | 比较从素材落到时间线的步骤；导入外部片段时明确区分“整体可移动”和“内部每个对象都能编辑”。 |
| [Osu! Storyboard Studio](https://github.com/wanjiaXG/Osu-Storyboard-Studio/blob/main/README.md) | 作者说明 Windows Beta，有时间线/预览/属性编辑、对象与命令关键帧、撤销重做、嵌套组、自定义缓动和视频导出。未核实完整发行包，也没有将 README 的版权表述当成代码复用许可。 | 界面分为预览、时间轴和属性面板是合理的，重点核对复杂效果能否拆开找源头，而不是照抄界面。 |
| [sbtw!](https://github.com/LeNitrous/sbtw/blob/develop/README.md) | 作者以 lazer 为基础、受 storybrew 启发，支持 JS/TS 写生成器；作者提醒工程结构和脚本语法不稳定。代码 MIT 的声明不覆盖引用的 osu!resources。 | 验证“创作工程 + 预览 + 生成数据”的方向，不把换成 JS 当作无代码。 |
| [osbe / SGL](https://github.com/halbekanne/sgl-Editor) | [原始 README](https://cdn.jsdelivr.net/gh/halbekanne/sgl-Editor@master/README)明确是 `.osb` 编辑和生成器；旧 Wiki 链本次不可用。源码许可可看原项目，不能由名字推断兼容性、GUI 细节或维护状态。 | 作为历史上的批量生成路线，优先借鉴规则复用，别把一门 DSL 变成用户门槛。 |

通用动画软件提供了一个有用的语言：例如 [After Effects 的预合成](https://helpx.adobe.com/after-effects/desktop/work-with-compositions/precomposing-and-nesting/precomposing-nesting-pre-rendering.html)将多层动画编成可复用的一组，[Blender NLA 片段](https://docs.blender.org/manual/en/latest/editors/nla/strips.html)是把动作放到时间线上使用的另一种参照。UV 值得学的是“修改素材定义”和“调整本次使用”的区别，不必在第一版复制影视软件的整套合成系统。

## 6. UV 逐项取舍

这张表对照整体方向，含作者工具阶段的能力，不是首轮指令清单。首轮已把 `clip` 和有限 `repeat` 纳入播放样片；可重用模板、乒乓和编辑功能仍分别评估。当前首轮范围和边界见[脚本与画面规格](storyboard-script-spec.md)。

| osu! 能力或创作习惯 | UV 怎么做 | 建议优先级 |
| --- | --- | --- |
| `Sprite`、`Animation` 和原点 | 保留，新增图集、真正可拖的枢轴、素材身份和显式起止。 | 首个作者版 |
| `F/M/MX/MY/S/V/R/C/P` | 保留对应能力：透明度、两轴移动、等比/独立缩放、角度和圈数、染色、翻转、Normal/受控 Additive。编辑器和 SDK 使用有意义的名字；同一属性冲突有显式规则。 | 首个作者版，原型先做位置和透明度 |
| 数字缓动和特殊简写 | 保留常用曲线语义，增加可视曲线和有限贝塞尔；发布文本使用命名和明确单位，不复制简写歧义。 | 首个作者版，曲线库逐步加 |
| 单对象 `L`、帧序列循环 | 保留有限循环；增加多对象片段、相位错开、乒乓、句内重复和可选“分离本次”。序列帧播放仍与动作循环分开。 | 首个作者版 |
| 毫秒与负时刻／尾段 | 保留媒体时间，另加 UV 精确拍位；首轮兼顾混合输入但不混用单片段单位。负时间和超时长只在草稿提示；片头、片尾和跳过规则另定。 | 两种时间进作者版；超出歌曲后续 |
| 同曲共享及每差分独立 | 保留，改成显式绑定表，可派生同一模板但有独立参数。不借 osu! 文件顺序做隐式覆盖。 | 首个可玩版 |
| Background、Foreground、Overlay | 保留分层和稳定排序；首轮作者的所有层仍在判定线与 Note 后。将来只在可证明读谱安全时加受控前景，不允许盖 HUD。 | 首个可玩版后景；Overlay 后续 |
| Pass/Fail 与 `T` | 不移植旧血量、连击和样本库规则。先加段落与计划音符锚点；需要实战反馈时另加只读、限额、有回放语义的事件。 | 计划锚点先做；实战触发后续 |
| `Sample`、`Video`、原背景替换 | 先保留 UV 原背景作为 Off 与失败兜底，不引入任意视频；短装饰音效以后通过独立总线评估，不能混淆命中声。 | 素材与视频能力后续 |
| 文本变量与 `UseSkinSprites` | 用强类型表单和素材 ID 替代，作品不依赖玩家装的 skin；不做全文件文本替换。 | 首个作者版 |
| storybrew/osbpy 的程序生成 | 保留作者阶段编程，在 UV 同一个窗口写 C# 或最终也可用 C++；两个 SDK 只产出同种可验证动作，游戏不加载源码或库。 | 先通 C#，C++ 隔离路线验证后接入 |
| 通用视觉扩展 | **新增**父子对象、场景拖动、路径、代码暴露参数、对生成节点的可追踪覆写、三轨对照、Full/Reduced/Off。频谱、文字、几何与粒子可由代码离线生成，常用的再做内置效果。 | 先把编程和场景编辑协作做好，不先做满面板 |

**兼容建议**：初版不以 `.osb` 互通卡原生工具。以后做只读导入时，优先 Sprite、Animation、基础属性、常用缓动、变量和有限循环。导入报告列出原样、近似和跳过；触发、样本、视频、skin、Overlay 或超出演出窗口的内容不应静默丢弃。支持原样播放一个外部片段、能逐对象编辑这个片段、能恢复原本的 C#/Python 源码，是三件不同的事。最后一件不承诺。
