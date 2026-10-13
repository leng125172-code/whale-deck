# Whale Deck 开发空间与开发环境

日期：2026-10-13。用户新增需求；状态：**设计已纳入，功能待实现**。对应 [完整目标](GOAL_P0_P5.md) 的 P3-13–P3-18；直接开发，不设置独立原型确认阶段。

## 1. 产品范围

开发空间是代码调试、远程开发测试的隔离工作区，不是生产网站或应用。一个空间可以同时包含多种语言、SDK、调试适配器、前后端服务与独立测试数据库。用户可通过 Git 或上传导入源码，在浏览器/本地 IDE 编辑、构建、测试、调试和访问私有预览；平台自动保存运行证据，输出可用的报告。

- 开发空间：资源所有者、源码工作区、环境版本、运行历史、预览/会话、配额与存储策略的集合。
- 开发环境：可重复的环境模板，声明多个 runtime 的版本、锁定镜像、扩展、任务、依赖服务、资源上限；一个空间可切换环境版本，而不是一个语言一个空间。
- 调试报告：由实际执行证据生成，不把“应用启动成功”当成测试通过，不把未知异常自动判为确定根因。
- 生产发布：另走 P3 网站/P1 应用的导出、预检、确认和发布流程。本模块不自动升级生产、不挂载生产数据卷、不直接复制生产 Secret。

初始语言模板覆盖 .NET/C#、Node.js/TypeScript/JavaScript（含 Vue）、Python、Go、Java、C/C++；版本由模板配置并锁定。第一条端到端验收使用同一空间的 C# API + Vue/Node 前端，随后逐语言验证构建、测试及可用调试适配器。SDK 安装与调试器/扩展许可分别核对，不假设所有 VS Code 扩展都可在第三方 Web IDE 使用。没有真实断点验收的语言不标“支持调试”。

## 2. 使用流程

1. 管理员或已获创建权限的开发者选择模板，组合语言/SDK 与测试依赖，设置资源预算并创建空间。
2. 从 Git 仓库/分支/指定 ref 导入，或上传文件、目录、ZIP/tar 归档。展示来源、提交/清单、冲突与预计占用。
3. 安装锁定依赖，在空间内打开 IDE；定义/选择构建、测试、运行、调试任务及依赖顺序。
4. 启动一个或多个服务，通过身份校验后的私有预览访问；断点、单步、异常定位和终端仅作用于空间。
5. 每次执行产生 run，自动关联源码版本、环境版本、步骤、日志、测试结果、资源指标及错误，支持取消、超时和断线后查看。
6. 查看/下载 Markdown 与 JSON 报告和有界诊断包；停止空闲环境保留源码，重建环境不静默删除工作树。

## 3. 环境、源码与调试能力

### 3.1 多语言与可重复环境

- 共享项目源码工作区，多 runtime 服务按需组合，不把全部 SDK 强制塞入一个巨型镜像。
- 记录镜像 digest、SDK/工具版本、依赖锁文件摘要和配置版本；环境重建使用原版本，升级是独立变更，不在活动调试期间自动换镜像。
- 与 Development Containers 配置采用受控兼容子集：可导入认可的 runtime/task 配置；原始 `devcontainer.json`/Compose 的 privileged、host namespace、宿主挂载和任意宿主命令不能照单执行。
- 工作区内依赖安装/构建脚本视为不可信代码，允许在隔离工作负载运行，而非扩展宿主 helper 权限。
- 测试数据库/缓存独立创建并记为该空间资源。可申请管理员批准的测试库和凭据，默认不使用现有生产数据库账户。

### 3.2 Git 与其他导入

