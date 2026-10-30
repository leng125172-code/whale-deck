# 容器普通权限：内部引擎与真实HDD元数据快照

2026-10-30，普通模式底层候选 `9a49c9d`，冻结实机验证器修正 `996a25c`。当前产品仍 `a59ec97` / Agent 1.46；本轮未安装新helper、RPC、永久业务任务、API或Vue权限界面，不把内部引擎签为完整功能。59项继续、17项移出不算完成。

## 内部实现

`whaledeck-container-permissions.py` 只接已固定的ContainerRoot，绑定准确CID/启动实例/容器targetProof、父inode、公开9项版本、文件或目录身份、目标普通模式和用户扩展属性摘要。文件与目录都在原inode上非递归chmod，保留UID/GID、size/mtime与内容；目录子项不操作。相同模式为noChange，记录结果但不再次chmod/改变ctime。元数据操作不读取或复制文件内容，10GiB+7稀疏文件不受上传大小限制，测试检查inode/size/blocks保持；没有声称对该大文件做全内容校验。

仅接受普通 `0NNN` 模式。当前候选不提供chown、递归、ACL/xattr编辑或特殊位；现有特殊位、ACL与security.*属性明确拒绝，避免普通chmod静默改变ACL掩码或文件能力。普通user.*扩展属性只读取有界摘要，值不进入计划、回执或日志，修改后验证摘要保持。所有这些未实现能力仍保留在完整范围中。

## HDD前置元数据历史

不放宽通用FileBackups的命名空间。通过已有固定HDD配置和内核mount/UUID/rotational证明，进入独立固定 `container-permissions-v1` 私有目录；root:root0700，JSON0600、单链接，独立资源类型ContainerPermissionMetadata/摘要域和完整原计划。固定全局history.lock与容量/版本上限控制写入；满历史仍可验证原记录，不为每个失败请求遗留额外锁。

写入并重读校验旧权限元数据后才允许chmod。此处是权限元数据快照，**不是文件内容备份**，不把它冒称NVMe文件已完整复制到HDD。已有内容历史与备份能力另行保留，后续接永久业务目录/恢复策略时须继续区分。

## 结果与中断

Preparing/HistoryVerified/Prepared/Applying/ModeChanged可信测试切点均用真实子进程SIGKILL验证。恢复只观察原操作：修改前、原对象仍完整匹配时Aborted；修改后、同inode/普通身份与HDD证明匹配时Completed；源/父/实例变化、HDD历史丢失或外部模式变化时Unknown。Unknown不重新chmod，不自动恢复旧模式，也不清除历史。未开始的discard永久阻止晚到Apply；Completed回执不可变，外部变化只使bindingCurrent失效。

该内部库不是授权入口，仍要求上层新增专用签名契约、当前管理员与会话复核、永久意图/计划/确认、共享发布屏障、Worker/审计及Unknown处置后才可公开。

## 自动化与真实工作站

- 新26项实际Linux-root测试通过；包含5个真实SIGKILL切点、普通文件/非递归目录、2MiB+7全部字节/原inode/UID/GID/mtime、10GiB+7稀疏文件、no-op、真实POSIX ACL拒绝且不变、用户属性保留、只读/挂载根/硬链接/符号链接/FIFO/特殊位、HDD故障/篡改/容量/满历史、外部替换/目标丢失与原回执幂等。
- 关联 `test_container_file*.py` 共320项实际执行通过、零跳过。首轮只有一条测试断言过严：Apply拒绝陈旧源后合法操作锁仍存在；改为断言只有准确锁、无执行JSON/无HDD记录，不删除锁来掩盖并发风险。首轮失败日志保留。
- 冻结 `996a25c` 在独占无网络/只读根/非root容器，使用真实HDD bind与内核证明的NVMe命名卷。两边普通文件、含文件和空目录的目录共20次操作：16 Completed、4 Aborted，包含8次修改前后强杀。每份实机文件2,097,159字节全部读回相同，原inode/UID/GID/普通模式/纳秒mtime保持，目录子项完整9项指纹不变。
- 两边全部原模式通过独立新操作恢复，后续Root版本与结果严格连续。20份真实HDD元数据摘要与原计划一致；原Root回执与HDD历史保留。准确CID、HDD源及NVMe卷在全部内容和inode证明后清理，11核心ID/镜像/状态/StartedAt不变。
- 另一只读进程重新核对20个原回执/HDD原件/规范摘要、完整版本与模式时间线、两个设备、孩子指纹、准确清理与11核心健康；它不修改文件权限、不创建历史，不在数据清理后冒称再读已删除的物理字节。

实机成功证据 `deploy/artifacts/container-permissions-472ce826b0f34238882b3d05ee8d9a6d/` 的prepared-fixture/baseline/results/root-state、20份HDD元数据归档、physical-final/verified/independent-final。日志 `/tmp/wd-container-permissions-unit{,-second,-third}.log`、`/tmp/wd-container-permissions-related.log`、`/tmp/wd-container-permissions-actual-second.log`、`/tmp/wd-container-permissions-independent.log`。

## 首轮实机挂载保护拒绝

`255f697` 的测试卷误用wdv前缀，没有满足既有whaledeck-app-*白名单，被CONTAINER_UNSAFE_MOUNT在预检前拒绝；无权限计划、无Root执行回执、无HDD元数据、未chmod。未放宽产品挂载保护，改正夹具命名，并将准备后的完整inode指纹提前保存再重跑。

原失败证据 `container-permissions-eaeb42a2e09948d0bf56526858ec6dc3` 与 `/tmp/wd-container-permissions-actual.log` 保留。失败容器已准确移除；对留下的准确HDD源/NVMe卷，另外复核原验证器可重建的全部合成字节、严格路径/标签/driver/无引用、UID/GID/模式/纳秒mtime/无扩展属性和当前完整库存，再精确清理。cleanup-proof-before与cleanup-verified明确原门禁仍失败，不冒称曾保存创建时inode基线，也不追改为原门禁通过。

## 下一步

接入C#强类型专用RPC和完整请求签名、权限/版本门禁、固定最小权限helper；再接EF永久业务、共享发布屏障、当前管理员二次授权、Worker/任务/审计、API与Vue确认/历史/异常界面并实机验收。继续所有者变更、递归/ACL/扩展属性、容器删除、历史策略与用户/资源隔离，完整P1-11/P2-03仍部分实现。
