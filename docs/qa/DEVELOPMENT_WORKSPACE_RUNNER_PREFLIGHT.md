# Runner 存储复核

2026-11-09，代码 `a75386eb` 增加 Agent 1.63 的 `development.storage.attest`，继续支持 1.62 分配协议。复核与分配分别签名，签名绑定方法和实际 protobuf 正文，一次性 nonce 不可重放。

宿主复核只读已发布的记录。它在逻辑根的共享锁内校验文件系统、独占项目 ID、目录 inode、项目继承标志与实际硬限额。再次打开当前路径核对后，在内部上下文内提供已打开的 `data` 文件描述符。对外响应只返回工作空间、根 ID、环境摘要、磁盘预算及配额确认。复核不会发布暂存目录、升级旧目录或修复变更的配额。专用参数过滤拒绝设置项目属性和配额。Linux 的 `Q_GETQUOTA` 本身需要可写挂载引用。 [Linux 内核配额实现](https://github.com/torvalds/linux/blob/master/fs/quota/quota.c)

BFF `POST /api/v1/development/workspaces/{id}/runner-preflight` 校验同源、CSRF、当前身份、创建权限、工作空间所有权和登记状态，使用数据库锁定的预算和环境发起复核。成功只返回本次检查对应的工作空间版本、环境与预算，不发放路径、文件描述符或启动凭证。正式执行须在打开工作路径时再次核对并使用已固定的描述符。

## 实际验证

无网络 QEMU/KVM 来宾使用真实 ext4/XFS、虚拟 PCIe NVMe 与内核配额，UUID 来自固定镜像，udev 记录由夹具提供。正式传输验证首次分配与重挂后的配额及凭据，检查目录项 inode、mtime、ctime 不变。ext4 强杀分配进程后，复核拒绝未发布暂存；对两种文件系统直接变更测试配额后，复核返回冲突且保持变更后的限额。宿主用例另外验证了共享锁、描述符释放、旧目录拒绝和内核配额写入过滤。

真实 Unix Socket 验证方法之间不能混用签名、nonce 重放被拒绝、有效回执须与请求匹配。真实 HTTP 和隔离 PostgreSQL 验证未分配、分配中、越权、撤权和配额漂移的拒绝，以及成功返回的固定环境与预算。HTTP 测试的身份与 Agent 边界使用夹具，宿主拒绝另由真实来宾验证。PostgreSQL 原生回归输出 114 个阶段标记，容器及网络已清理。

Release 编译零警告零错误，6,216 项单元测试、837 项默认集成测试、23 项存储用例和 8 项配额及过滤用例通过。默认集成另有 7 项按原生依赖条件跳过。过滤器夹具首轮传入负文件描述符，被 Python 在 syscall 前拦截，改用超出进程可用范围的非负描述符后验证了实际内核拒绝。证据位于 `deploy/artifacts/development-workspace-runner-preflight-a75386eb/`，清单 SHA-256 为 `e50c917d31ce664d16dabeefe10732da0648d9295d1f504c2b57393de040a76a`。

Runner 尚待接入工作路径、容器身份、隔离执行和完整生命周期。当前会话 `NoNewPrivs=1`，物理工作站的标准安装仍待验收。