- Git 支持公开/私有仓库、分支/ref 选择、浅克隆、手动拉取及差异；凭据来自 Secret 引用，不出现在 URL、命令日志或报告。
- Git 请求的 URL/重定向/子模块/协议/下载网络受控，拒绝本地文件协议、越界路径及未批准内部地址；项目脚本不能借导入阶段在宿主执行。
- 上传支持文件/目录及 ZIP/tar，分块与续传仅写该空间暂存区；校验单文件/总大小、文件数、解压后大小、压缩比、路径、硬链接/符号链接和设备文件。
- 导入先验证暂存内容再提交到工作区；记录 commit/ref 或文件清单内容摘要。失败/取消不覆盖有效源码；脏工作树和同名文件冲突必须选择保留、差异合并或确认替换。
- 所有导入和工作树修改形成可追溯 revision。运行绑定启动时的源码快照/指纹；若运行期间允许编辑，报告标明 `sourceChangedDuringRun`，不冒称全部输出来自原 commit。

### 3.3 IDE、调试和预览

- 优先复用锁定版本的开源 Web IDE（code-server 等）和成熟语言调试器，不自研完整编辑器/调试器；选型及许可在实现前验证。
- 本地 IDE 接入使用目标绑定短时会话/受控隧道；不要求把宿主 `user` 或长期 SSH/面板 Cookie 交给项目容器。
- 调试器按语言协商 DAP 能力，支持 launch/attach、断点/单步/调用栈；只可 attach 本空间任务，不能发现或附加宿主/其他空间进程。
- 预览由 Gateway 校验用户、空间和服务归属，IDE/调试端口不直接对 LAN 发布。用户代码与 IDE 使用独立浏览器 origin，不能与 Whale Deck 管理 UI 共用同源 Cookie/存储；使用受控独立域名或 IP+独立端口映射，面板 Cookie Path 收窄并避免转发给预览服务。方案必须验证跨端口 Cookie 行为，不能把“不同端口”当 Cookie 隔离。
- 终端/IDE/调试连接必须验证 Origin、短时票据和角色；会话到期、撤权、空间停止时断开并收尾子进程，不只隐藏按钮。

## 4. 页面清单

| ID | 建议路由 | 关键内容与状态 |
| --- | --- | --- |
| PAGE-DEV-001 | `/development/workspaces` | 本人/授权空间，语言、状态、资源、最近 run；搜索/筛选、创建、空/错误/离线/权限状态 |
| PAGE-DEV-002 | `/development/workspaces/new`、`/:id/environment` | 模板、多语言版本/服务/依赖、CPU/内存/存储、预检差异与环境重建确认 |
| PAGE-DEV-003 | `/:id/source` | Git 分支/ref/状态、上传进度/清单、未提交修改、冲突与导入历史 |
| PAGE-DEV-004 | `/:id/develop` | IDE 入口、服务预览、任务/调试配置、运行与终端会话；撤权立即退出 |
| PAGE-DEV-005 | `/:id/runs`、`/:id/runs/:runId` | 步骤时间线、实时脱敏日志、退出码、测试/覆盖率、资源趋势、取消与失败原因 |
| PAGE-DEV-006 | `/:id/reports/:reportId` | 结论、复现信息、诊断证据、来源/完整性/截断标记、Markdown/JSON/诊断包下载 |

布局与字体/主题/响应式沿用现有规范；开发界面显示明确“开发/测试空间”。报告缺失、工具未支持或解析失败必须展示，不能用演示成功填补。

## 5. 后端用例与接口

建议 REST 前缀 `/api/v1/development/workspaces`，资源/会话/报告 ID 均为 UUID。Controller 只映射 HTTP；授权、计划、配额、编排和报告归 Application。耗时写操作 `202 + jobId`，执行 run 与 operation job 分开关联；配置版本使用并发校验。

