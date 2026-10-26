# 容器覆盖前 HDD 捕获：Linux 候选验收

范围：当前单工作站 59 项中的容器文件写入前置条件，不是整个文件功能、P1/P2 或全部目标验收。源码尚未安装到 helper，既有四个只读 RPC、GET 接口和产品页面未改变；没有构建或启动产品镜像。

## 已实现

- 暂存时冻结根设备/inode、进程起始 tick、PID/mount namespace 和批准的 Docker 配置/挂载摘要。重新建立会话时重复验证，不能仅凭 ID 与启动时间延用旧目标。
- `ContainerBackups.capture` 从已授权的 Staged 回执解析原件，复核私有上传全内容、源版本、父版本和配置证明；不接受另一个宿主路径或 RootId。
- HDD 捕获采用固定 `container-files-v1` 子目录和 schema 2/ContainerFile 元数据。双方即使误置历史文件，也拒绝跨域采用；宿主 schema 1 不变。
- 共用物理磁盘 attestation、UUID、旋转盘检查、权限、空间保留、原子提交/fsync 和全摘要复核。源绑定适配器在复制中检查活实例与父目录，开始和结束重新检查保护/配置/路径。
- 回执先保存不可变原件意图，HDD Verified 后再保存捕获证明。源和私有上传保持原样，Staged 不等于发布。
- 部分复制保持 Preparing/Incomplete，禁止静默重抄；完整 data 的 Preparing、或 HDD 已完成但上传回执未记账，只能重新校验后补记。丢弃私有上传不会删除 HDD 历史。

## 实际执行证据

```bash
sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p test_container_file_backups.py
sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_container_file*.py'
sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_file_*.py'
sudo -n python3 deploy/validation/validate_container_file_backups.py
sudo -n python3 deploy/validation/validate_file_backup_engine.py
```

- 新增 19 项 Linux-root 实际文件系统测试全部通过；容器文件联合 104 项、宿主文件联合 184 项通过，无原生跳过。HDD mount/udev/sysfs 在单元夹具中明确为合成输入，不拿这些测试证明物理磁盘。
- 19 项包含完整 2 MiB+7 原件、主体验证、源/父版本、同 ID/启动时间下配置/命名空间/root 更换、错误磁盘/SSD、跨域记录、损坏内容、私有载荷变更、unsafe namespace、锁与丢弃。复制实际写入首块后失去实例时，留下不完整状态，原件不变，不能重抄。
- 三个边界使用真实 fork + SIGKILL，确认 WIFSIGNALED/SIGKILL：HDD data rename 前、HDD Verified metadata 前、上传捕获证明前。前者明确 Incomplete，后两者只验证既有完整数据，不再复制或发布。
- `validate_container_file_backups.py` 在冻结候选上创建一个缓存固定镜像、非 root、无网络、只读挂载的独占实际容器。读取 2,097,159 字节原件，经实际挂载/udev UUID/sysfs 旋转盘证明写入临时 HDD namespace；逐字节与全 SHA 比对、重复捕获、错误 UUID、停止实例拒绝和丢弃后 HDD 保留通过。源文件与父目录版本未变。
- 该容器夹具源也在 HDD，`differentSourceDevice=false`，只证明同盘版本留存。另运行已有宿主备份沙箱验证：实际 NVMe 源到物理 HDD 的 2,097,159 字节、完整 SHA、重放和错误 UUID 拒绝通过，`sourceDifferentDevice=true`。它不代替容器产品沙箱或异盘容器源验证。
- 两个验证器均确认其独占临时内容已清理；独立 `docker ps` 再确认 11 个核心容器 healthy，validation 标签容器为零。没有修改生产备份配置、现存历史或产品服务。

实际容器验证冻结源码 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| whaledeck-container-upload.py | d9f5f9dea7169963e1284d8c1b6dd91bfc35e53088c56b642c2e2b170f17d29f |
| whaledeck-file-backups.py | 0c555d78e00cbccff0d8214643ec3380708678c9744c5f2402e50484e69f16f1 |
| whaledeck-container-backups.py | 9b9a94786da10f52149c35271b40aa234c268d973b9bc72db25e0fe3e1ec16eb |

前序 d4ccc9a 的 GitHub CI 已观察到 installer、backend（含依赖安全审计）、native-postgres success；frontend 浏览器组当时仍在执行。不能把前序 CI 算成此次候选全绿，也没有为了观察超时重启该任务。

## 未完成的产品链路

原子发布/冲突与 Unknown 对账、计划风险、历史/恢复、独立 capability/RPC/helper、产品沙箱、EF/永久任务/Outbox/锁/审计、当前身份/权限、BFF/上传/文本编辑/历史 UI 仍需接入。需要实际安装态与普通/管理员/过期撤权/取消/重启恢复/桌面手机验收，之后才可签收完整容器文件功能。当前 59 项继续 active，17 项移出不算完成。
