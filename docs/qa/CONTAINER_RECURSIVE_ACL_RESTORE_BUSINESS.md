# 递归 ACL 历史恢复：内部业务编排候选

日期：2026-11-05。功能提交 `70862f8e8afd5c1fe15d0c8600ee9325e584e7b4`。**本批完成独立意图模型、编排、签名和 SDK 错误分类候选，未完成真实数据库持久化、共享写入排他、Worker 注册或业务页面整链。** 不把内存测试仓库当作持久化实现，不增加 59 项主任务的完整完成数。

## 实现

- 新增独立 `ContainerRecursiveAclRestoreIntent`、Repository/Protector 契约和 WorkflowService/Dispatcher。没有塞进旧普通 ACL 的 command，也没有把来源的逐 inode ACL 展开成请求列表；持久请求只选择 captureId/来源摘要/当前版本。
- 独立 request/owner/intent/execution/plan/receipt/confirmation 域，防止与旧普通递归 ACL、所有者、权限或其他历史恢复重用授权。完整 plan 的来源摘要十字段、来源文件九槽 frame、异构目标摘要与当前树信息全部进入业务 MAC 和确认绑定。
- 标准 JSON 必须规范重序列化、字段完整且无额外字段，保留 uint64 与纳秒精度及显式零值。请求和完整计划在 await 前冻结，避免外部提供者的对象别名影响已签结果。公开意图 DTO 可显示来源摘要和当前计划计数，但不暴露 raw frame 或逐 inode 原 ACL。
- 每次授权先查当前权威管理员、权限有效期，再检查文件权限和原登录是否有效；规划/写入前后与进行中继续核对取消、期限、登录、当前权限和租约。缓存仍显示正权限，不等于可忽略实时撤权。
- 规划与执行分离，需要显式确认，绑定原登录、原计划和独立 execution ID；幂等返回不续期，换来源或当前版本不能重用原 key。Partial/Unknown 保留保护；另一当前管理员只能明确确认后 Discard **原 execution ID** 并保存原事实，不恢复 ACL 或自动改报成功。
- 执行始终先 Inspect 原回执。曾 Started、超期、取消或失权后，只有观察和原 ID 的 Discard；丢失回执不重新 Apply。Root Completed 且当前绑定失效映射 Superseded，不改写历史结果。
- 新 SDK 仅将精确状态/错误码的规划拒绝转换为有限业务代码。Apply 的来源拒绝可能发生在部分写入后：Dispatcher 先 Inspect 原 ID，只有明确 NotFound 才 Discard；Partial/Unknown/Completed 都以实际原回执为准，不把异常当成“零写入”。观察/围栏本身不包装成重新验证来源的业务动作。未知文本、错误状态、一般连接/HDD 故障仍保留为不确定传输错误。

## 验证与证据边界

| 实际检查 | 结果 |
|---|---|
| Release 全解决方案构建 | **0 warning / 0 error** |
| 恢复协议/SDK/业务定向 | **296 项通过**，包含前序142项与本批154项，不重复相加 |
| 后端完整单元测试 | **5046 通过 / 0 跳过** |
| 后端完整集成测试 | **612 通过 / 7 默认原生依赖跳过** |
| 59项范围与 diff | **8项通过**，diff 检查通过 |
| 运行态只读保全 | 1434份标准保护文件、restore Root32/独立HDD46及普通Root的原字节/九字段与前批封存逐项相同；11核心healthy，两宿主PID/来源仍e066ec1；38 intents / 35 executions / 73 jobs保持，活动工作/helper为0 |

新增验证包含来源摘要十字段/frame九字段的绑定或严格拒绝、零值/精度、targetsDigest、旧签名域隔离、正确 MAC 下的非规范确认拒绝；普通/撤权/退出/超期/缓存与身份故障；续租/屏障忙、丢回执、后置撤权、Completed失去当前绑定、Partial/Unknown与跨管理员处置；修改 receipt JSON 与匹配列不能绕过 receipt MAC；有限来源拒绝先观察/再围栏与围栏不可达时保留保护。

业务测试明确使用内存 Repository、身份/登录和 Agent 提供者夹具，SDK使用隔离 HTTP/gRPC 响应。上述测试不证明 SQL 原子状态转换、共享 writer 排他、真实 Authentik、真实 Worker 或页面；7项跳过的原生依赖检查没有被改报为通过。本批无新增 schema 映射，未注册生产 Repository/Workflow/Worker/API，没有部署或重复执行前批实机操作。

首次测试编译因新夹具的 `AfterRenew` 尚未用于测试而触发 CS0649；补齐“RPC 后续租前冻结来源 frame”的真实测试用途后通过，没有关闭 TreatWarningsAsErrors 或删除守卫。实现复核未发现当前内部编排的阻断问题，但以下 Repository 契约仍必须实现，不能以本批单元测试代替。

## 下一步

1. 新增真实 EF 映射与迁移、永久 planning/execution Job、Outbox、审计和租约；确认期限/版本 CAS、Started 围栏及结果/事实单调更新必须在数据库原子落实。
2. 将新恢复意图加入所有文件 writer 的双向路径冲突、物理注册挂载作用域及全局 publish barrier；包含跨容器/目录别名、Partial/Unknown 不释放保护与明确 ACK 释放。用独立真实 PostgreSQL 验证并清理测试库，不能只验证模型元数据。
3. 注册独立 Worker 和业务依赖；禁止通用 retry/outbox 将原恢复重新排成可写任务，再接管理员 API/Vue、实际 Authentik → 页面 → SQL/Worker → Agent → 文件/HDD 整链及完整故障矩阵。
4. 继续可恢复删除、维护入口连续性及其余59项。前批维护入口4/67非200、源/HDD同device2049的边界保持；本批没有补签连续性、异盘或完整页面验收。user NOPASSWD按授权保留，dev不自动回滚。


后续接续：f7d6a68真实仓储/EF迁移、共享写入保护及Worker候选已通过独立PostgreSQL验收，见[持久化批次与边界](CONTAINER_RECURSIVE_ACL_RESTORE_PERSISTENCE.md)。本文70862f8当时的内存夹具/缺口与原测试记录保持，不据后续数据库分项补签生产迁移、Worker循环或真人页面整链。
