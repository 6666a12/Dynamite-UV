# 音符配色 v2 — 实现与验收

> 当前状态：2026-11-07 用户确认音符配色 v2 已完成。下文保留实现说明与历史自动验证；用户未逐项指定设备/平台，不据此补写具体设备测试结果。

## 入口与页面

设置 → EFFECTS → 音符配色。固定 1920×1080，不改玩法坐标。

- 主态：六类音符错落陈列、完整 Hold 头/条身/尾；右侧固定调色台。没有游玩场景、轨道、判定线。
- 调色：类型选择、SV/Hue、HEX、RGB 精确输入、原色/新色、最近五色（仅本次会话）、恢复当前颜色。
- 观察态：“可读性观察”进入同场景内的独立全屏状态。三种背景同尺寸比较、最近色类型对比/手动选择、真实计算的基础色对比度及 CIE76 ΔE。返回保留草稿。
- 类型图、样张与命中效果复用客户端材质，不使用设计图代替渲染。

## 草稿、预设、保存

- 进入时复制完整十槽配色库、当前槽与 Burst 跟随项；拖动只改变草稿。
- 应用才写 `user://settings.json`。与其他设置共存，保留已有设置 JSON 字段兼容。
- 20 步 undo/redo；一次 SV/Hue 拖拽或连续 HEX 输入为一步；净零变化不占历史、不清 redo。
- Ctrl/Cmd+Z 撤销，Ctrl/Cmd+Shift+Z 或 Ctrl/Cmd+Y 重做，Ctrl/Cmd+S 应用（输入框自己的文本撤销优先）。
- “按住对比原方案”使用进入此页时的原始快照；应用后该比较基准仍不变。松开、失焦、隐藏、切页和弹窗取消捕获。
- 原色小样取进入页时同槽颜色；新建槽没有原色时回退入页当前槽。
- 顶栏预设入口只列已占用槽与新建按钮。保留十槽容量，槽 1 可重命名/调色但不可清空。
- 新建/另存复制当前色到空槽；重命名 1–12 位大写 ASCII 字母、数字、空格、连字符，拒绝非法或重复名字。可打开历史重名库并修复。
- 清槽与恢复六色需确认；清当前槽选回槽 1；操作都可撤销。
- 有未应用修改时离开主态或换预设，选择“应用整个库草稿 / 放弃所有未应用修改 / 取消”。如果放弃导致目标新建预设消失，明确提示，不静默切换。
- 设置序列化完成后写同目录唯一临时文件，flush 后 rename/replace；失败时不接受草稿为已保存，回滚内存设置。此路径仅用于 globalize 后的 user://，不用于 res:// 虚拟资源。
- 这不是断电/文件系统崩溃的全面持久化保证；桌面失败回归不能代替 Android 存储专项。

## 可读性边界

- 深色基准 #05070F 与现有 NotePaletteMetrics 一致；#18212D 和 #35424F 是测试色，非场景采样。
- 对比度是基础色和背景的相对亮度，不包含透明条身、发光核心、全局亮度或动态叠加。
- 色差是 CIE76；最近类型按当前草稿实时排序。手动比较不修改颜色。
- 只提醒、不限制颜色，不显示通用 PASS，不冒充色觉障碍模拟或可读性认证。

## 代码职责

- `client/scripts/ui/NoteColorsScreen.cs`：草稿、导航、刷新、保存和比较协调。
- `NoteColorsScreen.Layout.cs`：两种全屏布局与观察数据刷新。
- `NoteColorsScreen.Dialogs.cs`：预设库、命名/RGB/确认弹窗。
- `NotePaletteDraft.cs`：Godot-free 不可变快照、历史和应用基准，核心测试直接链接。
- `NoteColorsWidgets.cs`：鼠标/触摸所有权、release-inside 点击、momentary 比较、SV/Hue 手势结束。
- `NotePaletteSpecimen.cs`：真实 NoteView、Hold 连接体和所选类型命中效果，隔离于游玩全局配色。
- `shared/IO/SettingsFilePersistence.cs`：可脱离 Godot 回归的阶段式设置文件写入（第三轮模块化迁入 shared）。

Public preset 和最终包门禁要求 `note_colors.tscn` 与 `note_material.gdshader`。2026-10-25 起 NotePalette/NoteMask 等共用视觉已进入 presentation，编辑器通过 ProjectReference 使用该程序集，不再源码链接 client；两端仍各自保有 shader/字体等资源。发布契约测试保护材质副本与依赖一致性。

## 2026-10-20 历史验证

以下为当时执行命令和结果；最近核对及历史警告边界统一见 [验证记录](<verification.md>)。从 community 根目录执行：

```powershell
dotnet build client/DynamiteUniverse.csproj --no-restore
dotnet run --project tools/core-tests/CoreTests.csproj --no-restore
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj --no-restore
dotnet build editor/DynaMakerUv.Editor.csproj --no-restore
python -B -m unittest discover -s tools/release/tests -p "test_*.py"
```

客户端 0 error，既有 SongSelect.cs:1223 CS8604 警告保留；编辑器 0 error/0 warning；core-tests 和 chart-editor-tests 通过。
新增配色草稿覆盖隔离、非法输入、手势、20步历史、分叉、应用/放弃/原基准、预设管理；设置文件回归覆盖首次保存、覆盖、编码失败、发布失败及临时文件清理。
发布测试最终 37 项全部通过（含新增空配色资源拒绝和编辑器材质依赖/副本一致性）；`git diff --check` 无空白错误。构建及测试均为桌面非图形验证，不代表 Godot shader 实机效果或 Android 触控通过。

## 历史实机验收清单

用户已整体确认完成，以下保留原核对范围，不再作为当前未完成任务；复验其他设备时可复用，未勾选不代表撤回用户确认。

- [ ] 首次进入，六类样张/材质与 Hold 条身都可见；字号、中文回退、边距无裁切。
- [ ] SV/Hue 拖动实时变化；在控件外松开、两指交替、窗口切走回来不会继续拖或产生大量撤销步。
- [ ] HEX/RGB 合法输入生效，非法输入不污染颜色；一次拖动可一次撤销，重做正确。
- [ ] 按住对比在主态/观察态均生效，松开和失焦恢复当前草稿。
- [ ] 观察态六类型、三背景、最近/手选比较均正确；返回保留草稿。
- [ ] 新建、重命名、恢复、清空、十槽满额；未应用换预设/离开三种选择可理解且无丢失。
- [ ] 应用后返回设置再进入颜色仍在；重启验证持久化。
- [ ] 测试命中随类型、Burst 跟随和 Full/Reduced/Off 设置变化；无整屏闪白。
- [ ] Android 横屏触控与软键盘、系统返回键；桌面键盘焦点不穿透模态。
- [ ] APK 导出后复验新增场景及材质资源。本轮未生成新 APK。
