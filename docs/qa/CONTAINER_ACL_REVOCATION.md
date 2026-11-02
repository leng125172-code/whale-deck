# 容器ACL：原登录实时移组与账号停用实机验收

后续记录：[写入前Worker强杀/原自然租约与Agent重启](CONTAINER_ACL_RESTARTS.md)已实机及独立原件验收；当前Agent换为PID3288068，维护PID3122598不变。以下本批原身份、PID、当时待办与网络失败事实逐字保留。

2026-11-03。运行保持 `633a2ca42012bbd6f388a67bd128fe3125bfab76`、Agent 1.51，没有重新发布或改生产schema。验证器冻结 `f3ca007`，27份验收依赖与11份安装模块逐字节核对。两套新独占容器/源目录/临时身份的真实Authentik→Vue→持久SQL/Worker→Agent→原文件/ACL/HDD门禁**首轮通过，独立只读核验通过**。不重复[原九常规场景](CONTAINER_ACL_REAL_UI.md)或[取消/正常退出](CONTAINER_ACL_FAULTS.md)。

仅签收**Root Preparing/HistoryVerified、业务Applying且尚未实际写ACL**的两种原登录实时失权。不是实际ACL变更后的撤权、自然两小时过期、身份源失联、Worker/Agent重启、Unknown或递归恢复验收。59主项继续部分实现，17项移出不算完成，master未交付。

## 实际身份变更与原页面拒绝

每案先使用正常可见Authentik登录与真实Vue编辑access ACL，原文件2100:3100、模式0640；目标为owner6、named user2201权限6、group_obj0、mask4、other0，模式仍0640而NoChange=false。原身份配置/快照必须仍为管理员；Ready后仅正常停止现有Worker，页面确认得到唯一执行Job，再启动原Worker。准确pidfd/固定ACL传输命令/原容器root FD/原任务0600空锁FD同时绑定，核对完整原inode、两类ACL/普通属性后暂停helper；暂停后再次证明原源未变、原helper仍活。

只有本案随机临时身份可更改：QA核对生成用户名、稳定Subject、唯一数字PK、原active及管理员组，并在PATCH前再次读取原记录。移组仅写groups=[]；停用仅写is_active=false并保持原组。每案只发一次准确PATCH，丢回执也不允许第二次变更；账号不会因验证而被重新启用或恢复组。凭据只在父进程内存/浏览器隐藏环境，未委派权限决定或实际身份写入。

原浏览器/context一直保留；没有先退出再登录、注入Cookie或使用新身份代查原任务。两案原权限快照在变更确认时均剩余超过一小时，实际约7165秒，不改时钟、SQL或缓存，不借自然过期制造撤权结果。

| 场景 | 变更确认时原快照剩余 | 产品结束helper | 原登录实际拒绝与页面 |
| --- | --- | --- | --- |
| group-removal | 7164.703104秒 | 5.195秒 | 旧任务403；/auth/me为200、原Subject相同且isAdministrator=false；原页面刷新到/forbidden，“没有管理权限” |
| account-disable | 7164.765949秒 | 5.213秒 | 旧任务403、/auth/me为403；原页面刷新到/login，“使用工作站账号登录” |

每案注册POST1、确认POST1、取消POST0、内容PUT0；cancel_requested_at_utc为空。两案失权后整个sessionStorage ACL引用键为null，私有ACL任务区域不存在；移组账号仍启用，停用账号原组不变。四张实际截图分别为320×600暗色移组进行中/拒绝页及1440×600亮色停用进行中/登录页，均已目视核对，无横向溢出。不把这两案说成正常BFF/Authentik双端退出，normalDualLogout=false；前批正常退出证明独立保留。

## 原任务、源文件与HDD原件

两个新意图、四个唯一Job、八条原Subject审计：plan-accepted2、plan-result Ready2、confirmed2、result Canceled2，无cancel-requested。planJob Succeeded、executionJob Canceled/CONTAINER_ACL_CANCELED，原RootStarted/执行ID保留，Root终态Aborted/aclConfirmed=false、actualIdentity为空、无publishedVersion；历史已验证，完成后原屏障/租约解除，相关待处理Outbox0。不是Ready阶段没有执行/历史的取消场景。

