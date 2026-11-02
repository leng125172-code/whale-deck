# 容器ACL：实际写入后实时移组与账号停用实机对账

2026-11-03。运行保持 `633a2ca42012bbd6f388a67bd128fe3125bfab76`、Agent 1.51，无应用发布或生产迁移。实际验收冻结 `8d93fbe`，schemaVersion5/27份验收依赖与11份安装模块逐字节核对。**两套新独占资源的真实写后切点、原登录实时失权页面、持久SQL/Worker/Agent/文件-HDD、不同当前管理员只读API与独立观察器通过；随后补证源码0b7d43c的真实任务中心只读查看也通过。** 模型连接中断后仅核对已有结果，未重做已通过的真实操作。

本批只签收**目标access ACL已写、Root仍Applying且尚无完成回执**时的实时组撤权/账号停用；当前容器和已变更inode仍然存在并绑定。与[写入前撤权](CONTAINER_ACL_REVOCATION.md)、[写后取消/Worker中断](CONTAINER_ACL_POST_CHANGE.md)、[容器消失Unknown](CONTAINER_ACL_UNKNOWN.md)分开，不计写后Agent重启、写后/实例变化Unknown、ACL递归/历史恢复或删除完成。59主项继续部分实现，17项移出不算完成，master未交付。

## 已发生变更与停止新权限的实际链路

每案新建准确随机容器/源目录/原管理员：固定既有镜像、非root1000:1000、无网络、只读根、唯一自有HDD bind。源acl-file.bin、acl-directory及existing.bin的两个目标文件各2097159字节，原UID/GID2100:3100、普通模式0640/目录0750、固定mtime与user.whaledeck-acl属性。正常可见Authentik登录→真实Vue完整条目→Ready预检，正常停止原Worker、页面确认唯一原执行Job、启动同CID/镜像Worker。

严格观察器绑定固定ACL传输argv/root身份、pidfd、容器root FD与原操作0600空锁FD，实际读到目标named user2201权限6/group_obj0/mask4/owner6/other0的access ACL、同原inode和新增ctime、未变default ACL/普通属性，再SIGSTOP并重新证明。目标模式仍0640而NoChange=false。Root必须Applying、aclConfirmed/actualIdentity为空、version为空、原ACL HDD历史已验证；未用安装钩子、SQL/Root/缓存/时钟编辑替代真实系统调用。

原浏览器/context保持登录，权限正快照仍剩一小时以上。仅对本案生成用户名/Subject/数字PK全部匹配、原active及管理员组已二次核对的准确身份发一次PATCH：移组只写groups=[]，停用只写is_active=false并保持原组。丢回复不重发；不重新启用原账号、不恢复组或修改真实用户。

| 场景 | 原正快照在变更确认时仍剩余 | 产品停止helper | 原登录实际结果 |
| --- | --- | --- | --- |
| post-acl-group-removal | 7163.424318秒 | 4.806秒 | 原任务GET403；/auth/me200、同Subject且isAdministrator=false；320×600暗色/forbidden，“没有管理权限” |
| post-acl-account-disable | 7163.675583秒 | 4.834秒 | 原任务GET403、/auth/me403；1440×600亮色/login，“使用工作站账号登录” |

退出时间由实际身份变更前启动的pidfd观察线程测量，helper由产品结束，QA未杀helper。两案浏览器注册POST1、确认POST1、取消POST0、内容PUT0，原cancel_requested_at_utc为空；失权后整个ACL引用键为null、私有任务区域不存在。没有先退出/换登录再测试撤权，也不是靠两小时自然到期放行。**本批原用户是失权后被拒绝，不是正常双端退出，normalDualLogout=false。**

## 历史成功事实与当前权限分开证明

原意图/执行Job最终均Succeeded/CONTAINER_ACL_COMPLETED，Root Completed、aclConfirmed=true、publishedVersion为已变更inode的真实版本；BindingCurrent=true表示实例/对象仍绑定，不能解释成原用户仍有管理员权限。Worker停止原执行后，通过只观察的Inspect按已有目标ACL/原HDD核实并fsync准确inode，保存已发生的事实；未发新的ACL修改或恢复旧ACL。原权限失效仍严格拒绝原用户GET和后续管理。

原两个文件完整内容、device/inode/UID/GID/link-count/size/mtime、default ACL及普通属性保持；access ACL确实变成目标条目、ctime确实增加，模式仍0640。目录/既有子项保持。切点后、最终对账及迟到同原ID/owner/plan的固定helper Apply前后，完整写后九字段/目标ACL/xattr/内容与原Root字节完全一致；返回Completed、replayed=true及同一版本/历史摘要。不是声称原ACL/ctime没有发生过变化。

每案永久保留1份ContainerAclMetadata原ACL HDD历史；原计划仍描述旧ACL/旧版本，原执行ID/owner、目标mode/NoChange、真实新actualIdentity/版本、SQL第一回执/MAC与Root/attest HDD逐项绑定，重算计划/历史域摘要。没有删除Root/HDD、重新登记任务或重做已完成变更。元数据不包含内容备份，不计异盘灾备或NVMe覆盖。

