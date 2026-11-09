# Runner 签名取消和中断回收

2026-11-09，代码 `b29fcee2`。Agent 1.65 提供 `development.run.control`。Cancel 和 Reclaim 分别签名，Gateway 复核已分配工作空间、归属和原运行预算，宿主核对原运行请求摘要。启动时写入 boot ID、systemd 启动单元的 cgroup 路径和设备号、inode，并在配额内预留取消意图。成功、失败及超时回执继续保留。

取消验证身份后先持久化意图，再通过该 cgroup 的已打开目录写 `cgroup.kill`。复核子树为空或原路径消失后，清除该运行的临时 bundle，以原子重命名发布 `Cancelled` 回执。Reclaim 检查同一身份，活跃时返回忙碌，确认已停止后清理并发布 `Interrupted`。若前一次取消已记录意图，回收会保留 `Cancelled`。原终态优先，重试只读已有回执。路径复用、状态不明、挂载位于 bundle 内或缺少执行身份记录时保留数据。[Linux cgroup v2](https://docs.kernel.org/admin-guide/cgroup-v2.html)

## 实际验证

- Linux amd64 的断网 KVM 使用非 root Agent、真实 Unix Socket、sudo、systemd 259、runc 1.5.2 和固定摘要 Node。两份不同工作空间的任务同时实际运行。Reclaim 拒绝回收活跃运行，Cancel 拒绝改动预算的请求。正确签名的取消只终止原 cgroup，另一工作空间的 Node 仍在运行。目标回执经重复取消、查询和启动重试保持一致，两个任务各执行一次。
- Agent 停止后实际重启。对遗留运行执行 Reclaim，删除其 bundle，保留中断回执，重试没有启动新任务。测试还覆盖旧执行记录缺少 cgroup 证明时保留 bundle，以及同名 cgroup 换 inode 时拒绝写入。
- 标准宿主发布包通过 `SHA256SUMS`，使用包内自包含 Agent 和固定摘要 umoci 重跑以上场景。额外在真实解包子组存在进程时发出取消，解包组退出，bundle 删除，工作负载命令的标记从未写入。独立 Linux 命名空间挂载真实嵌套 bind，回收拒绝越过挂载，源文件保持原样。
- 初轮真实取消已终止任务，但 systemd 先回收了 `cgroup.events`，旧目录 FD 仍报告非零链接数。改用同一 cgroup 根 FD 复核原路径和 inode 后，真实取消和回执重放通过。
- Agent 回归 3195 项通过。Runner、存储、隔离、rootfs、解包及范围回归与 Shell 语法检查通过。

Worker、BFF 与页面的任务编排继续接入。1.64 及更早缺少执行身份记录的遗留 bundle 需要独立核对，自动回收会拒绝。当前会话 `NoNewPrivs=1` 阻止标准特权安装，物理工作站仍待验收。

私有证据位于 `deploy/artifacts/development-workspace-runner-control-b29fcee2/`，清单 SHA-256 为 `996d464ac3bb94fbb701999ae191d264e46e6a3c6c6af5ef7b9607375236fdfa`。
