# DUX-Community 谱面格式 v2 草案

> 状态：讨论稿；v2 loader 和编辑器尚未接入，Hold/Mixer 派生判定已先移植到现有运行时。
>
> 最后整理：2026-08-13。
>
> 本文汇总当前关于社区谱面 v2 的全部讨论。标记为“已确认”的内容视为当前设计约束；
> 标记为“待确认”的内容是已提出但尚未最终拍板的方案，后续修改以新的讨论结果为准。

## 1. 设计目标

- 使用 clean-room 社区格式，不发布或依赖任何原版谱面和素材。
- 谱面包与单难度谱面分离，一个谱面包可包含多个难度。
- Hold 和 Mixer 使用父 Note 内嵌路径节点，不再使用顶层 `SubNoteId` 链。
- BarTime 使用精确有理数，支持任意正整数分母。
- BPM 与视觉变速相互独立。
- 不引入拍号事件；复杂拍子通过 BPM 换算和任意 Bar 分数表达。
- 同一侧 Note 放在同一个数组中，保持人工阅读和版本差异审查的清晰度。
- 编辑器网格属于编辑器状态，不进入发布谱面。
- 不保存可由权威字段稳定推导的烘焙数据。

## 2. 谱面包

谱面包是一个目录，至少包含 `meta.json`、一个谱面文件和对应音频。例如：

```text
meta.json
chart_hard.json
chart_tech.json
audio_default.ogg
audio_tech.ogg
cover.png
```

### 2.1 `meta.json`

```json
{
  "format": "dux-community-pack",
  "formatVersion": 2,
  "id": "author.song-id",
  "revision": 1,

  "title": "Song Title",
  "artist": "Artist",
  "audio": "audio_default.ogg",
  "cover": "cover.png",

  "preview": {
    "startSec": 30.0,
    "durationSec": 15.0
  },

  "charts": [
    {
      "id": "hard",
      "difficulty": "hard",
      "level": 12,
      "charters": ["Charter A"],
      "file": "chart_hard.json"
    },
    {
      "id": "tech",
      "difficulty": "tech",
      "level": 15,
      "charters": ["Charter A", "Charter B"],
      "file": "chart_tech.json",
      "audio": "audio_tech.ogg",
      "preview": {
        "startSec": 42.0,
        "durationSec": 15.0
      }
    }
  ]
}
```

### 2.2 包字段

| 字段 | 类型 | 必需 | 语义 |
| --- | --- | --- | --- |
| `format` | string | 是 | 固定为 `dux-community-pack` |
| `formatVersion` | int | 是 | 当前为 `2` |
| `id` | string | 是 | 社区内永久稳定的谱面包 ID |
| `revision` | int | 是 | 同一谱面包的内容修订号，从 1 开始递增 |
| `title` | string | 是 | 曲名 |
| `artist` | string | 是 | 曲师名 |
| `audio` | string | 条件必需 | 包级默认音频 |
| `cover` | string | 否 | 包内曲绘；省略时客户端使用 clean-room 占位图 |
| `preview` | object | 否 | 包级默认试听区间 |
| `charts` | array | 是 | 一个或多个难度条目 |

`preview.startSec` 和 `preview.durationSec` 均以音频秒为单位。`startSec >= 0`，
`durationSec > 0`。

### 2.3 难度条目

| 字段 | 类型 | 必需 | 语义 |
| --- | --- | --- | --- |
| `id` | string | 是 | 包内稳定且唯一的难度 ID |
| `difficulty` | enum | 是 | `casual/normal/hard/mega/giga/tech` |
| `level` | int | 是 | 显示等级 |
| `charters` | string[] | 是 | 一个或多个谱师 |
| `file` | string | 是 | 对应谱面文件 |
| `audio` | string | 否 | 该难度使用的差分音频 |
| `preview` | object | 否 | 该难度使用的试听区间 |

音频和试听区间按以下规则解析：

```text
resolvedAudio   = chartEntry.audio   ?? pack.audio   ?? error
resolvedPreview = chartEntry.preview ?? pack.preview ?? clientDefault
```

