# Dynamite Universe Chart Format v2

> 状态：正式目标格式，版本 `2`，规则集 `dynamite-uv-ruleset-2.0`。
>
> 发布日期：2026-08-14。
>
> 当前 Godot 客户端已接 strict v2 目录包加载、validator、legacy→v2 适配与 Gameplay Digest 成绩身份；legacy 仍是兼容输入。
> 原生 DynaMaker UV 已接包打开与基础编辑，保存/导出等尚未接入；当前能力见 [制谱器说明](<dyna-maker-uv.md>)，不能把本文最低要求当成 UI 完成证明。
> **实现偏差提醒（2026-10-07 静态核对）**：现有 EX-Tap 倍率为 1.0 且采用二值判定，而冻结 §12.1 保留 1.5 边界。文档整理未修改该冻结条款；需单独决定规则集/兼容策略，不能据“已接入”宣布全项符合。
> “正式”表示本文的数据合同与玩法语义已经冻结，客户端和工具必须按此 fail closed。

本文是 Dynamite Universe 社区谱面包 v2 的权威规范。机器可读约束见
`../schemas/chart-format-v2/`，clean-room 完整示例见
`../schemas/chart-format-v2/examples/golden-pack/`。

本文中的“必须”“不得”表示符合性要求；“应”“不应”表示只有在充分说明理由时才可偏离；
“可以”表示可选行为。格式标识一经发布不随应用显示名称变化：

```json
"format": "dynamite-uv-pack"
"format": "dynamite-uv-chart"
"formatVersion": 2
```

## 1. 范围与符合性

v2 规定：

- 谱面包目录、包元数据和单难度谱面结构；
- 精确 BarTime、BPM、视觉流速和三轨空间坐标；
- 普通 Note、Hold、Mixer 及共用路径曲线；
- 判定单元、计分口径和 Gameplay Digest v1；
- loader、validator、播放器和制谱器对正式文件的最低要求。

v2 不规定：

- 制谱器工程文件、撤销栈和编辑历史的持久化形式；
- 视口、选择、网格、图层锁定等编辑器状态；
- 社区服务器协议、账号、排行榜或谱面包分发协议；
- UI 视觉样式和具体音频编解码器清单。

符合 v2 的谱面包不得依赖任何原版谱面、音频或素材。示例包全部由项目自行合成，仅用于
格式测试。

符合性分为三层：

1. **结构符合**：JSON 能通过对应 Draft 2020-12 Schema。
2. **语义符合**：通过本文 §15 中跨字段、跨文件和数值语义校验。
3. **可游玩**：消费者支持包使用的音频、图片格式，并能实现本文规定的规则集。

只通过 JSON Schema 不等于语义符合或可游玩。

## 2. JSON、公用类型与路径

### 2.1 JSON 文本

`meta.json` 和 chart 文件必须：

- 使用 UTF-8；写出器不得添加 BOM；
- 是单个 JSON object；
- 不含重复属性名；
- 字符串不含未配对 UTF-16 surrogate；
- 数值是有限 JSON number，不得使用 `NaN`、`Infinity` 或其字符串替代；
- 不依赖属性顺序、缩进或换行。

消费者可以接受带 BOM 的已有文件并给出 warning，但规范写出器必须移除 BOM。未知字段
默认是错误，扩展必须等到新的格式版本明确命名，不能使用私有字段污染正式 chart。

### 2.2 ID

包、chart、父 Note 和路径节点都使用稳定 ID。v2 ID：

- 由 1–64 个 ASCII 字符组成；
- 首字符必须是 ASCII 字母或数字；
- 后续只允许 ASCII 字母、数字、`.`、`_`、`-`；
- 区分大小写，但生产工具应使用小写；
- 不表示播放顺序，也不得因标题、难度显示名或数组重排而自动改变。

`meta.json.id` 是社区中的稳定 `packId`。`charts[].id` 是包内稳定 `chartId`，必须与其
chart 文件的 `chartId` 一致。父 Note 和路径节点 ID 在整个 chart 内共享同一个唯一性空间。

### 2.3 包内相对路径

`audio`、`cover` 和 `file` 必须是普通文件的包内相对路径：

