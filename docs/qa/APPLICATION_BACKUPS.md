# 应用备份实施与验收

## 状态

下一阶段源码（未发布）补充应用策略一致性 migration、每日及备份后保留清理、独立清理 journal/部分结果恢复、Expired 历史投影与应用备份前端。验收清单：备份/实时与暂停模式切换/指定归档校验/保留天数和份数/清理计划及二次确认/取消原任务/关闭重开/6 秒轮询；截图检查 320/390/1045/1440/1920px 明暗主题、长 SHA256、内部滚动和固定可见 footer。异常检查含 403 清空并禁止操作、503 保留旧历史且显示错误、迟到响应与关闭后的确认不得提交。自动策略须真实验证确认、版本更新、按分钟调度、备份后清理和禁用。不得将本地 mocks 记作真实工作站验收。

本轮 GLM Flash 只读定位：输入 440610（缓存 390912）、输出 5036，176655ms，一次成功/无重试、实际费用未知，路由器保留计量。其“检查文件中没有保留份数执行”的范围不能推广为已有数据库原生适配器没有清理；仅采用核实过的应用策略/页面定位，实施与安全判断由 GPT 完成。

宿主机归档适配器已接入固定 helper、独立 systemd 服务、Agent RPC/API/Worker，运行时 `d7b7075` 已发布。真实临时应用验收通过配置/data/独占卷、手动记录、只读校验、取消后恢复及 Agent 重启后接续原任务。**前端、自动策略和保留清理仍未完成；强杀归档服务与断电恢复、真实普通用户/共享资源等扩大验收仍待补齐。** 不把已有数据库备份或配置仓库快照称为普通应用完整备份。

归档设计覆盖三类批准数据：`compose.yml/.env/manifest.json`、本应用固定 `data` 目录、当前挂载或具有该 Compose 项目所有权标签的本地命名卷。容器可写层、tmpfs 与外部服务明确排除。HDD 固定目标为 `/data/DockerData/whaledeck/application-backups`，私有目录 0700、归档及元数据 0600；不提供恢复或文件下载入口。

## 当前代码约束

- `whaledeck-application-backup.py` 只接受固定 verb、slug、UUID、模式和指纹，不接收任意路径、命令或 Compose 文本。根权限只在正式固定 helper 安装后提供；正式安装前不能将源码接线视为运行时可用。
- CLI `run` 还必须属于与 job UUID 完全匹配的专用 systemd cgroup；普通临时 sudo 命令不能绕过正式恢复接线直接暂停应用。
- 根注册表与 Docker 名称/标签双重拒绝核心容器。共享卷、跨应用挂载、带 driver options 的卷、符号链接配置/路径均拒绝；未挂载卷的名称前缀不足以证明所有权，还须项目标签一致。
- 计划使用私有能力密钥 HMAC 绑定源文件和资源，不公开 Secret 的低熵散列或配置值。预检不暂停、无归档写入。
- `live` 明示文件可变；`quiesced` 先持久化恢复 journal，再暂停原来运行的项目成员。成功、失败或取消均应恢复这些成员，不能解除原本被人工暂停的状态。**文件系统暂停不等于数据库事务一致性**，数据库应使用其原生备份适配器。
- 流式 gzip/tar 有文件数、总字节、文件名长度及校验名字内存上限，不跟随源符号链接，不接受设备/FIFO。校验只读、不解压，检查 tar 范围与 SHA256。
- HDD 使用率和最小空闲容量在写入期间每秒或每 16 MiB 压缩输出重新检查，越过阈值停止写入、清理本任务 partial 并恢复原暂停成员，不能仅在开头检查容量。
- `.partial` 在失败时精确清理；已完成归档不自动删除。恢复只处理本任务 journal 中仍存在、仍归属原项目且未受保护的容器。

## 必须完成后才开放入口

1. 固定 root helper 枚举、独立 systemd 有界执行、ExecStopPost 与开机/定时恢复已安装；取消和 Agent 重启通过。尚须强杀归档服务/断电恢复演练，并在存活服务期间记录实际 CPU/内存/沙箱属性，不能把服务被 collect 后返回的默认属性当作运行证据。
2. Agent 原生预检/执行/校验/取消、单次计划绑定与 1.13 RPC 能力、Application 权限/确认/幂等/资源锁及 Worker 记录投影已接线，真实 PostgreSQL 记录和匿名 401 通过。扩大真实权限与失败场景仍待完成。
3. 应用卡片/详情备份入口、范围与一致性确认、任务进度、历史、校验及策略。暂停可能影响服务，必须二次确认。HDD 容量与失败提示不能省略。
4. 自动策略、保留清理与告警；不能以手动归档代替计划任务功能。清理只针对验证过的本应用备份集合，不能删除应用运行数据。
5. 独立临时应用真实 Env/Cmd/bind/卷数据、gzip 内容和 SHA256 对照；核心/普通用户拒绝、共享资源拒绝、失效指纹、取消、失败恢复、损坏归档、预算上限与数据不泄漏。
6. Vue 正常点击、明暗主题、320/390/1045/1440/1920px、空/错误/权限过期、迟到响应和弹窗按钮；逐项截图复核。

