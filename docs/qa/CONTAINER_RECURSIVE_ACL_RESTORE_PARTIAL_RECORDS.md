# 部分恢复原件读取与首项证明候选

2026-11-05，承接[故障摘要观察候选](CONTAINER_RECURSIVE_ACL_RESTORE_FAULT_JOB_OBSERVATION.md)。现有完整恢复原件验证器只接受Completed/Aborted，不能拿成功验证器替Partial签收。本批新增只读Partial前缀读取和首项证明，为安装态故障门禁提供前置能力；不修改产品、不安装部署、不重放旧任务。

## 精确范围

只接受Root状态Partial、startedThrough=finishedThrough=0、第一子项Completed且实际改变，五计数严格为1/1/0/(n-1)/0。计划对象数支持2–10000，但本批实盘用4对象；不据此签26对象实机、不接受第二子项已启动、Aborted第二子项、Unknown或任意其他Partial形态。缺失、额外或不同绑定的原件均失败，不能默认为未开始。

读取已存在的私有Root/HDD：目录和归档路径须为可信root路径，拒绝符号链接与非私有记录；HDD通过原存储目标见证FD及O_NOFOLLOW打开，逐项JSON集合仅允许首项。原始字节归档使用不可覆盖语义，重开必须字节相同；没有新建生产命名空间、继续恢复、取消、ACK或helper信号。

证明逐项绑定原请求、来源完整清单和帧、本次计划/聚合前像、首项原/目标ACL及普通权限、非目标ACL、身份/版本、属性与其余完整树。第一子项必须有真实Completed身份和逐项HDD前像；验证resourceType/storageId、父操作ID、清单和历史摘要。根据首项Completed及其余NotStarted重建全部outcomeDigest，再对照原Root和回执计数、绑定/历史事实。第一项实际ctime要求严格递增，这是此专项的保守条件；较粗时间戳或特殊时钟环境不能据本测试签通过。

## 实际测试与边界

8项新增root测试和关联共106项工具测试通过，零跳过；2个Python AST、CI YAML、diff检查通过。测试在隔离临时Linux文件系统真实写入ACL、生成来源/恢复Root和HDD格式的实际字节，并在首项AfterEntry检查点注入取消；原调用被拒后读取真实Partial，五计数1/1/0/3/0、首项改变、其他3项保持。再次打开FD/归档验证原件保持，不重放Apply。还验证伪造Completed/零修改/Unknown计数、第二项启动标记、身份/HDD替换、其他对象变化、额外子原件、归档符号链接与非私有Root权限的拒绝。

容器会话、存储配置及故障检查点是显式测试夹具；“HDD”在这些测试中是隔离目录里的格式原件，不是生产HDD物理部署或故障注入。FD重开在测试进程内完成，未冒称独立进程验收。只读协作复核未发现确定漏验，指出ctime严格条件；最终实现和测试由主代理复核。本批没有真实Authentik、签名RPC、生产SQL/Job、独立管理员处置或真实页面证据，不提升整项或阶段完成率。

下一步接入全新自有容器、准确pidfd/双FD/Invocation的首项完成边界、正常取消、Partial/处置Jobs与SQL、原件和完整文件核验、前后独立进程及准确清理。第二子项已启动的Partial仍需独立支持；不能为提高门禁成功率放宽此证明。Unknown、其余故障矩阵、可恢复删除/长期清理、维护连续性及59项继续；旧维护失败及源/HDD同device限制保留。
