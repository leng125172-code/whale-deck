# 容器ACL：实际写入后的取消与Worker回执中断实机对账

后续记录：[ACL原容器消失Unknown及跨容器目录保护](CONTAINER_ACL_UNKNOWN.md)已在修正初始排队/视口证明后严格复验通过；不是整体首轮通过。以下本页原写后成功对账、失败边界和当时待办正文保留，未重做本页已签收操作。

2026-11-03。运行仍为 `633a2ca42012bbd6f388a67bd128fe3125bfab76`、Agent 1.51，无重新发布或生产迁移。验收冻结 `517c42a`，27份验收依赖与11份安装模块逐字节核对。**两个新独占资源的真实页面、实际ACL系统调用后切点、原任务/SQL/Worker/Agent/文件-HDD及独立只读核验首轮通过。** 不重复[写入前重启](CONTAINER_ACL_RESTARTS.md)、[取消/退出](CONTAINER_ACL_FAULTS.md)、[实时撤权](CONTAINER_ACL_REVOCATION.md)或[九常规场景](CONTAINER_ACL_REAL_UI.md)。

这里签收的是**目标ACL已实际写入，而Root仍Applying、aclConfirmed/actualIdentity为空、version为空**的取消或原Worker回执中断。产品最后按原事实确认Succeeded/Completed，不误报“未修改”，也不自动恢复旧ACL。不是实际写入后组撤权、Agent重启、目标消失Unknown、ACL递归恢复或删除验收；59主项继续部分实现，17项移出不算完成，master未交付。

## 实际写入后切点与页面动作

每案新建准确随机、固定既有镜像、非root1000:1000、无网络、只读根、唯一自有HDD bind容器，以及独立临时管理员。实际可见Authentik登录→Vue文件面板→acl-file.bin完整条目→Ready预检→正常停止原Worker→页面明确确认唯一原执行Job→启动同一Worker。源UID/GID2100:3100、模式0640、目标owner6/named user2201权限6/group_obj0/mask4/other0，目标模式仍0640、NoChange=false。

QA观察器只接受固定ACL传输命令/root身份、精确pidfd、原容器root FD及原操作0600空锁FD同时绑定的进程。先以真实fstat/read_acl证明同一原inode已具有目标access ACL、default与普通属性摘要保持，前八身份字段符合目标、ctime严格大于原值，再SIGSTOP并重新核对。必须为Root Applying/历史已验证/尚无完成回执；普通chmod、错误named权限、仅ctime变化、其他属主/链接/父inode/普通属性变化都不能当作这个切点。没有安装钩子、改SQL/Root/缓存/时钟或给产品放宽权限。

| 真实场景 | 实际动作与产品停止helper | 原任务对账 |
| --- | --- | --- |
| post-acl-cancel | 320×600暗色，先继续等待（取消POST0），再明确取消（POST1）；产品约5.313秒终止原helper | 原意图及执行Job Succeeded/CONTAINER_ACL_COMPLETED，Root Completed/aclConfirmed=true；cancel_requested_at_utc保留 |
| post-acl-worker-crash | 1440×600亮色，对准确原Worker CID实际SIGKILL，再显式启动同CID/镜像；产品约0.123秒终止helper | 原自然租约到期后同样Succeeded/Completed，无取消请求，不重新登记或替换执行ID |

helper退出由动作前启动的pidfd观察器并行测量，QA没有杀helper；Worker强杀是对已核对的唯一现有Worker容器。强杀前全局其他Job/终端/锁、九类容器活动/屏障/租约均0，transient只允许已绑定的本案helper。

Worker强杀前后原lease token/expiry完全相同，原到期时间2026-11-03 04:33:28.800385+08:00；实际等待约87.079秒、42次采样保持原Applying/Running执行Job、屏障、原租约、Root Applying原字节/SHA和**写后**完整物理清单。完成时间不早于原租约；无时钟/SQL编辑、提前放行或续写。与写入前强杀最终Aborted/Failed不同，本批Inspect按已发生的ACL及HDD历史核实并fsync准确仍绑定inode，最终Completed。

## 已改变与始终保留的实际事实

每案两个文件各2097159字节，acl-file.bin原device/inode/UID/GID/link-count/size/mtime、完整内容、普通user.whaledeck-acl属性不变；access ACL确实从基础6/4/0变为目标named/mask条目，ctime已增加，模式仍0640。default ACL、acl-directory及其既有子项全部保持。**不把整份原ACL/ctime说成未变化。** 从暂停后的实际清单到最终对账、迟到重放后，完整九字段、目标ACL、普通属性与内容完全一致。

