# 递归ACL部分写入后进程重启

2026-11-04，承接[Partial实时撤权](CONTAINER_RECURSIVE_ACL_PARTIAL_REVOCATION.md)。本批补齐 Worker 强杀及 Agent 重启的独立候选工具，尚未运行本批真实操作，不计完成。

只对新隔离容器的四对象树及两位新临时管理员操作。必须先证明首项 ACL 已真实完成、其余三项保持，再核对无其他工作，才强杀同 Worker 或重启固定 Agent。严格绑定当前 QA unit 的 MainPID/cgroup/Invocation、产品 helper 的两个内核 FD 与 pidfd，不接收任意 PID 或 unit。

Worker 场景要求原 SQL 租约自然到期，周期核对原 token/期限/执行身份、Root 字节及已发生的部分物理结果，不改时钟或数据库。Agent 场景要求实际 PID 变化、维护 PID 与版本保持，保存窗口内维护响应；采样成功不推断瞬时零中断。

恢复后要求 Partial 与保护保留，原页面显示真实五计数；另一管理员先“暂不处置”不改变任何事实，再一次明确 ACK。原执行 ID、实际修改、计数、Root/HDD 原件保留，不续做、不回滚。只在完整物理证明后精确清理本批源对象，失败原件保留。

候选检查：94 项相关 Python 工具测试在 Linux root 下全部通过、8 项范围测试通过，Node 语法和 diff 检查通过。尚未执行真实重启、原页面及独立验收，产品仍 b880208/1.53，不触发新发布。
