# 递归 ACL 历史恢复内核候选与实盘分项记录

日期：2026-11-04开始、2026-11-05收尾，dev。内核提交`372aca8`，增加Unknown反例后实盘冻结`4c943b41d883522a043089306ff793ccf1c16138`；验收工具修正与只读恢复冻结`e15d5bca6888b95723a124fcd0d3afb6e35e9763`。**底层异构恢复已实现，并取得7个实盘分项的物理/原件只读核验证据；原整轮因验收程序错误失败的事实保留，不补签为首轮成功。尚未安装、开放签名RPC、业务持久化或Vue恢复页面，不计完整恢复功能或59项主任务完成。**

## 恢复语义与数据边界

沿用现有单项恢复语义：只恢复所选捕获原来修改的`changedAclType`。access按每个文件/目录的原access ACL恢复，default只还原目录的原default ACL，文件以当前access做noChange证明；另一ACL类型、owner、普通属性、完整内容、inode及mtime保持当前状态。不会把统一file/directory ACL套给所有历史条目，也不把历史inode当成当前目标inode。

内部命令仅含当前容器/实例/路径/expectedVersion和captureId/expectedHistoryDigest，不接受目标ACL数组或uid/gid。来源由现有实际HDD历史读取器验证完整schema、摘要、manifest和拓扑；仅接受同容器/路径的来源，允许历史实例与当前实例不同，当前树必须与原件的相对路径/kind集合一致。规划绑定当前树各inode版本、属性/两ACL前像、来源摘要和源文件九字段，以及独立异构目标投影摘要；源文件同字节换inode也会使原计划失效。

新`acl_restore`仅为可信类型构造开关，不来自命令或helper输入。命令、plan、manifest、子项ID/历史、结果摘要、错误前缀、资源类型及HDD namespace均使用独立`acl-restore`域，旧chmod/chown/递归ACL默认域保持。新增`whaledeck-container-recursive-acl-restore.py`没有CLI或安装白名单授权，也没有RPC/API空接口。

## 写前留存与中断恢复

执行前重新生成完整计划并与原计划精确相等，先在attested配置HDD保存当前整树前像，再允许逐项修改。来源文件和其命名namespace以FD、摘要及九字段固定，在当前前像捕获前、每项写入前后复核配置/namespace/文件身份；变化即停止后续项。保留10000项、深度32、元数据/时间/容量预算、FD/no-follow及设备/别名保护；最终产品传输层的namespace/no-idmap与最小授权安装仍需接入验收。

恢复操作保存自己的完整前像、逐项结果和`Completed/Aborted/Partial/Unknown`状态。迟到Apply/Inspect/Discard只读取原操作并核对当前结果，不继续未完成项；检查已执行结果不再依赖原来源继续存在。取消、来源丢失或目标漂移不会自动回滚已完成的项，也不将Partial/Unknown报成成功。

恢复前像存于独立`container-recursive-acl-restore-v1`，普通历史域不受修改。本候选目前从原递归ACL历史域读取来源；新恢复前像的公开读取、再次作为恢复源、协议/业务/UI兼容仍待接入，不能因为已经保存前像就把“再次恢复/撤销恢复”记为完成。

## 临时Linux-root检查

最终`test_container_file*.py`共**521项通过、0跳过**，其中新增15项；另8范围检查通过，严格Python编译及diff检查通过。新增覆盖异构access/default、ID0、空default、基本access还原、来源摘要/文件替换/缺失、当前树增项或kind错配、目标摘要篡改、只读规划/noChange、取消/来源丢失后的Partial、目标消失后的Unknown、迟到不重放和专属域/恢复清理选择约束。这些检查实际执行Linux临时文件ACL系统调用，但容器liveness/HDD配置是显式隔离夹具，不冒充工作站安装态。

access使用现有`setxattr`写语义，未添加伪称执行过的access removexattr。本机测试文件系统及下述实盘恢复中，恢复为基本access ACL后`listxattr`确实不含`system.posix_acl_access`；空default按既有写法清除。旧schema只记录规范化ACL，没有原始presence位，不据此杜撰跨文件系统原始xattr字节状态保证。

