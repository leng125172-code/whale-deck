# 容器回收标准覆盖与生产迁移核验

2026-11-05后续：准确58b8174eb1b0d67ca4b0d307fb8adeed568c5a33的CI42017763151已completed/success，12路全部成功（六浏览器分片及frontend聚合均包含）。已显式执行一次标准update：unit whaledeck-dev-recycle-a6a446a40554，Invocation e0ed9c9ec2084cc5a41dc70ac20bcebb，exited/0。未自动回滚dev。

实际六组件（Agent/MaintenanceHost/API/Worker/Gateway/Migrator）、真实HTTP及宿主/proc可执行文件与DLL映射均对齐58b8174。三个应用容器实际替换，八个数据库平台容器原ID/镜像/StartedAt保持，11核心healthy。发布账本current=58b8174、previous=34153d2、pending为空；previous指之前完整发布，不把宿主分项3725aa7冒充完整版本。

生产migration20261105101619_ContainerRecycleIntents已存在，CHECK18/fence13/guard1实际查询通过，新意图0/活动0。2346保护文件内容仅发布账本改变，另限定四项正常安装元数据变化；能力key除ctime外身份不变、内容不变，开发用户NOPASSWD:ALL保持。旧来源/恢复/目录/回收Root-HDD及指定业务计数不变。通用回收业务已上线，但尚未创建真实人类用户的回收任务。

ContainersView/FilesView/JobsView三个实际HTTP资源分别233414/338784/29687字节，与Gateway容器文件逐字节一致。两个匿名回收GET均401、业务写0。原生探针对新Agent只发首行和quit，实际能力响应minor56、stderr空、未发Plan/Apply/Restore等操作帧；合成资源header未创建容器，不将能力读取当作真实业务或身份验收。

维护入口以两秒间隔采样78次、约154.5秒，全200；不证明瞬时零中断。主发布与观测unit终态均成功，后置host validate、原件/业务保持及生产约束核对通过。此前203/EXEC预构建启动错误及18文件子封存保留。

私有总封存container-recycle-release-eaa62cd784ff41aab773749d762a316c共60文件（含原18文件子封存），RELEASE_EVIDENCE_SHA256.json摘要53da2ef35e1d0648d7fb3678312d80975cbaceb95e5b05ee2e942f3330ddceba；含完整CI回执、标准发布/观测/后置证明、实际资源、能力和准确边界。

下一步以全新自有资源和临时真实Authentik身份，验收Vue→Job→Agent→文件/HDD常规整链、原引用/权限/退出，再完整故障矩阵。独立恢复业务、完整元数据/目录、长期清理及其余59项继续；25%–35%粗估、整项/阶段签收和AI子计划0%保持。

## 此前发布前置记录（原文保留，仅代表当时事实）

2026-11-05，目标准确提交58b8174eb1b0d67ca4b0d307fb8adeed568c5a33，包含[回收Job摘要候选](CONTAINER_FILE_RECYCLING_JOB_RESULTS.md)。本批完成运行目录fast-forward、四镜像预构建与内部身份核验，以及发布/后置核验脚本准备；CI尚未全终态，未执行标准覆盖、生产迁移或真实回收任务。

## CI当前事实

准确运行42017763151仍in_progress，最后采集时frontend_checks、native-valkey、native-postgres、backend、installer及frontend-browser(1/6)成功；其余5个浏览器分片进行中，最终frontend聚合尚待。完整要求是准确headSha、completed/success及12个预期job全部成功，不能用局部成功或本地测试替代。短SHA首次查询为空，改为完整SHA匹配；GitHub连接器一次返回内部错误，使用现有gh读取同一仓库/运行，未重跑CI。

## 已完成预构建

运行目录/data/GitRepos/whale-deck从3725aa7 fast-forward到目标58b8174，保留私有配置；工作区干净。通过标准deploy/build-images.sh从Git archive构建，SDK/运行时/Node/Nginx使用现有私有配置中的固定digest，不把私有配置放入构建上下文。

初次直接将非可执行脚本用作systemd Exec失败，状态203/EXEC，未进入构建。保留原unit/Invocation与journal后，按标准维护程序已有做法使用bash调用，未chmod或改权限。新unit whaledeck-qa-recycle-build-58f7d1d8b1c0、Invocation bc4541c0d9a54a5090edd5996530853a exited/0。API、Worker、Gateway、Migrator四镜像的实际image ID、revision标签及无网络/只读/去capability容器中的build-info均匹配58b8174。

预构建只更新本地镜像标签，不替换运行容器。2346保护文件、11核心容器和既有来源/恢复/目录/回收Root-HDD及业务计数前后保持；宿主仍3725aa7，运行应用仍34153d2。发布账本current仍34153d2且pending为空；生产回收表与新migration记录在本批前置检查中不存在。

## 待执行脚本与准确边界

私有证据目录container-recycle-release-eaa62cd784ff41aab773749d762a316c中保存start-release.py、postflight.py、observe-maintenance.py，Python编译通过，尚未执行发布。start脚本要求准确CI终态/全部预期检查、干净且匹配的运行目录、无活动文件/终端任务及原基线保持后，才启动维护观测和标准update。它不会因后台CI完成自动运行，也不自动回滚dev。

postflight准备核对：实际六组件/HTTP版本、宿主exe与DLL映射、三应用容器替换及八数据库平台原ID/健康保持，保护内容仅发布账本变化和限定安装元数据变化，旧Root/HDD/业务保持，以及生产回收migration1/CHECK18/fence13/guard1且新意图0。以上是未执行断言，不是已通过的部署证据。维护采样亦未启动，不声称本批已有持续可用性结果。

预构建快照prebuild-snapshot封存18文件，清单摘要c2918a03c54e735338cc5da4f56833d9292855c51f800c2637030f5650a5cc23。外层目录与current指针用于继续发布，后续不得改写已封存子目录。

下一步继续读取准确CI终态，全部通过后显式执行标准update和后置证明，再真实当前身份→Vue→Job→文件/HDD整链与故障矩阵。四镜像、脚本或CI局部成功不增加59项完整签收；整体25%–35%粗估、酒馆/供应商P4-08与Live2D看板娘P5-05的0%功能进度保持。
