# ACL历史恢复来源故障矩阵：现有事实与待实机范围

2026-11-04，本矩阵初版接续候选28062da及来源UI工具bb51f4d，当时仅有源码/夹具覆盖，不能作为来源故障实机签收。后续28062da已标准覆盖并完成[来源UI实机签收](CONTAINER_ACL_RECOVERY_SOURCE_REAL_UI.md)，再以新资源分别签收下表四个缺失来源边界及首次登记、Ready后、首次Apply前有效替换；其它替换、依赖不可用、期限及回执优先组合仍有缺口，不能把这些分项或页面展示推广为整个矩阵完成。原候选和发布准备事实保留在[历史记录](CONTAINER_ACL_RECOVERY_SOURCE_UI.md)。

## 边界与预期

| 边界 | 已实现的行为 | 已有证据 | 尚缺实机证据 |
|---|---|---|---|
| 首次登记 | 从Agent历史读取并校验规范快照，冻结完整来源及命令；缺来源/版本不符不登记 | MVC/gRPC夹具；[真实选源后丢失，首次POST409/同键404及SQL五项0](CONTAINER_ACL_SOURCE_REGISTER_MISSING.md)；[有效替换新摘要200/旧409、首次POST409且SQL五项0](CONTAINER_ACL_SOURCE_REGISTER_REPLACED.md) | 更多内容变化、依赖/期限等首次登记组合仍待实机 |
| 原幂等键重读 | 返回原存储来源，不重读已不可用HDD，不换ID、不延长过期时间；参数冲突拒绝 | 业务夹具、正常恢复原key重读；[Ready来源缺失时原key 200且完整SQL不变](CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md) | 更广依赖不可用、原期限到期及参数冲突实机组合仍待 |
| 异步预检前 | 再读真实来源，逐字段比较完整冻结快照；变更则预检失败，不调用Root Plan/Apply | SourceChanged/替换夹具；[Queued来源缺失真实Worker预检失败、无计划/执行及原件保持](CONTAINER_ACL_SOURCE_PLAN_MISSING.md) | 来源真实替换及其它依赖/期限/回执组合仍待实机 |
| 原计划确认 | 无执行ID时先核实来源，再登记执行；来源变更拒绝且仍无执行Job | 三边界夹具；原确认/登录绑定；[Ready后来源缺失409](CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md)及[同capture有效替换409/新摘要200](CONTAINER_ACL_SOURCE_CONFIRM_REPLACED.md)，均无新执行 | 更多确认内容变化、依赖/期限及其它组合仍待 |
| 第一次Apply前 | 先Inspect原执行；只有没有原回执且允许新写入时再验来源。变更时Discard原执行并失败，不调用业务首次Apply | SourceChanged/替换夹具；[Pending来源缺失真实Worker→原Root Aborted/无前像及迟到围栏](CONTAINER_ACL_SOURCE_APPLY_MISSING.md)；[有效替换新摘要200/旧409、原执行Aborted与原件保持](CONTAINER_ACL_SOURCE_APPLY_REPLACED.md) | 更多来源内容变化及依赖/期限/回执组合仍需验收 |
| 临时不可用后恢复 | IO/传输不可用不等于来源已变更；不能提前写入。恢复可用后沿用原身份/执行ID/期限继续 | 本批plan/confirm/apply三处IO夹具：一次正确重续、一次Apply、无另建执行或Discard | 真实依赖不可用与恢复、原时限内同任务继续及真实文件/HDD证明 |
| 已有原Root回执 | Completed/Unknown/Aborted先按原回执对账，不因旧来源失联重新Apply或改判历史事实 | 本批三态夹具；已有实盘写后目标/实例、撤权及重启组合各自记录 | 真实来源失联时原三态回执优先对账；不得用目标消失替代 |

IO夹具只表示来源读取通道故障，不证明整个Agent或HDD离线，也不能据此推断其它依赖仍可用。

2026-11-04后续工具候选：新增仅本轮新普通捕获的HDD元数据隔离/同inode精确还原工具，22项隔离测试及真实安装态只读137份清单预检通过；未调用生产隔离，未增加任何本表真实故障完成项。进程强杀恢复、无覆盖/原件保留及ctime边界见[工具候选与后续真实门禁](CONTAINER_ACL_SOURCE_QUARANTINE_CANDIDATE.md)。

