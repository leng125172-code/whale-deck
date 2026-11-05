# 独立回收恢复：标准覆盖与生产迁移

2026-11-06后续：全新资源常规真实身份/Vue/Jobs/原inode/HDD整链及Ready取消已分项通过，首轮工具失败与API收尾保留，见[实机记录](CONTAINER_FILE_RECYCLING_RESTORE_REAL_UI.md)。本页以下保留发布当时尚未执行业务的事实；不补签完整故障矩阵。

2026-11-06，准确提交 `3d80d17b7b68c1fd6909a3ff6220b5381217eac7` 已通过 [CI 42104459496](https://github.com/leng125172-code/whale-deck/actions/runs/42104459496)：completed/success，12 个预期检查全部成功，包含 6 个浏览器分片及 frontend 聚合。随后显式执行一次标准维护 update，未自动回滚 dev。

发布 unit `whaledeck-dev-recycle-restore-edffb4451c88`，Invocation `d32836b24a694883b6172bfe30ee24a3`，最终 exited/0。运行目录仅以仓库用户 fast-forward 到准确提交，保留私有配置；只读 Git 使用 GIT_OPTIONAL_LOCKS=0。发现的运行目录 index 归属异常只修正该文件，未递归修改仓库或私有证据权限。

## 实际发布与数据核验

- 四镜像和两宿主程序预构建来源一致；707 个宿主文件 SHA 校验通过。标准 update 仍走冻结源码、固定镜像 digest、自包含宿主构建/安装、迁移及应用强制替换的完整流程。
- 实际 Agent、MaintenanceHost、API、Worker、Gateway、Migrator 六组件及真实 HTTP 均匹配 `3d80d17`。两宿主实际 /proc 可执行文件与 DLL 映射匹配安装文件；.NETCore/AspNet/Pkcs 均为 10.0.13，安装后 validate 通过。
- 三应用容器实际替换，八数据库平台容器原 ID、镜像、StartedAt 保持，11 核心服务健康。发布账本 current=3d80d17、previous=58b8174、pending=null；previous 是上次完整发布，未把 f333a69 的宿主分项安装冒充完整版本。
- 生产迁移 `20261105184338_ContainerRecycleRestoreIntents` 实际存在；19 CHECK、14 写入围栏、1 个保护触发器，新恢复意图 0、活动 0。旧 16 条回收意图完整行前后相等，旧递归来源/恢复业务快照保持。
- 2346 个保护文件内容仅发布账本改变。安装元数据变化仅限账本、Agent sudo 配置、resources.json、agent.env 和能力 key；四个配置内容保持，能力 key 除 ctime 外原身份保持。开发用户 NOPASSWD:ALL 保留。
- 既有来源 Root1964、递归恢复 Root257/HDD169、目录 Root96、回收 Root32/HDD35、独立恢复 Root5 全部保持。本批未创建文件恢复任务或改变身份、原件、留存来源。

## 页面资源与维护观测

ContainersView、FilesView、JobsView 实际 HTTP 资源分别为 245296、344828、36554 字节，与运行 Gateway 文件逐字节相等；对应恢复按钮、异常处置入口和独立恢复安全结果文案均存在。三个新恢复匿名 GET 返回 401，业务写入 0。FilesView 是全局异常处置入口，恢复来源按钮在 ContainersView；发布前已据实修正资源检查脚本的文案匹配，没有把静态资源读取称为页面交互验收。

维护入口两秒采样 55 次、约 108.372 秒，全部 200。发布及观测 unit 均成功，保留原日志和准确 Invocation。采样不证明瞬时零中断，也不补签先前失败的维护连续性场景。

## 证据与剩余边界

私有证据目录 `deploy/artifacts/container-recycle-restore-release-c3f46c657fa644f494eaa7add107a01b` 保存准确 CI、两项构建回执、14 份冻结源码、六组件来源、发布日志、迁移/旧行/Root-HDD 对比、安装运行时、HTTP 资源与维护观测；SHA 清单另见本目录封存文件。私有配置和凭据不入 Git 或项目图谱。首次封存检查发现本批 request.json 仍属创建它的仓库用户（0600，父目录0700），已仅将该新证据文件归给root，字节保持，生产不受影响；原检查失败与修正记录保留。

最终封存52文件（含14份冻结源码），`RELEASE_EVIDENCE_SHA256.json` 摘要 `f116233179468e9100f38fc03f1ced60c6ca1ac7b7139242767e722089dfe8c1`；本地932个文档链接和8项范围检查通过，第1节完整目标正文逐字保持。

本批只签标准覆盖与生产核验。之前的构建预览含身份/依赖替身，安装态签名 RPC 也没有永久业务页面；均不能替代这次尚未执行的真实 Authentik→Vue→恢复意图/Jobs→Agent→原 inode/HDD 整链。下一步使用全新自有容器及临时真实身份，先在页面创建可回收来源，验证独立恢复 Ready 取消、另建意图成功、任务摘要、权限/原引用/正常双端退出及独立清理证明，再推进故障矩阵与长期清理。

59 项活动范围不变，17 项移出不算完成；整体 25%–35% 仍为粗估，整项 0/59、阶段 0/6、酒馆/供应商 P4-08 与 Live2D 看板娘 P5-05 功能 0% 保持。可部署 master 的完整交付尚未完成。
