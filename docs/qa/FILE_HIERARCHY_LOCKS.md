# 共享文件层级锁实机签收

2026-10-17，实际运行时6dd60dd60b4b0f5465e47b0cf595b98853f23502，Agent协议仍1.26。本次只签上传、普通文件变更和回收流程的共享层级锁改进，不签目录复制入口、整个文件管理或全部76项。

## 源码与自动化

- `FilePathScope`按Linux目录组件和Ordinal规则判断相同/祖先/后代冲突，不混淆兄弟、大小写和不同Unicode规范形式。
- 三个既有仓库统一传递逻辑位置，副本Token保持旧Hash。所有锁在同一41307341事务门闩下从原上传/变更/回收意图还原并核验；默认Legacy旧记录在成功事务中升级。无法确认owner/计划/Hash或元数据被篡改时拒绝新写入，不删锁或假定空闲；Unknown/过期租约仍保锁。
- EF迁移`20261017104235_FileHierarchyLockScopes`增加三个兼容字段和约束，保留既有主键、Restrict外键和锁。真实旧记录升级以及空库迁移均通过；没有清库或删除历史。
- Release零警告/错误，873 Unit/118 Integration通过；本机6原生Fact明确跳过。CI39026564309的backend、installer、native-postgres、frontend全部completed/success；安装171项文件Linux测试、前端218单元/136浏览器（10.2分钟）通过。原生CI另执行1、通过1、跳过0，非超级用户实际运行升级/约束/事务场景。
- 相同冻结源码工作站报告`dispatch-ebc5539003994d5d9b73d94b208ee863.json`：2026-10-17 10:53:35.658245–10:55:32.386794 UTC，Executed=1/Passed=1/cleanupConfirmed=true；SDK固定镜像身份`sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。只用隔离数据库与本地HTTP/TLS SMTP收集器，外部投递0。
- 新SQL辅助函数在同一实际Fact中覆盖父子双向争用、兄弟/大小写/Unicode、跨逻辑根、Legacy失败事务不落盘/成功升级、过期Unknown保锁、Path被篡成Token/孤儿锁失败关闭、两个真实连接竞逐父子和数据库约束。现有上传/变更/恢复/副本Token、事务失败和审计场景也在同一套件内实际执行。

## 实际发布

发布前源码精确6dd60dd、工作树干净、11核心healthy；上传/变更/回收/路径锁/终端均0，私有根策略未变、API/Worker无Docker Socket。确认旧维护单元/定时器不活动后，只调用一次固定`platform-update-schedule`；未因观察等待而重新调度。

实际更新服务在2026-10-17 **19:07:34 CST** `Deactivated successfully`，Result=success/ExecMainStatus=0，耗时1分19.930秒；不是仅Job排队成功。安装校验通过：宿主.NET/ASP.NET与PKCS10.0.13、UDS/权限/固定helper、真实HDD策略及当前网卡入口。Agent和Maintenance程序集包含完整6dd60dd提交号，三列模型已在生产库落地。

一个额外来源核验首次失败：将“宿主程序集嵌入Git SHA”的规则错误套用于API/Worker。其Dockerfile只复制源码、不复制Git元数据，也未显式传SourceRevisionId，所以容器程序集没有提交号；这不是本次部署失败或旧容器未替换。独立核实两个容器的Application程序集均有新FilePathScope、Infrastructure程序集均有FileHierarchyLockScopes及新失败关闭分支，并以实际新模型和链路复验确认运行改动。不把这些特征检测冒称完整镜像来源签名；统一容器构建来源标签/程序集提交号仍是P0/P5缺口，后续补齐。

## 覆盖后实际链路

通过真正Authentik临时管理员、Cookie/CSRF/Origin和维护入口/Gateway/API/SQL/Worker/UDS/已安装helper依次执行：

- `validate_files.py`：34,603,015字节完整上传/下载、7秒暂停、覆盖、SHA、幂等、取消栅栏、错误摘要、旧版本和人类审计通过。
- `validate_file_mutations.py`：65,543字节单文件复制/移动/权限与单叶mkdir、原登录/版本/幂等/CSRF/Origin/审计通过。输出中directoryCopyMovePermissions不表示目录树复制；脚本明确directoryTrees不在此范围。
- `validate_file_text.py`：UTF-8/BOM/CRLF/空/二进制与大小边界、原版本确认覆盖、真实HDD完整旧字节/私有回执及元数据审计通过。
- `validate_file_archives.py`：原登录计划找回、取消栅栏、普通文件回收、恢复不存在目标、覆盖前备份和只清选定版本、真实HDD字节/元数据/审计/永久目录通过。

全部脚本终态成功并精确清理自有文件/HDD测试副本；永久Job/审计/版本目录/根回执保留。独立目录查询临时身份剩余0；活跃上传/变更/回收/路径锁/终端均0，11核心healthy。没有操作生产故障注入、重启Docker/主机、改KDE/网卡/sudo或扩大Agent授权。

该文件源和HDD版本位于同设备，明确只是版本留存，不冒称异盘容灾；数据库NVMe/HDD架构未改变。物理坏盘/满盘/断电全组合不在这次生产验收范围。

前端没有新控件或布局变化。新运行时HTTP与活动Gateway的FilesView JS/CSS与本地已测产物SHA256相同：JS `c0eb763963ce2e1085bed517f61530af969e13a0f72c2b346ac9c7d8cfe75c65`，CSS `78bb8ae4c44e0774bbedc27cf40a028e83cc866f53453a8c79f8ce2370f325b3`。136 CI浏览器通过；没有把既有c60a7c7真实管理员/普通用户目测冒称本次重新执行的真实浏览器全量验收。

## 后续

新Python目录清单/复制引擎仍未安装、未注册新helper/RPC/API。下一步增加独立目录意图/异步计划、Agent契约与持久任务/取消/权限/审计，接入本层级锁并增加对应owner还原分支和页面，再完成目录移动、回收/恢复、归档、容器文件和长期清理。不同RootId仍是逻辑空间，物理bind别名须继续通过Agent inode与namespace锁核验，不能宣称SQL已经解析物理别名。全部P0–P5保持active。

GLM/glm-5.3-flash仅经只读router定位4个既有测试文件：489833输入/438208缓存/4721输出、205212ms、1成功/0重试，费用未返回。GPT复核入口、独立完成安全设计/修改/原生与实机验收。早期version局部变量重名、ArgumentException分析器、生成migration块namespace错误及容器来源检查假设失败均保留记录，未关闭分析器、降低审计或自动回滚。
