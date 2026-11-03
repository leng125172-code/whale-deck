# 递归ACL内部内核候选

2026-11-04，新增内部递归ACL内核，尚未安装/helper/RPC/永久业务/Worker/API/Vue，不算真实身份页面或完整递归ACL交付。沿用有界FD树遍历/聚合回执，仅trusted类型构造选择ACL配置；原chmod/chown输入、散列域、返回结构与安装授权保持。

access使用显式fileEntries/directoryEntries分别定义文件/目录完整ACL；default要求fileEntries显式null，只修改目录（directoryEntries可为空表示清除默认ACL），文件仍清点并验证原access/noChange，不能写默认ACL到文件。每项保存原两类ACL/身份/属性，独立ContainerRecursiveAclMetadata与EntryMetadata/HDD命名空间，最深先执行、原子日志与逐项备份；Partial/Unknown不续写目标，观察恢复可写回裁定和未开始子项Discard围栏，不能称inspect完全无写入。深度32/数量10000/16MiB元数据/300秒预算、链接/跨挂载/特殊模式与扩展安全属性拒绝沿用。

20新专项（19实际隔离Linux文件系统/1生成规则）纳入489项Linux-root文件测试，0跳过；旧递归chmod24/chown16/单ACL22亦分别通过，8范围及严格语法通过。首次17项里1项替换inode夹具未补普通user属性，snapshot以ENODATA提前失败；修正属性/原ACL/属主/mtime，并pin旧FD防inode复用后通过，原失败不改记通过。真实工作站候选六场景及独立核验随后退出0，实际边界如下。静态编排时的旧名称/loader索引/生成规则残留已在执行前复核纠正，不据语法成功当实机通过。

无协议minor/DB schema、公开接口、按钮或权限安装变化；源与历史同device时不得冒称异盘。来源完整故障矩阵、递归ACL全业务/历史恢复、可恢复删除/长期清理、维护连续性和其余59项继续，主项/阶段不升完成。[当前目标](../GOAL_P0_P5.md)。

## 实际候选运行与独立核验

冻结`b62d099755c056170a486bcf9cdd37676893ed48`，干净树、56个验证源码闭包及11个宿主模块逐字核对，以下实际命令均退出0：

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/validate_container_recursive_acl.py b62d099755c056170a486bcf9cdd37676893ed48
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 -u deploy/validation/verify_container_recursive_acl.py container-recursive-acl-00c12076031b4785a0c5368d93ecb5f5
```

私有证据`deploy/artifacts/container-recursive-acl-00c12076031b4785a0c5368d93ecb5f5/`为root:root/0700、文件0600，不入Git。冻结模块只复制到私有临时bundle，由新独占无网络/非root/只读根容器的固定bind、真实ContainerRoot进程/挂载/UID1000身份和注册HDD配置/UUID依赖运行；无系统安装、服务启停、签名capability/RPC、永久业务Job或真人UI。原用户NOPASSWD与产品Agent最小授权未改。

| 本轮新操作 | 实际结果 | 目标修改事实 |
|---|---|---|
| access-complete | Completed，4完成/4改变 | 两文件与两目录分别采用显式ACL，原default保持 |
| access-noop | Completed，4完成/0改变 | 完整九字段/两ACL保持，无ACL syscall重做 |
| default-complete | Completed，4完成/2改变 | 仅两目录default改变，两文件完整清单保持 |
| default-clear | Completed，4完成/2改变 | 仅两目录清除default，原access/文件保持 |
| partial-access | Partial，1改变/3未开始 | 首个深层文件实际完成后仅候选子进程SIGKILL；不续做其它项 |
| marker-only | Aborted，0改变 | 第一个started标记之后、子项ACL操作前仅候选SIGKILL，恢复写回Aborted围栏但目标全保持 |

共6个新操作，4完成/1部分/1中止，准确pidfd/等待状态证明两次SIGKILL命中候选子进程，不是杀生产Worker/Agent。每场实际核对两文件各2097159字节及完整原inode/属主/mtime/普通属性、非目标ACL，改变只允许目标ACL及其对应普通mode/ctime。无变化/写前中止场完整九字段保持；其它场保留实际ctime变化，不冒称全部原身份字段不变。每次迟到同ID Apply在禁止ACL写入的候选检查下仅返回原终态，原Root字节和物理清单保持；Partial未完成项不继续写，没有自动回滚ACL。

真实HDD保存6份ContainerRecursiveAclMetadata聚合清单和17份ContainerRecursiveAclEntryMetadata逐项原件（4全完成场各4份、部分1份，写前标记没有子项HDD），与永久候选Root/子项日志分列保留。默认ACL场的文件也有noChange验证和历史，不将其冒充default写入。子项原两类ACL、身份/普通属性、parentOperationId/manifestDigest、专属域和派生操作ID均绑定。

独立核验重新读取实际Root/HDD，重算聚合计划/清单/历史、各子项计划/历史/版本/原ACL/目标mode/普通属性与outcome摘要，逐项比对前后清单、计数和时间顺序；实际存储挂载再次attest，准确源和CID已不存在、11核心healthy。已清理源的完整字节由父门禁清理前实际验证，独立不声称重读已删除源。两模块的独立字节生成规则已离线正负校验。

原145份普通ACL历史全部字节及九字段保持，没有在普通ACL命名空间追加/替换旧记录；新递归ACL使用独立命名空间。7项受保护配置及完整部署账本保持，11核心全部ID/镜像/StartedAt及两宿主2178169/2178164保持，实际运行仍28062da/1.52；无活动任务/租约/屏障/终端/路径锁。root只读Git禁用可选锁，候选运行/独立核验前后用户索引所有者1000:1000、inode/模式/字节SHA保持。旧失败源树未动，新源/容器仅在完整证明后准确清理，Root/HDD永久保留。

源目录与备份HDD为同一device，本场不算异盘恢复或整机NVMe运行布局验收。20新专项中的Unknown/孤儿回执、ACL syscall中断等是实际隔离文件系统检查，其中ContainerRoot/挂载/UUID为显式夹具；不能拿它们补签本场没有运行的物理Unknown或生产故障。489 Linux-root（包含20新）/8范围及语法通过，不冒称本轮全量后端/前端重跑；CI已接语法和自动发现，终态另取。

下一批接专用.NET协议/SDK和固定受限传输，再实际安装与签名RPC/旧能力兼容；永久业务/共享保护/API/Vue、真实身份页面与完整故障/历史恢复随后逐项完成。当前只是内核候选实盘分项，不计递归ACL或主阶段整体完成。