首轮新检查的两处异常类型引用错误导致8个子例AttributeError，改为实际engine.f.Rejected后执行通过；Unknown反例最初只预期Rejected，目标ENOENT实际为受控OSError，修正异常预期后继续核对Unknown/不重放。一次完整回归由私有日志包装器继承umask077，导致4个既有夹具的原022权限预期失败；保留原日志，仅对子测试恢复原022后521项通过，产品/证据权限未放宽。

## 真实容器与配置HDD分项

新建一份自有非root、无网络、只读容器根夹具，4项包含中文文件名及各自不同原ACL，文件65536+7字节。冻结候选包使用实际Docker ContainerRoot及配置HDD，不安装候选，不重启Agent/Worker/MaintenanceHost。2笔普通来源捕获完成后，执行下列7个恢复操作；每轮完整内容、inode/owner/mtime/普通xattr、两ACL及实际Root/HDD记录均保存。

| 场景 | 实际状态 | 完成项/实际变更 |
|---|---|---|
| 异构access恢复 | Completed | 4/4 |
| 原来源再次新规划，当前已相同 | Completed/noChange | 4/0 |
| 不同目录default恢复 | Completed | 4/2 |
| 写前Discard围栏 | Aborted | 0/0 |
| default第3项后取消 | Partial | 3/1 |
| access第1项后候选进程SIGKILL | Partial | 1/1 |
| 第1项修改前候选进程SIGKILL | Aborted | 0/0 |

两次强杀仅针对新候选子进程，未杀生产服务。每轮随后屏蔽ACL写函数再调用同操作Apply，实际状态与源文件字节/元数据不变，证明迟到不续做。Source/target损坏、容量丢失、实时身份撤权及完整安装态故障矩阵仍须后续分项，不把临时检查或这7例代替全部组合。

首轮7个场景都到达上述状态，随后清理前独立程序错误调用子项历史不存在的`load`，实际接口为`read(fd,request)`。父级整体exit1，原回执/日志保留；既有夹具移除容器并保留未核准清理的源。阶段日志当时仍停在最后一个场景，实际异常位置以independent-live.log为准，未来阶段标记已修正。

e15d5bc修正只读程序后，另启进程重新读取仍保留的真实物理树、聚合原件和16份恢复子项HDD原件，通过每项源/目标、摘要、版本、父子范围和状态核验，再依据自有路径/父inode及original.bin完整字节准确清理。清理后再独立核验全部保留原件和源/容器缺席，两次均退出0；`originalWholeGatePassed=false`保持，没有新来源捕获、恢复重放或原整轮verified.json补签。清理后不宣称重新读取已删除源字节。

## 运行态、证据与下一步

原1394份配置/ACL Root/HDD保护文件字节及九字段保持；新增2来源的普通HDD元数据10份，标准快照1404。新恢复域22份JSON（6聚合、16子项）及6份锁文件，另用28文件快照封存；候选Root状态留在私有验收目录。目录Root94、永久业务38意图/35执行/73Job不变，活动任务/租约/屏障/终端/锁0；11核心ID/镜像/StartedAt及healthy、两宿主PID/390889d和user NOPASSWD保持。源/HDD实际device仍2049，不计异盘恢复。原维护连续性失败继续保留。

私有证据`deploy/artifacts/recursive-acl-restore-kernel-87f43318323b43089be4b18928f37a10/`120份及`deploy/artifacts/recursive-acl-restore-checks-4c943b4/`4份，均已逐项核验SHA256，不入Git。包含原失败、7实际状态、HDD原件、物理快照、只读复核/精确清理、完整回归失败与修正日志。没有真实Authentik或Vue恢复验收，也没有业务持久化证明。

下一步实现专用签名RPC/SDK/受限传输与最小授权安装，完整绑定小来源引用/plan/确认摘要；随后补真实持久化/共享保护、管理员API/Vue和整链恢复，再推进剩余故障、可恢复删除/长期清理、维护连续性及其余59项，不增加主任务完整完成数。

[历史多捕获读取实机记录](CONTAINER_RECURSIVE_ACL_HISTORY_INVENTORY_REAL_UI.md) · [当前目标](../GOAL_P0_P5.md)

项目图谱事件`whale-deck.container-acl.4c943b4.recursive-restore-kernel`已取得对应project/key/event成功回执、pending移至synced，阶段active。准确e15d5bc的CI run`41862952678`最后核对queued，未取得成功终态；首次远端GnuTLS中断后已正常重试推送成功。两个QA unit均按准确Invocation归档后停止，全部候选原件保留。
