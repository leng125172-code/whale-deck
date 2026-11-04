# 递归ACL历史读取底层候选与实盘验证

2026-11-04，承接[跨容器保护整链](CONTAINER_RECURSIVE_ACL_CROSS_DIRECTORY.md)。开始推进递归ACL历史恢复的来源读取能力；当前底层候选已完成实现、测试及真实容器/HDD验证；未安装或签收公开RPC、业务页面与实际恢复。

现有递归变更只接受文件和目录各一组统一ACL；历史树中每个inode原ACL可以不同，不能用一次统一ACL覆盖充当恢复。新增独立container-recursive-acl-v1来源读取器，完整验证聚合/清单摘要、原计划及每个child计划、路径/类型/计数/唯一inode/原父子关系与父版本；只读返回经选择摘要绑定的逐项原access/default ACL和模式，绝不直接写ACL。

复用单项历史已验证的只读namespace、HDD attestation、当前pinned ContainerRoot、版本分页和预算；默认单项域/错误码/库存摘要保持，递归域独立。聚合最大16MiB/10000项，读取分页最多50项且实际响应不超过128KiB；必须先验证完整清单再返回一页，不能把未验证的末尾藏在分页后。来源是完整聚合前像，不是仅已完成child历史；Partial未开始项也有前像。

新13项真实隔离Linux ACL专项通过；125相关递归工具、58历史工具（集合有重叠）、8范围检查均通过，root测试零跳过。包括异质ACL/显式ID0、default清除、Partial完整前像、同CID旧实例、无重复分页、预算/超时、损坏/链接/权限/挂载、同长修改及自洽重算摘要仍缺父目录的拒绝；单项历史17回归通过。容器/HDD权威提供器在单测中是明确夹具，不冒充实盘或人类身份。

初始候选之后已冻结源码并执行下述真实验证；仍需专属协议/SDK/受限传输/安装、API/Vue和逐inode恢复计划/确认/当前前像与故障矩阵。不能把读取或文档登记算作恢复功能完成。

## 首次候选门禁失败与只读补核

冻结951801e，Invocation6b2a752167c94c61bddabad76001001c，原件 `deploy/artifacts/container-recursive-acl-history-f5354ebf60de42eba8f189d35308b94d/`。已真实生成access/default/clear三个Completed与一个Partial，实际计数分别4/4/0/0/0、4/2/0/0/0、4/2/0/0/0、1/1/0/3/0；随后工具source_request用dict固定offset/take又通过**extra重复传参而TypeError，原总门禁退出1，不算只读分页通过。

原容器由fixture准确移除，未验证源保留。只读补核使用原冻结native模块直接验证实际HDD的4聚合、16个完整逐项前像及原物理/候选Root不变后，精确清理原树及original.bin；557旧配置/安装态Root/HDD文件及核心/宿主保持。`failed-readonly-supplement.json`明确originalGatePassed=false和publicPinnedReadAccepted=false，不补签原实时分页或UI/业务。

工具修正分页默认值合并，且只允许offset/take覆盖，不允许借机改CID/实例/来源；增加回归。须新资源完整候选及独立进程实盘复验，旧捕获不重做。

## 新资源完整候选通过与实际边界

修正冻结 `1e974b018c58d9c44bb9f1b4d93004542b0aa2c7`。复验启动曾误传HEAD_REPLACE，被SHA格式校验在preflight、证据创建及实盘动作前拒绝；Invocationdd29eac007ff4e5e85d367c1a148db90，18:49:14 CST退出1，零新捕获，原日志/回执单独保留。使用准确SHA的新资源完整门禁通过：`deploy/artifacts/container-recursive-acl-history-1567afce8622441b863bb8c685a36d0c/`，unit `wd-qa-recursive-acl-history-kernel-final-1e974b0.service`，Invocation `baf62ff1f6aa4f8484331361bc352153`，18:50:07–18:50:35 CST准确退出0。均在停止对应已结束QA unit前保存准确MainPID/Invocation/Result/ExecMainCode和Status，原失败不补签。