2026-11-04再后续：c3dca1f新资源完成Ready后来源丢失确认拒绝实机门禁及独立只读核验。原历史404、确认409/OPERATION_CONFLICT、原键200/完整SQL不变且执行0，精确还原来源200后页面仅明确取消一次并正常双端退出；原137份字节九字段保持，总138，本轮新捕获ctime改变如实记录。只更新上表“原计划确认”的缺失来源分项，不补签替换、预检、首次Apply或整盘离线。[完整事实与边界](CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md)。

2026-11-04后续bbd1b52完成已确认Pending、首次Apply前来源丢失：Worker停止时原确认202，仅隔离新捕获后真实404/原key200，再启动同一Worker；原执行Failed/SOURCE_CHANGED、Root Aborted/false、RootStarted始终空，原件还原后迟到同ID Root Apply仍Aborted且原字节保持。5实际PNG/两主题任务详情、4审计、双端退出/HTTP权限、实盘及独立复核、准确清理通过；旧138份字节九字段保持/总139，只有Worker StartedAt改变。仅新增上表该缺失来源分项，不补签其它阶段/替换/整盘不可用。[完整实机及边界](CONTAINER_ACL_SOURCE_APPLY_MISSING.md)。

2026-11-04后续4d38a51完成异步预检前来源丢失：真实选源后仅停原Worker，来源可读时登记202/Queued；新原件隔离后历史404/原key200，启动同Worker后原预检Failed/SOURCE_CHANGED，无计划/确认/执行/Root及恢复HDD前像。原件还原、5真PNG/两主题“未证明”预检摘要、2审计、双端退出/HTTP拒绝、完整实盘和独立核验及准确清理通过；旧139字节九字段保持/总140。只新增本表该缺失来源分项，首次登记拒绝、替换和依赖中断等不补签。[完整实机及边界](CONTAINER_ACL_SOURCE_PLAN_MISSING.md)。

2026-11-04后续1537d6a完成首次登记前来源缺失：真实选源后隔离准确新原件，历史404、原页面登记409/OPERATION_CONFLICT、同键和预期Job均404，SQL意图/Job/outbox/事件/恢复审计五项0，Root/HDD缺席；原件还原后仅查询，仍无登记，不伪造Failed任务。3真PNG/两主题原键未找到、正常退出/引用清空、HTTP权限、完整实盘及独立核验/准确清理通过；旧140份字节九字段保持/总141，11核心/两宿主完全保持，无服务启停。首次登记、异步预检、Ready确认、首次Apply四个缺失来源分项各有新资源实机证据；真实替换、依赖中断/期限/回执等仍未补签。[完整实机及边界](CONTAINER_ACL_SOURCE_REGISTER_MISSING.md)。

2026-11-04后续6c5b331实机Ready后有效替换通过：准确新capture以另一个临时inode只改时间+1/重算digest；新摘要200、旧摘要409/AGENT_VERSION_CONFLICT、原确认409，原Ready SQL完全不变、执行0。原件还原后明确取消/正常退出、2真PNG/3审计/HTTP拒绝、旧141份字节九字段保持/总142，临时替换字节归档并精确清理。独立首轮因验证器错误沿用旧数据前缀失败，06f179b修正并冻结后只读重核通过，未重放真实动作，不称整批首轮通过。仅该有效替换分项签收，其它阶段/变化与依赖故障继续。[实际事实与修正边界](CONTAINER_ACL_SOURCE_CONFIRM_REPLACED.md)。

2026-11-04 首次Apply前有效来源替换实机分项通过：fd23d93冻结92源码/13安装helper，新资源Ready后仅停原Worker，确认202/Pending后发布同capture独立inode的时间+1/自洽digest替换；旧摘要409/新摘要200、原key及Pending SQL保持。同Worker启动后原执行Failed/SOURCE_CHANGED、Root Aborted/false、RootStarted空/无当前前像；原件还原后迟到原ID Root Apply仍Aborted。5真PNG/两主题任务详情、4审计、正常双端退出/HTTP拒绝、独立只读及准确清理通过，旧142HDD字节九字段保持/总143、新原件ctime改变保留。11核心healthy/同ID镜像保持、仅Worker StartedAt变，两宿主/7配置账本与root只读Git索引保持；142离线检查0跳过，无发布宿主重启。其它替换/依赖/期限/回执、递归ACL/删除、维护连续性及59项继续。[实机与验收边界](CONTAINER_ACL_SOURCE_APPLY_REPLACED.md)。

