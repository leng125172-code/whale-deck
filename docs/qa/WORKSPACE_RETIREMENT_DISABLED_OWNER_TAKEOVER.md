# 原owner禁用期间由另一管理员接续退役

接续：a90bc7d230b2f801207af3e76316d19c2a0df273准确CI42964077225已全13成功；最终收据另存 `deploy/artifacts/workspace-retirement-disabled-owner-takeover-record-20261111/exact-ci-final.json`，原EOF查询失败保留，无重复部署。

2026-11-11，使用已部署产品356ba6c1。本批只扩展QA编排、身份状态断言和CI门禁，没有新产品部署或正式Agent授权扩展。仅对本批临时owner A的`is_active`改为false；组、密码及空间owner保持，不把禁用与移组混为同一证据。

前序5a89dd65fa3aee79e24f28a4112fafe7534b9cf6准确CI42961015657已全13成功，最终绑定收据另存 `deploy/artifacts/workspace-retirement-takeover-retry-record-20261111/exact-ci-final.json`。

## 实际验收

- A与普通账号经真实Authentik/Vue/BFF完成新QA空间分配、强制硬配额、源码导入发布、Node测试2/2、.NET独立构建和独立测试2/2，启停后Node复验，共四运行成功，测试零失败/跳过。HDD Capture及独立Verify后，页面另行确认删除。
- 真实open-unlinked拒绝令原execute结算Rejected，空间AttentionRequired并继续占预算。控制器核对准确生成账号、UUID、原活动状态和单管理员组，以及新QA/原失败task/无租约或恢复历史，再仅PATCH该账号活动标志。
- A原登录在正确CSRF/Origin和明确删除命令下，恢复POST和尝试历史GET均403；在这些实时拒绝之后，`auth/me`也实际403。拒绝窗口workflow/step/workspace/job完整摘要、恢复和completion计数保持，不把缓存中的旧管理员角色当作当前授权。
- A继续禁用期间，B在不复制Cookie/Token的独立context中可见登录，以当前有效管理员身份读取A的资源，页面独立勾选、新键创建attempt 1并恢复成功。B正常菜单退出后，A原context的`auth/me`仍403。
- 恢复A之前，身份源再次证明A仍inactive、groups与禁用前完全一致；只读SQL核对新execution/recovery/audit actor=B、原owner/job=A、旧失败step/plan/snapshot/archive/epoch/确认及有效期保持，以及previous/current/recovery/completion绑定、一次恢复和当前login binding一致性。只输出一致性布尔值，不导出binding或Token。
- **只有此时才恢复A原`is_active=true`。**没有重新登录或注入会话，A同一原context读取退役资源200，`auth/me`200且Subject保持A、管理员标记true。之后由A完成预算复用和正常退出。此批证明禁用窗口敏感操作及身份查询被拒、还原后的原上下文可用；不声称禁用会自动删除全部BFF票据或所有其他会话。
- 两空间均通过产品归档、明确删除确认及退役收尾；配额八项全零、源/claim缺席、相同预算可再次分配并退役，HDD树及退役后8份报告保持。三个临时身份删除，FD/restorer/wrapper正常退出，活动任务和锁归零，普通账号恢复/历史仍403。

完整wrapper首次通过，无修复重试。原20,000条记录、23条登记（5活动分配、18原退役）、54份报告及原源码/配额/HDD/journal保持；11核心和宿主进程身份保持，120,000,000,000/300,000,000,000字节硬限制、原用户source改动与user NOPASSWD保持。B的320暗色历史实图已查看，无横向溢出。

## 验证器契约与证据

新增显式`TAKEOVER_KIND`在Python、Node输入和child三处一致性校验，默认保留移组模式。5项新Node测试要求移组为200/非管理员、禁用为403，拒绝以401缺会话或200缓存命中代替禁用证明，并要求禁用时组不变；7项重试、6项归属、9项身份、6项窗口、5项磁盘、14项CI合同和8项范围检查通过，相关门禁接入CI。

恢复收据保留兼容字段`originalGroupRestored`和`originalActiveFlagRestored`，表示恢复后事实与原值匹配的后置条件，不表示每个字段都曾被PATCH。本批`originalGroupsUnchanged=true`，实际写入仅活动标志；移组模式仍要求`originalGroupStillAbsent=true`，不据兼容字段推定组发生恢复写入。

`deploy/artifacts/workspace-retirement-disabled-owner-takeover-20261111/`封存 **115文件 / 25,984,266字节**（不含manifest自身），manifest SHA256：
`6b0ec6f4b474738373506f5d9bac12f4f1a2441cdc79589cc936de55c7705705`。逐文件复验通过，10个实现/CI源码有摘要和副本。

重点：`takeover-1-removed.json`、`takeover-1-denial-window.json`、`takeover-1-while-revoked.json`、`takeover-1-restored.json`、`persistent-administrator-recovery.json`、`persistent-original-failure-preserved.json`、`accepted.json`、`batch-acceptance.json`和`resource-closure.json`。新空间为`de141e1a-2109-4992-aa48-6f825f6abff7`及`e064c4ab-b3ec-4f06-af8a-ee5c6b8b3ab0`；失败、成功、HDD、报告和审计均保留。

本批新提交准确CI按最终收据跟踪；账号删除期间接管、POST后/产品恢复执行中再中断、删除已开始时的故障及其余59项/ST272/PET6/供应商Live2D16继续。完整主任务0/59，17项移出不恢复、不计完成，不签master，不为纯验证器重复部署产品。
