# 真实账号禁用后的恢复拒绝与原状态恢复

2026-11-11，使用已部署且CI全13成功的产品356ba6c1。前序组撤权验证器提交5205143e的准确CI42944649886现已全13成功；本批扩展仅为QA验证器与测试，没有新产品部署或权限配置扩展，自身提交CI另待。

## 实际通过

- 新临时管理员/普通用户经真实Authentik、Vue、BFF、Worker和Agent完成新QA空间分配、真实硬配额、源码导入发布、Node测试2/2、.NET独立构建及测试2/2、停用后重新启用Node复验，共4运行成功，零测试失败/跳过。
- HDD Capture/独立Verify后，经独立删除确认与真实open-unlinked配额拒绝，原execute已结算Rejected、空间隔离占预算。仅对该声明的新QA owner核对用户名格式、UUID、数字PK、原单管理员组和活动状态；只PATCH `is_active=false`，组和密码不变。
- 同一已登录上下文携带正确CSRF/Origin与明确继续删除命令，恢复POST和尝试历史GET均403。原Cookie/组角色不能替代实时身份源的活动状态检查。
- 仅恢复原 `is_active=true`。独立SQL对禁用窗口前后workflow、step、workspace与job完整行摘要，以及恢复/completion计数逐一比较相同；没有新恢复任务、预算释放或原证明/计划/时间改写。
- 当前资源读取重新200后，Vue重新独立确认、使用不同于被拒请求的新键创建attempt 1接续原journal。原attempt 0失败保留，最新执行成功退役；内核配额八项全零，源和claim缺席，HDD归档和退役后8份报告保持。
- 相同预算空间再次登记、分配、归档及退役，原19014条记录、11条登记（5活动分配、6原退役）与30份报告保持。原源码/配额、HDD/journal、120,000,000,000/300,000,000,000字节硬限制及user NOPASSWD保持。两临时身份删除、正常双端退出、FD子进程退出、活动任务和锁归零；完整wrapper首次通过。

恢复辅助进程的原状态比较包括活动标志与组，拒绝覆盖其他身份或组变化；PATCH丢响应不得重复禁用，EOF/finally与父层恢复收据栅栏沿用。9项身份边界/禁用/恢复/并发变化测试、5项磁盘证明、14项CI合同及8项范围检查通过。320暗色真实历史图已人工查看，原失败、新成功和预算释放文字清楚，无横向溢出。

本次证明现有登录上下文内敏感操作的实时拒绝；没有进行登录票据清理测试，也不声称账号禁用会自动销毁所有BFF会话。另一管理员恢复、真实Interrupted/Stopped及其他身份/失败矩阵仍待，不计P3-18整项。

## 证据与交付边界

`deploy/artifacts/workspace-retirement-account-disable-20261111/`封存 **71文件 / 25,449,788字节**（不含manifest自身），manifest SHA256
`96227975fd579c77003567c691d89bd666c353d94937a3b1da1e017318f5492b`，逐文件复验。

重点：`authority-1-intent.json`、`authority-1-before.json`、`authority-1-removed.json`、`authority-1-restored.json`、`browser-process-1.json`、`accepted.json`、`identity-cleanup.json`、`persistent-original-failure-preserved.json`和`batch-acceptance.json`。实际执行源码与当前候选逐字节核对通过。无业务SQL写入、原任务重启、宿主进程停杀或自动回滚；旧组撤权批次的最终比较误拒及只读复验原件保留不变。

原用户source.py改动继续排除提交。下一步为本验证器准确CI、其他管理员及真实中断恢复矩阵和剩余59项/ST272/PET6/供应商Live2D16；完整主任务仍0/59，17项移出不恢复、不计完成。不为纯验证记录重复发布产品。