- 使用 `/` 分隔，不得使用 `\`；
- 不得为空，不得以 `/` 开头或结尾；
- 不得含 URI scheme、盘符、NUL、`.`、`..` 或空路径段；
- 解压前后解析出的最终路径必须仍位于目标包目录内；
- 完整路径长度为 1–1024 个 Unicode code points；
- archive symlink、hard link、reparse point 和其他可逃逸包根的条目必须拒绝；
- 在目标文件系统的大小写规则下发生碰撞的条目必须拒绝。

### 2.4 显示文本

`title`、`artist` 和每个 `charters[]` 元素必须：

- 是 Unicode NFC；
- 长度为 1–256 个 Unicode code points；
- 不含 C0 控制字符或 DEL；
- 不以 Unicode whitespace 开头或结尾。

这些字段只用于显示，不参与 Gameplay Digest。

### 2.5 精确 BarTime

BarTime 始终写成规范化对象：

```json
{
  "bar": 12,
  "numerator": 1,
  "denominator": 5
}
```

其精确值为 `12 + 1/5 bar`。规则：

- `bar`、`numerator` 是非负整数，`denominator` 是正整数；
- `0 <= numerator < denominator`；
- `numerator/denominator` 必须约分；
- 整 Bar 必须写成 `{ "bar": N, "numerator": 0, "denominator": 1 }`；
- 三个整数不得大于 JSON 安全整数 `9007199254740991`；
- 排序、判等、区间判断、Mixer 步进和主判定生成必须使用精确有理运算；
- 只有换算音频秒和最终渲染时才转为浮点。

实现应使用任意精度整数交叉相乘，不能先把 BarTime 转为 `double` 再排序。实现可以对恶意
输入设置总 Note 数、文件大小和运算资源上限，但不得把分母限制为常见音乐分割值。制谱器
可以接受 `12+1/5`、`61/5` 等输入，正式导出时必须写成上述对象。

本文用 `BT(x)` 表示 BarTime 对象对应的精确有理 bar 值。

## 3. 谱面包与 `meta.json`

一个包至少包含 `meta.json`、一个 chart 文件和所有 chart 可解析到的音频。例如：

```text
meta.json
chart_hard.json
chart_custom.json
audio.wav
cover.png
```

### 3.1 包字段

```json
{
  "format": "dynamite-uv-pack",
  "formatVersion": 2,
  "id": "example.synthetic-pulse",
  "revision": 1,
  "title": "Synthetic Pulse",
  "artist": "Dynamite Universe Contributors",
  "audio": "audio.wav",
  "preview": {
    "startSec": 0.5,
    "durationSec": 4.0
  },
  "charts": []
}
```

| 字段 | 类型 | 必需 | 语义 |
| --- | --- | --- | --- |
| `format` | string | 是 | 固定为 `dynamite-uv-pack` |
| `formatVersion` | integer | 是 | 固定为 `2` |
| `id` | ID | 是 | 永久稳定的 `packId` |
| `revision` | integer | 是 | 内容修订号，最小为 1；发布新修订时递增 |
| `title` | string | 是 | 曲名 |
| `artist` | string | 是 | 曲师/艺术家显示文本 |
| `audio` | path | 条件必需 | 包级默认音频 |
| `cover` | path | 否 | 包内曲绘；省略时消费者使用 clean-room 占位图 |
| `preview` | object | 否 | 包级默认试听区间 |
| `charts` | array | 是 | 一个或多个难度条目 |

`preview.startSec` 必须 `>= 0`，`preview.durationSec` 必须 `> 0`，单位均为音频文件秒。
`revision` 是分发和缓存元数据，不参与 Gameplay Digest。

### 3.2 难度条目

```json
{
  "id": "triple-star",
  "difficulty": "custom",
  "difficultyKey": "***",
  "level": 15,
  "charters": ["Example Charter"],
  "file": "chart_custom.json"
}
```

每个条目必须有：

| 字段 | 类型 | 语义 |
| --- | --- | --- |
| `id` | ID | 包内稳定且唯一的 `chartId` |
| `difficulty` | enum | 标准难度或 `custom` |
| `charters` | non-empty string[] | 一个或多个谱师显示名 |
| `file` | path | 对应的唯一 chart 文件 |

标准难度为：

```text
casual  normal  hard  mega  giga  tech
```

社区包不得使用 `tutorial`。标准难度不得携带 `difficultyKey`。

自定义难度必须使用 `difficulty: "custom"` 并提供 `difficultyKey`。`difficultyKey`：

- 是显示文本，不是稳定 ID；
- 必须是 Unicode NFC；
- 不得含控制字符、首尾空白；
- 长度为 1–24 个 Unicode code points；
- 同一包内按 `NFC + Unicode default case-fold` 后唯一。

例如上面的难度显示为 `*** 15`，不得自动加成 `TECH *** 15`。

定级必须二选一：

```json
{ "level": 15 }
```

或：

```json
{ "unrated": true }
```

`level` 必须是 `1..99` 的整数。`unrated` 存在时只能为 `true`，且必须省略 `level`。

`charts[]` 的数组顺序就是推荐显示顺序。条目 `id` 必须唯一，不同条目不得引用同一个
`file`；可以重复使用标准 `difficulty`，也可以共享音频。

### 3.3 判定预设

难度条目解析为固定 JudgePreset：

| `difficulty` | JudgePreset |
| --- | --- |
| `casual` | `casual` |
| `normal` | `normal` |
| `hard` / `mega` / `giga` / `tech` / `custom` | `hard` |

谱面文件不得自行覆盖 JudgePreset。预设属于 Gameplay Digest。

### 3.4 音频和试听解析

对每个难度条目：

```text
resolvedAudio   = chartEntry.audio   ?? pack.audio   ?? error
resolvedPreview = chartEntry.preview ?? pack.preview ?? clientDefault
```

若包级 `audio` 省略，每个难度条目都必须提供 `audio`。v2.0 不支持 `playbackRange`、音频
起止裁剪或循环区间；`resolvedAudio` 指向的包内文件就是从文件 0 秒开始播放的最终音频。
不同难度可以使用不同最终音频，并通过各自 chart 的 `audioOffsetSec` 校准。

试听区间若存在，必须满足：

```text
startSec >= 0
durationSec > 0
startSec + durationSec <= resolved audio duration
```

消费者默认试听行为不属于 chart 玩法语义，也不参与 Gameplay Digest。

## 4. 单难度 chart

```json
{
  "format": "dynamite-uv-chart",
  "formatVersion": 2,
  "chartId": "hard",
  "audioOffsetSec": 0.25,
  "timing": {
    "bpms": [
      {
        "time": { "bar": 0, "numerator": 0, "denominator": 1 },
        "bpm": 150.0
      }
    ]
  },
  "scrollSpeeds": [],
  "notesLeft": [],
  "notesCenter": [],
  "notesRight": []
}
```

| 字段 | 类型 | 必需 | 语义 |
| --- | --- | --- | --- |
| `format` | string | 是 | 固定为 `dynamite-uv-chart` |
| `formatVersion` | integer | 是 | 固定为 `2` |
| `chartId` | ID | 是 | 必须等于引用条目的 `id` |
| `audioOffsetSec` | number | 是 | BarTime 0 对应 resolved audio 的秒数，可为负 |
| `timing.bpms` | non-empty array | 是 | BPM 时间线 |
| `scrollSpeeds` | array | 否 | 视觉流速；省略或空数组时恒为 1 |
| `notesLeft` | array | 是 | 左侧轨 Note |
| `notesCenter` | array | 是 | 中央轨 Note |
| `notesRight` | array | 是 | 右侧轨 Note |

Note 和路径节点不保存 `track`；所属轨由数组确定。三个数组必须按精确 `time` 升序写出；
完全同刻时应按 `id` 升序以产生稳定 diff，但播放器不得让文件数组顺序改变判定语义。

## 5. BPM 与音频秒

每条 BPM 事件为：

```json
{
  "time": { "bar": 2, "numerator": 1, "denominator": 5 },
  "bpm": 180.0
}
```

规则：

- `bpm` 必须是有限数且 `> 0`；
- 第一条事件必须位于 BarTime 0；
- 时间必须严格递增，不允许同刻事件；
- BPM 在事件时刻瞬时变化，不插值；
- 不保存每段起始秒，消费者从前段累计。

对包含 `t` 的 BPM 段，设段起点 BarTime 为 `b`、累计音频秒为 `s`、BPM 为 `q`：

```text
second(t) = s + (BT(t) - BT(b)) * 240 / q
```

第一段 `s = audioOffsetSec`。计算到某一事件时使用前一段 BPM，事件左右的秒映射连续。

所有主判定单元换算出的音频秒必须 `>= 0`。最后一个主判定不得晚于 resolved audio 的
可解码时长加 `0.050s` codec tolerance。正常结算不得早于所有主判定完成。`barLine` 不属于
主判定，不延后正常结算。

v2 不提供拍号事件。复杂拍子通过 BPM 换算与任意 BarTime 分母表达。例如原曲为 5/4、
四分音符 BPM 为 `Q`：

```text
chartBpm = Q * 4/5
oneQuarterNote = 1/5 chart bar
```

## 6. 严格正向视觉流速

Scroll 事件为：

```json
{
  "time": { "bar": 1, "numerator": 1, "denominator": 3 },
  "value": 0.75,
  "curveToNext": "hold"
}
```

规则：

- `value` 必须有限且 `0 < value <= 64`；0 和负值非法；
- 省略或空数组等价于从 BarTime 0 起恒为 `1`；
- 非空数组的第一项必须在 BarTime 0；
- 事件时间严格递增；
- 非尾事件省略 `curveToNext` 等价于 `linear`；
- 尾事件不得携带 `curveToNext`，其值向后保持；
- Scroll 曲线只允许 `linear`、`hold`、`easeInQuad`、`easeOutQuad`、
  `easeInOutCubic`；明确禁止 `smooth`。

事件间以 §9.2 的进度函数插值 `value`。在后一个事件的精确时刻使用后一个事件值。
Scroll 只改变视觉，不改变命中秒。播放器先按当前精确 BarTime 采样 Scroll，再将该倍率作用
于剩余音频秒；当前社区客户端的固定二维渲染公式为：

```text
visualDistancePx =
    (noteAudioSecond - currentAudioSecond)
    * scrollSpeed(currentBarTime)
    * playerSpeed
    * 1026px/s
