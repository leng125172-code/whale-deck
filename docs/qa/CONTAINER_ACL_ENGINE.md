# 容器POSIX ACL：底层候选、Linux语义与真实HDD验证

后续更新（2026-11-03）：6a62027独立1.51四RPC/SDK/固定传输及初始namespace/准确FD非IDMAP检查、安装验证候选已通过分层测试，仍未安装/未真实签名RPC，见[后续RPC候选](CONTAINER_ACL_RPC.md)。以下ed50dc1底层/实盘正文逐字保留，其“尚无协议”属于当批状态；未重复已完成实盘操作。

2026-11-02候选实现及实盘；2026-11-03记录收尾。新增独立底层候选`ed50dc1a9083d490aa332e63461e9b9f01b839a7`，未安装、无签名RPC/永久业务/Worker/API/Vue。运行仍`b1fafed`/Agent1.50，不发布/回滚，已有正常权限/所有者/递归模块的ACL拒绝边界保持。59项仍部分实现，不能将本批记作完整ACL或页面验收。

## 实际实现及依赖

新增`deploy/host/whaledeck-container-acl.py`：显式替换单一对象的access或default POSIX ACL；每次只写一种，另一种原ACL必须保持，default仅目录可用，空default显式清除继承；access缩减为显式三条基础项可删除named扩展。不递归、不改UID/GID，不调用容器shell、外部setfacl或任意宿主路径。

严格限定user_obj/user/group_obj/group/mask/other、数值ID（0有效、非省略值）、0–7权限、64项预算、唯一且规范顺序；named项必须显式mask，不自动扩大有效权限。mask是group mode bits并限制named用户/组的有效权限，原named权限与effective权限不能混为一项。数字ID为宿主内核文件系统观察到的ID，不自动当作Authentik Subject或推断容器用户名；user namespace/idmapped映射及展示仍需在安装/业务接入前明确处理和验收。

