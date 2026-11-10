# 开发空间归档长任务执行控制候选

2026-11-10，基于 bd4aac06 实现载荷之外的持久长任务日志和精确执行控制。当前仅为内部候选：尚未安装、接入固定 helper、签名 Agent RPC、持久业务/Worker/BFF/Vue，没有新增权限或 capability 广告。完整用户归档、空间退役和预算释放仍未完成，59 项主任务及子门槛完成数不增加。实机继续为已验收的 [42ea297d](DEVELOPMENT_WORKSPACE_ARCHIVES_RELEASE.md)。

前一记录提交 bd4aac06 的准确 [CI 42806352410](https://github.com/leng125172-code/whale-deck/actions/runs/42806352410) 已取得完整终态：目标 SHA 精确匹配，12/12 成功。本批新代码仍须独立准确 CI 和后续标准安装、真实身份产品验收。

## 实现边界

Plan、Capture、Verify 各自绑定不可变 taskId 和完整请求摘要，共用原 archiveId。载荷外日志固定为 /var/lib/whaledeck-development-archive-operations，根/任务目录要求 root 0700，文件为 root 0600、单链接。每 task 仅执行一次；已持久执行证明但无结果的任务，只能观察原执行是否仍在，消失后为 Interrupted 或 Stopped，不自动重发或另建捕获。部分 HDD 字节继续保留。

短日志锁只覆盖入场和小型元数据；扫描、复制及独立重读在释放任务锁后执行。Observe/Cancel 不获取 NVMe 或 HDD 存储锁。长操作保持原日志根和任务目录的独立打开描述符，提交结果前验证原 dev/inode，既防止相同 JSON 被复制到替换目录后接收成功，也不会因持锁描述符的 dup 而阻断观察。

执行证明严格绑定 boot ID、专用 systemd cgroup 路径及 dev/inode。取消先持久落字节标记，再通过原目录 FD 写 cgroup.kill，并等待原 scope 确认空或消失。unit 名字不能授权停止同名替代进程；其他启动周期仅证明原执行不存在。预启动取消可以拒绝迟到 Start。已持久结果在原执行退出前只报告 Finishing，晚到取消保留已提交事实。Stopped 不表示 HDD 上没有字节。

通用文件打开规则保持只读边界；仅固定私密取消文件和原 cgroup.kill 使用独立的 O_NOFOLLOW 文件句柄。结果 schema 通过 canonical 字节校验，拒绝 Python 的 true/1 或 1.0/1 相等混淆。首次取消初始化留下空文件时，仅在权限/所有者/链接正确且执行、结果及其待提交文件均不存在时允许恢复；已有执行证据的空文件拒绝。

## 实际验证

- 专项 **24/24**，加既有归档 **18/18**，共 **42 项零跳过**。真实 FD、codec、内容摘要和日志落盘用于这些测试；配额、HDD 身份和 cgroup 文件行为的 double 明确标注。包含两个真实 fork/SIGKILL 崩溃窗口、复制替换根/任务目录拒绝、损坏 schema、不可变请求、晚取消和持存储锁时的有界观察/停止。
- 隔离原生脚本 validate_development_archive_execution.py --run 实际创建专用 systemd units，工作进程 capability 集合为空。七门槛通过：原 scope 与后代 pidfd 精确取消、同名重用拒绝旧证明、非取消强杀保留 Interrupted 且不重跑、真实退出后的成功与晚取消、预启动取消拒绝迟到 unit、原执行资源收尾、探针失败路径先精确停止自有执行再移除私有树。最终无本批 unit 遗留。
- 原生脚本的工作负载是明确的惰性存储 double，仅证明真实 systemd/cgroup/日志控制；没有打开正式空间或 HDD，不代替此前的实盘归档证明，更不构成授权产品链通过。
- CI 已加入专项测试和独立原生 systemd 步骤；必须实际执行，不能用语法检查或 skipped 结果替代。

## 失败与证据

保留初始语法错误、通用文件打开选项拒绝、错误的损坏状态测试前提及修正后完整日志。独立复核指出的首次取消初始化窗口和长任务日志目录替换漏洞均有回归。第一轮原生运行因无额外 capability 的进程无法穿过用户私有 home 读取脚本而失败；改为先核对并复制测试源码到 root 私有 /run 树，未扩大 capability，原失败和对应 journal 保留。验收工具自身的失败收尾也已补上并实际注入异常验证。

私有证据目录：deploy/artifacts/development-archive-task-control-20261110-bd4aac06/，包含失败/成功原件、准确前序 CI、候选源码摘要及清单。本批不回写此前封存目录。原未提交源码合并预览 diff SHA256 保持 e56a10d30f5cb49bf771ae6fc9bd35a3564bfc586b4ca3338899940b84c0eca2。项目图谱仍待同步。

## 接续

固定 helper 必须只接收完整、受限的任务请求并派生专用 unit，不复制 Runner 的 OCI、Delegate 或大 capability 集，不授予 CAP_SYS_RESOURCE 绕过配额。Agent 继续短 Start 与独立 Observe/Cancel、完整请求绑定和限时 nonce 签名，不能把归档扫描/复制塞进启停的同步 Apply。

随后接入持久归档意图、当前资源授权和共享事务围栏；待归档期间禁止恢复，失联不放行。独立 Verify 的 receiptSha256 必须来自服务端授权任务，不能信任浏览器或 HDD 自报摘要。只有完成明确确认与完整独立验证，才继续载荷外不可复活标记、精确移除及内核配额/claim 归零，最后释放数据库预算并保留原历史关联。
