# 项目配额与工作空间目录发布

2026-11-09，代码 `baa419ea` 将项目配额接入 Agent 存储分配。固定传输要求宿主批准配置 `schemaVersion: 2`，每个逻辑根登记 `projectIdStart` 和 `projectIdEnd`。范围须由管理员在该文件系统独占保留，至少包含 32 个 ID。同一文件系统 UUID 下的逻辑根不得有重叠范围。已有范围和凭据需保留至生命周期清理完成。

分配器在锁定的逻辑根内从 UUID 派生至多 32 个候选，跳过其他工作空间的凭据和已占用的内核配额记录。在写入配额前同步独占凭据，绑定文件系统、空间、环境摘要、磁盘预算、工作空间 inode 和 `data` inode。配额、继承标志和凭据回读成功后才原子发布。重试优先恢复原凭据。已发布目录回读不到原项目配额时拒绝，旧目录和可疑凭据保留现场。Runner 接入时须独立核对绑定后再开放工作路径。

Agent 1.62 对 BFF 确认 `project_quota_verified`。旧 Agent 及缺少该标志的回执均不能完成新的 BFF 分配事务。早期已保存的 `StorageAllocated` 行保留原状态，不能单凭状态推定已有配额。正式安装脚本将驱动和守卫安装为固定 Root 文件，系统服务仅为该动作提供 `CAP_SYS_ADMIN`，并限制可写路径和系统调用。传输在读取请求之前加载参数级过滤，限制 ioctl、配额命令与旁路入口。[Linux seccomp 过滤接口](https://www.kernel.org/doc/html/latest/userspace-api/seccomp_filter.html)

## 实际验证

无网络 QEMU/KVM 来宾以两块虚拟 PCIe NVMe 磁盘运行 ext4 与 XFS。设备拓扑、挂载、文件系统 ioctl 和配额内核接口是真实来宾行为，UUID 来自预设磁盘镜像，udev 记录由测试写入。正式 Python 传输在 capability bounding set 仅保留 `CAP_SYS_ADMIN` 且设置 `NoNewPrivs` 的子进程内通过分配及卸载重挂后回读。ext4 在内核设置硬限额后、绑定项目 inode 前遭遇真实 SIGKILL，确认目录未发布，原请求使用唯一凭据恢复。验证首轮时来宾文件夹受启动 umask 影响，收紧为 077 后通过。实际内核过滤拒绝其他 ioctl、配额命令与再次 exec。systemd 参数通过静态语法检查。

20 项存储测试、8 项配额与过滤测试、6,216 项 .NET 单元测试、8 项范围、12 项宿主运行时及 9 项 CI 契约检查通过。最终两项签名 RPC 专项再次通过。本地证据在 `deploy/artifacts/development-workspace-quota-publication-baa419ea/`，清单 SHA-256 为 `fe89272ce2f3bdb92f51373d667e0a8ae93c3ece2e3dd952028bf010d0cd1429`。

当前会话 `NoNewPrivs=1`，工作站标准安装及物理 NVMe 配额仍待验收。Runner、Vue 和完整开发空间验收继续实施。
