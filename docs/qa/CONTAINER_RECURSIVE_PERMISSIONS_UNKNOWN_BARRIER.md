# 递归Unknown保护：另一容器目录任务实际延期与释放后原任务执行

2026-11-02。产品保持已安装`0dede0804a5f9d1847c23744b90c7e3bae5f7751`/Agent 1.49，无发布、升级或回滚。验收源码冻结`7e8af47cecb31d9f5199c29b90dfa5aedb4df4f5`，19份依赖源码与Git blob/SHA核对；本批第二轮完整通过，首轮QA状态枚举错误与正常失败清理保留。此前[目标消失Unknown](CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN.md)的原回执不重做、不改写，本批以全新资源验证新增的跨容器写入组合。

## 真实递归Unknown与两个独占测试容器

两个独占随机命名容器均使用固定已有镜像、无网络、只读容器根、非root1000:1000、限资源、唯一自有HDD bind。原递归树为16文件+2目录，每文件完整2097159字节及普通user属性/混合UID/GID/固定纳秒mtime；另一容器仅有其自带原文件，之后用于排队创建空目录。

实际可见Authentik登录→Vue预检/确认→永久SQL/Worker/已装签名Agent/helper。布置准确pidfd/容器root FD/原operation lock FD观察器所需的短暂停启仅针对原Worker容器，没有强杀/重建核心或变更宿主。第一项实际修改且子回执Completed后，仅按准确CID/name/label/image/隔离/挂载核验移除本批原容器，源树保留；原页面正常双端退出，产品2.625秒内结束helper（观测上界，非验收器强杀）。

原Root/SQL为Unknown18、保护保留、未完成、无活动租约，原执行Job仍Running；宿主独立证明实际1项修改，不能由汇总changedCount=0推断物理零修改。原临时Authentik账号实际删除后，由另一位当前管理员继续。

## 当前管理员提交另一容器目录任务，至少4次真正延期

新管理员通过正常BFF登录，以真实API目录plan及唯一登记POST提交另一容器的`work/递归未知保护解除后的目录`，不是伪造SQL或模拟接口。目录意图为Pending，通用Job初始为Queued；二者是不同状态模型。登记一次，计划/实例/原ID/Subject/签名/请求绑定保留。

不能把“刚登记就有的下次尝试时间”当作已延期证明。观察器只采纳四个**租约已经释放、版本较前次至少增加2、nextAttempt严格推进**的点，并逐次证明Worker原容器/镜像/StartedAt相同且Running：

| 观测点 | 原目录意图版本 | 实际next_attempt_at_utc |
| --- | ---: | --- |
| 1 | 2 | 2026-11-02T17:46:56.534337+08:00 |
| 2 | 4 | 2026-11-02T17:47:03.406262+08:00 |
| 3 | 6 | 2026-11-02T17:47:10.386443+08:00 |
| 4 | 8 | 2026-11-02T17:47:17.405036+08:00 |

这四次claim/defer期间，原目录意图始终Pending、root_started为空、publish_barrier=false、未完成、登记审计1；目标目录、目录Root回执及私有发布目录均不存在。原递归Unknown快照/保护、Root原字节和实盘全树不变。每点显式只豁免原递归两个Job及本目录Job，其他任务/终端/宿主锁/上传与目录屏障为0，排除Worker停机或其他活动工作作为原因。

原递归保护为当时唯一保留的发布保护；代码的共享事务锁/Started检查和实际等待结果一致。没有缩短租约/改时钟、关闭Worker来伪造延期、删保护或重新登记替代任务。

## 真实页面明确处置，原目录任务才执行

同一新Subject在独立正常可见Chrome会话登录/files，320×600暗色页面核对Unknown18，先“暂不处置”（acknowledge POST0）；父进程确认原Unknown/完整源树仍不变，目录任务仍未开始，实际目录仍不存在。然后唯一明确处置POST202，经GET原回执确认Acknowledged，原递归Job以Failed结束，Root仍Unknown/18待证明，原历史与物理修改不改。

| 同一数据库持久化事实 | 实际时间 |
| --- | --- |
| 原递归completed_at（明确处置） | 2026-11-02T17:47:25.137157+08:00 |
| 等待目录root_started_at | 2026-11-02T17:47:25.353502+08:00 |
| 等待目录completed_at | 2026-11-02T17:47:26.202956+08:00 |

等待目录保持原ID、Subject、实例、计划和请求哈希，最终Succeeded/Root Completed、原登记审计1、Outbox0，无租约/保护；没有重新登记。当前管理员浏览器仅查询这个原等待任务，成功后打开实际另一容器/work，确认目录名完全进入viewport并保存真实PNG，页面无横向溢出。浏览器目录登记/plan POST为0、文件内容PUT为0；**本批目录登记来自同Subject的真实BFF API会话，不冒称浏览器点击“新建目录”**。两种会话各自正常登录，未注入Cookie或共享伪造会话。

