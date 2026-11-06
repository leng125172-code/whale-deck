# 归档恢复：执行中 Reserved 取消真实整链

2026-11-07，在已发布产品 `f318d9ab130ecab94668acf9754a6a6e448de92c` 上，冻结QA源码 `45ac608ad765f0fa4df20a6b480afeeb1cb0980a`，全新1,048,583字节资源首次通过归档执行中取消整链。承接[切点候选](CONTAINER_FILE_RECYCLING_ARCHIVE_CUT_CANDIDATE.md)和[常规整链](CONTAINER_FILE_RECYCLING_ARCHIVE_REAL_UI.md)。没有覆盖部署、修改SQL/Root事实或重放旧Apply；只签本组合。

## 真实操作与停止证明

临时真实Authentik管理员经可见Vue完成本轮原回收、留存清理Ready取消、新留存清理成功、归档Ready取消，再确认一个新归档意图。密码仅经Popen私有stdin交给Node，随后从fixture移除；没有会话注入、route替身、登录截图、trace或storageState。

观察线程在确认前启动，只匹配固定安装态归档apply argv、root UID、原MainPID/InvocationID/cgroup及五个不同的实际FD身份：容器根、归档锁、purge锁、原回收锁、当前HDD数据。pidfd打开后重绑，停止后原Root仍Reserved，四个复制身份为null、无目标/暂存，来源source/purge/marker与claim及HDD数据/历史元数据均重新核验。Applying及后续状态不计安全写前切点；本轮实际取得Reserved。

SQL的实际 `reservation_json` 经严格转换后与同一个请求、Root、来源及fingerprint关联，不能用合成Reserved回执代替。切点时SQL为Applying、publishBarrier为true，预留/Apply入口已持久化而最终receipt仍空；入口存在不证明复制已发生。

父进程完成物理证明后才放行真实页面取消。原pidfd在计时开始后 **5.556657秒**观察到退出，UI动作标记位于开始与退出之间；计时不包括先前HDD核验，也不以systemd服务排空代替进程退出。取消路径未恢复原Apply；失败或迟到观察句柄始终取回并精确清理，不因Future超时丢失停止进程。

## SQL、任务页与当前文件/HDD

原归档执行终态Canceled、Root Aborted、archiveCopied=false、claim已清除，未发布或暂存副本；SQL、实际公开receipt及19字段执行Job严格一致。原规划Job仍保存19字段Ready历史。执行确认与取消各一次、规划接受一次，lease/发布保护清除、相关Outbox已处理。原回收及成功purge的SQL/Root/HDD历史保持，不改成Restored。

可见任务详情验证实际摘要，普通9项/匿名5项拒绝；归档登出GET401，同Subject正常重新登录后9个原引用404，再次正常双端退出。Root/文件/HDD独立live检查通过后，仅移除已核对的本轮空旧私有目录及自有容器/夹具；没有清理HDD历史。final和第三次独立当前重读均通过，目标与stage仍缺席，原HDD字节、inode、属性和Purged历史保持。源与HDD同device，不能据此签跨物理盘灾备。

## 失败原件与实际范围

首次临时服务在Git信任检查处失败，尚未创建fixture；仅为后续该服务增加当前仓库的进程级safe.directory，不改全局Git配置。

随后64MiB夹具在原回收规划页面60秒观察时限失败；实际规划随后Canceled且无执行/Root/HDD写入。原文件完整字节/身份及全部旧基线核验后准确移除自有夹具，原失败保留。

4MiB新夹具在留存清理页面60秒观察时限失败，后台随后Succeeded/Completed。另行从SQL实际保存回执与Root/HDD独立核验成功清理事实后，仅清空的自有夹具收尾；未创建归档意图，不签页面成功。该核验明确标记SQL-derived，不能冒充API/UI回执观察。大文件耗时/页面观察问题仍待解决；最终1MiB用例通过不抹去两个失败，也不证明完整大小范围。

所有重试均为全新容器/意图，没有重新执行旧Apply。最终成功批含1回收/2清理/2归档，清理和归档各含一个Ready取消；执行中取消只针对第二个归档。含失败原件后生产总57回收/29恢复/23清理/4归档、活动0；Root/HDD139/137、恢复82、目录98、purge48、archive8。旧业务全行hash、2346保护、11核心、21安装模块和f318d9ab保持，user NOPASSWD未变。

## 检查与封存

完整回收493项root测试零跳过；最终11项专项、范围8项、CI合同14项及Python/Node语法通过。上批切点fd0d4970的准确CI `42292229759` 固定12路全success；本批新QA准确CI完整终态尚未签收。新代码只增加QA门禁，不把合成检查当实机通过。

真实unit `whaledeck-qa-archive-cancel-reserved-45ac608a`，invocation `e1c7f67382cb466398c733f22279fd97` 的journal报告passed并正常退出，耗时209.969秒。临时unit已被systemd回收，事后空的InvocationID/时间及默认systemctl值不作退出依据；单独保留按原invocation匹配的passed条目与journal证据。

所有私有证据root目录0700/文件0600，文件数不含manifest：

| 证据目录后缀 | 文件数 | manifest SHA256 |
| --- | ---: | --- |
| `archive-inflight-cancel-7af7a5f2d9cc4f82868522e8ebf437b8`（真实通过） | 218，含134冻结源码/12真图 | `af1127c1f6c83a03934c8a5804f40677f7753182c844bd30a52e7cf1b758920e` |
| `archive-inflight-cancel-d3eac9613e454c2ab6805a420e4b2f66`（64MiB失败与收尾） | 145 | `9d39d9f1115b174f06ea9a91d06ea9b67705d8bed216fb4afbf41de34177dcb7` |
| `archive-inflight-cancel-bacdd62f37f047e699f711e17532d97a`（4MiB失败与收尾） | 166 | `00a961427dd252610941e1eced57a2882ac70c1a7ff6ffa9b8db76c4620a9e1a` |
| `archive-inflight-prep-17c06b73c28f4061a53de3a43e1dc274`（检查与启动原件） | 141 | `bf7f159d2b8b6a5a6c07fad96eaa425d400b6d7662d26cc63a50a51df6df6926` |
| `archive-cancel-journal-receipt-d3c7dcd6939b41ca98b130e67582b771`（原invocation核验） | 2 | `f54a4acacc1ceb75e9124848d5ff67fb2a5966858c2c1498a019e0d8fad4886a` |

前四目录前缀为`container-recycle-`，最后一项目录名即表内值。真实通过后不重放已完成写入。继续正常退出、实时撤权、重启/自然租约、写后与Unknown、来源/跨写入、claim暂存/重复来源、长期策略和维护连续性及59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持；17项移出不算完成。
