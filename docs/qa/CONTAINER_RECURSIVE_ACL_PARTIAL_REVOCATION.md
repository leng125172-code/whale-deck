# 递归ACL部分写入后实时移组与账号停用实机验收

2026-11-04，承接[部分写入中正常退出](CONTAINER_RECURSIVE_ACL_PARTIAL_LOGOUT.md)。**两套新隔离资源真实首项ACL修改后，分别移除原用户管理员组或停用原账号；产品结束helper，Partial及保护保留，再由另一管理员明确处置。两个主门禁及独立核验均通过。** 原失权上下文未正常logout，不能把权限拒绝或登录页导航当作正常双端退出。

## 冻结、安全边界与真实动作

工具冻结f31e015，完整Python/MJS静态源码闭包及安装helper原字节在运行前核对。产品保持 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53，没有新发布、迁移、宿主重启、回滚或权限扩展。两场景分开运行，各用新的非root/无网络/只读根容器、四对象树和两位临时管理员。只停启同Worker安排cut，没有影响其他容器身份/镜像或给API/Worker挂载Docker socket。

原管理员通过真实Authentik/Vue填写文件与目录access目标、预检并确认；准确容器root FD/操作锁FD、固定transport/root UID/cgroup与pidfd绑定helper。cut要求首项child Completed、真实access/模式/ctime变化和HDD原件证明，其余三对象及Root保持原样；保存完整物理与Root证据后，原页面实际显示进行中任务，才执行准确撤权。

OwnedAuthentikRevocation再次匹配自有用户名、原Subject/UUID、PK、active及配置中的管理员组，只允许一次groups=[]或is_active=false；Group场景只清空组且账号仍active，Disable场景只变active并保留原组列表。不是修改全局组或其他用户；API凭据在内存，浏览器密码仅stdin，不进参数、日志或仓库。原正权限必须仍有效超过一小时，撤权后快照须仅改变指定字段。

## 两项实际结果

| 分项 | 移出管理员组 | 停用账号 |
| --- | --- | --- |
| 原正权限在确认时剩余 | 7160.537384秒 | 7159.655934秒 |
| 开始撤权到产品结束准确helper | 4.650秒 | 4.499秒 |
| Partial五计数：完成/修改/中止/未开始/待证明 | 1 / 1 / 1 / 2 / 0 | 1 / 1 / 1 / 2 / 0 |
| 原意图GET | 403 | 403 |
| 原auth/me | 200、同Subject、非管理员 | 403 |
| 原页面reload后 | 暗色320×600“没有管理权限”403页 | 暗色320×600登录表单 |

两例原意图及Job取消时间均为空，取消POST0、上传0、登记/确认各1次。原角色浏览器不再读取私有结果，result明确null；旧递归任务region/dialog消失、opaque引用null，页面无横向溢出。两张拒绝/登录页均已实际查看，停用图等待真实登录标题可见，未用启动层替代表单。HTTP拒绝是实际检查时的结果，不推断第一次拒绝的瞬时精确延迟；仍余约两小时的旧正权限不能解释为自然过期。

两例永久Partial/Root Partial，publish_barrier=true、completed为空、lease双空，执行Job Running且outbox待处置。各首项文件已真实改access/模式/ctime，完整内容、inode、UID/GID、nlink、size、mtime、default和普通属性保持；第二项最终Aborted、其余两个目录未开始，Root与其余三对象保持原状。每例1聚合+1完成子项HDD、3Root（聚合/Completed/Aborted）保留，Aborted无子HDD；完整清单/原计划/五计数/版本/outcome/manifest/history摘要与SQL、回执及实际源一致。

## 不同管理员处置与历史保持

原失权浏览器关闭后，第二个独立真实管理员登录共享异常处置，原owner接口404，原ID/五计数可读。先“暂不处置”ACK0，父过程重读完整SQL、源和Root/HDD保持；再一次明确ACK，Acknowledged/执行Job Failed-phaseAcknowledged、outbox完成、保护释放。两例唯一partial-acknowledged审计都属于各自第二管理员、原执行Job，结果Partial。

原执行ID/Started/命令计划/绑定、Partial事实、实际ACL修改及Root/HDD原字节不变，不会续做/回滚/删除目标或历史；仅Replayed等允许的观察字段按真实Discard回执更新。第二管理员各自正常双端退出及匿名共享GET401；原失权上下文normalDualLogout=false、authorityRevoked=true，不能补签provider end-session或把403当匿名401。临时身份仅在终态和处置证明完成后删除，不用删账号触发结果。

独立核验重新读取永久SQL/Job/outbox、Root/受信HDD、原正权限期限和准确身份变化、失权页面/引用清理、另一主体审计/正常退出、每例五张320×600图片（共十张）、完整源码/安装来源，全部通过。两源目录/容器及四个临时身份精确清理，原件留存；物理字节由父过程在删除前完整验证，独立过程不声称删除后重读源。新验证器也仅只读复核已通过的旧取消、正常退出两场景，未重放原动作或覆盖其独立报告。

## 运行记录及保持项

移组unit `wd-qa-recursive-acl-partial-group-removal.service`，Invocation `080abd5f391d447eb8e8e38342904f5b`，16:02:17–16:03:40 CST；停用unit `wd-qa-recursive-acl-partial-account-disable.service`，Invocation `11c7f5e84ba44c3f81a1a21364994220`，16:06:00–16:07:25 CST。均准确active/exited、MainPID0、Result success、ExecMainCode1/ExecMainStatus0；退出与完整Invocation日志归档后，只停止各自已完成QA unit。

私有证据分开保存于 `deploy/artifacts/container-recursive-acl-partial-e202e6c198c5446f9030b40449d22344/` 和 `deploy/artifacts/container-recursive-acl-partial-5390b997e7fe4df79113194cd049802c/`，含scope/baseline、cut/FD绑定、authority-change、原/恢复浏览器、Partial/ack两阶段SQL/Root/HDD/物理、独立、旧场景只读兼容、图片及unit退出/清理，不进入Git。

110项相关离线边界、8项范围、Node语法与diff检查通过。新增范围严格区分“正常退出”和“失权”，要求无cancel、无伪造owner结果、精确同主体/仅指定字段变化、旧权限仍有效及原上下文真实拒绝；新工具CI终态另核对。无产品C#/Vue/协议变更，不把旧产品测试报成本批全量重跑。

移组前504个旧文件、停用前509个文件全部保持字节SHA/九字段。普通Root145/HDD153不变，递归Root89→92→95、HDD109→111→113，恰好各新增3Root/2HDD；配置/账本及保留的user NOPASSWD不变，Agent最小授权不扩。最终21意图：8 Succeeded、5 Canceled、1无执行Expired、3预期中断Failed、4 Acknowledged，执行19/相关Job40、活动0；11核心healthy、两宿主Agent3795144/维护3598736与b880208保持，只有同Worker StartedAt因安排cut改变。

源/HDD仍同device，不签物理异盘、冷启动或维护瞬时零中断。剩余部分写入的Worker/Agent重启、Unknown/跨写入/路径/磁盘/依赖矩阵、递归ACL历史恢复、可恢复删除/长期清理及其余59项继续；仅这两项分项通过，整体任务/阶段完整签收计数不升，旧失败与截图边界保留。
