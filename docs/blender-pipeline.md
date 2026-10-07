# 评级资产 Blender 管线

> 当前状态核对：2026-10-07。采用 v5 + 表面电流五级评级，已于 2026-10-03 接入结算 v2；实机观感仍待用户验收。
> 方向比较、v4/v5 样片过程和历史构建结果见 [整理前原文](<archive/2026-10-07-cleanup/blender-pipeline.before.md>)。本文不宣称本次重新渲染或运行测试。
> 入库策略：整个 `design/` 是 Git 忽略的本地美术工作区；源工程、交付序列和下列本地设计链接不随克隆取得。客户端最终 PNG/SVG、生成脚本和发布来源清单仍入库，普通构建/导出使用这些已有运行资源；完整重建前须另行备份并恢复本地设计输入。

## 1. 当前采用版本与边界

- 用户选定原创 **双刃折返**、认可 v5 炫光，并要求附着表面电流；已撤回的“像碎屑”反馈不能作为削减光效依据。
- 五级 Ω/S/A/B/C 各有 512×512 RGBA 静态图、30 帧 24fps 透明入场。S 复用获选 v5 基底，只叠加电流；其余四级扩展同一材质/光效语言。
- 全部直接建原创网格，不依赖字体或原版游戏资产。设计源文件、分层渲染、视频、ZIP 不整体进入客户端；只打包批准的 PNG/SVG。
- 当前 [结算 v2](<result-v2-design.md>) 使用静态 PNG 和图集，不实时加载 Blender 3D 字形。Full 播放入场后约 7fps 往返末尾帧电流；Reduced/Off 只用静态评级。

## 2. 源文件、交付与运行时

| 位置 | 内容/用途 |
| --- | --- |
| [v5_current/motion_S](<../design/grades/v5_current/motion_S/>) | S 的电流源工程/分层合成；基底来自获选 v5 |
| [v5_current/set](<../design/grades/v5_current/set/>) | Ω/A/B/C 原创网格、主体、发光/电流 mask 和分层帧 |
| [v5_current/delivery](<../design/grades/v5_current/delivery/>) | 五张静图、五组序列、联排/动画预览、ZIP 和校验清单 |
| [client/assets/results](<../client/assets/results/>) | 游戏实际 PNG/SVG；grades 下为静图与 6×5 图集 |
| [runtime-assets.json](<../design/result-v2/runtime-assets.json>) | 运行时图集/静图生成清单 |
| [asset-provenance.json](<../release/asset-provenance.json>) | 公开资产来源与 SHA-256 |

交付目录的 `static/{omega,S,A,B,C}.png` 与各 `animation/<grade>/0030.png` 逐像素一致。
每级 30 帧无损拼成 **3072×2560** 图集，单帧 512×512，6 列×5 行。
游戏仅异步加载当前评级，未就绪时延长不透明屏风闭合等待，失败回退静图。
S 的独立 `idle_current/` 是可选源序列，**当前 runtime 并未使用这一单独序列**，使用的是入场图集末尾往返帧。

## 3. 重建当前交付

以下从 community 根目录运行；调整 Blender 路径为本机安装，不搜索/重建历史版本来替代当前版本：

```powershell
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_surface_current.py
python -B tools/grade-assets/compose_surface_current.py
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_grade_set.py
python -B tools/grade-assets/package_grade_set.py
```

这些脚本会覆盖相应生成目录；有手工改动时先保存副本或使用脚本支持的其他输出目录。
打包使用已有五级交付，并抽取结算 Penpot vectors：

```powershell
python -B tools/grade-assets/build_runtime_results.py
```

**注意：运行时打包不只是复制图片。** [打包脚本](<../tools/grade-assets/build_runtime_results.py>) 会写 runtime PNG/SVG、运行时清单，同时更新 [Public 导出白名单](<../client/export_presets.cfg>) 和 [资产来源清单](<../release/asset-provenance.json>)。执行前检查这些文件的现有改动，完成后审阅 diff，不能覆盖无关改动。
脚本逐帧回切图集验证 RGBA 完全一致，并校验静图/末帧相同。

可选联排视频（仅设计预览，不发布）：

```powershell
ffmpeg -y -framerate 24 -i design/grades/v5_current/delivery/preview_frames/%04d.png -frames:v 90 -c:v libx264 -pix_fmt yuv420p -crf 17 design/grades/v5_current/delivery/all_grades.mp4
```

## 4. 必须检查

- 五级静图/每帧尺寸与 RGBA 通道、30 帧/24fps、画布透明边缘和非透明内容边界。
- `fx_back` → `body/S_composite_####.png` → `fx_front` 普通 alpha 合成与最终帧重合；表面电流受实体遮挡，不能污染 beauty pass。
- v5 的发光区域采用 mask 与多尺度辉光，RGB 和 alpha 都保留；没有因 alpha 裁切造成硬边。
- S 基底哈希保持不变；图集回切、末帧/静图一致；生成器及运行时资产哈希更新一致。
- Full/Reduced/Off 语义与 [动效规格](<ui-motion.md>) 相同；美术接受由用户完成，自动校验不等同于视觉验收。
- Public 最终检查仍按 [发布流程](<releasing.md>)，不能以源图片合规代替 APK 检查。

## 5. 保留的历史参考

| 目录/板组 | 状态 |
| --- | --- |
| [v4](<../design/grades/v4/>)：装甲/双刃/晶体方向、薄光样片 | 历史比较，不是当前 runtime 来源 |
| [v5/motion_S](<../design/grades/v5/motion_S/>)：炫光 S | 获选基底保留，当前在其上叠电流 |
| [v6](<../design/grades/v6/>)：三帧灯光试验 | 未采用，不继续沿用 |
| Penpot Grade Directions/Lighting v4/v5 | 对照板，不自动替换当前结算 |
| Penpot Grade Surface Current / Grade Set v5 Current | 当前资产方向参考 |

历史重建命令及阶段验证见 [归档](<archive/README.md>)。本轮只整理文档，不移动或删掉这些资产及脚本。

## 6. Blender 5.2 安全约定

- 合成器用 `scene.compositing_node_group`，不是已删除的 `scene.node_tree`；Glare 参数走输入 socket，枚举如 `Fog Glow`。
- 相机近 -Z 视线时避免 `to_track_quat('-Z','Y')` 意外 roll；明确 Euler 倾角及光轴位置。
- Boolean 会合并材质槽。预先挂材质，事后清理多余槽并显式重设 `material_index`；先 apply 对象 scale。当前以多边形裁剪/直接建网格分别赋侧壁、正面、倒角材质。
- 不使用字体物体；`Action.fcurves` 已删除，发光强度可直接在 socket 上 `keyframe_insert('default_value', frame=...)`。
- 不把 `LayerWeight Facing` 当作未经确认的边光开关。
- 发光 mask 用 Standard、零 exposure、白色 emission，避免 AgX 压缩；渲染后恢复材质、色彩变换和采样设置。
- 手动切 `hide_render` 的 pass 不保留对应可见性 F-curve，否则依赖图重算可能打开电流遮罩物体污染 beauty；保留顶点动画、各 pass 独立设可见性。
- ffmpeg lavfi color 是无限流，合成预览须指定 `-t` 或 overlay `shortest=1`。

未获方向确认不擅自接入新资产，不替用户接受美术效果，不自动 git commit。
