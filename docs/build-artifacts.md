# 本机构建与验证产物索引

[builds](<../builds/>) 是 Git 忽略的本地目录，不随克隆取得。目录日期不能替代包身份；当前采用哪份包、其哈希及已测范围以[验证记录](<verification.md>)为准。目录里有实验、留证和测试候选，不是发布目录。

| 用途 | 当前位置 | 状态 |
| --- | --- | --- |
| 已确认的 Windows 制谱器测试基线 | [ZIP](<../builds/editor-test-20261112.zip>) · [解压目录](<../builds/editor-test-20261112-uninstall/>) | `windows-x64-test-v1`，未签名，不等于获准公开发行。保留其 [PCK 来源](<../builds/editor-candidate-20261111-transport/>)。 |
| 较新的 Windows 制谱器候选 | [v2.1 原候选](<../builds/win-custom-expr-20261120-d13d0c4e/>) · [v2.1 审查修复候选](<../builds/win-v21-fix-20261120-2034/>) | 仍未替代 11 月 12 日基线。验包清单引用前一候选的 `scratch` 和修复候选的 `src`，不能只留下 ZIP、拆走这些目录。 |
| 正式客户端菜单证据 | [整理后的实录、截图与证据](<../builds/official-v3-migration/>) · [早期原始录屏与日志](<../builds/formal-ui-migration/>) | 11 月 21 日菜单迁移的 Windows 验证，早于后续审查修复；不是 APK 或 Android 实测。 |
| 其他历史验证 | [65 条曲线的复测包](<../builds/qa-6047a15-curves/>) · [持久化专项](<../builds/persistence-lifecycle-20261111-final/>) | 旧候选、日志及仍有文档引用的证据保留原位。 |

## 已归档的实验

2026-11-21 将下列 16 个目录按类别整体移动。移动前后逐目录核对文件数及逻辑总字节，共 1,777 个文件、3,195,171,717 字节（约 2.98 GiB）；未删除、重打包或释放磁盘空间。

- [Windows 编辑器裁剪探针](<../builds/_archive/editor-probes-20261111/>)：11 月 11 日的 slim probe、slim runtime 与 slim managed 三个实验目录，不是固定测试包。
- [菜单 V3 原型](<../builds/_archive/menu-prototypes-20261120-21/>)：13 个弧面、触控、版式和专题原型目录。原型的可执行文件、素材、源码和录像一同保留，不当作正式客户端功能证据。

后续新增试验用独立运行名；整理前同时核查活跃文档、历史证据和包内清单的路径依赖。归档不会改变包的验证或发行状态。公开 APK 必须另走[发布流程](<releasing.md>)，不得把本地测试目录整体分发。

## 本地缓存清理

2026-11-21 清除 Shell 的 `DraftReview` 输出和中间文件、已退役的音频与资源探针编译产物，以及 4 份未占用的 `site-edge-profile-*` 浏览器临时配置。共 8 个目录、1,495 个文件，逻辑大小 774,489,139 字节（约 739 MiB）；清理前后 D 盘可用空间增加约 434 MiB。正式包、Release 输出、QA 证据、设计源和 Git 恢复备份不在本轮清理范围。
