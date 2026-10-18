# 可核对的构建来源

## Linux 安装态签收：b118454

固定源码 `b118454c7867ba3bf797541798e3508ffb6a6738` 已覆盖部署，以下“尚未部署”为历史记录。本阶段仅签收构建来源闭环，不代表完整 P0/P5 或 76 项完成。

- [CI39083441049](https://github.com/leng125172-code/whale-deck/actions/runs/39083441049) 四作业全部成功：1070 Unit/126 Integration、250 前端单元/203 浏览器，完整直接/间接依赖审计、五个真实程序离线 CLI 均通过。Linux 上 11 项真实 Git/archive/冻结构建流程与明确进程 double 边界测试通过；后者不是实际 Docker 证明。
- 工作站同源码冻结实际 PostgreSQL 报告 `dispatch-695b2d3414ab425abccf49f240cfc0bf.json`：00:40:24.768951–00:42:21.322786 UTC，执行 1 / 通过 1、清理确认，SDK 实际 ID `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。无外部投递，SQL 层 Root stub 的证据边界仍保持。
- 正式更新前四个实际镜像从 git archive 构建；逐个无网络/只读隔离运行内部 CLI 或读取 Gateway JSON，与各自镜像标签完整提交号一致。没有先替换线上容器。完整 CI 成功后，只调度一次覆盖更新。
- 更新服务实际 08:57:05–08:57:56 CST 成功终态，wall 50.639 秒、`Deactivated successfully`。不是仅 timer/Job 调度成功；数据库依赖实例未重建。首次跨版本启动前仅安装新的固定 maintenance helper，避免正在运行的旧脚本被不同长度内容覆盖；仍由既有固定调度入口执行，不改变 sudo 策略。
- 运行中 API/Worker/Gateway 按实际不可变 image ID 核对 OCI revision 和内部版本；Agent/MaintenanceHost 实际二进制离线 CLI、隔离 Migrator CLI、实际回环 API/Gateway HTTP 均返回完整 b118454、版本 0.1.0。Migrator 镜像 OCI revision 另独立查询一致。Agent RPC 协议版本仍 1.28，与产品构建版本是不同概念。

| 实际运行镜像 | 接收验收时 image ID |
| --- | --- |
| API | `sha256:3729fc0a3a8d116c7d58a5db81cf85f33047f8c8fc244b543ba34064746ac4f4` |
| Worker | `sha256:27fc0737781ebb95de9afcb6b9a6c0ba423406db792de0addda598c5f45d844a` |
| Gateway | `sha256:9ecc600700433efa278037f2986bf9e6edd626d9324f86d22f80217999366fc2` |

重复构建的 OCI manifest/attestation ID 可以不同；本阶段证明同一冻结源码绑定和运行内容，不声称位级可复现构建或密码学供应链证明。

- 实际 Authentik 新临时管理员→BFF→生产网页通过：320/1440、明暗主题、两个完整 SHA tooltip 与实际二进制/HTTP一致、无脚本错误/水平溢出、管理写请求 0。真实截图已目测320暗；没有用 Playwright route mock 冒充此验收。临时 Cookie 只经隐藏 SSH/进程内存/stdin，未写日志/文件/argv。
- 实际 OIDC 完整登录、Origin/CSRF、面板与 Authentik 同时退出、跳回登录页再回归通过；既有公共/通知 HTTP JS 与活动 Gateway 字节核对通过。四种验证身份前缀最终独立查询全部 0。
- 安装态 host validate 通过 systemd/权限/UDS/受限 helper/动态入口/HDD 策略；宿主 .NET/ASP.NET 与 PKCS 保持 10.0.13。11 核心容器 healthy。六类未结束文件工作流、路径锁和打开终端在维护前均 0；不触碰业务文件、网络/KDE/sudo 配置。
- 实际验收脚本 SHA256：`validate_build_provenance.py` `3e023614324a2b1212dc76eb2fe4da2013b0102e988b82e1e852617dfcec6692`；构建器 `e0622ac1ebe7bfcada6a361e87ffb03294b86cb8f1b01e4913a48bd1c9d79b35`。ignored 私有 QA channel.py `27bbaf63b09e195a3e66ba543d2c658cd80c28fd25ab5bcb6bebc66a8c4e3e82`，browser.mjs `2ceed780da73c5d31730107cd65794c2c2024344f72e4ff742e12cd845e3a860`，bridge.ps1 `38a009a588136af3c6b241c3e580a7f105dbb5b78dfba398e7214ab89ff28586`。不把这些后续 QA 脚本说成 b118454 的源码 CI 已包含。
- Windows PowerShell 备用宿主发布路径仅完成语法检查，未执行另一轮 Windows→Linux 自包含发布；本阶段实际安装证明来自 Linux 固定发布路径。故障/篡改防护与完整新机/离线安装继续按 P0/P5 验收，不由版本卡片替代。

## 新增源码，尚未部署

先前宿主发布会传 `SourceRevisionId`，但 API/Worker/Migrator 的 Docker 上下文排除了 `.git`，没有显式传提交号；不能拿宿主 SHA 当作容器程序集来源。此缺口独立于已修复的 PKCS 10.0.13。

本批变更：

- 五个 .NET 入口支持仅参数 `--build-info`，在配置、DI、端口、Socket 和数据库访问之前输出三个公共字段并退出。字段来自实际程序集 informational version，不读可变环境覆盖；缺失/非完整/带 dirty 后缀的 SHA 不捏造为已知提交。
- API 公共 `system/status` 保留 `V0.1.0` 兼容形状，新增只读 `build` 对象与 no-store。固定程序名称、版本、源码 SHA 不包含内部地址、路径、环境值或身份。
- 四镜像同时记录 OCI revision/source/version；三 .NET Dockerfile 显式传入 SDK `SourceRevisionId`。Gateway 有只读镜像内 JSON，独立 no-store HTTP 路由，不由启动环境替换。
- `deploy/build-images.sh` 要求已提交干净工作区、拒绝不匹配 revision，真实 `git archive` 冻结公开源码，私有配置只用于 Compose 插值、不进入上下文。构建后再次确认 checkout 未变，清理仅本次 mktemp 子目录；不删镜像、容器或数据。
- Compose 非构建命令允许 `unknown` 默认值，避免普通查询/既有首次升级因未设置新变量失效；Dockerfile 对缺失或非完整 SHA 失败关闭。新安装/更新通过固定构建器传入 SHA。仅显式人工回退旧提交保留旧构建路径；旧二进制不冒称通过新来源验证。dev 不自动回滚。
- PowerShell 宿主构建也改为冻结提交与显式传 SHA，不再直接读取构建期间可变的工作目录。
- 平台维护页展示实际 API 和 Gateway 版本/短 SHA（完整 SHA tooltip），不一致告警、获取失败不显示配置版本兜底；明确不据此证明 Worker/Agent 已同步。
- 安装态校验 `deploy/validation/validate_build_provenance.py --revision <40位提交>`：固定容器的不可变 image ID/标签与实际 `--build-info`、Gateway 镜像 JSON、两宿主二进制和隔离 Migrator CLI，再对照实际回环 HTTP。拒绝核验中容器替换；不读取 Docker Env，不执行迁移、不连数据库、不监听端口。它是可核对的构建元数据，不是密码学供应链证明。

## 本地证据与待验收

- 最终 Release/全方案测试零警告/错误，1070 Unit/126 Integration通过，6个本机原生明确跳过。
- 五个实际本地程序集离线 CLI 逐一输出恰一个 JSON 并成功退出，没有配置依赖。它们此时由未提交工作区构建，自动读取旧 HEAD，仅证明 CLI 行为，不作为新提交来源证明。
- 250 前端单元、类型/lint/格式和生产构建通过；新六项生产预览浏览器全部通过，320/1440明暗截图已生成，已独立目测320暗。
- Python来源核验5项明确进程 double通过；冻结Git/archive的6项shell用例在Windows明确跳过，待Linux CI实际执行。首次测试方法误覆写 unittest.run、后续Windows路径识别错误均已修正，保留初次失败，不把skip当通过。
- 真实冻结镜像/宿主安装、全部实际 CLI/标签/HTTP 与生产身份网页仍未签收；最后安装态仍8d77293 / Agent1.28。本批不修改数据库schema，也不由此签完整P0/P5或76项。
- GLM/glm-5.3-flash只读定位构建入口：820684输入、768640缓存、5905输出、261604ms，1成功/0重试，费用未返回；GPT负责实际设计/修改与复核。未共享私有配置。
