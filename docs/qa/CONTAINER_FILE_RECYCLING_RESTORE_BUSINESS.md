# 独立回收恢复的内部业务编排候选

2026-11-06，候选0a714ec84681eae6bfecc5ecdf5bd23869b9a81f。承接[失败自有资源收尾](CONTAINER_FILE_RECYCLING_RESTORE_OWNED_RECOVERY.md)与[安装态RPC分项](CONTAINER_FILE_RECYCLING_RESTORE_AGENT.md)，新增Domain意图、仓储契约、来源解析、确认/查询/取消/管理员处置服务、异步dispatcher及独立业务签名域。本批没有具体EF恢复Repository、迁移、运行时工作流注册、HTTP或Vue，不计真实持久化/页面完成。

## 来源只能由服务端解析

新Queue入口只接收容器ID、原回收意图ID和幂等键，不接收客户端owner、完整计划、宿主路径、HDD摘要或执行上下文。来源解析必须先验证当前权威管理员、文件管理授权及当前登录；再从既有IContainerRecycleRepository读取原永久意图，校验其request/plan/execution/receipt完整业务MAC。

候选来源必须为已完成/已开始过执行、无发布保护或取消标记的Succeeded原意图，签名回执为Trashed、HistoryVerified/HistoryAvailable/BindingCurrent为true、SourceRetained=true/SourceRestored=false。无记录、MAC不符、Unknown/未完成或未保留来源拒绝。原来源的operationId/owner/完整计划及HDD摘要由该记录推导；新意图和新执行Job使用独立确定性ID与当前actor/login派生owner，原意图不改写。

新增独立source域MAC覆盖原意图ID、原request/plan/execution/receipt哈希及完整来源命令。新request域同时绑定此来源证明、新actor/login/trace/owner、完整来源及过期时间；plan/execution/receipt/confirmation另有各自域。完整嵌套属性bytes、uint64、纳秒与显式零值经规范JSON深快照保存，比较使用完整规范值，不依赖数组引用相等。公开意图DTO只返回来源ID、位置和摘要等必要信息，不返回原owner或完整私有计划作为客户端写入权限。

## 独立确认、执行与保守恢复

Queue只排队，不扫描或恢复；异步预检通过后短期Ready，再由独立确认生成执行Job。确认绑定原login、plan hash、行version和expiry；相同幂等键只返回原快照，即使来源后来改变，也不会重建来源、续期或再次执行。不同来源/容器不能复用该键。

来源在预检前后、确认前和active执行守卫中重新解析并比较MAC及完整命令。执行期间按既有6秒守卫复核当前授权/来源/取消和租约，取消待决调用并排空任务；不是对权限变化的瞬时零窗口声明。始终先Inspect原恢复执行ID；只有无回执且未Started/未取消/未过期/仍获授权时，先持久化Started标记和保护，再调用Apply。Started后来源改变、回执丢失或进程恢复均不重做Apply；后续只观察或Discard围栏同一ID。

Completed只归属本恢复操作，SourceOperationId、完整计划/目标证明、HDD摘要及RestoredByThisOperation必须一致。Unknown保留null事实与保护，不转成false/零修改；无有效回执时不捏造历史事实。原状态/源事实/归属/HDD验证摘要不能被处置重写，当前绑定/HDD可用性仅作观测。不同当前管理员可明确处置原Unknown执行，先Discard并验证兼容事实，再提交处置；这不恢复、清除HDD或声称写入成功。Native来源claim的完整Unknown故障与释放仍需后续实机矩阵，未由本批单元替代。

仓储契约明确要求全部writer/宿主别名共享保护，并在确认和Started的事务锁内复核原来源资格/证明，意图、两个Job、Outbox与审计原子提交。当前仅接口要求；服务层事务前检查不能代替真实SQL行锁。实际Repository/EF约束、来源锁、租约围栏和双向共享保护须下一批在真实PG验证，再注册Worker/HTTP/Vue。

## 实际检查、修正与生产边界

54项新增业务检查通过，包含原意图不变/新旧ID分离、来源MAC及深快照、来源资格/实时撤权/登录、处理中来源变化导致取消、Started后不重做、丢回执原ID观察、取消/过期/失租、跨域确认与跨管理员Unknown处置、错误归属/摘要/可空事实及存储篡改拒绝。测试使用真实FilePlanProtector密码学及临时随机密钥，但仓储、身份、登录与Agent明确为内存/类型替身，不是实际PG、Authentik、Root或页面。

最终5428 Unit零失败/零跳过、705 Integration通过/7原生依赖默认跳过（712总）、8范围通过；Release构建零警告错误。集成检查是既有回归，没有新增HTTP或实际业务PG门禁，不把7个跳过记通过。初稿夹具因重复的嵌套路径CS1061、未赋值field CS0649及显式默认初始化CA1805失败，原22错误日志/原夹具保留；修正为正确路径和明确属性后通过，没有关闭分析器。

两次独立实际只读快照均逐值等于前一已签基线；2346保护文件、11核心healthy、回收Root32/HDD35、独立恢复Root5、目录Root96及既有数据库保持。实际查询生产container_recycle_restore前缀表为空；静态确认新意图未映射DbContext，新Workflow/SourceResolver/Dispatcher/Repository未注册运行时。原Gateway既有注册保持，不把它当成新业务Worker。两宿主仍f333a69a，四容器/运行checkout仍58b8174；无部署、新RPC、身份变更或新实机文件操作。

证据deploy/artifacts/container-recycle-restore-business-8963ec835ed74781b1a1d4192c6c86dc，22文件/11份本批变更源码，EVIDENCE_SHA256.json摘要160c805f290963d5a506d2f4b58553d8ed6b6d5088de232fac186c69ee200722，逐文件重算通过。包含初稿失败、最终完整检查日志与实际保持快照；候选源码固定Git身份可复核依赖。

后续先实现真实持久化/迁移、来源锁下复核和全部writer保护、真实PG事务/租约验收，再Worker/API/Vue、准确CI与标准部署及真实身份/原文件/HDD整链；完整故障矩阵、长期清理及其他59项继续。当前完整主项0/59、阶段0/6、25%–35%工作量粗估和AI子计划0%保持，17项移出不算完成。

项目图谱whale-deck/container-recycle已按本批稳定eventId回执同步active，pending移入synced；未标阶段或完整功能完成。
