# 谱面结构统计摘要

> 本文是开发阶段对 350 份内部样本的聚合统计，只用于验证 loader、时间线和结构假设。
> 不包含原谱逐条数据，也不定义 Type 语义；当前权威语义见
> [`docs/gameplay-spec.md`](../docs/gameplay-spec.md)。

## 1. 样本范围

| 项目 | 数量 |
| --- | ---: |
| 谱面 | 350 |
| Left 音符行 | 40,620 |
| Center 音符行 | 223,654 |
| Right 音符行 | 39,173 |
| 总音符行 | 303,447 |

三轨均出现 Type 1–9。统计数据仅描述样本分布，不代表社区版必须复刻原始内容。

## 2. 难度与判定预设

样本中的难度号与名称分布：

| 难度号 | 名称 | 谱面数 |
| --- | --- | ---: |
| 1 | Casual | 58 |
| 2 | Normal | 82 |
| 3 | Hard / Legacy | 84 |
| 4 | Mega | 67 |
| 5 | Giga | 30 |
| 6 | Tech | 20 |
| 8 | Legacy | 4 |
| 9 | Another | 1 |
| 12 | Tera | 2 |
| 16 | Tutorial | 2 |

当前客户端采用的判定预设映射为：Casual → Casual、Normal → Normal、Tutorial →
Tutorial，其余难度 → Hard。

## 3. Type 频次

| Type | Left | Center | Right |
| --- | ---: | ---: | ---: |
| 1 | 14,885 | 123,002 | 18,611 |
| 2 | 9,458 | 39,886 | 6,247 |
| 3 | 1,986 | 11,192 | 2,338 |
| 4 | 2,859 | 20,032 | 3,168 |
| 5 | 758 | 9,079 | 909 |
| 6 | 958 | 2,440 | 811 |
| 7 | 6,643 | 11,646 | 4,020 |
| 8 | 431 | 2,308 | 372 |
| 9 | 2,642 | 4,069 | 2,697 |

## 4. 主判定计数

`Baked_TotalMainNote` 只有 46/350 份样本等于三轨音符行总数，因此不能直接用音符行数
替代主判定数。

以下结构计数在 344/350 份样本中吻合：

```text
MainNote = SubNote 链头数 + Type3 数 + Type6 数 - Type9 数
```

按当前权威映射解释：HoldHead（T3）和 MixerHead（T6）路径各包含头、尾两个主判定；
BarLine（T9）没有主判定。社区版仍以 `JudgePlan.HeadlineUnitCount` 与
`Baked_TotalMainNote` 的一致性检查为准，不把该统计公式写入运行时。

## 5. SubNoteId 结构

| 项目 | 统计 |
| --- | ---: |
| 含 SubNote 的谱面 | 343 / 350 |
| 非 `-1` 的 SubNoteId | 48,239 |
| 指向同轨音符 | 48,238 |
| 跨轨引用 | 0 |
| 悬空引用 | 1 |
| Id 跨轨重复的谱面 | 0 |

主路径形态为 Hold 的 `3 → 4 → 4...` 与 Mixer 的 `6 → 7 → 7...`。样本中另有两条
Type 2 异常链，loader 按通用悬空/环容错处理，不将其提升为类型规则。

## 6. Position 与 Width

| 轨道 | Position 范围 | Width 范围 |
| --- | --- | --- |
| Left | −1.8 .. 4.7 | 0.1 .. 7.5 |
| Center | −5.5 .. 5.5 | 0.1 .. 16.0 |
| Right | −1.8 .. 5.3 | 0.2 .. 6.75 |

这些范围用于验证 loader 不应擅自钳制谱面坐标；实际渲染与触摸规则见玩法规格。

## 7. BPM 与时间换算

BPM 段数分布为：0 段×3、1 段×335、3 段×4、6 段×2、7 段×2、54 段×4。
对有时间线的样本，以下递推全部通过：

```text
next.Seconds - current.Seconds
= (next.BarTime - current.BarTime) * 240 / current.BPM
```

共验证 302,953 个音符的 BarTime → Baked_Second 换算，最大误差 0.510ms，无样本超过
11ms。空时间线样本必须回退到 `Baked_Second`。

## 8. DropSpeeds 与 TimeEnd

- 30/350 份样本包含 DropSpeeds。
- 347/350 份样本的 `TimeEnd` 为 0，不能作为通用谱面结束时间。
- 当前运行时对相邻 DropSpeed 事件做 BarTime 线性插值；具体公式见玩法规格 §2.2。

本文不保留原谱文件名、逐条 Note、DropSpeed 数组或 JSON 样例。需要重新核验统计时，
应在不进入发布内容的开发环境中重新生成。
