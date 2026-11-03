# ACL恢复：首次Apply前有效来源替换

2026-11-04，工具候选，尚未实机签收。显式profile6/source-apply-replaced仅针对新资源正常捕获的来源；已有缺失和Ready确认替换记录保留，不重放。

正常Authentik/Vue捕获并选源、Ready后，只停止原Worker；真实确认202/Pending后，用独立inode发布仅时间+1/自洽digest的替换记录。必须取得旧摘要409/AGENT_VERSION_CONFLICT、新摘要200和原key/SQL保持，才启动同一Worker。预期原执行Failed/CONTAINER_ACL_RESTORE_SOURCE_CHANGED、Root Aborted/false、RootStarted空且无当前恢复HDD前像。精确还原来源后迟到原ID Root Apply仍应Aborted；两主题真实任务摘要、正常双端退出、HTTP权限、原件及独立核验仍须取得。

142项相关离线检查以root执行通过、0跳过，包含新6项读取/冻结来源/独立数据生成规则检查，替换工具16、隔离工具22及旧故障证明与范围检查。普通用户首轮跳过38项权限测试，随后已root补跑；另一次范围测试文件名错误未收集，已按test_scope_baseline.py补验。不称前次跳过或空收集为通过。严格Python/Node语法通过。CI仅接入离线测试，不在CI自动调用实机门禁；新终态待取。

不扩大安装/sudo/RPC，不改产品、SQL/缓存/时钟或旧历史，不自动回滚dev。源与历史同device不算异盘恢复；独立核验不声称重读清理后的源文件。当前59项不升完成，后续[来源矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)、递归ACL、可恢复删除/长期清理与其它目标继续。
