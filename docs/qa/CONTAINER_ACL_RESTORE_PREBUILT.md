# ACL恢复冻结镜像与部署前基线

2026-11-03。运行源码目录`/data/GitRepos/whale-deck`由其所有者从08b2bb8干净快进至冻结候选`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`，来源是已推送开发仓库的同一Git对象；不是root改写开发者索引。随后只运行标准`deploy/build-images.sh`预构建，不执行宿主安装、生产migration或容器替换。

**四个镜像预构建成功且已核验，尚未部署或运行真实恢复。** 当前API/Worker/Gateway及宿主仍08b2bb8/1.52，11核心保持原ID/镜像/StartedAt且healthy；Agent684268/MaintenanceHost684263、发布账本current08b2bb8/pending空、7份受保护文件SHA/owner/mode保持。运行源码HEAD和实际运行版本分列，不从HEAD或可变latest标签推断部署成功。

## 已完成的实际检查

- 冻结Git archive构建上下文，不带`.env`、`.images.env`或`.git`；运行配置只从外部私有文件提供，未进入仓库、镜像上下文或日志。
- API、Worker、Migrator、Gateway四个镜像构建完成，实际镜像ID已保存；CLI或镜像内build-info及org.opencontainers.image.revision均准确为cad6182。CLI检查使用无网络、只读、cap-drop和no-new-privileges的临时容器，检查后移除，没有执行数据库连接或迁移。
- 编译和安装真实依赖完成；Ubuntu源下载较慢，Migrator仍正常安装了libgssapi-krb5-2及其依赖，没有删依赖、使用空实现或忽略失败。完整构建退出码0。
- 部署前所有活动Job、终端、主机锁、发布屏障和ACL/两递归等意图预检0，生产restore_source_json列不存在、CHECK仍17。
- 已安装的四个固定维护工具字节与候选匹配；7份私密/权限文件只保存SHA与元数据，不保存内容。包含user NOPASSWD规则及Agent能力密钥，均未修改。
- 公共状态接口HTTP200/source08b2bb8；预构建前后核心容器、两宿主进程和账本不变，没有启动任何新的发布unit。预构建不是六组件发布或真实权限恢复验收。

初次只读预检误写能力密钥路径`/etc/whaledeck-agent/capability.key`，在读取前FileNotFoundError退出；按安装器修正为`/etc/whaledeck/agent-capability.key`后取得完整基线。没有生成/覆盖/读取错误路径的密钥，没有修改配置或重做真实操作。

## CI及下一步

cad6182/run41619582863的installer、backend、native-postgres、native-valkey已成功，frontend已通过新增ACL历史/恢复/互锁组和容器布局组，正在最后的生产授权/文件/终端回归，尚无成功终态。运行中的日志blob尚未生成（404），仅按GitHub作业步骤状态记录，不假设其中每个未结束案例已通过。9db601c文档提交的独立CI亦未用于冒签cad候选完成。

待准确候选五路成功，再用固定维护update正常覆盖；发布仍会按标准流程重新核验冻结构建、宿主来源、生产迁移与容器替换。实际发布后的镜像ID与六组件来源需重新核对，不能仅凭预构建ID证明运行成功。失败继续测试修复覆盖dev，不自动回滚。

之后使用[真实恢复主验和独立核验器](CONTAINER_ACL_RESTORE_RELEASE_PREPARATION.md)的新独占资源，签收真实Authentik→Vue→SQL/Worker→Agent→实际ACL与写前当前原件HDD，并分别证明旧捕获保持、新备份与选中历史分列及正常双端退出。现有[API/Vue候选](CONTAINER_ACL_RESTORE_WEB.md)和[持久化候选](CONTAINER_ACL_RESTORE_PERSISTENCE.md)保留其原证据边界。

原件位于root私有忽略目录`deploy/artifacts/acl-restore-release-cad6182/`：部署前基线、预构建日志、4镜像ID/CLI/标签及受保护配置不变证明。未创建新的真实恢复意图、Root/HDD回执、Auth临时账号或恢复页面截图；不提高59项整项完成率。完整故障/递归ACL/可恢复删除/长期清理与其余59项继续，17项移出不算完成，master尚未交付。

收尾只读布尔查询首次未包to_json，psql返回f而JSON解码失败；改为to_json(EXISTS(...))后完成核对，无数据库写入。9份静态原件SHA与root目录0700/文件0600校验通过，目标第1节和此前3份QA全文保持，3份变动MD的594条相对链接有效；8项范围检查通过。

## 后续接续（2026-11-03）

本文件以上正文保留原时点证据与失败边界。cad6182随后取得准确CI五路成功、唯一标准覆盖和新独占六场景真实恢复及独立复核通过，见[实际发布与恢复验收](CONTAINER_ACL_RESTORE_REAL_UI.md)；后续通过不改写本记录当时的未部署/未验事实。恢复故障、递归ACL、删除及其余59项继续。
