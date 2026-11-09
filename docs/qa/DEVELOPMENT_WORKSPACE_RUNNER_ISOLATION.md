# Runner 私有挂载与 OCI 隔离

2026-11-09，代码 `5c579ee6`。独立 launcher 进程先进入新的私有挂载命名空间，再打开已复核的数据目录。驱动从 FD 创建非递归挂载，在发布前设置 `nosuid,nodev`，挂到 root 私有 OCI bundle 固定槽位并回读 inode。OCI 策略再次验证槽位，工作目录须属于指定的非 root UID/GID。根文件系统只读，网络仅有回环，无额外 capability，禁止提权，内存、交换、CPU 和 PID 使用 cgroup 限制。 [Linux FD 挂载接口](https://man7.org/linux/man-pages/man2/open_tree.2.html)

应用 seccomp 默认拒绝未登记的 syscall。允许普通线程、普通调度和诊断 FIFO，阻止项目 ID 与继承属性写入、配额写入、设备节点、io_uring 和新命名空间。旧方案尝试让 Docker 直接绑定 `/proc/<pid>/fd/<n>`，当前 Docker/runc 组合返回 EINVAL。私有挂载和实际运行时在同一 launcher 命名空间内验证。 [Docker seccomp 接口](https://docs.docker.com/engine/security/seccomp/)

## 实际验证

- 本机 Linux 用户命名空间用实际绑定挂载验证替换源路径后仍投影原 inode，嵌套挂载未复制，父命名空间看不到投影。复用已有槽位、错误 inode、错误 UID、相对命令与非法预算均拒绝。
- 无网络 QEMU/KVM 来宾通过正式存储复核、投影及 OCI 配置，在虚拟 PCIe NVMe 的 ext4 和 XFS 上用 runc 1.5.2 运行非 root 探针。配额内可写，超额分配被内核拒绝。拦截属性修改、配额写入、io_uring、挂载、新命名空间和设备节点，同时允许 FIFO。实际 cgroup 回读 500 毫核、128 MiB、交换 0、128 PID，网络仅有 `lo`。来宾使用专用最小 rootfs 和 `--no-pivot`。
- 工作站 Docker 使用同一 seccomp 策略、锁定摘要的 .NET SDK 和 Node 镜像，在断网、只读根文件系统和非 root 下完成 .NET 构建运行与 Node 测试。此项使用独立临时绑定目录。CoreCLR 和 MSBuild 初轮受限失败，经 syscall 跟踪补齐普通调度后通过。测试容器已清理。
- 2 项隔离测试、23 项存储回归、8 项配额回归、8 项范围校验及 9 项 CI 合同检查通过。

代码目前提供宿主隔离原语和 OCI 策略。摘要固定的 rootfs 准备、正式 Agent 启动与收尾、物理 NVMe 上的端到端验收继续实施。当前工作站会话的 `NoNewPrivs=1` 仍阻止标准宿主安装。证据在 `deploy/artifacts/development-workspace-runner-isolation-5c579ee6/`，清单 SHA-256 为 `dc5e47db5625cad7d384ba7a40cb7057c6e82aef1d73932c2ce458c65998fab6`。
