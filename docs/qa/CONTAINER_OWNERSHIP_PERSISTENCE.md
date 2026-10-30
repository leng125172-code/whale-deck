# 容器所有者：永久SQL、七方写入保护与专用Worker候选

日期：2026-10-30。源码候选`ef08f76`实现永久仓储/迁移及七方保护；`ac5c0f3`追加专用Worker注册、Outbox隔离和禁止通用重试。**尚未部署，没有公开所有者API/Vue或真实人类页面证据。** 不把数据库验证或合成Root回执当作实际fchown/HDD/UI验收。

## 本轮实现

- 独立`container_ownership_intents`，EF migration `20261030143305_ContainerOwnershipIntents`及snapshot；13类CHECK、双OperationJob受限外键、版本/租约/状态索引，唯一发布屏障及原请求/计划/执行/started/三态结果/HDD事实/终态不可改、永久意图不可删除。Down有历史行时拒绝，不自动丢弃历史。
- `ContainerOwnershipRepository`事务串行化于共享advisory gate `41307341`，原登录/幂等、预检与执行双Job、Outbox/事件/不可变审计同事务；有界路径预算、租约/取消/过期/Unknown/当前管理员封存，不自动重复Apply。
- 七方双向发布保护：主机`file_path_locks`及容器upload/directory/copy/move/permission/ownership；所有旧方的应用检查和数据库触发器均纳入ownership。目录层级保留和全局/本人操作预算双向检查；Unknown不释放保护，只有原始回执对账或明确封存后解除。
- 所有者`OwnerConfirmed`与普通权限`ModeConfirmed`分离，保留独立HDD元数据证明；不读取SQL中的命令来代替签名验证。
- DI注册保护器/仓储/用例/dispatcher，专用`ContainerOwnershipWorker`循环与受控告警；通用Outbox明确排除所有者请求/取消消息，通用任务重试禁止重跑chown。公开API/UI仍待接。

## 本轮自动化验证

最终源码本地：3581 Unit通过、214 Integration通过，7个默认原生依赖测试明确跳过。新增EF模型检查验证迁移非破坏、双Job/并发/索引/事实与七个触发器；相关所有者/通用重试228项定向测试通过。全量包含这些检查，不能重复累计为完成数。最终全方案Release构建0警告、0错误；8项范围检查通过。

## 真实依赖验证执行记录

正确入口：`python3 deploy/validation/validate_notification_dispatch_postgres.py`，冻结`ac5c0f3a561150f2f7b078150e7ded028a5d28b3`，回执`deploy/artifacts/native-status/dispatch-96ba5e10abb74ff28e9a5a59ce23f6fd.json`。执行器使用非superuser真实PostgreSQL随机隔离库、只读源码归档与受限SDK容器，finally只清理本次资源。首轮执行失败且已清理：目录清理旧测试仍固定期待permission迁移先拒绝降级，现ownership为最新永久表先拒绝。98bdcca更新该断言并独立执行旧permission guard，保留move/copy/upload/purge等原守卫检查；再次使用专用入口冻结`98bdcca69ff5b84897175511b02c6bbcc5aea042`完整重跑通过：1执行/1通过/0跳过，外部投递0，cleanupConfirmed=true。最终回执`dispatch-080c916b7cf6437ba35ade4c1efc63e9.json`；首轮`dispatch-96ba5e10abb74ff28e9a5a59ce23f6fd.json`仍为Failed并保留，不改写。

套件覆盖真实SQL双连接确认幂等、不可变触发器、UID/GID=0持久化、普通权限/所有者同路径及祖先/后代双向拒绝、七方原始SQL双向保护、Unknown/HDD事实单调、当前管理员封存、取消/过期、审计失败整体回滚、预算和拒绝降级。Root receipt是明确合成输入；不是安装态RPC、实际文件或人类身份证明。

首轮误将`dispatch`作为通用容量脚本位置参数，实际回执为capacity；该容量回归在`ef08f76`冻结源码真实执行通过且清理，单独保留`capacity-5a26b0a6672c4631b2b46681eba201f8.json`，不作为所有者证据。随后改用专用dispatch入口。迁移工具初始缺DOTNET_ROOT及EF生成代码触发IDE0161/CA1861已修正，未关闭分析器；旧失败日志保留。

## 接续门槛

真实PG子门槛已通过；另一个只读进程确认11核心容器ID/镜像/StartedAt/状态/healthy与测试前完全一致，3个本轮SDK容器不存在、三个清理回执为true，匹配本轮命名空间的容量/dispatch临时库为0。SDK固定image ID为`sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。日志分别保存在`/tmp/wd-ownership-*`，证据/临时私有值不入Git。

接管理员API/Vue原属主/目标UID/GID预检确认、接续/取消/Unknown处置与HTTP权限/CSRF/Origin/退出。最后覆盖发布并完成真实身份→Vue→SQL/Worker→Agent→实际文件/HDD与故障整链。仍需递归/ACL、可恢复删除、长期清理及其余59项，不签完整所有者功能。