2026-11-04 首次登记前有效来源替换实机分项通过：c0245ed冻结97源码/13安装helper，新资源真实选源后发布同capture独立inode的时间+1/自洽digest替换，旧摘要409/新摘要200；原POST仍原摘要/当前目标版本，登记409/同key及两个预期Job404，SQL意图/Job/outbox/事件/恢复审计五项0、Root/HDD缺席。原件还原后只查询仍无登记，3真PNG/两主题、正常双端退出/HTTP拒绝、独立只读与准确清理通过，旧143HDD字节九字段保持/总144、新原件ctime改变保留。11核心/两宿主/7配置账本与root只读Git索引保持，无发布启停；9202339另补异常请求查账与未写成日志stage保留告警，仅离线4新项，最终154检查0跳过，不重放。异步预检替换/依赖/期限/回执、递归ACL/删除和维护连续性及其余59项继续。[实机与工具修复边界](CONTAINER_ACL_SOURCE_REGISTER_REPLACED.md)。

上表的“同摘要不同时间戳”是显式不可信gateway夹具，用于证明服务层比较完整快照；不是实际HDD可绕过digest校验的结论。实际Agent历史读取还必须检查元数据digest、存储证明、对象及数值presence。

## 源码入口和已验证范围

- `ContainerAclWorkflowService.Restore.cs`：QueueRestore首次读取/冻结；GetRestoreByKey原键只读；ValidateRestoreSource/ReadSource在新写入边界核实。管理员Recovery摘要直接读取原不可变SQL快照，**不检查HDD当前可用，不能拿摘要存在当作来源可恢复证明**。
- `ContainerAclWorkflowService.cs::ConfirmAsync`：原owner/计划/版本校验后，在新执行登记前验来源，之后再次实时授权。
- `ContainerAclDispatcher.cs`：计划前、第一次Apply前重新验源，实际Started标记在验源之后。Inspect已有回执优先；来源Changed与普通IO不可用分别处理。
- `AgentGateway.ContainerAclRestoreSource.cs`：gRPC NotFound/Aborted映射SourceChanged；Unavailable/PermissionDenied/Unimplemented保留原分类，不能全当来源变化。
- `ContainerAclWorkflowTests.Restore.cs`：三个新写入边界、同digest替换拒绝、IO恢复原任务及三个原回执优先测试。它们使用显式身份/仓储/Agent/历史夹具，不能当真实PG、Authentik或磁盘验证。
- 既有真实隔离PG持久化门禁验证restore_source_json的不可剥离/替换、MAC、事务、约束和降级拒绝；其Root回执/历史为合成输入，不能补签本矩阵实际Worker来源故障。

## 真实记录的准确归属

[正常历史恢复](CONTAINER_ACL_RESTORE_REAL_UI.md)证明实际新来源选择和恢复；[写后目标消失](CONTAINER_ACL_RESTORE_UNKNOWN.md)、[写后实例变化](CONTAINER_ACL_RESTORE_INSTANCE_CHANGE.md)证明目标/实例的Unknown处置及保护。两者都不是在plan/confirm/apply之间制造历史来源失联或替换。已实机记录的原件保持，不重放旧动作来补新矩阵。

初始夹具批次的Debug相关业务回归121项全通过（26.8328秒），较当时前批113项净增8个边界/回执用例；未重跑此前全量、原生PG或浏览器测试来增加该批数字。该初始批次没有新账号、真实来源故障、HDD更改、Root写入、服务重启或发布，不覆盖后续各场实机事实。完整来源故障验收仍需全新自有资源、真实身份/Vue/API/Worker/Agent、SQL/审计、实际文件与HDD、原回执/期限/准确清理和独立只读核验。旧HDD原件、旧失败源树和受保护配置不能为取得证据而修改，17项移出不算完成，59主项及阶段完成率不升。


## 后续：来源暂不可用的原期限到期

2026-11-04，新增规划/执行两种原期限自然经过的夹具用例。先发生一次来源IO不可用并延期，再让注入时钟超过原deadline：规划以PLAN_EXPIRED失败，已确认执行仅Discard原ID并记录Aborted/Interrupted，Apply均0，不再读取来源、不换ID、不延长原期限、不改冻结来源，也不补RootStarted。两项专项通过，相关业务全组123项通过、0跳过（24秒）。这是实际业务编排的时限回归，时钟/仓储/依赖均为夹具，**不是生产时钟操作、HDD离线或实机超时签收**。没有改产品代码或增加自动重试。