固定FD上的`getxattr/setxattr/removexattr`直接使用Linux UAPI version2小端POSIX ACL格式，实际核对本机`/usr/include/linux/posix_acl{,_xattr}.h`及[Linux UAPI源码](https://github.com/torvalds/linux/blob/master/include/uapi/linux/posix_acl_xattr.h)、[内核ACL实现](https://github.com/torvalds/linux/blob/master/fs/posix_acl.c)。不引入libacl运行时依赖；候选实机独立文本观察使用已存在的`/usr/bin/getfacl`，未安装软件包。ENODATA仅表示无该ACL，ENOTSUP/EACCES/EIO等不伪装为空ACL；实际文件系统必须支持POSIX ACL/xattr。当前只有Linux POSIX ACL，不把NFSv4/Windows ACL、security属性或通用xattr编辑算作已实现。

复用固定ContainerRoot/原实例/核心保护/路径与可写挂载边界、原inode/完整九字段版本和父身份；硬链接、符号链接、特殊文件/特殊模式、安全属性及预算异常拒绝。预检绑定原access/default ACL、普通属性摘要、目标模式和noChange；ACL引起的普通mode变化显式入计划。独立plan/history域、`container-acl-v1` HDD命名空间和`ContainerAclMetadata`保存原ACL/身份，普通权限历史不混用；这不是文件内容备份。

永久候选journal采用Preparing→Prepared→Applying→Completed/Aborted/Unknown。写前先验证HDD历史，写入和确认之间再次核对原对象；恢复只观察，不重复ACL系统调用或自动回滚。缺Root但遗留HDD、历史丢失、外部ACL/对象交换等均保留Unknown。历史Completed不代表当前绑定/属性仍有效，迟到重放不能重新赋权。

复核发现原实现思路可能在ACL已可见但fsync失败时误记Completed，已在冻结前修正：恢复确认目标ACL后，须对准确原inode执行fsync成功才完成；失败保留Unknown。恢复fsync不重新写ACL。新增`AclWritten`切点与持续EIO注入覆盖该边界，不以当前可见代替持久证明。

## 隔离Linux与实际内核语义

22项新专项全部通过，纳入当前容器文件全套**456项Linux-root测试、0跳过**；Python -W error、范围8项及diff通过，CI显式编译新模块与两验收器，原`test_container_file*.py`入口覆盖专项。

- 实际fd ACL写入/清除、默认继承、64项边界及普通属性/完整2097159字节/原inode/owner/mtime保留。
- fork后真正setuid/setgid/setgroups，验证named用户与supplementary named组、mask拒绝写而允许读；不是仅断言编码或mock权限结果。`getfacl`数字输出交叉检查mask有效权限。
- 六个真实SIGKILL切点（Preparing/HistoryVerified/Prepared/Applying/AclWritten/AclChanged），写前Aborted、写后恢复Completed；inspect/apply/discard不重复写入。
- 非目标ACL外部变化、源交换、历史失败/丢失、Root丢失、持续源fsync EIO、noChange/ctime、旧完成后外部变化，以及畸形/重复/缺mask/boolean/越界/额外字段拒绝。

隔离测试使用真实Linux临时inode，但mount/udev与ContainerRoot明确是夹具；不是物理HDD、Docker、RPC、业务权限或页面证据。之前454项结果发生在再增加两个专项前，最新456项是新全套输出，不重复相加。

## 冻结候选的真实容器/HDD

私有目录`deploy/artifacts/container-acl-4c582461897b44a7ac89b91a15879947/`，主门禁和独立只读首次均Passed。9个宿主模块按冻结Git blob/SHA核对，在root私有临时bundle加载；候选Root-state单独0700，未改已安装helper/权限配置或生产业务schema。

唯一新容器无网络、只读根、1000:1000执行身份、唯一准确自有HDD bind、CPU/内存/PID预算。实际ContainerRoot/挂载注册和原instance核对，原文件及目录中现有文件各完整2097159字节、普通user属性、2100:3100、纳秒mtime。实际HDD经现有UUID/mount/udev/sysfs策略attest，不替换依赖或伪造回执。

| 操作 | 实际结果 |
| --- | --- |
| access添加named用户、相同ACL no-op、mask改为6、缩减基础ACL | 4 Completed；no-op九字段/ctime不变，mask对应模式改变 |
| 目录default添加、清除 | 2 Completed；access ACL/模式与既有子项保持 |
| 执行前discard | Aborted、未写ACL/未捕获HDD历史 |
| 准确候选子进程Applying切点SIGKILL | Aborted；源完整不变，无续做 |
| 准确候选子进程AclWritten切点SIGKILL | Completed；原ACL已经写入、恢复同步并观察原结果，不重写 |

共**9操作、7完成/2中止、2次候选真实SIGKILL、8份HDD原ACL元数据**。这两次信号针对本轮候选子进程，不是已装Agent/Worker故障或产品钩子。每次原计划/Root和HDD、完整文件/普通属性/非目标ACL/父与实例核对，inspect/apply/discard重放前后原Root字节与全树事实保持。最终getfacl实际显示`user:2201:rw-`受`mask::r--`限为effective `r--`，目录default已清除。

证明完整后只unlink两准确已核文件、rmdir准确空目录；原fixture准确CID和目录最终清理，永久候选Root/HDD原件保留。独立进程重读冻结源、9 Root、8份attest HDD、原计划/五态结果/版本与SHA、完整物理证明及准确清理；不声称清理后重读已删源字节。源/HDDdevice均2049，仅同设备元数据留存，不算异盘灾备或NVMe覆盖。

11核心ID/镜像/运行状态/StartedAt不变且healthy，实际Agent PID2542378、MaintenanceHost PID2380322/b1fafed来源保持，活动工作检查0。没有人类登录/模型请求/真实通知/主机重启/磁盘填满；用户NOPASSWD和密码未改，产品Agent最小权限未扩大。

原件目录root/0700、文件0600，含baseline/9模块SHA、每轮results/完整物理指纹/ACL、root-state、8份HDD元数据副本、getfacl-final、verified及independent-final；不入Git。日志`/tmp/wd-acl-native-{first,independent}.log`、`/tmp/wd-acl-linux-final.log`保留，无凭据。

下一步接C#强类型协议/SDK、完整方法与内容签名/短时单次capability、受限固定传输及最小宿主授权/安装验证，完成真实安装态RPC/HDD与拒绝兼容；再永久SQL/共享写入保护/Worker/API/Vue及真实Authentik页面与故障矩阵。ACL递归/历史恢复、其他未验属性策略、可恢复删除/长期清理、P0-05和其余59项/UI/C#酒馆/统一供应商/AI Live2D继续，17移出不算完成，master未交付。

记录检查：220份Markdown/1331处相对链接零缺失；范围8项与diff通过。旧递归所有者/Unknown/撤权QA正文未改写，规划完成比例与代码量估算不因本批测试数量自动上调。
