# 接管验收中止后的显式同意图重试

2026-11-11，修复的是QA接管编排的窗口指针，不是新增产品恢复接口。实机继续使用356ba6c1，没有新产品部署或正式Agent授权扩展。前序703a68f1082fedddf7a8e56541c1bfa47b36db9e准确CI42958546300已全13成功，最终收据在 `deploy/artifacts/workspace-retirement-takeover-record-20261111/exact-ci-final.json`。

## 修复和回归

原代码只在首次`??=`时保存`takeover.attempt`，新浏览器尝试的child会写窗口2，但最终核对仍可能读取窗口1。现在显式新尝试须先读取原组恢复收据，再深拷贝旧窗口和B会话证明到历史，建立新窗口指针和独立拒绝请求键；禁止重复/倒退或已完成窗口再次移组。新登录仍须是同一个生成的B Subject。

7项Node回归实际通过：原表达式保持旧指针的复现、当前指针更新、历史不共享引用、原组未恢复拒绝、重复/完成窗口拒绝、无副作用的浏览器尝试间隙、重复历史拒绝，以及本地中止必须在FD正常退出后且产品POST前、只发生一次。原6项窗口、5项归属、9项身份、5项磁盘、14项CI合同和8项范围检查通过；相关Node/Python门禁已加入CI。

## 真实中止与重试

- 原三个临时账号在同一个Python wrapper/ExitStack内始终保留。A真实分配新QA空间、导入发布源码，Node测试2/2、.NET独立构建和测试2/2及启停Node复验，共四运行成功，测试零失败/跳过；HDD Capture与独立Verify后明确确认删除。
- 真实open-unlinked拒绝使原产品任务Rejected。窗口1移除A原管理员组，A恢复/历史403、me非管理员，拒绝窗口账本保持；B独立可见登录读取原失败，关闭持有FD并保存release回执。
- **在产品恢复POST尚未发出时，验收程序故意抛一次本地错误。**没有伪造响应、重启任务或改业务SQL。B正常菜单退出，restorer经EOF恢复A原组，A也正常退出。`browser-process-1.json`的非零退出及`failureHistory`原件保留，不把它计为Node/.NET测试失败或产品恢复失败。
- 父层真实复核两个现有管理员仍active且原组准确、旧workflow/step/workspace/job及旧plan/snapshot/time保持、恢复/completion仍0、FD已退出、两个会话已清理，才进入显式修复等待。读取原证据及当前SQL再次核对后，提交受支持的`retry.json`，没有重建账号、空间或意图。
- 窗口2登录同一A，跳过已经完成的登记/导入/运行/归档，沿用同一retirement和原失败task。原FD不重开，held/open-unlinked/release三个证明的摘要与中止前一致；原四run及source-import ID列表不变。
- 窗口1完整证明和B退出回执深拷贝保留，当前指针为2并使用新拒绝请求键。再次移组A，B以同一Subject独立登录，在A仍无管理员权限期间页面独立勾选、新键确认恢复成功；原owner/job=A，新执行/恢复/审计=B，原失败证明保持，最后才恢复A原组。
- **两个浏览器验收窗口只对应一次产品恢复**：attempt 0原Rejected与attempt 1新Succeeded，恢复账本计数1；不是新建意图掩盖旧失败。第二次浏览器进程退出0，当前读取`takeover-2-*`，没有读取中止窗口的缺失成功证明。
- 两空间均通过产品流程归档退役、配额八项归零、源/claim缺席、预算复用、HDD及退役后8份报告保持。A/B两轮会话和最终普通账号会话均正常退出，原三个账号删除，FD/restorer/wrapper退出、活动任务和锁归零。

原19,836条记录、21条登记（5活动分配、16原退役）、50份报告及原源码/配额/HDD/journal保持；11核心与宿主进程身份保持。120,000,000,000/300,000,000,000字节硬限制、user NOPASSWD及原用户source改动保持。320暗色最终历史实图已查看，无横向溢出。

## 证据和剩余范围

`deploy/artifacts/workspace-retirement-takeover-retry-20261111/`封存 **146文件 / 26,240,927字节**（不含manifest自身），manifest SHA256：
`10cf68d35047b61a3fb4f5315940fd2810e269d875ea095a436e518b79356631`。逐文件复验通过，8个实现/CI源码有摘要和副本；两个浏览器尝试执行的源码摘要完全相同。

重点：`browser-process-1.json`、`takeover-retry-ready.json`、`explicit-retry-preflight.json`、`retry-consumed-1.json`、`takeover-1-restored.json`、`takeover-2-while-revoked.json`、`takeover-retry-accepted.json`、`persistent-original-failure-preserved.json`、`resource-closure.json`与`batch-acceptance.json`。新空间为`a67c5c95-2455-4b70-86bf-f67887f7901a`及`cbdbb809-9f42-4394-b94c-9211270f7745`。旧104文件首轮批次及其中已发现缺口的记录保持原样，不追加改写。

本批验证的是产品恢复POST前的验收中止与显式重试，不签POST后丢回复/产品恢复执行中再次中断；账号禁用/删除接管和删除途中故障仍待。新提交自身准确CI待完成，完整59项/ST272/PET6/供应商Live2D16继续，完整主任务0/59，17项移出不恢复、不计完成，不签master，不为纯QA重复部署产品。
