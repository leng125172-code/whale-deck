# 容器ACL：写入前Worker强杀、自然租约与Agent重启实机验收

2026-11-03。六组件运行源码仍 `633a2ca42012bbd6f388a67bd128fe3125bfab76`、Agent 1.51，没有重新发布或生产迁移。实际门禁冻结 `ed241e9`，27份验收依赖和11份安装模块逐字节核对；后续 `6fce7dc` 加固独立观察器的Worker实际启动时间与原租约绑定。**两套新独占资源的真实Authentik/Vue/持久SQL/Worker/Agent/原文件-ACL/HDD门禁首轮通过，独立只读首轮及加固后核验通过。** 不重做[取消/正常退出](CONTAINER_ACL_FAULTS.md)、[实时撤权](CONTAINER_ACL_REVOCATION.md)或[常规九场景](CONTAINER_ACL_REAL_UI.md)。

仅签收**业务Applying、Root Preparing/HistoryVerified、ACL系统调用尚未发生**时的两种进程中断。不是ACL已修改后的中断、Unknown、路径/实例交换、递归恢复或删除验收。59主项继续部分实现，17项移出不算完成，master未交付。

## 实际切点、重启范围与自然恢复

每案使用新准确随机容器/源目录/管理员：固定现有镜像、非root1000:1000、无网络、只读根、单一自有HDD bind。真实可见Authentik登录、Vue编辑acl-file.bin的access ACL并预检；Ready后正常停止现有Worker，页面确认唯一原执行Job，再启动同一Worker。目标owner6、named user2201权限6、group_obj0、mask4、other0，目标模式0640但NoChange=false。源UID/GID2100:3100与容器执行身份1000:1000分开。

观察器必须同时绑定固定ACL传输命令/root身份、精确pidfd、容器root FD、原操作0600空锁FD、SQL原计划/规范摘要，再验证父inode、九字段原身份、两类ACL与普通属性后SIGSTOP，暂停后重新证明原源未变。只停本案helper，未安装钩子或修改Root/SQL/缓存/时钟。未抓到真实切点不能算通过。

重启动作前再次核对：除本案原意图/执行Job外其他Job、终端、主机锁为0；上传、目录、复制、移动、普通权限、所有者、两类递归及其他ACL活动/屏障/任一租约均0。其他whaledeck transient单元必须已无活动PID；唯一允许仍活的是已双FD证明的本案helper，不能按任意PID发信号。

| 场景 | 实际动作与产品停止helper | 原任务与物理结果 |
| --- | --- | --- |
| worker-crash | 对准确现有Worker CID发SIGKILL，未自动启动时显式启动原CID/镜像；产品约0.116秒结束helper | 等原租约自然到期后Failed/CONTAINER_ACL_INTERRUPTED；Root Aborted、aclConfirmed=false，原ACL未写 |
| agent-restart | 只执行固定whaledeck-agent.service正常restart；产品约0.015秒结束helper，服务重启总30.634秒 | 原业务Failed/Interrupted、Root Aborted，未重做或恢复ACL；Agent换PID、维护入口保持 |

Worker强杀前后原lease token/expiry**完全相等**；原到期时间为2026-11-03 04:15:44.552486+08:00。断连后实际等待约87.490秒，42次覆盖等待区间的真实采样保持Applying、Running执行Job、原token/expiry、屏障与未取消事实；原Root字节/SHA和完整源字节、ACL、普通属性均保持，直到原到期后才完成。单调时长与墙钟一致，没有缩短、延长租约或强制放行。这里签收的是本批实际未续租的窗口，不以曾允许>=的观察器分支证明不可变。

独立进程在Worker恢复后另读实际Docker ID/镜像/StartedAt；与本案原CID/原镜像、记录的强杀后StartedAt一致，并与下一案开始前真实baseline再次吻合。第二案正常预检准备还会正常停止/启动Worker，因此不把整套最后StartedAt误当第一案强杀后的StartedAt；容器ID/镜像始终不变。

Agent由PID3122630变为3288068，实际exe --build-info仍633a2ca；MaintenanceHost PID3122598、来源均保持。重启前、期间、之后152次有界维护HTTP探测全部200、healthy/Ready、no-store；最大采样间隔0.207412秒、最长响应0.0685秒，原始白名单字段和2KiB响应上限逐次校验。Agent开始重启时有原任务有效租约；本案不宣称Worker继续运行时租约永不正常续期，也不要求正常终态保留已清除的租约。Worker自然等待与Agent重启是分别证明的两个场景。

## 真实页面、原回执、HDD与准确清理

