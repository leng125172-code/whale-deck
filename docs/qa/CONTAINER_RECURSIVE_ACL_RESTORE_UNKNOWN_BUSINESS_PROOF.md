# 26对象Unknown业务证据候选与隔离PostgreSQL验证

后续：新增独立门禁后，固定26对象首项后重命名Unknown及跨管理员处置已有[单次实机分项验收](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_RENAME_GATE.md)。本页原候选/构造与未验状态按发生时保留，不将隔离PG回执重新归类为真实Root或身份证据。

2026-11-05，承接[Unknown原件证明](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_RECORDS.md)和[标准覆盖](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_RELEASE.md)。候选源码冻结 `7f043f8a42866394bdd834b8a380f0db187f7374`，本批新增独立的固定26对象Unknown SQL/API/Job/审计校验器，并扩展真实隔离PostgreSQL门禁。产品与实际部署仍为 `34153d2`；没有新增真实Root故障、Authentik登录、Vue处置或覆盖发布。

## 固定业务形态

只接受26对象/25文件/1目录、access、计划修改26的已知回执形态：意图和Root均Unknown，结果码 `CONTAINER_RECURSIVE_ACL_RESTORE_RESULT_UNKNOWN`，五计数严格 `0/0/0/0/26`，空outcomeDigest，bindingCurrent=false，历史已验证且摘要完整。执行Job Running/phase AwaitingContainerRecursiveAclRestoreReceipt，意图与Job无完成/取消时间，围栏保留、租约释放、待处理outbox为正数。无回执、OUTPUT_INVALID/BINDING_CHANGED等其他Unknown来源、Partial及其他计数形状均不纳入此专属签收。

新校验器独立于原Partial入口；Ready规划整行、原意图/幂等键/登录绑定/命令/计划及hash、确定性执行ID、原主体/容器实例/路径、来源摘要及API版本逐一绑定，SQL规范化回执与API、Job列表/详情26字段和页面值一致。MAC列仅核对格式，不读取或冒称验证秘密MAC。原件proof中已证明首项真实改变1，不能填入聚合changedCount；真实文件/Root/HDD获取与相同请求绑定仍必须另作证明。

审计必须恰为plan-accepted/Accepted、plan-result/Ready、confirmed/Accepted、result/Unknown四条，没有取消；另一管理员ACK后新增unknown-acknowledged/Unknown一条，原执行与容器/实例/路径hash元数据保持。缺失、重复、错误actor/目标/结果码均拒绝。

ACK后意图Acknowledged、执行Job Failed/phase Acknowledged，围栏释放、完成时间存在、outbox归零；原Root Unknown、五计数、空outcomeDigest、历史摘要与验证事实、请求/计划/执行绑定保持。acknowledgement_json等于当前验证回执，当前HDD可用性/Replayed观测可变化，固定重命名场景bindingCurrent仍须false。新旧Job均要求“回执已证明修改”和Unknown说明，处置后继续显示“不代表整树恢复成功”。ACK不抹去不确定性，也不证明零修改或成功。

## 自动化与真实数据库边界

18项构造SQL/API/DOM证据测试通过；关联198项root恢复工具测试通过、零跳过，包含已有真实临时ACL/Unknown原件证明，不将18项重复累加。另3项native状态门禁测试通过。.NET10集成项目本机构建通过，零警告、零错误。

原生PostgreSQL针对同一冻结源码实际执行1个复合dispatch集成测试，在既有4对象Unknown/Partial用例之外增加完整26对象计划与Unknown回执：真实事务、MAC、迁移/约束/触发器、规划/执行Job投影、空摘要与五计数持久化、禁止直接SQL改写changed_count/outcome_digest、Unknown不再被claim、不同主体ACK、原回执/计划/执行ID/Ready快照保持、唯一unknown-acknowledged审计以及Failed/Acknowledged结束状态。新增公开phase `recursive-acl-restore-flat-unknown-ack-original-facts` 必须被runner观察到，避免只运行旧用例却报告新覆盖。

准确Invocation `64045b51e3c7430395d27cec2fcf2986` 对应unit `whaledeck-qa-unknown-business-7f043f8-r2`，exited/ExecMainCode=1/ExecMainStatus=0/Result=success；native状态Passed、executedTests=1/passedTests=1、cleanupConfirmed=true，临时数据库与测试容器已清理，外部投递0。它使用真实隔离PostgreSQL，但Root回执、当前管理员主体与页面观察仍是显式构造输入；不能据此签真实签名RPC、实际ACL/HDD写入、实时权威身份或Vue页面链路。

## 失败保留、原件与生产保持

首轮18项构造测试有2失败：转换夹具使用endswith(result)，误把plan-result/Ready也改为Unknown；改为精确action token后通过，未放宽审计要求。首轮集成构建有8个CS1503诊断，源于4个新断言用uint字面量比较nullable int投影；修正期望类型后零警告/错误。随后增加测试phase标记并冻结源码，隔离SDK实际编译、执行了该冻结版本。

首次原生unit `whaledeck-qa-unknown-business-7f043f8` 因root子进程Git安全目录校验拒绝，尚未建立native状态或创建数据库；原Invocation `655b13365073493597d222ffe8f9cd30` 与journal保留。第二次仅通过进程级Git配置声明本仓库精确safe.directory、关闭可选索引刷新，未改全局信任。构造测试及构建失败说明取自持久工具回执，未伪造未捕获的原始stderr文件。

真实数据库测试前后同口径比较：2292保护文件、来源Root1910、恢复Root252/HDD166、目录Root94、11核心身份/健康、两宿主进程及业务计数全部相同；恢复23意图18执行/活动0，普通递归ACL62意图56执行118Job。实际六组件与HTTP仍为34153d2。原32文件发布封存及30文件中断封存逐项保持，不重放旧任务、不重新签旧失败。

私有目录 `restore-unknown-business-b142bb19d72447bc8ec56be58eac538f` 封存145文件，`CANDIDATE_EVIDENCE_SHA256.json` 摘要 `2a4001ecb5ba32200d66f2f28a1efee8ccd6bda211f6c64e4641347cb408b915`，逐项大小/SHA256复核通过。包含130文件验证静态导入闭包、准确源码hash、native公开状态/两次启动回执/日志、前后生产保持与六组件来源、失败限制及验收边界；不包含凭据、临时连接文件或实际测试账户密码。

## 下一步与未验收范围

继续接入独立26对象真实coordinator、页面与只读verifier：新自有树首项恢复后固定item-01.bin→renamed-item-01.bin，绑定专属原件proof与本业务proof，核对真实来源/完整文件/HDD、SQL/API/Job、原管理员Unknown提示、另一管理员暂不处置及一次明确ACK、权限拒绝/正常退出、前后独立检查与准确清理。现有Partial硬编码和历史结果不能代替该入口。

本批不关闭真实Unknown整链或其他故障组合，不提升59项/阶段完整签收及整体25%–35%工作量粗估；可恢复删除等剩余功能继续，酒馆/供应商/看板娘功能仍0%。

收尾：范围8项、本地Markdown链接2087/缺失0、diff及145文件封存再核验通过，两Git索引仍归用户所有。准确7f043f8已推送dev；CI查询首次EOF，重试读到run41974226797仍in_progress，不报告整条CI终态成功。项目图谱事件whale-deck.container-acl.7f043f8.restore-unknown-business-proof取得projectId/key/eventId匹配、duplicate=false回执，pending移至synced、阶段active。
