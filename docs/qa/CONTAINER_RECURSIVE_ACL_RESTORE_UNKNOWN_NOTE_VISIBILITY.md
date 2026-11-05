# 已处置Unknown任务说明区：真实身份只读可视补验

2026-11-05，承接[固定重命名Unknown实机门禁](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_RENAME_GATE.md)。本批以新临时管理员正常Authentik登录，只读查看该已完成处置的原执行Job，补充完整说明段的可视证据；没有再次捕获来源、恢复ACL、取消、ACK或重放故障。验证源码 `d26d2bcf4539512095d34c8fcb151137fa1c03e7`，产品仍为 `34153d2`。

## 实际验收范围

进入前逐项验证原308文件封存，当前SQL必须完整等于原acknowledged.sql，原终态必须为Acknowledged、原回执Unknown、聚合五计数0/0/0/0/26。新观察者Subject必须不同于原用户及原处置者，不能将新登录冒充原上下文。

原Job列表/详情及26字段、23标签与SQL历史投影一致；实际页面切换320/1440宽度、600高度以及明暗主题四组合，核对HTML实际dark状态、字体就绪、两段完整说明文字，逐段滚动并要求可见及Intersection比例1，记录实际boundingBox严格位于视口内且无文档横向溢出：

- Unknown计数限制：0不代表未发生修改，缺回执保持未证明，需核对原任务、逐项记录及HDD历史。
- 处置限制：仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。

共8张完整说明区图，另2张当前Job计数/元数据图；均是真实部署页面，不用测试接口或响应拦截。人工检查了320明色Unknown说明图，完整文字实际可读。所有域内非GET/HEAD请求计数为0，正常BFF/提供方双端退出，Jobs列表/详情与全局恢复原任务匿名查询均401；临时观察者按原工具正常删除。

本批只签**当前Acknowledged且原回执Unknown**的显示，不回填为原Unknown阶段、原管理员或原处置者上下文的可视验收；原13张图、DOM文字检查和原308文件不改写。`preAcknowledgementVisibilityAccepted=false`保留为明确机器字段。

## 只读保持与证据

准确unit `whaledeck-qa-unknown-note-ce047be7`、Invocation `dafeae2dcce943aab936432fbd672a0c` exited/ExecMainCode=1/ExecMainStatus=0/Result=success。原任务SQL前后相同；2346保护文件、来源Root1964、恢复Root257/HDD169、目录Root94、11核心身份/健康和两宿主进程，以及普通62意图/56执行/118 Job、恢复24意图/19执行/活动0，前后全部相同。没有Worker或产品重启、覆盖发布。

新增8项构造证据负向/范围测试、关联220项root恢复工具通过，零跳过；3 Python AST/2 Node语法及diff通过。覆盖重复/缺失/错尺寸或主题、截断/非有限/布尔坐标、替换文字/标签/图名、旧主体冒充观察者、域写入/错误状态/事后补签、原回执不符、异常退出与响应/认证注入拒绝。构造矩形不算真实可视证据，实际四场景由上述部署页另行验证。

私有目录 `restore-unknown-note-650c64d1955b496f8fa24214cc02a3d9` 封存135文件，清单 `EVIDENCE_SHA256.json` 摘要 `46a1a68d160fff6d84a5f599c87255f08d82062bc5dd9308d6acfa8b7c2da396`，逐项大小/SHA256通过。含112文件冻结验证闭包、原封存引用、前后SQL/生产快照、真实10张PNG/文字与坐标/正常退出、准确unit回执和日志；不含密码、cookie或登录token。

## 可恢复删除的下一实现点

已按实际代码及安装清单核对：容器文件、复制/移动、上传备份、历史恢复均已安装并接入，不采信源码中早期“not installed”注释作为当前状态。删除内核可复用宿主 `whaledeck-file-archives.py` 的Trash专属HDD捕获、journal、FD相对不覆盖私有留存及恢复状态机；安装入口和BFF/Worker也已有宿主实现。

容器现有 `ContainerBackups` 是Overwrite/Available捕获，不能冒充Trash回收证明，也不能将宿主rootId/path证明当成容器证明。下一切片是容器普通单文件的可恢复删除内核：持续PIDFD/rootFD与容器实例/父目录绑定，先验证Trash HDD捕获，再原子私有留存原inode，恢复首片只允许空缺原路径；明确失败/中断恢复及幂等、容量/隔离/权限/扩展元数据边界。目录递归、覆盖恢复和永久清理保留为后续完整子功能；本批尚未实现或签收容器删除。

59项、阶段完整签收、整体25%–35%工作量粗估、AI子计划0%保持；其他故障组合与原Unknown阶段独立说明图也不据本只读补验自动通过。

收尾：准确d26d2bc已推送dev；范围8项、2099本地Markdown链接/缺失0、diff及135文件封存再核验通过，两Git索引仍归用户所有。项目图谱事件whale-deck.container-acl.d26d2bc.current-unknown-note-visibility取得projectId/key/eventId匹配、duplicate=false回执，pending移至synced、阶段active。本批未取得该提交整条CI终态，不将本地及只读页面通过报成CI全成功。
