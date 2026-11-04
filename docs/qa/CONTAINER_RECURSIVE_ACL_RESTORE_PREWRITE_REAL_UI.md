# 递归ACL恢复执行中首项写前取消：真实整链

2026-11-05，dev。承接[两批失败与修复](CONTAINER_RECURSIVE_ACL_RESTORE_PREWRITE_CANCEL.md)，保留原失败结论与原件，不回放旧来源或恢复。当前只签收执行中写前取消分项，不提升59项主任务的完整完成率。

## 准确运行与边界

- 产品仍为 `eeb24dd1b9d55062dfb53b25527973817c9722f9`，无产品发布、Agent重启或回滚。
- 门禁冻结 `f219b18a29f2a8f54af59015bf6efca590041f9c`，静态导入闭包100份源码；复用已核验的d15b698自包含探针，仅产生全新26对象签名捕获。
- 唯一unit `whaledeck-qa-restore-prewrite-f219b18.service`，Invocation `4c309b046b734f6e93fb639d144410e5`；最终 `Result=success / ExecMainStatus=0`。准确回执归档后才停止QA unit。
- 私有证据目录 `deploy/artifacts/recursive-acl-restore-prewrite-cancel-e69a0da6d10244c794513bef233f6c93/`；所有测试账号凭据只经内存/隐藏stdin传递，不入源码、命令参数或日志。

120秒有界观察及线程armed首次实机通过。armed只证明观察线程完成范围检查，不是产品写入栅栏；本次21.561秒观察到Preparing，24.747秒观察到Applying并发出SIGSTOP。暂停后Root仍Applying、startedThrough/finishedThrough均-1、首子项缺席；完整26对象仍与来源捕获后实体完全一致，当前聚合HDD前像已验证。原25秒案例的失败保持，不能因本次恰在25秒附近取得边界而倒推其他运行时序或保证未来永不漏窗。

精确绑定root UID、原transport argv、容器根FD与原操作锁FD，pidfd存活/启动时间、system.slice cgroup、MainPID、Invocation及进程单一INVOCATION_ID前后验证；未向任意PID发信号，未使用QA SIGKILL。当前来源/frame、manifest逐项恢复目标和聚合前像与Root摘要绑定后才放行页面动作。

## 真实页面、产品取消及原件

全新无网络/非root/只读根测试容器内25文件加根目录；实际文件全字节、九字段、异构access/default ACL、ID0及普通xattr先核验。真实管理员正常Authentik登录→Vue历史选择→Ready预检/原引用重载/前25项恢复预览；准确停原Worker后真实确认202→Pending，再启动同ID同镜像Worker，由观察器取得上述边界。

实际页面查询到Applying后，先打开取消框选“继续等待”，取消POST仍0；再一次明确“确认请求取消”，取消POST1。产品在5.138732798979618秒内终止原pidfd对应helper，QA没有强杀helper。SQL最终Canceled、Root Aborted；完成0/变更0/Aborted1/NotStarted25/Unknown0。当前聚合HDD1份保持暂停时原字节，无子项HDD；首项在取消结算时被记Aborted，没有ACL写入。所有实体与捕获后全树完全相同，不能把本次签成26项恢复完成。

SQL原意图/双Job/原owner与登录绑定、确定性执行ID、MAC字段、原五计数和5条精确审计由专项证明核对。登记/确认/取消各一次、上传0，原执行不重做。原上下文正常用户菜单BFF/Authentik双端退出、原引用清除，6个匿名GET均401；全新普通用户正常登录，私有UI请求0、6个GET及2个POST均403，无私有读取/恢复审计，再正常双端退出且6GET401。不存在注入cookie、模拟API、路由拦截或时钟修改。

5个真实320×600暗色PNG已保存；人工查看了进行中和终态。截图保留预览滚动后的底部操作与部分摘要，并非单帧囊括全部内容；Applying状态由真实DOM断言、API和父级SQL/Root交叉验证。底部仍展示持久ACCEPTED结果码的登记提示，不据此将任务误判Pending，后续可整理登记提示与当前状态的表述。

## 独立核验与清理

主门禁完成后，独立进程先重读实际完整26对象与全部原件，再精确清理自有文件/目录和容器；随后另一独立只读进程重开保留原件，SQL与终态、原件及旧保护保持。清理后不宣称再次读到已删除源字节。临时身份缺席，未清除永久审计或HDD历史。

- 旧1790保护文件字节/九字段保持，总1844；来源Root1462、恢复Root188/HDD130、目录Root94。
- 普通业务保持54意图/49执行/103Job；恢复累计11意图/8执行/活动0。
- 11核心容器healthy，ID/镜像保持；仅原Worker按门禁重启且等healthy，Agent1379812/维护1379807和eeb24dd保持。其他活动Job、终端、宿主锁及上传/目录保护均0。
- 源/HDD同device2049仍保留为部署限制，不称NVMe/HDD物理分离已完成。旧维护连续性失败也未补签。

本批155份文件SHA256逐项验证，清单摘要 `ba839631ae32a615e705d636ee24d20dc4e7fd934094dd783e027b43b58acb0b`。原两批248份与243份封存逐项保持，没有向旧封存目录写入。75项工具/范围测试本轮通过、零跳过；准确f219b18 CI41926062954查询时仍in_progress，不称全绿。

## 继续范围

本次只签收上述新资源的执行中首项写前取消、终态后原上下文退出与权限检查。执行中正常退出、实时撤权/停用、Worker/Agent重启、Partial/Unknown、实例变化、跨写入与来源异常组合不能借用旧普通ACL矩阵替代；可恢复删除、长期清理、稳定维护入口及其余59项继续。17项移出不算完成，完整可部署master尚未交付。


收尾：更新MD后8项范围再通过，变更MD的736个本地链接缺失0，diff检查通过，索引1000:1000/664。项目图谱事件whale-deck.container-acl.f219b18.restore-prewrite-real-ui已取得project/key/event匹配、duplicate=false成功回执，pending移入synced，阶段保持active。
