# 结构检查

从 `community` 运行 `python -B tools/architecture/check.py --report`。
完整非图形验证入口为 `python -B tools/verify.py`；依赖已还原时可加 `--no-restore`。

检查使用标准 Python 库，不启动 Godot，不安装依赖，不改源码。失败返回非零退出码。

- `policy.json` 固定九个 C# 项目的 ProjectReference、显式 Compile 源码链接和 PackageReference。
  特别保护 `shared` / chart-editor-core 的纯 C# 边界，以及两端对 presentation 的程序集引用。
  editor 已无 client 源码链接，禁止重新添加；`editor/shell` 只允许通过公开 NuGet 包使用 Avalonia/Dock/Ursa/Suki，不能依赖 `.tmp` 研究源码。
- 单个文件默认最多 1000 行（包括空行和注释）；场景入口、测试入口和命令接口有更低预算。
- 主要分部类按声明匹配累计全部文件行数，包括内部控件，预算以此次拆分后的存量向上取整。
  这只是抑制继续增长的检查，不能证明类内已经解耦；单个文件变短也不算减少类的职责。
- 核心目录额外拒绝 Godot/Game/Ui/Audio/Editor 的 using 声明。项目依赖和实际编译是主要边界；
  本脚本不是完整的 C# 语义分析器，也不替代构建、行为回归和设备验收。
- 第二轮增加渲染组件与浏览模型的依赖检查：渲染不能重新拿到 GameplayMain/判定引擎/全局会话，
  浏览模型不能直接读写 GameSession 或播放器。GameplayMain 总预算由 2900 收紧到 1850 行，
  新渲染器的分部类也计总量，独立会话/手势/浏览组件有单独预算。
- 第三轮增加公共视觉、HUD、音源、独立控件和 editor 页面边界。presentation 不能引用两端应用状态，
  HUD 读取值快照，控件的动效/落速用 provider 注入，页面组件不引用 shell。
  GameplayMain/SongSelect/CalibrationScreen/SettingsScreen/StandaloneEditorMain/AuthoringCanvas 的总量预算
  分别收紧为 1350/1150/1250/800/1250/2400 行；新增对象也有文件预算。没有以放宽预算完成拆分。
- 选择与创建请求拆分增加 CanvasSelectionState、EditorCreateRequest/Preflight 纯核心边界及对应回归；
  画布仍拥有 Godot 几何，shell 仍拥有文件查询、session 与页流。
- 命中与转场拆分增加 CanvasHitTesting、TrackHandoffPresentation/State；命中几何与转场路由仍由宿主拥有，
  TrackHandoffState 作为现有源码链接接入 core-tests，front 页面与独立放电 FX 不合并生命周期。
- 手势预览、选曲视口与设置编解码分别进入 CanvasGesturePreview、SongListViewport/ScrollState、GameSettingsCodec；
  纯状态回归接入现有 CLI。源码与两套测试均按功能族分组，场景/程序入口可留根，场景、测试链接与策略路径同步更新。
- client 续拆增加校准采样、卡片动画/详情请求和暂停覆盖层边界：纯模型链接进 core-tests；
  详情卡不得回读会话/宿主，暂停组件只通过回调触发输入清理、播放和导航，不能取得场景、判定或会话对象。
- 2026-10-30 旧制谱器 UI 删除后，旧页面依赖规则与 AuthoringCanvas/CreatePageOverlay 预算一并移除；
  空白 StandaloneEditorMain 保留 120 行入口预算，ChartEditor.Core 的纯 C# 边界与全部回归不变。

依赖或预算确需调整时，与具体模块拆分一并审查并更新 `docs/module-map.md`。
不要以重命名文件、增加分部文件或自动重写基线代替拆分；项目中的基线只保留显式审查后的版本。
