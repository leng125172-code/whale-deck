# 退役 Gateway 与应用层合同候选

2026-11-09，候选 `d5798837469b4997dc8dd685e429ad4420a33024`。新增独立Application DTO/校验/Gateway接口及Infrastructure客户端；没有业务编排/DI注册、API入口、Agent服务映射或能力升级。Agent仍1.59，安装态不具退役能力。

## 实现

五种RPC和Require入口都通过实际GetCapabilities协商：major必须1、minor至少60且包含精确专属能力。协商期限5秒，拒绝发生在生成本次签名和发送退役RPC前；不把ArchiveCopy能力或普通方法HMAC当作退役授权。既有Gateway构造器会读取配置密钥，本候选不声称协商前未读取密钥文件。

请求包含完整嵌套Archive/Purge/Source计划及上下文。所有DTO属性字节在第一次await前映射到拥有独立副本的protobuf；随后对固定快照验祖先Archive/Purge和外层指纹，使用独立内容哈希/HMAC域、45秒期限和新nonce签名。每个操作保留120秒RPC期限，不因协商成功推断当前资源授权或自然到期。

新增Application政策校验捕获期限1至3650天、精确due、诊断布尔一致性、祖先摘要/存储/HDD对象、当前新inode设备及原属性、三代来源与新job/ID分离；回执严格绑定来源/归档/指纹/due/状态/围栏。Unknown的null与false均按原合同保存，不可归一为成功。Inspect仅将确切NotFound加CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_FOUND转换为空；祖先缺席、错误状态和传输错误继续抛出。

计划回包在映射前拒绝未知protobuf字段，并校验命令等值、所有指纹及深层语义；动作回执经Wire及Application双层关联。Application.Plan本身仅检查指纹格式，Gateway两个实际边界负责规范指纹重算，不能把单独调用Application校验视作内容认证或执行授权；后续持久化/业务还须独立保护与当前权限检查。

## 验证与实际限制

新增41项Gateway专项，退役联合 **185通过、零跳过**；全量C#Unit **6097通过、0失败、0跳过（34秒）**，Release全解决方案构建0警告/0错误，14前端CI合同/8范围及diff检查通过。验证五类调用/协商拒绝优先于签名、短/空密钥不发操作、正文/方法/域HMAC、嵌套数组跨await冻结、祖先指纹篡改、重新哈希仍非法的计划、回执unknown/nullable和精确缺席。首轮编译与专项即通过。

这些测试通过自定义HttpMessageHandler检验实际SDK编码/解析的合成gRPC帧，**不是安装态Agent、Unix socket或native删除验收**。独立只读复核未发现当前Gateway通路阻断项，并指出Application单独校验不重算指纹的边界，已明确记录。

构建后原老化来源独立观察通过：未到期、未执行、无自动执行，最早仍为2026-11-10 10:00:02.252+08:00；冻结Python/原策略/claim/HDD及实例保持。没有生产服务部署、备份删除或新HDD来源。

上一批准确源码a93a35a9的完整CI是运行 **42558093298**，installer现已success，账号前提修复通过；backend/native-postgres/native-valkey/frontend_checks也success，查询时六个browser分片仍在运行。gh按commit最新返回的42558219507只有Dependabot，其success不能代表完整CI；本轮曾短暂口头误判，已立即纠正，不将该运行计为整链完成。新提交完整CI仍待终态。旧3c3项目图谱待同步记录本轮已成功补发并移入synced。

证据 `retirement-gateway-a7fda4467a7e41c0b71f5d22e7aa2c82`：9文件加manifest，SHA256 `8c2d52d6184e04273b23ea83c50407c62cd81851263d8b28c9e1203906c80f51`。包含5份提交源码、实际全量Unit/Release日志、原老化观察输出及验证摘要；目录Root:Root/0700，文件0600。

## 继续点

继续精确CI终态、native CLI/session/固定helper预算、原生探针和标准安装，再验真实RPC/自然到期单次执行与物理HDD前后证据。永久业务、当前资源/身份授权、共享写入保护、Worker/持久化/API/Vue/Jobs和全部59项仍待；不得按客户端诊断提前删除或缩短留存。17项移出不算完成，整项0/59、阶段0/6、工程25%–35%、酒馆/Live2D功能0%保持。
