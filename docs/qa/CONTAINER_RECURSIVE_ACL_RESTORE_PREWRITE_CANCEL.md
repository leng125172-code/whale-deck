# 递归ACL恢复执行中首项写前取消

2026-11-05，dev；承接[Ready退出与自然到期](CONTAINER_RECURSIVE_ACL_RESTORE_READY_LOGOUT.md)。本批目标是新26对象来源真实确认后，验证当前ACL前像已留存但首项未改写时的页面取消；不把Ready取消或正常递归ACL旧矩阵替代为RESTORE执行中证据。产品仍eeb24dd，不扩大Agent授权、不部署或回滚产品。

## 候选与安全边界（执行前）

新增RESTORE专属观察器：只接受自有wdv-container-publish目录、当前容器实例、26项access恢复（25文件/1目录/26计划修改）。根据实际apply transport argv、Root UID、容器根FD与原执行操作锁FD选择候选，pidfd必须仍指向原PID与/proc启动时间，前后核对，再核验精确system.slice cgroup、MainPID、InvocationID与进程单一INVOCATION_ID。unit后缀是随机driver ID，通过内核绑定识别，不按执行ID猜测单元名。只发送SIGSTOP与必要SIGCONT，不提供任意PID/路径入口；只有本观察器暂停过的进程才会恢复，不由QA发送SIGKILL。

暂停后必须再次确认Root Applying/historyVerified、完整当前树仍原样、首项未完成，以及首子项（若已建立）为Preparing/Prepared/尚未写入的Applying且actualIdentity/aclConfirmed空、version空。原HDD聚合与当前26项前像、选定来源/frame、完整manifest/逐项目标摘要验证后，才允许真实页面取消。若错过边界即失败，不自动重新Apply、不回放旧请求。

页面路径：正常Authentik登录→新来源历史选择→恢复Ready/原引用重载→当前Worker准确停止→真实确认得到Pending→同Worker启动与专属观察器绑定→真实页面Applying→撤销取消框保持POST0→明确取消1次。产品须终止原pidfd对应helper，原执行Inspect/recover后Canceled/Aborted、完成/变更/Unknown均0、0或1子项Aborted，其余NotStarted；聚合当前ACL前像HDD保持，实际完整文件/九字段/ACL/xattr不变。Worker无论失败或成功都恢复同ID/镜像并等healthy，回执只白名单身份/启动/健康，不保存Config.Env或Health.Log。

早期中止证明独立支持：聚合前像已留存而未开始子项、首项已建立但无子历史、首项有已验证历史但尚未修改；这些状态不能套用旧的无历史Aborted证明。每项可选子记录、HDD类型/独立ID域/owner/父manifest、原前像、五计数和outcome摘要严格核对；Partial/Unknown、目标变化或额外子JSON不能被签为零写入。SQL/双Job/5条精确审计、当前API终态/权限/正常双端退出、清理前独立实读与清理后原件复核均接入。

16项新专项及关联11Ready/10分页/10业务/10历史，共57项Root工具与8范围通过，零跳过，Python/Node/YAML及diff检查通过。临时真实Linux ACL/HDD测试覆盖4对象的BeforeEntry/Prepared/Applying早期异常恢复；真实Linux pidfd验证存活self与自有短子进程退出；其余SQL/进程标识负例为显式夹具，不是安装态RPC/真实页面签收。新26对象安装态门禁尚未运行。

复核期间补上pidfd fdinfo-Pid/启动时间/存活与信号前后绑定、仅已暂停进程恢复，覆盖PID退出/替换反例；增加Applying但全树未变的合法子项边界；Worker回执排除运行凭据。均在冻结前修订，未执行实机SIGSTOP/Worker操作，也没有通过放宽物理未变或HDD要求取得通过。

后续保留完整真实运行回执、失败及修复记录；本批只验执行中写前取消，不签执行中正常退出、写后/撤权/重启/Partial/Unknown/来源异常、删除/长期清理、维护连续性与其余59项。17项移出不算完成。
