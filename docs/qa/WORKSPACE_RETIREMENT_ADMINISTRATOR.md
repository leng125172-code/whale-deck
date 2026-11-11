# 另一管理员通过独立登录恢复退役

2026-11-11，使用已部署产品356ba6c1。本批只增加QA验证器、归属证明检查及CI门禁，没有新产品部署或正式Agent授权扩展。原owner保持有效，第二管理员只执行原owner空间的恢复；不转移空间所有权。

前序f735c2342e914b421a837c5f7a0458cfe5d50315准确CI42954293033已全13成功，最终绑定收据另存 `deploy/artifacts/workspace-retirement-stop-record-20261111/exact-ci-final.json`。本批新验证器自身准确CI仍另待。

## 实际验收

- 原owner管理员A与普通账号经真实Authentik/Vue/BFF完成新QA空间分配、强制硬配额、源码导入发布、Node测试2/2、.NET独立构建和独立测试2/2，启停后Node复验，共四运行成功、零测试失败/跳过。HDD Capture及独立Verify后，页面另行确认删除。
- 真实open-unlinked源码FD阻止配额释放，原execute结算Rejected，空间保持AttentionRequired和预算。原失败任务与计划继续保留。
- 临时管理员B在全新browser context中通过可见用户名/密码流程独立登录；不传storageState、不复制Cookie或Token、不替换A的上下文。B的真实Subject与A不同，当前管理员身份可读取A的空间，owner字段保持A。
- B在该上下文中核对原失败历史，关闭QA持有FD后，页面独立勾选继续删除、使用新幂等键创建attempt 1，并经真实Worker/Agent成功退役。恢复及完成验证、历史读取和320/1440截图均使用B的上下文；320暗色实图已查看，无横向溢出。
- B通过正常用户菜单退出，BFF与身份源会话已清理并关闭该上下文。A原上下文`auth/me`仍200且Subject保持A，随后由A完成报告下载、相同预算空间登记/分配/归档/退役及自己的正常退出；普通账号仍对恢复和历史403。
- 独立只读SQL核对原空间owner和原job actor均为A，新workflow execution actor、不可变恢复账本actor及确认恢复审计actor均为B；previous task、当前task、恢复记录与completion完整配对，恢复计数1，workflow与恢复账本login binding一致。只保存一致性布尔值，不导出binding或Token。原失败证明/请求/计划、派发/结算/首次确认及有效期保持。
- 两空间均经产品流程归档退役，内核配额八项全零、源和claim缺席、预算释放并可复用、HDD树与退役后8份报告保持。三个临时账号正常清理，FD进程正常退出，wrapper成功退出，活动任务和锁归零。

完整wrapper首次通过，无修复重试。原19,508条记录、17条登记（5活动分配、12原退役）、42份报告及原源码/配额/HDD/journal保持；11核心服务和宿主进程身份保持，120,000,000,000/300,000,000,000字节硬限制、原用户source改动及user NOPASSWD保持。

## 证明及交付边界

5项归属检查测试覆盖恢复者被原owner替换、owner/原job转移、旧completion或错误任务链、绑定/审计缺失、重复恢复及会话隔离/正常退出失败。原9项身份、5项磁盘、14项CI合同及8项范围检查通过；新JS/Python入口与归属门禁已加入CI。编译不能代替上述独立测试。本批提交自身准确CI另待，不为纯验证器重复部署产品。

`deploy/artifacts/workspace-retirement-administrator-20261111/`封存 **93文件 / 25,735,289字节**（不含manifest自身），manifest SHA256：
`bbf2f54291103d86422598fa016d3a373f75ab3b233325f36a1f66599cce9202`。逐文件复验通过，7个实现/CI源码有摘要和副本。

重点：`state.json`中的`recovery.administrator`、`persistent-administrator-recovery.json`、`persistent-original-failure-preserved.json`、`accepted.json`、`batch-acceptance.json`、`identity-cleanup.json`、`resource-closure.json`与实际窄屏截图。新空间为`3cebfd0d-bb39-40c2-b5a7-59e21465004e`及`7483e7fd-fc1c-45bd-ae17-bdfe0e11cab3`，原失败、恢复成功、HDD、报告及审计继续保留。

本轮原owner始终有效，不签“原owner被撤权/禁用/删除期间由B接管”的组合门槛，也未证明删除已开始时的中断。相关故障组合及其余59项/ST272/PET6/供应商Live2D16继续；完整主任务0/59，17项移出不恢复、不计完成，不签master。
