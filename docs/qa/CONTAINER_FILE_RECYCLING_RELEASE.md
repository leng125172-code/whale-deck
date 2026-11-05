# 容器回收版本发布前置与四镜像预构建

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
