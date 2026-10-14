# 全部已读：晚提交事务与实际集合快照

2026-10-14：工作站已重连，原 `bc39f46` 更新终态与真实 API 已核实，未重复发布原任务；有界清理增量 `ad0e661` 已原生验证、覆盖发布及真实签收。下方断线/未验收记录是历史，不能继续当作当前状态。完整 P0–P5 仍未完成。

## 缺陷复现

工作站独立 `wdv_snapshot_repro_*` 两张最小表执行旧谓词：较低序号 1 未提交、序号 2 已提交，首次批次改 1 条；提交序号 1 后，第二批又改 1 条，把后来可见的消息误读。执行输出 `lateMessageIncorrectlyRead=true`，测试库已精确删除。这证明 PostgreSQL/旧 SQL 谓词的时序问题，不冒称实际 C# 新方案也已通过。

复现脚本在 `deploy/validation/reproduce_notification_snapshot_sql.py`；新版 actual C# 原生 Fact 在 `NativeNotificationSnapshotTests`，必须另外独立执行。

## 实施

- `POST /api/v1/notifications/read-all-snapshot` 无客户端主体/权限参数；服务端解析当前 Authentik 主体/权限、锁定主体写入，将当时可见且未读的 ID 集合以单条 INSERT SELECT 保存。
- `POST /api/v1/notifications/read-all` `{snapshotId}`，每次最多处理 1000 个未处理成员，逐批再次校验当前主体/权限与事件有效性。旧 `{throughSequence}` 缺少有效 SnapshotId，明确 400，不能继续误读。
- 快照标识不是授权：所有者、管理员权限范围、有效期均匹配才允许消费；不存在/其他主体/降权/失效均为同一种 404 与稳定 `NOTIFICATION_READ_SNAPSHOT_UNAVAILABLE` 错误码。
- 同主体最多一份 IsCurrent 快照，由部分唯一索引约束。新准备仅原子失效旧意图并令其过期，不在请求内级联删除大量成员，也不撤销已完成读状态。闲置 5 分钟、活跃续期、绝对最多 1 小时保持不变。
- 周期保留锁定最多 10 个过期头，所有选中头的成员总删除量最多 1000；只删已无成员的头。FOR UPDATE/SKIP LOCKED 防止续期/FK 插入期间误删。源事实保留的原 1000 条事件预算与读状态/成员级联仍保持；该事件级联的物理行数会随读者/关联数扩大，不冒称所有相关表每轮总计只删除 1000 行。
- 记录每个成员 IsProcessed，返回 ChangedCount 与 ProcessedCount：独立逐条已读导致 ChangedCount=0 时仍可继续；完成快照重试返回 0，不把后来手工标成未读的消息又改回来。
- 不默默加“最多十万消息”的总量截断；ItemCount 为 bigint。准备仍有 SQL 10 秒和 HTTP 截止预算，失败原子回滚且不冒称完成；原生测试覆盖 100005 条的完整集合及单批上限。
- 每批修改/成员进度与原有审计同事务；源事件删除或过期不会变成越权/悬空 FK。保留周期与管理员广播/个人已读、原有消息保留规则不变。
- 前端一次准备、固定 SnapshotId 跨全部批次、不持久化到浏览器存储；关闭/退出/权限变化终止后续请求，已提交批次不自动回滚。失效明确提示重新操作；分页/摘要的 sequence 保留为展示/排序游标，不再当作已读操作的消息集合。

## 当前证据与后续

本机初轮 Array 常量 migration 分析器问题已修正；随后 476 Unit/62 Integration（6 原生本机明确跳过）与 Release 全方案 0 警告/错误通过。104 前端单元、类型、lint、生产构建通过；初轮 14 浏览器通过，最终版本还需复跑。原生新快照、现有消息/投递与远程真实 Authentik/API/Worker、生产资源签收尚待执行，不以本机跳过测试代替。

后续证据：最终 32 项消息/全页布局/搜索/告警浏览器回归通过，320px 暗色消息抽屉截图已走查。冻结源码 `dac41f0` 的工作站独立实际 C# 新快照及原消息原生均执行 1 项/通过 1 项并清理；新快照真实覆盖晚提交、不同主体/降权/过期/替换、完成后手工未读、0 Changed/1000 Processed、并发两批、100005 完整集合和 102018 条分批保留级联。对应原子报告分别为 `snapshots-99c5a730b9384661977932adfea72e4b.json`、`notifications-34218a38c710496bb3db089fccebc733.json`；不把合成消息当成实际用户投递。投递基础最后回归、发布和真实 OIDC/API 签收仍待完成。

投递基础回归也通过并清理，报告 `dispatch-ecf1340f1c67483486e8715a52e84564.json`，同一冻结源码实际 1 执行/1 通过；三份原生报告均 Passed、cleanupConfirmed=true。接下来按用户流程覆盖发布，不自动回滚，发布后的真实权限/Worker/生产资源另行签收。

