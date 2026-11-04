# 递归ACL写前实时移组与账号停用实机验收

2026-11-04，承接[执行中取消与正常退出](CONTAINER_RECURSIVE_ACL_PREWRITE_FAULTS.md)。**新增两项分项通过：HDD清单已验证、整树尚未修改时，准确临时身份被实时移出管理员组或停用，原执行安全停止，原页面失去管理访问并清除任务引用。** 本批不重做已通过的取消/正常退出，不把撤权当作正常双端退出，也不代表完整故障矩阵或递归恢复/删除已完成。

## 冻结版本与边界

工具提交 `5f5fabb`，运行前冻结整个Python/MJS静态导入闭包，并核对实际安装helper原字节与产品SHA。产品保持 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53；没有新发布、迁移、宿主重启、回滚或权限扩展。两套资源各使用新的非root/无网络/只读根容器、准确自有四对象树及新临时管理员。凭据只经stdin进入浏览器，不进入命令参数、日志或仓库。

真实Authentik可见登录后，经Vue填写文件/目录access目标、生成预检；只停止原Worker，正常确认返回202/Pending，再启动同ID/镜像Worker。沿用pidfd、固定transport/root UID、systemd cgroup、容器root FD与原操作锁FD绑定；聚合Root Applying/HDD已验证时暂停准确helper，再逐项核对完整源树仍为原字节/九字段/两ACL/普通属性。实际两个cut均startedThrough=-1、finishedThrough=-1，无child回执。未写入SQL/Root/时钟或产品checkpoint，没有任意PID或host路径输入。

浏览器已显示原进行中任务后，`OwnedAuthentikRevocation`再次验证自有用户名、原Subject/UUID、PK、active及配置中的管理员组，只允许一次`groups=[]`或`is_active=false`。旧正权限必须仍余一小时以上，且变化后的完整身份快照必须只改变对应字段；停用不修改原组列表。只变更这两个本批生成的身份，不改全局组或其他用户。API凭据仅在内存中使用。

## 实际结果

| 分项 | 移出管理员组 | 停用账号 |
| --- | --- | --- |
| 旧正权限在撤权确认时剩余 | 7161.060336秒 | 7160.533465秒 |
| 开始准确撤权到产品结束helper | 4.984秒 | 4.510秒 |
| 永久意图/执行Job | Canceled | Canceled |
| Root及实际修改 | Aborted / 0修改 | Aborted / 0修改 |
| 完成 / 修改 / 中止 / 未开始 / 待证明 | 0 / 0 / 1 / 3 / 0 | 0 / 0 / 0 / 4 / 0 |
| 原任务GET | 403 | 403 |
| 原`auth/me` | 200，同Subject、非管理员 | 403 |
| reload后的真实页面 | 320×600暗色403“没有管理权限” | 1440×600亮色登录页 |

两例原意图均没有显式取消时间，页面取消POST均为0，登记/确认各1次，内容上传0次。原任务region与dialog消失，opaque session引用为null；两份进行中、两份拒绝/登录页真实PNG均经过独立尺寸检查，拒绝/登录页已实际查看，无横向溢出。

**移组终态不改写成“四项均未开始”。** cut时首项尚未开始，最终Root记录startedThrough=0、首项独立Aborted标记、3项NotStarted；保存这一个child Root原件，但无child HDD历史。停用终态仍startedThrough=-1，无child Root。只记录观察到的进展，不推断是哪条内部调度路径推进了首项标记；两例完整物理证明均为全树九字段、字节、两ACL及普通属性与cut/原始状态完全相同。

每例新增并保留一份完整四对象聚合HDD及聚合Root，child HDD均为0。工具只用SIGSTOP/SIGCONT控制准确观察窗口，没有杀helper；结束由产品负责。临时身份只在终态及页面拒绝证明完成后删除，不用删除身份触发结果。没有用户菜单正常logout、OIDC end-session或匿名退出验收；本批`normalDualLogout=false`、`authorityRevoked=true`，不会用403冒充匿名401。

## 独立核验、保存及环境保持

独立只读过程重新读取永久SQL/Job/outbox、Root和受信HDD，核对源码闭包、准确身份变化、旧权限有效期、完整计数/摘要、原页面拒绝和引用清理。两临时身份/两源目录/两容器精确清理通过；物理字节由父过程在删除前完整校验，独立过程不声称清理后重读已删除源。还通过新核验器只读复核上一批取消/正常退出原门禁，保持其原证据，不重放任何已完成操作。

唯一unit `whaledeck-recursive-acl-revocation-5f5fabb.service`，Invocation `e8543e8311be46df85e22d617183be28`，14:19:24–14:21:39 CST。使用RemainAfterExit保留准确退出元数据：active/exited、MainPID=0、Result=success、ExecMainCode=1/ExecMainStatus=0。原状态和完整Invocation日志归档后，只停止这一个已完成QA unit；未停止其他产品服务或维护入口。

私有证据：`deploy/artifacts/container-recursive-acl-faults-e54a37ac030a4cfbbc3ec68d387842a3/`。包括scope、两套baseline/ready、cut SQL/Root/物理/FD绑定、准确authority-change、authority-denied、浏览器/图片、最终SQL/Root/HDD、physical-final、独立和上一批兼容核验、postflight及unit退出/清理记录。该目录不进入Git。

91项递归探针边界检查、12项准确临时身份限制检查、8项范围检查、Node语法与diff空白检查通过。现有CI自动发现新增两项证明测试；新工具CI终态另核查，不把此前产品测试或fixture浏览器当作本批重新运行的实机全量。无产品C#/Vue/协议变更，无需为这些验收工具重新覆盖运行版本。

旧484个受保护文件的字节SHA及九字段全部保持：7配置/权限文件、部署账本、145普通Root、78递归Root、153普通HDD、100递归HDD。新增后递归Root81（2聚合+1首项Aborted）、递归HDD102（2聚合），普通数目不变。生产现14意图：8 Succeeded、5 Canceled、1无执行Expired；执行12、相关Job26、活动0。两个宿主PID仍Agent3598741/维护3598736，实际来源b880208，11核心healthy，只有同Worker的StartedAt因本批停启改变。user NOPASSWD:ALL按授权保留，Agent最小授权不扩大。

源树/HDD实际device相同，不能作为物理异盘验收；未做主机重启、断电、填满生产盘或维护入口瞬时零中断测试。旧失败与因果未证边界原样保留。下一批继续Worker/Agent重启及自然租约、部分写入后的取消/退出/撤权、Partial/Unknown与跨写入、路径/磁盘/依赖等故障，再递归ACL历史恢复、可恢复删除/长期清理和其余59项；完整签收比例和阶段计数不因本批分项测试增加。
