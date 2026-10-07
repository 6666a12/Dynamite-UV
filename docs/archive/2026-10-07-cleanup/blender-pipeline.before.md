# 评级资产 Blender 管线

## 当前状态（2026-10-02）

用户已选择中间的 **02「双刃折返」** 方向，并明确确认 **v5 很好、保留 v5 设计思路，
在表面附着一些电流**。此前“像碎屑”的反馈由用户撤回，不能继续据此削减 v5 的光效。
当前已生产 v5 + 表面电流的 S 与同方向 Ω/A/B/C，五级各 512×512 RGBA 静态图和
30 帧、24fps 透明入场序列，交付目录 `design/grades/v5_current/delivery/`。
S 的 v5 基底保持逐字节不变；新字形与附着电流交由用户视觉验收，客户端尚未接入。
保留三个 S 字原始方向草案作对照：

| 文件前缀 | 方向 | 构造 |
| --- | --- | --- |
| `01_armor` | 断层装甲 | 原创折线轮廓、三个斜向错位装甲块、青色嵌面、暗色脊骨 |
| `02_blades` | 双刃折返 | 两把相反方向的折刃、交错腰部、长尖角与凹槽 |
| `03_crystal` | 晶体切面 | 变宽原创 S 骨架、不等高晶脊、硬切三角面与小碎晶 |

全部直接建网格，无字体依赖，无原版资产依赖。原始方向稿不加辉光；最新 v5 使用整圈
刀刃倒角发光、外扩辉光与命中爆发。该 v5 方向已由用户选定；当前只在其上追加表面电流。

源脚本：`tools/grade-assets/render_concepts.py`。产物在 `design/grades/v4/`：每案包含
可编辑 `.blend`、1024×1024 RGBA 母版与 512×512 RGBA PNG；`comparison.png` 为带方向名的
联排图，含 128px 深浅底对照。`manifest.json` 记录尺寸、透明通道及非透明内容包围盒。
Penpot `Game UI` 页的新板 `Game — Grade Directions v4` 位于 `4240,2560`，仅放三个
大样与三个小样。此比较板不替代原结算板，不表示获准修改客户端 UI。

## 重建

在 community 根目录运行：

```powershell
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_concepts.py -- --samples 96
python tools/grade-assets/prepare_preview.py
```

可加 `--only 02_blades` 单独渲染。默认 Cycles，检测到 OptiX 设备则使用 GPU，否则回退
CPU；默认输出到 `design/grades/v4/`。运行会重建同名本轮产物；要保留一轮已有手工编辑，
先使用 `--output` 指向另一目录，预览脚本同步调整目录后再跑。

评级配色对应 Ω/S/A/B/C，动效沿用「蓄能 → 命中爆发 → 余晖」；最终常态帧与静态 PNG
逐像素一致。交付成品见下方「v5 + 表面电流」；不自动修改结算页或启动游戏。

## v4 光效样片（已反馈不够亮，保留作对照）

`design/grades/v4/motion_S/` 包含 S 的 30 帧、24fps 透明 PNG，亮峰在第 8 帧，白热
接触光只留第 8–9 帧，青/粉两道环分别在第 8/10 帧开始，之后衰减；第 24–30 帧已稳定。
常态仅沿两条刀刃保留薄青光，不循环呼吸，不在整个字面上罩白雾。

- `S_light_study.mp4` / `.webp`：深蓝黑底视频预览，额外含停留和间隔；不是帧序列长度。
- `lighting_comparison.png`：原始材质 / 常态薄青光 / 第 8 帧接触亮峰。
- `frames/S_0001.png` … `S_0030.png`：512×512 RGBA 成品样帧，普通 alpha 合成。
- `S_idle_glow.png`：与第 30 帧逐像素一致；`S_idle_clean.png` 保留无额外光层的主体。
- `fx_back/` + `body/S_composite_####.png` + `fx_front/`：按此顺序普通 alpha 合成，
  已含光强与入场明暗，便于游戏中分别调强度。`body/S_####.png` 为原始 Blender 帧。