```

这里不是对历史流速积分。`1026px/s` 是当前客户端 Lv10 的 150 BPM 视觉基准，不属于谱面
序列化数据；谱面 BPM 只参与 BarTime/音频秒换算，不再改变实际像素流速。由于 Scroll 始终
为正，v2 不产生负流速回卷；速度变化仍可能使 Note 暂时远离判定线。

## 7. Note 公共模型与空间

### 7.1 类型和轨道

v2 Note 类型为：

```text
tap  drag  exTap  hold  mixer  mine  barLine
```

- `notesLeft`：左侧轨；轨内坐标 0→5 对应屏幕下→上。
- `notesCenter`：中央轨；轨内坐标 0→5 对应屏幕左→右。
- `notesRight`：右侧轨；轨内坐标 0→5 对应屏幕下→上。
- Mixer、Hold 和其他类型均可位于三轨。
- 路径节点继承父 Note 的轨道，不得单独指定或跨轨。

### 7.2 `center + width`

每个 Note 和路径点保存中心与宽度：

```text
left  = center - width/2
right = center + width/2
```

必须满足：

```text
center 是任意有限数
width 是有限数且 width > 0
```

`[left,right]` 可以部分或完全位于推荐轨内创作范围 `[0,5]` 之外；这用于表达 overscan、
全宽装饰和可无损迁移的既有谱面。播放器、validator 和转换器不得擅自 clamp、裁剪或移动
这些坐标。制谱器应默认把 `[0,5]` 作为可见安全区，并在对象越界时给出 warning，但不能仅
因越界拒绝正式 v2 文件。

旧格式左缘 `Position` 不属于 v2；迁移工具必须使用
`center = oldPosition + oldWidth/2`，但不得把 `position` 写入正式 v2。

### 7.3 ID、顺序与同时对象

- 父 Note 和所有路径节点 ID 在整个 chart 内唯一。
- `id` 用于编辑器选择、诊断和差异合并，不参与 Gameplay Digest。
- 完全同刻的多个 Note 同时发生。
- 语义相同但 ID 不同的重复 Note 是合法的独立对象，不能去重。
- loader 必须自行建立索引，不能以数组位置充当稳定 ID。

## 8. 普通 Note

普通结构只包含 `id/type/time/center/width`：

```json
{
  "id": "tap-001",
  "type": "tap",
  "time": { "bar": 0, "numerator": 1, "denominator": 5 },
  "center": 2.5,
  "width": 1.0
}
```

| 类型 | 语义 |
| --- | --- |
| `tap` | Began/Press 型四档判定 |
| `drag` | 接触型四档判定；已有触点经过即可 |
| `exTap` | Began/Press 型，四档窗口统一乘 1.5 |
| `mine` | 危险窗内触碰为 Miss，未触碰安全通过为 Prefect |
| `barLine` | 纯视觉，不产生主判定、不改变 Combo |

普通 Note 不得携带 `nodes`、`curveToNext` 或 `judge`。

## 9. 共用路径与曲线

Hold 与 Mixer 都由父 Note 加内嵌 `nodes` 组成有序控制点序列。父 Note 是点 `0`，最后一个
node 是终点。规则：

- `nodes` 至少一项；
- node 时间严格递增且全部晚于父 Note；
- 父 Note 和非尾 node 可提供 `curveToNext`；省略等价于 `linear`；
- 尾 node 不得提供 `curveToNext`；
- 判定范围、Hold/Mixer Body、Mixer 动态头和制谱器预览必须使用同一个 evaluator；
- 路径以 BarTime 为参数，不按音频秒比例求值。

### 9.1 区间参数

对相邻点 `i`、`i+1`，设精确 BarTime 为 `x_i < x_(i+1)`，当前时间为 `x`：

```text
u = (x - x_i) / (x_(i+1) - x_i),  0 <= u <= 1
```

非 `smooth` 区间使用同一个进度 `p=f(u)` 分别插值中心和宽度：

```text
center(x) = center_i + (center_(i+1)-center_i) * p
width(x)  = width_i  + (width_(i+1)-width_i)   * p
```

### 9.2 内置进度函数

路径允许：

```text
linear  hold  easeInQuad  easeOutQuad  easeInOutCubic  smooth
```

Scroll 只允许前五种。函数定义：

```text
linear:            f(u) = u
hold:              f(u) = 0 for 0 <= u < 1; f(1) = 1
easeInQuad:        f(u) = u^2
easeOutQuad:       f(u) = 1-(1-u)^2
easeInOutCubic:    f(u) = 4u^3                    when u < 1/2
                   f(u) = 1-((-2u+2)^3)/2         when u >= 1/2
