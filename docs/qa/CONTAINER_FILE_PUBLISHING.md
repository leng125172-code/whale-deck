# 容器文件原子发布与中断观察：内部候选

当前单工作站 59 项继续 active，17 项移出不算完成。本批为 P1-11、P2-03/P2-04 的写入引擎前置工作；尚未安装到 helper、注册新 RPC 或开放业务/UI 写入。没有构建产品镜像，运行中的 API/Worker/Agent/Gateway 不变。

## 实现

- 宿主私有暂存跨文件系统复制到目标父目录下 root/0700 的固定私有目录，输出完整读回 SHA 校验后才允许名称替换。
- 精确容器/启动实例、根/namespace/Docker 配置、主体、父/叶版本和当前路径复核。只读根、只读绑定、挂载根、链接和保护目标拒绝；new file 取实际 PID 的内核 filesystem UID/GID，默认 0600，覆盖保留普通权限/属主并剥离新内容的 setuid/setgid。
- 覆盖在准备前和提交前验证物理 HDD 原件。NOREPLACE 新建、EXCHANGE 覆盖；旧 inode 保留在私有目录，HDD 历史另留存。
- Applying 意图先持久化，rename 后 fsync 两目录，核对新/旧对象与完整内容后 Published。观察及重放不重复复制/替换，不自动回滚；外部并发导致证明不足时保留 Unknown。
- PublishPreparing/Interrupted 的明确取消仅删除属于回执的私有准备内容和宿主载荷，HDD 不删除。取消意图先持久化；重复明确取消可完成剩余清理，观察不删除文件。Published 历史不降级，后来的外部变化仅将 bindingCurrent 置 false。
- HDD 暂不可用时保持待确认状态；验证接口不创建缺失历史 namespace。旧暂存 inspect/discard 不能绕过发布状态机。

## 验证

```bash
sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_container_file*.py'
sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_file_*.py'
sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p test_tree_backups.py
python3 -m unittest discover -s deploy/validation -p test_scope_baseline.py
sudo -n python3 deploy/validation/validate_container_file_publishing.py
sudo -n python3 deploy/validation/validate_file_backup_engine.py
```

最终候选：127 项容器联合（包含 23 项新发布测试）、184 项宿主文件、14 项目录备份和 8 项范围全部通过。单元夹具的 Docker liveness/配置与 HDD mount/udev/sysfs 明确为合成输入；复制、rename、fsync、权限、inode、锁、真实进程强杀在实际 Linux 上执行，无 root 跳过。

23 项新增覆盖完整 2 MiB+7、空文件、UID/GID/权限、已发布重放/目标后来变化、只读与嵌套挂载、源/父/配置/主体冲突、Unsafe 私有目录、NOREPLACE 竞争、EXCHANGE 后原件变化、输出损坏/HDD 损坏/错误历史绑定、硬链接、父路径置换、原暂存入口绕过、并发锁、属主变化和历史 namespace 暂失。

两个测试组包含 10 个实际 fork+SIGKILL 位置：private 创建前、pending 身份记账前、复制中、Applying 记账前、rename 前/后、Published 记账前，以及取消的两次 unlink 前/Discarded 记账前。观察不做 rename；完整提交可补记 Published，准备不完整只保留中断或 Unknown，明确取消不改当前文件。进程强杀证据不冒充真实断电。

实机验证冻结源码，使用已缓存固定镜像，无拉取/构建；两个独占非 root、无网络容器：

1. 只读根 + 独占可写绑定：覆盖与新建各 2,097,159 字节；宿主暂存和目标实际 st_dev 不同，完整字节/SHA、UID1000/GID1000、0640/0600、旧 inode 与物理 HDD 原件一致。错误主体、只读根写入、停止实例拒绝，重放不再写入。
2. 无绑定的可写容器层：覆盖镜像原有普通文本文件为 2,097,159 字节，新建完整同长度文件；旧 inode 和原内容保留，HDD 原件一致。在 Published 回执写入前实际 SIGKILL 发布进程，确认内核终态与持久 Applying；新建真实 ContainerRoot 会话在禁止再次 copy/rename 的条件下对账为 Published。

容器绑定源与 HDD 留存同盘，仅称版本留存；`st_dev` 也不能单独证明 overlay 背后的物理盘。另已有宿主 NVMe→物理 HDD 的 2,097,159 字节沙箱重验通过。所有成功夹具、HDD 历史和独占容器已精确清理；独立最终检查 11 核心 healthy，validation 标签容器和本批临时目录为零。

## 原始失败与修正

- 首轮强杀测试发现 private 尚未创建时，首次观察为 Interrupted、重放却变为 Unknown；已保持该中断事实并重跑全部 23/127 项。
- 首两次实机验证的检查器使用 `HostConfig.Mounts.ReadOnly` 必存在的假设，但 Docker 对可写绑定省略该 false 字段。按缺省 false + 实际 Mounts.RW=true 双重验证修正；两次遗留容器和仅有已知合成原件的目录，经精确 ID/镜像/标签/挂载/完整字节与 inode 复核后清理，核心快照不变。未放宽有效挂载/只读断言。

冻结关键源码 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| whaledeck-container-publish.py | a4d94a09f62e6a31c9ec2ba6ffef22ba7f6d3dc06ffbc33cb0d2bf7850e423a5 |
| whaledeck-container-backups.py | 323a57720dcdc11d4c68af57c12fb16a8f1e0ba82e82cd319d35979216398a83 |
| whaledeck-container-upload.py | aadb80feae9df3c837462d05cec85114154b69c18b59c05b1dcfc3b8d2476918 |
| whaledeck-file-backups.py | 664648739bafe02005494c00ab4b4010787550f36e3fb1689bbff5e0b4ea9ebe |

## 待接产品流程

独立 capability/RPC/helper 与安装沙箱、永久任务/租约/Outbox/审计、当下权限/登录、管理员 Unknown 接管、历史/恢复及 BFF 上传/编辑/历史 UI 尚未接通。需要签名 UDS、实际管理员/普通用户/撤权/中断以及桌面手机验收，才能签收完整容器文件功能。实机根用户执行内部类，不算产品授权或 UI 证据。
