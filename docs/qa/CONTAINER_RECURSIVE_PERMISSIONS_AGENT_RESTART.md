# 递归普通权限：进行中Agent重启与维护入口连续观测

2026-11-02。产品保持`0dede0804a5f9d1847c23744b90c7e3bae5f7751`/协议1.49，无发布、升级或回滚。真实门禁源码冻结`6d257a21c5f503ae3195684e0b0dcb0aff2cde0f`，19份验收依赖源码与Git blob/SHA对账，**首次完整执行通过**。本批只新增“一项实际修改后重启Agent”这一切点，之前[取消/Partial](CONTAINER_RECURSIVE_PERMISSIONS_PARTIAL.md)、[Worker强杀/租约](CONTAINER_RECURSIVE_PERMISSIONS_WORKER_CRASH.md)、[Unknown及跨写入](CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN_BARRIER.md)不重做。

## 实际切点与唯一固定Agent重启

仅一个准确自有、无网络、非root1000:1000、只读容器根、限资源、唯一HDD bind的测试容器。树为16文件+2目录，每文件2097159完整字节、普通user属性、混合UID/GID和纳秒mtime；明确预检文件0600/目录0700，原模式0640/0750，18项预计修改。容器实例在本批没有重启或移除，直至最后准确清理。

正常可见Chrome/Authentik登录→Vue预检/唯一确认→永久SQL/双Job/Worker/已装签名Agent/helper。为预先布置观察器仅短暂停启同一个Worker，其他Job/终端/宿主锁/文件屏障均0。按准确pidfd、固定argv、容器root FD和原operation lock FD绑定，信号前后重验；Root聚合Applying、finishedThrough=0/startedThrough=1，第一项实际chmod完成且原子回执Completed。保存全树完整内容与九字段、SQL及Root切点，没有产品故障钩子。

再次只豁免原两个Job并核对其他transient unit/cgroup均空，重启前逐字核对11个已装helper/依赖与产品0dede08 Git blob。原实际320×600页面只查询原任务，**没有点击取消，取消POST为0**，然后只通过宿主QA权限调用一次固定`systemctl restart whaledeck-agent.service`。这不是普通用户可从面板停止核心的授权。

| 运行进程事实 | 重启前 | 重启后 |
| --- | --- | --- |
| Agent MainPID | 1432315 | 1851523 |
| Agent实际apphost/sourceRevision | WhaleDeck.Agent / 0dede08 | WhaleDeck.Agent / 0dede08 |
| MaintenanceHost MainPID | 1432309 | 1432309，不变 |
| MaintenanceHost实际apphost/sourceRevision | WhaleDeck.MaintenanceHost / 0dede08 | 同源，不变 |

Type=simple启动时只接受真正完成exec的新apphost及build-info，不把systemd的临时PID当应用，也不把磁盘文件当运行进程。原helper在产品控制下于2.290秒内结束（观测上界），验收器未强杀helper；服务重启观察窗口为UTC10:12:17.502062至10:12:20.110493。

## 稳定维护入口实际HTTP采样

独立观察线程在重启前启动，跨越重启至新进程身份核验后再停止。固定匿名只读`http://127.0.0.1:8080/maintenance/status`，禁用代理/Cookie，响应最多2048字节、短超时，只接受200/healthy/Ready/no-store以及已知公开字段，不写生产状态文件。

- 17次样本，UTC10:12:17.249255至10:12:20.495405，覆盖重启前、期间和结束后；每次HTTP200/healthy/Ready/no-store，响应136字节。
- 最大实际响应0.0062秒。MaintenanceHost全程是原PID/来源，API/Gateway未重启。
- 这是本机维护HTTP入口的采样证据；其健康读取API live状态，**不把它称为Agent在重启期间健康，也不外推为所有网卡/远程客户端/断电场景签收**。约200ms采样不是对采样间隙零中断的数学证明。

## 原操作只观察恢复，源树不续做

停止时允许正在开始的那一项收尾，但已完成项的九字段及未开始项必须不变；helper退出后到Worker恢复、页面处置和迟到重放，全树不许再变。本批实际**切点树与helper退出后的树完全相同**，没有额外项收尾，更没有重启后续做。

