# Dynamix Universe 谱面统计报告

- 谱面目录: `D:/Workspace/Dynamix/_rev/extracted/Charts`
- 谱面总数: 350

## 1. 难度号 → 难度名映射（文件名统计）

| 难度号 | 难度名 | 谱面数 |
| --- | --- | --- |
| 1 | Casual | 58 |
| 2 | Normal | 82 |
| 3 | Hard | 83 |
| 3 | Legacy | 1 |
| 4 | Mega | 67 |
| 5 | Giga | 30 |
| 6 | Tech | 20 |
| 8 | Legacy | 4 |
| 9 | Another | 1 |
| 12 | Tera | 2 |
| 16 | Tutorial | 2 |

难度号有多个名字: {3: Counter({'Hard': 83, 'Legacy': 1})}

已知集合(1,2,3,4,5,6,12,16)之外的难度号: [8, 9]


## 2. 音符数分布（按轨）

### NotesLeft
- 总音符数: 40620
- 每张谱 1 ~ 1199 个音符
- 分布（200 一档, 档内谱面数）:
  - [0, 200): 295
  - [200, 400): 52
  - [400, 600): 1
  - [1000, 1200): 2

### NotesCenter
- 总音符数: 223654
- 每张谱 1 ~ 2248 个音符
- 分布（200 一档, 档内谱面数）:
  - [0, 200): 34
  - [200, 400): 78
  - [400, 600): 62
  - [600, 800): 53
  - [800, 1000): 69
  - [1000, 1200): 28
  - [1200, 1400): 17
  - [1400, 1600): 7
  - [1800, 2000): 1
  - [2200, 2400): 1

### NotesRight
- 总音符数: 39173
- 每张谱 1 ~ 1066 个音符
- 分布（200 一档, 档内谱面数）:
  - [0, 200): 303
  - [200, 400): 46
  - [1000, 1200): 1

## 3. Baked_TotalMainNote 与 MainNote 公式

- `Baked_TotalMainNote == 三轨音符行总数` 的谱面: 46 / 350（即大多数谱面 TotalMainNote ≠ 音符行数）
- 统计发现公式: `MainNote = 链头数 + Type3数 + Type6数 - Type9数`
  - 链头 = Id 不被任何 SubNoteId 引用的音符
  - 语义解读（推测）: Chain(Type3)/Hold(Type6) 头计 2 次（头+尾各一次判定），MixerSub(Type9) 不计入主音符
- 公式吻合: 344 / 350
- 不吻合文件:
  - `75f188871f8d_Map_0004.4 - Chaotic_Reflexion [Mega].asset.json`: baked=898, 公式=897
  - `75f188871f8d_Map_9999.3 - Test [Hard].asset.json`: baked=427, 公式=482
  - `75f188871f8d_Map_9999.4 - Test [Mega].asset.json`: baked=550, 公式=626
  - `75f188871f8d_Map_TestDy1001.4 - Lacrimosa [Mega].asset.json`: baked=0, 公式=1194
  - `75f188871f8d_Map_TestDy1001.5 - Lacrimosa [Giga].asset.json`: baked=0, 公式=1539
  - `75f188871f8d_Map_TestDy1002.6 - Night of Verdigris [Tech].asset.json`: baked=0, 公式=25

## 3.1 难度 → JudgeSettings 资产引用（谱面 PPtr m_PathID 统计）

| 难度号 | JudgeSettings m_PathID | 谱面数 |
| --- | --- | --- |
| 1 | -4017723247194766367 | 58 |
| 2 | -1254297515179712648 | 82 |
| 3 | -7567386502407530553 | 84 |
| 4 | -7567386502407530553 | 67 |
| 5 | -7567386502407530553 | 30 |
| 6 | -7567386502407530553 | 19 |
| 6 | 0 | 1 |
| 8 | -7567386502407530553 | 4 |
| 9 | -7567386502407530553 | 1 |
| 12 | -7567386502407530553 | 2 |
| 16 | 5172049694144183429 | 2 |

已知 PathID 对应（AllAssets 实检）: -4017723247194766367=JudgeSettings_1 Casual, -1254297515179712648=JudgeSettings_2 Normal, -7567386502407530553=JudgeSettings_3 Hard, 5172049694144183429=JudgeSettings_16 Tutorial。
即: Casual→Casual, Normal→Normal, Tutorial→Tutorial, 其余全部难度(Hard/Mega/Giga/Tech/Tera/Legacy/Another)→Hard 档判定。

## 4. Type 取值（按轨频次）

