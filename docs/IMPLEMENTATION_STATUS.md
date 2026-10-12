# 实施状态与恢复入口

截至 2026-10-12（Asia/Shanghai），Whale Deck 的 MVP 与 P1 主体已部署，仍有功能细项和验收缺口。本文分别记录源码、已部署运行时和剩余功能；模块拥有页面或通过构建不等于全部功能完成。

## 当前运行状态

- 工作站仓库：`/data/GitRepos/whale-deck`，分支 `dev`，已快进到当前远端提交。
- `whaledeck-agent` 与 `whaledeck-maintenance` 由 systemd 管理并开机自启；Agent 仅监听 `/run/whaledeck/agent.sock`，不监听 TCP。
- `whaledeck-api`、`whaledeck-worker`、`whaledeck-gateway` 已完成强制重建与切换，均健康。Host Agent/MaintenanceHost 已安装并运行，数据库 migration 已执行。Gateway 只向宿主机回环发布 `18080/18081`，稳定入口由 MaintenanceHost 在 `0.0.0.0:8080/8081` 提供。
- 8 个 `database-platform-*` 依赖容器健康：PostgreSQL、MariaDB、SQL Server、MongoDB、Valkey 9、Valkey 7.2、Authentik Server 和 Authentik Worker。
- 数据库、缓存、Authentik、API 与 Worker 都没有直接向局域网发布容器端口。API/Worker 不挂载 `docker.sock`，所有宿主机与 Docker 操作均通过 Agent。
- Docker 使用有界 `local` 日志驱动（单文件 10 MiB、最多 5 个、压缩）并启用 `live-restore`。
- `database-platform-workstation-update.timer` 已启用，计划每周日 20:00 执行。
- 维护入口按当前启用的非容器 IPv4 网卡动态验收，支持板载、USB 等接口；回环可访问，Docker bridge 接口明确返回 403。

当前可从任意可达工作站网卡访问 `http://<工作站地址>:8080`。`precision-7920-tower.local` 的无端口入口尚未切换到 80 端口，不应在完成网络入口调整前写成已上线地址。

## 已实现并通过真实依赖验收

- Authentik OIDC Code + PKCE、HttpOnly Cookie、CSRF/Origin 防护、管理员稳定组 ID 映射、两小时权限快照和失败关闭。
- Authentik 用户创建/更新/禁用，以及批准类型的 OIDC 应用创建/更新/清理。
- 门户、普通用户/管理员字段裁剪、个人入口配置、概览与 6 秒指标采集。
- 容器清单、创建/复制、受保护资源策略、启停/重启/删除/重建计划、受保护容器批量操作、镜像拉取/更新/清理计划、网络/卷/镜像/systemd 资源页、有界日志、单次统计、事件时间线、更新历史和脱敏诊断包。
- Docker 配置读取、原生校验、原子备份、受控重启、健康等待与失败回滚。
- PostgreSQL、MariaDB、SQL Server、MongoDB、Valkey 9/7.2 的数据库或账号生命周期、授权、凭据轮换及连接管理适配器。
- 六类数据库备份、校验、记录、策略与保留清理；恢复入口未注册。
- 应用模板预检、安装、启停、更新、重装、卸载和原子配置目录。
- Job/Outbox、资源租约、重试、取消、Agent 操作恢复、幂等键、SSE 进度与 `Last-Event-ID` 续传。
- 主机信息、网卡、存储/SMART、journald、更新检查/安装入口、重启入口和受限诊断包。
- 主机进程列表/详情、CPU/内存排序、名称/PID 过滤与分页、每 6 秒采样；2026-10-12 在真实工作站读取 662 个进程，核对真实 Agent PID、启动身份、CPU/内存排序和采样推进，匿名 401、普通用户 403，临时用户已清理。无结束进程能力。
- 告警、审计、设置、计划任务、配置仓库快照/敏感扫描/固定参数提交推送、平台自检与更新计划。
- Authentik 稳定组 ID 到本地角色的动态映射、管理权限即时复核，以及按权限裁剪的全局搜索。
- Vue 前端已容器化，包含登录/加载流程、主题切换、响应式导航、仪表盘、个人门户及全部当前管理页面；管理员菜单、搜索结果和危险操作按权限显示。

真实工作站验收脚本覆盖：OIDC、概览、六类数据库、六类备份、应用生命周期、Docker 设置回滚、治理/告警/门户、Authentik 用户与 SSO、主机资源、容器日志/统计、诊断包、Job 幂等和 SSE 续传。临时测试资源均使用独立名称并在脚本结束时清理。

## 自动化验证基线

