# 容器所有者：内部业务编排与签名候选

后续更新：永久仓储/EF迁移、七方双向保护及专用Worker候选已实现，并通过真实隔离PostgreSQL，见[新批证据](CONTAINER_OWNERSHIP_PERSISTENCE.md)。下文保留deef721内部编排批次的原始测试与当时接续点；不代表当前源码仍缺SQL，但生产覆盖/API/Vue和真实页面仍待。

日期：2026-10-30。状态：**内部候选已实现并通过自动化验证，未接SQL/Worker注册/API/Vue，未部署，不是完整所有者验收。** 安装态引擎/1.48 RPC的既有证据见[前批记录](CONTAINER_OWNERSHIP_AGENT_RPC.md)。本轮不重新使用旧HDD/页面证据冒充新业务通过。

## 实现

- `ContainerOwnershipIntent`及仓储端口区分原请求、预检、确认执行、租约/取消/发布保护、Unknown与当前管理员处置；Domain实体尚未映射生产表，不是已完成持久化。
- Request/Plan/Execution分别有独立业务HMAC域，确认token另有独立域；稳定请求/执行ID和OwnerHash绑定主体/原登录/幂等键。严格canonical JSON和时间/大小/额外字段校验，不能跨普通chmod复用。
- Queue/Get/Confirm/Cancel与Unknown查询/处置分开；队列请求不直接扫描或fchown。预检与执行之间必须确认，过期读取不延长期限；同一原请求重试不创建新执行。
- Dispatcher调用现有强类型所有者Gateway，复核原登录/管理员、期限、取消和租约；已开始但响应丢失只观察或封存原操作，不能再次Apply。无回执不等于未修改；Unknown与HDD可能存在保留，其他管理员处置不伪造原登录或成功。
- OriginalUid/Gid来自inode，与容器执行FS身份及目标Uid/Gid分开。0为合法目标，uint.MaxValue sentinel拒绝；非递归、mode/ACL/目录子项不属于此命令。

## 验证（本轮实际运行）

```text
dotnet test tests/WhaleDeck.UnitTests/WhaleDeck.UnitTests.csproj --configuration Release --no-restore
Passed: 3579, Failed: 0, Skipped: 0

dotnet build WhaleDeck.slnx --configuration Release --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)
```

新增101项所有者业务/签名用例包含文件/目录、原登录/当前管理员、确认/过期/取消/撤权/租约、响应丢失/Unknown/不可重Apply、不可变字段篡改、UID/GID为0及接近上限、NoChange基于原inode而非执行身份、原/目标属主区别和普通权限签名域互拒。首轮89项专项通过后补齐12项所有者特有边界，再以完整3579项重验；测试使用明确内存Repository/Gateway，不称真实PG/安装态/实盘或人类UI证明。

## 未完成与接续

先实现EF migration及真实PostgreSQL仓储/约束/双Job/Outbox/原子审计，接七方容器写入及主机路径发布保护，确保双向互斥、Unknown保护和当前管理员封存成立；随后注册专用Worker、管理员API与Vue原UID/GID预检/确认/接续/取消/Unknown界面。公开写入必须在完整持久化/保护就绪后开放。

用真实身份→Vue→API/Worker→已装Agent→实际文件/HDD逐链验收，补取消/退出/撤权/Worker/Agent重启、发布与恢复故障。完成后继续递归/ACL、可恢复删除与长期清理及其余59项。不能将本轮101个内存用例代替这些门槛。

### 下批文件定位（只读核对）

- 新增`ContainerOwnershipRepository`及`PlatformDbContext.ContainerOwnerships`，在主DbContext添加DbSet/配置；使用真实EF migration和snapshot，不能直接把Domain实体当表已存在。
- 七方数据库发布保护为`file_path_locks`、`container_file_upload_operations`、`container_directory_operations`、`container_copy_intents`、`container_move_intents`、`container_permission_intents`及新增ownership意图；沿原事务锁和双向触发器扩展，每一方都须拒绝另一方在途/Unknown barrier。
- 同步`FilePathLocks`及上传/目录/copy/move/permission仓储的开始和路径预算检查，不能仅在ownership仓储单向检查。
- DI目前仅有所有者gateway；protector/repository/workflow/dispatcher和专用`ContainerOwnershipWorker`/HostedService尚未注册。SQL/保护具备后再开放公开入口。
- `NativeNotificationDispatchTests.ContainerPermissions`和`ContainerPermissionModelTests`的既有保护断言需保留，并新增ownership原生PG并发/约束/审计失败/过期/Unknown场景。普通权限的ModeConfirmed不得复制为所有者事实；所有者保留OwnerConfirmed及独立HDD元数据证明。
