# 可核对的构建来源

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
