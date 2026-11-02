# 容器 ACL：管理员 API、Vue 编辑与异常处置候选

> 后续实际验收（2026-11-03）：633a2ca已正常覆盖，生产迁移/六组件来源核验、0668215真实页面九场景/8HDD/35审计、同原上下文双端退出与独立准确清理通过。进行中故障、ACL递归/恢复和删除仍待。下方062e361候选“未部署”等为当时事实，正文逐字保留，不将预览截图改称真实页面。[实际证据](CONTAINER_ACL_REAL_UI.md)。

2026-11-03，源码 `062e361639517557b51fc3965910dfad10ee54d1`。已接实际永久业务服务的管理员接口、Vue容器文件与全局异常处置入口；**尚未部署，没有真实Authentik→Vue→API/SQL/Worker→Agent→实际ACL文件/HDD业务整链**。前批[1.51安装态机器RPC/HDD](CONTAINER_ACL_AGENT_RPC.md)与[双隔离真实PG持久化](CONTAINER_ACL_PERSISTENCE.md)保留为各自批次，不拼接为本轮真人业务验收。

本轮只新增产品前后端、明确的测试夹具和文档，无生产迁移、发布、重启或真实文件修改；API/Worker/Gateway b1fafed与宿主4a58961/1.51是前批运行记录，未重新采样本轮PID/健康，不把旧健康当复跑。59主项仍未整项完成，17项移出不算完成，master未交付，不自动回滚dev，user NOPASSWD和密码未改。

## 产品链路与安全

- 独立管理员接口 `/api/v1/containers/{containerId}/files/acls` 和 `/api/v1/files/container-acls/recovery`，提供异步预检登记、原key查询、原任务查询、确认/取消、Unknown清单/原结果与明确处置。Controller只处理传输，复用实际EF仓储、专用Worker、当前权威身份和原BFF登录；没有在HTTP内执行ACL syscall或注册测试仓储。
- 65536字节请求上限、no-store、规范单个Idempotency-Key与原任务ID匹配；普通User不扩权，Cookie/CSRF/Origin/原登录与当前管理员检查保持。无效字段的ProblemDetails只返回稳定错误码/traceId，不回显私有JSON。
- 请求根字段严格且仅允许instanceId/path/aclType/entries/expectedVersion；每项必须显式tag/id/permissions。named用户/组的ID为0至4294967294整数，0不是缺失；基础项的id必须显式null，permissions必须0至7整数。拒绝缺失、重复/额外/大小写替代、字符串/浮点/指数/布尔/负值/哨兵、缺基础项或mask、反序重复及超过64项。default空数组明确表示清空，access空数组拒绝。
- 确认与处置也有独立严格converter：字段不得缺失/重复/额外，confirmed必须布尔，version必须显式非负整数。对应原计划、原版本和原执行的业务HMAC/CAS仍由实际服务/仓储校验，不能仅靠Controller接受JSON。
- Vue原生编辑完整access/default条目，显示符号权限和mask后有效权限；数值ID初始留空，显式0有效，named项要求用户明确添加mask。default清空单独勾选，文件禁default；目录默认ACL仅影响后续继承，现有子项保持。初始基础项只按当前普通模式填写，明确提示它不代表原ACL。
- Ready后显示原文件UID/GID、执行身份、原两类ACL与目标条目、mask影响后的普通模式、NoChange、期限/双Job、原Root三态与HDD历史证明。冻结后隐藏重复不可编辑表单，优先呈现原/目标对照；不递归、不改属主、不隐式恢复或清历史。
- 各响应逐项验证条目规范、目标模式、NoChange、原任务/命令、版本单调性与原回执完整性。原Receipt冻结，不能因Replayed/BindingCurrent变化覆盖历史，Unknown不能改成另一个Root事实；缺首次回执的Unknown只接受服务端原ID可靠fence后保存的首次事实。
- 预检登记POST若返回带执行/回执的状态，前端拒绝直接当作本次已确认成功，仅提示查询原任务。合法的幂等旧结果仍能以原key GET接续，不自动创建新key或确认。丢确认/处置回复后锁定重发，先GET核对原状态/版本。
- sessionStorage只保存subject/containerId/key/id，最多4条；不存路径、ACL内容、令牌、Root回执或身份凭据。身份变化/退出/401清全局引用，403清本容器引用并隐藏迟到私有结果；关闭普通弹窗不取消永久任务。隐藏页暂停轮询，恢复只查原引用。
- ACL面板与上传/编辑/历史/目录/复制/移动/普通及递归权限/所有者双向禁用。Unknown由当前管理员明确处置，先暂不处置不发送写请求；处置后GET原任务，核对Acknowledged与版本前进及历史原件，不能把处置当ACL成功。
- Element Plus主题变量、紧凑dl对照、明暗/减少动画与响应式保留；弹窗身体独立滚动，所有宽度上下边距12px，页脚固定保留确认/取消/查询，不以长ACL和长路径挤出屏幕。未新增或升级依赖，锁文件保持。

## 自动验证

