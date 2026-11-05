# Unknown 摘要修正标准覆盖与生产后置核验

后续：固定26对象重命名Unknown已有[独立实机页面与跨管理员处置分项](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_RENAME_GATE.md)。以下未验收说明是本次发布时的边界，原发布证据不改写，也不因此签收其余故障组合。

2026-11-05，[上次发布中断](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_RELEASE_INTERRUPTION.md)之后，公开 NuGet 包完整下载恢复，沿用准确 CI `41965780773` 已 12 路成功的产品候选 `34153d2c454942b2665902772b7d5e48226cf7db`。本批完成四镜像预构建、一次新的标准覆盖及生产后置核验，**关闭混合版本和未部署缺口；真实 Unknown 故障/任务页/另一管理员处置整链仍未签收**。

## 新的发布回执

运行检出保持 clean、准确 `34153d2`，不为发布切换依赖版本或镜像 digest。四镜像由现有 frozen-source 构建器构建成功；对 API、Worker、Gateway、Migrator 镜像逐一运行不联网的 build-info 只读入口，镜像标签与实际二进制均为目标版本。没有修改 Docker/主机代理、路由或关闭 TLS 验证；两次公开包只读下载分别完整获得 5930986 字节，只作为依赖恢复诊断，真实构建成功另有独立回执。

预构建后新启维护观察器，再即时采集保护文件、来源/恢复/目录原件、核心身份及业务表基线，确认无活动恢复。一次显式标准 update 绑定新 unit `whaledeck-dev-unknown-retry-cbf10720`、Invocation `8976cb0c1daa473bbec91d78a12f6cb3`，走宿主发布安装→四镜像标准构建→Migrator→三个应用容器强制覆盖与健康等待→发布账本完成。没有跳过标准步骤、重放旧故障任务或自动回滚。

新的发布、后置检查、预构建和观察 unit 均有各自准确 Invocation、exited/ExecMainCode=1/ExecMainStatus=0/Result=success 回执；不使用不存在 unit 的默认成功值。postflight 依赖实际发布 unit 成功后执行，读取本批专属 pointer/即时基线，不复用上次启动或旧版本的运行身份断言。账本 currentRevision 为 `34153d2`、previousRevision 为 `ac90911`、pending=null；本次控制器回执 completed，不重放应用、不自动回滚。上次 interrupted 回执仍保持为 interrupted。

## 实际生产核验

| 检查 | 结果与边界 |
| --- | --- |
| 六组件与实际 HTTP | API、Worker、Gateway、Migrator、Agent、MaintenanceHost 的实际 build-info 均为 `34153d2`；API/Gateway 实际 HTTP 一致 |
| 宿主进程 | 两服务当前进程的 exe 和 DLL 映射均匹配已安装文件的 device/inode，不只核对磁盘元数据 |
| 核心容器 | 11 个 healthy；3 个应用容器被替换，8 个数据库平台容器 ID/镜像/StartedAt 与本次发布前完全相同 |
| 生产恢复迁移 | 已有目标迁移恰 1 条，18 个 CHECK、12 个 fence、1 个 guard，恢复意图 23、活动 0；不把已有迁移再记一次新增迁移 |
| 原件保持 | 来源 Root 1910、恢复 Root 252、恢复 HDD 166、目录 Root 94，全清单/字节摘要/身份保持 |
| 业务保持 | 恢复 23 意图/18 执行/活动 0；普通递归 ACL 62 意图/56 执行/118 Job，与即时基线一致 |
| 配置与权限 | 2292 保护文件路径集合相同；仅账本内容变化。限定正常安装元数据变化，capability key 仅 ctime 变化；user NOPASSWD:ALL 与最小 Agent 权限保持 |
| 维护采样 | 112401 ms、每 2 秒一次、57 样本全部 HTTP 200；仅签采样范围，不证明瞬时零中断，也不补签上次重启缺口 |

另从运行 Gateway 找到实际包含 Unknown 新标签与警告的 JS 资源，通过真实 HTTP 请求读取同一资源并逐字节比较，共 1 个 23648 字节 bundle，包含“回执已证明修改”“未知回执计数限制”“0不代表未发生修改”。这证明新资源实际下发；不是已登录页面截图、真实 Unknown 结果或人工可见性验收。

## 证据与保留范围

私有目录 `restore-unknown-retry-cbf107200e5b4e49afbebf107b8924a9` 封存 32 文件，清单 `RELEASE_EVIDENCE_SHA256.json` 摘要 `4a2e3a908ec64f258fb86eecc0e00492f02e08fc9732a56687bb66d5e8a7129b`，逐项大小/SHA256 复核通过。包含准确 CI、四镜像身份、预构建/发布/后置检查/观察 journal 与 unit 状态、即时前后基线、生产约束、宿主映射、实际下发资源摘要、完成账本与控制器回执、脚本及明确验收边界。原中断封存 30 文件逐项保持，不覆盖旧 current 指针或重写失败采样；旧候选/Partial/退出验收亦未重做。

没有新建来源/恢复业务意图或临时人类账号，本批没有真实 Authentik 登录、Unknown 故障注入或管理员处置。预构建通过后显式发起的是新的标准发布尝试，不能与上次中断合并为一个成功操作。

## 下一实施点

继续独立的 26 对象 Unknown 真实门禁：首项恢复后，固定 `item-01.bin`→`renamed-item-01.bin`，以已完成的专属原件 proof 验证聚合 Unknown0/0/0/0/26 与已证明首项真实改变1分别成立，迟到不续做。需新增对应真实 SQL/API/Job、原管理员页面及另一个管理员暂不处置/一次 ACK 的编排与独立核验，沿用冻结闭包、权限拒绝、原件保持、失败留存及准确清理。

现有 Partial coordinator/UI/verifier 有明确 Partial 硬编码，不能把该入口或历史 Partial 回执当 Unknown 门禁；现有 Unknown 单元 proof 也不能当工作站验收。其他故障矩阵、可恢复删除与59项继续，完整主任务/阶段完成数及整体25%–35%粗估不变；酒馆/供应商/看板娘功能仍0%。

收尾：范围 8 项通过、本地 Markdown 链接 2082/缺失 0、diff 及 32 文件封存再核验通过，两 Git 索引保持用户所有。项目图谱首次传输失败，同一 eventId `whale-deck.container-acl.34153d2.unknown-ui-release-cbf10720` 重试获得 projectId/key/eventId 匹配、duplicate=false 回执，pending 移至 synced、阶段 active；未将发布分项提升为整个阶段完成。
