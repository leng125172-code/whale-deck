# 容器上传：首字节前取消与延迟发送拒绝

2026-10-26，单工作站59项范围。minor40宿主已安装并完成下述签名UDS子门禁；不是永久业务任务或人类身份/UI验收。

## 安装态结果

冻结`136b8a0a96f34fc615b2ab22f4d4391512f13a44`构建并覆盖Agent和MaintenanceHost，实际build-info/固定模块来源核对、10.0.13运行库/PKCS、systemd零自动重启、权限/UDS/受限helper及当前动态入口校验通过。应用和基础容器未更新，本轮没有Whale Deck镜像构建。

独立探针`a58a48926420d24083919aa42bd34d41335ccc37`通过canonical安装态真实UDS：要求minor40及独立取消能力；完整2,097,159字节暂存、发布、原只读RPC逐字节读回、旧inode/HDD原件与主体/签名/实例保护通过；已有暂存实际丢弃/重放通过。第三个未发送操作先取消，Inspect/重复取消均为Prevented；保持客户端流未完结，发送其合法首帧，服务直接返回精确`FailedPrecondition + CONTAINER_UPLOAD_CANCELED`，不是任意失败状态。再次查询仍为Prevented，根私有目录无该操作partial/data，目标不存在。

独立完整输出/旧inode/HDD原件/绑定证明后，只清理已证明的三条自有QA回执、payload、取消标记及绑定目录/容器/历史。QA取消标记仅在唯一已知合成发送者RPC已终止后删除；这是测试资源清理，不是生产过期策略。复核根回执/payload为零、本批应用目录/profile历史目录/活writer为零，11核心全部healthy。受信root QA客户端和实际Agent→受限sudo helper不等同人类BFF权限，相关业务与页面仍缺。

### 后续业务中明确保留的约束

- 宿主与容器物理挂载别名需要在发布阶段共享持久写屏障，不能拿不同逻辑rootId或CID当作隔离；浏览器等待上传不应独占该屏障。现有宿主队列行/锁数和父子路径规则保持，业务实现仍待。
- 登录绑定不能只比较原Subject仍为管理员。BFF票据删除后，原上传必须停止发放发布能力；需接可撤销的登录存活索引及最终接收/发布前复核。当前ServerSideTicketStore只有原始随机引用的缓存条目，Worker不能从单向LoginBinding反查；Renew还须防止与Remove并发时复活会话。这是已定位的后续实现要求，不是已修复或实际被利用的结论。

## 修复的前置缺口

接永久任务前发现minor39的Discard只能处理已有上传回执。取消先于第一块数据时，NotFound不能证明延迟发送不会再创建暂存。SQL不应据此释放互斥或标记取消完成；等客户端断线或超时也不是持久拒收证明。

新Discard在严格容器ID/原始启动实例、owner、操作ID、根/命名空间/配置证明匹配且无既有回执时，使用与Stage相同的操作锁原子写root0700私有域内的0600回执，fsync后才返回`Prevented`。该schema2的resourceType为`ContainerUploadCancellation`，与正常schema1上传及HDD schema2 ContainerFile分开，严格字段、时间、单链接和权限检查。查询和重复取消保留原标记；Stage、Publish拒绝该操作。若Stage已经获得锁则返回Busy，不伪造取消完成；已有暂存仍必须实际校验及删除，返回Discarded。

Prevented仅允许空内容SHA256、零字节、无发布版本/当前绑定/旧原件/备份事实，不能冒充空文件Stage成功。正常写入原协议仍要求minor39；新增SDK `PreventContainerUploadAsync`另要求minor40和`docker.files.upload-cancel-fence`，拒绝旧Agent/缺失能力且不退回NotFound推断。五个写RPC、请求体签名及原只读四RPC不变。生产没有公开删除取消标记的接口；容量计入既有有限回执预算。

## 已验证

- Release零警告/错误，2441 Unit全部执行通过、160 Integration通过，6专用原生依赖夹具跳过。
- 21项新Agent codec、9项SDK真实私有UDS合成Agent、8项Linux-root引擎与4项QA清理守卫；容器联合166项全部执行通过。SDK夹具不是安装态或人类身份。
- 原生真实内核测试：live操作锁忙时无Prevented；SIGKILL释放锁后可持久拒收；写完标记、回包前SIGKILL，新引擎仍识别并拒绝延迟Stage；原件全内容不变。错owner/目标、畸形或硬链接标记不构成证明，取消记录消耗同一容量预算。
- 实际systemd profile在独占非root/无网络/只读根+批准绑定的容器中，完整2MiB+7发布/HDD原件、正常取消/精确InvocationID停止与新首字节前取消、跨helper查询/重放、延迟Stage及Publish拒绝通过。本轮载荷、容器、目录和HDD子域精确清理。profile仅替换自有QA路径，尚不充当canonical安装态。

初次新检查错误地把固定控制锁文件也算孤立载荷，已改为允许既有控制锁且明确拒绝任何partial/data，保留完整源身份/内容检查。未放宽对持久标记或载荷的断言。

## 接续

安装态取消→查询→重放→延迟首帧精确拒绝及清理已完成；接续永久容器意图/EF迁移、Job/Outbox/租约/共享写屏障、可撤销登录/当前权限/审计和前端。当前尚未新增这些业务接口。

宿主与不同容器可能映射同一物理目录，不能仅凭CID/rootId不同判断不冲突。后续发布阶段须与现有宿主文件事务门禁协调；浏览器等待上传期间不应占有全局写锁。该协调尚未实现，不把本取消子门禁当完整上传或P2完成。
