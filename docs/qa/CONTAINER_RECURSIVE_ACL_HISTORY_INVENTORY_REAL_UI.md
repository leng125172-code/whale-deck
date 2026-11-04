# 递归 ACL 历史多捕获列表真实分页验收

日期：2026-11-04，dev。产品运行源码仍为`390889dad8c6cb42a881b5b05f6c70b5fda59c09`，已发布探针来自`d15b6988ba31ff707bda767d6e2ba97c634d1e56`，成功门禁冻结`ba4c32636d67e0395d89925f535d20056c9bb95a`/71份验证源码。**超过25份捕获的真实库存继续扫描、已读列表25/1前后翻页和选源读取已通过；保留首轮失败，不表示递归恢复写入或59项主目标完成。**

## 新捕获与权限

复用已核验SHA256及程序集来源的.NET10.0.13自包含签名UDS探针，无新SDK下载、产品安装或服务重启。原4项模式仍受原协议/签名/短时能力/固定资源保护，26次Plan+26次Apply+quit共53动作，未超64动作预算；不扩大Agent授权。

独立自有无网络/非root/只读容器根夹具仅4个对象：根、普通文件、子目录、中文文件。每文件65536+7字节，固定普通xattr与非空目录default ACL，保留owner/inode/mtime等九字段。26笔均重新从实时树生成计划、使用全新operationId，access named/mask交替，首笔4项变更、随后每笔2文件变更/目录noChange。每笔都核验实际完整内容、ACL/非目标default/普通属性、签名计划、Root/HDD聚合与4项子记录、manifest/history/outcome摘要和逐项版本，再允许下一笔。没有重放旧ID、修改回执、SQL改账或构造假HDD记录。

共用捕获核验器增加显式inventory-named/inventory-mask分支，严格限定4项/2文件/2目录；原26对象前像模式保持原约束。新分支通过前，先从冻结535c4ed只读重验上批26对象已封存证据及874保护文件，未重复其原生写入或页面操作。

## 首轮失败、恢复与修正

首轮535c4ed已完成26笔真实捕获及全部前像证明，随后页面首屏检查失败：门禁错误要求take25必返回25条，而已安装服务允许在软时间预算到达时提前返回。实际首次GET为200，**返回22条、扫描79份、invalid0、complete=false**，产生1条实际PG列表审计；原截图和失败回执保留。该整轮UI门禁保持失败，无正常双端退出或完整列表签收，不创建补签verified.json。

既有夹具finally准确移除其容器，并保留尚未核准删除的源目录。由于历史绑定原containerId，已删除的容器不能由另一容器冒用，也没有修改旧元数据绑定。ba4c326恢复工具只读重验原26聚合/104子项Root/HDD与实际4项树、原夹具original.bin完整字节和父目录inode，再逐项清理精确自有源；Result=success/ExecMainStatus=0，旧历史全部保留，原UI状态仍失败，没有重放捕获或页面请求。

修正门禁接受0–25条的非末页，只要游标实际前进、版本固定、未跳过本容器capture；继续真实点击“继续扫描”直至完整，最多100次。新增22+4及空匹配进展页/三网络页反例，不强行改小take、延长产品预算或将空接口算成功。scannedCount扫描的是整个namespace，包含其它容器记录，不能等同当前返回数。

## 成功真实页面与PG审计

修正后在**另一全新独立夹具**完成26笔新捕获。真实Authentik管理员可见登录后打开Vue历史，实际网络页为 **18条+8条**，两次GET均200/no-store，take25，第二次精确沿用首个nextAfter与inventoryVersion。累计扫描123份库存、invalid0，完整capture集合与本批26份实际HDD原件一一对应；没有把网络页补成虚构的25+1。