| ID | 用例 | 输出/失败要求 |
| --- | --- | --- |
| UC-DEV-001 | 查询空间/模板/环境能力 | 按拥有/授权范围裁剪，包含配置版本、语言能力和限制 |
| UC-DEV-002 | 创建空间、环境变更计划与执行 | 配额/磁盘/镜像/端口/依赖预检，短时计划、幂等、异步进度 |
| UC-DEV-003 | Git 导入/更新 | 指定来源/版本、Secret 引用、冲突选择；失败不替换现有工作树 |
| UC-DEV-004 | 上传/暂存/验证/提交导入 | 所有者绑定上传票据、预算/路径检查、清单、过期暂存清理 |
| UC-DEV-005 | 创建/关闭 IDE、终端和调试会话 | 一次性票据、主体/空间/环境/目标绑定、连接预算、撤权断开 |
| UC-DEV-006 | 执行构建/测试/运行任务 | 不可变步骤配置、依赖图、资源锁、run/job、事件续传与退出结果 |
| UC-DEV-007 | 启动/附加调试与预览授权 | 语言能力及本空间进程验证；私有预览无授权返回拒绝 |
| UC-DEV-008 | 取消 run、收尾与断线恢复 | 可取消边界、杀本任务进程树、不重复执行、不遗漏终态报告 |
| UC-DEV-009 | 查询/生成/下载报告与诊断包 | 校验空间/run/工件归属、大小/保留预算、内容脱敏、下载审计 |
| UC-DEV-010 | 启停/重建/快照/删除空间 | 停机保留源码；删除/替换计划+确认，活动会话收尾、共享资源保护 |

Webhook 引入时沿用签名/防重放/固定仓库约束；本功能可先手动 Git 导入，不依赖 GitLab 服务部署。

## 6. Agent/Worker 契约

专用能力命名 `development.*`，gRPC 保持 `whaledeck.agent.v1` 并按次版本协商新增方法。这里只是设计，实际协议版本在源码实施后更新。

| ID | 拟新增 RPC/职责 | 输入边界 |
| --- | --- | --- |
| RPC-DEV-001 | PlanWorkspace / ApplyWorkspacePlan | 登记模板/空间/环境版本与计划 hash；不接受原始宿主 Compose 或路径 |
| RPC-DEV-002 | ImportWorkspaceSource | 源码来源引用、上传工件 ID、冲突策略；Secret 私有渠道与已批准网络 |
| RPC-DEV-003 | StartWorkspaceRun / ObserveWorkspaceRun | run/job/步骤版本、受控 argv/cwd（空间内）、预算；不得解释为宿主 shell RPC |
| RPC-DEV-004 | CancelWorkspaceRun | run/operation 与目标身份绑定；只结束该 run 的进程树/容器 |
| RPC-DEV-005 | OpenWorkspaceSession / CloseWorkspaceSession | 类型 IDE/terminal/debug/preview、主体/空间/目标、一次性 capability 和过期 |
| RPC-DEV-006 | CollectWorkspaceDiagnostics | run 与工件类别白名单、输出预算；不得抓取任意宿主文件或完整环境 |
| RPC-DEV-007 | ManageWorkspaceLifecycle | 登记空间/计划、start/stop/rebuild/snapshot/delete 枚举及数据保留策略 |

Agent 只管理登记工作负载与只读白名单路径。空间容器没有宿主 Agent/Docker Socket；run 的代码执行由空间内 runner/debug adapter 执行。Worker 协调任务、会话过期/孤儿回收、指标/工件持久化、报告生成与保留清理。跨重启凭原 run/operation 恢复观察，不重复启动测试；不可恢复的交互会话明确终态为中断，保留可用报告。

## 7. 权限与隔离

默认 User 保持仪表盘/门户。新增 Developer 表示 Authentik 显式组映射/资源授权结果，不建立本地密码用户；管理员控制模板、允许语言/网络、配额和委派。P3 先落地本人空间授权，P5 接入团队共享/节点/项目配额，不能为等待 P5 而对所有登录用户放开开发功能。

| 权限 | 默认 User | 授权 Developer | Administrator |
| --- | --- | --- | --- |
| PERM-DEV-CREATE | 否 | 被授予且配额内 | 是，配额/核心保护仍生效 |
| PERM-DEV-READ | 否 | 本人/被授权空间 | 获授权资源范围 |
| PERM-DEV-SOURCE-WRITE | 否 | 对应空间写权限 | 是，修改/导入审计 |
| PERM-DEV-RUN | 否 | 对应空间执行权限 | 是 |
| PERM-DEV-SESSION | 否 | 对应空间会话权限 | 是，打开本人会话并审计 |
| PERM-DEV-REPORT-READ | 否 | 对应空间报告权限 | 是，下载审计 |
| PERM-DEV-LIFECYCLE | 否 | 本人空间的批准动作 | 是，替换/删除二次确认 |
| PERM-DEV-TEMPLATE-MANAGE | 否 | 否 | 是 |
| PERM-DEV-GRANT | 否 | 否 | 是，授予/撤销显式开发权限 |

