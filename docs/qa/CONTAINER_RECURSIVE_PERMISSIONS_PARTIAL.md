# 递归普通权限：真实进行中取消与Partial跨管理员处置

2026-11-02。产品保持已安装`0dede0804a5f9d1847c23744b90c7e3bae5f7751`/Agent 1.49，本批不重复发布、不回滚。新增真实故障门禁与只读核验器；通过的是**一次实际部分修改后的页面取消及不同管理员明确处置**，不代表全部递归权限或59项完成。原常规6场景仍见[前批证据](CONTAINER_RECURSIVE_PERMISSIONS_REAL_UI.md)，没有重做它们。

## 实际切点、取消和计数

门禁冻结源码`c154d0ab923c3ca31fafc708897c0e20d3f0a772`。一个全新独占、无网络、非root UID1000:1000、只读容器根、限资源容器，仅挂载准确自有HDD目录。测试树为16文件+2目录（含根），每文件完整2097159字节及user.whaledeck-recursive属性；各项混合UID/GID2100..2117/3100..3117及固定纳秒mtime。原模式为文件0640/目录0750，明确目标为0600/0700，预检18项均预计修改。

正常Chrome/Authentik可见登录后，通过实际Vue生成预检。在其他任务/终端/锁/屏障均0时，短暂停止原Worker容器，页面确认原任务为Pending，再启动同一Worker容器以预先布置观察器；没有重建镜像、修改租约/时钟、SQL/缓存/Root回执或安装产品故障钩子。

观察器只用原SQL计划、准确容器instance PID、容器root FD和原operation lock FD两份内核身份，以及准确pidfd定位已装固定apply transport。信号前后重新验证，SIGSTOP时第一项实际权限已改且其Root子回执Completed，聚合仍Applying、finishedThrough=0/startedThrough=1、根目录保持完整原九字段。不是凭计时猜测或任意PID停止。

真实320×600暗色页面先选择“继续等待”（取消POST为0），再“确认请求取消”（原任务取消POST恰好1次）。产品自行在4.943秒结束原helper，验收器没有强杀它；接下来按原ID/原计划观察恢复，没有继续修改余项或自动恢复已改模式。

| 实际事实 | SQL / Root / 页面回执对账 |
| --- | --- |
| 状态 | Partial，保留publish_barrier，completed_at为空，执行Job仍Running |
| 已完成核对 / 实际修改 | 1 / 1，修改数包含在完成数内 |
| 已中止 / 未开始 / 待证明 | 1 / 16 / 0；互斥结果合计18 |
| 租约 | lease_token和lease_expires均为空，不自动Claim Partial续做 |
| HDD | 聚合历史1份、已完成子项历史1份；已中止子项没有伪造历史 |
| Root子项 | 1 Completed、1 Aborted；16项没有子回执，源九字段不变 |

原Authentik Subject/计划/执行Job未换；登记1次、确认1次、取消1次、文件内容PUT为0。真实页面显示Partial、实际逐项结果、保留保护警告和异常处置入口，不显示整树成功或可再次确认。保留的Partial截图是滚动至警告/原任务入口后的页面，不能将其冒称全部计数同时在首屏。

## 原账号删除后另一管理员真实处置

原临时账号正常用户菜单双端退出并实际删除。新建另一临时管理员，以新Chrome会话正常可见登录/files，在实际“容器递归权限修改异常处置”中定位唯一原ID：

1. 选择处置后先点“暂不处置”，acknowledge POST为0；父进程确认原SQL快照、保护、Root及实际完整树均不变。
2. 再明确“确认处置并释放保护”，唯一acknowledge POST返回202，页面GET原回执后展示不代表整树成功的结果。
3. SQL为Acknowledged，原执行Job以Failed结束，原计数与历史证明保留，保护释放，未处理Outbox为0；acknowledged_by为新Subject，原actor_subject不改。

原Root聚合/子回执和HDD聚合/子历史原字节保持不变，原目录树完整字节、inode、UID/GID、mtime和xattr保留，仅已证明的第一项模式与必要ctime变化。处置不续做、不自动回滚、不删除源文件或历史。

