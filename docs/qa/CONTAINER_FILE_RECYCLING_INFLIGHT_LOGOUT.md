# 容器回收执行中正常退出：HDD已验证、源未移除切点

2026-11-05，产品保持58b8174，门禁c149b67f；本批没有覆盖发布产品。承接[同切点明确取消](CONTAINER_FILE_RECYCLING_INFLIGHT_CANCEL.md)，以全新自有1,048,583字节普通文件、真实Authentik及Vue完成执行中正常退出。仅签本场景，不替代其他故障矩阵或完整删除/恢复业务。

## 已签行为与证据顺序

真实异步Ready后唯一确认执行。观察器按原SQL执行ID、完整计划与源inode/字节/属性，复核当前systemd MainPID、InvocationID、cgroup、原操作锁及容器根两个内核FD后，使用原pidfd暂停准确helper。切点为Preparing、historyVerified=true、私有空目录已绑定、源文件尚未移除，HDD原件已完整验证。

页面关闭回收弹窗与容器抽屉，通过实际用户菜单正常退出。门禁只在观察真实BFF导航POST `/api/v1/auth/logout` 后生成标记，并再核对原pidfd仍存活，拒绝在退出请求前已经退出的进程；不能靠“准备退出”标记推断先后关系。原pidfd从允许页面动作前开始并发观察，1.972293秒内退出；计时包含关闭弹窗、菜单与退出准备时间，产品停止原helper，QA未杀进程或编辑SQL/Root/HDD。

两次正常退出均取得BFF导航302、真实IdP end-session导航、BFF会话401、IdP会话403及最终登录页。第一次退出后原意图GET401；使用同一账号重新正常输入凭据，确认同Subject/管理员身份后，旧登录所属意图GET404。随后仅通过当前管理员恢复GET读取原任务，没有重新Apply或发送取消：实际登记1次、确认1次、取消POST为0，SQL cancel_requested_at_utc为空。第二次正常退出用于清理新会话。

原任务/唯一执行Job最终Canceled，Root Aborted、sourceRetained/sourceRestored均false、historyVerified为true，保护与租约释放，Outbox清零；规划成功与唯一登记/确认审计保持。源原inode、字节、扩展属性及九字段身份完整保持，HDD captureState=Canceled、完整历史原件和摘要通过。真实Jobs页面按原执行ID选中任务并显示取消安全摘要；“Canceled”是退出后产品收敛状态，不代表请求过取消接口。

普通用户8次拒绝、匿名4次拒绝，临时身份删除及自有资源精确清理通过。独立进程清理前重新读取SQL、Root/HDD原始JSON、完整HDD数据及源文件；清理后再次读取永久记录并核对自有容器/目录缺席。仅清理已证明原件及已绑定空私有目录/自有容器，Root/HDD历史保留。2346保护文件、11核心healthy、旧所有业务与历史保持；回收Root10→12/HDD13→15，前批未验证Preparing metadata与partial仍完整保留。源与HDD同设备，不签异盘灾备。

## 工具、启动失败与封存

新增6项门禁测试，关联53项工具测试以ResourceWarning为错误通过；8项范围、Python编译及Node语法通过。真实临时子进程/pidfd测试覆盖“实际退出请求被观察后仍活着”与“过早退出必须拒绝”，会话证明负测覆盖取消POST、未清除BFF/IdP及重登隔离失败。

首次systemd验收进程因Git dubious ownership提前拒绝，未创建测试证据目录、身份或资源，也未执行回收。保留失败unit `whaledeck-qa-recycle-logout-23ade334d853`、Invocation `bfb53768718b4dc6b88cd711300d1e4d`及原journal；后续仅向该验收进程显式提供当前仓库safe.directory，不修改全局信任、不改变产品或撤销属主检查。

成功unit `whaledeck-qa-recycle-logout-b0e6d8cd3bf0`，Invocation `08f3c99f37d8444c9e1cee985fdd8543`，准确Result=success/ExecMainStatus=0。原helper PID1200773、Invocation49e552cc36f64783bdeedc5d75cc8a13。意图604ba397-8caf-db26-457e-51f573e7187e，唯一执行4f1e0cb2-276f-3f8e-6c44-2bd911796be3。

证据目录 `deploy/artifacts/container-recycle-inflight-logout-48438c894a294434a8752664db23bf09`，127文件（含96份冻结验证源码、启动失败回执、两张真实320×600暗色截图、清理前后独立证明）。EVIDENCE_SHA256.json摘要：`2f4bb70f8f651312dfcd9ff88855676b80d6ac5576f57f5a394267f73c8b2329`，逐文件重算通过。证据为主机私有权限，未提交真实账号、凭据或内部回执原件。

## 未签范围与下一步

仅签HDD已验证/源未移除时的正常退出与同账号重登隔离，不签移除后的退出、主动撤权/停用、Worker/Agent重启、Unknown或跨写入组合。继续上述故障矩阵、独立恢复业务与页面、长期保留/彻底清理及其余59项。53项本地工具通过不等于远端CI终态，本批尚未核验新候选的准确CI终态；生产58b8174既有CI与部署证据保持。

整体25%–35%为低置信度工作量粗估；59项整项签收0/59、六阶段完整签收0/6及AI子计划0%保持，不因分项验收提升主任务完成率，17项移出不算完成。
