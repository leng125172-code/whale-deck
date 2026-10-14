# 通知显式测试与未知结果人工重试

2026-10-14。本增量属于 P4-06 的子项，不代表通知模块或完整 76 项完成。

## 行为与边界

- 管理员 `POST /api/v1/notifications/manage/channels/{id}/test` 返回 `202 + deliveryId`，不创建平台 Job；确认、CSRF、Origin、UUID 幂等键、渠道版本与审计必须通过。固定测试事实存活 5 分钟，每渠道 5 分钟最多 10 条；可明确测试停用渠道，但不改变渠道开关，也不向其他渠道扇出。
- `POST /api/v1/notifications/manage/deliveries/{id}/retry-unknown` 只接受 Unknown、当前启用且版本未变的原批准渠道、未过期管理员事实、已绑定且未变的私有配置。必须明确接受可能重复送达，并提供投递/渠道版本。原状态与尝试历史不可变；创建关联 ManualRetry 子记录，每个父记录最多一个子记录，链深有界。Succeeded/Failed/取消记录不可使用此入口。
- 网络仍在数据库事务和数据库自动重试之外。Unknown 不自动重发；人工子记录允许既有的确切未发送退避预算，不意味着 Unknown 可以自动重发。第 5 次明确未发送失败应为 Failed，不是 Unknown。
- Worker 在执行显式意图前再次解析请求者的 Authentik 管理权限；最长 2 小时权限缓存沿用既定策略。撤权取消；身份依赖不可确认时不调用传输。生产不使用测试授权器或放宽网络策略。
- 前端取消确认不提交；响应丢失后复用冻结正文与幂等键；已有人工子记录时禁用再次创建按钮。测试事实只显示“已提交”，不声称送达，也不会误导航到 Job。
- 新 migration 为旧记录补 Automatic/version=0，使用部分唯一索引和自引用 FK；保留清理级联子记录。存在显式历史时拒绝旧 schema 降级，不静默删除历史。

## 验证状态

本地 Release 全方案构建 0 警告/错误，543 Unit / 73 Integration 通过，6 个原生测试明确跳过；119 前端单元测试、类型/lint/生产构建通过。通知管理独立 8 项浏览器测试通过（320/1440px，明暗），通知/配置/限流/搜索联合回归共 18 项通过（包含上述 8 项，不能相加重复计数）。

冻结源码 `fe3e034135ce30d563073032ac4ceceb8a99f0c9` 原生实际执行 1 / 通过 1，清理确认 true；状态报告 `deploy/artifacts/native-status/dispatch-d5505e8e423943d18f5c4ed7667f233f.json`。时间 08:24:38.201514–08:26:39.117400 UTC，SDK immutable ID `sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6`。使用单独临时数据库、无生产数据挂载的 SDK 容器、仅测试内本地 HTTP 与 TLS SMTP 接收端。收到正文后丢弃响应、Unknown 不重发、确认子记录只创建一次、原记录不变、停用渠道显式测试和撤权不发出等新增断言均真正通过，不以本地跳过视为原生通过。

工作站发布由 Job `630cc30b-0e4b-466c-8072-f63ff873e61e` 调度，Job 成功只表示宿主更新启动。最终宿主 journal 确认 16:27:47 CST 启动、16:29:15 CST Deactivated successfully；目标记录为完整 `fe3e034` SHA，维护状态 healthy/更新完成、11 核心容器 healthy、Agent/维护/更新 timer active，完整宿主权限/UDS/受限 helper/动态入口验证通过，sudo 保留。新的 4 个投递字段实际存在，Worker 私有目录仍只读，不挂 Docker Socket。

实际 Authentik 管理员/普通用户/匿名验证、配置 CRUD/版本/幂等/CSRF/Origin，以及两个新增入口的确认/重复风险/非法版本/缺键拒绝、普通与匿名拒绝、OpenAPI 通过；测试账户与临时配置清理、生产发送 0。8 个 HTTP 入口/共享/页面资源与 active Gateway 逐字节相同；`index-DLEBSc8V.js` SHA256 `184cde306743531e416ba69c767cdef8ad38f0711924eff99992c3bd6a31f873`，`NotificationSettingsView-QHJHWuG5.js` SHA256 `2481c32fddabd1c4da63cd7ef5e960684deb53c8d27e807b723a015db31dc7d7`。真实生产身份浏览器未另行走查，不将 fixture 浏览器或资源核对冒充生产 UI 点击；实际发送与人工子记录正向路径在隔离原生套件中验证。

工作站真实 OIDC 验收脚本增加两新入口的确认/版本/幂等键与普通/匿名拒绝、OpenAPI 检查，不对生产目标提交发送意图。真实批准厂商/邮箱启用不在本轮自动测试中。

仍缺私有目标/凭据 WebUI 与 Agent 安全写入、模板/偏好、更全组合故障和批准厂商验收，后续继续，不缩减 P0–P5 范围。

## 外援

此前首轮 GLM `glm-5.3-flash` 只读测试清单：输入 11003、缓存 768、输出 474 Tokens，27357ms，成功 1 次，重试 0，费用未知。GPT 复核并拒绝“第 5 次明确未发送失败变 Unknown”的错误建议；安全决策、实现、验证与发布由 GPT 负责。
