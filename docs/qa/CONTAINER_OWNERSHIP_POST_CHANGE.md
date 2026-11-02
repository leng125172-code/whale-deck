# 容器所有者：实际变更后、完成回执前的中断恢复

2026-11-02。产品保持d1fcca8/Agent1.48；本批仅新增QA观察/验收/CI与记录，没有修改或重新部署产品，没有自动回滚。两例真实切点及独立复核通过，不据此签完整文件模块、所有故障组合或递归/ACL/删除完成。59主任务/17移出项保持。

## 真实切点

正常Authentik表单登录，在实际Vue文件页预检并明确确认2200:3200。先停当前Worker使确认落Pending，再恢复同CID/镜像Worker。QA只通过原执行/完整计划、精确容器根FD、原操作锁FD和固定transport argv绑定pidfd；不向任意进程发送信号，不使用产品测试钩子或模拟回执。

新freeze_after_owner只计Root为Applying、ownerConfirmed=null、actualIdentity为空、version为空且物理HDD历史已验证的窗口。独立stat证实同一inode的UID/GID已经成为目标，ctime已改变，device/inode/mode/nlink/size/mtime保持；原父目录匹配，属性digest重算一致，无符号链接/硬链接/ACL/security属性或外来变化。SIGSTOP后再次重读Root与实际inode，仍满足所有证明才计入。未变更/已Completed或错过窗口均不计；窗口很短，没有安装延时或checkpoint钩子。

两例均第一次取得实际Applying切点；停住后读取完整2097159字节并核对原SHA，证明物理所有者确已变化而内容未变。不是旧Preparing/Prepared切点，也不是SQL成功后的回执读取。

## 实际故障及结果

验收源码冻结08db4dc，原管理员每例页面登记/确认各1，内容PUT均0。

| 场景 | 实际操作与结果 | helper退出观察 |
| --- | --- | --- |
| 变更后页面取消 | 320暗色页面先继续等待、再一次明确取消；产品停止原helper，原任务对账Succeeded、Root Completed，取消请求仍保留 | 5.321秒 |
| 变更后Worker强杀 | 精确Worker容器SIGKILL并恢复同CID/镜像；原自然租约到期后由原任务Inspect恢复，Succeeded、Root Completed | 0.113秒 |

pidfd退出观察从故障动作前启动，绝对15秒窗口，验证器不向helper发送SIGKILL。Worker例保存原lease及三次到期前Applying/保护保持的实际样本；没有更改时钟/SQL/租约，最终完成时间不早于原租约到期。调用者在Root完成标记写出前实际中断，不能把RPC断开推断为“未修改”。

完成事实优先于晚到取消：源已实际fchown，产品用原计划/同inode/目标UID/GID/属性及HDD重建Completed。两例都不是Canceled/Aborted或新的执行，不自动恢复原属主。320暗色取消例、1440明色Worker例的可见终态均为“所有者修改已完成”，实际API终态ownerConfirmed/historyVerified为true，confirmation为空，双端退出通过；截图已目视复核。

## 不重复变更与持久化证明

- 两例均独立核对SQL双Job/原Subject/唯一结果审计/Outbox及原Root/HDD元数据，结果为Succeeded，发布版本由实际九字段身份重算一致。变更后完整指纹与暂停切点完全相同；原UID/GID与目标、内核执行FSUID/FSGID=1000:1000分开记录。
- 对同一原operationId/owner/plan通过固定受限helper发送迟到Apply（正确行帧），返回Completed、replayed=true及原版本/历史digest。Root原件完整SHA与实际九字段指纹前后一致，包含ctime，未再次fchown。
- 原文件完整内容、inode/mode/nlink/size/mtime保持，UID/GID预期成为2200:3200、ctime预期变化；不声称原九字段全部不变。HDD留存原所有者/属性元数据，不包含文件内容，不冒称内容灾备或本批NVMe/跨盘验证。
- 只在全量证明后清理准确合成源/CID和临时身份，永久SQL/Root/HDD保留。两个父命令和两个独立只读进程均退出0，重新核对原件/摘要/计划/审计/原任务及准确资源不存在；最终活动/屏障/租约0、11核心healthy。独立进程不声称源删除后重新读取物理字节。
- Worker CID/镜像保持，其余10核心ID/镜像/状态/StartedAt不变；宿主/UDS/helper/HDD及动态入口检查通过。原运行版本不变，没有扩大Agent权限或撤销开发user NOPASSWD:ALL。

## 检查与证据

新增8项Linux-root切点边界测试零跳过：实际chown前后、原inode/所有者/mode/size、替换/硬链接/符号链接、外来路径、属性漂移、未验证历史和外来scope；原16项观察器回归通过。Python -W error及JS语法通过，新检查已接CI。只改QA与记录，不冒称本批重跑后端全量或取得GitHub CI全绿。

- 变更后取消：deploy/artifacts/container-ownership-faults-a9b2b0e6249c4ad0872ab3716370fedc/。
- 变更后Worker强杀：container-ownership-faults-52f4a6db2ba54d56a14cf2350e464893/。
- 各含冻结源码及SHA、baseline、cut-root/bound-helper（实际changedIdentity）、原自然租约样本（Worker例）、final SQL/Root/HDD、after与late-apply、browser/verified、2张PNG及SHA清单、independent-final。
- 主日志/tmp/wd-owner-post-{cancel,worker}-first.log、对应independent.log、wd-owner-post-result-scope.log和wd-owner-post-host-final.log。私有证据均不入Git。

继续目录/实例重建/跨盘、变更后退出/撤权/Agent等其余组合；仍需通用传输/回执故障与坏元数据矩阵，不能以这两例代替全部恢复。后续实现递归权限、ACL、可恢复删除与长期清理，并推进其余59项/UI/C#酒馆/集中供应商/AI Live2D，master尚未正式交付。

收尾检查：8项范围测试通过，5份修改MD的504个本地链接无缺失，git diff --check通过。
