# 独立恢复原件移走与正常退出：Superseded 实机验收

2026-11-06。工具冻结 `3daded65efb7f215996a4908cba8fdccaf6e3332`，生产仍为093f44a9。全新隔离资源中，先实际返回原inode，再只移动本轮原件并正常退出原上下文；同一执行结算Superseded，Root回执仍Completed。清理前后独立核验通过。本页只签这个组合，不补签同CID实例变化或真正无法确证结果的Unknown。

## 范围及故障动作

实际临时Authentik身份通过可见Vue流程完成1次来源回收预检/确认、1次独立恢复预检/确认，共4个接受的业务POST。容器固定非root、只读根、无网络、有限预算及唯一自有bind；未改变Agent权限、宿主网络或真实业务文件。

精确returned cut要求本恢复日志Applying、来源日志Restored、原inode已返回且retained缺席、claim和当前HDD一致。动作前重新核对实际容器及原PID:StartedAt、Root目录和恢复锁/来源锁三份内核FD证明、helper的MainPID/cgroup/Invocation、停止状态和已打开父目录的原dev/inode。只将`recycle-rpc.bin`通过renameat2 NOREPLACE移动到固定`restore-displaced-returned.bin`，占位目标须不存在；前后内容/属性/大小、identity前8字段一致，ctime允许合法重命名变化，原helper仍活。不移动HDD、claim或其他原件。

页面正常BFF退出的实际导航POST被观察时，原helper仍活；Agent按Invocation停止原服务，systemd停止策略处理已SIGSTOP的进程，QA不提前SIGCONT制造退出。原pidfd约5.807秒退出，计时从允许页面动作之前开始，不宣称从BFF响应时刻起计时。错过切点、原进程提前退出或证据不符都不会重放Apply，未决资源保留。

## 实际业务与历史事实

unit `whaledeck-qa-restore-superseded-ca07c096`，Invocation `3923f6ad2ba64a029a091364ba55252f`，exited/Result success/ExecMainCode 1/ExecMainStatus 0。

同一恢复意图Superseded、结果码CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED；执行Job为Failed/phase Superseded，专属17字段摘要与SQL/API逐项对应。Root receipt为Completed，sourceState Restored、sourceRetained false、sourceRestored/本操作恢复归属/HDD验证与可用均true，而bindingCurrent false。保护已释放、租约清空、Outbox处理完毕，取消时间空、取消POST/审计和retry请求均0，无Unknown处置或ACK。

这保存了已发生恢复的历史事实；当前路径缺席不等于“从未恢复”，也不硬签Unknown。claim、sourceRoot、HDD history及data在cut、当前回执和独立读取间按完整结构/字节保持；本恢复Root由Applying变为Completed，另按原请求、回执和完整记录校验，不称其终态字节等同cut。当前源路径和retained均空，原inode仅位于本轮位移名；不调用要求原件仍在源路径的普通physical_proof冒充该命名空间。

两次正常BFF/OIDC双端退出均302、BFF me401/身份源me403、最终登录页成立。同subject重登后原意图ID/幂等键/原来源三个旧引用404，专属历史读取200；真实Jobs页展示“已有历史结果，当前原件状态已变化”、原来源ID和正确文件页链接，通用重试按钮缺席。普通9个、匿名5个请求拒绝。保留2张真实页面PNG；权限和持久化没有使用路由响应或会话注入。

## 独立核验与清理

清理前独立进程重新读取实际位移原件、Root/claim/HDD及SQL/Job，确认完整绑定。清理必须精确匹配已独立验证的位移快照、原inode稳定字段和原SHA/属性/大小，原路径与retained仍空；空私有目录的原身份、root所有权和0700先核对。随后仅移除本轮位移文件、空私有目录及经fixture规则验证的自有容器/种子，不递归删除未知内容，不恢复/重做原操作。final再次读取保留历史及HDD，使用先前live原件证明，不声称读取已删除的源。

9项专项与47项关联Linux root检查通过、零跳过；Python/Node语法、9项CI分片合同和8项范围检查通过。专项覆盖外来身份/执行、错误终态/回执、丢失摘要字段、非整数计数、未处理投递、旧验证快照及错误清理对象。部分是明确的纯投影或namespace替身，和上面的实际Auth→Vue→PG/Worker→签名Agent→原inode/HDD整链分列。CI增加新门禁语法检查，专项文件沿现有installer通配收集；不把工具的新CI运行状态称为新产品发布。

私有证据 `deploy/artifacts/container-recycle-restore-superseded-bc69807f69344bc5ad7f530fd814412e`，冻结实际123份源码闭包，共169文件清单 `EVIDENCE_SHA256.json` 摘要 `6aca83cfdca5e394820fd51d24cb77fa61f39004d0502c9e2e7a67b17fab704a`。实际逐文件复核，父目录0700、文件root:root/0600；凭据不入Git、日志或项目图谱。

最终33回收/19恢复意图、活动0；回收Root66/HDD69、独立恢复Root50。2346保护文件、11核心容器、旧完整业务行及093f44a9两宿主保持（Agent2974845、Maintenance2974836）。源/HDD仍同设备2049，不称独立物理盘灾备。下一步推进同CID实例变化、真正Unknown及跨写入组合、长期清理与其余59项。17项移出不算完成，整项0/59、阶段0/6、酒馆与Live2D功能0%保持，master完整交付尚未完成。
