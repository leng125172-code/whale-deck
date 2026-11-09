# Runner 镜像解包隔离

2026-11-09，代码 `615fb86d`。Runner 将已验证的 OCI 内容交给固定 SHA-256 的上游 umoci v0.6.0，逐次核验安装文件的所有者、权限和内容，再复制进只读沙箱。解包使用独立 runc 配置和私有状态目录。输入只读，输出目录挂载为 `nosuid,nodev,noexec`。解包进程启用 `noNewPrivileges`，仅保留恢复 tar 文件所有权与权限所需的能力，禁止 socket、挂载和设备节点创建，并限制 CPU、内存、进程数、单文件大小和运行时长。超时或退出失败时只清理本次容器，清理未确认会单独报错。只有成功退出且运行时状态清空后才交给 rootfs 发布门禁。 [umoci v0.6.0 发布](https://github.com/opencontainers/umoci/releases/tag/v0.6.0) · [OCI 运行时配置](https://github.com/opencontainers/runtime-spec/blob/main/config.md)

宿主发布可通过 `WHALEDECK_RUNNER_UMOCI_FILE` 携带已下载的本机架构文件。发布脚本验证摘要并纳入 `SHA256SUMS`。标准安装有此工件时先校验，再以原子替换写入 `/usr/local/libexec/whaledeck-umoci`。安装仍支持只更新现有 Agent 与 MaintenanceHost 的产物。

## 实际验证

- 在 Linux amd64 的断网 KVM 来宾，正式工件校验和原子安装入口成功运行。解包沙箱使用 runc 1.5.2 的默认 pivot 路径，分别解包固定摘要的 Node 与 .NET SDK。两者随后以只读 rootfs、UID 10001 和真实虚拟 NVMe ext4 项目配额运行，Node 测试及 .NET 构建执行通过。应用进程在最小来宾里仍用 `--no-pivot`。
- 同一套解包配置的静态探针实际读到镜像输入，写入输出标记。内核拒绝改写输入、创建 Unix/IPv4 socket、创建设备节点和挂载。首轮越界探针未在沙箱外放置标记，复验先确认标记存在再检查沙箱内不可见，并确认 `noNewPrivileges=1`。将探针留在运行中触发超时，确认专用 runc 状态与 cgroup 已清理。
- 实际运行 `deploy/host/publish.sh`，从提交 `615fb86d` 发布 Agent、MaintenanceHost 和 `runner/umoci`。两个 .NET 自包含产物的运行时和 PKCS 版本均为 10.0.13，`SHA256SUMS` 逐项验证通过。发行版旧 umoci 工件由摘要检查拒绝。
- 5 项解包测试、7 项 rootfs、2 项 Runner 隔离、12 项宿主运行时、8 项范围及 14 项 CI 合同检查通过。安装器 CLI 与修改的 Shell 脚本语法检查通过。

Agent 正式启动、取消、故障后恢复和物理工作站安装仍需验收。当前工作站会话的 `NoNewPrivs=1` 阻止标准特权安装。本批实际执行的是 amd64 工件。私有证据目录为 `deploy/artifacts/development-workspace-runner-unpack-615fb86d/`，清单 SHA-256 为 `7fd21dc8a08d370f9e0111f8bd23bdf2f8d628ea574e4e9e6c7afcf9980b687c`。
