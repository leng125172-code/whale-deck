# 递归ACL专用协议、SDK与受限传输候选

2026-11-04，候选源码`ae3cab5a08453e03125ac27528429251645e3076`，未安装或实际发布。本批接上上轮[递归ACL内核](CONTAINER_RECURSIVE_ACL_ENGINE.md)，不重放其6个原生候选操作；上轮真实Root/HDD不是本批安装态RPC证据。当前运行仍28062da/1.52，新helper/状态目录不存在；没有新业务表/迁移、Worker/公开API/Vue或真人页面验收，59项及阶段不升完成。

## 完整实现范围

- 新ContainerRecursiveAclService四个unary方法Plan/Apply/Inspect/Discard，独立request/capability散列域、完整消息与方法绑定、短时单次nonce；通用无正文签名不接受此服务，也不能借用普通ACL/递归chmod/chown签名。Agent候选minor1.53与docker.files.recursive-acl-intents专属协商，SDK拒绝旧minor/不同major/缺能力，旧方法和最低版本不降级。
- access命令分别指定fileEntries/directoryEntries。protobuf使用message wrapper保留presence：default必须file wrapper缺席、directory wrapper存在，目录空列表明确清除；不能把缺字段、默认null或空数组相混。Entry沿用显式optional ID/permissions，ID0及权限0有效，缺named ID/permissions、mask、乱序/重复或超过64项拒绝。JSON私有driver输入显式fileEntries:null，非模式/所有者命令；输出严格字段集合/重复/类型/目标与原计划校验。
- .NET Application DTO/验证与IContainerRecursiveAclGateway、Infrastructure SDK/DI、Agent gRPC/runner/能力拦截完整接通。SDK对ACL列表做序列比较，保留文件null与目录空；原计划/身份/原Job/幂等键/trace/owner及全部字段进入签名。Receipt额外绑定aclType，四类计数总和等于entryCount、changed不超已完成或预检变更数，default变更数不超目录数；Completed/Partial/Aborted/Unknown不可互冒，历史/可用性/当前绑定及outcome严格检查，无单一bool冒充整树已改。
- 独立固定stdin driver、root状态目录和systemd unit/stop分支，ReadWritePaths只含新状态目录/apps/固定HDD备份根；capability仅DAC_OVERRIDE/DAC_READ_SEARCH/FOWNER/SYS_PTRACE，无CHOWN/SYS_ADMIN。固定四动作、GUID单位名、实际InvocationID匹配后准确停止，BindsTo Agent、SIGTERM逐项协作/5秒硬停止、340秒/384MiB/CPU及16MiB文件限制。旧helper资源范围不扩大。
- 复用普通ACL的初始user namespace与statmount BY_FD/非IDMAP证明，但包装**最终confined child**及整树root，覆盖root最后写入和各子项parent/snapshot，防止递归parent override绕过检查。缺证明直接拒绝，ENOTSUP单独unsupported，EIO/未知传输故障不能冒充安全无变化；没有通过追加CAP_SYS_ADMIN/CHOWN解决映射。
- install.sh/validate.sh已接0700 root目录、0750 root模块、无符号链接/单链接检查，CI接语法及自动发现新root测试；这些是安装实现和离线验证，不是本轮执行了系统安装。安装后的真实依赖、准确stop/EOF、签名拒绝与兼容仍须实机取得。

## 实际检查与失败记录

完整Release构建0警告/0错误；4500单元通过、490集成通过，7项默认原生测试跳过（真实PG/Valkey单独门禁未重跑）。506 Linux-root文件测试通过、0跳过，包含本批17个传输/授权范围新检查和前批20个内核检查；本批递归ACL关联组37项亦通过。8范围、安装CLI5项（干净PATH dry-run，不执行系统操作）、Python/Bash语法及diff检查通过。没有前端代码修改或新增页面证据，不冒称前端全量重跑。

验证包括：presence/default清除的Agent JSON与SDK往返、数值0/缺permissions/ID、完整签名篡改与方法串域/过期/重放拒绝、旧能力拒绝、RPC目标/aclType/四状态与计数/历史伪造拒绝、固定argv及准确stop选择；Linux实际隔离ACL syscall和SIGTERM部分恢复，以及每个FD与最终root子项的映射guard/refusal。C# HTTP handler、capability密钥和身份均为显式测试夹具，Python容器/HDD/namespace/statmount提供者亦有夹具；不是新的真实Authentik、安装态HDD或受限unit/签名RPC实机通过。

首轮构建在新Guard的无消息ArgumentException上触发CA2208，补固定错误码后通过；首轮118个C#专项114过/4失败，复制的四个argv预期仍写chown名称，实际路由为专用ACL，修正预期。随后新增SDK缺permissions/default清除夹具触发CS8602可空解引用，改显式类型断言后完整构建及4500/490通过。失败均保留此事实，不称整批首轮通过，也未更改运行版本取得测试结果。末尾proto空行由diff检查发现并在提交前修正。

同源码工作树完成测试后冻结ae3cab5，再从干净提交重建；Agent --build-info实际退出0并返回sourceRevision=ae3cab5a08453e03125ac27528429251645e3076，构建身份分列保存，未启动新服务或声称安装完成。私有证据deploy/artifacts/recursive-acl-rpc-candidate-6ac20b13396443788762295938fc1bab/为0700、文件0600，含完整测试日志、构建来源与只读安装态观测，不入Git。

## 当前安装态与后续

只读比对前一已验内核基线：11核心全部ID/镜像/StartedAt保持且healthy，Agent2178169/维护2178164及实际28062da来源不变，7受保护配置与完整发布账本保持，原145普通ACL元数据字节及九字段不变，活动任务/租约/屏障/终端/路径锁0。user NOPASSWD保留、产品安装态授权未改变；root只读Git禁用可选锁、索引仍属1000:1000。未新建真实身份、容器、HDD捕获、发布unit或重启服务，不重复上轮原生操作。本轮比较针对上一验收基线，不能冒充本轮开始前另做了快照。

下一批补新协议原生探针与运行依赖证明，待准确CI成功后标准宿主安装、真实签名四RPC/HDD、边界拒绝/旧能力兼容与准确清理；其后永久业务/共享保护/Worker/API/Vue及真实身份整链。完整来源故障矩阵、递归ACL历史恢复、可恢复删除/长期清理、P0-05维护连续性与其余59项继续；不自动回滚dev。[当前目标](../GOAL_P0_P5.md)。
