# Runner 固定摘要 rootfs

2026-11-09，代码 `ff996ac1`。宿主 Runner 从 root 私有的离线 OCI 内容目录读取登记的 SHA-256 摘要，按本机架构解析 OCI 或 Docker v2 索引。验证入口、平台 manifest、配置及各层的摘要与字节长度，并逐层核对未压缩 DiffID。当前接受原始 tar 和 gzip 层，对压缩与解压后的总量分别设限。私有 staging 只向解包器提供经校验的层与最小 OCI manifest。 [OCI Image Layout](https://github.com/opencontainers/image-spec/blob/v1.1.1/image-layout.md) · [OCI Image Manifest](https://github.com/opencontainers/image-spec/blob/v1.1.1/manifest.md)

解包后同步文件系统，写入绑定登记摘要、平台 manifest、转换 manifest 及 rootfs 设备与 inode 的记录，再原子发布目录。已有内容的 bundle 会拒绝复用。Runner 在生成只读 OCI 配置前复核记录和目录身份，使用自身的启动配置。

## 实际验证

- 从本机 Docker 缓存导出保留原摘要的两份锁定镜像。Node 直接按 OCI manifest 验证，.NET SDK 从 Docker 多平台索引选定 Linux amd64。验证并转换后，使用 Ubuntu 包的 umoci `0.4.7+ds-4` 解包。验收记录保留包和二进制摘要。
- 离线 QEMU/KVM 来宾运行 Linux 7.0.0-42，输入磁盘只读。经正式存储复核、目录投影和 OCI 策略生成配置，以 runc `--no-pivot` 在只读 rootfs、UID 10001 及启用项目配额的虚拟 NVMe ext4 工作目录中运行。Node 实际通过 `node --test`，.NET 实际构建并执行 C#，返回 `DOTNET_PINNED_BUILD_OK 5`。
- 新增 7 项测试通过，包括篡改 blob、DiffID 不匹配、歧义平台、JSON 非法值、符号链接、目录替换和发布前 SIGKILL。强杀时记录已写入，最终目录尚未发布，复核拒绝进入，原 bundle 也拒绝重试。另通过 2 项隔离、23 项存储、8 项范围及 9 项 CI 合同回归。
- 初次来宾导入的目录保留了宿主 UID，权限门禁拒绝。校正隔离输入盘的目录所有者后，来宾又拒绝复用上次的 scratch bundle。最终用只读输入盘和每次重新初始化的临时盘通过。直接 chroot 启动 .NET 的诊断失败，实际 OCI 运行通过。

## 后续边界

当前宿主模块由可信调用者传入解包回调。标准安装还需部署经过版本固定的解包器及其隔离边界，接入 Agent 正式启动、取消和清理，并完成物理工作站验收。当前工作站会话为 `NoNewPrivs=1`，标准特权安装尚未验证。本批真实启动使用 Linux amd64。

私有证据目录为 `deploy/artifacts/development-workspace-runner-rootfs-ff996ac1/`。`SHA256SUMS` 已逐项核对，清单摘要为 `84628c32c1a08e902eab47e330f0814e4a05526e9a8eced2255a9b20d733f414`。
