# 递归 ACL 历史逐项前像真实跨页验收

日期：2026-11-04，dev。安装运行源码仍为 `390889dad8c6cb42a881b5b05f6c70b5fda59c09`，新探针与门禁冻结为 `d15b6988ba31ff707bda767d6e2ba97c634d1e56`，68份验证源码闭包。**真实26项前像的下一页、上一页、再次前进已通过；历史列表超过25份捕获的跨页尚未验收，恢复写入及59项主目标仍未完成。**

## 实现和权限边界

原探针固定4项，不能声称它已证明默认take25下的真实跨页。本批新增仅用于QA的`container-recursive-acl-page-capture`模式：计划必须是access/named、不开额外边界操作，明确要求26项/25文件/1目录；仅接受plan/apply/quit，沿用签名UDS、唯一operationId、固定路径、消息摘要和短时能力。原模式的4项断言保持，不改产品RPC、API/Vue、Agent授权或资源注册。

探针从准确Git archive经固定digest SDK镜像发布，未挂载含私有配置的checkout；自包含.NET10.0.13、实际二进制SHA256及全部发布文件清单核验。程序集来源由实际RPC进程检查；两宿主及已安装helper字节均另与390889d核对。门禁启动前确认源码干净、旧820保护文件及38意图/35执行/73Job与上批最终基线保持，无活动任务/租约/屏障。没有部署、宿主/Worker重启、SQL改账、Cookie注入、响应拦截或改小请求take。

## 一笔真实捕获

只新建一份自有无网络/非root/只读容器根的夹具，固定为根目录加25个普通文件，其中一个中文文件名。每文件65536+7字节，包含不同owner/mode和若干命名ID0/权限0的原ACL，根保留非空default ACL与普通xattr。完整核对文件内容及九字段、目录精确子集、单硬链接/非符号链接、原ACL和普通属性。

实际签名Plan一次、全新operationId Apply一次；没有重放上批捕获。结果Completed、实际变更26项，无Unknown/Aborted/未开始。读取Root聚合、HDD聚合及26项子记录原件，核对原计划、源identity、按UTF8顺序且根目录最后的manifest、每项原access/default ACL、目标mode、原属性摘要、逐项版本和outcome/history digest。保存54份原始Root/HDD证据，再逐轮原字节比对。来源是本批实际签名捕获，**不冒称本批由业务页面登记或Worker执行了一次26项写入**。

## 真实页面、审计和身份

真实管理员可见Authentik登录后，经当前Vue“递归ACL历史”读取唯一新capture。库存实际一页、take25；前像通过真实控件依次请求offset **0 → 25 → 0 → 25**，take始终25，分别返回 **25 → 1 → 25 → 1** 项。前页不含根目录，末页恰为根目录且complete=true/nextOffset=null；返回上一页重新发起实际GET，不用缓存或自造响应代替。

每页严格核对HTTP范围、实例/capture/完整摘要、原模式、原access/default ACL、ID0、权限0及有效权限；Vue当前DOM恰为当前页项数，无跨页残留。四次GET均200/no-store，点击下一页/上一页和关闭按钮在320×600暗色及1440×600亮色下可见，无横向溢出；5份真实PNG保存，其中两个尺寸的历史图已人工查看。

每页返回后，浏览器等待父级重新读取当前物理树、全部Root/HDD原件和旧保护文件，再允许继续翻页。实际PostgreSQL中 **1次List+4次Read共5条Validated审计** 精确对应请求/页进度、摘要/游标hash、offset/take/返回数/complete、管理员subject、container target和唯一id/trace，job_id=null，无明文ACL/路径额外字段。没有抑制数据库保存或以测试提供者替换审计。

管理员与普通用户使用各自原始浏览器上下文，正常页面菜单双端退出。普通用户页面拒绝、历史UI请求0，两种历史GET均403、该主体历史审计0；管理员及普通用户退出后两种GET均401。此前读取中实时撤权、依赖/来源故障等其它矩阵仍分项推进。

