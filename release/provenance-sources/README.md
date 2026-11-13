# 历史生成器来源快照

本目录仅存放现有资产的不可变来源证据，不是可执行工具入口。

- `build_runtime_results-12d83b48.py.txt`：从 Git `981b9c7` 对应版本取出的原始生成器字节，SHA-256 为 `12d83b489182084789ec12b6ffe8170cee6fae2bbc19915efd6a9b067f30abf5`，与既有 `asset-provenance.json` 的生成器 hash 一致。2026-11-13 修复当前生成器的递归收集缺陷后，现有资产继续指向此历史版本；没有重新生成图片，也没有把旧资产冒记为新版脚本的产物。

当前可维护入口仍是 `tools/grade-assets/build_runtime_results.py`。未来经授权重新生成时，该脚本会为本次明确生成的文件登记自身 hash，不更新未生成资产的来源。
