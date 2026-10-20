# 目录清理持久化候选（2026-10-21）

状态：active、未部署。接续 [业务材料](FILE_TREE_PURGING_BUSINESS_MATERIAL.md)，不是完整文件管理或 P0–P5 76 项验收。

## 实现边界

- 新增 EF 模型、additive migration 和 PostgreSQL 仓储；确认事务统一重验本人登录、签名、capture/scope、版本与短时 TTL，创建第二个 Job、Outbox、事件、审计及捕获 token/来源层级锁。相同确认只返回原执行，不创建第二次 Root 操作。
- 清理意图作为永久组件事实账本：签名 Root 回执与首次观察时间不可替换，正面移除事实单调，已完成记录不可改写，禁止删除；迁移降级遇非空记录拒绝。捕获目录原签名载荷不变，不修改或删除原 catalogue。
- 原件与归档分别折叠为 Retained/Purged/Uncertain；RootStarted 丢结果不伪造 Retained。流式读取全部执行历史，不用截断的最近 N 项掩盖旧删除事实；不能验证签名则失败关闭。
- 历史列表覆盖清理状态及活动标记；普通恢复和覆盖恢复均拒绝不可确认保留的归档与活动清理。仅清理原件、归档完整时仍可发起恢复。确认和 Root 准入再次核对，复用原恢复捕获锁，不靠页面禁用代替后端锁。
- Unknown 保留锁与非终态 Job；丢响应后只 Inspect/Discard，不重新 Apply。同登录人工确认保留既有签名回执和不确定性；仍不替代第 11 类当前管理员跨账号接管。
- 当前只注册签名器以供既有历史消费者使用；未接 Worker hosted loop、清理 Controller 或 WebUI，未执行生产 migration/清理任务。

## 已验证

- Release 构建零警告/错误；完整本机 2124 Unit / 147 Integration 通过，6 项原生依赖测试明确跳过。
- 新模型测试检查 migration snapshot、3 个 Restrict 绑定、索引、约束、不可变触发器和非空降级保护。
- 源码 `563f48a60cb808ca3aeb06f59c08db1496c53f2e` 已在工作站独立测试库跑完整原生编排：1 项实际执行/通过、0 跳过，包含新增清理分支以及原有真实升级/事务回归；非超级用户、SDK内容地址固定，完成记录 `dispatch-c8db79ec52f84a1f9a9d46c575bec2ec`，临时测试数据库/容器清理 `cleanupConfirmed=true`。不是新增一组数百项原生测试，也不把本机跳过计为通过。
- 原生用例包含 Trash/Overwrite、Archive/Originals/All、并发确认/争用、层级锁、独立组件顺序清理、丢响应/回执不可用、永久 Unknown 事实、取消/降权/超时、事务审计失败、预算及非空降级拒绝。Agent/身份在该 SQL 用例中明确为 transport stub，不冒充物理删除或真人身份验收。
- 前序 18fc8bd CI `39497508770` 四路 success，仅证明前序提交，不冒充本次 SQL 候选。
- 本批 `563f48a` CI `39503733202` 的 backend/native-postgres/installer 已 success，frontend 尚在运行，不能提前声称四路全绿。本机 NuGet 包安全审计未列出已知易受攻击依赖。
- 复验两宿主服务 active、11 核心容器 healthy；客户端绕过代理直连现有工作台端口 HTTP 200。初次误测未监听的 80 端口返回 000、远程继承代理的 HTTP 检查返回 502；更正为现有 8080 和直连后成功，没有据此修改监听/代理/服务。
- GLM 5.3 Flash 只读核对旧表名和索引（输入 43955、缓存 26048、输出 823 tokens、39215ms，资源包费用平台未返回）；只读协作代理核对测试遗漏。GPT 实现并复核事务、权限/永久证据规则与最终验证。

## 后续

接 Worker/API、第 11 类管理员接管与 UI/真人权限/错误态，并继续观察本批CI终态。未知及孤儿对象、长期元数据策略、容器文件、异盘/安装态中断和全部76项目标继续。当前生产 Agent/MaintenanceHost 56101c8/1.36、API/Worker/Gateway 2c8d1bb，没有因本批源码而覆盖服务。
