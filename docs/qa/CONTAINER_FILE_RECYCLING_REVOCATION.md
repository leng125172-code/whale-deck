# 容器回收执行中实时移组与账号停用

2026-11-05，产品保持58b8174、门禁d413cb97。承接[同切点正常退出](CONTAINER_FILE_RECYCLING_INFLIGHT_LOGOUT.md)，分别用全新自有容器、1,048,583字节普通文件和临时管理员完成移出管理员组、停用账号两项真实验收。本批未发布产品，生产与两宿主仍58b8174；仅签HDD已验证/源未移除切点。

## 实际切点与身份变更约束

原会话通过真实Authentik和Vue生成异步Ready并唯一确认。父进程使用原SQL执行ID，复核完整计划、源inode/字节/属性、systemd MainPID/InvocationID/cgroup、容器根与原操作锁双内核FD，才以原pidfd暂停准确helper。两次原Root均Preparing、historyVerified=true，源仍为完整原件，私有空目录已绑定。未到切点则失败，不重发Apply。

仅使用本轮生成账号的精确username/Subject/数字PK及管理员组绑定，由既有受限OwnedAuthentikRevocation执行一次PATCH。移组只改groups为空并保持账号启用；停用只改is_active=false并保持组。写前重读完整身份、写后再次GET确认，丢响应不重发；凭据和Bearer仅驻留进程内存，不写Git或证据。

先确认原pidfd仍活，再从身份变更前开始并发观察；变更确认后再次证明同一pidfd尚未退出，随后观察产品停止原helper。原执行距截止分别仍有894.396948秒和894.266231秒，超过10分钟，不能解释成自然到期；这里证明的是执行期限，不把它冒充身份缓存TTL。门禁不杀原helper、不修改SQL/Root/HDD，也未发送取消或原会话退出。

## 两项真实结果

| 场景 | 原helper退出时间 | 原意图GET | 原auth/me | 实际页面 |
| --- | ---: | --- | --- | --- |
| 移出管理员组 | 1.789474秒 | 403 | 200，同Subject、isAdministrator=false | /forbidden，“没有管理权限” |
| 停用账号 | 1.770008秒 | 403 | 403 | /login，“使用工作站账号登录” |

两次均在320×600暗色原页面验证旧回收任务区域、弹窗和sessionStorage提示引用已清除。每次原登记1、确认1、取消POST0、原会话退出POST0，SQL cancel_requested_at_utc为空；不把被动失权称为正常双端退出。

分别新建另一名临时管理员，通过隔离的新浏览器上下文正常输入凭据登录，只读GET原恢复结果和实际Jobs页面。确认不同Subject、原意图/唯一执行ID保持，Canceled/Root Aborted和原执行Job的取消安全摘要可见；当前管理员没有非认证管理写入，未确认、重试、取消、ACK或恢复原执行。该管理员各正常双端退出一次，实际BFF导航302、IdP end-session导航、BFF401/IdP403与登录页通过。

原任务最终Canceled，原Root Aborted、sourceRetained/sourceRestored均false、historyVerified=true，原保护/租约释放、Outbox清零，规划Succeeded及唯一登记/确认审计保持。源原inode/字节/扩展属性/UID/GID/模式/mtime/ctime完整不变；完整HDD原件验证通过，captureState=Canceled。清理前独立进程重新读取实际源、SQL、Root/HDD JSON及完整HDD数据；仅在独立通过后精确清理自有文件、已绑定空私有目录和容器，再独立重读永久记录/核对资源缺席，临时账号删除确认通过。每次另有普通8拒绝、匿名4拒绝。

第一组Root12→14、HDD15→17，第二组Root14→16、HDD17→19。2346保护文件、11核心healthy、两宿主及所有指定旧业务保持，前批未验证Preparing metadata与partial保持。源/HDD同设备，不签异盘灾备。Root/HDD永久历史不因源资源清理而删除。

## 工具与证据

新增6项证明/边界测试，关联59项回收工具测试（ResourceWarning作为错误）、12项临时身份约束测试、8项范围、Python编译与Node语法通过。负测覆盖错误身份/额外字段/错误变更类型、期限临近、权限页面不符、未清引用、同主体恢复、取消/退出/管理写入及IdP未清会话。新门禁未改生产接口或授权逻辑。

两组均准确Result=success/ExecMainStatus=0，各126文件、98份冻结验证源码及两张真实截图，逐文件SHA复算通过。证据目录均在deploy/artifacts下，保留私有权限，不提交凭据或原始内部回执。

| 场景 | container-recycle-revocation-目录后缀 | EVIDENCE_SHA256.json摘要 |
| --- | --- | --- |
| 移组 | f94a94ea3433451189fcd48809f08a62 | 927a8762730c0838353055d20fa006d485a4731cd50743712ac648822adc5136 |
| 停用 | 10e69af9c924454ea9309e74066a1759 | 865f6eb3533ac6ce445a4c5dd8c541a39d3ada4dd0fb1678301524aa69786032 |

移组unit `whaledeck-qa-recycle-revoke-471c1664b7ab`、Invocation `0bd0050e7ef64309a193045666555980`，原helper PID1229816/Invocation5c07f56641f44ea5afc49f5776a88ce6，意图acd5d1f6-66e7-55b3-bb96-7d468edf5f94、原执行a149f254-d9c1-bf51-4ece-e40226381e7d。

停用unit `whaledeck-qa-recycle-disable-0c85d9a975b8`、Invocation `4c2ccc3e5a3b41519cb4601837015537`，原helper PID1238062/Invocation034312ef7bc340fa875a3eb79ae205df，意图f4e495ce-887f-00eb-5ba2-eb72e64786ef、原执行21876b68-67cc-f66e-be5a-3ec1eff929ba。

## 仍需完成

仅签上述两个精确切点，不签移除后撤权、Worker/Agent重启、Unknown/跨写入、独立恢复业务、长期保留/彻底清理或完整删除能力。本批新候选准确远端CI终态尚未核验，既有生产部署证据保持。下一步推进进程重启/Unknown等矩阵，再独立恢复业务及其余59项。

整体25%–35%仍为低置信度工作量粗估，59项整项签收0/59、六阶段完整签收0/6和AI子计划0%保持；17项移出不算完成，验收工具新增不自动更新旧产品代码量快照。
