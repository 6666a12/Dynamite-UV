# Dynamite Universe Storyboard 与制谱器联动提案

> 状态：未实现设计提案，未冻结正式格式；2026-10-07 同步现有 shell 的能力边界。
> 当前没有 Storyboard 页面/模型/运行时播放器；现有编辑器状态见 [DynaMaker UV](<dyna-maker-uv.md>)。
>
> 本文用于在优化制谱器之前固定 Storyboard 的职责边界、编辑体验和未来运行时接口。
> 当前不修改客户端、shared、制谱器代码，也不改变 Chart Format v2。

## 1. 结论先行

Storyboard 应与制谱器一起推进，而不是先做一个 Godot 播放器，再事后补作者工具。

推荐的长期结构：

    制谱器 authoring model
            ↓ 共享确定性求值器
    编辑器预览 ─────────────→ Godot gameplay 播放
            ↓
    版本化 storyboard 数据 + 包内素材

制谱器应是 Storyboard 的唯一正式创作入口；Godot 只负责加载、按歌曲时间求值和渲染。
编辑器预览与游戏运行时必须使用同一套时间、插值和 easing 语义。

首版不追求兼容完整 osu! .osb，而是采用适合 Dynamite Universe 三面轨道、固定
1920×1080 舞台和移动端预算的受限二维舞台故事板。

## 2. 为什么要编辑器优先

运行时播放淡入、移动和缩放并不难；真正容易失控的是：

- 作者不知道对象位于 Note/HUD 的哪一层；
- 时间点在 BPM 变化后发生漂移；
- 编辑器预览和游戏实际画面不一致；
- 素材过大、对象过多，直到导出 APK 才发现性能问题；
- 视觉效果挡住判定线，却没有安全区域提示；
- 保存格式、素材路径和包发布流程互相脱节。

这些问题都应该在制谱器中被看见、编辑和拒绝，而不是留给运行时兜底。

先建立 editor-only 的可编辑模型，可以在不破坏 v2 的前提下验证关键帧、BarTime 网格、
对象树、Inspector 和资源预算。只有交互稳定后，才值得冻结正式 sidecar 格式并接入 Godot。

## 3. 现有项目约束

- docs/chart-format-v2.md 已冻结；未知字段必须报错，不能直接向 v2 chart 添加私有 storyboard 字段。
- 制谱器是 `editor/` 下独立的 Godot Windows export；Storyboard 不得使普通游戏项目或路由依赖编辑器工作区。
- 游玩舞台固定为 1920×1080，比例不符时保留黑边；Storyboard 使用同一设计坐标。
- SongPlayback/SongClock 是歌曲时钟来源；暂停、Seek 和固定帧验证必须可重复。
- GameplayMain 已分为舞台、Note 和 HUD 层；Storyboard 不得接触判定、输入、计分或 Gameplay Digest。
- 发布包只能包含包内安全路径和 clean-room 素材；编辑器外部路径只能存在于暂存导入阶段。
- shared/chart-editor-core 已有精确 BarTime、BPM/Scroll 数据模型、命令式 undo/redo、校验与 staging writer。当前 shell 已接包打开和基础画布命令，**未接完整 Scroll/属性 UI、保存发布、音频或 Storyboard**；应复用底层能力，但不能称现有 UI 已完成这些流程。

## 4. 目标与非目标

### 4.1 目标

- 让谱师可以在制谱器内创建、预览、修改和验证 Storyboard。
- 让预览和运行时使用同一套确定性求值逻辑。
- 让演出跟随歌曲时间，不因玩家判定偏移设置而漂移。
- 默认保护三面轨道、判定线和 HUD 的可读性。
- 将资源、数量、尺寸和闪烁风险在发布前检查。
- 允许关闭或减弱 Storyboard，而不改变玩法和成绩身份。

### 4.2 首版明确不做

- 任意 C#、GDScript、表达式或脚本回调。
- 自定义 Shader、3D 场景和视频。
- 根据 Perfect/Miss、连击或玩家行为分支。
- Storyboard 改变 Note、Scroll、Judge、输入或成绩。
- 完整复制 osu! 的 Fail/Pass、Trigger 和复杂嵌套 Loop 语法。
- 首版自定义字体文字；中文排版、字体打包和跨设备 fallback 应单独设计。

## 5. 时间与坐标模型

### 5.1 坐标

- 设计画布固定为 1920×1080。
- 原点、Sprite origin、位置和尺寸均以设计坐标记录。
- 编辑器只做视口缩放，不改变故事板坐标。
- 故事板预览应显示判定线、三轨安全区和 HUD 禁入区。

建议的安全区域：

    Backdrop     整个 1920×1080
    StageAccent  可以接近轨道，但默认不得遮挡 Note 主体
    Overlay      只能位于 Note 与 HUD 之间，并受 alpha/亮度/闪烁限制
    HUD 禁入区   顶部统计、暂停控件和底部曲目信息默认不可覆盖

### 5.2 时间