读完26条后，Vue自动切到已读列表末页1张卡，再通过“上一页历史”显示25张卡、“下一页已读历史”回到1张卡。两次本地换页不增加HTTP请求或审计；逐张核对capture顺序、访问ACL类型、完整原计划/变更计数，每屏最多25张卡。选中末条记录再实际GET一份4项前像，原模式、两ACL、有效权限与其HDD原始数据一致，属于单份快照读取；[上批26对象前像跨页](CONTAINER_RECURSIVE_ACL_HISTORY_PAGING_REAL_UI.md)继续单列。

父级在首屏、扫描完成、本地前后页、选源后分别重验物理树、全部26条捕获链、1134份读前保护文件与原始Root/HDD字节。实际PostgreSQL共 **2次List+1次Read=3条Validated审计**，精确核对subject、container target、唯一id/trace、job_id=null、游标/捕获/路径/实例摘要、take/offset及扫描/返回/complete，未抑制EF保存。

管理员和普通用户在各自原上下文可见登录并正常页面菜单完成BFF/Authentik双端退出。普通用户私有页面拒绝/历史UI请求0、两个历史GET均403且历史审计0，管理员与普通用户退出后两个GET均401。没有Cookie/响应注入，凭据仅经私有stdin，不进源码或日志。

## 独立复核、保留与检查

成功父级及清理前/后独立只读进程均退出0。清理前独立进程重新打开实际4项物理树、26份聚合及104份子项Root/HDD，逐项核对52次Plan/Apply响应、HTTP/DOM信号、PG审计及配置保护；父级最终核验后才准确清理自有源。清理后只重验保留原件、审计和身份/容器/源缺席，明确不声称重读已删除源字节。6份真实PNG保存，320×600暗色及1440×600亮色历史列表已人工查看，关闭/继续/前后页按钮可见，无横向溢出。

原874保护文件及首轮260份新记录均保留，成功批再增加260份Root/HDD记录，最终**1394份**。成功批读前1134份字节及九字段全部保持，目录Root94保持；业务SQL仍38意图/35执行/73Job，native捕获不冒称本批Worker/业务登记，历史读取审计另实际保存。11核心ID/镜像/StartedAt及healthy、两宿主PID/390889d、配置/账本/user NOPASSWD保持，活动任务/租约/屏障/终端/锁0；主开发索引1000:1000/664。

私有证据不入Git，均已生成并逐项核验SHA256清单：首轮`deploy/artifacts/recursive-acl-history-inventory-f4d5c1d68c7c4ef39cd370f4c5749a29/`276份；成功批`deploy/artifacts/recursive-acl-history-inventory-acaca95726b44d7db3c1a6d71a649295/`300份；回归/离线/推送诊断`deploy/artifacts/recursive-acl-inventory-checks-535c4ed/`4份。首轮失败、恢复及成功三个QA unit均按准确Invocation保存回执后停止，两份源均精确清理，全部历史保留。

最终143原边界+21既有历史证据+12库存证据+8范围=**184项，0跳过**，严格Python/Node语法及diff检查通过。首次推送出现GnuTLS握手中断，535c4ed重试及ba4c326同步均成功，未禁用TLS。准确ba4c326 CI run`41853450456`最后核对仍in_progress，不冒称终态成功；本批没有发布产品。

源与配置HDD的实际device均2049，不算异盘恢复验收。维护入口此前连续性失败仍待处理，本批不以稳定运行替代完整发布窗口签收。下一批推进逐inode异构ACL历史恢复写入、来源/目标版本绑定与当前前像先留存、Partial/Unknown/撤权等失败恢复、可恢复删除/长期清理及其余59项，不增加主任务完整完成数。

[常规历史整链](CONTAINER_RECURSIVE_ACL_HISTORY_REAL_UI.md) · [当前目标](../GOAL_P0_P5.md)

项目图谱事件`whale-deck.container-acl.ba4c326.recursive-history-inventory`取得对应project/key/event成功回执，pending已移至synced，阶段保持active。递归恢复写入尚未实现；后续需核实异构源及旧schema的完整还原边界，不从旧捕获中补造未记录的属性。
