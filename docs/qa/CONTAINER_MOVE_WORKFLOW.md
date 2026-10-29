# 容器移动业务候选：永久意图模型与调度边界

2026-10-29新增源码，**未接SQL/EF迁移、Worker注册、HTTP或Vue，也未覆盖应用容器**。安装态Agent/MaintenanceHost仍53701f8/minor46，API/Worker/Gateway仍7b3f44e。[安装态与跨盘证据](CONTAINER_MOVE_AGENT_RPC.md)是前一子阶段。

## 候选内容

- 独立 `ContainerMoveIntent`、请求/计划/确认执行绑定与仓储原子事务契约；不复用复制的业务ID、owner或签名域。
- 原subject+login+幂等键生成永久请求ID；执行ID独立且确定。首次15分钟预检，后台生成完整源计划与5分钟确认；读原任务或原key不续期、不重规划。确认记录有独立15分钟执行期限。
- 请求、完整强类型移动计划、确认执行分别MAC绑定；确认token独立命名。严格规范JSON、禁止重复/未知属性、精确保留uint64和纳秒；容器Move业务与宿主目录Move/容器Copy分别拒绝对方token。
- HTTP尺寸的应用服务只登记/查询/确认/取消；完整扫描与移动交由独立dispatcher。每次Root调用前复核权限/登录/租约，进行中每6秒复核，退出或失租取消RPC。
- Apply前原子登记RootStarted与共享发布屏障契约；失联后先观察原操作，有RootStarted却没有回执时只封闭原ID，不自动重发Apply。未知保持屏障；已完成但当前物理绑定变化显示Superseded。
- destinationPublished/sourceRemoved/sourceRetained进入nullable模型和DTO，Unknown保持未观察事实；Completed必须三true，Aborted必须三false。DTO明确OperationKind=Move，确认提示说明原路径移除与私有原件保留。
- 当前管理员可以对历史Unknown单独确认封闭，始终使用原执行ID/原actor上下文查询Root，随后由事务仓储进行当前管理员审计和保护释放。模型契约不等同数据库已实施。

## 验证与限制

- Release零警告错误；新增96项业务/绑定用例，全量3187 Unit和205 Integration通过，7项原生依赖夹具默认跳过。
- 使用真实Application服务与独立临时HMAC密钥，但仓储/Agent均为明确的内存测试替身，不声称已落库或操作真实容器。
- 覆盖原登录隔离、普通与撤权拒绝、幂等/过期/明确确认、原请求与计划全部身份字段防篡改、跨签名域拒绝、取消/完成竞态、已开始失联不重做、Unknown管理员封闭和三态事实。进行中取消与失租用例分别实际经过六秒复核。
- 首次构建发现 `VerifyMoveConfirmation(string)` 与宿主目录协议同名，已改为独立ContainerMove命名，旧方法未改动。初始86项通过后新增10项移动专属事实/跨域/强类型计划用例，最终全量通过。

## 下一步

实现EF模型/迁移、事务仓储、Job/Outbox/审计、与既有所有文件写入的双向共享屏障、租约恢复与三态事实保留；用真实隔离PostgreSQL验证并发、取消、事务回滚及审计原子性。之后注册Worker/HTTP、增加Vue计划确认和Unknown处置，执行真实身份/文件/重启/页面验收。59项保持active，17项移出不计完成。
