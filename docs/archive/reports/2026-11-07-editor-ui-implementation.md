# 2026-11-07 制谱器 UI 实施切片

> 历史记录，从原 UI 分层回归清单提取。当前覆盖见 [回归清单](../../editor-ui-regression.md)，实际执行见 [验证记录](../../verification.md)。用户2026-11-09确认这批制谱器功能整体满意；不外推所有设备/性能。

## 本轮实现范围

- RootDock 使用 Inline 自动隐藏预览，为工具面板保留布局空间，避免 Avalonia overlay 与原生 Godot 子窗口重叠。
- VIEW 恢复工具、时序、属性面板和原舞台；附加面板关闭后可重开。
- NativeStageHost 跟随祖先布局变化重新同步 HWND，而不只依赖自身 Bounds 改变。
- Dock 活动标题使用深色 header 画刷，活动/非活动切换保留可读标题；翻译下拉框使用实时选中项模板，切语言不靠重选刷新，Inspector 未应用选项不被重置。
- Auto 选择框读取实际 Note 本体的位置和宽度；命中残影不带选择框，Hold/Mixer 动态头结束回收后不留框，文档选择身份不因此清空。
- `WorkspaceVerification` 与 `WorkspaceDesktopVerification` 分别承载无窗口生命周期和真实指针路径；后者不能由前者替代。

Auto 播放预览现已接入共用游戏视觉。新增 Godot `--verify-auto` 覆盖三轨命中/残影、Mine 静默、Hold/Mixer、seek、暂停/PAN、重建和档位/镜像的对象状态；不是 GPU 像素、听感或完整真实窗口验收，也不扩大上表的 UI 覆盖范围。失败日志保留；最终执行结果集中更新到 `verification.md`。