每案注册POST1、确认POST1、取消POST0、内容PUT0，原cancel_requested_at_utc为空。原页面查询后明确显示“ACL修改未完成”，API原任务为Failed/CONTAINER_ACL_INTERRUPTED、confirmation=null、aclConfirmed=false、原receipt.state=Aborted且historyVerified=true；不能把Root Aborted误标为任务Canceled。四张实际PNG为Worker 1440×600亮色、Agent 320×600暗色的进行中/终态，已全部目视核对，无横向溢出，查询/关闭页脚可见。两案仍用原上下文从用户菜单正常BFF/Authentik双端退出；没有删账号或撤权代替退出。

实际两个意图、四个唯一Job、八条原Subject审计：plan-accepted2、plan-result Ready2、confirmed2、result Failed2，无cancel-requested。原planJob Succeeded、executionJob Failed，RootStarted/执行ID及原不可变回执保留；完成后屏障/租约解除、相关待处理Outbox0。

每案两个文件各2097159完整字节，原device/inode/mode/UID/GID/link-count/size/mtime/ctime九字段、access/default ACL、普通user.whaledeck-acl属性，在切点和最终清单完全一致，目录与既有子项也不变。各有一份永久ContainerAclMetadata HDD历史，原计划/实例/父inode/原ACL/执行身份/历史摘要与SQL/Root逐项绑定，attest只读读取并重算计划/历史域摘要，保存原始字节。产品读取路径验证原回执MAC；QA不另读Secret重算MAC。未重做Apply、自动恢复ACL或删除Root/HDD，ACL元数据不等于内容备份或异盘灾备。

父进程全量物理证明后才准确清理自有文件/空目录，再由fixture核对原original.bin及准确CID。独立观察器重读原SQL/Root/attest HDD、冻结来源、自然租约、实际Worker启动证明、维护采样和原页面结果，并核对源/CID/临时身份准确消失；不声称清理后重读了源字节。新v3核验通过后，另只读核对前批v2实时移组/停用两案原件兼容通过，旧目录不改、旧操作不重跑；v1/2来源不能冒充新重启场景。

除了本批Worker正常预检启停及一次实际SIGKILL/原容器启动，其他10核心容器ID/镜像/状态/StartedAt保持，8数据库平台不变；Agent仅按本案重启一次。最终11容器实际healthy，两宿主active/同源633a2ca，全局Job/终端/锁及各类容器活动/屏障/租约0。user NOPASSWD、密码、产品Agent最小授权和内部隔离未改，无dev自动回滚。

## 验证器修复与证据边界

新增重启离线测试首轮发现漏导入query，26项中出现13个子检查错误；已在冻结/实机动作前补正确导入，未放宽门禁。初冻结26 ACL+12原生身份+8范围共46项与语法通过。独立复核后补严实际Worker ID/镜像/StartedAt证明、强杀前后租约完全相等及Agent开始时有效租约；新增1项否定边界，最终**27 ACL+12身份+8范围=47项通过**，Python -W error、Node语法、diff检查通过，现有CI门禁已覆盖。真实源ed241e9的原租约确实完全相等，额外独立Docker见证已在下一案停止Worker前保存；加固后只重跑只读原件核验，没有重复两案中断。

真实两案、初版独立核验与加固后核验均通过；不是只跑模拟API或按脚本退出码签收。没有重新跑全量后端/前端/生产预览、没有新部署，也不声称远端CI全绿。47项离线边界不能代替上述真实门禁；剩余故障组合也不能由本两案推广通过。

root私有0700证据目录 `deploy/artifacts/container-acl-faults-6b64153773f6409a8882e235948cc630/`，54份原件的SHA目录，JSON/日志/PNG均0600；含冻结来源、两案baseline/原切点/双FD绑定/原SQL-Root-HDD/物理终态/父子信号/四PNG、实际Worker独立见证/42租约采样/152维护采样、independent-first/final、legacy-independent、final-summary。日志 `/tmp/wd-acl-restart-first.log`、`/tmp/wd-acl-restart-independent-first.log`、`/tmp/wd-acl-restart-independent-final.log`、`/tmp/wd-acl-restart-legacy.log` 留存。凭据只在内存或隐藏环境，未入仓库/日志或密码表单截图。

下一步：ACL实际系统调用后取消/Worker回执丢失/撤权，目标或实例变化Unknown及其他共享写入延期/明确处置组合；继续ACL递归/历史恢复、可恢复删除/长期清理、P0-05和其余59项/UI/C#酒馆/供应商/AI Live2D。写入前取消/退出、实时撤权、Worker/Agent中断子场景现已各有独立实盘证据，不算完整ACL故障矩阵完成。

文档终检：229份Markdown、1416处相对链接无缺失，目标第1节逐字不变、前批撤权QA原始正文逐字保留。项目图谱事件 `whale-deck.container-acl.ed241e9.process-restarts` 已取得projectId/key/eventId一致回执并移入synced，阶段保持active。
