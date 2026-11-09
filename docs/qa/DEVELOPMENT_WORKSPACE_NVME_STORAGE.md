# 开发空间 NVMe 存储内核

2026-11-09，候选 `fd8066ab`。宿主模块读取私有、Root 所有的存储配置。逻辑根对应固定目录 `/var/lib/whaledeck-development/<rootId>`，配置声明其所在挂载点、文件系统 UUID 和保留空间。模块核对打开文件描述符的挂载 ID、挂载记录、udev UUID、文件系统类型和 PCIe NVMe 拓扑。目录逐层使用 `openat2`，拒绝链接和未经批准的挂载跳转。

分配锁绑定根目录 FD。新目录先在同一根目录封存包含空间、环境摘要、预算及 inode 的凭据，然后用 `RENAME_NOREPLACE` 原子发布。相同请求可恢复已封存或已发布的目录，未知暂存、冲突目标和替换 inode 保留原状。调用检查当时可用空间及保留量。未来 Runner 提供写入前仍须设置和实测磁盘配额。

## 验证边界

工作站低层只读探针核对了当前真实根文件系统、udev UUID 和 PCIe NVMe 拓扑。探针没有登记宿主根、创建配置或分配目录。隔离测试使用合成设备及挂载元数据、真实 Linux `openat2`、`flock`、`fsync` 与原子重命名。真实子进程在暂存封存后被 SIGKILL，内核释放锁后原请求恢复。测试根、暂存和目录均为临时资源。

NVMe 的 14 项用例在工作站用户命名空间和无网络的隔离容器各通过一次。容器另通过 22 项 HDD 备份回归和 26 项文件根升级回归。容器用只读源码，所需的跨 UID 权限及临时空间均限制在容器内。范围校验 8 项、CI 契约 14 项及宿主运行时校验 12 项通过。日志在本地 `deploy/artifacts/development-workspace-nvme-storage-fd8066ab/`，`SHA256SUMS` 的 SHA-256 为 `8af319f6ba722e712e2d144be65e4f1ddf42eb7cc7a43acecad405cd3ce1a232`。

## 签名 Agent 与固定安装入口

2026-11-09，候选 `4b44200e` 增加 Agent 协议 1.61 的 `development.storage.allocate`。Gateway 只接受处于 Registered 且主体为所有者的工作空间，并协商能力后签发 45 秒一次性 HMAC。签名绑定方法、主体、追踪 ID、空间 UUID、存储根 ID、环境摘要和登记磁盘预算。Agent 校验实际 protobuf 消息，拒绝重复签名头、旧签名格式和已用 nonce。宿主侧重复分配继续通过封存凭据核对。返回给客户端的回执只包含分配请求的业务字段，设备 UUID 和 inode 保留在宿主。

标准安装脚本增加固定 Python 传输和分配模块，校验器核对安装文件与源码字节。固定 sudo 动作在独立短生命周期 systemd 单元内调用传输，只对 `/var/lib/whaledeck-development` 提供写权限。批准挂载为上级文件系统时，驱动要求这条可写绑定挂载同时匹配父挂载 ID、设备、文件系统内部源路径和固定挂载点，逻辑根以下继续拒绝挂载跳转。管理员需在 `/etc/whaledeck/development-storage.json` 中登记根及 UUID，预建权限为 root:root/0700 的逻辑根。安装脚本只在固定父目录缺失时创建该目录。

16 项宿主测试通过。真实私有挂载命名空间复现同设备错目录绑定、只读绑定、子目录过挂载及正确绑定上的分配与回放。设备元数据使用测试夹具。独立 Python 入口拒绝重复 JSON 字段与外部路径参数。6216 项 .NET 单元测试全部通过，包含实际 Unix Socket HTTP/2、Gateway、签名拦截器和测试分配端点的验证，以及真实 Agent 回执解析器测试。10 项退役安装回归、12 项宿主运行时、8 项范围及 5 项前端安装 CI 契约检查通过。本地日志位于 `deploy/artifacts/development-workspace-storage-rpc-4b44200e/`，`SHA256SUMS` 的 SHA-256 为 `35c286fecbcb13aedef37315ab5afa68f537d71d750af171824c184aeae73c50`。

当前会话 `NoNewPrivs=1`。正式工作站上的标准安装、systemd 单元到专用 NVMe 根的真实签名分配仍待验收。后续接入持久化业务计划、数据库状态迁移、磁盘配额和 Runner。
