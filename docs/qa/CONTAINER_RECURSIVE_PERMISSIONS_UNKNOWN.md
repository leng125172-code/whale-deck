# 递归普通权限：目标容器消失后的Unknown与不同管理员处置

2026-11-02。运行产品保持`0dede0804a5f9d1847c23744b90c7e3bae5f7751`/Agent 1.49。本批未发布/升级/回滚，验收源码冻结`300d19ecb37b4335b66a7925c05e0fdecba45b2c`，依赖的15份验收源码与Git blob/SHA对账。**新增分支首次完整执行通过**；此前[取消/Partial](CONTAINER_RECURSIVE_PERMISSIONS_PARTIAL.md)和[Worker强杀/原租约](CONTAINER_RECURSIVE_PERMISSIONS_WORKER_CRASH.md)没有重做。完整文件模块和59项仍未签收。

## 先证明真实修改，再失去原容器绑定

正常可见Chrome/Authentik登录，经实际Vue预检/确认、永久SQL/双Job/Worker、已装签名Agent和固定helper。仅新建一个准确随机命名、无网络、只读根、非root1000:1000、限资源、唯一自有HDD bind的容器；不修改已有生产容器/数据。不注入Cookie/响应，不改SQL/缓存/Root、时钟或租约，无产品故障钩子。

源树16文件+2目录（含根），每文件完整2097159字节、普通user属性、纳秒mtime与混合UID/GID2100..2117/3100..3117。预检明确文件0600/目录0700，原模式0640/0750，18项均预计修改。原fixture自带文件指纹及内容SHA也记录在baseline。

无其他任务/终端/锁/屏障时，先短暂停启同一Worker以布置观察器；没有强杀Worker或重启宿主。原helper由准确固定argv、容器root FD与原operation lock FD两份内核身份和pidfd绑定，信号前后复核。切点聚合Applying、finishedThrough=0/startedThrough=1；第一项已实际chmod且原子回执Completed，父进程保存全树物理九字段及完整内容，物理修改数为1。

原真实320×600页面仅查询/截图，登记1/确认1/取消0/处置0/内容PUT0。再次排除其他活动工作后，调用fixture的准确remove_owned：先核验CID/name/validation label/固定镜像、隔离与唯一挂载，再只对这个CID执行rm，随后确认目标不存在。宿主自有源目录/所有字节与九字段仍为切点状态，没有随容器删除。

原账号在实际页面正常菜单双端退出；产品在2.757秒内结束原helper（观测上界），验收器未强杀helper。目标Docker绑定已消失，原Root恢复不能再证明原实例目录树，按原ID永久收敛Unknown，不能用宿主仍能读取文件来伪装容器实例仍有效。

## Unknown汇总与独立物理事实分开

| 事实层次 | 本批实际结果 |
| --- | --- |
| SQL/Root汇总 | 0完成核对、0已证明修改、0中止、0未开始、18待证明，outcomeDigest为空 |
| 业务状态 | Unknown；publish_barrier=true、completed_at为空；lease token/expiry清空 |
| 原Job | 计划Succeeded；原执行仍Running，未处理消息保留，不能当作终态成功/取消 |
| 宿主侧完整源树 | 确认1项已修改；全树与移除容器前切点完全相同，无后续修改/自动恢复 |
| HDD聚合历史 | 1份，已验证原清单/计划/摘要；不是文件内容备份 |
| 子回执与子历史 | 1份Completed子回执及对应1份HDD历史，均保留；其余不伪造终态回执 |

**汇总changedCount=0表示该次Unknown汇总未证明任何当前原实例变更，不等于物理修改为0。** 页面五类实际计数为0/0/0/0/18，同时保留“结果未知/保护已保留”和不得推断零修改或成功的警告。宿主实盘1项修改单独保存在QA证明；不把聚合未知改成1成功+17未开始，也不把原子回执强行改成Aborted。

计划/清单/子计划及各独立历史域摘要重算，原UID/GID/inode/完整内容/mtime/user属性保留，只有已证明模式及必要ctime变化。源树集合、每项源计划身份、实际物理状态和原子历史核实；**没有宣称18个子项都完成当前实例的最终状态证明**。

## 原账号删除后真实当前管理员明确处置

