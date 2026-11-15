# 项目网页与制谱教程

## 职责与目录

- `site/` 是 GitHub Pages 的完整公开载荷：项目首页、DynaMaker UV 中文教程、样式、渐进增强脚本，以及明确挑选的图片/字体/练习音频。
- `tools/site/` 维护载荷白名单、资产来源/hash、离线检查与练习节拍生成器；不进入网页发布。
- `.github/workflows/pages.yml` 只上传 `site/`，绝不上传整个仓库或 `docs/`。网页与游戏、制谱器、服务器无运行时依赖，不调用网络后端、不保存账号或统计访问。
- 教程正文直接在 `site/guide.html` 维护；现行能力依据 `docs/dyna-maker-uv.md` 和对应 UI/快捷键实现。不要把禁用入口、底层模型能力或旧版 DynaMaker 操作写成当前功能。

## 本地预览

从仓库根目录运行：

```powershell
python -m http.server 8765 --bind 127.0.0.1 --directory site
```

访问 `http://127.0.0.1:8765/`；教程为 `/guide.html`。纯静态站，不需要 npm、打包工具或远程 CDN。目录搜索只过滤章节入口，不隐藏正文；禁用 JavaScript 仍能完整阅读和跳转。支持窄屏与打印样式，图片点开为原尺寸。

## GitHub Pages 上线

仓库为 `6666a12/Dynamite-UV`，默认项目网页地址将是：

`https://6666a12.github.io/Dynamite-UV/`

这是**预期地址，不代表已经上线**。初次发布需仓库维护者：

1. 审核并提交网页内容；推送到 `main`（本轮未替用户执行）。
2. 在 GitHub 仓库 **Settings → Pages → Build and deployment → Source** 选择 **GitHub Actions**。
3. 在 **Actions → Project website → Run workflow** 手动运行；以后 `main` 上网页相关改动会触发部署。
4. 首次部署若受环境审批保护，按仓库要求批准 `github-pages` 环境。完成后检查首页、教程、图片、字体和音频下载。

没有修改 GitHub 仓库设置，没有在本轮创建 Release 或发布任何测试软件包。发行按钮只指向现有 Releases 列表；是否有可下载版本以实际页面为准。不要为了补一个下载按钮公开尚未获准发行的本地测试 ZIP。

所有资源链接使用相对路径，可在项目子路径部署；不依赖自定义域名。不加入含 token 的 URL、开发机绝对路径、客户端测试数据或内部设计源。

## 资产来源

- 色彩沿用 `client/scripts/ui/home/HomePalette.cs` 的深色底、青色、粉色与字色；布局为网页重新排版。
- Space Grotesk Regular/Bold 是仓库既有字体的原样副本，随网页提供 `assets/licenses/SpaceGrotesk-OFL.txt`；中文使用系统字体，不向第三方字体服务发请求。
- `assets/mark.svg` 为本轮网页绘制的简单几何字标，不是音乐封面。
- 三张 WebP 已按用户反馈于 2026-11-15 重新采集：旧图混入桌面歌词，不再作为网页素材。当前来自 `tmp_shots/site-guide-clean-20261115/01-package.png`、`05-placement-ghost.png`、`05-hold-tail-ghost.png`；用户关闭歌词后重新运行真实窗口专项。使用现有 `Verification.CreatePackage` 的合成静音和自造 Note；未打开原作谱面/曲绘。WebP 为整张截图转码，没有拼接虚构控件。原图是本地证据，不随仓库取得。已对三张新图做本机 OCR 核对，未识别出先前混入的歌词；图片工具仍未提供可目视图像，不将 OCR 作为完整视觉审图证明。
- `assets/practice-120bpm.wav` 是 16 秒、22050 Hz、单声道 PCM 合成节拍，120 BPM，每四拍首拍使用较高音高。由 `python -B tools/site/generate_practice.py site/assets/practice-120bpm.wav` 重建；不含采样歌曲或第三方音频。
- 公开载荷资产 hash 与来源记录在 `tools/site/assets.json`。许可证 `.txt` 在计算哈希和比对源文件时仅将 CRLF 统一为 LF，避免 Windows/Git/Linux 检出换行差异；字体、音频、图片仍严格逐字节校验。图片替换后必须重新核对其公开适用性并更新 hash；白名单不意味着未来任意截图均适合发布。

仓库没有项目级 LICENSE；网页没有替原创代码、图片、音频扩大许可。第三方字体按其自身许可提供。

## 验证

```powershell
python -B tools/site/check.py
python -B -m unittest discover -s tools/release/tests -p test_website.py
node --check site/assets/site.js
```

站点检查包含公开载荷白名单、文件大小、symlink 拒绝、二进制 hash、字体原样复制、站内资源/锚点、项目路径兼容、基础元信息和练习音频格式。网页回归也接入现有 `tools/verify.py` 的 unittest discovery。它们不等于浏览器视觉验收或真实 GitHub Pages 部署验证。

浏览器手测建议：桌面与 360px 窄屏、目录跳转、关键词/无结果/清空搜索、FAQ 展开、音频下载、关闭 JavaScript、键盘 Tab、打印与减少动态效果设置。主文本/次级文本/青色按钮前景背景的静态对比度均高于4.5:1；这不等于完整无障碍认证。执行结果统一见 [verification.md](verification.md)。
