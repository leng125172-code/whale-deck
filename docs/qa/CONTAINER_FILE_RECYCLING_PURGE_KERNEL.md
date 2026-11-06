# 容器留存原件清理内核候选

2026-11-06，`73563cf0ac70791f5eabf11d3a5acf2855a29b3f` 在[预留围栏](CONTAINER_FILE_RECYCLING_PURGE_FENCES.md)上增加 `Originals` 实际清理内核。仅处理已预检且明确预留的普通文件留存原件，保留HDD数据/历史；真实自有容器/HDD分项通过。未安装、未接签名RPC/永久业务/Worker/API/Vue/Jobs、留存期限或自动调度，不能计长期清理完整交付。

## 删除与持久状态

锁顺序保持清理journal → 原来源journal → HDD gate。Apply要求已有匹配Reserved回执；不隐式预留，不接受调用方路径。先在来源锁内重新核对完整预检计划，再持有实际HDD gate和已打开备份FD，复核历史/配置摘要、目录身份、备份完整inode/字节/SHA，直到结束仍验证配置及FD/路径身份。

持久保存Applying、来源Deleting围栏及原来源Purging后，才将 `<sourceId>.trash` 原子NOREPLACE移至同一已验证私有目录内的 `<purgeId>.purging`。保存Quarantined身份、再次核对当前容器/父目录/私有目录、实际原件字节/属性/九字段身份、原目标仍缺席和HDD证明，然后持久保存Removing。持续打开待删除原件FD，最后重核后仅unlink该名称并fsync私有目录；通过原FD确认同inode链接数归零、内容/大小/属性保持，且原名称/临时名称/原目标均缺席，才保存Removed及unlinkConfirmed。

随后只将本来源HDD历史的captureState改为Purged，保存来源Purged、保留Purged围栏；完成所有context退出及备份身份复核后，最后保存本清理Completed。HDD `.data` 没有删除或替换，历史摘要保持，旧原生/独立留存inode恢复不能重新返回已清理原件。当前并未实现清理后的HDD归档重新恢复业务，后续须提供独立授权/计划/恢复链，不能把现有留存inode恢复称为归档恢复。

私有目录的可信权限、来源锁和未来共享业务围栏是修改隔离边界；不声称能阻止另一个具有宿主root权限的写入者在任意时刻篡改文件。原子隔离后发生替换、冲突或目标变化则拒绝并保留Unknown，不删除已发现的外来内容。文件名移除不等于安全擦除，也不保证其他进程持有的FD立即释放物理空间。

## 中断只观察

Apply一旦中断于Applying至Purged围栏之间，再次Apply/Inspect/Discard只保存Unknown和保护，不重做rename/unlink，不把“名称缺席”当作成功或零修改。即使Removed已记录unlinkConfirmed，而后续HDD元数据写入失败，也不签完整成功；Unknown的purgeApplied为null。只有已有Completed及匹配Purged围栏才报告true，重复Apply/观察/取消不再删除。Aborted/取消后的请求保持false，不可重新预留并执行。

记录丢失、半写入或围栏矛盾不重建成功；在不覆盖其他活动ID的前提下重建Unknown保护。来源Purging在原观察路径中可退为Unknown，但没有恢复删除动作。Completed回执丢失或Purged围栏丢失也降为Unknown保护，原Purged来源/HDD事实保留。Unknown明确处置、不可访问实例等安装态故障整链尚待后续业务实现。

## 检查及准备失败

16项专项包括实际删除/邻接文件保持、HDD数据inode与完整元数据保持、完整Purged状态、无预留/取消后拒绝、成功重放、备份损坏、恢复claim争用、NOREPLACE冲突、移位替换/外来目标保持、丢记录/围栏、旧围栏读者拒绝新阶段、unlink/fsync/历史元数据写入故障、无session及原件属性变化。12个真实自有子进程SIGKILL切点覆盖Applying、DeletingFenced、SourcePurging、QuarantinedNamespace、Quarantined、Removing、Unlinked、Removed、HistoryPurged、SourcePurged、PurgedFenced及Completed。新建内核对象从磁盘重读，未完成阶段禁止负载unlink，并核对残留原件/备份保持；这不是安装态进程冷启动验收。

16专项与关联共109项Linux root检查通过；回收相关206项零跳过通过，14项前端CI合同、8项范围和修改Python文件语法通过。临时测试的容器命名空间/HDD证明使用显式替身，实际临时文件IO及强杀与下面真实Docker/HDD分列。

初次86项运行的11个切点子用例失败，是全局unlink mock误拦回执保存器对已改名临时文件的收尾；后仅允许两个本轮journal目录中精确生成的临时名收尾，负载unlink仍禁止，磁盘快照保持断言未移除。未保存的原失败源码/日志没有补造，准备事实单独记录。

## 真实自有容器/HDD分项

unit `whaledeck-qa-purge-kernel-8e26816c`，Invocation `f35bdbe32c1e424586d761cd9b9bc487`，exited/Result success/ExecMainCode1/ExecMainStatus0。运行前冻结13宿主模块，在本批私有来源/恢复/清理状态目录操作全新固定镜像自有容器；没有替换安装态Agent或生产状态目录。

新建1,048,583字节、1000:1000/0640及扩展属性文件，原生回收后形成Trashed来源和实际配置HDD备份。完整预检/Reserved保持两份负载；实际Apply返回Completed/purgeApplied=true/ownedFence=true，留存名、临时名及原路径缺席。HDD数据的完整身份/字节/属性保持，历史仅captureState变为Purged，来源Purged/actualIdentity=null、本清理Completed/unlinkConfirmed及Purged围栏全部核对。重复Apply/Inspect/Discard回执保持；旧原生恢复明确拒绝。

成功后只移除已验证为空且inode匹配的本轮私有目录，再由既有严格fixture清理器移除自有容器及种子目录。这里实际删除的是本轮测试原件，备份和私有记录保留；不是在生产用户数据上启用清理。未另跑独立live/final验证进程，实机证明来自已记录的验证程序；封存时另做当前基线、完整原业务行摘要与活动状态复核。

生产仍093f44a9，两宿主、2346保护文件、11核心及36回收/22独立恢复完整业务行保持，活动0。生产回收Root72、独立恢复Root59、目录Root98保持；HDD79→81仅增加本轮 `.json/.data`，旧HDD身份及内容保持。源/HDD仍同设备2049，不称独立物理盘灾备。

私有证据 `deploy/artifacts/container-recycle-purges-286c8d2372ed4c4aa5bb35da3d7aefbd` 封存136文件（另有清单），清单SHA256 `3383d70c91090cd78e3bc8b5386bce5d68ddcbefdb6abdbaadb2da9e5347f9a7`。13宿主模块运行前冻结；106份验证源码闭包在运行后从准确提交保存并核对未变工作树，不能称全部运行前已冻结。目录root:root/0700、文件root:root/0600；资源身份/私有证据/凭据不上传项目图谱。

## 后续门禁

继续专用签名协议/SDK/原生探针、同步安装与旧能力拒绝、新生命周期兼容、永久意图/租约/共享写入保护/审计、管理页面及真实身份故障整链，再补可配置留存与清理后的归档恢复。Purging/Purged来源和Purged历史是新生命周期，旧安装态读者不认识，未同步升级前不得往生产来源写入这些状态。HDD历史清理/递归目录清理不属于本内核，须另行明确依赖与确认。59项与17项移出边界不变，整项0/59、阶段0/6、AI功能0%保持，master完整交付未完成。
