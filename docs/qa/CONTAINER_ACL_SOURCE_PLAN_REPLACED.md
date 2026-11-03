# ACL恢复：异步预检前有效来源替换

2026-11-04，工具候选，尚未实机签收。显式profile8/source-plan-replaced用全新资源完成正常捕获/选源；仅停止原Worker，真实登记202/Queued后才发布同capture独立inode的有效替换。原件保留，替换仅时间+1并重算规范digest。原摘要须409/AGENT_VERSION_CONFLICT、新摘要须200，原key仍返回原Queued/原来源，完整SQL/目标及旧HDD保持后才启动同Worker。

原任务预期规划Failed/CONTAINER_ACL_RESTORE_SOURCE_CHANGED，原来源/命令/身份/期限不变，plan_json/确认/执行/Root/当前恢复HDD缺席；ACL确认未证明，不当Aborted或已执行。原件还原后只查询原失败任务，两主题真实任务详情和原上下文正常退出/HTTP拒绝、独立核验与准确清理仍待取得。旧缺失及其它替换场景不重放。

新增8项快照/原Queued引用、请求绑定/目标版本与独立数据生成规则检查；相关162项root离线检查0跳过、严格Python/Node语法通过。复用登记尝试查账/未成日志stage保留告警，失败仅在证明终态和完整物理清单后清理，不删除不明源。CI只接入离线检查，不自动执行工作站故障；新CI终态另取，不称全量前后端重跑。

不发布、不重启宿主、Agent或数据库，不改SQL/缓存/时钟/旧历史或Agent授权，不自动回滚dev。当前59主项及阶段不升完成；[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)、递归ACL/恢复、可恢复删除/长期清理和维护连续性及其它目标继续。
