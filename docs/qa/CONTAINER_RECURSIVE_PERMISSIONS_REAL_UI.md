# 递归普通权限：dev覆盖发布与真实页面常规整链

> 后续2026-11-02：产品0dede08/1.49继续运行，真实进行中取消与Partial不同管理员处置、原计数/全文件/HDD及独立核验已通过，首轮QA错误与修复保留，见[后续Partial证据](CONTAINER_RECURSIVE_PERMISSIONS_PARTIAL.md)。Unknown和其他故障组合仍待；以下原批次正文与结果保留，不从历史“尚未”推断当前未接业务。

2026-11-02。实际运行产品`0dede0804a5f9d1847c23744b90c7e3bae5f7751`、Agent协议1.49。本批完成常规真实身份→Vue→API/永久SQL/Worker→Agent→实际目录树/HDD链路，不代表完整递归权限签收：进行中取消/退出/撤权/强杀/重启、业务Partial/Unknown跨管理员处置及其他故障仍需实机；递归所有者、ACL、可恢复删除/长期清理与其余59项继续，17项移出不算完成，master尚未交付。

## 唯一一次正常dev覆盖

运行checkout `/data/GitRepos/whale-deck`原为干净dev/d1fcca8，由仓库所有者通过本机Git源快进到固定0dede08。固定维护helper正常update，仅执行一次；没有自动回滚或重复启动。发布单元`whaledeck-dev-publish-0dede08.service`，InvocationID `f396efd8f67f4c0ca9e02a48315dc9f2`，同次journal明确Deactivated successfully，记录墙钟耗时17min47.399s。较长镜像构建期间没有把中间运行态报成发布完成。

- 六组件实际build-info、API/Worker/Gateway镜像revision及HTTP来源全部匹配0dede08。宿主MainPID的exe device/inode及实际映射DLL的device/inode与安装文件一致，两宿主PID按正常安装重启，非仅查看磁盘新文件。
- 生产迁移`20261102044324_ContainerRecursivePermissionIntents`已应用；验收前新表0行/0屏障。没有生产EnsureCreated或SQL/缓存状态注入。
- 11核心healthy，8个database-platform容器ID/镜像/运行状态/StartedAt与发布前一致。API/Worker无宿主端口、Docker Socket或privileged；两宿主active，已安装runtime/权限/UDS/固定helper/HDD及动态入口validate通过。
- user NOPASSWD:ALL按授权保留，sudo -n仍可用。产品Agent固定最小授权未扩大。发布当时backend/native-postgres/native-valkey/installer成功，frontend仍执行；后续冻结发布源0dede08的CI41402013851五路全部成功。验收器85d7d11为后续QA源，不借用该CI冒称QA新提交也已全绿。

## 真实登录、页面与逐场景实盘

验收源码固定`85d7d11`，与产品发布0dede08区分。新增Python父进程、实际Chrome/正常Authentik用户名密码登录及独立只读核验器；7项离线边界检查与Python -W error/Node语法检查通过。没有API响应替换、Cookie注入、SQL/缓存写入、伪造Root回执或产品故障钩子。临时身份凭据只在内存/子进程环境，不入Git/日志；不截图登录页面。

使用一个准确命名、独占、非root UID1000:1000、无网络、只读容器根、唯一自有HDD bind目录的容器。树为2文件+2目录（含根）；每文件完整2097159字节及user.whaledeck-recursive属性，四项混合UID/GID保留。根属主2100:3100与容器执行FSUID/FSGID1000:1000在UI/SQL/Root分别核实，不将执行身份当原所有者。

每个场景完成后，浏览器必须等待父进程实际重读源树全部九字段、完整文件字节/xattr、SQL/Job/审计及Root/HDD原件并回执，才进入下一场景；避免只核对最后恢复状态而掩盖中间错误。

| 场景 | 文件模式 / 目录模式 | 实际结果 |
| --- | --- | --- |
| complete | 0600 / 0700 | 4完成核对、2实际修改 |
| noop | 0600 / 0700 | 4完成核对、0修改，全树九字段不变 |
| alternate | 0640 / 0750 | 4完成核对、4实际修改 |
| zero | 0000 / 0777 | 显式普通零模式，4完成核对、4实际修改 |
| readable | 0640 / 0750 | 明确恢复可读模式，4完成核对、4实际修改；不冒称还原最初混合模式 |
| cancel | 预检0600 / 0700 | Ready后先继续等待、再明确取消；无执行Job/Root开始/Root回执/HDD历史，实际树不变 |

