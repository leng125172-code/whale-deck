# 容器递归 ACL 管理员 API 与 Vue 候选

2026-11-04，源码冻结 `c020c245f954e7a5b8e3a4b6d022e929c35794d7`，承接[真实隔离 PG 持久化](CONTAINER_RECURSIVE_ACL_PERSISTENCE.md)和[安装态签名 RPC/HDD 分项](CONTAINER_RECURSIVE_ACL_AGENT.md)。本批接入现有真实业务服务，完成公开管理员 API、目录操作和异常处置页面候选。**尚未标准覆盖、生产迁移或取得新的真实 Authentik→Vue→Worker→Agent→文件/HDD 整链证据**；59 项主任务及 P1/P2 不升为完成。

## 接口、授权与失败恢复

| 入口 | 行为与边界 |
| --- | --- |
| `POST /api/v1/containers/{containerId}/files/recursive-acls` | 原登录提交实例、目录路径、ACL 类型、两组目标与当前版本；异步预检，202 与原 Location，不在 HTTP 内写文件 |
| `GET .../recursive-acls/{id}`、`GET .../recursive-acls/by-key/{key}` | 查询原意图或登记回执丢失后的原幂等键，不自动换键生成新任务 |
| `POST .../recursive-acls/{id}/confirm` | 原登录、原计划 hash、版本、明确确认及原 ID 幂等键；只登记原执行任务 |
| `POST .../recursive-acls/{id}/cancel` | 明确请求取消原任务，不把 HTTP 成功当作已取消或已恢复 |
| `GET /api/v1/files/container-recursive-acls/recovery`、`GET .../recovery/{id}` | 当前实时管理员核对 Partial/Unknown 历史与最近观察，不能冒充原用户确认新执行 |
| `POST .../recovery/{id}/acknowledge` | 原版本、原 ID 与明确确认；复用既有业务永久封存和释放屏障，不删除 HDD 或改写原结果为成功 |

所有入口为 Administrator、NoStore；写入沿用 BFF Cookie/CSRF/Origin 约束，Controller 只做传输并调用已有 `ContainerRecursiveAclWorkflowService`。不新增本地账号、生产旁路、Agent 能力或直接 Docker socket。请求上限 65536 字节，登记键必须单个、小写规范 D 格式非空 GUID，确认/取消必须匹配原 ID。

专用 JSON 转换器逐字段要求显式存在，拒绝重复、额外、大小写变体、错误类型、浮点/指数/字符串数字、缺 ID/权限、非法 sentinel 和超过每组 64 条的目标。access 必须两组完整目标，default 必须 `fileEntries:null`，目录显式 `[]` 才表示清除 default；不能用缺字段、空 access 或隐式零值代替。保留 named UID/GID 0、权限 0 和 mask，拒绝缺 mask、重复或不规范顺序。非法绑定返回脱敏中文 ProblemDetails，不回显条目或异常原文。

补齐通用任务重试防线：后端在读取重试上下文、生成计划或入队之前拒绝两种递归 ACL JobType。任务中心隐藏通用重试，对 AwaitingContainerRecursiveAclResolution/Receipt 不提供通用取消，并指向文件页核对原任务；未提供伪造的通用结果摘要。

## 页面与交互

容器文件的合格普通目录行新增“递归ACL”，复用目录版本和实例绑定。文件、特殊模式、符号链接及受保护对象不能进入递归入口。与上传、编辑、mkdir、复制、移动、chmod/chown、递归权限/所有者及单项 ACL 互斥；自身锁不把自己的确认按钮锁死。

编辑阶段区分文件/目录完整 access，或仅目录 default 设置/明确清除。目标模板明确标为目标，不能冒充原有 ACL。可新增 named user/group/mask、显式数值 ID 与权限；空白或非数字不变成 root。登记前深复制位置和两组条目，登记后只展示不可变计划、状态与事实。先显示对象/预计变更/文件/目录数，再显示原根 UID/GID、普通模式、执行身份和完整目标及 mask 有效权限；不混淆 ACL ID 与 inode 所有者。

Ready 时仅确认未过期、未换实例且与原命令相同的计划。丢失登记/确认响应时查询原 ID/原键；保有内存中的原命令时才允许用户明确重试原登记。sessionStorage 仅存当前 Subject、容器 ID、键和意图 ID（最多四条），不存路径、ACL、hash 或 Cookie/token。登出、Subject/管理员权限变化清理引用与私有状态；隐藏页停止轮询，晚到结果按生命周期丢弃。

Partial/Unknown 保留五计数、历史/当前观察与保护状态，不自动续做或回滚。文件页新增“容器递归ACL修改异常”，当前管理员选择原任务明确处置；丢失 ACK 回执先核对原处置，不自动重发。关闭弹窗只关闭视图，不假装取消后台任务。

