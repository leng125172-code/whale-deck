# 已返回原件与当前绑定：预构建及后续故障验收边界

2026-11-06。本批完成093f44a9修复版本的预构建核验，并补充写后结果的隔离检查；没有覆盖生产或运行新的真实身份故障场景。

## 修复版本预构建

固定目标 `093f44a9c2b97cb164f655d65280fbafcd899ca6`，准确CI `42135680358`。镜像unit `whaledeck-qa-footer-bootstrap-images-45e7b915` / Invocation `e70b665bab3342f5be5b8b11cddaa0f3`，宿主unit `whaledeck-qa-footer-bootstrap-hosts-45e7b915` / Invocation `d75679409f3d40d7ae0e588a6fe802c6`，均exited/Result success/ExecMainCode 1/ExecMainStatus 0。

4镜像实际来源标签及隔离只读、无网络build-info、2宿主apphost来源均匹配目标，707宿主文件SHA校验及自包含10.0.13运行时检查通过。生产仍为150adfd，2346保护文件、11核心实际身份、旧29回收/15恢复完整行与Root/HDD快照保持，活动0。

当前恢复入口为 `deploy/artifacts/container-recycle-footer-bootstrap-release-45e7b91538444d94b9efaa22aec6d9a7/request.json`，stage为prebuild-verified-awaiting-ci。14份不可变预构建检查点清单摘要 `9023c304c44cf821177af07e9d6f5cb5480d8a8a8c568ef52be0de703122ae00`，实际逐文件复核通过。首次页尾发布的真实ElLink失败继续保留，不重放原发布或补签原页面；完整来由见[首轮发布和组件修复](CONTAINER_FILE_RECYCLING_RESTORE_FOOTER_BOOTSTRAP.md)。

## 新增写后结果检查

已独立证明原件返回后，当前目标缺席、新inode出现在同路径或会话不可用，并不抹去历史恢复归属。Agent的recover_locked在原来源日志Restored时保存Completed，public单独给bindingCurrent=false。现有ContainerRecycleRestoreDispatcher把Completed且绑定失效映射为Superseded/TARGET_CHANGED，仓储结果与Workflow兼容检查同样要求该对应关系。不能预设这些场景必定Unknown，更不能用Unknown的空归属字段代替真实已发生的返回事实；真正无法确证恢复结果的Unknown矩阵仍需完成。

新增 `test_container_recycle_restore_returned_outcomes.py`：在隔离临时文件中由自有子进程实际返回原inode并在SourceReturned切点结束，分别移开返回文件、创建相同内容的新inode、以缺席会话观察。每轮Inspect及Discard均要求Completed、当前绑定false、原来源Restored、本次归属true、HDD格式历史可读取；检查原件/替代件完整快照和来源日志/claim保持，并阻止再次执行交换。

3项新检查与原returned-cut/写后撤权证明合计21项Linux root执行，全通过、零跳过；ResourceWarning作为错误。新文件自动匹配现有installer CI的test_container_recycle*.py收集规则。这里是真实临时inode/字节/元数据I/O，但namespace/session及历史目录有明确替身，不是安装RPC、真实容器实例变化、永久SQL/页面Superseded或物理HDD验收；C#对应关系本批为源码复核，未冒称新增实盘业务测试。

新增隔离检查私有证据 `deploy/artifacts/container-recycle-restore-returned-outcomes-89117ae71d6c4d2a844d26a828556de1`，6文件清单摘要 `8c9cca72ec82d8b49d026cd7a30e9c154a954cbecd69df3c4010c70a1744490b`，逐文件复核通过。父目录0700、文件root:root/0600。检查前后再次与新发布基线完整核对生产保持，没有生产信号、身份变更或新业务写入。

准确CI检查点时5项基础任务成功，6个浏览器分片仍运行，frontend汇总尚未成功。接下来先等待固定12项全部成功，再标准覆盖和全新真实任务页复验；随后写后移组/停用、按真实事实区分Superseded/Unknown的故障组合、跨写入、长期清理及其余目标。59项活动范围保持，17项移出不算完成；整项0/59、阶段0/6、酒馆与Live2D功能0%不因本批上调，可部署master尚未交付。