6场景首次完整执行通过：5 Succeeded/1 Canceled。6次原登记HTTP重试、5次原确认重试均返回原ID/原执行Job；刷新后仅接续原引用，无重复登记/确认，文件内容PUT为0。完成后正常用户菜单双端退出，WD Cookie与Authentik会话失效；普通用户8路403、匿名4路401，缺CSRF400、错误Origin403、缺目录模式400及私有稳定错误码通过。

实际1440/320明暗与320×600取消通过；6张预检产品PNG及SHA清单保存。已目视复核320明色0000/0777、320暗色可读模式、320×600取消：原值/目标模式和计数明确，正文内部滚动，确认与取消完整可见，无页面横向溢出。这些是本批真实登录与实际目录的页面，不复用生产预览图片。

## 永久事实、独立核验与清理

- 11个唯一计划/执行Job、23条递归审计均为原Authentik Subject；审计细节仅intentId/containerId/instanceHash/pathHash，哈希重算一致，无路径/令牌/密码。各原任务、计划、执行、Outbox/事件与实际结果对账，终态无活动租约/屏障/未处理消息。
- 5份Root聚合回执、5份实际HDD聚合、20份Root子项及20份HDD子项逐一验证；原计划/清单/子计划/历史/实际版本/outcomeDigest按各自独立域重算，与SQL及页面计数对应，UID/GID、原inode、文件字节/mtime/xattr保留，只有明确模式与必要ctime变化。HDD保留的是元数据，不冒称文件内容备份。
- 来源与HDD备份位于同一实际设备，本批不计NVMe业务覆盖或异盘灾备；不填满磁盘、不重启主机、不发真实群消息/邮件。
- 父进程退出0、actualRecursivePermissionBrowser=passed且verifiedFixtureRemoved=true。随后另一个只读进程重新读SQL/Root/已attest HDD及私有原件，独立重算并确认5聚合/20子项、准确源/CID与临时身份不存在、11核心ID/镜像/StartedAt不变。它明确不声称清理后重新读取已删除源字节。
- 永久SQL/Root/HDD历史全部保留；没有清屏障、改完成时间、缩短租约或恢复权限来通过检查。进行中故障/Partial/Unknown真实业务处置不在本轮常规签收中。

## 私有证据与后续

- 发布：`deploy/artifacts/container-recursive-permission-deploy-0dede08/`，before/invocation/publish.log/provenance/unit-journal/verified。
- 实机：`deploy/artifacts/container-recursive-permission-browser-983041dfc641408f8eed68dfbdd4dac5/`，baseline（冻结源码与SHA）、逐场景SQL/Root/HDD原件、逐场景physical before/after及协作信号、browser/results/physical-final/verified/independent-final、audit-proof、6 PNG和screenshots摘要。均root私有，不入Git。
- 日志：`/tmp/wd-recursive-deploy-verify.log`、`/tmp/wd-recursive-real-installed-validation.log`、`/tmp/wd-recursive-real-browser-first.log`、`/tmp/wd-recursive-real-browser-independent.log`、`/tmp/wd-recursive-real-probe-final-tests.log`。新增7项探针边界+8项范围检查通过；未冒称本轮重跑前批3794/308/832全量。

下一步继续真实进行中故障/丢回复、Partial/Unknown当前管理员恢复、首次登记未到达等边界，再递归所有者/ACL、可恢复删除/长期清理及其余59项/UI/C#酒馆/统一供应商/AI Live2D。开发继续修复覆盖，不自动回滚dev。

本轮还只读发现维护回退来源记录缺口：固定helper在checkout快进后将HEAD写为previous-revision，当前记录等于新发布0dede08，不能据此指向更新前实际运行的d1fcca8。此问题登记在P0-05继续修复/非破坏验证；本轮未调用rollback，不把update成功报成手动回退已验收。