- `motion.json`：24fps 时间线、精确投影后的发光刃线、层顺序与状态。

重建：先运行 `render_blade_motion.py`（Blender），再用普通 Python 运行
`compose_blade_fx.py`（依赖 Pillow、NumPy）。预览视频由 ffmpeg 从 `preview_frames/` 以
24fps 合成，84 帧。Penpot 新板 `Game — Grade Lighting v4` 在 `6360,2560`，由左至右为
原始材质 / 常态薄青光 / 第 10 帧双环，不改原结算板。

此版本只是资产与合成方案，未改客户端。将来接入时遵守 `ui-motion.md`：Full 只播放一次；
Reduced 用稳定图在 260ms 内淡入，不做位移/缩放/旋转/双环；Off 立即显示稳定图。

本轮验证：客户端构建 0 警告/0 错误，编辑器构建 0 错误（NuGet 漏洞索引不可达导致
2 条 NU1900 警告）；core-tests 与 chart-editor-tests 全部通过。资产文件另做尺寸、RGBA、
序列帧数与末帧一致性检查。游戏未启动，视觉验收仍由用户完成。

## v5 炫光增强样片（已选定基底）

路径：`design/grades/v5/motion_S/`。保留双刃 S 的网格、相机与运动节奏，调整如下：

- 整圈刀刃倒角改为真实发光材质，常态 emission strength 2.2，命中 18；两条长刃分别
  为 8.8 / 72。Cycles 同时渲染光照对金属表面的影响。
- 单独渲染可见发光区域 mask，使用 3/10/26px 多尺度青色辉光。完整保留 alpha，光晕
  不会被主体轮廓裁掉。512px 画布最外 12px 平滑回落，最外沿 alpha 为 0。
- 命中放射强光、青/粉双冲击环、两道电弧与 24 路火花；第 8–9 帧白热高峰，第 10–11
  帧仍有闪光尾部。最后停在明显发亮的常态，不强制增加循环动效。

重建：

```powershell
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_blade_motion.py -- --bright
python tools/grade-assets/compose_blade_fx_bright.py
```

`S_light_study.mp4` 为新样片，`before_after.mp4` 为与 v4 同步的并排对照；
`lighting_comparison.png` 为旧常态 / 新常态 / 新亮峰。仍交付 30 帧 24fps 512×512 RGBA，
`fx_back` → `body/S_composite_####.png` → `fx_front` 使用普通 alpha 合成，
`emission_mask` 保存 Blender 的可见发光遮罩。`S_idle_glow.png` 与第 30 帧完全一致。
`validation.json` 记录逐帧分层重合、尺寸/通道/帧数、透明边缘与末帧一致性检查。

Penpot 比较板 `Game — Grade Lighting v5` 位于 `8480,2560`，1920×1080，
由左至右为旧常态 / 新常态 / 新第 10 帧爆发。v4 板保留，不改结算板或客户端。
本轮客户端与编辑器构建均为 0 错误，编辑器 1 条 NU1900；两套测试全绿。

## v5 + 表面电流（当前交付）

`design/grades/v5_current/motion_S/`：在原 v5 的每一帧上，仅叠加独立的 `current/`
透明层。电流位于自绘刀刃的正面，沿凹槽与接缝流动，受原相机、原刀刃动画和实体遮挡
约束。原 v5 纹理的 SHA-256 收录在 `manifest.json`，已核对原文件未变。
`before_after.mp4` 为原版与附着电流的同步并排，`S_surface_current.mp4` 为单独预览。

S 另有 24 帧独立 `idle_current/` 可选循环，可在等级停住后继续让电荷沿表面移动；
它不重复砸入、爆闪和冲击环。此为用户新增表面电流的资产提案，游戏是否启用持续循环
仍在接入时决定，Reduced/Off 使用稳定图，不播放电流循环。

