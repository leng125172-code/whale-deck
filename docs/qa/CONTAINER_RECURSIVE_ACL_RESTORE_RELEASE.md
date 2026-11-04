# 递归 ACL 恢复标准覆盖、生产迁移与验收准备

2026-11-05，运行版本 `eeb24dd1b9d55062dfb53b25527973817c9722f9`，独立证明工具候选 `7388f78`。承接[API/Vue候选](CONTAINER_RECURSIVE_ACL_RESTORE_API_UI.md)与[真实隔离PG](CONTAINER_RECURSIVE_ACL_RESTORE_PERSISTENCE.md)。**本批已标准发布并完成以下技术核验；尚未执行新真实Authentik→Vue→API/Worker→Agent→实际文件/HDD恢复整链，不签收恢复功能或59项整体完成。**

## 准确来源与一次标准发布

[CI 41894547867](https://github.com/leng125172-code/whale-deck/actions/runs/41894547867) 的完整head为eeb24dd，主运行completed/success；backend、installer、native-postgres、native-valkey、frontend_checks、六路浏览器与frontend聚合12路均成功。此前7906e45的完整CI也成功，但未拿它替代eeb的终态。等待期间仅独立预构建，不先安装；四镜像标签及隔离无网络CLI、两个自包含宿主CLI共六份实际build-info均为eeb，运行时/PKCS均10.0.13。

运行检出由user从390889d仅向前快进至eeb，来源为本地已推送Git对象，逐字匹配连接工具核验的远端CI SHA，无reset/强推/回退。真实发布前保存1434保护文件、来源Root1052、恢复Root32/HDD46、目录Root94、11核心身份/健康与两宿主e066基线，活动0。

维护观察器先就绪，正确地址为8081的 `/maintenance/status`，再仅启动一次已安装 `/usr/local/libexec/whaledeck-platform-maintenance update`。执行完整宿主发布安装、冻结镜像构建、Migrator、Compose三应用force-recreate；没有跳过构建/迁移、宿主重启或自动回滚。

- 发布unit `whaledeck-dev-publish-eeb24dd-af9981a0`，Invocation `5f363114e3f0459b92e3d2e8fb0eff42`。
- 预构建Invocation `b1d6d5e1b9b445cdb85640043c1ebe8a`；观察器Invocation `6a8c5f0f9f684e3d9b89aa10fddce971`。
- 三者均取得MainPID=0、active/exited、Result=success、ExecMainCode=1、ExecMainStatus=0及准确Invocation回执后归档，再只停止本批已完成QA残留；未停止产品服务。运行中默认Result=success未作成功证据。

## 安装与生产保护实查

六组件实际来源与API/Gateway HTTP均为eeb。两宿主实际 `/proc/PID/exe` 的build-info及apphost device/inode、运行进程DLL映射device/inode与安装文件逐项一致；Agent PID1379812、Maintenance PID1379807，均active。安装validator通过systemd/权限/UDS gRPC/受限helper/Docker访问、自包含运行时及动态发现入口；协议保持1.55。

生产只读SQL确认 `20261104182233_ContainerRecursiveAclRestoreIntents` 已应用1次，新表18个CHECK、12个发布fence、1个不可变guard，意图/活动数均0。新异常清单和原key查询两个匿名实际GET均401，不能当作管理员或普通用户登录验收。空表和运行中的Worker不等于实际恢复任务已执行，不向生产插入合成任务取得“通过”。

仅API/Worker/Gateway三应用正常替换，8个数据库平台容器ID/镜像/StartedAt保持，11核心均healthy。1434保护记录集合保持：只有发布账本内容正常变化；来源Root1052、恢复Root32/HDD46与目录Root94的原字节及九字段均保持。四份宿主配置的元数据按标准安装更新，但内容/mode/owner/group/size保持；其中capability key只有ctime变化，原inode/mtime及字节保持，没有轮换密钥。user NOPASSWD文件全部保持，产品Agent授权未扩大。账本currentRevision=eeb、previousRevision=390889d、pending=null。

观察器覆盖发布前至后置核验结束，367045ms内184个两秒样本全部HTTP200，经历healthy/updating/healthy；非200为0。只签收采样事实，不推断瞬时零中断，也不撤销前批67样本4次非200或签收整个P0-05。源与HDD同device2049的旧限制继续保留。

## 独立证明工具与测试边界

新增 `recursive_acl_restore_business_proof.py`：严格读取C# PascalCase计划/回执，绑定公开camelCase DTO、原意图/执行ID、source/frame/目标摘要、双Job/outbox/脱敏审计与原生Root/HDD/逐子项证明。拒绝重复/大小写投影碰撞/缺字段/浮点与字符串数值强转、错误来源/计数/Job、额外审计或明文路径；保留UInt64及带符号纳秒精度、ID域与来源原计数/当前恢复计数分离。取消SQL投影不能当作Root缺席证明，Partial/Unknown不由此正常完成证明签收；工具不读取HMAC密钥，MAC列格式检查不是独立密钥验签。

35项关联Root工具测试通过（含9项新增），零跳过，ResourceWarning作为错误；新代码严格Python编译、8范围及diff检查通过，CI installer已接新增9项。临时Linux ACL/逐项记录与no-op重留存实际执行，但SQL/C#投影、容器/HDD存储身份是明确夹具，不是生产SQL、安装RPC或真人页面证据。未重跑旧实机写入或把前批5048/660/1259测试计为本批重跑。

首次新工具测试发现临时夹具仍用tree路径，固定真实门禁要求work/recursive-tree；将自有临时目录与命令移至正确范围后通过，未放宽路径围栏。首次后置核验漏列标准安装对capability key的chown/chmod导致的ctime更新而拒绝，按安装脚本补入且逐字段确认只有ctime变化后通过；只读重新核验，没有重复发布或掩盖密钥更换。

私有证据 `deploy/artifacts/recursive-acl-restore-release-eeb24dd-af9981a088d7406c9231cc0f5eef35b3/` 含请求、准确CI、预构建及发布/观察器journal与Invocation终态、六组件及进程映射、生产迁移、完整前后快照、安装validator、184原始采样、首次核验拒绝与工具测试。标准预构建临时工作树已准确移除，原两配置仅链接到运行配置供读取，没有进入公开Git archive/Docker源码；预构建宿主包及证据保留。

下一步编排新自有容器/来源捕获，实际管理员通过历史前像选择、恢复预检/确认/取消和原引用重载，逐轮由独立进程核对全字节/ACL/非目标属性、真实SQL/审计与原Root/attested HDD再放行下一轮；完成原上下文正常双端退出、普通/匿名拒绝及精确清理，再完整恢复故障矩阵。当前只有证明桥接工具，完整真实浏览器门禁仍待接入；可恢复删除/长期清理、维护连续性及其余59项目标继续，17项移出不算完成。

项目图谱事件 `whale-deck.container-acl.eeb24dd.recursive-restore-release` 已获project/key/event匹配成功回执，pending移入synced，阶段保持active。


## 后续常规真实页面签收（2026-11-05）

原发布验收正文保留为当时状态。后续7837675真实门禁已完成新来源与五恢复场景、原上下文退出、清理前/后独立证明；首批工具失败及只读补核未抹去。见[常规真实恢复整链及未验边界](CONTAINER_RECURSIVE_ACL_RESTORE_REAL_UI.md)。完整进行中故障、真实恢复预览跨页、可恢复删除及其余59项继续。