14份冻结native模块及验证依赖在操作前核对，所有旧安装模块与运行版本逐字核验；新递归历史模块没有安装。使用真实publisher、container backups、FileBackups、ACL/递归引擎及当前真实ContainerRoot，源为新的1000:1000/无网络/只读根隔离容器。文件具有不同原ACL（含显式数字UID0和2202）、目录具有原default ACL、不同inode属主/模式，文件完整字节和普通属性真实核对；新读取器不使用模拟HDD。

真实生成4份聚合：access/default/clear三个Completed，以及仅当前候选自有fork进程在AfterEntry0被SIGKILL后观察为Partial的一个。每批实际计数分别4/4/0/0/0、4/2/0/0/0、4/2/0/0/0、1/1/0/3/0；没有杀Agent、Worker或真实业务进程，也没有给安装态添加故障钩子。保留4聚合+13子项HDD元数据及候选Root日志；Partial清单完整包含尚未执行的3项前像。

实际目录读取5页：4页各返回1个本容器capture，末页完成剩余扫描；共扫描49份完整聚合、invalidCount均0，返回本CID/path的4份，未返回其他容器历史。逐项读取按固定完整historyDigest绑定，共16页/16项，源access/default ACL与模式都等于捕获前完整物理清单，未把统一新目标或仅已完成子项当作历史。读期间ACL写入/日志写入函数被禁止，完整源字节/九字段/ACL/普通属性、候选Root日志及安装态/HDD快照前后保持。

独立进程重新加载冻结模块，实际重新打开同容器当前pinned根和HDD，验证全部分页结果、完整HDD摘要、逐项前像及实际源字节；`independent.json`明确candidateOnly=true、sourceBytesAndMetadataReadBeforeCleanup=true、readerWrites=false、installedRpcOrBusinessOrUiAccepted=false。独立核验在准确清理前执行，之后只精确移除已证明的树和原文件/容器，原HDD与候选日志保留，不声称删除后仍重读源。

最新13个核心历史专项加1个分页工具回归通过；126相关递归工具、59历史工具（有重叠）root全部通过/零跳过、8范围与diff检查通过，单项历史17回归保持。单测的ContainerRoot/设备权威是显式夹具；真实容器/HDD分项另有上述实际证据，均不能替代Authentik/RPC/UI恢复整链。

## 运行状态与后续

运行产品六组件仍b880208/Agent1.53，没有发布/安装/迁移、宿主服务/Worker重启、Agent授权扩大或自动回滚。Agent4044173/维护3598736与来源保持，11核心healthy且容器身份/StartedAt保持，永久递归ACL仍30意图、28执行/58Job，活动0；本批native捕获不冒充永久业务任务。开发user NOPASSWD:ALL保持，凭据不入仓库或日志。

首批557个旧配置/安装态Root/HDD文件保持，复验前574个保持；两套独立native捕获共新增8聚合+26子项HDD，当前普通Root145/递归安装态Root117/普通HDD153/递归HDD168/配置账本8，共591个保护文件。新的候选Root在各证据私有目录，不当作安装态Root。源与HDD仍同设备2049，不签物理跨盘或完整NVMe/HDD布局。首次push TLS失败后有界重试成功，前批8666da0 CI41812644148完成success；本批1e974b0 CI41816829582最后查询in_progress，未报成功终态。

下一批专属历史协议/SDK/受限传输/能力协商与安装态读取，再业务API/Vue及逐inode历史恢复计划。真正恢复必须为每个当前inode生成自己的目标ACL，重新绑定当前实例/路径集合/版本/属性，计划和执行前重读来源，先保存当前ACL前像，明确确认后执行，Partial/Unknown不续做或自动回滚。递归读取、安装、完整恢复、其余故障/删除/长期清理及59项仍未全部完成，17项移出不计完成，不提高整项完成率。

项目图谱事件 `whale-deck.container-acl.1e974b0.recursive-history-kernel` 已同步active，准确回执保存；候选不记完整功能或目标完成。
