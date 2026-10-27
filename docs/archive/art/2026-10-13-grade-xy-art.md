# X / Ψ 评级美术扩展

> 归档说明（2026-10-26）：下文是原日期的阶段记录/未实施提案，不作为当前能力、待办、验收或排期。当前见 [交接](<../../handoff.md>) 与 [模块地图](<../../module-map.md>)。正文中的旧路径、计数和未完成项按当时背景解释；本次仅添加标识并修正 Markdown 相对链接。整理前字节原文见 [快照清单](<../2026-10-26-cleanup/manifest.json>)。

本轮为获选 v5 双刃折返风格补齐两个原创字形；交付独立保存在本地美术工作区
`design/grades/v5_current_xy/`，用户已批准接入（2026-10-14）。该目录与其它 `design/` 内容一样被 Git 忽略，
完整源文件和渲染序列需要单独备份。

## 设计

| 文件键 | 显示字形 | 用户指定区间 | 造型与色彩 |
| --- | --- | --- | --- |
| X | X | 97.00–98.99% | 前后错层的两条交叉斜刃；切角尖端、深色珐琅、紫罗兰 `#A78BFA` 发光 |
| Y | Ψ | 99.00–99.99% | 两侧折返刃围绕贯通中刃；三个向上尖叉、保留向下长柄，兰紫粉 `#E879F9` 发光 |

内部 `Y` 只是文件键，图形和预览标签都是希腊字母 **Ψ**，不画成拉丁 Y。
原版参考仅用于识别交叉斜刃与三叉轮廓；网格顶点为本轮独立设计，没有原版贴图、字体网格、
球形底板或青粉金属渐变贴图。运行时已加入同色系背景、评级文字及静图/图集，Y 对外显示为 Ψ。

采用既有 `render_grade_set.py` 的相机、Cycles 48 samples beauty、8 samples mask、
AgX 金属表现、正交微俯仰透视、30 帧入场关键帧，以及同一 `bright_effects` 多尺度辉光。
X 后刃电流被前刃实体遮挡；Ψ 双叉在中轴后方相接，中刃保持三叉字形辨识。
电流曲线单独渲染，beauty 中隐藏；发光/电流 mask 用 Standard、零 exposure。

## 重建（community 根目录）

```powershell
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_grade_set.py -- --grade missing
python -B tools/grade-assets/package_grade_set.py --grade missing
python -B tools/grade-assets/review_missing_grades.py
```

`--grade missing` 只处理 X 与 Y，默认输出 `v5_current_xy`。旧 `--grade all` 保持原先五级流程。
渲染器支持 `--grade X` / `--grade Y`、`--frames 30` 样片和 `--output <set目录>`；
合成器支持单档及 `--source <工作区>` / `--output <delivery目录>`，用于另存迭代。
仅渲染样片时不能直接执行要求完整 30 帧的交付合成。
预览脚本使用 Pillow / NumPy / ffmpeg 和 Windows 微软雅黑字体。

## 交付位置

- `set/X/X.blend`、`set/Y/Y.blend`：原创网格、材质、灯光、相机、装配入场和表面电流关键帧。
- `set/<key>/{body,emission_mask,current_mask}/`：每层 30 帧 Blender 输出。
- `set/<key>/{fx_back,body_composite,fx_front,current}/`：普通 Alpha 合成分层。
- `delivery/static/{X,Y}.png`：512×512 RGBA 静图，等于各档第 30 帧。
- `delivery/animation/{X,Y}/0001.png … 0030.png`：24fps / 1.25 秒透明入场，第 8 帧碰撞峰值。
- `delivery/all_grades.png`：两档大图与 128px 小图。
- `delivery/family_context.png`：现有 S → 新 X → 新 Ψ → 现有 Ω 的同屏风格对照。
- `delivery/intro_contact_sheet.png`：第 1/5/8/12/20/30 帧对照。
- `delivery/alpha_backgrounds.png`：深浅底透明边缘预览。
- `delivery/X_intro.webp`、`delivery/Y_intro.webp`：无损透明动图预览，30 帧总时长 1250ms。
- `delivery/X_Psi_preview.mp4`：24fps 联排预览，90 帧 / 3.75 秒（含末帧停留与结尾空场）。
- `delivery/grades_v5_current_xy.zip`：静图、动画 PNG、基本预览和交付说明。
- `delivery/manifest.json`、`delivery/review_checks.json`：格式、逐像素合成、透明边界、帧哈希与源文件哈希。

全层合成顺序：`fx_back → body_composite → fx_front → current`，全部普通 straight alpha。
自动检查与用户目测验收分开；设计已获准接入，游戏内显示仍待实机确认。

## 本轮实际检查（2026-10-13）

- Blender **5.2.0 LTS** 已实际渲染：两档各 30 帧 × beauty / emission mask / current mask，合计 180 张分层 PNG。
- 合成交付 60 帧全部为 512×512 RGBA；逐像素分层重合、透明画布边缘、静图 = 末帧检查通过。
- 60 帧 beauty 在合成边缘渐隐前均无主体触边裁切。
- 两个透明 WebP 解码确认各 30 帧、RGBA、1250ms；ffprobe 确认 MP4 为 768×480、24fps、90 帧、3.75 秒。
- 既有 `v5_current/` 和获选 `v5/motion_S/` 中 1,823 个文件与生成前 SHA-256 快照一致。
- 三个修改/新增 Python 脚本语法检查与定向 `git diff --check` 通过；Git 提示已有 CRLF 转换策略。
- 已查看静图联排、128px 小图、家族对照与关键帧联排；字形保持 X / Ψ 识别，待用户美术验收。

本轮无渲染工具阻碍；源工程与交付均在上述路径。客户端构建、核心规则测试和游戏实机验收不属于此次美术检查结论。