```

在控制点精确时刻始终取该控制点自身的 `center/width`。相邻不同曲线允许速度不连续，
消费者不得擅自修改曲线或跨边界匹配切线。

### 9.3 `smooth`：`pchip-v1`

`smooth` 固定为保形分段三次 Hermite 插值（PCHIP），不能替换为 Bézier、Catmull–Rom 或
平台内置的其他 spline。Center 和 Width 在同一控制点序列上**分别**计算 PCHIP。

连续标记为 `smooth` 的一个或多个区间组成最大 smooth run。该 run 只使用从其起点到终点的
控制点，不读取显式 `linear/hold/ease*` 边界另一侧的点。若 run 只有一个区间，两端斜率都
等于该区间割线斜率。

对 run 内点 `(x_k,y_k)`，其中 `x` 是精确 BarTime 转成的实数，定义：

```text
h_k = x_(k+1) - x_k
d_k = (y_(k+1) - y_k) / h_k
```

内部点 `k=1..n-1` 的斜率 `m_k`：

```text
if d_(k-1) == 0 or d_k == 0 or sign(d_(k-1)) != sign(d_k):
    m_k = 0
else:
    w1 = 2*h_k + h_(k-1)
    w2 = h_k + 2*h_(k-1)
    m_k = (w1+w2) / (w1/d_(k-1) + w2/d_k)
```

run 有至少三个点时，左端候选斜率：

```text
m_0 = ((2*h_0+h_1)*d_0 - h_0*d_1) / (h_0+h_1)
if sign(m_0) != sign(d_0): m_0 = 0
else if sign(d_0) != sign(d_1) and abs(m_0) > abs(3*d_0): m_0 = 3*d_0
```

右端使用镜像公式：

```text
m_n = ((2*h_(n-1)+h_(n-2))*d_(n-1) - h_(n-1)*d_(n-2))
      / (h_(n-1)+h_(n-2))
