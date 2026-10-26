# 容器上传：首字节前取消与延迟发送拒绝

2026-10-26，单工作站59项范围。当前为minor40候选；以下本地/原生profile通过不等同已安装或业务/UI验收。

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

扩展同源码安装态探针，验证capability、真实签名UDS取消→查询→重放→延迟首帧拒绝，独立检查根回执和零载荷后精确清理。随后接永久容器意图/EF迁移、Job/Outbox/租约/权限/审计和前端；当前尚未新增这些业务接口。

宿主与不同容器可能映射同一物理目录，不能仅凭CID/rootId不同判断不冲突。后续发布阶段须与现有宿主文件事务门禁协调；浏览器等待上传期间不应占有全局写锁。该协调尚未实现，不把本取消子门禁当完整上传或P2完成。
