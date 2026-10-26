# 容器文件上传与发布

属于单工作站 59 项中的 P1-11、P2-03/P2-04。四个只读 RPC 与现有 GET 接口保持原状；暂存和 HDD 捕获引擎尚未注册到 helper、Agent、业务任务或前端。

## 暂存绑定

`deploy/host/whaledeck-container-upload.py` 的 `ContainerUploads` 接收可信调用者构造的 `ContainerRoot`、既有 openat2 文件原语与私有状态目录。没有 CLI，不接受宿主路径、命令、Exec 或 Docker archive PUT。

输入精确限定为完整容器 ID、本次启动实例、容器相对路径、父目录版本、原文件版本或 ABSENT、操作 ID、主体绑定摘要、长度与 SHA-256。Root 会话必须属于请求的容器和启动实例；指纹包含上述所有字段。状态/取消同样绑定原容器、实例、操作与主体。

回执还冻结实际根目录设备/inode、进程起始 tick、PID/mount 命名空间 inode 和批准的 Docker 配置/挂载摘要，使用独立版本域形成 `targetProof`。重新建立会话后，即使容器 ID 和启动时间相同，根、命名空间或配置改变也拒绝重放、查询、丢弃和捕获。先前内部回执缺少该证明时拒绝采用；这些候选尚未部署，不对产品旧数据进行静默补签。

沿用 PID/root FD、挂载与核心保护复核，以及禁止链接穿越的 openat2。暂存只创建宿主私有内容，不创建容器目标、不覆盖原件或改变其父目录。Staged 不是发布成功；覆盖前的 HDD 证明仍是独立发布阶段的必需条件。

## 内容与回执

- 单文件最多 1 GiB、分块最多 32768 字节、接收最多 300 秒；校验偏移、Base64、实际长度、全量 SHA 和 commit 后准确 EOF。
- 两个全局内核流锁、同操作独立非阻塞锁；状态目录为真实 root 所有的 0700 目录，锁/回执/内容均为 root 私有普通单链接文件。保留至少 128 MiB 或所在文件系统 5% 空间。
- 新操作回执及目录条目有界。已有操作不受新操作容量门禁阻塞，仍可查询、重放和丢弃；不存在的查询不创建锁。
- `Receiving → Verified → Staged` 对应部分接收、完整数据与证明已 fsync、私有名称提交。进程丢失后的 Receiving 只能观察为 Interrupted，不能把部分内容当成功或自动重收。
- Verified 的完整 `.data` 可按 inode、长度、权限和全 SHA 对账成 Staged；不发布到容器。重放仍消费并验证完整输入，不重复创建内容。
- 明确丢弃只删除属于原回执的私有对象。inode、权限、链接数或完整内容变化时保留拒绝；原容器文件保持原样。

## 覆盖前 HDD 捕获

`whaledeck-container-backups.py` 的 `ContainerBackups` 只从当前主体/原容器/原启动实例的 Staged 回执解析目标，不另收宿主路径或 RootId。确认完整私有载荷和冻结的 targetProof 后，读取并绑定原文件全量摘要与源身份。ABSENT 新建不伪造原件备份。

共用备份存储引擎的只读源适配器不改变 HDD 挂载、UUID、真实旋转盘、ext4/xfs、root 权限、容量保留、原子写入/fsync、全量复核规则。每个原件读取/复制块检查活实例与父目录；关键阶段重查保护注册表、Docker 配置/挂载和当前路径。捕获不创建、覆盖或删除容器路径，也不把 Staged 变成已发布。

容器捕获使用固定私有子目录 `container-files-v1`，与宿主历史隔离；元数据 schema 2、resourceType ContainerFile，包含容器/启动实例/路径、父版本、targetProof、完整上传指纹、源版本/对象/长度/SHA 和主体。双方误置的记录也不能跨域采用。已有宿主 schema 1 与其调用方式保持兼容。

Preparing 但没有完整 `.data` 的捕获保持 Incomplete，不覆盖重抄。完整 `.data` 且 Preparing 时，只能在重新取得当前权限与源证明后验证成 Verified；无自动发布。HDD 已完整但暂存回执还未记账时，同样验证并补记。明确丢弃私有上传不会删除已验证的 HDD 原件。是否异盘根据实际 st_dev 返回，不能将同盘留存称为灾备。

实机和失败边界见 [HDD 捕获验证](qa/CONTAINER_FILE_HDD_CAPTURE.md)。

## 仍须实现

1. HDD 捕获已有内部候选；原子发布与 Unknown 对账、公开历史和恢复、新建/覆盖计划及依赖风险仍待实现。当前 targetProof 已冻结目标配置，不能代替业务预检与当前授权。
2. 独立 RPC/capability、必要的受限 helper 权限、暂存/发布/状态/取消协议；不能将测试 root 能力当成产品沙箱证据。
3. EF 迁移、永久意图、Job/Outbox/锁/租约、当前权限与登录复核、取消/Unknown 对账及人类审计。
4. BFF 上传、文本编辑、历史/恢复 UI，以及普通用户拒绝、桌面/手机实际身份端到端验收。

内部引擎通过不构成完整容器文件管理或 59 项完成。执行证据见 [暂存验证](qa/CONTAINER_FILE_STAGING.md)。