| Type | Left | Center | Right |
| --- | --- | --- | --- |
| 1 | 14885 | 123002 | 18611 |
| 2 | 9458 | 39886 | 6247 |
| 3 | 1986 | 11192 | 2338 |
| 4 | 2859 | 20032 | 3168 |
| 5 | 758 | 9079 | 909 |
| 6 | 958 | 2440 | 811 |
| 7 | 6643 | 11646 | 4020 |
| 8 | 431 | 2308 | 372 |
| 9 | 2642 | 4069 | 2697 |

NotesLeft Type 集合: [1, 2, 3, 4, 5, 6, 7, 8, 9]

NotesCenter Type 集合: [1, 2, 3, 4, 5, 6, 7, 8, 9]

NotesRight Type 集合: [1, 2, 3, 4, 5, 6, 7, 8, 9]

## 5. Position / Width（按轨）

### NotesLeft
- Position 范围: [-1.7999999523162842, 4.699999809265137]
- Width 范围: [0.10000000149011612, 7.5]
- Position 最常见值 (Top10): -0.25×2763, 3.0×2406, 0.0×1835, 2.0×1544, 1.5×1277, 1.0×1116, 2.5×865, 3.5×864, 0.6×780, 3.2×778
- Width 最常见值 (Top10): 2.0×20307, 1.5×3627, 1.0×2681, 5.5×2646, 2.5×1387, 3.0×1086, 1.6×847, 1.8×739, 1.9×648, 1.7×631

### NotesCenter
- Position 范围: [-5.5, 5.5]
- Width 范围: [0.10000000149011612, 16.0]
- Position 最常见值 (Top10): 2.0×7012, 3.0×6525, 0.0×6421, 2.5×6357, 1.0×6135, 0.5×5907, 1.5×5775, 3.5×5328, 3.1×4511, -0.25×4387
- Width 最常见值 (Top10): 1.0×155513, 0.8×10206, 1.5×7839, 2.0×5688, 1.2×5325, 5.5×4107, 1.3×4037, 0.9×3386, 0.7×2941, 0.5×2719

### NotesRight
- Position 范围: [-1.7999999523162842, 5.300000190734863]
- Width 范围: [0.20000000298023224, 6.75]
- Position 最常见值 (Top10): -0.25×2805, 3.0×2559, 0.0×1706, 2.0×1381, 1.5×1369, 1.0×1227, 2.5×1029, 0.5×737, 2.6×614, 1.4×605
- Width 最常见值 (Top10): 2.0×18957, 1.5×3045, 5.5×2724, 1.0×2436, 2.5×1431, 3.0×1290, 1.7×737, 1.9×717, 1.8×711, 2.1×601

## 6. SubNoteId 分布

- 含 SubNote 的谱面: 343 / 350
- 带 SubNoteId(≠-1) 的音符: 48239
- 指向同轨音符: 48238
- 指向跨轨音符: 0
- 指向不存在 Id: 1
- 关系细分: {'同轨': 48238, '指向不存在的Id': 1}
- Id 跨轨重复（同一 Id 出现在多轨）的文件数: 0

### 6.1 SubNote 链的 Type 配对（源Type → 目标Type）

| 源Type | 目标Type | 次数 |
| --- | --- | --- |
| 2 | None | 1 |
| 2 | 2 | 1 |
| 3 | 4 | 15516 |
| 4 | 4 | 10485 |
| 6 | 7 | 4209 |
| 7 | 7 | 18027 |

结构: 3→4→4→…（滑链）与 6→7→7→…（长条）两条链型。

### 6.2 链长分布（按链头 Type，Top）

- 链头 Type 2: 共 2 条链, 最长 2, 常见链长: 1×1, 2×1
- 链头 Type 3: 共 15516 条链, 最长 513, 常见链长: 2×13640, 3×633, 4×357, 5×252, 6×196, 7×72, 8×71, 9×45
- 链头 Type 4: 共 30 条链, 最长 9, 常见链长: 2×10, 3×6, 4×3, 5×3, 6×3, 7×2, 8×2, 9×1
- 链头 Type 6: 共 4209 条链, 最长 128, 常见链长: 2×1213, 3×599, 5×481, 4×460, 6×319, 7×213, 9×183, 8×145
- 链头 Type 7: 共 71 条链, 最长 46, 常见链长: 2×2, 3×2, 4×2, 5×2, 6×2, 7×2, 8×2, 9×2

## 7. Baked_SyncNote 取值集合

{0: 275963, 1: 18055, 2: 4324, 3: 5105}

## 8. TimeEnd

