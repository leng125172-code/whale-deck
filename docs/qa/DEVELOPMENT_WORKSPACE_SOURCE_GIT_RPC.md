# 开发空间签名 Git 获取

2026-11-10，代码 `2d4e83f3`。Agent 协议 1.70 增加 `development.source.git`。签名绑定空间分配请求、导入 ID、仓库 ID、ref 和可选凭据引用。Agent 只在通过签名和防重放校验后启动固定 sudo 动作。返回流先传来源与归档清单，再传 64 KiB 数据块。最后一块须等长度、SHA-256、输出 EOF 与宿主进程退出状态均通过后才发送。后续仍通过原有源码暂存与显式发布接口写入工作树。

标准安装准备空的 `/etc/whaledeck/development-git.json` 和私有凭据目录，保留已有安全登记的内容并收紧为 root 所有、0600。管理员在登记中为仓库配置专属 `credentialRef`，私有文件为 `/etc/whaledeck/development-git-credentials/<credentialRef>.json`，root 所有、0600，字段为 `ref`、`username` 和 `token`。临时服务通过 systemd 凭据只读取得登记和选定的一份私有凭据。凭据值留在宿主获取器内。

获取服务使用动态 UID、空 capability 集、`NoNewPrivileges`、512 MiB 专用 tmpfs 和固定单实例上限。服务无法访问 Agent Socket、工作树、主机数据目录和凭据源目录。取消请求会停止该固定临时服务。获取器继续将 Git 连接限制在批准的 Smart HTTPS 仓库。

## 实际验证

- 在无外网网卡的 KVM 来宾运行真实 systemd、sudo、非 root .NET Agent 和 Unix Socket，并运行 TLS 下的 Git Smart HTTP 后端。确认动态 UID、零有效 capability、512 MiB tmpfs、Agent Socket 和凭据源路径不可见。取消正在等待 TLS 上游的请求后，临时服务退出，后续获取成功。
- 匿名、旧式签名、篡改请求及签名重放被拒绝。未登记仓库、凭据引用错配、凭据文件缺失及符号链接源码被拒绝。真实 Git 分支和 Basic 私有附注标签获取成功，清单绑定准确提交，客户端独立验证多块 TAR 的长度和 SHA-256。
- 同一 TAR 经真实签名 Agent 源码暂存与确认发布，在 ext4 项目硬配额下得到真实工作树。逐字节复核原始内容、可执行位、工作空间 UID 和 UTF-8 空文件。
- 6227 项 .NET 单测（含 8 项源码 RPC 专项）、6 项 Python Git 与归档宿主专项、8 项范围基线及 Shell 语法通过。首轮发现动态用户的默认私有 `/tmp` 覆盖显式容量，改用独立挂载后验证 512 MiB。隔离镜像补齐 Git 与 CA 目录的遍历权限后通过最终 TLS 验收。

继续接 Worker 的持久化获取和重试、BFF 授权与 Vue、工作树更新和冲突，以及物理工作站安装部署验收。

私有证据位于 `deploy/artifacts/development-workspace-source-git-rpc-2d4e83f3/`，清单 SHA-256 为 `1451bbfc7f921d127b6637cc941c34c79d331756785e2cc9c90d733d3b3e3da7`。