| 检查 | 实际结果 | 边界 |
| --- | --- | --- |
| Release全解决方案 | 0 Warning / 0 Error | 本地候选，不发布 |
| 后端Unit | 4272通过 / 0失败 / 0跳过 | 原编排/签名等回归 |
| Integration全量最终 | 432通过 / 0失败 / 7默认原生跳过 | 较前批新增68个传输/实际回环HTTP用例；不是新PG验收 |
| 前端Unit | 972通过 / 55文件 | 新增63个ACL条目/计划/回执/网络/引用边界用例；此前967轮不相加 |
| 类型/生产构建/oxlint/eslint/Prettier | 通过 | 最后构建包含真实页脚修复；未改规则/依赖 |
| ACL生产预览浏览器 | **43通过 / 0失败 / 0跳过** | 一次完整两worker运行，API/身份/存储/Agent为测试夹具 |
| 既有权限/所有者/两种递归生产预览 | 156通过 / 0失败 / 0跳过 | 与43项另组（2.0分钟及7.0分钟），不冒称单次199项 |
| 当前范围 | 8通过 | 59保留/17移出的文档记账 |

实际回环HTTP测试经过Kestrel、MVC、受保护Cookie、CSRF与Origin；覆盖各路由匿名/普通用户拒绝、当前降权、原登录幂等/跨登录拒绝、原key和路由键、0/缺字段、脱敏错误。身份目录/仓储/Agent是显式夹具，不是实际Authentik/生产SQL/Root。

43项预览包括320/1045/1440三宽度×明暗×文件access/目录access/目录default，320×600默认ACL清空/取消、进行中取消、失联原key/原确认/处置恢复、401/403/404/503、实例变更、原计划替换拒绝、错三态回执拒绝、普通用户、Unknown明确处置及原回执保留、过期/隐藏轮询/peer锁与会话结束信号后迟到GET丢弃。最后一项只验证UI会话失效信号和引用清理，不冒称真实Authentik双端退出。

## 预览、来源及失败保留

使用 `WHALEDECK_PRODUCTION_PREVIEW_URL=http://127.0.0.1:5175` 与固定既有 `/opt/google/chrome/chrome`，只允许本地生产构建预览；没有访问实际工作站业务端口。收尾的少量尚未结束GET转发本机无服务的5080产生ECONNREFUSED，不能当实际后端连通或新业务故障；未触及8080真实写入。

18张PNG实际尺寸320/1045/1440×800与SHA通过，已查看320明/暗Ready截图；320×600动作可见由浏览器断言验证。5175实际HTML和入口JS与dist逐字节一致。17个改动源文件与提交062e361逐字节一致，构建/测试在提交前相同内容树执行，不把本地程序集当已安装062e361。证据目录 `deploy/artifacts/acl-web-062e361/` 包含292份前端源码、170份产物、PNG摘要、构建/单元/集成/浏览器及失败日志；原图另在 `deploy/artifacts/ui-candidates/acl-web/`。这些PNG仅证明测试接口页面。

首轮前端类型检查使用Object.hasOwn而项目目标不提供该API，改为兼容的hasOwnProperty.call，未改target。HTTP夹具批量名称适配留下Aclship，编译前失败，修正类型名后64项ACL专项通过；补5项原key测试后全量432通过。lint要求异常断言带错误信息，已补正向匹配，负向断言仍检查任何异常，不关闭规则。

浏览器首轮3个超时/33未运行：点击Element Plus透明输入被选中文字遮挡。改正常ArrowDown展开、可见radio/checkbox标签操作；随后发现关闭动画期间上一listbox尚在，按控件的可访问listbox名称限定选项。诊断的^前缀过滤曾返回No tests found，不计通过；一次修脚本目录错误未落盘，随后的未修改诊断仍失败，均记录且无真实资源动作。

选择器修正后发现真实桌面目录页脚按钮只有62.5%在视口内：默认15vh上边距加长内容超过视口。已固定所有尺寸12px上下边距，并去掉冻结后的重复编辑表单。该轮11通过/3失败/23未运行保留。后续37项35通过/2失败：401正确清空sessionStorage后断言误对null调用not.toContain，以及错误提示未按文字限定导致与警告双重匹配。只修测试断言，加入6项接续/取消/失效组合，最后43完整通过。各轮数量不累加。

首轮临时test-results按Playwright默认在诊断时被替换，未宣称后续截图是首轮原件；首轮原日志保留。后续诊断/listbox/页脚/断言失败截图与trace分别保留在 `deploy/artifacts/ui-candidates/acl-web-failed-*`，没有把失败改写为通过。

## 下一步

正常冻结覆盖API/Worker/Gateway/Migrator并前向迁移，必要宿主组件保持同源和稳定维护入口。以新独占资源完成真实Authentik→Vue→API/SQL/Worker→Agent→实际ACL/HDD：access/default设置、显式ID0/权限0、mask/NoChange、default清空、完整字节/inode/UID-GID/mtime/普通属性/另一ACL/子项保留、原Root/HDD和重放不写；再取消/退出/撤权/重启/丢回执/Unknown及其他写入保护组合。取得实际原件、独立核验和准确清理后才更新真实验收。ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D继续。

文档终检：225份Markdown、1375处相对链接无缺失；目标第1节不变，旧持久化QA正文逐字保留，`git diff --check`通过。文档登记不替代真实业务验收。

项目图谱事件 `whale-deck.container-acl.062e361.web-ui` 已取得projectId/key/eventId一致的成功回执，pending记录已移入synced；阶段保持active，不把API/Vue候选标成整个ACL完成。
