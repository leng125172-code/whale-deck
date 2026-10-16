# 容器终端 BFF 与 WebUI

2026-10-16，初始源码 `f81324c`，最终已部署运行时 **`085acef`**。继续 P1-11，不缩减完整76项目标。本文件区分本地实现/验证、CI和工作站签收；没有把UI模拟传输当成真实Docker执行。中间离线/待验收记录保留，最终结果见文末。

## 已实现

- 管理员 `GET /api/v1/terminals/containers/{containerId}` 通过独立Agent Inspect读取当前运行实例、名称、镜像和配置用户，不返回Env；只接受完整小写64位容器ID。注册表/标签核心保护与运行/隔离检查继续由Agent和固定root驱动独立执行。
- `POST /api/v1/terminals/containers/grants` 要求确认、相同body/header UUID幂等键、既有Cookie/CSRF/同源写入保护。签发前重新检查当前管理员及启动实例；同一幂等键不能改为另一个容器、实例、shell或宿主目标。
- EF迁移 `20261016092652_ContainerTerminalTargets` 扩展原会话表：TargetKind、InstanceId、Shell；原Host记录默认兼容，SQL检查区分Host与完整Container目标/固定shell/非空实例。只存元数据和票据哈希，绝不存终端输入输出。迁移不是已在工作站执行的证据。
- HMAC v2包含会话/主体/当前登录/目标/实例/shell。消费票据后再次验证实际持久字段的签名，阻止元数据错配重定向；失配关闭本次记录且不签发Agent能力。部署时旧版尚未消费的短时v1票据不继续使用，用户重新确认申请新会话；不迁移或放宽旧票据。
- WebSocket仍只接收首帧token，再按SQL消费的固定目标派发专用Container RPC；没有浏览器自选目标/UID/命令参数。保留4全局/2主体预算、30秒票据、10分钟硬期限、10秒服务器登录及租约复查、有界帧和正常Close握手。会话审计同事务记录人类主体与目标/实例/shell，正文不进入审计。
- 宿主/容器界面提取为共享 `TerminalSessionPanel`，保留明暗主题、xterm有界显示、中文/IME、尺寸调整、读屏和断开清理。容器页卡片/表格/详情增加真实入口，受保护目标不显示，非运行目标禁用。点击连接先Inspect，再确认配置身份及sh/bash/ash；不自动连接、不自动重连。更换容器路由重新挂载组件、关闭旧会话，不能沿用旧许可。
- `validate_container_terminal.py` 已准备真实验收：只用既有Worker不可变镜像ID创建无网络/端口/生产卷、只读、cap-drop、资源限制且配置用户1000:1000的自有临时容器；通过真实OIDC/BFF/UDS验证作用域、幂等、Origin/query、UID/尺寸/退出持久化/撤销，核对原PID1启动实例不变，最终按完整ID及所有权标签精确删除。脚本准备完成不等于已执行。

## 本地实际证据

- 仓库锁定SDK10.0.402。此电脑原来仅10.0.300；从微软发布元数据选择Windows ZIP并核实SHA-512，安装在独立Codex runtime目录，未降级global.json或改系统SDK。Release全方案0警告/0错误，**683 Unit /100 Integration通过，6原生PG明确跳过**；EF `has-pending-model-changes`无缺失迁移。
- 新增服务层非法目标/旧实例/降权/跨目标幂等/HMAC错配关闭，真实签名器目标/实例/shell/主体/登录用途分离，Host/Container传输派发和关闭握手、注销后禁止签发能力、控制器Origin/query/header和管理员属性等测试。模型SQL与持久审计断言加入隔离原生PG套件，但本轮尚未运行原生数据库，不能将其列为已通过。
- 完整间接依赖restore/audit通过，现有PKCS10.0.13保留，没有审计豁免。
- **176前端单元通过**，类型检查/lint/格式/生产构建通过。首轮并发过高造成认证模块加载超时，以及新useRouter mock缺失，均保留为失败事实；修复mock后使用2 workers全集重跑通过，不放宽断言/超时。
- **35项本地浏览器回归通过**：5项生产初始授权门禁、12项容器UI和18项原宿主UI；320/390/1045/1440/1920明暗、确认取消、配置身份、中文/IME、尺寸/退出、检查失败和换容器路由关闭。截图已人工检查390明亮与1440暗色，无水平溢出。终端输出明确是UI fixture，不证明真实Exec。
- Playwright首轮缺对应浏览器，自动下载又发生TLS bad-record-mac；未关闭TLS验证或更改系统代理。最终使用本机Chrome155.0.8059.84完成上述回归；CI仍安装其匹配Chromium并运行相同测试集。
- 两个新真实BFF脚本Python编译通过。Windows上Linux驱动策略测试因缺fcntl无法运行，未打补丁伪造fcntl或声称15项本机通过；交给Linux CI及工作站验收。

