# ACL历史恢复来源故障矩阵：现有事实与待实机范围

2026-11-04，本矩阵最初接续产品候选28062da及来源UI工具bb51f4d。**本文件记录源码/夹具覆盖和实机缺口，不是新的HDD来源丢失/替换实机签收。** 后续28062da已标准覆盖并完成[来源UI实机签收](CONTAINER_ACL_RECOVERY_SOURCE_REAL_UI.md)；原候选与发布准备事实保留在[历史记录](CONTAINER_ACL_RECOVERY_SOURCE_UI.md)，页面来源展示通过不补签本矩阵。

## 边界与预期

| 边界 | 已实现的行为 | 已有证据 | 尚缺实机证据 |
|---|---|---|---|
| 首次登记 | 从Agent历史读取并校验规范快照，冻结完整来源及命令；缺来源/版本不符不登记 | 实际MVC/gRPC夹具拒绝测试；正常实盘登记见前批 | HDD真实来源不存在或被替换时的首次登记拒绝 |
| 原幂等键重读 | 返回原存储来源，不重读已不可用HDD，不换ID、不延长过期时间；参数冲突拒绝 | 业务夹具、真实正常恢复原key重读 | 原真实来源失联期间原key只读核实 |
| 异步预检前 | 再读真实来源，逐字段比较完整冻结快照；变更则预检失败，不调用Root Plan/Apply | SourceChanged异常与同摘要不同时间戳的业务夹具 | 在实际Worker预检边界发生来源不可读/替换，核实SQL、原件、Root零调用 |
| 原计划确认 | 无执行ID时先核实来源，再登记执行；来源变更拒绝且仍无执行Job | 三边界异常及替换夹具；原确认/登录绑定测试 | Ready后真实来源失联/替换，真实Vue确认拒绝、无新执行 |
| 第一次Apply前 | 先Inspect原执行；只有没有原回执且允许新写入时再验来源。变更时Discard原执行并失败，不调用Apply | SourceChanged/替换夹具；新测RootStarted为空、原来源字节保持 | 已确认任务被Worker阻止；Root仅留原ID的Discard/Aborted回执，目标ACL/字节不变、不新增当前ACL前像HDD |
| 临时不可用后恢复 | IO/传输不可用不等于来源已变更；不能提前写入。恢复可用后沿用原身份/执行ID/期限继续 | 本批plan/confirm/apply三处IO夹具：一次正确重续、一次Apply、无另建执行或Discard | 真实依赖不可用与恢复、原时限内同任务继续及真实文件/HDD证明 |
| 已有原Root回执 | Completed/Unknown/Aborted先按原回执对账，不因旧来源失联重新Apply或改判历史事实 | 本批三态夹具；已有实盘写后目标/实例、撤权及重启组合各自记录 | 真实来源失联时原三态回执优先对账；不得用目标消失替代 |

IO夹具只表示来源读取通道故障，不证明整个Agent或HDD离线，也不能据此推断其它依赖仍可用。

2026-11-04后续工具候选：新增仅本轮新普通捕获的HDD元数据隔离/同inode精确还原工具，22项隔离测试及真实安装态只读137份清单预检通过；未调用生产隔离，未增加任何本表真实故障完成项。进程强杀恢复、无覆盖/原件保留及ctime边界见[工具候选与后续真实门禁](CONTAINER_ACL_SOURCE_QUARANTINE_CANDIDATE.md)。

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

本批Debug相关业务回归121项全通过（26.8328秒），较前批113项净增8个边界/回执用例；未重跑前批全量、原生PG或浏览器测试来增加本批数字。没有新账号、真实来源故障、HDD更改、Root写入、服务重启或发布。完整来源故障验收仍需全新自有资源、真实身份/Vue/API/Worker/Agent、SQL/审计、实际文件与HDD、原回执/期限/准确清理和独立只读核验。旧HDD原件、旧失败源树和受保护配置不能为取得证据而修改，17项移出不算完成，59主项及阶段完成率不升。


## 后续：来源暂不可用的原期限到期

2026-11-04，新增规划/执行两种原期限自然经过的夹具用例。先发生一次来源IO不可用并延期，再让注入时钟超过原deadline：规划以PLAN_EXPIRED失败，已确认执行仅Discard原ID并记录Aborted/Interrupted，Apply均0，不再读取来源、不换ID、不延长原期限、不改冻结来源，也不补RootStarted。两项专项通过，相关业务全组123项通过、0跳过（24秒）。这是实际业务编排的时限回归，时钟/仓储/依赖均为夹具，**不是生产时钟操作、HDD离线或实机超时签收**。没有改产品代码或增加自动重试。