- 管理员的“是”不代表可隐身接管他人的现有交互会话；仅新建自己有权访问的会话，有可查审计。
- API 入队、Worker 签发、Agent 执行/会话建立分别校验目标与权限；2 小时权限复核继续适用，紧急撤权主动断开。
- 非特权、非 root 用户运行，capabilities 最小化、no-new-privileges、资源/pids/磁盘/网络预算；禁止 host PID/network、设备直通、宿主敏感挂载和生产网络。
- 默认仅工作负载私网；依赖下载按批准源/代理开通，私有预览经授权网关。不能以一般 Docker bridge 存在声称已经阻止全部横向访问，须验证实际 egress/跨网络策略。
- 项目空间不获得宿主 `user` 的 NOPASSWD:ALL；所有脚本/断点/变量访问属于不可信代码，内核共享容器不被宣称为绝对沙箱。高风险外来项目可使用后续 P5 VM 隔离模式。
- 空间资源使用独立 `whaledeck-dev-*` 命名及稳定 workspace/run ID 标签，并同时登记资源注册表；不放入 `database-platform-*` 基础依赖或普通生产应用目录。通用容器页不能绕过空间权限/活动会话锁来修改空间；标签本身不能赋权。
- 禁止 Secret/OIDC Token/API Key 进入 PostgreSQL 业务列、审计、报告或镜像层；日志过滤已知凭据并对敏感模式脱敏。无法证明任意用户代码不会打印未知 Secret，界面应提醒不要把生产凭据带入空间。

## 8. 数据库与存储模型

模型为待实现设计；稳定字段明确建表，EF migration、外键/唯一/索引/租户约束随实现生成，不使用生产 EnsureCreated。

| ID / 实体 | 关键字段、关系与约束 |
| --- | --- |
| DM-DEV-WORKSPACE | UUID、ownerSubject、可选 project/node、slug、state、environmentVersion、storageRootId、quotas、Version；节点/slug 唯一、所有者+状态索引 |
| DM-DEV-ENVIRONMENT | 模板/版本、manifestDigest、immutable/创建时间、依赖/任务规范；模板版本唯一，不存凭据值 |
| DM-DEV-RUNTIME | environmentId、serviceName、language、SDK/toolchainVersion、imageDigest、debugCapabilities、预算；环境/服务唯一、多行支持多语言 |
| DM-DEV-SOURCE | workspaceId、revision、Git URL（无凭据）/ref/commit 或导入清单 hash、originKind、worktreeFingerprint、dirty；空间/revision 唯一 |
| DM-DEV-RUN | workspace/source/environmentVersion、jobId、actorSubject、state、started/ended UTC、exitSummary、sourceChangedDuringRun、traceId；空间/开始时间索引 |
| DM-DEV-STEP | runId、stepKey、依赖、state、exitCode、start/end、timeout/truncated、artifactRefs；run/stepKey 唯一，步骤可多个语言并行 |
| DM-DEV-REPORT | runId、schemaVersion、state、summary、redaction/truncation、evidenceRefs、integrityHash、retentionUntil；run/报告版本唯一 |
| DM-DEV-SESSION | workspace/run/目标、actorSubject、sessionType、issued/expiry/revoked、state；只存票据摘要，不存可重用票据或终端内容全文 |
| DM-DEV-GRANT | workspace/主体或组稳定 ID、动作集、版本与授予者；资源/主体唯一、不复制身份资料 |
| DM-DEV-ARTIFACT | workspace/run、类别、approvedStorageId/相对定位、bytes/hash、state/expiry；归属/大小/校验和约束，下载查权限 |

空间源码/可重复 SDK 缓存与运行数据放批准 NVMe 根，报告/归档可按容量策略写 HDD。根由管理员登记存储 ID 并检查真实设备/权限，不能把目前 HDD `/data` 自动认作 NVMe。空间之间目录/UID/共享卷隔离，备份写 HDD 后再可选异地；root registry 保护生产资源。

