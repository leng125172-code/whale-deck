# 开发空间归档固定 helper 与隔离实盘候选

后续2026-11-10：01206806准确CI已12/12成功，并已标准发布dev、独立通过六组件来源/健康/模块字节及原空间与记录保持核验；本页以下仍保留原候选时态。[签名RPC隔离实盘及本次安装记录](DEVELOPMENT_ARCHIVE_RPC.md)另列，新RPC与用户归档退役闭环尚未部署验收。

2026-11-10，接续 [长任务执行控制](DEVELOPMENT_ARCHIVE_TASK_CONTROL.md)。前提交 c63fa91126922e9366c3de1feb481c792a346959 的准确 [CI 42809843872](https://github.com/leng125172-code/whale-deck/actions/runs/42809843872) 已完整 12/12 成功。本批固定宿主 helper、安装清单和验证代码尚未标准安装或接入签名 Agent RPC、持久业务、Worker/BFF/Vue；实机仍为 42ea297d。没有开放空间删除或配额释放，不增加59项主任务及全部子门槛完成数。

## 实现

新增固定 development-archive-start/observe/cancel/run 分支。Start 先在原任务目录持久记录完整请求绑定的 dispatch fence，再调用 PID 1 以固定 taskId 启动独立单元；请求通过 LoadCredential 复制，不把载荷、外部路径、命令或凭据正文放进参数。模糊启动回执保留原 fence，不重复派发。长单元不随发起 RPC/Agent 消失而停止，重复 Start 只观察原任务；尚无执行证明的派发不确定状态也不代表成功。

控制单元 capability 集合为空，长单元仅有 CAP_SYS_ADMIN 和 CAP_DAC_READ_SEARCH，用于实际只读配额及私密源码读取。没有 CAP_SYS_RESOURCE、Runner OCI/Delegate 或通用命令入口。运输入口在加载模块/读请求前检查实际 CapEff/CapPrm/CapBnd 精确集合、NoNewPrivs=1 和 Seccomp=2，不符合即拒绝。

Landlock ABI≥3 限制新增写入：Start 仅任务日志，工作单元仅任务日志和固定 HDD，Cancel 仅日志和精确原 cgroup。NVMe 保持内核配额所需的 writable mount 引用，源码写入仍被 Landlock 拒绝。systemd 独立拒绝 chmod/chown/时间及 xattr 写入；内部 seccomp 仅增加只读 FS_IOC_GETFLAGS，原通用配额 guard 默认规则不变，配额 setter、SETFLAGS、执行和进程/FD捕获继续拒绝。

取消选择执行证明、安装写入限制和持久取消标记处于同一任务 EX 锁中，堵住首次读取无 proof 后 worker 刚入场的窗口；观察/停止仍不获取两存储根锁。安装器为载荷外日志创建/验证 root 0700 目录，已存在的不安全目录拒绝；新增模块同时进入安装及字节一致性核验清单。当前这些安装改动尚未在正式主机执行。

## 验证

- 归档/操作/运输专项 **53/53**，存储 **23/23**（含真实隔离挂载命名空间）、启停 **9/9**、配额 **8/8**、宿主清单 **12/12** 通过，全部零跳过；安装预览 **5** 检查及相关 shell 语法通过。专项包括真实内核 Landlock 对截断、删除、移出、硬链接、越界新建和符号链接目标写入的拒绝，以及实际 seccomp GETFLAGS 允许/SETFLAGS 和 exec 拒绝。运输调度、HDD 身份和部分 cgroup 单测中的 double 均明确标注。
- 前轮独立执行控制的真实 systemd/cgroup/pidfd **7 门槛**回归通过；全部本批 unit 最终消失。
- 最终实盘使用新建并完全预留的 NVMe/HDD 各 **2 GiB** ext4 镜像，真实固定 helper、systemd sandbox、只读配额 seccomp、Landlock 和实际物理身份链，完成独立 Plan/Capture/Verify 三个长任务，捕获和验证回执一致。没有存储或执行 double。
- 另建隔离空间写入 **200 MiB** 高熵文件，在真实捕获持有 NVMe/HDD 锁时，经固定 Observe 得 Running、Cancel 得 Stopped；部分压缩包的 inode/长度/SHA 保持，重复 Start 不重派发。部分包原件和日志留在私有证据。为稳定观察窗口，QA 复制件将工作单元 CPUQuota 收紧为10%、运行上限收紧为90秒，其他安全设置保持；正式候选为100%/7200秒，未被测试工具改写。
- 内核项目硬配额/定长文件系统分别实际拒绝非特权写入；既有三次直接捕获、重命名两侧两次真实强杀、重挂及无源码独立验证继续通过。原源码 inode/字节与内核配额前后相同，正式配置字节保持；全部自有镜像、挂载和私有执行资源按原身份收尾。正式300GB HDD只读核验，没有创建生产归档。

## 失败与边界

初始隔离路径替换发生二次替换，随后正则分支边界遗漏又改变了测试复制件的镜像根。实际拒绝与清理记录保留；现用一次有边界替换并有独立回归。一次 Observe 恰遇结果提交短锁返回 BUSY，探针此前错误当成不可重试；现仅对 Observe/Cancel 的精确 BUSY 做有界重试并保留各次原回执，Start 不自动重试。未改变产品的失败或忙碌门槛。

原生预检还发现 Landlock 会拒绝新开可写 /dev/null，现只在限制前打开这一固定 sink；实际源码 FD 均在限制之后打开，未放宽允许写入路径。独立审查发现并修复上述取消入场竞态，并发测试断言 guard 安装时新 worker 只能 BUSY，随后保持 Stopped。

最终原生证据：deploy/artifacts/development-archives-native-2f6b0ce9395d414b8d06893ecc9e9988/；含实际模块复制件摘要、每次控制回执、完整回执、取消部分包、原源码/配额及清理记录。其余五轮原件分别封存，含三轮未通过与两轮早期通过，不能代替最终候选。汇总与新CI观察在 development-archive-helper-candidate-20261110-c63fa911 下；本批原未提交 source 合并预览 diff SHA256 继续为 e56a10d30f5cb49bf771ae6fc9bd35a3564bfc586b4ca3338899940b84c0eca2。

继续签名 Agent Start/Observe/Cancel、能力版本门禁和完整请求/nonce 绑定，再进入服务端持久归档意图、当前资源授权、共享事务围栏、明确确认及 BFF/Vue。归档回执摘要必须由服务端原任务持久保存，独立验证不得信任浏览器或 HDD 自报摘要。空间退役、防复活、quota/claim 清零和最后释放数据库预算仍待完成；维护监听连续性、320px 布局以及其余59项/全部子门槛也继续保留。项目图谱待同步。
