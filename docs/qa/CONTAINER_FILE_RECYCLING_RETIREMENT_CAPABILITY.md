# 退役能力验证器与实际正文绑定候选

2026-11-09，候选 `2de34c2d487f2482d14c328531fbf4831c54f60f`。本批实现服务端能力验证器及拦截器准备，未注册/宣告/部署退役RPC，Agent运行协议仍为59。原冻结Python闭包和老化来源未改。

## 权限边界

新增 `ValidateContainerRecycleArchiveRetirement`，仅允许独立退役服务的Plan/Reserve/Apply/Inspect/Discard五种精确方法，复用既有 `ValidateContentBound` 的60秒最大有效期、规范32位非零小写GUID nonce、规范64位大写请求哈希/签名、至少32字节密钥、固定时间HMAC比较及并发单次nonce消费。使用独立退役域，不复用ArchiveCopy或Originals purge权限。

通用 Validate 在公共GetHealth/GetCapabilities/WatchHealth后缀豁免之前，先拒绝整个退役服务前缀。看起来是健康查询的未知退役方法也不能绕过。错误方法、错误正文或无效签名不会误耗合法nonce。

拦截器退役分支只接受Unary。Plan必须是准确PlanRequest，其余已允许动作必须是准确Mutation Request；在消费nonce前递归拒绝未知protobuf字段，并从实际IMessage完整正文重算哈希，与请求头逐字节匹配，再检查专属签名。不能只拿签过的旧哈希头搭配新正文。server/client/duplex三种流式形态均在授权前拒绝，不向续调委托传递请求。

这只是请求完整性及短时单次能力验证，不是完整资源/Actor授权或原生计划语义政策。后续gRPC处理器仍须核对Subject/trace/job/owner/操作ID、原链、计划内容、资源范围、大小和执行状态；Gateway、服务端政策、原生适配和标准协议/安装均未实现，不因新验证方法存在而对外开放。

## 验证

29项新增能力用例通过，联合前批26合同为55项：五方法短时内容签名、缺失/错误hash、旧签名及跨域、过期/超长寿命、规范hash/nonce、缺失/短密钥、并发16次仅一次成功、未知/公共样式后缀在验证器及拦截器均拒绝、缺token、改变实际正文、重放、错误描述符、未知字段、三类stream拒绝和拒绝后nonce仍可正确使用。

全量能力回归 **677项通过、零跳过**；全量C#Unit **5967项通过、零跳过，28秒**；NET10.0.402编译通过，14前端CI合同、8范围和Git diff检查通过。首轮构建因测试中的stream泛型类型推断CS0411失败，未执行测试；指定准确泛型后构建和最终测试全部通过，不追认首轮成功。没有改变生产配置或运行服务。

独立只读复核未发现签名/分流/正文绑定绕过或旧服务回归阻断项；指出的未知后缀拦截器覆盖已补充。复核未运行测试，不替代结果。原合同测试继续确认Agent不广告退役能力、proto未声明该服务。

构建后原自然老化来源在干净环境观察通过：captureAgeElapsed=false、retirementExecuted=false、automaticExecution=false，最早时间仍为 **2026-11-10 10:00:02.252+08:00**。原claim/HDD/实例/保护保持，无新备份来源和实际退役。

证据 `retirement-capability-validator-fbd62c9ffb7b4faa9d20deea90a2752f`：5文件另加manifest，SHA256 `c0f28eca58d7b5713f6034e914e32cbb2bf4270b3ea73b1d131a27f283889568`；Root:Root/0700目录、0600文件，包含3提交源码、实际观察器输出和核验计数摘要。只使用随机临时测试密钥，不存储或输出实际能力密钥；不重建未保存的完整测试原始日志。

## 续跑

本批新CI待准确完整终态。下一步补退役请求/计划政策、gRPC/native适配、Gateway和原生探针；标准版本/安装门禁通过后才宣告能力，再补持久化业务/Worker/API/Vue/Jobs与真人整链。自然到期仍须原来源独立重核、显式受控执行/物理证据与失败恢复/收尾。全部59项继续，17项移出不算完成，整项0/59、阶段0/6保持。