Valkey 保存有 TTL 的上传/会话一次性票据、运行进度与缓存，可从 PostgreSQL/Agent 重建；长期日志与工件不放缓存。默认诊断/报告保留 14 天（可配置、有配额）；源码保留至显式删除/快照策略，不按报告保留期删除。工件到期先记录过期再有界精确清理，不因空间停止自动删代码。

## 9. 任务依赖与验收

| 任务 | 实施包 | 完成证据 |
| --- | --- | --- |
| P3-13 | PAGE-DEV-001/002 + 环境/空间模型 + UC-001/002 + RPC-001 | 同空间 C# 与 Node 组合、镜像/版本可重复、创建权限/配额、失败清理 |
| P3-14 | PAGE-DEV-003 + SOURCE/ARTIFACT + UC-003/004 + RPC-002 | 公私 Git/ref 与上传原生导入、冲突与取消、恶意归档拒绝、凭据扫描 |
| P3-15 | PAGE-DEV-004 + SESSION + UC-005/007 + RPC-005 | Web IDE + 本地接入；真实断点/单步；权限/票据/跨 origin/过期/WS 测试 |
| P3-16 | PAGE-DEV-005 + RUN/STEP + UC-006/008 + RPC-003/004 | 多服务运行、编译失败/测试失败/超时/取消/崩溃、Agent/Worker 重启、不重复执行 |
| P3-17 | PAGE-DEV-006 + REPORT + UC-009 + RPC-006 | 自动收集并下载 Markdown/JSON；实际测试解析/指标关联/脱敏/有界，失败也有报告 |
| P3-18 | UC-010 + RPC-007 + QA/文档 | 空闲停机/重建保留源码、删除确认、NVMe/HDD/备份、跨用户/越权/核心保护和精确清理 |

先完成 P0 权限/任务/指标基础、P1 创建容器及 P2 受控文件/备份能力；P3 内并行准备模板，但不跳过安全边界。不等 P4 GitLab 才提供通用 Git 导入；P5 接入团队与多节点。当前 P0 监控聚合任务继续，补充范围不暂停活动目标。

### 报告必须包含

- workspace/run/job/trace 标识，UTC 时间、作者、源码 commit/文件清单/dirty 与执行期间变化、环境 digest 和工具版本（不是环境变量全文）。
- 执行步骤、耗时、退出码、失败/取消/超时/崩溃状态；构建诊断含文件/行号（若工具提供）与可复制的空间内复现步骤。
- 测试数量、通过/失败/跳过、失败案例；支持实际 TRX/JUnit 等解析，覆盖率仅在实际采集时展示。格式不支持/结果缺失不得算全部通过。
- 有界 stdout/stderr、异常栈、服务启动/健康探测及资源摘要；日志/采集不完整、过期或截断须标注。
- 区分确认事实、规则建议和待查原因，允许跳转原证据；默认不向第三方模型发送源码/日志。敏感变量、Cookie、Token、完整进程环境和调试变量转储默认不采集。
- Markdown/JSON 下载及受控诊断包，文件归属校验、内容摘要/校验和、脱敏标记、保留期限；不执行不可信报告 HTML/脚本。

最终真实验收：在一个隔离空间导入 C# + Vue 项目，分别断点调试，执行成功测试及预设失败测试，观察前后端私有预览，自动生成与下载两份事实一致报告；断线后找回 run，停止/重建后代码保留，撤权后会话断开，清理只移除该测试空间。其余模板分别验收，不用一条组合示例代替所有语言支持。

## 10. 实现参照（不等同已实施）

- [Development Container Specification](https://containers.dev/implementors/spec/)：可重复环境配置的兼容参照，Whale Deck 的允许子集及隔离要求是本项目设计。
- [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/overview.html)：复用语言调试适配器的协议参照；授权、进程启动与隔离由平台另行实现。
- [code-server 官方 FAQ](https://coder.com/docs/code-server/FAQ)：Web IDE 与扩展可用性的选型核对入口；最终版本/扩展许可、访问方案和原生调试能力在实施时验收。