- 包级 `audio` 可以省略，但此时每个难度条目都必须提供 `audio`。
- 不同难度可使用不同剪辑、混音、长度或前置静音的音频。
- `audioOffsetSec` 位于各自的谱面文件中，因此差分音频可以独立校准。
- 成绩键使用 `packId + chartId`，不得只使用 `difficulty`。
- `charts[].id` 在包内唯一；不同条目可以使用相同 `difficulty`，但 `id` 不得重复。

### 2.4 包路径安全

- `audio`、`cover` 和 `file` 必须是包内相对路径。
- 禁止绝对路径、URI、空路径段和 `..` 路径穿越。
- 导入器在解压前后都必须验证最终路径仍位于目标谱面包目录内。

## 3. 单难度谱面

### 3.1 顶层结构

```json
{
  "format": "dux-community-chart",
  "formatVersion": 2,
  "chartId": "tech",
  "audioOffsetSec": 0.137,

  "timing": {
    "bpms": [
      {
        "time": { "bar": 0, "numerator": 0, "denominator": 1 },
        "bpm": 150.0
      },
      {
        "time": { "bar": 16, "numerator": 1, "denominator": 5 },
        "bpm": 180.0
      }
    ]
  },

  "scrollSpeeds": [
    {
      "time": { "bar": 0, "numerator": 0, "denominator": 1 },
      "value": 1.0,
      "curveToNext": "linear"
    },
    {
      "time": { "bar": 8, "numerator": 0, "denominator": 1 },
      "value": 0.4,
      "curveToNext": "easeInOutSine"
    },
    {
      "time": { "bar": 9, "numerator": 0, "denominator": 1 },
      "value": -0.5,
      "curveToNext": "hold"
    },
    {
      "time": { "bar": 10, "numerator": 0, "denominator": 1 },
      "value": 1.0
    }
  ],

  "notesLeft": [],
  "notesCenter": [],
  "notesRight": []
}
```

| 字段 | 类型 | 必需 | 语义 |
| --- | --- | --- | --- |
| `format` | string | 是 | 固定为 `dux-community-chart` |
| `formatVersion` | int | 是 | 当前为 `2` |
| `chartId` | string | 是 | 必须与引用它的 `meta.json charts[].id` 一致 |
| `audioOffsetSec` | number | 是 | BarTime 0 对应的音频秒数，可为负数 |
| `timing.bpms` | array | 是 | BPM 时间线 |
| `scrollSpeeds` | array | 否 | 视觉流速时间线；省略时恒为 1 |
| `notesLeft` | array | 是 | 左侧 Note |
| `notesCenter` | array | 是 | 中央 Note |
| `notesRight` | array | 是 | 右侧 Note |

顶层不提供统一 `notes[]`。Note 和路径节点均不保存 `track`，其所属侧由所在数组决定。

## 4. 精确 BarTime

### 4.1 表示

```json
{
  "bar": 12,
  "numerator": 1,
  "denominator": 5
}
```

其值为：

```text
12 + 1/5 bar
```

### 4.2 规范形式

- `bar` 是非负整数。
- `denominator` 是正整数，不设音乐意义上的分母白名单。
- `0 <= numerator < denominator`。
- `numerator/denominator` 必须约分，例如 `2/10` 保存为 `1/5`。
- 整 Bar 统一保存为 `{bar: N, numerator: 0, denominator: 1}`。
- 正式谱面禁止使用浮点 BarTime。
- 排序、判等、区间判断、步进和派生判定点均使用精确有理数运算。
- 只有换算音频秒数及最终渲染时才转为浮点数。

编辑器可接受 `12+1/5`、`61/5` 等输入形式，但导出时必须规范化为上述对象。

实现可以设置防止恶意输入和整数溢出的技术上限，但不得把分母限制为常用音乐分割值。

## 5. BPM 时间线

```json
{
  "timing": {
    "bpms": [
      {
        "time": { "bar": 0, "numerator": 0, "denominator": 1 },
        "bpm": 150.0
      }
    ]
  }
}
```

规则：

- `bpm > 0` 且必须为有限数值。
- 第一条 BPM 事件必须位于 BarTime 0。
- BPM 事件时间必须严格递增，不允许同刻事件。
- BPM 变化是瞬时变化，不使用 `curveToNext`。
- 不保存每段起始秒数，loader 根据前段累计时间推导。

对于 BPM 段起点 `segment.time` 和对应音频秒 `segment.second`：

