# 源码退役执行内核与真实配额收尾候选

2026-11-11，本批接续 `dae0cc94` 和原未提交候选，实现内部宿主源码退役原语。仅在新建的专属隔离空间执行删除；原五个产品空间继续保留。本批没有生产 CLI、安装清单、签名 RPC、当前身份授权、Worker/API/Vue 退役入口或应用预算释放，不能计 P3-18 或完整主任务完成。

## 实现与恢复约束

`whaledeck-development-retirement-execution.py` 只接受严格绑定原存储、Stopped 偶数 epoch、已归档/独立验证回执、退役计划摘要及新退役 ID 的内部请求。持有原 NVMe 根排他锁，独立重读 HDD 完整归档并核对源码清单、allocation inode、原配额 claim 和环境预算。

不可逆操作之前持久创建 `.retirement-<workspace>` 围栏和私密 prepared 日志。旧分配、受证实载荷、启停及依赖它们的源文件/Runner 入口在围栏存在时拒绝，包括空目录、异常对象和准备中断。原 allocation 禁止覆盖地移入同文件系统的 journal/source；每个文件/目录均核对原 inode/类型/权限/所有者及内容，先写持久删除意图，再删除并同步目录。未知项、替换/符号链接、无意图缺席和晚到源码均不生成完成回执。围栏、原生命周期及审计日志永久保留。

源文件和全部目录 FD 关闭后，只有真实项目配额的字节与 inode 用量均为零、限额仍为准确原值或已在同一受保护重试中清零，才允许清除限额；随后 syncfs 并回读八项状态全零，持久写入释放检查点，再核对原 claim 身份并删除。释放检查点之后重试不会重清已复用 ID 的 claim 或内核配额。完成回执始终标记 `releasesApplicationBudget=false`，预算必须由后续授权业务事务闭合。

## 失败复现与修复

新增两个确定性故障回归，修复前实际取得一项失败和一项错误：日志原子重命名已可见、目录 fsync 失败后，再次读取已有日志未同步父目录，可能继续删除源码或 claim，继而在再次掉电后丢失检查点。本批统一使用 durable_read 同步文件及父目录，重试再次同步失败时保留源码/claim 并中止；两条回归修复后通过。修复前源码及原始测试输出单独保留，不追认旧候选为最终版。

## 实际执行与证据边界

- 修复后开发空间联合测试实际运行 **199 项，全部通过、0 跳过**；scope **8 项**、CI 合同 **14 项**和安装器无副作用预览 **5 项**通过。独立只读复核未发现当前隔离路径的具体阻断问题。
- 实际运行 `sudo python3 -B -W error::ResourceWarning deploy/validation/validate_development_archives_native.py --retirement-execution`，仅格式化/挂载 O_EXCL 创建且绑定 inode 的 NVMe/HDD 各 2 GiB 全额预留新镜像；生产 300 GB HDD 只读核对。原归档、固定 helper/systemd/seccomp/Landlock、复制中取消、EDQUOT/ENOSPC、两次归档强杀接续和重挂验证保持通过。
- **7 个新隔离 allocation** 分别验证直接完成、prepared 之前、原 allocation 重命名之后、实际文件 unlink 之后、打开的已删除文件、内核配额清零之后、释放检查点发布之后。其中 prepared 前、重命名后、unlink 后、配额清零后及释放检查点后五个切点实际执行 SIGSTOP/SIGKILL，父进程再用原请求接续；全部通过。
- 持有真实文件 FD 时，源码路径已移除但内核仍报告非零字节/inode，用 `QUOTA_STILL_REFERENCED` 拒绝；完成回执和配额释放检查点缺席，原 claim 保持。关闭 FD 后原请求重试成功，各项目内核限额/用量八项全零。
- 每次完成后独立验证 HDD 字节/inode/目录快照保持、归档仍完整可读、同请求幂等、旧 allocation/计划/resume 永久拒绝。原隔离证明空间及其配额保持。精确卸载并移除本批镜像、挂载和私密运行目录；没有操作原产品空间。

首次原生验证保留于 `deploy/artifacts/development-archives-native-a1a8af9a31f94a7e98adc14e9cdcb876/`；最终修复版于 `deploy/artifacts/development-archives-native-6a4f801d0c544b3f8ec0765ee1e67624/`，各含原源码、真实进程/配额/归档记录、独立清理及 SHA256 清单。汇总、故障前原件、最终测试输出和产品前后核对于 `deploy/artifacts/development-retirement-execution-20261111/`。不将故障注入的 fsync 错误等同于实际断电或物理磁盘错误。

首次原生清单132文件，摘要 `a39d1ced5e27072577acf86ecb5a9a535fbc7fb208029442e56806fde265409c`；最终原生清单133文件，摘要 `43fb171059cd30a43d657edc1adfb6fd92d5fe316e39d86173d073b2688c622a`。独立对照上批封存最终快照及本批前后快照，5空间/18报告/18,384条原记录、原HDD/journal、11核心容器和两宿主进程、120GB/300GB完整预留与强制限制、私密配置及user NOPASSWD保持，任务/路径锁均为零。原用户source helper修改摘要及两个user:user/0664 Git index保持。

## 发布与下一步

本批仍是内部候选，最终准确提交的 CI、标准 dev 部署及安装态签名执行尚待；部署基线继续为已验收的 `da519f27`，不自动回滚。后续接固定 helper 的最小权限/长任务控制及签名协议，再接当前身份授权、原持久退役意图、完成回执与预算墓碑、Worker/API/Vue，并使用新专属验收空间完成真实用户退役闭环。

全部59项与 ST272/PET6/供应商及Live2D16继续；本批不增加完整主任务完成计数，17项移出不恢复。项目图谱仅保存本地 pending，未声称网站已同步。