- 关键帧的主要时间类型为精确 BarTime，沿用现有编辑器的 canonical bar+n/d。
- 运行时通过该难度的 BPM 时间线转换为音频秒。
- Storyboard 使用实际 chart/audio time；不能使用包含玩家 timing offset 的判定时间。
- 暂停、恢复、Seek、重播和固定帧验证都必须直接按当前时间求值。
- 不允许通过上一帧状态累积 Tween 作为权威状态。

未来如果对白或自由演出确实需要绝对秒轨道，应作为明确的第二种时间类型加入格式，不能让作者在 BarTime 和浮点秒之间隐式切换。

## 6. 建议的数据模型

下面是概念模型，不是已冻结的 JSON 合同。

    StoryboardDocument
    ├─ chartId
    ├─ canvas (1920, 1080)
    ├─ objects[]
    └─ shared assets / resource references

    StoryboardObject
    ├─ id
    ├─ kind: sprite | frameAnimation | rect | mask
    ├─ layer: backdrop | stageAccent | overlay
    ├─ asset (仅适用于需要素材的 kind)
    ├─ origin
    ├─ base transform
    └─ property tracks[]

    PropertyTrack
    ├─ property: opacity | position | scale | rotation | color | visibility
    ├─ keyframes[]
    └─ interpolation/easing

    Keyframe
    ├─ time: ExactBarTime
    ├─ value
    └─ easingToNext

设计原则：对象 ID 稳定且与显示顺序无关；基础值和关键帧分开；每条属性轨道独立求值；
相同时间的关键帧必须被拒绝或明确规定覆盖规则；Loop 若加入，首版只允许有限、扁平区间，
不允许嵌套。

首版建议只保留 sprite、frameAnimation 和程序化 rect；mask 可以先作为编辑器预留类型。

## 7. 图层和可读性策略

| 层 | 默认顺序 | 用途 | 首版限制 |
| --- | --- | --- | --- |
| Backdrop | Note 后方 | 环境、曲绘、慢速色调和镜头感 | 可全屏，但避免高频闪烁 |
| StageAccent | Backdrop 之上、Note 之下 | 节拍脉冲、侧轨灯带、几何装饰 | 不得遮挡 Note 轮廓 |
| Overlay | Note 之上、HUD 之下 | 短促低透明度强调 | 限制 alpha、亮度和闪烁 |

首版不提供可覆盖 HUD 的 Storyboard 层，也不引入 osu! 的 Fail/Pass 层。

编辑器应提供：显示 Note、判定线、Mixer 装饰条和 HUD 禁入区；Storyboard only、Notes only、
Combined 三种预览；Overlay 越界警告；高 alpha、大面积 Additive、短周期闪烁警告；
Full/Reduced/Off 预览；播放时锁定 Storyboard 的开关。

## 8. 制谱器交互提案

未来新增独立 Storyboard 页面或工作区；当前 shell 没有可交互 Note Inspector，不应假设已有完整属性面板：

    左侧：固定 1920×1080 舞台预览
    右侧：对象树 + 当前对象 Inspector
    底部：共享 transport、scrubber 和 Storyboard 时间轨

对象树至少显示图层分组、对象名称和稳定 ID、可见/锁定开关、资源缩略图及动画轨道标记。
Inspector 至少编辑资源、层、origin、位置、尺寸、旋转、当前时间的属性值、关键帧新增/移动/复制/删除、easing 和预览层级。

时间轴沿用现有 Grid/Snap 和精确 BarTime 输入；支持对象级和属性轨道级折叠；拖动优先吸附，
修饰键临时关闭吸附；Inspector 输入和拖动都产生可撤销 command；播放循环区间默认为编辑器状态，
除非未来明确把它定义为正式演出 Loop。

推荐工作流：创建对象 → 导入或选择包内素材 → 选择层 → 在时间轴设置初始值和关键帧 → Combined 预览 → Reduced/Off 预览 → 运行校验 → 保存并发布。

## 9. 编辑器文档与发布包的分离

Storyboard authoring 状态不应和正式 v2 chart 混在一起。

编辑器状态可以包含画布缩放、滚动位置、对象树展开状态、当前选择、锁定/隐藏状态、预览循环区间、undo/redo 历史和临时资源索引；这些内容不进入发布包，也不进入 Gameplay Digest。

正式 Storyboard 只包含稳定对象和轨道数据、包内资源引用、可重复求值所需的时间/属性/easing、版本号和明确的画布/层语义。

正式格式应采用未来明确版本的 sidecar 或包扩展，而不是污染当前 v2 chart。可以考虑每个 chart 一个 charts/<chartId>.storyboard.json，但路径和版本号需要实现前单独冻结。

Storyboard 可以影响包 revision、资源缓存和内容校验，但不进入玩法成绩的 Gameplay Digest。

## 10. 资源与性能校验

