# 容器所有者：实时权威身份候选

2026-11-02。发现并修复源码中的执行中撤权缺口，**候选未部署，尚未真实移除组/停用临时账号验收**。最后已记录运行产品仍e91a770/协议1.48，历史四类中断与Unknown见[实机故障](CONTAINER_OWNERSHIP_FAULTS.md)，不能把正常退出当作真实组撤权。

## 原问题与实现

原所有者Worker虽然每6秒调用Authorize，但FileManagementService通过普通IIdentityDirectory读取最长2小时的授权快照；真实Authentik组移除或is_active=false发生在正快照之后时，无法据此承诺6秒内撤权。GET /auth/me也只读取原快照，不是强制刷新入口。原两小时自然过期证明仍独立有效，不替代执行中权威检查。

新增独立IPrivilegedIdentityDirectory，由唯一Authentik实现，DI显式注册到同一身份目录实例，不提供静默缓存或claims回退。所有者用例在验证原actor格式后，先调用权威查询，再通过既有文件权限及原登录liveness检查；所有者预检/确认/查询/取消/异常处置，以及Dispatcher执行前、每6秒巡检和完成后均经过此用例。不扩大其他文件模块、终端或通用Job的验收范围。

- 每次直接查询Authentik固定API，要求恰好一个结果、uuid与原主体精确匹配、显式布尔is_active；按真实groups_obj和当前本地组映射判断管理员。
- 即使普通快照为管理员、配置允许claim fallback，缺token/身份服务失败/身份不匹配也不授权此操作。身份组已移除时返回非管理员，更新普通快照使后续页面看到降权；恢复授权可以被下一次权威查询观察，不受旧负快照阻断。
- 显式停用或无法取得权威身份时移除旧快照并拒绝；既有原登录IsActive检查仍执行，凭据不进入Application/Job/Root/日志。其他普通页面的两小时快照策略保持，没有新增人类本地账号或复制资料。
- 原FileManagementService的actor检查提取为内部公共复用逻辑，规则不变；仅所有者接入新权威能力。没有协议、Root capability、持久化schema或依赖包变更。
- 现有Guarded生命周期在权限检查异常时取消原RPC并等待产品回收；Root事实仍需Inspect/Discard原操作才能判定。已经实际完成的所有者变更仍按原回执对账，不因为晚到撤权伪造“未发生”或自动恢复。

6秒是轮询节奏，不是任意网络条件下的总停止承诺；权威HTTP检查仍受现有单次检查取消预算及RPC/helper生命周期约束。真实停用后的浏览器/BFF所有会话注销、其他模块的即时撤权不由本候选声明完成。

## 本批验证

- 19项PermissionSnapshot集成检查通过，其中新增11项覆盖正快照后真组信息改变、负快照后权威恢复、停用移除正快照、token缺失/HTTP503及fallback=true/false、缺失或错误uuid/is_active。HTTP为明确Stub，不能算实际Authentik服务验收。
- 所有者专项首次69项通过，随后新增2项执行中活跃RPC撤权/身份服务失败测试纳入全量。缓存身份仍为管理员时，实时降权或实时查询异常必须取消原RPC、保留发布保护；只对原执行Inspect/Discard后才能结束，不重Apply。另两项验证未取得实时权限时预检零登记、零Agent调用。
- 最终全方案3585 Unit通过、258 Integration通过、7个默认原生测试明确跳过；Release构建0 warnings/0 errors。未改Vue，没有重复执行前端测试或将旧浏览器结果记为本轮新验收。
- 日志：/tmp/wd-owner-live-identity-tests.log、wd-owner-live-workflow-tests.log、wd-owner-live-all-backend-final.log、wd-owner-live-release-final.log。首个全量命令误写WhaleDeck.sln触发MSB1009，未执行测试；改用实际WhaleDeck.slnx后完整通过，原错误日志保留。

## 接续门槛

冻结并正常覆盖dev候选，确认六组件/生产来源。只对本轮生成的临时Authentik身份执行真实组移除与账号停用：实际登录/预检确认、精确原helper和原inode/HDD切点、权限源变化、产品结束helper、原SQL/Root/审计/HDD及全部字节核对，另一个只读进程确认；页面必须拒绝旧权限且清理原提示。不得用编辑cache/SQL/时钟、改应用组映射代替真实身份变更，也不触碰用户或生产组。

仍需fchown之后/回执丢失等其余故障、递归/ACL、可恢复删除/长期清理和全部59项。此候选不签实时撤权实机完成，也不签完整所有者或P0-02完成。

范围检查首次因更新nextStep漏写“17项移出不算完成”而失败；已补回元数据声明，59主ID及17移出项均未改变，不修改检查规则。

最终记录检查：8项范围测试通过，5份修改MD的500个本地链接无缺失，git diff --check通过。