每案在原页面拒绝已验证后，再生成一个**不同的当前管理员**，通过正常Authentik/BFF HTTP登录，GET现有管理员恢复端点的原ID，实际200返回同一Succeeded/执行ID/新版本、aclConfirmed=true、receipt Completed/bindingCurrent=true和原历史摘要。该GET读取结束后正常HTTP BFF/Authentik双端退出（401及401/403、最终/login）；不ACK、不登记或修改文件。这个端点的GET允许当前管理员读取已有终态，不代表Unknown处置。随后用另一个不同当前管理员正常可见SSO登录任务中心，实际点击两个原执行Job的“详情”，在320×600暗色与1440×600亮色看到原执行ID、containers.files.acl类型及Succeeded状态，成功任务不提供取消/重试按钮；管理写请求0、原SQL前后完全一致、正常浏览器双端退出通过。源码0b7d43c及5份只读依赖独立冻结，两张截图已目视确认。任务中心证明的是**历史Job状态**；完整ACL字段仍由正常只读API/原件证明，任务中心当前resultJson为空且显示“任务成功后显示安全结果。”，完整安全结果摘要与误导空文案仍待修复，不记作完整任务结果展示。

两案实际2意图/4唯一Job/8条原Subject审计：plan-accepted2、plan-result Ready2、confirmed2、result Succeeded2，无cancel-requested或unknown-acknowledged。两planJob及两executionJob均Succeeded；原actor_subject/登录绑定/请求/计划/执行ID/回执保持，相关待处理Outbox0、完成后屏障/租约清除。观察管理员只读身份不替代原结果审计主体，原账号在全部失权证明后才准确删除。

## 清理、独立观察器与边界

父进程清理前完整读取源字节/九字段/ACL/xattr，确认与写后切点完全相同，再准确移除自有文件/空目录，由fixture检查原original.bin并移除本案准确CID/源目录。Root/HDD原件永久保留。独立新进程重读原SQL/Root/attest HDD、冻结来源、原始身份PATCH/未过期快照、原页面拒绝和不同当前管理员只读结果、迟到重放及准确清理；不声称在清理后重读了已删除源字节。

四张原用户实际PNG为移组320×600暗色、停用1440×600亮色的进行中/拒绝页，另两张为不同当前管理员的任务中心原执行详情；六张有效截图已逐张目视核对，无横向溢出。已写入后的新现场与旧权限/原执行身份分开证明，不用截图相似性替代原Subject/Job/实例/inode/HDD绑定。凭据只在内存/隐藏环境，不进仓库/日志或密码表单截图。

只按每案预检正常停止/启动现有Worker，原CID/镜像保持；其他10核心容器ID/镜像/状态/StartedAt和8数据库平台不变。Agent PID3288068、MaintenanceHost PID3122598及实际exe来源633a2ca保持、两宿主active，最终11核心实际healthy；全局Job/终端/锁及各容器活动/屏障/租约0、准确源/CID与临时身份不存在。user NOPASSWD、密码、产品最小授权及内部隔离不改，无dev自动回滚。

最终**36 ACL边界＋12原生身份变更边界＋8范围=56项通过**，Python -W error/Node语法及diff检查通过，现有CI门禁覆盖。新增写后失权映射及不同当前管理员/原ID/版本的否定用例；旧schema1–4不能冒充新增两场景，schema5要求新来源。离线测试不代替上述真实门禁。本批真实两案和独立核验均通过；中断时主脚本已完成，两次操作均未重新执行。补充任务中心只读页面首轮因Succeeded同时存在于“状态”和“阶段”，旧定位器匹配两处而失败；保留原源码1579fec、失败截图与日志，仅将断言限定到状态标签，再以新只读登录验证成功（0b7d43c），未重做ACL/撤权操作。失败读取没有记作正常退出，临时身份准确清理；成功只读登录正常双端退出、再一次原SQL/Root/HDD独立核验均通过。资料定位曾误读不存在的Agent服务路径，随后使用rg返回的实际Services/ContainerAclGrpcService.cs，无产品动作。

本轮未重新发布、未改生产schema，也未复跑全量后端/前端/生产预览或声明远端CI全绿。任务中心ACL完整结果摘要及空文案、写后Agent重启、写后或实例Unknown与其他共享写入组合仍待；ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D继续。

root私有0700目录 `deploy/artifacts/container-acl-faults-07f0faaf6b9e4f5d826e766402928fa0/`，67份原件SHA目录，JSON/日志/PNG均0600；含27依赖/11安装模块来源、两案baseline/写后切点/双FD绑定/authority-change/原SQL-Root-HDD/物理终态/迟到Apply/原页面信号/六张有效PNG/current-admin-read/独立任务中心只读来源及原失败目录/independent-final/independent-after-jobs/final-summary。日志 `/tmp/wd-acl-post-revocation-first.log`、`/tmp/wd-acl-post-revocation-independent.log`、`/tmp/wd-acl-post-revocation-jobs.log`、`/tmp/wd-acl-post-revocation-jobs-strict.log`与`/tmp/wd-acl-post-revocation-after-jobs.log` 保留。

## 文档收尾与项目同步

本批检查232份跟踪或新增Markdown的1455条相对链接，缺失0；最新目标第1节与ea47e7e逐字一致，CONTAINER_ACL_UNKNOWN.md原始验收正文逐字保留，仅增加新批次前置说明。59主项与旧证据的范围没有改写。项目图谱首次两次列表读取因传输失败未作为成功，随后项目元数据更新成功、阶段更新回执的projectId/key/eventId全部匹配，事件`whale-deck.container-acl.8d93fbe.post-revocation`已由pending移至synced；阶段仍为active，不代表全部ACL或项目完成。
