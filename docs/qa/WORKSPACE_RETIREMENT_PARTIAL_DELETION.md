# 真实源码部分删除后中断与显式恢复

2026-11-11，使用已部署产品356ba6c1。本批只扩展QA观察器、CI门禁与验收记录，没有新产品部署或正式Agent授权扩展。前序a90bc7d230b2f801207af3e76316d19c2a0df273准确CI42964077225已全13成功，最终收据另存 `deploy/artifacts/workspace-retirement-disabled-owner-takeover-record-20261111/exact-ci-final.json`；中途元数据查询EOF保留，没有重跑工作流。

## 实际验收

- 新临时管理员经真实Authentik/Vue/BFF完成新QA空间分配、强制硬配额、源码导入发布、Node测试2/2、.NET独立构建和独立测试2/2，启停后Node复验，共四运行成功，测试零失败/跳过。HDD Capture及独立Verify后，页面另行明确确认删除。
- 控制器只接纳声明的新QA首个execute，排除原空间及恢复尝试；核对当前数据库owner/空间/原计划/任务和完整宿主request，复核当前boot、固定单元路径、真实cgroup device/inode。只持有该task日志flock排除结果提交，不获取执行器正持有的存储锁，不写业务SQL、源码、配额、cancel或result，不冻结进程、不停Worker/Agent、不按名称扫杀。
- 新只读观察器通过已安装driver的namespace验证批准文件系统，核对prepared原engine request、绑定摘要、原plan摘要与UTF-8长度前缀manifest摘要，绑定journal/source allocation/data inode。观察倒序删除末项确已消失且存在精确持久remove意图后，才用已安装Execution控制停止该精确执行。
- **执行停止后再次完整盘点原716条manifest：4个文件已删除，502个文件仍在；共4条缺失、711条非根剩余条目。**每处缺失均有精确原index/entry的持久删除意图，原allocation已detach、data仍在。活跃时的采样只作触发；停止后的盘点才是部分删除证明。全部删除或完全未删均不能通过。剩余条目核对原identity/version；观察器不声称独立重算全部剩余内容，后续真实产品接续执行负责完整内容校验。
- 宿主真实Observe及Worker结算原task `33f9004f-de7d-4a2e-9dac-d673c15b57f4` 为Interrupted，未写取消或成功结果，空间AttentionRequired并保留预算。页面独立勾选接续删除、新幂等键创建新task `5f632080-ff61-44ef-8de7-07d472b4e6bc`，沿原plan/manifest/journal恢复Succeeded；原失败证明、原请求、派发/结算、首次确认及有效期保持。
- 首空间源码、detached source及claim缺席、八项内核限额/用量全零，应用预算仅完整成功后释放。页面再登记相同预算的-reuse空间，真实分配后经产品归档及独立删除确认退役；两HDD树、历史任务、审计和退役后8份报告可读。普通用户资源及恢复/历史拒绝，两个会话正常退出、两个临时账号删除，wrapper成功退出，活动任务/锁归零；故障cgroup在封存前再次只读确认已空。

完整wrapper首次通过，无修复重试。原20,164条记录、25条登记（5活动分配、20原退役）、58份报告及原源码/配额/HDD/journal保持；11核心及宿主进程身份保持，120,000,000,000/300,000,000,000字节硬限制、原用户source改动与user NOPASSWD保持。320暗色恢复历史实图已查看，无横向溢出，原中断及新恢复task清晰。

## 验证器与封存

6项新检查覆盖生产Unicode manifest摘要、请求/计划/内容摘要变化拒绝、缺失必须有精确意图、未删/全删不能冒充部分删除、剩余身份/version变化、符号链接祖先拒绝、进度观察失败不得触发kill。独立systemd验证实际使用生产remove_tree和私有/run树：最终版本停止后47个文件已删、753个仍在，原task日志逐字节保持、原task不能重启，沿同manifest接续删除成功且旧意图字节保持。它只证明执行控制及删除前缀，不代替上面的实机身份、配额、HDD和预算验收。原4项中断边界、9项身份、5项磁盘、14项CI合同与8项范围检查通过；CI新增真实systemd删除门禁。

首轮离线夹具意图文件权限为0644，被真实元数据检查正确拒绝；夹具显式改为0600后通过。后续收集器的0077 umask使未修正夹具也通过的事实及收集断言失败说明均保留，不当作生产失败或放宽权限门槛；未影响实机用户流程。

`deploy/artifacts/workspace-retirement-partial-deletion-20261111/`封存 **117文件 / 26,438,727字节**（不含manifest自身），manifest SHA256：
`3a8d4bb5f06e10cbf8c905d6694e0cd10e6a9579f2f36db1eef2c2baed890d8d`。逐文件复验通过，6个实现/CI源码有独立摘要与副本。

重点：`interruption-1-before-kill.json`、`interruption-1-execution-stopped.json`、`interruption-1-after-kill.json`、`interruption-1-settled.json`、`persistent-original-failure-preserved.json`、`accepted.json`、`batch-acceptance.json`、`resource-closure.json`、`candidate-source-sha256.json`和真实恢复历史截图。新QA资源为`2172eacc-5672-44b6-b14b-39a0d4d33ad3`及`0b210aaa-ef79-4e98-867f-d429b8dab4ec`，通过产品流程收尾，登记、失败和成功任务、报告及审计继续保留。

本批只签原attempt 0真实部分删除后中断→attempt 1显式接续及收尾，不签恢复尝试再次中断、配额清零后崩溃或原owner被删除等组合。新提交自身准确CI另待；完整59项、ST272、PET6及供应商/Live2D16继续。完整主任务0/59，17项移出不恢复、不计完成，不签master。