追加一次迟到原apply直接通过已装固定privileged helper，返回原Partial/replayed且所有计数保留；再读全树九字段/完整内容及Root原字节不变。**该迟到探针不是单独的签名UDS RPC，不借用本批实际业务链的Agent签名来称其已做签名RPC重放验收。**

## 独立核验、权限与安全边界

- 主进程退出0，verifiedFixtureRemoved=true：每个文件完整读取后，逐项准确unlink和空目录rmdir；准确CID及自有目录不存在。没有递归删除生产目录；永久SQL/Root/HDD全部保留。
- 另一个只读进程先通过基础核验；随后冻结`3fbbd45`增强核验器重读实际SQL/Root/已attest HDD，与原始私有字节相等。独立核对迟到回执、计数/outcomeDigest、原Subject与新处置Subject、准确清理和4张320×600 PNG。明确不声称清理后再次读取已删除源字节。
- 6条实际递归审计中，只有唯一partial-acknowledged属于新管理员，其余为原Subject；details仅intentId/containerId/instanceHash/pathHash，全部重算一致，无明文路径或凭据。
- 已安装11个helper/依赖源码按产品0dede08的Git blob逐字核对；实际Agent/MaintenanceHost PID和sourceRevision未变。11核心healthy，8数据库平台容器ID/镜像/运行状态/StartedAt不变，API/Gateway不变，仅同一个Worker容器的StartedAt按故障编排预期改变。
- 源与HDD实际device均2049。本批为同设备元数据历史，不计异盘灾备、NVMe业务覆盖或文件内容备份。user NOPASSWD:ALL保留，Agent授权未扩大；无部署、回滚、主机重启、磁盘填满或外部通知。
- 本批主要验证实际管理员登录、原登录取消与不同当前管理员处置；匿名/普通拒绝、CSRF/Origin及常规6场景为前批同产品版本证据，不冒称本批全量重跑。新增5项边界+原7项浏览器探针+8项范围共20项通过，Python/Node语法及diff检查通过；没有重跑或借用前批3794/308/832全量结果。

## 保留首轮门禁失败

首轮冻结`69edf22`也在真实切点得到相同1/1/1/16/0 Partial，但QA错误要求未处置Outbox为0，实际为2。产品仓储设计仅在终态/明确处置时处理这些原消息，Partial不属于终态；因此等待断言超时，浏览器也按协调超时退出，**该轮不算通过**。

首轮真实SQL/Root切点和pending=2观察、截图、错误位置、正常BFF失败清理acknowledge均保留。其原执行Job为Failed、状态Acknowledged，消息0/保护释放，完整自有文件核对后准确清理，没有通过SQL抹掉任务。修订门禁同时区分Replayed/BindingCurrent/HistoryAvailable观测与永久事实；新增独立迟到回执核验。第二轮使用全新容器/目录/Subject/原ID，完整通过，不复用首轮结果或修改其失败记录。

## 私有证据、命令及后续

- 首轮失败：`deploy/artifacts/container-recursive-permission-faults-6618b8a705ef41afb3979f666bd9bfd8/`。
- 第二轮通过：`deploy/artifacts/container-recursive-permission-faults-46acab0129964cb9a268fd08715d0bd7/`，baseline与冻结SHA、bound-helper/cut-root、SQL/physical、Root/HDD原字节、两次真人页面回执、late-apply/history-proof、audit-proof/installed-and-core-proof、verified/independent-final及4 PNG。root私有，不入Git。
- 日志：`/tmp/wd-recursive-partial-first.log`、`/tmp/wd-recursive-partial-second.log`、`/tmp/wd-recursive-partial-independent.log`、`/tmp/wd-recursive-partial-independent-final.log`。未截图登录页，临时凭据仅驻留内存/子进程环境。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-46acab0129964cb9a268fd08715d0bd7
```

第一条会创建新资源、短暂停启原Worker并真实改变自有树模式，只在无其他活动工作及源码冻结后运行；已有本批回执，不为展示重复执行。第二条只读复核已保留事实和准确清理。

仍待递归退出/实时撤权、Worker强杀自然租约、Agent重启、丢回复/回执、业务Unknown跨管理员及其他故障组合；递归所有者、ACL、可恢复删除/长期清理、P0-05回退来源缺口和其余59项/UI/C#酒馆/统一供应商/AI Live2D继续。17项移出不算完成，master尚未交付。
