# 首轮失败自有来源的离线归位与精确清理

2026-11-06，工具b3d00e1cabc0cdc2cf6cb4edb9513c0684015363，产品仍f333a69a/Agent1.57，四容器仍58。承接[安装态恢复分项](CONTAINER_FILE_RECYCLING_RESTORE_AGENT.md)，本批只处理3ce57573失败后保留的唯一自有测试来源，不签新的产品恢复、原实例重绑定或业务页面能力。

## 为什么单独处理

该容器56b7166c已按原600秒sleep在原StartedAt对应运行中正常退出，Pid=0/ExitCode=0；未启动或重建它。当前产品恢复限定原运行实例，不能把这份过期实例的离线取回包装成新恢复RPC成功。

回收操作843db5d4b04b4d3787d3df23dcdadac2及原Root/HDD、留存inode/字节/属性均按已封存失败证据核对；失败manifest必须等于5bf7957d2f244e56c2453ac091b6a9310ea7bb0a0c7f30561491da851f946849。没有重放原Apply，未修改原失败目录、Root、claim或HDD；旧失败继续failed。

新增脚本仅放在deploy/validation，固定此失败证据和自有路径，不安装通用root入口、不扩大Agent sudo或资源授权。它是root运行的QA资源收尾，不是产品恢复接口。

## 实际执行与防覆盖

先验证唯一wdv自有目录、容器ID/镜像/绑定挂载和安全配置、准确的已退出实例、原普通文件与根目录身份；原original.bin还须符合该夹具生成器的确定字节摘要、1000:1000/0640/单链接及空用户属性。目录多出文件、符号链接、硬链接、来源摘要/身份变化、目标出现或来源已有恢复claim均拒绝。

持有原来源的准确root:root/0600空lock及非阻塞独占锁后，再读Root和实际HDD元数据/数据，保留原字节。对私有目录和源文件使用无符号链接FD，核对完整身份，以Linux renameat2/RENAME_NOREPLACE把原留存inode原子归位到recycle-rpc.bin，并fsync两个父目录；记录归位后的九字段、完整字节及用户属性。归位不是复制，也不会覆盖迟到目标；每个QA检查点文件排他创建并fsync。

独立进程在实际归位后重读当前inode/字节/属性、私有目录、原Root/HDD与全部保持快照。通过后才以非force docker rm移除该已退出的准确容器；并发启动会使删除失败并保留数据。再次确认两份夹具文件的身份和摘要后，只unlink两个明确文件、rmdir已验证空私有目录和唯一自有目录，没有递归删除。清理后另一个独立进程确认容器/目录缺席、Root/HDD原字节及其他保持字段不变。

本批人工QA取回不改写原生历史。原Root和HDD captureState仍是最后的Trashed原生事实；它们不证明人工取回之后的当前物理留存，也没有伪造Restored/Completed回执或新业务意图。另存manualQaTakeback=true/productRestore=false，保留完整来源和动作证据；今后的产品操作仍必须检查实例及实际来源，不可仅依赖旧标签。

## 检查与证据

五项新增真实临时Linux文件检查通过，覆盖原inode归位、实际EEXIST保留源和迟到目标、字节/属性/inode篡改、额外条目/符号链接/硬链接与归位后伪造证明拒绝。含既有工具共121项、范围8项零失败/零跳过；这些临时检查不算产品或UI验收。

实际unit whaledeck-qa-recycle-owned-recovery-b3d00e1c，Invocationb5024d028bd748dcae7e741f5f6e2013，准确exited/Result=success/ExecMainStatus=0。归位后及清理后两次独立检查通过；第三次实际读取与清理前后全量快照逐值相同。2346保护文件、11核心、回收Root32/HDD35、独立恢复Root5、目录Root96及数据库保持；两宿主仍f333，未部署、重启或改变身份。

证据deploy/artifacts/container-recycle-owned-recovery-ea8a1efb1dc045d2b8f0892e71034c19，共121文件/101冻结源码，EVIDENCE_SHA256.json摘要447c9f37b54cc121e6ddf0bf3aee28040bf72a3b9f8706d5d5d6d18c3ea5ad85，逐文件重算通过。原失败及成功轮证据保持原封存摘要，未补签旧失败为通过。

## 后续

首轮自有留存的人工归位与清理已结束。接下来实现独立永久恢复业务：只从可授权的原回收意图解析不可变完整来源/HDD摘要，新意图与新Job独立确认、实时授权/取消/租约、原执行Inspect优先、Unknown保护与回执归属；再真实PG/共享写保护/Worker/API/Vue和真人恢复整链、完整故障矩阵、长期清理及其余59项。当前仍无该独立业务Repository/Domain意图/Dispatcher/API，不能把已有Gateway当成完成。完整主项0/59、阶段0/6、25%–35%工作量粗估和AI子计划0%保持，17项移出不算完成。

项目图谱whale-deck/container-recycle已按稳定eventId回执同步active，pending移入synced；未标整项或阶段完成。

2026-11-06后续：本记录所述当时尚无的Domain意图与编排，已形成[内部业务候选](CONTAINER_FILE_RECYCLING_RESTORE_BUSINESS.md)并通过单元验证；仍无具体持久化Repository/EF迁移或运行时工作流、实际PG及页面证据。本记录原文按当时边界保留。