- TimeEnd == 0 的谱面: 347 / 350 (99.1%)
- 非零样例: [('75f188871f8d_Map_0001.16 - 六億光年先の鳥 [Tutorial].asset.json', 69.5), ('75f188871f8d_Map_0014.16 - Star Cape [Tutorial].asset.json', 80.87000274658203), ('75f188871f8d_Map_9999.2 - Test [Normal].asset.json', 5.0)]

## 9. NoteSystem__DropSpeeds

- 非空谱面: 30 / 350 (8.6%)
- `75f188871f8d_Map_0005.6 - The Villager [Tech].asset.json`: `[{"BarTime": 42.0, "Value": 1.0, "get_type": {}}, {"BarTime": 42.125, "Value": 0.699999988079071, "get_type": {}}, {"BarTime": 50.0, "Value": 0.699999988079071, "get_type": {}}, {"BarTime": 58.0, "Value": 0.8999999761581421, "get_type": {}}, {"BarTime": 58.125, "Value": 1.149999976158142, "get_type": {}}, {"BarTime": 73.375, "Value": 1.149999976158142, "get_type": {}}, {"BarTime": 74.125, "Value":`
- `75f188871f8d_Map_0019.4 - Glassage [Mega].asset.json`: `[{"BarTime": 56.5, "Value": 1.0, "get_type": {}}, {"BarTime": 56.75, "Value": 1.149999976158142, "get_type": {}}, {"BarTime": 64.375, "Value": 1.149999976158142, "get_type": {}}, {"BarTime": 64.5, "Value": 1.0, "get_type": {}}]`
- `75f188871f8d_Map_0021.5 - Tablear [Giga].asset.json`: `[{"BarTime": 0.0, "Value": 1.0, "get_type": {}}, {"BarTime": 51.5, "Value": 1.0, "get_type": {}}, {"BarTime": 51.875, "Value": 1.0, "get_type": {}}, {"BarTime": 52.5, "Value": 0.30000001192092896, "get_type": {}}, {"BarTime": 60.0, "Value": 1.0, "get_type": {}}, {"BarTime": 66.875, "Value": 1.0, "get_type": {}}, {"BarTime": 67.0, "Value": 0.6000000238418579, "get_type": {}}, {"BarTime": 69.0, "Val`
- `75f188871f8d_Map_0034.4 - GhostAndGhost [Mega].asset.json`: `[{"BarTime": 0.0, "Value": 1.0, "get_type": {}}, {"BarTime": 24.874000549316406, "Value": 1.0, "get_type": {}}, {"BarTime": 24.875, "Value": 12.0, "get_type": {}}, {"BarTime": 25.1875, "Value": 1.0, "get_type": {}}, {"BarTime": 25.375, "Value": 0.800000011920929, "get_type": {}}, {"BarTime": 25.5625, "Value": 0.699999988079071, "get_type": {}}, {"BarTime": 25.75, "Value": 0.6000000238418579, "get_`
- `75f188871f8d_Map_0074.4 - Beyond the Eternity [Mega].asset.json`: `[{"BarTime": 50.0, "Value": 1.0, "get_type": {}}, {"BarTime": 50.125, "Value": 0.800000011920929, "get_type": {}}, {"BarTime": 53.25, "Value": 0.6499999761581421, "get_type": {}}, {"BarTime": 53.375, "Value": 1.0, "get_type": {}}]`

## 10. BPM 时间线

- 每谱 BakedBarSections 段数分布: 0段×3谱, 1段×335谱, 3段×4谱, 6段×2谱, 7段×2谱, 54段×4谱
- BPM 取值种数: 92
- BPM 最常见值 (Top15): 200.0×41, 170.0×28, 180.0×23, 175.0×22, 194.0×20, 155.0×20, 174.0×15, 195.0×15, 185.0×14, 140.0×14, 150.0×12, 160.0×11, 128.0×11, 192.0×9, 190.0×9

### Seconds 递推验证（相邻段: Δsec = Δbar × 240 / BPM，即每小节 4 拍）
- 验证的多段谱面: 347
- 失败数: 0

## 11. BarTime → Baked_Second 全量换算验证

公式: `sec = sec[i] + (bar - bar[i]) * 240 / BPM[i]`（i = 最后一个 BarTime ≤ bar 的段；240 = 60秒 × 4拍/小节）

- 验证音符总数: 302953
- 误差 < 11ms 的音符: 302953 (100.000%)
- 最大误差: 0.000510 秒
- 有误差超限的文件数: 0

## 12. 代表谱面抽样

### `75f188871f8d_Map_0076.12 - LAST Re;SØRT [Tera].asset.json`（12=Tera，总音符 2043）

