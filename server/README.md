# 网络服务脚手架

ASP.NET Core / .NET 10 模块化单体，独立于 Godot、编辑器及外层 C++ server。范围依据 [第一阶段计划](../docs/network-services-phase1.md)。当前**不是可公开运营的账号/曲库服务**。

## 已有与未有

- 有：稳定实例ID配置校验、无凭据协议发现、存活/就绪检查、Problem Details错误、请求体/请求头限制、全实例固定窗口限流、真实回环HTTP回归；Npgsql连接池、事务迁移、数据库实例绑定及独立数据库健康检查。
- 已接通：管理员发布/下架、strict v2谱包存储校验、登录用户列表/详情/下载、成绩幂等上传与本人查询，详见[API说明](API.md)。账号说明见[账号约定](Accounts/README.md)。游戏端已接账号、首版商店/首次安全安装和结算上传，正式部署仍未完成。发布端额外需要运营者安装`ffmpeg`/`ffprobe`并放入PATH；缺工具时新发布失败关闭，不在游戏端安装这些工具。
- 不做：排行榜、段位、多人联机、反作弊、支付、公共投稿、任意脚本执行。

## 本机运行

需要.NET 10 SDK。首次为自己的实例生成UUID并持久保存；重启必须复用同一值，不能使用示例共享身份。开发回环测试的ID可用 `python -c "import uuid; print(uuid.uuid4())"` 生成。

```sh
Instance__Id='<自己的UUID>' Instance__Name='本机开发实例' \
  dotnet run --project server/DynamiteUniverse.Server.csproj
curl http://127.0.0.1:5080/.well-known/dynamite-universe
curl http://127.0.0.1:5080/health/live
# 返回503是当前脚手架的预期结果，而非业务就绪
curl -i http://127.0.0.1:5080/health/ready
```

默认只监听127.0.0.1:5080。未提供有效`Instance__Id`会启动失败。appsettings从程序集目录加载，不依赖调用者当前目录。`Instance__Id`和展示名称都不是可信身份认证；客户端未来必须结合来源URL和TLS隔离会话。

数据库连接从环境变量`ConnectionStrings__Postgres`读取，不写入仓库或命令参数；远端必须`SSL Mode=VerifyFull`并配置可信CA，只有精确回环主机允许开发明文。错误响应不带连接串、SQL详情或密码。未来本地谱包存储必须在Web根以外，不接收任意路径，不把`client/testdata`作为公共曲库。

### 省维护的本地开发库

前置：本机已安装用户目录版PostgreSQL 18.7（无系统服务、不修改PATH）。脚本优先使用`DYNA_POSTGRES_BIN`或`%LOCALAPPDATA%/DynamiteUniverse/postgresql-18.7-1/pgsql/bin`；其他机器可提供同样的原生运行文件，或使用Docker Engine + Compose。

```sh
python server/dev.py up       # 新建随机凭据/实例ID，启动数据库并执行迁移
python server/dev.py run      # 使用同一配置启动API
python server/dev.py test     # 新建独立随机测试数据库，运行后只删除该测试库
python server/dev.py stop     # 停止数据库；不删卷
```

仅在明确要向当前本机开发曲库添加测试内容时运行`python server/dev.py mock-download`：保留一个标记为MOCK的合成谱包，临时新建测试管理员、走HTTP发布/下载/安装和strict加载核对，完成后禁用该测试账号并撤销会话，保留审计。它**会修改现有开发库**，不是默认隔离测试；不改变已有用户权限、不使用client/testdata、不写游戏存档。重复执行复用同packId已发布mock。首轮实测及收尾修复边界见docs/verification.md。

数据库仅绑定`127.0.0.1:55432`。原生模式的数据保留在`server/.local/pgdata`，日志为`server/.local/postgres.log`；Docker备选模式保留在Docker卷。`server/.local/database.json`为忽略的本机开发秘密，脚本不打印；POSIX创建权限0600，Windows新建时移除继承权限，仅授予当前用户和SYSTEM完全控制，不能当生产secret store。首次`up`只生成一次，切勿删除该文件以“重置”密码；已有卷密码不会随文件重建而改变。开发角色是容器初始化管理员，仅限本机；生产应区分迁移角色与低权限运行角色。不要运行`down -v`清除数据。

首次创建配置会保存`backend`（native/docker），之后不会因安装/卸载另一套程序而切换。旧配置缺少该字段时，`up/stop`会拒绝猜测；操作者确认原本使用的后端后，运行一次`python server/dev.py up --backend native`（或`--backend docker`）持久化选择。已保存后端不可用或要求切换时会明确拒绝；`run/test/migrate`继续使用原固定连接，不改配置。原生每次启动均显式指定127.0.0.1:55432，不依赖初始化中途写入postgresql.conf。

已有自管PostgreSQL可自行提供连接变量和`Instance__Id`后运行`dotnet run --project server -- --migrate`。HTTP普通启动**不改表**。迁移采用事务+数据库咨询锁、版本/规范化SQL hash检查；失败可重试，未知版本/改动旧迁移/实例ID不一致则拒绝。当前已创建实例/迁移、账号/会话、曲库版本/差分、管理审计和本人成绩表。不要改旧SQL，后续追加新迁移。必须使用本服务专用数据库，生产迁移前先备份；备份恢复与升级仍需运营者负责，自动化不等于无需维护。

