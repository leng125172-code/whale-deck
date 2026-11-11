# 原owner移组期间由另一管理员接续退役

接续：703a68f1082fedddf7a8e56541c1bfa47b36db9e准确CI42958546300已全13成功，最终收据另存。本批登记的QA窗口指针缺口已后续修复，并通过真实FD释放后本地中止→原组/会话收尾→同账号同空间同意图显式retry验证；见[146文件真实重试证据](WORKSPACE_RETIREMENT_TAKEOVER_RETRY.md)。新修复提交自身CI仍待，以下首轮与缺口发现记录按原事实保留。

2026-11-11，使用已部署产品356ba6c1。本批仅新增QA编排、证据检查及CI门禁，没有新产品部署或正式Agent授权扩展。只移除本批临时owner A的原单管理员组；不禁用账号、不改变密码、不改管理员组本身，也不转移空间owner。

前序21886668eeafee56ed111d73119d3d5b4cdd8ff3准确CI42956312074已全13成功，最终绑定收据另存 `deploy/artifacts/workspace-retirement-administrator-record-20261111/exact-ci-final.json`。

## 首轮真实验收

- A与普通账号完成真实Authentik/Vue/BFF空间分配、强制硬配额、源码导入发布、Node测试2/2、.NET独立构建及独立测试2/2，启停后Node复验，共四运行成功，零测试失败/跳过。HDD Capture及独立Verify完成后，页面另行确认删除。
- 真实open-unlinked源码FD使原execute结算Rejected，空间隔离并继续占预算。控制器逐项核对新QA slug/空间/owner/退役/task，排除原空间、未结算或已有恢复/completion的意图；只对唯一生成账号、准确UUID和初始原单管理员组移组。
- A原登录携带正确CSRF/Origin、明确继续删除命令请求恢复及尝试历史，均403；`auth/me`仍200且Subject保持A、管理员标记false。独立SQL比较该拒绝窗口前后workflow、step、workspace和job整行摘要及恢复/completion计数相同。
- **A的组继续缺席**，B通过全新context、可见登录及当前管理员身份访问A空间。B在自身上下文核对原失败、关闭本批持有FD、页面独立勾选继续删除并以新键创建attempt 1，真实Worker/Agent完成恢复。B正常UI退出后，A原会话仍200且管理员标记仍false。
- 控制器在恢复A权限前，再从真实身份源核对A当前组仍等于移除后的值；独立SQL确认新执行/恢复/审计actor=B，原owner及原job actor=A，previous/current/recovery/completion任务配对、当前binding一致且恢复计数1。只输出绑定一致性布尔值，不导出binding或Token。
- 原失败step完整行摘要、原计划、snapshot、archive、epoch、首次确认和有效期逐一保持。只有此时才恢复A原组，不覆盖其他身份或组变化。A同一会话再次读取资源200，`auth/me`管理员标记恢复true，随后由A完成预算复用和正常退出。
- 两空间均经产品流程归档退役，配额八项归零、源/claim缺席、预算释放后可复用并再次退役；HDD树和退役后8份报告保持。三个临时账号删除，FD及权限恢复子进程正常退出，wrapper成功退出、活动任务和锁归零。

完整wrapper首次通过，无修复重试。原19,672条记录、19条登记（5活动分配、14原退役）、46份报告及原源码/配额/HDD/journal保持；11核心及宿主进程身份保持，120,000,000,000/300,000,000,000字节硬限制、原用户source改动与user NOPASSWD保持。B的320暗色真实历史截图已查看，无横向溢出。

## 证据与未闭合项

6项窗口证明测试覆盖提前恢复A权限、篡改旧step/plan/snapshot/time、转移owner/job、错误新actor/previous task及失败/重复管理员证明；5项管理员归属、9项身份变更、5项磁盘、14项CI合同及8项范围检查通过，真实只读SQL列预检通过。EOF/异常会先恢复A原组，再允许父层显式修复或临时账号清理；这种中止不会记为接管通过。

`deploy/artifacts/workspace-retirement-takeover-20261111/`封存 **104文件 / 25,843,695字节**（不含manifest自身），manifest SHA256：
`cfc38255d169802d6012fd5bf61381d70c00d0c8022b6f824a96fbe525fe8aa8`。逐文件复验通过，7个实现/CI源码有摘要与副本。

重点：`takeover-1-before.json`、`takeover-1-denial-window.json`、`takeover-1-while-revoked.json`、`takeover-1-restored.json`、`persistent-administrator-recovery.json`、`persistent-original-failure-preserved.json`、`accepted.json`、`batch-acceptance.json`和`resource-closure.json`。新空间为`a0858c7f-1b20-464c-8e07-da2f44c9bf78`及`f734a86e-49cb-4156-9246-abe12843fd6a`；失败、成功、归档、报告和审计均保留。

已发现但未在本轮触发的**QA显式重试缺口**：中止窗口后child会按新的`authorityAttempt`写新收据，但持久`takeover.attempt`可能仍指旧窗口，最终只读核对可能误读旧收据。详见封存的`known-probe-limitation.json`。本轮只签首轮移组接管，不签异常退出后显式重试；下一批须修复窗口指针/保留旧证据，并在同账号、同空间、同意图上实际重验。

本批新提交准确CI仍须按最终收据跟踪。账号禁用/删除期间接管、删除已开始时的中断、其余59项/ST272/PET6/供应商Live2D16继续；完整主任务0/59，17项移出不恢复、不计完成，不签master，不为纯验证器重复部署产品。
