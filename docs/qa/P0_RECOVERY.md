# P0 恢复安全与新版目标启动验收

日期：2026-10-13（Asia/Shanghai）。关联 `P0-03/P0-04/P0-06`；仅本轮子项证据，不代表 P0 或 P0–P5 全部完成。

## 已部署实现

运行时 `ad4f0d8`，Agent 协议仍为 1.15。部署 job `2e38dbfb-3f9d-4958-9d51-e061e1801642`；实际更新 unit inactive/dead、Result=success、ExecMainStatus=0。安装 helper 与该公开源码逐字比对一致，11 个核心容器健康，Agent/Maintenance/恢复 timer active。

修复：metadata 默认必须为 JSON object；注册表必须为 list 且元素为 object。journal 的 slug/job/paused/cleanup/pendingCleanup 显式校验，拒绝重复/非字符串/超预算暂停 ID。journal.job 必须绑定文件名任务 UUID。坏 JSON 类型不能阻断其他 job 恢复；坏记录保留待修复，不猜测并删除。清理 receipt/entry 字段在 unlink 前校验，避免删除后才因字段损坏报错。

## 检查与范围

- Windows helper：38 项（36 通过、2 POSIX 明确跳过）；Linux 38 项全部通过。真实临时文件/归档配 fake Engine，包含 null/数组/错类型/错 UUID/重复 paused、一个坏 journal 后继续恢复好 journal、人工暂停保留与清理字段损坏不能删除。这不是生产主机断电试验。
- PID 绑定：8 项本机及 Linux 通过；数字 PID/开始 ticks/cgroup/确切 helper argv 核验，打开 pidfd 后才发送；错误身份不发信号，句柄总关闭。
- Release 全解决方案构建 0 警告/0 错误；377 Unit + 62 Integration 通过，新增 NativePostgresFact 在本机明确跳过；真实数据库容量状态已有前阶段独立证据，不把跳过变成通过。
- 本轮没有 Vue 源码改动；既有前端 43 单元/96 全页/34 最终布局验收是上一阶段证据，不声称本轮重跑全部浏览器。

## 原生隔离应用验收

独立应用 `wdv-backup-2d5f1d21c3c3`，全链路 Authentik Cookie/CSRF → API → Worker → Agent → root helper/systemd：

- 配置、bind、独占命名卷内容精确对照，0600，SHA256/记录/校验有效；原容器 ID/StartedAt 不改变。
- 停止的外来容器挂载同一卷时，备份预检拒绝；移除自己的外来探针后可继续。
- 失效配置计划 job `9d9f96a9-93ee-4b49-9dc4-b3c0590b530a`：Failed/AgentRejected/AGENT_PRECONDITION_FAILED，在独立归档执行前拒绝，不暂停应用。
- 取消 job `cc5402b2-692e-45fb-9d28-9b94a774eed9`：Canceled 后恢复；Agent 受控重启时原独立归档 job 继续并成功，不创建替代任务。
- SIGKILL job `f4a95fba-470b-449e-a0d9-5bd167821be8`：确认服务活跃且应用 paused，核验 pidfd/身份后只杀指定归档 Python 进程；原 job 最终 Failed/APPLICATION_BACKUP_FAILED、应用恢复未暂停、partial 不存在、备份记录未伪造成功。ExecStopPost 恢复生效；其余容器 ID/StartedAt/Paused 未变化。
- 完成后仅截断本次自己的归档，再由 API 校验 job `c8de38d9-eefc-46a7-a423-10f98e4cad34`：Failed/APPLICATION_BACKUP_FAILED，坏文件未被解包/恢复。
- 在存活服务期间读到 CPUQuotaPerSecUSec=500ms、MemoryMax=268435456、IOWeight=50、NoNewPrivileges/ProtectHome/PrivateTmp/PrivateDevices=yes、ProtectSystem=strict、RestrictAddressFamilies=AF_UNIX，不使用已 collect 单元的默认值。
- PostgreSQL 任务/审计的 synthetic Secret marker 计数均 0；匿名记录读取 401。容量、迁移与生产管理员 OpenAPI 复验通过。
- 清理 job `64e4e7c8-7e62-49b9-bbe2-9a631f894666` 成功；自己的容器/卷/保留目录/HDD 归档及临时用户清理，成功归档历史转 Expired。最终 Docker 无 wdv-backup 容器/卷。
- 新运行时另复测应用策略：普通用户容量/记录/配置/策略 403，未经确认启用 400；手动保留只删旧归档、保留 Expired 历史，分钟自动备份/备份后清理、禁用和版本递增通过。清理 job `aba5d1a0` 成功，策略临时应用/归档/用户清理通过；证明收紧清理字段校验没有阻断正常原生路径。

此前受限 Docker SIGKILL 执行器权限拒绝，不能作为恢复通过；本轮使用用户已授权的开发 sudo，仅执行 pidfd 绑定的验收脚本。没有加入产品 RPC/任意 kill helper，没有关闭 Docker AppArmor 或挂载 host PID/Docker socket 到 API/Worker。

## 开发环境与恢复点

用户确认跳过独立原型，激活新版全量目标，旧 HOLD 全解除。user NOPASSWD:ALL 已按明确授权恢复；固定文件 `/etc/sudoers.d/90-whaledeck-development-user` root:root/0440，完整 visudo 与 sudo -n true 校验通过，Agent 的受限 sudoers 未扩大。之后由用户手动撤销和改密码，不自动撤销。

断线遗留的 `wdv-backup-46b45bd7244f` 无验证进程，全部任务终态、未暂停、无活跃归档单元；核验测试标记/Compose 项目/独占挂载后通过 API 精确补清理，其他数据保留，历史转 Expired。

## 仍未证明/未实现

真实主机断电/电源故障恢复、原生人工暂停/异常更换身份等更广范围组合、长期负载及新版全部功能仍须后续证据。坏 journal 类型校验本轮是隔离文件/fake Engine 测试，不冒充生产 journal 故障注入。数据库/应用恢复与文件下载尚未实现，已列入 P2，不再视为 HOLD。

外援：GLM Flash 仅只读定位 3 个公开文件；一次成功/无重试，输入 257970 Tokens（缓存 205952）、输出 4434，131644ms，实际费用未返回。GPT 完成实现、sudo/故障范围判断和最终核验。
