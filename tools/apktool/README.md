# Apktool（外部研究依赖）

行为研究脚本可能需要 Apktool，但客户端构建、核心测试和 APK 发布检查均不依赖它。

- 曾在本目录捆绑的本地版本为 Apktool 3.0.2；二进制 JAR 已从仓库移除。
- 需要时从 <https://apktool.org/> 或其官方发布页单独安装，并自行核验版本和校验值。
- 不要把 `apktool.jar`、反编译产物、原版 APK 或签名密钥加入仓库。
- Apktool 使用 Apache License 2.0；遵守其上游许可证和 NOTICE 要求。

本目录属于研究工具边界，不会进入 Godot 客户端导出。
