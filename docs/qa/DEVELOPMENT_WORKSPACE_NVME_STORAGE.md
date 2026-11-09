# 开发空间 NVMe 存储内核

2026-11-09，候选 `fd8066ab`。宿主模块读取私有、Root 所有的存储配置。逻辑根对应固定目录 `/var/lib/whaledeck-development/<rootId>`，配置声明其所在挂载点、文件系统 UUID 和保留空间。模块核对打开文件描述符的挂载 ID、挂载记录、udev UUID、文件系统类型和 PCIe NVMe 拓扑。目录逐层使用 `openat2`，拒绝链接和未经批准的挂载跳转。

分配锁绑定根目录 FD。新目录先在同一根目录封存包含空间、环境摘要、预算及 inode 的凭据，然后用 `RENAME_NOREPLACE` 原子发布。相同请求可恢复已封存或已发布的目录，未知暂存、冲突目标和替换 inode 保留原状。调用检查当时可用空间及保留量。未来 Runner 提供写入前仍须设置和实测磁盘配额。

## 验证边界

工作站低层只读探针核对了当前真实根文件系统、udev UUID 和 PCIe NVMe 拓扑。探针没有登记宿主根、创建配置或分配目录。隔离测试使用合成设备及挂载元数据、真实 Linux `openat2`、`flock`、`fsync` 与原子重命名。真实子进程在暂存封存后被 SIGKILL，内核释放锁后原请求恢复。测试根、暂存和目录均为临时资源。

NVMe 的 14 项用例在工作站用户命名空间和无网络的隔离容器各通过一次。容器另通过 22 项 HDD 备份回归和 26 项文件根升级回归。容器用只读源码，所需的跨 UID 权限及临时空间均限制在容器内。范围校验 8 项、CI 契约 14 项及宿主运行时校验 12 项通过。日志在本地 `deploy/artifacts/development-workspace-nvme-storage-fd8066ab/`，`SHA256SUMS` 的 SHA-256 为 `8af319f6ba722e712e2d144be65e4f1ddf42eb7cc7a43acecad405cd3ce1a232`。

模块当前作为后续 Agent 接入的宿主候选。调用授权、签名请求、标准安装、systemd 下的可写挂载与实际工作站分配仍待验收。现有会话 `NoNewPrivs=1`，工作站配置未写入。