GLM glm-5.3-flash 仅只读定位：113959ms，输入 120584 Tokens、缓存 90752、输出 3927，1 次成功/0 重试，费用未返回。实际方案、安全、修改和复核由 GPT 完成。

## GLM 审查后的本地增量（未原生签收）

以下为当时本机证据；后续签收见末节。

- 新增 `20261014022643_NotificationSnapshotBoundedRetention` migration：旧头默认 IsCurrent=true，Subject 唯一索引改成 current 条件；保留存量成员。Down 有重复主体时先报固定错误而非删除历史，需要先完成有界清理，不增加 dev 自动回滚。
- `NativeNotificationSnapshotTests` 增加旧 schema 的实际头/成员升级、两次替换十万条集合而不级联删除、旧 ID 拒绝、多主体过期而源事实仍有效、每轮成员总预算/新当前集合保护、100005 条全部 101 批消费、完整读状态与重试，以及 105021 条源事实最终清理。此用例本机明确跳过，**新增路径尚未真实执行**；旧 dac41f0 三份 Passed 不证明新增实现。
- 离线两项模型/migration 测试真正通过；Release 全方案 0 警告/错误，476 Unit/64 Integration 通过，6 原生 Fact 明确跳过。本机 Windows/WSL 未找到可用 PostgreSQL/Docker CLI，未连接工作站或修改服务。前端与 API wire contract 未修改，本轮未冒称重跑 UI/浏览器或真实外部投递。
- 重连先检查同一更新 unit 的终态与实际 runtime/迁移/11 核心健康，再执行原发布版本的真实 API 签收。新清理增量必须冻结新提交，另行重跑 snapshots/inbox/dispatch 隔离原生回归通过后才覆盖发布，不能重复发未确认的原更新。
- GLM Flash 测试边界草案：29816ms、输入11121/缓存0/输出585、1次成功0重试、费用未返回。GPT 修正“所有 current 永不清理”为“仅未过期 current 不能误删”，以及旧 schema 原本全 Subject 唯一的迁移前提，并独立实施与复核。

## 重连后最终签收：ad0e661

先核实断线前更新 unit 为 inactive/dead、Result=success、ExecMainStatus=0，root 部署记录为 bc39f46，真实身份/API/Worker 快照消费与后续恢复未读通过；没有因断线重发。

新源码冻结 `ad0e66127867d558b12438151ea41f1a00876b62`，三个隔离原生用例实际各执行1/通过1，均 Passed、cleanupConfirmed=true，SDK image 为 `sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6`：

| 套件 | 原子报告 | UTC执行区间 |
| --- | --- | --- |
| 新快照/有界清理 | snapshots-8f6318898bc14efda9134f2a1d605a19.json | 03:26:34–03:30:59 |
| 原消息中心兼容 | notifications-22433d54ec074b9998ead5da511eefe1.json | 03:30:59–03:33:12 |
| 投递基础兼容 | dispatch-b52b861118844f7d97e9c5872da1df2a.json | 03:33:12–03:35:45 |

快照原生真正完成旧 schema 头/成员升级、晚提交与范围隔离、100005 条全部101批消费、两次替换延后删除、多主体成员总预算/新当前保护、完成重试，以及105021条源事实最终清理。它没有执行 migration Down 错误路径；Down 保全历史的离线模型检查不冒称原生故障注入。

通过后才提交更新任务 `0d8b40f6-f50a-43c4-b5cd-33fa14fd5060`；任务 Succeeded 只是移交宿主构建，另行等待更新 unit 最终 inactive/dead、Result=success、ExecMainStatus=0。部署记录仍精确 ad0e661，新 `20261014022643_NotificationSnapshotBoundedRetention` 已落库，11核心全部健康，Agent/维护/timer active，sudo保留，无临时原生容器遗留。

发布后重复真实 Authentik/API/Worker：匿名401、普通/管理员范围、隐藏ID404、CSRF/Origin拒绝、消费进度/完成重试/后续恢复消息未读、NoStore均通过。临时普通与wrapper管理员删除，规则禁用恢复，历史/审计/消息按设计保留；没有外部发送。HTTP入口脚本 `/assets/index-aZAgwrDN.js` 与当前Gateway文件完全相同，SHA256=`e15517621100aadc2a6d812d15bedd291824552fa494be11c0b900799fce75aa`，新快照客户端契约存在；这是HTTP资源核对，不冒称现场浏览器登录走查。

此前476 Unit/64 Integration与Release证据的backend代码未变；本轮另3项身份清理ownership模拟单元及2项原子报告器测试通过。修复wrapper仅清理本次sso_slug/关联provider，不再按共有前缀删除其他测试会话；模拟断言与实际身份账户清理明确分开。完整外部通知/配置UI/传输/限流/签名等继续，P0-07/P2-12/P4-06整体仍部分实现，76项未完成。