BakedBarSections 前 3 段:
```json
[
 {
  "BPM": 140.0,
  "BarTime": 0.0,
  "BarTimeRangeStarted": 0.0,
  "Seconds": 0.0
 },
 {
  "BPM": 140.0,
  "BarTime": 1.0,
  "BarTimeRangeStarted": 1.0,
  "Seconds": 1.7142857313156128
 },
 {
  "BPM": 90.0,
  "BarTime": 2.0,
  "BarTimeRangeStarted": 2.0,
  "Seconds": 3.4285714626312256
 }
]
```
NotesLeft 前 3 个音符:
```json
[
 {
  "Baked_Second": 9.961999893188477,
  "Baked_SyncNote": 0,
  "BarTime": 5.5,
  "Id": 4735,
  "Position": -0.25,
  "SubNoteId": -1,
  "Type": 9,
  "Width": 5.5
 },
 {
  "Baked_Second": 11.161999702453613,
  "Baked_SyncNote": 0,
  "BarTime": 6.0,
  "Id": 4739,
  "Position": -0.25,
  "SubNoteId": -1,
  "Type": 9,
  "Width": 5.5
 },
 {
  "Baked_Second": 12.362000465393066,
  "Baked_SyncNote": 0,
  "BarTime": 6.5,
  "Id": 4740,
  "Position": -0.25,
  "SubNoteId": -1,
  "Type": 9,
  "Width": 5.5
 }
]
```
NotesCenter 前 3 个音符:
```json
[
 {
  "Baked_Second": 0.5360000133514404,
  "Baked_SyncNote": 0,
  "BarTime": 0.3125,
  "Id": 4449,
  "Position": 2.700000047683716,
  "SubNoteId": 4450,
  "Type": 3,
  "Width": 1.0
 },
 {
  "Baked_Second": 0.5360000133514404,
  "Baked_SyncNote": 0,
  "BarTime": 0.3125,
  "Id": 4429,
  "Position": 1.2999999523162842,
  "SubNoteId": 4430,
  "Type": 3,
  "Width": 1.0
 },
 {
  "Baked_Second": 0.5360000133514404,
  "Baked_SyncNote": 0,
  "BarTime": 0.3125,
  "Id": 4453,
  "Position": 0.0,
  "SubNoteId": 4454,
  "Type": 3,
  "Width": 1.0
 }
]
```
NotesRight 前 3 个音符:
```json
[
 {
  "Baked_Second": 9.961999893188477,
  "Baked_SyncNote": 0,
  "BarTime": 5.5,
  "Id": 4734,
  "Position": -0.25,
  "SubNoteId": -1,
  "Type": 9,
  "Width": 5.5
 },
 {
  "Baked_Second": 11.161999702453613,
  "Baked_SyncNote": 1,
  "BarTime": 6.0,
  "Id": 1738,
  "Position": 3.5500001907348633,
  "SubNoteId": 1740,
  "Type": 3,
  "Width": 1.5
 },
 {
  "Baked_Second": 11.161999702453613,
  "Baked_SyncNote": 1,
  "BarTime": 6.0,
  "Id": 1743,
  "Position": 0.5499999523162842,
  "SubNoteId": 1744,
  "Type": 3,
  "Width": 1.5
 }
]
```

### `75f188871f8d_Map_0005.6 - The Villager [Tech].asset.json`（6=Tech，总音符 2490）