```text
second(t) = segment.second + (t - segment.time) * 240 / segment.bpm
```

第一段的 `segment.second = audioOffsetSec`。

### 5.1 不提供拍号事件

复杂拍子通过谱面 BPM 和任意 Bar 分数表达。例如原曲为 5/4、四分音符 BPM 为 `Q`：

```text
chartBpm = Q * 4/5
oneQuarterNote = 1/5 chart bar
```

同理可以使用 7、11、13 等任意分母。运行时不解释拍号，只解释 BarTime 和 BPM。

## 6. 视觉变速

```json
{
  "time": { "bar": 8, "numerator": 0, "denominator": 1 },
  "value": 0.4,
  "curveToNext": "easeInOutSine"
}
```

规则：

- `value` 必须为有限数值，可以为正数、0 或负数。
- 关键帧时间必须严格递增，不允许同刻关键帧。
- `curveToNext` 描述当前关键帧到下一关键帧的插值。
- 最后一个关键帧不得提供 `curveToNext`。
- 空数组或省略字段等价于从 BarTime 0 开始恒为 `1.0`。
- BPM 决定 BarTime 与音频秒的换算；`scrollSpeeds` 只影响视觉位置。

运行时沿用已经确认的非积分模型：

```text
visualDistance =
    (noteTime - currentTime)
    * scrollSpeed(currentTime)
    * playerSpeed
```

该模型允许变速时 Note 暂时远离判定线或回溯。它不是对历史流速进行距离积分，也不使用
透视投影。

## 7. Note 的公共规则

### 7.1 类型

```text
tap
drag
exTap
hold
mixer
mine
barLine
```

### 7.2 侧别

- `notesLeft` 包含左侧全部 Note。
- `notesCenter` 包含中央全部 Note。
- `notesRight` 包含右侧全部 Note。
- Mixer 只能位于 `notesLeft` 或 `notesRight`。
- Hold 可以位于三侧。
- 路径节点自动继承父 Note 所在侧，不允许单独指定或跨侧。

### 7.3 ID 和顺序

- 每个父 Note 的 `id` 在整个 `chart.json` 中唯一。
- 路径节点的 `id` 也在整个 `chart.json` 中唯一。
- ID 是编辑器选择、撤销、诊断和差异合并的稳定标识，不表示播放顺序。
- 三个 Note 数组按 `time` 升序保存；完全同刻时建议按 `id` 排序以获得稳定输出。
- 运行时不得依赖文件数组顺序，加载后仍需校验并建立索引。
- 完全同刻的多个 Note 视为同时发生。

### 7.4 空间坐标（待确认）

当前最新提案是 v2 不再保存左缘 `position`，改为保存中心坐标 `center` 和宽度 `width`：

```text
left  = center - width / 2
right = center + width / 2
```

约束：

```text
width > 0
width / 2 <= center <= 5 - width / 2
```

旧格式导入 v2 时：

```text
center = oldPosition + oldWidth / 2
```

此项用于改善路径编辑和缓动的认知模型，但尚未由用户最终确认。若保留旧格式左缘语义，
其判定范围仍应为 `[position, position + width]`。

## 8. 普通 Note

```json
{
  "id": "tap-001",
  "type": "tap",
  "time": { "bar": 4, "numerator": 1, "denominator": 5 },
  "center": 2.5,
  "width": 1.0
}
```

`tap`、`drag`、`exTap`、`mine` 和 `barLine` 均使用此结构。

- `tap`：按下型普通判定。
- `drag`：接触型判定，已有触点经过即可命中。
- `exTap`：宽判定窗的按下型 Note。
- `mine`：危险窗内触碰产生 Miss；未触碰安全通过。
- `barLine`：纯视觉对象，不计分、不改变 Combo。

## 9. Hold

```json
{
  "id": "hold-001",
  "type": "hold",
  "time": { "bar": 6, "numerator": 0, "denominator": 1 },
  "center": 2.5,
  "width": 1.2,
  "curveToNext": "smooth",
  "nodes": [
    {
      "id": "hold-001-node-1",
      "time": { "bar": 6, "numerator": 1, "denominator": 4 },
      "center": 3.2,
      "width": 0.8,
      "curveToNext": "smooth"
    },
    {
      "id": "hold-001-node-2",
      "time": { "bar": 6, "numerator": 1, "denominator": 2 },
      "center": 2.8,
      "width": 1.4
    }
  ]
}
```

