# 独立恢复：原件已归还后的真实页面取消

2026-11-06后续：另一个全新来源的写后Worker强杀/自然租约已实机通过，取消请求为0且同执行保留Succeeded/Completed，见[Worker记录](CONTAINER_FILE_RECYCLING_RESTORE_POSTRETURN_WORKER.md)。以下保留晚到取消的原始边界，不混用触发或补签其余故障。

2026-11-06，修订工具 `52b7e0cbc185db1a2765bd8ed4e58fc60da4eb98` 在保持3d80d17的实际部署上，以全新来源完成“原inode已返回、本恢复最终journal尚未落盘”切点的页面取消。最终保留Succeeded/Completed及本操作归属，而不是把晚到取消写成Canceled/Aborted。

正式门禁 `whaledeck-qa-restore-postreturn-cancel-fixed`，Invocation `b6f034ab2c264708b335c7da5a8eac52`，exited/0；清理前后两个独立进程均passed。4项新专项结合回归共120项Linux root通过、零跳过，8范围、Python/Node语法通过。仅签此写后取消组合，完整写后/Unknown故障矩阵与59项交付仍未完成。

## 已捕获的实际切点

正常可见Authentik登录后，Vue回收全新1 MiB+7字节原件，1000:1000/0640/user属性及原Root/HDD验证通过，再以新ID预检并确认恢复。固定restore apply helper经MainPID/Invocation/cgroup、容器root/恢复锁/原来源锁三个FD绑定，在原来源已Restored而本Root仍Applying时暂停；原helper PID2702297、Invocation `d0e02dae8d164078beb6e8fa6c98c91f`。

暂停后实际目标为原留存inode，设备/inode/模式/UID/GID/链接数/大小/mtime保持、ctime不倒退，字节SHA/属性吻合，trash路径缺席、来源claim精确指向本恢复。父进程独立重开Applying记录、来源Restored记录、claim、当前HDD元数据及实际数据并归档，才放行页面操作。此时只验证结构和数据，明确未观察到最终RPC回执；没有用合成Source观察投影伪称签名RPC回复。

320×600明主题的真实页面明确请求取消，唯一POST202，取消时间存在且执行取消审计恰1。原pidfd约5.861042秒观察helper退出，计时始于放行前，含HDD核对和页面交互，不是只测取消HTTP耗时。

产品随后在同一意图/执行上观察到真实Completed：最终Succeeded、`CONTAINER_RECYCLE_RESTORE_COMPLETED`、bindingCurrent/historyVerified/historyAvailable=true，sourceState=Restored、sourceRetained=false/sourceRestored=true/restoredByThisOperation=true。RootStarted/执行ID/计划归属保持，租约清空、Outbox0、写入保护释放。成功来源claim继续保留并匹配本恢复，不能套用写前Aborted时清claim的结论。

## 实际页面、权限和清理

Vue显示“本次文件恢复已完成”；准确take=100列表中的原Job详情展示17字段成功/Completed摘要、原来源引用与本操作已恢复事实。三张真实PNG保存暂停、成功结果及原Job；文件名沿用canceled-result仅为历史命名，实际图像与断言均为成功。

普通用户5 GET/4 POST均403、匿名5 GET均401。原上下文正常双端退出、同账号可见重登后，原恢复意图、原键及原来源三个Owned引用404，再正常双端退出；这里不签执行中退出。每轮临时身份精确移除，没有其他用户变更。

独立进程重读SQL、真实最终回执与Root、保留的精确claim、来源Root/HDD实际数据，关联暂停时Applying原件，验证来源/claim/HDD不变及实际原inode仍在目标。之后才精确删除本轮已恢复的自有叶文件、空私有目录及容器；清理后再次独立读取永久历史和资源缺席。没有重复归还或删除永久历史。原回收业务行仍是其历史Succeeded/Trashed，不能用当前Restored物理事实改写旧回收Job。

## 首轮失败及收尾边界

首轮 `eb033a21` 已真实捕获同一类写后切点、发送一次取消并落为Succeeded/Completed，但浏览器误等待写前“恢复已取消”文案，整体门禁失败。原unit `whaledeck-qa-restore-postreturn-cancel-first` / Invocation `8637051fd8e64e059fa7ddfdacf91c28`、日志、失败截图与源码均保留，不补签成功。

首轮只读SQL投影复核（不是HTTP DTO）及独立进程证明实际晚到取消/成功归属/原件HDD后，精确清理其夹具。清理后的首次收尾调用误引用正常live-proof文件名，发生FileNotFoundError；没有重做清理，只补读SQL/Root/HDD并完成保持核验。两项工具错误另存，原门禁仍失败。修正页面选择器后，才换全新来源运行正式门禁。

## 保持与剩余

2346保护、11核心、应用和两宿主3d80d17保持，没有部署/服务重启。正式轮前25来源/11恢复完整行SHA保持；包含首轮两次新来源/成功恢复后，共新增2来源/2Succeeded恢复、活动0。递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root48→50→52/HDD51→53→55，恢复Root23→26→29，两执行各增json/lock/对应来源claim。

源/HDD同device2049，不算异盘灾难恢复；本批无维护连续性采样。继续写后Worker/Agent/退出/撤权、Unknown/来源丢失/实例变化/跨写入与长期清理等剩余组合及59项。17项移出不算完成，25%–35%仍为粗估，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持。

私有封存均在deploy/artifacts：正式轮 `container-recycle-restore-postreturn-cancel-e6828e6aa45442ea94f7f8d72cb76a25`，162文件/115冻结源码，`POSTRETURN_CANCEL_SHA256.json` 摘要 `525da85e16e7ede0f35909a6c5127ef67aaed75f7247fc8f13812df321007cb8`；首轮失败及只读收尾 `container-recycle-restore-postreturn-cancel-3d8f65fa3aee43b79cba98eafd83312b`，162文件/115源码，摘要 `e07743efc2987069d0c72b70b37659ffadc94dfc74421b45205cba2912a5aacf`。凭据、私有配置及身份原件不入仓库或图谱。
