# Publishing且命名空间已发布：完成回执前停止点候选

2026-11-07，QA `783114bd66c7cb585424bcffbb7c0a252d690a6b` 新增发布后观察器及14专项，承接[Prepared写后重启](CONTAINER_FILE_RECYCLING_ARCHIVE_PREPARED_RESTARTS.md)。本批仅为候选和隔离真实临时I/O/自有子进程检查，未向安装态helper发信号、未新增业务或部署，不签真实页面发布后Unknown/管理员处置。

## 实际切点与边界

内核先保存Publishing，再调用`whaledeck-files.exchange(..., flags=1)`；该函数仅包装renameat2，flags=1是RENAME_NOREPLACE，不是RENAME_EXCHANGE。随后有PublishedNamespace测试checkpoint、目录fsync、完整目标校验/目录枚举，再保存Published和publishedIdentity，最后关闭FD并核binding、保存Completed。

本候选要求Root**仍Publishing且publishedIdentity为空**，但目标已实际存在且是原pending/output inode，stage仍存在而copy.partial缺席。它证明的是命名空间已发布、完整新文件可见，不能从该Root状态和文件存在推导parent/private fsync已经完成，也不能称Published/Completed持久回执已落盘。当前Published窗口随后很快关闭FD，因此不靠它作八FD停止点，不增加产品hook或改已安装helper来取得证据。

## 原进程与物理证明

仅接受固定自有fixture、SQL原意图/分域执行ID、完整计划/owner、容器实例、固定work/recycle-rpc.bin及安装态apply argv。原MainPID/InvocationID/cgroup、root UID和pidfd先绑定；只有Root Publishing、廉价lstat预筛显示目标为pending inode、原八个FD均在该进程且互异，才允许STOP：容器根、archive/purge/source三锁、当前HDD数据、目标parent、stage及output。

停止后重读Root并重新完成全部证明：

- source/purge/marker仍Purged/Completed/Purged，操作/owner/计划及三份摘要不变，共享claim属于原归档；HDD数据与历史元数据由attested只读FD读证且交出句柄前再次attest。
- 目标完整frame前8项匹配preparedIdentity、ctime不回退，inode等于pending且不同于原inode/HDD inode；nlink1，完整uid/gid/mode/mtime/size/xattrs及SHA256匹配来源和计划。
- target已存在、old retained/purging缺席、stage身份等于privateIdentity且staged copy缺席，目录内容由读取器限定为空。
- 父目录身份不变；发布后的独立版本不同于历史parentAfterStage，且停止期间前后重算一致。QA名字仅做固定白名单，不声称完整before entries或original.bin内容未变；外层真实门禁仍须独立原文件/全局基线保护。

若仍是Publishing但rename尚未发生，目标缺席且完整stage还在，不能算发布后。若已经Published/Completed/Unknown/Aborted，或暂停后阶段/身份竞态，候选miss；不重放Apply。任何已STOP的证明/attestation失败仅经精确原pidfd终止并等退出，交出的HeldArchive清理同样不SIGCONT，不留下暂停破坏性进程。没有CLI或任意PID/路径入口。

## 检查与保持

14专项通过：临时真实内核在NOREPLACE后读完整目标/属性与空stage；Publishing前rename反例、非相关/持久终态拒绝；pending/prepared/published身份与ctime类型/范围拒绝；目标替换/内容/xattr/硬链接、来源/claim变化、未知目录项拒绝；Unknown后inspect/discard/apply保持已发布目标和原记录，不重写/再发布。八FD、错argv/缺任意FD、无关实际sleep持FD也拒绝、外来fixture在storage/pidfd前拒绝，以及实际自有sleep在attest失败/阶段竞态/成功交出后不CONT且关闭pidfd均通过。

Proc/unit成功绑定是明确替身，可信checkpoint仅在隔离临时原生内核测试使用，信号仅指向测试创建的sleep；本批不能代替安装态transport、真实UI/身份/Unknown持久化与处置或跨重启证明。测试的PublishedNamespace checkpoint紧贴rename且在目录fsync之前，正是`directoryFsyncProven=false`必须保留的原因。

完整576项回收root检查零跳过（36.316秒），包含14专项；8范围、14 CI合同及2份Python编译通过。现有CI通配已覆盖，新QA远端完整CI终态未签收。

当前两次只读基线相等，且与上批Prepared Agent最后基线一致；四类业务全行hash保持，仍67回收/29恢复/43清理/24归档、活动0。Root/HDD169/157、恢复85、目录98、purge68、archive28，2346保护/11核心/21安装模块/f318d9ab及user NOPASSWD保持，无服务控制或业务写入。

root目录0700/文件0600私有证据 `container-recycle-archive-published-candidate-e0eff289b533464eac7b2cfe78a8a760`：124文件及manifest，含118冻结验证源码、检查和双基线/业务hash；manifest SHA256 `f04f94e7b00a7a18526498dc68c26f80a595fcc62c6593630604771a485a7ad0`。

下一步接全新资源真实发布后中断，独立核目标仍属原pending inode/Root Unknown/复制null和HDD、管理员唯一处置及准确目标/空stage收尾；不能用Prepared-only cleanup删除已发布文件，也不能拿当前文件存在冒充历史Completed。继续其余发布/绑定不确定性、写后权限/退出、来源/跨写入、claim/长期策略/维护连续性、大文件稳定性及59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
