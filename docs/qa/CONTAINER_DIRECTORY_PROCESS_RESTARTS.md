# 容器目录 Prepared 期间 Worker/Agent 真实中断

2026-10-28。实际产品仍 `5f3622d07f7e265762785be56712323fde1b5405` / Agent minor44，没有因验证器修改重复构建或部署。以下是安装态真实业务任务，区别于内部引擎测试，也不代表完整目录故障矩阵或文件管理完成。

## 已通过的两项

| 切点 | 实际动作与观察 | 原任务最终结果 |
|---|---|---|
| Root Prepared，业务 SQL Applying 且屏障生效 | 准确原 Worker CID SIGKILL，显式启动同 CID/镜像；产品在0.674秒结束旧 helper。保留原90秒租约，三次观察仍为 Prepared/屏障生效/目标不存在，API可用；自然租约到期后对账 | Failed / `CONTAINER_DIRECTORY_INTERRUPTED`；Root Aborted；目标不存在，租约/屏障/待处理Outbox清空 |
| 同一 Prepared 业务切点 | 排除其他活动 Job/终端/锁/上传屏障后，实际重启 Agent；旧 helper 在0.165秒退出，总优雅重启30.474秒；维护入口 PID 不变 | 同样 Failed / Interrupted，Root Aborted；未重复 Apply，目标不存在，租约/屏障清空 |

Worker 证据：`deploy/artifacts/container-directory-inflight-252d909408954717b89a55232805c05c/` 的 worker-crash 基线、Prepared/Aborted Root、绑定驱动、原租约等待及结果。原任务 `d723440e-27ec-8e96-a4d3-b60d9dded5b7`。这是首轮组合运行中已独立通过的 Worker 项，**该轮整体因后续 Agent 验证器预检失败，不记整轮通过**。

Agent 最终通过证据：`deploy/artifacts/container-directory-inflight-b50266c178af4515a0efcf753aa5c1a2/`。原任务 `68b40483-a341-059a-511c-f37eda8bf361`，验证器来源 `745e8cb`；`verified.json` 仅声明这一项 Agent 门禁通过。使用 pidfd 在另一个观察线程等待旧 helper 退出，完整重启就绪耗时另行记录，15秒 helper 上限未放宽。

两项均没有杀 helper、修改 SQL/缓存/系统时钟或缩短租约。冻结只暂停同时证明持有准确容器 root FD 和原操作锁 FD 的自有 helper。保留永久 Job、审计、Root 原件；准确验证后清理各自测试容器、源及空目录。

## 独立终态复核

最终 Agent 证据目录的 `independent-final.json` 再次读取实际 PostgreSQL、Root 文件和 Docker：

- 两个原任务 SQL/Job 都是 Failed，稳定错误码 Interrupted，各一条接受审计、有终态审计、待处理Outbox为0。
- 永久 Root 与当时归档的完整 JSON 字段一致，均 Aborted；独立记录当前原件 SHA-256。归档格式化 JSON 与原始紧凑 JSON 的字节不同，不把字节差异误报为字段变化。
- Worker 完成时间不早于最初租约到期时间；没有伪造提前恢复。
- 两个准确 CID 已不存在，原测试源目录不存在；11个核心容器全部 healthy。
- 目录累计24任务：13 Succeeded、3 Canceled、4 Failed、4 Acknowledged；活动/租约/屏障均0。其他 Job/终端/宿主路径锁/上传屏障也为0。

## 失败记录保留

1. 首轮 Agent 预检把旧的终态失败 transient unit 文件当作活进程，未执行此次 Agent 重启。验证器现要求读取 ActiveState、MainPID/ControlPID及cgroup：只有确实终态、PID为0且cgroup无进程时才忽略。未删除或重置旧失败记录。该测试任务释放后正常完成，已准确清理。
2. `container-directory-inflight-0ff5bb443b254aa4a87906d01a82f476/` 实际成功重启 Agent，但验证器在阻塞约30秒的 `systemctl restart` 返回后才计算 helper 耗时，错误混入了优雅退出耗时。此轮保留为失败；改为并行观察后重新执行通过。没有据失败轮补造“15秒内退出”的证据。
3. 本次只读独立复核曾错误比较格式化与紧凑 JSON 字节，以及大小写不同的 Docker 404 文本；均修正为完整字段比较/明确对象不存在后重验，不能把前两次断言失败计为成功。

## 继续工作

目录交换前后剩余故障组合继续保留。下一步推进容器复制独立计划域、永久回执、签名RPC、持久业务/共享屏障/Worker和真实身份页面；移动、删除、权限、完整隔离及长期留存仍保留。当前59项，17项明确移出不算完成。
