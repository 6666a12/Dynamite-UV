# 发布流程

本文区分两种合法但用途不同的 Android APK。两条流程不能混用。

> 本次更名把 Android application ID 改为 `com.dynamiteuniverse.game`。Android 会将其视为新的应用身份，旧 `org.duxcommunity.game` 沙箱中的设置、谱面和成绩无法由新应用自动读取；需要用户自行导出并迁入可移植数据。

| 模式 | Godot preset | application id | 输出 | 官方测试数据 |
| --- | --- | --- | --- | --- |
| Public | `Android Public Release (NO TESTDATA)` | `com.dynamiteuniverse.game` | `builds/public/dynamite-universe-public.apk` | 禁止 |
| Internal | `Android Internal Testdata (DO NOT DISTRIBUTE)` | `com.dynamiteuniverse.game.internaltest` | `builds/internal/dynamite-universe-internal-testdata.apk` | 允许且必须存在 |

Public APK 可以公开分发；Internal APK 只能给开发设备和受控测试者使用，**不得上传到公开
Release、网盘、应用商店或公开聊天群**。

## 1. 发布边界

- 整个 `client/testdata/` 是内部开发区。里面可以临时保存官方测试谱、音频和曲绘，但这些
  文件被 Git 忽略，且不得进入 Public APK。
- Internal APK 含官方测试数据是当前制谱器完成前的预期行为，不是缺陷。
- Public APK 对 `testdata` 的源文件和 Godot `.godot/imported` 派生产物均零容忍。
- 社区曲绘由谱师随谱面包提供。公开客户端不内置 AI 生成曲绘。
- Public 禁止 C# 源码内容和 PDB 调试符号；Internal 可保留 PDB 供诊断；
- `tools/` 下研究/逆向工具、反编译输出、原版 APK、外部 JAR、调试 keystore 和生成缓存
  均不是游戏发行内容。
- 项目当前没有项目级 LICENSE；原创代码和原创资产保留所有权利。第三方许可见
  `THIRD_PARTY_NOTICES.md`。

## 2. 首次配置

`client/export_presets.cfg` 是可版本化的非秘密配置。Godot 4.7 将 Android keystore 路径、
用户和密码等秘密写入 `client/.godot/export_credentials.cfg`；该文件和整个 `.godot/` 已被
Git 忽略。也可用 Godot 支持的 Android keystore 环境变量在 CI/本机注入秘密。

不要把任何 `.keystore`、`.jks`、`.p12`、`.pfx`、`.pem` 或 `.key` 文件放进仓库。
历史中的 `tools/apktool/dynamix_debug.keystore` 已公开暴露并从当前树移除，必须视为不可信，
绝不能用于产品签名。

## 3. 构建和 clean-room 测试

从仓库根目录执行：

```powershell
dotnet build client/DynamiteUniverse.csproj --configuration Release
dotnet run --project tools/core-tests/CoreTests.csproj --configuration Release
python -m unittest discover -s tools/release/tests -p "test_*.py"
```

默认核心测试只使用 `tools/core-tests/fixtures/` 中人工合成的 clean-room fixture。可选的
本地官方语料回归要显式执行，且不是 Public 发布的可复现门禁：

```powershell
dotnet run --project tools/core-tests/CoreTests.csproj --configuration Release -- `
  --dev-testdata client/testdata/packs
```

## 4. 导出 Public APK

### 4.1 必须使用干净 worktree

`.gitignore` 不能控制 Godot 导出，陈旧 `.godot/imported` 也可能残留。因此 Public APK
必须从当前提交的临时 clean worktree 导出，而不是直接从带有本地 `client/testdata/` 的开发
目录导出。示例：

```powershell
git worktree add --detach ..\community-public-export HEAD
```

在临时 worktree 中配置本地签名凭据后，按项目约定先结束所有残留 Godot 进程：

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe
```

然后使用 Godot 4.7.1 Mono 导出：

```powershell
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" `
  --headless --path ..\community-public-export\client `
  --export-release "Android Public Release (NO TESTDATA)" `
  ..\community-public-export\client\builds\public\dynamite-universe-public.apk
```

若 Godot 位于其他目录，只调整可执行文件路径；不要修改并提交机器绝对路径。

### 4.2 强制检查最终产物

```powershell
python tools/release/check_apk.py --mode public `
  --apk ..\community-public-export\client\builds\public\dynamite-universe-public.apk
```

只有退出码为 0 且显示 Public 检查通过的 APK 才能分发。Public 首次验包前先取得产品
签名证书 SHA-256（例如从受控签名流程的 `apksigner --print-certs` 输出），然后仅在当前
shell 中设置：

```powershell
# Windows PowerShell；Git Bash 使用 export DYNAMITE_UNIVERSE_PUBLIC_SIGNER_SHA256=<值>
$env:DYNAMITE_UNIVERSE_PUBLIC_SIGNER_SHA256 = "<64位产品证书SHA-256>"
```

该值是证书公钥身份，不是私钥；不应把私钥、keystore 或密码写入仓库。检查器会验证：

- ZIP 完整性和 SHA-256；
- application id、应用名、version name/version code 和 `debuggable=false`；
- Public custom feature、核心 Godot 场景/资源、.NET 程序集依赖、arm64 ABI 和 PDB 禁止项；
- APK 签名完整性、v2/v3 scheme 和产品签名证书 SHA-256；
- Public 文件名；
- 任意层级的 `testdata` 路径；
- `.import` 中 testdata 来源及相应哈希导入产物；
- 任何不在 Public policy 白名单内的孤立 Godot 导入 payload；
- Orbitron、Godot 和 .NET 许可/第三方通知存在且 SHA-256 正确；
- 已移除的 AI 原型曲绘及其导入产物。

任何关键检查无法执行都会失败，而不是降级为 warning。上传时将检查器输出的 SHA-256 一并
保存。完成后可删除临时 worktree：

```powershell
git worktree remove ..\community-public-export
```

## 5. 导出 Internal APK

Internal APK 可以从含 `client/testdata/` 的开发目录导出。启动 Godot 前同样先结束残留进程：

```powershell
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" `
  --headless --path client `
  --export-debug "Android Internal Testdata (DO NOT DISTRIBUTE)" `
  client\builds\internal\dynamite-universe-internal-testdata.apk
```

导出后必须检查内部身份和 testdata 的存在：

```powershell
python tools/release/check_apk.py --mode internal `
  --apk client\builds\internal\dynamite-universe-internal-testdata.apk
```

Internal 模式要求独立 application id、内部应用名、内部版本和内部文件名；如果没有 testdata，
会失败以提示选错了 preset。成功输出会明确显示 `DO NOT DISTRIBUTE`。

## 6. 发布前清单

- [ ] 当前提交中的 `client/testdata/` 跟踪文件数量为 0。
- [ ] Public APK 从 clean worktree 导出。
- [ ] 使用正确的 Public preset、包名和签名密钥；`DYNAMITE_UNIVERSE_PUBLIC_SIGNER_SHA256` 已钉住产品证书。
- [ ] Release build、默认核心测试和 APK checker 单测通过。
- [ ] `check_apk.py --mode public` 退出码为 0。
- [ ] 已记录 APK SHA-256。
- [ ] 发布附件中没有 Internal APK、官方谱面、原版音频、研究输出或签名材料。
- [ ] 第三方通知与当前资产一致。

Internal 测试包另行保存，文件名不得改成 Public 命名，也不得与 Public 产物放在同一上传目录。