数据库连通/迁移状态见`/health/database`；`/health/ready`同样验证业务schema与实例身份（200/503），不替代所有文件完好性与生产部署验收。

## 管理员命令行

先执行`dotnet build server`，预检复用已构建的服务端校验器（需要FFmpeg/ffprobe），不会修改输入ZIP。其余操作连接正在运行的服务，交互输入已有管理员账号和密码；不会自动提权、创建管理员或保存令牌。远程只接受HTTPS，禁止重定向。

```sh
python -I server/admin.py check --file path/to/pack.zip
python -I server/admin.py list --server http://127.0.0.1:5080 --offset 0
python -I server/admin.py publish --file path/to/pack.zip
python -I server/admin.py withdraw
```

publish会先本地预检，再要求输入版本及授权说明并确认；现有授权HTTP头仅接受ASCII描述。withdraw要求输入准确发布UUID、原因并再次输入UUID确认。退出时撤销工具所用会话；网络故障则提示未确认撤销。不要把密码/token放到命令行参数。list每页50，包含下架版本。

## 暂定协议入口

| 方法/路径 | 当前结果 |
|---|---|
| GET `/.well-known/dynamite-universe` | 协议版本1、实例ID/名称；三项能力随数据库就绪状态 |
| GET `/health/live` | 200，仅表示进程可响应 |
| GET `/health/ready` | 业务schema/实例检查，200或503 |
| GET `/health/database` | 实际连接/迁移/实例ID检查，200或503，不泄露连接详情 |
| POST `/api/v1/accounts/register`、`/login` | 已实现，见账号约定；缺数据库返回503 |
| GET `/api/v1/accounts/me`、POST `/api/v1/accounts/logout` | 校验bearer后返回本人／撤销当前会话 |
| GET `/api/v1/catalog/packs` | 登录用户曲库列表，详情/下载与管理入口见API说明 |
| GET/POST `/api/v1/me/plays/` | 本人分页记录／幂等提交，不提供排行 |

这是开发预览版本，不是已冻结的业务DTO。管理员接口要求数据库角色，普通注册不能提升权限；无默认管理员账号，授权必须使用本机受控命令。

## 模块职责与下一批

- `Hosting/`：宿主、配置、错误、HTTP限制；不拥有账号或成绩状态。
- `Protocol/`：发现响应；未来客户端协议库独立拆出，不让游戏引用Web宿主。
- `Accounts/`：已接安全密码哈希、开放注册、本人鉴权和会话撤销；恢复账号/客户端登录保持待后续。
- `Catalog/`：已接管理员发布、授权来源说明、不可变版本/差分Digest、受限下载及下架；没有公众投稿。
- `Records/`：已接认证主体账号范围内的playId幂等写入与本人分页，核对发布/规则身份；客户端上报非反作弊认证成绩。
- `Persistence/`：Npgsql 10.0.3连接池、显式事务迁移、数据库状态检查；账号/曲库/记录依然各自拥有业务规则，不把SQL放进HTTP宿主。依赖归属见[服务端通知](THIRD_PARTY_NOTICES.md)。

远程服务必须配置HTTPS、AllowedHosts及受信反代网络。当前不信任转发头、不启用CORS、不自动重定向携带凭据的请求。全实例120请求/分钟只是脚手架资源护栏；ready/database共用独立120请求/分钟健康预算，不消耗业务额度，live仍豁免。账号接口整体十秒deadline覆盖就绪检查和后续查询；曲库发布/传输不继承账号十秒限制。以上并非足够的登录滥用防护；生产前增加账号/IP分区限流、连接/并发预算、代理部署和备份恢复验证。配置对外监听不等同发布授权。

## 验证

```sh
dotnet run --project tools/server-tests/ServerTests.csproj
python -B tools/architecture/check.py
```

默认测试启动真实Kestrel，仅绑定127.0.0.1随机端口，使用合成身份和HttpClient，不连接数据库/外网，不启动Godot。数据库专项通过`python server/dev.py test`，或设置`DYNA_TEST_POSTGRES`后执行`dotnet run --project tools/server-tests -- --postgres`；必须使用本机专用测试角色（需要CREATEDB），创建唯一随机数据库，测试结束仅删此库，覆盖并发迁移、重复迁移、实例拒绝/回滚、hash不匹配。未配置时专项失败退出而不是假通过；默认仓库verify明确打印数据库专项未执行。本机PostgreSQL18.7真实专项现已通过，另验证停止/重启后实例与迁移记录保留；仍不是正式远程TLS/生产部署验收。原生进程不随开机启动，需要时执行`up`，关闭时执行`stop`。不要在已有原生库和Docker库间自动搬迁数据；它们是独立存储，备份恢复另行操作。

项目无项目级LICENSE；自部署目标不自动授予代码/素材再分发许可。
