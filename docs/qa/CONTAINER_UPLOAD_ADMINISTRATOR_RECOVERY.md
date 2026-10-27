# 容器上传管理员接管持久化与界面候选

日期：2026-10-27。单工作站59项中的文件管理子范围，未部署候选。Root minor41历史观察/封存协议在上一阶段已有源码、Linux-root与真实systemd profile证据；本批接Application/SQL/现有恢复调度器与Vue，不据此签安装态RPC、完整上传或文件模块。

## 业务与数据

- 新增第12类`container-upload`，独立于宿主`upload`，保留原11类rank/游标、MAC域和全部旧行为。容器inventory只有独立container三字段，source/target=null，不伪造rootId；MAC无效时不输出私有定位。不再以default分支解析成宿主move。
- 只能AcknowledgeUnknown。历史Root Published、Missing或Staged均映射为业务Unknown，不能ReconcileCompleted或伪造当前版本/上传成功。原上传最终Failed/CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED；新恢复任务Disposed/FILES_RECOVERY_DISPOSED、OriginalResolution=Failed，两个DTO分开验证。
- 复用永久恢复计划/Job/Outbox、五分钟确认、十五分钟处理、90秒租约和唯一原任务预留。原CID、纳秒实例、owner、Trace、登录、原Job/幂等与版本仍MAC验证并冻结；当前恢复执行ID与已批准观察单独携带，不改原身份。
- 新类型的服务入队/确认/读取和调度器cap签发前/过程中均校验当前管理员及原始当前登录存活；缺少registry、退出、降权或缓存故障时拒绝。接管不要求旧登录继续有效，也不冒充旧用户。权限TTL仍按全局两小时约定。
- 业务层独立校验Root历史摘要、原scope和精确JSON字段；确认计划绑定全部观察。完成封存但回执丢失时，只观察同一恢复ID的封存并对账；请求尚未到Root时，仅可重试相同幂等Seal，不重传文件、不重做Publish。另一个管理员可以明确批准已存在封存的同一完整事实，而不改写旧sealer。
- 源Unknown记录/版本、无活租约和未完成原Job在共享advisory gate下重验。只有已批准的封存证明才在一个SQL事务中保存原上传失败、清除全局publish_barrier、结束原Job/Outbox并写当前管理员不可变审计。审计失败回滚上述全部内容；租约到期或普通Unknown不能清除保护。
- EF迁移20261027015755新增kind与仅允许AcknowledgeUnknown的数据库约束；Down遇该类永久历史明确拒绝，不删除或改类。永久意图原有禁止删除触发器保留。EF model无待迁移差异。
- 完整业务上传预检提升为minor41与独立恢复能力；旧minor40暂存/发布原语兼容诊断，不代表支持新的完整业务流程。新HTTP/Worker上传链尚未开放，不在缺少恢复能力时接受新业务上传。

## 验证和失败记录

- Release零警告错误；最终2593 Unit/174 Integration通过，默认7原生夹具明确跳过。新增原材料/调度检查包括独立kind、旧MAC/上下文/私有快照篡改拒绝、历史Published不可伪成功、错误JSON/摘要/封存拒绝、原观察变化需新确认、退出/降权/无registry/缓存故障前置拒绝、封存中退出不对账，以及收包前丢请求和完成后丢回执两条不同路径。
- 冻结e450f97真实隔离PostgreSQL1执行/1通过/0跳过，迁移、新恢复与全部原生旧业务通过。补收包前丢请求后，再冻结34ffb2e复验1执行/1通过/0跳过（02:40:37Z–02:43:24Z）；临时SDK容器和专用数据库清理通过，独立Docker查询无残留。Root、当前身份及登录registry为明确合成夹具；这不是实机Root写入或Authentik权限验收。
- SQL场景覆盖独立容器inventory/无宿主别名、固定幂等与新登录隔离、缺权限/退出/无registry不触达Root、错误版本/确认拒绝、原意图不变、确认后丢封存响应/Worker重建只观察、审计失败原子回滚、接受后唯一屏障释放/其他CID才能发布，以及收包前丢Seal后只幂等Seal重试。UPDATE不可变请求与直接INSERT绕过接受范围均23514，后者精确命中ck_file_recovery_kind。
- 首轮78cab60未通过：旧目录清理降级测试先撞到新类型的历史约束，旧消息断言不再适用。第二轮26616f0未通过：测试teardown试图删除永久恢复意图，正确触发23514。修后保留所有永久历史到整个专用库销毁；完整降级验证最早的新guard，同时执行旧迁移真实DownOperations中的guard验证旧保护，保留旧消息断言，并查询实际迁移历史/约束证明未发生部分降级。未关闭触发器、未放宽约束、未删旧测试。
- 第12类Vue只显示接受历史不确定性，隐藏/阻断成功对账；独立容器定位沿用上传的CID/原始实例/UTF-8路径边界。前端协作初稿混淆两个DTO结果码，主模型按后端纠正并加拒绝负例。最终恢复只接受Disposed/Failed/FILES_RECOVERY_DISPOSED，不能把原上传结果码当恢复回执。
- 主模型全量前端455单元、Oxlint/ESLint、Prettier、类型/生产构建通过；两处格式问题已修。协作初轮完整恢复生产预览41通过，契约纠正后相关3通过，主模型基于最终构建再复验3/3通过：320长CID/路径无横向溢出、固定登记/确认、二次确认/零文件字节、离开后私有信息清除与迟到确认零写、普通用户零管理请求。均本地合成API，不冒充真实SSO/SQL/Agent/页面。
- 实机11核心容器逐项healthy；本批未安装Agent、未更新应用容器。私有日志保存在deploy/artifacts/container-upload-administrator-recovery-20261027（目录0700/文件0600），包含两次失败和两次成功，不上传私有环境、路径正文或凭据。

## 继续工作

接通容器上传HTTP/专属HostedService/DI与精确YARP/nginx流预算，再安装minor41并做真实Cookie/当前管理员与原登录、实际Worker/SQL→UDS→Root→HDD/完整读回和桌面手机验收。继续编辑、HDD历史恢复及其余59项；本阶段没有以模拟页面或单个SQL原生Fact签完整功能。

阶段同步：先前Root封存阶段与本批管理员恢复阶段均已用原稳定eventId补发成功，并经list_projects读回确认active，记录从pending移入synced。没有将整个项目或文件模块标为完成。
