# Partial取消与另一管理员处置：业务证据校验候选

2026-11-05，承接[首项完成边界](CONTAINER_RECURSIVE_ACL_RESTORE_FIRST_COMPLETED_CUT.md)和[故障摘要观察](CONTAINER_RECURSIVE_ACL_RESTORE_FAULT_JOB_OBSERVATION.md)。本批补充只读SQL/API/Job/审计前后校验，未启动新工作站故障、真实登录或处置页面，不修改产品、不部署、不重放旧任务。

## 精确链路

固定新26对象access计划及首项已恢复、其余25项未开始，五计数1/1/0/25/0。真实取消请求之后，预期意图Partial、Root Partial、原执行Job Running/phase AwaitingContainerRecursiveAclRestoreResolution，完成时间为空、发布围栏保留、租约释放；原取消时间保留、待处理outbox为正数。不能套用零写取消的Canceled/Aborted或提前清空消息。

Ready规划Job整行保持；原意图/幂等键/登录绑定/命令/计划及其hash、执行Job确定性ID、原用户/容器实例/路径、来源摘要与计划计数分别绑定。SQL中的原回执与API回执逐字段一致，API版本、规划/执行ID、命令、来源/清单计数、根与执行者身份和Job完整26字段、标签/历史提示相符。MAC列仅检查格式，不读取密钥，也不冒称独立验证秘密MAC；原生Root/HDD、来源及全树证明仍是必要的另外一层。

另一管理员处置后，意图Acknowledged、执行Job Failed/phase Acknowledged，完成时间存在、围栏false、消息归零、处置者与原用户不同。原绑定、计数、回执状态、outcome/history摘要、历史验证及原取消时间均保持；acknowledgement_json须等于新观察回执，原Ready Job不改写。只允许Replayed/当前绑定/HDD当前可用观测变化，不把HDD当前不可用抹成从未验证过。页面必须显示处置不代表整树恢复成功的提示，处置前后都核对同一原Job。

精确审计为plan-accepted/Accepted、plan-result/Ready、confirmed/Accepted、cancel-requested/Accepted、result/Partial五条；处置后新增partial-acknowledged/Partial一条，由另一管理员产生，仍绑定原执行Job。逐条检查唯一ID、actor、container target、trace与原意图/容器/实例及路径hash元数据；缺失、重复、错误结果或替换用户不通过。恢复工作流结果码保留RESTORE域，不与原始递归ACL修改域混用。

## 测试、失败和未验范围

新增9项构造证据测试、关联121项root工具通过，零跳过；2个Python AST、CI YAML和diff检查通过。覆盖Partial提前结束/释放保护/清消息、错误phase与结果码、审计缺失或重复/错误actor、原Ready/计划绑定变化、同用户处置、改写原计数/摘要/执行、API或Job与SQL不符，以及历史事实保持但当前HDD可用性改变的合法情况。首轮破坏计划JSON的夹具曾因JSONDecodeError与预期AssertionError不符失败，第一次改动误中同形断言未修正该例；最终使用合法JSON中的指纹差异，实际验证计划绑定而非解析器报错，复验通过，未放宽业务判定。

SQL、API、审计、DOM摘要均为显式构造记录，不是真PostgreSQL事务、真实Cookie、真实取消或真实处置证据。只读协作复核了产品状态/phase/outbox及审计映射；主代理确认原始递归ACL域不含RESTORE的结果码本身正确，两工作流不可混用。未给安装态Partial/管理员页面或59项补签；也不能以此代替Root/HDD完整原件和物理证明。

下一步将本校验与首项完成观察器、原件证明、正常取消和另一管理员页面操作接成完整父门禁，包含原任务排空、准确清理及清理前后独立进程。其后仍有第二项已启动等Partial形态、Unknown、其他故障组合、可恢复删除/长期清理、维护连续性及59项。旧维护连接失败、源/HDD同device限制和17项移出决定保持，不提高完整完成率。
