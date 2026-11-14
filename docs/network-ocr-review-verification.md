# 网络第一阶段 OCR 报告核实（2026-11-14）

## 范围与结论

- 输入：根目录 `result.json`，SHA-256 `022617070e0c0917696d2142e80a27dfd0d148218edea494996439469f8ea27d`。
- 报告与核实时源码均为 `6e1720985d3ce25ce16704ec222d54430443da28`；核实时工作区仅有未跟踪的 `result.json`。最初核实未修改业务代码或原报告；后续授权修复单列于下文。
- 报告实际有 **10 条结论**；118 个改动文件中 selected/completed 为 **83**，excluded 为 **35**（33 个不支持的扩展名、2 个默认过滤路径）。所以不能表述为118文件全部审查完成。
- 两次 file_read 失败指向不存在的 `ServerAddress.cs`、`V2BuiltInAudioProbe.cs`；本轮实际读了 `AccountProtocol.cs` 与 `AudioProbe*.cs`。它们不自动否定结论，但报告的 completed 也不代表每次工具调用成功。
- **9 条缺陷/风险的核心结论成立；另1条账号竞态在模型层复现，但当前UI触发条件需限定。没有可直接判为完全误报的条目。** 不把潜在卡顿等同已测性能瓶颈，不把部署工具问题等同当前本机数据库损坏。
- 本轮是对10条报告结论及其调用链的复核，不冒称重新独立穷举审查83/118个文件，也不宣称没有其他缺陷。

编号按 `comments` 原顺序。执行方法与边界统一见 [verification.md](verification.md) 的2026-11-14网络OCR记录。

## 逐条核实

### 1. 拒绝曲库会话后，迟到刷新恢复旧登录——模型缺陷成立，UI可达性需限定

位置：`client/scripts/network/AccountConnection.cs:49–51,117–123,138–164`。

`RefreshAsync` 捕获旧login，完成时只核对generation；`RejectCatalogSession`清login但不增加generation，因此不能阻止旧刷新写回。原类隔离探针：reject后account为空/status=unauthorized，释放延迟Me响应后account恢复/status=signed_in。

但现有刷新入口在`OnlineScreen`，reject调用在`StoreScreen`；账号页返回、继续本地和退出树均取消pending，商店没有刷新入口。未找到普通单页面操作必然并发两者的路径。应修复模型不变量，但不能声称当前玩家能稳定触发，亦非服务端鉴权绕过（服务器仍核验token）。

建议：拒绝当前会话时使旧操作失效，或刷新落地时核对捕获login仍为当前login；加延迟刷新回归。

### 2. 选中一行重建全部商店行——成立

位置：`client/scripts/ui/store/StoreScreen.cs:118–141`；已有局部更新方法`StoreRow.cs:24`。

每次Select都会RemoveChild/QueueFree全部行并新建Button和4个StyleBox；因此操作量随累计列表线性增长，也移除当前键盘焦点节点。当前分页可累计约一万行，50行也会发生无必要重建。代码可确认对象重建/焦点对象消失；没有启动Godot，未测卡顿毫秒数或实际键盘体验。

建议：Select仅更新已有row.Select状态；只在内容集合变化时Rebuild。该修复不要求做完整列表虚拟化。

### 3. 安装根目录枚举异常越过逐项catch——成立，异常场景触发

位置：`client/scripts/network/CatalogInstaller.cs:30–54`；调用方`ChartCatalog.cs:31`、`StoreScreen.cs:112–116`。

try在foreach循环体内，无法覆盖创建枚举器或MoveNext抛出的I/O/权限异常。Directory.Exists只作前置检查，不保证稍后可枚举；例如检查后目录被移走、目录可查询但不可列举。两个直接调用方没有该异常兜底。结果是整次谱库扫描或商店初始化中断，并非仅跳过在线安装。

限定：一部分权限情形Directory.Exists本身会返回false，这些情况不会抛出；不是任何目录缺失都会出错。源码确认，未改用户目录ACL或人为中断用户存储。

