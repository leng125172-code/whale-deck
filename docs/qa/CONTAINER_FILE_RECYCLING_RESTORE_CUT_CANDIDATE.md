# 独立恢复写入前切点候选

2026-11-06，最终候选 `033f80ebbdf677f714b74de0fae85f1ac05a81e7` 新增QA专用 `bound_container_recycle_restore_cut.py`，为后续全新资源的执行中取消/退出等故障门禁准备准确切点。本批未修改产品、安装宿主、发布或创建业务操作；没有向生产helper发送SIGSTOP/SIGCONT，不签收真实执行中取消。

## 新工具的准入与拒绝

只接受固定自有测试目录、原容器实例PID、完整永久恢复行及派生的新执行ID。恢复Root须与C#完整计划/owner/原来源派生执行一致；仅Applying、正确且持久的来源claim、原来源仍Trashed、目标缺席、留存inode九字段/字节/属性与原计划相等时，才可能构成写入前切点。Preparing、终态、来源Restoring/Restored、目标出现或留存消失均不计切点。

进程必须是固定安装路径的restore apply transport，匹配当前systemd MainPID/InvocationID、确切cgroup/argv/root身份，以及容器root FD、新恢复操作锁FD、原回收操作锁FD三个不同的内核身份。使用pidfd暂停后再次检查进程、记录、claim和物理状态；任何切点错过或错配不重放Apply，未停止的描述符不发CONT。恢复与原回收的服务前缀/transport/操作空间不混用，没有通用PID信号CLI或新增产品暂停端点。

私有记录读取支持完整64KiB格式，拒绝非root/非0600、符号链接、硬链接、重复键、NaN、过长或短读；原子替换导致的失效采样只重读。切点证明明确 `hddDataReread=false`，完整当前HDD字节必须由门禁父进程另行核验，不能从历史historyVerified标签推断。

## 实际验证与首轮失败

新增12项及关联回归共51项在Linux root临时环境通过、零跳过；包含真实临时文件、原生Applying记录与自有测试子进程SIGKILL切点，以及持有三个FD的真实无关子进程拒绝。Proc正向匹配是明确的目录替身，不冒称实际systemd helper暂停。未接真实Authentik/浏览器故障门禁。

首轮12项中2项因夹具仍按上一层目录查找留存文件而FileNotFoundError；实际源已移入work。修正仅涉及测试路径，初稿源码及失败摘要保留，之后才得到51项全通过。后续追加拒绝来源记录未知或布尔schema版本，51项再次全部通过；初版ee710的只读安装/终态核对保留，最终版再次完整读取生产保持快照。Python语法和范围检查另行验证。

真实生产只读核对：三个安装helper逐字节匹配3d80d17；两条已有Completed执行与完整计划/派生ID匹配，并明确拒绝作为切点，该终态判定不读取已清理的物理命名空间。实际Agent进程也被拒绝作为restore transport，活动restore helper为0。没有新RPC或业务/身份/Root/HDD写入；前后2346保护、11核心、全部Root/HDD/宿主与旧业务完整行摘要保持，活动0。运行应用及两宿主仍3d80d17，开发用户NOPASSWD:ALL保持。

## 下一步与交付边界

私有证据 `deploy/artifacts/container-recycle-restore-cut-73bb2f43239640079b59e3e7180c2201` 共126文件，含109份源码闭包、最终两份差异源码、初稿失败、两轮51项日志、真实只读与前后保持快照。`CUT_EVIDENCE_SHA256.json` 摘要 `25eeb37c79daf27b232c7d459216a12150a451f5033c6a293559ab0aba55a876`。8范围检查、939本地链接及第1节目标正文保持校验通过；私有配置和凭据不入仓库或图谱。

需另接真实页面与独立进程门禁，使用全新自有来源，在准确暂停及当前HDD核验后由页面请求取消，证明原helper退出、Canceled/Aborted、来源仍留存及本操作未恢复、Job摘要/原引用/权限/退出与清理前后保持。尚未取得这些实机证据；常规恢复已通过的事实见[常规整链](CONTAINER_FILE_RECYCLING_RESTORE_REAL_UI.md)，不重做或补签。

之后继续执行中正常退出、实时撤权、Worker/Agent重启与自然租约、Unknown/实例变化、跨写入、长期清理及其余59项。候选通过不增加完整主任务/阶段签收；17项移出不算完成，整体25%–35%粗估、整项0/59、阶段0/6、酒馆/供应商及Live2D子计划0%保持。
