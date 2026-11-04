# 递归ACL执行中写前取消与正常退出实机验收

2026-11-04。承接[引用保护发布及预检取消](CONTAINER_RECURSIVE_ACL_AUTH_RELEASE.md)。**本批新增两项真实故障分项通过：原任务已确认执行、产品已验证聚合HDD清单、首项尚未开始时的显式取消与原登录上下文正常双端退出。** 不重复七类既有写入或预检取消，不代表完整递归ACL故障矩阵、递归历史恢复或删除已经完成。

## 冻结工具与实际切点

产品仍为已部署的 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53，没有新发布、迁移、宿主重启或自动回滚。工具冻结 `54ee000ff1c8276ee7a351f06e028bb3bf068abc`，新主控、浏览器、独立核验器及整个静态Python/MJS导入闭包在运行前逐文件验证。实际安装helper原字节与产品SHA核对通过。

工具只建立两套新的非root/无网络/只读根容器和自有四对象树，两位临时管理员的凭据仅经stdin交给浏览器。实际Authentik登录后，由Vue控件填写独立文件/目录access目标、生成预检，暂时停止原Worker，真实确认返回202/Pending，再启动同ID/同镜像Worker。没有SQL、Root回执、测试时钟或产品checkpoint写入。

观察器以原SQL计划、精确自有bind路径、容器实例、固定transport命令/root UID、systemd cgroup、容器root FD及操作锁FD共同绑定pidfd。仅在聚合Root为Applying、HDD已验证、finishedThrough=-1且startedThrough为-1或0时尝试SIGSTOP；停止后再次核对整个树的字节、九字段、两类ACL及普通属性。已完成项或实际发生ACL修改都不满足本门禁。工具不向helper发送SIGKILL；取消/退出后的helper终止必须由产品完成，错过切点记失败，不自动重放操作。

本批两个实际切点均为startedThrough=-1、finishedThrough=-1，尚无child回执；不能以工具也允许0这一点宣称“首项已经登记但未写”的独立场景已测。

## 两个真实结果

| 分项 | 真实页面及动作 | 产品结束绑定helper | 永久结果 | 实际五计数 |
| --- | --- | ---: | --- | --- |
| 显式取消 | 320×600暗色；进行中页面先“继续等待”（取消POST为0），再明确确认取消 | 4.925秒 | SQL/执行Job Canceled，Root Aborted | 0完成 / 0修改 / 0中止项 / 4未开始 / 0待证明 |
| 正常退出 | 1440×600亮色；原用户菜单正常双端退出，未撤组或停用账号 | 5.022秒 | SQL/执行Job Canceled，Root Aborted；cancel时间为空 | 同上 |

每项实际预检/确认各1次；取消POST分别1次/0次，内容上传0次。取消场景重新查询原任务并显示“已取消”、读取原回执200/no-store后正常退出；退出场景从原认证上下文退出，并确认opaque引用清空。两个上下文均观察退出POST 302/303、provider end-session导航、BFF 401、身份端401/403及最终登录页，随后原任务匿名GET为401。两身份只在产品终止和终态核对完成后删除，未用删除身份来触发退出结果。

各套树两文件完整内容、inode/模式/UID/GID/nlink/size/mtime/ctime、两类ACL和普通属性全部保持，HDD保留原完整四对象清单。两份新聚合历史及Root留存，child历史为0；不是“已确认执行但完全没有备份”的预检取消。parent逐套证明后才精确清理自有文件/容器，未验证数据不会递归删除。

独立只读核验器再次读取永久SQL/Job/outbox计数、Root及受信HDD，逐项比对冻结源码、原件摘要、实际计数、双端退出回执和三份真实PNG，确认两个临时身份及两套源/容器均已不存在。清理后核验的是父过程保存的物理快照，不声称重新读取已删除源字节。截图已查看：取消场景进行中及终态状态、固定底部操作区在320px下可见；进行中截图保留当时查询尚忙的控件外观，实际取消点击在控件可用后完成。

## 运行与保存证据

唯一unit：`whaledeck-recursive-acl-faults-54ee000.service`；Invocation `72018fbf83e045388903440cf8f183e5`。14:07:33–14:09:42 CST运行约128.7秒，进程输出两项passed，systemd同Invocation管理日志记录Deactivated successfully。此Type=exec临时unit成功后已被systemd回收；后续show的空Invocation/时间和默认ExecMainCode不冒充实际原退出元数据。原始启动回执、Invocation进程日志及完整manager JSONL已分别保留，无须为补元数据重跑已通过操作。后续此类门禁可用RemainAfterExit保留退出状态，归档后再精确停止。

私有证据：`deploy/artifacts/container-recursive-acl-faults-73d44523c836431b963753a11fbdce9b/`。包含scope冻结闭包、两套baseline、原登录/确认/动作信号、cut SQL/Root/物理与FD绑定证明、最终SQL/Root/HDD/物理、退出与浏览器回执、verified、独立核验及unit管理日志。凭据、Cookie和token不写入证据；该目录不进入Git。

89项相关离线探针边界检查、8项范围检查、Node语法与diff空白检查通过。现有CI的`test_recursive_*_probe_scope.py`自动发现新6项。以上是验收工具和真实场景证据，不把此前产品单元测试或测试接口浏览器报告成这轮重新运行的实机全量；工具新CI终态另行核查。

更新进度元数据后的首次范围复查出现1失败：nextStep重写时漏掉固定的“17项移出不算完成”声明。已补回明确范围声明，不删除或放宽该测试；修正后8项范围复查全部通过。本批真实操作和独立证明不因这次文案检查而重放。

## 保持项与未验边界

旧480个文件全部保持字节SHA及九字段：7个配置/权限文件、部署账本、145普通ACL Root、76递归Root、153普通HDD、98递归HDD。新增后递归Root为78、递归HDD为100，普通记录数不变。当前生产共12意图：8 Succeeded、3 Canceled、1无执行Expired，执行10个、相关Job22个、活动0；本批正好新增2规划/2执行和2聚合HDD，没有清理永久业务历史。

两个宿主PID及实际来源保持Agent3598741/维护3598736和b880208，11核心容器均healthy，只有同Worker的StartedAt因停启改变。user NOPASSWD:ALL按授权保留，产品Agent授权与部署配置不扩展。新树/HDD实际device相同，本批不签物理异盘或冷启动；也没有覆盖维护入口瞬时零中断，既有失败记录继续有效。

下一步仍包括：已修改部分后的取消/正常退出、实时撤权、Worker/Agent重启与自然租约、Partial/Unknown及跨写入保护，路径/磁盘/依赖等其余故障；递归ACL历史恢复、可恢复删除和长期清理，以及其余59项主任务。旧常规批次总门禁失败和原引用丢失因果未证的边界原样保留，不因这两项分项签收提升59项完整完成率。
