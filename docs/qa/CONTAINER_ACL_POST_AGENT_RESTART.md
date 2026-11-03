# 容器 ACL：实际写入后 Agent 重启、维护入口与原任务摘要实机验收

后续[写后目标消失Unknown/明确处置摘要与小屏修复](CONTAINER_ACL_POST_UNKNOWN.md)已在447070e实机签收，当前Agent3947643/维护3947551；以下本页612f77c重启事实、当时PID和剩余范围原文保留。

2026-11-03。产品保持`612f77c9293f1f3a78b3cb90b84d2a80b0e4ae76`、Agent协议1.51；无重新部署或生产迁移。真实门禁冻结`47a17696c704b3e3420ac7f07fcdecf5be7d9f57`，schema6/27份验收依赖与11安装模块逐字节核对。**本批一个新的独占资源场景、原执行/预检摘要真实页面、正常双端退出、SQL/Root/HDD及独立只读复核首轮通过。** 不重做此前已完成的ACL操作，也不按单场景提升59主项整项完成率。

## 写后切点与实际重启

新容器非root1000:1000、无网络、只读根、唯一自有HDD bind；两个文件各2097159字节，原属主2100:3100、固定mtime及user.whaledeck-acl，原original.bin不属于ACL变更目标。正常可见Authentik登录→真实Vue生成原版本预检；仅正常停止原Worker、页面确认唯一原执行Job、启动同CID/镜像Worker，以便捕获真实固定helper。

观察器绑定固定ACL传输argv/root身份、pidfd、准确容器root FD与原0600操作锁FD并重新证明。实际已读到同inode的目标access ACL：owner6、named user2201权限6、group_obj0、mask4、other0；mode仍0640但NoChange=false，ctime实际增加。Root仍Applying，aclConfirmed/actualIdentity为null、version为空，HDD原ACL历史已留存；不是仅凭任务Running或截图声称已写入。没有安装钩子或改SQL/Root/缓存/时钟。

原上下文真实进行中页面可见后，重新证明除本案外Job/终端/文件操作/锁/屏障/租约均0，准确原helper是唯一允许的活动临时单元，才实际执行systemctl restart whaledeck-agent.service。产品在重启开始后**0.013秒**结束原绑定helper，QA没有杀helper。Agent PID**3747527→3835700**，前后实际exe来源均612f77c；维护PID**3747520不变**。实际重启约30.661秒，**153次**维护HTTP均200 healthy/Ready、no-store、输出有界，覆盖重启前、中、后，没有切换维护进程或伪造状态。

本批只有Agent重启；Worker仅用于准确预检的正常启停，没有强杀。也不把此案计为Worker自然租约等待验收。

## 完成事实与不重复写入

原意图/执行Job最终Succeeded/CONTAINER_ACL_COMPLETED，Root Completed、AclConfirmed=true、BindingCurrent=true，原操作ID、owner、plan、第一SQL回执/MAC与实际新inode版本/HDD摘要一致。重启后的恢复按已有物理事实保存完成结果，未把已经发生的ACL修改报成未修改或恢复旧ACL。

原内容完整字节、inode、UID/GID、mtime、default ACL和普通xattr保持；access ACL确实已变、新ctime确实增加。切点之后与最终物理清单完全相同。追加固定helper同operationId/owner/plan的迟到Apply返回Completed/replayed=true，原Root字节、版本、原HDD摘要、完整写后现场均不再变化；它不是新业务登记，也不将这次固定helper探针写成额外签名RPC。

实际1意图/2唯一Job/4条原主体审计：plan-accepted1、plan-result Ready1、confirmed1、result Succeeded1，无取消审计或cancel_requested。1份永久ContainerAclMetadata保存原ACL，原件未删除；它是元数据历史，本批源和HDD同设备，不计内容备份、NVMe覆盖或异盘灾备。

## 原任务安全摘要与页面