原intent/执行Job/Subject/容器实例/计划及签名/Root开始时间不换，Worker按原操作观察，SQL/Root为Partial：**1完成核对、1实际修改、1中止、16未开始、0待证明**，互斥总数18。租约清空，保护保留，原执行Job仍Running；不是整树成功。原HDD聚合1份、已完成项子历史1份、Root两个子回执Completed/Aborted逐一对账，不创建16个未开始项的假成功回执。

完整2097159字节、inode、UID/GID、mtime/user属性保留，仅第一项明确模式与必要ctime变化。原角色仍在时，真实页面按dt/dd核对五类计数，Partial警告、原任务查询和处置入口可见，正常双端退出。原临时账号实际删除后，另一管理员正常可见登录，在320×600明色页面先暂不处置（POST0），再明确确认（唯一POST202），原Job Failed/Acknowledged，保护释放，实际计数/Root/HDD/完整源树保留。

追加已装固定privileged helper迟到apply原请求，仍Partial/replayed，Root原字节及源全树九字段/完整内容不变；**该追加探针不是单独签名UDS RPC**，不借主业务链签名外推。没有恢复模式来伪装未发生。

## 独立核验、清理与QA加固

- 主进程退出0、verifiedFixtureRemoved=true。先核对每文件内容/九字段/目录集合，再准确unlink/空目录rmdir和CID清理；原SQL/Root/HDD永久历史保留。
- 另一个只读进程重读实际SQL/Root/已attest HDD，与原始私有字节一致，重算计划/清单/子计划/版本/历史/outcome独立域摘要，核对新旧宿主、17个维护样本、切点/停止后/恢复后物理状态、迟到回执与准确清理。它明确不声称清理后重读已删源字节。
- 5条实际递归审计，唯一partial-acknowledged为新Subject，其余为原Subject；details只含intentId/containerId与重算的instanceHash/pathHash。5张320×600 PNG通过，目视复核暗色五类结果与明色完整暂不处置/确认按钮。
- 11个helper重启前后字节一致且与0dede08匹配，11核心healthy、8数据库平台容器ID/镜像/状态/StartedAt不变；API/Gateway不变，原Worker仅为布置观察器预期停启。实际新Agent PID保持到最终独立核验，MaintenanceHost未重启。
- 真门禁执行前29项探针/关联/范围通过；同批后续QA异常收尾加固后共**30项通过**，Python/Node语法及diff通过。未重跑/借用旧后端/前端全量或新产品CI。
- 观察线程退出回调保证任何异常都先停止HTTP采样再join。后续提交`5496626`把Agent清理放入try/finally，保证其状态检查/启动/身份核验抛错时也调用原Worker恢复；离线注入三种错误路径验证通过。**这是QA收尾错误注入，不是真实Agent启动失败；没有为了验证该加固再次重启Agent，也不把后续源码当真实门禁版本6d257a2。**
- 来源/HDD实际device均2049，只计同设备元数据留存，不计NVMe业务、异盘灾备或HDD故障。user NOPASSWD授权保留，产品Agent权限未扩大，无主机重启、磁盘填满、真实外部通知或自动回滚。

## 私有证据及后续

`deploy/artifacts/container-recursive-permission-faults-78110d41009a4c208d5d6559c143e2c4/`：baseline/19份冻结SHA/安装前helper SHA、bound-helper/cut-root、pre-agent-restart SQL、physical-cut/after-helper-exit/partial/final、agent-restart（17样本与前后PID/时刻）、host-after、SQL/Root/HDD原件、实际两用户回执、history/late/audit/installed-core/verified/independent-final及5 PNG。均私有，不入Git。日志为`/tmp/wd-recursive-agent-restart-first.log`、`/tmp/wd-recursive-agent-restart-independent.log`；无登录截图，凭据仅在内存/子进程环境。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751 agent-restart
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-78110d41009a4c208d5d6559c143e2c4
```

已有本批回执，不为展示重复第一条；它会在准确自有切点真实重启固定Agent，需要源码冻结/无其他工作。第二条只读复核记录；后续若Agent再次重启/产品换版，应区分新的运行边界，不重写本批原PID证据。

仍需原实例保留时退出/实时撤权、其他Agent/Worker/回执/HDD/路径与跨盘组合、其他写入类型及首次登记未到达，递归所有者/ACL/可恢复删除/长期清理、P0-05手动回退来源与其余59项/UI/C#酒馆/供应商/AI Live2D继续。17项移出不计完成，master尚未交付。
