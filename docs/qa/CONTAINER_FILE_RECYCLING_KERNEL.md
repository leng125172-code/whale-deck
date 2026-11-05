# 容器普通文件可恢复删除内核候选

2026-11-05，承接[文件管理与下一切片](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_NOTE_VISIBILITY.md)。本批实现内部普通文件Trash捕获、原inode私有留存、空缺原路径恢复、幂等与中断判定。新模块尚未安装，不提供CLI/RPC或BFF按钮；签名协议/SDK、最小helper授权、永久业务/共享写入保护、Worker和Vue仍待接入，不计完整删除功能已交付。

## 已实现的内核

`whaledeck-container-recycling.py`复用现有ContainerRoot、上传目标证明、目录FD/状态锁和HDD配置证明，独立使用`container-recycling-v1`命名空间。计划绑定容器ID/实例、namespace/root/mount证明、父目录identity/version、执行UID/GID、完整9字段源identity、内容SHA256及有界普通user属性；无宿主rootId别名。仅普通单链接文件，拒绝setid、目录、符号/硬链接、ACL、安全及其他非user扩展元数据，不能静默丢失它们。普通属性最多32项/4096字节。

删除前先完成实际HDD容量/设备证明和全量内容捕获，专属`ContainerFileTrash/Trash`元数据与计划绑定，不采用Overwrite证明。Preparing/Verified及Pending/Trashed/Restored/Canceled状态分开，完整数据和摘要通过后才允许源命名空间变更；部分文件或丢失Root的孤儿历史不重新复制或接管，保留为不确定结果。备份缺失/损坏不能继续删除或恢复。

原件以父目录FD和RENAME_NOREPLACE移入同一挂载下root-only私有目录，记录private inode，保留原UID/GID/mode/mtime、普通属性和原inode；不unlink源数据。首次恢复仅把该已验证留存inode移回空缺原路径，目标占用时拒绝覆盖。HDD重新构建丢失的私有原件、目录/ACL完整元数据、覆盖恢复与永久清理尚未实现，不能据此签收。

Root journal、锁、private与HDD记录均做类型/权限/owner/inode/摘要校验。中断恢复只观察源/留存/HDD既有事实，分类Aborted/Trashed/Restored/Unknown，不再次移动、复制或自动回滚。Unknown的`sourceRetained/sourceRestored`为null（未证明），不是false；当前绑定可用性与历史事实分别表达。重复Apply/Restore不再次变更同一原操作。

## 真实工作站候选证据：两个不同场景

| 场景 | 实际源码与回执 |
| --- | --- |
| 正常回收→新Root session→空缺原路径恢复及幂等 | `0821ab604148fe05804b6f9e5adf8e80569581d5`，Invocation `5c935368cf214ea896ade3d80e7bc710` |
| 恢复检查点后目标晚到→不覆盖/不续做→Unknown未证明标志 | `6e7fe460b9365ed9ea0f75da4d7bc4501858092d`，Invocation `c06fd0ed735e4f33932cb41dad9b02b4` |

两次均为新自有容器/合成文件、真实pidfd/root namespace、实际挂载HDD/UUID/rotational及容量证明；捕获完整1MiB+7字节与原属性。正常场景原inode、UID/GID/mode/mtime和user属性恢复，Root session独立重开后仍验证成功；重试未再移动。冲突场景通过可信候选检查点只在该自有路径创建晚到文件，内核不覆盖它，原数据继续留存，HDD保持Trashed；Unknown的两个源事实为null，再次恢复被拒绝。

两次unit都准确exited/ExecMainCode=1/ExecMainStatus=0/Result=success，原生产前后快照相同，精确核对后清理各自合成文件、空私有目录、源目录及容器，保留HDD捕获和候选journal；不是产品自动删除旧回收站。没有安装/签名RPC、真实人类身份、永久业务或页面证据，也不是实际容器中强杀恢复验收。两个场景`differentSourceDevice=false`，只证明实际HDD捕获，不证明NVMe/HDD跨盘隔离。

正常证据`container-recycling-369a84b8f34c4ecb95ce22ce6f15dc7e`封存37文件，清单摘要`f5e30280666610b004874bc3edd9f337fd8569e023151f29e4176ff9dbbe97b3`。冲突证据`container-recycling-0627fc616bab4f99b16e2300b6bdaec6`封存37文件，清单摘要`9071787a8edb1420597492f76b10fbbf49e0c1d9da0481d841bf0053e903ec3a`。两份原件不合并为同一版本的全功能通过，也没有重放正常写入来补签冲突。

## 最新加固、测试与中断后的只读复核

`47cdc26`在恢复发布前增加实际执行UID/GID与重新打开的父目录、私有目录及留存原件复验，防止检查点后执行者/目录变化授权移动。实现批次关联510项容器文件工具通过、零跳过，含17项新回收专项；会话恢复后17专项再次通过。源文件/备份是真实临时文件，namespace/liveness/udev/mount为显式夹具，不冒充实际Docker或物理HDD。

专项包含真实测试子进程SIGKILL：Preparing、HistoryVerified、Retaining、Retained、Restoring及RestoredNamespace观察恢复；另验容量不足、损坏数据、原件改变/孤儿历史、链接/目录/真实POSIX ACL拒绝、目标晚到、错误owner/instance/计划/保护路径，以及执行者和私有目录变化拒绝。测试只杀自己创建并用pidfd绑定的子进程，有绝对等待上限。首轮12个夹具错误源于固定PID777不存在，改为实际自有测试进程PID后通过，未放宽产品绑定；原始未捕获stderr不伪造。

最新`47cdc26`的执行者/私有目录拒绝尚未做实际容器变更验收。为避免重做已完成操作，新增冻结`f4c4e56536c99ad23ded5cee9b5cb4837c09591c`只读复核，以最新内核读取两个已清理场景的既有Root/HDD；分别得到历史Restored和Unknown，HDD字节/原封存不变、当前绑定false、Unknown源事实null。没有重建旧容器或再次Trash/Restore，不能由此推导最新加固已通过实机写入。

只读证据`container-recycling-readback-9294ea85a7764f4eb513440c16fa6f42`封存23文件，摘要`52f62c4034d467ba81c6a60345a95e988f71ec8ea9669ac55c7b97b0fc010752`。原生产2346保护文件、来源Root1964、恢复Root257/HDD169、11核心/两宿主及普通62/56/118、恢复24意图19执行/活动0保持。运行产品仍34153d2，没有发布覆盖或扩大产品Agent权限。

## 下一步

接入专属签名协议/SDK/受限传输与能力版本，实际安装验证；随后永久意图/幂等/租约、跨写入围栏、失败处置与实时权限、API/Worker/Vue和真实身份整链。最新加固实机拒绝、HDD重建、ACL/目录/覆盖恢复、清理/保留策略及其他故障组合继续保留。当前59项及阶段完整签收、整体25%–35%粗估、AI子计划0%不变。

收尾：范围8项、本地Markdown链接2104/缺失0、diff及37/37/23文件封存再核验通过，两Git索引仍归用户所有。项目图谱事件whale-deck.container-recycle.f4c4e56.kernel-native-readback取得projectId/key/eventId匹配、duplicate=false回执，pending移至synced；container-recycle阶段active，不将候选分项提升为整阶段完成。本批未取得新候选整条CI终态，不报CI全成功。
