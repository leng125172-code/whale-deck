# 全部已读：晚提交事务与实际集合快照

2026-10-14：正在验证，尚未发布；线上 `4aa036b` 的序号截止方案不覆盖该组合。完整 P0–P5 仍未完成。

## 缺陷复现

工作站独立 `wdv_snapshot_repro_*` 两张最小表执行旧谓词：较低序号 1 未提交、序号 2 已提交，首次批次改 1 条；提交序号 1 后，第二批又改 1 条，把后来可见的消息误读。执行输出 `lateMessageIncorrectlyRead=true`，测试库已精确删除。这证明 PostgreSQL/旧 SQL 谓词的时序问题，不冒称实际 C# 新方案也已通过。

复现脚本在 `deploy/validation/reproduce_notification_snapshot_sql.py`；新版 actual C# 原生 Fact 在 `NativeNotificationSnapshotTests`，必须另外独立执行。

## 实施

- `POST /api/v1/notifications/read-all-snapshot` 无客户端主体/权限参数；服务端解析当前 Authentik 主体/权限、锁定主体写入，将当时可见且未读的 ID 集合以单条 INSERT SELECT 保存。
- `POST /api/v1/notifications/read-all` `{snapshotId}`，每次最多处理 1000 个未处理成员，逐批再次校验当前主体/权限与事件有效性。旧 `{throughSequence}` 缺少有效 SnapshotId，明确 400，不能继续误读。
- 快照标识不是授权：所有者、管理员权限范围、有效期均匹配才允许消费；不存在/其他主体/降权/失效均为同一种 404 与稳定 `NOTIFICATION_READ_SNAPSHOT_UNAVAILABLE` 错误码。
- 同主体最多一份快照，新准备原子替换旧意图，不撤销已完成的已读状态。闲置 5 分钟过期，活跃批次续期，绝对最多 1 小时；周期保留清理每批最多 10 个过期快照头，成员级联删除。
- 记录每个成员 IsProcessed，返回 ChangedCount 与 ProcessedCount：独立逐条已读导致 ChangedCount=0 时仍可继续；完成快照重试返回 0，不把后来手工标成未读的消息又改回来。
- 不默默加“最多十万消息”的总量截断；ItemCount 为 bigint。准备仍有 SQL 10 秒和 HTTP 截止预算，失败原子回滚且不冒称完成；原生测试覆盖 100005 条的完整集合及单批上限。
- 每批修改/成员进度与原有审计同事务；源事件删除或过期不会变成越权/悬空 FK。保留周期与管理员广播/个人已读、原有消息保留规则不变。
- 前端一次准备、固定 SnapshotId 跨全部批次、不持久化到浏览器存储；关闭/退出/权限变化终止后续请求，已提交批次不自动回滚。失效明确提示重新操作；分页/摘要的 sequence 保留为展示/排序游标，不再当作已读操作的消息集合。

## 当前证据与后续

本机初轮 Array 常量 migration 分析器问题已修正；随后 476 Unit/62 Integration（6 原生本机明确跳过）与 Release 全方案 0 警告/错误通过。104 前端单元、类型、lint、生产构建通过；初轮 14 浏览器通过，最终版本还需复跑。原生新快照、现有消息/投递与远程真实 Authentik/API/Worker、生产资源签收尚待执行，不以本机跳过测试代替。

GLM glm-5.3-flash 仅只读定位：113959ms，输入 120584 Tokens、缓存 90752、输出 3927，1 次成功/0 重试，费用未返回。实际方案、安全、修改和复核由 GPT 完成。
