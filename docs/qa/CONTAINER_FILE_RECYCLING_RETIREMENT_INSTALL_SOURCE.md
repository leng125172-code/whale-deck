# 退役宿主取消与业务来源校验

2026-11-09。宿主准备 `cd986658`，取消和安装修正 `3cee128b`，业务来源校验 `5aff320e`。本批验收范围是隔离 systemd 取消、安装路径合同与应用来源校验。

## 宿主准备

计划单元无写路径。Reserve、Inspect 和 Discard 仅能写退役状态目录，Apply 额外拥有固定回收 HDD namespace 的写路径。安装器检查退役目录及 claims 的 canonical 路径和私有权限，并拒绝与既有状态目录共用 inode。模块只允许使用普通单链接文件。安装和验证遇到符号链接、硬链接、目录或 FIFO 会拒绝，原有记录和备份不因安装创建或清除。

取消只针对精确 Driver ID、InvocationID、MainPID 和 cgroup。复核发现 Agent 的 capability bounding set 缺少读取目标进程初始环境所需的 `CAP_SYS_PTRACE`。修正将这项权限限制在独立的 8 秒 systemd 单元内，标准输入及输出关闭，不允许业务路径写入。核对绑定后经原 pidfd 发送信号，避免停止复用单元名或误伤新进程。

## 实际验证

- GitHub 托管 VM 上 `3cee128b` 的 [CI 42581102016](https://github.com/leng125172-code/whale-deck/actions/runs/42581102016) 中，真实 systemd/pidfd 专项步骤为 success。测试从缺 `CAP_SYS_PTRACE` 的调用方启动取消，错误 InvocationID 失败且原进程仍活，正确 InvocationID 停止同一专用 sleep 进程。测试仅重定向 helper 内部脚本的安装路径到 Root 私有临时目录。
- 本地用户命名空间通过 10 项安装与 helper 合同、11 项 pidfd 专项。后者用实际自有子进程，systemd 查询及 cgroup 有明确替身。Shell、Python 编译、14 项前端 CI 合同、8 项范围和 diff 检查通过。尝试在同一单 UID 命名空间运行完整回收回归失败，夹具不能 chown 到未映射的 UID/GID，完整 root 回归需查托管 CI。
- 当前会话的 `NoNewPrivs=1` 使 `sudo -n true` 被内核策略拒绝，Agent Unix socket 对当前进程不可读。只读查询显示 Agent 和 MaintenanceHost 为 active/running。工作站上的标准安装、配置 HDD 和新 RPC 正向仍待实测。

## 业务来源

从当前登录和实时管理员授权下的精确归档业务 ID 解析，拒绝跨容器引用。在读到归档行后复核请求、计划、执行、预留和回执 MAC，重查原 purge 与 recycle 来源，再验证当前登录及权限。新退役来源 MAC 绑定归档业务 ID、原执行 ID、预留与终态回执、祖先证明、显式保留天数以及已发布副本 inode/ctime。规划结果还须与该命令和已签副本身份一致。旧 Completed 回执缺失原围栏所有权或当时绑定已丢失时，保留历史复制成功事实，拒绝将它用于退役。

316 项归档与退役定向 Unit 通过，Release 全量 Unit 6145 项通过，均零跳过。其中包含 8 个新增来源用例，使用内存仓储、Gateway 和登录/身份替身，以及真实 HMAC。持久化事务、执行围栏、Worker、API/Vue/Jobs 与工作站授权仍待完成。当前协议仍为 1.59，标准发布后的有效沙箱、新 RPC、配置 HDD 和原来源自然到期为独立验收门槛。