建议：在完整枚举边界处理可恢复异常，返回已收集项并提供适当诊断，不吞掉无关编程错误。

### 4. 十秒deadline没有传给账号处理器——成立

位置：`server/Accounts/AccountRequestFilter.cs:17–28`；`AccountEndpoints.cs:11,16,21,27–29`；`AccountService.cs:25–32,53–84`。

linked token只交给StatusAsync。后续handler及SQL用原RequestAborted，因此注册唯一键等待、登录FOR UPDATE等不会因该十秒计时取消。数据库CommandTimeout=15秒是单命令上限，不是整个请求上限；CredentialWorkFilter持有两个槽之一直到next结束。不是无限等待，也不是密码哈希可被token强制中断。源码确认，未在实际开发库制造锁等待。

**修复注意**：AccountRequestFilter也应用于曲库下载、发布、成绩。不能机械地把整个通用filter之后的操作一律限制十秒，否则合法大包上传/媒体校验可能被误杀。应明确账号操作deadline与发布/传输deadline，传递正确token，并用原始断连token判断取消原因/恢复RequestAborted。

### 5. ready/database与业务共享限流——成立

位置：`server/Hosting/ServerApplication.cs:45–64,84–103`。

只有live标记DisableRateLimiting，另两个健康端点使用全实例120次/分钟预算。独立无数据库Kestrel探针：ready原为503；耗尽预算后live=200，ready=429，database=429。这证实健康路由被限流先拦截。若实例数据库就绪，业务预算耗尽同样会覆盖原本200的判断。

限定：没有测试生产编排器，也没有声称当前本机已被摘除；移出服务是部署系统依赖ready时的后果。

建议：独立健康检查预算或受控探针豁免。数据库检查比live重，不应不加考虑地向公网提供无限查询。

### 6. 开发数据库后端随二进制可用性切换——成立，环境变化触发

位置：`server/dev.py:32,50–68`；`server/local_postgres.py:30–33`。

配置没有backend，命令每次重新检测native。Docker初始化后安装native，再stop会走native；没有本机PG_VERSION则返回0，根本没发Docker stop。隔离mock使用原脚本验证返回0、子进程调用数0。再次up可能端口冲突，或在Docker停止后另建空集簇；反方向丢失native也会错误选择Docker。

当前用户一直使用native，未发现已经切错库或丢失数据。两个存储本身没有自动搬迁/清除。

建议：首次选定后持久化后端；对旧配置做明确识别/确认，不用“当前找到了什么程序”推断数据归属。

### 7. initdb成功后、写端口前中断无法自动恢复——成立，低频初始化窗口

位置：`server/local_postgres.py:34–56`。

配置追加在not data.exists分支；PG_VERSION存在就认为可继续启动。隔离mock构造已有PG_VERSION、默认port配置，再up后配置未改变，pg_ctl启动无端口覆盖，但psql固定55432。原报告中断窗口真实存在。未杀实际initdb进程，未启动第二数据库。

建议：启动前幂等验证/应用此开发集簇所需的监听地址和端口，或采用覆盖配置步骤的完成标记。不要删除数据目录来“修复”。

### 8. 发布校验阶段不响应请求取消——成立

位置：`server/Catalog/CatalogService.cs:67–86,112–118`；`PackageInspector.cs:14–51`；`CatalogMediaValidator.cs:48–77`。

PublishAsync有ct，但Inspect签名无token；Extract的可选token未传入，媒体进程只有各自45秒deadline。请求已取消时，校验仍可能逐文件执行，独占发布信号量一直到Inspect结束或失败；其他发布返回publication_busy。后续hash/SQL重新观察ct，不意味着前面工作会及时停止。

限定：不是永久锁死；单进程deadline和包预算仍在。也不代表普通未授权用户能进入发布处理器。源码确认，未用恶意/慢媒体占用实际服务。

建议：向Inspect、解压、媒体处理传递取消；子进程deadline链接请求token并终止进程树；同步解析/哈希至少在阶段及文件之间检查。取消应保留为取消，不统一伪装成invalid_package。