if sign(m_n) != sign(d_(n-1)): m_n = 0
else if sign(d_(n-1)) != sign(d_(n-2)) and abs(m_n) > abs(3*d_(n-1)):
    m_n = 3*d_(n-1)
```

其中 `sign(0)=0`。在区间 `k`，使用 §9.1 的 `u`：

```text
H00 =  2u^3 - 3u^2 + 1
H10 =    u^3 - 2u^2 + u
H01 = -2u^3 + 3u^2
H11 =    u^3 -   u^2

y(u) = H00*y_k + H10*h_k*m_k + H01*y_(k+1) + H11*h_k*m_(k+1)
```

Center 和 Width 各自把 `y` 替换为对应值。实现至少使用 IEEE 754 binary64 计算曲线；
控制点时间的分数比较仍须精确。

整个连续路径（不只是控制点）的 Center 和 Width 都必须有限，且 Width 必须始终 `>0`。
对 PCHIP，validator 应检查每段 width cubic 在端点和所有区间内导数为零的点均为正；
`center±width/2` 允许超出 `[0,5]`，不得据此拒绝或 clamp。仅做固定步长采样不足以证明路径
合法。

渲染器应按屏幕空间误差自适应细分；误差超过约 `0.5px` 时继续细分。视觉细分精度不得
改变判定 evaluator 的结果。

## 10. Hold

```json
{
  "id": "hold-001",
  "type": "hold",
  "time": { "bar": 0, "numerator": 3, "denominator": 5 },
  "center": 2.5,
  "width": 1.2,
  "curveToNext": "smooth",
  "nodes": [
    {
      "id": "hold-001-shape",
      "time": { "bar": 1, "numerator": 1, "denominator": 10 },
      "center": 3.2,
      "width": 0.8,
      "curveToNext": "smooth",
      "judge": false
    },
    {
      "id": "hold-001-tail",
      "time": { "bar": 1, "numerator": 4, "denominator": 5 },
      "center": 2.8,
      "width": 1.4,
      "judge": true
    }
  ]
}
```

### 10.1 结构和判定单元

- 父 Note 是 Hold 头，始终参与塑形并产生一个四档主判定。
- **所有 node 无论 `judge` 值如何都参与路径形状、区间划分、渲染和触摸范围。**
- `judge` 只决定 node 是否产生主判定；省略等价于 `true`。
- 最后一个 node 是 Hold 尾，必须显式写 `"judge": true`。
- Hold 主判定数为 `1 + count(nodes where resolved judge == true)`。
- Hold 头 Miss 时，全部尚未结算的 `judge:true` node 同帧 Miss；`judge:false` node 不产生结果。

### 10.2 持续、grace 与 D4-C

成功判定 Hold 头后进入 Holding。消费者在每个输入时间点先形成完整触点快照，再按该时刻
路径范围判断是否有同轨有效覆盖。接触区间定义为 `[beganTime, releaseTime)`：

- Began 恰好发生在中间 node 或 Mixer tick 时计入覆盖；
- Release 恰好同刻不计入覆盖；
- 若另一触点在同刻 Began 并覆盖范围，完整快照仍视为连接；
- Hold 尾的 release 评级另按下述专门规则处理。

连接时经过 `judge:true` 中间 node，该单元为 Prefect。首次失去全部有效覆盖时记录
`lostAt=L`，并只在该时刻计算一次：

```text
effectiveBpm = clamp(BPM-at-audio-time(L), 120, 200)
graceDuration = 30 / effectiveBpm seconds
deadline D = L + graceDuration
```

grace 期间不随 BPM 每帧重算。在 `D` 前重新覆盖则清除本次 `lostAt`；之后再次失联会建立
新的 `lostAt`。grace 中经过的 `judge:true` 中间 node 仍结算为 Prefect。

设尾的音频时刻为 `T`。未恢复连接时：

```text
settlementTime = min(T, D)
```

- 若 `T <= D`，在尾时立即结算，不等待剩余 grace；尾用 `Judge(target=T,input=L)` 四档评级。
- 若 `D < T`，在 deadline 把尚未结算的 `judge:true` 中间 node 批量判 Miss；尾仍立即用
  `Judge(target=T,input=L)` 评级，而不是用 `D` 评级。
- 批量结算只执行一次；Hold 随后永久失败，不可重接，未来 node 不得重复清 Combo。
- 若持续连接到尾，尾为 Prefect。
- Release 恰好发生在 `T` 时，等价于 `Judge(T,T)`，尾为 Prefect。
- 持有过尾后才收到 Release 必须钳到 `T`，尾仍为 Prefect。

这里的 `BPM-at-audio-time(L)` 使用 §5 的分段映射找到失联瞬间所在 BPM 段。

## 11. Mixer

Mixer 与 Hold 共用路径结构，可以位于三轨；Mixer node 不允许 `judge`：

```json
{
  "id": "mixer-001",
  "type": "mixer",
  "time": { "bar": 1, "numerator": 1, "denominator": 7 },
  "center": 1.3,
  "width": 0.8,
  "curveToNext": "smooth",
  "nodes": [
    {
      "id": "mixer-001-node-1",
      "time": { "bar": 1, "numerator": 3, "denominator": 7 },
      "center": 3.0,
      "width": 0.6,
      "curveToNext": "smooth"
    },
    {
      "id": "mixer-001-tail",
      "time": { "bar": 2, "numerator": 1, "denominator": 10 },
      "center": 2.4,
      "width": 1.0
    }
  ]
}
```

### 11.1 D5-A 判定网格

设精确头时间为 `H`，路径尾时间为 `E`：

```text
tick(k) = H + k/8 bar
k = 1, 2, ..., floor(8 * (E-H))
```

- `k=0` 不属于后续 tick；它是唯一的 Mixer 头。
- 头是 Began/Press 型普通四档判定，必须由新按下触发。
- 头 Miss 只影响头，不会使 Body 永久失败。
- `k>=1` 的 tick 只产生 Prefect 或 Miss：该精确时刻 connected 为 Prefect，否则 Miss。
- 后续 Began/Moved/Stationary 均可连接或重接；过去的 tick 不补判，未来 tick 继续正常判定。
- 非网格尾不补判，没有独立尾判；恰好落在网格上的尾自然是最后一个 tick。
- 总判定数为 `1 + floor(8*(E-H))`；持续一 bar 时为头加 8 tick，共 9 个。
- 每个 tick 的范围必须用该精确 BarTime 的路径 evaluator 求值，不能使用渲染帧当前时刻范围。

同一时刻必须按以下顺序：收集全部 pointer 事件、形成完整触点快照、更新 connected 状态、
最后结算头或 tick。Body 始终存在；动态头只在 connected 时显示。Body 的可见性和动态头
不改变判定单元。

## 12. 判定、计分与派生数据

### 12.1 规则集窗口

`dynamite-uv-ruleset-2.0` 使用以下绝对时间窗：

| JudgePreset | Prefect | Great | Good | Miss 候选范围 |
| --- | --- | --- | --- | --- |
| `casual` | ±100ms | ±150ms | ±200ms | ±250ms |
| `normal` | ±62.5ms | ±150ms | ±200ms | ±250ms |
| `hard` | ±62.5ms | ±112.5ms | ±162.5ms | ±250ms |

`Judge(target,input)` 使用 `input-target` 的绝对值选择最内层匹配等级；超出 Good 为 Miss。
Miss 候选范围用于输入候选选择，不能把超出 Good 的输入升级为 Good。`exTap` 的全部边界
统一乘 1.5。Mixer 后续 tick 和 Hold 已连接的中间 node 不使用四档窗口。

### 12.2 主判定展开

- `tap/drag/exTap/mine`：各一个主判定。
- `hold`：头加所有 resolved `judge:true` node。
- `mixer`：头加 §11.1 的后续 ticks。
- `barLine`：零个。

语义 validator 必须拒绝主判定总数为零的 chart。

每个主判定权重均为：

| Prefect | Great | Good | Miss |
| --- | --- | --- | --- |
| 100 | 70 | 50 | 0 |

Prefect/Great/Good 增加 Combo，Miss 清零 Combo。所有主判定进入 P/GR/GD/M、Combo、Score、
CLEAR、Health 和 Boost。理论满分为 `主判定数 * 100`。

百万分使用整数 half-up：

```text
normalizedScore = floor((RawScore * 1000000 + TheoreticalMax/2) / TheoreticalMax)
```

结果钳制到 `0..1000000`。CLEAR 为 `RawScore/TheoreticalMax*100%`；评级：Ω≥98、S≥95、
A≥90、B≥80，否则 C。

### 12.3 同步多押描边

Tap 的异轨同步金色描边是运行时派生视觉，不保存 baked 标记。加载器按**精确 BarTime**把
按下型父 Note 分组；若同一组覆盖至少两个不同轨道，则组内 Tap 显示同步描边。当前按下型
集合为 `tap`、`exTap`、`hold` 和 `mixer`；Drag、Mine、BarLine 与路径 node 不参与组判定。

同轨重复 Note 不会单独构成异轨多押；近似但不完全相等的 BarTime 也不合并。该描边由已
进入 Gameplay Digest 的轨道、类型与时间唯一推导，因此不单独进入 projection。legacy
`Baked_SyncNote` 转换时必须丢弃，并可在审计报告中列出它与重新计算结果的差异。

### 12.4 不落盘的派生字段

正式 v2 不保存：

```text
SubNoteId
Baked_Second
Baked_SyncNote
Baked_TotalMainNote
BPM 段起始 Seconds
Note/node 的音频秒
Note 级 track
主判定总数和理论满分
编辑器网格、视口、选择、锁定和撤销栈
```

消费者必须从权威字段重算这些信息。编辑历史可以由未来制谱器保存在正式包之外，但其格式
不属于本规范。

## 13. Gameplay Digest v1

### 13.1 目的与成绩身份

Gameplay Digest 用于区分“展示信息变化”和“玩法内容变化”。谱师不得在 chart 中手工维护
Digest；导出器、安装器和成绩系统必须自动计算并互相校验。

```text
algorithm      = "gameplay-v1"
rulesetId      = "dynamite-uv-ruleset-2.0"
gameplayDigest = lowercase-hex(SHA-256(UTF-8(JCS(gameplayProjection))))
scoreIdentity  = (packId, chartId, rulesetId, gameplayDigest)
```

`scoreIdentity` 是保留四个成员边界的有序四元组，不是无分隔字符串拼接；持久化实现可以用
结构化 object、数组或无歧义的长度前缀编码。

`JCS` 是 RFC 8785 JSON Canonicalization Scheme。实现必须按 RFC 8785 处理属性排序、字符串和
IEEE 754 binary64 数值序列化，不能以语言默认的 pretty JSON 或字典顺序代替。投影前把数值
负零规范化为正零。

### 13.2 投影结构

投影固定为：

```json
{
  "rulesetId": "dynamite-uv-ruleset-2.0",
  "judgePreset": "hard",
  "audio": {
    "sha256": "<resolved audio 文件原始字节的 lowercase SHA-256>"
  },
  "timing": {
    "audioOffsetSec": 0.25,
    "bpms": []
  },
  "scrollSpeeds": [],
  "notes": {
    "left": [],
    "center": [],
    "right": []
  }
}
```

生成步骤：

1. 严格验证包和 chart，解析 JudgePreset 与 `resolvedAudio`。
2. 对 resolved audio **文件原始字节**计算 SHA-256；不是对解码 PCM、文件名或路径哈希。
3. BarTime 统一约分；所有 `-0` 统一为 `0`。
4. 复制 BPM 的 `time/bpm` 和 `audioOffsetSec`。
5. Scroll 省略或空数组时规范化为一个 BarTime 0、`value:1` 的尾事件。非尾事件省略曲线时
   写入 `"curveToNext":"linear"`；尾事件不写曲线。
6. 对 Note 移除所有 ID；保留 `type/time/center/width`。Hold/Mixer 父点和非尾 node 省略
   曲线时写入 `linear`，尾 node 不写曲线。Hold 每个 node 都写出 resolved `judge`；Mixer
   node 不写 `judge`。
7. 每轨独立按 `(精确 time, typeOrder, JCS(noteProjection) UTF-8 bytes)` 升序排序；
   `typeOrder` 固定为 `tap,drag,exTap,hold,mixer,mine,barLine`。完全相同的重复项保留多份。
8. 对整个 projection 执行 RFC 8785 JCS，再计算 SHA-256。

BPM 和 Scroll 已由语义校验保证按时间严格递增，路径 node 保持路径顺序，不另行排序。

### 13.3 包含与排除

投影包含所有能改变玩法或视觉读谱的信息：

- `rulesetId` 与解析后的 JudgePreset；
- resolved audio 原始文件 SHA-256；
- `audioOffsetSec`、BPM；
- Scroll 值和曲线；
- 三轨归属、Note 类型、时间、center、width；
- Hold/Mixer 全部塑形节点和路径曲线；
- Hold node 的 resolved `judge`。

投影不包含：

- `packId`、`chartId`；它们位于成绩外层身份；
- pack revision；
- 父 Note/node ID 的纯重命名；
- title、artist、charters、cover、preview；
- difficulty、difficultyKey、level、unrated 本身；只有解析后的 JudgePreset 进入投影；
- 文件名、包路径、JSON 空白、属性顺序；
- 编辑器状态和编辑历史。

因此只改封面、标题、等级、谱师署名或 ID 时 Digest 不变；改 Note、BPM、Scroll、路径、
`judge`、offset、最终音频字节或 ruleset 时 Digest 必须变化。旧成绩可以保留在历史中，但只有
完整 `scoreIdentity` 相同的记录才可作为当前 BEST。

### 13.4 Known-answer vector

`../schemas/chart-format-v2/vectors/gameplay-digest-v1.json` 保存：

- 输入 golden chart 和音频路径；
- audio SHA-256；
- 完整 `projection`；
- RFC 8785 `canonicalJson`；
- 预期 `expectedSha256`。

未来 C#、TypeScript 或其他实现必须用该向量做跨语言测试。测试不得只把向量内已有的
`canonicalJson` 再哈希；还必须从源包独立生成 projection 并逐字段比较。

## 14. 制谱器和消费者要求

制谱器应让谱师操作音乐网格、可视路径和难度信息，而不是要求手写 JSON。正式导出必须：

- 规范化 BarTime、Unicode NFC 和字段默认值；
- 输出稳定 ID 与稳定数组顺序；
- 运行结构、语义、音频边界和 Digest 校验；
- 对错误报告 `文件 + JSON Pointer + 原因`；
- 不把视口、选择、网格、图层锁定、撤销栈或工程缓存写入正式文件。

编辑网格可以把当前 bar 划分为任意正整数份，5、7、11、13 与常用 4、8、16、24、32 等分
具有同等能力。吸附在 BarTime 域执行，切换网格不修改已放置 Note。Mixer 判定网格始终相对
自身头部每 `1/8 chart bar`，不受编辑器网格影响。

导入器不得静默修复会改变玩法的错误。BarTime 约分、NFC、字段排序等不改变语义的规范化
可以在明确提示后自动完成；越界、重复 ID、未知字段、非法曲线等必须拒绝。

## 15. 校验要求

### 15.1 JSON Schema 覆盖

`pack.schema.json` 和 `chart.schema.json` 覆盖：

- 固定 format/version、必需字段、类型与未知字段；
- ID、相对路径、preview、难度与 `level/unrated` 条件；
- 包音频 fallback 的结构条件；
- BarTime 基础整数范围、BPM、严格正 Scroll 值与曲线 enum；
- 普通/Hold/Mixer union、node 字段和三轨 Mixer。

### 15.2 必须额外执行的语义校验

JSON Schema 之外，validator 至少必须拒绝：

- 重复 JSON 属性名、非 NFC `difficultyKey`、规范化后重复自定义难度键；
- 重复 `chartId`、重复 chart 文件引用、chart 文件中的 `chartId` 不匹配；
- 不可解析的音频、包外路径、路径碰撞或 archive link；
- BarTime 未约分、`numerator >= denominator` 或整数超出实现安全范围；
- BPM/Scroll 不从 BarTime 0 开始、时间不严格递增或尾事件仍带曲线；
- Note 数组时间降序，父 Note/node ID 重复；
- `center/width` 端点或曲线内部出现非有限值，或 `width <= 0`；
- Hold/Mixer node 不严格晚于前一点；
- 路径尾仍带 `curveToNext`；
- Hold 尾未显式 `judge:true`；
- Mixer node 出现 `judge`；
- 主判定总数为零；
- 任一主判定音频秒早于 0 或末判超过音频时长加 50ms；
- preview 超出 resolved audio；
- 计算出的 Gameplay Digest 与缓存/成绩声明不一致。

结构 Schema 无法证明分数约分、有序性、唯一性、跨文件引用、PCHIP 全区间边界和音频时长，
实现不得因“已经过 Schema”而跳过这些检查。

## 16. 完整示例

版本化的完整示例位于：

```text
schemas/chart-format-v2/examples/golden-pack/meta.json
schemas/chart-format-v2/examples/golden-pack/chart_hard.json
schemas/chart-format-v2/examples/golden-pack/chart_custom.json
schemas/chart-format-v2/examples/golden-pack/audio.wav
```

其中 `meta.json` 同时覆盖标准 hard 与自定义 `*** 15`：

```json
{
  "format": "dynamite-uv-pack",
  "formatVersion": 2,
  "id": "example.synthetic-pulse",
  "revision": 1,
  "title": "Synthetic Pulse",
  "artist": "Dynamite Universe Contributors",
  "audio": "audio.wav",
  "preview": { "startSec": 0.5, "durationSec": 4.0 },
  "charts": [
    {
      "id": "hard",
      "difficulty": "hard",
      "level": 12,
      "charters": ["Example Charter"],
      "file": "chart_hard.json"
    },
    {
      "id": "triple-star",
      "difficulty": "custom",
      "difficultyKey": "***",
      "level": 15,
      "charters": ["Example Charter"],
      "file": "chart_custom.json"
    }
  ]
}
```

`chart_hard.json` 展示任意分母、BPM 变化、正向 Scroll、`center+width`、包含
`judge:false` 塑形节点的 Hold、`smooth` PCHIP 和非网格尾 Mixer。该目录中的 WAV 是项目
用标准库生成的原创合成测试音，不包含任何原版内容。示例是回归资产，不是需要随 Public APK
发布的内置歌曲。

## 附录 A：`pchip-v1` 数值样例

以下样例用于检查端点公式和转向/平台处理。令三个控制点的 BarTime 为 `x=[0,1,2]`。

Center 使用 `y=[1,2,2]`：

```text
d = [1, 0]
m = [1.5, 0, 0]
y(0.5) = 1.6875
y(1.5) = 2
```

Width 使用 `y=[1,0.8,1.2]`：

```text
d = [-0.2, 0.4]
m = [-0.5, 0, 0.7]
y(0.5) = 0.8375
y(1.5) = 0.9125
```

误差比较建议绝对误差不大于 `1e-12`。该样例只验证 evaluator；正式 validator 仍须检查
整个 `center±width/2` 范围。

## 附录 B：实现检查表

- [ ] UTF-8、无重复属性、未知字段拒绝
- [ ] 包路径安全与引用完整
- [ ] BarTime 规范化并使用精确比较
- [ ] BPM/Scroll/Note/node 严格有序
- [ ] Scroll 始终 `>0` 且不接受 `smooth`
- [ ] `center+width` 全路径有限且 width 始终为正；overscan 不 clamp
- [ ] 三轨 Mixer 使用同一套路径与判定语义
- [ ] 同步 Tap 金框按精确 BarTime 和异轨按下型父 Note 派生
- [ ] 路径统一 evaluator 与 `pchip-v1`
- [ ] Hold `judge` 只影响判定，不影响形状
- [ ] Hold D4-C 使用首次 `lostAt` 和固定 grace deadline
- [ ] Mixer D5-A：四档头、后续 Prefect/Miss、可重接
- [ ] 音频与 preview 边界检查
- [ ] 主判定、理论满分和统计全部重算
- [ ] Gameplay Digest 从语义投影自动计算
- [ ] 成绩使用 `(packId, chartId, rulesetId, gameplayDigest)` 四元组
- [ ] 编辑器状态不进入正式 chart

## 附录 C：版本策略

- 对未知 `format` 或不支持的 `formatVersion` 必须 fail closed。
- 添加可忽略展示字段也必须通过新规范明确；v2 文件不能依赖消费者忽略未知字段。
- 改变本文中判定、路径或 Digest 语义时必须分配新的 `rulesetId`；若同时改变 JSON 合同，
  还必须提升 `formatVersion`。
- `dynamite-uv-pack`、`dynamite-uv-chart` 与版本 `2` 的字面值永久保留。
