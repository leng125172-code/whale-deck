# HDD 目录恢复 Agent 1.33：源码候选，尚未部署

## 已实现与边界

- 独立 `whaledeck.agent.v1.FileTreeRestoringService` 的 Plan/Apply/Inspect/Discard，能力 `files.tree-restore-intents`，协议次版本 33；不复用普通解压或回收的方法签名，单次短时 capability 与既有 UDS 调用方限制保持。
- 原历史捕获绑定源根/相对路径/版本、captureId、原资源 owner、Trash/Overwrite 类型和 manifest SHA；当前操作 owner 与当前 Actor 单独传递。历史 owner 不代表当前人类身份，未来 Application 仍须从可信历史索引解析捕获并对当前管理员授权，不能接受浏览器自选 owner。
- 强类型计划绑定输入归档 SHA/字节、内容摘要、条目数、总字节、目标父设备/inode、双根策略和独立计划指纹。UInt64 保持无损；不接受宿主任意路径、可执行命令、UID/GID、成员清单或私有归档目录。
- 与实时目录复制不同，原源目录可已不存在，允许恢复到同一原路径；目标必须尚不存在，原生 NO_REPLACE 发布仍生效。该步骤不是完整覆盖恢复，后续还须实现覆盖前目标留存/隔离及二次确认。
- Completed/Aborted/Unknown 回执单独保留原 captureId、输入归档摘要/字节和可能残留状态；新 operationId 不得被当作原 captureId，Unknown 不得捏造新版本或完成状态。
- 固定 root helper 新增 `tree-restore-plan/apply/status/discard`，沿用隔离 transient unit、精确实例取消和树操作预算，不扩大普通单文件 1GiB 传输限制。只加载五个固定 root 所有、非可写、单链接、非符号链接模块和既有可信备份适配器。
- 安装/校验新增恢复模块。只有精确匹配的旧默认根才增加 `files-tree-restore`，保留独立 `.pre-tree-restore.json`；新增冻结 `file-roots.v1.32.json`，自定义/收紧配置不动，备份冲突拒绝覆盖。旧捕获精确新增 restore/trash 授权兼容保持，不改写历史元数据。
- 新真实 UDS C# 探针及独立磁盘观察脚本已编写并编译：仅为自己创建的 2MiB+7 与 1GiB+7 合成目录执行回收/还原，检查原路径与另一目的地、方法签名/nonce/owner/指纹/取消/重放、完整 HDD 归档、原文件 inode 与新输出的全字节/元数据；仅精确清理其合成对象。**尚未运行，不能作为已安装或实盘通过证据。**

## 已执行验证

- .NET Release 全解决方案构建 0 警告/0 错误，包含新探针。
- 后端完整 1336 Unit、141 Integration 通过；6 项本机原生依赖测试明确跳过，不算真实 PostgreSQL 验收。定向新恢复/既有回收/方法签名 86 项属于完整单元集的子集，不重复累计。
- 工作站公开源码隔离副本 `deploy/artifacts/restore-agent-20261020a`：19 项恢复测试 91.584 秒、19 项配置升级测试 0.025 秒通过，均以 ResourceWarning 即错误运行；包括固定 loader 所有模块的属主/可写/硬链接/符号链接拒绝、固定 CLI 四动作/尾随输入与非法动作、取消先到与只观察重放、完整 1GiB+7 字节还原及独立重读、七个实际 fork/SIGKILL 检查点。HDD mount/udev 身份明确为测试夹具，不能当成物理 HDD/跨设备容灾验收。
- 新验收脚本标识符/探针入口与失败阶段两项非变更测试及 Python 编译检查通过。探针没有用 Python 引擎替代实际 RPC，真实执行仍待安装态验证。
- shell helper/install/validate 语法通过。同一隔离副本既有回收 23 项（106.944 秒）、解压 24 项（49.869 秒）、HDD 捕获 14 项（0.144 秒）回归通过，均以 ResourceWarning 即错误；整个顺序命令 exit0。连同上方恢复/根升级共 99 项不同 Linux 测试，不累计重复执行。观察完成前曾标为运行中，本条以最终句柄终态为准。
- 前序仅验收脚本/文档提交 `7e87a6d` 的 CI `39350038062` 四项已全部成功；这是前序证据，不冒充本批新产品源码 CI。

## 尚未完成

本批未安装、未更新线上容器/Agent、未进行新恢复真实身份 BFF 或浏览器验收；实际仍为 `e0175a0 / RPC1.32`。新 RPC 后续必须接 Application 当前管理员资源授权、永久双阶段任务、路径/捕获共享锁、Worker、事务审计、取消/Unknown 当前管理员处置、HTTP、公开目录恢复页面，再覆盖部署和实测。目录覆盖恢复、彻底清理/长期保留、容器文件及全部 76 项继续，P2-03/P2-04 不签完成。

GLM `glm-5.3-flash` 只读定位一次成功，164936ms，input252262/cached197184/output5004，零重试，费用未返回。只读定位结论由 GPT 对照原文件复核；权限/恢复边界、实际实现、测试及清理判断均由 GPT 完成，未发送凭据。