编辑器在导入和发布前应检查：包内相对路径；拒绝 ..、盘符、URI、符号链接和路径碰撞；图片格式、解码尺寸、字节大小和纹理边长；帧数、帧率和总时长；同时活动对象数、全屏透明对象数和 Additive 面积；Overlay 最大 alpha、亮度和闪烁频率；缺失/未引用/重复资源；Storyboard 结束时间是否超出 resolved audio。

移动端预算首版可以先做警告，但明显的路径逃逸、解码危险和安全区违规应 fail closed。具体性能数值应通过编辑器预览和真实设备测试后再冻结。

## 11. 共享求值器和测试要求

建议在 shared 中放置与 UI 引擎无关的 Storyboard evaluator：输入 Storyboard 文档和精确 BarTime 或音频秒，输出对象当前可见状态、变换、颜色和层；不持有上一帧状态；相同输入产生完全相同的输出；明确处理关键帧边界、easing、循环和隐藏。

最低回归应覆盖：BPM 变化前后求值；关键帧恰好落在当前时间；Pause/Resume/Seek 后一致；Full/Reduced/Off 确定性降级；缺失、重复、越界和非法资源引用；编辑器预览快照与运行时快照一致。

## 12. 分阶段实施顺序

### Phase 0：编辑器基础闭环与模型试验（当前建议）

- 先补现有制谱器的保存/资源/真实音频闭环，再建立对象树、可交互 Inspector 与时间轴；当前状态标签/视觉游标不能替代这些能力。
- 抽象可复用的时间轨、精确 BarTime 输入、选择/锁定和 command。
- 只在 editor-only 内存模型中试验 Storyboard，不发布、不接入 Godot。

### Phase 1：Storyboard 编辑器原型

- 建立对象、图层、属性轨和关键帧模型。
- 支持静态 Sprite、Fade、Move、Scale、Rotate、Color。
- 支持 Combined/Storyboard only/Notes only 预览。
- 加入安全区、资源和基础性能警告。

### Phase 2：共享求值器与可保存草案

- 将插值/easing 从 UI 代码下沉到 shared。
- 制谱器可以保存草案 sidecar，但正式 v2 包仍不加载。
- 增加 seek、固定帧和编辑器/求值器一致性测试。

### Phase 3：正式 sidecar 与发布验证

- 冻结版本化 Storyboard 合同。
- 扩展 package writer、strict reopen、资源校验和 revision 语义。
- 明确缺失 Storyboard 时的兼容行为。

### Phase 4：Godot 播放器

- 在 stage、Note 层和 HUD 下方插入 Storyboard 渲染层。
- 按实际 chart/audio time 求值。
- 接入 Full/Reduced/Off 和移动端预算。
- 用编辑器导出的 golden Storyboard 做运行时回归。

### Phase 5：扩展能力

只有前几阶段稳定后再评估文字、帧动画、有限 Loop、遮罩、Additive 和共享资源图集。任何扩展都必须先有编辑器预览、运行时求值和资源预算。

## 13. 待拍板事项

1. Storyboard 正式 sidecar 是随 chart 存放，还是由 pack 级清单统一引用？
2. 是否允许多个 chart 共享 Storyboard，还是每个难度独立一份？
3. 首版图片格式和帧动画资源上限是多少？
4. Overlay 的最大 alpha、闪烁频率和可覆盖安全区如何定义？
5. 是否需要绝对音频秒轨道，还是长期只使用 BarTime？
6. 是否把 rect/mask 作为程序化对象，减少素材导入压力？
7. 文字能力是否单独做成第二阶段格式，而不是首版对象类型？
8. Reduced 模式是发布者预先指定轨道，还是运行时按对象属性自动降级？

在这些问题没有结论前，编辑器可以继续做 UI 和内存模型优化，但不应冻结正式 JSON。

## 14. 编辑器优先的完成标准

进入 Godot 播放器阶段前，应满足：作者可在制谱器内完成创建、移动、复制、删除和调整关键帧；修改可撤销/重做且 dirty 正确；BarTime、BPM 变化和 Seek 不会让关键帧漂移；Combined 预览显示 Note、判定线和 HUD 安全区；Reduced/Off 结果稳定；资源、路径和性能校验在发布前给出可定位诊断；同一 Storyboard 在编辑器快照和 shared evaluator 中一致；不修改当前 v2 未知字段规则，不影响现有 Gameplay Digest 和成绩。

## 15. 参考文档

- [Chart Format v2](<chart-format-v2.md>)：正式格式、未知字段与摘要边界。
- [运行时架构](<runtime-architecture.md>)：依赖方向和 gameplay 帧顺序。
- [当前交接](<handoff.md>) / [制谱器说明](<dyna-maker-uv.md>)：现有能力和真实缺口。
- [EditorDocument](<../tools/chart-editor-core/EditorDocument.cs>)：文档、精确时间与 undo/redo。
- [V2PackageWriter](<../shared/Chart/V2/V2PackageWriter.cs>)：staging、严格验证与发布底层。
- [历史原文](<archive/2026-10-07-cleanup/storyboard-editor-proposal.before.md>)：保留最初提案上下文，不作为实现证明。
