# 递归所有者：管理员API与Vue候选

2026-11-02，源码冻结`a806f56d0202013d94dc15e1de035e5373332344`。已接真实Application/EF业务服务的管理员HTTP入口及Vue容器文件/异常处置页面；本地MVC、前端合同及生产预览验证在本批记录。**尚未部署，未做真实Authentik→页面→SQL/Worker→Agent→实际树/HDD业务整链**。前批[安装态RPC/HDD](CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)与[双隔离真实PG持久化](CONTAINER_RECURSIVE_OWNERSHIP_PERSISTENCE.md)保持独立，不能拼接算作新页面实机签收。

59主项继续部分实现，17项移出不计完成；master尚未交付，不自动回滚dev。本批未修改生产schema、运行服务或真实树，没有重做此前已完成的实机操作。宿主22906f5/1.50与应用0dede08为前批运行记录，本批不把旧健康检查冒称复跑。

## 已接产品链路

- 管理员独立路由`/api/v1/containers/{containerId}/files/recursive-ownerships`及`/api/v1/files/container-recursive-ownerships/recovery`，提供异步登记、原key查询、原任务状态、明确确认、取消、异常清单/原任务查询和明确处置。Controller仅处理传输，复用已实现的永久仓储、专用Worker与当前权威身份/原BFF登录校验，不在HTTP内扫描或修改树。
- 请求上限65536字节，元数据no-store；新入口仍仅管理员，普通User不扩权，BFF Cookie/CSRF/Origin与原登录/资源归属继续验证。单个规范幂等键固定原任务，返回丢失只查询或同原key重试登记，不产生新执行。
- UID/GID必须分别显式输入整数0至4294967294，0表示root。严格Body converter拒绝遗漏、重复/额外/大小写替代字段、字符串/浮点/指数/负数/null/布尔/哨兵值；确认与处置也要求明确布尔、非负整数版本和原计划。无效输入返回稳定脱敏ProblemDetails，不回显私有请求。
- Vue新增目录专用入口、异步预检/计划确认、原引用恢复/查询/取消与异常处置。目标数字ID初始留空，不沿用原属主或执行身份；分别展示目标UID/GID、原根属主、执行身份、树清单摘要、预检总数/预计变更/文件/目录数量与五类实际计数。
- 截获的fixture身份分别为原根1000:1001、目标2200:3200、执行5100:5101，并验证显式0:0。真实各inode旧属主仍由Root/HDD负责，不据根属主推断整树都相同。
- 原计划确认后表单冻结；容器实例变化、计划过期或响应未核实不能确认。Started/Partial/Unknown不自动重做或回滚；实际计数、历史验证/可能存在与最近回执当前可用性分列，不把Unknown的0已证明修改推断为物理零修改。
- 原引用只在sessionStorage存Subject/containerId/key/id，最多4项；不存路径、目标UID/GID、计划令牌或回执。身份变化/降权/401/403/跨页或跨标签退出清除迟到私有结果及引用，关闭普通弹窗不取消永久任务。
- 新面板与上传/编辑/历史/目录/复制/移动/普通权限/单对象所有者/递归chmod互斥；文件/受保护项不提供递归入口。当前另一管理员明确确认后处置原Partial/Unknown，核对返回的原任务/版本/计数事实，丢处置响应只查询原处置，不自动重发。
- 沿用Element Plus变量、紧凑两列指标卡、明暗/响应式/减少动画；弹窗身体独立滚动、页脚保留动作，异常处置区按窄屏折行。布局继承既有页面，未引入新依赖或独立原型。

## 后端及前端验证

本地Release全方案0警告/0错误，4036 Unit通过。Integration全量363通过、7项默认原生依赖跳过；新增54项传输/真实回环MVC测试包含管理员策略/no-store、UID/GID类型与缺失/重复、显式root、确认/版本、脱敏输入、原登录幂等/跨登录拒绝、当前降权及匿名/普通/CSRF/Origin拒绝。MVC真实经过Cookie/CSRF管线，但身份、仓储和Agent均为明确测试夹具，不算真实Authentik/PG/Root/HDD。

前端全量54文件/909项Unit通过，含77项新增递归所有者合同、计数/历史不可变、UID/GID范围、原key网络/引用存储与身份切换检查。类型检查、生产构建、oxlint/eslint及Prettier检查通过，未修改依赖锁文件。当前产品使用实际API/永久业务服务；测试接口与内存仓储仅属于测试，不注册为产品实现。

日志位于本机忽略/临时目录：`/tmp/wd-recursive-owner-ui-{unit-first,integration,backend-unit,build-frozen,webbuild-frozen,typecheck,lint}.log`。

## 生产预览与失败记录

入口是`WHALEDECK_PRODUCTION_PREVIEW_URL=http://127.0.0.1:5175`、固定现有Google Chrome及`web/playwright.config.ts`；只允许本地5175预览。页面测试拦截合成API，既不连接真实工作站业务URL，也不进行真实所有者写入。冻结源码对应生产构建，不将Vite开发预览结果冒称安装页面。

首轮6个常规宽度/主题用例在确认POST断言处失败：夹具已改用独立`isolated-recursive-owner-confirmation`，断言仍期待旧chmod夹具的`isolated-recursive-confirmation`。实际页面传递新令牌正确；修正测试断言，没有放宽产品校验。首轮4通过/6失败/1中断/33未运行，准确SIGINT本轮Playwright CLI后保留`deploy/artifacts/recursive-ownership-ui-first-browser/test-results`及`/tmp/wd-recursive-owner-ui-browser-first.log`，不把部分执行算全绿。

冻结候选的完整递归所有者44项生产预览全部通过，日志`/tmp/wd-recursive-owner-ui-browser-final.log`；关联普通权限/所有者/递归chmod的112项以两worker另组完整运行全部通过，日志`/tmp/wd-recursive-owner-ui-browser-related.log`；两组均零失败/零跳过，不冒称单次156项全绿。预览测试收尾曾有尚未结束的GET转发至无服务的本机5080而记录ECONNREFUSED，不代表真实后端连通；未触及实际8080工作站或产生真实写入。截图仅是fixture页面证据，不能证明真实人类身份、磁盘变更或已安装版本。

## 下一步真实验收

正常冻结覆盖API/Worker/Gateway/Migrator与必要宿主组件，应用前向迁移，再以新独占测试资源验证真实Authentik→Vue→API/SQL/Worker→Agent→实际树/HDD：目标/原属主/执行身份、显式root/零变更、完整字节及inode/权限/mtime/属性保留、聚合/逐项历史、取消/退出/撤权/重启/回执丢失/Partial/Unknown/其他写入互锁和不同管理员处置，独立准确清理。未获得的依赖和故障组合保持未验收，不用UI测试替代。ACL、可恢复删除、长期清理、P0-05及其余59项/UI/C#酒馆/统一供应商/AI Live2D继续。

## 截图与构建来源

6张320/1045/1440明暗PNG尺寸与SHA校验通过，已人工查看320明暗截图；本地5175实际HTML及入口JS逐字节匹配冻结生产产物。`deploy/artifacts/recursive-ownership-ui-a806f56/ui-evidence.json`保存6张图、286份前端源文件与170份产物摘要。截图不是已安装页面；320×600取消及600/800高异常处置由浏览器用例验证。

文档门禁：8项范围检查、213份MD/1292相对链接无缺失及diff检查通过；前批真实PG QA原始正文逐字保留，只前置后续说明。