源原文件的完整内容/指纹不变；实际空目录inode与原Root pendingIdentity一致，UID/GID1000:1000、模式0750，私有目录inode与回执一致且为空。验证SQL/Root/计划独立域摘要/版本/物理后，只精确rmdir该空目录与空私有目录；不递归清理未知对象。两个fixture自带文件及原递归源树也在全部证明后准确清理，两个准确CID/源目录不存在。

## 永久历史、独立核验与边界

- 原递归完整字节、九字段、UID/GID、mtime/xattr与Root/HDD原件跨延期、处置、目录发布及迟到原固定helper回放不变；Root保持Unknown，迟到不续做。追加迟到探针不是单独签名UDS RPC。元数据历史不包含文件内容。
- 主门禁退出0，两个verifiedFixtureRemoved均true。独立只读进程重新读取两份实际SQL快照、原Root/HDD及目录Root字节，重算原计划/清单/历史/outcome规则和目录独立摘要；核对四次版本/租约/延期时间、处置与开始时序、唯一登记/同原Job、实际inode现场保存证据、5张320×600 PNG及两组准确清理。它不声称清理后重读已删源字节。
- 递归5条审计：仅唯一unknown-acknowledged为新管理员，其他为原Subject，details仅原ID/containerID和重算的instanceHash/pathHash。目录2条审计均属新管理员，唯一mkdir.accepted和终态结果，details仅重算的pathHash/instanceHash。原Subject未被处置人覆盖，无凭据/明文路径进入审计。
- 已装11个helper/依赖与产品0dede08 Git blob逐字一致，实际Agent/MaintenanceHost PID/sourceRevision不变。11核心healthy，8数据库平台容器ID/镜像/状态/StartedAt不变；API/Gateway不变，仅同一个Worker为布置观察器预期停启，没有本批强杀。
- 新增2个等待意图/Job身份、提前发布/终态拒绝边界；连同原9探针、7关联浏览器探针及8范围，**26项通过**。Python/Node语法、diff与文档链接通过；不借用旧后端/前端全量或CI冒称本批全量。
- 源/HDD同设备2049，只计元数据历史与原文件保留，不计NVMe业务、异盘灾备或硬盘故障。未改SQL/缓存/Root、时钟、租约或产品钩子；user NOPASSWD保留，Agent授权不扩大。

## 首轮QA失败保留

首轮冻结`ab82087`在“等待另一容器目录任务”断言失败：错误把通用Job初始状态也当作Pending，真实模型为Queued，目录意图才是Pending。未取得四次延期或恢复页面完整证据，不能计通过。失败后以正常BFF明确处置原Unknown，原递归Job Failed/Acknowledged；等待目录随后Succeeded，原Root/物理空目录核对后精确清理，两个fixture均准确移除。原失败日志/SQL/Root/清理证据保留，没有SQL抹账。

修正为按各自模型区分Pending意图与Queued/Running Job，保留所有不许提前开始/完成/重登记的检查；新增每轮最新SQL观察，避免失败前快照丢失。第二轮全新容器/目录/Subject/原ID完成完整组合，旧失败不改成通过。失败清理若遇等待目录Unknown，也仅走该准确原ID的正常BFF处置；无法证明的对象保留，失败不计通过。

## 私有证据与后续

- 首轮失败：`deploy/artifacts/container-recursive-permission-faults-b9bfc297beed4f69b0755b1dcb3eec58/`。
- 第二轮通过：`deploy/artifacts/container-recursive-permission-faults-1128a448e9e24e54a4cf6a83b91e5d67/`。包含19份冻结SHA、递归切点/移除/SQL/Root/HDD/完整物理原件、waiting-baseline/blocked/latest-observation/final-sql/physical、独立waiting-proof目录内的目录Root原字节、两用户正常页面回执、late-apply、audit/installed-core/verified/independent-final/waiting-independent及5 PNG。均私有，不入Git。
- 日志：`/tmp/wd-recursive-unknown-barrier-first.log`、`/tmp/wd-recursive-unknown-barrier-second.log`、`/tmp/wd-recursive-unknown-barrier-independent.log`。无登录截图；临时凭据仅在内存/子进程环境，身份实际删除。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751 target-disappearance-barrier
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-1128a448e9e24e54a4cf6a83b91e5d67
```

已有本批回执，不为展示重复第一条，它真实创建/移除准确自有容器和目录，要求源码冻结/无其他工作。第二条只读复核保留证据，后续运行环境变化需区分新的边界。

本批仅签收**跨容器新建目录这一写入组合**；其他写入类型、同资源冲突、原实例保留时退出/实时撤权、Agent重启、其他Worker/回执/HDD/跨盘故障、首次登记未到达，以及递归所有者/ACL/可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D仍待。目录模块的即时身份撤销不由本批签收，17移出不计完成，master尚未交付。