## CI与远程状态

初始记录：源码已推送dev。CI [38883144757](https://github.com/leng125172-code/whale-deck/actions/runs/38883144757)当时仍待终态；后来实际成功，最终同源码四作业CI见文末。

初始两个已知网卡地址SSH均超时，.local未解析出可用地址；当时未调度新部署，最后签收运行时保持a0b0001。稍后网络恢复，按文末证据继续而不是重启已活跃的任务。开发sudo策略保留，未改网络、DNS、桌面或Docker配置。

## 下一步与未签收范围

工作站恢复连接后先执行同源码冻结原生PG，确认扩展迁移与数据库约束/持久审计实际通过，再测试修复并覆盖发布dev；随后运行真实Host/Container BFF、普通/匿名403/401、CSRF/Origin、Swagger、真实浏览器容器终端与原身份/资产/宿主回归，核对更新unit终态与核心健康。仅CI或UI通过不能关闭P1-11。

文件传输、租户级隔离、长期会话清理及更广重启/挂载故障等仍未实现/验收。容器使用其配置用户；如果镜像默认root会明确提示，不伪装为非root。关闭只清理交互Exec，不停止容器；主动脱离终端的服务仍归容器生命周期，不能声称强隔离开发空间已经完成。

GLM只读前端文件定位：glm-5.3-flash，1次成功/0重试，输入264585、缓存202112、输出2829 Tokens，211282ms，费用未返回。GPT复核定位、决定安全边界并完成实际修改/验证；未向外援发送凭据或私有配置。

## 重连后的真实浏览器验收清单（执行前）

采用playwright-interactive技能保持同一浏览器句柄；只用临时账号经Authentik实际表单登录，不注入Cookie或模拟API。下列是待执行清单，不预签通过。

| 用户可见行为/状态 | 功能与视觉证据 |
| --- | --- |
| 容器列表真实入口与核心保护 | 只对自有临时容器点击终端，核心无入口；停止目标不可连接 |
| 默认不自动连接、确认取消/建立 | 初始surface、取消不建会话，实际配置用户/目标和shell可见 |
| 输入/中文IME/resize/读屏 | 真实UID1000、中文与stty；桌面到390手机resize、读屏开关往返不重连 |
| 清屏、退出、断开与重新确认 | 显示正确状态和exit7，清屏不新建连接；重新连接须确认 |
| 明暗/桌面/手机 | 初始与活动态截图、核心控件bounds，无水平滚动/遮挡/重叠 |
| 异常与探索 | 错shell/已停容器拒绝，切换目标/离页关闭旧会话；30–90秒正常交互探索 |
| 生产身份拒绝 | 新普通账号首次直达与刷新均403，surface为0；匿名回登录 |
| 清理 | 只删除本次临时容器、关闭会话与临时账号/助手；原容器PID1不变，审计保留 |

## 最终覆盖签收：085acef

- [CI 38884046890](https://github.com/leng125172-code/whale-deck/actions/runs/38884046890)四作业全部成功：Release/683 Unit/100 Integration/完整间接审计、Linux15项驱动策略、176前端单元和35项浏览器，以及新增真实PostgreSQL作业。后者使用锁定18.6镜像、仅回环发布的一次性服务、NOSUPERUSER/NOCREATEDB/NOCREATEROLE的应用角色；先迁到旧终端表并写旧Host行，再实际升级新目标列，确认原行兼容。包括容器持久字段、SQL拒绝跨类型/空shell/空实例/别名、事务审计与原生命周期断言；TRX要求实际执行1/通过1，不能跳过冒绿。CI服务停止成功。
- 工作站从冻结同一085acef运行原生PG报告 `dispatch-a14ca475ba084e1faf248426592035d1.json`，10:02:13–10:03:47 UTC，executedTests=1、passedTests=1、cleanupConfirmed=true，SDK镜像ID `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。没有生产DDL或外部通知发送。
- 真实管理员Plan/确认任务 `30b313ef-56b6-41b2-999e-e3e8cf16b17d` 仅代表调度。实际覆盖更新unit于 **18:07:25 CST** Deactivated successfully，inactive/dead、Result=success、ExecMainStatus=0；新迁移落库，API/Worker/Gateway覆盖重建。无自动回滚。
- 真实OIDC Cookie → 稳定维护代理 → Gateway → BFF → UDS → Docker Exec通过：完整目标/实例/shell幂等，换shell/跨Host幂等冲突，core Inspect/Grant403、写入缺CSRF和错Origin拒绝，WS错Origin/query、Ready、UID1000、40×120、exit7持久化、一次性重放和静默6秒后的显式关闭/正常RFC6455握手。原Host链路也复验通过，临时容器原PID1启动实例不变并按完整ID/标签精确删除。扩展保护/CSRF验证在最终运行时再执行一次通过，没有放宽关闭帧断言。
- 真实临时管理员经Authentik身份/密码/授权表单进入，从容器列表点击自己临时容器的终端；没有Cookie注入/API替换。确认取消/再次确认、UID1000、真实中文命令、**原生IME composition及读屏往返**、桌面↔390手机在线stty尺寸、主题往返、清屏、exit7、重新确认/主动断开和刷新不重连通过。截图 `web/test-results/container-production-mobile-final.png` 与 `container-production-desktop-final.png` 已独立查看，长随机容器名可换行，控件清楚，无水平溢出或终端字符重叠。普通正文提示可随正常页面纵向滚动，不把滚动指标当作视觉证明。
- 新普通账号真实OIDC首次回跳、直接进入容器终端和刷新均403，surface=0，截图 `container-production-ordinary-forbidden.png`；匿名初始回登录的生产构建门禁和实际API401均通过。实际管理员退出回/login。没有将前述本地fixture当作这些实机结论。
- 原通知管理CRUD/幂等/版本/CSRF/Origin及普通/匿名验证通过，新增容器Inspect/Grant真实403/401，管理员OpenAPI含两个新路径；8个HTTP资源与活动Gateway字节一致。原宿主安装验证/UDS/权限/受限helper/动态入口通过，实际宿主包框架与PKCS仍10.0.13。Agent/维护/timer active，最终11核心healthy，sudo -n true保留。
- 临时账号独立API计数为0，助手进程与测试容器列表为空，无终端transient unit残留；SQL会话状态只有Closed（本轮结束时Container7、Host21），保留不可变审计，没有删除生产数据或日志。

真实浏览器首轮有两类测试失败，均保留边界：认证按钮从英文动态切换中文，旧locator触发工具超时/重置，核查后浏览器和账号助手确已结束；隐藏switch input不能直接点击，改点用户可见开关外壳。第一次临时容器600秒到期自动精确删除，使交互进程正确显示137而非7，未把该轮签为exit7；确认旧助手终态后使用新自有容器完成原生IME/清屏/exit7与清理。没有因单纯观察超时重新部署，也没有因此修改产品超时、降低断言或扩展权限。

容器终端BFF与WebUI子范围已覆盖部署签收。**文件传输、租户级隔离、长期清理和更广故障仍缺，P1-11及全部76项保持active**。下一步继续这些完整范围，不因本轮签收子范围关闭整体目标。
