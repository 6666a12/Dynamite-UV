# DUX-Community 交接文档（给后续 agent）

> 最后更新：2026-08-06。读这一篇即可接手；细节规格见 `gameplay-spec.md`、
> `pixel-calibration.md`、`video-geometry-analysis.md`，UI 设计稿见 `ui-mock/index.html`。

## 1. 项目目标与硬约束

- 基于 Dynamix Universe 逆向结果做 **clean-room 社区版**：公开版 **0 原版内容**，
  谱面几乎全自制；现有两份开发用谱面包（tablear / the_villager）仅 dev 用，不发布。
- 难度体系沿用原版 5 档（CASUAL/NORMAL/HARD/MEGA/GIGA）。
- UI 风格模仿 UV 但更简洁（社区风评 UV 不如前代 Dynamix）；四屏样式稿已获用户批准，
  见 `docs/ui-mock/index.html`（Orbitron + 切角按钮 + 霓虹斜线背景）。
- **编辑器不集成进游戏**：先网页版，再本地 app（尚未开工）。
- 用户偏好：中文交流、说话直接；**改完代码不用启动游戏/跑测试验证，用户自己跑看效果**
  （需要截图时才按 §4 套路抓）；额度有限，避免不必要的子代理和大动作。

## 2. 当前状态（已验证 ✅ / 待办 ⬜）

- ✅ 渲染模型：Hold/Mixer 为**刚性形状**（整体对着判定线下落，节点只塑形），
  命中端裁剪吞噬（与原版"连续吞噬"一致，用户确认）。
- ✅ 游玩几何已对齐**手机原生 2340×1080** 截图实测（底线/Mixer/行程），
  侧线按用户偏好拍板（比实测更贴边）：
  底线 y=955（88.5% 屏高）、**侧线 x=280/2060（≈12%，用户指定；手机实测是 421/1942）**、
  行程 790px / lead 0.77s（顶部 y≈165 生成）、Mixer 粉条 y=655、
  侧轨纵向 SidePosUnitPx=**170**（205 时 P=0 落 y=50 顶部溢出；现 P∈[0,4]→y∈[120,800]）。
  常量在 `client/scripts/game/GameplayMain.cs` 顶部，注释含依据。
  ⚠️ 858/268/2072（iPad 1440 视频）和 425/1915（手机实测）都**不要回退**，现值是用户拍板。
- ✅ 闭环：主菜单 → 选曲（难度点击循环切换+最佳成绩）→ 游玩（Esc/按钮暂停、
  顶部实时判定计数、F1 Auto）→ 结算（封面压暗背景、评级 Ω/S/A/B/C、NEW RECORD、
  成绩写 user://scores.json）→ 返回/下一首/再来一次。
- ✅ 结算不进 bug 已修：音频停后时钟冻结，现检测 `!Playing && t>1` 也触发
  （`SongClock.ManualFallback`）。headless 验证 t=170s 正常出 RESULT。
- ✅ `tools/pack_chart.py` 谱面打包（可重复执行合并难度）；`shared/` 判定引擎单测全过。
- ⬜ 结算屏**视觉效果未截图验证**（逻辑已验证；用 F12 跳谱尾快速验证）。
- ⬜ **分辨率/比例适配**：现全是 2340×1080 硬编码像素，换比例会崩。已提议方案
  （用户尚未拍板）：归一化 `PlayfieldLayout` 类——常量改占屏比例
  （底线 0.885H、侧线 0.12W/0.88W、P 映射 0.2645W+0.1179W·P、速度用 H/s、lead 0.77s 不变），
  宽高比插值两个实测锚点（21:9 底线 88.5% / 4:3 79.4%；P 扩散 0.118W / 0.142W），
  HUD 改锚点，stretch=canvas_items+expand 监听 resize 重算。
- ⬜ 侧轨 y 精确公式依赖 Frida dump il2cpp——子代理跑过 2h 超时无产出，**已搁置**，等用户定夺。
- ⬜ 素材管线（ComfyUI 生成按钮/音符/特效贴图，参考 `tools/style_refs.json`、
  `tools/comfy_gen.py`）未开工；用户说"可以往后放"。
- ⬜ 谱面编辑器（网页版优先）、社区服务器（阶段 3，ASP.NET Core）未开工。

## 3. 目录地图

- `client/` — Godot 4.7.1 mono 工程（主场景 `scenes/main.tscn`）
  - `scripts/game/GameplayMain.cs` — 游玩场景本体（布局常量在文件顶部，勿乱改）
  - `scripts/game/{ChartPack,GameSession,ScoreStore,Res,NoteView}.cs`
  - `scripts/audio/SongClock.cs`、`scripts/ui/*`（UiFonts/CutPanel/CutButton/SongRow/NeonBackground/SongSelect）
  - `testdata/packs/{tablear,the_villager}/` — meta.json + chart_<diff>.json + wav + 封面
- `shared/` — 纯 C# 谱面/判定核心（`tools/core-tests` 跑它的单测）
- `tools/` — `pack_chart.py` 打包、`chart_stats.py` 统计、`frida/`、`comfy_gen.py` 等

## 4. 运行 / 验证套路（Windows + Git Bash，都已踩过坑）

```bash
# 0. 先杀旧 Godot 进程（用户要求每次启动前必做，残留窗口会污染截图）
taskkill //F //IM Godot_v4.7.1-stable_mono_win64_console.exe \
         //IM Godot_v4.7.1-stable_mono_win64.exe 2>/dev/null

# 1. 编译（须 0 错误）
cd community/client && dotnet build

# 2. 启动（同一 Bash 调用内完成 启动+截图+kill，跨调用后台进程不可靠；
#    不带场景参数进主菜单，带 gameplay 场景且无 GameSession 时回退 testdata AUTO 演示）
../../godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
  --path . --position 0,0 --resolution 1707x1067 --borderless --always-on-top \
  res://scenes/gameplay.tscn > /tmp/g.log 2>&1 &

# 3. 截图（-frames:v 必须在 -i 之后；游戏画面实际渲染在窗口上部 1707x775）
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1707x1067 \
  -i desktop -frames:v 1 /tmp/duxshots/x.png

# 4. 录屏（yuv420p 要求偶数尺寸，必须加 scale）
ffmpeg -y -f gdigrab -offset_x 0 -offset_y 0 -video_size 1707x775 -framerate 30 \
  -i desktop -t 45 -vf "scale=trunc(iw/2)*2:trunc(ih/2)*2" \
  -c:v libx264 -pix_fmt yuv420p -preset veryfast /tmp/duxshots/demo.mp4
```

- ReadMediaFile **读不了 /tmp**（映射到 D:\tmp）：先 `cp` 到仓库内 `tmp_shots/` 再读。
- 游玩内调试键：**F1** 切 Auto、**Esc** 暂停、**F12** 跳到谱尾前 3s（验结算屏用）。
- 原版参考素材：手机原生截图 `comfyui_dl/play*.png`（2340×1080 真机）；
  录屏在 `gameplay_videos/`（注意区分手机录屏和 1440 iPad 谱面确认视频）。

## 5. 环境

- Godot 4.7.1 **mono** 版在 `godot/Godot_v4.7.1-stable_mono_win64/`（用户另有
  `Z:\Godot_v4.7.1-stable_win64.exe`，标准版无 C#，别用）。
- dotnet SDK 本机已装（net9.0 target，dotnet 10 SDK 可构建）。
- ComfyUI 便携版在 `comfyui/ComfyUI_windows_portable/`（:8188，可能还在后台跑）。
- AVD / Android Studio 可用（此前用于抓真机截图）；Frida 环境在 `_rev/frida` 等。
