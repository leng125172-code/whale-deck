# 真实取消退役执行后的Stopped恢复

2026-11-11，使用已部署且准确CI通过的产品356ba6c1。本批仅增加QA控制器、验证脚本与CI门禁，没有新产品部署，也没有扩大正式Agent权限。前序1b5cefd6准确CI42951997927在证据封存时仍运行中，后端、安装器及原生门禁已成功；查询传输失败另行保留，不重跑CI。

封存后接续：1b5cefd62518e235ad419ba3c6594f3841714404的准确CI42951997927已全13成功，最终绑定收据保存于 `deploy/artifacts/workspace-retirement-interruption-record-20261111/exact-ci-final.json`。本批Stopped验证器自身提交CI仍另待，封存目录不追加改写。

## 实际验收

- 新临时管理员和普通用户通过真实Authentik、Vue/BFF、Worker及Agent完成空间分配、强制硬配额、源码导入发布、Node测试2/2、.NET独立构建和独立测试2/2，启停后Node复验，共四运行成功、零测试失败/跳过。普通用户恢复和历史均拒绝，正常双端退出。
- HDD Capture/独立Verify后，页面另行明确确认删除。控制器仅接收本批声明的新QA首个execute；验证空间/owner/原计划/当前task/完整宿主请求，排除原空间、已有恢复历史、已完成或取消的任务。
- 精确核对当前boot、固定retirement单元路径、实际cgroup device/inode后，在任务flock内核对无result/cancel，并短暂冻结该存活执行。先保存故障前证据，冻结后释放任务flock，再让真实页面点击“取消退役”。观察器不kill执行，不写cancel/result/源码/配额或业务SQL，不停Worker/Agent。
- 真实取消POST得到202，绑定原task `ed6f6969-7f7c-4065-bc01-c82ef3d1f627`。Worker经签名Cancel调用固定helper，宿主真实停止该冻结执行，写原任务取消墓碑且无成功结果；原attempt 0结算Stopped，空间AttentionRequired、cancelRequested=true，预算继续保留。不是执行前取消，也不是以直接强杀伪造Stopped。
- 页面重新独立勾选继续删除，使用新幂等键创建attempt 1 `e94fe80f-8897-4d4c-b941-c6110fa77933`。当前取消标志由产品恢复流程清除，新任务成功接续原engine请求。原请求/证明/计划、派发/结算/首次确认及有效期由API前后列表和独立SQL比较保持；320暗色实图已查看，原停止和新成功同时可见，无横向溢出。
- 两新空间均通过产品归档、独立删除确认和退役收尾。内核配额八项全零，源/claim缺席，预算释放后能登记并分配相同预算，再走同一流程退役；HDD树和退役后8份报告保持。
- 完整wrapper首次通过，无修复重试。原19,342条记录、15条登记（5活动分配、10原退役）、38份报告、原源码/配额及HDD/journal保持；11核心服务及宿主进程身份保持，120,000,000,000/300,000,000,000字节硬限制、原用户source改动和user NOPASSWD保持。两临时账号删除、冻结窗口解除、控制器正常退出、wrapper成功退出、活动任务和锁归零。

## 有界控制及证据

冻结窗口最多20秒，EOF、UI失败、异常、SIGALRM和SIGTERM均进入finally，使用原pinned dirfd解冻；路径同名替换不获得写权限。独立真实systemd测试验证冻结存活执行由原生Cancel停止、子进程死亡、只有取消位从0变1且其他日志字节保持、原task不能重启；另实际触发闹钟并验证仍存活的测试组解冻。惰性工作夹具只证明控制器，不代替上述产品验收。

6项离线测试覆盖cgroup inode变化/已有冻结拒绝、证据失败不冻结、写结果不确定也解冻、EOF/异常收尾、畸形events及固定写值。原4项中断边界、9项身份、5项磁盘、14项CI合同及8项范围检查通过。新实际systemd门禁和Node/Python检查已加入CI，候选自身准确CI另待。

`deploy/artifacts/workspace-retirement-stop-20261111/`封存 **98文件 / 25,711,769字节**（不含manifest自身），manifest SHA256：
`886fa073a1463f3c6a11699802adeda951307998ac1fb2c706ba61122fce5110`。逐文件复验通过，9个实现/CI源码有摘要及副本。

重点：`stop-1-binding.json`、`stop-1-before-freeze.json`、`stop-1-frozen.json`、`stop-1-stopped.json`、`stop-1-thawed.json`、`stop-1-settled.json`、`persistent-original-failure-preserved.json`、`accepted.json`、`batch-acceptance.json`和`resource-closure.json`。新空间为`e6d1de2f-c955-4578-809c-d0d3ef955717`及`c17e0fec-7bf9-4f50-bbdf-98f8e079c456`；失败、成功、HDD、报告及审计均保留。

本轮确认执行已存活后由真实UI取消；没有独立证明取消时源码已detach或删除，也不签观察器SIGKILL后的解冻。删除途中故障、另一管理员恢复、其余59项/ST272/PET6/供应商Live2D16仍待。完整主任务0/59，17项移出不恢复、不计完成；不为纯验证器重复部署产品，不签master。
