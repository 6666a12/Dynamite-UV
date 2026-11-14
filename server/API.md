# 第一阶段后端接口（开发预览）

当前实现账号、管理员发布、登录用户曲库下载、本人成绩记录。游戏端已接账号、首版商店/首次安装及结算上传。不是生产发布批准。

## 管理员与发布

先通过注册接口创建账号，再由服务器本机操作者执行：

```sh
python server/dev.py grant-admin --username <已有用户名>
```

没有默认管理员；不自动把首个注册者提升权限。授予权限与发布/下架均写`du.admin_audit`。本批没有创建真实管理员或上架真实素材。

- POST `/api/v1/admin/catalog/releases?version=v1`：管理员Bearer；`Content-Type: application/zip`；`X-Content-Rights`记录谱面/音频/曲绘作者和授权依据（当前接受HTTP头可表达的文本，详细来源系统留后续）；正文为ZIP，成功201，重用同packId/version为409。
- POST `/api/v1/admin/catalog/releases/{releaseId}/withdraw`：管理员Bearer，JSON `{"reason":"原因"}`；成功204。下架保留历史文件/审计，不物理删除。

发布版本独立于v2 revision；title/artist/packId/revision和chartId/Digest由包内容校验计算，不能靠上传参数伪造。版本内容不可覆盖。同一包多个版本时列表选发布时间最新的可见版本；详情、下载、成绩均用不可变releaseId。

归档限制：压缩128MiB、解压256MiB、最多1024成员、单文件64MiB、JSON8MiB；拒绝逃逸/重复/大小写冲突/链接/不支持扩展名/未被包引用内容。ZIP应只包含文件，不需要显式目录项。流式写入私有暂存目录、严格v2跨文件校验、内置音频容器/时长校验和shared Gameplay Digest后才发布；不执行包内代码。图片与音频在发布端调用运营者安装的`ffprobe`/`ffmpeg`完整解码检查（不随客户端分发），程序不可用或检查失败则拒绝发布；不执行SVG或包内代码。`desktop-mobile-media-1`限制静态PNG/JPEG/WebP封面8MiB、单边4096、1600万像素；单流音频1–2声道、8–96kHz、按float PCM估算不超过256MiB，保守接受PCM WAV、MP3、Vorbis、FLAC。FLAC另外遵守与游戏共享的`FlacDecoderOptions.GamePlayback`：压缩输入及16-bit交错PCM各不超过64MiB，不扩大客户端预算。每次工具调用最多45秒、输出有界、禁网络协议；请求取消传递至解压、校验阶段及媒体进程，终止进行中的媒体进程树。归档文件名统一拒绝Windows非法字符，不因发布主机是Linux而放宽。该profile是服务端的保守兼容审核，不等同Godot所有平台逐编码器实机认证，也不等同操作系统级解码器沙箱。管理员必须自行拥有分发权，不能将client/testdata作为公共曲库。

文件完成后、数据库事务提交才可见；提交结果不确定时保留完整文件防止误删已提交内容，可能留下未登记孤儿文件，暂不自动GC。成功发布不受暂存清理失败误报影响。存储目录须由服务独占且位于Web根外；开发脚本配置`server/.local/catalog`。

## 登录用户曲库

全部要求Bearer（不提供匿名曲库/下载）：

- GET `/api/v1/catalog/packs?q=<文字>&offset=0`：每页最多50、offset上限10000，q最多80字符，按发布时间/id倒序，字面子串搜索标题、曲师、包ID及差分展示元数据，参数化SQL。
- GET `/api/v1/catalog/releases/{releaseId}`：版本元数据、授权说明、各差分Digest/ruleset。
- GET `/api/v1/catalog/releases/{releaseId}/download`：下载ZIP，支持Range；服务打开同一文件句柄校验大小/SHA-256后输出，篡改返回503。下架或不存在404。文件已开始输出时下架不能追溯撤回该响应。

- GET `/api/v1/catalog/releases/{releaseId}/cover`：同源、固定发布封面，鉴权/下架规则同下载；对实际输出文件验证大小/hash，无封面404。

列表与详情的`release.display`包含`validationProfile`、各差分`chartId/difficulty/difficultyKey/level/unrated/charters/bpmMin/bpmMax`与可选`cover(mediaType,size,sha256)`。005追加迁移只新增nullable字段，历史发布不伪造审核结果；缺少当前profile的旧发布仍可展示，但新客户端拒绝安装，需运营者重新审核后发布新版本。当前未提供原发布的批量回填命令。

## 本人成绩

- POST `/api/v1/me/plays/`：Bearer；JSON字段见`Records/RecordService.cs`的`PlaySubmission`。
- GET `/api/v1/me/plays/?offset=0`：仅本人，每页最多50，offset上限10000；不接受accountId选择他人。

必须带playId/releaseId/chartId/digest/rulesetId/mode/mirror/auto/completed/score/perfect/great/good/miss/maxCombo。开发首版保守接受已完成、非Auto、已发布版本的standard/hardcore结果；拒绝中断/失败/未知手工包；客户端调试游玩资格仍须接线时限制。身份由Bearer决定，不从JSON信任账号字段。客户端必须固定该局账号/服务器和playId再重试，不把结果改投另一身份。

首次201；同账号同playId完全相同内容200；不同内容409。并发重试只留一条。下架后已保存结果仍可幂等重试，但新结果422。查询返回`verification=client_reported`：只有身份/基础范围/引用检查，**没有重放、判定总量与服务端重算分数认证，也不是反作弊**。不提供排行榜或BEST摘要，模式单独存储、不修改本地成绩身份。

- GET/POST `/api/v1/me/plays/v2`：个人记录合同2，游戏端使用。POST字段为shared `PlayReport`，在v1字段基础上增加contractVersion=2、bleed、rawScore、theoreticalMax、clear、finishReason、startedAt、verificationRun、usedSeek、debugRun；正常完成、非Auto/验证/seek/debug才接受，检查原始分数、未舍入CLEAR与百万整数分一致性，但不据此声称反作弊。GET保留原始完整payload，旧记录不补造新字段。数据库仍使用现有jsonb，不改旧迁移；v1入口继续兼容旧调用方。

## 运维及未完成项

`python server/dev.py up`迁移；`run`启动；`test`跑独立数据库+HTTP闭环。数据库迁移/实例验证成功时发现能力均为true，`/health/ready`表示数据库业务schema可用，不保证所有磁盘文件完整或对外生产就绪。

尚需：生产低权限角色/可信反代TLS、针对IP与账号的限流、下载并发/带宽与存储总配额、备份恢复、过期会话/孤儿文件维护、更多设备上的商店/安装验收与网络上传接线。现有全实例限流只是开发护栏。所有源码/素材许可限制保持不变。