BakedBarSections 前 3 段:
```json
[
 {
  "BPM": 110.5,
  "BarTime": 0.0,
  "BarTimeRangeStarted": 0.0,
  "Seconds": 0.0
 }
]
```
NotesLeft 前 3 个音符:
```json
[
 {
  "Baked_Second": 8.958999633789062,
  "Baked_SyncNote": 0,
  "BarTime": 4.125,
  "Id": 98,
  "Position": 1.6500000953674316,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 1.5
 },
 {
  "Baked_Second": 9.501999855041504,
  "Baked_SyncNote": 0,
  "BarTime": 4.375,
  "Id": 99,
  "Position": 1.6500000953674316,
  "SubNoteId": 100,
  "Type": 6,
  "Width": 1.5
 },
 {
  "Baked_Second": 9.637999534606934,
  "Baked_SyncNote": 0,
  "BarTime": 4.4375,
  "Id": 100,
  "Position": 0.6499999761581421,
  "SubNoteId": 101,
  "Type": 7,
  "Width": 1.5
 }
]
```
NotesCenter 前 3 个音符:
```json
[
 {
  "Baked_Second": 3.257999897003174,
  "Baked_SyncNote": 0,
  "BarTime": 1.5,
  "Id": 5,
  "Position": 1.4500000476837158,
  "SubNoteId": -1,
  "Type": 2,
  "Width": 0.30000001192092896
 },
 {
  "Baked_Second": 3.257999897003174,
  "Baked_SyncNote": 0,
  "BarTime": 1.5,
  "Id": 92,
  "Position": 2.9000000953674316,
  "SubNoteId": -1,
  "Type": 8,
  "Width": 1.0
 },
 {
  "Baked_Second": 3.438999891281128,
  "Baked_SyncNote": 0,
  "BarTime": 1.5833330154418945,
  "Id": 6,
  "Position": 1.4500000476837158,
  "SubNoteId": -1,
  "Type": 2,
  "Width": 0.30000001192092896
 }
]
```
NotesRight 前 3 个音符:
```json
[
 {
  "Baked_Second": 4.614999771118164,
  "Baked_SyncNote": 0,
  "BarTime": 2.125,
  "Id": 64,
  "Position": 1.8499999046325684,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 1.5
 },
 {
  "Baked_Second": 5.1579999923706055,
  "Baked_SyncNote": 0,
  "BarTime": 2.375,
  "Id": 65,
  "Position": 1.8499999046325684,
  "SubNoteId": 67,
  "Type": 6,
  "Width": 1.5
 },
 {
  "Baked_Second": 5.294000148773193,
  "Baked_SyncNote": 0,
  "BarTime": 2.4375,
  "Id": 67,
  "Position": 1.25,
  "SubNoteId": 68,
  "Type": 7,
  "Width": 1.5
 }
]
```

### `75f188871f8d_Map_0076.5 - LAST Re;SØRT [Giga].asset.json`（5=Giga，总音符 1977）

BakedBarSections 前 3 段:
```json
[
 {
  "BPM": 200.0,
  "BarTime": 0.0,
  "BarTimeRangeStarted": 0.0,
  "Seconds": 0.0
 },
 {
  "BPM": 210.0,
  "BarTime": 117.75,
  "BarTimeRangeStarted": 117.75,
  "Seconds": 141.3000030517578
 },
 {
  "BPM": 223.0,
  "BarTime": 118.75,
  "BarTimeRangeStarted": 118.75,
  "Seconds": 142.44285583496094
 }
]
```
NotesLeft 前 3 个音符:
```json
[
 {
  "Baked_Second": 6.0,
  "Baked_SyncNote": 1,
  "BarTime": 5.0,
  "Id": 1078,
  "Position": 2.4000000953674316,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 3.0999999046325684
 },
 {
  "Baked_Second": 6.150000095367432,
  "Baked_SyncNote": 1,
  "BarTime": 5.125,
  "Id": 1080,
  "Position": 2.4000000953674316,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 3.0999999046325684
 },
 {
  "Baked_Second": 6.300000190734863,
  "Baked_SyncNote": 1,
  "BarTime": 5.25,
  "Id": 1082,
  "Position": 2.4000000953674316,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 3.0999999046325684
 }
]
```
NotesCenter 前 3 个音符:
```json
[
 {
  "Baked_Second": 1.2000000476837158,
  "Baked_SyncNote": 0,
  "BarTime": 1.0,
  "Id": 1,
  "Position": 2.9000000953674316,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 1.0
 },
 {
  "Baked_Second": 1.2000000476837158,
  "Baked_SyncNote": 0,
  "BarTime": 1.0,
  "Id": 2,
  "Position": 0.6000000238418579,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 1.0
 },
 {
  "Baked_Second": 6.0,
  "Baked_SyncNote": 2,
  "BarTime": 5.0,
  "Id": 2911,
  "Position": 0.9500000476837158,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 3.0999999046325684
 }
]
```
NotesRight 前 3 个音符:
```json
[
 {
  "Baked_Second": 1.350000023841858,
  "Baked_SyncNote": 0,
  "BarTime": 1.125,
  "Id": 1276,
  "Position": 3.8499999046325684,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 1.9500000476837158
 },
 {
  "Baked_Second": 1.5,
  "Baked_SyncNote": 0,
  "BarTime": 1.25,
  "Id": 1277,
  "Position": 2.4499998092651367,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 1.9500000476837158
 },
 {
  "Baked_Second": 1.7999999523162842,
  "Baked_SyncNote": 0,
  "BarTime": 1.5,
  "Id": 1278,
  "Position": 2.3499999046325684,
  "SubNoteId": -1,
  "Type": 1,
  "Width": 1.9500000476837158
 }
]
```
