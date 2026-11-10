# 开发空间定长存储与真实工作站配置

2026-11-10，代码 `40535499a84d78c14d9ab539a76e3b8a270b66c0`。按用户指定，将 `/dataNvme/DevSpace/` 限制为 120 GB、`/data/DevSpace/` 限制为 300 GB。这里 GB 为十进制：120,000,000,000 与 300,000,000,000 字节。文件系统元数据也占用容量，因此实际可用数据空间略小。

两个目录原本为空，分别位于 NVMe 系统根与 HDD。现有两个 ext4 分区都没有 project/quota 特性。本批没有修改这些分区的特性、格式化现有分区或调整分区表，而是在各自物理文件系统创建全额预分配的新文件系统映像。NVMe 映像位于 Root 私有的 `/var/lib/whaledeck-development-disks/nvme.ext4`；HDD 映像位于 `/data/.whaledeck-development-disks/archive.ext4`。两者挂载到指定空目录，NVMe 再绑定至 Agent 固定的 `/var/lib/whaledeck-development`，保留原最小写入范围。

## 来源、容量与配置安全

存储声明新增显式 `diskImage`，绑定固定私有目录内的文件路径、物理设备、inode、长度和外层文件系统 UUID。核验文件为 Root 单链接 0600 普通文件、容量已完整预留，再独立检查底层 PCIe NVMe。内层 mount UUID、loop 设备/子系统、映射文件、零偏移、整文件映射长度及可写状态均须匹配，并在读前后复查。没有声明的 loop 继续拒绝；原直接 NVMe 路径保持原核验。

来源检查使用内核公开的 [loop sysfs 接口](https://www.kernel.org/doc/html/v7.0/admin-guide/abi-testing.html)，不给分配器开放块设备。真实受限服务仍启用 `PrivateDevices`、`NoNewPrivileges`、受限系统调用和配额只读过滤。

Root 专用的 `whaledeck-development-provision.py plan/apply` 接入标准宿主安装及字节核验，未加入 Agent sudo 动作。只读计划绑定目标原 inode/权限/挂载和容量；检查目标为空、映像与目标在同一物理文件系统及外层剩余保留量。申请后在 Root 私有账本持久化文件创建、格式化与挂载状态。只允许创建新映像，或接续本事务已声明且 inode 绑定的未发布文件。未知非空文件、配置和 mount unit 冲突都拒绝覆盖。

目录转为 Root 私有后再次核对空目录，迟到写入原样保留。配置和 mount unit 使用完整临时文件、fsync 与 `RENAME_NOREPLACE` 发布。中途失败保留状态，不自动撤销已完成步骤；同一计划可继续。部分挂载尚未完成时不发布存储配置；本批不签物理断电/冷启动恢复。

## 真实内核与恢复验证

- 5 项来源策略测试通过，覆盖错误 loop 映射、偏移/长度/只读、复制 inode、错误物理设备、稀疏文件、硬链接、权限及迟到替换。23 项原存储测试全部通过，包含真实私有挂载命名空间；12 项宿主运行时与 8 项范围检查通过。
- 真实 NVMe 上新建 1 GiB 试验映像。普通 UID 的 1 MiB 写入成功，项目 256 MiB 硬限额下 260 MiB 分配得到 `EDQUOT`；配额外相同请求成功。超出映像文件系统的 2 GiB 请求得到 `ENOSPC`。卸载重挂后配额仍可回读；实际 `PrivateDevices` systemd 单元完成来源和只读配额核验，修改配额和创建文件均被拒绝。临时挂载和映像已清理。
- 独立的 2 GB NVMe/HDD 试验走实际格式化、三个 mount unit 和配置发布。分别在创建映像后、格式化后、第一块磁盘挂载后和配置发布后结束进程，随后沿原 inode/UUID/计划接续。整个过程恰好格式化两次，完成后的重放没有重格式化、重挂载或改配置正文。
- 普通 UID 通过已打开目录 FD 在最初空检查后写入，配置器准确拒绝挂载，保留迟到内容。非空目标、未知非空中断文件及错误物理文件系统的映像位置也均拒绝。试验 mount unit、映像和目录已准确清理，未写生产存储配置。

两份主要私有验收包：

- `development-disk-image-5b4e1b77bf004aa899617c61ec2186be`，SHA256SUMS 摘要 `046a15ea1c65238804d47b16e460d1569aca65dc58bc9e05d79831b65e16aef8`。
- `development-storage-provision-be5a6e0fc4ac453ea5a7b97ec57b5d08`，SHA256SUMS 摘要 `619575d9a4fef7c58e08a39d311c0072454569bf4fa57c5d659efb410dded2f5`。冻结配置器字节与最终代码一致，验收包装器随后仅调整配置器文件名。

## 实际部署及用户目录

干净 `dev` 经标准维护单元 `whaledeck-dev-publish-40535499.service` 覆盖成功，Invocation `9cef78d28f854e92a9a98bf850cd38c5`，终态 active/exited、MainPID 0、Result success、ExecMainStatus 0。六组件及 API/Gateway HTTP 来源一致为 `40535499`。账本 current=`40535499`、previous=`c51ed786`、pending=null。11 核心 healthy，8 数据库平台容器身份保持，私密运行配置和两份 sudo 规则摘要保持；标准 host validate 通过。

安装态配置器与提交源码字节一致。实际只读计划摘要 `48b6b9462db035fb3349b2908c66f8d17ad1de6ad76f6a981f1555f3c4b2fe16` 应用成功，状态 ready。两个映像分别为上述 120 GB/300 GB，已完整预留；两处用户目录为 ext4、`nosuid,nodev,prjquota`，硬配额处于强制执行状态。mount unit 已启用；存储配置注册 `nvme-main`。HDD 当前只完成独立容量与目的目录配置，开发归档/备份工作流仍须接入和验收。

本批维护采样 239 次均 HTTP 200，覆盖 11:25:04–11:33:01 CST；不涵盖发布最初时间，不签零停机或物理冷启动。原批 APT 失败及两次维护重启连接失败继续保留于[首次实机记录](DEVELOPMENT_WORKSPACE_INSTALLED_BASELINE.md)。该修复版 `c51ed786` 的 CI `42744552152` 后续已全部 success。

实际部署和容量配置原件位于 `deploy/artifacts/development-disks-release-40535499-20261110/`，10 份原件，SHA256SUMS 摘要 `0cf5651ecfc164bace046c64cc39477f3944837bb6452aee7d4b3f6c4130df2b`，Root 私有。固定运行时缓存、签名 Agent 分配、源码导入/运行、报告及逐行差异的实机整链继续实施。合并、IDE、预览和其余 59 主项不因此签收，`master` 尚未交付。