### 9. 缺失Windows非法文件名字符检查——成立，跨平台发布风险

位置：`shared/IO/SafePackageArchive.cs:22–37`；`shared/Chart/V2/V2JsonDecoder.cs:477–494`；`V2PackageWriter.cs:643–683`。

未统一拒绝 `" < > | ? *`，下游路径解码也允许。原解码器接受引用chart?.json；Windows原解压器直到创建文件才抛IOException，而非便携性预检拒绝。Linux允许这些文件名，代码链未提供其他统一拒绝，因此Linux发布/Windows安装存在兼容性缺口。

限定：本轮没有Linux运行环境，Linux完整发布路径为源码/平台规则确认，不写成实际跨OS端到端测试。Windows失败不会生成完成receipt，所以不是目录逃逸或半包进入选曲。

建议：归档边界使用固定跨平台禁止字符集，不能只使用运行主机GetInvalidFileNameChars；增加被meta实际引用的路径用例。

### 10. 服务端FLAC预算大于客户端——完整校验＋解码探针确认

位置：`server/Catalog/CatalogMediaValidator.cs:39–43`；`client/scripts/game/common/Res.cs:20–25,128–136`。

服务器以float估算上限256MiB，等价16-bit PCM最多128MiB；客户端FLAC的16-bit PCM上限64MiB。shared发布probe校验FLAC结构和样本数但不强制客户端PCM预算，不能阻止该差异。

生成8分钟、44100Hz、双声道16-bit静音FLAC，压缩文件79,618字节，以golden schema差分打包：**完整PackageInspector通过**，然后实际FlacDecoder按Res相同选项拒绝，报16,777,728帧超过16,777,216帧预算。整曲预期16-bit PCM为84,672,000字节，约80.75MiB。未上架到开发曲库、未使用原版资产、未启动游戏。

建议：发布时执行客户端FLAC特有预算，最好由无Godot依赖的共享兼容策略定义。不要为了让既有错误profile“通过”而放大客户端内存上限。

## 用户授权修复（2026-11-14）

用户随后授权“你先修吧”，10项已实施，保留上文作为修复前核实证据：

- #1拒绝当前曲库会话时取消旧操作/增加代际；#2选中仅更新现有行，不销毁焦点节点；#3在完整目录枚举边界处理I/O/权限异常，保留已读安全receipt。
- #4仅账号路由标记整体十秒deadline，传给下游处理器，finally恢复原RequestAborted；曲库/发布不继承该截止。#5数据库健康路由独立120次/分钟预算，live维持豁免。
- #6新配置保存backend，旧配置up/stop需要操作者显式确认；后端不可用/切换请求拒绝，不改密码或实例；本轮未替用户修改实际本机配置。#7原生pg_ctl每次启动显式设置监听/端口，消除initdb后写配置的中断窗口。
- #8请求取消贯穿Inspect/解压/媒体进程，并在同步校验阶段之间检查，保留取消异常，清理staging并释放发布槽。
- #9固定跨平台非法字符检查，不依赖主机文件名规则；#10游戏和服务器共享FLAC播放预算，发布端额外检查64MiB的16-bit PCM上限，客户端原上限不变。

新增回归与实际执行见[验证记录](verification.md)。不修改冻结v2/Digest、判定顺序、成绩归属；不新增历史列表、更新或卸载。UI调用方仍拥有选中状态与详情生命周期，安装器拥有扫描容错；会话模型拥有认证失效，服务端拥有发布审核和进程清理。

## 建议处理顺序（修复前评估，已获后续授权）

1. 先修影响内容可用性的 #10、#9，及异常目录导致整库加载中断的 #3。
2. 同批修 #2、#4、#8、#5：现有UI局部更新与服务端取消/健康边界。
3. 修 #6、#7 的开发工具持久身份/恢复；小范围补 #1 模型竞态护栏。

这些都不需要新增历史列表、更新/删除、排行榜或生产部署范围；后续授权与实施见上一节。
