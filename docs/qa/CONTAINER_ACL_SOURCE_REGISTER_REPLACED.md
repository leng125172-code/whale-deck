# ACL恢复：首次登记前有效来源替换

2026-11-04，候选工具尚未实机签收。profile7/source-register-replaced使用全新自有普通ACL捕获，真实页面选源后才有效替换规范元数据；原件保留，另一临时inode仅时间+1并重算自洽digest。原摘要须409/AGENT_VERSION_CONFLICT，新摘要须200且完整快照仅上述两字段改变，原POST必须仍携带原摘要/目标版本。

登记须409/OPERATION_CONFLICT、同key与两个推导预期Job须404，SQL意图/Job/outbox/事件/恢复审计五项整数0，Root/HDD缺席。精确还原后仅查询，仍不得凭空登记或自动重试；不把推导ID当实际Job，不制造Failed任务。

150相关离线测试以root执行通过、0跳过，含8新专项、16替换工具、22隔离、旧各阶段来源与恢复和8范围检查；严格Python/Node语法通过。CI只接入离线门禁，前批c9baf14/41721278529仍in_progress，新终态待取，不称全量前后端重跑或实机已通过。

不发布、不启停Worker/Agent/维护宿主、不改SQL/缓存/系统时钟/旧历史或Agent授权，不自动回滚dev。后续真实身份/Vue/API/Worker/Agent、物理文件/HDD、两主题页面/正常退出与独立核验仍待；当前59主项不升完成。[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。
