# 结果前奏与正常结束条件

> 本文交接 BLEED 耗尽、正常游玩结束和结果标识前奏。当前代码行为、用户已确认但尚未实施的方案、静态设计候选分开记录；不要把候选图当成已接入功能。

## 1. 当前代码行为（已接入）

### 正常完成

`client/scripts/game/GameplayMain.cs` 当前在以下任一条件先成立时调用 `ShowResults()`：

- 当前音乐时间超过 `JudgePlan.Plan.EndTime + 2 秒`；
- `SongPlayback.HasNaturallyEnded` 检测到音频自然结束。

**当前是两者取早，不是取晚，也不检查所有 Note 是否已判定。** `Plan.EndTime` 取最后判定单元与持续音符尾部的最晚时间（`shared/Judge/JudgeEngine.cs`）。

`ShowResults()`（`client/scripts/game/gameplay/GameplayMain.Presentation.cs`）在当前判定状态上计算并保存成绩，然后显示游玩场景内的 `ResultScreen`。现有揭晓为 600ms 合屏、320ms 停顿、480ms 拉屏及后续信息入场，完整约 2650ms；它不是一次场景跳转。自然完成后音乐尾奏继续播放，离开或重开才停止。

### BLEED 耗尽

- 规则位于 `shared/Judge/BleedHealth.cs`：100 血；Standard 下 Miss −2、Perfect +0.2、Great/Good +0.05；Hardcore 下 Miss −4、Perfect +0.1、Great/Good 不回血。Hardcore 强制启用 BLEED。
- 普通判定经 `GameplayMain.ApplyUnit` 同步血量；Hold 头 Miss 与中途断触的 shared 批量结算也在客户端同步，Mine 不额外扣血。
- 血量耗尽后，当前帧判定和 Mixer 收尾完成，先 `_playback.Pause()`，再复用正常 `ShowResults()`。暂停是 2026-10-27 的修复；正常自然完成的尾奏不变。
- 当前没有专用失败结算页、结果前奏或 BLEED 血条 UI。失败仍复用正常结算页占位。

## 2. 用户已确认、尚未实施的方案

### 正常结束改为两者取晚

正常完成必须同时满足：

1. 已到达谱面结束时间 +2 秒；
2. 音频已自然播放结束。

目的：长音乐尾段中的 Storyboard 不应被谱末 +2 秒的结算屏风提前遮住。实现时分别锁存 `chartEndReached` 与 `audioEnded`，再判断二者都成立。**不能只写瞬时 `t > end && HasNaturallyEnded`**：音频自然结束后播放位置可能归零，谱末条件会因此无法再次成立。

当前 `HasNaturallyEnded` 在 fixed-frame 和部分手动回退时钟下不产生自然音频结束信号；修改时需要同步验证路径/测试时钟，不能让自动回归永远等不到结束。

若以后 Storyboard 在音频结束后仍有独立内容，再单独讨论第三个完成条件，本轮不预设。

### 结果前奏放在屏风转场之前

统一流程为“游玩结束 → 结果标识前奏 → 现有屏风转场 → 结算页”。

- 正常完成：两个结束条件都满足后计算一次本局结果，按结果显示 `ALL PERFECT`、`FULL COMBO` 等标识，再进入现有结算转场。AP 优先于 FC，与现有结算徽章口径一致。
- BLEED 失败：不等待谱面或音乐结束；耗尽当帧锁定判定、暂停音乐，显示 `STAGE FAILURE`，再进入现有结算转场。
- “正常完成后显示标识”指正常游玩结束条件满足后、结算页出现前，不是结算页展示完毕后再显示。
- 成绩只计算/保存一次；前奏和屏风复用同一个结果快照，不能在各阶段重复 `TryUpdate` 或改变判定数据。
- 专用失败结算页与 BLEED 血条仍是后续独立设计，不由前奏任务顺带实现。

音乐与音效本轮只保留接入点，不制作或接入资产。以后 Failure 可搭配倒塌音效与音乐淡出，成功也可配置标识音效；淡出不能直接复用当前立即停流的 `Pause()`，音效应与 Music 通道分离。

## 3. 最新静态候选（未接客户端）

最新目录：[`design/result-outcomes/expressive/`](../design/result-outcomes/expressive/)，包含六种结果：

- `all-perfect`
- `full-combo`
- `one-miss`
- `one-good`
- `double-one`
- `stage-failure`

每种均有独立透明 SVG、1600×400 透明 PNG 和 1920×1080 暗底 preview；[contact.png](../design/result-outcomes/expressive/contact.png) 是全套对照。`build_expressive.py` 可重复生成全部产物，SVG 为纯 path，无系统字体依赖；本机渲染器为 `rsvg-convert`。

最新设计方向：删除徽记、底线和统计字符，只保留字标；以笔画变形、字高、宽窄、倾角和基线错落形成个性。用户要求“更激进、不要保守字形、不要多余装饰”，这是当前最新候选，**动态效果尚未设计，用户尚未做最终视觉定稿**。

早期草案 `ap-fc/`、`judgements/`、`failure/` 与统一版 `family/` 仅作对照，不应覆盖或误当成最新版本。参考图 `D:/Dynamite app/cankao/Dynamite ui pics/StdEffect_full.png` 只用于理解上一代结果类型与变形思路，不直接复制其像素字资产。

`design/` 是被 Git 忽略的本地设计目录，不是发行资产；接入时需明确复制/生成到允许的客户端资源目录并更新资产来源与发布清单。生成脚本中的 rsvg 路径是本机配置，其他机器需调整，不能假定工具已安装。

## 4. 后续实施与回归边界

1. 先实现并测试正常结束的双锁存条件，再接入结果前奏；不要在前奏任务中改判定窗口、分数或成绩身份。
2. 正常结束回归：长音频在谱末 +2 秒后继续；音频先结束时等待谱末；音频结束导致时间归零后仍能完成；固定帧验证能正常收尾。
3. BLEED 回归：耗尽立即暂停并进入 Failure 前奏，不等待正常结束；前奏只触发一次，随后只进入一次结算。
4. 前奏结束后接现有 `ResultScreen`，保留跳过、输入防穿透及 Full/Reduced/Off 行为；前奏自身也需要明确三档动效策略。
5. 实机确认长音乐 Storyboard 尾段不被截断，以及失败/成功前奏的视觉与后续音效混音。静态检查不能替代这些验收。

最近 BLEED 实现与暂停修复的完整非图形验证记录见 [verification.md](<verification.md>)；本文所述未来方案尚未因此获得构建或实机验证。
