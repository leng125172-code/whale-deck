# 递归ACL历史读取底层候选

2026-11-04，承接[跨容器保护整链](CONTAINER_RECURSIVE_ACL_CROSS_DIRECTORY.md)。开始推进递归ACL历史恢复的来源读取能力；当前是有完整实现和测试的底层候选，未安装或签收公开RPC、业务页面与实际恢复。

现有递归变更只接受文件和目录各一组统一ACL；历史树中每个inode原ACL可以不同，不能用一次统一ACL覆盖充当恢复。新增独立container-recursive-acl-v1来源读取器，完整验证聚合/清单摘要、原计划及每个child计划、路径/类型/计数/唯一inode/原父子关系与父版本；只读返回经选择摘要绑定的逐项原access/default ACL和模式，绝不直接写ACL。

复用单项历史已验证的只读namespace、HDD attestation、当前pinned ContainerRoot、版本分页和预算；默认单项域/错误码/库存摘要保持，递归域独立。聚合最大16MiB/10000项，读取分页最多50项且实际响应不超过128KiB；必须先验证完整清单再返回一页，不能把未验证的末尾藏在分页后。来源是完整聚合前像，不是仅已完成child历史；Partial未开始项也有前像。

新13项真实隔离Linux ACL专项通过；125相关递归工具、58历史工具（集合有重叠）、8范围检查均通过，root测试零跳过。包括异质ACL/显式ID0、default清除、Partial完整前像、同CID旧实例、无重复分页、预算/超时、损坏/链接/权限/挂载、同长修改及自洽重算摘要仍缺父目录的拒绝；单项历史17回归通过。容器/HDD权威提供器在单测中是明确夹具，不冒充实盘或人类身份。

下一步冻结源码，在新隔离真实容器/HDD验证来源读取、独立进程重开当前ContainerRoot与完整物理保持；仍需专属协议/SDK/受限传输/安装、API/Vue和逐inode恢复计划/确认/当前前像与故障矩阵。不能把读取或文档登记算作恢复功能完成。

## 首次候选门禁失败与只读补核

冻结951801e，Invocation6b2a752167c94c61bddabad76001001c，原件 `deploy/artifacts/container-recursive-acl-history-f5354ebf60de42eba8f189d35308b94d/`。已真实生成access/default/clear三个Completed与一个Partial，实际计数分别4/4/0/0/0、4/2/0/0/0、4/2/0/0/0、1/1/0/3/0；随后工具source_request用dict固定offset/take又通过**extra重复传参而TypeError，原总门禁退出1，不算只读分页通过。

原容器由fixture准确移除，未验证源保留。只读补核使用原冻结native模块直接验证实际HDD的4聚合、16个完整逐项前像及原物理/候选Root不变后，精确清理原树及original.bin；557旧配置/安装态Root/HDD文件及核心/宿主保持。`failed-readonly-supplement.json`明确originalGatePassed=false和publicPinnedReadAccepted=false，不补签原实时分页或UI/业务。

工具修正分页默认值合并，且只允许offset/take覆盖，不允许借机改CID/实例/来源；增加回归。须新资源完整候选及独立进程实盘复验，旧捕获不重做。