同一原登录仍有效，真实Vue核对原ACL终态和原API Completed回执。随后只读读取本案planning/execution两个原Job，核对精确15字段摘要与各自原SQL/Job ID/阶段，排除路径、实例、主体/登录绑定、原ACL条目及各种证明/摘要材料。原预检Job保留当时Ready/null确认，执行Job显示Succeeded/已确认/Completed及历史验证，不能用计划成功替代执行成功。

正常导航任务中心，按本案原ID定位两个“详情”，在320×600暗色真实页面核对任务ID、状态标签、原意图、阶段、NoChange=false、目标0640、确认三态、历史回执/验证，且无取消/通用重试控件、无横向溢出。任务中心管理写0，两次context.request均为明确GET，不用测试接口或Cookie注入。原进行中、终态和两个任务详情共**4张真实PNG已目视复核**；截图不声称一次展示全部滚动字段。

原上下文通过用户菜单正常BFF/Authentik双端退出，provider导航及401/401或403、最终/login由门禁与独立观察器核对。退出、源文件检查和独立读证据不是拼接其他场景，也未删除旧账号来冒充正常退出。

## 准确清理、独立复核与测试边界

父进程在清理前完整读取原内容、九字段、ACL/xattr并确认写后现场保持，才准确删除自有源/空目录和容器；original.bin由原fixture独立检查。Root/HDD永久保留。独立新进程按冻结来源重读原SQL/Root/attest HDD、双FD切点、实际重启/153维护采样、两原摘要及PNG、唯一结果审计、迟到重放与准确清理；临时身份/源/容器均不存在，11核心实际healthy。独立进程不声称清理后重新读取已删除源字节。

API/Gateway和8数据库平台的CID/镜像/StartedAt保持；Worker同CID/镜像仅发生本案准备所需启停。维护入口不变、Agent保持新PID和612f77c来源，最后无活动任务/终端/锁/屏障/租约。user NOPASSWD、密码、产品最小授权及内部隔离不改，无dev自动回滚。

冻结前首轮离线测试仍把schema6列作无效版本，导致一个断言失败；且非root运行的15个原生观察器边界跳过，未记作通过。修正schema门槛后，以root实际执行**40门禁＋8范围=48项全部通过、无跳过**；Python -W error/Node语法、diff检查通过。新旧selector改为显式分组并验证post-revocation仍只有原两案，新post-Agent为单案；schema1–5不能冒充新schema6事实。没有放宽产品或验收权限。本批不声明全量后端/前端或整条远端CI重新通过。

本批原件位于root:root/0700目录`deploy/artifacts/container-acl-faults-a74edfe7a4b4420481800552325423be/`，JSON/PNG/日志0600，33份原件SHA目录及4PNG；主日志`/tmp/wd-acl-post-agent-real.log`、独立日志`/tmp/wd-acl-post-agent-independent.log`保留，四张查看副本在忽略目录`deploy/artifacts/ui-candidates/acl-job-summary/post-agent-47a1769/`。实际新场景和独立核验均首轮通过，没有失败后重复写入。前批TLS推送暂失败后，本轮已正常推送至47a1769，旧MD/修复/验收记录均已同步。

## 尚未签收

本批未验证ACL写后或实例变化Unknown、其他共享写入、剩余路径/磁盘组合；Unknown无回执/明确处置/Superseded/Failed等任务摘要分支仍须各自真实页面证据。ACL递归/历史恢复、可恢复删除/长期清理、P0-05旧来源问题及其他59项/UI/C#酒馆/供应商/AI Live2D继续。[上批部署和常规摘要](CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)原始事实保留。

文档检查：8份变动MD的750条相对链接无缺失，目标第1节与6d9597c逐字一致，上批部署/常规摘要QA原文逐字保留；另修正目标阶段概览残留的当前633a2ca表述为已验证612f77c。项目图谱事件`whale-deck.container-acl.47a1769.post-agent-restart`已核对projectId/key/eventId回执并移入synced，阶段仍active。
