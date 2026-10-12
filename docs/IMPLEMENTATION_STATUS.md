# 实施状态与恢复入口

截至 2026-10-11（Asia/Shanghai），Whale Deck 的当前非 `HOLD` 范围已完成主体开发。本文分别记录源码、已部署运行时和待切换发布物，避免把“已经构建”误写成“已经上线”。

## 当前运行状态

- 工作站仓库：`/data/GitRepos/whale-deck`，分支 `dev`，已快进到当前远端提交。
- `whaledeck-agent` 与 `whaledeck-maintenance` 由 systemd 管理并开机自启；Agent 仅监听 `/run/whaledeck/agent.sock`，不监听 TCP。
- `whaledeck-api`、`whaledeck-worker`、`whaledeck-gateway` 已以容器运行。新版镜像已经在工作站完成构建，数据库的四个增量 migration 已执行；镜像会在新版 Host Agent 安装后统一切换。Gateway 只向宿主机回环发布 `18080/18081`，稳定入口由 MaintenanceHost 在 `0.0.0.0:8080/8081` 提供。
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
- 告警、审计、设置、计划任务、配置仓库快照/敏感扫描/固定参数提交推送、平台自检与更新计划。
- Authentik 稳定组 ID 到本地角色的动态映射、管理权限即时复核，以及按权限裁剪的全局搜索。
- Vue 前端已容器化，包含登录/加载流程、主题切换、响应式导航、仪表盘、个人门户及全部当前管理页面；管理员菜单、搜索结果和危险操作按权限显示。

真实工作站验收脚本覆盖：OIDC、概览、六类数据库、六类备份、应用生命周期、Docker 设置回滚、治理/告警/门户、Authentik 用户与 SSO、主机资源、容器日志/统计、诊断包、Job 幂等和 SSE 续传。临时测试资源均使用独立名称并在脚本结束时清理。

## 自动化验证基线

- `.NET 10` Release 构建：0 警告、0 错误。
- 自动化测试：141 项 UnitTests、29 项 IntegrationTests 通过。
- 前端：Prettier、Oxlint、ESLint、Vue TypeScript 检查、5 项 Vitest 和 Vite 生产构建通过。
- Agent 协议当前为 `1.6`；除容器日志和统计外，提供普通 Docker 资源管理、镜像元数据、有界事件时间线，以及注册资源与 Docker 资源相互隔离的预检计划能力。
- 工作站健康入口与 systemd 服务已复验；最近一次真实主机验收返回 40 个网络接口、8 条存储记录、20 条系统日志及有效诊断包。

## 安全与恢复入口

- Agent capability 最长 60 秒，绑定调用方法并使用一次性 nonce；UDS 同时校验 Unix peer credentials。
- 普通管理操作不能停止或删除 Whale Deck/`database-platform` 受保护资源；删除 Docker 标签不能解除宿主机注册表保护。
- 一次性 Secret 使用 Valkey `GETDEL`，按 Authentik subject 隔离并设置 5 分钟 TTL，不进入 PostgreSQL、URL、审计或日志。
- 迁移安全备份保留在 `/data/DockerData/migration-dcfs-20261009_163559`。最终验收后已删除旧 `dcfs*` 容器对象、空旧网络和 `/data/GitRepos/DCFS` 兼容链接，未删除任何 NVMe/HDD 数据或迁移备份。
- 临时 `/etc/sudoers.d/whaledeck-temporary-user` 已删除，完整 sudoers 已通过 `visudo`；普通 `user` 的非交互式全量 sudo 已失效，长期只保留 `whaledeck-agent` 固定 helper 的最小权限。

## 尚未收尾

1. 在工作站交互式执行新版 Host Agent/MaintenanceHost 安装，随后切换 API/Worker/Gateway 到已构建镜像并完成真实运行时回归；该步骤不会使用历史密码或临时全量免密 sudo。
2. 最终发布前使用正式账号再复核危险操作确认；匿名登录、Authentik 跳转、明暗主题、1045px 导航断点、600px 搜索图标、390px 手机布局和普通用户菜单裁剪已完成浏览器走查。
3. 决定并实施局域网正式入口：将 Whale Deck 切换到 `http://precision-7920-tower.local` 的 80 端口前，先确认与现有服务无冲突；其他服务继续按端口区分。
4. 创建稳定发布时再把 `dev` 通过 PR 合并到 `master`，并为正式镜像记录不可变摘要。

主机重启、系统更新安装和平台自身回滚属于高风险入口，代码与计划流程已提供，但不会为了验收而无故执行。GitLab、数据库恢复、容器终端、文件管理、防火墙/网络写入、外部通知、多主机、域名/HTTPS 仍为 `HOLD` 或 P2，不属于本阶段缺口。