每案永久保留一份ContainerAclMetadata HDD原ACL历史；SQL原请求/计划/执行ID、原Root Applying切点与最终Completed、完整源身份、目标mode/NoChange、原/目标ACL及历史摘要逐项绑定，attest只读重读HDD并重算计划/历史域摘要。原计划仍记录旧ACL/旧版本，publishedVersion和actualIdentity对应已发生的变更，不用新计划篡改历史。产品正常读取原意图时校验回执MAC；QA未另读Secret重算MAC。该元数据不是文件内容备份，也不计异盘灾备。

两案完成后分别使用固定受限helper对**同一原操作ID/owner/plan**发送迟到Apply。返回Completed、aclConfirmed=true、replayed=true，版本/历史摘要与原结果相同；原Root原始字节与全源清单再次完全一致，无新执行任务/HDD记录或第二次ACL变更。不能把重复调用等同再次写入，也不把这次检查推广为所有网络丢包/重放组合完成。

实际两个意图、四个唯一Job、九条原Subject审计：plan-accepted2、plan-result Ready2、confirmed2、cancel-requested1、result Succeeded2。两planJob与两executionJob均Succeeded；原RootStarted/执行ID/独立MAC回执与历史保留，完成后屏障/租约清除、相关待处理Outbox0。浏览器每案注册POST1、确认POST1、内容PUT0；原页面明确显示“ACL修改已完成”，原GET为Succeeded/COMPLETED、confirmation=null、aclConfirmed=true、historyVerified=true、receipt Completed/bindingCurrent=true，版本与Root实际身份匹配。

四张真实PNG为取消320×600暗色及Worker1440×600亮色的进行中/终态，已全部目视核对；查询/关闭页脚在视口内、无横向溢出。两案原上下文从用户菜单正常BFF/Authentik双端退出通过，不以删除账号或撤权替代退出。

## 准确清理、独立核验与测试边界

全量物理/SQL/Root/HDD与迟到重放证明后，只删除已核对的自有文件/空目录，再由fixture验证原original.bin及准确移除CID/源目录；Root/HDD永久原件不删。独立新进程重读原SQL/Root/attest HDD、原切点/实际ACL/租约/重放与页面信号，核对准确源/CID/临时身份不存在。源字节的实际证明来自清理前父进程，不声称清理后重新读取已删除文件。另只读重验前批v3写入前Worker/Agent两案原件兼容，通过且旧目录不改、旧操作未重复。

只有本批Worker预检正常启停与一次实际SIGKILL/原容器启动；其他10核心容器ID/镜像/状态/StartedAt、8数据库平台不变。Agent PID3288068、MaintenanceHost PID3122598及实际exe来源633a2ca保持，两宿主active；最终11容器实际healthy，全局任务/终端/锁及各容器活动/屏障/租约0。user NOPASSWD、密码、产品最小授权和内部隔离未改，无dev自动回滚。

新增5项真实本机临时inode/ACL切点否定边界及2项未抓到切点的准确清理边界；共**34 ACL+12原生身份+8范围=54项通过**，Python -W error/Node语法与diff检查通过，已有CI覆盖这些工具。v4来源只允许已加入新逻辑的冻结源码签收post场景，v1–v3不能冒充。漏抓切点仍必须失败：Unknown/活动租约/来源不符保留；若原任务已真实Succeeded，只在完整原件和准确自有源证明后允许清理，另记gatePassed=false，不把清理当验收通过。

**本批两次真实写后切点、主门禁及独立核验均首轮通过**，漏抓失败清理分支未触发，没有失败后只重跑片段或重复旧已签收操作。本轮只修改验收工具，无产品发布或生产schema修改，没有重跑全量后端/前端/生产预览，也不声称远端CI全绿。离线54项不能代替上述真实门禁，更不代表完整ACL故障矩阵完成。

root私有0700目录 `deploy/artifacts/container-acl-faults-d33f573691064ae1af338312216a6087/`，55份原件SHA目录，JSON/日志/PNG均0600；含27依赖/11安装模块来源、每案baseline/Root切点/实际写后清单/双FD绑定/原SQL-Root-HDD/租约/迟到重放/物理终态/父子信号/四PNG、independent-final、legacy-independent、final-summary。日志 `/tmp/wd-acl-post-first.log`、`/tmp/wd-acl-post-independent.log`、`/tmp/wd-acl-post-legacy.log` 留存；凭据只在内存/隐藏环境，不进仓库、日志或密码表单截图。

下一步：ACL实际写入后撤权/Agent重启等剩余组合，目标或实例变化Unknown及其他共享写入延期/明确处置；继续ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D。

文档终检：230份Markdown、1429处相对链接无缺失，目标第1节逐字不变、前批写入前重启QA原始正文逐字保留。项目图谱事件 `whale-deck.container-acl.517c42a.post-change` 已取得projectId/key/eventId一致回执并移入synced，阶段保持active。