原临时Authentik账号双端退出后实际删除。另一个临时管理员以独立Chrome会话正常可见登录/files，在实际递归异常处置面板定位原ID，逐一核对五类dt/dd及18待证明，保存320×600暗色计数PNG：

1. 先选择“暂不处置”，acknowledge POST为0；父进程重读原SQL快照、保护、Root和完整源树，均不变。
2. 再明确“确认处置并释放保护”，唯一POST返回202，页面GET原回执并展示处置不代表整树成功、不续做/自动恢复/删除原对象。
3. 原业务为Acknowledged，原执行Job以Failed结束，保护释放、Outbox0；新Subject仅为acknowledged_by，原actor_subject/Job/计划/Root开始/汇总计数/摘要/历史保留。

Root仍Unknown，原聚合和子回执/HDD原字节不变；没有借处置进行chmod或删除历史。原固定privileged helper迟到apply同一原请求，仍返回Unknown/replayed/18待证明，源全树九字段/完整字节及Root字节不变。**该追加迟到探针不是单独签名UDS RPC**；不借主业务链的签名来重复签收该层。

## 独立核验与准确清理

- 主门禁退出0，verifiedFixtureRemoved=true。在完整物理证明及原意图明确处置后，逐文件准确unlink、逐空目录rmdir；不恢复权限来伪造未发生。原CID已按绑定移除，自有源目录准确消失；永久SQL/Root/HDD保留。
- 独立只读进程重读实际SQL/Root/已attest HDD，与私有原件字节一致；验证Unknown18、物理1修改、迟到回执、不同Subject、准确清理及4张320×600 PNG。不声称清理后重新读取已删除源字节。
- 5条实际递归审计中，唯一unknown-acknowledged属于新管理员，其余为原Subject；details仅intentId/containerId及重算一致的instanceHash/pathHash，无明文路径/凭据。
- 已装11个helper/依赖按产品0dede08 Git blob逐字一致，两个实际宿主PID/sourceRevision不变。11核心healthy、8数据库平台容器ID/镜像/状态/StartedAt不变，API/Gateway不变，只有同一个Worker为布置观察器预期停启。
- 9项探针边界（新增2个Unknown错误计数/非整数预算拒绝）、7项关联浏览器探针、8项范围共**24项通过**，Python/Node语法和diff通过。没有重跑/借用前批后端/前端全量或把旧发布CI报成新QA提交CI。
- 实际源/HDDdevice均2049，只计同设备元数据历史，不计NVMe业务、异盘灾备或HDD失效恢复。user NOPASSWD授权保留；产品Agent权限未扩大，没有主机重启、磁盘填满或外部通知。

## 私有证据及后续

本批私有目录：`deploy/artifacts/container-recursive-permission-faults-7720bb9b7dce4cdab76c36768bc0b29f/`。包含baseline/SHA、bound-helper/cut-root、pre-disappearance SQL/target-disappearance/physical-cut、原页面/新管理员回执、原/最终SQL/物理状态、Root/HDD原件、history/late/audit/installed-core/verified/independent-final及4 PNG。沿用门禁的`partial-*`文件名只是私有归档命名，**其本批实际内容state为Unknown**，不把它们计为新的Partial场景。

日志：`/tmp/wd-recursive-unknown-first.log`、`/tmp/wd-recursive-unknown-independent.log`；均私有，无登录页截图，凭据只在内存/子进程环境。原未知字段与完整原件保留，没有擦掉失败/未知事实来通过。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751 target-disappearance
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-7720bb9b7dce4cdab76c36768bc0b29f
```

已有回执，不为展示重复第一条；它会真实移除其准确自建容器且需要冻结源码与无其他活动工作。第二条只读核验保留证据，后续运行版本/进程变化需区分新运行边界。

仍需原实例保留时的退出/实时撤权、Agent重启、其他Worker/回执丢失及HDD/路径/跨盘故障组合、Unknown期间其他真实写入的延期/释放实机证据、首次登记未到达，以及递归所有者/ACL/可恢复删除/长期清理、P0-05回退来源和其余59项/UI/C#酒馆/供应商/AI Live2D。本批仅签收目标消失这一Unknown分支，17移出不算完成，master尚未交付；[进度比例和代码量](../PROGRESS_ESTIMATE.md)仍为独立规划估算。
