# Unknown UI 发布中断与只读恢复核验

2026-11-05，承接[Unknown 原件与 UI 候选](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_RECORDS.md)。准确候选 `34153d2c454942b2665902772b7d5e48226cf7db` 的 CI `41965780773` 已 completed/success，12 个 job 全部成功。这关闭了等待 CI 终态的前置条件，但本次标准覆盖被主机重启打断，**发布未成功，新 UI 与真实 Unknown 处置整链均未签收**。

## 单次发布与中断事实

运行检出由 `ac90911` 正常快进至 `34153d2`，随后仅一次调用固定维护 helper 的 update，绑定 unit `whaledeck-dev-unknown-3a2625c4`、Invocation `b096166cacf0430bbb2af6414653fe25`。没有重置分支、手动替换应用镜像、跳过构建/迁移或自动回滚。两宿主安装已经执行；构建阶段 apt 下载缓慢、NuGet 包下载出现 EOF，尚无成功完成或应用替换回执。

本机于本地 12:47:42 启动，boot ID 已由原发布所属启动改变。原 transient unit 已不存在，systemctl 的默认 Result=success/状态码 0 不能作为原发布成功证据，计划的后置核验没有成功回执。本任务没有发起主机重启，不将这次意外事件补签为计划内断电/重启验收。原发布 journal 按准确 Invocation 保存，没有重新运行该发布或旧任务。

维护公开状态为 failed。实际冷启动恢复回执为 interrupted、controlsRestored=true、applicationReplayed=false、automaticRollback=false；活动控制器 checkpoint 已结束，三份已安装维护控制器与原 checkpoint 字节相同。账本仍以 `ac90911` 为 currentRevision、保留 `34153d2` 的 pending，未将目标提升为成功版本。这符合控制器恢复机制；控制器恢复不等于应用发布成功。

## 重启后的实际状态与数据保持

只读 build-info 与真实宿主进程 exe/DLL 映射核对显示：Agent、MaintenanceHost 为 `34153d2`；API、Worker、Gateway 仍为 `ac90911`。这是混合版本，不声称六组件一致。两个宿主服务 active，11 个核心容器 healthy，全部容器 ID/镜像与发布前相同，StartedAt 均因重启改变，不能沿用“核心进程未重启”的旧断言。`src` 与 `deploy/host` 在两个发布版本间无差异，当前候选的产品改动在 Vue。

发布前、首次启动后与后续只读复核对比结果：

| 对象 | 结果 |
| --- | --- |
| 来源 Root 1910、恢复 Root 252、恢复 HDD 166、目录 Root 94 | 完整清单、字节摘要和原身份字段保持，三次口径一致 |
| 恢复业务 | 23 意图、18 执行、活动 0，与发布前一致 |
| 普通递归 ACL 业务 | 62 意图、56 执行、118 Job，与发布前一致 |
| 2292 个保护文件 | 路径集合相同；仅发布账本内容变化，其他内容摘要保持 |
| 安装允许的元数据变化 | 仅账本、Agent sudoers/resources/env 及 capability key；权限/属主等约束保持，key 仅 ctime 改变 |
| 权限与配置 | user NOPASSWD:ALL 文件保持；Agent 权限未扩大，私有配置/密钥未输出或提交 |

这些是指定记录及业务计数的保持证明，不扩称所有数据库业务、迁移重新执行或全量 HDD 内容验收。维护观察原始 JSONL 有 540 条可解析样本，均 HTTP 200，另有 1 行损坏；原字节保留且未过滤重写。存在重启及采样缺口，不能证明维护入口全程连续或瞬时零中断。未完成的零字节观察摘要也保留，不作为成功证据。

## 回归补充、失败与证据封存

新增维护 shell 回归：在临时 Git/状态目录中让替身 builder 到达构建检查点，强杀本测试创建的真实进程组，移除自有临时 status/lock 模拟易失状态丢失，连续两次 recover 均保持账本/应用版本/事件不变，不执行 migrate/up。随后显式 update 复用原 pending operation，在独立新 checkpoint 完成；原 interrupted 回执字节保持，新增 completed 回执，成功后才提升账本。Docker、安装和实际运行版本边界为明确夹具，无真实 Docker 构建、工作站重启或第二次生产发布。

`python3 -m unittest discover -s tests -p 'test_platform_*.py'` 补测后 **58 项通过、零跳过**。先前 root 单独恢复套件 16 项含 1 项普通调用者测试跳过，普通用户完整套件已覆盖；不将两组叠加为额外覆盖。

只读核验第一版将实时 core tuple 与 JSON list 直接比较而停止，没有数据变更；保留原脚本，第二版先归一化再完整通过。早先恢复读取输出过量没有作为成功回执；本批只保存限定字段。最近公开 NuGet 只读下载仍 TLS EOF（curl 35、HTTP 000、0 字节），诊断来自持久工具回执，未伪造原始 stderr 文件；未关闭 TLS 校验、改依赖版本或调整网络来掩盖失败。

私有目录 `restore-unknown-release-3a2625c4e4e941f4a5cf5e971e49986e` 的中断证据封存 **30 文件**，清单 `INTERRUPTION_EVIDENCE_SHA256.json` 摘要 `f757c7715293779cb1a63331c70bf031fdd4b130c5e0dbda9fcf39aa32584196`，逐项大小/摘要复核通过。包含原始 journal/观察字节、CI 成功回执、发布前/启动后清单、控制器 checkpoint/恢复回执、实际组件与进程映射、核验脚本及失败限制、测试完成工具回执。首份测试回执是 JSON 字符串包装，保留并另存 v2 对象，不覆盖原件。旧候选和真实 Partial/退出封存不改写。

## 继续执行的边界

当前没有后台发布进程继续执行。后续先恢复依赖下载，再以新的证据目录、发布 unit 和即时基线显式重新调用标准 update；不能复用本次成功标记、旧 StartedAt 或旧固定指针。新的发布需要六组件/HTTP/宿主映射、迁移与保护记录、维护采样重新核对，再执行全新自有资源上的真实 Authentik→Vue→API/Worker→Agent→实际文件/HDD Unknown 与另一管理员处置门禁。

59 项及阶段完整签收计数不变；整体 25%–35% 工作量粗估、酒馆/供应商/看板娘功能 0% 保持。其余故障矩阵、可恢复删除及其余目标继续保留，当前中断不是完成项。
