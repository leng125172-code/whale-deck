# 容器所有者：dev覆盖发布与真实页面常规整链

日期：2026-10-31。实际运行产品为 `d5e62cae9049f38239e847f0d473d27a3d3540c9`，Agent协议1.48。本批签收非递归所有者修改的常规真实页面链路；进行中取消/退出/撤权/Worker强杀/Agent重启/Unknown处置等所有者故障仍待。递归、ACL、可恢复删除、长期清理及其余59项继续，17项移出不计完成，master尚未正式交付。

## 一次开发覆盖及实际来源

- 运行源码位于 `/data/GitRepos/whale-deck`，与开发目录分开。先核对该checkout干净且在dev，由仓库所有者通过本机Git源快进到冻结d5e62ca，再调用既有固定维护helper；没有从未知远端直接替换未经测试的代码。
- 唯一发布单元 `whaledeck-dev-publish-d5e62ca.service`，启动回执InvocationID `98d579304a2f4594bd336a107a041584`。同次journal从Started到Deactivated successfully为208.435秒，Result=success、ExecMainStatus=0；没有重复发布或自动回滚。
- 六组件实际build-info/容器镜像revision及API/Gateway HTTP来源全部为d5e62ca；宿主运行MainPID的exe device/inode及托管DLL映射inode与安装文件一致。生产迁移 `20261030143305_ContainerOwnershipIntents` 和 `container_ownership_intents` 已入库。
- 11核心容器healthy；既有database-platform各容器ID/镜像/运行状态/StartedAt与发布前一致。API/Worker无宿主端口、无Docker Socket、非privileged；宿主systemd/UDS/受限helper/HDD策略及动态网卡入口检查通过。
- user NOPASSWD:ALL继续保留，固定sudoers文件root:root/0440、单文件及全量visudo校验通过。产品Agent的固定授权边界未扩大。

## 真实登录、Vue与实际文件

验收源码冻结 `c73ecc2`（与运行产品d5e62ca区分）。使用真实Chrome、正常Authentik用户名/密码表单、BFF Cookie/CSRF/Origin及实际API/SQL/Worker/Agent/helper。没有API响应替换、Cookie注入、SQL/缓存写入或伪造Root回执。只操作准确命名的非root、无网络、只读容器根和唯一合成HDD绑定目录；临时身份凭据仅在内存/子进程环境，不写Git或输出。

7个场景全部通过，最终6 Succeeded/1 Canceled：

| 场景 | 目标与结果 |
| --- | --- |
| 文件修改 | 1000:1000 → 2200:3200 |
| 文件无变化 | 2200:3200 → 同属主，完整九字段指纹不变 |
| 显式root数值 | 2200:3200 → 0:0，页面与实际目标都明确为0 |
| 文件恢复原属主 | 0:0 → 1000:1000 |
| 目录修改 | 2100:3100 → 2200:3200，混合属主子项保持 |
| 目录恢复原属主 | 2200:3200 → 2100:3100 |
| 执行前取消 | Ready后先继续等待，再明确取消；无执行Job/RootStarted或HDD历史 |

- 每个计划均先刷新页面，只接续原引用，再明确确认/取消。7次原登记和6次原确认HTTP重试都返回原ID/原执行Job；页面登记7、确认6、文件内容PUT为0。原inode属主、目标UID/GID与容器执行FSUID/FSGID=1000:1000分别校验，没有混淆身份。
- 1440/320明暗真实页面通过，额外取消场景为320×600，取消按钮完整处于视口。7张预检PNG及SHA256清单已保存；320暗色0:0、320明色目录恢复与320×600取消已目视复核，正文内滚动保留底部动作，无页面横向溢出。
- 实际缺CSRF返回400、错误Origin403、缺UID返回400及稳定错误码；普通身份8路403，匿名4路401。正常用户菜单双端退出，旧WD Cookie与Authentik会话失效，页面回到登录。
- SQL原主体/双Job/Outbox/事件/审计，与6份Root原回执、6份物理HDD元数据逐一核对，原计划和历史独立域摘要及发布版本重算一致，ownerConfirmed=true/historyVerified保持。
- 完整2097159字节文件与子文件读回一致；恢复后原inode、普通mode、原UID/GID和纳秒mtime匹配，目录子项完整指纹未变。每次Root实际身份仅允许目标UID/GID与ctime发生相应变化；不承诺ctime保持，不递归修改子项。HDD只保留所有者元数据，不冒称内容备份。

## 独立复核与准确清理

父命令退出0，`actualOwnershipBrowser=passed`、`verifiedFixtureRemoved=true`；另一个只读进程退出0，重新读取SQL/6 Root/HDD原件并与保存证据逐字节匹配，重算摘要，验证原UID/GID、目标/执行身份及实际结果。准确源/CID不存在，临时身份0，所有者活动/屏障/租约0，11核心不变。永久SQL/Root/HDD历史保留；独立进程不声称清理后重新读取了物理源字节。

本轮实际业务来源和元数据都在HDD，不冒称本轮验证了NVMe业务或异盘内容灾备。前批NVMe内部引擎、安装态RPC、真实隔离PG和生产预览测试仍是各自独立证据。

## 核对器问题与修复

发布后首个核对脚本要求已结束transient单元继续暴露InvocationID而触发断言，部署本身已成功。改用启动时回执中的原ID及同次journal成功终态，再完整检查实际组件/进程/生产SQL和隔离，通过后保存verified；没有重开发布或改记原断言为成功。

首版真实验收器的root `git status` 可刷新用户索引为root:root/0600，导致用户后续Git读被拒绝。已按 `.git` 原所有者准确恢复索引属主，未改工作文件/提交；接续代码设置 `GIT_OPTIONAL_LOCKS=0`，并实测只读status不改变用户索引owner/inode。此为验收器副作用修复，真实页面首轮和独立复核均已通过，不虚构产品故障或额外实机重跑。原验收源码c73ecc2与三个SHA保留在baseline；该预防修复单独提交。

## 证据与验证边界

- 发布：`deploy/artifacts/container-ownership-deploy-d5e62ca/`，before/provenance/verified、原Invocation及unit journal、verification-notes。
- 实机：`deploy/artifacts/container-ownership-browser-f26a676f52ad489aa7d288c441e9ff5f/`，baseline（含冻结验收源码与SHA）、browser/verified/independent-final、after、6组SQL/Root/HDD及取消SQL、7张PNG与screenshots摘要。
- 日志：`/tmp/wd-container-ownership-real-browser-first.log`、`/tmp/wd-container-ownership-independent.log`、`/tmp/wd-ownership-installed-validation.log`、sudoers校验日志。以上私有证据均不入Git；本页只记录非敏感结论。
- 本批实际执行新增验收器Python -W error编译/Node语法、真实7场景与独立只读复核、安装来源/宿主/隔离/生产迁移/准确清理及sudoers检查。前批3581 Unit/247 Integration（7默认原生跳过）、755前端/34所有者生产预览/89关联浏览器、Release/类型/lint/格式保持有效，没有声称这轮重新跑过全部。CI未取得全绿终态，不签全绿。文档更新后8项范围检查通过，7份修改MD的496个本地链接无缺失，`git diff --check`通过。

继续所有者进行中故障矩阵与Unknown当前管理员真实页面，再递归/ACL、可恢复删除/长期清理及其余59项，保持dev修复覆盖、不自动回滚。