### 9.1 结构

- 父 Note 是 Hold 头，也是第一个主判定。
- `nodes` 至少包含一个节点。
- 每个节点都是主判定；最后一个节点天然是 Hold 尾。
- 节点时间必须严格递增，且全部晚于父 Note。
- 最后一个节点不得提供 `curveToNext`。
- Hold 不提供“只塑形、不判定”的路径节点。
- 路径形状、视觉采样和触摸范围必须由同一个路径求值器产生。

### 9.2 判定

- Hold 头按普通输入窗口判定。
- Hold 头和每个节点均使用完整 `100/70/50/0` 权重，并进入 Combo、
  P/Great/Good/Miss、分数、CLEAR 和理论满分。
- 头部越过判定线后，只要仍在 Late 窗内，仍可正常接起。
- Early、Exact 和 Late 接起后的持续效果及正在判定的路径都位于判定线上。
- Hold 持续期间逐帧检查接触，并保留断触宽限。
- Hold 头 Miss 时，所有尚未结算节点在同一时刻批量判为 Miss。
- 断触一旦确认，所有尚未结算节点在断触时刻批量判为 Miss。
- Hold 失败后永久失败，不能重新接回。
- 批量 Miss 必须一次性影响 Combo，不能等未来各节点到线后再次清空 Combo。

## 10. Mixer

```json
{
  "id": "mixer-001",
  "type": "mixer",
  "time": { "bar": 8, "numerator": 1, "denominator": 7 },
  "center": 1.4,
  "width": 0.8,
  "curveToNext": "smooth",
  "nodes": [
    {
      "id": "mixer-001-node-1",
      "time": { "bar": 8, "numerator": 4, "denominator": 7 },
      "center": 3.3,
      "width": 0.6,
      "curveToNext": "smooth"
    },
    {
      "id": "mixer-001-node-2",
      "time": { "bar": 9, "numerator": 1, "denominator": 7 },
      "center": 2.5,
      "width": 1.0
    }
  ]
}
```

### 10.1 结构

- Mixer 只能位于侧轨。
- 父 Note 是路径头。
- `nodes` 至少包含一个节点，只负责塑形，不直接产生判定。
- 最后一个节点是路径终点。
- 节点时间必须严格递增，且全部晚于父 Note。
- 最后一个节点不得提供 `curveToNext`。
- Mixer Body 始终按完整路径渲染。
- 只有当前触点接上 Mixer 时，才在判定线上的实际滑块位置渲染动态头。

### 10.2 派生判定网格

Mixer 判定从父 Note 的精确时间开始，每 `1/8 chart bar` 一次：

```text
tick(k) = headTime + k * 1/8 bar, k = 0, 1, 2, ...
保留 tick(k) <= endTime 的点
```

- Mixer 头不要求位于全谱八分网格。
- 网格相位只由该 Mixer 的头部时间决定。
- 尾若恰好落在相对头部的八分网格上，自然形成一次判定。
- 尾不在该网格上时，不补额外尾判。
- 持续 1 bar 的 Mixer 有 `0, 1/8, ..., 1` 共 9 次判定。
- 判定数量为 `floor((endTime - headTime) * 8) + 1`，乘法和取整使用精确有理数。

### 10.3 判定行为

- 每个派生点都是社区版主判定，使用完整 `100/70/50/0` 权重，并进入 Combo、
  P/Great/Good/Miss、分数、CLEAR 和理论满分。
- Mixer 没有超时 Miss 或永久失败状态。
- 单个判定点 Miss 后，后续任意时刻重新接上仍可继续判定。
- 每个判定点的触摸范围由该精确时刻的路径求值结果决定。
- Mixer 不使用普通 Note 越线后淡出的 Miss 处理。

## 11. 路径曲线（部分待确认）

### 11.1 单一曲线字段

路径父 Note 和除尾节点外的每个节点最多提供一个 `curveToNext`。不为位置、宽度分别增加
两套曲线字段，也不允许脚本、JavaScript、C# 表达式或任意函数代码进入谱面。

已讨论的内置曲线名：

