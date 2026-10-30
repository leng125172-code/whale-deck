# C# 酒馆与 AI 看板娘：现有阶段扩展设计及验收

决策日期：2026-10-30。用户要求把酒馆完整前后端能力纳入现有计划，后端使用 C# 重构，Vue 前端集成 AI 调用与酒馆配置；看板娘参考 dsh-pet，必须具备动画、交互与物理、碎碎念与对话、余额展示和睡觉。

**状态：已纳入计划，待实现、待真实依赖及页面验收。** 本次是源码调研与设计登记，没有运行上游应用、调用模型、安装扩展、生成角色素材或部署新服务。59 个主任务 ID 保持；P4-08 的业务范围明确扩大，不能再用“仅 AI 容器模板”解释该项。17 个移出主任务不恢复，不以主任务数不变声称工作量不变。所有者/递归/ACL/删除等已有计划继续保留。

## 1. 事实、来源与覆盖口径

| 对象 | 固定基线与核实内容 | 尚未证明 |
| --- | --- | --- |
| 酒馆功能清单 | [完整清单](research/SILLYTAVERN_FEATURE_INVENTORY.md)：22 域、206 个核心/随包能力 ID，另有 66 个目录扩展 ID，共 272 个唯一 ID；14 个随包目录不重复计数 | 272 项均没有 Whale Deck 移植验收；66 个扩展未逐仓库源码/许可证/运行审计 |
| 酒馆核心 | [1.19.0 固定源码](https://github.com/SillyTavern/SillyTavern/tree/7e8663cd9c184a550b37238218bdd32c6efc68e9)：复核角色/聊天端点、浏览器 PromptManager/world-info、插件加载器和 14 个 manifest | 阅读文件及目录不能代替完整行为对照、数据迁移或实际模型验证 |
| 资源目录 | [固定 index.json](https://github.com/SillyTavern/SillyTavern-Content/blob/72acaca394237790106fa7b33e57ee50ea197127/index.json) 的 66 个 `type=extension` 条目再次核实 | 目录收录不代表兼容、可安全执行、官方作者或所有第三方扩展全集 |
| dsh-pet | [固定提交 3ec5f5f](https://github.com/PC2005-cloud/dsh-pet/tree/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4)，包清单版本 0.3.8；核实 README/FEATURES/CONFIG/API 和动画、物理、聊天、碎碎念、余额、睡眠源文件 | 固定 main 提交不等于 npm 发布物；未运行其 UI，不借用上游自报测试数作为本项目证据 |
| 本项目 | `web/src/layouts/AppShell.vue`、`web/src/router/index.ts`、`web/src/services/apiClient.ts`；`src/WhaleDeck.Api/Program.cs`、`Controllers/JobsController.cs`；`src/WhaleDeck.Infrastructure/DependencyInjection.cs`、`deploy/gateway/nginx.conf` | 当前没有这些 AI 路由、模型编排、宠物组件或相关 EF migration；现有登录/SSE不代表 AI 已接通 |

关键事实：酒馆不只是 Node 代理。世界书扫描和提示词管理等大量业务在原浏览器 JavaScript 中，因此“C# 重构后端”必须把这些权威编排也迁入 Application；前端只能提供编辑、预览和渲染，不能让两套上下文各自决定模型请求。[PromptManager](https://github.com/SillyTavern/SillyTavern/blob/7e8663cd9c184a550b37238218bdd32c6efc68e9/public/scripts/PromptManager.js)、[world-info](https://github.com/SillyTavern/SillyTavern/blob/7e8663cd9c184a550b37238218bdd32c6efc68e9/public/scripts/world-info.js)。

所有 ST ID 的逐项原文、实施批次和验收组见 [AI_TAVERN_ACCEPTANCE.md](AI_TAVERN_ACCEPTANCE.md)。核心/随包全部纳入；66 个可安装扩展全部建账并纳入功能迁移与兼容验收，不能只实现安装按钮、列出名字或静默删除难项。第三方生态为开放集合，“完整”以本次固定清单为可核验边界；新增上游条目走版本化变更，不冒称永久兼容所有插件。

## 2. 对现有范围的明确修订

| 主归属 | 新增子范围 | 联动要求 |
| --- | --- | --- |
| P4-08 | C# 酒馆编排、角色/Persona/聊天/群聊/世界书、模板/采样/连接、RAG/媒体/语音、脚本/扩展兼容、余额读取 | 见 DEV-AI-001..008 与完整 ST 账本；GPU 模板和设备保护继续保留 |
| P0-08 / P5-05 | 原生聊天/配置页与全局看板娘，含六类必需交互、明暗/移动/减少动画 | 使用[紧凑卡片规范](UI_STYLE_GUIDE.md)，不以 iframe 或外链替代原生功能 |
| P0-01/02/03/06/10 | Authentik/资源授权、持久生成/取消/事件续传、Secret 与证据账本 | 默认普通用户不自动得到 AI 资源/模型凭据或宿主管理权 |
| P2-07/08/10 | 聊天/角色/世界书/配置/附件/向量来源完整备份、隔离恢复和新机演练 | HDD 校验和实际还原，不能只导出缺媒体的 JSONL |
| P4-11/12 / P5-09/10/11/12 | 身份与审计、依赖安装升级、负载/安全/故障/真实页面和最终交付 | 每个 ST/PET 子项有版本与实际证据，不因主项部分实现而签收新增范围 |

P4-09 仍不恢复独立商业 AI 网关/计费/Skills 市场。此次允许为酒馆连接实际授权的本地或云模型、读取用户自己账户的余额/额度；这不要求建设收费路由、套餐或结算。P5-06 仍不恢复通用平台插件生态；此次增加**酒馆功能边界内**的扩展协议及隔离兼容层，不能将此范围例外变成任意平台插件/Agent 扩权入口。ST-ADMIN 原生密码/登录/资料功能按 Authentik/BFF 等效映射验收，保留用户隔离、停用/撤权、配置和备份语义，不复制本地密码体系。

## 3. 后端实施设计（.NET 10 / C#）

| 层 | 应承担职责 | 与现有工程衔接 |
| --- | --- | --- |
| Domain | 角色/版本、Persona、世界书/条目、会话/消息/候选/分支、连接配置、生成与宠物状态的规则 | 保持可空引用与实体不依赖 EF/HTTP/前端 |
| Application | 权威上下文管线：宏/正则→世界书扫描与预算→提示模块排序/深度→历史裁剪/分词→供应商能力适配；群聊调度、脚本解释、权限/幂等/取消 | 处理所有生成类型和固定版本的实际顺序；上述箭头是模块职责示意，精确执行先后由上游差分用例冻结，不凭概述硬编码 |
| Infrastructure | EF Repository、文件/角色卡解析、受控媒体、分词/向量实现、HTTP 模型/余额/TTS/STT/绘图适配、隔离扩展 runner 客户端 | 复用 PostgreSQL 与有界 Valkey；每次 schema 变更 migration，Secret 仅引用 |
| Api / BFF | Cookie/CSRF/Origin、输入校验、DTO、流式传输/续传、配置查询与确认 | 新设计路径 `/api/v1/ai/*`，不是当前已注册端点；Controller 不放编排或持久业务 |
| Worker | 生成调度/租约、RAG 索引/转换、导入/导出/备份、碎碎念/梦话/余额后台任务 | 独立限额/队列，不能阻塞已有文件/运维 Job；事件和终态持久，不把缓存当真相 |
| Agent / MaintenanceHost | 延用既有安装/容器资源生命周期与维护入口 | **聊天、动画和余额不新增 root RPC**；产品 Agent 最小授权，AI 不持有 Docker/Agent socket，维护入口独立于模型故障 |

优先在既有 C# 项目中建立明确 AI 模块和专用处理队列；需要分进程时沿用同一程序集分层与内部服务鉴权。最终服务端编排为 C#；不以反代原酒馆 Node 服务或仅包一层 API 作为重构完成。扩展兼容若需 JS/WASM runner，仅放在隔离进程/容器，不能承担酒馆核心权威编排。

生成先创建持久 `generationId` 与消息版本，再单独读取事件；POST 创建带 CSRF/幂等键，GET 续流按事件序号读取原生成，断线不重发模型请求。记录 `Queued/Preparing/Streaming/Completed/Canceled/Failed/Interrupted/Unknown`，区分未发出、部分输出、已取消与远端结果未知；客户端 Abort 不等于供应商停止。未确认远端状态时不自动重试有费用的生成，也不把失败后的模型切换当透明成功。配置/角色/提示词/模型版本随生成冻结，编辑中的草稿不改变在途生成。

现有 `apiClient.ts` 是 15 秒超时、JSON 返回；复用其 CSRF/401/Problem Details语义，新增独立流式客户端。不能为聊天取消全站超时，也不能把 Job 的一分钟 SSE 轮换复制成重新生成。Gateway 对 AI events 设置精确 location、关闭缓冲、心跳与有界读超时；实测首 token、断线续传、退出清空和旧事件隔离。

供应商按“文本/聊天、流式/取消、tokenizer、参数、reasoning/logprobs、工具、视觉、embedding、语音、绘图、余额/额度”建立能力矩阵。不支持参数必须明示并阻止误配置，不能接通文本就认定全部可用。保留此前Codex CLI/订阅接入方向，作为首批待验证模型适配之一；C#实现不被Subscriptions/Node宿主绑定。旧[桥接调研](research/AI_MASCOT_SILLYTAVERN_CODEX_DOCKER.md)保留为历史，模型桥接不能替代角色编排或其他后端；实际版本、授权登录、协议和额度需单独核实验收。

## 4. 原生前端与配置页面（设计，未注册）

| 页面 ID | 设计入口 | 用户应能完成的工作 |
| --- | --- | --- |
| PAGE-AI-001 | `/ai/chat/:conversationId?` | 完整聊天、swipe/继续/代写、编辑/排除/分支/检查点、群聊、真实生成状态及提示词检查 |
| PAGE-AI-002 | `/ai/characters` | 角色卡/Persona、头像与图库、标签、导入导出、角色/版本/看板娘绑定 |
| PAGE-AI-003 | `/ai/worlds` | 世界书/条目/绑定、递归/概率/预算/插槽、资料库/附件和索引状态 |
| PAGE-AI-004 | `/ai/configuration` | 连接/Secret选择、模型与能力、采样/模板/tokenizer、作者注释、Regex/宏/STscript/快捷回复；有序表单及高级编辑器 |
| PAGE-AI-005 | `/ai/extensions` | 14 随包及 66 目录条目的实际迁移/依赖/审核/安装/启停/更新/删除/失败恢复，权限与版本可见 |
| PAGE-AI-006 | `/settings/ai` | 管理员提供商/出站允许范围、Secret录入、模型授权、资源上限与依赖健康；个人设置不显示服务密钥 |
| PAGE-PET-001 | AppShell 内全局组件 + `/ai/mascot` | 启用/隐藏/大小位置/宠物包、六类能力与余额隐私、静音/睡眠、独立模型/频率/预算配置；全屏编辑页、终端和确认弹窗避让 |

普通用户的 AI 入口只在获得显式 `PERM-AI-USE` 及模型/资源授权后出现；无权路由服务端403。本人角色、聊天、Persona不是 Authentik 人类资料，可独立管理；管理员不默认能读所有人的聊天正文/余额。侧栏抽屉用于快捷聊天，完整配置与历史有可直达页面；不可把全部配置塞入气泡。各 ST 字段必须有编辑/导入/执行/显示证据，展示原酒馆配置 JSON 但忽略字段不算兼容。

## 5. dsh-pet 六类必需能力与改造验收

上游是 React/Cordis/DSH host + 可选 Electron 的桌宠，不能直接安装为 Vue 插件。取其状态与纯函数设计，浏览器渲染重写为 Vue composable/组件，后端人设/生成/配置/余额重写为 C#。浏览器动画与物理仍在 TypeScript 执行；它们不因“C# 后端”而移到服务器逐帧运行。此次要求的是 Whale Deck 页内看板娘，独立桌面窗/托盘/多显示器 OS 窗口控制不自动变成新交付范围。

| 验收 ID | 源码核实事实 | Whale Deck 实施与通过标准 |
| --- | --- | --- |
| PET-ANIM-01 | [README](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/README.md) 描述 100+透明视频动画；[next-animation](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/client/pet/animation/next-animation.ts)决策下一动作 | 实际有待机/转向/移动/点击/拖拽/说话/工作/余额/睡眠素材；按包内权重和状态连续切换，解码失败/404/离线明确降级，解除后可恢复；不是静态头像加CSS晃动 |
| PET-PHYS-01 | [physics](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/shared/physics.ts)公开重力/抛掷/弹簧/挤压/碰撞；[throw](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/shared/physics/throw.ts)有dt夹取与子步进 | 点击回应、拖拽、松手速度/甩抛、反弹/摩擦、弹弓、互撞及可调参数；Pointer capture、触摸取消/失焦/缩放/切页/手机旋转不丢失对象；不越过页面安全区域，不劫持滚动/正文选择和表单快捷键；减少动画可关闭物理 |
| PET-WHISPER-01 | [whisper](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/whisper.ts)独立模型、30秒生成超时、结构化失败，梦话复用生成链 | 按角色/世界书编排真实请求，手动与周期独立开关；可见/活跃会话和后台租约去重，多标签不重复扣费；频率/并发/消耗上限、授权模型回退可见；离线/429/401失败有事实，预置台词标为本地台词 |
| PET-CHAT-01 | [chat](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/chat.ts)用DSH llm聚合流式块后返回文本，历史窗口与持久历史不同，支持本轮图片与配图 | 本项目使用持久流式聊天和ST编排；气泡与完整聊天引用同一会话，不复制两套记忆；停止/继续/断线/刷新/切角色/退出正确。图片/粘贴/拖入、语音与配图按能力开启，屏幕捕获每次由用户主动授权，不后台截屏 |
| PET-BALANCE-01 | [registry](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/balance/registry.ts)内置opencode-go/commandcode/deepseek-official/deepseek-account四适配；[FEATURES](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/FEATURES.md)说明六档动画和不支持提示 | C#查询获授权账户，货币/点数/滚动额度分别建模；展示单位、账户别名、采样时间/额度窗口/重置、过期/未知/不支持。仅供应商给出分母时算百分比；无总额不估算档位，不从token推余额，不将订阅限制叫现金余额；本人可隐藏/手动刷新，轮询去重/退避，Secret不回浏览器 |
| PET-SLEEP-01 | [SleepStore](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/sleep.ts)为内存态、重启即醒；[sleep-machine](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/client/pet/card/sleep-machine.ts)管理三段；无素材套时接口明确拒绝 | 采用C#持久用户意图+前端三段状态机：入睡/循环/醒来、点击或菜单/聚焦快捷键唤醒；睡姿可拖动，拖动结束不误唤醒；睡眠停止随机动作、工作气泡、普通碎碎念和余额/待办打扰。可选梦话只弹气泡、不换动画/不朗读，也有独立请求预算；刷新/后端重启恢复睡眠，重复命令幂等，异步旧回复不唤醒 |

上游额外具备工作六档、待办、通知、多个pet pack/实例与两套对外API。[功能说明](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/FEATURES.md)、[配置说明](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/CONFIG.md)。本项目把已授权AI生成/Job状态映射为工作动画，状态来自真实事件，不能把播放成功动画当Job成功。待办/通知复用已有中心的授权事件，宠物睡觉不抑制真正的安全告警/维护入口。多实例通过同一角色包和独立实例ID管理，余额/聊天/素材跨用户隔离；没有已授权AI操控者，不复刻上游“取锁自动叫醒”的外部控制权限。

上游 README 的 HTTP 接口是本机无鉴权，不能复制到本面板。所有宠物状态/配置/生成/余额通过BFF认证和资源授权；页面内总线只接受受控事件，不接受扩展任意执行代码。

## 6. 数据、权限与安全契约

设计实体（均未建生产表）：

| 标识 | 关键持久事实与约束 |
| --- | --- |
| DM-AI-CHARACTER / PERSONA / WORLD / ENTRY / PROMPT / PROFILE | UUID、ownerSubject、版本、作用域/显式共享授权、关联资源；Persona只是角色扮演身份。连接profile只存Secret引用，模板/未知兼容字段保留并验证版本 |
| DM-AI-CONVERSATION / MESSAGE / CANDIDATE / CHECKPOINT | owner、角色/群成员、父分支/检查点、稳定顺序、消息/候选版本与上下文排除；约束跨用户外键归属，消息内容是私有业务数据，不写审计 |
| DM-AI-GENERATION / EVENT / TOOL-CALL | 原登录/权限版本、幂等键、模型与提示快照哈希、供应商请求ID、状态/序号/租约/取消、用量事实；唯一owner+幂等键，事件序号唯一，正文分块有保留上限 |
| DM-AI-DOCUMENT / CHUNK / INDEX / MEDIA | owner、来源/许可、内容摘要/字节/格式、引用计数、向量模型/维数/索引版本、索引任务；NVMe私有对象，清理不误删仍被聊天引用素材 |
| DM-AI-EXTENSION / VERSION / GRANT | 固定源commit/摘要、许可与依赖、协议/能力、权限审核、安装启停/更新/失败；不能只保存任意manifest JSON当作已实现 |
| DM-PET-PACK / INSTANCE / PREFERENCE / STATE | 包版本/许可/素材摘要、owner/角色绑定、大小/安全位置/物理/模型开关；sleep intent及单调版本/事件，临时逐帧坐标不入库不产生日志风暴 |
| DM-AI-BALANCE-SNAPSHOT | owner、连接/账户引用、money/points/window-quota类型、精确数值/单位、可空总额、窗口/重置/采样时间、Supported/Unknown/Stale/Error；不存账户凭据 |

Valkey只存可重建快照、去重/租约/限流/连接心跳，锁丢失不代表生成或付费请求未发生。所有稳定模型用EF migration与真实PG约束，不能用缓存、前端localStorage或一个大JSONB取代关系与归属；聊天正文/提示词/附件/密钥不放localStorage。

权限设计：`PERM-AI-USE`为显式授权能力，`PERM-AI-READ-OWN/EDIT-OWN/GENERATE/IMPORT-EXPORT`验证资源与允许的模型；`PERM-AI-CONFIGURE-SERVICE/EXTENSION-MANAGE/SHARE`为独立管理动作；`PERM-PET-CONFIGURE-OWN/STATE-OWN`与`PERM-AI-BALANCE-READ-OWN`仅本人绑定资源。管理员通过独立已审计的恢复流程管理孤儿数据，不能冒充原用户继续调用其私有连接。授权/停用/撤权复用Authentik+BFF，流式期间和Worker发起前复核，过期/退出清空页内私有状态。

角色卡/世界书/脚本/模型输出都是不可信数据：安全Markdown/代码渲染、DOM消毒；自定义CSS限定聊天工作面，不能遮盖平台确认/身份。Regex有超时和输入预算，宏/脚本有步数/深度/时间/内存限制；恶意正则/递归/工具循环不能拖垮API。工具schema、被授权资源、每次调用及结果有审计；运维写操作仍走原Application计划/确认/Job/Agent保护，提示注入不能取得Secret或把角色台词当用户授权。

导入角色卡PNG/JSON、JSONL、ZIP、媒体/文档和Git扩展须限制大小/解压比/文件数/媒体解码/路径与符号链接，先完整验证再原子提交，失败保留有效旧版本。私有媒体的GET/HEAD/Range同样检查资源归属，签名链接短时且绑定对象/用途，不因静态资源缓存泄漏他人附件。网页抓取/链接导入/模型自定义URL/余额自定义适配均防SSRF与DNS重绑定；默认拒绝元数据/回环/未授权内网，管理员显式批准的内部模型地址单独允许；每跳重验、域名/协议/目的端受控，凭据不随重定向跨主机转发。

## 7. 完整扩展生态与依赖落地

14 随包模块按原功能在C#/Vue内置重构，覆盖Assets、Data Bank、Caption、Connection Profiles、Expressions、Gallery、Summarize、Quick Replies、Regex、Image Generation、Token Counter、Translation、TTS、Vectors。66目录扩展逐项确定固定提交/许可、前端DOM/事件/宏/命令、服务端API、外部服务与存储依赖，再实现**等效功能或受控兼容适配**并做原有数据导入验证；两种路线均在账本记录差异，不声称原包二进制/JS可直接兼容。

原浏览器扩展依赖jQuery/DOM或服务器插件依赖Express/Node，C#不能直接加载。前端扩展应迁为Vue受控组件或独立源沙箱页+显式消息协议，不能注入主页面全局；沙箱不共享BFF Cookie/存储，消息校验来源/实例/请求ID与能力，CSP和sandbox权限不能组合成同源逃逸。后台代码runner非root、只读根、无Docker/Agent/socket/宿主目录、限制出站、CPU/内存/进程数/时间和可写工作域。动态路由通过BFF注册的受控命名空间转发与授权，扩展不能覆盖登录/管理接口。安装/启停/更新/删除有版本、任务锁、依赖预检、失败恢复与审计；不在生产启动时拉取main或npm浮动依赖。

实际依赖计划：

| 能力 | 依赖与部署验收 |
| --- | --- |
| 核心 | .NET10/EF、现有PG/Valkey、同源Gateway；每个新库/镜像锁版本/摘要与SBOM，空环境和升级验证；不新增酒馆原生账号 |
| 文本与分词 | 至少实际授权的Chat与Text Completion后端；固定tokenizer词表/模板与模型能力。不能只依赖一个订阅CLI承诺全部采样/多模态功能 |
| RAG与转换 | 真实embedding、向量存储方案/索引迁移、PDF/ePUB/HTML/Office转换沙箱；实现前确定版本/许可，未装LibreOffice或服务端插件不能宣称Office可用 |
| 图片/语音 | 实际选定的视觉/绘图（含ComfyUI工作流）/TTS/STT，按条件单独部署、健康/认证、禁网/超时/取消；模型文件、GPU和音频设备不自动授予 |
| 动画/立绘 | 带许可/摘要的透明视频角色包、解码/缓存/首帧检查；Live2D/VRM为酒馆清单另一路能力，锁定运行库/模型许可，不能拿视频动画冒充Live2D/VRM |
| 余额与可选桥接 | 实际供应商查询或额度协议、Secret引用、出站策略、缓存/退避；不支持查询则明确状态，支持适配仍须凭真实账户验证。Codex等登录仅在专用隔离工作域，不复制宿主开发配置 |

数据/媒体/运行缓存放NVMe，HDD保存一致性备份及校验清单；备份包含SQL、角色版本、世界书、预设、聊天与媒体引用、pet pack配置、Secret恢复引用说明和索引来源，向量可重建部分明确区分。隔离恢复后再切换，失败不覆盖有效数据；Secret恢复经专用受控通道，绝不把明文放普通导出包。内部服务不发布宿主端口，只有Gateway/BFF访问，出站代理和离线资源包单独测试。

许可事实：[酒馆LICENSE](https://github.com/SillyTavern/SillyTavern/blob/7e8663cd9c184a550b37238218bdd32c6efc68e9/LICENSE)为AGPLv3；[dsh-pet LICENSE](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/LICENSE)代码为MIT，但[README许可说明](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/README.md#许可)对动画/提示词/源视频另列非商用，形象列CC BY-NC-SA 4.0。C#重写不自动消除复制/改编源码的许可义务；逐组件记录来源/修改/NOTICE/对应源码交付方式与实际分发要求。优先参考机制并使用已获授权的zilii自有角色包；本轮未复制上游形象/动画，素材取得及许可确认是待完成依赖。

## 8. 分批实施及真实验收门槛

| 子任务 | 依赖与交付内容 | 必须取得的验收证据 |
| --- | --- | --- |
| DEV-AI-001 | 固定272项/模型能力与扩展提交、协议/schema、数据/权限/migration、来源许可、资源与Secret配置 | 每项有固定输入/输出及验收组，真实PG/Valkey迁移/并发/隔离；设计登记不算功能通过 |
| DEV-AI-002 | C#连接/生成/事件、角色/Persona、完整聊天/群聊/分支与Vue页面 | 上游数据导入/双向导出、授权真实模型、流式停止/继续/断线/刷新/重启、两用户拒绝 |
| DEV-AI-003 | 世界书/提示模块/模板/tokenizer/采样/调试、宏/Regex/STscript/Quick Replies | 固定上游实例差分与黄金用例；最终提示、顺序/预算/边界、恶意输入与脚本限制；原生配置实际改变请求 |
| DEV-AI-004 | 资料/RAG/总结/翻译/图片/语音/立绘/Live2D/VRM及14随包模块 | 实际转换/embedding/检索、媒体生成/识别/播放、模型条件与失败；独立存储核验与相关Vue页面 |
| DEV-AI-005 | 66扩展逐项源码/许可/依赖审计、功能迁移/隔离适配、生命周期页面 | 每条ST-ADDON的固定版本、外部依赖、正常功能/权限/失败恢复/更新和卸载证据；未审核项保持待实现 |
| DEV-AI-006 | Vue看板娘与角色包，动画/物理/碎碎念/聊天/工作状态/睡眠 | PET-ANIM/PHYS/WHISPER/CHAT/SLEEP真实浏览器交互、跨页/多标签/退出/减少动画；资产解码及实际模型链路 |
| DEV-AI-007 | C#余额/额度适配、隐私/预算/刷新与动画 | PET-BALANCE：真实数值与供应商响应核对、unknown/不支持/过期/429、币种/窗口/无分母、不泄密与睡眠去重 |
| DEV-AI-008 | 全依赖安装升级、备份恢复/离线包、负载/故障/页面与master交付门禁 | 272 ST+6 PET逐项签收、真实身份/实际依赖/SQL/文件/UI证据、部署来源一致、HDD恢复和新机；保留失败与修复记录 |

按当前P1/P2进度衔接安排，方案/契约先登记；功能按批次直接实现前后端，不先交原型或以最小聊天链路替代完整范围。每批可独立发布/修复；dev失败继续修复并覆盖，不自动回滚。上游孤立运行可用于对照测试，不作为最终产品依赖或“已完成C#重构”的证据。

统一验收组：

- **A-IDENTITY**：匿名401、未授权/跨用户403、CSRF/Origin、原登录退出/撤权/两小时期限、缓存与身份服务故障；普通用户只用本人授权AI，不读管理员运行数据。
- **A-SEMANTICS**：固定ST实例+输入/时钟/随机种子/模型响应的差分测试，覆盖每个ST ID及字段、导入导出/升级。概率和浮点/分词差异制定容差，不能“看起来差不多”即通过；保留真实外部请求验收。
- **A-RECOVERY**：请求前/远端发出后/部分流/落库前后取消、网络断开、API/Worker/runner重启、PG/Valkey/模型不可用、磁盘满与版本冲突；不重复扣费、不丢持久终态、未知结果诚实呈现。
- **A-DEPENDENCIES**：记录真实服务/模型/镜像摘要/原生库/素材版本、健康/认证/实际输出与清理；夹具和模拟服务只计协议测试。没有所需依赖/凭据的条目保持待条件，不作为最终通过。
- **A-UI**：真实Authentik→Vue配置/操作→C#→真实模型/文件/数据库/外部服务，全链请求ID和脱敏截图；320/1045/1440/1920px明暗/系统主题、触摸/键盘/焦点/读屏/减少动画/隐藏页/多标签，睡眠无骚扰、宠物不遮确认。
- **A-DELIVERY**：后端/前端必要检查、真实PG/Valkey、浏览器回归、供应商能力矩阵、负载/持续内存与解码预算、受控日志/审计、NVMe/HDD恢复/安装升级及源码版本一致。

验收记录必须逐条写明ST/PET ID、部署commit/镜像、配置/依赖、操作者与授权类别、输入/预期/实际、正常/拒绝/故障/恢复、UI证据及限制。每条缺一项仍未签收；不能把“已纳入计划”或上游CI通过记成Whale Deck实现完成。