## 隔离验证

`python3 -m unittest discover -s tests -p test_application_backup_helper.py -v` 使用临时目录和 fake Engine，不调用真实 Docker 或 root helper。Windows 不支持的 POSIX symlink/flock 检查必须在 Linux 补测，不能把跳过记为通过。真实归档使用独立脚本 `deploy/validation/validate_application_backups.py`，不得以隔离检查代替。

2026-10-13 最终源码 `f0c82a0` 在 Linux 工作站隔离检查 18 项全部通过，包含真实 POSIX flock、symlink、63 字符 slug 上限和专用 systemd cgroup 护栏检查；Windows 为 16 项通过、2 项跳过。测试只使用临时目录和 fake Engine，未安装 helper，未暂停或重建任何现有容器。工作站仍使用上一轮 `a3a7971` 运行时，11 个核心容器健康；本次源码同步不等同备份入口上线。

GLM Flash 仅定位公开源码。其“手动备份没有 BackupRecord”的结论与当前 `JobRepository.EnqueueAsync` 不符，已由 GPT 直接核实并拒绝采用；实现和安全边界均由 GPT 负责。

本轮只读外援计量：`glm-5.3-flash`，一次成功、无重试，输入 1084105 Tokens（缓存命中 1024704），输出 6315，耗时 187897ms。资源包实际扣费未由响应返回，不记作 0；路由器已保存原始计量。

本轮后端接线 Release 构建 0 警告/0 错误，348 项单元测试与 62 项集成测试通过。Windows 归档隔离测试 21 项通过、2 项 POSIX 跳过；Linux 与真实系统服务仍须在发布后补测。只读外援 `glm-5.3-flash` 检查 Worker/JobRepository/GovernanceRepository 的现状，输入 195170 Tokens（缓存 156544）、输出 4773、125957ms、一次成功/无重试，实际费用未返回；所有实现与复核由 GPT 完成。

`1a14b05` 已发布，Linux 隔离 23 项通过，真实配置/bind/卷归档、0600、SHA256、记录与校验已通过。取消验收发现 Worker 并发保存冲突后丢失 Agent handle，误报 CanceledBeforeDispatch，而原服务仍运行；当前正在修复发送前持久化稳定操作 ID 和取消收尾。该轮临时应用及其测试数据已精确清理，核心容器未改变。整条备份链路尚未验收通过，前端、策略与保留清理仍未实现。

## 2026-10-13 修复后运行时验收

- `d7b7075` Release 0 警告/0 错误，349 项单元测试 + 62 项集成测试通过；Linux 归档隔离 23 项通过，包含持锁时恢复不得 unpause、释放锁后恢复。
- 固定 update 流程完成 Host 安装与 API/Worker/Gateway 覆盖部署，部署单元最终 inactive/dead、Result=success、ExecMainStatus=0。恢复 timer active，普通 user 全量 sudo 仍禁用；长期固定 helper 权限未扩大。
- 临时 `wdv-backup-4d5d603f4144` 使用真实 Authentik Cookie/CSRF，经 API → Worker → Agent → UUID 绑定服务运行。配置 Secret 仅在测试私有归档内读取，任务/audit 的 synthetic marker 计数均 0。
- 配置 `.env`、bind proof、独占命名卷 proof 与原文精确一致；归档/元数据 0600，SHA256、BackupRecord 和 verified 时间有效。原容器 ID/StartedAt 保持不变，无隐式重建。
- 真实取消返回 Canceled 后容器已 unpause。Agent 受控重启时原归档单元被观察为 active，同一个 API job 最终 Succeeded，未创建替代归档任务。
- 匿名备份记录 401。测试临时应用、命名卷、保留配置/data 和自己的 HDD 归档已精确清理，最终无 wdv-backup 容器/卷；11 个核心容器健康，Agent/Maintenance/恢复 timer active。
- 未实测强杀归档服务、真实损坏/共享资源/普通用户拒绝与前端；未实现应用策略/保留/恢复/下载。当前可签收的是手动备份后端闭环，不是完整应用备份功能。
