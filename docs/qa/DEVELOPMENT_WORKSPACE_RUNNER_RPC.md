# Runner 签名 Agent RPC

2026-11-09，代码 `8dc16e42`。Agent 协议 1.64 增加 `development.run`，通过现有 Unix Socket 提供 Start 和 Observe。签名覆盖方法、完整 Protobuf 请求、到期时间和一次性 nonce。基础设施 Gateway 在签名前复核空间归属、已分配状态、锁定环境和登记预算，收到回执后验证运行 ID、宿主输入摘要及终态和退出码的对应关系。HTTP/Worker 的任务编排继续接入。

固定 sudo helper 启动与 Agent 单元绑定的 systemd transient service。启动器先核对本进程处在指定单元的 `launcher` 子组，再将解包和实际任务放入同一单元下独立的 `extract` 与 `workload` 子组。设置按请求时长推导的单元运行上限、进程数上限和整组清理。查询使用单独的短时单元。 [systemd cgroup 委托](https://www.freedesktop.org/software/systemd/man/254/systemd.resource-control.html)

## 实际验证

- Linux amd64 断网 KVM 使用 systemd 259、真实系统 D-Bus、sudo 和非 root WhaleDeck.Agent。通过实际 Infrastructure Gateway 和 Unix Socket 查询未启动运行、启动固定摘要 Node 任务、查询并重放原回执。工作目录中标记只写入一次，工作目录位于虚拟 PCIe NVMe ext4 的真实项目配额内。
- 来宾核对解包和任务各自位于受委托的子组，实际任务回读 500 毫核、512 MiB、128 PID。Node 核对 UID/GID 10001、`NoNewPrivs=1`，有效及 ambient capability 均为空。完成后单元与 bundle 均清理。
- 在另一运行的 Node 进程写入标记后，来宾实际停止 Agent。原 RPC 断开，受委托 cgroup 回收。重新启动 Agent 后，查询和重试均返回 `WORKSPACE_RUN_INTERRUPTED`，标记仍只有一条。
- 初轮在 systemd 单元内初始化 runc 时，`setuid(10001)` 返回 EPERM。跟踪发现 systemd 设置 seccomp 后移除了启动器的有效 `CAP_SETUID`。仅在受信任的启动单元内显式保留它，两个 OCI 配置均清空 ambient 集，应用能力集仍为空，复验通过。最小来宾另补齐了动态库、程序集读权限、PAM/D-Bus 和 systemd 重挂 `/run` 后的隔离 NVMe 设备记录。 [systemd 执行时的能力处理](https://raw.githubusercontent.com/systemd/systemd/v259/src/core/exec-invoke.c) · [runc 切换工作负载身份](https://raw.githubusercontent.com/opencontainers/runc/v1.5.2/libcontainer/init_linux.go)
- Agent 回归 3195 项通过，覆盖真实 Unix Socket 的签名重放、篡改、跨方法令牌和回执不匹配。Runner、挂载隔离、rootfs 与解包测试共 19 项通过，范围与 Shell 语法检查通过。标准宿主发布从 `8dc16e42` 生成 Agent、MaintenanceHost 和固定摘要的 umoci，整包 `SHA256SUMS` 通过。最终来宾使用包内自包含 Agent 及 umoci，并经标准 Agent 单元的 ExecStart 复验了正常和停机路径。

用户主动取消、保留 bundle 的回收、Worker 与 BFF 的任务编排及实际工作站部署仍待接入或验收。当前工作站会话的 `NoNewPrivs=1` 阻止标准特权安装。

私有证据位于 `deploy/artifacts/development-workspace-runner-rpc-8dc16e42/`，清单 SHA-256 为 `b157a46a5122b3e0b549c767ccbf880c869d50f570de3e9a454cf23ee68be9b0`。
