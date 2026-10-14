# D 评级美术扩展

本轮补齐原创 D 字形，沿用已获选 v5 金属双刃、分层炫光与表面电流管线。
输出独立保存在 `design/grades/v5_current_d/`，用户已批准并接入静图、图集与背景（2026-10-14）。
预览只标 **D**；本专题不定义或修改 D 分数阈值。

## 设计

- 直立左脊配一条切角折返外刃，形成单个大内腔；与 B 的双腔、C 的开口轮廓区分。
- 保留倒角亮边、深色金属侧壁、内嵌珐琅和沿刃表面电流；轮廓全部由原创网格生成。
- 低饱和钢蓝灰 **`#697F91`**，以现有 C/B 为低档家族参照。
- 采用同一相机、Cycles 48 samples beauty / 8 samples mask、AgX、30 帧装配入场和第 8 帧碰撞峰值。
- 为低档控制强度：相对通用 v5 参数，主体发光 ×0.55，电流半径 ×0.72，前后 FX Alpha ×0.55，电流 Alpha ×0.70。
- 透明底不含球形背景；字体只用于预览文字，不参与图标网格。

## 重建

从 `community` 根目录：

```powershell
& 'Z:\blender\blender.exe' --background --python tools/grade-assets/render_grade_set.py -- --grade D
python -B tools/grade-assets/package_grade_set.py --grade D
python -B tools/grade-assets/review_missing_grades.py --grade D
```

`--grade D` 默认仅写 `v5_current_d`。原 `--grade all` 和 X/Ψ 的 `--grade missing` 保持原范围。
脚本的 `--output` / `--source` 可用于另存迭代；预览依赖 Pillow、NumPy、ffmpeg 和 Windows 微软雅黑。

## 交付路径

以下相对 `community/design/grades/v5_current_d/`：

| 内容 | 路径 |
| --- | --- |
| Blender 源工程 | `set/D/D.blend` |
| 静图，512×512 RGBA | `delivery/static/D.png` |
| 透明入场，30 帧 / 24fps / 1.25 秒 | `delivery/animation/D/0001.png … 0030.png` |
| 无损透明动图预览 | `delivery/D_intro.webp` |
| 视频预览 | `delivery/D_preview.mp4` |
| B/C/D 家族大小图对照 | `delivery/family_context.png` |
| D 大小图 | `delivery/all_grades.png` |
| 入场关键帧联排 | `delivery/intro_contact_sheet.png` |
| 深浅背景透明效果 | `delivery/alpha_backgrounds.png` |
| PNG 交付包 | `delivery/grades_v5_current_d.zip` |
| 自动检查与哈希 | `delivery/manifest.json`、`delivery/review_checks.json` |

`set/D/` 保留 Blender 三层输出 `body / emission_mask / current_mask`，以及合成四层
`fx_back / body_composite / fx_front / current`，每层 30 帧。
合成顺序为上述四层顺序，使用普通 straight alpha；电流 mask 受实体深度遮挡，beauty 隐藏电流遮罩物体。

整个 `design/` 被 Git 忽略，源工程与交付需单独备份。美术已获准接入，客户端游戏内效果仍待实机确认。

## 实际验证（2026-10-13）

- Blender **5.2.0 LTS** 实际完成 30 帧 × 3 层，共 90 张分层渲染 PNG，并保存 `D.blend`。
- 合成 30 帧全部为 512×512 RGBA；逐像素四层重合、零画布边缘 Alpha、静图等于第 30 帧检查通过。
- 30 帧 beauty 在后期渐隐前均无主体触边裁切。
- 无损透明 WebP 解码确认 30 帧、512×512 RGBA、总时长 1250ms。
- ffprobe 确认视频为 384×480、24fps、90 帧、3.75 秒（含末帧停留和结束空场）。
- 原五级、获选 S 基底和此前 X/Ψ 目录共 **2,414 个文件**与制作前 SHA-256 快照完全一致。
- 已查看静图大小图、B/C/D 同屏对照与入场关键帧；预览不标分数阈值。
- 技术强度对照保存在 `delivery/strength_comparison.json`：同尺寸图像的 `sRGB luma × Alpha` 总和，
  D 静态约 32,327（C 40,793 / B 52,705），碰撞帧约 50,019（C 63,592 / B 70,570）。
  该指标仅辅助确认强度，没有替代美术目测验收。
- Python 语法检查、脚本 CLI 参数检查及定向 `git diff --check` 通过，Git 提示工作区既有 CRLF 转换策略。

无渲染工具阻碍；设计已获准接入。
