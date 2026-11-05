# 容器回收独立恢复内核候选：新journal、来源claim与迟到围栏

2026-11-06，候选da00d990，生产仍58b8174/Agent协议1.56。本批实现独立恢复底层，未安装、未增加专用RPC/SDK/业务/数据库/Worker/API/Vue入口，不签真实工作站恢复整链。承接[回收跨写入签收](CONTAINER_FILE_RECYCLING_CROSS_DIRECTORY.md)，完整恢复、长期清理及59项继续。

## 调研发现的前置缺口

现有低层Restore把原回收OperationId作为源引用，Context.JobId虽要求另一个ID，但Native输入不包含该Job ID，也没有独立恢复预检或恢复journal。旧Inspect/Discard都观察原回收记录，不能把它们包装成“新恢复任务已取消”的永久围栏；旧回收Plan针对现存源文件，也不能直接作为恢复前预检。直接接永久业务会留下取消/迟到调用和归因边界。

因此先实现内部whaledeck-container-recycle-restores.py库。它接收受信源回收完整引用及期望HDD摘要，恢复使用独立OperationId/owner/plan/journal，禁止与原回收ID相同。源引用仍是原OperationId/owner/完整计划；不重新调用Trash Apply、不伪改原业务历史。

## 已实现内核行为

恢复预检要求原Native记录为Trashed、HDD摘要匹配且完整验证、目标路径缺席、绑定私有目录和留存inode九字段/字节/属性吻合；再次校验原目标证明、执行UID/GID与父目录版本。返回计划深冻结源引用、HDD摘要、目标证明、父identity/version、执行身份、私有目录identity和留存identity，独立域SHA指纹绑定。预检不创建恢复journal或claim，不移动文件。

Apply先锁新恢复journal，再锁原回收来源；保存Preparing后，来源验证仍通过才写入并fsync来源claim，之后保存Applying并fsync，再允许原恢复原子NOREPLACE rename。claim以源回收ID索引，完整绑定来源、新恢复ID/owner/计划指纹；只有匹配claim的独立恢复才能修改该来源。同来源竞争、新旧入口均失败关闭，原回收/新恢复双锁顺序一致，容量及私有权限有界。

旧内核的restore_locked仅供受信新内核在持有原来源锁时复用；旧JSON请求不增加可控restore_id或路径。旧传输固定配置/var/lib/whaledeck-container-recycle-restores/claims，新旧进程共享该围栏。RESTORE_CLAIMED保持有界私有错误，Agent映射为FailedPrecondition，不泄漏任意异常细节。

独立Discard在新恢复ID下保存Aborted，原Trashed/inode/HDD不变，迟到Apply不能恢复；不把原回收Discard当作新恢复取消。Preparing或Applying已存在时只观察收敛，不重新执行rename。命名空间移动后丢回执，可凭匹配claim、原恢复结果与Applying恢复为Completed；无法证明归因、记录/claim丢失或目标不确定则Unknown，不能据原回收Restored冒认某个新任务完成。已知claim索引丢失时重新建立该来源围栏，但仍记录Unknown，不补造成功。

目标在预检后出现时不覆盖，保存独立无写围栏；Restoring之后出现原子冲突则Unknown并保留来源claim/原件。Unknown的restoredByThisOperation、sourceRetained/sourceRestored为null，不把已观察来源状态误作本次恢复成功或零修改。完成及中止记录组合严格验证，拒绝Aborted却声称Restored等损坏状态。

来源HDD仍复用原已验证完整备份，不为缺席目标伪造“覆盖前备份”；成功保持原inode、字节、权限、UID/GID、mtime和user属性，ctime允许rename改变。旧Native回收journal会按现有物理恢复语义变为Restored；原C#业务回收历史不得随之改写，后续独立业务须保留这一区别。

## 真实临时I/O与故障测试

新增23项内核测试，文件、journal、HDD格式数据、目录锁、原子交换和自有子进程均实际执行；ContainerRoot/运行实例存活、HDD设备与挂载证明是显式替身，不是Docker/物理HDD/RPC验收。原文件1,048,583字节，UID/GID2100:3100、0640、指定mtime及user属性，内容与inode恢复保持。

六个独立SIGKILL切点由原pidfd观察自有子进程，不操作生产helper：

| 切点 | 恢复后的结果 |
| --- | --- |
| Preparing已持久化、claim前 | Aborted，原Trash/HDD不变，迟到Apply不续做 |
| claim已持久化、Applying前 | Aborted，只释放本claim，不移动原件 |
| Applying已持久化、原恢复前 | Aborted，原件仍留存，迟到请求被围栏 |
| 原Restoring已记录、rename前 | Aborted，原来源收敛Trashed |
| 实际rename后、原回执前 | Completed，依原ID/claim观测归因，不再rename |
| 原来源Restored、独立完成回执前 | Completed，独立journal观测收敛 |

另有目标晚到/原子冲突、留存属性/父版本变化、HDD数据损坏、错误源owner/摘要、同ID/计划漂移、不同恢复竞争、另一配置过的旧引擎遵守claim、丢失journal或claim、unsafe权限及不一致记录负测。复核发现“配置的claims目录缺失仍按无claim处理”的边界，已改为STATE_UNSAFE并增测；未配置的旧内部测试实例兼容路径保留，但生产传输始终显式配置固定目录。

安装/校验声明补齐独立状态及claims目录root:root/0700、恢复内部库root:root/0750和符号链接拒绝，未增加CLI/权限入口或通用sudo规则。实际安装尚未执行，不以声明代替安装验收。

## 检查和生产保持

最终48项回收/恢复原生组、579项原生文件回归、93项现有回收验收工具、215项C#定向测试及8项范围通过；Python编译与安装/校验Shell语法通过。23项新测试已纳入现有CI test_container_file*.py发现入口，新候选远端CI终态尚未核验。

冻结候选后专属进程重新执行48项无跳过原生组，生产前后及另一进程独立重读与上批已签基线完全一致：2346保护文件、11核心healthy、回收Root28/HDD31、目录Root96及全部指定来源/恢复记录保持。生产/两宿主仍58b8174，独立恢复namespace未安装；没有创建生产资源、调用新RPC或真实恢复。

证据deploy/artifacts/container-recycle-restore-kernel-7166f5418efe449d8f0aa4b61488103b，共176文件（162份冻结验证/Host库/相关C#源码），EVIDENCE_SHA256.json摘要5d1eb88091e18ce22a2120b1e3456c248033c940cda3f12db567d426c72aa6d0，逐文件重算通过。unit whaledeck-qa-recycle-restore-kernel-9793edb31db9、Invocation acbc09e758494782ab6df1b0eaaab4e2准确Result=success/ExecMainStatus=0。

## 待实现边界

当前候选仅在原CID/原运行实例/原路径恢复留存inode，不支持显式新实例目标重绑定、丢失留存inode后从HDD重建或目录/完整元数据恢复；这些不被静默算成完成。内核不承担人类授权和全局数据库排他，后续须新增专用签名协议/SDK/受限传输及能力门禁、安装态分项，再实现C#独立请求/来源/计划/确认/执行/回执绑定、永久持久化/共享写入保护、Worker/API/Vue与真实身份/文件/HDD整链。Unknown处置、迟到/取消/重启等完整矩阵及长期清理继续。

整体25%–35%仍为粗估；整项0/59、完整阶段0/6、AI子计划0%与旧代码量快照保持，17项移出不算完成。
