# 容器复制页面与安装态独立协议

2026-10-29。页面候选接在 `ab406eb` 的永久 SQL/Worker/API 之后；59 项继续 active，17项移出不算完成。

## 页面行为

- 容器文件页对普通、非保护文件及目录提供复制入口。冻结容器启动实例、源路径与源版本，目标只接收相对路径；不把文件内容经过浏览器。
- 先登记异步预检，Ready 显示完整项数、统一格式的字节量及五分钟期限，再明确确认执行。原预检和执行任务分别展示，取消有二次确认。
- sessionStorage 仅保存当前主体、容器 ID、原登记键和意图 ID，最多四个；不保存源/目标路径、文件内容或确认令牌，不静默挤掉未核实引用。无法保存引用时不发送首次写入。
- 丢登记、确认或取消响应后保留原引用；刷新只读原任务，不自动重新登记或确认。原登记重试只在当前页面仍有原命令时显式提供，复用原键与命令。
- 拒绝错误容器/意图/版本、变化的原计划和非法完成证明；查询失败禁用旧 Ready 确认。容器重启后只能查原任务，不能把旧计划换绑到新实例。
- 每六秒只轮询活动任务；隐藏、卸载和身份变化取消请求，迟到回包不能恢复旧界面。退出、401、换用户及管理员降权清理导航引用。
- Unknown 保留保护，文件页提供当前管理员明确处置。响应丢失只查原处置，必须读回更高版本的 Acknowledged；处置不等于复制成功，也不声称私有副本已删除。
- 复制期间互锁其他容器文件写面板。320/1045/1440、明暗主题下内容可滚动，底部操作可见，不改变通用顶栏样式。

## 已执行验证

- 全量前端：49 文件、584 项单元测试通过；lint、Prettier、类型及生产构建通过。
- 生产构建预览新增 28 项浏览器测试通过：文件/目录、三种宽度与明暗主题、丢响应刷新、自然轮询、实例变化、过期、403/404/503、显式取消、普通用户、异常处置及非法完成证明。已纳入 CI。
- 第一轮 22 通过/6 失败均为测试要求 `1 GiB` 而统一格式为 `1.0 GiB`；修正单位断言后重跑全 28 项通过，未修改产品单位格式或放宽布局边界。
- 主代理实际目视检查生产预览 320 明色长文件名、320 暗色目录、1045 暗色目录及1440 明色长文件名截图；长路径换行，内容与按钮没有水平越界。
- 关联容器详情/关系/读取/上传/编辑/历史/目录与跨标签退出页面 77 项通过；两轮合计 105 项生产预览浏览器通过，日志 `/tmp/wd-copy-ui-associated.log`。
- Linux-root 复制专项 42 项、关联容器文件 294 项全部执行通过，零跳过。

日志：`/tmp/wd-copy-ui-all-unit.log`、`/tmp/wd-copy-ui-{lint,format,build,browser}.log`、`/tmp/wd-copy-{root-tests,associated-root-tests}.log`。
这些浏览器测试使用隔离 HTTP 夹具，不能替代真实身份、SQL、Worker、UDS 与实际文件整链。

## 安装态 Agent 1.45 独立签名协议

权限恢复后，冻结提交 `ab406eba663ccf0fdc55f99d53fabfe7c1dbd8d5` 发布并覆盖 Agent 与 MaintenanceHost。二者 build-info 与冻结提交一致，宿主校验通过 systemd/UDS/权限/固定 helper/动态入口/HDD；运行框架及 PKCS 为 10.0.13。Agent active、NRestarts=0。

实际签名 UDS 和固定受限 helper 探针通过：独占真实 Docker 测试容器中复制一个完整二进制文件和一棵目录树，独立核对全字节、inode/版本、UID/GID、普通权限和纳秒修改时间；执行前永久取消保留 Aborted，目标没有创建。缺签名、错误签名域、请求体篡改、错误内核所有者和核心资源拒绝由探针验证。原 Root 记录保留，没有修改 API/Worker 容器。

独立第二进程重读三份归档与 Root 原件逐字节相同，2 Completed/1 Aborted；准确 CID 和测试源已不存在，11 核心容器全部 healthy。证据：`deploy/artifacts/container-copy-agent-6eaad75ec167437581f072004700bd4f/{verified,independent-final}.json`。

失败保留：首轮验证器相对模块路径在创建测试容器前报 FileNotFoundError；改用已验证摘要的 `/usr/local/libexec/whaledeck-container-files.py` 后通过。探针发布脚本无执行位，改为显式 bash 调用；未更改系统安全目录信任策略。
日志 `/tmp/wd-copy-host-{publish,install,validate}.log`、`/tmp/wd-copy-probe-publish-2.log`、`/tmp/wd-copy-installed-agent{,-2}.log`。

## 7c3facd 开发覆盖

冻结 `7c3facd4d3021890d9cd51b9af8755dd2666abdf` 已正常开发覆盖，无自动回滚。systemd 发布单元 `whaledeck-dev-publish-7c3facd.service`，InvocationID `ccbce1b97da84c2181bc82b852989e16`，终态 inactive/dead、MainPID=0、Result=success、ExecMainStatus=0。
实际 Agent/API/Worker/Gateway/MaintenanceHost/Migrator 六组件及 HTTP build-info 均匹配，宿主安装校验再次通过；复制迁移 `20261028083811_ContainerCopyIntents` 已入库，`container_copy_intents` 表存在、目前意图0，11核心容器全部healthy。
日志 `/tmp/wd-copy-installed-provenance.log`、`/tmp/wd-copy-host-validate-7c3facd.log`。GitHub CI `40801153829` 已启动，尚未确认完整终态；不能把本地测试和发布成功算作CI全绿。页面仍待真实身份/实际数据业务整链签收。

## 剩余门禁

CI终态，以及真实 Authentik Cookie → SQL/Worker → Agent/实际字节 → UI、进行中撤权/重启/未知结果处置整链仍需独立验收。Agent 独立协议通过不算完整复制业务已部署，也不代表整个文件管理或全部 59 项完成。
