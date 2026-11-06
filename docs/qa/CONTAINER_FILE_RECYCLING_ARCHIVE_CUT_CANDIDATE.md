# 归档恢复：复制前停止点候选

2026-11-07，QA源码 `fd0d497006f16629cb17a43202d2c29b800257ad` 新增归档专用观察器和15项专项。当前只完成候选、隔离临时文件/HDD和自有子进程验证；没有向安装态helper发送信号，没有新增业务任务或覆盖部署，不签执行中取消、退出或其他故障组合。既有[常规真实整链](CONTAINER_FILE_RECYCLING_ARCHIVE_REAL_UI.md)保持。

## 可接受的切点

仅Root `Reserved` 且四个复制身份均null、`parentAfterStage`为空、目标和暂存均不存在，才证明未开始复制。`Applying`本身已是持久化不确定边界，即使尚未mkdir，也不能算写前安全取消。错过窗口返回未取得切点，不授权重放Apply。

原SQL意图、执行域、来源purge执行域、完整计划、owner、容器实例及固定`work/recycle-rpc.bin`逐项绑定。原source/purge/marker三个当前Root记录的精确结构、状态及摘要和当前共享claim须一致。source仍Purged、purge仍Completed且unlinkConfirmed，原件和purging名称缺席，旧私有目录身份保持。

只接受固定归档apply argv、root UID、固定格式systemd服务、当前MainPID/InvocationID、实际cgroup及进程环境InvocationID，并同时证明五个不同的内核FD身份：容器根、归档锁、purge锁、原回收锁和HDD数据。Driver随机服务后缀不冒充业务执行ID。pidfd打开后复核绑定，停止后观察实际T/t状态，再核Root、namespace和进程身份，才能交给外层页面门禁。

HDD证明使用只读、无namespace创建的attested FD，读取完整数据计算SHA256，核inode/元数据/长度、配置及存储身份；同时读取当前历史JSON，核`archive-history-v1`摘要、Purged状态和historyDigest。交出句柄前再次核历史JSON和attestation、原HDD FD元数据。复核发现的历史元数据摘要缺口已补齐；`hddDataReread`仅在实际数据重读后为true，Root/namespace证明自身不声称已读HDD。

## 失败与验证

最终HDD证明失败、Root已越过Reserved或外层页面操作失败时，只清理已精确绑定且由本观察器停止的原pidfd。候选专用句柄关闭前终止该原进程；不使用SIGCONT恢复Apply。终止或取得切点本身都不算产品取消成功，外层仍须证明页面操作、原helper退出时限及SQL/Jobs/Root/HDD终态。

15项专项包括真实临时source/purge/claim/namespace与HDD读取、来源/owner/结构/状态和摘要变化拒绝、目标或stage抢占拒绝、当前历史变化/数据损坏拒绝、执行域绑定、五FD与精确argv/cgroup/Invocation/root UID、错误进程拒绝，以及最终证明失败/越过Reserved/成功交出后的描述符清理。Proc、systemd及成功切点是明确替身；实际信号只发给测试新建的sleep子进程，均验证无SIGCONT，不能替代安装态正向验收。

实际检查输出：完整回收集 `Ran 482 tests in 30.130s / OK`，零跳过，含本批15项；范围8项、CI合同14项及两文件Python编译通过。现有CI的`test_container_recycle*.py`通配已覆盖本批，不另增一套测试入口；本批远端CI完整终态未核验。文档更新后的首版范围复核因nextStep漏写固定说明“17项移出不算完成”中的“项”字出现7通过/1失败；补回原说明后8项全部通过，范围未放宽。

## 生产保持与下一步

两次当前只读基线一致，并与上批常规整链最终基线完全一致；四类业务全行hash保持，仍54回收/29恢复/19清理/2归档、活动0，Root/HDD133/133、恢复82、目录98、purge44、archive6。已发布f318d9ab、21安装模块、2346保护及11核心保持，user NOPASSWD未变。没有业务写入或旧操作重放。

私有证据 `container-recycle-archive-cut-candidate-2c5b7499b9f44c86b6cdfbd8340c764e` 封存125文件及manifest，含119冻结源码、检查结果、当前双基线及四类业务hash；root目录0700/文件0600。manifest SHA256：`3c776408e881f3a91a33f415adbcc7542a583163b8633f08f8e11b14c8868f9d`。

下一步将切点接入全新资源的真实身份/可见页面取消、正常退出流程，独立核当前HDD、原pidfd退出、执行Job/审计及Root Aborted或Unknown，不把错过窗口或脚本清理算通过。随后撤权、重启、Unknown、来源故障、跨写入、claim暂存/重复来源/长期清理/维护连续性及59项继续。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%均不因候选测试增加。
