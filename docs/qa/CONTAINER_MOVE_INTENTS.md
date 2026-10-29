# 容器移动：独立计划、永久回执和三态事实候选

2026-10-29，冻结源码`c2c78b7da2924a16a7ad8c72bc1106ebb55705e1`。产品运行时仍`7b3f44e`/minor45，未安装移动入口；单工作站59项active，17项移出不算完成。前序FD候选见[范围适配器](CONTAINER_MOVE_SCOPE.md)。

## 实现及边界

新增`whaledeck-container-moves.py`，复用复制模块严格的私有回执存储/锁原语，移动的计划域、OperationKind=Move、内层命令、状态观察及物理事实单独实现。计划绑定容器/实例、两端路径、源九字段身份、两端父inode、目标父版本、配置/内核用户证明和完整内层计划。文件必须原生move，目录必须独立move/copyPlan及移动策略；复制/权限命令不能重解释为移动。

计划摘要采用独立域SHA256；它不是独立的请求授权签名。固定helper/短时capability、签名RPC、业务意图与共享发布保护仍需后续实现，不能直接暴露本内部引擎。

回执永久保存外/内层关联、内层完整摘要、容器ETag、目标身份和`destinationPublished/sourceRemoved/sourceRetained`三项可空布尔事实。Completed要求目标全内容/树摘要及身份匹配、源名缺失、私有原件身份/字节/manifest全部验证，三项均true；计划期明确未开始的取消可以false。Unknown不能用false代替未证明；已发布目标但源尚未完整保留时保留单独的正面事实。`privateMayExist`描述未确认的暂存/部分现场，Completed的已知原件另外由sourceRetained=true表示，不冒称私有目录已为空。

恢复只观察/封闭，从不重新复制、重命名或移除源。Running但无内层记录、容器/绑定丢失或部分原件保留保持Unknown；原任务之后Apply不继续副作用。Completed历史不可变，当前目标、保留原件或内层回执摘要变化只令bindingCurrent=false。

关联审查修复两处：目录移动从原先的复制归一属主改为逐条保留原UID/GID，普通复制仍用登记目标身份；移动/复制当前绑定均要求内层回执完整摘要仍匹配原永久回执。修改未覆盖到当前安装态。

## 已执行测试

- 19项回执测试通过，包含9次只针对测试子进程的真实SIGKILL：Preparing、Running、文件发布前/发布后源移除前、内层完成外层未写、目录发布后/部分原件保留、目标丢失、会话不可用。目录部分保留测试实际有两个文件，一个已保留、另一个仍在源，未冒称全量保留。
- 389项容器相关Linux-root测试、35项宿主目录移动、27项目录复制分别全部执行通过；包含混合UID/GID保留、复制保持归一属主及原件摘要漂移。0跳过。日志`/tmp/wd-container-move-intents-{2,3}.log`、`/tmp/wd-container-move-intents-all.log`、`/tmp/wd-move-ownership-{moves,copies}.log`。
- 首轮移动测试预期“发布前崩溃应保留暂存”失败；实际底层按已登记inode清理本操作暂存并Aborted，源未动。修正错误预期为准确清理，未放宽副作用/原件门禁。
- 广泛`test_tree_*.py`首轮276项中275通过、1项仍要求跨设备移动归一到目标UID1000，日志`/tmp/wd-move-ownership-tree.log`保留。该断言随明确的保留源属主语义改为验证目标与原件UID/GID一致（本例原0:0）；之后完整35移动项通过。没有将首轮276宣称全绿。

## 冻结源码实际Docker

`validate_container_move_scope.py <sha> --intents`使用同一严格独占夹具、HDD bind与NVMe命名卷，不改注册表/生产文件/服务。真实三次跨文件系统移动，每文件2,097,159字节，全字节/普通属性/原件inode/永久重放通过；目录根2100:3100、文件2101:3101、空目录2102:3102保留。公开版本为容器ETag，区别于宿主内部版本，原外层回执重复观察字节不变。

证据`deploy/artifacts/container-move-scope-5c4815d42eb349ce816b3815c1b36ac3/`：baseline保存完整非敏感coreBefore与实际不同st_dev；永久外/内层Root状态及manifest、文件系统证明、results/verified/independent-final保留。独立只读复核三组Root状态/actor/原计划、独立域摘要/内层摘要、原件与目标身份、混合所有者，以及准确源/CID/卷缺失。11核心前后ID/镜像/状态/StartedAt完全一致。日志`/tmp/wd-container-move-intents-native.log`。

本批没有真实移动RPC、SQL/Job/Worker/API或可见页面验收，不把模拟Docker身份的Linux测试算成真实容器中断，不把真实容器常规移动算成实机进行中SIGKILL。后续继续固定transport/独立契约/签名、能力门禁、永久意图/迁移/共享屏障、权限审计、Worker/API/Vue和全部59项。
