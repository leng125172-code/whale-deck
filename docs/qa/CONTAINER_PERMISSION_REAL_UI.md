# 容器普通权限：覆盖发布与真实页面常规整链

> 后续：五类真实页面中断/重启/目标消失Unknown及不同管理员处置已通过，见[故障证据](CONTAINER_PERMISSION_FAULTS.md)。下方未验范围为当时记录；所有者、递归、ACL、删除及其余故障继续。

2026-10-30。运行版本 `cb4c4000db7a186cacd890554ae5fc7cd594ddd6`，Agent协议1.47。此次签收非递归普通模式修改的常规整链；所有者、递归、ACL、进行中取消/撤权/重启和Unknown故障矩阵仍继续，不能签完整文件模块。当前范围59项，17项移出不算完成。

## 开发覆盖与运行来源

- 原单元 `whaledeck-dev-publish-cb4c400.service`，InvocationID `6e734e4ae36849cca4482e8b150130d0`；同一次发布2分20.387秒结束，journal明确Deactivated successfully。没有重开发布或自动回滚。
- 六组件实际build-info、镜像revision与API/Gateway HTTP来源一致。额外核对宿主MainPID的exe device/inode、托管DLL映射inode均为当前安装文件。权限迁移和`container_permission_intents`已入库。
- 安装态宿主校验通过：受限helper、UDS、权限、运行库及动态发现入口可用。11核心healthy；既有database-platform容器ID/镜像/运行状态/StartedAt与发布前一致。
- 新API/Worker实际无宿主发布端口、不挂Docker Socket、非privileged。开发user的NOPASSWD:ALL继续保留，root:root/0440与单文件/全量visudo及sudo -n true通过；产品Agent规则未扩大。

## 真实浏览器与实际磁盘

使用Chrome、正常Authentik用户名/密码表单登录、真实BFF Cookie/API/Worker/已安装Agent和固定helper；没有响应替换、会话注入、SQL/缓存写入或伪造Root回执。只操作准确命名的非root、无网络临时容器和合成文件。

- 六场景：文件0640→0600、0600无变化核实、0600→0640恢复，目录0750→0700→0750，以及第六个Ready计划先返回继续等待、再明确取消。最终5 Succeeded/1 Canceled；取消未生成执行Job或RootStarted，事实保持null/false。
- 每个计划刷新页面后仅接续原引用，再发送明确确认。六次原登记、五次原确认HTTP重试均返回原ID/执行Job，未重复业务操作；页面登记6次/确认5次，文件内容PUT为0。
- 1440/320与明暗主题均有真实页面证据；320暗色文件、320明色目录已目视复核，整页无横向溢出，预检警告、原模式、确认和取消可见。六张预检PNG与完整SHA256清单保留；成功终态由可见页面断言核验。
- 正常从用户菜单双端退出，WD旧Cookie401、Authentik拒绝，页面回到登录页。缺CSRF400、错误Origin403；新普通身份8路403、匿名4路401。
- SQL双Job/Outbox/事件/审计与五份原Root回执、五份物理HDD元数据逐一核对，计划和历史独立域摘要重算一致，模式事实true、历史verified和发布版本一致。HDD元数据不包含文件内容。
- 完整2097159字节文件与同内容子文件均读回一致；原inode、UID/GID、普通mode及纳秒mtime保持，目录混合属主2100:3100和子项2200:3200保持，无变化操作的完整实际指纹不变。只比较chmod允许变化的ctime，未把ctime保持列为承诺。
- 验证后准确临时容器、子文件/目录及源已清理，永久SQL/Root/HDD历史保留。独立只读进程重读原SQL/Root/HDD、摘要与准确资源不存在，临时身份0、活动/租约/屏障0、11核心不变；不声称删除后又读了物理字节。

本轮真实业务使用HDD绑定源与物理HDD元数据位置，不冒称本次验证了NVMe来源、内容异盘灾备或跨盘业务。先前内部引擎的两设备证据独立保留。

## 验证器失败与重验

- 第一、二轮在第一个任务成功显示和原确认重试之后，关闭容器详情时误用`.el-dialog__headerbtn`，实际为`.el-drawer__close-btn`。第二轮新增细分阶段和仅登录后产品页面截图定位；没有修改产品来适配脚本。
- 第三轮六个浏览器场景和正常退出已通过，后置Python将计划的容器执行OwnerUid/Gid与实际inode属主混淆而失败。GPT最初复核时引入了这个错误，修为执行身份1000:1000、SourceIdentity属主2100:3100分别验证，完整实际指纹仍独立检查。
- 三轮原失败、日志和私有现场证据保留；分别核对完整内容、原指纹/最终Root事实及准确CID不存在后，只删除各自合成源，不改永久SQL/Root/HDD，也不把失败轮改记通过。第三轮五份SQL/Root/HDD修正观察器后另行核对通过，但原父命令仍记失败。
- 第四轮完整父命令退出0，`actualPermissionBrowser=passed`、`verifiedFixtureRemoved=true`；独立只读观察器退出0，五份原回执及元数据一致，身份和活动保护清零。验证源码冻结`364f6b0`并记录三个文件SHA，与运行产品cb4c400区分。

## 检查与证据位置

本次重跑3375 Unit/213 Integration通过，7项默认原生依赖跳过；695前端单元、类型检查/生产构建、26普通权限及9传输Linux-root测试零跳过、12宿主运行库/5来源/8范围检查通过。此前隔离真实PostgreSQL与100生产预览页面证据继续有效，未冒称本轮重新执行。GitHub CI仍在运行，尚未签全绿。

- 发布：`deploy/artifacts/container-permission-deploy-cb4c400/`，before/provenance/verified/isolation及原发布结束journal。
- 第四轮：`deploy/artifacts/container-permission-browser-93203b6e769c401d90386afd94bec1da/`，baseline/browser/verified/independent-final、五份SQL/Root/HDD、after、六张PNG和screenshots/validation-source摘要。
- 三轮失败：`container-permission-browser-43365fd1ff844f1b93c6bdf7ae1abd5f`、`container-permission-browser-fd8a0434cd354923a4f7a71fdbaeb506`、`container-permission-browser-2e3d573699174554931c46667967ddb1`，各自失败和准确清理证据保留。
- 日志：`/tmp/wd-container-permission-real-browser{,-second,-third,-fourth}.log`、`/tmp/wd-container-permission-independent.log`及宿主/来源日志。以上为私有本机证据，不入Git；本页只记录非敏感结果。

继续权限真实故障矩阵、所有者/递归/ACL、容器删除、长期保留和剩余59项。保持dev开发、测试、修复、覆盖发布；master尚未正式交付。
