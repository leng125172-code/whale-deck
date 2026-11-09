# 工作空间 Linux 项目配额

2026-11-09，代码 `0552bc8a` 实现使用已打开文件描述符的项目配额驱动。驱动要求项目配额同时启用计量与强制执行，以登记的磁盘 MiB 设置硬块限额，并设置有界的 inode 硬限额。只对空的、Root 所有的私有目录初次绑定项目 ID 和继承标志。调用方须先持久化该项目 ID 与目录 inode 的独占凭据。重复调用只接受同一项目和相同配额，遇到其他项目、已有计量、额度漂移或不支持的 ABI 时拒绝。驱动使用 [Linux quotactl 接口](https://man7.org/linux/man-pages/man2/quotactl.2.html)。

## 实际验证

无网络的 QEMU/KVM 来宾运行 Linux 7.0.0-42，使用两块从空白镜像创建的虚拟 NVMe 磁盘。ext4 与 XFS 分别设置 256 MiB 和 65,536 inode 硬限额，子目录继承项目 ID。降至非特权 UID 后，1 MiB 分配成功，260 MiB 分配被拒绝；同一磁盘上配额外的 260 MiB 对照分配成功。ext4 返回 `EDQUOT`，XFS 返回 `ENOSPC`，与 XFS 项目限额实现一致。两个文件系统卸载重挂后配额和继承标志均能回读。XFS 仅计量模式也由来宾内核实际拒绝。 [XFS 内核实现](https://github.com/torvalds/linux/blob/master/fs/xfs/xfs_trans_dquot.c)

初次来宾启动时补载了 quota 格式模块；随后用虚拟 NVMe 序列号识别设备，避免异步枚举改变设备编号。ext4 的失败预分配可先占用部分空间至限额，测试据此检查用量不超过硬限额，同时保留失败回执和配额外对照。最终 7 项配额用例、16 项存储回归、8 项范围校验和 9 项 CI 合同测试通过。

工作站根的只读查询返回 `QUOTA_UNAVAILABLE`。虚拟机证据覆盖真实内核行为，正式工作站上的物理 NVMe 配置仍待验收。下一步将独占项目 ID 凭据、限额驱动和回读门禁接到固定 Agent 分配前，随后验收 Runner 的隔离与使用权限。

本地证据在 `deploy/artifacts/development-workspace-project-quota-0552bc8a/`，`SHA256SUMS` 的 SHA-256 为 `eb19e664d45eef4aa761640b6a77a0d8f295db51c54ffa24de70e28ae85824e5`。
