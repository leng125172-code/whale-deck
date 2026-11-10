# 退役签名 RPC 标准发布与安装态计划验收

2026-11-11，`d0bb965cc84d1378fc7f942178a816ea5d47c624` 已完成准确 CI、标准 dev 更新及安装态只读退役计划验收。当前实机不再是候选前的34fcc3f0。本批只闭合部署和计划 RPC 分项；完整用户删除、应用预算释放及59项主任务没有新增完成数。

## 已部署

- 准确 CI [42887558226](https://github.com/leng125172-code/whale-deck/actions/runs/42887558226) 全部13项成功，包含六组完整浏览器回归、后端测试、安装器、真实 PostgreSQL、Valkey 和维护 socket 门禁。
- 正式干净 dev 检出经固定 `whaledeck-platform-maintenance update` 执行一次更新，退出0，没有自动回滚。六组件及实际 HTTP 来源同为d0bb965c，23个开发宿主模块逐字节一致，host validator通过，11核心健康，两个宿主服务运行中的程序inode与安装文件一致。
- 本次四端点414个HTTP样本为391×200、23×维护HTML503，零连接错误、零429，最大耗时5.003秒；PID1的8080/8081监听inode及激活时间保持。状态API每5秒采样，客户端10秒超时覆盖维护上游5秒超时。本次新版本升级连续性分项通过；34fcc3f0的首次限流/超时失败不追认，P0-05整项仍不签完成。

首次协调器在更新服务启动前停于正式仓库Git拉取，未产生deployment-request或部署日志。只读对照记录：sudo清空代理后ls-remote在12秒内未完成，显式保留已有代理变量后1.388秒返回准确d0bb965c。保留原协调器、CI与前置失败记录，仅让同一仓库拉取继承现有代理，再继续首次标准更新。代理值未入参数/文件/日志，未改系统代理或sudo配置。

## 已真实验收

新验证入口在任何账号、停用或RPC派发之前复核六组件准确版本。真实临时Authentik管理员通过已安装Vue/BFF/Worker/Agent完成：原QA空间Active23→Stopped24；首个HDD计划取消；第二个计划显式确认Capture、独立Verify并重载；可信QA客户端查询安装态退役计划；最后通过产品恢复Active25并正常双端退出。

安装态Agent1.78的计划结果逐字段关联该次持久归档和独立读取的HDD回执。源码为38项/4132字节，HDD载荷3025字节，摘要/inode一致。只读计划前后HDD完整树逐事实相同；全程新增HDD集合精确为该次归档目录及request.json、receipt.json、payload.tar.gz四项。旧18项HDD及174项归档journal保持，新归档journal增加24项。

Stopped下计划Succeeded，恢复Active后同一归档前提被准确拒绝为WORKSPACE_STORAGE_ARCHIVE_NOT_STOPPED。两种状态各验证7类签名拒绝、完整原task重放、晚Cancel不改终态，以及新task预取消后的Start/Observe保持Stopped。新增4个准确任务的请求、摘要和结果与journal关联，无残留PID/cgroup进程。没有执行phase请求。

独立SQL/内核/文件核对确认18419条原非空间记录、原5个空间的登记/源码/硬配额/预算及18份报告保持；原其他四空间和目标除生命周期标记外的事实保持。120GB NVMe与300GB HDD的完全预留强制限制保持。临时账号已删除，活动Job、锁、租约和执行资源收尾。真实320px暗色及1440px亮色页面截图保留，320px暗色实图已查看。

## 验收入口和失败保护

新增固定QA空间、plan-only、显式启用且root-only的安装态客户端。凭据仅由固定文件读入内存，stdin传请求，输出仅安全事实，结束清零key；不会把真实身份归档成功当作用户删除授权。未启用/非root拒绝及错版本在任何写入前拒绝已实际验证。

复核修正了两处验收缺口：补齐HDD精确新增集合及计划前后全树不变；将浏览器handoff从180秒延长到300秒，覆盖120秒RPC、60秒精确清理及宿主核对。父进程只有确认原task终态后才允许UI恢复；超时、坏输出或任何未settled任务保留Stopped和原证据，不能抢先resume。6项离线失败门禁、14项CI合同、8项归档身份范围和Python/Node语法检查通过，C#客户端Release发布通过。这些离线门禁不冒充真实身份或物理验证。

证据位于 `deploy/artifacts/development-retirement-rpc-release-d0bb965c-20261111/`，清单覆盖65文件，manifest SHA-256为 `8010abc66307de80f60ea3b4c1c3d60dd1c538b4f0f424d1d3688ce8fdd3956a`。入口拒绝和Git网络对照、已查看实图另存 `deploy/artifacts/development-retirement-installed-qa-20261111/`。本批验收脚本及记录的新提交仍须自身准确CI，不把已部署应用提交的成功移记给新提交。

## 剩余工作

持久层当前只有Preparation/Acknowledge；确认不会派发删除。继续补独立plan/execute任务、一次dispatch/丢回复仅观察原task、当前身份与显式删除授权、Worker/API/Vue及原子应用预算墓碑；执行成功还须与持久原计划比较storage/source摘要，再验收新专属空间完整用户退役。保留现有五空间和全部审计，用户未提交source预览改动及两个仓库user:user/0664索引保持。

完整主任务仍0/59；ST272、PET6及供应商/Live2D16继续，17项移出不恢复。不签master或总目标完成；项目图谱本地pending待同步。