```text
smooth
linear
hold
easeInSine
easeOutSine
easeInOutSine
easeInQuad
easeOutQuad
easeInOutQuad
easeInCubic
easeOutCubic
easeInOutCubic
```

`linear` 是线性插值；`hold` 在区间内保持起点值，到下一节点时瞬时切换。其他 `ease*`
函数采用标准归一化缓动，输入输出范围均为 `[0, 1]`。

### 11.2 最新路径提案

单纯把左缘换成中心后继续对 `center` 和 `width` 使用同一缓动参数，视觉结果与原左缘模型
在数学上等价，不能解决路径不自然的问题。最新提案因此是：

- `curveToNext` 控制路径中心线的运动。
- `smooth` 使用穿过整组节点且一阶连续的保形三次 Hermite/PCHIP 曲线。
- 相邻 `smooth` 段在中间节点连续通过，不在每个节点重复减速到零。
- 转向节点自然减速后反向。
- `linear` 和显式 `ease*` 允许谱师有意制造折点、停顿或分段速度变化。
- Width 沿路径使用独立但无需额外配置的保形平滑，以中心为基准向两侧变化。
- Width 插值不得产生负值，也不应越过相邻节点给出的宽度范围。

这套“中心线由 `curveToNext` 控制、Width 自动平滑”的具体语义尚未最终确认。它替代了更早的
“同一个 `curveToNext` 同时作用于 Position 和 Width”提案。

### 11.3 路径采样与渲染

- 不能只把每两个节点画成一个梯形，否则缓动曲线不会真实显示。
- 渲染器应从权威路径求值器采样，构造连续带状网格。
- 建议根据屏幕空间误差自适应细分；曲线与直线近似误差超过约 `0.5px` 时继续细分。
- 判定、Mixer 动态头、Hold/Mixer Body 和编辑器预览必须共用同一个路径求值器。
- 宽度始终表示判定线方向上的实际宽度，不沿曲线法线扩宽。
- 不使用透视投影。

## 12. 编辑器网格

编辑器网格不属于正式谱面 schema，`chart.json` 不保存 `editor` 或 `gridSections` 字段。

- 谱师编辑时可以把当前 Bar 划分为任意正整数份。
- 常用 2、4、8、12、16、24、32、48、64 可作为快捷项，但不是限制。
- 5、7、11、13 等分割应与常用分割具有同等能力。
- 吸附在 BarTime 域执行，因此 BPM 变化不会破坏精确分数位置。
- 切换分割数不修改已经放置的 Note。
- 当前分割数可以保存在浏览器本地设置、编辑器工作区或未发布的工程缓存中。
- 发布谱面只保留规范化后的精确 BarTime。
- Mixer 的判定网格始终是从 Mixer 头开始的 `1/8 chart bar`，不受编辑器当前分割影响。

## 13. 不保存的派生字段

v2 正式谱面不保存：

```text
SubNoteId
Baked_Second
Baked_SyncNote
Baked_TotalMainNote
BPM 段起始 Seconds
编辑器网格和吸附分母
Note 级 track
```

派生规则：

- Note 秒数由 `audioOffsetSec + timing.bpms` 计算。
- 同刻多押由精确 BarTime 和 Note 类型计算。
- BPM 段起始秒由前段累计得到。
- Note 所属侧由 `notesLeft/notesCenter/notesRight` 得到。
- Hold 主判定数为 `1 + nodes.length`。
- Mixer 主判定数为 `floor((endTime - headTime) * 8) + 1`。
- `tap/drag/exTap/mine` 各产生一个主判定。
- `barLine` 不产生判定。

理论满分、主判定总数和各类统计必须由 loader/validator 从权威谱面数据重新计算，不接受
谱面作者提供的烘焙总数。

## 14. 基础校验

导入器至少应拒绝以下情况：

- 未知 `format` 或不支持的 `formatVersion`。
- `chartId` 与包内难度条目不一致。
- 重复的父 Note 或路径节点 ID。
- 非规范、有零分母或超出实现安全范围的 BarTime。
- BPM 非正数、非有限数值、未从 BarTime 0 开始或时间不递增。
- ScrollSpeed 非有限数值、同刻重复或时间不递增。
- Note 时间早于 BarTime 0。
- `center/width` 超出轨道范围；若最终保留左缘模型，则检查 `position/width` 范围。
- Hold/Mixer 没有节点，或节点时间不严格递增。
- 尾节点仍带有 `curveToNext`。
- Mixer 出现在中央轨。
- 未知 `type` 或未知 `curveToNext`。
- 路径字段指向包外文件，或包内没有可解析的最终音频。