## 独立复核、清理和保留

父级运行Result=success、ExecMainCode=1、ExecMainStatus=0。清理前另启独立只读进程，重新读取**仍存在**的实际26项物理树、Root/HDD、HTTP转录和真实PG审计；此时不宣称源已清理。父级最后再次核验完整树后逐个unlink/rmdir，夹具再准确删除原容器和源目录。清理后另启独立只读进程复核原始记录、审计、身份缺席与准确清理，明确`sourceBytesRereadAfterCleanup=false`。两个独立进程均退出0。

旧820份保护文件字节及九字段全部保持，新增54份Root/HDD，总874；目录Root94文件保持。原业务SQL仍38意图/35执行/73Job（本批来源捕获不新建这些业务行），读取审计另实际新增5条。11核心ID/镜像/StartedAt及healthy、两宿主PID/版本、配置和发布账本保持，活动任务/租约/屏障/终端/锁0；user NOPASSWD保留，主开发索引1000:1000/664。历史全部保留，不自动清除新证据或回滚发布。

私有证据不入Git：`deploy/artifacts/recursive-acl-history-paging-a8cfca3267dd47db98c4b92fee5447b7/` 保存实际HTTP/SQL审计、54原始记录、物理/配置保护基线、5PNG、独立前后核验及准确systemd回执。探针构建日志、发布来源和离线日志在`deploy/artifacts/recursive-acl-history-paging-probe-d15b698/`，二进制在`deploy/artifacts/terminal-probe.FxAZfq/`，均有有界私有权限和SHA256清单。

## 离线结果、失败与未验收范围

Release探针构建0警告/0错误；143项既有原探针边界、20项历史证据检查（含9项新跨页）、8范围检查通过，均0跳过，严格Python/Node语法检查通过。首轮9项新检查中，一个方法的2个篡改子例未触发拒绝：测试输入与预期共享同一ACL数组引用，修改时同时改了预期；将返回信号深复制后检查通过。该问题发生在离线测试夹具，无真实依赖或产品修改，记录保留，不改记为首轮全绿。

准确d15b698的CI run `41846325266` 最后核对仍in_progress，尚不称终态成功；本批运行产品继续是上批准确CI通过的390889d，不因QA探针提交自动发布。原文件与HDD配置实际device均2049，不算异盘恢复验收；维护入口此前连续性失败仍待修复，本批不重启服务也不签收全发布窗口连续性。

**后续：** 新建专属超过25份捕获的真实历史列表夹具，验证继续扫描/已读卡片分页；继续逐inode递归历史恢复写入、来源/撤权/依赖与Partial/Unknown故障、可恢复删除及长期清理、维护连续性和其余59项。不增加主任务完整完成数。

[原常规整链及历史证据](CONTAINER_RECURSIVE_ACL_HISTORY_REAL_UI.md) · [当前目标](../GOAL_P0_P5.md)

后续只读补强：`e8d8287`增加捕获前像与初始baseline的严格类型比较、实际已发布探针二进制SHA与发布目录清单的独立核验，以及拒绝替换初始状态的反例。冻结`8d38d0431b58ad95aa3b92db373805c306667d26`后，仅对同一证据执行`--recheck`，Result=success/ExecMainCode=1/ExecMainStatus=0；仍为原1笔捕获/原4次页面读取/原5审计，无新写入、登录或页面重放，不改写原d15b698的两个独立成功回执。补强后历史证据专项21项（新跨页10项）、143原边界和8范围，共172项通过。

实机目录92份、构建/离线目录8份文件均已生成并逐项复核SHA256清单，探针保留原发布清单；三个QA unit均按准确Invocation归档后停止，源及临时身份已精确清理。项目图谱事件`whale-deck.container-acl.d15b698.recursive-history-paging`取得对应project/key/event成功回执、pending移至synced，阶段保持active。