每案准确自有容器为固定既有镜像、非root1000:1000、无网络、只读根、唯一自有HDD bind；仅原original.bin以及acl-file.bin、acl-directory/existing.bin。两个目标文件各2097159完整字节、原九字段device/inode/mode/UID/GID/link-count/size/mtime/ctime、access/default ACL及普通user.whaledeck-acl属性，操作前、暂停时、原任务结算后完整一致；目录/既有子项也不变。

各保留一份永久ContainerAclMetadata HDD历史。父进程逐项比对SQL原回执/摘要、Root完整计划/原身份/历史与attest只读HDD原件，重算计划及历史域摘要，原始字节留存；没有重做Apply、自动恢复ACL或删除Root/HDD。产品回执MAC由产品信任链负责，QA未另读Secret重算MAC。本批单对象ACL元数据不是文件内容备份，也不计异盘灾备或NVMe覆盖。

物理证明完成后才准确清理自有文件和空目录，fixture再核对原original.bin的完整内容/身份并准确移除CID/源目录；临时身份在拒绝证明后准确删除。独立新进程重读原SQL/Root/attest HDD与冻结27依赖来源，逐字节匹配原件、原身份变更/未过期时间/页面拒绝、清理与健康；不声称在源已清理后重新读取了其字节。

仅按门禁正常停止/启动现有Worker两次，其容器ID/镜像保持；其余10核心ID/镜像/状态/StartedAt及8数据库平台容器不变。最终实际11容器healthy，Agent PID3122630、MaintenanceHost PID3122598仍active，实际exe --build-info均633a2ca。全局Job/终端/主机锁及各类容器活动/屏障/租约0，准确源/CID/临时身份不存在。user NOPASSWD、密码、产品最小授权和维护入口未更改，无dev自动回滚。

## 验证、来源兼容与失败记录

**18项ACL观察器/失败清理/撤权证据边界＋12项准确原生身份变更边界＋8项范围=38项通过**，Python -W error/Node语法通过；现有CI条目已覆盖这些工具。新断言拒绝原快照已接近到期、错误Subject/PK/用户名/账号状态、仍能读取旧任务、未清引用、用退出/取消代替撤权，以及停用时改变原组。只修改验收工具，未以离线测试代替本批实机。

v2冻结来源强制包含owned_authentik_revocation.py，v1仅允许旧cancel/logout；缺依赖、未来/错误schema或用旧来源冒充撤权均拒绝。当前独立观察器还只读复核了前批两例原SQL/Root/HDD和准确清理，通过且未重复旧操作、未改前批证据目录。

**真实两案及本批独立核验首轮通过，没有失败后只重跑片段或重复已完成写入。** 上轮TLS失败后，本轮首先成功推送c8a16eb..546b6cc，原失败记录保留；随后独立ls-remote和候选f3ca007推送再次出现GnuTLS握手中断，远端状态以最终成功回执为准，不编造CI全绿。本轮未复跑全量后端/前端或生产预览，无重新发布。

证据在root私有0700目录 `deploy/artifacts/container-acl-faults-cdbd65b02a224be29bb475c1a725b2fa/`，JSON/日志/PNG均0600，包含来源、各案baseline/原切点/绑定helper/authority-change/原SQL-Root-HDD/物理终态/父子信号/四PNG、independent-final、legacy-independent和final-summary及SHA目录。日志 `/tmp/wd-acl-revocation-first.log`、`/tmp/wd-acl-revocation-independent.log`、`/tmp/wd-acl-legacy-independent.log` 保留；无真实凭据入库或密码表单截图。

剩余：ACL写入前Worker强杀/自然租约和Agent重启、实际系统调用后取消/丢回执/撤权、目标/实例变化Unknown、其他共享写入延期/明确处置组合，ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D。不把本批两个失权场景推广为所有写入或全部权限故障完成。

文档终检：228份Markdown、1404处相对链接无缺失，目标第1节逐字不变、前批取消/退出QA原始正文逐字保留。项目图谱事件 `whale-deck.container-acl.f3ca007.live-revocation` 已取得projectId/key/eventId一致回执并移入synced，阶段保持active。