预检/异常弹窗采用限定视口高度、正文滚动和固定换行底栏；320×600 明暗主题的确认、取消及长路径处置均有候选页面测试。状态进入预检后隐藏编辑表单，把计划计数放到前面，避免长表单挤走主要事实。继续使用 Element Plus 主题变量与紧凑布局，未引入新依赖。

## 自动化证据与实际边界

干净冻结后完整 Release 构建 0 警告/0 错误，Worker/Agent/MaintenanceHost 三个实际 CLI `--build-info` 均返回准确 c020c24 来源，不启动候选服务。25 个源码/测试文件 SHA 已保存。代码已推送 dev；准确 SHA 的 CI [41762751191](https://github.com/leng125172-code/whale-deck/actions/runs/41762751191) 最后观察到 9 路成功、2 路浏览器仍运行，聚合门禁尚未产生，不能当作整条通过。

最终本地源代码回归：4651 后端单元通过；569 默认集成通过，7 项默认原生测试跳过；1161 前端单元通过。新增 78 项集成包含严格输入和真实回环 MVC/Cookie/CSRF 请求，原有模型检查也通过；回环测试的身份目录、Repository、Agent 仍是隔离夹具，不能称为真实 Authentik、生产 PostgreSQL 或文件操作。新增前端目标/状态专项及任务中心限制已包含在全量结果中。

类型检查、生产构建、oxlint/eslint、格式检查及 8 项范围基线通过；281 份已跟踪 MD 无合并冲突标记，当前完整目标第 1 节未变。新递归 ACL 页面 30 项开发构建浏览器测试通过；随后生产构建预览配置下，新 30 项及关联单项 ACL/递归权限/递归所有者 140 项合并运行，最终 170 passed（14.1 分钟），无失败。所有这些浏览器请求使用测试接口；生产构建预览仅证明构建产物的交互，**不算已部署或真实身份/磁盘整链验收**。

失败记录保留：最初默认 Chromium 缓存缺失导致 17 项在启动阶段失败；指定已有 `/usr/bin/google-chrome` 后才进入页面。首轮页面 default 选择器定位隐藏输入超时，第二轮明确清除的原生 checkbox 隐藏输入超时；改为键盘展开可见选项、点击可见标签并核对选中状态，未用强制点击/DOM 注入绕过。最初回环 default 非空目标夹具使用 PascalCase 嵌套字段，被产品严格绑定正确拒绝；改为 Web JSON 后 79 项专项通过。首轮截图复核后调整计划显示顺序，30 项重验通过。最终关联轮日志另有两次辅助 Vite 开发代理请求（usage/原递归权限 GET）连接本机未启动的 5080，记录 ECONNREFUSED；未将此解释为生产依赖可达或真实接口验收，原日志保持。证据汇总初次误把 TRX notExecuted 当跳过数（实际为 0），按 total−executed=7 并逐项检查 NotExecuted 结果修正；这是汇总脚本失败，不是新增产品测试失败。浏览器早期失败日志及截图保留；HTTP 首轮夹具失败的工具回执保留在会话，最终专项日志与 TRX 保存在本批目录，不冒称首轮全绿。

私有证据位于 `deploy/artifacts/recursive-acl-api-ui-d8a0d25d50af4acfa54690eb55e11f74/`，保留 TRX、完整构建/单元/浏览器日志、启动/两轮失败原件、冻结 25 文件 SHA 与只读运行核对。证据不入 Git，不包含真实身份 Cookie 或账户凭据。

## 运行基线和待办

本批只读运行核对与**前一持久化阶段**保存的快照比较：11 核心容器 ID/镜像/StartedAt 不变且 healthy；Agent PID 2799620、维护 PID 2799617 不变；7 受保护配置和发布账本 hash/权限/所有者一致；全部 153 普通 ACL 与 58 递归 ACL HDD 元数据的字节及九字段一致。Git 索引仍为用户所有，开发 NOPASSWD 策略保持。此比较不是新造的“本批测试前快照”，也不宣称本批新执行了活动任务 SQL 检查。

安装宿主仍为 7cb3935/1.53，运行应用和发布账本仍为 28062da。上批直接检查生产递归 ACL 表不存在；本批无生产迁移/部署，不重放已完成的 Agent 实盘操作。没有宿主重启、额外核心重建或自动回滚。

下一步取得准确代码 SHA 的完整 CI 终态，再走标准覆盖/前向迁移及真实 Authentik 登录、原 Vue 预检/确认、实际 Worker/Agent 文件与 HDD、权限拒绝及同上下文正常双端退出的整链和独立核验。随后完成递归 ACL 的进行中取消、实时撤权、重启/租约、Partial/Unknown、跨写入和路径/磁盘故障及递归 ACL 历史恢复；可恢复删除/长期清理、维护入口连续性和其它 59 项仍待。未完成部分不能用已有下层分项补签。