Validator 应把能明确定位的问题报告为 `文件 + JSON 路径 + 原因`，不得静默改写会改变谱面
语义的数据。只有 BarTime 约分、字段排序等不改变语义的规范化操作可由编辑器自动完成。

## 15. 完整示例

以下示例仅展示 schema 组合，不代表最终曲线方案已经拍板：

```json
{
  "format": "dux-community-chart",
  "formatVersion": 2,
  "chartId": "tech",
  "audioOffsetSec": 0.137,
  "timing": {
    "bpms": [
      {
        "time": { "bar": 0, "numerator": 0, "denominator": 1 },
        "bpm": 150.0
      },
      {
        "time": { "bar": 16, "numerator": 1, "denominator": 5 },
        "bpm": 180.0
      }
    ]
  },
  "scrollSpeeds": [
    {
      "time": { "bar": 0, "numerator": 0, "denominator": 1 },
      "value": 1.0,
      "curveToNext": "linear"
    },
    {
      "time": { "bar": 8, "numerator": 0, "denominator": 1 },
      "value": 0.4,
      "curveToNext": "easeInOutSine"
    },
    {
      "time": { "bar": 9, "numerator": 0, "denominator": 1 },
      "value": -0.5,
      "curveToNext": "hold"
    },
    {
      "time": { "bar": 10, "numerator": 0, "denominator": 1 },
      "value": 1.0
    }
  ],
  "notesLeft": [
    {
      "id": "mixer-001",
      "type": "mixer",
      "time": { "bar": 8, "numerator": 1, "denominator": 7 },
      "center": 1.4,
      "width": 0.8,
      "curveToNext": "smooth",
      "nodes": [
        {
          "id": "mixer-001-node-1",
          "time": { "bar": 8, "numerator": 4, "denominator": 7 },
          "center": 3.3,
          "width": 0.6,
          "curveToNext": "smooth"
        },
        {
          "id": "mixer-001-node-2",
          "time": { "bar": 9, "numerator": 1, "denominator": 7 },
          "center": 2.5,
          "width": 1.0
        }
      ]
    }
  ],
  "notesCenter": [
    {
      "id": "tap-001",
      "type": "tap",
      "time": { "bar": 4, "numerator": 1, "denominator": 5 },
      "center": 2.5,
      "width": 1.0
    },
    {
      "id": "hold-001",
      "type": "hold",
      "time": { "bar": 6, "numerator": 0, "denominator": 1 },
      "center": 2.5,
      "width": 1.2,
      "curveToNext": "smooth",
      "nodes": [
        {
          "id": "hold-001-node-1",
          "time": { "bar": 6, "numerator": 1, "denominator": 4 },
          "center": 3.2,
          "width": 0.8,
          "curveToNext": "smooth"
        },
        {
          "id": "hold-001-node-2",
          "time": { "bar": 6, "numerator": 1, "denominator": 2 },
          "center": 2.8,
          "width": 1.4
        }
      ]
    }
  ],
  "notesRight": [
    {
      "id": "mine-001",
      "type": "mine",
      "time": { "bar": 12, "numerator": 3, "denominator": 11 },
      "center": 3.8,
      "width": 0.6
    }
  ]
}
```

## 16. 待确认问题

当前仍需明确拍板：

1. v2 是否正式把所有 Note 空间字段从左缘 `position` 改成 `center`。
2. `curveToNext` 是否只控制中心线，Width 是否固定采用无需配置的 PCHIP 平滑。
3. `smooth` 是否作为 Hold/Mixer 缺省曲线；若省略 `curveToNext`，究竟表示 `smooth` 还是
   `linear`。
4. 显式 `ease*` 与相邻 `smooth` 段相接时，是否允许速度不连续，还是需要自动匹配切线。
5. 是否需要限制 `level` 的数据范围，以及是否允许小数等级。
6. 是否允许同一个谱面文件被多个 `charts[]` 条目引用。
7. 差分音频是否需要除 `audioOffsetSec` 外再提供独立的音频结束点或裁剪区间。
