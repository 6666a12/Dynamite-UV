# 正式菜单的 Windows 窗口回归

从仓库根目录运行。需要 Godot 4.7.1 Mono Windows、.NET 9 SDK、Python 3；录制为 MP4 还需要 `ffmpeg` 在 PATH 中。正式工程的公开资产应已导入。先构建当前 Debug 程序集，再运行：

```powershell
dotnet build client/DynamiteUniverse.csproj --configuration Debug
python -B tools/client-menu-qa/run.py --godot '<Godot Mono console 的绝对路径>' --suite all --record --fps 30 --timeout 150
```

`--suite` 可选 `library`、`store`、`palette`、`calibration` 或 `all`。`--prepare-only` 只校验并准备隔离工程，不开窗口。不要同时启动多个录制套件争夺系统焦点。

- 准备器按资源归属清单校验源码侧资产、复制正式 Debug 程序集及所需导入缓存；不读取 `client/testdata`。QA 输入节点只注入临时工程，不在任何正式场景或导出内。每次使用全新的 AppData 和 `user://`；只管理本次启动的进程。
- 本地曲库是临时生成的原创短音频和 v2 谱包。商店测试服务只绑定回环地址，用一次性身份，提供真实发现、登录、鉴权、发行元数据和可安装的项目自制示例包，不连接公网。
- 驱动走真实菜单，发送 Godot 鼠标、触摸和键盘事件；比较视口像素、正式保存值、目录安装状态和原生音频播放状态。校准只在真实音频时钟上击打，不改输入时间。此测试不测人手延迟、Android 触控或 GPU 真机性能。
- `library` 还检查完整动效下长按结束后原触点松手仍有反馈、第二指不接管，以及鼠标移出后收卡仍显示新选曲的高亮。
- `store` 在过滤和下载前实际展开、收起、再展开商店详情，检查同一条曲包行没有被替换。
- 每次输出到忽略目录 `.tmp/production-migration-qa/runs/`，含 `result.json`、`player.log`、`frames/`、正式程序集/转场摘要。使用 `--record` 时，`capture.avi` 必须是大于 64 KiB、有 RIFF/AVI 文件头的普通文件，否则退出失败，即使未安装 ffmpeg 或使用 Python 优化模式。装有 ffmpeg 时额外转出 `capture.mp4`。勿分发整个临时用户目录；视频外发先剪去输入测试凭据的过程。
- 不启动引擎的录制门禁回归：`python -O -B -m unittest discover -s tools/client-menu-qa -p test_run.py`，已纳入 [统一验证](<../verify.py>)。它不替代运行上述 Windows 窗口套件。
