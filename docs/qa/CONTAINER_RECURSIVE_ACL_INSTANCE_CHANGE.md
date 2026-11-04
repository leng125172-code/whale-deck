# 递归ACL首项修改后同容器实例变化实机验收

2026-11-04，承接[目标消失Unknown](CONTAINER_RECURSIVE_ACL_PARTIAL_UNKNOWN.md)。**新的隔离容器首项ACL真实修改后，同一CID重启为新实例，原执行成为Unknown；不同管理员处置、原件保持及独立核验全部通过。** 本批只签这一组合，不代表完整故障矩阵、完整递归ACL或59项已完成。

## 冻结来源与准确回执

工具冻结 `9fe980ec784ef8136bf1303ec54448c4cc5908fa`；Python/MJS静态源码闭包及安装helper原字节均在操作前核对。产品仍 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent1.53，无产品发布、迁移、Agent/维护入口重启、权限扩展或自动回滚。

原件目录 `deploy/artifacts/container-recursive-acl-partial-6dd7f3b6b8ed4a16ab20bb03fcc4cde6/`，unit `wd-qa-recursive-acl-partial-instance-9fe980e.service`，Invocation `c883e387722041089c204ec83a5dbdd2`，17:16:55–17:18:17 CST。停止对应已结束QA unit前保存准确Invocation、MainPID=0、active/exited、Result=success、ExecMainCode=1、ExecMainStatus=0及原日志；主门禁与独立核验都通过，不借用其他场景回执。

## 故障前保护与实际实例变化

新隔离容器以1000:1000、无网络、只读根、唯一自有/work绑定、drop ALL/no-new-privileges、16进程/32MiB/0.25CPU及无自动重启运行，两位新临时管理员经真实Authentik登录。原用户Vue设置完整文件/目录access ACL目标，实际异步预检、确认同一原执行；准确两个内核FD/操作锁、transport/cgroup/root身份与pidfd绑定helper。cut要求首项child Completed、ACL/模式/ctime实际变化及HDD原件证明，其余三对象原样，目录/root最后处理。

重启前检查10类文件工作、发布屏障和原租约；拒绝所有其他活动，仅允许准确当前QA unit与绑定helper。QA例外还要求当前MainPID/cgroup/Invocation一致，不是宽泛前缀放行。保存重启请求后，仅对准确自有CID执行一次固定docker restart；不执行任意命令或主机/核心服务重启。

| 证明 | 实际结果 |
|---|---|
| CID、名字、镜像、用户、网络、只读根、挂载和资源预算 | 重启前后保持 |
| 原实例 | PID4152489，StartedAt 2026-11-04T09:16:57.515317579Z |
| 新实例 | PID4155649，StartedAt 2026-11-04T09:17:50.912910133Z |
| 源与新实例物理清单 | 宿主源及新PID namespace下的四对象、完整字节/九字段/ACL/普通属性均等于cut |
| 核心容器完整身份/StartedAt | 故障重启前后保持；安排cut时仅同Worker正常停启 |
| helper | 新实例启动后仍活；原用户正常退出后由产品结束，从重启前计时起3.686秒，验证器未杀helper |

`instance-restart-request.json`、`instance-transition.json`记录精确前后frame，排除Docker环境与无关标签，固定PID/时间/CID/镜像/挂载/安全设置均独立复核。三个过程不相互替代：先真实首项写入，再实例变化，最后原用户正常退出与产品结束helper。

## Unknown、不同管理员处置及原事实留存

原执行/原计划继续绑定旧instanceId，未换成新实例或重放修改。Root/SQL为Unknown，五计数0/0/0/0/4、outcome digest空；这是旧实例当前不可证明的整树结果，**不能解释为实际零修改**。首项Completed原字节、cut和完整文件清单独立证明已经发生的一次ACL修改，其他三对象原样；1聚合+1完成子项HDD及2Root保留，清理的是自有测试源，未删历史。

本次cut与最终Root实际startedThrough=0/finishedThrough=0，不宣称中断在第二项准备中。另一管理员真实独立登录，原owner端点404，共享异常页实际显示Unknown和五计数。先“暂不处置”ACK0，完整SQL、源、Root/HDD不变；再一次明确ACK202→GET Acknowledged，执行Job Failed/Acknowledged、outbox0/屏障释放，唯一unknown-acknowledged审计为不同主体。原执行、登录绑定、命令、计划、RootStarted、Unknown计数、首项实际修改及Root/HDD字节保持，不自动续做、恢复或宣称整树成功。

`instance-after-ack.json`保存实际ACK之后同CID的新实例仍运行、PID/StartedAt仍为新实例及两处完整物理清单不变的证明，独立核验检查观察时刻不早于数据库处置时刻。其后才精确清理测试树、original.bin及自有容器；独立核验最终检查准确源/容器和临时身份缺席，不能把清理后的容器缺席冒充实例变化本身的证明。

两位原上下文均用户菜单双端正常退出、私有GET401，原用户退出时身份/组/active保持且原正权限仍余一小时以上；取消和上传请求0。5张真实320×600明暗PNG，Unknown计数/处置确认与登录表单已目视检查。父门禁在清理前完整读证源文件；独立核验清理后重读SQL、Root/HDD及唯一审计，不声称删除后重读源字节。

## 检查与最终状态

104项相关工具root测试全部通过、零跳过；8项范围、Node语法和diff检查通过。新增边界拒绝外来CID/镜像/挂载、相同PID/时间、改变安全/资源预算、其他活动/布尔伪计数、重复重启、变化的物理清单与错误QA Invocation；私有环境不进入frame。没有产品C#/Vue/协议改动，不把旧产品全量测试当本批重跑。旧目标消失Unknown门禁在同宿主下只读兼容通过，未重放删除或修改旧原件。

原535配置/账本/旧Root/HDD文件字节及九字段保持；最终11核心running且健康检查healthy、Agent4044173/维护3598736与来源保持、活动0。当前8配置账本、145普通ACL Root、108递归Root、153普通ACL HDD、125递归HDD，共539保护文件。数据库27意图：8Succeeded/5Canceled/1无执行Expired/3既有中断Failed/10Acknowledged，25执行/52相关Job。

源与逻辑HDD实际设备均2049，不签物理跨盘或完整NVMe/HDD布局；user NOPASSWD:ALL按授权保留，凭据只经stdin/服务端内存。上一批2375732 CI41803734887已完成success；本批9fe980e CI41806186808最后查询in_progress，尚未取得成功终态。

仍需Unknown跨容器写入延期/释放、路径/磁盘/依赖、其他时点与迟到RPC组合、递归ACL历史恢复、可恢复删除/长期清理、维护入口完整连续性及其余59项。17项移出不计完成，酒馆/供应商/Live2D及紧凑UI子计划保持，不提升整项完成率。

项目图谱事件 `whale-deck.container-acl.9fe980e.partial-instance-change` 已同步为active，准确回执保留；本批不改变完整目标未完成的状态。
