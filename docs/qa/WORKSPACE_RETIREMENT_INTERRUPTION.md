# 真实退役执行中断与显式新任务恢复

接续：2026-11-11，后续已独立补验真实源码部分删除后中断、显式恢复与资源收尾，见[部分删除实机证据](WORKSPACE_RETIREMENT_PARTIAL_DELETION.md)。以下保留原批次仅确认执行启动的边界。

接续：1b5cefd62518e235ad419ba3c6594f3841714404准确CI42951997927现已全13成功，最终收据另存 `deploy/artifacts/workspace-retirement-interruption-record-20261111/exact-ci-final.json`；连接器传输失败及后续只读查询记录保留。没有重复发布产品。真实Stopped与独立恢复也已后续验收，见[Stopped实机证据](WORKSPACE_RETIREMENT_STOP.md)。以下保留原批次边界。

2026-11-11，使用已部署产品356ba6c1。前序892e79583c1cfd134a1af4f936b5d386049fd754准确CI42948605633全13成功；本批只有QA验证器、CI门禁及记录，没有新产品部署，也没有扩大正式Agent授权。

## 实际验收

- 新临时管理员经真实Authentik、Vue/BFF、Worker及Agent完成空间分配、强制硬配额、源码导入发布、Node测试2/2、.NET独立构建及独立测试2/2，重新启用后Node复验，共四运行成功，零测试失败或跳过。普通用户访问及恢复均拒绝，双端正常退出。
- HDD Capture和独立Verify完成后，页面另行确认删除。仅对声明的新QA首个execute任务核对数据库当前空间/owner/退役/attempt/原计划及完整宿主请求；排除原空间、已结算任务、恢复尝试、已取消或已有结果任务。
- 控制器在该任务私有日志flock内，复核不可变request/dispatch、当前boot、固定retirement单元路径和实际cgroup device/inode。先持久化故障前证据，再使用已安装Execution控制精确终止该存活执行及其子进程；不写cancel、result、源码、配额或业务SQL，不停Worker/Agent，不冻结进程，不按名称扫杀。
- 宿主真实Observe为Interrupted，Worker结算原attempt 0，空间保持AttentionRequired且预算未释放。原task为`a89c3bc0-d74c-4044-bb4a-4ab953e82eba`。不是模拟回执，也不是把Rejected计为Interrupted。
- 页面独立勾选接续删除、使用新幂等键创建attempt 1：`e1df34a0-1b76-4cd6-9fbd-14794f5bf1f3`。新任务接续原engine请求并成功退役；原证明、请求、计划、派发/结算/首次确认及有效期由独立SQL与前后API列表比较保持。320暗色实际历史图已查看，原中断和新成功同时清晰可见，无横向溢出。
- 首空间及同预算复用空间均通过产品流程归档、独立确认删除和退役。配额八项全零，源/claim缺席，HDD树保持，退役后8份报告可下载，预算回到原值；不是直接删目录或改SQL释放容量。
- 完整wrapper首次通过，无修复重试。原19,178条记录、13条登记（5活动分配、8原退役）、34份报告及原HDD/journal保持；11核心服务及宿主进程身份保持。120,000,000,000/300,000,000,000字节硬限制、原用户source改动及user NOPASSWD保持。两临时身份删除、控制器正常退出、精确故障执行已空、wrapper成功退出、活动任务和锁归零。

## 控制器验证与证据

4项离线边界测试覆盖原空间/owner/计划/当前任务替换、旧boot/错cgroup/已完成或取消任务拒绝、证据失败不得触发kill。新独立systemd实测使用惰性工作夹具，确认真实子进程死亡、整个私有日志逐字节保持、原任务不能重新执行；这是执行控制证明，不代替上面的真实产品验收。原9项身份、5项磁盘、14项CI合同及8项范围检查通过。CI新增实际systemd门禁和Python/Node入口检查。

`deploy/artifacts/workspace-retirement-interruption-20261111/`封存 **91文件 / 25,618,016字节**（不含manifest自身），manifest SHA256：
`12936fe57a15c1e457d87b435151efb00592a64c52d6ea04131fea62999c0978`。逐文件复验通过，8个实现/CI源码均有独立摘要和副本。

重点：`interruption-1-binding.json`、`interruption-1-before-kill.json`、`interruption-1-after-kill.json`、`interruption-1-settled.json`、`persistent-original-failure-preserved.json`、`accepted.json`、`batch-acceptance.json`、`resource-closure.json`、`candidate-source-sha256.json`和真实窄屏截图。验收新空间为`39ab62a2-45a8-4838-9bb8-33f2fd4a33be`及`8981b278-45ca-4efc-8fb6-5325cc00950a`；登记、失败和成功任务、报告及审计继续保留。

本轮确认执行已存活后精确终止；没有独立证明终止时源码已发生detach或删除，因此不签“删除途中强杀”门槛。真实Stopped、另一管理员恢复、其他故障矩阵和全部59项/ST272/PET6/供应商Live2D16仍待；本批提交自身准确CI另待。完整主任务0/59，17项移出不恢复、不计完成；不为纯验证记录重复发布产品，不签master。