`design/grades/v5_current/set/`：Ω/A/B/C 的原创网格与分层渲染。Ω 为断开的冠形马蹄与
双刃基脚；A 为两根错层斜刃与横向锁片；B 为斜脊与两个多边形折返碗部；C 为错位的
上下钩刃。全部复用 v5 的材质、光效与充电/命中时间线。S 原本已被选定，因此直接复用
其已生成的帧，不重建字形。

运行文件集中在 `design/grades/v5_current/delivery/`：

- `static/{omega,S,A,B,C}.png`：五张 512×512 RGBA，保留原评级色。
- `animation/<grade>/0001.png` … `0030.png`：每级 30 帧、24fps，普通 alpha 混合。
- `all_grades.png`：五级联排与 128px 对照；`all_grades.mp4`：同步入场预览。
- `grades_v5_current.zip`：以上运行图片、说明、静态联排与校验清单；不含客户端修改。
- `manifest.json`：规格、颜色、静态 SHA-256、透明边缘、分层重合与末帧一致性验证。

重建：

```powershell
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_surface_current.py
python tools/grade-assets/compose_surface_current.py
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_grade_set.py
python tools/grade-assets/package_grade_set.py
ffmpeg -y -framerate 24 -i design/grades/v5_current/delivery/preview_frames/%04d.png -frames:v 90 -c:v libx264 -pix_fmt yuv420p -crf 17 design/grades/v5_current/delivery/all_grades.mp4
```

新四级源 `.blend`、主体、发光 mask、电流 mask 与各层帧在 `set/<grade>/`；S 电流源
`.blend` 在 `motion_S/`。`render_light_studies.py` 与 `design/grades/v6/` 是用户撤回
反馈前仅渲染 3 帧的未采用灯光试验，**不是当前方向，不继续沿用**。

Penpot：`Game — Grade Surface Current v5` 在 `10600,2560`（S 对照），
`Game — Grade Set v5 Current` 在 `0,3840`（五级总览），均为 1920×1080。
既有设计板保持不变。构建与两套 C# 测试通过，编辑器仍有 1 条 NU1900；美术验收由用户完成。

## Blender 5.2 注意事项

- 合成器使用 `scene.compositing_node_group`，不能使用已删除的 `scene.node_tree`。
  Glare 参数走输入 socket，枚举为 `Fog Glow` 等带空格字符串。
- 相机接近 -Z 视线时避免 `to_track_quat('-Z','Y')` 导致意外 roll。明确 Euler 倾角，
  将位置设在该旋转对应的光轴上；当前草案采用正交相机。
- Boolean 会合并材质槽。被切对象在求解前挂材质，之后清理多余槽并显式重设各面的
  `material_index`；对象级 scale 要先 apply。旧稿「洗白」曾与错材质槽有关。
  本轮用多边形裁剪和直接建网格，侧壁、正面、倒角分别指定材质。
- 字体归一化后正面深度随缩放变化；本轮不使用文字物体。
- `Action.fcurves` 已删除；发光强度动画可直接在 socket 上
  `keyframe_insert('default_value', frame=...)`。
- `LayerWeight Facing` 的方向容易误判，不能把它当作未经验证的边光开关。
- 保存的是标准 RGBA PNG。v5 光晕同时写入 RGB 与 alpha；独立发光 mask 使用 Standard
  色彩变换、零 exposure 和白色 emission，避免 AgX 压缩 mask。渲染 mask 后必须恢复
  原材质、色彩变换与采样设置。用透明边缘与分层重合检查避免光晕被 alpha 剪掉。
- **按 pass 手动切 `hide_render` 时不能同时给它保留可见性 F-curve**：渲染依赖图会重算
  F-curve，把只供白色遮罩用的电流曲线重新打开，污染 beauty pass。`render_grade_set.py`
  的曲线保留顶点动画，但清除对象级可见性动画，各 pass 独立设置可见性。
- `ffmpeg` 的 lavfi color 输入是无限流，合成预览必须指定 `-t` 或 overlay `shortest=1`。

只检查生成文件、透明通道和裁切边界，不代替用户接受美术效果。未获方向确认前不接入
客户端，不做 git commit。
