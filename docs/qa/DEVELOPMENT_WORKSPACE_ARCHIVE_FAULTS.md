# 开发空间归档：真实丢回执、Worker 重启与派发前撤权

2026-11-11，在已标准部署且准确 CI 13/13 成功的 b46f1bbc 上完成本批三个真实场景。新增的是显式启用的安装态验收入口和范围测试，产品组件未更换。不是完整故障矩阵、源码退役或 P3-18 整项完成，完整主任务仍 0/59。

前批验收提交 141a8bd0 的 [CI 42856063610](https://github.com/leng125172-code/whale-deck/actions/runs/42856063610) 也已准确取得全部13个job成功。本批新增脚本、CI接线与记录须另外取得自身CI，不沿用前批结论。

## 实现与实际执行

入口为 deploy/validation/validate_workspace_archive_faults_installed.py/.mjs，仅接受固定 QA 空间和准确部署 SHA；源码/配额/预算及全部历史保留。Python 只读 SQL 协调当前状态，临时凭据只经内存/隐藏 stdin 进入浏览器，不进参数、证据或日志。真实 UI、BFF、Authentik、PostgreSQL、Worker、Agent 和 HDD 均未以合成响应替代。

1. **服务端接受后丢 Begin 回执。** 浏览器转发原 UI 请求，核实真实服务端 202 后主动断开该回执，不伪造成功内容。产品自动 current 查询恢复同一归档 ID，未再次点击 Begin。原待确认计划保持恢复围栏，Worker 在无其他活动工作时按原容器 ID 停止/启动。数据库完整意图与原 task/request/proof 前后相同，页面重载仍须人工确认；经 UI 取消，未 Capture。
2. **派发前移除临时管理员组。** 在 Worker 暂停时由该身份正常 UI 提交计划；独立确认原 plan step 尚无 dispatch/proof 后，只对刚生成且用户名/UUID/PK/原组均吻合的账号执行一次 groups=[]。恢复同一 Worker 后，当前身份检查把原意图置为 AuthorizationRequired，原 task/request 未变化，也没有宿主派发或 HDD 副本。旧身份 GET/confirm/authorize/cancel 均真实返回 403，页面清空归档详情，6.5 秒观察期无自动归档读取。另一位真实管理员经 UI 重新授权，同 task 完成 Plan 后仍须显式勾选确认，随后真实 Capture/独立 Verify 和重载回执通过。
3. **派发前禁用临时账号。** 同样在未派发的原 plan 上只写 is_active=false，保留原管理员组；恢复 Worker 后原任务因当前身份失效暂停。旧身份四类 API 均为 403，UI 清空详情并停止轮询。另一管理员重授权原 task/request，Plan 成功后保持 AwaitingConfirmation；经 UI 取消，没有 Capture 或 HDD 副本。

两类身份变更均先唯一查找、二次核对精确原状态，只发一次 PATCH，丢回执后也不重试。8 项离线变更范围测试覆盖错误用户名/UUID/PK/组/激活状态、分页结果、查找后变化、失败回执和非法动作；全部通过。新增入口的 Python/Node 语法检查及原 CI 分片合同 9 项通过，完整 CI 中也接入语法和范围检查。

## 独立结果及收尾

- 3 个新持久归档意图、5 个唯一阶段任务与 5 个新 job，状态为 Canceled/Succeeded/Canceled；每个 task 的 canonical request 摘要、dispatch、proof、settled 与审计已核对。这里只证明同一 task/请求恢复和不新建阶段，未额外测量 Agent Start RPC 的物理调用次数。
- 仅移组后的管理员接管场景产生新 HDD 副本，3,025 字节实际文件的 SHA-256/大小/inode、实际 receipt、数据库 Capture 与 Verify 一致；副本、任务、审计和 Root journal 均保留。其他两个取消意图没有 Capture。
- 空间 Active epoch17→Stopped18→Active19，五空间除目标预期生命周期字段外的登记/资源字段保持；源码树/inode/硬配额/预算保持，18 原报告保留。18,282 条原不可变记录逐摘要保持，原 HDD 6 条目及 journal 96 条目的 inode/权限/文件内容保持，前两批 82 个封存证据文件再次通过逐字节复核。
- 三次 Worker 停启仅作用于同一容器 ID/镜像，每次先检查没有非本批活动 job、终端、路径锁或发布围栏；最终仍 healthy。其他十个核心容器及两个宿主进程不变，11 核心 healthy。120 GB/300 GB 镜像、完整预留、实际文件系统和 NVMe prjquota 保持。
- 恢复管理员通过正常菜单完成双端退出，三个临时账号经身份源删除并确认 404。活动 job/路径锁/归档 lease/各阶段 cgroup 进程为 0。浏览器置于仅本批独占的临时 systemd cgroup，正常和异常清理均能精确收口；成功执行后该 cgroup 无进程。未扩大 Agent sudo、修改 user NOPASSWD 或覆盖原用户 source.py 未提交改动；两个 Git index 仍为 user:user 0664。

私有证据目录：deploy/artifacts/development-archive-faults-141a8bd0-20261111/。主要为 accepted.json、independent-counts.json、independent-archive-proofs.json、worker-restart-proof.json、两类 predispatch/same-task proof、before/after.json、身份和 cgroup 清理、源码原件及实际 320px 暗色截图。该实图已查看，可读且无水平溢出。

该目录的 53 个文件已封存并逐字节重验，SHA256-MANIFEST.json 摘要为 d2c7e0e0e488736c563b63bf0ccafebdf55eafd5dbb3e5212f8b19f3d721ed49；后续提交与 CI 回执另存，不向封存目录追加。

## 验收边界与接续

本批是人工注入的真实网络丢回执、待确认期 Worker 重启及派发前身份撤销，不是自然网络事故，也不是在途 Capture/Verify 被中断、租约丢失或 Worker 在 dispatch 提交窗口死亡的证明。候选/真实隔离数据库及签名实盘 RPC 的既有故障证据继续保留，不能混记为本批生产实机时序。

接续独立源码退役计划/确认/精确执行、预算释放及持久墓碑；已校验 HDD 副本不自动构成源删除授权，原五空间不用于直接删源试验。新退役实机验收应使用新建且明确归属的资源，通过产品流程收尾。新增验收提交仍需准确 CI，图谱更新仅本地 pending。其余59项及 ST272/PET6/供应商 Live2D16 门槛继续，17项移出不恢复，不签 master。
