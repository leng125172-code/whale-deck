# 归档Prepared：写后、发布前停止点候选

2026-11-07，QA `dd6ec60ee0908e018e0597d7e343df579ca607fc` 新增Prepared专用观察器和13项检查。承接[Reserved重启/自然租约](CONTAINER_FILE_RECYCLING_ARCHIVE_RESTARTS.md)，本批仅完成候选、隔离临时真实文件/HDD及自有子进程验证，未向安装态helper发送信号、未新增业务任务或部署，不签真实写后中断、Unknown页面或管理员处置。

## 切点语义

Native copy顺序为Applying→创建并绑定stage/output→Copying→写完整字节、恢复uid/gid/mode/xattr/mtime、fsync(file/private)并核验→Prepared及preparedIdentity持久化→复核→Publishing→NOREPLACE发布→Published→关闭archive/parent/private/output上下文→binding核验→Completed。

因此本候选只接受Prepared：暂存的新inode已完整写入、属性和SHA256核验且fsync完成，但目标仍缺席、publishedIdentity为空。不能把Copying中的部分内容当完整复制，也不能把Publishing、Published或Completed当作发布前切点。中断Prepared后内核reconcile保持Unknown、claim及原暂存，不重复copy/rename/清理；这不等于复制成功或零写入取消。

## 八FD与停止后证明

SQL原意图/分域执行ID、source purge执行域、完整计划、owner、容器实例和固定work/recycle-rpc.bin由已有严格record_for关联。只有本轮自有fixture目录、固定安装态apply argv、root UID、MainPID/InvocationID/cgroup和原pidfd，才能进入观察。

在原五FD（容器根、归档锁、purge锁、source锁、当前HDD数据）之外，增加目标父目录、已绑定stage目录和新output inode三项；八个内核身份必须互异且都存在于原进程FD集合。初次绑定在写入前后均可观察，只有Root实际Prepared及八FD均匹配才发送STOP。停止后读取实际T/t、重读原Root并重新核全部绑定与物理证明，最终HDD数据/元数据attestation完成后才交出句柄。

Prepared证明要求：

- source/purge/marker三个Root结构、Purged/Completed状态、操作/owner/计划和摘要不变，共享claim精确属于当前归档。
- 目标、原retained/purging名称缺席；stage与其唯一copy.partial、preparedIdentity/pendingIdentity和实际当前inode相等，新inode不同于原inode及HDD inode。
- 文件完整大小、SHA256、uid/gid/mode/link count/mtime及用户属性匹配原来源；publishedIdentity仍null。
- 当前父目录身份及独立重算的parentAfterStage版本在检查前后均一致。目录名字仅检查固定QA白名单，不声称冻结完整before entries或证明original.bin内容；外层真实门禁仍须保留fixture原始文件与全局基线证明。

任何停后证明失败、阶段已越界或最终attestation失败，都只终止已精确绑定的原pidfd并关闭句柄；HeldArchive清理也绝不SIGCONT恢复Apply。错过窗口不重放，不把脚本清理当产品中断成功。没有CLI、任意PID/路径入口或产品hook。

## 检查与边界

13项专项以ResourceWarning为错误通过：实际临时内核到Prepared并读完整暂存字节/属性/identity；原source/purge/HDD保持；非Prepared拒绝；prepared/pending/published/parent版本变更拒绝；目标抢占、暂存内容/xattr变化、额外目录项、来源/claim变化拒绝；Unknown下inspect/discard/apply均不重写或发布。另验证八个互异FD、缺任一FD或错误argv拒绝、外来夹具在storage/pidfd/信号前拒绝、实际不相关sleep即便持全部FD也不能冒充；最终证明失败/阶段竞态/成功交出后清理用实际自有sleep pidfd验证无CONT且关闭描述符。

Proc/unit和成功停止点的正向过程绑定是明确替身；可信checkpoint只用于隔离临时原生内核测试，不注入已安装helper。数据/fsync/Unknown行为是真实临时Linux I/O，自有sleep的STOP/KILL是真实信号；这些不能代替安装态RPC、浏览器身份、实际Unknown持久化/处置及故障后HDD独立重读。

完整回收集539项root测试零跳过（31.710秒），范围8项、CI合同14项及2份Python编译通过。现有CI通配test_container_recycle*.py已覆盖本批；新QA远端CI完整终态尚未签收。

## 生产保持与封存

当前两次只读基线相等，且与上轮Agent验收最终基线完全相等；四类业务全行hash保持，仍64回收/29恢复/37清理/18归档、活动0。Root/HDD160/151、恢复82、目录98、purge62、archive22及f318d9ab/21安装模块/2346保护/11核心保持。没有服务控制、安装更新、业务写入或旧Apply重放，user NOPASSWD未变。

root目录0700/文件0600私有证据 `container-recycle-archive-prepared-candidate-e5067b832672464599f26d12063dde53`：123文件及manifest，含117冻结验证源码、检查结果、当前双基线与业务hash；manifest SHA256 `30dd9d5c459e63eafebf1bb7aa8f1605efd19ded7470609b42fd766c97ea058d`。

下一步将候选接入全新资源的真实身份/页面写后中断，独立核Unknown/null事实、原claim/暂存和HDD，管理员只能明确处置原结果，不能自动恢复或重新Apply；收尾另须精确暂存inode与目录证明，不能调用只接受Completed的普通新副本cleanup冒充Unknown恢复。继续写后/Unknown其他组合、来源/跨写入、claim/长期策略/维护连续性及59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
