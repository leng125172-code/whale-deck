# 开发空间独立退役签名 RPC

2026-11-11，接续已部署的34fcc3f0，新增Agent 1.78独立退役协议、C#固定helper入口和内部Gateway。候选通过真实隔离Unix Socket→C# Agent→sudo→固定helper→systemd→NVMe/HDD实盘验证；尚未部署本候选，也没有当前身份/Worker/API/Vue完整用户退役闭环。不增加完整主任务。

## 合同和边界

独立DevelopmentRetirementService提供Start/Observe/Cancel，使用development.retirement.tasks与独立HMAC域。完整protobuf请求、准确方法、短时有效期和一次性nonce参与签名；归档域、旧通用签名、缺失/重复token、错方法和请求篡改均拒绝。全局Agent广告1.78；旧Agent或缺能力在退役RPC派发前明确拒绝。

Gateway只接受内部已解析空间与退役任务，核对Allocated/Stopped/准确epoch，再构造固定请求。浏览器没有接收这些原始字段的新入口。宿主六字段canonical请求绑定原归档/捕获摘要、退役ID、阶段和原计划摘要，四个资源/操作UUID必须不同；actor/trace完整签名，但不改变原宿主任务摘要，使新的当前有权操作者可观察原任务。当前目录授权、持久归档链和显式确认仍由后续业务层负责，内部Gateway本身不授予用户删除权。

计划22字段与执行15字段均严格核对字段集合、类型、摘要、请求身份和大小；保留UInt64 inode精度，拒绝重复键、非canonical回执及非法结果。宿主成功必须同时sourceRemoved/quotaReleased/claimReleased/retainsHdd=true且releasesApplicationBudget=false。执行结果中的storage/source事实由可信helper产生，后续业务还须与已持久化计划比较，不能把单纯合法摘要格式当作完整归档链证明。

Start/Cancel断连不取消独立宿主任务；丢Start回复不自动重发、不换UUID。网关明确区分能力/密钥预检前未派发与RPC已调用后的未知结果。服务的启动、观察、取消分别限流，观察保留调用取消令牌，固定helper控制仍有界；错误仅返回受控码。

## 实际运行的验证

- Release全解决方案构建成功，0警告、0错误。6266项.NET单测通过、0跳过；最终13项退役RPC专项进一步覆盖计划/执行三控制方法及新操作者签名。普通集成837通过，7项专用native因未配置相应依赖跳过，未将其计通过；准确CI的专用真实依赖门禁仍待。
- 213项开发空间测试在独立mount namespace中全部通过、0跳过；14项CI合同通过。新增验证客户端纳入backend CI编译。Python golden向量是明确合成夹具；Unix单测的helper receiver也是明确double，均不冒充实盘或真实身份。
- root显式调用固定仓库验证项目，私有mount namespace只绑定隔离helper副本；生产helper、服务、socket、key、配置均不替换。随机临时capability key仅0600临时文件，结束后删除。发布物逐字节复制校验，桥进程确切退出后才清理临时目录，最后核对原生产helper inode/摘要保持。
- 在真实NVMe/HDD上各新建全额预留2GiB ext4，保留原归档及7项退役内核回归（5次真实强杀）。新增2个QA allocation经真实签名RPC完成0700用户源码目录删除、项目硬限额和用量八项清零、永久旧入口围栏、HDD保持。真实执行中Observe/Cancel、同task不重发、精确停止后显式新task接续原engine请求、晚取消保持完成回执均通过。所有隔离镜像、挂载、cgroup及桥进程收尾。

成功实盘证据：deploy/artifacts/development-archives-native-552dbb7b10b845cdbd44b72012ba3db0/，292文件封存，清单SHA-256为a80fbe014b2847cd685ed8a5f313ade311458db619e7874950c7fe32fc9f7826。

首轮development-archives-native-bbf1d6e279c742d1953b9a0e04e924e5保留149文件：root的sudo PATH无dotnet，桥进程启动前TypeError，尚未派发退役RPC；原归档/内核路径已执行，隔离资源精确收尾。指定现有固定SDK目录后完整重验通过，无安装新SDK或扩大Agent sudo规则。

候选测试首次因新JSON fixture未登记CopyToOutputDirectory而13项缺文件；登记后12通过/1失败，失败断言错误地要求协议拒绝请求中不存在的任意合法storage/source摘要。修正为格式拒绝，并明确业务层后续比较原计划；最终13项通过。最初普通root运行213项有1项namespace测试跳过，随后使用真实独立namespace全部213通过。失败如实保留，不计通过。

本批测试/源码摘要与生产保护核对位于deploy/artifacts/development-retirement-rpc-20261111/。实盘后原5空间/18报告/源码、预算、硬配额、HDD/journal、120GB/300GB完全预留限制与11核心保持；用户原source改动和两仓库user:user/0664 index保持。

## 发布与剩余门槛

已部署34fcc3f0准确CI 42879989081全13成功，安装态及真实身份归档回归另见[固定helper发布记录](DEVELOPMENT_WORKSPACE_RETIREMENT_HELPER.md)。本候选尚需自身准确CI和标准dev发布；本轮旧版本HTTP连续性失败不追认，下一新版本须降低状态API采样频率、记录完整时延并合理覆盖维护上游超时后重新验证。

继续当前身份授权、持久退役意图/原计划/原task与dispatch恢复、执行确认/撤权/取消、Worker/API/Vue以及新专属QA空间完整用户退役和应用预算墓碑。只可信宿主回执不等于预算已释放。完整主任务仍0/59，ST272/PET6/供应商Live2D16及其余门槛继续，17项移出不恢复。项目图谱本地pending待同步。
