# 恢复故障摘要观察与处置事实保持候选

2026-11-05，承接[成功摘要五场景](CONTAINER_RECURSIVE_ACL_RESTORE_SUCCESS_JOB_RESULTS.md)。本批只扩展验收工具与证据校验，不修改产品接口、不部署、不启动真实故障操作或重放旧任务。Partial、Unknown、处置摘要尚未因本批取得新的真实验收。

## 候选能力和依据

共享真实Jobs页面观察工具新增Partial/Unknown/Acknowledged；固定JobsView take=100列表/详情，原26字段、标签、计数及历史提示继续严格比对。Partial与Unknown对应Job Running，phase分别为AwaitingContainerRecursiveAclRestoreResolution与AwaitingContainerRecursiveAclRestoreReceipt；处置后对应Job Failed/phase Acknowledged，不能把应用意图的Acknowledged当作Job Succeeded。三个故障阶段采用独立截图前缀，不覆盖规划或成功图。

Acknowledged页面必须实际显示“管理员处置仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。”，并保留原Job不能通用取消/重试的观察要求；工具没有新增任何取消、处置、回放、Cookie注入或身份绕过动作。

`acknowledgement_facts`是补充性的只读前后摘要对照。固定原意图/执行Job、阶段、ACL类型、计划/来源计数；处置后围栏必须false，结果码由原Partial或Unknown决定。已有回执时，原实际五计数、回执状态及历史验证不可改写。无回执Unknown保持null/“未证明”，处置时可补获Completed、Partial、Aborted或Unknown真实回执，结果码仍为UNKNOWN_ACKNOWLEDGED，不能改成PARTIAL_ACKNOWLEDGED或COMPLETED。

当前对象绑定与HDD当前可用属于可重新观测的事实，不强制等于旧摘要；历史可能存在、历史已验证不可倒退。该对照并非独立schema/回执验证器；不能据26字段对照签完整Root、outcomeDigest、historyDigest、物理树或HDD。后续真实门禁仍必须执行原SQL绑定、原生Root/HDD全证据、准确进程边界、真正管理员处置和清理前后独立核验。

## 验证与未验范围

8项新增Python负向检查、关联共98项root工具测试通过，零跳过；5项独立Node观察契约测试通过，零跳过。覆盖Running/Failed与phase误配、无回执当零、处置误报成功、历史计数或范围被改写、HDD当前不可用的合法变化、无回执Unknown补获四种回执及原结果码保持。3个Python AST、Node语法、CI YAML、diff检查通过。测试中的状态/SQL/摘要均为显式构造夹具，不是安装态故障、真实登录或真实处置的证据。只读协作审查对照产品Repository/WorkflowValidation，未发现确定的状态或事实保持差异；最终实现与验证由主代理复核。

上批eb477a4准确CI41945001041现已completed/success，不再是浏览器运行中；该事实只更新上批CI终态，不能替代本批后续CI或真实故障验收。已验Ready/Canceled及四次成功摘要结果保持原范围。继续全新资源Partial/Unknown/处置整链、其余恢复故障组合、可恢复删除/长期清理、维护入口连续性和59项，不提升整项或阶段完成率；旧维护连接失败及源/HDD同device限制保留。


收尾：项目元数据首次遗漏“17项移出不算完成”，范围检查1项失败，补回后8项全部通过；目标正文59项未变，原失败事实保留。748本地链接/缺失0及diff检查通过。项目图谱事件whale-deck.container-acl.1c8f3d7.restore-fault-job-observation已取得project/key/event匹配且duplicate=false回执，pending移入synced，阶段active。
