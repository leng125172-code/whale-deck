# 容器文件回收协议、SDK与受限传输候选

后续：准确3725aa7的CI已12路全部成功，并有[冻结原生探针/独立证明及旧版本拒绝记录](CONTAINER_FILE_RECYCLING_PROBE.md)。以下“未取得CI终态”保留为本批发生时的历史状态；安装态签名文件RPC与业务页面仍未签收。

2026-11-05，承接[内核候选及分项实盘](CONTAINER_FILE_RECYCLING_KERNEL.md)。源码冻结`3725aa7`，已实现Agent协议1.56候选、SDK、请求绑定、Root传输及安装清单；运行中的Agent/应用仍为`34153d2`，回收传输尚未安装。原内核实盘不补签为本批签名RPC验收，业务消费者尚未实现。

## 独立协议与授权

新增`ContainerRecycleService`的Plan/Apply/Inspect/Discard/Restore五个强类型Unary RPC和`docker.files.recycle`能力。请求摘要按消息完整类型名及字节编码独立分域，短期单次HMAC再绑定方法、摘要、到期时间和nonce；通用及其他文件能力不能替代。Plan只接受规划上下文；写入和查询携带原Root operation/owner/完整计划及调用者/trace/job/idempotency上下文，禁止内嵌旧capability。

Restore引用原回收operation，但要求不同的新job ID；Inspect/Apply/Discard的job ID匹配原operation。这样不把恢复动作冒充原删除作业，未来永久业务仍需自行授权当前主体对原记录的恢复权限。SDK需协议major=1、minor>=56且明确能力存在，旧Agent或缺能力在发送计划前拒绝。

完整计划包含9字段源identity（含ctime纳秒）、父目录/版本、执行UID/GID、namespace目标证明、内容摘要及最多32项/4096字节普通user属性。仅普通单链接、无setid文件，数据最多1GiB；属性按UTF8字节序校验以匹配Python Unicode排序，二进制值完整保留。Root JSON字段精确限定，拒绝额外字段、替换命令及非规范Base64。Plan/operation DTO的ToString不输出计划和属性内容。

回执区分Trashed/Restored/Aborted/Unknown，原件留存/恢复使用optional bool，Unknown必须缺失/null，不能用false代替未证明。历史可能存在、已验证、摘要、当前HDD可用及当前绑定分别校验；只有专属NOT_FOUND映射为空记录。目标消失、HDD/socket故障或Root丢失但HDD仍有原件，不能推导没有发生操作。

## Root传输与取消边界

固定`container-file-recycling`动作及单JSON私有stdin，模块、状态目录和systemd属性均固定。130秒服务上限、120秒runner、110秒动作/10秒输入上限，有界内存/任务/FD/文件大小、AF_UNIX、PrivateDevices、无自动重启；只开放指定状态/应用/HDD写路径，CapabilityBoundingSet没有新增SYS_ADMIN/CHOWN。stop必须同时匹配随机unit ID和准确Invocation。安装器创建root:root/0700状态目录、安装0750模块，validator校验权限、非符号链接和单链接；本批只改源码与清单，没有执行安装。

Recycle专属Ready顺序为：先收到并绑定Root Invocation，再发送请求正文并EOF。Ready前取消/非法头不会释放一个已含操作内容的EOF；Ready后取消使用已有精确Invocation stop。内存流单元验证输入先后顺序，尚未验证安装态helper取消。旧传输保持其既有输入顺序。

内核新增Discard：未开始且没有历史原件时写Aborted围栏，晚到Apply只读该围栏；有孤儿历史则保留Unknown。Inspect发现Root缺失但HDD有原件也登记Unknown，保留未证明源事实；仅明确目标消失/实例变化等才允许无namespace观察旧记录，普通Docker/HDD/registry故障原样失败。没有引入重新移动或重新复制旧操作。

## 已验证结果与失败记录

候选回归通过：5149项后端Unit零跳过（含74项新回收policy/capability/SDK/Ready专项）、518项root容器文件工具零跳过（含8项新传输测试），以及141项安装/维护检查。恢复执行后本轮74专项再次通过，原集成项目构建完成回执为0警告/0错误；3个Python AST及逐文件3个shell语法检查、diff通过。不同集合不叠加为额外覆盖。

签名/SDK使用真实HMAC与Protobuf/HTTP2编码，但HTTP响应handler、namespace及HDD attestation是显式夹具；Python执行真实临时inode I/O。覆盖字段篡改/跨域/跨方法/过期/nonce重用、旧版本/缺能力、恢复新job、错误回执与可空事实、未知动作、命令参数/stop身份、取消围栏、目标消失后原件观察、孤儿历史与普通故障拒绝。它们不是安装态RPC、实机传输取消、真实业务或UI证据。

生成式替换曾把PermissionDenied错写成RecycleDenied，导致CS0117；已修正。安装validator一度产生嵌套for生成错误，复核后改为完整单循环并逐文件检查，未执行坏安装器。SDK夹具路径不一致导致5失败，修正夹具后保留严格命令比对。首次全量回归有1个旧精确minor=55断言失败，更新当前服务器预期为56，旧SDK最低版本拒绝测试保留；最终完整回归通过。这些失败说明来自持久工具回执，未重建不存在的原始stderr日志。

## 来源、生产状态和待验范围

私有目录`container-recycle-rpc-candidate-52502a3d0f2a4a35a1019c3ec0534317`封存42文件，清单摘要`13a778236b0946b3963fff5e39b76f5b70ba3dac427966d315717b921a09738b`。包括36份变更源码与hash、当前74专项完成工具回执、回归结果来源说明、失败边界和生产只读核对；完整Unit/Python原始日志未捕获，结果以持久工具回执为据，不伪造完整日志。

2346保护文件、来源Root1964、恢复Root257/HDD169、11核心与两宿主进程、恢复24意图/19执行/活动0和普通62/56/118，与上批只读基线一致；新Root传输实际缺席。没有部署、扩大正在运行的Agent权限或执行新的回收写入。

下一步补专属原生探针和独立签名/回执证明，等待准确CI后标准安装并验证真实RPC/HDD及取消/兼容性。再接永久意图/租约/共享围栏、Application workflow、API、Worker与Vue；当前接口仅DI注册，尚无业务消费者。最新内核执行者加固的实机拒绝、ACL/目录/HDD重建/覆盖恢复/清理及其余59项仍待，完整完成率不变。

收尾：准确3725aa7已推送dev，范围8项、2110本地Markdown链接/缺失0、diff与42文件封存复核通过，两Git索引仍归用户所有。项目图谱事件whale-deck.container-recycle.3725aa7.rpc-sdk-transport-candidate取得projectId/key/eventId匹配、duplicate=false回执，pending移至synced，阶段active。未取得准确候选整条CI终态，不报CI全成功。