- `.NET 10` Release 构建：0 警告、0 错误。
- 自动化测试：209 项 UnitTests、49 项 IntegrationTests 通过（含进程差分 CPU、PID 复用、部分/过期快照、参数边界、固定 status 字段解析、内存溢出防护、只读管理员路由与错误分类，以及调试文档管理员授权、401/403 Problem Details、无重定向和路径边界）。
- 前端：Prettier、Oxlint、ESLint、Vue TypeScript 检查、11 项 Vitest 和 Vite 生产构建通过；进程权限失效会清空旧数据并作废在途响应。
- 页面水平溢出修复：Grid 卡片最小宽度、日志筛选栏容器断点与长资源名换行已补齐；22 项浏览器回归覆盖 320–1920px、1045px 断点、明暗主题、10 个有数据的管理页面切换（含进程详情抽屉）及空/错误状态，保留表格内部横向滚动。320px 顶栏收紧间距，额外断言工具栏按钮不能被裁切。
- Agent 协议为 `1.7`，新增 `host.processes` 只读能力，已随 `e1660f4` 部署并通过真实工作站进程验收；此前协议 `1.6` 提供普通 Docker 资源管理、镜像元数据、有界事件时间线，以及注册资源与 Docker 资源相互隔离的预检计划能力。
- 工作站健康入口与 systemd 服务已复验；本轮真实主机回归返回 33 个网络接口、8 条存储记录、20 条系统日志及有效诊断包；OIDC、Origin、CSRF 与容器管理员查询回归通过。

## 安全与恢复入口

- 生产调试文档入口已统一返回匿名 401、普通用户 403 的 Problem Details，含稳定错误码与追踪号，不触发 OIDC 重定向；管理员 OpenAPI/Swagger UI 真实验收通过。进程名称/内存取数已改为固定 `/proc/<pid>/status` 字段，避免框架补全进程名时读取 cmdline；这项 IO 修正的工作站复验待本轮发布完成。
- 2026-10-12 修复 Authentik 已配置管理员/OIDC 但租户 `setup` 标记缺失导致 `/setup` 500 的问题。安装阶段仅在已有可用管理员密码、两个 Whale Deck OIDC 应用和登录流程时补齐官方初始化收尾；不会重置密码。脚本默认只读，显式启用修复，重复运行无变更。
- Agent capability 最长 60 秒，绑定调用方法并使用一次性 nonce；UDS 同时校验 Unix peer credentials。
- 普通管理操作不能停止或删除 Whale Deck/`database-platform` 受保护资源；删除 Docker 标签不能解除宿主机注册表保护。
- 一次性 Secret 使用 Valkey `GETDEL`，按 Authentik subject 隔离并设置 5 分钟 TTL，不进入 PostgreSQL、URL、审计或日志。
- 迁移安全备份保留在 `/data/DockerData/migration-dcfs-20261009_163559`。最终验收后已删除旧 `dcfs*` 容器对象、空旧网络和 `/data/GitRepos/DCFS` 兼容链接，未删除任何 NVMe/HDD 数据或迁移备份。
- 临时 `/etc/sudoers.d/whaledeck-temporary-user` 已删除，完整 sudoers 已通过 `visudo`；普通 `user` 的非交互式全量 sudo 已失效，长期只保留 `whaledeck-agent` 固定 helper 的最小权限。

## 尚未收尾

1. 任务重试 API、前端确认与预检流程已发布；2026-10-12 临时容器真实验收通过：失败源任务保留，新任务使用当前资源预检，二次确认及幂等成立。治理、门户、告警与身份回归，以及 Docker 配置原值应用后的受控重启已在同日通过。
2. 最终发布前使用正式账号再复核危险操作确认；匿名登录、Authentik 跳转、明暗主题、1045px 导航断点、600px 搜索图标、390px 手机布局和普通用户菜单裁剪已完成浏览器走查。
3. 决定并实施局域网正式入口：将 Whale Deck 切换到 `http://precision-7920-tower.local` 的 80 端口前，先确认与现有服务无冲突；其他服务继续按端口区分。
4. 当前使用 `dev` 开发、测试、修复后覆盖发布流程；部署问题修复后覆盖，不自动回退开发版本。稳定发布和合入 `master` 属于后续发布节点。

## 功能细项缺口

以下是对源码与页面的核对结果，仍需逐项实现和验收，不能用上方模块级描述替代：

- Compose 项目的配置差异、拉取、重部署和项目生命周期完整闭环。
- 路由、监听端口、防火墙只读视图，大目录扫描与清理建议。
- 镜像更新检查、构建缓存详情、卷容量与依赖图的完整展示。
- 异地备份复制与状态；数据库配置差异；公共门户引用/隐藏和完整可用性探测。
- 通知已读/未读和历史；会话续期提示、最近登录与安全事件展示。
- 所有管理页面的操作、空/错误/过期状态及普通用户拒绝访问，需完成最终浏览器回归。

P2 的镜像安全扫描/SBOM、Compose Git 同步、数据库专用工具入口、受控结束进程、反向代理路由和多 Agent 尚未形成完整实现。macvlan/ipvlan 与 Web Shell 在功能基线中仍明确为设计预留；不因 P2 标签而解除其预留状态。最终交付须以已确认的非预留范围逐项验收。

主机重启、系统更新安装和平台自身回滚属于高风险入口，代码与计划流程已提供，但不会为了验收而无故执行。GitLab、数据库恢复、容器终端、文件管理、防火墙/网络写入、外部通知、多主机、域名/HTTPS 仍为 `HOLD` 或 P2，不属于本阶段缺口。
