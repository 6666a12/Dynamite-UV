# 2026-10-25 client 续拆与验证记录

> 历史记录：2026-10-26 从整理前的 handoff 与 module-map 提取，未重新执行当时测试。
> 当前状态以 [交接](<../../handoff.md>)、[模块地图](<../../module-map.md>)、[验证记录](<../../verification.md>) 为准；不能用本页证明最新工作区/设备通过。

## 当时范围

校准采样、选曲卡片动画/详情卡/详情请求和暂停覆盖层独立为对象；GameplayMain/SongSelect/CalibrationScreen 总量为 1317/1296/1213 行。
判定帧序、音频时钟、详情/试听双 loader、成绩保存与 editor 未有意改动。
新增核心回归覆盖采样相位、卡片状态和请求代际。

## 当时验证

`python -B tools/verify.py --no-restore` 通过，结构检查 219 个源码文件；客户端/编辑器构建均 0 warning / 0 error，核心/编辑器回归及 37 项发布测试通过。
GameplayMain/SongSelect/CalibrationScreen 总预算收紧为 1350/1300/1250。

隔离 headless 比较覆盖 calibration/song/pause × Full/Reduced/Off，9 组各 220 帧共 1980 帧逐记录一致；仅将新 `SongDetailCard` 类型名归一化为旧 `Control`。
本地证据位于 `.tmp/modularize3/phase4-before-*` 与 `phase4-after-*`，它们被忽略，不随克隆取得。

试听探针固定 worker 完成顺序和 elapsed，不证明真实 I/O 时延一致；未做 GPU 像素比较、可见窗口、APK 导出或真机验收，也未提交 Git。

## 同日第一轮的环境记录

第一轮统一 `--no-restore` 验证也记录为两端 0 warning / 0 error、核心/编辑回归与 37 项发布测试通过。
随后单独 editor 依赖还原/构建成功，但 NuGet 漏洞索引不可达产生 2 条 NU1900（0 error）；输出和 deps.json 中已核对 ChartEditor.Core。
第三轮则进一步核对 Windows ExportRelease 发布输出的 Presentation 与编辑核心程序集，详见 [第三轮历史报告](<2026-10-25-modularization-phase3.md>)。

## 原始依据

整理前两份原文均按字节保存在 [清理快照清单](<../2026-10-26-cleanup/manifest.json>)，本页是历史摘要，不覆盖原始报告。
