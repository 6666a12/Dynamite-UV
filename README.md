# DUX-Community

Dynamix 风格社区音游 —— clean-room 重实现（不使用任何原版素材）。
**接手先读 `docs/handoff.md`**（当前状态/待办/验证套路）；
玩法规格见 `docs/gameplay-spec.md`，UI 设计稿见 `docs/ui-mock/index.html`。

## 目录

- `client/` — Godot 4.7.1 (.NET/C#) 游戏工程（主菜单→选曲→游玩→结算闭环已通）
- `docs/` — 玩法规格书、交接文档、UI 样式稿
- `server/` —（阶段 3，未开工）ASP.NET Core 社区服务器
- `tools/` — 谱面打包（`pack_chart.py`）、统计、Frida、ComfyUI 等辅助脚本

## 环境

- Godot **4.7.1 .NET (mono) 版**（标准版无 C# 支持）
  - 本仓库 `../godot/Godot_v4.7.1-stable_mono_win64/` 已有一份可用副本
  - 官方下载：`Godot_v4.7.1-stable_mono_win64.zip`
- .NET SDK：本项目 TargetFramework 为 `net9.0`（Godot Android 导出要求 .NET 9+），本机 dotnet 10 SDK 可构建

## 构建 / 运行

```bash
# 仅编译 C#（CI 同款方式）
cd client && dotnet build

# 运行（先杀旧进程；不带场景参数进主菜单）
taskkill //F //IM Godot_v4.7.1-stable_mono_win64_console.exe //IM Godot_v4.7.1-stable_mono_win64.exe 2>/dev/null
../../godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path .

# 直接启动某场景调试（gameplay 无选曲参数时回退 testdata AUTO 演示）
..._console.exe --path . res://scenes/gameplay.tscn
```

截图/录屏/真机对比的完整套路见 `docs/handoff.md` §4。
