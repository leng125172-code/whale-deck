# 退役 gRPC 与受限 native transport 候选

2026-11-09，源码候选 `a93a35a97bf16132a09c153db09ad067524f5c8b`。新增独立五方法 protobuf service 声明和 Agent 处理器，但 **Program 未映射服务，Agent 仍广告 1.59 且没有退役能力**；不是安装态 RPC 或删除正向验收。

## 改动与边界

Plan/Reserve/Apply/Inspect/Discard 先执行完整语义和上下文校验，再验证实际 protobuf 正文哈希，随后才可进入独立 native runner；返回计划与 nullable 回执仍按原严格政策解析。复用规范 command 投影，不改变指纹或原归档复制权限。

Runner 仅允许五个固定动作，使用独立 `container-file-recycle-archive-retirements` helper/stop 名称及原 InvocationID。UTF-8 请求不超过65536字节，拒绝CR/LF；不在命令参数或环境中传操作内容。Ready绑定前不发送正文，取消时保持零正文；120秒内部期限和调用方取消分开处理。JSON行上限仍65536字节，新退役结果单独限深20，原归档16和旧结果8保持；尾随输出或完成失败会释放结果并关闭驱动。

失败码只将确切退役 NOT_FOUND 视作该意图不存在，祖先归档/来源缺席不会归一为退役缺席；未知失败不回传任意错误文本。该源码尚无Gateway调用和当前资源授权，未开放给产品用户。版本门禁在Gateway，native Ready只表示本次调用身份，不能作为版本证明；未来Gateway必须先满足1.60和专属能力，不能仅凭声明调用新端点。

## 本轮实际验证

新增32项执行适配测试，退役联合144通过；全量C# Unit **6056通过、0失败、0跳过（29秒）**。Release全解决方案及TerminalProbe构建通过，0警告/0错误。795项回收Python回归（86.296秒）、18项老化专项、14项前端CI合同、8项范围检查通过，Git diff检查通过。独立只读复核未发现本范围阻断项，未替代运行态或资源授权验收。

首轮专项编译因测试常量数组CA1861失败，提取只读静态数组后通过，未放宽规则。旧安装态helper实测新transport退出2、stdout/Ready零字节，未向stdin发送操作内容；这只签不支持时拒绝，不签新helper运行成功。C#共享驱动会对该非零退出返回固定FILES_HELPER_REJECTED。

原老化来源独立重读通过：captureAgeElapsed=false、retirementExecuted=false、automaticExecution=false；最早仍为 **2026-11-10 10:00:02.252+08:00**。未修改冻结Python、原来源、claim、策略、HDD或实例，无生产部署变更。

上一批准确3c3bb53c CI运行42556373269的installer实际失败：GitHub runner没有冻结测试所需的`user`账号，pwd.getpwnam抛KeyError。此次仅在CI job准备非特权system账号（无home/nologin，存在时保留且断言UID非零）；本机18专项及795全回归通过，**新提交CI实际分支仍待远端终态**。不重跑旧提交并将其追认为通过；两前端分片查询时仍在运行。

证据 `retirement-rpc-transport-0f207c2f351f410f878699dd78c95951`：13文件加manifest，SHA256 `2ae90bfcf237a48599f154c3193bf0c4438908624a6da0987ec19193e43743ce`；含9份提交源码、原始Release/795回归日志、实际老化观察输出和验证计数摘要。Unit/专项的完整原始日志未另存，不将计数摘要冒充原始日志。目录Root:Root/0700、文件0600。

## 继续点

新CI终态、Gateway版本/内容签名/资源授权、native CLI/session/错误和固定helper安装预算、独立探针、标准安装与真正RPC/HDD整链仍待。自然到期后受控单次执行与独立物理前后证据、永久业务/共享保护/Worker/API/Vue/Jobs和全部59项继续；不按客户端诊断提前删除，不缩短原策略。整项0/59、阶段0/6、工程25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
